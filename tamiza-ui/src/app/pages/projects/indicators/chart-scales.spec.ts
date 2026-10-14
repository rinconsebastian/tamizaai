import { QueryRow } from '../../../core/projects/indicators.service';
import { histogram, pivot } from './chart-data';
import {
  bandScale,
  linePath,
  linearScale,
  naturalCompare,
  niceAxis,
  ringsPath,
  sequentialColor,
  sequentialStep,
  seriesColor,
  slicePath,
  truncate,
} from './chart-scales';

const r = (indicator: string, group: string | null, value: number): QueryRow => ({ indicator, group, value, geometry: null });

describe('chart scales', () => {
  describe('linearScale', () => {
    it('maps the domain onto the range, in either direction', () => {
      const up = linearScale([0, 10], [0, 100]);
      const flipped = linearScale([0, 10], [200, 100]);

      expect([up(0), up(5), up(10)]).toEqual([0, 50, 100]);
      expect([flipped(0), flipped(10)]).toEqual([200, 100]);
    });

    it('maps a flat domain to the middle of the range', () => {
      expect(linearScale([5, 5], [0, 100])(5)).toBe(50);
    });
  });

  describe('niceAxis', () => {
    it.each<[number, number, number, number, number]>([
      [0, 97, 0, 100, 20],
      [0, 7, 0, 8, 2],
      [-20, 50, -20, 60, 20],
      [0, 0.8, 0, 0.8, 0.2],
      [0, 1234, 0, 1250, 250],
    ])('covers %s to %s with round steps', (min, max, niceMin, niceMax, step) => {
      const axis = niceAxis(min, max);

      expect(axis.min).toBeCloseTo(niceMin);
      expect(axis.max).toBeCloseTo(niceMax);
      expect(axis.step).toBeCloseTo(step);
      expect(axis.ticks[0]).toBeCloseTo(niceMin);
      expect(axis.ticks.at(-1)).toBeCloseTo(niceMax);
    });

    it('widens a flat range so there is something to draw', () => {
      const axis = niceAxis(5, 5);

      expect(axis.min).toBeLessThan(5);
      expect(axis.max).toBeGreaterThan(5);
    });

    it('answers a usable axis for numbers that are not finite', () => {
      expect(niceAxis(NaN, Infinity).ticks.length).toBeGreaterThan(1);
    });
  });

  it('lays bands out evenly with a gap between', () => {
    const bands = bandScale(4, [100, 500], 0.25);

    expect(bands.step).toBe(100);
    expect(bands.bandwidth).toBe(75);
    expect(bands.start(0)).toBeCloseTo(112.5);
    expect(bands.start(3)).toBeCloseTo(412.5);
    expect(bandScale(0, [0, 100]).step).toBe(100);
  });

  describe('paths', () => {
    it('writes a line through points', () => {
      expect(linePath([{ x: 0, y: 10 }, { x: 5.123, y: 20 }])).toBe('M0 10 L5.12 20');
      expect(linePath([])).toBe('');
    });

    it('closes each ring of a polygon', () => {
      expect(ringsPath([[{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }], [{ x: 0.2, y: 0.2 }, { x: 0.4, y: 0.2 }, { x: 0.4, y: 0.4 }]])).toBe('M0 0 L1 0 L1 1 Z M0.2 0.2 L0.4 0.2 L0.4 0.4 Z');
    });

    it('writes a pie slice from 12 o\'clock clockwise, large arcs flagged', () => {
      expect(slicePath(50, 50, 40, 0, Math.PI / 2)).toBe('M50 50 L50 10 A40 40 0 0 1 90 50 Z');
      expect(slicePath(50, 50, 40, 0, (3 * Math.PI) / 2)).toContain('A40 40 0 1 1');
    });
  });

  describe('colors', () => {
    it('gives each series a fixed color token and a neutral past the eighth', () => {
      expect(seriesColor(0)).toBe('var(--tmz-series-1)');
      expect(seriesColor(7)).toBe('var(--tmz-series-8)');
      expect(seriesColor(8)).toBe('var(--tmz-series-other)');
    });

    it('steps a value through the sequential ramp, low to high', () => {
      expect([0, 14, 50, 99, 100].map((v) => sequentialStep(v, 0, 100))).toEqual([1, 1, 4, 7, 7]);
      expect(sequentialStep(5, 5, 5)).toBe(4);
      expect(sequentialColor(3)).toBe('var(--tmz-seq-3)');
    });
  });

  it('compares text naturally', () => {
    expect(['10', '2', '1', 'b', 'a'].sort(naturalCompare)).toEqual(['1', '2', '10', 'a', 'b']);
  });

  it('cuts long text with an ellipsis', () => {
    expect(truncate('Municipality', 6)).toBe('Munic…');
    expect(truncate('Cali', 6)).toBe('Cali');
    expect(truncate('Cali', 0)).toBe('');
  });
});

describe('chart data', () => {
  describe('pivot', () => {
    const rows = [r('coverage', 'Cali', 5), r('target', 'Cali', 8), r('coverage', 'Bogotá', 3), r('coverage', null, 1)];

    it('makes a category per group and a series per indicator, in the order listed', () => {
      const data = pivot(rows, ['coverage', 'target'], '(none)', 'listed');

      expect(data.categories).toEqual(['Cali', 'Bogotá', '(none)']);
      expect(data.series).toEqual([
        { name: 'coverage', values: [5, 3, 1] },
        { name: 'target', values: [8, null, null] },
      ]);
    });

    it('orders categories naturally on request, a missing group last', () => {
      const data = pivot([r('a', null, 1), r('a', '10', 1), r('a', '2', 1)], ['a'], '(none)', 'natural');

      expect(data.categories).toEqual(['2', '10', '(none)']);
    });

    it('keeps a series for a named indicator that has no rows, and adds one for an unnamed indicator', () => {
      const data = pivot([r('extra', 'A', 1)], ['coverage'], '(none)', 'listed');

      expect(data.series.map((s) => s.name)).toEqual(['coverage', 'extra']);
      expect(data.series[0].values).toEqual([null]);
    });

    it('sums rows that share an indicator and a group', () => {
      const data = pivot([r('coverage', 'Cali', 40), r('coverage', 'Cali', 35)], ['coverage'], '(none)', 'listed');

      expect(data.series[0].values).toEqual([75]);
    });
  });

  describe('histogram', () => {
    it('counts rows in equal-width ranges between the smallest and the largest value', () => {
      const { buckets } = histogram([r('a', '1', 0), r('a', '2', 2), r('a', '3', 5), r('a', '4', 9), r('a', '5', 10)], ['a'], 2);

      expect(buckets.map((b) => [b.from, b.to, b.counts])).toEqual([[0, 5, [2]], [5, 10, [3]]]);
    });

    it('answers no buckets when there are no rows', () => {
      expect(histogram([], ['a'], 10).buckets).toEqual([]);
    });

    it('treats a bucket count below one as one', () => {
      expect(histogram([r('a', '1', 1), r('a', '2', 3)], ['a'], 0).buckets).toHaveLength(1);
    });
  });
});
