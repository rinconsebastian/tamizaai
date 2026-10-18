import { DIALOG_DATA, Dialog, DialogRef } from '@angular/cdk/dialog';
import { Component, Signal, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Observable, catchError, of, switchMap, tap } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import { Basemaps } from '../../../core/basemaps.service';
import { DataFilter } from '../../../core/projects/data.service';
import {
  BasemapKind,
  ChartType,
  IndicatorColumns,
  IndicatorEntry,
  IndicatorsService,
  Widget,
  WidgetConfig,
  WidgetPatch,
  chartTypes,
  emptyConfig,
  maxBuckets,
  maxFilters,
  maxTitleLength,
  maxTooltipItems,
  maxTooltipLabel,
  needsGroup,
  TooltipFormat,
  WidgetSort,
  WidgetTooltipItem,
  basemapKinds,
} from '../../../core/projects/indicators.service';
import { DashboardEntry } from '../../../core/projects/projects.service';
import { Alert, Button, Disclosure, FormField, Input } from '../../../shared/ui';
import { DataFilterDialog, DataFilterDialogData, DataFilterDialogResult } from '../data-filter-dialog';
import { describeFilter, normalizeFilter } from '../data-view';
import { WidgetView, savedConfig } from './widget-view';

export interface WidgetDialogData {
  projectId: string;
  /** The dashboard the dialog is opened on: a new widget is added to it, and the edited widget is on it. */
  dashboardId: string;
  /** The project's dashboards as the page knows them, kept current: an edited widget can move to another of them (design D9). */
  dashboards: Signal<DashboardEntry[]>;
  /** The widget to edit; null for a new one. */
  widget: Widget | null;
  /** The project's indicators, whether or not they have published. */
  indicators: IndicatorEntry[];
  timeZone: string;
  /** Told when the API refuses the dashboard chosen (removed meanwhile, or full): the page's list of dashboards may be out of date. */
  refused?: () => void;
}

/** The widget saved, or `gone` when its dashboard no longer exists (`dashboards.notFound`), for the page to catch up. */
export type WidgetDialogResult = Widget | { gone: true };

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
type Problems = Partial<
  Record<'title' | 'dashboardId' | 'indicator' | 'measure' | 'ratio' | 'groupBy' | 'geometryColumn' | 'buckets' | 'sort' | 'tooltip' | 'basemap', string>
>;
/** What the server refused in one tooltip item. */
type ItemProblems = Partial<Record<'column' | 'label' | 'format', string>>;

const tooltipFormats: readonly TooltipFormat[] = ['count', 'decimal', 'percent'];

/**
 * Adds or edits a widget (design D6, D8): its chart type and title, its query (the indicator, a measure or a ratio of two numeric
 * columns, the group-by column, filters, the geometry column of a map, the buckets of a histogram) and a live preview from the
 * same query endpoint that renders a saved widget. The columns offered are those of the chosen indicator's published table. A draft
 * the server refuses shows that refusal where the preview is, never the previous chart. An edited widget can move to another of the
 * project's dashboards (design D9 of add-multiple-dashboards).
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
      <!-- Every control here changes the widget, so a change marks the form edited; the Dashboard select alone does not (see touched). -->
      <div class="flex min-w-0 flex-col gap-4" (input)="touched($event)" (change)="touched($event)">
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

      @if (offersDashboards()) {
        <div class="grid gap-4 sm:grid-cols-2">
          <tmz-form-field [label]="t('indicators.dialog.dashboard')" fieldId="widgetDashboard" [error]="shown().dashboardId" [hint]="t('indicators.dialog.dashboardHelp')">
            <select tmzInput id="widgetDashboard" (change)="setDashboard($any($event.target).value)" data-testid="dialog-dashboard">
              @for (dashboard of data.dashboards(); track dashboard.id) {
                <option [value]="dashboard.id" [selected]="dashboard.id === destination()">{{ dashboard.name }}</option>
              }
            </select>
          </tmz-form-field>
        </div>
      }

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
          @if (basemapOptions().length) {
            <tmz-form-field [label]="t('indicators.dialog.basemap')" fieldId="widgetBasemap" [error]="shown().basemap" [hint]="t('indicators.dialog.basemapHelp')">
              <select tmzInput id="widgetBasemap" (change)="setBasemap($any($event.target).value)" data-testid="dialog-basemap">
                <option value="" [selected]="!basemap()">{{ t('indicators.map.basemaps.none') }}</option>
                @for (option of basemapOptions(); track option.kind) {
                  <option [value]="option.kind" [selected]="option.kind === basemap()">{{ option.offered ? t('indicators.map.basemaps.' + option.kind) : t('indicators.dialog.basemapGone', { name: t('indicators.map.basemaps.' + option.kind) }) }}</option>
                }
              </select>
            </tmz-form-field>
          }
        }

        @if (chartType() === 'histogram') {
          <tmz-form-field [label]="t('indicators.dialog.buckets')" fieldId="widgetBuckets" [error]="shown().buckets" [hint]="t('indicators.dialog.bucketsHelp', { max: maxBuckets })">
            <input tmzInput id="widgetBuckets" type="number" min="1" [attr.max]="maxBuckets" step="1" inputmode="numeric" [value]="buckets()" (input)="buckets.set($any($event.target).value)" data-testid="dialog-buckets" />
          </tmz-form-field>
        }
      </div>

      @if (chartType() === 'map') {
        <fieldset class="flex flex-col gap-3" data-testid="dialog-tooltip">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.tooltip.legend') }}</legend>
          <p class="-mt-2 text-sm text-body">{{ t('indicators.dialog.tooltip.help', { max: maxTooltipItems }) }}</p>
          @for (item of tooltip(); track $index; let i = $index; let first = $first; let last = $last) {
            <div class="grid gap-2 rounded-base border border-default p-3 sm:grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)_auto]" data-testid="tooltip-item">
              <div class="min-w-0">
                <select tmzInput [attr.id]="'tooltipColumn' + i" [attr.aria-label]="t('indicators.dialog.tooltip.column', { n: i + 1 })" (change)="setTooltipColumn(i, $any($event.target).value)" data-testid="tooltip-column">
                  <option value="" [selected]="!item.column">{{ t('indicators.dialog.columnChoose') }}</option>
                  @for (option of tooltipOptions(i); track option.name) {
                    <option [value]="option.name" [selected]="option.name === item.column">{{ option.missing ? t('indicators.dialog.tooltip.missing', { column: option.name }) : option.name }}</option>
                  }
                </select>
                @if (itemProblems()[i]?.column; as message) {
                  <p class="mt-1 text-sm text-fg-danger-strong" data-testid="tooltip-error">{{ message }}</p>
                }
              </div>
              <div class="min-w-0">
                <input tmzInput [attr.id]="'tooltipLabel' + i" autocomplete="off" [attr.maxlength]="maxTooltipLabel" [attr.aria-label]="t('indicators.dialog.tooltip.label', { n: i + 1 })" [placeholder]="item.column || t('indicators.dialog.tooltip.labelHint')" [value]="item.label ?? ''" (input)="setTooltipLabel(i, $any($event.target).value)" data-testid="tooltip-label" />
                @if (itemProblems()[i]?.label; as message) {
                  <p class="mt-1 text-sm text-fg-danger-strong" data-testid="tooltip-error">{{ message }}</p>
                }
              </div>
              <div class="flex items-start gap-1 sm:justify-end">
                <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary disabled:cursor-not-allowed disabled:opacity-40" [disabled]="first" [attr.aria-label]="t('indicators.dialog.tooltip.up', { n: i + 1 })" (click)="moveTooltip(i, -1)" data-testid="tooltip-up">
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 15 6-6 6 6" /></svg>
                </button>
                <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary disabled:cursor-not-allowed disabled:opacity-40" [disabled]="last" [attr.aria-label]="t('indicators.dialog.tooltip.down', { n: i + 1 })" (click)="moveTooltip(i, 1)" data-testid="tooltip-down">
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6" /></svg>
                </button>
                <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary" [attr.aria-label]="t('indicators.dialog.tooltip.remove', { n: i + 1 })" (click)="removeTooltip(i)" data-testid="tooltip-remove">
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
                </button>
              </div>
              @if (isNumeric(item.column)) {
                <div class="min-w-0 sm:col-span-2">
                  <select tmzInput [attr.id]="'tooltipFormat' + i" [attr.aria-label]="t('indicators.dialog.tooltip.format', { n: i + 1 })" (change)="setTooltipFormat(i, $any($event.target).value)" data-testid="tooltip-format">
                    <option value="" [selected]="!item.format">{{ t('indicators.dialog.tooltip.formatDefault') }}</option>
                    @for (format of tooltipFormats; track format) {
                      <option [value]="format" [selected]="format === item.format">{{ t('scripts.formats.' + format) }}</option>
                    }
                  </select>
                  @if (itemProblems()[i]?.format; as message) {
                    <p class="mt-1 text-sm text-fg-danger-strong" data-testid="tooltip-error">{{ message }}</p>
                  }
                </div>
              }
            </div>
          }
          @if (shown().tooltip; as message) {
            <p class="text-sm text-fg-danger-strong" data-testid="tooltip-list-error">{{ message }}</p>
          }
          <div class="flex flex-wrap items-center gap-3">
            <button tmzButton variant="secondary" type="button" [disabled]="tooltip().length >= maxTooltipItems" (click)="addTooltip()" data-testid="tooltip-add">{{ t('indicators.dialog.tooltip.add') }}</button>
            @if (tooltip().length >= maxTooltipItems) {
              <span class="text-sm text-body" data-testid="tooltip-full">{{ t('indicators.dialog.tooltip.full', { max: maxTooltipItems }) }}</span>
            }
          </div>
        </fieldset>
      }

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
  protected readonly ref = inject<DialogRef<WidgetDialogResult>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly dialog = inject(Dialog);
  private readonly transloco = inject(TranslocoService);
  private readonly basemaps = inject(Basemaps);

  protected readonly chartTypes = chartTypes;
  protected readonly maxBuckets = maxBuckets;
  protected readonly maxFilters = maxFilters;
  protected readonly maxTitleLength = maxTitleLength;
  protected readonly maxTooltipItems = maxTooltipItems;
  protected readonly maxTooltipLabel = maxTooltipLabel;
  protected readonly tooltipFormats = tooltipFormats;
  protected readonly previewDelayMs = previewDelayMs;

  private readonly start = this.data.widget?.config ?? emptyConfig();
  /** The filters start open only for a widget that already has some; adding the first one never closes or reopens it. */
  protected readonly startsWithFilters = this.start.filters.length > 0;

  /** The dashboard chosen for the widget: its own until another is chosen (design D9). */
  private readonly chosenDashboard = signal(this.data.dashboardId);
  /**
   * The dashboard the select shows and the save sends: the one chosen while the project lists it, the widget's own once a reload of
   * the project dropped it (another member removed it).
   */
  protected readonly destination = computed(() => {
    const chosen = this.chosenDashboard();
    return this.data.dashboards().some((d) => d.id === chosen) ? chosen : this.data.dashboardId;
  });
  /**
   * A control other than the Dashboard select changed the widget. Until one does, saving a moved widget sends the move alone, never
   * the draft: the draft is recomputed from the saved query and can differ from it where a script changed the table, which would
   * refuse a widget that only moves (design D9).
   */
  private edited = false;

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
  /** The map's tooltip columns, as the form holds them: a blank column is an item not yet filled in. */
  protected readonly tooltip = signal<WidgetTooltipItem[]>((this.start.tooltip ?? []).map((item) => ({ ...item })));
  /** The saved basemap; a kind the deployment no longer offers stays as it is, unless the member changes it. */
  protected readonly basemap = signal<BasemapKind | null>(this.start.basemap ?? null);

  /**
   * The basemaps to choose from: the kinds the deployment offers and, when the widget was saved with one it no longer offers, that
   * kind too, marked, so saving the widget without changing it keeps it. Empty (no select at all) when the deployment offers none
   * and the widget has none.
   */
  protected readonly basemapOptions = computed<{ kind: BasemapKind; offered: boolean }[]>(() => {
    const offered = new Set((this.basemaps.list()?.basemaps ?? []).map((entry) => entry.kind));
    const saved = this.basemap();
    return basemapKinds.filter((kind) => offered.has(kind) || kind === saved).map((kind) => ({ kind, offered: offered.has(kind) }));
  });

  /** The chosen indicator's published table; null while it loads, when none is chosen or when it cannot be read. */
  protected readonly catalog = signal<IndicatorColumns | null>(null);
  /** The next table to arrive fills in the defaults: a new widget, or another indicator just chosen. */
  private fresh = !this.data.widget;

  protected readonly submitted = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly serverFieldErrors = signal<Problems>({});
  /** What the server refused in each tooltip item, by position. */
  protected readonly itemProblems = signal<Record<number, ItemProblems>>({});

  /**
   * The Dashboard select: when editing, in a project with another dashboard to move to. It stays while it shows the server's refusal,
   * even when the reload that followed left the project one dashboard.
   */
  protected readonly offersDashboards = computed(() => !!this.data.widget && (this.data.dashboards().length > 1 || !!this.serverFieldErrors().dashboardId));

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

  /** What the form holds, whole: the tooltip items with their labels and formats, and the basemap. */
  private readonly formConfig = computed<WidgetConfig>(() => {
    const buckets = this.buckets().trim();
    return {
      indicator: this.indicator(),
      measure: this.mode() === 'measure' && this.measure() ? this.measure() : null,
      ratio: this.mode() === 'ratio' ? { numerator: this.numerator(), denominator: this.denominator() } : null,
      groupBy: this.groupBy() || null,
      filters: this.filters(),
      geometryColumn: this.geometryColumn() || null,
      buckets: buckets === '' ? null : Number(buckets),
      sort: this.sortConfig(),
      tooltip: this.tooltipItems(),
      basemap: this.basemap(),
    };
  });

  /**
   * What is saved and what the preview is given: the query, and for a map the whole tooltip items and the basemap. The preview's
   * own re-query key leaves labels, formats and the basemap out, so editing them never queries again.
   */
  private readonly draft = computed<WidgetConfig>(() => savedConfig(this.chartType(), this.formConfig()));

  /**
   * The items to save. A format on a column the table now knows as text, a date or true/false is dropped (as `compareAs` is), so a
   * format left from before a script changed the column never blocks saving; a column the table does not know keeps its own.
   */
  private tooltipItems(): WidgetTooltipItem[] {
    return this.tooltip().map((item) => ({
      column: item.column,
      label: item.label?.trim() ? item.label : null,
      format: this.typeOf(item.column) === null || this.isNumeric(item.column) ? item.format : null,
    }));
  }

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
    if (config.tooltip.some((item) => !item.column)) {
      problems.tooltip = t('indicators.dialog.errors.tooltip');
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
    this.basemaps.load();
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

  /** Choosing another dashboard moves the widget; it is not an edit of the widget. */
  protected setDashboard(id: string): void {
    this.chosenDashboard.set(id);
    this.serverFieldErrors.update(({ dashboardId: _dashboardId, ...others }) => others);
  }

  /**
   * Marks the form edited when one of its controls changes, except the Dashboard select (a move) and the column picked before a
   * filter is added (nothing saved changes until the filter is). The buttons that change the widget mark it themselves.
   */
  protected touched(event: Event): void {
    const id = (event.target as HTMLElement | null)?.id;
    if (id !== 'widgetDashboard' && id !== 'widgetFilterColumn') {
      this.edited = true;
    }
  }

  protected setType(value: string): void {
    this.chartType.set(value as ChartType);
    this.serverFieldErrors.set({});
    this.itemProblems.set({});
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

  protected setBasemap(value: string): void {
    this.basemap.set(basemapKinds.includes(value as BasemapKind) ? (value as BasemapKind) : null);
    this.serverFieldErrors.update(({ basemap: _basemap, ...others }) => others);
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
    this.tooltip.set([]);
    this.fresh = true;
    this.serverFieldErrors.set({});
    this.itemProblems.set({});
  }

  /** The type the table gives a column; null while the table loads, when it cannot be read and when the table has no such column. */
  private typeOf(column: string): string | null {
    return this.catalog()?.columns.find((c) => c.name === column)?.type ?? null;
  }

  /** Only a number takes a format. */
  protected isNumeric(column: string): boolean {
    const type = this.typeOf(column);
    return type === 'integer' || type === 'decimal';
  }

  /**
   * The columns item `index` can be: every column of the table except the geometry column and those the other items took, with the
   * item's own column kept when the table no longer has it, marked so the form shows what is saved.
   */
  protected tooltipOptions(index: number): { name: string; missing: boolean }[] {
    const items = this.tooltip();
    const own = items[index]?.column ?? '';
    const taken = new Set(items.filter((_, i) => i !== index).map((item) => item.column));
    const names = (this.catalog()?.columns.map((c) => c.name) ?? []).filter((name) => name !== this.geometryColumn() && !taken.has(name));
    const missing = !!own && !!this.catalog()?.published && !(this.catalog()?.columns ?? []).some((c) => c.name === own);
    return [...(missing ? [{ name: own, missing: true }] : []), ...this.columnsWith(names, own && !missing ? [own] : []).map((name) => ({ name, missing: false }))];
  }

  protected addTooltip(): void {
    if (this.tooltip().length < maxTooltipItems) {
      this.edited = true;
      this.tooltip.update((items) => [...items, { column: '', label: null, format: null }]);
    }
  }

  protected removeTooltip(index: number): void {
    this.edited = true;
    this.tooltip.update((items) => items.filter((_, i) => i !== index));
    this.itemProblems.set({});
    this.serverFieldErrors.update(({ tooltip: _tooltip, ...others }) => others);
  }

  protected moveTooltip(index: number, by: -1 | 1): void {
    this.edited = true;
    this.tooltip.update((items) => {
      const to = index + by;
      if (to < 0 || to >= items.length) {
        return items;
      }
      const moved = [...items];
      [moved[index], moved[to]] = [moved[to], moved[index]];
      return moved;
    });
    this.itemProblems.set({});
  }

  /** Another column is another type, so a format from the old one goes. */
  protected setTooltipColumn(index: number, column: string): void {
    this.updateTooltip(index, { column, format: null });
  }

  protected setTooltipLabel(index: number, label: string): void {
    this.updateTooltip(index, { label: label === '' ? null : label });
  }

  protected setTooltipFormat(index: number, format: string): void {
    this.updateTooltip(index, { format: tooltipFormats.includes(format as TooltipFormat) ? (format as TooltipFormat) : null });
  }

  private updateTooltip(index: number, change: Partial<WidgetTooltipItem>): void {
    this.tooltip.update((items) => items.map((item, i) => (i === index ? { ...item, ...change } : item)));
    this.itemProblems.update(({ [index]: _own, ...others }) => others);
  }

  protected removeFilter(column: string): void {
    this.edited = true;
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
        this.edited = true;
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

  /**
   * Adds the widget, or saves the edited one: the whole form, with the dashboard chosen when it moves. A move with nothing else
   * changed sends the dashboard alone, so neither the form's checks nor the server's can refuse a stale query (design D9).
   */
  protected save(): void {
    this.serverFieldErrors.set({});
    this.itemProblems.set({});
    this.error.set(null);
    const { projectId, dashboardId, widget } = this.data;
    const target = widget && this.destination() !== dashboardId ? this.destination() : null;
    const moveOnly = target !== null && !this.edited;
    if (!moveOnly) {
      this.submitted.set(true);
      if (Object.keys(this.problems()).length) {
        return;
      }
    }
    const body = { title: this.title().trim(), chartType: this.chartType(), config: this.draft() };
    const patch: WidgetPatch = target === null ? body : moveOnly ? { dashboardId: target } : { ...body, dashboardId: target };
    this.busy.set(true);
    const request = widget ? this.service.update(projectId, dashboardId, widget.id, patch) : this.service.create(projectId, dashboardId, body);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          // Another member removed the dashboard: nothing was saved, and the page takes the member to the first one.
          this.ref.close({ gone: true });
          return;
        }
        const fields: Problems = {};
        const items: Record<number, ItemProblems> = {};
        for (const [path, message] of Object.entries(parsed.fieldErrors)) {
          const item = this.itemFor(path);
          if (item) {
            items[item.index] = { ...items[item.index], [item.part]: items[item.index]?.[item.part] ?? message };
            continue;
          }
          const key = this.fieldFor(path);
          if (key && !fields[key]) {
            fields[key] = message;
          }
        }
        this.serverFieldErrors.set(fields);
        this.itemProblems.set(items);
        const others = Object.entries(parsed.fieldErrors).filter(([path]) => !this.itemFor(path) && !this.fieldFor(path));
        this.error.set(Object.keys(fields).length && !others.length ? null : others.length ? others.map(([, message]) => message).join(' ') : parsed.message);
        if (fields.dashboardId) {
          // The dashboard chosen was removed meanwhile, or is full: the project is read again, so a removed one leaves the select
          // and the tab row (design D9).
          this.data.refused?.();
        }
      },
    });
  }

  /** `config.tooltip[2].format`: the item and the part of it the server refused. An item with no part is refused as a whole (a null one). */
  private itemFor(path: string): { index: number; part: keyof ItemProblems } | null {
    const match = /tooltip\[(\d+)\](?:\.(\w+))?$/i.exec(path);
    if (!match) {
      return null;
    }
    const part = match[2]?.toLowerCase();
    return { index: Number(match[1]), part: part === 'label' || part === 'format' ? part : 'column' };
  }

  private fieldFor(path: string): keyof Problems | null {
    const name = path.toLowerCase();
    if (name === 'dashboardid') return 'dashboardId';
    if (name.includes('tooltip')) return 'tooltip';
    if (name.includes('basemap')) return 'basemap';
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
