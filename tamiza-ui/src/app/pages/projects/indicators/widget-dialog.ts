import { DIALOG_DATA, Dialog, DialogRef } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Observable, catchError, of, switchMap, tap } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import { DataFilter } from '../../../core/projects/data.service';
import {
  ChartType,
  IndicatorColumns,
  IndicatorEntry,
  IndicatorsService,
  Widget,
  WidgetConfig,
  chartTypes,
  emptyConfig,
  maxBuckets,
  maxFilters,
  maxTitleLength,
  needsGroup,
  WidgetSort,
} from '../../../core/projects/indicators.service';
import { Alert, Button, Disclosure, FormField, Input } from '../../../shared/ui';
import { DataFilterDialog, DataFilterDialogData, DataFilterDialogResult } from '../data-filter-dialog';
import { describeFilter, normalizeFilter } from '../data-view';
import { WidgetView, queryFor } from './widget-view';

export interface WidgetDialogData {
  projectId: string;
  /** The widget to edit; null for a new one. */
  widget: Widget | null;
  /** The project's indicators, whether or not they have published. */
  indicators: IndicatorEntry[];
  timeZone: string;
}

function sortKeyOf(sort: WidgetSort | null): string {
  return !sort ? '' : sort.by === 'value' ? 'value' : `column:${sort.column ?? ''}`;
}

/** How a column is compared when the order does not say: text as text, a number as a number, others in their own order (null). */
function ownComparison(type: string): 'text' | 'number' | null {
  return type === 'text' ? 'text' : type === 'integer' || type === 'decimal' ? 'number' : null;
}

/** Debounce of the live preview: one request once the draft stops changing. */
export const previewDelayMs = 400;

type Mode = 'measure' | 'ratio';
type Problems = Partial<Record<'title' | 'indicator' | 'measure' | 'ratio' | 'groupBy' | 'geometryColumn' | 'buckets' | 'sort', string>>;

/**
 * Adds or edits a widget (design D6, D8): its chart type and title, its query (the indicator, a measure or a ratio of two numeric
 * columns, the group-by column, filters, the geometry column of a map, the buckets of a histogram) and a live preview from the
 * same query endpoint that renders a saved widget. The columns offered are those of the chosen indicator's published table. A draft
 * the server refuses shows that refusal where the preview is, never the previous chart.
 */
@Component({
  selector: 'app-widget-dialog',
  imports: [TranslocoDirective, Alert, Button, Disclosure, FormField, Input, WidgetView],
  template: `
    <form
      class="flex h-dvh w-screen flex-col gap-4 overflow-y-auto bg-neutral-primary-soft p-4 md:h-auto md:max-h-[calc(100dvh-2rem)] md:w-[min(46rem,calc(100vw-2rem))] lg:w-[min(64rem,calc(100vw-2rem))] md:rounded-base md:border md:border-default md:p-6 md:shadow-sm"
      novalidate
      (submit)="$event.preventDefault(); save()"
      *transloco="let t"
      data-testid="widget-dialog"
    >
      <h2 id="widget-dialog-title" class="break-words text-lg font-medium text-heading">{{ data.widget ? t('indicators.dialog.editTitle') : t('indicators.dialog.addTitle') }}</h2>

      <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,24rem)]">
      <div class="flex min-w-0 flex-col gap-4">
      <div class="grid gap-4 sm:grid-cols-2">
        <tmz-form-field [label]="t('indicators.dialog.chartType')" fieldId="widgetType">
          <select tmzInput id="widgetType" (change)="setType($any($event.target).value)" data-testid="dialog-chart-type">
            @for (type of chartTypes; track type) {
              <option [value]="type" [selected]="type === chartType()">{{ t('indicators.chartTypes.' + type) }}</option>
            }
          </select>
        </tmz-form-field>
        <tmz-form-field [label]="t('indicators.dialog.title')" fieldId="widgetTitle" [error]="shown().title">
          <input tmzInput id="widgetTitle" autocomplete="off" [attr.maxlength]="maxTitleLength" [value]="title()" (input)="title.set($any($event.target).value)" data-testid="dialog-title" />
        </tmz-form-field>
      </div>

      <tmz-form-field [label]="t('indicators.dialog.indicator')" fieldId="widgetIndicator" [error]="shown().indicator">
        <select tmzInput id="widgetIndicator" (change)="pick($any($event.target).value)" data-testid="dialog-indicator">
          <option value="" [selected]="!indicator()">{{ t('indicators.dialog.indicatorChoose') }}</option>
          @for (option of listed(); track option.name) {
            <option [value]="option.name" [selected]="option.name === indicator()">{{ optionText(option, t) }}</option>
          }
        </select>
      </tmz-form-field>
      @if (entry(); as entry) {
        <p class="-mt-2 text-xs text-body" data-testid="dialog-format-note">
          @if (entry.description) {
            {{ entry.description }} ·
          }
          {{ entry.unit ? t('indicators.dialog.formatNoteUnit', { format: t('scripts.formats.' + entry.format), unit: entry.unit }) : t('indicators.dialog.formatNote', { format: t('scripts.formats.' + entry.format) }) }}
        </p>
      }
      @if (catalog(); as catalog) {
        @if (!catalog.published) {
          <tmz-alert tone="warning" data-testid="dialog-unpublished">{{ t('indicators.dialog.unpublished', { label: entry()?.label ?? indicator() }) }}</tmz-alert>
        }
      }

      <div class="grid gap-4 sm:grid-cols-2">
        @if (numericColumns().length >= 2 || mode() === 'ratio') {
          <tmz-form-field [label]="t('indicators.dialog.plot')" fieldId="widgetMode">
            <select tmzInput id="widgetMode" (change)="setMode($any($event.target).value)" data-testid="dialog-mode">
              <option value="measure" [selected]="mode() === 'measure'">{{ t('indicators.dialog.modeMeasure') }}</option>
              <option value="ratio" [selected]="mode() === 'ratio'">{{ t('indicators.dialog.modeRatio') }}</option>
            </select>
          </tmz-form-field>
        }
        @if (mode() === 'measure') {
          <tmz-form-field [label]="t('indicators.dialog.measure')" fieldId="widgetMeasure" [error]="shown().measure">
            <select tmzInput id="widgetMeasure" (change)="measure.set($any($event.target).value)" data-testid="dialog-measure">
              <option value="" [selected]="!measure()">{{ defaultMeasureText(t) }}</option>
              @for (column of numericColumns(); track column) {
                <option [value]="column" [selected]="column === measure()">{{ column }}</option>
              }
            </select>
          </tmz-form-field>
        }
      </div>
      @if (mode() === 'ratio') {
        <div class="grid gap-4 sm:grid-cols-2">
          <tmz-form-field [label]="t('indicators.dialog.numerator')" fieldId="widgetNumerator" [error]="shown().ratio">
            <select tmzInput id="widgetNumerator" (change)="numerator.set($any($event.target).value)" data-testid="dialog-numerator">
              <option value="" [selected]="!numerator()">{{ t('indicators.dialog.columnChoose') }}</option>
              @for (column of numericColumns(); track column) {
                <option [value]="column" [selected]="column === numerator()">{{ column }}</option>
              }
            </select>
          </tmz-form-field>
          <tmz-form-field [label]="t('indicators.dialog.denominator')" fieldId="widgetDenominator" [hint]="t('indicators.dialog.ratioHelp')">
            <select tmzInput id="widgetDenominator" (change)="denominator.set($any($event.target).value)" data-testid="dialog-denominator">
              <option value="" [selected]="!denominator()">{{ t('indicators.dialog.columnChoose') }}</option>
              @for (column of numericColumns(); track column) {
                <option [value]="column" [selected]="column === denominator()">{{ column }}</option>
              }
            </select>
          </tmz-form-field>
        </div>
      }

      <div class="grid gap-4 sm:grid-cols-2">
        @if (chartType() !== 'stat') {
          <tmz-form-field [label]="t('indicators.dialog.groupBy')" fieldId="widgetGroupBy" [error]="shown().groupBy" [hint]="groups() ? null : t('indicators.dialog.groupByOptional')">
            <select tmzInput id="widgetGroupBy" (change)="groupBy.set($any($event.target).value)" data-testid="dialog-group-by">
              <option value="" [selected]="!groupBy()">{{ groups() ? t('indicators.dialog.columnChoose') : t('indicators.dialog.groupByNone') }}</option>
              @for (column of allColumns(); track column) {
                <option [value]="column" [selected]="column === groupBy()">{{ column }}</option>
              }
            </select>
          </tmz-form-field>
        }

        @if (sortable()) {
          <tmz-form-field [label]="t('indicators.dialog.sortBy')" fieldId="widgetSortBy" [error]="shown().sort">
            <select tmzInput id="widgetSortBy" (change)="setSortKey($any($event.target).value)" data-testid="dialog-sort-by">
              <option value="" [selected]="sortKey() === ''">{{ t('indicators.dialog.sortDefault') }}</option>
              <option value="value" [selected]="sortKey() === 'value'">{{ t('indicators.dialog.sortValue') }}</option>
              @for (column of sortColumns(); track column) {
                <option [value]="'column:' + column" [selected]="sortKey() === 'column:' + column">{{ column }}</option>
              }
            </select>
          </tmz-form-field>

          @if (sortKey() !== '') {
            <tmz-form-field [label]="t('indicators.dialog.sortDirection')" fieldId="widgetSortDirection">
              <select tmzInput id="widgetSortDirection" (change)="sortDirection.set($any($event.target).value)" data-testid="dialog-sort-direction">
                <option value="asc" [selected]="sortDirection() === 'asc'">{{ t('indicators.dialog.sortAsc') }}</option>
                <option value="desc" [selected]="sortDirection() === 'desc'">{{ t('indicators.dialog.sortDesc') }}</option>
              </select>
            </tmz-form-field>
          }

          @if (showCompareAs()) {
            <tmz-form-field [label]="t('indicators.dialog.sortCompareAs')" fieldId="widgetSortCompareAs" [hint]="compareAs() === 'number' ? t('indicators.dialog.sortCompareAsNumberHelp') : null">
              <select tmzInput id="widgetSortCompareAs" (change)="sortCompareAs.set($any($event.target).value)" data-testid="dialog-sort-compare-as">
                <option value="text" [selected]="compareAs() === 'text'">{{ t('indicators.dialog.sortCompareText') }}</option>
                <option value="number" [selected]="compareAs() === 'number'">{{ t('indicators.dialog.sortCompareNumber') }}</option>
              </select>
            </tmz-form-field>
          }
        }

        @if (chartType() === 'map') {
          <tmz-form-field [label]="t('indicators.dialog.geometry')" fieldId="widgetGeometry" [error]="shown().geometryColumn" [hint]="t('indicators.dialog.geometryHelp')">
            <select tmzInput id="widgetGeometry" (change)="geometryColumn.set($any($event.target).value)" data-testid="dialog-geometry">
              <option value="" [selected]="!geometryColumn()">{{ t('indicators.dialog.columnChoose') }}</option>
              @for (column of geometryColumns(); track column) {
                <option [value]="column" [selected]="column === geometryColumn()">{{ column }}</option>
              }
            </select>
          </tmz-form-field>
        }

        @if (chartType() === 'histogram') {
          <tmz-form-field [label]="t('indicators.dialog.buckets')" fieldId="widgetBuckets" [error]="shown().buckets" [hint]="t('indicators.dialog.bucketsHelp', { max: maxBuckets })">
            <input tmzInput id="widgetBuckets" type="number" min="1" [attr.max]="maxBuckets" step="1" inputmode="numeric" [value]="buckets()" (input)="buckets.set($any($event.target).value)" data-testid="dialog-buckets" />
          </tmz-form-field>
        }
      </div>

      <tmz-disclosure [heading]="t('indicators.dialog.filtersCount', { count: filters().length })" [open]="startsWithFilters" data-testid="dialog-filters">
        @if (catalog()?.published) {
          @if (filters().length) {
            <ul class="mb-3 flex flex-wrap items-center gap-2">
              @for (filter of filters(); track $index) {
                <li class="inline-flex max-w-full items-center rounded-full border border-brand-subtle bg-brand-softer text-sm text-fg-brand-strong" data-testid="dialog-filter-chip">
                  <button type="button" class="min-h-9 min-w-0 truncate rounded-s-full px-3 py-1 text-left focus:outline-none focus:ring-4 focus:ring-brand-medium" [attr.aria-label]="t('cleanData.chip.edit', { name: filter.column })" (click)="openFilter(filter.column)" data-testid="dialog-filter-edit">{{ describe(filter, t) }}</button>
                  <button type="button" class="min-h-9 min-w-9 rounded-e-full px-2 hover:bg-brand-soft focus:outline-none focus:ring-4 focus:ring-brand-medium" [attr.aria-label]="t('cleanData.chip.remove', { name: filter.column })" (click)="removeFilter(filter.column)" data-testid="dialog-filter-remove">
                    <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
                  </button>
                </li>
              }
            </ul>
          }
          @if (filters().length < maxFilters) {
            <div class="flex flex-col gap-2 sm:flex-row sm:items-end">
              <div class="min-w-0 sm:w-64">
                <label for="widgetFilterColumn" class="mb-2 block text-sm text-body">{{ t('indicators.dialog.filterColumn') }}</label>
                <select tmzInput id="widgetFilterColumn" data-testid="filter-column" #filterColumn>
                  @for (column of allColumns(); track column) {
                    <option [value]="column">{{ column }}</option>
                  }
                </select>
              </div>
              <button tmzButton variant="secondary" type="button" (click)="openFilter(filterColumn.value)" data-testid="filter-add">{{ t('indicators.dialog.addFilter') }}</button>
            </div>
          } @else {
            <p class="text-sm text-body" data-testid="filters-full">{{ t('indicators.dialog.filtersFull', { count: maxFilters }) }}</p>
          }
        } @else {
          <p class="text-sm text-body">{{ t('indicators.dialog.filtersUnpublished') }}</p>
        }
      </tmz-disclosure>
      </div>

      <section aria-labelledby="widgetPreviewTitle" class="lg:sticky lg:top-0 lg:self-start">
        <h3 id="widgetPreviewTitle" class="mb-2 text-sm font-medium text-heading">{{ t('indicators.dialog.preview') }}</h3>
        <div class="rounded-base border border-default p-3" [class]="chartType() === 'stat' ? 'h-40' : 'h-72'" data-testid="dialog-preview">
          <app-widget-view
            [projectId]="data.projectId"
            [chartType]="chartType()"
            [config]="previewConfig()"
            [indicator]="entry()"
            [debounceMs]="previewDelayMs"
            [placeholder]="t('indicators.dialog.previewIncomplete')"
          />
        </div>
      </section>
      </div>

      @if (error(); as message) {
        <tmz-alert tone="danger" data-testid="dialog-error">{{ message }}</tmz-alert>
      }

      <div class="mt-2 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()" data-testid="dialog-cancel">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" [disabled]="busy()" data-testid="dialog-save">{{ busy() ? t('indicators.dialog.saving') : t('common.save') }}</button>
      </div>
    </form>
  `,
})
export class WidgetDialog {
  protected readonly data = inject<WidgetDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Widget>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly dialog = inject(Dialog);
  private readonly transloco = inject(TranslocoService);

  protected readonly chartTypes = chartTypes;
  protected readonly maxBuckets = maxBuckets;
  protected readonly maxFilters = maxFilters;
  protected readonly maxTitleLength = maxTitleLength;
  protected readonly previewDelayMs = previewDelayMs;

  private readonly start = this.data.widget?.config ?? emptyConfig();
  /** The filters start open only for a widget that already has some; adding the first one never closes or reopens it. */
  protected readonly startsWithFilters = this.start.filters.length > 0;

  protected readonly chartType = signal<ChartType>(this.data.widget?.chartType ?? 'column');
  protected readonly title = signal(this.data.widget?.title ?? '');
  /** A new widget starts on the first indicator that has published, or the first there is. */
  protected readonly indicator = signal(
    this.data.widget ? this.start.indicator : (this.data.indicators.find((i) => i.published) ?? this.data.indicators[0])?.name ?? '',
  );
  protected readonly mode = signal<Mode>(this.start.ratio ? 'ratio' : 'measure');
  protected readonly measure = signal(this.start.measure ?? '');
  protected readonly numerator = signal(this.start.ratio?.numerator ?? '');
  protected readonly denominator = signal(this.start.ratio?.denominator ?? '');
  protected readonly groupBy = signal(this.start.groupBy ?? '');
  protected readonly filters = signal<DataFilter[]>(this.start.filters.map(normalizeFilter));
  protected readonly geometryColumn = signal(this.start.geometryColumn ?? '');
  protected readonly buckets = signal(this.start.buckets === null ? '' : String(this.start.buckets));
  /** The order, as the "Order bars by" select holds it: '' for the default order, 'value', or 'column:' and the column's name. */
  protected readonly sortKey = signal(sortKeyOf(this.start.sort));
  protected readonly sortDirection = signal<'asc' | 'desc'>(this.start.sort?.direction ?? 'asc');
  /** What was chosen, or saved; what is sent also depends on the column's type (see `compareAs`). */
  protected readonly sortCompareAs = signal<'text' | 'number' | null>(this.start.sort?.compareAs ?? null);

  /** The chosen indicator's published table; null while it loads, when none is chosen or when it cannot be read. */
  protected readonly catalog = signal<IndicatorColumns | null>(null);
  /** The next table to arrive fills in the defaults: a new widget, or another indicator just chosen. */
  private fresh = !this.data.widget;

  protected readonly submitted = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly serverFieldErrors = signal<Problems>({});

  protected readonly entry = computed(() => this.data.indicators.find((i) => i.name === this.indicator()) ?? null);

  /** The indicators to choose from, with the one the widget names kept on top when the project no longer lists it. */
  protected readonly listed = computed<IndicatorEntry[]>(() => {
    const chosen = this.indicator();
    const all = this.data.indicators;
    return chosen && !all.some((i) => i.name === chosen)
      ? [{ name: chosen, label: chosen, description: null, unit: null, format: 'decimal', published: false, publishedAt: null, runId: null, rows: null }, ...all]
      : all;
  });

  /** Whether the chart type plots one value per group, so the group column is required. */
  protected readonly groups = computed(() => needsGroup(this.chartType()));

  /** The table's columns, with any the widget names that the table no longer has kept so the form shows what is saved. */
  private columnsWith(names: readonly string[], current: readonly string[]): string[] {
    return [...current.filter((name) => name && !names.includes(name)), ...names];
  }

  protected readonly numericColumns = computed(() =>
    this.columnsWith(this.catalog()?.numeric ?? [], [this.measure(), this.numerator(), this.denominator()]).filter((name, i, all) => all.indexOf(name) === i),
  );
  protected readonly allColumns = computed(() => this.columnsWith(this.catalog()?.columns.map((c) => c.name) ?? [], [this.groupBy()]));
  protected readonly sortColumn = computed(() => (this.sortKey().startsWith('column:') ? this.sortKey().slice('column:'.length) : ''));
  /** A saved sort column the table no longer has is kept in front here only, not in the group-by or filter lists. */
  protected readonly sortColumns = computed(() => this.columnsWith(this.catalog()?.columns.map((c) => c.name) ?? [], [this.sortColumn()]));
  protected readonly sortable = computed(() => this.chartType() === 'bar' || this.chartType() === 'column');
  /** The sort column's type; null while the table loads, when it cannot be read and when the table no longer has the column. */
  private readonly sortType = computed(() => this.catalog()?.columns.find((c) => c.name === this.sortColumn())?.type ?? null);
  /** Only a text, integer or decimal column is compared as text or as a number; a date, timestamp or true/false column is not. */
  protected readonly showCompareAs = computed(() => this.sortable() && ['text', 'integer', 'decimal'].includes(this.sortType() ?? ''));
  /**
   * How the sort column is compared, as it is shown and sent. A date, timestamp or true/false column sends none, which also clears
   * a value left from a previous column. For a text, integer or decimal column, a missing value shows the type's own way. While the
   * type is unknown, what was saved is kept.
   */
  protected readonly compareAs = computed<'text' | 'number' | null>(() => {
    const type = this.sortType();
    if (type === null) {
      return this.sortCompareAs();
    }
    return this.showCompareAs() ? (this.sortCompareAs() ?? ownComparison(type)) : null;
  });
  /** A geometry is WKT text. */
  protected readonly geometryColumns = computed(() =>
    this.columnsWith(this.catalog()?.columns.filter((c) => c.type === 'text').map((c) => c.name) ?? [], [this.geometryColumn()]),
  );

  private readonly draft = computed<WidgetConfig>(() => {
    const buckets = this.buckets().trim();
    return queryFor(this.chartType(), {
      indicator: this.indicator(),
      measure: this.mode() === 'measure' && this.measure() ? this.measure() : null,
      ratio: this.mode() === 'ratio' ? { numerator: this.numerator(), denominator: this.denominator() } : null,
      groupBy: this.groupBy() || null,
      filters: this.filters(),
      geometryColumn: this.geometryColumn() || null,
      buckets: buckets === '' ? null : Number(buckets),
      sort: this.sortConfig(),
    });
  });

  private sortConfig(): WidgetSort | null {
    const key = this.sortKey();
    if (key === '') {
      return null;
    }
    const direction = this.sortDirection();
    return key === 'value'
      ? { by: 'value', column: null, compareAs: null, direction }
      : { by: 'column', column: this.sortColumn() || null, compareAs: this.compareAs(), direction };
  }

  /** What is wrong with the draft, by field. */
  protected readonly problems = computed<Problems>(() => {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const problems: Problems = {};
    const type = this.chartType();
    const config = this.draft();
    const title = this.title().trim();
    if (!title) {
      problems.title = t('indicators.dialog.errors.title');
    } else if (title.length > maxTitleLength) {
      problems.title = t('indicators.dialog.errors.titleLong', { max: maxTitleLength });
    }
    if (!config.indicator) {
      problems.indicator = t('indicators.dialog.errors.indicator');
    }
    if (config.ratio && (!config.ratio.numerator || !config.ratio.denominator)) {
      problems.ratio = t('indicators.dialog.errors.ratio');
    }
    if (needsGroup(type) && !config.groupBy) {
      problems.groupBy = t('indicators.dialog.errors.groupBy');
    }
    if (type === 'map' && !config.geometryColumn) {
      problems.geometryColumn = t('indicators.dialog.errors.geometry');
    }
    if (type === 'histogram' && config.buckets !== null && (!Number.isInteger(config.buckets) || config.buckets < 1 || config.buckets > maxBuckets)) {
      problems.buckets = t('indicators.dialog.errors.buckets', { max: maxBuckets });
    }
    if (config.sort?.by === 'column' && !config.sort.column) {
      problems.sort = t('indicators.dialog.errors.sort');
    }
    return problems;
  });

  /** Problems are shown once Save was tried, and the server's refusals as they came. */
  protected readonly shown = computed<Problems>(() => ({ ...(this.submitted() ? this.problems() : {}), ...this.serverFieldErrors() }));

  /** The query to preview; null while the query part of the draft is incomplete. */
  protected readonly previewConfig = computed<WidgetConfig | null>(() => {
    const { title: _title, ...query } = this.problems();
    return Object.keys(query).length ? null : this.draft();
  });

  constructor() {
    toObservable(this.indicator)
      .pipe(
        tap(() => this.catalog.set(null)),
        switchMap((name) =>
          name
            ? this.service.columns(this.data.projectId, name).pipe(
                catchError((failure) => {
                  this.errors.report(failure);
                  return of(null);
                }),
              )
            : of(null),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((catalog) => {
        this.catalog.set(catalog);
        if (catalog?.published && this.fresh) {
          this.fillDefaults(catalog);
        }
        this.fresh = false;
      });
  }

  /** What a new widget over the table starts with: its default group column and, for a ratio, the conventional columns. */
  private fillDefaults(catalog: IndicatorColumns): void {
    if (!this.groupBy()) {
      this.groupBy.set(catalog.defaults.groupBy ?? '');
    }
    if (!this.numerator() && catalog.numeric.includes('numerator')) {
      this.numerator.set('numerator');
    }
    if (!this.denominator() && catalog.numeric.includes('denominator')) {
      this.denominator.set('denominator');
    }
  }

  protected optionText(option: IndicatorEntry, t: (key: string, params?: Record<string, unknown>) => string): string {
    const text = `${option.label} (${option.name})`;
    return option.published ? text : `${text} · ${t('indicators.dialog.notPublished')}`;
  }

  protected defaultMeasureText(t: (key: string, params?: Record<string, unknown>) => string): string {
    const measure = this.catalog()?.defaults.measure;
    return measure ? t('indicators.dialog.measureDefault', { column: measure }) : t('indicators.dialog.measureDefaultUnknown');
  }

  protected describe(filter: DataFilter, t: (key: string, params?: Record<string, unknown>) => string): string {
    return describeFilter(filter, t);
  }

  protected setType(value: string): void {
    this.chartType.set(value as ChartType);
    this.serverFieldErrors.set({});
  }

  /**
   * Choosing an order sets the direction that reads naturally (the plotted value, largest first; a column, smallest first) when the
   * order was off or changes between the value and a column; moving between columns keeps it. A column sets how it is compared from
   * its type.
   */
  protected setSortKey(value: string): void {
    const previous = this.sortKey();
    this.sortKey.set(value);
    this.serverFieldErrors.set({});
    if (value === 'value' && previous !== 'value') {
      this.sortDirection.set('desc');
    } else if (value.startsWith('column:') && !previous.startsWith('column:')) {
      this.sortDirection.set('asc');
    }
    if (value.startsWith('column:')) {
      const type = this.catalog()?.columns.find((c) => c.name === this.sortColumn())?.type;
      this.sortCompareAs.set(type ? ownComparison(type) : null);
    } else {
      this.sortCompareAs.set(null);
    }
  }

  protected setMode(value: string): void {
    this.mode.set(value as Mode);
    this.serverFieldErrors.set({});
  }

  /** Another indicator has other columns: what was chosen from the old table is dropped, and the new one's defaults fill in. */
  protected pick(name: string): void {
    if (name === this.indicator()) {
      return;
    }
    this.indicator.set(name);
    this.measure.set('');
    this.numerator.set('');
    this.denominator.set('');
    this.groupBy.set('');
    this.geometryColumn.set('');
    this.filters.set([]);
    this.sortKey.set('');
    this.sortDirection.set('asc');
    this.sortCompareAs.set(null);
    this.fresh = true;
    this.serverFieldErrors.set({});
  }

  protected removeFilter(column: string): void {
    this.filters.update((list) => list.filter((f) => f.column !== column));
  }

  /** Opens the clean data dashboard's filter dialog for a column; one filter per column, so an existing one is edited. */
  protected openFilter(columnName: string): void {
    const column = this.catalog()?.columns.find((c) => c.name === columnName);
    if (!column) {
      return;
    }
    const existing = this.filters().find((f) => f.column === columnName) ?? null;
    const data: DataFilterDialogData = {
      column,
      filter: existing,
      timeZone: this.data.timeZone,
      // The table cannot list a column's values, so they are typed.
      allowTypedValues: true,
      loadValues: () => of([]),
      check: (filter) => this.check(filter),
    };
    const ref = this.dialog.open<DataFilterDialogResult, DataFilterDialogData>(DataFilterDialog, {
      data,
      ariaLabelledBy: 'data-filter-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
      maxWidth: '100vw',
      maxHeight: '100dvh',
    });
    ref.closed.subscribe((result) => {
      if (!result) {
        return;
      }
      if ('remove' in result) {
        this.removeFilter(columnName);
      } else {
        const normalized = normalizeFilter(result.filter);
        this.filters.update((list) => (existing ? list.map((f) => (f.column === columnName ? normalized : f)) : [...list, normalized]));
      }
    });
  }

  /** The server's verdict on a filter, asked with the rest of the draft as it stands; there is nothing to ask until an indicator is chosen. */
  private check(filter: DataFilter): Observable<unknown> {
    const config = this.draft();
    if (!config.indicator) {
      return of(null);
    }
    const others = config.filters.filter((f) => f.column !== filter.column);
    return this.service.query(this.data.projectId, { ...config, filters: [...others, normalizeFilter(filter)] }, this.chartType());
  }

  protected save(): void {
    this.submitted.set(true);
    this.serverFieldErrors.set({});
    this.error.set(null);
    if (Object.keys(this.problems()).length) {
      return;
    }
    const body = { title: this.title().trim(), chartType: this.chartType(), config: this.draft() };
    this.busy.set(true);
    const request = this.data.widget ? this.service.update(this.data.projectId, this.data.widget.id, body) : this.service.create(this.data.projectId, body);
    request.subscribe({
      next: (widget) => this.ref.close(widget),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        const fields: Problems = {};
        for (const [path, message] of Object.entries(parsed.fieldErrors)) {
          const key = this.fieldFor(path);
          if (key && !fields[key]) {
            fields[key] = message;
          }
        }
        this.serverFieldErrors.set(fields);
        const others = Object.entries(parsed.fieldErrors).filter(([path]) => !this.fieldFor(path));
        this.error.set(Object.keys(fields).length && !others.length ? null : others.length ? others.map(([, message]) => message).join(' ') : parsed.message);
      },
    });
  }

  private fieldFor(path: string): keyof Problems | null {
    const name = path.toLowerCase();
    if (name.includes('title')) return 'title';
    if (name.includes('ratio')) return 'ratio';
    if (name.includes('measure')) return 'measure';
    if (name.includes('indicator')) return 'indicator';
    if (name.includes('groupby')) return 'groupBy';
    if (name.includes('geometrycolumn')) return 'geometryColumn';
    if (name.includes('buckets')) return 'buckets';
    if (name.includes('sort')) return 'sort';
    return null;
  }
}
