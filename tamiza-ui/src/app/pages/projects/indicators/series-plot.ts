import { SeriesLayout } from '../../../core/projects/indicators.service';
import { ChartTooltipContent } from '../../../shared/ui';
import { CategoryOrders } from './category-order';
import { MarkAt, MarkGrid, MarkPoint } from './mark-tooltips';
import { Point, bandScale, charWidth, linePath, linearScale, niceAxis, stepPath, truncate } from './chart-scales';
import {
  Box,
  PlotEdges,
  ReferenceMark,
  ReferenceStroke,
  TextAt,
  boxOf,
  dataLabelMargin,
  labelGap,
  minLabelBar,
  overlaps,
  placeReferences,
  pointGap,
  referenceMargin,
  textWidth,
} from './chart-annotations';
import { SeriesRow, SeriesTable, chartOrder } from './series-table';
import { ValueFormatter } from './format';

// The marks of a bar, column or line chart with a series (design D5): grouped, stacked and stacked-to-100% bars and columns, and one
// line per series. Pure geometry over a `SeriesTable`, so it is tested without a browser; `AxisChart` draws what it returns.

export interface Line {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  zero: boolean;
}
export interface Label {
  x: number;
  y: number;
  text: string;
  anchor: 'start' | 'middle' | 'end';
  title?: string;
  /** Written down the column, ending at (x, y), for a label that does not fit across it. */
  rotate?: boolean;
}
export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
  color: string;
  title: string;
  /** A mark of a chart with a series: the place its tooltip and the keyboard know it by. */
  at?: MarkAt;
}
export interface Path {
  d: string;
  color: string;
  name: string;
}
export interface Dot {
  cx: number;
  cy: number;
  color: string;
  title: string;
}
/** The area that answers a pointer for a mark: larger than the mark, with the gap between marks included (design D5, interaction). */
export interface Hit {
  x: number;
  y: number;
  width: number;
  height: number;
  at: MarkAt;
  /** A line chart's hairline at the group the pointer or the keyboard is on. */
  guide?: Line;
}

export interface SeriesPlotInput {
  kind: 'bar' | 'column' | 'line';
  layout: SeriesLayout;
  table: SeriesTable;
  width: number;
  height: number;
  /** One per column of the table: the series' name in the legend and the tooltips, and its color. */
  names: readonly string[];
  colors: readonly string[];
  groupLabel: (group: string | null) => string;
  /** What the widget declares for the order of its groups (add-declared-category-order); the table's columns are arranged already. */
  orders?: CategoryOrders | null;
  /** The value with the indicator's format and unit, and an axis tick; a share as a percent (and a tick of a 100% axis). */
  value: ValueFormatter;
  share: (share: number) => string;
  /** What the indicator is called, for the value line of a share's tooltip. */
  what: string;
  /** The reference lines to draw (add-chart-annotations, design D5): where each sits, the text written for it and its dash. The axis reaches them. */
  references?: readonly ReferenceMark[];
  /** A line chart draws steps (design D6) in place of straight segments. */
  steps?: boolean;
  /** Writes each bar, column or point's value on it (design D7). A stacked and a 100% layout have none whatever this says. */
  dataLabels?: boolean;
}

export interface SeriesPlot {
  grid: Line[];
  ticks: Label[];
  labels: Label[];
  rects: Rect[];
  lines: Path[];
  dots: Dot[];
  hits: Hit[];
  /** The marks by group and series, for the tooltips and the keyboard. */
  marks: MarkGrid;
  /** The reference lines across the plot, and the texts that fit at their ends (design D5). Empty for a chart without lines. */
  references: ReferenceStroke[];
  referenceLabels: Label[];
  /** The values written on the marks (design D7). Empty for a chart without data labels. */
  dataLabels: Label[];
}

/** The top margin of a plain chart; a chart with reference lines or data labels grows it by the room their texts need. */
const baseTop = 8;
const right = 14;
/** A bar or column is thin (design D5): grouped ones side by side at most 24 px, a stack at most 28. */
export const groupedThickness = 24;
export const stackedThickness = 28;
/** The surface-colored space between touching fills. */
export const surfaceGap = 2;
/** A hit area is at least this long along a bar. */
const minHit = 12;
const maxLinePoints = 40;

/** One segment of a stack, in value units, from `a` to `b`. */
interface Segment {
  series: number;
  a: number;
  b: number;
}

/** The stacks of a group: positive values from the axis up, negative ones from the axis down, in the order of the series. */
function stacks(values: readonly (number | null)[]): Segment[][] {
  const up: Segment[] = [];
  const down: Segment[] = [];
  let high = 0;
  let low = 0;
  values.forEach((value, series) => {
    if (value === null || value === 0) {
      return;
    }
    if (value > 0) {
      up.push({ series, a: high, b: (high += value) });
    } else {
      down.push({ series, a: low, b: (low += value) });
    }
  });
  return [up, down];
}

export function seriesPlot(input: SeriesPlotInput): SeriesPlot {
  const { kind, table, width: w, height: h } = input;
  const rows = chartOrder(table, kind, input.orders ?? null);
  const count = rows.length;
  const series = table.columns.length;
  const categories = rows.map((row) => input.groupLabel(row.group));
  const percent = kind !== 'line' && input.layout === 'percent';
  const stacked = kind !== 'line' && input.layout !== 'grouped';
  const horizontal = kind === 'bar';
  const refs = input.references ?? [];
  const labelsOn = !!input.dataLabels && !stacked;
  // Reference lines and data labels need room above the plot for their texts. A chart with neither keeps its top margin.
  const top = baseTop + (refs.length ? referenceMargin : 0) + (labelsOn && !horizontal ? dataLabelMargin : 0);
  // A 100% bar plots each cell's share; the other layouts plot the cell.
  const valueOf = (row: SeriesRow, s: number): number | null => (percent ? (row.shares?.[s] ?? null) : row.cells[s]);

  let low = 0;
  let high = 0;
  if (percent) {
    high = 1;
  } else if (stacked) {
    for (const row of rows) {
      const [up, down] = stacks(row.cells);
      high = Math.max(high, up.at(-1)?.b ?? 0);
      low = Math.min(low, down.at(-1)?.b ?? 0);
    }
  } else {
    for (const row of rows) {
      for (const cell of row.cells) {
        if (cell !== null) {
          high = Math.max(high, cell);
          low = Math.min(low, cell);
        }
      }
    }
  }
  if (!percent) {
    for (const ref of refs) {
      high = Math.max(high, ref.value);
      low = Math.min(low, ref.value);
    }
  }
  const axis = percent ? { min: 0, max: 1, step: 0.25, ticks: [0, 0.25, 0.5, 0.75, 1] } : low === 0 && high === 0 ? niceAxis(0, 1) : niceAxis(low, high);
  const tickText = (value: number) => (percent ? input.share(value) : input.value.tick(value));

  const grid: Line[] = [];
  const ticks: Label[] = [];
  const labels: Label[] = [];
  const rects: Rect[] = [];
  const lines: Path[] = [];
  const dots: Dot[] = [];
  const hits: Hit[] = [];
  const dataLabels: Label[] = [];
  /** The boxes of the texts already placed, which no later text may cover: the reference labels first, then the data labels. */
  const taken: Box[] = [];
  const marks: (MarkPoint | null)[][] = rows.map(() => Array.from({ length: kind === 'line' ? 1 : series }, () => null));
  /** Writes a data label unless it would cover a text already placed. */
  const writeLabel = (label: TextAt) => writeDataLabel(label, taken, dataLabels);

  const content = (row: SeriesRow, s: number, value: number, share: number | null): ChartTooltipContent => ({
    heading: input.groupLabel(row.group),
    lines:
      share !== null
        ? [
            { label: input.names[s], value: input.share(share), align: 'end', color: input.colors[s] },
            { label: input.what, value: input.value(value), align: 'end' },
          ]
        : [{ label: input.names[s], value: input.value(value), align: 'end', color: input.colors[s] }],
    footer: null,
  });

  if (kind === 'line') {
    return linePlot(input, rows, categories, axis, { grid, ticks, labels, rects, lines, dots, hits, marks, dataLabels, taken, top, refs });
  }

  // Along the value axis a position is `v`; along the categories, the band of a group is `bands`. `rectOf` turns the two into a rect.
  let v: (value: number) => number;
  let bands: ReturnType<typeof bandScale>;
  let plot: PlotEdges;
  let barLabels = false;
  if (horizontal) {
    const longest = Math.max(0, ...categories.map((c) => c.length));
    const labelChars = Math.max(3, Math.min(Math.floor((w * 0.35) / charWidth), longest));
    const left = labelChars * charWidth + 14;
    const bottom = 24;
    bands = bandScale(count, [top, h - bottom], 0.25);
    // Side by side, a bar is at most 24 px thick; one thinner than a text line has no label, and the others keep the room past their end.
    barLabels = labelsOn && groupedBarThickness(bands.bandwidth, series) >= minLabelBar;
    const widest = barLabels ? Math.max(0, ...rows.flatMap((row) => row.cells.flatMap((cell) => (cell === null ? [] : [textWidth(input.value(cell))])))) : 0;
    const edge = right + (barLabels ? widest + labelGap : 0);
    v = linearScale([axis.min, axis.max], [left, w - edge]);
    plot = { left, right: w - edge, top, bottom: h - bottom };
    const stride = Math.max(1, Math.ceil(14 / (bands.step || 1)));
    for (const tick of axis.ticks) {
      grid.push({ x1: v(tick), y1: top, x2: v(tick), y2: h - bottom, zero: tick === 0 });
      ticks.push({ x: v(tick), y: h - 8, text: tickText(tick), anchor: 'middle' });
    }
    categories.forEach((category, i) => {
      if (i % stride === 0) {
        labels.push({ x: left - 6, y: bands.start(i) + bands.bandwidth / 2 + 4, text: truncate(category, labelChars), anchor: 'end', title: truncate(category, labelChars) === category ? undefined : category });
      }
    });
  } else {
    const tickTexts = axis.ticks.map((tick) => tickText(tick));
    const left = Math.max(...tickTexts.map((t) => t.length)) * charWidth + 14;
    const bottom = 26;
    v = linearScale([axis.min, axis.max], [h - bottom, top]);
    bands = bandScale(count, [left, w - right], 0.3);
    plot = { left, right: w - right, top, bottom: h - bottom };
    axis.ticks.forEach((tick, i) => {
      grid.push({ x1: left, y1: v(tick), x2: w - right, y2: v(tick), zero: tick === 0 });
      ticks.push({ x: left - 6, y: v(tick) + 4, text: tickTexts[i], anchor: 'end' });
    });
    const longest = Math.max(0, ...categories.map((c) => c.length));
    const stride = Math.max(1, Math.ceil((Math.min(longest, 14) * charWidth + 6) / (bands.step || 1)));
    const labelChars = Math.max(2, Math.floor((stride * bands.step) / charWidth) - 1);
    categories.forEach((category, i) => {
      if (i % stride === 0) {
        labels.push({ x: bands.start(i) + bands.bandwidth / 2, y: h - 8, text: truncate(category, labelChars), anchor: 'middle', title: truncate(category, labelChars) === category ? undefined : category });
      }
    });
  }
  const placed = placeReferences(refs, horizontal, v, plot, taken);
  const rectOf = (v0: number, v1: number, c0: number, size: number) =>
    horizontal ? { x: Math.min(v0, v1), y: c0, width: Math.abs(v1 - v0), height: size } : { x: c0, y: Math.min(v0, v1), width: size, height: Math.abs(v1 - v0) };

  const place = (g: number, s: number, value: number, rect: { x: number; y: number; width: number; height: number }, hit: { x: number; y: number; width: number; height: number }) => {
    const share = percent ? valueOf(rows[g], s) : null;
    const cell = rows[g].cells[s] ?? value;
    rects.push({ ...rect, color: input.colors[s], title: '', at: { g, s } });
    hits.push({ ...hit, at: { g, s } });
    marks[g][s] = { content: content(rows[g], s, cell, share), x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 };
  };
  /** A hit area at least `minHit` long along the value axis, centered on the mark. */
  const along = (start: number, length: number) => (length >= minHit ? { start, length } : { start: start + (length - minHit) / 2, length: minHit });

  rows.forEach((row, g) => {
    const bandStart = bands.start(g);
    const bandSize = bands.bandwidth;
    const slotStart = bandStart - (bands.step - bandSize) / 2;
    if (!stacked) {
      // Side by side: the group's band holds one thin bar per series, with the surface between them.
      const cluster = Math.min(series * (groupedThickness + surfaceGap), bandSize);
      const slot = cluster / series;
      const thickness = Math.max(1, Math.min(groupedThickness, slot - surfaceGap));
      row.cells.forEach((cell, s) => {
        if (cell === null) {
          return;
        }
        const start = bandStart + (bandSize - cluster) / 2 + s * slot;
        const bar = rectOf(v(0), v(cell), start + (slot - thickness) / 2, thickness);
        const span = horizontal ? along(bar.x, bar.width) : along(bar.y, bar.height);
        place(g, s, cell, bar, horizontal ? { x: span.start, y: start, width: span.length, height: slot } : { x: start, y: span.start, width: slot, height: span.length });
        if (labelsOn) {
          const text = input.value(cell);
          // A negative mark that reaches the axis minimum has no room beyond it: its label would cover the category names, so it is left out.
          if (horizontal && barLabels) {
            const at = { x: cell >= 0 ? bar.x + bar.width + labelGap : bar.x - labelGap, y: bar.y + bar.height / 2 + 4, text, anchor: cell >= 0 ? ('start' as const) : ('end' as const) };
            if (boxOf(at).x0 >= plot.left) {
              writeLabel(at);
            }
          } else if (!horizontal && textWidth(text) <= slot) {
            // A column label sits above its end, or below a negative column, and is left out when it is wider than the column slot.
            const at = { x: bar.x + bar.width / 2, y: cell >= 0 ? bar.y - labelGap : bar.y + bar.height + labelGap + 10, text, anchor: 'middle' as const };
            if (boxOf(at).y1 <= plot.bottom + 2) {
              writeLabel(at);
            }
          }
        }
      });
      return;
    }
    // End to end: positive segments from the axis up (or right), negative ones from the axis down (or left), in the order of the
    // series. A fill touching another leaves one pixel of surface on its side, so two neighbors are two pixels apart.
    const thickness = Math.max(1, Math.min(stackedThickness, bandSize));
    const start = bandStart + (bandSize - thickness) / 2;
    const cells = percent ? row.shares : row.cells;
    for (const stack of stacks(cells ?? [])) {
      stack.forEach((segment, k) => {
        let p0 = v(segment.a);
        let p1 = v(segment.b);
        const sign = Math.sign(p1 - p0) || 1;
        const near = k > 0 ? 1 : 0;
        const far = k < stack.length - 1 ? 1 : 0;
        if (Math.abs(p1 - p0) > near + far + 1) {
          p0 += sign * near;
          p1 -= sign * far;
        }
        const bar = rectOf(p0, p1, start, thickness);
        const span = horizontal ? along(bar.x - near, bar.width + near + far) : along(bar.y - far, bar.height + near + far);
        place(g, segment.series, cells![segment.series]!, bar, horizontal ? { x: span.start, y: slotStart, width: span.length, height: bands.step } : { x: slotStart, y: span.start, width: bands.step, height: span.length });
      });
    }
  });

  return { grid, ticks, labels, rects, lines, dots, hits, marks, references: placed.strokes, referenceLabels: placed.labels, dataLabels };
}

/** The thickness of a bar drawn side by side with the others of its group (design D5): at most 24 px, with the surface between them. */
function groupedBarThickness(bandSize: number, series: number): number {
  const cluster = Math.min(series * (groupedThickness + surfaceGap), bandSize);
  return Math.max(1, Math.min(groupedThickness, cluster / series - surfaceGap));
}

/**
 * Writes a data label unless its box would cover a text already placed (design D7), and records its box when it is written. The rule is
 * the same for bars, columns, points and slices: a label that does not fit is left out, never moved.
 */
export function writeDataLabel(label: TextAt, taken: Box[], into: Label[]): boolean {
  const box = boxOf(label);
  if (taken.some((other) => overlaps(other, box))) {
    return false;
  }
  taken.push(box);
  into.push(label);
  return true;
}

/** One line per series (design D5): broken where a group has no value, dots up to 40 groups, and one tooltip per group with every series. */
function linePlot(
  input: SeriesPlotInput,
  rows: SeriesRow[],
  categories: string[],
  axis: { min: number; max: number; ticks: number[] },
  parts: Pick<SeriesPlot, 'grid' | 'ticks' | 'labels' | 'rects' | 'lines' | 'dots' | 'hits' | 'dataLabels'> & {
    marks: (MarkPoint | null)[][];
    /** The boxes of the texts already placed, and the top margin and reference lines `seriesPlot` worked out. */
    taken: Box[];
    top: number;
    refs: readonly ReferenceMark[];
  },
): SeriesPlot {
  const { width: w, height: h, table } = input;
  const { grid, ticks, labels, lines, dots, hits, marks, dataLabels, taken, top, refs } = parts;
  const count = rows.length;
  const tickTexts = axis.ticks.map((tick) => input.value.tick(tick));
  const left = Math.max(...tickTexts.map((t) => t.length)) * charWidth + 14;
  const bottom = 26;
  const y = linearScale([axis.min, axis.max], [h - bottom, top]);
  const bands = bandScale(count, [left, w - right], 0);
  const placed = placeReferences(refs, false, y, { left, right: w - right, top, bottom: h - bottom }, taken);
  axis.ticks.forEach((tick, i) => {
    grid.push({ x1: left, y1: y(tick), x2: w - right, y2: y(tick), zero: tick === 0 });
    ticks.push({ x: left - 6, y: y(tick) + 4, text: tickTexts[i], anchor: 'end' });
  });
  const longest = Math.max(0, ...categories.map((c) => c.length));
  const stride = Math.max(1, Math.ceil((Math.min(longest, 14) * charWidth + 6) / (bands.step || 1)));
  const labelChars = Math.max(2, Math.floor((stride * bands.step) / charWidth) - 1);
  const centerOf = (g: number) => bands.start(g) + bands.bandwidth / 2;
  categories.forEach((category, i) => {
    if (i % stride === 0) {
      labels.push({ x: centerOf(i), y: h - 8, text: truncate(category, labelChars), anchor: 'middle', title: truncate(category, labelChars) === category ? undefined : category });
    }
  });

  table.columns.forEach((_, s) => {
    // The runs of groups that have a value: a gap in the data is a gap in the line, and a lone point is a dot with no segment.
    const runs: Point[][] = [[]];
    rows.forEach((row, g) => {
      const value = row.cells[s];
      if (value === null) {
        if (runs.at(-1)!.length) {
          runs.push([]);
        }
        return;
      }
      const point = { x: centerOf(g), y: y(value) };
      runs.at(-1)!.push(point);
      if (count <= maxLinePoints) {
        dots.push({ cx: point.x, cy: point.y, color: input.colors[s], title: '' });
      }
      if (input.dataLabels) {
        // Series by series and point by point, left to right: a label that would cover one already placed is left out, as is one off the plot.
        const text = input.value(value);
        const box = boxOf({ x: point.x, y: point.y - pointGap, text, anchor: 'middle' });
        if (box.x0 >= left && box.x1 <= w - labelGap) {
          writeDataLabel({ x: point.x, y: point.y - pointGap, text, anchor: 'middle' }, taken, dataLabels);
        }
      }
    });
    const path = input.steps ? stepPath : linePath;
    lines.push({ d: runs.filter((run) => run.length > 1).map((run) => path(run)).join(' '), color: input.colors[s], name: input.names[s] });
  });

  rows.forEach((row, g) => {
    const present = row.cells.flatMap((cell, s) => (cell === null ? [] : [{ s, cell }]));
    const x = centerOf(g);
    marks[g][0] = {
      content: {
        heading: categories[g],
        lines: present.map(({ s, cell }) => ({ label: input.names[s], value: input.value(cell), align: 'end' as const, color: input.colors[s] })),
        footer: null,
      },
      x,
      y: Math.min(...present.map(({ cell }) => y(cell)), h - bottom),
    };
    hits.push({ x: bands.start(g) - (bands.step - bands.bandwidth) / 2, y: top, width: bands.step, height: h - bottom - top, at: { g, s: 0 }, guide: { x1: x, y1: top, x2: x, y2: h - bottom, zero: false } });
  });

  return { grid, ticks, labels, rects: [], lines, dots, hits, marks, references: placed.strokes, referenceLabels: placed.labels, dataLabels };
}
