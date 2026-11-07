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

/**
 * The room a chart with reference lines gives its top, so a label near the top stays inside the box: one 12 px text line and the room of
 * its box (improve-widget-text-hierarchy, design D7).
 */
export const referenceMargin = 16;
/** The room a reference text's box gives it beyond the text: 3 px each side and 2 px above and below. */
const boxPadX = 3;
const boxPadY = 2;
/** How far the search for a free stretch moves a horizontal line's text each step, leftward from the plot's right end. */
const searchStep = 8;
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

/**
 * The width of a reference text of 11 px: about 5.5 px a character, against the 6.5 the other texts are estimated with, which is made for
 * 12 px text and left a box a third wider than the text it holds (checked on the stack, improve-widget-text-hierarchy task 9.3).
 */
export const referenceCharWidth = 5.5;

/** The box a reference text is drawn on: the text's own box and the room around it (improve-widget-text-hierarchy, design D7). */
export function referenceBox(at: TextAt): Box {
  const width = at.text.length * referenceCharWidth;
  const x0 = at.anchor === 'start' ? at.x : at.anchor === 'end' ? at.x - width : at.x - width / 2;
  return { x0: x0 - boxPadX, y0: at.y - (textHeight - 2) - boxPadY, x1: x0 + width + boxPadX, y1: at.y + 2 + boxPadY };
}

/** The edges of the plot, in pixels: where the lines run from and to, and the texts must stay inside. */
export interface PlotEdges {
  left: number;
  right: number;
  top: number;
  bottom: number;
}

/** A reference text laid out at its line, with the box it is drawn on. */
export interface ReferenceLabel extends TextAt {
  box: Box;
}

export interface PlacedReferences {
  strokes: ReferenceStroke[];
  /** The texts that were drawn at their lines, each on its box. */
  labels: ReferenceLabel[];
  /**
   * The lines whose text was not drawn at the line: wider than the plot, or over another line's text (improve-widget-text-hierarchy, design D7).
   * The widget's foot names them, since the key under the chart is gone.
   */
  unplaced: ReferenceMark[];
}

/**
 * Lays out the reference lines (design D5; improve-widget-text-hierarchy, design D7). `vertical` is a bar chart's lines (across the value
 * axis, which runs along x); the others are horizontal. A line crosses the plot from edge to edge. Its text is drawn on a box:
 *
 * - on a column or line chart, just above the line (4 px) and end-anchored at the first free stretch from the plot's right end leftward,
 *   in steps of 8 px: where its box covers none of the `obstacles` (the marks' rectangles, the points' discs, the labels already placed)
 *   and none of the boxes `taken`. With no free stretch it goes at the right end, over the marks, where it stays readable on its box
 *   unless it would cover a text already placed;
 * - on a bar chart at the top, just right of the line, flipped to its left when it would pass the plot's edge.
 *
 * A text wider than the plot, one that fits on neither side of a bar chart's line, and one whose box would cover a text already placed
 * (the widget's order decides which) is not drawn at its line: it is in `unplaced`, never cut or moved away from its line.
 */
export function placeReferences(
  marks: readonly ReferenceMark[],
  vertical: boolean,
  scale: (value: number) => number,
  plot: PlotEdges,
  taken: Box[] = [],
  obstacles: readonly Box[] = [],
): PlacedReferences {
  const strokes: ReferenceStroke[] = [];
  const labels: ReferenceLabel[] = [];
  const unplaced: ReferenceMark[] = [];
  for (const mark of marks) {
    const at = scale(mark.value);
    const dash = dashOf(mark.place);
    strokes.push(
      vertical
        ? { x1: at, y1: plot.top, x2: at, y2: plot.bottom, dash, text: mark.text }
        : { x1: plot.left, y1: at, x2: plot.right, y2: at, dash, text: mark.text },
    );
    const width = mark.text.length * referenceCharWidth;
    if (width > plot.right - plot.left) {
      unplaced.push(mark);
      continue;
    }
    let label: TextAt;
    if (vertical) {
      const y = plot.top - 3;
      label = { x: at + labelGap, y, text: mark.text, anchor: 'start' };
      if (label.x + width > plot.right) {
        label = { x: at - labelGap, y, text: mark.text, anchor: 'end' };
        if (label.x - width < plot.left) {
          unplaced.push(mark);
          continue;
        }
      }
    } else {
      // From the plot's right end leftward, the first stretch where the text's box covers no mark and no text already placed.
      const y = at - labelGap;
      let found: TextAt | null = null;
      for (let x = plot.right; x - width >= plot.left; x -= searchStep) {
        const candidate: TextAt = { x, y, text: mark.text, anchor: 'end' };
        const around = referenceBox(candidate);
        if (!obstacles.some((other) => overlaps(other, around)) && !taken.some((other) => overlaps(other, around))) {
          found = candidate;
          break;
        }
      }
      label = found ?? { x: plot.right, y, text: mark.text, anchor: 'end' };
    }
    const box = referenceBox(label);
    if (taken.some((other) => overlaps(other, box))) {
      unplaced.push(mark);
      continue;
    }
    taken.push(box);
    labels.push({ ...label, box });
  }
  return { strokes, labels, unplaced };
}
