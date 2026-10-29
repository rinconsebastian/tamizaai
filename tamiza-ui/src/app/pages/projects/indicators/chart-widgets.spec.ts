import { byTestId, settle } from '../../../../testing/project-harness';
import { ChartType, IndicatorEntry, QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { Context, all, clearOverlay, config, indicator, isQuery, open, printOf, result, row, text, wait, widget } from './indicators-testing';

interface Query {
  config: WidgetConfig;
  chartType: ChartType;
}

/** Opens a dashboard with one widget of `chartType` over `settings`, and answers its one query with `rows`. */
async function show(chartType: ChartType, settings: Partial<WidgetConfig>, rows: QueryRow[], indicators?: IndicatorEntry[]) {
  const context = await open('viewer', [widget('w1', { chartType, config: config(settings) })], null, indicators ? { indicators } : {});
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(result(rows));
  await settle(context.harness);
  return { ...context, query: requests[0].request.body as Query };
}

async function toggle(context: Context) {
  byTestId(context.root, 'widget-table-toggle')!.click();
  await settle(context.harness);
}

const marks = (context: Context) => all(context.root, 'chart-mark');
const titles = (elements: HTMLElement[]) => elements.map((e) => text(e.querySelector('title')));
const label = (context: Context) => byTestId(context.root, 'chart-svg')!.getAttribute('aria-label');
const tableRows = (context: Context) => all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
const tableHeaders = (context: Context) => Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text);
const svgTexts = (context: Context) => Array.from(context.root.querySelectorAll('[data-testid="chart-svg"] text')).map(text);
const x = (element: Element) => Number(element.getAttribute('x'));

const households = indicator('households_visited');
const rate = indicator('piped_water_rate');

describe('chart widgets', () => {
  afterEach(clearOverlay);

  describe('stat tile', () => {
    it('shows one number for the indicator, written with its format and unit (single figure)', async () => {
      const { root, query } = await show('stat', { indicator: 'households_visited', measure: 'value' }, [row(null, 12450)]);

      expect(text(byTestId(root, 'stat-value'))).toBe('12,450 households');
      expect(text(byTestId(root, 'stat-label'))).toBe('Households visited: value');
      expect(byTestId(root, 'widget-table-toggle')).toBeNull(); // a single figure has no plot
      // The server sums the measure over every row; a stat tile never groups.
      expect(query).toEqual({ config: { ...config({ indicator: 'households_visited', measure: 'value' }), groupBy: null }, chartType: 'stat' });
    });

    it('shows a rate stored as a fraction as a percentage', async () => {
      const { root } = await show('stat', { indicator: 'piped_water_rate' }, [row(null, 0.42)]);

      expect(text(byTestId(root, 'stat-value'))).toBe('42%');
      expect(text(byTestId(root, 'stat-label'))).toBe('Piped water rate');
    });

    it('shows the ratio of the sums of a numerator and a denominator, as the server computes it', async () => {
      const ratio = { numerator: 'numerator', denominator: 'denominator' };
      const { root, query } = await show('stat', { indicator: 'piped_water_rate', measure: null, ratio }, [row(null, 0.5)]);

      expect(text(byTestId(root, 'stat-value'))).toBe('50%');
      expect(text(byTestId(root, 'stat-label'))).toBe('Piped water rate: numerator / denominator');
      expect(query.config.ratio).toEqual(ratio);
      expect(query.config.measure).toBeNull();
    });

    it('writes decimals with two digits, and any other unit after the value', async () => {
      const { root } = await show('stat', { indicator: 'response_rate' }, [row(null, 3.5)], [indicator('response_rate', { unit: 'visits' })]);

      expect(text(byTestId(root, 'stat-value'))).toBe('3.50 visits');
    });

    it('says when nothing matches, for a ratio whose denominator sums to zero has no value', async () => {
      const { root } = await show('stat', { indicator: 'piped_water_rate', ratio: { numerator: 'numerator', denominator: 'denominator' } }, []);

      expect(text(byTestId(root, 'widget-empty'))).toBe('No rows match this widget’s query.');
      expect(byTestId(root, 'stat')).toBeNull();
    });
  });

  describe('column chart', () => {
    const groups = [row('Cali', 75), row('Bogotá', 60), row('Pasto', 35), row('Quibdó', 90), row('Leticia', 10)];

    it('shows one column per group value (one indicator, several groups)', async () => {
      const context = await show('column', {}, groups);

      expect(marks(context)).toHaveLength(5);
      expect(titles(marks(context))).toEqual(['Cali: 75', 'Bogotá: 60', 'Pasto: 35', 'Quibdó: 90', 'Leticia: 10']);
      expect(all(context.root, 'legend-item')).toHaveLength(0); // one series: the caption names it
      expect(context.query).toEqual({ config: config(), chartType: 'column' });
    });

    it('labels each column with its value, format and unit (count with a unit)', async () => {
      const context = await show('column', { indicator: 'households_visited' }, [row('Cali', 12450), row('Pasto', 800)], [households]);

      expect(titles(marks(context))).toEqual(['Cali: 12,450 households', 'Pasto: 800 households']);
    });

    it('writes the axis, the tooltips and the table with the indicator\'s format', async () => {
      const context = await show('column', { indicator: 'piped_water_rate' }, [row('Cali', 0.5), row('Pasto', 0.25)], [rate]);

      expect(titles(marks(context))).toEqual(['Cali: 50%', 'Pasto: 25%']);
      expect(svgTexts(context)).toEqual(expect.arrayContaining(['0%', '10%', '50%']));
      expect(label(context)).toBe('Column chart of Piped water rate by municipality: 2 groups, from 25% to 50%. Highest: Cali.');
      await toggle(context);
      expect(tableRows(context)).toEqual([['Cali', '50%'], ['Pasto', '25%']]);
    });

    it('labels only as many groups as fit, however many groups there are', async () => {
      const many = Array.from({ length: 1500 }, (_, i) => row(`Group ${String(i).padStart(4, '0')}`, i));
      const context = await show('column', {}, many);

      const width = Number(byTestId(context.root, 'chart-svg')!.getAttribute('width'));
      const groupLabels = svgTexts(context).filter((t) => t.startsWith('Gr'));
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

    it('describes a single group in the singular', async () => {
      const context = await show('column', {}, [row('Cali', 75)]);

      expect(label(context)).toBe('Column chart of Coverage by municipality: 1 group, from 75 to 75. Highest: Cali.');
    });

    it('describes itself to assistive technology and offers the same numbers as a table', async () => {
      const context = await show('column', {}, groups);

      const svg = byTestId(context.root, 'chart-svg')!;
      expect(svg.getAttribute('role')).toBe('img');
      expect(label(context)).toBe('Column chart of Coverage by municipality: 5 groups, from 10 to 90. Highest: Quibdó.');
      expect(text(byTestId(context.root, 'chart-caption'))).toBe('Coverage by municipality');

      await toggle(context);
      expect(byTestId(context.root, 'chart-svg')).toBeNull();
      expect(tableHeaders(context)).toEqual(['municipality', 'Coverage']);
      expect(tableRows(context)).toEqual([['Cali', '75'], ['Bogotá', '60'], ['Pasto', '35'], ['Quibdó', '90'], ['Leticia', '10']]);
      expect(byTestId(context.root, 'widget-table-toggle')!.getAttribute('aria-pressed')).toBe('true'); // pressed while the table is shown

      await toggle(context);
      expect(marks(context)).toHaveLength(5);
    });

    it('names a chosen measure or the two columns of a ratio in the caption', async () => {
      const measure = await show('column', { measure: 'count' }, groups);
      expect(text(byTestId(measure.root, 'chart-caption'))).toBe('Coverage: count by municipality');
    });

    it('names the two columns of a ratio in the caption and sends the ratio', async () => {
      const ratio = { numerator: 'numerator', denominator: 'denominator' };
      const context = await show('column', { ratio }, groups);

      expect(text(byTestId(context.root, 'chart-caption'))).toBe('Coverage: numerator / denominator by municipality');
      expect(context.query.config).toMatchObject({ measure: null, ratio });
    });

    it('labels a group that has no value', async () => {
      const context = await show('column', {}, [row(null, 3)]);

      await toggle(context);
      expect(tableRows(context)).toEqual([['(none)', '3']]);
    });

    it('draws a negative value below the zero line', async () => {
      const context = await show('column', {}, [row('A', 50), row('B', -20)]);

      const [a, b] = marks(context);
      expect(Number(b.getAttribute('y'))).toBeGreaterThanOrEqual(Number(a.getAttribute('y')) + Number(a.getAttribute('height')) - 0.01);
    });
  });

  describe('bar chart', () => {
    const rows = [row('Cali', 75), row('Bogotá', 60), row('Pasto', 35)];

    it('describes a single group in the singular', async () => {
      const context = await show('bar', {}, [row('Cali', 75)]);

      expect(label(context)).toBe('Bar chart of Coverage by municipality: 1 group, from 75 to 75. Highest: Cali.');
    });

    it('shows one bar per group, the groups down the chart', async () => {
      const context = await show('bar', {}, rows);

      expect(titles(marks(context))).toEqual(['Cali: 75', 'Bogotá: 60', 'Pasto: 35']);
      expect(svgTexts(context)).toEqual(expect.arrayContaining(['Cali', 'Bogotá', 'Pasto']));
      expect(all(context.root, 'legend-item')).toHaveLength(0);
    });

    it('labels only as many groups as fit, however many groups there are', async () => {
      const many = Array.from({ length: 400 }, (_, i) => row(`Group ${String(i).padStart(4, '0')}`, i));
      const context = await show('bar', {}, many);

      const height = Number(byTestId(context.root, 'chart-svg')!.getAttribute('height'));
      const groupLabels = svgTexts(context).filter((t) => t.startsWith('Gr'));
      expect(groupLabels.length).toBeGreaterThan(0);
      expect(groupLabels.length).toBeLessThanOrEqual(Math.floor(height / 14));
    });

    it('draws bars horizontally from the same baseline, longer for larger values', async () => {
      const context = await show('bar', {}, rows);

      const bars = marks(context);
      expect(new Set(bars.map((b) => b.getAttribute('x'))).size).toBe(1);
      expect(Number(bars[0].getAttribute('width'))).toBeGreaterThan(Number(bars[1].getAttribute('width')));
      expect(Number(bars[1].getAttribute('y'))).toBeGreaterThan(Number(bars[0].getAttribute('y')));
    });

    it('lists the group and the value in the table', async () => {
      const context = await show('bar', {}, rows);

      await toggle(context);
      expect(tableHeaders(context)).toEqual(['municipality', 'Coverage']);
      expect(tableRows(context)[0]).toEqual(['Cali', '75']);
    });
  });

  describe('line chart', () => {
    const rounds = [row('3', 80), row('1', 60), row('10', 95), row('2', 70)];
    const settings = { indicator: 'attendance', groupBy: 'round' };

    it('draws one line through the points in the natural order of the group values (trend over a grouped column)', async () => {
      const context = await show('line', settings, rounds);

      const lines = all(context.root, 'chart-line');
      expect(lines).toHaveLength(1);
      expect(titles(all(context.root, 'chart-dot'))).toEqual(['1: 60', '2: 70', '3: 80', '10: 95']);
      const path = lines[0].getAttribute('d')!;
      expect(path.match(/[ML]/g)).toEqual(['M', 'L', 'L', 'L']);
      const xs = Array.from(path.matchAll(/[ML]([\d.]+) /g)).map((m) => Number(m[1]));
      expect(xs).toEqual([...xs].sort((a, b) => a - b));
    });

    it('draws the three points of three rounds in order', async () => {
      const context = await show('line', settings, [row('1', 60), row('2', 70), row('3', 80)]);

      expect(all(context.root, 'chart-line')).toHaveLength(1);
      expect(titles(all(context.root, 'chart-dot'))).toEqual(['1: 60', '2: 70', '3: 80']);
    });

    it('draws a lone point as a marker, with no line to draw', async () => {
      const context = await show('line', settings, [row('1', 60)]);

      expect(all(context.root, 'chart-line')[0].getAttribute('d')).toBe('');
      expect(all(context.root, 'chart-dot')).toHaveLength(1);
      expect(label(context)).toBe('Line chart of Attendance by round: 1 point, from 60 to 60. Highest: 1.');
    });

    it('summarizes the trend and lists it in the table', async () => {
      const context = await show('line', settings, rounds);

      expect(label(context)).toBe('Line chart of Attendance by round: 4 points, from 60 to 95. Highest: 10.');
      await toggle(context);
      expect(tableRows(context).map((r) => r[0])).toEqual(['1', '2', '3', '10']);
    });
  });

  describe('histogram', () => {
    const values = Array.from({ length: 80 }, (_, i) => 10 + (130 * i) / 79);
    const rowsOf = (list: number[]) => list.map((value) => row(null, value));
    const coverage = rowsOf(values);

    it('shows one bar per bucket, each counting the rows whose value falls in its range (distribution across many groups)', async () => {
      const context = await show('histogram', { buckets: 10, groupBy: null }, coverage);

      expect(marks(context)).toHaveLength(10);
      const counts = titles(marks(context)).map((t) => Number(t.split(': ')[1].split(' ')[0]));
      expect(counts.reduce((a, b) => a + b, 0)).toBe(80);
      // 80 evenly spaced values over 10 ranges: 7 or 8 in each.
      expect(counts).toEqual(Array.from({ length: 10 }, (_, k) => values.filter((_v, i) => Math.min(9, Math.floor((i * 10) / 79)) === k).length));
      expect(Math.min(...counts)).toBeGreaterThanOrEqual(7);
      expect(titles(marks(context))[0]).toBe('10–23: 8 rows');
    });

    it('asks the server for one row per table row when there is no group column, and counts them in the browser', async () => {
      const { query } = await show('histogram', { buckets: 10, groupBy: null }, coverage);

      expect(query).toEqual({ config: config({ buckets: 10, groupBy: null }), chartType: 'histogram' });
    });

    it('can group first, and then counts the groups', async () => {
      const { query, root } = await show('histogram', { buckets: 4 }, [row('a', 0), row('b', 10), row('c', 5), row('d', 3)]);

      expect(query.config.groupBy).toBe('municipality');
      expect(titles(all(root, 'chart-mark'))).toEqual(['0–2.5: 1 row', '2.5–5: 1 row', '5–7.5: 1 row', '7.5–10: 1 row']);
    });

    it('uses ten buckets when none is set', async () => {
      const context = await show('histogram', { groupBy: null }, coverage);

      expect(marks(context)).toHaveLength(10);
    });

    it('buckets to the number asked for, putting the largest value in the last bucket', async () => {
      const context = await show('histogram', { buckets: 4, groupBy: null }, rowsOf([0, 10, 5, 3]));

      expect(titles(marks(context)).map((t) => t.split(': ')[1])).toEqual(['1 row', '1 row', '1 row', '1 row']);
      expect(titles(marks(context))[0]).toBe('0–2.5: 1 row');
      expect(titles(marks(context))[3]).toBe('7.5–10: 1 row');
    });

    it('describes a single range in the singular', async () => {
      const context = await show('histogram', { buckets: 1, groupBy: null }, rowsOf([7, 8]));

      expect(label(context)).toContain(': 1 range, ');
    });

    it('puts every row in one bucket when all values are equal', async () => {
      const context = await show('histogram', { buckets: 3, groupBy: null }, rowsOf([7, 7]));

      const counts = titles(marks(context)).map((t) => Number(t.split(': ')[1].split(' ')[0]));
      expect(counts[0]).toBe(2);
    });

    it('writes the ranges with the indicator\'s format, and the counts as plain numbers', async () => {
      const context = await show('histogram', { indicator: 'piped_water_rate', buckets: 2, groupBy: null }, rowsOf([0.1, 0.2, 0.9]), [rate]);

      expect(titles(marks(context))).toEqual(['10%–50%: 2 rows', '50%–90%: 1 row']);
    });

    it('lists the ranges and counts in the table', async () => {
      const context = await show('histogram', { buckets: 10, groupBy: null }, coverage);

      expect(label(context)).toContain('Histogram of Coverage: 10 ranges');
      await toggle(context);
      expect(tableHeaders(context)).toEqual(['Range', 'Rows of Coverage']);
      expect(tableRows(context)).toHaveLength(10);
      expect(tableRows(context)[0]).toEqual(['10–23', '8']);
    });
  });

  describe('pie chart', () => {
    const visits = [row('Cali', 50), row('Bogotá', 30), row('Pasto', 15), row('Leticia', 5)];
    const settings = { indicator: 'households_visited' };

    it('shows each group as a slice sized by its share of the summed total (shares of a total)', async () => {
      const context = await show('pie', settings, visits, [households]);

      const slices = all(context.root, 'pie-slice');
      expect(slices).toHaveLength(4);
      expect(titles(slices)).toEqual([
        'Cali: 50 households (50%)',
        'Bogotá: 30 households (30%)',
        'Pasto: 15 households (15%)',
        'Leticia: 5 households (5%)',
      ]);
      const legend = all(context.root, 'legend-item').map((item) => Array.from(item.querySelectorAll('span:not([aria-hidden])')).map(text));
      expect(legend).toEqual([
        ['Cali', '50 households · 50%'],
        ['Bogotá', '30 households · 30%'],
        ['Pasto', '15 households · 15%'],
        ['Leticia', '5 households · 5%'],
      ]);
    });

    it('prints the whole legend, never the part a scroll would show (Charts on paper)', async () => {
      const context = await show('pie', settings, visits, [households]);

      const legend = byTestId(context.root, 'chart-legend')!;
      expect(legend.classList).toContain('paper:max-h-none');
      expect(legend.classList).toContain('paper:overflow-visible');
    });

    it('draws the slices from the largest, each in its own color', async () => {
      const context = await show('pie', {}, [row('Small', 10), row('Large', 90)]);

      expect(titles(all(context.root, 'pie-slice'))).toEqual(['Large: 90 (90%)', 'Small: 10 (10%)']);
      expect(all(context.root, 'pie-slice').map((s) => s.getAttribute('style'))).toEqual([expect.stringContaining('var(--tmz-series-1)'), expect.stringContaining('var(--tmz-series-2)')]);
    });

    it('counts a single value at or below zero in the singular', async () => {
      const context = await show('pie', {}, [row('A', 10), row('B', 0)]);

      expect(text(byTestId(context.root, 'pie-skipped'))).toBe('1 group with a zero or negative value is not drawn; the table lists it.');
    });

    it('draws a whole circle for a single group', async () => {
      const context = await show('pie', {}, [row('Cali', 50)]);

      const slice = all(context.root, 'pie-slice')[0];
      expect(slice.tagName.toLowerCase()).toBe('circle');
      expect(titles([slice])).toEqual(['Cali: 50 (100%)']);
      expect(label(context)).toContain(': 1 slice. Largest: Cali, 100%.');
    });

    it('folds the smallest groups past the eighth color into one "Other" slice', async () => {
      const many = Array.from({ length: 10 }, (_, i) => row(`G${i + 1}`, 100 - i * 5));
      const context = await show('pie', {}, many);

      const slices = all(context.root, 'pie-slice');
      expect(slices).toHaveLength(8);
      expect(titles(slices).at(-1)).toMatch(/^Other: /);
      expect(Number(titles(slices).at(-1)!.match(/: (\d+) /)![1])).toBe(100 - 7 * 5 + (100 - 8 * 5) + (100 - 9 * 5));
    });

    it('counts values at or below zero instead of drawing them, and keeps them in the table', async () => {
      const context = await show('pie', {}, [row('A', 10), row('B', 0), row('C', -4)]);

      expect(all(context.root, 'pie-slice')).toHaveLength(1);
      expect(text(byTestId(context.root, 'pie-skipped'))).toBe('2 groups with a zero or negative value are not drawn; the table lists them.');
      await toggle(context);
      expect(tableHeaders(context)).toEqual(['municipality', 'Coverage', 'Share']);
      expect(tableRows(context)).toEqual([['A', '10', '100%'], ['B', '0', '—'], ['C', '-4', '—']]);
    });

    it('summarizes the largest slice for assistive technology', async () => {
      const context = await show('pie', settings, visits, [households]);

      expect(label(context)).toBe('Pie chart of Households visited by municipality: 4 slices. Largest: Cali, 50%.');
    });
  });

  describe('order of bars and columns', () => {
    // As the server returns them under an order: largest first, which is neither alphabetical nor the reverse of it.
    const ordered = [row('Palmira', 120), row('Cali', 75), row('Buga', 40)];
    const sort = { by: 'value', column: null, compareAs: null, direction: 'desc' } as const;

    it('sends the order with the query of a bar chart and draws the bars in the order returned, first at the top (largest value first)', async () => {
      const context = await show('bar', { sort }, ordered);

      expect(context.query.config.sort).toEqual(sort);
      const ys = marks(context).map((m) => Number(m.getAttribute('y')));
      expect(ys).toEqual([...ys].sort((a, b) => a - b));
      expect(marks(context)).toHaveLength(3);
      await toggle(context);
      expect(tableRows(context).map((r) => r[0])).toEqual(['Palmira', 'Cali', 'Buga']);
    });

    it('sends the order with the query of a column chart and draws the columns in the order returned, first on the left', async () => {
      const context = await show('column', { sort }, ordered);

      expect(context.query.config.sort).toEqual(sort);
      const xs = marks(context).map((m) => x(m));
      expect(xs).toEqual([...xs].sort((a, b) => a - b));
      await toggle(context);
      expect(tableRows(context).map((r) => r[0])).toEqual(['Palmira', 'Cali', 'Buga']);
    });

    it('lists the rows of a chart without an order as the server returned them (default order)', async () => {
      const context = await show('column', {}, [row('1', 10), row('2', 20), row('10', 30)]);

      expect(context.query.config.sort).toBeNull();
      await toggle(context);
      expect(tableRows(context).map((r) => r[0])).toEqual(['1', '2', '10']);
    });
  });
});

describe('widget states on paper (dashboard printing)', () => {
  const view = document.defaultView!;

  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    document.documentElement.classList.remove('tmz-printing');
    document.getElementById('tmz-print-page')?.remove();
  });

  /** The browser's dialog, as Chrome's: it blocks, then says it closed. */
  const stubDialog = () => vi.spyOn(view, 'print').mockImplementation(() => view.dispatchEvent(new Event('afterprint')));

  it('holds the print back while a widget\'s rows are on their way, and prints once they come (Widgets still loading)', async () => {
    const context = await open('viewer', [widget('w1')], null);
    const dialog = stubDialog();

    const done = printOf(context).print();
    await wait(150);
    expect(dialog).not.toHaveBeenCalled();

    context.http.expectOne(isQuery).flush(result([row('Cali', 75)]));
    await settle(context.harness);
    await done;

    expect(dialog).toHaveBeenCalledTimes(1);
  });

  it('prints once a widget\'s query has failed: an error is an answer', async () => {
    const context = await open('viewer', [widget('w1')], null);
    const dialog = stubDialog();

    const done = printOf(context).print();
    context.http.expectOne(isQuery).flush({}, { status: 500, statusText: 'Server Error' });
    await settle(context.harness);
    await done;

    expect(dialog).toHaveBeenCalledTimes(1);
  });

  it('says on paper that a widget had not finished loading, in place of its skeleton (Widget that takes too long)', async () => {
    const context = await open('viewer', [widget('w1')], null);

    expect(byTestId(context.root, 'view-loading')!.classList).toContain('paper:hidden');
    const note = byTestId(context.root, 'view-not-loaded')!;
    expect(note.classList).toContain('hidden');
    expect(note.classList).toContain('paper:block');
    expect(text(note)).toBe('This widget had not finished loading when it was printed.');
    context.http.match(isQuery);
  });

  it('leaves the link to the scripts off the paper (Widget whose indicator has not published)', async () => {
    const context = await open('analyst', [widget('w1')], result([], { published: false, missing: { indicator: true, columns: [] } }));

    expect(byTestId(context.root, 'view-scripts-link')!.classList).toContain('paper:hidden');
    expect(byTestId(context.root, 'view-not-published')!.closest('.paper\:hidden')).toBeNull();
  });
});
