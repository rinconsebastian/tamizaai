import { Label, writeDataLabel } from './series-plot';
import { Box, ReferenceMark, boxOf, dashOf, dashPatterns, overlaps, placeReferences, referenceBox, textWidth } from './chart-annotations';
import { linearScale } from './chart-scales';

// The rules of reference lines and data labels as geometry (add-chart-annotations, design D5, D7): what is drawn, and what is left out.

const mark = (value: number, text: string, place = 0): ReferenceMark => ({ value, text, label: text.split(': ')[0], figure: text.split(': ')[1] ?? '', place });
const plot = { left: 40, right: 440, top: 20, bottom: 250 };
const y = linearScale([0, 1], [250, 20]);
const x = linearScale([0, 1], [40, 440]);
/** The box a text of the first reference test would have if it ended at `end`: for asking whether the step before a free stretch was blocked. */
const referenceBoxAt = (end: number): Box => referenceBox({ x: end, y: 131, text: 'Total Duitama: 16.67%', anchor: 'end' });

describe('reference lines', () => {
  it('gives each of the first four places its own dash pattern, and goes round after them', () => {
    expect(new Set(dashPatterns).size).toBe(4);
    expect([0, 1, 2, 3, 4].map(dashOf)).toEqual(['6 4', '2 3', '8 3 2 3', '12 4', '6 4']);
  });

  it('draws a horizontal line across the plot for a column or line chart, its text at the right edge 4 px above the line, on its box', () => {
    const { strokes, labels, unplaced } = placeReferences([mark(0.5, 'Mean: 50%')], false, y, plot);

    expect(strokes).toEqual([{ x1: 40, y1: 135, x2: 440, y2: 135, dash: '6 4', text: 'Mean: 50%' }]);
    // The box is the text's (5.5 px a character), with 3 px each side and 2 above and below (improve-widget-text-hierarchy, design D7).
    expect(labels).toEqual([{ x: 440, y: 131, text: 'Mean: 50%', anchor: 'end', box: { x0: 387.5, y0: 119, x1: 443, y1: 135 } }]);
    expect(unplaced).toEqual([]);
  });

  it('moves the text of a horizontal line leftward, 8 px at a time, to the first stretch where its box covers no mark (Free stretch on the left)', () => {
    // Two columns rise above the line at the right end of the plot, as the last column of "3.1. Clases hoy, por estrato" does.
    const columns: Box[] = [{ x0: 400, y0: 80, x1: 440, y1: 250 }];
    const { labels } = placeReferences([mark(0.5, 'Total Duitama: 16.67%')], false, y, plot, [], columns);

    const [label] = labels;
    expect(label.anchor).toBe('end');
    expect(label.x).toBeLessThan(400 - 3);
    expect(440 - label.x).toBeGreaterThan(0);
    expect((440 - label.x) % 8).toBe(0);
    expect(overlaps(label.box, columns[0])).toBe(false);
    // The step before it would have covered the column.
    expect(overlaps(referenceBoxAt(label.x + 8), columns[0])).toBe(true);
  });

  it('keeps the text at the right end, over the marks, when no stretch is free (No free stretch)', () => {
    const everywhere: Box[] = [{ x0: 40, y0: 80, x1: 440, y1: 250 }];
    const { labels, unplaced } = placeReferences([mark(0.5, 'Mean: 50%')], false, y, plot, [], everywhere);

    expect(labels.map((label) => label.x)).toEqual([440]);
    expect(unplaced).toEqual([]);
  });

  it('keeps clear of the boxes already taken, and of the labels already placed', () => {
    const taken: Box[] = [{ x0: 300, y0: 100, x1: 443, y1: 140 }];
    const { labels } = placeReferences([mark(0.5, 'Mean: 50%')], false, y, plot, taken);

    expect(overlaps(labels[0].box, { x0: 300, y0: 100, x1: 443, y1: 140 })).toBe(false);
    expect(taken).toHaveLength(2);
  });

  it('draws a vertical line for a bar chart, its text at the top just right of the line', () => {
    const { strokes, labels } = placeReferences([mark(0.25, 'Mean: 25%')], true, x, plot);

    expect(strokes).toEqual([{ x1: 140, y1: 20, x2: 140, y2: 250, dash: '6 4', text: 'Mean: 25%' }]);
    expect(labels).toEqual([{ x: 144, y: 17, text: 'Mean: 25%', anchor: 'start', box: { x0: 141, y0: 5, x1: 196.5, y1: 21 } }]);
  });

  it('flips the text of a vertical line to its left when it would pass the right edge of the plot', () => {
    const { labels } = placeReferences([mark(0.95, 'Mean: 95%')], true, x, plot);

    expect(labels).toEqual([{ x: 416, y: 17, text: 'Mean: 95%', anchor: 'end', box: { x0: 363.5, y0: 5, x1: 419, y1: 21 } }]);
  });

  it('leaves out the text of a line that fits on neither side, and the text wider than the plot, but still draws the line', () => {
    const narrow = { left: 40, right: 140, top: 20, bottom: 250 };
    const wide = 'Meta del plan de desarrollo municipal: 50% de los hogares';

    const tooWide = placeReferences([mark(0.5, wide)], false, y, narrow);
    const neither = placeReferences([mark(0.5, 'Meta: 50% de los')], true, linearScale([0, 1], [40, 140]), narrow);

    expect(textWidth(wide)).toBeGreaterThan(narrow.right - narrow.left);
    expect(tooWide.strokes).toHaveLength(1);
    expect(tooWide.labels).toEqual([]);
    expect(neither.strokes).toHaveLength(1);
    expect(neither.labels).toEqual([]);
    // Their texts are not lost: the widget's foot names them (Text left out).
    expect(tooWide.unplaced.map((line) => line.text)).toEqual([wide]);
    expect(neither.unplaced.map((line) => line.text)).toEqual(['Meta: 50% de los']);
  });

  it('writes both texts of two lines so close that their texts would overlap, the second at the first free stretch to the left of the first (Lines close together)', () => {
    const close = placeReferences([mark(0.425, 'Mean: 42.5%', 0), mark(0.45, 'Median: 45%', 1)], false, y, plot);
    const apart = placeReferences([mark(0.2, 'Mean: 20%', 0), mark(0.6, 'Median: 60%', 1)], false, y, plot);

    expect(close.strokes.map((line) => line.dash)).toEqual(['6 4', '2 3']);
    expect(close.labels.map((label) => label.text)).toEqual(['Mean: 42.5%', 'Median: 45%']);
    expect(overlaps(close.labels[0].box, close.labels[1].box)).toBe(false);
    expect(close.labels[1].x).toBeLessThan(close.labels[0].x);
    expect(close.unplaced).toEqual([]);
    expect(apart.labels.map((label) => label.text)).toEqual(['Mean: 20%', 'Median: 60%']);
  });

  it('sends to the foot the text of a line that would cover another line\'s text wherever it goes', () => {
    // A plot as wide as one text: the second text has no stretch to move to, and the right end is taken.
    const tight = { left: 40, right: 110, top: 20, bottom: 250 };
    const { labels, unplaced } = placeReferences([mark(0.5, 'Mean: 50%', 0), mark(0.5, 'Median: 50%', 1)], false, y, tight);

    expect(labels.map((label) => label.text)).toEqual(['Mean: 50%']);
    expect(unplaced.map((line) => line.text)).toEqual(['Median: 50%']);
  });

  it('leaves out a text that would cover one already placed, as the data labels do', () => {
    const taken: Box[] = [];
    const written: Label[] = [];

    expect(writeDataLabel({ x: 100, y: 100, text: '12%', anchor: 'middle' }, taken, written)).toBe(true);
    expect(writeDataLabel({ x: 104, y: 100, text: '13%', anchor: 'middle' }, taken, written)).toBe(false);
    expect(writeDataLabel({ x: 200, y: 100, text: '14%', anchor: 'middle' }, taken, written)).toBe(true);
    expect(written.map((label) => label.text)).toEqual(['12%', '14%']);
  });

  it('measures a text by its characters, and boxes it by its anchor', () => {
    expect(textWidth('abcd')).toBe(26);
    const start = boxOf({ x: 10, y: 50, text: 'abcd', anchor: 'start' });
    const end = boxOf({ x: 10, y: 50, text: 'abcd', anchor: 'end' });
    const middle = boxOf({ x: 10, y: 50, text: 'abcd', anchor: 'middle' });

    expect([start.x0, end.x0, middle.x0]).toEqual([10, -16, -3]);
    expect(overlaps(start, end)).toBe(false);
    expect(overlaps(start, middle)).toBe(true);
  });
});
