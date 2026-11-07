import { Component, DestroyRef, ElementRef, LOCALE_ID, computed, effect, inject, input, output, untracked, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { countKey } from '../../../core/i18n/plural';
import { BoxOrientation, QueryBox, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartTooltip, ChartTooltipContent, ChartTooltips } from '../../../shared/ui';
import { ChartBases, SmallMark, isSmallBase, noChartBases, smallBaseDash, smallBaseTooltip, summaryWithSmall } from './base-figures';
import { CategoryOrders, arrange, entryOf, shown, shownValues } from './category-order';
import { ChartColors } from './chart-colors';
import { registerExportParts } from './export-parts';
import { observeSize } from './chart-data';
import { NiceAxis, bandScale, charWidth, linearScale, niceAxis, truncate } from './chart-scales';
import { ColumnNamer, ValueFormatter, named, plotted, rawNames } from './format';
import { MarkPoint, markTooltips } from './mark-tooltips';
import { Hit, Label, Line } from './series-plot';
import { TableModel, WidgetTable } from './widget-table';

type Translate = (key: string, params?: Record<string, unknown>) => string;

// A box plot (design D5 of add-boxplot-widget): one box per group on one linear scale that need not start at zero. Pure geometry over
// the boxes the API computed, so it is tested without a browser; `BoxplotChart` draws what it returns.

/** A straight mark: a median line, a whisker, a cap, an arm of the mean's cross. */
export interface BoxSeg {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface BoxShape {
  /** The box's place among the boxes, in display order. */
  g: number;
  group: string | null;
  color: string;
  /** The box from the first to the third quartile; null when they are equal, so the box is only its median line. */
  rect: { x: number; y: number; width: number; height: number } | null;
  median: BoxSeg;
  /** The two arms of the cross that marks the mean. */
  mean: BoxSeg[];
  /** The stems from the box to the whisker ends, and the caps across them. */
  whiskers: BoxSeg[];
  /** A box of fewer values than the threshold is drawn lighter with a dashed outline (add-small-sample-warnings, design D2, D10). */
  small: boolean;
}

export interface OutlierDot {
  cx: number;
  cy: number;
  color: string;
  /** The group's label, and the outlier's value as the tooltip writes it. */
  heading: string;
  text: string;
  box: number;
}

export interface BoxplotPlot {
  width: number;
  height: number;
  axis: NiceAxis;
  grid: Line[];
  ticks: Label[];
  labels: Label[];
  boxes: BoxShape[];
  outliers: OutlierDot[];
  /** Over each box and its whiskers; the outliers have hit areas of their own, drawn after these. */
  hits: Hit[];
  /** The marks by box (one each), for the tooltips and the keyboard. */
  marks: (MarkPoint | null)[][];
  /** How many outliers the boxes did not draw because they have more than the API sends. */
  notDrawn: number;
  /** How many boxes have a small base. */
  small: number;
  /** The boxes drawn dimmed, by name and number of values, in drawing order (improve-widget-text-hierarchy, design D5). */
  smallMarks: SmallMark[];
}

/** The words of a box's tooltip and of the key, already translated. */
export interface BoxplotWords {
  values: string;
  maximum: string;
  upperWhisker: string;
  q3: string;
  median: string;
  mean: string;
  q1: string;
  lowerWhisker: string;
  minimum: string;
  outliers: string;
  outlier: string;
  /** "N outliers not drawn", with the right plural. */
  notDrawn: (count: number) => string;
}

export interface BoxplotPlotInput {
  boxes: readonly QueryBox[];
  orientation: BoxOrientation;
  width: number;
  height: number;
  /** One box's color: the one kept for its group value, else the widget's. */
  color: (box: QueryBox) => string;
  groupLabel: (group: string | null) => string;
  /** The indicator's figures as a box writes them (a count with two decimals), and a whole number for the numbers of values. */
  value: ValueFormatter;
  whole: (value: number) => string;
  words: BoxplotWords;
  /** How far a flat domain (every figure equal) is padded when the figure is 0: 0.01 for a percent, 1 otherwise. */
  zeroPad: number;
  /** The threshold in force (a box's n is its number of values), and the note a box under it says; left out for no small base. */
  minN?: number | null;
  smallNote?: (n: number) => string | null;
}

const top = 8;
const right = 14;
const bottomValueAxis = 24;
const bottomLabels = 26;
const maxBoxWidth = 56;
const minHit = 12;
const outlierRadius = 3;

/**
 * The domain of the value axis: every whisker end, every mean and every outlier drawn. A domain whose figures are all equal is padded
 * in proportion to its value (a fixed pad would make 1,000,000 read the same at every tick, and a percent at 0 run from -100%), and
 * kept at 0 or above when every figure is 0 or more.
 */
export function boxDomain(boxes: readonly QueryBox[], zeroPad: number): NiceAxis {
  // A loop, not Math.min(...figures): 5,000 boxes of 100 outliers are hundreds of thousands of figures, more than a spread allows.
  let low = Infinity;
  let high = -Infinity;
  const see = (value: number) => {
    if (Number.isFinite(value)) {
      low = Math.min(low, value);
      high = Math.max(high, value);
    }
  };
  for (const box of boxes) {
    see(box.lowerWhisker);
    see(box.upperWhisker);
    see(box.mean);
    box.outliers.forEach(see);
  }
  if (low === Infinity) {
    return niceAxis(0, 1);
  }
  if (low === high) {
    const pad = Math.abs(low) * 0.05 || zeroPad;
    const wasNonNegative = low >= 0;
    low -= pad;
    high += pad;
    if (wasNonNegative) {
      low = Math.max(0, low);
    }
  }
  return niceAxis(low, high);
}

export function boxplotPlot(input: BoxplotPlotInput): BoxplotPlot {
  const { boxes, orientation, width: w, height: h, words } = input;
  const horizontal = orientation === 'horizontal';
  const count = boxes.length;
  const axis = boxDomain(boxes, input.zeroPad);
  const categories = boxes.map((box) => input.groupLabel(box.group));

  const grid: Line[] = [];
  const ticks: Label[] = [];
  const labels: Label[] = [];
  const boxesOut: BoxShape[] = [];
  const outliers: OutlierDot[] = [];
  const hits: Hit[] = [];
  const marks: (MarkPoint | null)[][] = boxes.map(() => [null]);

  // Along the value axis a position is `v`; across it, the band of a box is `bands`. `seg` and `rect` turn the two into marks.
  let v: (value: number) => number;
  let bands: ReturnType<typeof bandScale>;
  if (horizontal) {
    const longest = Math.max(0, ...categories.map((c) => c.length));
    const labelChars = Math.max(3, Math.min(Math.floor((w * 0.3) / charWidth), longest));
    const left = labelChars * charWidth + 14;
    v = linearScale([axis.min, axis.max], [left, w - right]);
    bands = bandScale(count, [top, h - bottomValueAxis], 0.3);
    const stride = Math.max(1, Math.ceil(14 / (bands.step || 1)));
    for (const tick of axis.ticks) {
      grid.push({ x1: v(tick), y1: top, x2: v(tick), y2: h - bottomValueAxis, zero: tick === 0 });
      ticks.push({ x: v(tick), y: h - 8, text: input.value.tick(tick), anchor: 'middle' });
    }
    categories.forEach((category, i) => {
      if (i % stride === 0) {
        const text = truncate(category, labelChars);
        labels.push({ x: left - 6, y: bands.start(i) + bands.bandwidth / 2 + 4, text, anchor: 'end', title: text === category ? undefined : category });
      }
    });
  } else {
    const tickTexts = axis.ticks.map((tick) => input.value.tick(tick));
    const left = Math.max(...tickTexts.map((t) => t.length)) * charWidth + 14;
    v = linearScale([axis.min, axis.max], [h - bottomLabels, top]);
    bands = bandScale(count, [left, w - right], 0.3);
    axis.ticks.forEach((tick, i) => {
      grid.push({ x1: left, y1: v(tick), x2: w - right, y2: v(tick), zero: tick === 0 });
      ticks.push({ x: left - 6, y: v(tick) + 4, text: tickTexts[i], anchor: 'end' });
    });
    const longest = Math.max(0, ...categories.map((c) => c.length));
    const stride = Math.max(1, Math.ceil((Math.min(longest, 14) * charWidth + 6) / (bands.step || 1)));
    const labelChars = Math.max(2, Math.floor((stride * bands.step) / charWidth) - 1);
    categories.forEach((category, i) => {
      if (i % stride === 0) {
        const text = truncate(category, labelChars);
        labels.push({ x: bands.start(i) + bands.bandwidth / 2, y: h - 8, text, anchor: 'middle', title: text === category ? undefined : category });
      }
    });
  }

  /** A segment from (value `a1`, across `c1`) to (value `a2`, across `c2`). */
  const seg = (a1: number, c1: number, a2: number, c2: number): BoxSeg =>
    horizontal ? { x1: a1, y1: c1, x2: a2, y2: c2 } : { x1: c1, y1: a1, x2: c2, y2: a2 };
  const asRect = (a0: number, a1: number, c0: number, size: number) =>
    horizontal ? { x: Math.min(a0, a1), y: c0, width: Math.abs(a1 - a0), height: size } : { x: c0, y: Math.min(a0, a1), width: size, height: Math.abs(a1 - a0) };
  const along = (start: number, length: number) => (length >= minHit ? { start, length } : { start: start - (minHit - length) / 2, length: minHit });

  let notDrawn = 0;
  let small = 0;
  const smallMarks: SmallMark[] = [];
  boxes.forEach((box, g) => {
    const color = input.color(box);
    const bandStart = bands.start(g);
    const size = Math.max(1, Math.min(maxBoxWidth, bands.bandwidth));
    const c0 = bandStart + (bands.bandwidth - size) / 2;
    const center = c0 + size / 2;
    const cap = size / 4;

    const q1 = v(box.q1);
    const q3 = v(box.q3);
    const median = v(box.median);
    const lower = v(box.lowerWhisker);
    const upper = v(box.upperWhisker);
    const meanAt = v(box.mean);

    const whiskers: BoxSeg[] = [];
    if (box.upperWhisker > box.q3) {
      whiskers.push(seg(q3, center, upper, center), seg(upper, center - cap, upper, center + cap));
    }
    if (box.lowerWhisker < box.q1) {
      whiskers.push(seg(q1, center, lower, center), seg(lower, center - cap, lower, center + cap));
    }
    const arm = 3.5;
    boxesOut.push({
      g,
      group: box.group,
      color,
      rect: box.q3 > box.q1 ? asRect(q1, q3, c0, size) : null,
      median: seg(median, c0, median, c0 + size),
      // A box of one value is its median line alone: its mean is the same point.
      mean: box.values > 1 ? [seg(meanAt - arm, center - arm, meanAt + arm, center + arm), seg(meanAt - arm, center + arm, meanAt + arm, center - arm)] : [],
      whiskers,
      small: isSmallBase(box.values, input.minN),
    });
    small += isSmallBase(box.values, input.minN) ? 1 : 0;
    if (isSmallBase(box.values, input.minN)) {
      smallMarks.push({ name: input.groupLabel(box.group), n: box.values });
    }

    // The hit area covers the box and its whiskers across the box's width; an outlier's own hit area is drawn after it.
    const nearest = Math.min(upper, lower, q1, q3, meanAt);
    const reach = along(nearest, Math.max(upper, lower, q1, q3, meanAt) - nearest);
    // At least minHit wide, but never wider than the band: the neighbours' areas must not cover this box.
    const acrossLength = Math.max(1, Math.min(Math.max(size, minHit), bands.step));
    const across = { start: center - acrossLength / 2, length: acrossLength };
    hits.push(
      horizontal
        ? { x: reach.start, y: across.start, width: reach.length, height: across.length, at: { g, s: 0 } }
        : { x: across.start, y: reach.start, width: across.length, height: reach.length, at: { g, s: 0 } },
    );

    const heading = categories[g];
    const hidden = box.outlierCount - box.outliers.length;
    notDrawn += Math.max(0, hidden);
    for (const value of box.outliers) {
      const a = v(value);
      outliers.push({ cx: horizontal ? a : center, cy: horizontal ? center : a, color, heading, text: input.value(value), box: g });
    }

    const line = (label: string, value: string, own?: string) => ({ label, value, align: 'end' as const, color: own });
    const content: ChartTooltipContent = {
      heading,
      lines: [
        line(words.values, input.whole(box.values)),
        line(words.maximum, input.value(box.max)),
        line(words.upperWhisker, input.value(box.upperWhisker)),
        line(words.q3, input.value(box.q3)),
        line(words.median, input.value(box.median)),
        line(words.mean, input.value(box.mean)),
        line(words.q1, input.value(box.q1)),
        line(words.lowerWhisker, input.value(box.lowerWhisker)),
        line(words.minimum, input.value(box.min)),
        line(words.outliers, input.whole(box.outlierCount)),
      ],
      footer: [hidden > 0 ? words.notDrawn(hidden) : null, input.smallNote?.(box.values) ?? null].filter((note): note is string => !!note).join(' · ') || null,
    };
    marks[g][0] = { content, x: horizontal ? median : center, y: horizontal ? center : median };
  });

  return { width: w, height: h, axis, grid, ticks, labels, boxes: boxesOut, outliers, hits, marks, notDrawn, small, smallMarks };
}

/**
 * A box plot as "Show as table" lists it, and as the scheduled emails do (design D6): one row per box, in the chart's order, with its
 * number of values, its figures and its number of outliers. The figures are written by `value` and the numbers by `whole`.
 */
export function boxTableModel(
  boxes: readonly QueryBox[],
  groupHeader: string,
  words: Pick<BoxplotWords, 'values' | 'minimum' | 'q1' | 'median' | 'q3' | 'maximum' | 'mean' | 'outliers'>,
  groupLabel: (group: string | null) => string,
  value: ValueFormatter,
  whole: (value: number) => string,
): TableModel {
  return {
    headers: [groupHeader, words.values, words.minimum, words.q1, words.median, words.q3, words.maximum, words.mean, words.outliers],
    rows: boxes.map((box) => [
      groupLabel(box.group),
      whole(box.values),
      value(box.min),
      value(box.q1),
      value(box.median),
      value(box.q3),
      value(box.max),
      value(box.mean),
      whole(box.outlierCount),
    ]),
  };
}

/**
 * A box plot (design D5 of add-boxplot-widget): one box per group on one scale, vertical with the first box on the left or horizontal
 * with it at the top. A box spans the first to the third quartile with the median as a line and the mean as a cross; its whiskers
 * reach the last values within 1.5 times the box's length, and the values beyond are drawn as points, at most 100 for each box. Each
 * box has the color kept for its group value, else the widget's color. The chart is one Tab stop: arrows step from box to box, and a
 * key under the plot says what each mark is. The outliers have a pointer tooltip of their own, not keyboard stops.
 */
@Component({
  selector: 'app-boxplot-chart',
  imports: [TranslocoDirective, WidgetTable],
  template: `
    <ng-container *transloco="let t">
      @if (asTable()) {
        <app-widget-table class="min-h-0 flex-1 overflow-auto" [model]="table(t)" />
      } @else {
        <div #box class="relative min-h-0 flex-1" data-testid="chart-box">
          <svg
            #chartSvg
            role="application"
            class="tmz-chart-svg absolute inset-0 block focus:outline-none focus-visible:ring-4 focus-visible:ring-brand-medium"
            [attr.width]="plot().width"
            [attr.height]="plot().height"
            [attr.viewBox]="'0 0 ' + plot().width + ' ' + plot().height"
            [attr.aria-label]="summary(t)"
            [attr.aria-roledescription]="t('indicators.boxplot.roleDescription')"
            [attr.aria-describedby]="marks.helpId"
            tabindex="0"
            (keydown)="marks.keydown($event)"
            (focusout)="marks.focusout($event)"
            data-testid="chart-svg"
          >
            @for (line of plot().grid; track $index) {
              <line [attr.x1]="line.x1" [attr.y1]="line.y1" [attr.x2]="line.x2" [attr.y2]="line.y2" [class]="line.zero ? 'stroke-default-medium' : 'stroke-default'" stroke-width="1" />
            }
            @for (tick of plot().ticks; track $index) {
              <text class="fill-body" font-size="11" [attr.x]="tick.x" [attr.y]="tick.y" [attr.text-anchor]="tick.anchor">{{ tick.text }}</text>
            }
            @for (label of plot().labels; track $index) {
              <text class="fill-body" font-size="11" [attr.x]="label.x" [attr.y]="label.y" [attr.text-anchor]="label.anchor">
                @if (label.title) {
                  <title>{{ label.title }}</title>
                }
                {{ label.text }}
              </text>
            }
            @for (shape of plot().boxes; track shape.g) {
              <g data-testid="chart-mark" [attr.data-group]="shape.group" [attr.data-small]="shape.small ? 'true' : null">
                @for (whisker of shape.whiskers; track $index) {
                  <line [attr.x1]="whisker.x1" [attr.y1]="whisker.y1" [attr.x2]="whisker.x2" [attr.y2]="whisker.y2" [style.stroke]="shape.color" stroke-width="1.5" />
                }
                @if (shape.rect; as rect) {
                  <rect
                    rx="2"
                    [attr.x]="rect.x"
                    [attr.y]="rect.y"
                    [attr.width]="rect.width"
                    [attr.height]="rect.height"
                    [style.fill]="shape.color"
                    [attr.fill-opacity]="shape.small ? 0.07 : 0.2"
                    [style.stroke]="shape.color"
                    [attr.stroke-width]="marks.isLifted(shape.g, 0) ? 2.5 : 1.5"
                    [attr.stroke-dasharray]="shape.small ? smallDash : null"
                    data-testid="box-rect"
                  />
                }
                <line class="stroke-heading" [attr.x1]="shape.median.x1" [attr.y1]="shape.median.y1" [attr.x2]="shape.median.x2" [attr.y2]="shape.median.y2" [attr.stroke-width]="marks.isLifted(shape.g, 0) ? 3.5 : 2" data-testid="box-median" />
                @for (arm of shape.mean; track $index) {
                  <line class="stroke-heading" [attr.x1]="arm.x1" [attr.y1]="arm.y1" [attr.x2]="arm.x2" [attr.y2]="arm.y2" stroke-width="1.5" data-testid="box-mean" />
                }
              </g>
            }
            @for (dot of plot().outliers; track $index) {
              <circle [attr.cx]="dot.cx" [attr.cy]="dot.cy" r="3" fill="none" [style.stroke]="dot.color" stroke-width="1.5" data-testid="box-outlier" />
            }
            @for (hit of plot().hits; track $index) {
              <rect
                fill="transparent"
                [attr.x]="hit.x"
                [attr.y]="hit.y"
                [attr.width]="hit.width"
                [attr.height]="hit.height"
                (pointerenter)="marks.hover(hit.at, $event)"
                (pointermove)="marks.move(hit.at, $event)"
                (pointerleave)="marks.leave(hit.at)"
                (pointerdown)="marks.tap(hit.at, $event)"
                data-testid="chart-hit"
              />
            }
            <!-- Drawn after the boxes' areas, so a point wins the pointer where they meet. Not stops of the keyboard. -->
            @for (dot of plot().outliers; track $index) {
              <circle
                fill="transparent"
                [attr.cx]="dot.cx"
                [attr.cy]="dot.cy"
                r="6"
                (pointerenter)="showOutlier(dot, $event)"
                (pointerdown)="tapOutlier(dot, $event)"
                (pointermove)="moveOutlier($event)"
                (pointerleave)="hideOutlier($event)"
                data-testid="outlier-hit"
              />
            }
          </svg>
          <p [id]="marks.helpId" class="sr-only">{{ t(orientation() === 'horizontal' ? 'indicators.series.keyHelpGroupsDown' : 'indicators.series.keyHelpGroups') }}</p>
          <p class="sr-only" role="status" aria-live="polite" data-testid="marks-announcement">{{ marks.announcement() }}</p>
        </div>
        <!-- What each mark is, and the outliers the boxes did not draw. Both print. -->
        <ul class="flex shrink-0 flex-wrap items-center gap-x-4 gap-y-1 text-xs text-body" data-testid="boxplot-key">
          <li class="flex items-center gap-1">
            <svg width="16" height="12" aria-hidden="true"><rect x="1" y="1" width="14" height="10" rx="2" fill-opacity="0.2" [style.fill]="colors().accent" [style.stroke]="colors().accent" stroke-width="1.5" /></svg>
            {{ t('indicators.boxplot.key.box') }}
          </li>
          <li class="flex items-center gap-1">
            <svg width="16" height="12" aria-hidden="true"><line class="stroke-heading" x1="1" y1="6" x2="15" y2="6" stroke-width="2" /></svg>
            {{ t('indicators.boxplot.key.median') }}
          </li>
          <li class="flex items-center gap-1">
            <svg width="16" height="12" aria-hidden="true">
              <line class="stroke-heading" x1="4.5" y1="2.5" x2="11.5" y2="9.5" stroke-width="1.5" />
              <line class="stroke-heading" x1="4.5" y1="9.5" x2="11.5" y2="2.5" stroke-width="1.5" />
            </svg>
            {{ t('indicators.boxplot.key.mean') }}
          </li>
          <li class="flex items-center gap-1">
            <svg width="16" height="12" aria-hidden="true">
              <line [style.stroke]="colors().accent" x1="8" y1="1" x2="8" y2="11" stroke-width="1.5" />
              <line [style.stroke]="colors().accent" x1="4" y1="1" x2="12" y2="1" stroke-width="1.5" />
              <line [style.stroke]="colors().accent" x1="4" y1="11" x2="12" y2="11" stroke-width="1.5" />
            </svg>
            {{ t('indicators.boxplot.key.whiskers') }}
          </li>
          <li class="flex items-center gap-1">
            <svg width="16" height="12" aria-hidden="true"><circle cx="8" cy="6" r="3" fill="none" [style.stroke]="colors().accent" stroke-width="1.5" /></svg>
            {{ t('indicators.boxplot.key.outliers') }}
          </li>
        </ul>
        @if (plot().notDrawn > 0) {
          <p class="shrink-0 text-xs text-body" data-testid="boxplot-not-drawn">{{ notDrawnText(plot().notDrawn) }}</p>
        }
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class BoxplotChart {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  readonly answer = input.required<QueryResult>();
  readonly config = input.required<WidgetConfig>();
  /** The indicator's label, and the formatter of its figures as a box plot writes them (see `boxFormat`). */
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();
  /** The name shown for each column (add-indicator-column-metadata); the columns' own names when none is given. */
  readonly names = input<ColumnNamer>(rawNames);
  /** What the widget declares for the order of its values and the text they are shown with (add-declared-category-order, design D8). */
  readonly orders = input<CategoryOrders | null>(null);
  readonly asTable = input(false);
  /** Whether the indicator is a percent: it decides how far a flat scale is padded at 0. */
  readonly percent = input(false);
  /** The colors of the widget, from the dashboard's palette and its colors kept by value. */
  readonly colors = input.required<ChartColors>();
  /** The threshold in force (a box's n is its number of values), for the small bases of the boxes (add-small-sample-warnings). */
  readonly bases = input<ChartBases>(noChartBases);
  /** How many boxes the chart draws dimmed for a small base, so the widget can explain the dimming under it (design D10, D11). */
  readonly dimmed = output<SmallMark[]>();
  protected readonly smallDash = smallBaseDash;

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  private readonly svg = viewChild<ElementRef<SVGElement>>('chartSvg');
  protected readonly size = observeSize(() => this.box());

  private readonly tr: Translate = (key, params) => this.transloco.translate(key, params);
  private readonly whole = (() => {
    const format = new Intl.NumberFormat(this.locale, { maximumFractionDigits: 0 });
    return (value: number) => format.format(value);
  })();

  protected readonly what = computed(() => plotted(this.config(), this.label(), this.names()));
  /** The group column under its name shown; the colors kept by value stay keyed by the column itself. */
  protected readonly groupShown = computed(() => named(this.names(), this.config().groupBy));
  /** The boxes in the order the widget declares for its groups (design D4); as the server sends them, without a declaration. */
  protected readonly boxes = computed(() => arrange(this.answer().boxes ?? [], (box) => entryOf(box.group), this.orders()?.groups ?? null));
  private readonly groupText = computed(() => (this.orders()?.hideGroupPrefixes ? shownValues(this.boxes().map((box) => box.group), true) : null));
  protected readonly orientation = computed<BoxOrientation>(() => (this.config().display?.orientation === 'horizontal' ? 'horizontal' : 'vertical'));
  private readonly groupLabel = (group: string | null): string =>
    shown(this.groupText(), group) ?? this.tr(this.config().groupBy ? 'indicators.chart.noGroup' : 'indicators.boxplot.allRows');
  protected notDrawnText = (count: number): string => this.tr(countKey('indicators.boxplot.notDrawn', count, this.transloco.getActiveLang()), { count });

  private words(): BoxplotWords {
    const word = (key: string) => this.tr(`indicators.boxplot.${key}`);
    return {
      values: word('values'),
      maximum: word('maximum'),
      upperWhisker: word('upperWhisker'),
      q3: word('q3'),
      median: word('median'),
      mean: word('mean'),
      q1: word('q1'),
      lowerWhisker: word('lowerWhisker'),
      minimum: word('minimum'),
      outliers: word('outliers'),
      outlier: word('outlier'),
      notDrawn: this.notDrawnText,
    };
  }

  protected readonly plot = computed(() =>
    boxplotPlot({
      boxes: this.boxes(),
      orientation: this.orientation(),
      width: Math.max(160, this.size().width),
      height: Math.max(120, this.size().height),
      color: (box) => this.colors().kept(this.config().groupBy ?? '', box.group) ?? this.colors().accent,
      groupLabel: this.groupLabel,
      value: this.format(),
      whole: this.whole,
      words: this.words(),
      zeroPad: this.percent() ? 0.01 : 1,
      minN: this.bases().minN,
      smallNote: (n) => smallBaseTooltip(n, this.bases().minN, this.locale, this.tr),
    }),
  );

  protected readonly marks = markTooltips({ host: () => this.svg()?.nativeElement, marks: () => this.plot().marks, groupsAcross: () => this.orientation() === 'vertical' });

  /** The outliers' own tooltip: pointer only, so no arrow, Home or End key ever lands on one. */
  private readonly outlierTip: ChartTooltip = inject(ChartTooltips).create();
  private kept = false;
  private watch: AbortController | null = null;

  constructor() {
    effect(() => this.dimmed.emit(this.plot().smallMarks));
    // What an image of the box plot says around its boxes (add-widget-export): the key of its marks, and the outliers it did not draw.
    registerExportParts({
      plot: () => (this.asTable() ? null : (this.svg()?.nativeElement ?? null)),
      settle: () => {
        this.marks.reset();
        this.outlierTip.close();
      },
      parts: () => {
        if (this.asTable()) {
          return { caption: null, legend: null, notes: [], stat: null, table: this.table(this.tr) };
        }
        const key = (name: string) => this.tr(`indicators.boxplot.key.${name}`);
        const notDrawn = this.plot().notDrawn;
        return {
          caption: null,
          legend: {
            kind: 'key' as const,
            accent: this.colors().accent,
            items: [
              { glyph: 'box' as const, label: key('box') },
              { glyph: 'median' as const, label: key('median') },
              { glyph: 'mean' as const, label: key('mean') },
              { glyph: 'whiskers' as const, label: key('whiskers') },
              { glyph: 'outliers' as const, label: key('outliers') },
            ],
          },
          notes: notDrawn > 0 ? [this.notDrawnText(notDrawn)] : [],
          stat: null,
          table: null,
        };
      },
    });
    inject(DestroyRef).onDestroy(() => {
      this.watch?.abort();
      this.outlierTip.destroy();
    });
    effect(() => {
      this.plot();
      untracked(() => this.outlierTip.close());
    });
  }

  protected showOutlier(dot: OutlierDot, event: MouseEvent): void {
    const content: ChartTooltipContent = {
      heading: dot.heading,
      lines: [{ label: this.tr('indicators.boxplot.outlier'), value: dot.text, align: 'end', color: dot.color }],
      footer: null,
    };
    this.outlierTip.open({ x: event.clientX, y: event.clientY }, content);
  }

  protected moveOutlier(event: MouseEvent): void {
    if (this.outlierTip.isOpen) {
      this.outlierTip.move({ x: event.clientX, y: event.clientY });
    }
  }

  /** A tap or a pen keeps the outlier's tooltip until the next touch elsewhere or a scroll, as a box's tooltip is kept (a mouse just points). */
  protected tapOutlier(dot: OutlierDot, event: PointerEvent): void {
    if (event.pointerType === 'mouse') {
      return;
    }
    this.showOutlier(dot, event);
    this.kept = true;
    this.watch?.abort();
    const abort = new AbortController();
    this.watch = abort;
    const close = (touched?: Event) => {
      if (touched?.target instanceof Element && touched.target.matches('[data-testid="outlier-hit"]')) {
        return;
      }
      this.hideOutlier();
    };
    document.addEventListener('pointerdown', close, { capture: true, passive: true, signal: abort.signal });
    document.addEventListener('scroll', () => close(), { capture: true, passive: true, signal: abort.signal });
  }

  protected hideOutlier(event?: PointerEvent): void {
    if (this.kept && event && event.pointerType !== 'mouse') {
      return; // lifting a finger leaves a tapped tooltip open
    }
    this.kept = false;
    this.watch?.abort();
    this.watch = null;
    this.outlierTip.close();
  }

  protected table(t: Translate): TableModel {
    const words = this.words();
    return boxTableModel(this.boxes(), this.groupShown() ?? t('indicators.boxplot.group'), words, this.groupLabel, this.format(), this.whole);
  }

  protected summary(t: Translate): string {
    return summaryWithSmall(this.summaryWords(t), this.plot().small, t);
  }

  private summaryWords(t: Translate): string {
    const boxes = this.boxes();
    if (!boxes.length) {
      return t('indicators.chart.summaryEmpty', { what: this.what() });
    }
    const medians = boxes.map((box) => box.median);
    const low = Math.min(...medians);
    const high = Math.max(...medians);
    if (boxes.length > 1 && low === high && this.config().groupBy) {
      return t('indicators.boxplot.summarySame', { what: this.what(), group: this.groupShown(), count: boxes.length, median: this.format()(low) });
    }
    if (!this.config().groupBy) {
      return t('indicators.boxplot.summaryAll', { what: this.what(), median: this.format()(boxes[0].median), min: this.format()(boxes[0].min), max: this.format()(boxes[0].max) });
    }
    return t(countKey('indicators.boxplot.summary', boxes.length, this.transloco.getActiveLang()), {
      what: this.what(),
      group: this.groupShown(),
      count: boxes.length,
      min: this.format()(low),
      max: this.format()(high),
    });
  }
}
