import { Layout } from '../../../core/projects/indicators.service';
import { Placed, changed, clampLayout, defaultSize, movedBy, overlaps, readingOrder, reflow, resizedBy, unitsFor } from './grid-layout';

const at = (id: string, x: number, y: number, w: number, h: number): Placed => ({ id, layout: { x, y, w, h } });
const layoutOf = (items: Placed[], id: string): Layout => items.find((i) => i.id === id)!.layout;

describe('grid layout', () => {
  it('sizes a new widget by its chart type', () => {
    expect(defaultSize('stat')).toEqual({ w: 3, h: 2 });
    expect(defaultSize('map')).toEqual({ w: 6, h: 6 });
    for (const type of ['bar', 'column', 'line', 'histogram', 'pie'] as const) expect(defaultSize(type)).toEqual({ w: 6, h: 4 });
  });

  describe('clampLayout', () => {
    it.each<[string, Layout, Layout]>([
      ['a widget past the right edge', { x: 10, y: 1, w: 6, h: 2 }, { x: 6, y: 1, w: 6, h: 2 }],
      ['a negative place', { x: -3, y: -1, w: 2, h: 2 }, { x: 0, y: 0, w: 2, h: 2 }],
      ['a too wide widget', { x: 0, y: 0, w: 20, h: 2 }, { x: 0, y: 0, w: 12, h: 2 }],
      ['a too small widget', { x: 0, y: 0, w: 0, h: 0 }, { x: 0, y: 0, w: 1, h: 1 }],
      ['a too tall widget', { x: 0, y: 0, w: 2, h: 90 }, { x: 0, y: 0, w: 2, h: 40 }],
      ['fractions', { x: 1.4, y: 2.6, w: 2.5, h: 2.4 }, { x: 1, y: 3, w: 3, h: 2 }],
    ])('keeps %s inside the grid', (_name, input, expected) => {
      expect(clampLayout(input)).toEqual(expected);
    });
  });

  it('tells overlapping places from touching ones', () => {
    expect(overlaps({ x: 0, y: 0, w: 6, h: 4 }, { x: 5, y: 3, w: 2, h: 2 })).toBe(true);
    expect(overlaps({ x: 0, y: 0, w: 6, h: 4 }, { x: 6, y: 0, w: 6, h: 4 })).toBe(false);
    expect(overlaps({ x: 0, y: 0, w: 6, h: 4 }, { x: 0, y: 4, w: 6, h: 4 })).toBe(false);
  });

  it('orders by row, then column', () => {
    const order = readingOrder([at('d', 6, 4, 6, 4), at('b', 6, 0, 6, 4), at('c', 0, 4, 6, 4), at('a', 0, 0, 6, 4)]);
    expect(order.map((i) => i.id)).toEqual(['a', 'b', 'c', 'd']);
  });

  describe('reflow', () => {
    it('leaves the others where they are when nothing is in the way', () => {
      const result = reflow([at('a', 0, 0, 6, 4), at('b', 6, 0, 6, 4)], 'a', { x: 0, y: 4, w: 6, h: 4 });

      expect(layoutOf(result, 'a')).toEqual({ x: 0, y: 4, w: 6, h: 4 });
      expect(layoutOf(result, 'b')).toEqual({ x: 6, y: 0, w: 6, h: 4 });
    });

    it('pushes the widget it lands on below the dropped one', () => {
      const result = reflow([at('a', 0, 0, 6, 4), at('b', 6, 0, 6, 4)], 'a', { x: 6, y: 0, w: 6, h: 4 });

      expect(layoutOf(result, 'a')).toEqual({ x: 6, y: 0, w: 6, h: 4 });
      expect(layoutOf(result, 'b')).toEqual({ x: 6, y: 4, w: 6, h: 4 });
    });

    it('pushes a chain of widgets down, each only as far as needed', () => {
      const items = [at('a', 0, 0, 6, 2), at('b', 0, 2, 6, 2), at('c', 0, 4, 6, 2), at('d', 6, 0, 6, 2)];
      const result = reflow(items, 'd', { x: 0, y: 0, w: 6, h: 3 });

      expect(layoutOf(result, 'd')).toEqual({ x: 0, y: 0, w: 6, h: 3 });
      expect(layoutOf(result, 'a')).toEqual({ x: 0, y: 3, w: 6, h: 2 });
      expect(layoutOf(result, 'b')).toEqual({ x: 0, y: 5, w: 6, h: 2 });
      expect(layoutOf(result, 'c')).toEqual({ x: 0, y: 7, w: 6, h: 2 });
    });

    it('never leaves two widgets overlapping', () => {
      const items = [at('a', 0, 0, 4, 3), at('b', 4, 0, 4, 3), at('c', 8, 0, 4, 3), at('d', 0, 3, 12, 2), at('e', 2, 5, 3, 3)];
      const result = reflow(items, 'e', { x: 3, y: 0, w: 6, h: 6 });

      for (const first of result) {
        for (const second of result) {
          if (first.id !== second.id) expect(overlaps(first.layout, second.layout), `${first.id} ${second.id}`).toBe(false);
        }
      }
    });

    it('keeps the dropped widget inside the grid', () => {
      const result = reflow([at('a', 0, 0, 6, 4)], 'a', { x: 9, y: -2, w: 6, h: 4 });

      expect(layoutOf(result, 'a')).toEqual({ x: 6, y: 0, w: 6, h: 4 });
    });

    it('does not change the input', () => {
      const items = [at('a', 0, 0, 6, 4), at('b', 6, 0, 6, 4)];
      reflow(items, 'a', { x: 6, y: 0, w: 6, h: 4 });

      expect(items.map((i) => i.layout)).toEqual([{ x: 0, y: 0, w: 6, h: 4 }, { x: 6, y: 0, w: 6, h: 4 }]);
    });
  });

  it('moves and resizes by grid units within bounds', () => {
    expect(movedBy({ x: 2, y: 2, w: 4, h: 2 }, 3, -1)).toEqual({ x: 5, y: 1, w: 4, h: 2 });
    expect(movedBy({ x: 2, y: 2, w: 4, h: 2 }, 9, -9)).toEqual({ x: 8, y: 0, w: 4, h: 2 });
    expect(resizedBy({ x: 8, y: 0, w: 4, h: 2 }, 2, 3)).toEqual({ x: 8, y: 0, w: 4, h: 5 });
    expect(resizedBy({ x: 2, y: 0, w: 4, h: 2 }, -9, -9)).toEqual({ x: 2, y: 0, w: 1, h: 1 });
  });

  it('converts a pointer distance to grid units from the width of the grid', () => {
    // 12 columns of 85.33 px plus 16 px gaps: a cell and its gap are 101.33 px wide; rows are 72 + 16 = 88 px.
    expect(unitsFor({ x: 203, y: 176 }, 1200)).toEqual({ columns: 2, rows: 2 });
    expect(unitsFor({ x: -50, y: -43 }, 1200)).toEqual({ columns: 0, rows: 0 });
    expect(unitsFor({ x: -60, y: -45 }, 1200)).toEqual({ columns: -1, rows: -1 });
  });

  it('lists only the layouts that differ', () => {
    const before = [at('a', 0, 0, 6, 4), at('b', 6, 0, 6, 4)];
    const after = [at('a', 0, 0, 6, 4), at('b', 6, 4, 6, 4)];

    expect(changed(before, after).map((p) => p.id)).toEqual(['b']);
    expect(changed(before, before)).toEqual([]);
  });
});
