import { DIALOG_DATA, Dialog, DialogRef } from '@angular/cdk/dialog';
import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChildren } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { catchError, forkJoin, map, of } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import { DataColumn } from '../../../core/projects/data.service';
import {
  DashboardFilter,
  DashboardFilterKind,
  DashboardFilterValue,
  IndicatorEntry,
  IndicatorsService,
  Widget,
  maxDashboardFilterLabelLength,
  maxDashboardFilters,
} from '../../../core/projects/indicators.service';
import { Alert, ApiLang, Button, FormField, Input, markApiText } from '../../../shared/ui';
import { DataFilterDialog, DataFilterDialogData, DataFilterDialogResult } from '../data-filter-dialog';
import { columnFor, describeValue, fromDataFilter, kindOf, operationOf, toDataFilter } from './dashboard-filters';

export interface DashboardFiltersData {
  projectId: string;
  dashboardId: string;
  /** The dashboard's filters as the page has them. */
  filters: DashboardFilter[];
  /** The dashboard's widgets: their indicators' tables give the columns offered, and how many widgets each would narrow. */
  widgets: Widget[];
  /** The project's indicators, to know which have published a table. */
  indicators: IndicatorEntry[];
  timeZone: string;
}

/** The filters as saved, or `gone` when the dashboard no longer exists. */
export type DashboardFiltersResult = DashboardFilter[] | { gone: true };

/** A column of the dashboard's tables a filter can narrow, with its kind and how many of the dashboard's widgets it would narrow. */
export interface ColumnChoice {
  column: string;
  kind: DashboardFilterKind;
  narrows: number;
}

/** A filter as the form holds it; `key` keeps a row's inputs with it when the rows are moved. */
interface Row {
  key: number;
  label: string;
  column: string;
  kind: DashboardFilterKind;
  default: DashboardFilterValue | null;
}

type RowErrors = Partial<Record<'label' | 'column' | 'kind' | 'default', string>>;

/**
 * The columns of the dashboard's published indicator tables a filter can narrow, one per name and kind (integer and decimal are one
 * number, date and timestamp one date), each with the number of widgets it would narrow: those whose table has it with that kind
 * and that do not ignore it.
 */
export function columnChoices(tables: Record<string, DataColumn[]>, widgets: Widget[]): ColumnChoice[] {
  const found = new Map<string, { column: string; kind: DashboardFilterKind }>();
  for (const columns of Object.values(tables)) {
    for (const column of columns) {
      const kind = kindOf(column.type);
      found.set(`${column.name}|${kind}`, { column: column.name, kind });
    }
  }
  const narrows = (column: string, kind: DashboardFilterKind) =>
    widgets.filter(
      (w) =>
        (tables[w.config.indicator] ?? []).some((c) => c.name === column && kindOf(c.type) === kind) &&
        !(w.config.ignoredDashboardFilters ?? []).includes(column),
    ).length;
  return [...found.values()]
    .map((choice) => ({ ...choice, narrows: narrows(choice.column, choice.kind) }))
    .sort((a, b) => a.column.localeCompare(b.column) || a.kind.localeCompare(b.kind));
}

/** A label proposed from a column's name: `visit_round` reads "Visit round". */
function proposedLabel(column: string): string {
  const words = column.replace(/^_+/, '').replace(/_+/g, ' ').trim();
  return (words.charAt(0).toUpperCase() + words.slice(1)).slice(0, maxDashboardFilterLabelLength);
}

/**
 * Defines a dashboard's filters (dashboard filters, design D8): each with a label, a column of the dashboard's tables, the kind that
 * column has and an optional default, in an order. The whole list is saved at once. Full screen on phones, a panel from `md` up.
 */
@Component({
  selector: 'app-dashboard-filters-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, FormField, Input],
  template: `
    <form
      class="flex h-dvh w-screen flex-col overflow-y-auto bg-neutral-primary-soft p-4 md:h-auto md:max-h-[calc(100dvh-2rem)] md:w-[min(44rem,calc(100vw-2rem))] md:rounded-base md:border md:border-default md:p-6 md:shadow-sm"
      novalidate
      (submit)="$event.preventDefault(); save()"
      *transloco="let t"
      data-testid="dashboard-filters-dialog"
    >
      <h2 id="dashboard-filters-dialog-title" class="text-lg font-medium text-heading">{{ t('dashboardFilters.dialog.title') }}</h2>
      <p class="mt-1 text-sm text-body">{{ t('dashboardFilters.dialog.intro') }}</p>

      @if (choices() === null) {
        <p class="mt-4 text-sm text-body" data-testid="dashboard-filters-loading">{{ t('dashboardFilters.dialog.loadingColumns') }}</p>
      } @else if (columnsError(); as message) {
        <tmz-alert tone="warning" class="mt-4" data-testid="dashboard-filters-columns-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      } @else if (!choices()!.length) {
        <p class="mt-4 text-sm text-body" data-testid="dashboard-filters-no-columns">{{ t('dashboardFilters.dialog.noColumns') }}</p>
      }

      @if (rows().length) {
        <ol class="mt-4 flex flex-col gap-4" data-testid="dashboard-filters-rows">
          @for (row of rows(); track row.key; let i = $index, first = $first, last = $last) {
            <li class="rounded-base border border-default p-3" data-testid="dashboard-filters-row">
              <div class="grid gap-3 sm:grid-cols-2">
                <tmz-form-field [label]="t('dashboardFilters.dialog.label')" [fieldId]="'filter-label-' + row.key" [error]="errorsOf(i).label">
                  <input
                    #labelInput
                    tmzInput
                    [id]="'filter-label-' + row.key"
                    autocomplete="off"
                    [value]="row.label"
                    (input)="setLabel(i, $any($event.target).value)"
                    data-testid="dashboard-filters-label"
                  />
                </tmz-form-field>
                <tmz-form-field [label]="t('dashboardFilters.dialog.column')" [fieldId]="'filter-column-' + row.key" [error]="errorsOf(i).column ?? errorsOf(i).kind">
                  <select tmzInput [id]="'filter-column-' + row.key" (change)="setColumn(i, $any($event.target).value)" data-testid="dashboard-filters-column">
                    @if (!known(row)) {
                      <option [value]="row.column + '|' + row.kind" selected>{{ t('dashboardFilters.dialog.columnMissing', { column: row.column, kind: t('dashboardFilters.kinds.' + row.kind) }) }}</option>
                    }
                    @for (choice of offered(i); track choice.column + '|' + choice.kind) {
                      <option [value]="choice.column + '|' + choice.kind" [selected]="choice.column === row.column && choice.kind === row.kind">
                        {{ t('dashboardFilters.dialog.columnOption', { column: choice.column, kind: t('dashboardFilters.kinds.' + choice.kind), count: choice.narrows, total: data.widgets.length }) }}
                      </option>
                    }
                  </select>
                </tmz-form-field>
              </div>
              <div class="mt-3 flex flex-wrap items-center gap-2">
                <p class="min-w-0 flex-1 break-words text-sm text-body" data-testid="dashboard-filters-default">
                  {{ t('dashboardFilters.dialog.default') }}: {{ row.default ? describe(row, t) : t('dashboardFilters.dialog.noDefault') }}
                </p>
                <button tmzButton variant="secondary" size="sm" type="button" (click)="editDefault(i)" data-testid="dashboard-filters-set-default">
                  {{ row.default ? t('dashboardFilters.dialog.changeDefault') : t('dashboardFilters.dialog.setDefault') }}
                </button>
              </div>
              @if (errorsOf(i).default; as message) {
                <p class="mt-1 text-sm text-fg-danger-strong" data-testid="field-error" [tmzApiLang]="message">{{ message }}</p>
              }
              <div class="mt-3 flex flex-wrap justify-end gap-2">
                <button tmzButton variant="ghost" size="sm" type="button" [disabled]="first" [attr.aria-label]="t('dashboardFilters.dialog.moveUp', { label: nameOf(row) })" (click)="move(i, -1)" data-testid="dashboard-filters-up">↑</button>
                <button tmzButton variant="ghost" size="sm" type="button" [disabled]="last" [attr.aria-label]="t('dashboardFilters.dialog.moveDown', { label: nameOf(row) })" (click)="move(i, 1)" data-testid="dashboard-filters-down">↓</button>
                <button tmzButton variant="ghost" size="sm" type="button" (click)="remove(i)" data-testid="dashboard-filters-remove">{{ t('dashboardFilters.dialog.remove', { label: nameOf(row) }) }}</button>
              </div>
            </li>
          }
        </ol>
      } @else {
        <p class="mt-4 text-sm text-body" data-testid="dashboard-filters-empty">{{ t('dashboardFilters.dialog.empty') }}</p>
      }

      <div class="mt-4">
        <!-- At the limit, or with no column to offer, the button stays focusable so the hint saying why can be read. -->
        <button
          tmzButton
          variant="secondary"
          size="sm"
          type="button"
          class="aria-disabled:cursor-not-allowed aria-disabled:opacity-50"
          [attr.aria-disabled]="cannotAdd() ? 'true' : null"
          [attr.aria-describedby]="atLimit() ? 'dashboard-filters-limit-hint' : null"
          (click)="add()"
          data-testid="dashboard-filters-add"
        >
          {{ t('dashboardFilters.dialog.add') }}
        </button>
        @if (atLimit()) {
          <p id="dashboard-filters-limit-hint" class="mt-2 text-sm text-body" data-testid="dashboard-filters-limit-hint">{{ t('dashboardFilters.dialog.limitHint', { max: maxFilters }) }}</p>
        }
      </div>

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="dashboard-filters-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()" data-testid="dashboard-filters-cancel">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" [disabled]="busy()" [attr.aria-busy]="busy()" data-testid="dashboard-filters-save">
          {{ busy() ? t('dashboardFilters.dialog.saving') : t('dashboardFilters.dialog.save') }}
        </button>
      </div>
    </form>
  `,
})
export class DashboardFiltersDialog {
  protected readonly data = inject<DashboardFiltersData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DashboardFiltersResult>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(Dialog);
  private readonly injector = inject(Injector);

  protected readonly maxFilters = maxDashboardFilters;
  private nextKey = 0;
  protected readonly rows = signal<Row[]>(this.data.filters.map((f) => ({ key: this.nextKey++, label: f.label, column: f.column, kind: f.kind, default: f.default })));
  /** The columns offered; null while they are read. */
  protected readonly choices = signal<ColumnChoice[] | null>(null);
  protected readonly columnsError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly submitted = signal(false);
  /** The server's errors, by row; cleared for a row when it changes. */
  private readonly serverErrors = signal<Record<number, RowErrors>>({});

  protected readonly atLimit = computed(() => this.rows().length >= maxDashboardFilters);
  protected readonly cannotAdd = computed(() => this.atLimit() || !this.unused().length);
  /** The columns no row uses yet. */
  private readonly unused = computed(() => (this.choices() ?? []).filter((c) => !this.rows().some((r) => r.column === c.column)));

  private readonly labelInputs = viewChildren<ElementRef<HTMLInputElement>>('labelInput');

  constructor() {
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
      .subscribe((tables) => this.choices.set(tables ? columnChoices(Object.fromEntries(tables), this.data.widgets) : []));
  }

  /** A row's column is among the columns offered (or they are still being read). */
  protected known(row: Row): boolean {
    const choices = this.choices();
    return choices === null || choices.some((c) => c.column === row.column && c.kind === row.kind);
  }

  /** The columns a row can take: its own, and those no other row uses. */
  protected offered(index: number): ColumnChoice[] {
    const others = this.rows().filter((_, i) => i !== index);
    return (this.choices() ?? []).filter((c) => !others.some((r) => r.column === c.column));
  }

  protected nameOf(row: Row): string {
    return row.label.trim() || row.column;
  }

  protected describe(row: Row, t: (key: string, params?: Record<string, unknown>) => string): string {
    return describeValue({ column: row.column, label: row.label, kind: row.kind, default: null }, row.default, t);
  }

  /** What is wrong with a row: an empty, long or taken label once the form was sent, or what the server said. */
  protected errorsOf(index: number): RowErrors {
    const row = this.rows()[index];
    const own: RowErrors = {};
    if (this.submitted()) {
      const label = row.label.trim();
      const taken = this.rows().some((other, i) => i < index && other.label.trim().toLowerCase() === label.toLowerCase());
      if (!label) {
        own.label = this.transloco.translate('dashboardFilters.dialog.errors.labelRequired');
      } else if (label.length > maxDashboardFilterLabelLength) {
        own.label = this.transloco.translate('dashboardFilters.dialog.errors.labelLong', { max: maxDashboardFilterLabelLength });
      } else if (taken) {
        own.label = this.transloco.translate('dashboardFilters.dialog.errors.labelTaken', { label });
      }
    }
    return { ...this.serverErrors()[row.key], ...own };
  }

  protected setLabel(index: number, label: string): void {
    this.update(index, { label });
  }

  /** A column of another kind takes another kind of value, so the default goes. */
  protected setColumn(index: number, value: string): void {
    const [column, kind] = value.split('|') as [string, DashboardFilterKind];
    const row = this.rows()[index];
    this.update(index, { column, kind, default: kind === row.kind ? row.default : null });
  }

  protected add(): void {
    const choice = this.unused()[0];
    if (this.cannotAdd() || !choice) {
      return;
    }
    this.rows.update((rows) => [...rows, { key: this.nextKey++, label: proposedLabel(choice.column), column: choice.column, kind: choice.kind, default: null }]);
    // The new filter's label, ready to be replaced.
    afterNextRender(
      () => {
        const input = this.labelInputs().at(-1)?.nativeElement;
        input?.focus();
        input?.select();
      },
      { injector: this.injector },
    );
  }

  protected move(index: number, step: -1 | 1): void {
    const to = index + step;
    this.rows.update((rows) => {
      if (to < 0 || to >= rows.length) {
        return rows;
      }
      const moved = [...rows];
      [moved[index], moved[to]] = [moved[to], moved[index]];
      return moved;
    });
  }

  protected remove(index: number): void {
    this.rows.update((rows) => rows.filter((_, i) => i !== index));
  }

  /** Sets a row's default with the clean data filter dialog, titled with its label, with the one condition its kind takes. */
  protected editDefault(index: number): void {
    const row = this.rows()[index];
    const data: DataFilterDialogData = {
      column: columnFor(row),
      filter: row.default ? toDataFilter(row.column, row.kind, row.default) : null,
      timeZone: this.data.timeZone,
      loadValues: (search) =>
        this.service.dashboardValues(this.data.projectId, this.data.dashboardId, row.column, search).pipe(map((r) => r.values.map((v) => ({ value: v.value, count: null })))),
      check: () => of(null),
      allowTypedValues: true,
      title: this.nameOf(row),
      operations: [operationOf(row.kind)],
      removeLabel: this.transloco.translate('dashboardFilters.dialog.clearDefault'),
    };
    this.dialog
      .open<DataFilterDialogResult, DataFilterDialogData>(DataFilterDialog, {
        data,
        ariaLabelledBy: 'data-filter-title',
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        backdropClass: ['cdk-overlay-dark-backdrop'],
        maxWidth: '100vw',
        maxHeight: '100dvh',
      })
      .closed.subscribe((result) => {
        if (!result) {
          return;
        }
        const current = this.rows().findIndex((r) => r.key === row.key);
        if (current >= 0) {
          this.update(current, { default: 'remove' in result ? null : fromDataFilter(row.kind, result.filter) });
        }
      });
  }

  protected save(): void {
    this.submitted.set(true);
    this.error.set(null);
    if (this.busy() || this.rows().some((_, i) => Object.keys(this.errorsOf(i)).length)) {
      return;
    }
    const filters: DashboardFilter[] = this.rows().map((r) => ({ column: r.column, label: r.label.trim(), kind: r.kind, default: r.default }));
    this.busy.set(true);
    this.service.saveDashboardFilters(this.data.projectId, this.data.dashboardId, filters).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        // filters[i].label, .column, .kind, .default: next to the row; anything else as the form's error.
        const byRow: Record<number, RowErrors> = {};
        const others: string[] = [];
        for (const [path, message] of Object.entries(parsed.fieldErrors)) {
          const match = /^filters\[(\d+)\]\.(label|column|kind|default)$/i.exec(path);
          const row = match ? this.rows()[Number(match[1])] : undefined;
          if (match && row) {
            byRow[row.key] = { ...byRow[row.key], [match[2].toLowerCase()]: message };
          } else {
            others.push(message);
          }
        }
        this.serverErrors.set(byRow);
        this.error.set(others.length ? markApiText(others.join(' ')) : Object.keys(byRow).length ? null : parsed.message);
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
