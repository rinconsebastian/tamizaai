import { byTestId, settle } from '../../../../testing/project-harness';
import { ChartType, QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { Context, all, clearOverlay, config, isQuery, open, result, row, text, widget } from './indicators-testing';

async function show(chartType: ChartType, settings: Partial<WidgetConfig>, rows: QueryRow[]) {
  const context = await open('viewer', [widget('w1', { chartType, config: config(settings) })], null);
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(result(rows));
  await settle(context.harness);
  return { ...context, query: requests[0].request.body as WidgetConfig };
}

async function toggle(context: Context) {
  byTestId(context.root, 'chart-toggle')!.click();
  await settle(context.harness);
}

const marks = (context: Context) => all(context.root, 'chart-mark');
const titles = (elements: HTMLElement[]) => elements.map((e) => text(e.querySelector('title')));
const label = (context: Context) => byTestId(context.root, 'chart-svg')!.getAttribute('aria-label');
const tableRows = (context: Context) => all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
const tableHeaders = (context: Context) => Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text);
const x = (element: Element) => Number(element.getAttribute('x'));

describe('chart widgets', () => {
  afterEach(clearOverlay);

  describe('stat tile', () => {
    it('shows one number, the sum of the indicator\'s plotted field over every row (single figure)', async () => {
      const { root, query } = await show('stat', { indicators: ['response_rate'] }, [row('response_rate', 'Cali', 40.25), row('response_rate', 'Bogotá', 35), row('response_rate', 'Pasto', 12)]);

      expect(text(byTestId(root, 'stat-value'))).toBe('87.25');
      expect(text(byTestId(root, 'stat-label'))).toBe('response_rate · value');
      expect(text(byTestId(root, 'stat-rows'))).toBe('Sum over 3 rows');
      expect(query.indicators).toEqual(['response_rate']);
    });

    it('names the plotted field and has no table toggle, for a single figure has no plot', async () => {
      const { root } = await show('stat', { indicators: ['coverage'], valueField: 'denominator' }, [row('coverage', null, 1500)]);

      expect(text(byTestId(root, 'stat-value'))).toBe('1,500');
      expect(text(byTestId(root, 'stat-label'))).toBe('coverage · denominator');
      expect(byTestId(root, 'chart-toggle')).toBeNull();
    });
  });

  describe('column chart', () => {
    const groups = [row('coverage', 'Cali', 75), row('coverage', 'Bogotá', 60), row('coverage', 'Pasto', 35), row('coverage', 'Quibdó', 90), row('coverage', 'Leticia', 10)];

    it('shows one column per group value (one indicator, several groups)', async () => {
      const context = await show('column', {}, groups);

      expect(marks(context)).toHaveLength(5);
      expect(titles(marks(context))).toEqual(['Cali · coverage: 75', 'Bogotá · coverage: 60', 'Pasto · coverage: 35', 'Quibdó · coverage: 90', 'Leticia · coverage: 10']);
      expect(all(context.root, 'legend-item')).toHaveLength(0); // one series: the title names it
    });

    it('labels only as many groups as fit, however many groups there are', async () => {
      const many = Array.from({ length: 1500 }, (_, i) => row('coverage', `Group ${String(i).padStart(4, '0')}`, i));
      const context = await show('column', {}, many);

      const width = Number(byTestId(context.root, 'chart-svg')!.getAttribute('width'));
      const groupLabels = Array.from(context.root.querySelectorAll('[data-testid="chart-svg"] text')).map(text).filter((t) => t.startsWith('Gr'));
      expect(groupLabels.length).toBeGreaterThan(0);
      expect(groupLabels.length).toBeLessThanOrEqual(Math.ceil(width / 40));
    });

    it('draws taller columns for larger values, left to right in the order of the rows', async () => {
      const context = await show('column', {}, groups);

      const heights = marks(context).map((m) => Number(m.getAttribute('height')));
      expect(heights[3]).toBeGreaterThan(heights[0]);
      expect(heights[0]).toBeGreaterThan(heights[1]);
      expect(heights[4]).toBeLessThan(heights[2]);
      const xs = marks(context).map(x);
      expect(xs).toEqual([...xs].sort((a, b) => a - b));
    });

    it('colors the marks from the color tokens, not literal colors', async () => {
      const context = await show('column', {}, groups);

      expect(marks(context)[0].getAttribute('style')).toContain('var(--tmz-series-1)');
    });

    it('describes itself to assistive technology and offers the same numbers as a table', async () => {
      const context = await show('column', {}, groups);

      const svg = byTestId(context.root, 'chart-svg')!;
      expect(svg.getAttribute('role')).toBe('img');
      expect(label(context)).toBe('Column chart of coverage by disaggregation: 5 groups, from 10 to 90. Highest: Quibdó.');
      expect(text(byTestId(context.root, 'chart-caption'))).toBe('value by disaggregation');

      await toggle(context);
      expect(byTestId(context.root, 'chart-svg')).toBeNull();
      expect(tableHeaders(context)).toEqual(['disaggregation', 'coverage']);
      expect(tableRows(context)).toEqual([['Cali', '75'], ['Bogotá', '60'], ['Pasto', '35'], ['Quibdó', '90'], ['Leticia', '10']]);
      expect(text(byTestId(context.root, 'chart-toggle'))).toBe('Show as chart');

      await toggle(context);
      expect(marks(context)).toHaveLength(5);
    });

    it('labels a group that has no value', async () => {
      const context = await show('column', {}, [row('coverage', null, 3)]);

      await toggle(context);
      expect(tableRows(context)).toEqual([['(none)', '3']]);
    });

    it('draws a negative value below the zero line', async () => {
      const context = await show('column', {}, [row('coverage', 'A', 50), row('coverage', 'B', -20)]);

      const [a, b] = marks(context);
      expect(Number(b.getAttribute('y'))).toBeGreaterThanOrEqual(Number(a.getAttribute('y')) + Number(a.getAttribute('height')) - 0.01);
    });
  });

  describe('bar chart', () => {
    const rows = [
      row('coverage', 'Cali', 75), row('target', 'Cali', 80),
      row('coverage', 'Bogotá', 60), row('target', 'Bogotá', 70),
      row('coverage', 'Pasto', 35), row('target', 'Pasto', 50),
    ];

    it('shows one series per indicator, sharing the group values on the axis (several indicators compared)', async () => {
      const context = await show('bar', { indicators: ['coverage', 'target'] }, rows);

      expect(marks(context)).toHaveLength(6);
      // Two bars per group, in series order, the groups down the chart.
      expect(titles(marks(context))).toEqual(['Cali · coverage: 75', 'Cali · target: 80', 'Bogotá · coverage: 60', 'Bogotá · target: 70', 'Pasto · coverage: 35', 'Pasto · target: 50']);
      const labels = Array.from(context.root.querySelectorAll('[data-testid="chart-svg"] text')).map(text);
      expect(labels).toEqual(expect.arrayContaining(['Cali', 'Bogotá', 'Pasto']));
    });

    it('labels only as many groups as fit, however many groups there are', async () => {
      const many = Array.from({ length: 400 }, (_, i) => row('coverage', `Group ${String(i).padStart(4, '0')}`, i));
      const context = await show('bar', { indicators: ['coverage'] }, many);

      const height = Number(byTestId(context.root, 'chart-svg')!.getAttribute('height'));
      const groupLabels = Array.from(context.root.querySelectorAll('[data-testid="chart-svg"] text')).map(text).filter((t) => t.startsWith('Gr'));
      expect(groupLabels.length).toBeGreaterThan(0);
      expect(groupLabels.length).toBeLessThanOrEqual(Math.floor(height / 14));
    });

    it('draws bars horizontally from the same baseline', async () => {
      const context = await show('bar', { indicators: ['coverage', 'target'] }, rows);

      const bars = marks(context);
      expect(new Set(bars.map((b) => b.getAttribute('x'))).size).toBe(1);
      expect(Number(bars[1].getAttribute('width'))).toBeGreaterThan(Number(bars[0].getAttribute('width')));
      expect(Number(bars[1].getAttribute('y'))).toBeGreaterThan(Number(bars[0].getAttribute('y')));
    });

    it('gives each series its own color and a legend, so no series is told apart by color alone', async () => {
      const context = await show('bar', { indicators: ['coverage', 'target'] }, rows);

      expect(all(context.root, 'legend-item').map(text)).toEqual(['coverage', 'target']);
      const colors = marks(context).map((m) => m.getAttribute('style'));
      expect(colors[0]).toContain('var(--tmz-series-1)');
      expect(colors[1]).toContain('var(--tmz-series-2)');
      expect(colors[2]).toBe(colors[0]);
    });

    it('lists the series as columns of the table', async () => {
      const context = await show('bar', { indicators: ['coverage', 'target'] }, rows);

      await toggle(context);
      expect(tableHeaders(context)).toEqual(['disaggregation', 'coverage', 'target']);
      expect(tableRows(context)[0]).toEqual(['Cali', '75', '80']);
    });

    it('leaves a gap where an indicator has no row for a group', async () => {
      const context = await show('bar', { indicators: ['coverage', 'target'] }, [row('coverage', 'Cali', 75), row('target', 'Bogotá', 70)]);

      expect(marks(context)).toHaveLength(2);
      await toggle(context);
      expect(tableRows(context)).toEqual([['Cali', '75', '—'], ['Bogotá', '—', '70']]);
    });
  });

  describe('line chart', () => {
    const rounds = [row('attendance', '3', 80), row('attendance', '1', 60), row('attendance', '10', 95), row('attendance', '2', 70)];

    it('draws one line through the points in the natural order of the group values (trend over a grouped column)', async () => {
      const context = await show('line', { indicators: ['attendance'], groupBy: 'round' }, rounds);

      const lines = all(context.root, 'chart-line');
      expect(lines).toHaveLength(1);
      expect(titles(all(context.root, 'chart-dot'))).toEqual(['1 · attendance: 60', '2 · attendance: 70', '3 · attendance: 80', '10 · attendance: 95']);
      const path = lines[0].getAttribute('d')!;
      expect(path.match(/[ML]/g)).toEqual(['M', 'L', 'L', 'L']);
      const xs = Array.from(path.matchAll(/[ML]([\d.]+) /g)).map((m) => Number(m[1]));
      expect(xs).toEqual([...xs].sort((a, b) => a - b));
    });

    it('draws a line per indicator with a legend', async () => {
      const context = await show('line', { indicators: ['attendance', 'target'], groupBy: 'round' }, [
        ...rounds, row('target', '1', 80), row('target', '2', 80), row('target', '3', 80), row('target', '10', 80),
      ]);

      expect(all(context.root, 'chart-line')).toHaveLength(2);
      expect(all(context.root, 'legend-item').map(text)).toEqual(['attendance', 'target']);
    });

    it('breaks the line where a group has no value', async () => {
      const context = await show('line', { indicators: ['attendance', 'target'], groupBy: 'round' }, [
        row('attendance', '1', 60), row('attendance', '2', 70), row('attendance', '3', 80), row('target', '1', 80), row('target', '3', 80),
      ]);

      const target = all(context.root, 'chart-line')[1];
      expect(target.getAttribute('d')).toBe(''); // two lone points: markers only, no line between them
      expect(all(context.root, 'chart-dot')).toHaveLength(5);
    });

    it('summarizes the trend and lists it in the table', async () => {
      const context = await show('line', { indicators: ['attendance'], groupBy: 'round' }, rounds);

      expect(label(context)).toBe('Line chart of attendance by round: 4 points, from 60 to 95. Highest: 10.');
      await toggle(context);
      expect(tableRows(context).map((r) => r[0])).toEqual(['1', '2', '3', '10']);
    });
  });

  describe('histogram', () => {
    const coverage = Array.from({ length: 80 }, (_, i) => row('coverage', `Municipality ${i + 1}`, 10 + (130 * i) / 79));

    it('shows one bar per bucket, each counting the groups whose value falls in its range (distribution across many groups)', async () => {
      const context = await show('histogram', { indicators: ['coverage'], buckets: 10 }, coverage);

      expect(marks(context)).toHaveLength(10);
      const counts = titles(marks(context)).map((t) => Number(t.split(': ')[1]));
      expect(counts.reduce((a, b) => a + b, 0)).toBe(80);
      // 80 evenly spaced values over 10 ranges: 7 or 8 in each.
      expect(counts).toEqual(Array.from({ length: 10 }, (_, k) => coverage.filter((_r, i) => Math.min(9, Math.floor((i * 10) / 79)) === k).length));
      expect(Math.min(...counts)).toBeGreaterThanOrEqual(7);
      expect(titles(marks(context))[0]).toBe('10–23 · coverage: 8');
    });

    it('asks the server for the rows, not for the buckets, and counts them in the browser', async () => {
      const { query } = await show('histogram', { indicators: ['coverage'], buckets: 10 }, coverage);

      expect(query).toEqual({ ...config({ buckets: 10 }), geometryColumn: null });
    });

    it('uses ten buckets when none is set', async () => {
      const context = await show('histogram', { indicators: ['coverage'] }, coverage);

      expect(marks(context)).toHaveLength(10);
    });

    it('buckets to the number asked for, putting the largest value in the last bucket', async () => {
      const context = await show('histogram', { indicators: ['coverage'], buckets: 4 }, [row('coverage', 'a', 0), row('coverage', 'b', 10), row('coverage', 'c', 5), row('coverage', 'd', 3)]);

      expect(titles(marks(context))).toEqual(['0–2.5 · coverage: 1', '2.5–5 · coverage: 1', '5–7.5 · coverage: 1', '7.5–10 · coverage: 1']);
    });

    it('puts every row in one bucket when all values are equal', async () => {
      const context = await show('histogram', { indicators: ['coverage'], buckets: 3 }, [row('coverage', 'a', 7), row('coverage', 'b', 7)]);

      const counts = titles(marks(context)).map((t) => Number(t.split(': ')[1]));
      expect(counts[0]).toBe(2);
    });

    it('counts each indicator\'s rows apart in shared ranges', async () => {
      const context = await show('histogram', { indicators: ['coverage', 'target'], buckets: 2 }, [row('coverage', 'a', 0), row('coverage', 'b', 1), row('target', 'a', 10), row('target', 'b', 9)]);

      expect(titles(marks(context))).toEqual(['0–5 · coverage: 2', '0–5 · target: 0', '5–10 · coverage: 0', '5–10 · target: 2']);
      expect(all(context.root, 'legend-item').map(text)).toEqual(['coverage', 'target']);
    });

    it('lists the ranges and counts in the table', async () => {
      const context = await show('histogram', { indicators: ['coverage'], buckets: 10 }, coverage);

      expect(label(context)).toContain('Histogram of coverage: 10 ranges');
      await toggle(context);
      expect(tableHeaders(context)).toEqual(['Range', 'Rows of coverage']);
      expect(tableRows(context)).toHaveLength(10);
      expect(tableRows(context)[0]).toEqual(['10–23', '8']);
    });
  });

  describe('pie chart', () => {
    const submissions = [row('submissions', 'Cali', 50), row('submissions', 'Bogotá', 30), row('submissions', 'Pasto', 15), row('submissions', 'Leticia', 5)];

    it('shows each group as a slice sized by its share of the summed total (shares of a total)', async () => {
      const context = await show('pie', { indicators: ['submissions'] }, submissions);

      const slices = all(context.root, 'pie-slice');
      expect(slices).toHaveLength(4);
      expect(titles(slices)).toEqual(['Cali: 50 (50%)', 'Bogotá: 30 (30%)', 'Pasto: 15 (15%)', 'Leticia: 5 (5%)']);
      const legend = all(context.root, 'legend-item').map((item) => Array.from(item.querySelectorAll('span:not([aria-hidden])')).map(text));
      expect(legend).toEqual([['Cali', '50 · 50%'], ['Bogotá', '30 · 30%'], ['Pasto', '15 · 15%'], ['Leticia', '5 · 5%']]);
    });

    it('draws the slices from the largest, each in its own color', async () => {
      const context = await show('pie', { indicators: ['submissions'] }, [row('submissions', 'Small', 10), row('submissions', 'Large', 90)]);

      expect(titles(all(context.root, 'pie-slice'))).toEqual(['Large: 90 (90%)', 'Small: 10 (10%)']);
      expect(all(context.root, 'pie-slice').map((s) => s.getAttribute('style'))).toEqual([expect.stringContaining('var(--tmz-series-1)'), expect.stringContaining('var(--tmz-series-2)')]);
    });

    it('draws a whole circle for a single group', async () => {
      const context = await show('pie', { indicators: ['submissions'] }, [row('submissions', 'Cali', 50)]);

      const slice = all(context.root, 'pie-slice')[0];
      expect(slice.tagName.toLowerCase()).toBe('circle');
      expect(titles([slice])).toEqual(['Cali: 50 (100%)']);
    });

    it('folds the smallest groups past the eighth color into one "Other" slice', async () => {
      const many = Array.from({ length: 10 }, (_, i) => row('submissions', `G${i + 1}`, 100 - i * 5));
      const context = await show('pie', { indicators: ['submissions'] }, many);

      const slices = all(context.root, 'pie-slice');
      expect(slices).toHaveLength(8);
      expect(titles(slices).at(-1)).toMatch(/^Other: /);
      expect(Number(titles(slices).at(-1)!.match(/: (\d+) /)![1])).toBe(100 - 7 * 5 + (100 - 8 * 5) + (100 - 9 * 5));
    });

    it('counts values at or below zero instead of drawing them, and keeps them in the table', async () => {
      const context = await show('pie', { indicators: ['submissions'] }, [row('submissions', 'A', 10), row('submissions', 'B', 0), row('submissions', 'C', -4)]);

      expect(all(context.root, 'pie-slice')).toHaveLength(1);
      expect(text(byTestId(context.root, 'pie-skipped'))).toBe('2 groups with a zero or negative value are not drawn; the table lists them.');
      await toggle(context);
      expect(tableHeaders(context)).toEqual(['disaggregation', 'submissions', 'Share']);
      expect(tableRows(context)).toEqual([['A', '10', '100%'], ['B', '0', '—'], ['C', '-4', '—']]);
    });

    it('summarizes the largest slice for assistive technology', async () => {
      const context = await show('pie', { indicators: ['submissions'] }, submissions);

      expect(label(context)).toBe('Pie chart of submissions by disaggregation: 4 slices. Largest: Cali, 50%.');
    });
  });
});
