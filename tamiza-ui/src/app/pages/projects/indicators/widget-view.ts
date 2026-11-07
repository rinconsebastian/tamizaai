import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, ElementRef, LOCALE_ID, computed, effect, inject, input, model, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { catchError, distinctUntilChanged, map, of, switchMap, tap, timer } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import {
  AppliedDashboardFilter,
  ChartType,
  DashboardBases,
  DashboardColors,
  IndicatorEntry,
  IndicatorsService,
  QueryResult,
  WidgetCaption,
  WidgetCategoryOrder,
  WidgetConfig,
  ReferenceLineKind,
  WidgetDisplay,
  WidgetReferenceLine,
  WidgetSort,
  emptyConfig,
  hasSeries,
  takesMissingFooter,
  takesSeries,
} from '../../../core/projects/indicators.service';
import { Alert, ApiLang, Button, Skeleton } from '../../../shared/ui';
import { AxisChart, AxisKind } from './axis-chart';
import { ChartBases, SmallMark, answerColumnsOf, baseColumnOf, figureNames, isSmallBase, nLine, noBaseSettings, noDataLine, overOnAnswers, settingsInForce, smallBaseMarksText, statNLine } from './base-figures';
import { CategoryOrders, arrange, entryOf, ordersFor } from './category-order';
import { pivot } from './chart-data';
import { isSeriesAnswer, orderedSeriesTable } from './series-table';
import { BoxplotChart } from './boxplot-chart';
import { ChartColors, chartColors, defaultColors } from './chart-colors';
import { CrosstabWidget } from './crosstab-widget';
import { HeatmapChart } from './heatmap-chart';
import { LikertChart } from './likert-chart';
import { boxIndicator, captionInFoot, columnLabels, figureFormatter, named, ownCaption, plainIndicator, valueAxisTitle, valueFormatter, withWidgetUnit } from './format';
import { ActiveDashboardFilter, appliedOf } from './dashboard-filters';
import { DashboardPrint } from './dashboard-print';
import { WidgetExportHost } from './export-parts';
import { MapPicture } from './map-engine';
import { MapReadiness, MapWidget } from './map-widget';
import { PieWidget } from './pie-widget';
import { PyramidChart } from './pyramid-chart';
import { StatWidget, statDescription } from './stat-widget';
import { FootItem, WidgetFoot } from './widget-foot';

/**
 * Whether the widget can be exported now (add-widget-export, design D3). `null` means the format is available; a string is the reason
 * it is not, already translated, for the menu to show under the format. `imageNote` is a line the available image formats carry.
 */
export interface ExportState {
  image: string | null;
  workbook: string | null;
  imageNote: string | null;
}

type State = { kind: 'idle' } | { kind: 'loading' } | { kind: 'error'; message: string } | { kind: 'ready'; result: QueryResult };

/** Plotting the value is always by number, and which column it is makes no sense then: those are not sent. */
function sortFor(sort: WidgetSort): WidgetSort {
  return sort.by === 'value' ? { ...sort, column: null, compareAs: null } : sort;
}

/**
 * The settings of the second dimension a chart type uses (design D1, D7): the series column of the kinds that take or need one, the
 * layout of bars and columns, a pyramid's sides, a Likert widget's scale. A key with no value is left out, not sent as null, so a
 * widget without a series queries and saves exactly what it did before.
 */
function seriesSettings(type: ChartType, config: WidgetConfig): Partial<WidgetConfig> {
  return {
    ...(hasSeries(type) && config.seriesBy ? { seriesBy: config.seriesBy } : {}),
    ...((type === 'bar' || type === 'column') && config.layout ? { layout: config.layout } : {}),
    ...(type === 'pyramid' && config.pyramid ? { pyramid: config.pyramid } : {}),
    ...(type === 'likert' && config.scale?.length ? { scale: config.scale } : {}),
  };
}

/**
 * The widget's own color and scale (add-dashboard-palettes, design D3), for the kinds that take them and with only the fields each
 * takes: a color for a bar, column or line chart with no series, for a histogram and for a box plot, a scale for a map and a heat map,
 * an orientation for a box plot, and, on every kind, the widget's own column labels (add-indicator-column-metadata, design D5: a chart
 * type change keeps them) and its texts, subtitle, footnote, caption, unit and description button (add-widget-context, design D1: a
 * chart type change keeps them too, and none of them is part of the query). Then the chart annotations (add-chart-annotations, design D2):
 * the reference lines the kind, the series and the layout take, steps for a line, and data labels for a bar, column, line or pie (a stacked
 * or 100% layout takes none). A widget with nothing to keep has no `display` at all, not an empty one, so a widget saved before palettes,
 * labels, texts, annotations or base settings saves exactly what it did.
 */
function displayFor(type: ChartType, config: WidgetConfig): { display?: WidgetDisplay } {
  const { color, ramp, orientation, labels, subtitle, footnote, caption, unit, showDescription, referenceLines, interpolation, dataLabels, order, minN, showN, secondary } = config.display ?? {};
  const named = labels ? Object.entries(labels).filter(([, label]) => label.trim()) : [];
  const captionText = caption?.text?.trim() ? caption.text : null;
  // A caption keeps `hidden: false` (improve-widget-text-hierarchy, design D2): it keeps the line in sight under a subtitle. A text with
  // `hidden: true` is still saved as the text alone, as the form never offers both.
  const captionKept: WidgetCaption | null = captionText
    ? { text: captionText, ...(caption?.hidden === false ? { hidden: false } : {}) }
    : caption?.hidden === true
      ? { hidden: true }
      : caption?.hidden === false
        ? { hidden: false }
        : null;
  const lines = referenceLinesOf(type, config);
  // Data labels: a saved `false` is kept as it is on the kinds that take them; `true` is not taken by a stacked or 100% layout.
  const layout = layoutOf(type, config);
  const dataLabelsKept = typeof dataLabels === 'boolean' && (type === 'bar' || type === 'column' || type === 'line' || type === 'pie') && !(dataLabels && (layout === 'stacked' || layout === 'percent'));
  const kept: WidgetDisplay = {
    ...((type === 'histogram' || type === 'boxplot' || (takesSeries(type) && !config.seriesBy)) && color != null ? { color } : {}),
    ...((type === 'map' || type === 'heatmap') && ramp ? { ramp } : {}),
    ...(type === 'boxplot' && orientation ? { orientation } : {}),
    ...(named.length ? { labels: Object.fromEntries(named) } : {}),
    ...(subtitle?.trim() ? { subtitle } : {}),
    ...(footnote?.trim() ? { footnote } : {}),
    ...(captionKept ? { caption: captionKept } : {}),
    ...(unit?.trim() ? { unit } : {}),
    ...(showDescription === true ? { showDescription } : {}),
    ...(lines.length ? { referenceLines: lines } : {}),
    ...(type === 'line' && interpolation ? { interpolation } : {}),
    ...(dataLabelsKept ? { dataLabels } : {}),
    ...orderKept(type, config, order),
    // The base settings (add-small-sample-warnings, design D7) are kept on every kind, a 0 and a false as saved: a chart-type change never drops them.
    ...(typeof minN === 'number' ? { minN } : {}),
    ...(typeof showN === 'boolean' ? { showN } : {}),
    // A stat tile's lines under its figure (improve-stat-cards, design D3), in the order saved; no other kind has them.
    ...(type === 'stat' && secondary?.length ? { secondary } : {}),
  };
  return Object.keys(kept).length ? { display: kept } : {};
}

/**
 * A widget's own order of values that the kind takes (add-declared-category-order, design D7): the order of groups on every kind but a
 * stat tile, a histogram and a map, when the widget has a group column and no sort (or order by median) decides it; the order of series on
 * a bar, column, line, heat map or cross table that has a series column. A change of kind, a cleared column or a new sort drops them, as a
 * change of kind drops an orientation, so the form never saves what the API refuses.
 */
function orderKept(type: ChartType, config: WidgetConfig, order: WidgetCategoryOrder | undefined): { order?: WidgetCategoryOrder } {
  const groupTakes = type !== 'stat' && type !== 'histogram' && type !== 'map' && !!config.groupBy && !sortOf(type, config);
  const seriesTakes = (type === 'bar' || type === 'column' || type === 'line' || type === 'heatmap' || type === 'crosstab') && !!config.seriesBy;
  const kept: WidgetCategoryOrder = {
    ...(groupTakes && order?.group?.length ? { group: order.group } : {}),
    ...(seriesTakes && order?.series?.length ? { series: order.series } : {}),
  };
  return kept.group || kept.series ? { order: kept } : {};
}

/** The group and series values a widget draws, in order, in full text. */
export interface ShownValues {
  groups: string[];
  series: string[];
}

/**
 * The values a chart of `type` draws for an answer, in the order it draws them (add-declared-category-order, design D8): the groups of
 * every kind that has groups and the series of a bar, column, line, heat map and cross table. A pie draws from the largest unless a
 * declared order applies; a stat tile, a histogram and a map have no groups to list.
 */
export function shownValuesOf(type: ChartType, answer: QueryResult, config: WidgetConfig, orders: CategoryOrders | null): ShownValues {
  const texts = (values: (string | null)[]) => values.filter((value): value is string => value !== null);
  if (type === 'stat' || type === 'histogram' || type === 'map') {
    return { groups: [], series: [] };
  }
  if (type === 'boxplot') {
    return { groups: texts(arrange(answer.boxes ?? [], (box) => entryOf(box.group), orders?.groups ?? null).map((box) => box.group)), series: [] };
  }
  if (hasSeries(type) && isSeriesAnswer(answer)) {
    const table = orderedSeriesTable(answer, type, config.layout ?? null, orders);
    return { groups: texts(table.rows.map((row) => row.group)), series: texts(table.columns.map((column) => (column.kind === 'value' ? column.series : null))) };
  }
  if (type === 'pie') {
    const ranked = [...answer.rows].filter((row) => row.value > 0).sort((a, b) => b.value - a.value);
    return { groups: texts(arrange(ranked, (row) => entryOf(row.group), orders?.groups ?? null).map((row) => row.group)), series: [] };
  }
  return { groups: texts(pivot(answer.rows, '', type === 'line' ? 'natural' : 'listed', orders).groups), series: [] };
}

/**
 * The layout a bar or column with a series is drawn in; null for a chart with no series, which is always plain, and for a line, which has
 * none. Reference lines and data labels depend on it (add-chart-annotations, design D2).
 */
function layoutOf(type: ChartType, config: AnnotationRules): string | null {
  return (type === 'bar' || type === 'column') && config.seriesBy ? (config.layout ?? 'grouped') : null;
}

/** What `referenceKindsFor` and `offersDataLabels` read of a config: the settings the rules depend on. */
type AnnotationRules = Partial<Pick<WidgetConfig, 'groupBy' | 'seriesBy' | 'layout' | 'ratio'>>;

/**
 * The kinds of reference line a widget takes (add-chart-annotations, design D2, D8), in the order the form offers them: none outside a
 * bar, column or line; a mean or a median only with no series; an overall line only over a ratio; every kind but the fixed value is out
 * of a stacked or 100% layout. The form offers these and `savedConfig` keeps the lines of these kinds. Twin of the API's rules in
 * `IndicatorValidation.CheckDisplay`.
 *
 * With the indicator's declared answer columns (add-small-sample-warnings, design D16) a widget over one of them is not offered an overall
 * line; `savedConfig` passes none, so a line saved before the declaration is kept and nothing is refused.
 */
export function referenceKindsFor(type: ChartType, config: AnnotationRules, answerColumns: readonly string[] = []): ReferenceLineKind[] {
  if (type !== 'bar' && type !== 'column' && type !== 'line') {
    return [];
  }
  const layout = layoutOf(type, config);
  const fixedOnly = layout === 'stacked' || layout === 'percent';
  const overall = !!config.ratio && !fixedOnly && !overOnAnswers(config, answerColumns);
  return [...(!config.seriesBy && !fixedOnly ? (['mean', 'median'] as const) : []), ...(overall ? (['overall'] as const) : []), 'value'];
}

/** Whether the widget can show data labels (design D2): a bar, column, line or pie, but not in a stacked or 100% layout. */
export function offersDataLabels(type: ChartType, config: AnnotationRules): boolean {
  const layout = layoutOf(type, config);
  return (type === 'bar' || type === 'column' || type === 'line' || type === 'pie') && layout !== 'stacked' && layout !== 'percent';
}

/**
 * The reference lines a widget is saved with (add-chart-annotations, design D2, D8): only on a bar, column or line, and only the kinds
 * its series and layout take. A mean or a median needs a chart with no series; an overall line needs a ratio; a stacked or 100% layout
 * takes only fixed values. A line saves its kind, its value (only a `value` line has one; one without it is saved as it is, for the API
 * to refuse) and its label, in the order the API writes them, and a blank label is none. The form keeps what a change of kind or series
 * dropped; this is what is saved.
 */
function referenceLinesOf(type: ChartType, config: WidgetConfig): WidgetReferenceLine[] {
  const kinds = referenceKindsFor(type, config);
  return (config.display?.referenceLines ?? [])
    .filter((line) => kinds.includes(line.kind))
    .map((line) => ({
      kind: line.kind,
      ...(line.kind === 'value' && line.value != null ? { value: line.value } : {}),
      ...(line.label?.trim() ? { label: line.label } : {}),
    }));
}

/**
 * Whether a saved widget asks its query for the overall value too (add-chart-annotations, design D4): a bar, column or line with no
 * series and a ratio, whose display has an `overall` line. It is a flag of the query request, never part of a config: a widget with a
 * series reads its overall line from its totals, and a measure has no overall line. Twin of `WidgetQuery.Overall` in tamiza-api; change
 * both together.
 */
export function overallFor(type: ChartType, config: WidgetConfig): boolean {
  return (
    (type === 'bar' || type === 'column' || type === 'line') &&
    !config.seriesBy &&
    !!config.ratio &&
    !!config.display?.referenceLines?.some((line) => line?.kind === 'overall')
  );
}

/**
 * Whether a saved widget asks its query for its value without the dashboard filters too (improve-stat-cards, design D3): a stat tile whose
 * `display.secondary` holds `unfiltered`. It is a flag of the query request, never part of a config: `queryFor` builds the same query with
 * the line checked or not, and the `n` line alone asks for nothing. Twin of `WidgetQuery.Unfiltered` in tamiza-api; change both together.
 */
export function unfilteredFor(type: ChartType, config: WidgetConfig): boolean {
  return type === 'stat' && !!config.display?.secondary?.includes('unfiltered');
}

/**
 * The order a chart type sends. A bar or column keeps its order. A box plot keeps only an order by its median, and only with a group
 * column: the form keeps the choice while the group is cleared, but the query is not sent it, so it is not refused for it. Twin of
 * `SortOf` in `WidgetQuery` (tamiza-api).
 */
function sortOf(type: ChartType, config: WidgetConfig): WidgetSort | null {
  if ((type === 'bar' || type === 'column') && config.sort) {
    return sortFor(config.sort);
  }
  return type === 'boxplot' && config.groupBy && config.sort?.by === 'value' ? sortFor(config.sort) : null;
}

/**
 * The query a chart type actually uses: settings that belong to another chart type are not sent, and a stat tile never groups.
 * Twin of `WidgetQuery.For` in tamiza-api (scheduled dashboard emails and the MCP tools); change both together. The widget's
 * `display` (its color, scale and column labels) is not part of it, so choosing a color or a label never queries again. The option to leave
 * out the values that mean no data (`missingFooter`) is: it changes the rows.
 */
export function queryFor(type: ChartType, config: WidgetConfig): WidgetConfig {
  return {
    indicator: config.indicator,
    measure: config.ratio ? null : config.measure,
    ratio: config.ratio,
    groupBy: type === 'stat' ? null : config.groupBy,
    filters: config.filters,
    geometryColumn: type === 'map' ? config.geometryColumn : null,
    buckets: type === 'histogram' ? config.buckets : null,
    sort: sortOf(type, config),
    // The query needs the columns' names to fetch them; a label, a format and the basemap change nothing in the rows, so they stay out
    // of it (and out of the re-query key), and editing them never blanks the preview.
    // (A config from an API that predates the tooltip has neither field; it reads as none.)
    tooltip: type === 'map' ? (config.tooltip ?? []).map((item) => ({ column: item.column, label: null, format: null })) : [],
    basemap: null,
    // Which dashboard filters the widget ignores decides what the page sends with the query, not the query itself.
    ignoredDashboardFilters: [],
    ...seriesSettings(type, config),
    // Left out unless set, and only where the kind takes it, so the query of every widget without it is what it was (add-small-sample-warnings, design D6).
    ...(takesMissingFooter(type) && typeof config.missingFooter === 'boolean' ? { missingFooter: config.missingFooter } : {}),
    // The statistic of a stat tile (improve-stat-cards, design D2): left out unless set, and on no other kind, so a change of kind drops it.
    ...(type === 'stat' && config.statistic ? { statistic: config.statistic } : {}),
  };
}

/**
 * The axis chart that draws a chart type, or null for the kinds other components draw. Every kind is listed, with no default branch,
 * so a kind added later cannot fall into a column chart unseen (design D6).
 */
export function axisKindOf(type: ChartType): AxisKind | null {
  switch (type) {
    case 'bar':
    case 'column':
    case 'line':
    case 'histogram':
      return type;
    case 'stat':
    case 'pie':
    case 'map':
    case 'heatmap':
    case 'pyramid':
    case 'likert':
    case 'crosstab':
    case 'boxplot':
      return null;
  }
}

/**
 * What a widget is saved with, and what its preview is given: the query, plus (for a map) the whole tooltip items and the basemap,
 * the dashboard filters it ignores, and its color or scale where its kind takes one. Any other chart type keeps no tooltip, basemap or
 * display, so changing a map into one saves no tooltip columns, and a column chart that gains a series saves no color.
 */
export function savedConfig(type: ChartType, config: WidgetConfig): WidgetConfig {
  return {
    ...queryFor(type, config),
    tooltip: type === 'map' ? (config.tooltip ?? []) : [],
    basemap: type === 'map' ? (config.basemap ?? null) : null,
    ignoredDashboardFilters: config.ignoredDashboardFilters ?? [],
    ...displayFor(type, config),
  };
}

/**
 * What a widget shows: it asks the server for the widget's rows (the same query renders a saved widget and previews a draft),
 * then draws the chart for its type, with the indicator's format and unit (design D6), or says why it cannot (design D8): the
 * indicator has not published, the table no longer has a column the widget names, or the query was refused.
 */
@Component({
  selector: 'app-widget-view',
  imports: [NgTemplateOutlet, RouterLink, TranslocoDirective, Alert, ApiLang, Button, Skeleton, WidgetFoot, AxisChart, BoxplotChart, CrosstabWidget, HeatmapChart, LikertChart, MapWidget, PieWidget, PyramidChart, StatWidget],
  template: `
    <div class="flex h-full min-h-0 flex-col gap-2" *transloco="let t" [attr.aria-busy]="state().kind === 'loading'">
      @switch (state().kind) {
        @case ('idle') {
          @if (blocked()) {
            <ng-container *ngTemplateOutlet="notPublished" />
          } @else {
            <p class="text-sm text-body" data-testid="view-idle">{{ placeholder() }}</p>
          }
        }
        @case ('loading') {
          <div class="flex flex-col gap-3 paper:hidden" data-testid="view-loading"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
          <!-- A widget printed before its rows came says so, rather than print grey bars (dashboard printing, design D6). -->
          <p class="hidden text-sm text-body paper:block" data-testid="view-not-loaded">{{ t('dashboards.print.notLoaded') }}</p>
        }
        @case ('error') {
          <tmz-alert tone="danger" data-testid="view-error"><span [tmzApiLang]="error()">{{ error() }}</span></tmz-alert>
        }
        @case ('ready') {
          @if (result(); as result) {
            @if (!result.published || result.missing.indicator) {
              <ng-container *ngTemplateOutlet="notPublished" />
            } @else if (result.missing.columns.length) {
              <tmz-alert tone="warning" [heading]="t('indicators.missing.title')" class="overflow-auto" data-testid="widget-missing">
                <p data-testid="missing-columns">{{ t('indicators.missing.columns', { names: result.missing.columns.join(', ') }) }}</p>
                <p class="mt-1">{{ t('indicators.missing.help') }}</p>
              </tmz-alert>
            } @else if (result.combinedCategories) {
              <!-- A ratio that would add up several answers of a multiple-choice question: the message in place of the figures (improve-stat-cards, design D4b). -->
              <tmz-alert tone="warning" [heading]="t('indicators.combined.title')" class="overflow-auto" data-testid="widget-combined">
                <p data-testid="combined-text">{{ t('indicators.combined.text', { column: combinedColumn() }) }}</p>
              </tmz-alert>
            } @else {
              @if (notFiltered().length) {
                <!-- Not a warning: the rows are right, only not narrowed by these filters (dashboard filters, design D7). -->
                <p class="shrink-0 text-xs text-body" data-testid="widget-not-filtered">{{ t('dashboardFilters.notFiltered', { names: notFiltered().join(', ') }) }}</p>
              }
              @if (result.rowLimitReached) {
                <p class="shrink-0 text-xs text-fg-warning" data-testid="widget-row-limit">{{ t(chartType() === 'boxplot' ? 'indicators.widget.rowLimitBoxes' : 'indicators.widget.rowLimit', { count: result.rows.length }) }}</p>
              }
              @if (result.rows.length === 0) {
                <p class="text-sm text-body" data-testid="widget-empty">{{ t('indicators.widget.empty') }}</p>
                <app-widget-foot [items]="footItems()" />
              } @else {
                @if (showToggle() && chartType() !== 'stat') {
                  <div class="flex shrink-0 justify-end">
                    <button tmzButton variant="secondary" size="sm" type="button" [attr.aria-pressed]="asTable()" (click)="asTable.set(!asTable())" data-testid="chart-toggle">
                      {{ asTable() ? t('indicators.widget.showChart') : t('indicators.widget.showTable') }}
                    </button>
                  </div>
                }
                <!-- With a footnote or a line of bases the box cuts what does not fit, so a chart too big for its room never paints over the text (add-widget-context, design D5; add-small-sample-warnings, design D11). -->
                <div class="min-h-0 flex-1" [class.overflow-hidden]="footItems().length > 0" [class]="statBoxClass()">
                  @switch (chartType()) {
                    @case ('stat') {
                      <app-stat-widget [rows]="result.rows" [config]="config()!" [label]="label()" [format]="formatter()" [names]="names()" [bases]="chartBases()" [n]="result.n" [unfiltered]="result.unfiltered" />
                    }
                    @case ('pie') {
                      <app-pie-widget [rows]="result.rows" [domain]="result.domain" [config]="config()!" [label]="label()" [format]="formatter()" [names]="names()" [orders]="orders()" [colors]="resolvedColors()" [bases]="chartBases()" [asTable]="asTable()" (dimmed)="dimmedMarks.set($event)" />
                    }
                    @case ('map') {
                      <app-map-widget [rows]="result.rows" [config]="config()!" [label]="label()" [format]="formatter()" [names]="names()" [orders]="orders()" [colors]="resolvedColors()" [tooltipTypes]="result.tooltipTypes" [bases]="chartBases()" [asTable]="asTable()" (dimmed)="dimmedMarks.set($event)" (exportReadiness)="mapReadiness.set($event)" (basemapShown)="mapBasemap.set($event)" />
                    }
                    @case ('bar') {
                      <ng-container *ngTemplateOutlet="axis" />
                    }
                    @case ('column') {
                      <ng-container *ngTemplateOutlet="axis" />
                    }
                    @case ('line') {
                      <ng-container *ngTemplateOutlet="axis" />
                    }
                    @case ('histogram') {
                      <ng-container *ngTemplateOutlet="axis" />
                    }
                    @case ('heatmap') {
                      <app-heatmap-chart [answer]="result" [config]="config()!" [label]="label()" [format]="formatter()" [names]="names()" [orders]="orders()" [colors]="resolvedColors()" [bases]="chartBases()" [asTable]="asTable()" (dimmed)="dimmedMarks.set($event)" />
                    }
                    @case ('pyramid') {
                      <app-pyramid-chart [answer]="result" [config]="config()!" [label]="label()" [format]="formatter()" [names]="names()" [orders]="orders()" [colors]="resolvedColors()" [bases]="chartBases()" [asTable]="asTable()" (dimmed)="dimmedMarks.set($event)" />
                    }
                    @case ('likert') {
                      <app-likert-chart [answer]="result" [config]="config()!" [label]="label()" [format]="formatter()" [names]="names()" [orders]="orders()" [bases]="chartBases()" [asTable]="asTable()" (dimmed)="dimmedMarks.set($event)" />
                    }
                    @case ('crosstab') {
                      <app-crosstab-widget [answer]="result" [config]="config()!" [label]="label()" [format]="formatter()" [names]="names()" [orders]="orders()" [bases]="chartBases()" chartType="crosstab" />
                    }
                    @case ('boxplot') {
                      <app-boxplot-chart [answer]="result" [config]="config()!" [label]="label()" [format]="boxFormatter()" [names]="names()" [orders]="orders()" [percent]="isPercent()" [colors]="resolvedColors()" [bases]="chartBases()" [asTable]="asTable()" (dimmed)="dimmedMarks.set($event)" />
                    }
                  }
                </div>
                <app-widget-foot [items]="footItems()" [compact]="chartType() === 'stat'" />
              }
            }
          }
        }
      }
    </div>

    <!-- A bar, column or line chart with a series is given the whole answer, which holds the series and the totals. -->
    <ng-template #axis>
      <app-axis-chart [kind]="axisKind()!" [rows]="result()!.rows" [answer]="result()" [config]="config()!" [label]="label()" [format]="formatter()" [axisTitle]="axisTitle()" [names]="names()" [orders]="orders()" [colors]="resolvedColors()" [bases]="chartBases()" [asTable]="asTable()" (dimmed)="dimmedMarks.set($event)" (unplaced)="unplacedLines.set($event)" />
    </ng-template>

    <ng-template #notPublished>
      <div class="flex flex-col items-start gap-2 text-sm text-body" data-testid="view-not-published" *transloco="let t">
        <p>{{ t('indicators.widget.notPublished', { label: label() }) }}</p>
        @if (scriptsLink()) {
          <a [routerLink]="['/projects', projectId(), 'scripts']" class="text-fg-brand hover:underline paper:hidden" data-testid="view-scripts-link">{{ t('indicators.widget.scriptsLink') }}</a>
        }
      </div>
    </ng-template>
  `,
  providers: [WidgetExportHost],
  host: { class: 'block h-full min-h-0' },
})
export class WidgetView {
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly locale = inject(LOCALE_ID);
  private readonly transloco = inject(TranslocoService);
  private readonly exportHost = inject(WidgetExportHost);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly map = viewChild(MapWidget);

  readonly projectId = input.required<string>();
  readonly chartType = input.required<ChartType>();
  /** The query to show; null while it is incomplete, which shows `placeholder`. */
  readonly config = input.required<WidgetConfig | null>();
  /** The indicator the query names, as the project lists it; null when it lists none by that name. */
  readonly indicator = input<IndicatorEntry | null>(null);
  /** Analysts and admins: a widget whose indicator has not published links to the scripts. */
  readonly scriptsLink = input(false);
  /** Wait this long after the last change before asking (a draft being typed); 0 asks at once. */
  readonly debounceMs = input(0);
  readonly placeholder = input('');
  /** The colors of the dashboard the widget is on, or of the version shown (add-dashboard-palettes, design D7). */
  readonly colors = input<DashboardColors>(defaultColors);
  /**
   * The tile is a stat tile 2 rows high or less (improve-widget-text-hierarchy, design D6): its subtitle is not under its title but in the
   * foot's Detail chip. Set by the grid; false anywhere else.
   */
  readonly compactStat = input(false);
  /** The grid columns the tile spans, which the stat box on paper reads to guess whether the label takes two lines; 0 when not known. */
  readonly columns = input(0);
  /**
   * What a stat tile's box keeps on paper (design D6): the room of its figure and a label of one line, or of two when the label is longer
   * than a line of that many columns holds (about 10.5 characters of 14 px per column; the text it measures is the one the tile draws as its label, the statistic included). A tile two rows high gives the foot what is left,
   * so a short label lets the Detail line and the notes print. Not known, it keeps the room of two lines.
   */
  protected readonly statBoxClass = computed(() => {
    if (this.chartType() !== 'stat') {
      return '';
    }
    this.lang();
    const config = this.config();
    const drawn = config ? statDescription(config, this.label(), this.names(), (key, params) => this.transloco.translate(key, params)) : '';
    const perLine = Math.max(1, this.columns()) * 10.5;
    return this.columns() > 0 && drawn.length <= perLine ? 'paper:min-h-[2.6rem]' : 'paper:min-h-[3.7rem]';
  });
  /**
   * The base settings of the dashboard the widget is on, or of the version shown (add-small-sample-warnings, design D7, D8): the small-base
   * threshold and whether widgets show their n line. The widget's own settings in its display win over them.
   */
  readonly bases = input<DashboardBases>(noBaseSettings);

  /** Show the table of the rows instead of the chart. The grid drives it from the tile header; elsewhere the widget's own toggle does. */
  readonly asTable = model(false);
  /** Whether the widget shows its own chart/table toggle; the grid turns it off and puts the toggle in the tile header. */
  readonly showToggle = input(true);
  /**
   * The dashboard filters with a value in force, in the bar's order, each marked when the widget ignores it. Those it does not ignore
   * go with the query; the widget says which ones did not narrow it.
   */
  readonly dashboardFilters = input<ActiveDashboardFilter[]>([]);
  /** True while the widget has rows it can show as a chart or as a table (never for a stat tile), so a host can offer the toggle. */
  readonly tableAvailable = output<boolean>();
  /**
   * The values the widget draws, in the order it draws them and in full text (add-declared-category-order, design D8): its groups and its
   * series. The widget form seeds its order editors from them and lists apart the values an order holds that are not among them. Null
   * while there is no answer to draw.
   */
  readonly valuesShown = output<ShownValues | null>();
  protected readonly state = signal<State>({ kind: 'idle' });
  private readonly drawable = computed(() => {
    const result = this.result();
    // A cross table is a table already: it has no other view to switch to.
    return this.chartType() !== 'stat' && this.chartType() !== 'crosstab' && !!result && result.published && !result.missing.indicator && !result.missing.columns.length && result.rows.length > 0;
  });

  protected readonly result = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.result : null;
  });
  /** What the query sends of the dashboard filters: those the widget does not ignore. */
  private readonly applied = computed(() => appliedOf(this.dashboardFilters()));
  /**
   * The labels of the active filters that do not narrow the rows: ignored by the widget, or not taken by its table. Twin of
   * `WidgetQuery.NotNarrowedBy` in tamiza-api.
   */
  protected readonly notFiltered = computed(() => {
    const missed = new Set(this.result()?.notFiltered ?? []);
    return this.dashboardFilters()
      .filter((active) => active.ignored || missed.has(active.filter.column))
      .map((active) => active.filter.label);
  });
  protected readonly error = computed(() => {
    const state = this.state();
    return state.kind === 'error' ? state.message : '';
  });
  /** The widget's footnote, trimmed, or null while it has none. */
  protected readonly footnote = computed(() => this.config()?.display?.footnote?.trim() || null);
  /** The indicator is listed and has no table yet: the widget says so without asking. */
  protected readonly blocked = computed(() => !!this.indicator() && !this.indicator()!.published);
  /** What the widget calls its indicator: the label, or the name when the project lists none. */
  protected readonly label = computed(() => this.indicator()?.label ?? this.config()?.indicator ?? '');
  /** The name shown for each column: the widget's own label, else the indicator's, else the column (design D6, D7). */
  protected readonly names = computed(() => columnLabels(this.indicator(), this.config()?.display));
  /**
   * What the widget declares for the order of its values and the text they are shown with (add-declared-category-order, design D8): its own
   * order, else its indicator's, and the indicator's hidden prefixes. Null when it declares nothing, and every chart then draws as it always did.
   */
  protected readonly orders = computed(() => ordersFor(this.chartType(), this.config(), this.indicator()?.columns));
  /** The base settings in force for the widget: its own over the dashboard's. */
  protected readonly inForce = computed(() => settingsInForce(this.config()?.display, this.bases()));
  /** What a chart is given to write its figures and mark its small bases: the threshold in force and the indicator's base column. */
  protected readonly chartBases = computed<ChartBases>(() => ({ minN: this.inForce().minN, showN: this.inForce().showN, baseColumn: baseColumnOf(this.indicator()?.columns), answerColumns: answerColumnsOf(this.indicator()?.columns) }));
  /**
   * The marks the chart drew dimmed for a small base, named and with their n, told by the chart itself (design D10; improve-widget-text-hierarchy,
   * design D5): the foot's small-base chip names them.
   */
  protected readonly dimmedMarks = signal<SmallMark[]>([]);
  /** The threshold of the small-base chip, while a mark is dimmed: a table says it with its own note, a cross table by its cells. */
  protected readonly smallMin = computed(() => (this.inForce().minN !== null && !this.asTable() && this.chartType() !== 'crosstab' && this.dimmedMarks().length > 0 ? this.inForce().minN : null));
  /** The widget's n: the query's, else a stat tile's own row's (improve-stat-cards, design D2). */
  private readonly widgetN = computed(() => this.result()?.n ?? (this.chartType() === 'stat' ? this.result()?.rows[0]?.n : undefined));
  /**
   * The base n line (design D11): "n = 223 Hogares que asistieron", while the widget shows it and has an n. A stat tile shows it when it
   * chooses it or its dashboard does, by the rules it always had (`statNLine`, improve-widget-text-hierarchy, design D6); it is the n chip of the
   * foot, for a tile as for a chart.
   */
  protected readonly nLineText = computed(() => {
    const config = this.config() ?? emptyConfig();
    return this.chartType() === 'stat'
      ? statNLine(config, this.names(), this.chartBases(), this.widgetN(), this.locale)
      : this.inForce().showN
        ? nLine(this.result()?.n, figureNames(config, this.names(), baseColumnOf(this.indicator()?.columns)).base, this.locale)
        : null;
  });
  /** What the widget left out as no data, in the widget's formatter and unit (design D6, D11). */
  protected readonly noDataText = computed(() => noDataLine(this.result()?.noData, this.formatter(), this.inForce().showN, this.locale));
  /** The note under the chart: a stat tile draws its own inside (improve-stat-cards, design D1), so none is drawn under it. */
  protected readonly smallMinBelow = computed(() => (this.chartType() === 'stat' ? null : this.smallMin()));

  /** The language the foot's texts are written in: they are worked out in a computed signal, so a change of language must redo them. */
  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  /**
   * The reference lines whose text could not be drawn at the line, with their whole texts (design D7): a chart tells the widget, and the
   * foot's reference-lines chip names them. Empty while every text fits.
   */
  readonly unplacedLines = signal<string[]>([]);

  /**
   * The chips of the widget's foot, in their order (improve-widget-text-hierarchy, design D4): the caption's own text when a subtitle took it
   * out of sight, the base n, the small bases, the no-data value, the reference lines whose text did not fit, a compact stat tile's subtitle
   * and the footnote. A chip with nothing to say is left out. The page shows them as chips, paper and an image as lines of whole text.
   */
  protected readonly footItems = computed<FootItem[]>(() => {
    this.lang();
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const config = this.config();
    const display = config?.display;
    const items: FootItem[] = [];
    const own = config ? ownCaption(config) : null;
    if (config && own && captionInFoot(this.chartType(), config)) {
      items.push({ kind: 'caption', label: own, text: t('indicators.chart.caption', { what: own, group: named(this.names(), config.groupBy) }), tone: 'neutral' });
    }
    // The n chip is gray while the base is enough and amber below the threshold in force, which a stat tile says whether or not it shows its
    // n: it has no other mark of a small base now that its figure is no longer dimmed (design D6).
    const n = this.nLineText();
    const widgetN = this.widgetN();
    const smallN = typeof widgetN === 'number' && isSmallBase(widgetN, this.inForce().minN);
    if (n || (this.chartType() === 'stat' && smallN)) {
      if (smallN) {
        const number = figureFormatter(this.locale);
        items.push({
          kind: 'n',
          label: `n = ${number(widgetN)}`,
          text: t(this.chartType() === 'stat' ? 'indicators.foot.smallStat' : 'indicators.foot.smallChart', { n: number(widgetN), min: number(this.inForce().minN!) }),
          tone: 'warning',
          // A tile two rows high has no room for the whole sentence on paper: the sheet prints a line short enough to keep the warning.
          printText: this.chartType() === 'stat' ? t('indicators.foot.smallStatShort', { n: number(widgetN), min: number(this.inForce().minN!) }) : undefined,
        });
      } else if (n) {
        items.push({ kind: 'n', label: n, text: n, tone: 'neutral' });
      }
    }
    const min = this.smallMinBelow();
    if (min !== null) {
      const marks = this.dimmedMarks();
      items.push({ kind: 'small', label: t('indicators.foot.small', { count: marks.length }), text: smallBaseMarksText(marks, min, this.locale, t), tone: 'warning' });
    }
    const noData = this.noDataText();
    if (noData) {
      const lost = this.result()?.noData;
      const share = typeof lost?.share === 'number' ? new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 }).format(lost.share) : null;
      const figure = share ?? (typeof lost?.value === 'number' ? this.formatter()(lost.value) : '');
      items.push({ kind: 'noData', label: t('indicators.foot.noData', { figure }).trim(), text: noData, tone: 'neutral' });
    }
    const lines = this.axisKind() ? this.unplacedLines() : [];
    if (lines.length) {
      items.push({ kind: 'lines', label: t('indicators.foot.lines'), text: lines.join('; '), tone: 'neutral' });
    }
    const subtitle = display?.subtitle?.trim();
    if (this.compactStat() && subtitle) {
      items.push({ kind: 'detail', label: t('indicators.foot.detail'), text: subtitle, tone: 'neutral' });
    }
    const footnote = this.footnote();
    if (footnote) {
      items.push({ kind: 'note', label: t('indicators.foot.note'), text: footnote, tone: 'neutral' });
    }
    return items;
  });

  /** The message of a widget that would add up several answers of a multiple-choice question, naming the column (improve-stat-cards, design D4b). */
  protected readonly combinedColumn = computed(() => {
    const column = this.result()?.combinedCategories;
    return column ? this.names()(column) : '';
  });

  private readonly drawn = computed<ShownValues | null>(() => {
    const result = this.result();
    const config = this.config();
    return result && config && result.published && !result.missing.indicator && !result.missing.columns.length ? shownValuesOf(this.chartType(), result, config, this.orders()) : null;
  });
  /**
   * The values are written with the indicator's format and the unit of the widget, which replaces the indicator's when it has one
   * (add-widget-context, design D4); a widget whose indicator the project no longer lists keeps its own unit too.
   */
  protected readonly unitOf = computed(() => withWidgetUnit(this.indicator() ?? plainIndicator, this.config()));
  protected readonly formatter = computed(() => valueFormatter(this.unitOf(), this.locale));
  /** The unit a bar, column or line chart writes at the end of its value axis while its caption line is out of sight (design D3). */
  protected readonly axisTitle = computed(() => valueAxisTitle(this.unitOf()));
  /** A box plot's figures are written as a decimal for a count (quartiles and means fall between whole numbers). */
  protected readonly boxFormatter = computed(() => valueFormatter(boxIndicator(this.unitOf()), this.locale));
  protected readonly isPercent = computed(() => (this.indicator() ?? plainIndicator).format === 'percent');
  protected readonly axisKind = computed(() => axisKindOf(this.chartType()));
  /**
   * The colors the widget is drawn in: the dashboard's palette and colors kept by value, and the widget's own color or scale. It reads
   * the whole config, `display` included, so a color change redraws the chart without a query: the query's key leaves it out.
   */
  readonly resolvedColors = computed<ChartColors>(() => chartColors(this.colors(), this.chartType(), this.config() ?? emptyConfig()));

  /** The width of the widget's body on screen, which an image of it keeps (add-widget-export, design D7). */
  bodyWidth(): number {
    return Math.round(this.element.nativeElement.clientWidth);
  }

  /** The widget's own element, where the styles that reach its chart are set. */
  get host(): HTMLElement {
    return this.element.nativeElement;
  }

  /** The shapes of a map drawn as a chart, as the member's view shows them; null for any other widget. */
  mapPicture(): MapPicture | null {
    return this.map()?.picture() ?? null;
  }

  /** What the chart or tile drawn inside registered for an image of the widget (add-widget-export), or null before one exists. */
  readonly exportRegistration = this.exportHost.current;

  /**
   * What the tile says above its chart (add-widget-export, design D8): the filters in force that did not narrow it, and the row limit,
   * in the words the tile uses.
   */
  readonly exportNotesAbove = computed(() => {
    const notes: string[] = [];
    if (this.notFiltered().length) {
      notes.push(this.transloco.translate('dashboardFilters.notFiltered', { names: this.notFiltered().join(', ') }));
    }
    const result = this.result();
    if (result?.published && !result.missing.indicator && !result.missing.columns.length && result.rowLimitReached) {
      notes.push(this.transloco.translate(this.chartType() === 'boxplot' ? 'indicators.widget.rowLimitBoxes' : 'indicators.widget.rowLimit', { count: result.rows.length }));
    }
    return notes;
  });

  /**
   * What the tile says under its chart (improve-widget-text-hierarchy, design D10): the texts its foot opens, as lines of whole text, in the
   * foot's order, which an image draws among its notes below, before the footnote. The subtitle of a compact stat tile and the footnote are
   * the image's own parameters, so they are not here.
   */
  readonly exportNotesBelow = computed(() => this.footItems().filter((item) => item.kind !== 'detail' && item.kind !== 'note').map((item) => item.text));

  /** What a map as a chart says about itself; a map shown as a table has no engine and is read as a table. */
  readonly mapReadiness = signal<MapReadiness>('drawing');
  readonly mapBasemap = signal(false);

  /**
   * Which formats of this widget can be saved now, and why not (design D3). A map's images wait for its engine and its moving to end and
   * need a shape to draw, while its workbook only needs its rows. The reasons are in the interface language at the time of the call.
   */
  readonly exportState = computed<ExportState>(() => {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const neither = (reason: string): ExportState => ({ image: reason, workbook: reason, imageNote: null });
    const state = this.state();
    if (state.kind === 'idle') {
      return neither(this.blocked() ? t('indicators.widget.notPublished', { label: this.label() }) : t('indicators.widgetExport.reason.loading'));
    }
    if (state.kind === 'loading') {
      return neither(t('indicators.widgetExport.reason.loading'));
    }
    if (state.kind === 'error') {
      return neither(t('indicators.widgetExport.reason.failed'));
    }
    const result = state.result;
    if (!result.published || result.missing.indicator) {
      return neither(t('indicators.widget.notPublished', { label: this.label() }));
    }
    if (result.missing.columns.length) {
      return neither(t('indicators.widgetExport.reason.missing', { names: result.missing.columns.join(', ') }));
    }
    if (result.combinedCategories) {
      return neither(t('indicators.combined.text', { column: this.combinedColumn() }));
    }
    if (result.rows.length === 0) {
      return neither(t('indicators.widget.empty'));
    }
    if (this.chartType() === 'map' && !this.asTable()) {
      const readiness = this.mapReadiness();
      if (readiness !== 'ready') {
        return { image: t(`indicators.widgetExport.reason.${readiness === 'nothing' ? 'mapNothing' : 'mapDrawing'}`), workbook: null, imageNote: null };
      }
      return { image: null, workbook: null, imageNote: this.mapBasemap() ? t('indicators.widgetExport.withoutBasemap') : null };
    }
    return { image: null, workbook: null, imageNote: null };
  });

  private readonly key = computed(() => {
    const config = this.config();
    // `overall` enters the key only while a line of that kind needs it, so the key of every other widget is what it was.
    return config && !this.blocked()
      ? JSON.stringify({
          type: this.chartType(),
          config: queryFor(this.chartType(), config),
          dashboardFilters: this.applied(),
          ...(overallFor(this.chartType(), config) ? { overall: true } : {}),
          // Likewise `unfiltered`, only while a stat tile asks for its value without the dashboard filters (improve-stat-cards, design D3).
          ...(unfilteredFor(this.chartType(), config) ? { unfiltered: true } : {}),
        })
      : null;
  });

  constructor() {
    // On the dashboard page, the Print action waits while the rows are on their way (dashboard printing, design D6).
    const print = inject(DashboardPrint, { optional: true });
    if (print) {
      inject(DestroyRef).onDestroy(print.registerPending(() => this.state().kind === 'loading'));
    }
    effect(() => this.tableAvailable.emit(this.drawable()));
    effect(() => this.valuesShown.emit(this.drawn()));
    toObservable(this.key)
      .pipe(
        distinctUntilChanged(),
        // The old picture goes at once: a changed draft never shows the previous chart while it waits or fails.
        tap((key) => this.state.set(key === null ? { kind: 'idle' } : { kind: 'loading' })),
        switchMap((key) =>
          key === null
            ? of<State>({ kind: 'idle' })
            : // A newer draft cancels both the wait and the request, so an old answer never lands on a changed draft.
              timer(this.debounceMs()).pipe(
                switchMap(() => {
                  const { type, config, dashboardFilters, overall, unfiltered } = JSON.parse(key) as { type: ChartType; config: WidgetConfig; dashboardFilters: AppliedDashboardFilter[]; overall?: true; unfiltered?: true };
                  return this.service.query(this.projectId(), config, type, dashboardFilters, overall === true, unfiltered === true);
                }),
                map((result): State => ({ kind: 'ready', result })),
                catchError((failure) => {
                  const parsed = this.errors.parse(failure);
                  return of<State>({ kind: 'error', message: Object.values(parsed.fieldErrors)[0] ?? parsed.message });
                }),
              ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((state) => this.state.set(state));
  }
}
