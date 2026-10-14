import { LOCALE_ID, Component, ElementRef, computed, inject, input, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { QueryRow, WidgetConfig, defaultBuckets } from '../../../core/projects/indicators.service';

import { Pivot, histogram, observeSize, pivot } from './chart-data';
import { bandScale, charWidth, compactNumber, linePath, linearScale, niceAxis, plainNumber, seriesColor, truncate, Point } from './chart-scales';
import { TableModel, WidgetTable } from './widget-table';

export type AxisKind = 'bar' | 'column' | 'line' | 'histogram';

interface Line {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  zero: boolean;
}
interface Label {
  x: number;
  y: number;
  text: string;
  anchor: 'start' | 'middle' | 'end';
  title?: string;
}
interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
  color: string;
  title: string;
}
interface Path {
  d: string;
  color: string;
  name: string;
}
interface Dot {
  cx: number;
  cy: number;
  color: string;
  title: string;
}

type Translate = (key: string, params?: Record<string, unknown>) => string;

const maxThickness = 40;
const minSlot = 14;

/**
 * Bar (horizontal), column (vertical), line and histogram charts as inline SVG (design D5), one series per indicator. The SVG is
 * drawn at the pixel size of its box, so text keeps one size however the widget is resized; `role="img"` with a computed summary
 * and a table of the same numbers stand in for it to assistive technology.
 */
@Component({
  selector: 'app-axis-chart',
  imports: [TranslocoDirective, WidgetTable],
  template: `
    <ng-container *transloco="let t">
      <p class="shrink-0 text-xs text-body" data-testid="chart-caption">{{ caption(t) }}</p>
      @if (asTable()) {
        <app-widget-table class="flex-1 overflow-auto" [model]="table(t)" />
      } @else {
        <div #box class="relative min-h-0 flex-1" data-testid="chart-box">
          <svg role="img" class="absolute inset-0 block" [attr.width]="layout().width" [attr.height]="layout().height" [attr.viewBox]="'0 0 ' + layout().width + ' ' + layout().height" [attr.aria-label]="summary(t)" data-testid="chart-svg">
            @for (line of layout().grid; track $index) {
              <line [attr.x1]="line.x1" [attr.y1]="line.y1" [attr.x2]="line.x2" [attr.y2]="line.y2" [class]="line.zero ? 'stroke-default-medium' : 'stroke-default'" stroke-width="1" />
            }
            @for (tick of layout().ticks; track $index) {
              <text class="fill-body" font-size="11" [attr.x]="tick.x" [attr.y]="tick.y" [attr.text-anchor]="tick.anchor">{{ tick.text }}</text>
            }
            @for (label of layout().labels; track $index) {
              <text class="fill-body" font-size="11" [attr.x]="label.x" [attr.y]="label.y" [attr.text-anchor]="label.anchor">
                @if (label.title) {
                  <title>{{ label.title }}</title>
                }
                {{ label.text }}
              </text>
            }
            @for (rect of layout().rects; track $index) {
              <rect class="hover:opacity-80" rx="2" [attr.x]="rect.x" [attr.y]="rect.y" [attr.width]="rect.width" [attr.height]="rect.height" [style.fill]="rect.color" data-testid="chart-mark">
                <title>{{ rect.title }}</title>
              </rect>
            }
            @for (path of layout().lines; track $index) {
              <path fill="none" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" [attr.d]="path.d" [style.stroke]="path.color" data-testid="chart-line">
                <title>{{ path.name }}</title>
              </path>
            }
            @for (dot of layout().dots; track $index) {
              <circle r="4" stroke-width="2" class="stroke-neutral-primary-soft" [attr.cx]="dot.cx" [attr.cy]="dot.cy" [style.fill]="dot.color" data-testid="chart-dot">
                <title>{{ dot.title }}</title>
              </circle>
            }
          </svg>
        </div>
        @if (data().series.length > 1) {
          <ul class="flex shrink-0 flex-wrap gap-x-4 gap-y-1 text-xs text-body" data-testid="chart-legend">
            @for (series of data().series; track series.name; let i = $index) {
              <li class="inline-flex items-center gap-1.5" data-testid="legend-item">
                <span aria-hidden="true" class="inline-block size-2.5 rounded-sm" [style.background]="color(i)"></span>
                <span class="break-all">{{ series.name }}</span>
              </li>
            }
          </ul>
        }
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class AxisChart {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  readonly kind = input.required<AxisKind>();
  readonly rows = input.required<QueryRow[]>();
  readonly config = input.required<WidgetConfig>();
  readonly asTable = input(false);

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  protected readonly size = observeSize(() => this.box());

  private readonly compact = compactNumber(this.locale);
  private readonly plain = plainNumber(this.locale);

  /** Categories and series as the chart plots them: group values for bar, column and line; value ranges for a histogram. */
  protected readonly data = computed<Pivot & { ranges: boolean }>(() => {
    const kind = this.kind();
    const config = this.config();
    if (kind === 'histogram') {
      const { buckets, names } = histogram(this.rows(), config.indicators, config.buckets ?? defaultBuckets);
      return {
        ranges: true,
        categories: buckets.map((b) => `${this.plain(b.from)}–${this.plain(b.to)}`),
        series: names.map((name, i) => ({ name, values: buckets.map((b) => b.counts[i]) })),
      };
    }
    const empty = this.transloco.translate('indicators.chart.noGroup');
    return { ranges: false, ...pivot(this.rows(), config.indicators, empty, kind === 'line' ? 'natural' : 'listed') };
  });

  protected readonly layout = computed(() => {
    const kind = this.kind();
    const { categories, series } = this.data();
    const w = Math.max(160, this.size().width);
    const h = Math.max(120, this.size().height);
    const count = categories.length;
    const numbers = series.flatMap((s) => s.values).filter((v): v is number => v !== null);
    const low = Math.min(0, ...numbers);
    const high = Math.max(0, ...numbers);
    const axis = low === 0 && high === 0 ? niceAxis(0, 1) : niceAxis(low, high);
    const grid: Line[] = [];
    const ticks: Label[] = [];
    const labels: Label[] = [];
    const rects: Rect[] = [];
    const lines: Path[] = [];
    const dots: Dot[] = [];
    const slots = Math.max(1, series.length);
    const titleOf = (category: string, name: string, value: number) => `${category} · ${name}: ${this.plain(value)}`;
    const top = 8;
    const right = 14;

    if (kind === 'bar') {
      const longest = Math.max(0, ...categories.map((c) => c.length));
      const labelChars = Math.max(3, Math.min(Math.floor((w * 0.35) / charWidth), longest));
      const left = labelChars * charWidth + 14;
      const bottom = 24;
      const x = linearScale([axis.min, axis.max], [left, w - right]);
      const bands = bandScale(count, [top, h - bottom], 0.25);
      const stride = Math.max(1, Math.ceil(minSlot / (bands.step || 1)));
      for (const tick of axis.ticks) {
        grid.push({ x1: x(tick), y1: top, x2: x(tick), y2: h - bottom, zero: tick === 0 });
        ticks.push({ x: x(tick), y: h - 8, text: this.compact(tick), anchor: 'middle' });
      }
      const sub = bands.bandwidth / slots;
      const thickness = Math.min(maxThickness, Math.max(1, sub - (slots > 1 ? 2 : 0)));
      categories.forEach((category, i) => {
        if (i % stride === 0) {
          labels.push({ x: left - 6, y: bands.start(i) + bands.bandwidth / 2 + 4, text: truncate(category, labelChars), anchor: 'end', title: truncate(category, labelChars) === category ? undefined : category });
        }
        series.forEach((s, j) => {
          const value = s.values[i];
          if (value === null) return;
          const zero = x(0);
          const end = x(value);
          rects.push({
            x: Math.min(zero, end),
            y: bands.start(i) + j * sub + (sub - thickness) / 2,
            width: Math.max(1, Math.abs(end - zero)),
            height: thickness,
            color: seriesColor(j),
            title: titleOf(category, s.name, value),
          });
        });
      });
      return { width: w, height: h, grid, ticks, labels, rects, lines, dots };
    }

    const tickTexts = axis.ticks.map((tick) => this.compact(tick));
    const left = Math.max(...tickTexts.map((t) => t.length)) * charWidth + 14;
    const bottom = 26;
    const y = linearScale([axis.min, axis.max], [h - bottom, top]);
    const bands = bandScale(count, [left, w - right], kind === 'line' ? 0 : kind === 'histogram' ? 0.06 : 0.3);
    axis.ticks.forEach((tick, i) => {
      grid.push({ x1: left, y1: y(tick), x2: w - right, y2: y(tick), zero: tick === 0 });
      ticks.push({ x: left - 6, y: y(tick) + 4, text: tickTexts[i], anchor: 'end' });
    });
    const longest = Math.max(0, ...categories.map((c) => c.length));
    const stride = Math.max(1, Math.ceil((Math.min(longest, 14) * charWidth + 6) / (bands.step || 1)));
    const labelChars = Math.max(2, Math.floor((stride * bands.step) / charWidth) - 1);
    categories.forEach((category, i) => {
      if (i % stride === 0) {
        labels.push({ x: bands.start(i) + bands.bandwidth / 2, y: h - 8, text: truncate(category, labelChars), anchor: 'middle', title: truncate(category, labelChars) === category ? undefined : category });
      }
    });

    if (kind === 'line') {
      series.forEach((s, j) => {
        const color = seriesColor(j);
        const segments: Point[][] = [[]];
        s.values.forEach((value, i) => {
          if (value === null) {
            if (segments[segments.length - 1].length) segments.push([]);
            return;
          }
          const point = { x: bands.start(i) + bands.bandwidth / 2, y: y(value) };
          segments[segments.length - 1].push(point);
          if (count <= 40 || (s.values[i - 1] ?? null) === null) {
            dots.push({ cx: point.x, cy: point.y, color, title: titleOf(categories[i], s.name, value) });
          }
        });
        lines.push({ d: segments.filter((p) => p.length > 1).map(linePath).join(' '), color, name: s.name });
      });
      return { width: w, height: h, grid, ticks, labels, rects, lines, dots };
    }

    const sub = bands.bandwidth / slots;
    const thickness = Math.min(maxThickness, Math.max(1, sub - (slots > 1 ? 2 : 0)));
    categories.forEach((category, i) => {
      series.forEach((s, j) => {
        const value = s.values[i];
        if (value === null) return;
        const zero = y(0);
        const end = y(value);
        rects.push({
          x: bands.start(i) + j * sub + (sub - thickness) / 2,
          y: Math.min(zero, end),
          width: thickness,
          height: Math.max(1, Math.abs(end - zero)),
          color: seriesColor(j),
          title: titleOf(category, s.name, value),
        });
      });
    });
    return { width: w, height: h, grid, ticks, labels, rects, lines, dots };
  });

  protected color(index: number): string {
    return seriesColor(index);
  }

  protected caption(t: Translate): string {
    const config = this.config();
    return this.kind() === 'histogram'
      ? t('indicators.chart.captionHistogram', { field: t(`indicators.valueFields.${config.valueField}`), buckets: config.buckets ?? defaultBuckets })
      : t('indicators.chart.caption', { field: t(`indicators.valueFields.${config.valueField}`), group: config.groupBy });
  }

  protected summary(t: Translate): string {
    const { categories, series } = this.data();
    const config = this.config();
    const names = series.map((s) => s.name).join(', ');
    const entries = series.flatMap((s, j) => s.values.map((value, i) => ({ value, category: categories[i], series: j }))).filter((e): e is { value: number; category: string; series: number } => e.value !== null);
    if (!entries.length) {
      return t('indicators.chart.summaryEmpty', { indicators: names });
    }
    const top = entries.reduce((a, b) => (b.value > a.value ? b : a));
    const low = entries.reduce((a, b) => (b.value < a.value ? b : a));
    return t(`indicators.chart.summary.${this.kind()}`, {
      indicators: names,
      group: config.groupBy,
      count: categories.length,
      min: this.plain(low.value),
      max: this.plain(top.value),
      top: top.category,
    });
  }

  protected table(t: Translate): TableModel {
    const { categories, series, ranges } = this.data();
    const first = ranges ? t('indicators.chart.range') : this.config().groupBy;
    return {
      headers: [first, ...series.map((s) => (ranges ? t('indicators.chart.rowsOf', { indicator: s.name }) : s.name))],
      rows: categories.map((category, i) => [category, ...series.map((s) => s.values[i])]),
    };
  }
}
