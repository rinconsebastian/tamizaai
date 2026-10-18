import { NgTemplateOutlet } from '@angular/common';
import { Component, LOCALE_ID, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { catchError, distinctUntilChanged, map, of, switchMap, tap, timer } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import { ChartType, IndicatorEntry, IndicatorsService, QueryResult, WidgetConfig, WidgetSort } from '../../../core/projects/indicators.service';
import { Alert, Button, Skeleton } from '../../../shared/ui';
import { AxisChart } from './axis-chart';
import { plainIndicator, valueFormatter } from './format';
import { MapWidget } from './map-widget';
import { PieWidget } from './pie-widget';
import { StatWidget } from './stat-widget';

type State = { kind: 'idle' } | { kind: 'loading' } | { kind: 'error'; message: string } | { kind: 'ready'; result: QueryResult };

/** Plotting the value is always by number, and which column it is makes no sense then: those are not sent. */
function sortFor(sort: WidgetSort): WidgetSort {
  return sort.by === 'value' ? { ...sort, column: null, compareAs: null } : sort;
}

/** The query a chart type actually uses: settings that belong to another chart type are not sent, and a stat tile never groups. */
export function queryFor(type: ChartType, config: WidgetConfig): WidgetConfig {
  return {
    indicator: config.indicator,
    measure: config.ratio ? null : config.measure,
    ratio: config.ratio,
    groupBy: type === 'stat' ? null : config.groupBy,
    filters: config.filters,
    geometryColumn: type === 'map' ? config.geometryColumn : null,
    buckets: type === 'histogram' ? config.buckets : null,
    sort: (type === 'bar' || type === 'column') && config.sort ? sortFor(config.sort) : null,
    // The query needs the columns' names to fetch them; a label, a format and the basemap change nothing in the rows, so they stay out
    // of it (and out of the re-query key), and editing them never blanks the preview.
    // (A config from an API that predates the tooltip has neither field; it reads as none.)
    tooltip: type === 'map' ? (config.tooltip ?? []).map((item) => ({ column: item.column, label: null, format: null })) : [],
    basemap: null,
  };
}

/**
 * What a widget is saved with, and what its preview is given: the query, plus (for a map) the whole tooltip items and the basemap.
 * Any other chart type keeps neither, so changing a map into one saves no tooltip columns.
 */
export function savedConfig(type: ChartType, config: WidgetConfig): WidgetConfig {
  return { ...queryFor(type, config), tooltip: type === 'map' ? (config.tooltip ?? []) : [], basemap: type === 'map' ? (config.basemap ?? null) : null };
}

/**
 * What a widget shows: it asks the server for the widget's rows (the same query renders a saved widget and previews a draft),
 * then draws the chart for its type, with the indicator's format and unit (design D6), or says why it cannot (design D8): the
 * indicator has not published, the table no longer has a column the widget names, or the query was refused.
 */
@Component({
  selector: 'app-widget-view',
  imports: [NgTemplateOutlet, RouterLink, TranslocoDirective, Alert, Button, Skeleton, AxisChart, MapWidget, PieWidget, StatWidget],
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
          <div class="flex flex-col gap-3" data-testid="view-loading"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
        }
        @case ('error') {
          <tmz-alert tone="danger" data-testid="view-error">{{ error() }}</tmz-alert>
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
              @if (result.rowLimitReached) {
                <p class="shrink-0 text-xs text-fg-warning" data-testid="widget-row-limit">{{ t('indicators.widget.rowLimit', { count: result.rows.length }) }}</p>
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
                      <app-pie-widget [rows]="result.rows" [config]="config()!" [label]="label()" [format]="formatter()" [asTable]="asTable()" />
                    }
                    @case ('map') {
                      <app-map-widget [rows]="result.rows" [config]="config()!" [label]="label()" [format]="formatter()" [tooltipTypes]="result.tooltipTypes" [asTable]="asTable()" />
                    }
                    @default {
                      <app-axis-chart [kind]="axisKind()" [rows]="result.rows" [config]="config()!" [label]="label()" [format]="formatter()" [asTable]="asTable()" />
                    }
                  }
                </div>
              }
            }
          }
        }
      }
    </div>

    <ng-template #notPublished>
      <div class="flex flex-col items-start gap-2 text-sm text-body" data-testid="view-not-published" *transloco="let t">
        <p>{{ t('indicators.widget.notPublished', { label: label() }) }}</p>
        @if (scriptsLink()) {
          <a [routerLink]="['/projects', projectId(), 'scripts']" class="text-fg-brand hover:underline" data-testid="view-scripts-link">{{ t('indicators.widget.scriptsLink') }}</a>
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

  /** Show the table of the rows instead of the chart. The grid drives it from the tile header; elsewhere the widget's own toggle does. */
  readonly asTable = model(false);
  /** Whether the widget shows its own chart/table toggle; the grid turns it off and puts the toggle in the tile header. */
  readonly showToggle = input(true);
  /** True while the widget has rows it can show as a chart or as a table (never for a stat tile), so a host can offer the toggle. */
  readonly tableAvailable = output<boolean>();
  protected readonly state = signal<State>({ kind: 'idle' });
  private readonly drawable = computed(() => {
    const result = this.result();
    return this.chartType() !== 'stat' && !!result && result.published && !result.missing.indicator && !result.missing.columns.length && result.rows.length > 0;
  });

  protected readonly result = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.result : null;
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
  protected readonly axisKind = computed(() => {
    const type = this.chartType();
    return type === 'bar' || type === 'column' || type === 'line' || type === 'histogram' ? type : 'column';
  });

  private readonly key = computed(() => {
    const config = this.config();
    return config && !this.blocked() ? JSON.stringify({ type: this.chartType(), config: queryFor(this.chartType(), config) }) : null;
  });

  constructor() {
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
                  const { type, config } = JSON.parse(key) as { type: ChartType; config: WidgetConfig };
                  return this.service.query(this.projectId(), config, type);
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
