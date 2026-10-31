import { Component, ElementRef, computed, inject, input, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { countKey } from '../../../core/i18n/plural';
import { QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { observeSize } from './chart-data';
import { bandScale, charWidth, linearScale, niceAxis, seriesColor, truncate } from './chart-scales';
import { CrosstabWidget } from './crosstab-widget';
import { ValueFormatter, plotted } from './format';
import { MarkPoint, markTooltips } from './mark-tooltips';
import { LegendEntry, SeriesLegend, SeriesNotes, seriesName } from './series-legend';
import { Hit, Label, Line, Rect } from './series-plot';
import { SeriesTable, buildSeriesTable, orderedSeriesTable } from './series-table';

type Translate = (key: string, params?: Record<string, unknown>) => string;

export interface PyramidPlot {
  width: number;
  height: number;
  grid: Line[];
  ticks: Label[];
  labels: Label[];
  rects: Rect[];
  /** The value of each bar, written at its tip where there is room. */
  values: Label[];
  hits: Hit[];
  /** The marks by row, from the top, then by side (0 left, 1 right). */
  marks: (MarkPoint | null)[][];
  /** The groups from the top: the last group of the table first, so the first group is at the bottom. */
  groups: (string | null)[];
}

export interface PyramidPlotInput {
  table: SeriesTable;
  /** Which columns of the table are the left and the right side. */
  sides: readonly [number, number];
  width: number;
  height: number;
  names: readonly [string, string];
  colors: readonly [string, string];
  groupLabel: (group: string | null) => string;
  value: ValueFormatter;
}

const top = 8;
const right = 14;
const bottomRoom = 24;
const maxThickness = 24;
const minLabelBand = 13;

/**
 * The bars of a population pyramid (design D5): one scale measured outward from a central axis for both sides, the first group at
 * the bottom, ticks written without signs. A group with no group value draws no bar. A value is written at the tip of its bar where
 * the room beside it holds the text.
 */
export function pyramidPlot(input: PyramidPlotInput): PyramidPlot {
  const { table, sides, width: w, height: h } = input;
  // The table lists the groups from the first; the chart draws them from the bottom. A missing group has no bar.
  const listed = table.rows.map((row, index) => ({ row, index })).filter(({ row }) => row.group !== null);
  const rows = [...listed].reverse();
  const texts = rows.map(({ row }) => input.groupLabel(row.group));
  const longest = Math.max(0, ...texts.map((text) => text.length));
  const labelChars = Math.max(3, Math.min(Math.floor((w * 0.25) / charWidth), longest));
  const left = labelChars * charWidth + 14;
  const bottom = bottomRoom;
  const cells = rows.flatMap(({ row }) => sides.map((column) => Math.max(0, row.cells[column] ?? 0)));
  const largest = Math.max(0, ...cells);
  const axis = largest === 0 ? niceAxis(0, 1) : niceAxis(0, largest);
  const center = (left + (w - right)) / 2;
  const bands = bandScale(rows.length, [top, h - bottom], 0.25);
  const thickness = Math.min(maxThickness, Math.max(1, bands.bandwidth));
  // A value is written at the tip of its bar, in one way for every bar: as the indicator writes it when the longest of them fits the
  // room kept beside the longest bar, else as the compact figure of the axis, else not at all. Written bar by bar, the bars that
  // are labelled would be the shortest ones, and the longest would have none.
  const spare = ((w - right - left) / 2) * 0.3;
  const widest = (write: (value: number) => string) => Math.max(0, write(axis.max).length, ...cells.map((cell) => write(cell).length)) * charWidth * 0.9 + 8;
  const writing: ((value: number) => string) | null =
    bands.step < minLabelBand ? null : widest(input.value) <= spare ? input.value : widest(input.value.tick) <= spare ? input.value.tick : null;
  const labelRoom = Math.min(spare, widest(writing ?? input.value));
  const half = (w - right - left) / 2 - labelRoom;
  const scale = linearScale([0, axis.max], [0, Math.max(1, half)]);

  const grid: Line[] = [];
  const ticks: Label[] = [];
  const labels: Label[] = [];
  const rectsOut: Rect[] = [];
  const values: Label[] = [];
  const hits: Hit[] = [];
  const marks: (MarkPoint | null)[][] = rows.map(() => [null, null]);

  for (const tick of axis.ticks) {
    const text = input.value.tick(tick);
    const edges = tick === 0 ? [center] : [center - scale(tick), center + scale(tick)];
    for (const x of edges) {
      grid.push({ x1: x, y1: top, x2: x, y2: h - bottom, zero: tick === 0 });
      ticks.push({ x, y: h - 8, text, anchor: 'middle' });
    }
  }
  const stride = Math.max(1, Math.ceil(14 / (bands.step || 1)));
  texts.forEach((text, i) => {
    if (i % stride === 0) {
      labels.push({ x: left - 6, y: bands.start(i) + bands.bandwidth / 2 + 4, text: truncate(text, labelChars), anchor: 'end', title: truncate(text, labelChars) === text ? undefined : text });
    }
  });

  rows.forEach(({ row }, visual) => {
    sides.forEach((column, side) => {
      const cell = row.cells[column];
      if (cell === null || cell === undefined) {
        return;
      }
      const length = scale(Math.max(0, cell));
      const x = side === 0 ? center - length : center;
      const y = bands.start(visual) + (bands.bandwidth - thickness) / 2;
      const bar = { x, y, width: Math.max(1, length), height: thickness };
      rectsOut.push({ ...bar, color: input.colors[side], title: '', at: { g: visual, s: side } });
      // The hit area is the whole row's half, the gap above and below the bar included.
      hits.push({ x: side === 0 ? left : center, y: bands.start(visual) - (bands.step - bands.bandwidth) / 2, width: (w - right - left) / 2, height: bands.step, at: { g: visual, s: side } });
      if (writing) {
        const text = writing(cell);
        const room = side === 0 ? x - left : w - right - (x + bar.width);
        if (room >= text.length * charWidth * 0.9 + 4) {
          values.push({ x: side === 0 ? x - 4 : x + bar.width + 4, y: y + thickness / 2 + 4, text, anchor: side === 0 ? 'end' : 'start' });
        }
      }
      marks[visual][side] = {
        content: { heading: input.groupLabel(row.group), lines: [{ label: input.names[side], value: input.value(cell), align: 'end', color: input.colors[side] }], footer: null },
        x: bar.x + bar.width / 2,
        y: y + thickness / 2,
      };
    });
  });
  return { width: w, height: h, grid, ticks, labels, rects: rectsOut, values, hits, marks, groups: rows.map(({ row }) => row.group) };
}

/**
 * A population pyramid (design D5): the groups (age bands) as rows in natural order, the first at the bottom, with the bars of one
 * series value to the left of a central axis and another to the right, on one scale, labelled without signs. The left side is in
 * series color 1 and the right in color 2; the legend names them. Rows of any other series value are left out and counted, and the
 * widget says how many (see `SeriesNotes`). Values read as the indicator formats them.
 */
@Component({
  selector: 'app-pyramid-chart',
  imports: [TranslocoDirective, CrosstabWidget, SeriesLegend, SeriesNotes],
  template: `
    <ng-container *transloco="let t">
      <app-series-legend [entries]="legend()" />
      @if (asTable()) {
        <app-crosstab-widget class="min-h-0 flex-1" [answer]="answer()" [config]="config()" [label]="label()" [format]="format()" chartType="pyramid" />
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
              <rect
                rx="2"
                [attr.x]="rect.x"
                [attr.y]="rect.y"
                [attr.width]="rect.width"
                [attr.height]="rect.height"
                [style.fill]="rect.color"
                [attr.opacity]="rect.at && marks.isLifted(rect.at.g, rect.at.s) ? 0.8 : null"
                [class.stroke-heading]="rect.at && marks.active()?.g === rect.at.g && marks.active()?.s === rect.at.s"
                [attr.stroke-width]="rect.at && marks.active()?.g === rect.at.g && marks.active()?.s === rect.at.s ? 2 : null"
                data-testid="chart-mark"
              />
            }
            @for (value of plot().values; track $index) {
              <text class="fill-heading" font-size="11" [attr.x]="value.x" [attr.y]="value.y" [attr.text-anchor]="value.anchor" data-testid="pyramid-value">{{ value.text }}</text>
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
        <app-series-notes [leftOut]="table().leftOut" kind="pyramid" />
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class PyramidChart {
  private readonly transloco = inject(TranslocoService);

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
  protected readonly table = computed(() => orderedSeriesTable(this.answer(), 'pyramid', null));
  private readonly tr = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);

  /**
   * The columns that are the sides. The answer lists the two sides in order (left, right) whether or not rows exist for them; a side
   * the widget names is found by its value, so the left is the left whatever the order.
   */
  private readonly sides = computed<readonly [number, number]>(() => {
    const columns = this.table().columns;
    const pyramid = this.config().pyramid;
    const find = (value: string | undefined, fallback: number) => {
      const at = value === undefined ? -1 : columns.findIndex((column) => column.kind === 'value' && column.series === value);
      return at === -1 ? fallback : at;
    };
    return [find(pyramid?.left, 0), find(pyramid?.right, 1)];
  });
  private readonly names = computed<readonly [string, string]>(() => {
    const columns = this.table().columns;
    const [a, b] = this.sides();
    return [columns[a] ? seriesName(columns[a], this.tr) : '', columns[b] ? seriesName(columns[b], this.tr) : ''];
  });
  /** The left side is series color 1 and the right is color 2. */
  private readonly colors: readonly [string, string] = [seriesColor(0), seriesColor(1)];
  protected readonly legend = computed<LegendEntry[]>(() => this.names().map((name, i) => ({ name, color: this.colors[i] })));

  protected readonly plot = computed(() =>
    pyramidPlot({
      table: this.table(),
      sides: this.sides(),
      width: Math.max(160, this.size().width),
      height: Math.max(120, this.size().height),
      names: this.names(),
      colors: this.colors,
      groupLabel: (group) => group ?? this.tr('indicators.chart.noGroup'),
      value: this.format(),
    }),
  );

  protected readonly marks = markTooltips({ host: () => this.svg()?.nativeElement, marks: () => this.plot().marks, groupsAcross: () => false });

  protected summary(t: Translate): string {
    const [left, right] = this.names();
    const bars = this.table()
      .rows.filter((row) => row.group !== null)
      .flatMap((row) => this.sides().map((column, side) => ({ group: row.group!, side, value: row.cells[column] })))
      .filter((bar): bar is { group: string; side: number; value: number } => bar.value !== null);
    if (!bars.length) {
      return t('indicators.chart.summaryEmpty', { what: this.what() });
    }
    const best = bars.reduce((top, bar) => (bar.value > top.value ? bar : top));
    const groups = new Set(bars.map((bar) => bar.group)).size;
    return t('indicators.pyramid.summary', {
      what: this.what(),
      group: this.config().groupBy,
      left,
      right,
      groups: t(countKey('indicators.series.groupsCount', groups, this.transloco.getActiveLang()), { count: groups }),
      max: this.format()(best.value),
      top: `${best.group}, ${best.side === 0 ? left : right}`,
    });
  }
}
