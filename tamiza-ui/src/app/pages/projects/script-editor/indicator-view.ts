import { LOCALE_ID, Component, computed, inject, input, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import type { IndicatorMetadata, IndicatorTestView } from '../../../core/projects/pipeline.service';
import { Alert, Badge, Input } from '../../../shared/ui';
import { AxisChart } from '../indicators/axis-chart';
import { ValueFormatter, valueFormatter } from '../indicators/format';
import { StatWidget } from '../indicators/stat-widget';

export interface ViewWarning {
  key: string;
  params: Record<string, string | number>;
}

// Columns whose names say they are labels of something, not figures to add up.
const dimensionLike = /^(year|month|code|id)$|_(id|code)$/;

/**
 * What an indicator's table does that its author may not expect on the dashboard. The facts come from the worker; the
 * format is the one being edited, which is why the warnings are worked out here and not on the server.
 */
export function viewWarnings(facts: IndicatorTestView, format: ValueFormatter, indicatorFormat: IndicatorMetadata['format']): ViewWarning[] {
  const warnings: ViewWarning[] = [];
  const measure = facts.defaultMeasure ?? '';
  if (facts.rows === 0) {
    warnings.push({ key: 'noRows', params: {} });
  }
  if (!facts.measures.includes('value') && measure) {
    warnings.push({ key: 'noValue', params: { measure } });
  }
  if (facts.measures.includes('numerator') !== facts.measures.includes('denominator')) {
    warnings.push({ key: 'ratioIncomplete', params: {} });
  }
  if (facts.defaultMeasureNulls > 0) {
    warnings.push({ key: 'emptyValues', params: { count: facts.defaultMeasureNulls, measure } });
  }
  if (indicatorFormat === 'percent' && facts.defaultMeasureMax !== null && facts.defaultMeasureMax > 1) {
    warnings.push({ key: 'percentAboveOne', params: { max: facts.defaultMeasureMax, shown: format(facts.defaultMeasureMax) } });
  }
  for (const column of facts.measures) {
    if (dimensionLike.test(column)) {
      warnings.push({ key: 'dimensionAsMeasure', params: { column } });
    }
  }
  if (facts.duplicateRows > 0) {
    warnings.push({ key: 'duplicateRows', params: { count: facts.duplicateRows } });
  }
  return warnings;
}

/**
 * An indicator test's table as the indicators dashboard will offer it: its measures, ratio, dimensions and geometry
 * columns, the figures a stat tile would show, a bar chart by a chosen dimension, and warnings. The tile and the chart
 * are the dashboard's own components, formatted with the format and unit being edited, so changing them re-draws the
 * view without a new test.
 */
@Component({
  selector: 'app-indicator-view',
  imports: [TranslocoDirective, Alert, Badge, Input, AxisChart, StatWidget],
  template: `
    <section class="flex flex-col gap-3 rounded-base border border-default p-3" *transloco="let t" aria-labelledby="indicator-view-title" data-testid="indicator-view">
      <h3 id="indicator-view-title" class="font-medium text-heading">{{ t('scriptTests.view.title') }}</h3>

      @for (warning of warnings(); track warning.key + ':' + $index) {
        <tmz-alert tone="warning" data-testid="view-warning">{{ t('scriptTests.view.warnings.' + warning.key, warning.params) }}</tmz-alert>
      }

      <dl class="grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
        <dt class="text-body">{{ t('scriptTests.view.measures') }}</dt>
        <dd class="flex flex-wrap gap-1" data-testid="view-measures">
          @for (measure of facts().measures; track measure) {
            <tmz-badge [tone]="measure === facts().defaultMeasure ? 'brand' : 'neutral'">{{ measure }}</tmz-badge>
          }
          @if (facts().defaultMeasure) {
            <span class="text-body">{{ t('scriptTests.view.defaultMeasure', { measure: facts().defaultMeasure }) }}</span>
          }
        </dd>
        <dt class="text-body">{{ t('scriptTests.view.ratio') }}</dt>
        <dd data-testid="view-ratio">{{ facts().ratio ? t('scriptTests.view.ratioYes') : t('scriptTests.view.ratioNo') }}</dd>
        <dt class="text-body">{{ t('scriptTests.view.dimensions') }}</dt>
        <dd class="flex flex-wrap gap-1" data-testid="view-dimensions">
          @for (dimension of facts().dimensions; track dimension) {
            <tmz-badge tone="neutral">{{ dimension }}</tmz-badge>
          } @empty {
            <span class="text-body">{{ t('scriptTests.view.none') }}</span>
          }
        </dd>
        @if (facts().geometry.length) {
          <dt class="text-body">{{ t('scriptTests.view.geometry') }}</dt>
          <dd class="flex flex-wrap gap-1" data-testid="view-geometry">
            @for (column of facts().geometry; track column) {
              <tmz-badge tone="success">{{ column }}</tmz-badge>
            }
          </dd>
        }
      </dl>

      <div class="grid gap-3 sm:grid-cols-2">
        @if (facts().defaultMeasure; as measure) {
          <div class="h-28 rounded-base border border-default p-3" data-testid="view-measure-tile">
            <app-stat-widget [rows]="measureTotal()" [config]="measureConfig()" [label]="label()" [format]="format()" />
          </div>
        }
        @if (facts().ratio) {
          <div class="h-28 rounded-base border border-default p-3" data-testid="view-ratio-tile">
            <app-stat-widget [rows]="ratioTotal()" [config]="ratioConfig()" [label]="label()" [format]="format()" />
          </div>
        }
      </div>

      @if (chartable().length) {
        <div class="flex flex-col gap-2">
          <div class="flex flex-wrap items-center gap-3">
            <label class="text-sm font-medium text-heading" for="view-dimension">{{ t('scriptTests.view.chartBy') }}</label>
            <select tmzInput id="view-dimension" class="sm:max-w-xs" [value]="dimension()" (change)="chosenDimension.set($any($event.target).value)" data-testid="view-dimension">
              @for (name of chartable(); track name) {
                <option [value]="name">{{ name }}</option>
              }
            </select>
            @if (facts().ratio) {
              <label class="text-sm font-medium text-heading" for="view-plot">{{ t('scriptTests.view.plot') }}</label>
              <select tmzInput id="view-plot" class="sm:max-w-xs" [value]="plot()" (change)="chosenPlot.set($any($event.target).value)" data-testid="view-plot">
                <option value="measure">{{ facts().defaultMeasure }}</option>
                <option value="ratio">{{ t('scriptTests.view.plotRatio') }}</option>
              </select>
            }
          </div>
          <div class="h-72" data-testid="view-chart">
            <app-axis-chart kind="bar" [rows]="chartRows()" [config]="plot() === 'ratio' ? ratioConfig() : measureConfig()" [label]="label()" [format]="format()" />
          </div>
        </div>
      }
      @for (name of tooMany(); track name) {
        <p class="text-sm text-body" data-testid="view-too-many">{{ t('scriptTests.view.tooMany', { dimension: name, max: 50 }) }}</p>
      }
    </section>
  `,
})
export class IndicatorView {
  readonly facts = input.required<IndicatorTestView>();
  /** The metadata being edited: the format and unit the figures are written with. */
  readonly metadata = input.required<IndicatorMetadata>();

  private readonly locale = inject(LOCALE_ID);
  protected readonly chosenDimension = signal<string | null>(null);
  protected readonly chosenPlot = signal<'measure' | 'ratio'>('measure');

  protected readonly format = computed(() => valueFormatter({ format: this.metadata().format, unit: this.metadata().unit.trim() || null }, this.locale));
  protected readonly label = computed(() => this.metadata().label.trim() || this.facts().defaultMeasure || '');
  protected readonly warnings = computed(() => viewWarnings(this.facts(), this.format(), this.metadata().format));

  /** The dimensions a bar chart can be drawn by: those with at most 50 values. */
  protected readonly chartable = computed(() => Object.entries(this.facts().charts).filter(([, chart]) => chart.groups !== null).map(([name]) => name));
  protected readonly tooMany = computed(() => Object.entries(this.facts().charts).filter(([, chart]) => chart.groups === null).map(([name]) => name));
  protected readonly dimension = computed(() => {
    const chosen = this.chosenDimension();
    const options = this.chartable();
    return chosen && options.includes(chosen) ? chosen : (options.includes(this.facts().defaultDimension ?? '') ? this.facts().defaultDimension! : options[0]);
  });
  protected readonly plot = computed(() => (this.facts().ratio ? this.chosenPlot() : 'measure'));

  protected readonly measureConfig = computed<WidgetConfig>(() => this.config({ measure: this.facts().defaultMeasure, ratio: null }));
  protected readonly ratioConfig = computed<WidgetConfig>(() => this.config({ measure: null, ratio: { numerator: 'numerator', denominator: 'denominator' } }));

  /** The default measure summed over the whole table, as a stat tile shows it. */
  protected readonly measureTotal = computed<QueryRow[]>(() => {
    const measure = this.facts().defaultMeasure;
    return [{ group: null, value: measure ? (this.facts().totals[measure] ?? 0) : 0, geometry: null }];
  });

  /** The ratio of the sums, with no figure when the denominator sums to zero. */
  protected readonly ratioTotal = computed<QueryRow[]>(() => {
    const { numerator, denominator } = this.facts().totals;
    return denominator ? [{ group: null, value: (numerator ?? 0) / denominator, geometry: null }] : [];
  });

  /** One bar per value of the chosen dimension: the measure summed, or the ratio of sums (none where the denominator is zero). */
  protected readonly chartRows = computed<QueryRow[]>(() => {
    const groups = this.facts().charts[this.dimension()]?.groups ?? [];
    if (this.plot() === 'ratio') {
      return groups.filter((g) => g.denominator).map((g) => ({ group: g.group, value: (g.numerator ?? 0) / g.denominator!, geometry: null }));
    }
    return groups.filter((g) => g.value !== null).map((g) => ({ group: g.group, value: g.value!, geometry: null }));
  });

  private config(parts: Pick<WidgetConfig, 'measure' | 'ratio'>): WidgetConfig {
    return { indicator: '', groupBy: this.dimension() ?? null, filters: [], geometryColumn: null, buckets: null, ...parts };
  }
}
