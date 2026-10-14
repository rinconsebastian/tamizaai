import { DIALOG_DATA, Dialog, DialogRef } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Observable, of } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import { DataColumn, DataFilter } from '../../../core/projects/data.service';
import {
  ChartType,
  IndicatorsColumns,
  IndicatorsService,
  ValueField,
  Widget,
  WidgetConfig,
  chartTypes,
  emptyConfig,
  maxBuckets,
  maxFilters,
  maxTitleLength,
  takesOneIndicator,
  valueFields,
} from '../../../core/projects/indicators.service';
import { Alert, Button, FormField, Input } from '../../../shared/ui';
import { DataFilterDialog, DataFilterDialogData, DataFilterDialogResult } from '../data-filter-dialog';
import { describeFilter, normalizeFilter } from '../data-view';
import { WidgetView, queryFor } from './widget-view';

export interface WidgetDialogData {
  projectId: string;
  /** The widget to edit; null for a new one. */
  widget: Widget | null;
  /** The published table's columns and indicators; empty (published false) while nothing is published. */
  catalog: IndicatorsColumns;
  timeZone: string;
}

/** Most series the palette has colors for: a chart of several indicators takes this many. */
export const maxSeries = 8;
/** Debounce of the live preview: one request once the draft stops changing. */
export const previewDelayMs = 400;
/** Past this many indicators the picker offers a search box. */
const searchFrom = 8;

type Problems = Partial<Record<'title' | 'indicators' | 'groupBy' | 'geometryColumn' | 'buckets', string>>;

/**
 * Adds or edits a widget (design D7, D9): its chart type and title, its query (indicators the chart type allows, group-by column,
 * plotted field, filters, geometry column for a map, histogram buckets) and a live preview from the same query endpoint that
 * renders a saved widget. A draft the server refuses shows that refusal where the preview is, never the previous chart.
 */
@Component({
  selector: 'app-widget-dialog',
  imports: [TranslocoDirective, Alert, Button, FormField, Input, WidgetView],
  template: `
    <form
      class="flex h-dvh w-screen flex-col gap-4 overflow-y-auto bg-neutral-primary-soft p-4 md:h-auto md:max-h-[calc(100dvh-2rem)] md:w-[min(46rem,calc(100vw-2rem))] md:rounded-base md:border md:border-default md:p-6 md:shadow-sm"
      novalidate
      (submit)="$event.preventDefault(); save()"
      *transloco="let t"
      data-testid="widget-dialog"
    >
      <h2 id="widget-dialog-title" class="break-words text-lg font-medium text-heading">{{ data.widget ? t('indicators.dialog.editTitle') : t('indicators.dialog.addTitle') }}</h2>

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

      <fieldset class="min-w-0" data-testid="dialog-indicators" [attr.aria-describedby]="shown().indicators ? 'widgetIndicatorsError' : null">
        <legend class="mb-2 text-sm font-medium text-heading">{{ t('indicators.dialog.indicators') }}</legend>
        <p class="mb-2 text-xs text-body" data-testid="indicators-rule">{{ single() ? t('indicators.dialog.indicatorsOne') : t('indicators.dialog.indicatorsMany', { count: maxSeries }) }}</p>
        @if (catalog.published) {
          @if (catalog.indicators.length > searchFrom) {
            <input tmzInput type="search" class="mb-2" autocomplete="off" [attr.aria-label]="t('indicators.dialog.search')" [placeholder]="t('indicators.dialog.search')" [value]="search()" (input)="search.set($any($event.target).value)" data-testid="indicator-search" />
          }
          <ul class="max-h-44 divide-y divide-default overflow-y-auto rounded-base border border-default" data-testid="indicator-list">
            @for (name of listed(); track name) {
              <li>
                <label class="flex min-h-10 cursor-pointer items-start gap-3 px-3 py-2 text-sm">
                  @if (single()) {
                    <input type="radio" name="widgetIndicator" class="mt-0.5 size-4 shrink-0" [value]="name" [checked]="indicators().includes(name)" (change)="pick(name)" data-testid="indicator-option" />
                  } @else {
                    <input
                      type="checkbox"
                      name="widgetIndicator"
                      class="mt-0.5 size-4 shrink-0"
                      [value]="name"
                      [checked]="indicators().includes(name)"
                      [disabled]="!indicators().includes(name) && indicators().length >= maxSeries"
                      (change)="pick(name)"
                      data-testid="indicator-option"
                    />
                  }
                  <span class="min-w-0 flex-1 break-all text-heading">{{ name }}</span>
                  @if (!known(name)) {
                    <span class="shrink-0 text-xs text-fg-warning" data-testid="indicator-gone">{{ t('indicators.dialog.notPublished') }}</span>
                  }
                </label>
              </li>
            } @empty {
              <li class="px-3 py-2 text-sm text-body">{{ t('indicators.dialog.noIndicators') }}</li>
            }
          </ul>
        } @else {
          <input tmzInput autocomplete="off" [attr.aria-label]="t('indicators.dialog.indicatorsText')" [value]="indicators().join(', ')" (input)="typeIndicators($any($event.target).value)" data-testid="indicator-text" />
          <p class="mt-2 text-xs text-body">{{ t('indicators.dialog.unpublishedHelp') }}</p>
        }
        <p class="mt-2 text-sm text-body" data-testid="indicators-selected">{{ t('indicators.dialog.selected', { count: indicators().length }) }}</p>
        @if (shown().indicators; as message) {
          <p id="widgetIndicatorsError" class="mt-1 text-sm text-fg-danger-strong" data-testid="field-error">{{ message }}</p>
        }
      </fieldset>

      <div class="grid gap-4 sm:grid-cols-2">
        <tmz-form-field [label]="t('indicators.dialog.groupBy')" fieldId="widgetGroupBy" [error]="shown().groupBy">
          @if (catalog.published) {
            <select tmzInput id="widgetGroupBy" (change)="groupBy.set($any($event.target).value)" data-testid="dialog-group-by">
              @for (column of groupColumns(); track column.name) {
                <option [value]="column.name" [selected]="column.name === groupBy()">{{ column.name }}</option>
              }
            </select>
          } @else {
            <input tmzInput id="widgetGroupBy" autocomplete="off" [value]="groupBy()" (input)="groupBy.set($any($event.target).value)" data-testid="dialog-group-by" />
          }
        </tmz-form-field>
        <tmz-form-field [label]="t('indicators.dialog.valueField')" fieldId="widgetValueField">
          <select tmzInput id="widgetValueField" (change)="valueField.set($any($event.target).value)" data-testid="dialog-value-field">
            @for (field of valueFields; track field) {
              <option [value]="field" [selected]="field === valueField()">{{ t('indicators.valueFields.' + field) }}</option>
            }
          </select>
        </tmz-form-field>
      </div>

      @if (chartType() === 'map') {
        <tmz-form-field [label]="t('indicators.dialog.geometry')" fieldId="widgetGeometry" [error]="shown().geometryColumn" [hint]="t('indicators.dialog.geometryHelp')">
          @if (catalog.published) {
            <select tmzInput id="widgetGeometry" (change)="geometryColumn.set($any($event.target).value)" data-testid="dialog-geometry">
              <option value="" [selected]="!geometryColumn()">{{ t('indicators.dialog.geometryChoose') }}</option>
              @for (column of geometryColumns(); track column.name) {
                <option [value]="column.name" [selected]="column.name === geometryColumn()">{{ column.name }}</option>
              }
            </select>
          } @else {
            <input tmzInput id="widgetGeometry" autocomplete="off" [value]="geometryColumn()" (input)="geometryColumn.set($any($event.target).value)" data-testid="dialog-geometry" />
          }
        </tmz-form-field>
      }

      @if (chartType() === 'histogram') {
        <tmz-form-field class="sm:w-56" [label]="t('indicators.dialog.buckets')" fieldId="widgetBuckets" [error]="shown().buckets" [hint]="t('indicators.dialog.bucketsHelp', { max: maxBuckets })">
          <input tmzInput id="widgetBuckets" type="number" min="1" [attr.max]="maxBuckets" step="1" inputmode="numeric" [value]="buckets()" (input)="buckets.set($any($event.target).value)" data-testid="dialog-buckets" />
        </tmz-form-field>
      }

      <section aria-labelledby="widgetFiltersTitle" data-testid="dialog-filters">
        <h3 id="widgetFiltersTitle" class="mb-2 text-sm font-medium text-heading">{{ t('indicators.dialog.filters') }}</h3>
        @if (catalog.published) {
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
                  @for (column of catalog.columns; track column.name) {
                    <option [value]="column.name">{{ column.name }}</option>
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
      </section>

      <section aria-labelledby="widgetPreviewTitle">
        <h3 id="widgetPreviewTitle" class="mb-2 text-sm font-medium text-heading">{{ t('indicators.dialog.preview') }}</h3>
        <div class="h-72 rounded-base border border-default p-3" data-testid="dialog-preview">
          <app-widget-view
            [projectId]="data.projectId"
            [chartType]="chartType()"
            [config]="previewConfig()"
            [enabled]="catalog.published"
            [debounceMs]="previewDelayMs"
            [placeholder]="t('indicators.dialog.previewIncomplete')"
          />
        </div>
      </section>

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

  protected readonly catalog = this.data.catalog;
  protected readonly chartTypes = chartTypes;
  protected readonly valueFields = valueFields;
  protected readonly maxSeries = maxSeries;
  protected readonly maxBuckets = maxBuckets;
  protected readonly maxFilters = maxFilters;
  protected readonly maxTitleLength = maxTitleLength;
  protected readonly previewDelayMs = previewDelayMs;
  protected readonly searchFrom = searchFrom;

  private readonly start = this.data.widget?.config ?? emptyConfig();

  protected readonly chartType = signal<ChartType>(this.data.widget?.chartType ?? 'column');
  protected readonly title = signal(this.data.widget?.title ?? '');
  protected readonly indicators = signal<string[]>([...this.start.indicators]);
  protected readonly groupBy = signal(this.start.groupBy);
  protected readonly valueField = signal<ValueField>(this.start.valueField);
  protected readonly filters = signal<DataFilter[]>(this.start.filters.map(normalizeFilter));
  protected readonly geometryColumn = signal(this.start.geometryColumn ?? '');
  protected readonly buckets = signal(this.start.buckets === null ? '' : String(this.start.buckets));
  protected readonly search = signal('');

  protected readonly submitted = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly serverFieldErrors = signal<Problems>({});

  protected readonly single = computed(() => takesOneIndicator(this.chartType()));

  /** The indicators to choose from, with any the widget names that the table no longer has kept on top. */
  protected readonly listed = computed(() => {
    const all = this.catalog.indicators;
    const gone = this.indicators().filter((name) => !all.includes(name));
    const term = this.search().trim().toLowerCase();
    return [...gone, ...all.filter((name) => !term || name.toLowerCase().includes(term))];
  });

  protected readonly groupColumns = computed(() => {
    const columns = this.catalog.columns;
    return columns.some((c) => c.name === this.groupBy()) ? columns : [{ name: this.groupBy(), type: 'text', label: null } as DataColumn, ...columns];
  });

  /** A geometry is WKT text. */
  protected readonly geometryColumns = computed(() => {
    const text = this.catalog.columns.filter((c) => c.type === 'text');
    const current = this.geometryColumn();
    return current && !text.some((c) => c.name === current) ? [{ name: current, type: 'text', label: null } as DataColumn, ...text] : text;
  });

  private readonly draft = computed<WidgetConfig>(() => {
    const type = this.chartType();
    const buckets = this.buckets().trim();
    return queryFor(type, {
      indicators: this.indicators(),
      groupBy: this.groupBy().trim(),
      valueField: this.valueField(),
      filters: this.filters(),
      geometryColumn: this.geometryColumn().trim() || null,
      buckets: buckets === '' ? null : Number(buckets),
    });
  });

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
    if (takesOneIndicator(type) ? config.indicators.length !== 1 : config.indicators.length < 1) {
      problems.indicators = takesOneIndicator(type) ? t('indicators.dialog.errors.indicatorsOne') : t('indicators.dialog.errors.indicatorsMany');
    } else if (!takesOneIndicator(type) && config.indicators.length > maxSeries) {
      problems.indicators = t('indicators.dialog.errors.indicatorsMax', { count: maxSeries });
    }
    if (!config.groupBy) {
      problems.groupBy = t('indicators.dialog.errors.groupBy');
    }
    if (type === 'map' && !config.geometryColumn) {
      problems.geometryColumn = t('indicators.dialog.errors.geometry');
    }
    if (type === 'histogram' && config.buckets !== null && (!Number.isInteger(config.buckets) || config.buckets < 1 || config.buckets > maxBuckets)) {
      problems.buckets = t('indicators.dialog.errors.buckets', { max: maxBuckets });
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

  protected known(name: string): boolean {
    return this.catalog.indicators.includes(name);
  }

  protected describe(filter: DataFilter, t: (key: string, params?: Record<string, unknown>) => string): string {
    return describeFilter(filter, t);
  }

  protected setType(value: string): void {
    const type = value as ChartType;
    this.chartType.set(type);
    if (takesOneIndicator(type) && this.indicators().length > 1) {
      this.indicators.update((list) => list.slice(0, 1)); // a second indicator does not fit this chart type
    }
    this.serverFieldErrors.set({});
  }

  protected pick(name: string): void {
    if (this.single()) {
      this.indicators.set([name]);
    } else {
      this.indicators.update((list) => (list.includes(name) ? list.filter((n) => n !== name) : list.length < maxSeries ? [...list, name] : list));
    }
    this.serverFieldErrors.set({});
  }

  /** While nothing is published there is no list to choose from: indicators are typed, separated by commas. */
  protected typeIndicators(text: string): void {
    const names = text
      .split(',')
      .map((n) => n.trim())
      .filter((n, i, all) => n && all.indexOf(n) === i);
    this.indicators.set(this.single() ? names.slice(0, 1) : names);
  }

  protected removeFilter(column: string): void {
    this.filters.update((list) => list.filter((f) => f.column !== column));
  }

  /** Opens the clean data dashboard's filter dialog for a column; one filter per column, so an existing one is edited. */
  protected openFilter(columnName: string): void {
    const column = this.catalog.columns.find((c) => c.name === columnName);
    if (!column) {
      return;
    }
    const existing = this.filters().find((f) => f.column === columnName) ?? null;
    const data: DataFilterDialogData = {
      column,
      filter: existing,
      timeZone: this.data.timeZone,
      allowTypedValues: columnName !== 'indicator',
      loadValues: (search) => of(this.indicatorValues(columnName, search)),
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

  /** The column `indicator` can list its values; no other column can, so values are typed. */
  private indicatorValues(column: string, search: string): { value: string; count: number | null }[] {
    if (column !== 'indicator') {
      return [];
    }
    const term = search.toLowerCase();
    return this.catalog.indicators.filter((name) => !term || name.toLowerCase().includes(term)).map((value) => ({ value, count: null }));
  }

  /** The server's verdict on a filter, asked with the rest of the draft as it stands; there is nothing to ask until an indicator is chosen. */
  private check(filter: DataFilter): Observable<unknown> {
    const config = this.draft();
    if (!config.indicators.length || !config.groupBy) {
      return of(null);
    }
    const others = config.filters.filter((f) => f.column !== filter.column);
    return this.service.query(this.data.projectId, { ...config, filters: [...others, normalizeFilter(filter)] });
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
    if (name.includes('indicators')) return 'indicators';
    if (name.includes('groupby')) return 'groupBy';
    if (name.includes('geometrycolumn')) return 'geometryColumn';
    if (name.includes('buckets')) return 'buckets';
    return null;
  }
}
