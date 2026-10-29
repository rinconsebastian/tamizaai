import { LOCALE_ID, Component, ElementRef, computed, inject, input, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { QueryRow, WidgetConfig, defaultBuckets } from '../../../core/projects/indicators.service';

import { histogram, observeSize, pivot } from './chart-data';
import { bandScale, charWidth, compactNumber, linePath, linearScale, niceAxis, seriesColor, truncate, Point } from './chart-scales';
import { ValueFormatter, plotted } from './format';
import { TableModel, WidgetTable } from './widget-table';
import { countKey, isSingular } from '../../../core/i18n/plural';

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
 * Bar (horizontal), column (vertical), line and histogram charts as inline SVG (design D5), one series: the widget's indicator.
 * Values are written with the indicator's format and unit (design D6). The SVG is drawn at the pixel size of its box, so text
 * keeps one size however the widget is resized; `role="img"` with a computed summary and a table of the same numbers stand in
 * for it to assistive technology.
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
          <svg role="img" class="tmz-chart-svg absolute inset-0 block" [attr.width]="layout().width" [attr.height]="layout().height" [attr.viewBox]="'0 0 ' + layout().width + ' ' + layout().height" [attr.aria-label]="summary(t)" data-testid="chart-svg">
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
  /** The indicator's label and the formatter of its values. */
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();
  readonly asTable = input(false);

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  protected readonly size = observeSize(() => this.box());

  /** Counts of rows (a histogram's axis) are not values of the indicator, so they are written plainly. */
  private readonly compact = compactNumber(this.locale);
  private readonly whole = new Intl.NumberFormat(this.locale, { maximumFractionDigits: 0 });

  /** Categories and values as the chart plots them: group values for bar, column and line; value ranges for a histogram. */
  protected readonly data = computed(() => {
    const kind = this.kind();
    if (kind === 'histogram') {
      const buckets = histogram(this.rows(), this.config().buckets ?? defaultBuckets);
      const format = this.format();
      return {
        ranges: true,
        categories: buckets.map((b) => `${format.plain(b.from)}–${format.plain(b.to)}`),
        values: buckets.map((b) => b.count),
      };
    }
    const empty = this.transloco.translate('indicators.chart.noGroup');
    return { ranges: false, ...pivot(this.rows(), empty, kind === 'line' ? 'natural' : 'listed') };
  });

  protected readonly layout = computed(() => {
    const kind = this.kind();
    const { categories, values, ranges } = this.data();
    const format = this.format();
    const tickText = (value: number) => (ranges ? this.compact(value) : format.tick(value));
    const valueText = (value: number) => (ranges ? this.transloco.translate(isSingular(value, this.transloco.getActiveLang()) ? 'indicators.chart.rowCountOne' : 'indicators.chart.rowCount', { count: this.whole.format(value) }) : format(value));
    const w = Math.max(160, this.size().width);
    const h = Math.max(120, this.size().height);
    const count = categories.length;
    const low = Math.min(0, ...values);
    const high = Math.max(0, ...values);
    const axis = low === 0 && high === 0 ? niceAxis(0, 1) : niceAxis(low, high);
    const grid: Line[] = [];
    const ticks: Label[] = [];
    const labels: Label[] = [];
    const rects: Rect[] = [];
    const lines: Path[] = [];
    const dots: Dot[] = [];
    const color = seriesColor(0);
    const titleOf = (category: string, value: number) => `${category}: ${valueText(value)}`;
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
        ticks.push({ x: x(tick), y: h - 8, text: tickText(tick), anchor: 'middle' });
      }
      const thickness = Math.min(maxThickness, Math.max(1, bands.bandwidth));
      categories.forEach((category, i) => {
        if (i % stride === 0) {
          labels.push({ x: left - 6, y: bands.start(i) + bands.bandwidth / 2 + 4, text: truncate(category, labelChars), anchor: 'end', title: truncate(category, labelChars) === category ? undefined : category });
        }
        const zero = x(0);
        const end = x(values[i]);
        rects.push({
          x: Math.min(zero, end),
          y: bands.start(i) + (bands.bandwidth - thickness) / 2,
          width: Math.max(1, Math.abs(end - zero)),
          height: thickness,
          color,
          title: titleOf(category, values[i]),
        });
      });
      return { width: w, height: h, grid, ticks, labels, rects, lines, dots };
    }

    const tickTexts = axis.ticks.map((tick) => tickText(tick));
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
      const points: Point[] = values.map((value, i) => ({ x: bands.start(i) + bands.bandwidth / 2, y: y(value) }));
      points.forEach((point, i) => {
        if (count <= 40) {
          dots.push({ cx: point.x, cy: point.y, color, title: titleOf(categories[i], values[i]) });
        }
      });
      lines.push({ d: points.length > 1 ? linePath(points) : '', color, name: this.label() });
      return { width: w, height: h, grid, ticks, labels, rects, lines, dots };
    }

    const thickness = Math.min(maxThickness, Math.max(1, bands.bandwidth));
    categories.forEach((category, i) => {
      const zero = y(0);
      const end = y(values[i]);
      rects.push({
        x: bands.start(i) + (bands.bandwidth - thickness) / 2,
        y: Math.min(zero, end),
        width: thickness,
        height: Math.max(1, Math.abs(end - zero)),
        color,
        title: titleOf(category, values[i]),
      });
    });
    return { width: w, height: h, grid, ticks, labels, rects, lines, dots };
  });

  protected caption(t: Translate): string {
    const config = this.config();
    const what = plotted(config, this.label());
    return this.kind() === 'histogram'
      ? t('indicators.chart.captionHistogram', { what, buckets: config.buckets ?? defaultBuckets })
      : t('indicators.chart.caption', { what, group: config.groupBy });
  }

  protected summary(t: Translate): string {
    const { categories, values } = this.data();
    const config = this.config();
    const what = plotted(config, this.label());
    if (!values.length) {
      return t('indicators.chart.summaryEmpty', { what });
    }
    const format = this.format();
    const top = values.indexOf(Math.max(...values));
    return t(countKey(`indicators.chart.summary.${this.kind()}`, categories.length, this.transloco.getActiveLang()), {
      what,
      group: config.groupBy,
      count: categories.length,
      min: this.data().ranges ? this.whole.format(Math.min(...values)) : format(Math.min(...values)),
      max: this.data().ranges ? this.whole.format(Math.max(...values)) : format(Math.max(...values)),
      top: categories[top],
    });
  }

  protected table(t: Translate): TableModel {
    const { categories, values, ranges } = this.data();
    const what = plotted(this.config(), this.label());
    const format = this.format();
    return {
      headers: [ranges ? t('indicators.chart.range') : (this.config().groupBy ?? ''), ranges ? t('indicators.chart.rowsOf', { what }) : what],
      rows: categories.map((category, i) => [category, ranges ? this.whole.format(values[i]) : format(values[i])]),
    };
  }
}
