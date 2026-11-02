import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, LOCALE_ID, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { catchError, distinctUntilChanged, map, of, switchMap, tap, timer } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import {
  AppliedDashboardFilter,
  ChartType,
  DashboardColors,
  IndicatorEntry,
  IndicatorsService,
  QueryResult,
  WidgetConfig,
  WidgetDisplay,
  WidgetSort,
  emptyConfig,
  hasSeries,
  takesSeries,
} from '../../../core/projects/indicators.service';
import { Alert, ApiLang, Button, Skeleton } from '../../../shared/ui';
import { AxisChart, AxisKind } from './axis-chart';
import { BoxplotChart } from './boxplot-chart';
import { ChartColors, chartColors, defaultColors } from './chart-colors';
import { CrosstabWidget } from './crosstab-widget';
import { HeatmapChart } from './heatmap-chart';
import { LikertChart } from './likert-chart';
import { boxIndicator, plainIndicator, valueFormatter } from './format';
import { ActiveDashboardFilter, appliedOf } from './dashboard-filters';
import { DashboardPrint } from './dashboard-print';
import { MapWidget } from './map-widget';
import { PieWidget } from './pie-widget';
import { PyramidChart } from './pyramid-chart';
import { StatWidget } from './stat-widget';

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
 * an orientation for a box plot. A widget with nothing to keep has no `display` at all, not an empty one, so a widget saved before palettes saves exactly what it did.
 */
function displayFor(type: ChartType, config: WidgetConfig): { display?: WidgetDisplay } {
  const { color, ramp, orientation } = config.display ?? {};
  const kept: WidgetDisplay = {
    ...((type === 'histogram' || type === 'boxplot' || (takesSeries(type) && !config.seriesBy)) && color != null ? { color } : {}),
    ...((type === 'map' || type === 'heatmap') && ramp ? { ramp } : {}),
    ...(type === 'boxplot' && orientation ? { orientation } : {}),
  };
  return Object.keys(kept).length ? { display: kept } : {};
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
 * `display` is not part of it, so choosing a color never queries again.
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
  imports: [NgTemplateOutlet, RouterLink, TranslocoDirective, Alert, ApiLang, Button, Skeleton, AxisChart, BoxplotChart, CrosstabWidget, HeatmapChart, LikertChart, MapWidget, PieWidget, PyramidChart, StatWidget],
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
              } @else {
                @if (showToggle() && chartType() !== 'stat') {
                  <div class="flex shrink-0 justify-end">
                    <button tmzButton variant="secondary" size="sm" type="button" [attr.aria-pressed]="asTable()" (click)="asTable.set(!asTable())" data-testid="chart-toggle">
                      {{ asTable() ? t('indicators.widget.showChart') : t('indicators.widget.showTable') }}
                    </button>
                  </div>
                }
                <div class="min-h-0 flex-1">
                  @switch (chartType()) {
                    @case ('stat') {
                      <app-stat-widget [rows]="result.rows" [config]="config()!" [label]="label()" [format]="formatter()" />
                    }
                    @case ('pie') {
                      <app-pie-widget [rows]="result.rows" [domain]="result.domain" [config]="config()!" [label]="label()" [format]="formatter()" [colors]="resolvedColors()" [asTable]="asTable()" />
                    }
                    @case ('map') {
                      <app-map-widget [rows]="result.rows" [config]="config()!" [label]="label()" [format]="formatter()" [colors]="resolvedColors()" [tooltipTypes]="result.tooltipTypes" [asTable]="asTable()" />
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
                      <app-heatmap-chart [answer]="result" [config]="config()!" [label]="label()" [format]="formatter()" [colors]="resolvedColors()" [asTable]="asTable()" />
                    }
                    @case ('pyramid') {
                      <app-pyramid-chart [answer]="result" [config]="config()!" [label]="label()" [format]="formatter()" [colors]="resolvedColors()" [asTable]="asTable()" />
                    }
                    @case ('likert') {
                      <app-likert-chart [answer]="result" [config]="config()!" [label]="label()" [format]="formatter()" [asTable]="asTable()" />
                    }
                    @case ('crosstab') {
                      <app-crosstab-widget [answer]="result" [config]="config()!" [label]="label()" [format]="formatter()" chartType="crosstab" />
                    }
                    @case ('boxplot') {
                      <app-boxplot-chart [answer]="result" [config]="config()!" [label]="label()" [format]="boxFormatter()" [percent]="isPercent()" [colors]="resolvedColors()" [asTable]="asTable()" />
                    }
                  }
                </div>
              }
            }
          }
        }
      }
    </div>

    <!-- A bar, column or line chart with a series is given the whole answer, which holds the series and the totals. -->
    <ng-template #axis>
      <app-axis-chart [kind]="axisKind()!" [rows]="result()!.rows" [answer]="result()" [config]="config()!" [label]="label()" [format]="formatter()" [colors]="resolvedColors()" [asTable]="asTable()" />
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
  host: { class: 'block h-full min-h-0' },
})
export class WidgetView {
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly locale = inject(LOCALE_ID);

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
  /** The indicator is listed and has no table yet: the widget says so without asking. */
  protected readonly blocked = computed(() => !!this.indicator() && !this.indicator()!.published);
  /** What the widget calls its indicator: the label, or the name when the project lists none. */
  protected readonly label = computed(() => this.indicator()?.label ?? this.config()?.indicator ?? '');
  protected readonly formatter = computed(() => valueFormatter(this.indicator() ?? plainIndicator, this.locale));
  /** A box plot's figures are written as a decimal for a count (quartiles and means fall between whole numbers). */
  protected readonly boxFormatter = computed(() => valueFormatter(boxIndicator(this.indicator() ?? plainIndicator), this.locale));
  protected readonly isPercent = computed(() => (this.indicator() ?? plainIndicator).format === 'percent');
  protected readonly axisKind = computed(() => axisKindOf(this.chartType()));
  /**
   * The colors the widget is drawn in: the dashboard's palette and colors kept by value, and the widget's own color or scale. It reads
   * the whole config, `display` included, so a color change redraws the chart without a query: the query's key leaves it out.
   */
  readonly resolvedColors = computed<ChartColors>(() => chartColors(this.colors(), this.chartType(), this.config() ?? emptyConfig()));

  private readonly key = computed(() => {
    const config = this.config();
    return config && !this.blocked() ? JSON.stringify({ type: this.chartType(), config: queryFor(this.chartType(), config), dashboardFilters: this.applied() }) : null;
  });

  constructor() {
    // On the dashboard page, the Print action waits while the rows are on their way (dashboard printing, design D6).
    const print = inject(DashboardPrint, { optional: true });
    if (print) {
      inject(DestroyRef).onDestroy(print.registerPending(() => this.state().kind === 'loading'));
    }
    effect(() => this.tableAvailable.emit(this.drawable()));
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
                  const { type, config, dashboardFilters } = JSON.parse(key) as { type: ChartType; config: WidgetConfig; dashboardFilters: AppliedDashboardFilter[] };
                  return this.service.query(this.projectId(), config, type, dashboardFilters);
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
