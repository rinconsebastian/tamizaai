import { DIALOG_DATA, Dialog, DialogRef } from '@angular/cdk/dialog';
import { Component, Directive, ElementRef, Injector, LOCALE_ID, Signal, afterNextRender, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Observable, catchError, map, of, switchMap, tap, timer } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import { Basemaps } from '../../../core/basemaps.service';
import { DataFilter } from '../../../core/projects/data.service';
import {
  BasemapKind,
  BoxOrientation,
  ChartType,
  DashboardBases,
  DashboardColors,
  IndicatorColumns,
  IndicatorEntry,
  DashboardFilter,
  IndicatorsService,
  LikertSide,
  LikertStep,
  LineInterpolation,
  ReferenceLineKind,
  ScaleId,
  SeriesLayout,
  StatSecondaryLine,
  StatStatistic,
  Widget,
  WidgetConfig,
  WidgetCaption,
  WidgetDisplay,
  WidgetReferenceLine,
  boxOrientations,
  WidgetPatch,
  chartTypes,
  emptyConfig,
  basesOf,
  hasSeries,
  maxBuckets,
  defaultBuckets,
  maxCaptionLength,
  maxColumnLabel,
  maxDashboardFilters,
  maxFilters,
  maxFootnoteLength,
  maxMinN,
  maxReferenceLineLabel,
  maxReferenceLines,
  maxSubtitleLength,
  maxWidgetUnitLength,
  maxWidgetLabels,
  maxTitleLength,
  maxLikertSide,
  maxLikertValues,
  maxTooltipItems,
  maxTooltipLabel,
  minLikertValues,
  needsGroup,
  needsSeries,
  lineInterpolations,
  paletteSize,
  scaleIds,
  seriesLayouts,
  statSecondaryLines,
  statStatistics,
  takesMissingFooter,
  takesSeries,
  TooltipFormat,
  WidgetSort,
  WidgetTooltipItem,
  basemapKinds,
} from '../../../core/projects/indicators.service';
import { DashboardEntry } from '../../../core/projects/projects.service';
import { Alert, ApiLang, Button, Disclosure, FormField, Input, markApiText, numberText } from '../../../shared/ui';
import { DataFilterDialog, DataFilterDialogData, DataFilterDialogResult } from '../data-filter-dialog';
import { describeFilter, normalizeFilter } from '../data-view';
import { FilterValues, activeFilters } from './dashboard-filters';
import { columnLabels, textsCrowd } from './format';
import { defaultSize } from './grid-layout';
import { ValueOrderEditor } from './value-order-editor';
import { answerColumnsOf, baseColumnOf, noDataValuesOf, settingsInForce } from './base-figures';
import { ShownValues, WidgetView, offersDataLabels, queryFor, referenceKindsFor, savedConfig } from './widget-view';
import { naturalCompare } from './chart-scales';
import { colorsOf, defaultScale, paletteColor, rampGradient } from './chart-colors';

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
  /** The open dashboard's filters: the widget can ignore any of them (dashboard filters, design D6). */
  filters?: DashboardFilter[];
  /** Their values in force on the member's screen when the form opened, which the preview applies (design D9). */
  values?: FilterValues;
  /** The dashboard's palette and colors kept by value: the preview draws the widget in them (add-dashboard-palettes, design D7, D9). */
  colors: DashboardColors;
  /**
   * The dashboard's base settings, or the shown version's (add-small-sample-warnings, design D12): the preview applies them under the widget's
   * own, and the form says what "as the dashboard says" means. None while the dashboard has none.
   */
  bases?: DashboardBases;
}

/** The widget saved, or `gone` when its dashboard no longer exists (`dashboards.notFound`), for the page to catch up. */
export type WidgetDialogResult = Widget | { gone: true };

/**
 * A widget's text as it is saved (add-widget-context, design D11): control characters, line breaks included, become a space and the
 * ends are trimmed, so a stored text is saved as it is and a pasted line break never reaches the server.
 */
function singleLine(text: string): string {
  return text.replace(/[\u0000-\u001f\u007f-\u009f]+/g, ' ').trim();
}

/**
 * Moves the decimal point of a plain decimal text `places` digits to the right (a negative number moves it left) by working on the
 * digits, never by multiplying or dividing: 33.3 typed as a percent is 0.333, not 0.33299999999999996 (add-chart-annotations, design D8).
 * A comma is read as a point. Null when the text is not a number.
 */
function shiftDecimal(text: string, places: number): string | null {
  const match = /^([+-]?)(\d*)(?:[.,](\d*))?$/.exec(text.trim());
  if (!match || (match[2] === '' && !match[3])) {
    return null;
  }
  const [, sign, whole, fraction = ''] = match;
  const digits = whole + fraction;
  const point = whole.length + places;
  const padded = point < 0 ? '0'.repeat(-point) + digits : point > digits.length ? digits + '0'.repeat(point - digits.length) : digits;
  const at = Math.max(0, point);
  const left = padded.slice(0, at).replace(/^0+(?=\d)/, '') || '0';
  const right = padded.slice(at).replace(/0+$/, '');
  return `${sign === '-' ? '-' : ''}${left}${right ? '.' + right : ''}`;
}

/** The text of a saved value as the input shows it: a percent is typed as a percent (0.5 shows as 50), any other value as it is. */
function valueText(value: number, percent: boolean): string {
  let text = String(value);
  const exponent = /^([+-]?\d+(?:\.\d+)?)e([+-]\d+)$/i.exec(text);
  if (exponent) {
    text = shiftDecimal(exponent[1], Number(exponent[2])) ?? text;
  }
  return percent ? (shiftDecimal(text, 2) ?? text) : text;
}

/**
 * A text such as 1.500 or 12,345 in a value that is not a percent: a single separator followed by exactly three digits, after a whole
 * part that does not start with a zero. Some regions write a thousands separator there (1.500 is 1500 in es-CO) and others a decimal
 * point (1.5), and a wrong guess would save a line a thousand times off, so the form asks for the number without a separator instead.
 * A zero before the point (0.125), another number of digits (33.3, 1.5000) or a percent (33.333 is a share) is not ambiguous.
 */
function isAmbiguousSeparator(typed: string, percent: boolean): boolean {
  return !percent && /^[+-]?[1-9]\d{0,2}[.,]\d{3}$/.test(typed.trim());
}

/** The number a typed text means, or null when it is empty, not a number or ambiguous. A percent (50, or 50%) is stored as the fraction (0.5). */
function textValue(typed: string, percent: boolean): number | null {
  const text = typed.trim().replace(/%$/, '');
  if (isAmbiguousSeparator(text, percent)) {
    return null;
  }
  const plain = percent ? shiftDecimal(text, -2) : shiftDecimal(text, 0);
  const value = plain === null ? NaN : Number(plain);
  return Number.isFinite(value) ? value : null;
}

/**
 * A reference line as the form holds it (add-chart-annotations, design D8). `value` is the number the line saves; `typed` the text in
 * its input once the member has typed there, so a saved number is shown, and kept as it is, until the input is edited.
 */
interface LineRow {
  kind: ReferenceLineKind;
  value: number | null;
  typed: string | null;
  /** Whether the input was in percent when `typed` was typed: after a change of units the text means something else, so the number is shown. */
  typedPercent?: boolean;
  label: string;
}
/** What the server refused in one reference line. */
type LineProblems = Partial<Record<'kind' | 'value' | 'label', string>>;

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
/** How a widget's caption reads: the line it has (default), an own text, or no line (add-widget-context, design D3). */
type CaptionMode = 'default' | 'text' | 'hidden';
const captionModes: readonly CaptionMode[] = ['default', 'text', 'hidden'];
/** What the form says of the base n line, as the select holds it: as the dashboard says, or the widget's own choice (add-small-sample-warnings, design D12). */
type ShowNChoice = 'dashboard' | 'show' | 'hide';
/** What the form says of the small-base threshold: as the dashboard says, off for the widget (a 0) or its own number. */
type MinNChoice = 'dashboard' | 'off' | 'own';
/** Why a widget has no base n, which the form says when the n line is on (design D4, D12). */
type NoNReason = 'kind' | 'undeclared' | 'measure';
type Problems = Partial<
  Record<
    | 'title'
    | 'dashboardId'
    | 'indicator'
    | 'measure'
    | 'ratio'
    | 'groupBy'
    | 'geometryColumn'
    | 'buckets'
    | 'sort'
    | 'tooltip'
    | 'basemap'
    | 'dashboardFilters'
    | 'seriesBy'
    | 'layout'
    | 'pyramidLeft'
    | 'pyramidRight'
    | 'scale'
    | 'color'
    | 'ramp'
    | 'orientation'
    | 'labels'
    | 'subtitle'
    | 'footnote'
    | 'caption'
    | 'captionText'
    | 'unit'
    | 'referenceLines'
    | 'interpolation'
    | 'dataLabels'
    | 'groupOrder'
    | 'seriesOrder'
    | 'minN'
    | 'showN'
    | 'missingFooter'
    | 'statistic'
    | 'secondary',
    string
  >
>;

/** A value of a Likert scale as the editor holds it: a side, or `out` for a value of the data that is not part of the scale. */
interface ScaleItem {
  value: string;
  side: LikertSide | 'out';
}
const scaleSides: readonly (LikertSide | 'out')[] = ['negative', 'neutral', 'positive', 'out'];
const sideRank: Record<LikertSide, number> = { negative: 0, neutral: 1, positive: 2 };
/** What the server refused in one tooltip item. */
type ItemProblems = Partial<Record<'column' | 'label' | 'format', string>>;

const tooltipFormats: readonly TooltipFormat[] = ['count', 'decimal', 'percent'];

/** The places in a palette, in order: the choices of a widget's color. */
const places = Array.from({ length: paletteSize }, (_, i) => i + 1);
/** The choices of a map's or heat map's scale: the palette's own (null), then each scale. */
const rampChoices: readonly (ScaleId | null)[] = [null, ...scaleIds];

/**
 * Adds or edits a widget (design D6, D8): its chart type and title, its query (the indicator, a measure or a ratio of two numeric
 * columns, the group-by column, filters, the geometry column of a map, the buckets of a histogram) and a live preview from the
 * same query endpoint that renders a saved widget. The columns offered are those of the chosen indicator's published table. A draft
 * the server refuses shows that refusal where the preview is, never the previous chart. An edited widget can move to another of the
 * project's dashboards (design D9 of add-multiple-dashboards).
 */
/**
 * The error state of a control of a reference line's row, which has no `tmz-form-field` around it: marks the control invalid and points
 * it at the texts that describe it, as the field does (`aria-invalid`, `aria-describedby`).
 */
@Directive({ selector: '[tmzLineField]' })
export class LineField {
  readonly failed = input(false, { alias: 'tmzLineField' });
  readonly describedBy = input<string | null>(null, { alias: 'tmzDescribedBy' });
  private readonly control = inject(Input, { self: true });

  constructor() {
    effect(() => {
      this.control.invalid.set(this.failed());
      this.control.describedBy.set(this.describedBy());
    });
  }
}

@Component({
  selector: 'app-widget-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, Disclosure, FormField, Input, LineField, ValueOrderEditor, WidgetView],
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

      <tmz-form-field [label]="t('indicators.dialog.subtitle.label')" fieldId="widgetSubtitle" [error]="shown().subtitle" [hint]="t('indicators.dialog.subtitle.hint')">
        <input tmzInput id="widgetSubtitle" autocomplete="off" [attr.maxlength]="maxSubtitleLength" [value]="subtitle()" (input)="setText('subtitle', $any($event.target).value)" data-testid="dialog-subtitle" />
      </tmz-form-field>

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
          <tmz-form-field [label]="t('indicators.dialog.denominator')" fieldId="widgetDenominator" [hint]="t(ratioHelpKey())">
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
        @if (chartType() === 'stat') {
          <!-- The statistic of a stat tile (improve-stat-cards, design D6): the sum or ratio of sums by default, or the median, a quartile or the mean of the rows' values. -->
          <tmz-form-field [label]="t('indicators.dialog.statistic.label')" fieldId="widgetStatistic" [error]="shown().statistic" [hint]="t('indicators.dialog.statistic.hint')">
            <select tmzInput id="widgetStatistic" (change)="setStatistic($any($event.target).value)" data-testid="dialog-statistic">
              <option value="" [selected]="!statistic()">{{ t(mode() === 'ratio' ? 'indicators.dialog.statistic.defaultRatio' : 'indicators.dialog.statistic.defaultMeasure') }}</option>
              @for (option of statistics; track option) {
                <option [value]="option" [selected]="option === statistic()">{{ t('indicators.dialog.statistic.' + option) }}</option>
              }
            </select>
          </tmz-form-field>
        }
        @if (chartType() !== 'stat') {
          <tmz-form-field [label]="t('indicators.dialog.groupBy')" fieldId="widgetGroupBy" [error]="shown().groupBy" [hint]="groups() ? null : t(chartType() === 'boxplot' ? 'indicators.dialog.groupByOptionalBoxplot' : 'indicators.dialog.groupByOptional')">
            <select tmzInput id="widgetGroupBy" (change)="groupBy.set($any($event.target).value)" data-testid="dialog-group-by">
              <option value="" [selected]="!groupBy()">{{ groups() ? t('indicators.dialog.columnChoose') : t('indicators.dialog.groupByNone') }}</option>
              @for (column of allColumns(); track column) {
                <option [value]="column" [selected]="column === groupBy()">{{ column }}</option>
              }
            </select>
          </tmz-form-field>
        }

        @if (offersSeries()) {
          <tmz-form-field [label]="t('indicators.dialog.seriesBy')" fieldId="widgetSeriesBy" [error]="shown().seriesBy" [hint]="requiresSeries() ? t('indicators.dialog.seriesHelpRequired') : t('indicators.dialog.seriesHelp')">
            <select tmzInput id="widgetSeriesBy" (change)="setSeriesBy($any($event.target).value)" data-testid="dialog-series-by">
              <option value="" [selected]="!seriesBy()">{{ requiresSeries() ? t('indicators.dialog.columnChoose') : t('indicators.dialog.seriesByNone') }}</option>
              @for (column of seriesColumns(); track column) {
                <option [value]="column" [selected]="column === seriesBy()">{{ column }}</option>
              }
            </select>
          </tmz-form-field>
        }

        @if (sortable()) {
          <!-- A box plot is ordered by its median only, and only with a group column; a bar or column by the value or a column. -->
          <tmz-form-field
            [label]="t(chartType() === 'boxplot' ? 'indicators.dialog.sortByBoxes' : 'indicators.dialog.sortBy')"
            fieldId="widgetSortBy"
            [error]="shown().sort"
            [hint]="orderBlocked() ? t('indicators.dialog.sortNeedsGroup') : null"
          >
            <select tmzInput id="widgetSortBy" [disabled]="orderBlocked()" (change)="setSortKey($any($event.target).value)" data-testid="dialog-sort-by">
              <option value="" [selected]="effectiveSortKey() === ''">{{ t('indicators.dialog.sortDefault') }}</option>
              <option value="value" [selected]="effectiveSortKey() === 'value'">{{ t(chartType() === 'boxplot' ? 'indicators.dialog.sortMedian' : 'indicators.dialog.sortValue') }}</option>
              @if (chartType() !== 'boxplot') {
                @for (column of sortColumns(); track column) {
                  <option [value]="'column:' + column" [selected]="sortKey() === 'column:' + column">{{ column }}</option>
                }
              }
            </select>
          </tmz-form-field>

          @if (effectiveSortKey() !== '') {
            <tmz-form-field [label]="t('indicators.dialog.sortDirection')" fieldId="widgetSortDirection">
              <select tmzInput id="widgetSortDirection" [disabled]="orderBlocked()" (change)="sortDirection.set($any($event.target).value)" data-testid="dialog-sort-direction">
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
                  <p class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="tooltip-error">{{ message }}</p>
                }
              </div>
              <div class="min-w-0">
                <input tmzInput [attr.id]="'tooltipLabel' + i" autocomplete="off" [attr.maxlength]="maxTooltipLabel" [attr.aria-label]="t('indicators.dialog.tooltip.label', { n: i + 1 })" [placeholder]="item.column ? labelPlaceholder(item.column) : t('indicators.dialog.tooltip.labelHint')" [value]="item.label ?? ''" (input)="setTooltipLabel(i, $any($event.target).value)" data-testid="tooltip-label" />
                @if (itemProblems()[i]?.label; as message) {
                  <p class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="tooltip-error">{{ message }}</p>
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
                    <p class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="tooltip-error">{{ message }}</p>
                  }
                </div>
              }
            </div>
          }
          @if (shown().tooltip; as message) {
            <p class="text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="tooltip-list-error">{{ message }}</p>
          }
          <div class="flex flex-wrap items-center gap-3">
            <button tmzButton variant="secondary" type="button" [disabled]="tooltip().length >= maxTooltipItems" (click)="addTooltip()" data-testid="tooltip-add">{{ t('indicators.dialog.tooltip.add') }}</button>
            @if (tooltip().length >= maxTooltipItems) {
              <span class="text-sm text-body" data-testid="tooltip-full">{{ t('indicators.dialog.tooltip.full', { max: maxTooltipItems }) }}</span>
            }
          </div>
        </fieldset>
      }

      @if (offersLayout()) {
        <fieldset class="min-w-0" data-testid="dialog-layout">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.layout.legend') }}</legend>
          <div class="flex flex-wrap gap-x-6 gap-y-1">
            @for (option of layouts; track option) {
              <label class="flex min-h-10 items-center gap-2 text-sm text-heading" [class.cursor-pointer]="!layoutDisabled(option)" [class.opacity-60]="layoutDisabled(option)">
                <input
                  type="radio"
                  name="widgetLayout"
                  class="size-4 shrink-0"
                  [value]="option"
                  [checked]="chosenLayout() === option"
                  [disabled]="layoutDisabled(option)"
                  (change)="setLayout(option)"
                  [attr.data-testid]="'dialog-layout-' + option"
                />
                {{ t('indicators.dialog.layout.' + option) }}
              </label>
            }
          </div>
          @if (mode() === 'ratio') {
            <p class="mt-1 text-sm text-body" data-testid="dialog-layout-reason">{{ t('indicators.dialog.layout.ratioReason') }}</p>
          }
          @if (shown().layout; as message) {
            <p class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
          }
        </fieldset>
      }

      @if (chartType() === 'boxplot') {
        <fieldset class="min-w-0" [attr.aria-describedby]="shown().orientation ? 'widgetOrientation-error' : null" data-testid="dialog-orientation">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.orientation.legend') }}</legend>
          <div class="flex flex-wrap gap-x-6 gap-y-1">
            @for (option of orientations; track option) {
              <label class="flex min-h-10 cursor-pointer items-center gap-2 text-sm text-heading">
                <input
                  type="radio"
                  name="widgetOrientation"
                  class="size-4 shrink-0"
                  [value]="option"
                  [checked]="chosenOrientation() === option"
                  (change)="setOrientation(option)"
                  [attr.data-testid]="'dialog-orientation-' + option"
                />
                {{ t('indicators.dialog.orientation.' + option) }}
              </label>
            }
          </div>
          <p class="mt-1 text-sm text-body">{{ t('indicators.dialog.orientation.help') }}</p>
          @if (shown().orientation; as message) {
            <p id="widgetOrientation-error" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
          }
        </fieldset>
      }

      @if (offersColor()) {
        <!-- The dashboard's eight colors by their numbers. Native radios, so the arrow keys move through them with no code of ours. -->
        <fieldset class="min-w-0" [attr.aria-describedby]="shown().color ? 'widgetColor-error' : null" data-testid="dialog-color">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.color.legend') }}</legend>
          <p class="mb-2 text-sm text-body">{{ t('indicators.dialog.color.help') }}</p>
          <div class="flex flex-wrap gap-1.5">
            @for (n of places; track n) {
              <label class="relative">
                <input
                  type="radio"
                  name="widgetColor"
                  class="peer sr-only"
                  [value]="n"
                  [checked]="chosenColor() === n"
                  [attr.aria-label]="t('dashboardColors.kept.colorOption', { number: n, hue: t('colors.hues.' + palette + '.' + n) })"
                  (change)="setColor(n)"
                  [attr.data-color]="n"
                  data-testid="dialog-color-option"
                />
                <span
                  class="flex min-h-10 min-w-12 cursor-pointer items-center justify-center gap-1.5 rounded-base border border-default-medium bg-neutral-secondary-medium px-2 text-sm text-heading peer-checked:border-brand peer-checked:bg-brand-softer peer-checked:ring-2 peer-checked:ring-brand peer-focus-visible:ring-4 peer-focus-visible:ring-brand-medium"
                  [title]="t('colors.hues.' + palette + '.' + n)"
                >
                  <span class="size-4 shrink-0 rounded-full border border-default" aria-hidden="true" [style.background-color]="swatch(n)" data-testid="dialog-color-swatch"></span>
                  <span class="tabular-nums" aria-hidden="true">{{ n }}</span>
                </span>
              </label>
            }
          </div>
          <!-- The name of the color chosen, in text for those who do not see the swatches' names; the checked radio already says it. -->
          @if (places.includes(chosenColor())) {
            <p class="mt-2 text-sm text-body" aria-hidden="true" data-testid="dialog-color-chosen">{{ t('dashboardColors.kept.colorOption', { number: chosenColor(), hue: t('colors.hues.' + palette + '.' + chosenColor()) }) }}</p>
          }
          @if (shown().color; as message) {
            <p id="widgetColor-error" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
          }
        </fieldset>
      }

      @if (offersRamp()) {
        <!-- The palette's own scale first, then each scale as its steps. A scale is named in text, never by its gradient alone. -->
        <fieldset class="min-w-0" [attr.aria-describedby]="shown().ramp ? 'widgetRamp-error' : null" data-testid="dialog-ramp">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.ramp.legend') }}</legend>
          <p class="mb-2 text-sm text-body">{{ t('indicators.dialog.ramp.help') }}</p>
          <div class="grid grid-cols-2 gap-2 sm:grid-cols-4">
            @for (choice of rampChoices; track choice) {
              <label class="relative block">
                <input
                  type="radio"
                  name="widgetRamp"
                  class="peer sr-only"
                  [value]="choice ?? ''"
                  [checked]="ramp() === choice"
                  (change)="setRamp(choice)"
                  [attr.data-ramp]="choice ?? 'palette'"
                  data-testid="dialog-ramp-option"
                />
                <!-- The rows start at the card's top: the cards of a row are as tall as the tallest (the palette's scale has a caption more), and centered rows would put its gradient higher than the others'. -->
                <span
                  class="grid h-full min-h-10 cursor-pointer content-start gap-1 rounded-base border border-default-medium bg-neutral-secondary-medium p-2 text-sm text-heading peer-checked:border-brand peer-checked:bg-brand-softer peer-checked:ring-2 peer-checked:ring-brand peer-focus-visible:ring-4 peer-focus-visible:ring-brand-medium"
                >
                  <span class="h-3 rounded-sm border border-default" aria-hidden="true" [style.background-image]="gradient(choice)" data-testid="dialog-ramp-gradient"></span>
                  <span>{{ choice === null ? t('indicators.dialog.ramp.palette') : t('colors.scales.' + choice) }}</span>
                  @if (choice === null) {
                    <!-- The space keeps the name a screen reader builds from the label ("Palette's scale Orange") in two words. -->
                    &ngsp;<span class="text-xs text-body" data-testid="dialog-ramp-palette-scale">{{ t('colors.scales.' + paletteScale) }}</span>
                  }
                </span>
              </label>
            }
          </div>
          @if (shown().ramp; as message) {
            <p id="widgetRamp-error" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
          }
        </fieldset>
      }

      @if (offersLines()) {
        <!-- Reference lines (add-chart-annotations, design D8): dashed lines across the chart to compare its marks with. The row's kinds are those the widget takes. -->
        <fieldset class="flex min-w-0 flex-col gap-3" [attr.aria-describedby]="shown().referenceLines ? 'widgetLines-error' : null" data-testid="dialog-reference-lines">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.referenceLines.legend') }}</legend>
          <p class="-mt-2 text-sm text-body">{{ t('indicators.dialog.referenceLines.help', { max: maxReferenceLines }) }}</p>
          @for (row of referenceLines(); track $index; let i = $index) {
            <div class="grid gap-2 rounded-base border border-default p-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto]" data-testid="reference-line">
              <div class="min-w-0">
                <select tmzInput [tmzLineField]="!!lineProblems()[i]?.kind" [tmzDescribedBy]="kindDescribedBy(i)" [attr.id]="'referenceKind' + i" [attr.aria-label]="t('indicators.dialog.referenceLines.kind', { n: i + 1 })" (change)="setLineKind(i, $any($event.target).value)" data-testid="reference-kind">
                  @for (kind of kindOptions(i); track kind) {
                    <option [value]="kind" [selected]="kind === row.kind">{{ t('indicators.dialog.referenceLines.kinds.' + kind) }}</option>
                  }
                </select>
                @if (lineProblems()[i]?.kind; as message) {
                  <p [attr.id]="'referenceKind' + i + '-error'" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="reference-error">{{ message }}</p>
                }
              </div>
              <div class="min-w-0">
                @if (row.kind === 'value') {
                  <div class="flex items-center gap-2">
                    <input
                      tmzInput
                      [tmzLineField]="valueFailed(i)"
                      [tmzDescribedBy]="valueDescribedBy(i)"
                      class="min-w-0 flex-1"
                      [attr.id]="'referenceValue' + i"
                      inputmode="decimal"
                      autocomplete="off"
                      [attr.aria-label]="t('indicators.dialog.referenceLines.value', { n: i + 1 })"
                      [value]="valueOf(row)"
                      (input)="setLineValue(i, $any($event.target).value)"
                      data-testid="reference-value"
                    />
                    @if (valueUnit()) {
                      <span [attr.id]="'referenceValue' + i + '-unit'" class="shrink-0 text-sm text-body" data-testid="reference-unit">{{ valueUnit() }}</span>
                    }
                  </div>
                  @if (lineProblems()[i]?.value; as message) {
                    <p [attr.id]="'referenceValue' + i + '-error'" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="reference-error">{{ message }}</p>
                  }
                }
              </div>
              <div class="flex items-start sm:justify-end">
                <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary" [attr.aria-label]="t('indicators.dialog.referenceLines.remove', { n: i + 1 })" (click)="removeLine(i)" data-testid="reference-remove">
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
                </button>
              </div>
              <div class="min-w-0 sm:col-span-3">
                <input
                  tmzInput
                  [tmzLineField]="!!lineProblems()[i]?.label"
                  [tmzDescribedBy]="lineProblems()[i]?.label ? 'referenceLabel' + i + '-error' : null"
                  [attr.id]="'referenceLabel' + i"
                  autocomplete="off"
                  [attr.maxlength]="maxReferenceLineLabel"
                  [attr.aria-label]="t('indicators.dialog.referenceLines.label', { n: i + 1 })"
                  [placeholder]="t('indicators.referenceLines.kinds.' + row.kind)"
                  [value]="row.label"
                  (input)="setLineLabel(i, $any($event.target).value)"
                  data-testid="reference-label"
                />
                @if (lineProblems()[i]?.label; as message) {
                  <p [attr.id]="'referenceLabel' + i + '-error'" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="reference-error">{{ message }}</p>
                }
                <p [attr.id]="'referenceHelp' + i" class="mt-1 text-sm text-body" data-testid="reference-help">{{ t('indicators.dialog.referenceLines.kindHelp.' + row.kind, { units: valueIsPercent() ? t('indicators.dialog.referenceLines.unitsPercent') : t('indicators.dialog.referenceLines.unitsPlain') }) }}</p>
                @if (row.kind === 'overall') {
                  <p [attr.id]="'referenceCaution' + i" class="mt-1 text-sm text-body" data-testid="reference-overall-help">{{ t('indicators.dialog.referenceLines.overallCaution') }}</p>
                }
              </div>
            </div>
          }
          @if (shown().referenceLines; as message) {
            <p id="widgetLines-error" class="text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="reference-list-error">{{ message }}</p>
          }
          <div class="flex flex-wrap items-center gap-3">
            <button tmzButton variant="secondary" type="button" [disabled]="referenceLines().length >= maxReferenceLines" (click)="addLine()" data-testid="reference-add">{{ t('indicators.dialog.referenceLines.add') }}</button>
            @if (referenceLines().length >= maxReferenceLines) {
              <span class="text-sm text-body" data-testid="reference-full">{{ t('indicators.dialog.referenceLines.full', { max: maxReferenceLines }) }}</span>
            }
          </div>
        </fieldset>
      }

      @if (offersSteps()) {
        <fieldset class="min-w-0" [attr.aria-describedby]="shown().interpolation ? 'widgetInterpolation-error' : null" data-testid="dialog-interpolation">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.interpolation.legend') }}</legend>
          <div class="flex flex-wrap gap-x-6 gap-y-1">
            @for (option of interpolations; track option) {
              <label class="flex min-h-10 cursor-pointer items-center gap-2 text-sm text-heading">
                <input
                  type="radio"
                  name="widgetInterpolation"
                  class="size-4 shrink-0"
                  [value]="option"
                  [checked]="chosenInterpolation() === option"
                  (change)="setInterpolation(option)"
                  [attr.data-testid]="'dialog-interpolation-' + option"
                />
                {{ t('indicators.dialog.interpolation.' + option) }}
              </label>
            }
          </div>
          <p class="mt-1 text-sm text-body">{{ t('indicators.dialog.interpolation.help') }}</p>
          @if (shown().interpolation; as message) {
            <p id="widgetInterpolation-error" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
          }
        </fieldset>
      }

      @if (offersDataLabels()) {
        <div data-testid="dialog-data-labels-group">
          <label class="flex min-h-10 cursor-pointer items-start gap-3 py-1 text-sm">
            <input
              type="checkbox"
              class="mt-0.5 size-4 shrink-0"
              [checked]="dataLabels() === true"
              (change)="setDataLabels($any($event.target).checked)"
              data-testid="dialog-data-labels"
            />
            <span class="min-w-0 flex-1 break-words text-heading">{{ t('indicators.dialog.dataLabels.label') }}</span>
          </label>
          <p class="text-sm text-body">{{ t('indicators.dialog.dataLabels.help') }}</p>
          @if (shown().dataLabels; as message) {
            <p class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
          }
        </div>
      }

      @if (droppedNote(); as reason) {
        <!-- What the form holds and the widget does not take: kept here, not saved, back when the choice is undone (design D8). -->
        <p class="text-sm text-body" role="status" data-testid="dialog-annotations-note">{{ t('indicators.dialog.referenceLines.dropped.' + reason) }}</p>
      }
      @if (droppedLabels()) {
        <p class="text-sm text-body" role="status" data-testid="dialog-data-labels-note">{{ t('indicators.dialog.dataLabels.dropped') }}</p>
      }

      <!-- The words around the widget (add-widget-context, design D11): its caption, unit and footnote, and the button of the indicator's description. -->
      <fieldset class="flex min-w-0 flex-col gap-4" data-testid="dialog-context">
        <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.context.legend') }}</legend>
        <p class="-mt-2 text-sm text-body">{{ t('indicators.dialog.context.help') }}</p>

        <fieldset class="min-w-0" [attr.aria-describedby]="shown().caption ? 'widgetCaption-error' : null" data-testid="dialog-caption">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.context.caption.legend') }}</legend>
          <p class="mb-1 text-sm text-body">{{ t('indicators.dialog.context.caption.help') }}</p>
          <div class="flex flex-wrap gap-x-6 gap-y-1">
            @for (option of captionModes; track option) {
              <label class="flex min-h-10 cursor-pointer items-center gap-2 text-sm text-heading">
                <input
                  type="radio"
                  name="widgetCaptionMode"
                  class="size-4 shrink-0"
                  [value]="option"
                  [checked]="captionMode() === option"
                  (change)="setCaptionMode(option)"
                  [attr.data-testid]="'dialog-caption-' + option"
                />
                {{ t('indicators.dialog.context.caption.' + option) }}
              </label>
            }
          </div>
          @if (shown().caption; as message) {
            <p id="widgetCaption-error" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
          }
          @if (captionMode() === 'text') {
            <tmz-form-field class="mt-2" [label]="t('indicators.dialog.context.caption.textLabel')" fieldId="widgetCaptionText" [error]="shown().captionText" [hint]="captionHint(t)">
              <input tmzInput id="widgetCaptionText" autocomplete="off" [attr.maxlength]="maxCaptionLength" [value]="captionText()" (input)="setText('captionText', $any($event.target).value)" data-testid="dialog-caption-own" />
            </tmz-form-field>
          }
        </fieldset>

        <tmz-form-field [label]="t('indicators.dialog.context.unit.label')" fieldId="widgetUnit" [error]="shown().unit" [hint]="unitHint(t)">
          <input tmzInput id="widgetUnit" autocomplete="off" [attr.maxlength]="maxUnitLength" [value]="unit()" (input)="setText('unit', $any($event.target).value)" data-testid="dialog-unit" />
        </tmz-form-field>

        <tmz-form-field [label]="t('indicators.dialog.context.footnote.label')" fieldId="widgetFootnote" [error]="shown().footnote" [hint]="t('indicators.dialog.context.footnote.hint')">
          <textarea tmzInput id="widgetFootnote" rows="2" [attr.maxlength]="maxFootnoteLength" [value]="footnote()" (input)="setText('footnote', $any($event.target).value)" data-testid="dialog-footnote"></textarea>
        </tmz-form-field>

        <div>
          <label class="flex min-h-10 items-start gap-3 py-1 text-sm" [class.cursor-pointer]="descriptionAvailable()" [class.opacity-60]="!descriptionAvailable()">
            <input
              type="checkbox"
              class="mt-0.5 size-4 shrink-0"
              [checked]="showDescription()"
              [disabled]="!descriptionAvailable()"
              (change)="showDescription.set($any($event.target).checked)"
              data-testid="dialog-show-description"
            />
            <span class="min-w-0 flex-1 break-words text-heading">{{ t('indicators.dialog.context.description.label') }}</span>
          </label>
          <p class="text-sm text-body" data-testid="dialog-show-description-help">
            {{ hasDescription() ? t('indicators.dialog.context.description.help') : t('indicators.dialog.context.description.none') }}
          </p>
        </div>

        @if (crowded()) {
          <!-- A hint, not a refusal: the texts take room from the chart, and the tile keeps its size. -->
          <tmz-alert tone="warning" data-testid="dialog-context-warning">{{ t(chartType() === 'stat' ? 'indicators.dialog.context.crowdedFigure' : 'indicators.dialog.context.crowded') }}</tmz-alert>
        }
      </fieldset>

      @if (chartType() === 'stat') {
        <!-- The lines under what a stat tile plots (improve-stat-cards, design D6): its base n and its value without the dashboard filters. -->
        <fieldset class="min-w-0" [attr.aria-describedby]="shown().secondary ? 'widgetSecondary-error' : null" data-testid="dialog-secondary">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.secondary.legend') }}</legend>
          <p class="mb-1 text-sm text-body">{{ t('indicators.dialog.secondary.help') }}</p>
          @for (line of secondaryLines; track line) {
            <label class="flex min-h-10 cursor-pointer items-start gap-3 py-1 text-sm">
              <input
                type="checkbox"
                class="mt-0.5 size-4 shrink-0"
                [checked]="secondary().includes(line)"
                (change)="setSecondary(line, $any($event.target).checked)"
                [attr.data-testid]="'dialog-secondary-' + line"
              />
              <span class="min-w-0 flex-1 break-words text-heading">{{ t('indicators.dialog.secondary.' + line) }}</span>
            </label>
          }
          @if (secondaryNoN(); as reason) {
            <p class="text-sm text-body" data-testid="dialog-secondary-n-hint">{{ t('indicators.dialog.bases.noN.' + reason) }}</p>
          }
          <p class="text-sm text-body" data-testid="dialog-secondary-room">{{ t('indicators.dialog.secondary.room') }}</p>
          @if (shown().secondary; as message) {
            <p id="widgetSecondary-error" class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
          }
        </fieldset>
      }

      <!-- The bases (add-small-sample-warnings, design D12): the base n line, the small-base threshold and the values that mean no data. A histogram has no n. -->
      @if (offersBases() || offersMissingFooter()) {
        <fieldset class="flex min-w-0 flex-col gap-4" data-testid="dialog-bases">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.bases.legend') }}</legend>
          <p class="-mt-2 text-sm text-body">{{ t('indicators.dialog.bases.help') }}</p>

          @if (offersBases()) {
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="min-w-0">
                <tmz-form-field [label]="t('indicators.dialog.bases.showN.label')" fieldId="widgetShowN" [error]="shown().showN">
                  <select tmzInput id="widgetShowN" (change)="setShowN($any($event.target).value)" data-testid="dialog-show-n">
                    <option value="dashboard" [selected]="showNChoice() === 'dashboard'">{{ t(dashboardBases.showN ? 'indicators.dialog.bases.showN.dashboardShown' : 'indicators.dialog.bases.showN.dashboardHidden') }}</option>
                    <option value="show" [selected]="showNChoice() === 'show'">{{ t('indicators.dialog.bases.showN.show') }}</option>
                    <option value="hide" [selected]="showNChoice() === 'hide'">{{ t('indicators.dialog.bases.showN.hide') }}</option>
                  </select>
                </tmz-form-field>
                @if (noNReason(); as reason) {
                  <p class="mt-2 text-sm text-body" data-testid="dialog-n-hint">{{ t('indicators.dialog.bases.noN.' + reason) }}</p>
                }
              </div>
              <div class="min-w-0">
                <tmz-form-field [label]="t('indicators.dialog.bases.minN.label')" fieldId="widgetMinNChoice">
                  <select tmzInput id="widgetMinNChoice" (change)="setMinNChoice($any($event.target).value)" data-testid="dialog-min-n-choice">
                    <option value="dashboard" [selected]="minNChoice() === 'dashboard'">{{ dashboardBases.minN === null ? t('indicators.dialog.bases.minN.dashboardOff') : t('indicators.dialog.bases.minN.dashboardBelow', { min: dashboardBases.minN }) }}</option>
                    <option value="off" [selected]="minNChoice() === 'off'">{{ t('indicators.dialog.bases.minN.off') }}</option>
                    <option value="own" [selected]="minNChoice() === 'own'">{{ t('indicators.dialog.bases.minN.own') }}</option>
                  </select>
                </tmz-form-field>
              </div>
              @if (minNChoice() === 'own') {
                <tmz-form-field [label]="t('indicators.dialog.bases.minN.valueLabel')" fieldId="widgetMinN" [error]="shown().minN" [hint]="t('indicators.dialog.bases.minN.hint', { max: maxMinNText })">
                  <input tmzInput id="widgetMinN" inputmode="numeric" autocomplete="off" [value]="minNText()" (input)="setMinNText($any($event.target).value)" data-testid="dialog-min-n" />
                </tmz-form-field>
              }
            </div>
          }

          @if (offersMissingFooter()) {
            <div data-testid="dialog-missing-group">
              <label class="flex min-h-10 cursor-pointer items-start gap-3 py-1 text-sm">
                <input
                  type="checkbox"
                  class="mt-0.5 size-4 shrink-0"
                  [checked]="missingFooter() === true"
                  (change)="setMissingFooter($any($event.target).checked)"
                  data-testid="dialog-missing-footer"
                />
                <span class="min-w-0 flex-1 break-words text-heading">{{ t('indicators.dialog.bases.missing.label') }}</span>
              </label>
              <p class="text-sm text-body" data-testid="dialog-missing-help">{{ missingHelp(t) }}</p>
              @if (shown().missingFooter; as message) {
                <p class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
              }
            </div>
          }
        </fieldset>
      }

      @if (labelColumns().length) {
        <!-- The names this widget gives its columns (add-indicator-column-metadata, design D9); closed until it has some of its own. -->
        <tmz-disclosure [heading]="t('indicators.dialog.labels.heading')" [open]="startsWithLabels || hasLabelProblems()" data-testid="dialog-labels">
          <fieldset class="flex min-w-0 flex-col gap-3" [attr.aria-describedby]="shown().labels ? 'widgetLabels-error' : null">
            <legend class="sr-only">{{ t('indicators.dialog.labels.heading') }}</legend>
            <p class="text-sm text-body">{{ t('indicators.dialog.labels.help', { max: maxWidgetLabels }) }}</p>
            <div class="grid gap-4 sm:grid-cols-2">
              @for (column of labelColumns(); track column) {
                <tmz-form-field [label]="column" [fieldId]="'widgetLabel-' + column" [error]="labelProblems()[column]">
                  <input
                    tmzInput
                    [id]="'widgetLabel-' + column"
                    autocomplete="off"
                    [attr.maxlength]="maxColumnLabel"
                    [placeholder]="labelPlaceholder(column)"
                    [value]="labels()[column] ?? ''"
                    [attr.data-column]="column"
                    (input)="setLabel(column, $any($event.target).value)"
                    data-testid="dialog-label"
                  />
                </tmz-form-field>
              }
            </div>
            @if (shown().labels; as message) {
              <p id="widgetLabels-error" class="text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="labels-error">{{ message }}</p>
            }
          </fieldset>
        </tmz-disclosure>
      }

      @if (chartType() === 'pyramid') {
        <fieldset class="flex flex-col gap-3" data-testid="dialog-pyramid">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.pyramid.legend') }}</legend>
          <p class="-mt-2 text-sm text-body">{{ t('indicators.dialog.pyramid.help') }}</p>
          <div class="grid gap-4 sm:grid-cols-2">
            <tmz-form-field [label]="t('indicators.dialog.pyramid.left')" fieldId="widgetPyramidLeft" [error]="shown().pyramidLeft">
              <input tmzInput id="widgetPyramidLeft" list="widgetSeriesValues" autocomplete="off" [placeholder]="t('indicators.dialog.pyramid.placeholder')" [value]="pyramidLeft()" (input)="setPyramid('left', $any($event.target).value)" data-testid="dialog-pyramid-left" />
            </tmz-form-field>
            <tmz-form-field [label]="t('indicators.dialog.pyramid.right')" fieldId="widgetPyramidRight" [error]="shown().pyramidRight">
              <input tmzInput id="widgetPyramidRight" list="widgetSeriesValues" autocomplete="off" [placeholder]="t('indicators.dialog.pyramid.placeholder')" [value]="pyramidRight()" (input)="setPyramid('right', $any($event.target).value)" data-testid="dialog-pyramid-right" />
            </tmz-form-field>
          </div>
          <!-- The values the data holds; any other can be typed, for a dashboard filter can hide a side and a script can change the values later. -->
          <datalist id="widgetSeriesValues">
            @for (value of seriesValues(); track value) {
              <option [value]="value"></option>
            }
          </datalist>
        </fieldset>
      }

      @if (chartType() === 'likert') {
        <fieldset class="flex flex-col gap-3" data-testid="dialog-scale">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.scale.legend') }}</legend>
          <p class="-mt-2 text-sm text-body">{{ t('indicators.dialog.scale.help') }}</p>
          @if (!seriesBy()) {
            <p class="text-sm text-body" data-testid="scale-none">{{ t('indicators.dialog.scale.none') }}</p>
          }
          @for (item of scaleItems(); track $index; let i = $index; let first = $first; let last = $last) {
            <div class="grid gap-2 rounded-base border border-default p-3 sm:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_auto]" data-testid="scale-item">
              <div class="min-w-0">
                <input tmzInput autocomplete="off" [attr.id]="'scaleValue' + i" [attr.aria-label]="t('indicators.dialog.scale.value', { n: i + 1 })" [value]="item.value" (input)="setScaleValue(i, $any($event.target).value)" data-testid="scale-value" />
                @if (scaleItemProblems()[i]; as message) {
                  <p class="mt-1 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="scale-item-error">{{ message }}</p>
                }
              </div>
              <div class="min-w-0">
                <select tmzInput [attr.id]="'scaleSide' + i" [attr.aria-label]="t('indicators.dialog.scale.side', { value: item.value })" (change)="setScaleSide(i, $any($event.target).value)" data-testid="scale-side">
                  @for (side of scaleSides; track side) {
                    <option [value]="side" [selected]="side === item.side">{{ t('indicators.dialog.scale.' + side) }}</option>
                  }
                </select>
              </div>
              <div class="flex items-start gap-1 sm:justify-end">
                <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary disabled:cursor-not-allowed disabled:opacity-40" [disabled]="first" [attr.aria-label]="t('indicators.dialog.scale.up', { value: item.value })" (click)="moveScale(i, -1)" data-testid="scale-up">
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 15 6-6 6 6" /></svg>
                </button>
                <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary disabled:cursor-not-allowed disabled:opacity-40" [disabled]="last" [attr.aria-label]="t('indicators.dialog.scale.down', { value: item.value })" (click)="moveScale(i, 1)" data-testid="scale-down">
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6" /></svg>
                </button>
                <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary" [attr.aria-label]="t('indicators.dialog.scale.remove', { value: item.value })" (click)="removeScale(i)" data-testid="scale-remove">
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
                </button>
              </div>
            </div>
          }
          @if (shown().scale; as message) {
            <p class="text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="scale-error">{{ message }}</p>
          }
          <div class="flex flex-col gap-2 sm:flex-row sm:items-end">
            <div class="min-w-0 sm:w-64">
              <label for="widgetScaleNew" class="mb-2 block text-sm text-body">{{ t('indicators.dialog.scale.addLabel') }}</label>
              <input tmzInput id="widgetScaleNew" autocomplete="off" [placeholder]="t('indicators.dialog.scale.addPlaceholder')" [value]="scaleNew()" (input)="scaleNew.set($any($event.target).value)" (keydown.enter)="$event.preventDefault(); addScale()" data-testid="scale-new" />
            </div>
            <button tmzButton variant="secondary" type="button" (click)="addScale()" data-testid="scale-add">{{ t('indicators.dialog.scale.add') }}</button>
          </div>
        </fieldset>
      }

      @if (offersGroupOrder() || offersSeriesOrder()) {
        <fieldset class="flex min-w-0 flex-col gap-4" data-testid="dialog-order">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.order.legend') }}</legend>
          <p class="-mt-2 text-sm text-body">{{ t('indicators.dialog.order.help') }}</p>
          @if (offersGroupOrder()) {
            <div class="flex min-w-0 flex-col gap-2" data-testid="dialog-order-group">
              <p class="text-sm font-medium text-heading">{{ t('indicators.dialog.order.group', { column: groupBy() }) }}</p>
              @if (groupOrderSorted()) {
                <p class="text-sm text-body" data-testid="order-sorted">{{ t(chartType() === 'boxplot' ? 'indicators.dialog.order.sortedBoxes' : 'indicators.dialog.order.sorted') }}</p>
              } @else {
                <div class="flex flex-wrap gap-x-6 gap-y-1">
                  <label class="flex min-h-10 cursor-pointer items-center gap-2 text-sm text-heading">
                    <input type="radio" name="widgetGroupOrder" class="size-4 shrink-0" [checked]="groupOrder() === null" (change)="chooseOrder('group', false)" data-testid="dialog-order-group-default" />
                    {{ t('indicators.dialog.order.default.' + groupOrderDefault()) }}
                  </label>
                  <label class="flex min-h-10 cursor-pointer items-center gap-2 text-sm text-heading">
                    <input type="radio" name="widgetGroupOrder" class="size-4 shrink-0" [checked]="groupOrder() !== null" (change)="chooseOrder('group', true)" data-testid="dialog-order-group-own" />
                    {{ t('indicators.dialog.order.own') }}
                  </label>
                </div>
                @if (groupOrder(); as values) {
                  <app-value-order-editor [values]="values" [shown]="valuesNow()?.groups ?? null" [offered]="valuesNow()?.groups ?? []" (changed)="setOrder('group', $event)" testId="order-editor-group" />
                }
                @if (shown().groupOrder; as message) {
                  <p class="text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
                }
                @if (groupOrder() !== null && chartType() === 'pie') {
                  <p class="text-sm text-body" data-testid="order-colors">{{ t('indicators.dialog.order.colors') }}</p>
                }
              }
            </div>
          }
          @if (offersSeriesOrder()) {
            <div class="flex min-w-0 flex-col gap-2" data-testid="dialog-order-series">
              <p class="text-sm font-medium text-heading">{{ t('indicators.dialog.order.series', { column: seriesBy() }) }}</p>
              <div class="flex flex-wrap gap-x-6 gap-y-1">
                <label class="flex min-h-10 cursor-pointer items-center gap-2 text-sm text-heading">
                  <input type="radio" name="widgetSeriesOrder" class="size-4 shrink-0" [checked]="seriesOrder() === null" (change)="chooseOrder('series', false)" data-testid="dialog-order-series-default" />
                  {{ t('indicators.dialog.order.default.' + seriesOrderDefault()) }}
                </label>
                <label class="flex min-h-10 cursor-pointer items-center gap-2 text-sm text-heading">
                  <input type="radio" name="widgetSeriesOrder" class="size-4 shrink-0" [checked]="seriesOrder() !== null" (change)="chooseOrder('series', true)" data-testid="dialog-order-series-own" />
                  {{ t('indicators.dialog.order.own') }}
                </label>
              </div>
              @if (seriesOrder(); as values) {
                <app-value-order-editor [values]="values" [shown]="valuesNow()?.series ?? null" [offered]="valuesNow()?.series ?? []" (changed)="setOrder('series', $event)" testId="order-editor-series" />
              }
              @if (shown().seriesOrder; as message) {
                <p class="text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
              }
              @if (seriesOrder() !== null) {
                <p class="text-sm text-body" data-testid="order-colors">{{ t('indicators.dialog.order.colors') }}</p>
              }
            </div>
          }
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
          @if (answersHint(); as hint) {
            <!-- A ratio over several values of a column that may hold the answers of one question (improve-stat-cards, design D5). It never blocks saving. -->
            <p class="mb-3 text-sm text-body" data-testid="dialog-answers-hint">{{ t('indicators.dialog.answersHint', { count: hint.count, column: hint.column }) }}</p>
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

      @if (dashboardFilters.length) {
        <!-- Checked: the filter narrows this widget; unchecked: the widget ignores it, as a total that must not change does. -->
        <fieldset class="min-w-0" data-testid="dialog-dashboard-filters">
          <legend class="mb-1 text-sm font-medium text-heading">{{ t('indicators.dialog.dashboardFilters') }}</legend>
          <p class="mb-2 text-sm text-body">{{ t('indicators.dialog.dashboardFiltersHelp') }}</p>
          @for (filter of dashboardFilters; track filter.column) {
            <label class="flex min-h-10 cursor-pointer items-start gap-3 py-1 text-sm">
              <input
                type="checkbox"
                class="mt-0.5 size-4 shrink-0"
                [checked]="!ignored().includes(filter.column)"
                (change)="setIgnored(filter.column, !$any($event.target).checked)"
                [attr.data-column]="filter.column"
                data-testid="dialog-dashboard-filter"
              />
              <span class="min-w-0 flex-1 break-words text-heading">{{ t('indicators.dialog.applyFilter', { label: filter.label }) }}</span>
            </label>
          }
          @if (shown().dashboardFilters; as message) {
            <p class="mt-1 text-sm text-fg-danger-strong" data-testid="field-error" [tmzApiLang]="message">{{ message }}</p>
          }
        </fieldset>
      }
      </div>

      <section aria-labelledby="widgetPreviewTitle" class="lg:sticky lg:top-0 lg:self-start">
        <h3 id="widgetPreviewTitle" class="mb-2 text-sm font-medium text-heading">{{ t('indicators.dialog.preview') }}</h3>
        <div class="flex flex-col gap-2 rounded-base border border-default p-3" [class]="chartType() === 'stat' ? 'h-40' : 'h-72'" data-testid="dialog-preview">
          <!-- The subtitle sits in a tile's header, which the preview does not draw; the footnote is the widget's own. -->
          @if (draftSubtitle(); as subtitle) {
            <p class="shrink-0 text-xs text-body" [class.line-clamp-1]="tileRows() <= 2" [class.line-clamp-2]="tileRows() > 2" [attr.title]="subtitle" data-testid="dialog-preview-subtitle">{{ subtitle }}</p>
          }
          <div class="min-h-0 flex-1">
            <app-widget-view
              [projectId]="data.projectId"
              [chartType]="chartType()"
              [config]="previewConfig()"
              [indicator]="entry()"
              [debounceMs]="previewDelayMs"
              [placeholder]="t('indicators.dialog.previewIncomplete')"
              [dashboardFilters]="previewFilters()"
              [colors]="data.colors"
              [bases]="dashboardBases"
              [tileRows]="tileRows()"
              (valuesShown)="valuesNow.set($event)"
            />
          </div>
        </div>
        @if (previewFilteredBy().length) {
          <p class="mt-2 text-xs text-body" data-testid="dialog-preview-filtered">{{ t('indicators.dialog.previewFiltered', { names: previewFilteredBy().join(', ') }) }}</p>
        }
      </section>
      </div>

      @if (error(); as message) {
        <tmz-alert tone="danger" data-testid="dialog-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly basemaps = inject(Basemaps);

  protected readonly chartTypes = chartTypes;
  protected readonly maxBuckets = maxBuckets;
  protected readonly maxFilters = maxFilters;
  protected readonly maxTitleLength = maxTitleLength;
  protected readonly maxTooltipItems = maxTooltipItems;
  protected readonly maxTooltipLabel = maxTooltipLabel;
  protected readonly maxColumnLabel = maxColumnLabel;
  protected readonly maxWidgetLabels = maxWidgetLabels;
  protected readonly tooltipFormats = tooltipFormats;
  protected readonly previewDelayMs = previewDelayMs;
  protected readonly layouts = seriesLayouts;
  protected readonly scaleSides = scaleSides;
  protected readonly maxSubtitleLength = maxSubtitleLength;
  protected readonly maxFootnoteLength = maxFootnoteLength;
  protected readonly maxCaptionLength = maxCaptionLength;
  protected readonly maxUnitLength = maxWidgetUnitLength;
  protected readonly captionModes = captionModes;
  protected readonly maxReferenceLines = maxReferenceLines;
  protected readonly maxReferenceLineLabel = maxReferenceLineLabel;
  protected readonly interpolations = lineInterpolations;

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
  /** The series column, '' for none (design D7). Changing the indicator or the series column resets what depends on it. */
  protected readonly seriesBy = signal(this.start.seriesBy ?? '');
  /**
   * A widget saved with no layout and a series (the API allows it) keeps it that way: it is read as grouped, but the form saves what it
   * read until the member picks a layout, as `setOrientation` keeps a box plot's missing orientation, so a form saved unchanged sends the
   * config it was opened with and makes no version. A new widget, or one saved with a layout, saves the layout shown.
   */
  private readonly layoutUnset = !!this.data.widget && this.start.layout == null;
  /** The layout of a bar or column chart with a series: null for one saved with none, which is drawn as grouped. */
  protected readonly layout = signal<SeriesLayout | null>(this.layoutUnset ? null : (this.start.layout ?? 'grouped'));
  /** The layout the radios show as chosen. */
  protected readonly chosenLayout = computed<SeriesLayout>(() => this.layout() ?? 'grouped');
  /** The two sides of a population pyramid, as typed or chosen. */
  protected readonly pyramidLeft = signal(this.start.pyramid?.left ?? '');
  protected readonly pyramidRight = signal(this.start.pyramid?.right ?? '');
  /** The values of a Likert scale in order, with the values of the data that are not in it (side `out`) listed after them. */
  protected readonly scaleItems = signal<ScaleItem[]>((this.start.scale ?? []).map((step) => ({ value: step.value, side: step.side })));
  protected readonly scaleNew = signal('');
  /** What the server refused in each value of the scale, by its place in the editor. */
  protected readonly scaleItemProblems = signal<Record<number, string>>({});
  /** The values of the series column in the data, in natural order: what the pyramid's sides and the Likert scale offer. */
  protected readonly seriesValues = signal<string[]>([]);
  /** The dashboard's palette, whose eight colors the form offers (an unknown one is Tamiza, as the charts read it). */
  protected readonly palette = colorsOf(this.data.colors).palette;
  /** The scale a map or a heat map is drawn on when it names none: the palette's own. */
  protected readonly paletteScale = defaultScale(this.palette);
  protected readonly places = places;
  protected readonly rampChoices = rampChoices;
  /** The widget's own color, a place in the palette (1 to 8); null is unset, which is color 1 (design D3). */
  protected readonly color = signal<number | null>(this.start.display?.color ?? null);
  /** The widget's own scale; null is the palette's own. */
  protected readonly ramp = signal<ScaleId | null>(this.start.display?.ramp ?? null);
  /** The color the group shows as chosen. */
  protected readonly chosenColor = computed(() => this.color() ?? 1);
  /** A box plot's direction; null is unset, which draws it vertical (design D1 of add-boxplot-widget). */
  protected readonly orientation = signal<BoxOrientation | null>(this.start.display?.orientation ?? null);
  protected readonly orientations = boxOrientations;
  protected readonly chosenOrientation = computed(() => this.orientation() ?? 'vertical');
  /**
   * The names the widget gives its columns, keyed by the column, as the form holds them: the saved ones to start with, then what is
   * typed. A column the widget does not show keeps its name here, so changing the chart type or the plot back and forth loses none.
   */
  protected readonly labels = signal<Record<string, string>>({ ...(this.start.display?.labels ?? {}) });
  /** The group starts open only for a widget that already has names of its own. */
  protected readonly startsWithLabels = Object.values(this.start.display?.labels ?? {}).some((label) => label.trim());
  /** A label field was typed in: from then on the draft holds only the labels of the columns the widget shows (design D9). */
  private readonly labelsTyped = signal(false);
  /** The server's refusals of a label, by column. */
  protected readonly labelProblems = signal<Record<string, string>>({});
  protected readonly hasLabelProblems = computed(() => Object.keys(this.labelProblems()).length > 0 || !!this.serverFieldErrors().labels);
  /**
   * The widget's texts as the form holds them (add-widget-context, design D11): seeded from the saved display, so a widget that already
   * has them (set through the API or MCP) saves them again when the form is saved unchanged. Unset is an empty text, the default
   * caption and no description button.
   */
  protected readonly subtitle = signal(this.start.display?.subtitle ?? '');
  protected readonly footnote = signal(this.start.display?.footnote ?? '');
  protected readonly captionMode = signal<CaptionMode>(
    this.start.display?.caption?.hidden ? 'hidden' : this.start.display?.caption?.text ? 'text' : 'default',
  );
  protected readonly captionText = signal(this.start.display?.caption?.text ?? '');
  protected readonly unit = signal(this.start.display?.unit ?? '');
  protected readonly showDescription = signal(this.start.display?.showDescription === true);
  /** The dashboard's base settings, or the shown version's: what a widget that says nothing of its own follows. */
  protected readonly dashboardBases = basesOf(this.data.bases);
  protected readonly maxMinNText = numberText(maxMinN, inject(LOCALE_ID)) ?? String(maxMinN);
  /**
   * The base settings of the widget (add-small-sample-warnings, design D7, D12), seeded from what is saved so a form saved unchanged sends
   * what it read: a 0 and a false are kept as saved, and an absent setting follows the dashboard.
   */
  protected readonly showNChoice = signal<ShowNChoice>(this.start.display?.showN === true ? 'show' : this.start.display?.showN === false ? 'hide' : 'dashboard');
  protected readonly minNChoice = signal<MinNChoice>(typeof this.start.display?.minN !== 'number' ? 'dashboard' : this.start.display.minN === 0 ? 'off' : 'own');
  /** The threshold as typed while the widget has its own. */
  protected readonly minNText = signal(typeof this.start.display?.minN === 'number' && this.start.display.minN > 0 ? String(this.start.display.minN) : '');
  /** The statistic of a stat tile (improve-stat-cards, design D6): empty for the default, the sum or the ratio of sums. Kept while the kind changes; `savedConfig` drops it from any other kind. */
  protected readonly statistic = signal<StatStatistic | ''>(this.start.statistic ?? '');
  protected readonly statistics = statStatistics;
  protected readonly secondaryLines = statSecondaryLines;
  /**
   * The lines a stat tile shows under what it plots, as the form holds them: the stored list as it was (its order too) until a box is
   * changed, then in the fixed order, `n` first, so a form saved unchanged sends what it read (design D3).
   */
  protected readonly secondary = signal<StatSecondaryLine[]>([...(this.start.display?.secondary ?? [])]);
  /** Whether to leave out the values that mean no data: stored `true` when ticked, and `false` only for a widget that already had it saved (design D6). */
  protected readonly missingFooter = signal<boolean | null>(typeof this.start.missingFooter === 'boolean' ? this.start.missingFooter : null);
  /**
   * The chart annotations (add-chart-annotations, design D8): the reference lines as rows, seeded from the saved display, and the two
   * settings with their default stored only when the widget already had it saved (`setInterpolation`, `setDataLabels`), so a form
   * saved unchanged sends what it read. `savedConfig` keeps what the kind, the series and the layout take; the rest stays here while the
   * member changes them back and forth, as a color does.
   */
  protected readonly referenceLines = signal<LineRow[]>(
    (this.start.display?.referenceLines ?? []).filter((line) => !!line).map((line) => ({ kind: line.kind, value: line.value ?? null, typed: null, label: line.label ?? '' })),
  );
  protected readonly interpolation = signal<LineInterpolation | null>(this.start.display?.interpolation ?? null);
  protected readonly dataLabels = signal<boolean | null>(this.start.display?.dataLabels ?? null);
  /**
   * The widget's own order of its groups and of its series (add-declared-category-order, design D9), or null for the default: its
   * indicator's order, else the chart's. Kept while the member changes the kind or the sort, as a color is: `savedConfig` keeps them only
   * where the kind, the columns and the sort take them.
   */
  protected readonly groupOrder = signal<string[] | null>(this.start.display?.order?.group ? [...this.start.display.order.group] : null);
  protected readonly seriesOrder = signal<string[] | null>(this.start.display?.order?.series ? [...this.start.display.order.series] : null);
  /** The values the preview draws, in the order it draws them: what an own order is seeded from and what the editors offer. */
  protected readonly valuesNow = signal<ShownValues | null>(null);
  /** What the server refused in each reference line, by its row. */
  protected readonly lineProblems = signal<Record<number, LineProblems>>({});
  protected readonly chosenInterpolation = computed<LineInterpolation>(() => this.interpolation() ?? 'linear');
  /** The indicator has a description the button could open. */
  protected readonly hasDescription = computed(() => !!this.entry()?.description?.trim());
  /**
   * The option is unavailable while the indicator has no description. A widget saved with the option on for an indicator that has none
   * (the description was removed since) can still turn it off.
   */
  protected readonly descriptionAvailable = computed(() => this.hasDescription() || this.showDescription());
  /** The tile's size: the widget's own, or what its kind starts with when it is new (design D5). */
  private readonly tile = computed(() => this.data.widget?.layout ?? defaultSize(this.chartType()));
  /** The rows of that tile, which the texts of the preview are cut by as they are on the dashboard. */
  protected readonly tileRows = computed(() => this.tile().h);
  /** The subtitle as the draft saves it, or null: the preview shows it above the widget. */
  protected readonly draftSubtitle = computed(() => singleLine(this.subtitle()) || null);
  /** Whether the subtitle and footnote are likely to crowd the tile (design D5): a hint, never a refusal. */
  protected readonly crowded = computed(() => textsCrowd(this.tile(), singleLine(this.subtitle()), singleLine(this.footnote())));
  /** A bar, column or line chart with no series, a histogram and a box plot take a color; any other chart takes its colors from its values. */
  protected readonly offersColor = computed(
    () => this.chartType() === 'histogram' || this.chartType() === 'boxplot' || (takesSeries(this.chartType()) && !this.seriesBy()),
  );
  /** A map and a heat map take a scale. */
  protected readonly offersRamp = computed(() => this.chartType() === 'map' || this.chartType() === 'heatmap');
  /** The open dashboard's filters, each offered with a box to ignore it. */
  protected readonly dashboardFilters = this.data.filters ?? [];
  /**
   * The columns of the dashboard filters the widget ignores (design D6), with those of filters the dashboard does not have now: the
   * widget keeps ignoring a filter removed and defined again.
   */
  protected readonly ignored = signal<string[]>([...(this.start.ignoredDashboardFilters ?? [])]);
  /** What is saved: every entry while they fit, those of filters the dashboard has not dropped first, oldest first, when not. */
  private readonly ignoredDashboardFilters = computed(() => {
    const shown = new Set(this.dashboardFilters.map((f) => f.column));
    const ignored = this.ignored();
    let excess = Math.max(0, ignored.length - maxDashboardFilters);
    return ignored.filter((column) => shown.has(column) || excess-- <= 0);
  });
  /** The dashboard filters in force that the preview applies, marked where the widget ignores them. */
  protected readonly previewFilters = computed(() => activeFilters(this.dashboardFilters, this.data.values ?? {}, this.ignored()));
  /** The labels of those the preview is narrowed by. */
  protected readonly previewFilteredBy = computed(() => this.previewFilters().filter((a) => !a.ignored).map((a) => a.filter.label));

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
  /** Bar, column and line widgets may have a series column; heat map, pyramid, Likert and cross table widgets need one. */
  protected readonly offersSeries = computed(() => hasSeries(this.chartType()));
  protected readonly requiresSeries = computed(() => needsSeries(this.chartType()));
  /** Every kind with groups takes an order of them, once there is a group column (a stat tile, a histogram and a map list none). */
  protected readonly offersGroupOrder = computed(() => !['stat', 'histogram', 'map'].includes(this.chartType()) && !!this.groupBy());
  /** A sort, or an order by median, decides the order of the groups: an own order of them is off. */
  protected readonly groupOrderSorted = computed(() => {
    const type = this.chartType();
    return ((type === 'bar' || type === 'column') && this.sortKey() !== '') || (type === 'boxplot' && this.sortKey() === 'value');
  });
  /** A pyramid's sides and a Likert scale set the order of their series, and the other kinds have none. */
  protected readonly offersSeriesOrder = computed(() => ['bar', 'column', 'line', 'heatmap', 'crosstab'].includes(this.chartType()) && !!this.seriesBy());
  /** What the default order is called: the indicator's, when it declares one for the column, else the chart's own. */
  protected readonly groupOrderDefault = computed(() => this.defaultOrderOf(this.groupBy()));
  protected readonly seriesOrderDefault = computed(() => this.defaultOrderOf(this.seriesBy()));
  /** Values with no color kept take the palette's colors in the order of the series, and of a pie's slices (design D10). */
  protected readonly orderRecolors = computed(() => this.chartType() === 'pie' || this.offersSeriesOrder());
  /** The layout is the choice of a bar or column chart that has a series. */
  protected readonly offersLayout = computed(() => (this.chartType() === 'bar' || this.chartType() === 'column') && !!this.seriesBy());
  /** What the rules of the annotations read of the form: the series, the layout and whether it plots a ratio. */
  private readonly annotationRules = computed(() => ({
    seriesBy: this.seriesBy() || null,
    layout: this.offersLayout() ? this.layout() : null,
    ratio: this.mode() === 'ratio' ? { numerator: this.numerator(), denominator: this.denominator() } : null,
  }));
  /** The kinds of reference line the widget takes now (design D2, D8); a bar, column or line only. */
  protected readonly lineKinds = computed(() => referenceKindsFor(this.chartType(), this.annotationRules()));
  protected readonly offersLines = computed(() => this.lineKinds().length > 0);
  /**
   * The kinds a line may be added or changed to: those the widget takes, without an overall line while it groups or splits by a column its
   * indicator declares as answers (add-small-sample-warnings, design D16). A line saved before the declaration keeps its kind and is saved
   * as it was: `lineKinds`, which decides what is saved, does not know the declaration.
   */
  protected readonly offeredKinds = computed(() =>
    referenceKindsFor(this.chartType(), { ...this.annotationRules(), groupBy: this.groupBy() || null }, answerColumnsOf(this.entry()?.columns)),
  );
  /** The base n line and the threshold are offered on every kind but a histogram, which has no n (design D7). */
  protected readonly offersBases = computed(() => this.chartType() !== 'histogram');
  /** The option to leave out the values that mean no data is the choice of the kinds that take it (design D6). */
  protected readonly offersMissingFooter = computed(() => takesMissingFooter(this.chartType()));
  /** The threshold the widget has of its own, or null: a typed whole number from 1 to the largest. */
  private readonly ownMinN = computed(() => {
    const typed = this.minNText().trim();
    const value = /^\d{1,5}$/.test(typed) ? Number(typed) : NaN;
    return value >= 1 && value <= maxMinN ? value : null;
  });
  /** Why the widget has no base n, said while its n line is on (design D4, D12): the kind, no declaration at all, or a measure with no base column. */
  protected readonly noNReason = computed<NoNReason | null>(() => {
    if (!settingsInForce(this.draft().display, this.dashboardBases).showN) {
      return null;
    }
    const type = this.chartType();
    if (type === 'histogram' || type === 'boxplot') {
      return 'kind';
    }
    const declared = this.entry()?.columns;
    const base = baseColumnOf(declared);
    if (!base && answerColumnsOf(declared).length === 0) {
      return 'undeclared';
    }
    return this.mode() === 'measure' && !base ? 'measure' : null;
  });
  /**
   * Why a stat tile with no statistic can have no n, for the hint under its n line (improve-stat-cards, design D6): its indicator declares
   * neither a base column nor an answer column, or it plots a measure with no base column. A tile with a statistic always has one (design D10).
   */
  protected readonly secondaryNoN = computed<NoNReason | null>(() => {
    const declared = this.entry()?.columns;
    if (this.statistic() || !this.entry()) {
      return null;
    }
    const base = baseColumnOf(declared);
    if (!base && answerColumnsOf(declared).length === 0) {
      return 'undeclared';
    }
    return this.mode() === 'measure' && !base ? 'measure' : null;
  });
  /** The hint under the denominator: a box plot and a stat tile with a statistic divide each row by its own denominator (design D6). */
  protected ratioHelpKey(): string {
    return this.chartType() === 'boxplot' ? 'indicators.dialog.ratioHelpBoxplot' : this.chartType() === 'stat' && this.statistic() ? 'indicators.dialog.ratioHelpStatistic' : 'indicators.dialog.ratioHelp';
  }
  /**
   * The hint of design D5: the widget plots a ratio and a filter keeps several values of a text column that is neither its group column
   * nor its series column and that its indicator does not declare as the answers of one question. The first such filter; null otherwise.
   */
  protected readonly answersHint = computed<{ count: number; column: string } | null>(() => {
    const catalog = this.catalog();
    if (this.mode() !== 'ratio' || !catalog?.published) {
      return null;
    }
    const type = this.chartType();
    const apart = new Set([type === 'stat' ? '' : this.groupBy(), hasSeries(type) ? this.seriesBy() : ''].filter((column) => !!column));
    const declared = this.entry()?.columns ?? {};
    for (const filter of this.filters()) {
      const values = filter.values ?? [];
      const text = catalog.columns.find((column) => column.name === filter.column)?.type === 'text';
      if (filter.op === 'in' && values.length >= 2 && text && !apart.has(filter.column) && !declared[filter.column]?.categories) {
        return { count: values.length, column: filter.column };
      }
    }
    return null;
  });
  /** Steps are the choice of a line chart. */
  protected readonly offersSteps = computed(() => this.chartType() === 'line');
  protected readonly offersDataLabels = computed(() => offersDataLabels(this.chartType(), this.annotationRules()));
  /** A percent indicator, and the share of a 100% layout, take a value typed as a percent (50 for 0.5). */
  protected readonly valueIsPercent = computed(() => this.entry()?.format === 'percent' || (this.offersLayout() && this.layout() === 'percent'));
  /** The unit written beside a value: % for a percent, otherwise the widget's own unit, else the indicator's. */
  protected readonly valueUnit = computed(() => (this.valueIsPercent() ? '%' : singleLine(this.unit()) || (this.entry()?.unit ?? '')));

  /** The table's columns, with any the widget names that the table no longer has kept so the form shows what is saved. */
  private columnsWith(names: readonly string[], current: readonly string[]): string[] {
    return [...current.filter((name) => name && !names.includes(name)), ...names];
  }

  protected readonly numericColumns = computed(() =>
    this.columnsWith(this.catalog()?.numeric ?? [], [this.measure(), this.numerator(), this.denominator()]).filter((name, i, all) => all.indexOf(name) === i),
  );
  protected readonly allColumns = computed(() => this.columnsWith(this.catalog()?.columns.map((c) => c.name) ?? [], [this.groupBy()]));
  /** Every column but the group column; a saved series column the table no longer has is kept, so the form shows what is saved. */
  protected readonly seriesColumns = computed(() => this.columnsWith(this.catalog()?.columns.map((c) => c.name) ?? [], [this.seriesBy()]).filter((name) => name !== this.groupBy()));
  protected readonly sortColumn = computed(() => (this.sortKey().startsWith('column:') ? this.sortKey().slice('column:'.length) : ''));
  /** A saved sort column the table no longer has is kept in front here only, not in the group-by or filter lists. */
  protected readonly sortColumns = computed(() => this.columnsWith(this.catalog()?.columns.map((c) => c.name) ?? [], [this.sortColumn()]));
  protected readonly sortable = computed(() => this.chartType() === 'bar' || this.chartType() === 'column' || this.chartType() === 'boxplot');
  /**
   * The order the select shows. A box plot is ordered by its median only: an order by a column left from a bar or a column chart reads
   * as the default one, and is not sent (see `queryFor`).
   */
  protected readonly effectiveSortKey = computed(() => (this.chartType() === 'boxplot' && this.sortKey().startsWith('column:') ? '' : this.sortKey()));
  /** A box plot cannot be ordered without a group column: the API refuses it, so the field says why it is off. */
  protected readonly orderBlocked = computed(() => this.chartType() === 'boxplot' && !this.groupBy());
  /** The sort column's type; null while the table loads, when it cannot be read and when the table no longer has the column. */
  private readonly sortType = computed(() => this.catalog()?.columns.find((c) => c.name === this.sortColumn())?.type ?? null);
  /** Only a text, integer or decimal column is compared as text or as a number; a date, timestamp or true/false column is not. */
  protected readonly showCompareAs = computed(() => this.chartType() !== 'boxplot' && this.sortable() && ['text', 'integer', 'decimal'].includes(this.sortType() ?? ''));
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

  /** The columns the widget shows and so offers a name for: its measure (when it names one) or its ratio's columns, its group column (not a stat tile's) and its series column. */
  protected readonly labelColumns = computed<string[]>(() => {
    const type = this.chartType();
    const [first, second, group, series] = this.mode() === 'ratio' ? [this.numerator(), this.denominator(), this.groupBy(), this.seriesBy()] : [this.measure(), '', this.groupBy(), this.seriesBy()];
    const shown = [first, second, type === 'stat' ? '' : group, hasSeries(type) ? series : ''];
    return shown.filter((column, i) => column && shown.indexOf(column) === i);
  });

  /** The columns the widget named when it was opened, to tell that the member changed one (the chart type alone is not such a change). */
  private readonly startColumns = JSON.stringify([
    ...(this.start.ratio ? [this.start.ratio.numerator, this.start.ratio.denominator] : [this.start.measure ?? '']),
    this.start.groupBy ?? '',
    this.start.seriesBy ?? '',
  ]);

  /**
   * The labels the draft holds. While the member has typed in no label field and has changed no column of the plot, they are the saved
   * ones as they were, so a widget saved by an AI client with labels for columns it does not show saves its stored text again. From
   * the first such change they are those of the columns the widget shows, so a replaced column loses its label and the form never passes
   * the limit of labels (design D9). A blank label is none.
   */
  private draftLabels(): Record<string, string> | undefined {
    const moved = this.labelsTyped() || JSON.stringify(this.queryColumnsForStart()) !== this.startColumns;
    const shown = new Set(this.labelColumns());
    const kept = Object.entries(this.labels()).filter(([column, label]) => label.trim() && (!moved || shown.has(column)));
    return kept.length ? Object.fromEntries(kept) : undefined;
  }

  /** The columns of the plot in the order `startColumns` holds them. */
  private queryColumnsForStart(): string[] {
    const ratio = this.mode() === 'ratio';
    return [...(ratio ? [this.numerator(), this.denominator()] : [this.measure()]), this.groupBy(), this.seriesBy()];
  }

  /** The name a field proposes for a column: its indicator's, else the column's own. */
  protected labelPlaceholder(column: string): string {
    return columnLabels(this.entry(), null)(column);
  }

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
      ignoredDashboardFilters: this.ignoredDashboardFilters(),
      seriesBy: this.seriesBy() || null,
      layout: this.offersLayout() ? this.layout() : null,
      pyramid: this.pyramidLeft() || this.pyramidRight() ? { left: this.pyramidLeft(), right: this.pyramidRight() } : null,
      scale: this.scaleSteps(),
      ...(this.missingFooter() !== null ? { missingFooter: this.missingFooter()! } : {}),
      ...(this.statistic() ? { statistic: this.statistic() as StatStatistic } : {}),
      ...this.displayDraft(),
    };
  });

  /**
   * The widget's color and scale as the form holds them, kept while the member changes the kind or the series back and forth: `savedConfig`
   * keeps them only for the kinds that take them. A widget with neither has no `display` at all, so one saved before palettes saves
   * exactly what it did.
   */
  private displayDraft(): { display?: WidgetDisplay } {
    const color = this.color();
    const ramp = this.ramp();
    const orientation = this.orientation();
    const labels = this.draftLabels();
    const subtitle = singleLine(this.subtitle());
    const footnote = singleLine(this.footnote());
    const unit = singleLine(this.unit());
    const captionText = singleLine(this.captionText());
    const caption: WidgetCaption | null =
      this.captionMode() === 'hidden' ? { hidden: true } : this.captionMode() === 'text' && captionText ? { text: captionText } : null;
    const description = this.showDescription();
    const lines = this.draftLines();
    const interpolation = this.interpolation();
    const dataLabels = this.dataLabels();
    const groups = this.groupOrder();
    const series = this.seriesOrder();
    const order = groups?.length || series?.length ? { ...(groups?.length ? { group: [...groups] } : {}), ...(series?.length ? { series: [...series] } : {}) } : null;
    const showN = this.showNChoice() === 'dashboard' ? null : this.showNChoice() === 'show';
    const minN = this.minNChoice() === 'dashboard' ? null : this.minNChoice() === 'off' ? 0 : this.ownMinN();
    const secondary = this.secondary();
    return color === null &&
      ramp === null &&
      orientation === null &&
      !labels &&
      !subtitle &&
      !footnote &&
      !unit &&
      !caption &&
      !description &&
      !lines.length &&
      interpolation === null &&
      dataLabels === null &&
      !order &&
      showN === null &&
      minN === null &&
      !secondary.length
      ? {}
      : {
          display: {
            ...(color !== null ? { color } : {}),
            ...(ramp !== null ? { ramp } : {}),
            ...(orientation !== null ? { orientation } : {}),
            ...(labels ? { labels } : {}),
            ...(subtitle ? { subtitle } : {}),
            ...(footnote ? { footnote } : {}),
            ...(caption ? { caption } : {}),
            ...(unit ? { unit } : {}),
            ...(description ? { showDescription: true as const } : {}),
            ...(lines.length ? { referenceLines: lines } : {}),
            ...(interpolation !== null ? { interpolation } : {}),
            ...(dataLabels !== null ? { dataLabels } : {}),
            ...(order ? { order } : {}),
            ...(minN !== null ? { minN } : {}),
            ...(showN !== null ? { showN } : {}),
            ...(secondary.length ? { secondary: [...secondary] } : {}),
          },
        };
  }

  /**
   * The reference lines the draft holds (design D8): every row with a number where it needs one, in the member's order. `savedConfig`
   * then keeps the kinds the widget takes. A fixed value with no valid number is not drawn or saved, and blocks saving (see `problems`).
   */
  private draftLines(): WidgetReferenceLine[] {
    return this.referenceLines().flatMap((row): WidgetReferenceLine[] => {
      if (row.kind === 'value' && row.value === null) {
        return [];
      }
      const label = singleLine(row.label);
      return [{ kind: row.kind, ...(row.kind === 'value' ? { value: row.value as number } : {}), ...(label ? { label } : {}) }];
    });
  }

  /** The rows `savedConfig` keeps, by place in the form: the kinds the widget takes, with a number where a fixed value needs one. */
  private keptRows(): number[] {
    const kinds = this.lineKinds();
    return this.referenceLines().flatMap((row, i) => (kinds.includes(row.kind) && !(row.kind === 'value' && row.value === null) ? [i] : []));
  }

  /** The scale to save: the values that are in it, in order. */
  private scaleSteps(): LikertStep[] {
    return this.scaleItems()
      .filter((item): item is ScaleItem & { side: LikertSide } => item.side !== 'out')
      .map((item) => ({ value: item.value, side: item.side }));
  }

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

  /** The first rule a Likert scale breaks (design D2), as the form says it, or null. */
  private scaleProblem(scale: readonly LikertStep[], t: (key: string, params?: Record<string, unknown>) => string): string | null {
    if (scale.length < minLikertValues || scale.length > maxLikertValues) {
      return t('indicators.dialog.errors.scaleCount', { min: minLikertValues, max: maxLikertValues });
    }
    if (scale.some((step) => !step.value.trim())) {
      return t('indicators.dialog.errors.scaleEmpty');
    }
    if (new Set(scale.map((step) => step.value)).size !== scale.length) {
      return t('indicators.dialog.errors.scaleDuplicate');
    }
    if (scale.some((step, i) => i > 0 && sideRank[step.side] < sideRank[scale[i - 1].side])) {
      return t('indicators.dialog.errors.scaleOrder');
    }
    if (scale.filter((step) => step.side === 'neutral').length > 1) {
      return t('indicators.dialog.errors.scaleNeutral');
    }
    if (['negative', 'positive'].some((side) => scale.filter((step) => step.side === side).length > maxLikertSide)) {
      return t('indicators.dialog.errors.scaleSide', { max: maxLikertSide });
    }
    return null;
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
    if (needsSeries(type) && !config.seriesBy) {
      problems.seriesBy = t('indicators.dialog.errors.seriesBy');
    } else if (hasSeries(type) && config.seriesBy && config.seriesBy === config.groupBy) {
      problems.seriesBy = t('indicators.dialog.errors.seriesSame');
    }
    if (type === 'likert' && config.ratio && !problems.ratio) {
      problems.ratio = t('indicators.dialog.errors.likertRatio');
    }
    if (type === 'pyramid') {
      const { left, right } = { left: this.pyramidLeft(), right: this.pyramidRight() };
      if (!left.trim()) {
        problems.pyramidLeft = t('indicators.dialog.errors.pyramidLeft');
      }
      if (!right.trim()) {
        problems.pyramidRight = t('indicators.dialog.errors.pyramidRight');
      } else if (left === right) {
        problems.pyramidRight = t('indicators.dialog.errors.pyramidSame');
      }
    }
    if (type === 'likert') {
      const scale = this.scaleProblem(config.scale ?? [], t);
      if (scale) {
        problems.scale = scale;
      }
    }
    if (this.minNChoice() === 'own' && this.ownMinN() === null) {
      problems.minN = t('indicators.dialog.errors.minN', { max: this.maxMinNText });
    }
    // A fixed value needs its number. The preview leaves the line out meanwhile (see `previewConfig`).
    const kinds = this.lineKinds();
    const unreadable = this.referenceLines().filter((row) => row.kind === 'value' && row.value === null && kinds.includes('value'));
    if (unreadable.length) {
      const ambiguous = unreadable.find((row) => !!row.typed && isAmbiguousSeparator(row.typed.trim().replace(/%$/, ''), this.valueIsPercent()));
      problems.referenceLines = ambiguous ? t('indicators.dialog.referenceLines.errors.separator', { text: ambiguous.typed!.trim() }) : t('indicators.dialog.referenceLines.errors.value');
    }
    return problems;
  });

  /** Problems are shown once Save was tried, and the server's refusals as they came. */
  protected readonly shown = computed<Problems>(() => ({ ...(this.submitted() ? this.problems() : {}), ...this.serverFieldErrors() }));

  /** The query to preview; null while the query part of the draft is incomplete. */
  protected readonly previewConfig = computed<WidgetConfig | null>(() => {
    // A threshold is no part of the query: a draft with a wrong one is still previewed, without it.
    const { title: _title, referenceLines: _lines, minN: _minN, ...query } = this.problems();
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
    // A pyramid's sides and a Likert scale are values of the series column. Their own query has no sides and no scale yet, so it
    // cannot say which values there are; the same rows as a heat map's columns can, whatever the sides and the scale are (design D7).
    toObservable(this.seriesDomainKey)
      .pipe(
        switchMap((key) =>
          key === null
            ? of<string[]>([])
            : timer(previewDelayMs).pipe(
                switchMap(() => this.service.query(this.data.projectId, JSON.parse(key) as WidgetConfig, 'heatmap')),
                map((result) =>
                  (result.series ?? [])
                    .filter((entry) => entry.kind === 'value' && entry.value !== null)
                    .map((entry) => entry.value as string)
                    .sort(naturalCompare),
                ),
                catchError(() => of<string[]>([])),
              ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((values) => this.offerValues(values));
  }

  /** The query that lists the values of the series column: the widget's own rows, as a heat map. Null while the widget cannot ask. */
  private readonly seriesDomainKey = computed(() => {
    const type = this.chartType();
    const config = this.formConfig();
    if ((type !== 'pyramid' && type !== 'likert') || !this.catalog()?.published || !config.indicator || !config.groupBy || !config.seriesBy || config.seriesBy === config.groupBy) {
      return null;
    }
    if (config.ratio && (!config.ratio.numerator || !config.ratio.denominator)) {
      return null;
    }
    return JSON.stringify(queryFor('heatmap', config));
  });

  /** The values of the data arrive: they are offered to the pyramid, and the scale lists those it does not hold yet, left out. */
  private offerValues(values: string[]): void {
    this.seriesValues.set(values);
    this.scaleItems.update((items) => {
      const held = new Set(items.map((item) => item.value));
      const added = values.filter((value) => !held.has(value)).map((value): ScaleItem => ({ value, side: 'out' }));
      return added.length ? [...items, ...added] : items;
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

  protected setIgnored(column: string, ignore: boolean): void {
    this.ignored.update((list) => (ignore ? (list.includes(column) ? list : [...list, column]) : list.filter((c) => c !== column)));
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
    if (id !== 'widgetDashboard' && id !== 'widgetFilterColumn' && id !== 'widgetScaleNew') {
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
    // Stacked bars add their values, so a ratio goes with the grouped layout only.
    if (value === 'ratio') {
      this.layout.set(this.layoutUnset ? null : 'grouped');
    }
    this.serverFieldErrors.set({});
  }

  protected setSeriesBy(value: string): void {
    if (value === this.seriesBy()) {
      return;
    }
    this.seriesBy.set(value);
    // The sides of a pyramid and the values of a scale belong to the series column: another column has other values.
    this.pyramidLeft.set('');
    this.pyramidRight.set('');
    this.scaleItems.set([]);
    this.seriesValues.set([]);
    this.serverFieldErrors.set({});
    this.scaleItemProblems.set({});
  }

  protected setLayout(option: SeriesLayout): void {
    this.layout.set(option === 'grouped' && this.layoutUnset ? null : option);
    this.serverFieldErrors.update(({ layout: _layout, ...others }) => others);
  }

  /**
   * The widget's color. Color 1 is the default: picking it on a widget saved without a color leaves it without one, so trying a color
   * and coming back saves what was saved. The preview redraws from the draft; the query does not change, so nothing is asked again.
   */
  protected setColor(n: number): void {
    this.color.set(n === 1 && this.start.display?.color == null ? null : n);
    this.serverFieldErrors.update(({ color: _color, ...others }) => others);
  }

  /**
   * The widget's orientation. Vertical is the default: picking it on a widget saved without one leaves it without one, as color 1 does,
   * so a form saved unchanged saves what was saved. The preview redraws from the draft; the query does not change.
   */
  protected setOrientation(orientation: BoxOrientation): void {
    this.orientation.set(orientation === 'vertical' && this.start.display?.orientation == null ? null : orientation);
    this.serverFieldErrors.update(({ orientation: _orientation, ...others }) => others);
  }

  /**
   * Steps or straight segments. Straight is the default: picking it on a widget saved without a choice leaves it without one, as color 1
   * does, so a form saved unchanged sends what it read. A display setting, so the preview redraws and nothing is asked again.
   */
  protected setInterpolation(value: LineInterpolation): void {
    this.interpolation.set(value === 'linear' && this.start.display?.interpolation == null ? null : value);
    this.serverFieldErrors.update(({ interpolation: _interpolation, ...others }) => others);
  }

  private defaultOrderOf(column: string): 'indicator' | 'chart' {
    const meta = column ? this.entry()?.columns?.[column] : undefined;
    return meta?.order?.length ? 'indicator' : 'chart';
  }

  /** Chooses the default order again (null) or an order of the widget's own, which starts as the order the preview draws now. */
  protected chooseOrder(place: 'group' | 'series', own: boolean): void {
    const current = (place === 'group' ? this.valuesNow()?.groups : this.valuesNow()?.series) ?? [];
    this.setOrder(place, own ? [...current] : null);
  }

  protected setOrder(place: 'group' | 'series', values: string[] | null): void {
    this.edited = true;
    (place === 'group' ? this.groupOrder : this.seriesOrder).set(values);
    this.serverFieldErrors.update(({ groupOrder, seriesOrder, ...others }) => (place === 'group' ? { ...others, ...(seriesOrder ? { seriesOrder } : {}) } : { ...others, ...(groupOrder ? { groupOrder } : {}) }));
  }

  /** Data labels. Off is the default: unchecking on a widget saved without the setting leaves it without one. */
  protected setDataLabels(on: boolean): void {
    this.dataLabels.set(on ? true : this.start.display?.dataLabels == null ? null : false);
    this.serverFieldErrors.update(({ dataLabels: _dataLabels, ...others }) => others);
  }

  /** The statistic of a stat tile. The default stores none; a statistic changes the rows, so the preview asks again. */
  protected setStatistic(value: string): void {
    this.statistic.set((statStatistics as readonly string[]).includes(value) ? (value as StatStatistic) : '');
    this.serverFieldErrors.update(({ statistic: _statistic, ...others }) => others);
  }

  /** A line under the figure of a stat tile: the lines are written in the fixed order, `n` first. Checking `n` asks for nothing; the other line does. */
  protected setSecondary(line: StatSecondaryLine, on: boolean): void {
    this.secondary.update((held) => statSecondaryLines.filter((candidate) => (candidate === line ? on : held.includes(candidate))));
    this.serverFieldErrors.update(({ secondary: _secondary, ...others }) => others);
  }

  /** The base n line: as the dashboard says, or shown or hidden for this widget. A display setting: the preview redraws and nothing is asked again. */
  protected setShowN(value: string): void {
    this.showNChoice.set(value === 'show' || value === 'hide' ? value : 'dashboard');
    this.serverFieldErrors.update(({ showN: _showN, ...others }) => others);
  }

  /** The threshold: as the dashboard says, off for this widget or its own. A number is proposed when its own is chosen with none typed. */
  protected setMinNChoice(value: string): void {
    const choice: MinNChoice = value === 'off' || value === 'own' ? value : 'dashboard';
    this.minNChoice.set(choice);
    if (choice === 'own' && !this.minNText().trim()) {
      this.minNText.set(String(this.dashboardBases.minN ?? 30));
    }
    this.serverFieldErrors.update(({ minN: _minN, ...others }) => others);
  }

  protected setMinNText(value: string): void {
    this.minNText.set(value);
    this.serverFieldErrors.update(({ minN: _minN, ...others }) => others);
  }

  /** Leaving out the values that mean no data. Off is the default: unticking on a widget saved without the setting leaves it without one. */
  protected setMissingFooter(on: boolean): void {
    this.missingFooter.set(on ? true : this.start.missingFooter == null ? null : false);
    this.serverFieldErrors.update(({ missingFooter: _missing, ...others }) => others);
  }

  /** What the option to leave out values names: the values the indicator declares as no data for the widget's group and series columns, or that it declares none. */
  protected missingHelp(t: (key: string, params?: Record<string, unknown>) => string): string {
    const declared = noDataValuesOf(this.entry()?.columns);
    const columns = [this.groupBy(), hasSeries(this.chartType()) ? this.seriesBy() : ''].filter((column, i, all) => column && all.indexOf(column) === i);
    const named = columns.filter((column) => declared[column]).map((column) => `${column}: ${declared[column].join(', ')}`);
    return named.length ? t('indicators.dialog.bases.missing.named', { values: named.join('; ') }) : t('indicators.dialog.bases.missing.none');
  }

  /** The kinds row `index` offers: those the widget takes (each of mean, median and overall once), and the row's own kind so its select shows it. */
  protected kindOptions(index: number): ReferenceLineKind[] {
    const rows = this.referenceLines();
    const taken = new Set(rows.filter((_, i) => i !== index).map((row) => row.kind));
    const own = rows[index]?.kind;
    const offered = this.offeredKinds().filter((kind) => kind === 'value' || !taken.has(kind));
    return own && !offered.includes(own) ? [own, ...offered] : offered;
  }

  /** Adds a line of the first kind the widget takes that no line has, else a fixed value. At most `maxReferenceLines`. */
  protected addLine(): void {
    if (this.referenceLines().length >= maxReferenceLines) {
      return;
    }
    this.edited = true;
    const used = new Set(this.referenceLines().map((row) => row.kind));
    const kind = this.offeredKinds().find((candidate) => candidate !== 'value' && !used.has(candidate)) ?? 'value';
    this.referenceLines.update((rows) => [...rows, { kind, value: null, typed: null, label: '' }]);
    this.clearLineProblems();
    // The new row is drawn after this: the keyboard goes to its kind select.
    this.focusLine(this.referenceLines().length - 1);
  }

  protected removeLine(index: number): void {
    this.edited = true;
    this.referenceLines.update((rows) => rows.filter((_, i) => i !== index));
    this.clearLineProblems();
    // The button that was pressed is gone with its row: the keyboard goes to the kind select of the line that took its place, or of the one
    // before it, and with no line left to the button that adds one.
    this.focusLine(Math.min(index, this.referenceLines().length - 1));
  }

  /** Once the lines are drawn again, puts the keyboard on the kind select of row `index`, or on the button that adds a line when there is none. */
  private focusLine(index: number): void {
    afterNextRender(
      () => {
        const select = index >= 0 ? this.host.nativeElement.querySelector<HTMLElement>(`#referenceKind${index}`) : null;
        (select ?? this.host.nativeElement.querySelector<HTMLElement>('[data-testid="reference-add"]'))?.focus();
      },
      { injector: this.injector },
    );
  }

  protected setLineKind(index: number, kind: string): void {
    this.updateLine(index, { kind: kind as ReferenceLineKind });
  }

  protected setLineLabel(index: number, label: string): void {
    this.updateLine(index, { label });
  }

  /** The number typed for a fixed value; read in the units of the indicator, so a percent typed as 50 is saved as 0.5. */
  protected setLineValue(index: number, typed: string): void {
    const percent = this.valueIsPercent();
    this.updateLine(index, { typed, typedPercent: percent, value: textValue(typed, percent) });
  }

  private updateLine(index: number, change: Partial<LineRow>): void {
    this.referenceLines.update((rows) => rows.map((row, i) => (i === index ? { ...row, ...change } : row)));
    this.lineProblems.update(({ [index]: _own, ...others }) => others);
    this.serverFieldErrors.update(({ referenceLines: _lines, ...others }) => others);
  }

  private clearLineProblems(): void {
    this.lineProblems.set({});
    this.serverFieldErrors.update(({ referenceLines: _lines, ...others }) => others);
  }

  /** The text of a row's value input: what was typed, else the saved number in the units the input takes. */
  protected valueOf(row: LineRow): string {
    const percent = this.valueIsPercent();
    // What was typed, while the input still has the units it was typed in; else the number it came to, in the units now in force. A text that
    // came to no number (value null) has nothing to convert, so it stays.
    return row.typed !== null && (row.typedPercent === percent || row.value === null) ? row.typed : row.value === null ? '' : valueText(row.value, percent);
  }

  /** Whether row `index` is a fixed value with no usable number, once Save was tried: the preview leaves it out and the form does not save. */
  protected valueInvalid(index: number): boolean {
    const row = this.referenceLines()[index];
    return !!row && row.kind === 'value' && row.value === null && this.lineKinds().includes('value') && !!this.shown().referenceLines;
  }

  /** The ids of the texts that describe a row's kind select: the help of its kind, the caution for an overall line and the server's refusal. */
  protected kindDescribedBy(index: number): string {
    const row = this.referenceLines()[index];
    const ids = [`referenceHelp${index}`, ...(row?.kind === 'overall' ? [`referenceCaution${index}`] : []), ...(this.lineProblems()[index]?.kind ? [`referenceKind${index}-error`] : [])];
    return ids.join(' ');
  }

  /** The ids of the texts that describe a row's value input: its unit, then the refusal of the server or of the form. */
  protected valueDescribedBy(index: number): string | null {
    const ids = [
      ...(this.valueUnit() ? [`referenceValue${index}-unit`] : []),
      ...(this.lineProblems()[index]?.value ? [`referenceValue${index}-error`] : []),
      ...(this.valueInvalid(index) ? ['widgetLines-error'] : []),
    ];
    return ids.length ? ids.join(' ') : null;
  }

  /** Whether the server refused part of row `index`, or the form can tell its value is wrong. */
  protected valueFailed(index: number): boolean {
    return !!this.lineProblems()[index]?.value || this.valueInvalid(index);
  }

  /**
   * Why the form holds lines the widget does not take (design D8): they stay here, and come back when the chart type, the series, the
   * layout or the plot changes back, but they are not saved. Null while nothing is dropped.
   */
  protected readonly droppedNote = computed<'type' | 'layout' | 'measure' | 'series' | null>(() => {
    const dropped = this.referenceLines().filter((row) => !this.lineKinds().includes(row.kind));
    if (!dropped.length) {
      return null;
    }
    if (!this.offersLines()) {
      return 'type';
    }
    const rules = this.annotationRules();
    if (rules.layout === 'stacked' || rules.layout === 'percent') {
      return 'layout';
    }
    return dropped.every((row) => row.kind === 'overall') ? 'measure' : 'series';
  });
  /** A data labels setting turned on that the widget no longer takes: stacked and 100% bars have none. */
  protected readonly droppedLabels = computed(() => this.dataLabels() === true && !this.offersDataLabels() && (this.chartType() === 'bar' || this.chartType() === 'column'));

  /** The name the member types for a column. An emptied field is no name. */
  protected setLabel(column: string, label: string): void {
    this.labelsTyped.set(true);
    this.labels.update((labels) => ({ ...labels, [column]: label }));
    this.labelProblems.update(({ [column]: _own, ...others }) => others);
    this.serverFieldErrors.update(({ labels: _labels, ...others }) => others);
  }

  /** The widget's scale; null is the palette's own. */
  protected setRamp(scale: ScaleId | null): void {
    this.ramp.set(scale);
    this.serverFieldErrors.update(({ ramp: _ramp, ...others }) => others);
  }

  /** The CSS value of the n-th color of the dashboard's palette: the same custom property the charts read. */
  protected swatch(n: number): string {
    return paletteColor(this.palette, n);
  }

  /** A scale's steps, lightest to darkest; the palette's own (null) shows the scale it stands for. */
  protected gradient(scale: ScaleId | null): string {
    return rampGradient(scale ?? this.paletteScale);
  }

  /** Stacked and 100% add the values of a bar, so they cannot plot a ratio. */
  protected layoutDisabled(option: SeriesLayout): boolean {
    return this.mode() === 'ratio' && option !== 'grouped';
  }

  protected setPyramid(side: 'left' | 'right', value: string): void {
    (side === 'left' ? this.pyramidLeft : this.pyramidRight).set(value);
    this.serverFieldErrors.update(({ pyramidLeft: _left, pyramidRight: _right, ...others }) => others);
  }

  protected setScaleValue(index: number, value: string): void {
    this.updateScale(index, { value });
  }

  protected setScaleSide(index: number, side: string): void {
    this.updateScale(index, { side: scaleSides.includes(side as LikertSide | 'out') ? (side as LikertSide | 'out') : 'out' });
  }

  private updateScale(index: number, change: Partial<ScaleItem>): void {
    this.scaleItems.update((items) => items.map((item, i) => (i === index ? { ...item, ...change } : item)));
    this.clearScaleProblems();
  }

  protected moveScale(index: number, by: -1 | 1): void {
    this.edited = true;
    const to = index + by;
    this.scaleItems.update((items) => {
      if (to < 0 || to >= items.length) {
        return items;
      }
      const moved = [...items];
      [moved[index], moved[to]] = [moved[to], moved[index]];
      return moved;
    });
    this.clearScaleProblems();
    // The rows are redrawn in place, so the keyboard would stay on the row and act on whatever value moved into it: it follows the
    // value instead, on the same button, or on the other one when the value reached an end and that button is off.
    if (to >= 0 && to < this.scaleItems().length) {
      this.focusScale(to, by > 0 ? ['scale-down', 'scale-up'] : ['scale-up', 'scale-down']);
    }
  }

  protected removeScale(index: number): void {
    this.edited = true;
    this.scaleItems.update((items) => items.filter((_, i) => i !== index));
    this.clearScaleProblems();
    // The button that was pressed is gone with its row: the keyboard goes to the remove button of the value that took its place, or of
    // the one before it, and with no value left to the field that adds one.
    const last = this.scaleItems().length - 1;
    this.focusScale(Math.min(index, last), ['scale-remove']);
  }

  /** Once the editor is drawn again, puts the keyboard on the first enabled control of `testIds` in row `index`, or on the field that adds a value. */
  private focusScale(index: number, testIds: readonly string[]): void {
    afterNextRender(
      () => {
        const row = index >= 0 ? this.host.nativeElement.querySelectorAll('[data-testid="scale-item"]')[index] : undefined;
        for (const id of testIds) {
          const control = row?.querySelector<HTMLButtonElement>(`[data-testid="${id}"]`);
          if (control && !control.disabled) {
            control.focus();
            return;
          }
        }
        this.host.nativeElement.querySelector<HTMLElement>('#widgetScaleNew')?.focus();
      },
      { injector: this.injector },
    );
  }

  /** A value typed by hand, for one the data does not show (a dashboard filter can hide it, a script can change it later). Left out until marked. */
  protected addScale(): void {
    const value = this.scaleNew();
    if (value.trim() && !this.scaleItems().some((item) => item.value === value)) {
      this.edited = true;
      this.scaleItems.update((items) => [...items, { value, side: 'out' }]);
      this.clearScaleProblems();
    }
    this.scaleNew.set('');
  }

  private clearScaleProblems(): void {
    this.scaleItemProblems.set({});
    this.serverFieldErrors.update(({ scale: _scale, ...others }) => others);
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
    this.seriesBy.set('');
    this.layout.set(this.layoutUnset ? null : 'grouped');
    this.pyramidLeft.set('');
    this.pyramidRight.set('');
    this.scaleItems.set([]);
    this.seriesValues.set([]);
    this.fresh = true;
    this.serverFieldErrors.set({});
    this.itemProblems.set({});
    this.scaleItemProblems.set({});
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

  /** Types into one of the widget's texts. The refusal of that field goes with the change. */
  protected setText(field: 'subtitle' | 'footnote' | 'unit' | 'captionText', value: string): void {
    (field === 'subtitle' ? this.subtitle : field === 'footnote' ? this.footnote : field === 'unit' ? this.unit : this.captionText).set(value);
    this.serverFieldErrors.update(({ [field]: _own, ...others }) => others);
  }

  /** The caption's mode: the default line, an own text or none. The text typed is kept while the mode is another. */
  protected setCaptionMode(mode: CaptionMode): void {
    this.captionMode.set(mode);
    this.serverFieldErrors.update(({ caption: _caption, captionText: _text, ...others }) => others);
  }

  /** What the unit field says about the indicator's unit, which a widget's own replaces. */
  protected unitHint(t: (key: string, params?: Record<string, unknown>) => string): string {
    const unit = this.entry()?.unit?.trim();
    return unit ? t('indicators.dialog.context.unit.hint', { unit }) : t('indicators.dialog.context.unit.hintNone');
  }

  /**
   * The line an own caption text makes (design D11), in the words of the chart that draws it: "{text} by {group}". A chart with a series
   * names it in its legend and a pyramid, Likert or box plot draws no line, so those have none to show.
   */
  protected captionHint(t: (key: string, params?: Record<string, unknown>) => string): string | null {
    const what = singleLine(this.captionText());
    if (!what) {
      return null;
    }
    const type = this.chartType();
    const names = columnLabels(this.entry(), this.draft().display);
    const group = this.groupBy() ? names(this.groupBy()) : t('indicators.chart.noGroup');
    let line: string | null = null;
    if (type === 'stat') {
      line = what;
    } else if (type === 'histogram') {
      line = t('indicators.chart.captionHistogram', { what, buckets: this.buckets().trim() || defaultBuckets });
    } else if (type === 'pie' || type === 'map' || ((type === 'bar' || type === 'column' || type === 'line') && !this.seriesBy())) {
      line = t(type === 'map' ? 'indicators.map.caption' : 'indicators.chart.caption', { what, group });
    } else if ((type === 'heatmap' || type === 'crosstab') && this.seriesBy()) {
      line = t('indicators.series.caption', { what, group, series: names(this.seriesBy()) });
    }
    return line ? t('indicators.dialog.context.caption.line', { line }) : null;
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
    this.scaleItemProblems.set({});
    this.labelProblems.set({});
    this.lineProblems.set({});
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
        const steps: Record<number, string> = {};
        const named: Record<string, string> = {};
        const lines: Record<number, LineProblems> = {};
        // The scale the server read holds only the values that are in it; its `scale[2]` is the third of those, not of the editor.
        const inScale = this.scaleItems().flatMap((item, i) => (item.side === 'out' ? [] : [i]));
        // Likewise the lines: the server read the rows the widget takes, so `referenceLines[1]` is the second of those.
        const keptRows = this.keptRows();
        for (const [path, message] of Object.entries(parsed.fieldErrors)) {
          const step = this.stepFor(path);
          if (step !== null) {
            const at = inScale[step] ?? step;
            steps[at] = steps[at] ?? message;
            continue;
          }
          const label = this.labelFor(path);
          if (label) {
            named[label] = named[label] ?? message;
            continue;
          }
          const line = this.lineFor(path);
          if (line) {
            const at = keptRows[line.index] ?? line.index;
            lines[at] = { ...lines[at], [line.part]: lines[at]?.[line.part] ?? message };
            continue;
          }
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
        this.scaleItemProblems.set(steps);
        this.labelProblems.set(named);
        this.lineProblems.set(lines);
        const others = Object.entries(parsed.fieldErrors).filter(([path]) => this.stepFor(path) === null && !this.labelFor(path) && !this.lineFor(path) && !this.itemFor(path) && !this.fieldFor(path));
        const shownOnFields = Object.keys(fields).length > 0 || Object.keys(steps).length > 0 || Object.keys(named).length > 0 || Object.keys(lines).length > 0;
        this.error.set(shownOnFields && !others.length ? null : others.length ? markApiText(others.map(([, message]) => message).join(' ')) : parsed.message);
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

  /** `config.display.referenceLines[1].label`: the line and the part of it the server refused. A line with no part is refused as a whole (a null one). */
  private lineFor(path: string): { index: number; part: keyof LineProblems } | null {
    const match = /display\.referencelines\[(\d+)\](?:\.(\w+))?$/i.exec(path);
    if (!match) {
      return null;
    }
    const part = match[2]?.toLowerCase();
    return { index: Number(match[1]), part: part === 'label' || part === 'value' ? part : 'kind' };
  }

  /** `config.display.labels.numerator`: the column whose name the server refused. The refusal of the labels as a whole has none. */
  private labelFor(path: string): string | null {
    const match = /display\.labels\.(.+)$/i.exec(path);
    return match ? match[1] : null;
  }

  /** `config.scale[2]`: the place of the value of the scale the server refused. A refusal of the scale as a whole has none. */
  private stepFor(path: string): number | null {
    const match = /scale\[(\d+)\]/i.exec(path);
    return match ? Number(match[1]) : null;
  }

  private fieldFor(path: string): keyof Problems | null {
    const name = path.toLowerCase();
    if (name === 'dashboardid') return 'dashboardId';
    // First, because the name of a column in the path can hold any of the words below.
    if (name.includes('display.order.group')) return 'groupOrder';
    if (name.includes('display.order.series')) return 'seriesOrder';
    if (name.includes('display.labels')) return 'labels';
    if (name.includes('display.secondary')) return 'secondary';
    if (name.includes('config.statistic') || name === 'statistic') return 'statistic';
    if (name.includes('display.minn')) return 'minN';
    if (name.includes('display.shown')) return 'showN';
    if (name.includes('missingfooter')) return 'missingFooter';
    if (name.includes('display.referencelines')) return 'referenceLines';
    if (name.includes('display.interpolation')) return 'interpolation';
    if (name.includes('display.datalabels')) return 'dataLabels';
    if (name.includes('display.orientation')) return 'orientation';
    if (name.includes('display.color')) return 'color';
    if (name.includes('display.ramp')) return 'ramp';
    // Before the title: `display.subtitle` holds `title`.
    if (name.includes('display.subtitle')) return 'subtitle';
    if (name.includes('display.footnote')) return 'footnote';
    if (name.includes('display.caption.text')) return 'captionText';
    if (name.includes('display.caption')) return 'caption';
    if (name.includes('display.unit')) return 'unit';
    if (name.includes('seriesby')) return 'seriesBy';
    if (name.includes('layout')) return 'layout';
    if (name.includes('pyramid.left')) return 'pyramidLeft';
    if (name.includes('pyramid')) return 'pyramidRight';
    if (name.includes('scale')) return 'scale';
    if (name.includes('ignoreddashboardfilters')) return 'dashboardFilters';
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
