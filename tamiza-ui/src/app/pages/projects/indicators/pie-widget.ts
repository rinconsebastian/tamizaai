import { DecimalPipe, PercentPipe } from '@angular/common';
import { LOCALE_ID, Component, ElementRef, computed, inject, input, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { observeSize, pivot } from './chart-data';
import { plainNumber, seriesColor, seriesCount, slicePath } from './chart-scales';
import { TableModel, WidgetTable } from './widget-table';

type Translate = (key: string, params?: Record<string, unknown>) => string;

interface Slice {
  name: string;
  value: number;
  share: number;
  color: string;
  /** Null for a slice that is the whole circle. */
  d: string | null;
}

/**
 * A pie chart (design D5): one slice per group value of the one indicator, sized by its share of the total. Slices run from the
 * largest; past the eighth color the smallest fold into one "Other" slice. Values at or below zero have no share and are only
 * counted, and listed in the table.
 */
@Component({
  selector: 'app-pie-widget',
  imports: [DecimalPipe, PercentPipe, TranslocoDirective, WidgetTable],
  template: `
    <ng-container *transloco="let t">
      <p class="shrink-0 text-xs text-body" data-testid="chart-caption">{{ t('indicators.chart.caption', { field: t('indicators.valueFields.' + config().valueField), group: config().groupBy }) }}</p>
      @if (asTable()) {
        <app-widget-table class="flex-1 overflow-auto" [model]="table(t)" />
      } @else {
        <div #box class="relative min-h-0 flex-1" data-testid="chart-box">
          <svg role="img" class="absolute inset-0 block" [attr.width]="geometry().width" [attr.height]="geometry().height" [attr.viewBox]="'0 0 ' + geometry().width + ' ' + geometry().height" [attr.aria-label]="summary(t)" data-testid="chart-svg">
            @for (slice of slices(); track slice.name) {
              @if (slice.d) {
                <path class="stroke-neutral-primary-soft hover:opacity-80" stroke-width="2" [attr.d]="slice.d" [style.fill]="slice.color" data-testid="pie-slice">
                  <title>{{ sliceTitle(slice) }}</title>
                </path>
              } @else {
                <circle class="stroke-neutral-primary-soft hover:opacity-80" stroke-width="2" [attr.cx]="geometry().cx" [attr.cy]="geometry().cy" [attr.r]="geometry().radius" [style.fill]="slice.color" data-testid="pie-slice">
                  <title>{{ sliceTitle(slice) }}</title>
                </circle>
              }
            }
          </svg>
        </div>
        <ul class="max-h-28 shrink-0 overflow-y-auto text-xs text-body" data-testid="chart-legend">
          @for (slice of slices(); track slice.name) {
            <li class="flex items-center gap-1.5 py-0.5" data-testid="legend-item">
              <span aria-hidden="true" class="inline-block size-2.5 shrink-0 rounded-sm" [style.background]="slice.color"></span>
              <span class="min-w-0 flex-1 break-all">{{ slice.name }}</span>
              <span class="tabular-nums">{{ slice.value | number: '1.0-2' }} · {{ slice.share | percent: '1.0-1' }}</span>
            </li>
          }
        </ul>
        @if (notDrawn() > 0) {
          <p class="shrink-0 text-xs text-body" data-testid="pie-skipped">{{ t('indicators.pie.skipped', { count: notDrawn() }) }}</p>
        }
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class PieWidget {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  readonly rows = input.required<QueryRow[]>();
  readonly config = input.required<WidgetConfig>();
  readonly asTable = input(false);

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  private readonly size = observeSize(() => this.box());
  private readonly plain = plainNumber(this.locale);

  private readonly all = computed(() => {
    const data = pivot(this.rows(), this.config().indicators, this.transloco.translate('indicators.chart.noGroup'), 'listed');
    const values = data.series[0]?.values ?? [];
    return data.categories.map((name, i) => ({ name, value: values[i] ?? 0 }));
  });

  protected readonly notDrawn = computed(() => this.all().filter((entry) => entry.value <= 0).length);

  protected readonly slices = computed<Slice[]>(() => {
    const positive = this.all()
      .filter((entry) => entry.value > 0)
      .sort((a, b) => b.value - a.value);
    const total = positive.reduce((sum, entry) => sum + entry.value, 0);
    if (!total) {
      return [];
    }
    let entries = positive;
    if (positive.length > seriesCount) {
      const rest = positive.slice(seriesCount - 1);
      entries = [...positive.slice(0, seriesCount - 1), { name: this.transloco.translate('indicators.pie.other'), value: rest.reduce((sum, entry) => sum + entry.value, 0) }];
    }
    const { cx, cy, radius } = this.geometry();
    let angle = 0;
    return entries.map((entry, i) => {
      const share = entry.value / total;
      const next = angle + share * 2 * Math.PI;
      const d = entries.length === 1 ? null : slicePath(cx, cy, radius, angle, next);
      angle = next;
      return { ...entry, share, color: seriesColor(i), d };
    });
  });

  protected readonly geometry = computed(() => {
    const width = Math.max(120, this.size().width);
    const height = Math.max(100, this.size().height);
    return { width, height, cx: width / 2, cy: height / 2, radius: Math.max(20, Math.min(width, height) / 2 - 6) };
  });

  protected sliceTitle(slice: Slice): string {
    return `${slice.name}: ${this.plain(slice.value)} (${new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 }).format(slice.share)})`;
  }

  protected summary(t: Translate): string {
    const slices = this.slices();
    if (!slices.length) {
      return t('indicators.chart.summaryEmpty', { indicators: this.config().indicators.join(', ') });
    }
    return t('indicators.chart.summary.pie', {
      indicators: this.config().indicators.join(', '),
      group: this.config().groupBy,
      count: slices.length,
      top: slices[0].name,
      share: new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 }).format(slices[0].share),
    });
  }

  protected table(t: Translate): TableModel {
    const total = this.all().reduce((sum, entry) => sum + Math.max(0, entry.value), 0);
    return {
      headers: [this.config().groupBy, this.config().indicators[0] ?? '', t('indicators.pie.share')],
      rows: this.all().map((entry) => [entry.name, entry.value, entry.value > 0 && total ? `${this.plain((entry.value / total) * 100)}%` : '—']),
    };
  }
}
