import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild, viewChildren } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Subject, catchError, debounce, filter, forkJoin, groupBy, map, mergeMap, of, switchMap, timer } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import {
  DashboardColors,
  IndicatorEntry,
  IndicatorsService,
  PaletteId,
  ValueColor,
  Widget,
  maxValueColors,
  paletteIds,
  paletteSize,
} from '../../../core/projects/indicators.service';
import { Alert, ApiLang, Button, FormField, Input, markApiText } from '../../../shared/ui';
import { valueSearchDelayMs } from '../data-filter-dialog';
import { colorsOf, defaultScale, paletteColor, rampGradient } from './chart-colors';
import { columnChoices } from './dashboard-filters-dialog';

export interface DashboardColorsData {
  projectId: string;
  dashboardId: string;
  /** The dashboard's palette and colors kept by value as the page has them. */
  colors: DashboardColors;
  /** The dashboard's widgets: their indicators' tables give the columns offered. */
  widgets: Widget[];
  /** The project's indicators, to know which have published a table. */
  indicators: IndicatorEntry[];
}

/** The colors as saved, or `gone` when the dashboard no longer exists. Closing with nothing means nothing was saved or changed. */
export type DashboardColorsResult = DashboardColors | { gone: true };

/** A color kept by value as the form holds it; `key` keeps a row's inputs with it when other rows are added or removed. */
interface Row {
  key: number;
  column: string;
  value: string;
  /** The place in the palette, 1 to 8. */
  color: number;
  /** The saved entry has an empty text for its value (a group value, `dashboard-colors` D4): left as it is, not asked for again. */
  savedBlank: boolean;
}

type RowErrors = Partial<Record<'column' | 'value' | 'color' | 'entry', string>>;

/** The places in a palette, in order. */
const places = Array.from({ length: paletteSize }, (_, i) => i + 1);

/** The longest value an entry takes (`DataQueryBuilder.MaxTextLength`). */
const maxValueLength = 500;

/** Both hold the same palette and the same entries in the same order, which is what the API compares before it writes. */
export function sameColors(a: DashboardColors, b: DashboardColors): boolean {
  return (
    a.palette === b.palette &&
    a.valueColors.length === b.valueColors.length &&
    a.valueColors.every((entry, i) => entry.column === b.valueColors[i].column && entry.value === b.valueColors[i].value && entry.color === b.valueColors[i].color)
  );
}

/**
 * Chooses a dashboard's palette and the colors kept by value (add-dashboard-palettes, design D9). The palettes are native radio inputs
 * styled as cards, each showing its eight colors in order and its sequential scale. A color kept by value names a text column of the
 * dashboard's indicator tables, a value (picked from the column's values or typed) and one of the palette's eight colors. The whole
 * list is saved at once, and saving what the dashboard has already sends nothing. Full screen on phones, a panel from `md` up.
 */
@Component({
  selector: 'app-dashboard-colors-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, FormField, Input],
  template: `
    <form
      class="flex h-dvh w-screen flex-col overflow-y-auto bg-neutral-primary-soft p-4 md:h-auto md:max-h-[calc(100dvh-2rem)] md:w-[min(44rem,calc(100vw-2rem))] md:rounded-base md:border md:border-default md:p-6 md:shadow-sm"
      novalidate
      (submit)="$event.preventDefault(); save()"
      *transloco="let t"
      data-testid="dashboard-colors-dialog"
    >
      <h2 id="dashboard-colors-dialog-title" class="text-lg font-medium text-heading">{{ t('dashboardColors.title') }}</h2>
      <p class="mt-1 text-sm text-body">{{ t('dashboardColors.intro') }}</p>

      <fieldset class="mt-4 min-w-0" data-testid="dashboard-colors-palettes" [attr.aria-describedby]="paletteError() ? 'dashboard-colors-palette-error' : null">
        <legend class="mb-2 text-sm font-medium text-heading">{{ t('dashboardColors.palette.legend') }}</legend>
        <div class="grid gap-3 sm:grid-cols-2">
          @for (id of palettes; track id) {
            <label
              class="flex cursor-pointer flex-col gap-2 rounded-base border border-default-medium bg-neutral-secondary-medium p-3 has-[:checked]:border-brand has-[:checked]:bg-brand-softer has-[:focus-visible]:ring-4 has-[:focus-visible]:ring-brand-medium"
              data-testid="dashboard-colors-palette-card"
              [attr.data-palette]="id"
            >
              <span class="flex items-center gap-2">
                <!-- The dialog opens on the palette in use: it is the radio group's one tab stop and the one the arrow keys move from. The
                     others leave the tab order (roving tabindex) because the focus trap wraps to the first tabbable element in DOM order and
                     knows nothing of radio groups: it would land on the first palette, unchecked, as a second stop. -->
                <input
                  type="radio"
                  name="dashboardPalette"
                  class="size-4 shrink-0"
                  [value]="id"
                  [checked]="palette() === id"
                  [attr.tabindex]="palette() === id ? 0 : -1"
                  [attr.cdkFocusInitial]="palette() === id ? '' : null"
                  [attr.aria-labelledby]="'dashboard-colors-palette-' + id + '-name'"
                  [attr.aria-describedby]="'dashboard-colors-palette-' + id + '-info'"
                  (change)="setPalette(id)"
                  data-testid="dashboard-colors-palette"
                />
                <span [id]="'dashboard-colors-palette-' + id + '-name'" class="text-sm font-medium text-heading" data-testid="dashboard-colors-palette-name">{{ t('colors.palettes.' + id) }}</span>
              </span>
              <!-- The eight colors in the order a chart takes them. The color-blind and screen reader reading is in the description below. -->
              <span class="flex gap-1" aria-hidden="true" data-testid="dashboard-colors-palette-swatches">
                @for (n of places; track n) {
                  <span class="h-6 min-w-0 flex-1 rounded-sm border border-default" [style.background-color]="swatch(id, n)" [attr.data-color]="n"></span>
                }
              </span>
              <!-- The caption above its gradient: side by side, a long one (Spanish: "Escala de mapas y mapas de calor: Verde azulado")
                   takes the whole card and leaves the gradient no width. -->
              <span [id]="'dashboard-colors-palette-' + id + '-info'" class="flex flex-col gap-1 text-xs text-body">
                <span data-testid="dashboard-colors-palette-scale">{{ t('dashboardColors.palette.scale', { scale: t('colors.scales.' + scaleOf(id)) }) }}</span>
                <span class="h-3 rounded-sm" aria-hidden="true" [style.background-image]="gradient(id)" [attr.data-scale]="scaleOf(id)"></span>
                <span class="sr-only">{{ t('dashboardColors.palette.colors', { colors: hueList(id, t) }) }}</span>
              </span>
            </label>
          }
        </div>
        @if (paletteError(); as message) {
          <p id="dashboard-colors-palette-error" class="mt-2 text-sm text-fg-danger-strong" data-testid="field-error" [tmzApiLang]="message">{{ message }}</p>
        }
      </fieldset>

      <h3 class="mt-6 text-base font-medium text-heading">{{ t('dashboardColors.kept.title') }}</h3>
      <p class="mt-1 text-sm text-body">{{ t('dashboardColors.kept.intro') }}</p>

      @if (choices() === null) {
        <p class="mt-4 text-sm text-body" data-testid="dashboard-colors-loading">{{ t('dashboardColors.kept.loadingColumns') }}</p>
      } @else if (columnsError(); as message) {
        <tmz-alert tone="warning" class="mt-4" data-testid="dashboard-colors-columns-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      } @else if (!choices()!.length) {
        <p class="mt-4 text-sm text-body" data-testid="dashboard-colors-no-columns">{{ t('dashboardColors.kept.noColumns') }}</p>
      }

      @if (rows().length) {
        <ol class="mt-4 flex flex-col gap-4" data-testid="dashboard-colors-rows">
          @for (row of rows(); track row.key; let i = $index) {
            <li class="rounded-base border border-default p-3" data-testid="dashboard-colors-row">
              <div class="grid gap-3 sm:grid-cols-2">
                <!-- Both controls are named from their visible label and the entry they belong to: with several entries, "Column" and "Value" alone would read the same in each. -->
                <tmz-form-field [label]="t('dashboardColors.kept.column')" [fieldId]="'color-column-' + row.key" [error]="errorsOf(i).column">
                  <select
                    tmzInput
                    [id]="'color-column-' + row.key"
                    #columnSelect
                    [attr.aria-label]="t('dashboardColors.kept.columnOfEntry', { number: i + 1, total: rows().length })"
                    (change)="setColumn(i, $any($event.target).value)"
                    data-testid="dashboard-colors-column"
                  >
                    @if (!known(row)) {
                      <option [value]="row.column" selected>{{ t('dashboardColors.kept.columnMissing', { column: row.column }) }}</option>
                    }
                    @for (column of choices() ?? []; track column) {
                      <option [value]="column" [selected]="column === row.column">{{ column }}</option>
                    }
                  </select>
                </tmz-form-field>
                <tmz-form-field [label]="t('dashboardColors.kept.value')" [fieldId]="'color-value-' + row.key" [error]="errorsOf(i).value">
                  <!-- A native combobox: the column's values are suggested as the member types, and any text can still be typed. -->
                  <input
                    tmzInput
                    [id]="'color-value-' + row.key"
                    autocomplete="off"
                    [attr.maxlength]="maxValueLength"
                    [attr.list]="'color-values-' + row.key"
                    [attr.aria-label]="t('dashboardColors.kept.valueOfEntry', { number: i + 1, total: rows().length })"
                    [attr.placeholder]="t('dashboardColors.kept.valuePlaceholder')"
                    [value]="row.value"
                    (focus)="listValues(i)"
                    (input)="setValue(i, $any($event.target).value)"
                    data-testid="dashboard-colors-value"
                  />
                  <datalist [id]="'color-values-' + row.key" data-testid="dashboard-colors-values">
                    @for (option of suggestionsOf(row); track option) {
                      <option [value]="option"></option>
                    }
                  </datalist>
                </tmz-form-field>
              </div>

              <fieldset class="mt-3 min-w-0" [attr.aria-describedby]="errorsOf(i).color ? 'color-choice-' + row.key + '-error' : null" data-testid="dashboard-colors-choice">
                <legend class="mb-2 text-sm font-medium text-heading">{{ t('dashboardColors.kept.color') }}<span class="sr-only"> {{ t('dashboardColors.kept.colorFor', { name: nameOf(row) }) }}</span></legend>
                <div class="flex flex-wrap gap-1.5">
                  @for (n of places; track n) {
                    <label class="relative">
                      <input
                        type="radio"
                        class="peer sr-only"
                        [name]="'value-color-' + row.key"
                        [value]="n"
                        [checked]="row.color === n"
                        [attr.aria-label]="t('dashboardColors.kept.colorOption', { number: n, hue: t('colors.hues.' + palette() + '.' + n) })"
                        (change)="setColor(i, n)"
                        data-testid="dashboard-colors-color"
                        [attr.data-color]="n"
                      />
                      <span
                        class="flex min-h-10 min-w-12 cursor-pointer items-center justify-center gap-1.5 rounded-base border border-default-medium bg-neutral-secondary-medium px-2 text-sm text-heading peer-checked:border-brand peer-checked:bg-brand-softer peer-checked:ring-2 peer-checked:ring-brand peer-focus-visible:ring-4 peer-focus-visible:ring-brand-medium"
                        [title]="t('colors.hues.' + palette() + '.' + n)"
                      >
                        <span class="size-4 shrink-0 rounded-full border border-default" aria-hidden="true" [style.background-color]="swatch(palette(), n)"></span>
                        <span class="tabular-nums" aria-hidden="true" data-testid="dashboard-colors-color-number">{{ n }}</span>
                      </span>
                    </label>
                  }
                </div>
                @if (errorsOf(i).color; as message) {
                  <p [id]="'color-choice-' + row.key + '-error'" class="mt-2 text-sm text-fg-danger-strong" data-testid="field-error" [tmzApiLang]="message">{{ message }}</p>
                }
              </fieldset>

              @if (errorsOf(i).entry; as message) {
                <p class="mt-3 text-sm text-fg-danger-strong" data-testid="dashboard-colors-row-error" [tmzApiLang]="message">{{ message }}</p>
              }
              <div class="mt-3 flex justify-end">
                <button
                  #removeButton
                  tmzButton
                  variant="ghost"
                  size="sm"
                  type="button"
                  [attr.aria-label]="t('dashboardColors.kept.remove', { name: nameOf(row) })"
                  (click)="remove(i)"
                  data-testid="dashboard-colors-remove"
                >
                  {{ t('dashboardColors.kept.removeShort') }}
                </button>
              </div>
            </li>
          }
        </ol>
      } @else {
        <p class="mt-4 text-sm text-body" data-testid="dashboard-colors-empty">{{ t('dashboardColors.kept.empty') }}</p>
      }

      <div class="mt-4">
        <!-- At the limit, or with no column to offer, the button stays focusable so the hint saying why can be read. -->
        <button
          #addButton
          tmzButton
          variant="secondary"
          size="sm"
          type="button"
          class="aria-disabled:cursor-not-allowed aria-disabled:opacity-50"
          [attr.aria-disabled]="cannotAdd() ? 'true' : null"
          [attr.aria-describedby]="atLimit() ? 'dashboard-colors-limit-hint' : null"
          (click)="add()"
          data-testid="dashboard-colors-add"
        >
          {{ t('dashboardColors.kept.add') }}
        </button>
        @if (atLimit()) {
          <p id="dashboard-colors-limit-hint" class="mt-2 text-sm text-body" data-testid="dashboard-colors-limit-hint">{{ t('dashboardColors.kept.limitHint', { max: maxEntries }) }}</p>
        }
        @if (listError(); as message) {
          <p class="mt-2 text-sm text-fg-danger-strong" data-testid="field-error" [tmzApiLang]="message">{{ message }}</p>
        }
      </div>

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="dashboard-colors-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()" data-testid="dashboard-colors-cancel">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" [disabled]="busy()" [attr.aria-busy]="busy()" data-testid="dashboard-colors-save">
          {{ busy() ? t('dashboardColors.saving') : t('dashboardColors.save') }}
        </button>
      </div>
    </form>
  `,
})
export class DashboardColorsDialog {
  protected readonly data = inject<DashboardColorsData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DashboardColorsResult>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly injector = inject(Injector);

  protected readonly palettes = paletteIds;
  protected readonly places = places;
  protected readonly maxEntries = maxValueColors;
  protected readonly maxValueLength = maxValueLength;

  private readonly start = colorsOf(this.data.colors);
  private nextKey = 0;
  protected readonly palette = signal<PaletteId>(this.start.palette);
  protected readonly rows = signal<Row[]>(
    this.start.valueColors.map((entry) => ({ key: this.nextKey++, column: entry.column, value: entry.value, color: entry.color, savedBlank: entry.value === '' })),
  );
  /** The text columns of the dashboard's tables; null while they are read. */
  protected readonly choices = signal<string[] | null>(null);
  protected readonly columnsError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly paletteError = signal<string | null>(null);
  protected readonly listError = signal<string | null>(null);
  private readonly submitted = signal(false);
  /** The server's errors, by row; cleared for a row when it changes. */
  private readonly serverErrors = signal<Record<number, RowErrors>>({});
  /** The values suggested for each row's value, by the row's key. */
  private readonly suggestions = signal<Record<number, readonly string[]>>({});

  protected readonly atLimit = computed(() => this.rows().length >= maxValueColors);
  protected readonly cannotAdd = computed(() => this.atLimit() || !(this.choices() ?? []).length);

  private readonly columnSelects = viewChildren<ElementRef<HTMLSelectElement>>('columnSelect');
  private readonly removeButtons = viewChildren<ElementRef<HTMLButtonElement>>('removeButton');
  private readonly addButton = viewChild<ElementRef<HTMLButtonElement>>('addButton');

  /** One request at a time per row, once typing stops; the first of a row's lookups is not waited for. */
  private readonly lookups = new Subject<{ key: number; column: string; search: string; delay: number }>();

  constructor() {
    this.lookups
      .pipe(
        groupBy((lookup) => lookup.key),
        mergeMap((own) =>
          own.pipe(
            debounce((lookup) => timer(lookup.delay)),
            // The column may have changed while the member was typing: its old column's values are not asked for.
            filter((lookup) => this.columnOf(lookup.key) === lookup.column),
            switchMap((lookup) =>
              this.service.dashboardValues(this.data.projectId, this.data.dashboardId, lookup.column, lookup.search || undefined).pipe(
                map((answer) => ({ key: lookup.key, column: lookup.column, values: answer.values.map((v) => v.value).filter((value) => value !== '') })),
                // Typing a value still works without the suggestions.
                catchError(() => of({ key: lookup.key, column: lookup.column, values: [] as string[] })),
              ),
            ),
          ),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(({ key, column, values }) => {
        // An answer for a column the row no longer has (or for a row that was removed) belongs to no list: `setColumn` cleared the
        // row's suggestions, and this one would land in the new column's list and keep `listValues` from asking for its values.
        if (this.columnOf(key) === column) {
          this.suggestions.update((all) => ({ ...all, [key]: values }));
        }
      });

    const published = new Set(this.data.indicators.filter((i) => i.published).map((i) => i.name));
    const names = [...new Set(this.data.widgets.map((w) => w.config.indicator))].filter((name) => published.has(name));
    if (!names.length) {
      this.choices.set([]);
      return;
    }
    forkJoin(names.map((name) => this.service.columns(this.data.projectId, name).pipe(map((catalog) => [name, catalog.columns] as const))))
      .pipe(
        catchError((failure) => {
          // Saving the list as it is still works without the columns.
          this.columnsError.set(this.errors.parse(failure).message);
          return of(null);
        }),
      )
      .subscribe((tables) => {
        const found = tables ? columnChoices(Object.fromEntries(tables), this.data.widgets) : [];
        this.choices.set([...new Set(found.filter((c) => c.kind === 'text').map((c) => c.column))]);
      });
  }

  /** The CSS value of a palette's n-th color: the same custom property the charts read, so the preview is the chart's color. */
  protected swatch(palette: PaletteId, n: number): string {
    return paletteColor(palette, n);
  }

  protected scaleOf(palette: PaletteId): string {
    return defaultScale(palette);
  }

  /** A palette's default sequential scale from its lightest step to its darkest. */
  protected gradient(palette: PaletteId): string {
    return rampGradient(defaultScale(palette));
  }

  /** "1 blue, 2 orange…": what the eight swatches are called, for a screen reader. */
  protected hueList(palette: PaletteId, t: (key: string) => string): string {
    return places.map((n) => `${n} ${t(`colors.hues.${palette}.${n}`)}`).join(', ');
  }

  protected setPalette(palette: PaletteId): void {
    this.palette.set(palette);
    this.paletteError.set(null);
  }

  /** A row's column is among the columns offered (or they are still being read). */
  protected known(row: Row): boolean {
    const choices = this.choices();
    return choices === null || choices.includes(row.column);
  }

  /** A row as a screen reader and the remove button name it: its column and its value. */
  protected nameOf(row: Row): string {
    return row.value === '' ? row.column : `${row.column}: ${row.value}`;
  }

  protected suggestionsOf(row: Row): readonly string[] {
    return this.suggestions()[row.key] ?? [];
  }

  /** The column a row has now; undefined once the row was removed. */
  private columnOf(key: number): string | undefined {
    return this.rows().find((row) => row.key === key)?.column;
  }

  /** What is wrong with a row: no value once the form was sent, or what the server said. */
  protected errorsOf(index: number): RowErrors {
    const row = this.rows()[index];
    const own: RowErrors = {};
    if (this.submitted() && this.incomplete(row)) {
      own.value = this.transloco.translate('dashboardColors.kept.errors.valueRequired');
    }
    return { ...this.serverErrors()[row.key], ...own };
  }

  /** A row with no value: asked for, except for an entry saved with the empty text. */
  private incomplete(row: Row): boolean {
    return row.value === '' && !row.savedBlank;
  }

  protected setColumn(index: number, column: string): void {
    const key = this.rows()[index].key;
    this.update(index, { column });
    // The values of the new column, ready for the value that is typed or picked next.
    this.suggestions.update((all) => {
      const rest = { ...all };
      delete rest[key];
      return rest;
    });
  }

  protected setValue(index: number, value: string): void {
    this.update(index, { value, savedBlank: false });
    this.lookups.next({ key: this.rows()[index].key, column: this.rows()[index].column, search: value, delay: valueSearchDelayMs });
  }

  protected setColor(index: number, color: number): void {
    this.update(index, { color });
  }

  /** The column's most frequent values, asked for when the value is first focused (and again once it is typed in). */
  protected listValues(index: number): void {
    const row = this.rows()[index];
    if (this.suggestions()[row.key] === undefined) {
      this.lookups.next({ key: row.key, column: row.column, search: '', delay: 0 });
    }
  }

  protected add(): void {
    const choices = this.choices() ?? [];
    if (this.cannotAdd() || !choices.length) {
      return;
    }
    // The column of the row before, since entries often share one (`sex`: Female, Male). A saved entry can be on a column that is no
    // longer offered, and a column can use all eight colors, which the API refuses a ninth of: the new row then starts on the first
    // column offered that has a color left, in the first one it does not use yet. With none left it starts on the column before in
    // color 1, and the API says so on save.
    const before = this.rows().at(-1)?.column;
    const candidates = before !== undefined && choices.includes(before) ? [before, ...choices] : choices;
    const column = candidates.find((c) => this.freeColor(c) !== undefined) ?? candidates[0];
    const color = this.freeColor(column) ?? 1;
    this.rows.update((rows) => [...rows, { key: this.nextKey++, column, value: '', color, savedBlank: false }]);
    // The new entry's column, ready to be changed.
    afterNextRender(() => this.columnSelects().at(-1)?.nativeElement.focus(), { injector: this.injector });
  }

  /** The first color of the palette that no entry on the column uses; undefined when it uses all eight. */
  private freeColor(column: string): number | undefined {
    const used = new Set(this.rows().filter((r) => r.column === column).map((r) => r.color));
    return places.find((n) => !used.has(n));
  }

  protected remove(index: number): void {
    this.rows.update((rows) => rows.filter((_, i) => i !== index));
    // The button that was pressed is gone: focus goes to the next entry's, the last one's, or "Add".
    afterNextRender(
      () => {
        const buttons = this.removeButtons();
        (buttons[Math.min(index, buttons.length - 1)] ?? this.addButton())?.nativeElement.focus();
      },
      { injector: this.injector },
    );
  }

  protected save(): void {
    this.submitted.set(true);
    this.error.set(null);
    this.paletteError.set(null);
    this.listError.set(null);
    if (this.busy() || this.rows().some((_, i) => Object.keys(this.errorsOf(i)).length)) {
      return;
    }
    const colors: DashboardColors = {
      palette: this.palette(),
      valueColors: this.rows().map((r): ValueColor => ({ column: r.column, value: r.value, color: r.color })),
    };
    // Nothing changed: nothing is sent, and the page keeps what it has.
    if (sameColors(colors, this.start)) {
      this.ref.close();
      return;
    }
    this.busy.set(true);
    this.service.saveColors(this.data.projectId, this.data.dashboardId, colors).subscribe({
      next: (saved) => this.ref.close(colorsOf(saved)),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        // palette, valueColors, valueColors[i] and valueColors[i].column, .value, .color (design D4): next to what they name; anything
        // else as the form's error.
        const byRow: Record<number, RowErrors> = {};
        const others: string[] = [];
        for (const [path, message] of Object.entries(parsed.fieldErrors)) {
          const entry = /^valueColors\[(\d+)\](?:\.(column|value|color))?$/i.exec(path);
          const row = entry ? this.rows()[Number(entry[1])] : undefined;
          if (entry && row) {
            byRow[row.key] = { ...byRow[row.key], [entry[2]?.toLowerCase() ?? 'entry']: message };
          } else if (path.toLowerCase() === 'palette') {
            this.paletteError.set(message);
          } else if (path.toLowerCase() === 'valuecolors') {
            this.listError.set(message);
          } else {
            others.push(message);
          }
        }
        this.serverErrors.set(byRow);
        // The form's alert is announced: it says there is something to fix, and what the server said that no field shows.
        this.error.set(others.length ? markApiText(others.join(' ')) : parsed.message);
      },
    });
  }

  private update(index: number, change: Partial<Row>): void {
    const key = this.rows()[index]?.key;
    this.rows.update((rows) => rows.map((r, i) => (i === index ? { ...r, ...change } : r)));
    this.serverErrors.update((errors) => {
      const rest = { ...errors };
      delete rest[key];
      return rest;
    });
  }
}
