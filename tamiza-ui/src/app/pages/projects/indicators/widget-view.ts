import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { catchError, distinctUntilChanged, map, of, switchMap, tap, timer } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import { ChartType, IndicatorsService, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { Alert, Button, Skeleton } from '../../../shared/ui';
import { AxisChart } from './axis-chart';
import { MapWidget } from './map-widget';
import { PieWidget } from './pie-widget';
import { StatWidget } from './stat-widget';

type State = { kind: 'idle' } | { kind: 'loading' } | { kind: 'error'; message: string } | { kind: 'ready'; result: QueryResult };

/** The query a chart type actually uses: settings that belong to another chart type are not sent. */
export function queryFor(type: ChartType, config: WidgetConfig): WidgetConfig {
  return {
    indicators: config.indicators,
    groupBy: config.groupBy,
    valueField: config.valueField,
    filters: config.filters,
    geometryColumn: type === 'map' ? config.geometryColumn : null,
    buckets: type === 'histogram' ? config.buckets : null,
  };
}

/**
 * What a widget shows: it asks the server for the widget's rows (the same query renders a saved widget and previews a draft),
 * then draws the chart for its type, or says why it cannot (design D8): the table has no longer an indicator or a column the
 * widget names, the query was refused, or nothing is published yet.
 */
@Component({
  selector: 'app-widget-view',
  imports: [TranslocoDirective, Alert, Button, Skeleton, AxisChart, MapWidget, PieWidget, StatWidget],
  template: `
    <div class="flex h-full min-h-0 flex-col gap-2" *transloco="let t" [attr.aria-busy]="state().kind === 'loading'">
      @switch (state().kind) {
        @case ('idle') {
          <p class="text-sm text-body" data-testid="view-idle">{{ idle() }}</p>
        }
        @case ('loading') {
          <div class="flex flex-col gap-3" data-testid="view-loading"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
        }
        @case ('error') {
          <tmz-alert tone="danger" data-testid="view-error">{{ error() }}</tmz-alert>
        }
        @case ('ready') {
          @if (result(); as result) {
            @if (!result.published) {
              <p class="text-sm text-body" data-testid="view-unpublished">{{ t('indicators.widget.unpublished') }}</p>
            } @else if (result.missing.indicators.length || result.missing.columns.length) {
              <tmz-alert tone="warning" [heading]="t('indicators.missing.title')" class="overflow-auto" data-testid="widget-missing">
                @if (result.missing.indicators.length) {
                  <p data-testid="missing-indicators">{{ t('indicators.missing.indicators', { names: result.missing.indicators.join(', ') }) }}</p>
                }
                @if (result.missing.columns.length) {
                  <p data-testid="missing-columns">{{ t('indicators.missing.columns', { names: result.missing.columns.join(', ') }) }}</p>
                }
                <p class="mt-1">{{ t('indicators.missing.help') }}</p>
              </tmz-alert>
            } @else {
              @if (result.rowLimitReached) {
                <tmz-alert tone="warning" data-testid="widget-row-limit">{{ t('indicators.widget.rowLimit', { count: result.rows.length }) }}</tmz-alert>
              }
              @if (result.rows.length === 0) {
                <p class="text-sm text-body" data-testid="widget-empty">{{ t('indicators.widget.empty') }}</p>
              } @else {
                @if (chartType() !== 'stat') {
                  <div class="flex shrink-0 justify-end">
                    <button tmzButton variant="secondary" size="sm" type="button" [attr.aria-pressed]="asTable()" (click)="asTable.set(!asTable())" data-testid="chart-toggle">
                      {{ asTable() ? t('indicators.widget.showChart') : t('indicators.widget.showTable') }}
                    </button>
                  </div>
                }
                <div class="min-h-0 flex-1">
                  @switch (chartType()) {
                    @case ('stat') {
                      <app-stat-widget [rows]="result.rows" [config]="config()!" />
                    }
                    @case ('pie') {
                      <app-pie-widget [rows]="result.rows" [config]="config()!" [asTable]="asTable()" />
                    }
                    @case ('map') {
                      <app-map-widget [rows]="result.rows" [config]="config()!" [asTable]="asTable()" />
                    }
                    @default {
                      <app-axis-chart [kind]="axisKind()" [rows]="result.rows" [config]="config()!" [asTable]="asTable()" />
                    }
                  }
                </div>
              }
            }
          }
        }
      }
    </div>
  `,
  host: { class: 'block h-full min-h-0' },
})
export class WidgetView {
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);

  readonly projectId = input.required<string>();
  readonly chartType = input.required<ChartType>();
  /** The query to show; null while it is incomplete, which shows `placeholder`. */
  readonly config = input.required<WidgetConfig | null>();
  /** False while nothing is published: the widget says so without asking. */
  readonly enabled = input(true);
  /** Wait this long after the last change before asking (a draft being typed); 0 asks at once. */
  readonly debounceMs = input(0);
  readonly placeholder = input('');
  /** Every widget's table at once; the widget's own toggle can still change it afterwards. */
  readonly allTables = input(false);

  protected readonly asTable = signal(false);
  protected readonly state = signal<State>({ kind: 'idle' });

  protected readonly result = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.result : null;
  });
  protected readonly error = computed(() => {
    const state = this.state();
    return state.kind === 'error' ? state.message : '';
  });
  protected readonly idle = computed(() => (this.enabled() ? this.placeholder() : this.transloco.translate('indicators.widget.unpublished')));
  protected readonly axisKind = computed(() => {
    const type = this.chartType();
    return type === 'bar' || type === 'column' || type === 'line' || type === 'histogram' ? type : 'column';
  });

  private readonly key = computed(() => {
    const config = this.config();
    return this.enabled() && config ? JSON.stringify(queryFor(this.chartType(), config)) : null;
  });

  constructor() {
    effect(() => this.asTable.set(this.allTables()));
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
                switchMap(() => this.service.query(this.projectId(), JSON.parse(key) as WidgetConfig)),
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
