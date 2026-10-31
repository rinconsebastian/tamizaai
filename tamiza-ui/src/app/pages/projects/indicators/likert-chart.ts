import { Component, ElementRef, LOCALE_ID, computed, inject, input, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { countKey } from '../../../core/i18n/plural';
import { LikertSide, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { observeSize } from './chart-data';
import { bandScale, charWidth, linearScale, niceAxis, truncate } from './chart-scales';
import { CrosstabWidget } from './crosstab-widget';
import { ValueFormatter, plotted } from './format';
import { MarkPoint, markTooltips } from './mark-tooltips';
import { LegendEntry, SeriesLegend, SeriesNotes, seriesName } from './series-legend';
import { Hit, Label, Line, Rect } from './series-plot';
import { SeriesTable, buildSeriesTable, orderedSeriesTable } from './series-table';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/**
 * The steps of an arm of the diverging scale (`--tmz-div-neg-1..5`, `--tmz-div-pos-1..5`) that `count` values of one side take,
 * nearest the neutral first: each arm is stepped by distance from the neutral, and a side with fewer than five values spreads its
 * values over the arm so they stay apart.
 */
export function armSteps(count: number): number[] {
  const steps: Record<number, number[]> = { 0: [], 1: [3], 2: [2, 4], 3: [1, 3, 5], 4: [1, 2, 4, 5], 5: [1, 2, 3, 4, 5] };
  return steps[Math.min(5, Math.max(0, count))];
}

/**
 * The color of every value of a scale, in the scale's order: the red arm for the negatives, the neutral midpoint, the blue arm for the
 * positives. The values nearest the neutral take the first steps.
 */
export function scaleColors(sides: readonly LikertSide[]): string[] {
  const negatives = sides.map((side, i) => (side === 'negative' ? i : -1)).filter((i) => i >= 0);
  const positives = sides.map((side, i) => (side === 'positive' ? i : -1)).filter((i) => i >= 0);
  const colors = sides.map(() => 'var(--tmz-div-neutral)');
  // The scale runs from the most negative to the most positive, so the nearest negative is the last of them.
  const near = armSteps(negatives.length);
  [...negatives].reverse().forEach((at, k) => (colors[at] = `var(--tmz-div-neg-${near[k]})`));
  const far = armSteps(positives.length);
  positives.forEach((at, k) => (colors[at] = `var(--tmz-div-pos-${far[k]})`));
  return colors;
}

export interface LikertPlot {
  width: number;
  height: number;
  grid: Line[];
  ticks: Label[];
  labels: Label[];
  rects: Rect[];
  /** The share of each bar that is left of the center and right of it, written at the ends of the bar where there is room. */
  ends: Label[];
  hits: Hit[];
  marks: (MarkPoint | null)[][];
}

export interface LikertPlotInput {
  table: SeriesTable;
  /** The side and the color of each column, in the scale's order. */
  sides: readonly LikertSide[];
  colors: readonly string[];
  names: readonly string[];
  width: number;
  height: number;
  groupLabel: (group: string | null) => string;
  value: ValueFormatter;
  share: (share: number) => string;
  what: string;
  sideName: (side: LikertSide) => string;
}

const top = 8;
const right = 14;
const bottomRoom = 24;
const maxThickness = 28;
const minHit = 12;

/**
 * The bars of a diverging Likert widget (design D5): each group's composition in percent, laid out in the order of the scale and
 * centered so that the neutral value is split across the line at 0% (without a neutral value, the line is between the negatives and the
 * positives). The negatives lie to its left, the nearest nearest the center, the positives to its right. The axis runs from the
 * largest extent to the left to the largest extent to the right, in percent, written without signs.
 */
export function likertPlot(input: LikertPlotInput): LikertPlot {
  const { table, width: w, height: h } = input;
  const rows = table.rows;
  const texts = rows.map((row) => input.groupLabel(row.group));
  const longest = Math.max(0, ...texts.map((text) => text.length));
  const labelChars = Math.max(3, Math.min(Math.floor((w * 0.3) / charWidth), longest));
  const left = labelChars * charWidth + 14;
  // The end labels sit outside the bars, so the longest bar keeps room for its.
  const endRoom = 4 * charWidth * 0.9 + 6;

  /** The shares of a row on the negative side, on the neutral value and on the positive side. */
  const sums = (shares: readonly (number | null)[]) => {
    const sum = (side: LikertSide) => shares.reduce<number>((total, share, i) => total + (input.sides[i] === side ? (share ?? 0) : 0), 0);
    return { negative: sum('negative'), neutral: sum('neutral'), positive: sum('positive') };
  };
  /** Where the first segment of a row starts, in percent: minus the negative shares and half of the neutral one. */
  const startOf = (shares: readonly (number | null)[]) => {
    const { negative, neutral } = sums(shares);
    return -(negative + neutral / 2) * 100;
  };
  const extents = rows.map((row) => {
    const shares = row.shares ?? row.cells.map(() => null);
    const start = startOf(shares);
    const whole = shares.reduce<number>((sum, share) => sum + (share ?? 0), 0) * 100;
    return { start, end: start + whole };
  });
  const axis = niceAxis(Math.min(0, ...extents.map((e) => e.start)), Math.max(0, ...extents.map((e) => e.end)) || 1);
  const x = linearScale([axis.min, axis.max], [left + endRoom, w - right - endRoom]);
  const bands = bandScale(rows.length, [top, h - bottomRoom], 0.25);
  const thickness = Math.min(maxThickness, Math.max(1, bands.bandwidth));
  const stride = Math.max(1, Math.ceil(14 / (bands.step || 1)));

  const grid: Line[] = [];
  const ticks: Label[] = [];
  const labels: Label[] = [];
  const rects: Rect[] = [];
  const ends: Label[] = [];
  const hits: Hit[] = [];
  const marks: (MarkPoint | null)[][] = rows.map((row) => row.cells.map(() => null));

  for (const tick of axis.ticks) {
    grid.push({ x1: x(tick), y1: top, x2: x(tick), y2: h - bottomRoom, zero: tick === 0 });
    ticks.push({ x: x(tick), y: h - 8, text: input.share(Math.abs(tick) / 100), anchor: 'middle' });
  }
  texts.forEach((text, g) => {
    if (g % stride === 0) {
      labels.push({ x: left - 6, y: bands.start(g) + bands.bandwidth / 2 + 4, text: truncate(text, labelChars), anchor: 'end', title: truncate(text, labelChars) === text ? undefined : text });
    }
  });

  rows.forEach((row, g) => {
    const shares = row.shares ?? row.cells.map(() => null);
    const y = bands.start(g) + (bands.bandwidth - thickness) / 2;
    const present = shares.flatMap((share, s) => (share !== null && share > 0 ? [s] : []));
    let at = extents[g].start;
    shares.forEach((share, s) => {
      if (share === null || share <= 0) {
        return;
      }
      const from = at;
      at += share * 100;
      // A fill touching another leaves one pixel of surface on its side, so two neighbors are two pixels apart.
      const first = s === present[0];
      const last = s === present.at(-1);
      const x0 = x(from) + (first ? 0 : 1);
      const x1 = x(at) - (last ? 0 : 1);
      const width = Math.max(1, x1 - x0);
      rects.push({ x: x0, y, width, height: thickness, color: input.colors[s], title: '', at: { g, s } });
      const length = width >= minHit ? { x: x0 - (first ? 0 : 1), width: width + (first ? 0 : 1) + (last ? 0 : 1) } : { x: x0 + (width - minHit) / 2, width: minHit };
      hits.push({ x: length.x, y: bands.start(g) - (bands.step - bands.bandwidth) / 2, width: length.width, height: bands.step, at: { g, s } });
      marks[g][s] = {
        content: {
          heading: input.groupLabel(row.group),
          lines: [
            { label: input.names[s], value: input.share(share), align: 'end', color: input.colors[s] },
            { label: input.what, value: input.value(row.cells[s]!), align: 'end' },
          ],
          footer: input.sideName(input.sides[s]),
        },
        x: x0 + width / 2,
        y: y + thickness / 2,
      };
    });
    if (present.length && bands.step >= 13) {
      const { negative, neutral, positive } = sums(shares);
      const toLeft = negative + neutral / 2;
      const toRight = positive + neutral / 2;
      if (toLeft > 0) ends.push({ x: x(extents[g].start) - 4, y: y + thickness / 2 + 4, text: input.share(toLeft), anchor: 'end' });
      if (toRight > 0) ends.push({ x: x(extents[g].end) + 4, y: y + thickness / 2 + 4, text: input.share(toRight), anchor: 'start' });
    }
  });
  return { width: w, height: h, grid, ticks, labels, rects, ends, hits, marks };
}

/**
 * Diverging Likert bars (design D5): for agreement scales. Each group's bar is its composition in percent over the scale's values,
 * centered on the neutral value, the negatives to the left and the positives to the right, in the diverging colors. The legend lists
 * the scale in order. Values outside the scale are left out and counted (see `SeriesNotes`). "Show as table" lists one share per
 * value of the scale and the group's total.
 */
@Component({
  selector: 'app-likert-chart',
  imports: [TranslocoDirective, CrosstabWidget, SeriesLegend, SeriesNotes],
  template: `
    <ng-container *transloco="let t">
      <app-series-legend [entries]="legend()" />
      @if (asTable()) {
        <app-crosstab-widget class="min-h-0 flex-1" [answer]="answer()" [config]="config()" [label]="label()" [format]="format()" chartType="likert" />
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
            [attr.aria-roledescription]="t('indicators.series.roleDescription')"
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
            @for (rect of plot().rects; track $index) {
              <!-- The neutral fill is as light as the surface by design, so it alone has a hairline. -->
              <rect
                rx="2"
                [class.stroke-default-strong]="sides()[rect.at!.s] === 'neutral'"
                [attr.stroke-width]="sides()[rect.at!.s] === 'neutral' ? 1 : null"
                [attr.x]="rect.x"
                [attr.y]="rect.y"
                [attr.width]="rect.width"
                [attr.height]="rect.height"
                [style.fill]="rect.color"
                [attr.opacity]="marks.isLifted(rect.at!.g, rect.at!.s) ? 0.8 : null"
                [attr.data-side]="sides()[rect.at!.s]"
                data-testid="chart-mark"
              />
              @if (marks.active()?.g === rect.at!.g && marks.active()?.s === rect.at!.s) {
                <rect class="stroke-heading" fill="none" stroke-width="2" rx="2" [attr.x]="rect.x" [attr.y]="rect.y" [attr.width]="rect.width" [attr.height]="rect.height" data-testid="likert-active" />
              }
            }
            @for (end of plot().ends; track $index) {
              <text class="fill-heading" font-size="11" [attr.x]="end.x" [attr.y]="end.y" [attr.text-anchor]="end.anchor" data-testid="likert-end">{{ end.text }}</text>
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
          </svg>
          <p [id]="marks.helpId" class="sr-only">{{ t('indicators.series.keyHelpDown') }}</p>
          <p class="sr-only" role="status" aria-live="polite" data-testid="marks-announcement">{{ marks.announcement() }}</p>
        </div>
        <!-- In the table view the cross table says it itself. -->
        <app-series-notes [leftOut]="table().leftOut" kind="likert" />
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class LikertChart {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  readonly answer = input.required<QueryResult>();
  readonly config = input.required<WidgetConfig>();
  /** The indicator's label and the formatter of its values. */
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();
  readonly asTable = input(false);

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  private readonly svg = viewChild<ElementRef<SVGElement>>('chartSvg');
  protected readonly size = observeSize(() => this.box());

  protected readonly what = computed(() => plotted(this.config(), this.label()));
  protected readonly table = computed(() => orderedSeriesTable(this.answer(), 'likert', null));
  private readonly tr = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);

  /** The side of each value of the scale, in the answer's order: the scale of the widget says it, by value. */
  protected readonly sides = computed<LikertSide[]>(() => {
    const scale = this.config().scale ?? [];
    return this.table().columns.map((column, i) => scale.find((step) => step.value === column.series)?.side ?? scale[i]?.side ?? 'neutral');
  });
  private readonly colors = computed(() => scaleColors(this.sides()));
  private readonly names = computed(() => this.table().columns.map((column) => seriesName(column, this.tr)));
  protected readonly legend = computed<LegendEntry[]>(() => this.names().map((name, i) => ({ name, color: this.colors()[i], outlined: this.sides()[i] === 'neutral' })));
  private readonly percent = computed(() => new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 }));

  protected readonly plot = computed(() =>
    likertPlot({
      table: this.table(),
      sides: this.sides(),
      colors: this.colors(),
      names: this.names(),
      width: Math.max(160, this.size().width),
      height: Math.max(120, this.size().height),
      groupLabel: (group) => group ?? this.tr('indicators.chart.noGroup'),
      value: this.format(),
      share: (share) => this.percent().format(share),
      what: this.what(),
      sideName: (side) => this.tr(`indicators.likert.sides.${side}`),
    }),
  );

  protected readonly marks = markTooltips({ host: () => this.svg()?.nativeElement, marks: () => this.plot().marks, groupsAcross: () => false });

  protected summary(t: Translate): string {
    const table = this.table();
    const rows = table.rows.filter((row) => row.shares);
    if (!rows.length) {
      return t('indicators.chart.summaryEmpty', { what: this.what() });
    }
    const sides = this.sides();
    const positive = (row: (typeof rows)[number]) => row.shares!.reduce<number>((sum, share, i) => sum + (sides[i] === 'positive' ? (share ?? 0) : 0), 0);
    const best = rows.reduce((top, row) => (positive(row) > positive(top) ? row : top));
    const lang = this.transloco.getActiveLang();
    return t('indicators.likert.summary', {
      what: this.what(),
      group: this.config().groupBy,
      groups: t(countKey('indicators.series.groupsCount', table.rows.length, lang), { count: table.rows.length }),
      seriesCount: t(countKey('indicators.series.seriesCount', table.columns.length, lang), { count: table.columns.length }),
      top: best.group ?? t('indicators.chart.noGroup'),
      share: this.percent().format(positive(best)),
    });
  }
}
