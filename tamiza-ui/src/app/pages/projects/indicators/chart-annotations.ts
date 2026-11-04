import { charWidth } from './chart-scales';

// The geometry of reference lines and data labels (add-chart-annotations, design D5, D7): pure functions over numbers, so every rule
// (what is drawn, what is left out) is tested without a browser. `AxisChart`, `seriesPlot` and `PieWidget` draw what they return.

/** One piece of text laid out in the SVG: where its baseline starts, and which way it runs from there. */
export interface TextAt {
  x: number;
  y: number;
  text: string;
  anchor: 'start' | 'middle' | 'end';
}

/** A reference line to draw: where it sits on the value axis, the text written for it and its place in the widget's list, which gives its dash. */
export interface ReferenceMark {
  value: number;
  /** What is written for the line, `{label}: {figure}`: at its end, in the key, under a table and in an image. */
  text: string;
  /** The text's two parts: the line's name and its value written as the widget writes values. */
  label: string;
  figure: string;
  place: number;
}

/** A reference line across the plot: its ends, its dash pattern and the text it is listed with (the `<title>` of a chart with no series). */
export interface ReferenceStroke {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  dash: string;
  text: string;
}

/** The dash pattern of each place in the widget's list, told apart in the plot and in the key (design D5). */
export const dashPatterns = ['6 4', '2 3', '8 3 2 3', '12 4'] as const;

/** The pattern of the line at this place in the widget's list. */
export function dashOf(place: number): string {
  return dashPatterns[Math.max(0, place) % dashPatterns.length];
}

/** The room a chart with reference lines gives its top, so a label near the top stays inside the box: one 12 px text line. */
export const referenceMargin = 12;
/** The room a column or line chart with data labels gives its top for the labels above its highest mark. */
export const dataLabelMargin = 14;
/** A bar thinner than this (px) has no data label: the text would be taller than the bar. */
export const minLabelBar = 11;
/** The space between a mark and the label written beside it. */
export const labelGap = 4;
/** The space between a point of a line and the label above it. */
export const pointGap = 8;
const textHeight = 12;

/** A rectangle on the plot, for deciding whether two texts would cover each other. */
export interface Box {
  x0: number;
  y0: number;
  x1: number;
  y1: number;
}

/** Rough width of a text of 11 px, with the measure the chart's other text uses (`charWidth`). */
export function textWidth(text: string): number {
  return text.length * charWidth;
}

/** The box a text covers: 11 px text is about 10 px above its baseline and 2 below. */
export function boxOf(at: TextAt): Box {
  const width = textWidth(at.text);
  const x0 = at.anchor === 'start' ? at.x : at.anchor === 'end' ? at.x - width : at.x - width / 2;
  return { x0, y0: at.y - (textHeight - 2), x1: x0 + width, y1: at.y + 2 };
}

export function overlaps(a: Box, b: Box): boolean {
  return a.x0 < b.x1 && b.x0 < a.x1 && a.y0 < b.y1 && b.y0 < a.y1;
}

/** The edges of the plot, in pixels: where the lines run from and to, and the texts must stay inside. */
export interface PlotEdges {
  left: number;
  right: number;
  top: number;
  bottom: number;
}

export interface PlacedReferences {
  strokes: ReferenceStroke[];
  /** The texts that were drawn at their lines. A text that is wider than the plot, or would cover a text already placed, has none. */
  labels: TextAt[];
}

/**
 * Lays out the reference lines (design D5). `vertical` is a bar chart's lines (across the value axis, which runs along x); the others
 * are horizontal. A line crosses the plot from edge to edge. Its text is written at its end: on a column or line chart, end-anchored at
 * the plot's right edge and 4 px above the line; on a bar chart at the top, just right of the line, flipped to its left when it
 * would pass the plot's edge. A text wider than the plot, one that fits on neither side, and one whose box would cover a text already
 * placed (the widget's order decides which) is left out, never cut or moved away from its line.
 */
export function placeReferences(marks: readonly ReferenceMark[], vertical: boolean, scale: (value: number) => number, plot: PlotEdges, taken: Box[] = []): PlacedReferences {
  const strokes: ReferenceStroke[] = [];
  const labels: TextAt[] = [];
  for (const mark of marks) {
    const at = scale(mark.value);
    const dash = dashOf(mark.place);
    strokes.push(
      vertical
        ? { x1: at, y1: plot.top, x2: at, y2: plot.bottom, dash, text: mark.text }
        : { x1: plot.left, y1: at, x2: plot.right, y2: at, dash, text: mark.text },
    );
    const width = textWidth(mark.text);
    if (width > plot.right - plot.left) {
      continue;
    }
    let label: TextAt;
    if (vertical) {
      const y = plot.top - 3;
      label = { x: at + labelGap, y, text: mark.text, anchor: 'start' };
      if (label.x + width > plot.right) {
        label = { x: at - labelGap, y, text: mark.text, anchor: 'end' };
        if (label.x - width < plot.left) {
          continue;
        }
      }
    } else {
      label = { x: plot.right, y: at - labelGap, text: mark.text, anchor: 'end' };
    }
    const box = boxOf(label);
    if (taken.some((other) => overlaps(other, box))) {
      continue;
    }
    taken.push(box);
    labels.push(label);
  }
  return { strokes, labels };
}
