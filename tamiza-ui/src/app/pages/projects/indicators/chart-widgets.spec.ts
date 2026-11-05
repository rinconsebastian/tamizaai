import en from '../../../../../public/i18n/en.json';
import es from '../../../../../public/i18n/es.json';
import { byTestId, settle } from '../../../../testing/project-harness';
import { pressKey } from '../../../../testing/cdk';
import { ChartType, DashboardColors, IndicatorEntry, PaletteId, QueryResult, QueryRow, QuerySeries, ValueColor, WidgetConfig } from '../../../core/projects/indicators.service';
import { Context, all, cell, clearOverlay, config, indicator, isQuery, open, overlay, printOf, result, row, seriesResult, text, tips, wait, menuItem, openWidgetMenu, toggleTable, widget } from './indicators-testing';

interface Query {
  config: WidgetConfig;
  chartType: ChartType;
}

/**
 * Opens a dashboard with one widget of `chartType` over `settings`, and answers its one query with `rows` (and, for a pie, its `domain`).
 * The dashboard has the palette Tamiza and no colors kept by value unless `colors` says otherwise.
 */
async function show(chartType: ChartType, settings: Partial<WidgetConfig>, rows: QueryRow[], indicators?: IndicatorEntry[], colors?: DashboardColors, domain?: QuerySeries[]) {
  const context = await open('viewer', [widget('w1', { chartType, config: config(settings) })], null, indicators ? { indicators } : {}, colors ? { colors } : {});
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(result(rows, domain ? { domain } : {}));
  await settle(context.harness);
  return { ...context, query: requests[0].request.body as Query };
}

/** Opens a dashboard with one widget of `chartType` over `settings`, and answers its one query with the whole `answer` (a widget with a series). */
async function showAnswer(chartType: ChartType, settings: Partial<WidgetConfig>, answer: QueryResult, indicators?: IndicatorEntry[], colors?: DashboardColors) {
  const context = await open('viewer', [widget('w1', { chartType, config: config(settings) })], null, indicators ? { indicators } : {}, colors ? { colors } : {});
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(answer);
  await settle(context.harness);
  return { ...context, query: requests[0].request.body as Query };
}

async function toggle(context: Context) {
  await toggleTable(context);
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

/** The color token a mark is drawn in: the `var(--tmz-…)` of its style, whichever property holds it (fill, stroke, background). */
const colorOf = (element: Element | null) => /var\(--[\w-]+\)/.exec(element?.getAttribute('style') ?? '')?.[0] ?? null;
/** The colors of a dashboard: a palette and the values it keeps a color for. */
const palette = (name: PaletteId, valueColors: ValueColor[] = []): DashboardColors => ({ palette: name, valueColors });
const pal = (name: PaletteId, n: number) => `var(--tmz-pal-${name}-${n})`;

describe('chart widgets', () => {
  afterEach(clearOverlay);

  describe('stat tile', () => {
    it('shows one number for the indicator, written with its format and unit (single figure)', async () => {
      const { root, query, harness } = await show('stat', { indicator: 'households_visited', measure: 'value' }, [row(null, 12450)]);

      expect(text(byTestId(root, 'stat-value'))).toBe('12,450 households');
      expect(text(byTestId(root, 'stat-label'))).toBe('Households visited: value');
      await openWidgetMenu({ root, harness });
      expect(menuItem('widget-show-table')).toBeNull(); // a single figure has no plot
      // The server sums the measure over every row; a stat tile never groups.
      expect(query).toEqual({ config: { ...config({ indicator: 'households_visited', measure: 'value' }), groupBy: null }, chartType: 'stat' });
    });

    it.each([
      ['a subtitle', { subtitle: '% de quienes asistieron (E4)' }],
      ['a footnote', { footnote: 'Sin ponderar.' }],
    ])('centers its column while it fits and starts at the top once it has %s, so its figure stays whole (Stat tile two rows high)', async (_name, display) => {
      const { root } = await show('stat', { indicator: 'piped_water_rate', display }, [row(null, 0.42)]);

      const column = byTestId(root, 'stat')!;
      expect(column.classList.contains('justify-center-safe')).toBe(true);
      expect(column.classList.contains('justify-center')).toBe(false);
    });

    it.each([
      ['no texts', undefined],
      ['a color', { color: 2 }],
      ['a hidden caption', { caption: { hidden: true as const } }],
      ['a blank subtitle', { subtitle: '   ' }],
    ])('keeps its column centered as it was with %s (Widgets saved before)', async (_name, display) => {
      const { root } = await show('stat', { indicator: 'piped_water_rate', ...(display ? { display } : {}) }, [row(null, 0.42)]);

      const column = byTestId(root, 'stat')!;
      expect(column.classList.contains('justify-center')).toBe(true);
      expect(column.classList.contains('justify-center-safe')).toBe(false);
    });

    it('shows the footnote under the figure and its label', async () => {
      const { root } = await show('stat', { indicator: 'piped_water_rate', display: { footnote: 'Sin ponderar.' } }, [row(null, 0.42)]);

      expect(text(byTestId(root, 'stat-value'))).toBe('42%');
      expect(text(byTestId(root, 'widget-footnote'))).toBe('Sin ponderar.');
      expect(byTestId(root, 'stat-label')!.compareDocumentPosition(byTestId(root, 'widget-footnote')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
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
      expect(await tips(context)).toEqual(['Cali: 75', 'Bogotá: 60', 'Pasto: 35', 'Quibdó: 90', 'Leticia: 10']);
      expect(all(context.root, 'legend-item')).toHaveLength(0); // one series: the caption names it
      expect(context.query).toEqual({ config: config(), chartType: 'column' });
    });

    it('labels each column with its value, format and unit (count with a unit)', async () => {
      const context = await show('column', { indicator: 'households_visited' }, [row('Cali', 12450), row('Pasto', 800)], [households]);

      expect(await tips(context)).toEqual(['Cali: 12,450 households', 'Pasto: 800 households']);
    });

    it('writes the axis, the tooltips and the table with the indicator\'s format', async () => {
      const context = await show('column', { indicator: 'piped_water_rate' }, [row('Cali', 0.5), row('Pasto', 0.25)], [rate]);

      expect(await tips(context)).toEqual(['Cali: 50%', 'Pasto: 25%']);
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

      expect(marks(context)[0].getAttribute('style')).toContain('var(--tmz-pal-tamiza-1)');
    });

    it('describes a single group in the singular', async () => {
      const context = await show('column', {}, [row('Cali', 75)]);

      expect(label(context)).toBe('Column chart of Coverage by municipality: 1 group, from 75 to 75. Highest: Cali.');
    });

    it('describes itself to assistive technology and offers the same numbers as a table', async () => {
      const context = await show('column', {}, groups);

      const svg = byTestId(context.root, 'chart-svg')!;
      expect(svg.getAttribute('role')).toBe('application'); // one Tab stop with a tooltip for each column (add-small-sample-warnings, design D9)
      expect(svg.getAttribute('tabindex')).toBe('0');
      expect(label(context)).toBe('Column chart of Coverage by municipality: 5 groups, from 10 to 90. Highest: Quibdó.');
      expect(text(byTestId(context.root, 'chart-caption'))).toBe('Coverage by municipality');

      await toggle(context);
      expect(byTestId(context.root, 'chart-svg')).toBeNull();
      expect(tableHeaders(context)).toEqual(['municipality', 'Coverage']);
      expect(tableRows(context)).toEqual([['Cali', '75'], ['Bogotá', '60'], ['Pasto', '35'], ['Quibdó', '90'], ['Leticia', '10']]);
      await openWidgetMenu(context);
      expect(menuItem('widget-show-table')!.getAttribute('aria-checked')).toBe('true'); // checked while the table is shown

      menuItem('widget-show-table')!.click();
      await settle(context.harness);
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

      expect(await tips(context)).toEqual(['Cali: 75', 'Bogotá: 60', 'Pasto: 35']);
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
      expect(await tips(context)).toEqual(['1: 60', '2: 70', '3: 80', '10: 95']);
      const path = lines[0].getAttribute('d')!;
      expect(path.match(/[ML]/g)).toEqual(['M', 'L', 'L', 'L']);
      const xs = Array.from(path.matchAll(/[ML]([\d.]+) /g)).map((m) => Number(m[1]));
      expect(xs).toEqual([...xs].sort((a, b) => a - b));
    });

    it('draws the three points of three rounds in order', async () => {
      const context = await show('line', settings, [row('1', 60), row('2', 70), row('3', 80)]);

      expect(all(context.root, 'chart-line')).toHaveLength(1);
      expect(await tips(context)).toEqual(['1: 60', '2: 70', '3: 80']);
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
      const counts = (await tips(context)).map((t) => Number(t.split(': ')[1].split(' ')[0]));
      expect(counts.reduce((a, b) => a + b, 0)).toBe(80);
      // 80 evenly spaced values over 10 ranges: 7 or 8 in each.
      expect(counts).toEqual(Array.from({ length: 10 }, (_, k) => values.filter((_v, i) => Math.min(9, Math.floor((i * 10) / 79)) === k).length));
      expect(Math.min(...counts)).toBeGreaterThanOrEqual(7);
      expect((await tips(context))[0]).toBe('10–23: 8');
    });

    it('asks the server for one row per table row when there is no group column, and counts them in the browser', async () => {
      const { query } = await show('histogram', { buckets: 10, groupBy: null }, coverage);

      expect(query).toEqual({ config: config({ buckets: 10, groupBy: null }), chartType: 'histogram' });
    });

    it('can group first, and then counts the groups', async () => {
      const { query, root, harness } = await show('histogram', { buckets: 4 }, [row('a', 0), row('b', 10), row('c', 5), row('d', 3)]);

      expect(query.config.groupBy).toBe('municipality');
      expect(await tips({ root, harness })).toEqual(['0–2.5: 1', '2.5–5: 1', '5–7.5: 1', '7.5–10: 1']);
    });

    it('uses ten buckets when none is set', async () => {
      const context = await show('histogram', { groupBy: null }, coverage);

      expect(marks(context)).toHaveLength(10);
    });

    it('buckets to the number asked for, putting the largest value in the last bucket', async () => {
      const context = await show('histogram', { buckets: 4, groupBy: null }, rowsOf([0, 10, 5, 3]));

      expect((await tips(context)).map((t) => t.split(': ')[1])).toEqual(['1', '1', '1', '1']);
      expect((await tips(context))[0]).toBe('0–2.5: 1');
      expect((await tips(context))[3]).toBe('7.5–10: 1');
    });

    it('describes a single range in the singular', async () => {
      const context = await show('histogram', { buckets: 1, groupBy: null }, rowsOf([7, 8]));

      expect(label(context)).toContain(': 1 range, ');
    });

    it('puts every row in one bucket when all values are equal', async () => {
      const context = await show('histogram', { buckets: 3, groupBy: null }, rowsOf([7, 7]));

      const counts = (await tips(context)).map((t) => Number(t.split(': ')[1].split(' ')[0]));
      expect(counts[0]).toBe(2);
    });

    it('writes the ranges with the indicator\'s format, and the counts as plain numbers', async () => {
      const context = await show('histogram', { indicator: 'piped_water_rate', buckets: 2, groupBy: null }, rowsOf([0.1, 0.2, 0.9]), [rate]);

      expect(await tips(context)).toEqual(['10%–50%: 2', '50%–90%: 1']);
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
      expect(await tips(context, 'pie')).toEqual([
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

      expect(await tips(context, 'pie')).toEqual(['Large: 90 (90%)', 'Small: 10 (10%)']);
      expect(all(context.root, 'pie-slice').map((s) => s.getAttribute('style'))).toEqual([expect.stringContaining('var(--tmz-pal-tamiza-1)'), expect.stringContaining('var(--tmz-pal-tamiza-2)')]);
    });

    describe('colors from the domain (add-dashboard-palettes)', () => {
      const own = { groupBy: 'water_source' };
      const named = (...values: string[]): QuerySeries[] => values.map((value) => ({ value, kind: 'value' }));
      const sliceTitles = (context: Context) => tips(context, 'pie');
      const sliceColors = (context: Context) => all(context.root, 'pie-slice').map(colorOf);

      it('keeps the colors of the slices when a dashboard filter makes the second the largest, and sizes them by the filtered rows (Colors kept under a dashboard filter)', async () => {
        // Piped is the larger in the widget\'s own rows; after the filter Well is.
        const context = await show('pie', own, [row('Piped', 20), row('Well', 50)], undefined, undefined, named('Piped', 'Well'));

        expect((await sliceTitles(context))).toEqual(['Well: 50 (71.4%)', 'Piped: 20 (28.6%)']);
        expect(sliceColors(context)).toEqual([pal('tamiza', 2), pal('tamiza', 1)]);
        expect(all(context.root, 'legend-item').map((item) => colorOf(item.querySelector('span[aria-hidden]')))).toEqual([pal('tamiza', 2), pal('tamiza', 1)]);
      });

      it('keeps a value that folded into "Other" in "Other" when a dashboard filter makes it the largest (Other kept under a dashboard filter)', async () => {
        // Ten values in the widget\'s own rows: the seven largest have a color, the three smallest (G8, G9, G10) are "Other".
        const domain: QuerySeries[] = [...named('G1', 'G2', 'G3', 'G4', 'G5', 'G6', 'G7'), { value: null, kind: 'other' }];
        const filtered = [row('G2', 10), row('G8', 5), row('G9', 50), row('G10', 1), row('G1', 3)];
        const context = await show('pie', own, filtered, undefined, undefined, domain);

        expect((await sliceTitles(context))).toEqual(['G2: 10 (14.5%)', 'G1: 3 (4.3%)', 'Other: 56 (81.2%)']);
        expect(sliceColors(context)).toEqual([pal('tamiza', 2), pal('tamiza', 1), 'var(--tmz-series-other)']);
      });

      it('draws "Other" after the named slices and in the neutral color whatever is kept by value', async () => {
        const domain: QuerySeries[] = [...named('A', 'B'), { value: null, kind: 'other' }];
        const kept = palette('tamiza', [{ column: 'water_source', value: 'Other', color: 3 }]);
        const context = await show('pie', own, [row('A', 10), row('B', 20), row('Z', 100)], undefined, kept, domain);

        expect((await sliceTitles(context)).map((title) => title.split(':')[0])).toEqual(['B', 'A', 'Other']);
        expect(sliceColors(context)).toEqual([pal('tamiza', 2), pal('tamiza', 1), 'var(--tmz-series-other)']);
      });

      it('colors a slice by its value: the color kept for it, then the free colors in the order of the domain (Same value, same color)', async () => {
        const kept = palette('tamiza', [{ column: 'water_source', value: 'River', color: 1 }]);
        const context = await show('pie', own, [row('Piped', 50), row('Well', 30), row('River', 20)], undefined, kept, named('Piped', 'Well', 'River'));

        expect((await sliceTitles(context)).map((title) => title.split(':')[0])).toEqual(['Piped', 'Well', 'River']);
        expect(sliceColors(context)).toEqual([pal('tamiza', 2), pal('tamiza', 3), pal('tamiza', 1)]);
      });

      it('draws in the palette of its dashboard', async () => {
        const context = await show('pie', own, [row('Piped', 50), row('Well', 30)], undefined, palette('earth'), named('Piped', 'Well'));

        expect(sliceColors(context)).toEqual([pal('earth', 1), pal('earth', 2)]);
      });

      it('gives the missing group its place in the domain and never a color kept by value', async () => {
        const kept = palette('tamiza', [{ column: 'water_source', value: '(none)', color: 4 }]);
        const domain: QuerySeries[] = [...named('Piped'), { value: null, kind: 'none' }];
        const context = await show('pie', own, [row('Piped', 30), row(null, 10)], undefined, kept, domain);

        expect((await sliceTitles(context))).toEqual(['Piped: 30 (75%)', '(none): 10 (25%)']);
        expect(sliceColors(context)).toEqual([pal('tamiza', 1), pal('tamiza', 2)]);
      });

      it('keeps the missing group in the place the domain ranks it, with the next free color, whatever is kept by value', async () => {
        // The domain ranks the missing group by size, as the series' domain does: here between Piped and River.
        const kept = palette('tamiza', [
          { column: 'water_source', value: 'River', color: 2 },
          { column: 'water_source', value: '(none)', color: 4 },
        ]);
        const domain: QuerySeries[] = [...named('Piped'), { value: null, kind: 'none' }, ...named('River')];
        const context = await show('pie', own, [row('Piped', 50), row(null, 30), row('River', 20)], undefined, kept, domain);

        expect((await sliceTitles(context)).map((title) => title.split(':')[0])).toEqual(['Piped', '(none)', 'River']);
        // River's color is set aside, so Piped and the missing group take the free ones in order of the domain.
        expect(sliceColors(context)).toEqual([pal('tamiza', 1), pal('tamiza', 3), pal('tamiza', 2)]);
      });

      describe('the missing group and the fold, by rank', () => {
        // Ten groups of one pie's own rows. The missing group is the third by size, so the domain ranks it there, takes the seven
        // largest and folds G8, G9 and G10 into "Other": the order and the fold this component made from the rows before the domain.
        const rows = [row('G1', 100), row('G2', 90), row(null, 80), row('G4', 70), row('G5', 60), row('G6', 50), row('G7', 40), row('G8', 30), row('G9', 20), row('G10', 10)];
        const domain: QuerySeries[] = [...named('G1', 'G2'), { value: null, kind: 'none' }, ...named('G4', 'G5', 'G6', 'G7'), { value: null, kind: 'other' }];
        const expectedNames = ['G1', 'G2', '(none)', 'G4', 'G5', 'G6', 'G7', 'Other'];
        const expectedColors = [1, 2, 3, 4, 5, 6, 7].map((n) => pal('tamiza', n)).concat('var(--tmz-series-other)');
        const names = async (context: Context) => (await sliceTitles(context)).map((title) => title.split(':')[0]);

        it('colors the missing group by its rank and folds the smallest groups, not it, into "Other"', async () => {
          const context = await show('pie', own, rows, undefined, undefined, domain);

          expect(await names(context)).toEqual(expectedNames);
          expect(sliceColors(context)).toEqual(expectedColors);
          expect((await sliceTitles(context)).at(-1)).toMatch(/^Other: 60 /);
        });

        it('draws the same slices in the same colors from an answer with no domain (an older API)', async () => {
          const context = await show('pie', own, rows);

          expect(await names(context)).toEqual(expectedNames);
          expect(sliceColors(context)).toEqual(expectedColors);
          expect((await sliceTitles(context)).at(-1)).toMatch(/^Other: 60 /);
        });

        it('draws an answer with an empty domain as one with none', async () => {
          const context = await show('pie', own, rows, undefined, undefined, []);

          expect(await names(context)).toEqual(expectedNames);
          expect(sliceColors(context)).toEqual(expectedColors);
        });
      });

      it('sums into "Other" a group the domain does not hold but the filtered rows give a value above zero, after the named slices and in the neutral color', async () => {
        // In the pie\'s own rows Lake and River have no share, so the domain leaves them out and has no "Other" entry. A dashboard
        // filter then makes both positive.
        const context = await show('pie', own, [row('Piped', 50), row('Well', 10), row('Lake', 15), row('River', 25)], undefined, undefined, named('Piped', 'Well'));

        expect((await sliceTitles(context))).toEqual(['Piped: 50 (50%)', 'Well: 10 (10%)', 'Other: 40 (40%)']);
        expect(sliceColors(context)).toEqual([pal('tamiza', 1), pal('tamiza', 2), 'var(--tmz-series-other)']);
        expect(all(context.root, 'legend-item').map((item) => colorOf(item.querySelector('span[aria-hidden]')))).toEqual([pal('tamiza', 1), pal('tamiza', 2), 'var(--tmz-series-other)']);
        expect(all(context.root, 'legend-item').map((item) => text(item.querySelector('span:not([aria-hidden])')))).toEqual(['Piped', 'Well', 'Other']);
        expect(byTestId(context.root, 'pie-skipped')).toBeNull();
      });

      it('leaves a group outside the domain with a value at or below zero out of "Other", and counts it', async () => {
        const context = await show('pie', own, [row('Piped', 30), row('Lake', -5), row('River', 0)], undefined, undefined, named('Piped'));

        expect((await sliceTitles(context))).toEqual(['Piped: 30 (100%)']);
        expect(text(byTestId(context.root, 'pie-skipped'))).toBe('2 groups with a zero or negative value are not drawn; the table lists them.');
      });

      it('adds a group outside the domain to the "Other" the domain already holds', async () => {
        const domain: QuerySeries[] = [...named('A'), { value: null, kind: 'other' }];
        const context = await show('pie', own, [row('A', 10), row('Z', 30), row('Y', 20)], undefined, undefined, domain);

        expect((await sliceTitles(context))).toEqual(['A: 10 (16.7%)', 'Other: 50 (83.3%)']);
        expect(sliceColors(context)).toEqual([pal('tamiza', 1), 'var(--tmz-series-other)']);
      });

      it('still counts a group at or below zero instead of drawing it', async () => {
        const context = await show('pie', own, [row('Piped', 30), row('Well', 0)], undefined, undefined, named('Piped', 'Well'));

        expect((await sliceTitles(context))).toEqual(['Piped: 30 (100%)']);
        expect(text(byTestId(context.root, 'pie-skipped'))).toBe('1 group with a zero or negative value is not drawn; the table lists it.');
      });

      it('lists every group in the table, "Other" folded or not', async () => {
        const domain: QuerySeries[] = [...named('A'), { value: null, kind: 'other' }];
        const context = await show('pie', own, [row('A', 10), row('Z', 30)], undefined, undefined, domain);

        await toggle(context);
        expect(tableRows(context)).toEqual([['A', '10', '25%'], ['Z', '30', '75%']]);
      });

      it('still draws a pie whose answer has no domain (an older API), in the order of size and with the kept colors', async () => {
        const kept = palette('tamiza', [{ column: 'water_source', value: 'Small', color: 5 }]);
        const context = await show('pie', own, [row('Small', 10), row('Large', 90)], undefined, kept);

        expect((await sliceTitles(context))).toEqual(['Large: 90 (90%)', 'Small: 10 (10%)']);
        expect(sliceColors(context)).toEqual([pal('tamiza', 1), pal('tamiza', 5)]);
      });
    });

    it('counts a single value at or below zero in the singular', async () => {
      const context = await show('pie', {}, [row('A', 10), row('B', 0)]);

      expect(text(byTestId(context.root, 'pie-skipped'))).toBe('1 group with a zero or negative value is not drawn; the table lists it.');
    });

    it('draws a whole circle for a single group', async () => {
      const context = await show('pie', {}, [row('Cali', 50)]);

      const slice = all(context.root, 'pie-slice')[0];
      expect(slice.tagName.toLowerCase()).toBe('circle');
      expect(await tips(context, 'pie')).toEqual(['Cali: 50 (100%)']);
      expect(label(context)).toContain(': 1 slice. Largest: Cali, 100%.');
    });

    it('folds the smallest groups past the eighth color into one "Other" slice', async () => {
      const many = Array.from({ length: 10 }, (_, i) => row(`G${i + 1}`, 100 - i * 5));
      const context = await show('pie', {}, many);

      const slices = all(context.root, 'pie-slice');
      expect(slices).toHaveLength(8);
      expect((await tips(context, 'pie')).at(-1)).toMatch(/^Other: /);
      expect(Number((await tips(context, 'pie')).at(-1)!.match(/: (\d+) /)![1])).toBe(100 - 7 * 5 + (100 - 8 * 5) + (100 - 9 * 5));
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

  describe('colors of a chart with no series (add-dashboard-palettes)', () => {
    const groups = [row('Cali', 75), row('Bogotá', 60), row('Pasto', 35), row('Rionegro', 90), row('Leticia', 10)];
    const accent = (kind: 'column' | 'bar', colors: DashboardColors, display?: WidgetConfig['display']) => show(kind, display ? { display } : {}, groups, undefined, colors);

    it('draws the columns in the place the widget keeps in its palette (Column chart in color 3)', async () => {
      const context = await accent('column', palette('tamiza'), { color: 3 });

      expect(marks(context).map(colorOf)).toEqual(Array(5).fill(pal('tamiza', 3)));
    });

    it('draws a widget saved before colors in color 1 and sends the query it always sent (Widget saved before colors)', async () => {
      const context = await accent('column', palette('tamiza'));

      expect(marks(context).map(colorOf)).toEqual(Array(5).fill(pal('tamiza', 1)));
      expect(context.query.config).not.toHaveProperty('display');
    });

    it('keeps the widget\'s number and draws it in the palette of the dashboard it is on (Palette changed)', async () => {
      const context = await accent('column', palette('vivid'), { color: 3 });

      expect(marks(context).map(colorOf)).toEqual(Array(5).fill(pal('vivid', 3)));
    });

    it('draws a bar chart in its color too', async () => {
      const context = await accent('bar', palette('earth'), { color: 5 });

      expect(marks(context).map(colorOf)).toEqual(Array(5).fill(pal('earth', 5)));
    });

    it('draws the one column that has a color kept in it, and every other column in the widget\'s color (One bar highlighted)', async () => {
      const context = await accent('column', palette('tamiza', [{ column: 'municipality', value: 'Rionegro', color: 2 }]));

      expect(marks(context).map(colorOf)).toEqual([pal('tamiza', 1), pal('tamiza', 1), pal('tamiza', 1), pal('tamiza', 2), pal('tamiza', 1)]);
    });

    it('highlights a bar of a bar chart the same way, and draws the others in the widget\'s own color', async () => {
      const context = await accent('bar', palette('earth', [{ column: 'municipality', value: 'Cali', color: 8 }]), { color: 4 });

      expect(marks(context).map(colorOf)).toEqual([pal('earth', 8), pal('earth', 4), pal('earth', 4), pal('earth', 4), pal('earth', 4)]);
    });

    it('does not give a bar the next free color: a value with no color kept stays in the widget\'s color', async () => {
      const context = await accent('column', palette('tamiza', [{ column: 'municipality', value: 'Pasto', color: 1 }]), { color: 6 });

      expect(marks(context).map(colorOf)).toEqual([pal('tamiza', 6), pal('tamiza', 6), pal('tamiza', 1), pal('tamiza', 6), pal('tamiza', 6)]);
    });

    it('matches a group value by its exact text, and never takes the missing group for "(none)"', async () => {
      const kept = palette('tamiza', [
        { column: 'municipality', value: 'cali', color: 3 },
        { column: 'municipality', value: '(none)', color: 4 },
        { column: 'sex', value: 'Pasto', color: 5 },
      ]);
      const context = await show('column', {}, [row('Cali', 75), row('Pasto', 35), row(null, 5)], undefined, kept);

      expect(marks(context).map(colorOf)).toEqual(Array(3).fill(pal('tamiza', 1)));
    });

    it('draws a line chart with no series in the widget\'s color whatever is kept by value', async () => {
      const kept = palette('tamiza', [{ column: 'round', value: '2', color: 2 }]);
      const context = await show('line', { indicator: 'attendance', groupBy: 'round', display: { color: 4 } }, [row('1', 60), row('2', 70), row('3', 80)], undefined, kept);

      expect(all(context.root, 'chart-line').map(colorOf)).toEqual([pal('tamiza', 4)]);
      expect(all(context.root, 'chart-dot').map(colorOf)).toEqual(Array(3).fill(pal('tamiza', 4)));
    });

    it('draws a histogram in the widget\'s color whatever is kept by value', async () => {
      const kept = palette('tamiza', [{ column: 'municipality', value: 'Cali', color: 2 }]);
      const context = await show('histogram', { buckets: 2, groupBy: null, display: { color: 7 } }, [row('Cali', 1), row('Pasto', 5), row('Buga', 9)], undefined, kept);

      expect(marks(context).map(colorOf)).toEqual(Array(2).fill(pal('tamiza', 7)));
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

describe('chart widgets with a series (design D5)', () => {
  afterEach(clearOverlay);

  const hits = (context: Context) => all(context.root, 'chart-hit');
  const legend = (context: Context) => all(context.root, 'legend-name').map(text);
  const swatches = (context: Context) => all(context.root, 'legend-swatch').map((s) => s.getAttribute('style'));
  const tooltip = () => overlay().querySelector<HTMLElement>('[data-testid="chart-tooltip"]');
  const tooltipLines = () => Array.from(overlay().querySelectorAll('[data-testid="tooltip-line"]')).map(text);
  const hover = async (context: Context, target: Element) => {
    target.dispatchEvent(new MouseEvent('pointerenter', { clientX: 40, clientY: 30 }));
    await settle(context.harness);
  };
  const number = (element: Element, name: string) => Number(element.getAttribute(name));
  /** What the live region says; a zero-width mark makes the same words twice in a row a change, and is not read. */
  const spoken = (context: Context) => text(byTestId(context.root, 'marks-announcement')).replaceAll('\u200b', '');

  /** `households_by_source` (municipality by water source): Cali has 35 Piped and 15 Well, Palmira 20 Piped. */
  const answer = () => seriesResult([cell('Cali', 'Piped', 35, 3), cell('Cali', 'Well', 15, 2), cell('Palmira', 'Piped', 20, 1)], ['Piped', 'Well']);
  const source = { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source' } as const;

  describe('grouped columns', () => {
    it('puts one column per series side by side in each group, with a legend (Grouped columns)', async () => {
      const context = await showAnswer('column', { ...source, layout: 'grouped' }, answer(), [households]);

      const marks_ = marks(context);
      expect(marks_).toHaveLength(3);
      const [piped, well] = marks_;
      expect(x(well)).toBeGreaterThan(x(piped) + number(piped, 'width')); // side by side, with the surface between
      expect(x(well) - (x(piped) + number(piped, 'width'))).toBeGreaterThanOrEqual(2);
      expect(number(piped, 'y') + number(piped, 'height')).toBeCloseTo(number(well, 'y') + number(well, 'height'), 1); // one baseline
      expect(number(piped, 'height')).toBeGreaterThan(number(well, 'height'));
      expect(legend(context)).toEqual(['Piped', 'Well']);
      expect(byTestId(context.root, 'chart-caption')).toBeNull(); // the legend takes the caption's place
      expect(context.query.config).toMatchObject({ seriesBy: 'water_source', layout: 'grouped' });
    });

    it('keeps a series in the same color in every group, by its place among the series, and thin marks', async () => {
      const context = await showAnswer('column', { ...source, layout: 'grouped' }, answer(), [households]);

      expect(marks(context).map((m) => m.getAttribute('style'))).toEqual([
        expect.stringContaining('var(--tmz-pal-tamiza-1)'),
        expect.stringContaining('var(--tmz-pal-tamiza-2)'),
        expect.stringContaining('var(--tmz-pal-tamiza-1)'),
      ]);
      expect(swatches(context)).toEqual([expect.stringContaining('var(--tmz-pal-tamiza-1)'), expect.stringContaining('var(--tmz-pal-tamiza-2)')]);
      for (const mark of marks(context)) {
        expect(number(mark, 'width')).toBeLessThanOrEqual(24);
      }
    });

    it('draws a bar chart grouped the same way, the groups down the chart', async () => {
      const context = await showAnswer('bar', { ...source, layout: 'grouped' }, answer(), [households]);

      const [piped, well, palmira] = marks(context);
      expect(number(well, 'y')).toBeGreaterThan(number(piped, 'y') + number(piped, 'height'));
      expect(number(palmira, 'y')).toBeGreaterThan(number(well, 'y'));
      expect(number(piped, 'x')).toBe(number(well, 'x')); // one baseline
      expect(number(piped, 'width')).toBeGreaterThan(number(well, 'width'));
    });

    it('draws a group with no value for a series without that column', async () => {
      const context = await showAnswer('column', { ...source, layout: 'grouped' }, answer(), [households]);

      // Palmira has no Well: two groups, three columns.
      expect(marks(context)).toHaveLength(3);
    });
  });

  describe('stacked columns', () => {
    it('stacks the segments from the axis in the order of the series, with two pixels between them', async () => {
      const context = await showAnswer('column', { ...source, layout: 'stacked' }, answer(), [households]);

      const [piped, well] = marks(context);
      expect(x(well)).toBe(x(piped));
      expect(number(piped, 'width')).toBe(number(well, 'width'));
      const gap = number(piped, 'y') - (number(well, 'y') + number(well, 'height'));
      expect(gap).toBeCloseTo(2, 1);
      expect(number(piped, 'height')).toBeGreaterThan(number(well, 'height'));
      expect(number(piped, 'width')).toBeLessThanOrEqual(28);
    });

    it('stacks the segments of a bar chart to the right', async () => {
      const context = await showAnswer('bar', { ...source, layout: 'stacked' }, answer(), [households]);

      const [piped, well] = marks(context);
      expect(number(well, 'x') - (number(piped, 'x') + number(piped, 'width'))).toBeCloseTo(2, 1);
      expect(number(piped, 'y')).toBe(number(well, 'y'));
    });

    it('stacks a value below zero below the axis (a negative value stacked)', async () => {
      const mixed = seriesResult([cell('Cali', 'Piped', 30), cell('Cali', 'Well', -10), cell('Palmira', 'Piped', 20), cell('Palmira', 'Well', 15)], ['Piped', 'Well']);
      const context = await showAnswer('column', { ...source, layout: 'stacked' }, mixed, [households]);

      const [piped, well, palmiraPiped, palmiraWell] = marks(context);
      expect(number(well, 'y')).toBeGreaterThanOrEqual(number(piped, 'y') + number(piped, 'height') - 0.01); // below the axis, not on top
      expect(number(palmiraWell, 'y')).toBeLessThan(number(palmiraPiped, 'y')); // positive values still stack up
      expect(svgTexts(context)).toEqual(expect.arrayContaining(['-10', '0']));
    });

    it('lists a stacked chart as a cross table with a Total column and a Total row (Stacked chart as a table)', async () => {
      const context = await showAnswer('column', { ...source, layout: 'stacked' }, answer(), [households]);

      await toggle(context);
      expect(byTestId(context.root, 'chart-svg')).toBeNull();
      expect(tableHeaders(context)).toEqual(['municipality', 'Piped', 'Well', 'Total']);
      expect(tableRows(context)).toEqual([
        ['Cali', '35 households', '15 households', '50 households'],
        ['Palmira', '20 households', '—', '20 households'],
        ['Total', '55 households', '15 households', '70 households'],
      ]);
    });
  });

  describe('stacked to 100%', () => {
    const shares = () => seriesResult([cell('Cali', 'Piped', 60, 6), cell('Cali', 'Well', 40, 4)], ['Piped', 'Well']);

    it('draws each bar as shares on a 0 to 100% axis, whatever the indicator\'s format (Stacked to 100%)', async () => {
      const context = await showAnswer('bar', { ...source, layout: 'percent' }, shares(), [households]);

      const [piped, well] = marks(context);
      const plot = number(piped, 'width') + number(well, 'width') + 2;
      expect(number(piped, 'width') / plot).toBeCloseTo(0.6, 1);
      expect(number(well, 'width') / plot).toBeCloseTo(0.4, 1);
      expect(svgTexts(context)).toEqual(expect.arrayContaining(['0%', '25%', '50%', '75%', '100%']));
    });

    it('reads 60% and 40% in the tooltip of a segment, and the values 60 and 40', async () => {
      const context = await showAnswer('bar', { ...source, layout: 'percent' }, shares(), [households]);

      await hover(context, hits(context)[0]);
      expect(text(overlay().querySelector('[data-testid="tooltip-heading"]'))).toBe('Cali');
      expect(tooltipLines()).toEqual(['Piped: 60%', 'Households visited: 60 households']);
      await hover(context, hits(context)[1]);
      expect(tooltipLines()).toEqual(['Well: 40%', 'Households visited: 40 households']);
    });

    it('leaves a negative value out of the shares and counts it in a note (a negative value left out of a 100% bar)', async () => {
      const mixed = seriesResult([cell('Palmira', 'Piped', 30), cell('Palmira', 'River', -5), cell('Palmira', 'Well', 10)], ['Piped', 'River', 'Well']);
      const context = await showAnswer('bar', { ...source, layout: 'percent' }, mixed, [households]);

      expect(marks(context)).toHaveLength(2);
      const [piped, well] = marks(context);
      const plot = number(piped, 'width') + number(well, 'width') + 2;
      expect(number(piped, 'width') / plot).toBeCloseTo(0.75, 1);
      expect(text(byTestId(context.root, 'series-negative-left-out'))).toBe('1 negative value is left out of the shares.');
      await toggle(context);
      expect(tableHeaders(context)).toEqual(['municipality', 'Piped', 'River', 'Well', 'Total']);
      expect(tableRows(context)).toEqual([['Palmira', '75%', null, '25%', '35 households'].map((cellText) => cellText ?? '—')]);
    });
  });

  describe('order and colors', () => {
    it('draws the bars in the order of the answer and keeps the series in the same order inside each (Stacked bars ordered by their totals)', async () => {
      const sort = { by: 'value', column: null, compareAs: null, direction: 'desc' } as const;
      const ordered = seriesResult(
        [cell('Palmira', 'Piped', 300), cell('Palmira', 'Well', 200), cell('Cali', 'Piped', 100), cell('Cali', 'Well', 200)],
        ['Piped', 'Well'],
      );
      const context = await showAnswer('bar', { ...source, layout: 'stacked', sort }, ordered, [households]);

      expect(context.query.config.sort).toEqual(sort);
      const [palmiraPiped, palmiraWell, caliPiped, caliWell] = marks(context);
      expect(number(palmiraPiped, 'y')).toBeLessThan(number(caliPiped, 'y')); // Palmira (500) first, at the top
      expect(number(palmiraPiped, 'x')).toBe(number(caliPiped, 'x'));
      expect(number(palmiraWell, 'x')).toBeGreaterThan(number(palmiraPiped, 'x')); // Piped nearest the axis in both
      expect(number(caliWell, 'x')).toBeGreaterThan(number(caliPiped, 'x'));
      await toggle(context);
      expect(tableRows(context).map((r) => r[0])).toEqual(['Palmira', 'Cali', 'Total']);
    });

    it('lists the series in the order the answer gives, then "(none)", then "Other", the last in its own neutral', async () => {
      const folded = seriesResult(
        [cell('Cali', 'Piped', 10), cell('Cali', null, 2, 1, 'none'), cell('Cali', null, 3, 1, 'other')],
        ['Piped', { value: 'Well', kind: 'value' }, { value: null, kind: 'none' }, { value: null, kind: 'other' }],
      );
      const context = await showAnswer('column', { ...source, layout: 'stacked' }, folded, [households]);

      expect(legend(context)).toEqual(['Piped', 'Well', '(none)', 'Other']);
      expect(swatches(context)).toEqual([
        expect.stringContaining('var(--tmz-pal-tamiza-1)'),
        expect.stringContaining('var(--tmz-pal-tamiza-2)'),
        expect.stringContaining('var(--tmz-pal-tamiza-3)'),
        expect.stringContaining('var(--tmz-series-other)'),
      ]);
      expect(marks(context).map((m) => m.getAttribute('style'))).toEqual([
        expect.stringContaining('var(--tmz-pal-tamiza-1)'),
        expect.stringContaining('var(--tmz-pal-tamiza-3)'),
        expect.stringContaining('var(--tmz-series-other)'),
      ]);
    });

    it('keeps the colors and the legend of every series when a dashboard filter leaves one without rows (Colors kept under a dashboard filter)', async () => {
      // Well has no rows after the filter, but the answer still lists it, in its place.
      const filtered = seriesResult([cell('Cali', 'Piped', 35), cell('Cali', 'River', 5)], ['Piped', 'Well', 'River']);
      const context = await showAnswer('column', { ...source, layout: 'stacked' }, filtered, [households]);

      expect(legend(context)).toEqual(['Piped', 'Well', 'River']);
      expect(marks(context).map((m) => m.getAttribute('style'))).toEqual([expect.stringContaining('var(--tmz-pal-tamiza-1)'), expect.stringContaining('var(--tmz-pal-tamiza-3)')]);
    });

    describe('colors of the dashboard (add-dashboard-palettes)', () => {
      const three = () => seriesResult([cell('Cali', 'Piped', 35), cell('Cali', 'River', 5), cell('Cali', 'Well', 15)], ['Piped', 'River', 'Well']);
      const swatchColors = (context: Context) => all(context.root, 'legend-swatch').map(colorOf);

      it('draws a series in the color its dashboard keeps for its value, in its segments and in its legend (Color kept by value)', async () => {
        const kept = palette('tamiza', [{ column: 'water_source', value: 'River', color: 6 }]);
        const context = await showAnswer('column', { ...source, layout: 'stacked' }, three(), [households], kept);

        expect(legend(context)).toEqual(['Piped', 'River', 'Well']);
        expect(swatchColors(context)).toEqual([pal('tamiza', 1), pal('tamiza', 6), pal('tamiza', 2)]);
        expect(marks(context).map(colorOf)).toEqual([pal('tamiza', 1), pal('tamiza', 6), pal('tamiza', 2)]);
      });

      it('sets the colors kept aside, so the others take the remaining colors in the order of the series (Colors set aside)', async () => {
        const kept = palette('tamiza', [{ column: 'water_source', value: 'Well', color: 1 }]);
        const context = await showAnswer('column', { ...source, layout: 'stacked' }, three(), [households], kept);

        expect(swatchColors(context)).toEqual([pal('tamiza', 2), pal('tamiza', 3), pal('tamiza', 1)]); // Piped, River, Well
        expect(marks(context).map(colorOf)).toEqual(swatchColors(context));
      });

      it('keeps a series in its color when a dashboard filter leaves it without rows, kept or not', async () => {
        const kept = palette('tamiza', [{ column: 'water_source', value: 'Well', color: 1 }]);
        const filtered = seriesResult([cell('Cali', 'Piped', 35), cell('Cali', 'River', 5)], ['Piped', 'River', 'Well']);
        const context = await showAnswer('column', { ...source, layout: 'stacked' }, filtered, [households], kept);

        expect(swatchColors(context)).toEqual([pal('tamiza', 2), pal('tamiza', 3), pal('tamiza', 1)]);
        expect(marks(context).map(colorOf)).toEqual([pal('tamiza', 2), pal('tamiza', 3)]);
      });

      it('draws "Other" in the neutral color whatever is kept by value (Other stays neutral)', async () => {
        const kept = palette('tamiza', [
          { column: 'water_source', value: 'Piped', color: 8 },
          { column: 'water_source', value: 'Other', color: 4 },
        ]);
        const folded = seriesResult(
          [cell('Cali', 'Piped', 10), cell('Cali', 'Well', 5), cell('Cali', null, 3, 1, 'other')],
          ['Piped', 'Well', { value: null, kind: 'other' }],
        );
        const context = await showAnswer('column', { ...source, layout: 'stacked' }, folded, [households], kept);

        expect(swatchColors(context)).toEqual([pal('tamiza', 8), pal('tamiza', 1), 'var(--tmz-series-other)']);
      });

      it('draws the series in the palette of the dashboard (Palette changed)', async () => {
        const context = await showAnswer('column', { ...source, layout: 'grouped' }, answer(), [households], palette('vivid'));

        expect(swatchColors(context)).toEqual([pal('vivid', 1), pal('vivid', 2)]);
        expect(marks(context).map(colorOf)).toEqual([pal('vivid', 1), pal('vivid', 2), pal('vivid', 1)]);
      });

      it('colors the lines of a line chart by their series\' values too', async () => {
        const lines = seriesResult([cell('1', 'Cali', 60), cell('2', 'Cali', 70), cell('1', 'Buga', 50), cell('2', 'Buga', 55)], ['Buga', 'Cali']);
        const kept = palette('earth', [{ column: 'municipality', value: 'Cali', color: 7 }]);
        const context = await showAnswer('line', { indicator: 'attendance', groupBy: 'round', seriesBy: 'municipality' }, lines, undefined, kept);

        expect(swatchColors(context)).toEqual([pal('earth', 1), pal('earth', 7)]);
        expect(all(context.root, 'chart-line').map(colorOf)).toEqual([pal('earth', 1), pal('earth', 7)]);
      });

      it('gives a chart with a series no color of its own: the widget\'s color is for a chart with none', async () => {
        const context = await showAnswer('column', { ...source, layout: 'grouped', display: { color: 5 } }, answer(), [households], palette('tamiza'));

        expect(swatchColors(context)).toEqual([pal('tamiza', 1), pal('tamiza', 2)]);
      });
    });

    it('draws no legend for a widget whose answer holds a single series, and names it in the caption', async () => {
      const one = seriesResult([cell('Cali', 'Piped', 35), cell('Palmira', 'Piped', 20)], ['Piped']);
      const context = await showAnswer('column', { ...source, layout: 'grouped' }, one, [households]);

      expect(all(context.root, 'legend-item')).toHaveLength(0);
      expect(text(byTestId(context.root, 'chart-caption'))).toBe('Households visited by municipality');
    });
  });

  describe('lines by series', () => {
    const rounds = { indicator: 'attendance', groupBy: 'round', seriesBy: 'municipality' } as const;

    it('draws one line per series through its own points, with a legend naming them (One line per series)', async () => {
      const lines = seriesResult(
        [cell('1', 'Cali', 60), cell('2', 'Cali', 70), cell('3', 'Cali', 80), cell('1', 'Buga', 50), cell('2', 'Buga', 55), cell('3', 'Buga', 65)],
        ['Buga', 'Cali'],
      );
      const context = await showAnswer('line', rounds, lines);

      expect(all(context.root, 'chart-line')).toHaveLength(2);
      expect(all(context.root, 'chart-line').map((line) => line.getAttribute('d')!.match(/[ML]/g))).toEqual([['M', 'L', 'L'], ['M', 'L', 'L']]);
      expect(all(context.root, 'chart-dot')).toHaveLength(6);
      expect(legend(context)).toEqual(['Buga', 'Cali']);
      expect(all(context.root, 'chart-line').map((line) => line.getAttribute('style'))).toEqual([expect.stringContaining('var(--tmz-pal-tamiza-1)'), expect.stringContaining('var(--tmz-pal-tamiza-2)')]);
    });

    it('puts the groups in natural order, 2 before 10, whatever order the answer lists them in', async () => {
      const lines = seriesResult([cell('10', 'Cali', 90), cell('2', 'Cali', 70), cell('1', 'Cali', 60), cell('10', 'Buga', 95), cell('2', 'Buga', 75)], ['Buga', 'Cali']);
      const context = await showAnswer('line', rounds, lines);

      const xs = all(context.root, 'chart-hit').map((hit) => number(hit, 'x'));
      expect(xs).toEqual([...xs].sort((a, b) => a - b));
      await toggle(context);
      expect(tableRows(context).map((r) => r[0])).toEqual(['1', '2', '10', 'Total']);
    });

    it('has a gap, not a segment, where a series has no value for a group (Gap in a line)', async () => {
      const gapped = seriesResult(
        [cell('1', 'Cali', 60), cell('2', 'Cali', 70), cell('3', 'Cali', 80), cell('1', 'Buga', 50), cell('3', 'Buga', 65)],
        ['Buga', 'Cali'],
      );
      const context = await showAnswer('line', rounds, gapped);

      const [buga, cali] = all(context.root, 'chart-line');
      expect(cali.getAttribute('d')!.match(/[ML]/g)).toEqual(['M', 'L', 'L']);
      expect(buga.getAttribute('d')).toBe(''); // points at 1 and 3, no segment through 2
      expect(all(context.root, 'chart-dot')).toHaveLength(5);
    });

    it('breaks a line into runs where it has a gap between longer stretches', async () => {
      const cells = ['1', '2', '4', '5'].map((round) => cell(round, 'Cali', Number(round) * 10));
      const context = await showAnswer('line', rounds, seriesResult([...cells, cell('3', 'Buga', 5)], ['Buga', 'Cali']));

      const cali = all(context.root, 'chart-line')[1].getAttribute('d')!;
      expect(cali.match(/M/g)).toHaveLength(2);
      expect(cali.match(/L/g)).toHaveLength(2);
    });

    it('shows one tooltip per group with every series that has a value there', async () => {
      const lines = seriesResult([cell('1', 'Cali', 60), cell('2', 'Cali', 70), cell('1', 'Buga', 50)], ['Buga', 'Cali']);
      const context = await showAnswer('line', rounds, lines);

      await hover(context, hits(context)[0]);
      expect(text(overlay().querySelector('[data-testid="tooltip-heading"]'))).toBe('1');
      expect(tooltipLines()).toEqual(['Buga: 50', 'Cali: 60']);
      expect(all(context.root, 'chart-guide')).toHaveLength(1);
      await hover(context, hits(context)[1]);
      expect(tooltipLines()).toEqual(['Cali: 70']);
    });
  });

  describe('tooltips and keyboard', () => {
    it('gives a tooltip line the color of its mark, the one its dashboard keeps for the value included', async () => {
      const kept = palette('earth', [{ column: 'water_source', value: 'Well', color: 6 }]);
      const context = await showAnswer('column', { ...source, layout: 'stacked' }, answer(), [households], kept);

      const [piped, well] = marks(context);
      await hover(context, hits(context)[1]);
      expect(tooltipLines()).toEqual(['Well: 15 households']);
      expect(colorOf(overlay().querySelector('[data-testid="tooltip-key"]'))).toBe(pal('earth', 6));
      expect(colorOf(well)).toBe(pal('earth', 6));
      expect(colorOf(piped)).toBe(pal('earth', 1));
    });

    it('gives each line of a line chart\'s tooltip the color of its line', async () => {
      const lines = seriesResult([cell('1', 'Cali', 60), cell('1', 'Buga', 50)], ['Buga', 'Cali']);
      const kept = palette('tamiza', [{ column: 'municipality', value: 'Cali', color: 5 }]);
      const context = await showAnswer('line', { indicator: 'attendance', groupBy: 'round', seriesBy: 'municipality' }, lines, undefined, kept);

      await hover(context, hits(context)[0]);
      expect(Array.from(overlay().querySelectorAll('[data-testid="tooltip-key"]')).map(colorOf)).toEqual([pal('tamiza', 1), pal('tamiza', 5)]);
      expect(all(context.root, 'chart-line').map(colorOf)).toEqual([pal('tamiza', 1), pal('tamiza', 5)]);
    });

    it('shows the group, the series value and the value with the indicator\'s unit on hover, and closes on leaving (Segment tooltip)', async () => {
      const context = await showAnswer('column', { ...source, layout: 'stacked' }, answer(), [households]);

      await hover(context, hits(context)[0]);
      expect(text(overlay().querySelector('[data-testid="tooltip-heading"]'))).toBe('Cali');
      expect(tooltipLines()).toEqual(['Piped: 35 households']);
      expect(overlay().querySelector<HTMLElement>('[data-testid="tooltip-key"]')!.getAttribute('style')).toContain('var(--tmz-pal-tamiza-1)');

      hits(context)[0].dispatchEvent(new MouseEvent('pointerleave'));
      await settle(context.harness);
      expect(tooltip()).toBeNull();
    });

    it('is one Tab stop that announces the mark the arrow keys are on, with the same words as its tooltip', async () => {
      const context = await showAnswer('column', { ...source, layout: 'stacked' }, answer(), [households]);
      const chart = byTestId(context.root, 'chart-svg')!;

      expect(chart.getAttribute('role')).toBe('application');
      expect(chart.getAttribute('tabindex')).toBe('0');
      expect(chart.getAttribute('aria-roledescription')).toBe('chart');
      expect(document.getElementById(chart.getAttribute('aria-describedby')!)!.textContent).toContain('Left and right arrows move between the groups');
      expect(all(context.root, 'chart-mark').every((mark) => !mark.hasAttribute('tabindex'))).toBe(true);

      chart.focus();
      pressKey(chart, 'ArrowRight');
      await settle(context.harness);
      expect(tooltipLines()).toEqual(['Piped: 35 households']);
      expect(spoken(context)).toBe('Cali. Piped: 35 households');

      pressKey(chart, 'ArrowDown'); // the next series of the same group
      await settle(context.harness);
      expect(spoken(context)).toBe('Cali. Well: 15 households');

      pressKey(chart, 'ArrowRight'); // the next group: Palmira has no Well, so its nearest series
      await settle(context.harness);
      expect(spoken(context)).toBe('Palmira. Piped: 20 households');

      pressKey(chart, 'Escape');
      await settle(context.harness);
      expect(tooltip()).toBeNull();
      expect(document.activeElement).toBe(chart);
    });

    it('describes itself to assistive technology with its kind, what it plots, and the highest cell', async () => {
      const context = await showAnswer('column', { ...source, layout: 'percent' }, answer(), [households]);

      expect(label(context)).toBe(
        'Column chart stacked to 100% of Households visited by municipality and water_source: 2 groups, 2 series, from 15 households to 35 households. Highest: Cali, Piped.',
      );
    });
  });
});

describe('one value in several widgets (add-dashboard-palettes)', () => {
  afterEach(clearOverlay);

  it('draws Female in color 5 in a pie by sex and in a stacked column chart with the series sex (Same value, same color)', async () => {
    const pie = widget('w1', { chartType: 'pie', config: config({ indicator: 'households_visited', groupBy: 'sex' }) });
    const column = widget('w2', {
      chartType: 'column',
      config: config({ indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'sex', layout: 'stacked' }),
      layout: { x: 6, y: 0, w: 6, h: 4 },
    });
    const sexes = [{ value: 'Male', kind: 'value' as const }, { value: 'Female', kind: 'value' as const }];
    const context = await open(
      'viewer',
      [pie, column],
      (body) =>
        body.chartType === 'pie'
          ? result([row('Male', 60), row('Female', 40)], { domain: sexes })
          : seriesResult([cell('Cali', 'Female', 30), cell('Cali', 'Male', 50)], ['Female', 'Male']),
      { indicators: [households] },
      { colors: palette('tamiza', [{ column: 'sex', value: 'Female', color: 5 }]) },
    );

    const slices = all(context.root, 'pie-slice');
    expect(await tips(context, 'pie')).toEqual(['Male: 60 households (60%)', 'Female: 40 households (40%)']);
    expect(slices.map(colorOf)).toEqual([pal('tamiza', 1), pal('tamiza', 5)]);
    const segments = marks(context);
    expect(segments.map(colorOf)).toEqual([pal('tamiza', 5), pal('tamiza', 1)]); // Female, Male
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

describe('column labels in chart widgets (add-indicator-column-metadata)', () => {
  afterEach(clearOverlay);

  const ratio = { numerator: 'numerator', denominator: 'denominator' };
  const groups = [row('Zona 1', 0.5), row('Zona 2', 0.25)];
  /** `teatro_hoy`, labelled "Teatro hoy", with the names a member typed for the ratio's two columns and its dimension. */
  const teatro = indicator('teatro_hoy', {
    label: 'Teatro hoy',
    format: 'percent',
    columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' }, zona: { label: 'Zona' } },
  });

  it('names the columns of a ratio in the caption of a chart (Caption of a ratio)', async () => {
    const context = await show('column', { indicator: 'teatro_hoy', groupBy: 'zona', ratio }, groups, [teatro]);

    expect(text(byTestId(context.root, 'chart-caption'))).toBe('Teatro hoy: Hogares que asistieron / Hogares by Zona');
    expect(label(context)).toContain('Teatro hoy: Hogares que asistieron / Hogares');
    expect(label(context)).toContain('by Zona');
    // What the page sends names the columns themselves: a label never reaches the query.
    expect(context.query.config).toMatchObject({ groupBy: 'zona', ratio });
    expect(context.query.config).not.toHaveProperty('display');
  });

  it('names the columns in the headers of "Show as table" (Table headers)', async () => {
    const context = await show('column', { indicator: 'teatro_hoy', groupBy: 'zona', ratio }, groups, [teatro]);

    await toggle(context);

    expect(tableHeaders(context)).toEqual(['Zona', 'Teatro hoy: Hogares que asistieron / Hogares']);
  });

  it('names the columns of the ratio in the description of a stat tile (Stat tile description)', async () => {
    const context = await show('stat', { indicator: 'teatro_hoy', groupBy: null, ratio }, [row(null, 0.5)], [teatro]);

    expect(text(byTestId(context.root, 'stat-label'))).toBe('Teatro hoy: Hogares que asistieron / Hogares');
  });

  it('names a chosen measure under its label (Measure named)', async () => {
    const counted = indicator('teatro_conteo', { label: 'Teatro conteo', columns: { value: { label: 'Hogares' } } });
    const context = await show('column', { indicator: 'teatro_conteo', groupBy: 'zona', measure: 'value' }, groups, [counted]);

    expect(text(byTestId(context.root, 'chart-caption'))).toBe('Teatro conteo: Hogares by zona');
  });

  it('prefers the widget\'s own label to the indicator\'s (Widget label over the indicator\'s)', async () => {
    const context = await show('column', { indicator: 'teatro_hoy', groupBy: 'zona', ratio, display: { labels: { denominator: 'Hogares de la base E1' } } }, groups, [teatro]);

    expect(text(byTestId(context.root, 'chart-caption'))).toBe('Teatro hoy: Hogares que asistieron / Hogares de la base E1 by Zona');
    expect(context.query.config).not.toHaveProperty('display');
  });

  it('names a column by its own name when neither the widget nor the indicator labels it (Column\'s own name)', async () => {
    const context = await show('column', { indicator: 'teatro_hoy', groupBy: 'estrato', ratio }, groups, [teatro]);

    expect(text(byTestId(context.root, 'chart-caption'))).toBe('Teatro hoy: Hogares que asistieron / Hogares by estrato');
  });

  it('keeps the indicator\'s label alone for the default measure (Default measure)', async () => {
    const counted = indicator('teatro_conteo', { label: 'Teatro conteo', columns: { value: { label: 'Personas' }, zona: { label: 'Zona' } } });
    const context = await show('column', { indicator: 'teatro_conteo', groupBy: 'zona' }, groups, [counted]);

    expect(text(byTestId(context.root, 'chart-caption'))).toBe('Teatro conteo by Zona');
    await toggle(context);
    expect(tableHeaders(context)).toEqual(['Zona', 'Teatro conteo']);
  });

  it('keeps the color kept for a value, whatever the column is called (Colors kept by value)', async () => {
    const colors = palette('tamiza', [{ column: 'zona', value: 'Zona 2', color: 2 }]);
    const context = await show('column', { indicator: 'teatro_hoy', groupBy: 'zona', ratio }, groups, [teatro], colors);

    expect(marks(context).map(colorOf)).toEqual([pal('tamiza', 1), pal('tamiza', 2)]);
  });

  it('names the columns in the caption of a bar chart', async () => {
    const bar = await show('bar', { indicator: 'teatro_hoy', groupBy: 'zona', ratio }, groups, [teatro]);

    expect(text(byTestId(bar.root, 'chart-caption'))).toBe('Teatro hoy: Hogares que asistieron / Hogares by Zona');
  });

  it('names the columns in the caption and the table of a pie', async () => {
    const pie = await show('pie', { indicator: 'teatro_hoy', groupBy: 'zona', ratio }, groups, [teatro]);

    expect(text(byTestId(pie.root, 'chart-caption'))).toBe('Teatro hoy: Hogares que asistieron / Hogares by Zona');
    await toggle(pie);
    expect(tableHeaders(pie)).toEqual(['Zona', 'Teatro hoy: Hogares que asistieron / Hogares', 'Share']);
  });

  it('names the columns in the caption of a histogram', async () => {
    const context = await show('histogram', { indicator: 'teatro_hoy', groupBy: null, ratio }, [row('Zona 1', 0.5), row('Zona 2', 0.25)], [teatro]);

    expect(text(byTestId(context.root, 'chart-caption'))).toContain('Teatro hoy: Hogares que asistieron / Hogares');
  });
});

describe('the unit of a widget (add-widget-context, design D4)', () => {
  afterEach(clearOverlay);

  const zones = [row('Zona 1', 0.1121), row('Zona 2', 0.25)];
  /** `teatro_hoy`, a rate of the households, whose widgets over a subgroup name another base. */
  const teatro = indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', unit: 'de los hogares' });

  it('writes the values with the unit of the widget in place of the indicator\'s (Base of a subgroup)', async () => {
    const context = await show('column', { indicator: 'teatro_hoy', groupBy: 'zona', display: { unit: 'de quienes asistieron' } }, zones, [teatro]);

    expect(await tips(context)).toEqual(['Zona 1: 11.21% de quienes asistieron', 'Zona 2: 25% de quienes asistieron']);
    await toggle(context);
    expect(tableRows(context)).toEqual([['Zona 1', '11.21% de quienes asistieron'], ['Zona 2', '25% de quienes asistieron']]);
    // The unit draws, it does not query.
    expect(context.query.config).not.toHaveProperty('display');
  });

  it('leaves the unit of the indicator on the other widgets over it (Other widgets keep the indicator\'s unit)', async () => {
    const context = await show('column', { indicator: 'teatro_hoy', groupBy: 'zona' }, zones, [teatro]);

    expect(await tips(context)).toEqual(['Zona 1: 11.21% de los hogares', 'Zona 2: 25% de los hogares']);
  });

  it('replaces the unit of a count and keeps its format (Widget\'s own unit)', async () => {
    const context = await show('column', { indicator: 'households_visited', display: { unit: 'households with children' } }, [row('Cali', 12450)], [households]);

    expect(await tips(context)).toEqual(['Cali: 12,450 households with children']);
  });

  it('writes the figure of a stat tile with the unit of the widget (Stat tile with its own unit)', async () => {
    const context = await show('stat', { indicator: 'teatro_hoy', groupBy: null, display: { unit: 'de quienes asistieron' } }, [row(null, 0.2422)], [teatro]);

    expect(text(byTestId(context.root, 'stat-value'))).toBe('24.22% de quienes asistieron');
  });

  it('does not repeat a unit of % after the percentage of the widget', async () => {
    const context = await show('stat', { indicator: 'teatro_hoy', groupBy: null, display: { unit: '%' } }, [row(null, 0.2422)], [teatro]);

    expect(text(byTestId(context.root, 'stat-value'))).toBe('24.22%');
  });

  it('writes a pie and its table with the unit of the widget too', async () => {
    const context = await show('pie', { indicator: 'households_visited', groupBy: 'zona', display: { unit: 'viviendas' } }, [row('Zona 1', 30), row('Zona 2', 10)], [households]);

    await toggle(context);
    expect(tableRows(context).map((r) => r[1])).toEqual(['30 viviendas', '10 viviendas']);
  });

  it('shows two decimals and its own unit for a widget whose indicator the project no longer lists', async () => {
    const own = await show('column', { indicator: 'encuesta_hoy', groupBy: 'zona', display: { unit: 'viviendas' } }, [row('Zona 1', 5)], [households]);
    expect(await tips(own)).toEqual(['Zona 1: 5.00 viviendas']);
  });

  it('shows two decimals and no unit for a widget whose indicator the project no longer lists and which has no unit of its own', async () => {
    const none = await show('column', { indicator: 'encuesta_hoy', groupBy: 'zona' }, [row('Zona 1', 5)], [households]);

    expect(await tips(none)).toEqual(['Zona 1: 5.00']);
  });
});

describe('the caption of a widget (add-widget-context, design D3)', () => {
  afterEach(clearOverlay);

  const ratio = { numerator: 'numerator', denominator: 'denominator' };
  const categories = [row('Teatro', 0.5), row('Cine', 0.25)];
  const teatro = indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', unit: 'de los hogares' });
  const bars = { indicator: 'teatro_hoy', groupBy: 'categoria', ratio } as const;
  const caption = (context: Context) => byTestId(context.root, 'chart-caption')!;
  const hidden = { caption: { hidden: true as const } };

  describe('own text', () => {
    it('replaces what is plotted above the bars and keeps the rest of the line (Own text above the bars)', async () => {
      const context = await show('bar', { ...bars, display: { caption: { text: '% de quienes asistieron' } } }, categories, [teatro]);

      expect(text(caption(context))).toBe('% de quienes asistieron by categoria');
      expect(label(context)).toContain('% de quienes asistieron');
      expect(context.query.config).not.toHaveProperty('display');
    });

    const rounds = [row('1', 60), row('2', 70), row('3', 80)];

    it('names the line of a line chart with no series by the own text (Own text above the bars)', async () => {
      const context = await show('line', { indicator: 'teatro_hoy', groupBy: 'ronda', display: { caption: { text: '% de quienes asistieron' } } }, rounds, [teatro]);

      expect(titles(all(context.root, 'chart-line'))).toEqual(['% de quienes asistieron']);
    });

    it('names the line of a line chart with no own text by the indicator\'s label, as it always did', async () => {
      const context = await show('line', { indicator: 'teatro_hoy', groupBy: 'ronda' }, rounds, [teatro]);

      expect(titles(all(context.root, 'chart-line'))).toEqual(['Teatro hoy']);
    });

    it('replaces what is plotted in the line of a histogram and keeps "Rows per range of"', async () => {
      const context = await show('histogram', { indicator: 'teatro_hoy', groupBy: null, buckets: 4, display: { caption: { text: 'Ingreso por hogar' } } }, [row(null, 1), row(null, 5)], [teatro]);

      expect(text(caption(context))).toBe('Rows per range of Ingreso por hogar, in 4 ranges');
    });

    it('heads the value column of the table with it (Own text in the table)', async () => {
      const context = await show('bar', { ...bars, display: { caption: { text: '% de quienes asistieron' } } }, categories, [teatro]);

      await toggle(context);

      expect(tableHeaders(context)).toEqual(['categoria', '% de quienes asistieron']);
    });

    it('replaces the line above a pie, and the header of its table', async () => {
      const context = await show('pie', { ...bars, display: { caption: { text: 'Proporción de asistentes' } } }, categories, [teatro]);

      expect(text(caption(context))).toBe('Proporción de asistentes by categoria');
      await toggle(context);
      expect(tableHeaders(context)).toEqual(['categoria', 'Proporción de asistentes', 'Share']);
    });

    it('replaces the label under the figure of a stat tile (Own text on a stat tile)', async () => {
      const context = await show('stat', { indicator: 'teatro_hoy', groupBy: null, ratio, display: { caption: { text: 'de quienes asistieron' } } }, [row(null, 0.2422)], [teatro]);

      expect(text(byTestId(context.root, 'stat-label'))).toBe('de quienes asistieron');
    });

    it('keeps the legend of a chart with a series and names the value in the tooltip of its marks (Series legend kept)', async () => {
      const answer = seriesResult([cell('Zona 1', 'Sí', 30, 3), cell('Zona 1', 'No', 10, 2), cell('Zona 2', 'Sí', 20, 1)], ['Sí', 'No']);
      const context = await showAnswer(
        'bar',
        { indicator: 'households_visited', groupBy: 'zona', seriesBy: 'respuesta', layout: 'percent', display: { caption: { text: 'Iría al teatro (I1)' } } },
        answer,
        [households],
      );

      expect(all(context.root, 'legend-name').map(text)).toEqual(['Sí', 'No']);
      all(context.root, 'chart-hit')[0].dispatchEvent(new MouseEvent('pointerenter', { clientX: 40, clientY: 30 }));
      await settle(context.harness);
      expect(Array.from(overlay().querySelectorAll('[data-testid="tooltip-line"]')).map(text).join(' ')).toContain('Iría al teatro (I1): 30 households');
    });

    it('is none when it holds only spaces', async () => {
      const context = await show('bar', { ...bars, display: { caption: { text: '   ' } } }, categories, [teatro]);

      expect(text(caption(context))).toBe('Teatro hoy: numerator / denominator by categoria');
    });
  });

  describe('hidden', () => {
    it('keeps the line for screen readers and takes it out of sight, and the chart still names what it plots (Hidden above the columns)', async () => {
      const context = await show('column', { ...bars, display: hidden }, categories, [teatro]);

      expect(caption(context).classList).toContain('sr-only');
      expect(text(caption(context))).toBe('Teatro hoy: numerator / denominator by categoria');
      expect(label(context)).toContain('Teatro hoy: numerator / denominator');
    });

    it('does the same above a bar chart, a pie and a histogram', async () => {
      const bar = await show('bar', { ...bars, display: hidden }, categories, [teatro]);
      expect(caption(bar).classList).toContain('sr-only');
      expect(text(caption(bar))).toContain('Teatro hoy: numerator / denominator');
    });

    it('does the same above a pie', async () => {
      const pie = await show('pie', { ...bars, display: hidden }, categories, [teatro]);
      expect(caption(pie).classList).toContain('sr-only');
      expect(text(caption(pie))).toContain('Teatro hoy: numerator / denominator');
    });

    it('does the same above a histogram', async () => {
      const histogram = await show('histogram', { ...bars, groupBy: null, display: hidden }, categories, [teatro]);
      expect(caption(histogram).classList).toContain('sr-only');
      expect(text(caption(histogram))).toContain('Teatro hoy: numerator / denominator');
    });

    it('leaves the label of a stat tile for screen readers (Hidden on a stat tile)', async () => {
      const context = await show('stat', { indicator: 'teatro_hoy', groupBy: null, ratio, display: hidden }, [row(null, 0.2422)], [teatro]);

      const stat = byTestId(context.root, 'stat-label')!;
      expect(stat.classList).toContain('sr-only');
      expect(text(stat)).toBe('Teatro hoy: numerator / denominator');
      expect(text(byTestId(context.root, 'stat-value'))).toBe('24.22% de los hogares');
    });

    it('still heads the value column of the table with what is plotted (Table header kept)', async () => {
      const context = await show('column', { ...bars, display: hidden }, categories, [teatro]);

      await toggle(context);

      expect(tableHeaders(context)).toEqual(['categoria', 'Teatro hoy: numerator / denominator']);
    });

    it('does not hide the legend of a chart with a series (Legend kept)', async () => {
      const answer = seriesResult([cell('Zona 1', 'Acceso', 30, 3), cell('Zona 1', 'Interés', 10, 2), cell('Zona 1', 'Otra', 5, 1)], ['Acceso', 'Interés', 'Otra']);
      const context = await showAnswer('bar', { indicator: 'households_visited', groupBy: 'zona', seriesBy: 'barrera_tipo', layout: 'stacked', display: hidden }, answer, [households]);

      expect(all(context.root, 'legend-name').map(text)).toEqual(['Acceso', 'Interés', 'Otra']);
      expect(all(context.root, 'legend-name').every((name) => !name.closest('.sr-only'))).toBe(true);
    });

    it('names the value in the tooltips of its marks', async () => {
      const context = await show('column', { indicator: 'households_visited', groupBy: 'municipality', display: hidden }, [row('Cali', 12450)], [households]);

      expect(await tips(context)).toEqual(['Cali: 12,450 households']);
    });
  });

  it('shows the line of a widget with no caption setting, with no sr-only on it', async () => {
    const context = await show('column', { ...bars, display: { caption: {}, subtitle: 'Pregunta' } }, categories, [teatro]);

    expect(caption(context).classList).not.toContain('sr-only');
    expect(text(caption(context))).toBe('Teatro hoy: numerator / denominator by categoria');
  });

  it('shows the line of a widget with no display at all, with no sr-only on it', async () => {
    const context = await show('column', bars, categories, [teatro]);

    expect(caption(context).classList).not.toContain('sr-only');
  });
});

describe('chart annotations (add-chart-annotations)', () => {
  afterEach(clearOverlay);

  const teatro = indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', unit: 'de los hogares' });
  const conteo = indicator('teatro_conteo', { label: 'Teatro conteo', format: 'count', unit: 'hogares' });
  const zones = [row('Zona 1', 0.4), row('Zona 2', 0.5), row('Zona 3', 0.6), row('Zona 4', 0.2)];
  const ratio = { numerator: 'numerator', denominator: 'denominator' };
  const plainColumn = { indicator: 'teatro_hoy', groupBy: 'zona' } as const;

  const svgOf = (context: Context) => byTestId(context.root, 'chart-svg')!;
  const referenceLines = (context: Context) => all(context.root, 'reference-line');
  const referenceTexts = (context: Context) => all(context.root, 'reference-label').map(text);
  const keyTexts = (context: Context) => all(context.root, 'reference-key-text').map(text);
  const dataLabels = (context: Context) => all(context.root, 'data-label');
  const dataTexts = (context: Context) => dataLabels(context).map(text);
  const num = (element: Element, name: string) => Number(element.getAttribute(name));
  /** The text of the axis tick that reads `tick`. */
  const tickOf = (context: Context, tick: string) => Array.from(svgOf(context).querySelectorAll('text')).find((t) => text(t) === tick)!;
  /** The grid lines of the plot: the hairlines of the axis, not the reference lines. */
  const gridOf = (context: Context) => Array.from(svgOf(context).querySelectorAll('line')).filter((l) => !l.hasAttribute('data-testid'));

  describe('reference lines drawn (design D5)', () => {
    it('crosses horizontal bars with a vertical line at the mean of the bars, labelled with the mean (Mean line on horizontal bars)', async () => {
      const context = await show('bar', { ...plainColumn, display: { referenceLines: [{ kind: 'mean' }] } }, [row('Zona 1', 0.2), row('Zona 2', 0.4), row('Zona 3', 0.6)], [teatro]);

      const [line] = referenceLines(context);
      expect(referenceLines(context)).toHaveLength(1);
      expect(line.getAttribute('x1')).toBe(line.getAttribute('x2'));
      // The mean is the second bar's value: the line sits at the end of the second bar.
      const second = marks(context)[1];
      expect(num(line, 'x1')).toBeCloseTo(num(second, 'x') + num(second, 'width'), 6);
      expect(line.getAttribute('stroke-dasharray')).toBe('6 4');
      expect(line.classList).toContain('stroke-heading');
      expect(referenceTexts(context)).toEqual(['Mean: 40% de los hogares']);
      expect(text(line.querySelector('title'))).toBe('Mean: 40% de los hogares');
      // Not part of the query: the chart's display never travels.
      expect(context.query.config).not.toHaveProperty('display');
    });

    it('draws a horizontal line across a column chart, its text at the right edge above the line', async () => {
      const context = await show('column', { ...plainColumn, display: { referenceLines: [{ kind: 'median' }] } }, zones, [teatro]);

      const [line] = referenceLines(context);
      expect(line.getAttribute('y1')).toBe(line.getAttribute('y2'));
      const [label] = all(context.root, 'reference-label');
      expect(label.getAttribute('text-anchor')).toBe('end');
      expect(num(label, 'y')).toBe(num(line, 'y1') - 4);
      expect(num(label, 'x')).toBe(num(line, 'x2'));
      expect(text(label)).toBe('Median: 45% de los hogares');
    });

    it('draws the lines of a line chart over its points', async () => {
      const context = await show('line', { indicator: 'teatro_hoy', groupBy: 'ronda', display: { referenceLines: [{ kind: 'mean', label: 'Promedio de rondas' }] } }, [row('1', 0.2), row('2', 0.6)], [teatro]);

      expect(referenceLines(context)).toHaveLength(1);
      expect(referenceTexts(context)).toEqual(['Promedio de rondas: 40% de los hogares']);
    });

    it('stretches the axis to reach a line above every column (Axis widened)', async () => {
      const context = await show('column', { ...plainColumn, display: { referenceLines: [{ kind: 'value', value: 0.5 }] } }, [row('Zona 1', 0.1), row('Zona 2', 0.2), row('Zona 3', 0.25)], [teatro]);

      expect(Array.from(svgOf(context).querySelectorAll('text')).map(text)).toContain('50%');
      const [line] = referenceLines(context);
      const columnTops = marks(context).map((mark) => num(mark, 'y'));
      expect(num(line, 'y1')).toBeLessThan(Math.min(...columnTops));
      expect(num(line, 'y1')).toBeGreaterThanOrEqual(0);
      // The line is at the tick that reads 50%.
      expect(num(line, 'y1')).toBeCloseTo(num(tickOf(context, '50%'), 'y') - 4, 6);
    });

    it('leaves a chart without lines on the axis it had, and the key and the labels out of it (Widget saved before reference lines)', async () => {
      const context = await show('column', plainColumn, zones, [teatro]);

      expect(referenceLines(context)).toHaveLength(0);
      expect(all(context.root, 'reference-key')).toHaveLength(0);
      expect(dataLabels(context)).toHaveLength(0);
      // The top of the plot is 8 px from the top of the box, as it was, and the columns stand where they stood.
      expect(Math.min(...gridOf(context).map((l) => num(l, 'y1')))).toBeCloseTo(8, 6);
      expect(Array.from(svgOf(context).querySelectorAll('text')).map(text)).toEqual(expect.arrayContaining(['0%', '20%', '60%']));
      expect(svgOf(context).getAttribute('height')).toBe('280');
    });

    it('draws a chart with an empty list of lines and a saved default as it draws one without them', async () => {
      const context = await show('column', { ...plainColumn, display: { color: 2, referenceLines: [], dataLabels: false } }, zones, [teatro]);

      expect(referenceLines(context)).toHaveLength(0);
      expect(all(context.root, 'reference-key')).toHaveLength(0);
      expect(Math.min(...gridOf(context).map((l) => num(l, 'y1')))).toBeCloseTo(8, 6);
    });

    it('grows the top margin of a chart with lines by one text line', async () => {
      const context = await show('column', { ...plainColumn, display: { referenceLines: [{ kind: 'mean' }] } }, zones, [teatro]);

      expect(Math.min(...gridOf(context).map((l) => num(l, 'y1')))).toBeCloseTo(20, 6);
    });

    it('writes only the first of two lines so close that their texts would overlap, and the key lists both (Lines close together)', async () => {
      const context = await show('column', { ...plainColumn, display: { referenceLines: [{ kind: 'mean' }, { kind: 'median' }] } }, zones, [teatro]);

      expect(referenceLines(context)).toHaveLength(2);
      expect(referenceLines(context).map((l) => l.getAttribute('stroke-dasharray'))).toEqual(['6 4', '2 3']);
      expect(referenceTexts(context)).toEqual(['Mean: 42.5% de los hogares']);
      expect(keyTexts(context)).toEqual(['Mean: 42.5% de los hogares', 'Median: 45% de los hogares']);
    });

    it('draws no text at a line whose text is wider than the plot, and the key lists the whole text (Text too wide for the plot)', async () => {
      const unit = 'de los hogares encuestados en la zona urbana del municipio';
      const context = await show('column', { ...plainColumn, display: { unit, referenceLines: [{ kind: 'value', value: 0.5, label: 'Meta del plan de desarrollo municipal' }] } }, zones, [teatro]);

      expect(referenceLines(context)).toHaveLength(1);
      expect(referenceTexts(context)).toEqual([]);
      expect(keyTexts(context)).toEqual([`Meta del plan de desarrollo municipal: 50% ${unit}`]);
      expect(all(context.root, 'reference-label').every((label) => text(label).length < 200)).toBe(true);
    });

    it('draws the overall line of grouped columns at the total of all their rows (Overall line on grouped columns)', async () => {
      const answer = seriesResult([cell('Cali', 'Piped', 30, 3), cell('Cali', 'Well', 10, 2), cell('Palmira', 'Piped', 20, 1)], ['Piped', 'Well']);
      const context = await showAnswer('column', { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', layout: 'grouped', ratio, display: { referenceLines: [{ kind: 'overall' }] } }, answer, [households]);

      expect(answer.totals!.all.value).toBe(60);
      const [line] = referenceLines(context);
      expect(num(line, 'y1')).toBeCloseTo(num(tickOf(context, '60'), 'y') - 4, 6);
      // A series' marks are keyboard stops with their own tooltips: the line has no title of its own.
      expect(line.querySelector('title')).toBeNull();
      expect(referenceTexts(context)).toEqual(['Overall: 60 households']);
    });

    it('draws the overall line of a widget with no series at the overall value of the answer', async () => {
      const context = await showAnswer('column', { ...plainColumn, ratio, display: { referenceLines: [{ kind: 'mean' }, { kind: 'overall' }] } }, result(zones, { overall: 0.4599 }), [teatro]);

      expect(context.query).toMatchObject({ chartType: 'column' });
      expect(keyTexts(context)).toEqual(['Mean: 42.5% de los hogares', 'Overall: 45.99% de los hogares']);
    });

    it('leaves out an overall line the engine could not give', async () => {
      const context = await showAnswer('column', { ...plainColumn, ratio, display: { referenceLines: [{ kind: 'overall' }] } }, result(zones), [teatro]);

      expect(referenceLines(context)).toHaveLength(0);
      expect(all(context.root, 'reference-key')).toHaveLength(0);
    });

    it('crosses every bar of a 100% layout at half its length (Half of every bar)', async () => {
      const answer = seriesResult([cell('Cali', 'Piped', 30, 3), cell('Cali', 'Well', 10, 2), cell('Palmira', 'Piped', 20, 1)], ['Piped', 'Well']);
      const context = await showAnswer('bar', { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', layout: 'percent', display: { referenceLines: [{ kind: 'value', value: 0.5 }] } }, answer, [households]);

      const [line] = referenceLines(context);
      expect(num(line, 'x1')).toBeCloseTo((num(tickOf(context, '0%'), 'x') + num(tickOf(context, '100%'), 'x')) / 2, 6);
      expect(keyTexts(context)).toEqual(['Reference: 50%']);
    });

    it('draws the lines of a stacked chart at their own numbers, on an axis that reaches them', async () => {
      const answer = seriesResult([cell('Cali', 'Piped', 30, 3), cell('Cali', 'Well', 10, 2)], ['Piped', 'Well']);
      const context = await showAnswer('column', { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', layout: 'stacked', display: { referenceLines: [{ kind: 'value', value: 100 }] } }, answer, [households]);

      expect(Array.from(svgOf(context).querySelectorAll('text')).map(text)).toContain('100');
      expect(referenceLines(context)).toHaveLength(1);
    });

    it('gives each line the dash of its place in the widget\'s list, whichever lines have a value', async () => {
      const context = await showAnswer(
        'column',
        { ...plainColumn, ratio, display: { referenceLines: [{ kind: 'overall' }, { kind: 'value', value: 0.9 }, { kind: 'mean' }] } },
        result(zones),
        [teatro],
      );

      // The overall line has no value, so only two are drawn; the second and the third keep their own patterns.
      expect(referenceLines(context).map((l) => l.getAttribute('stroke-dasharray'))).toEqual(['2 3', '8 3 2 3']);
    });
  });

  describe('what the lines say (design D3)', () => {
    it('writes its own label and the value as the widget writes values (Own label)', async () => {
      const context = await show('column', { indicator: 'piped_water_rate', groupBy: 'zona', display: { referenceLines: [{ kind: 'value', value: 0.5, label: 'Meta' }] } }, [row('Zona 1', 0.3)], [rate]);

      expect(referenceTexts(context)).toEqual(['Meta: 50%']);
      expect(keyTexts(context)).toEqual(['Meta: 50%']);
    });

    it('writes a count with its unit (Count with a unit)', async () => {
      const context = await show('column', { indicator: 'teatro_conteo', groupBy: 'zona', display: { referenceLines: [{ kind: 'value', value: 100 }] } }, [row('Zona 1', 80)], [conteo]);

      expect(referenceTexts(context)).toEqual(['Reference: 100 hogares']);
      expect(keyTexts(context)).toEqual(['Reference: 100 hogares']);
    });

    it('writes a target on a percent indicator with its unit (Target on a percent indicator)', async () => {
      const context = await show('bar', { indicator: 'teatro_hoy', groupBy: 'categoria', display: { referenceLines: [{ kind: 'value', value: 0.25, label: 'Meta' }] } }, [row('Cine', 0.1), row('Teatro', 0.3)], [teatro]);

      expect(referenceTexts(context)).toEqual(['Meta: 25% de los hogares']);
      expect(keyTexts(context)).toEqual(['Meta: 25% de los hogares']);
    });

    it('writes the default label in the interface language, with the browser entry that chose it (Default label in Spanish)', async () => {
      const context = await open('viewer', [widget('w1', { chartType: 'column', config: config({ ...plainColumn, display: { referenceLines: [{ kind: 'mean' }] } }) })], null, { indicators: [teatro] }, { lang: 'es' });
      context.http.match(isQuery)[0].flush(result([row('Zona 1', 0.3417)]));
      await settle(context.harness);

      // The percent sign follows the region's own pattern (a space before it or none, by the ICU data of the runtime).
      expect(keyTexts(context)).toEqual([expect.stringMatching(/^Promedio: 34,17\s?% de los hogares$/)]);
      expect(referenceTexts(context)).toEqual([expect.stringMatching(/^Promedio: 34,17\s?% de los hogares$/)]);
    });

    it('has the four default labels in each language', () => {
      expect(en.indicators.referenceLines.kinds).toEqual({ mean: 'Mean', median: 'Median', overall: 'Overall', value: 'Reference' });
      expect(es.indicators.referenceLines.kinds).toEqual({ mean: 'Promedio', median: 'Mediana', overall: 'Valor general', value: 'Referencia' });
    });

    it('names each line and its value after the chart\'s summary for a screen reader (Screen reader)', async () => {
      const context = await showAnswer('column', { ...plainColumn, ratio, display: { referenceLines: [{ kind: 'overall' }] } }, result(zones, { overall: 0.4599 }), [teatro]);

      expect(label(context)).toMatch(/Highest: Zona 3\. Reference lines: Overall 45\.99% de los hogares\.$/);
    });

    it('says nothing more to a screen reader about a chart without lines', async () => {
      const context = await show('column', plainColumn, zones, [teatro]);

      expect(label(context)).not.toContain('Reference lines');
    });
  });

  describe('the key (design D5)', () => {
    it('lists every line under the plot, each with a dashed swatch, and nothing for a chart without lines', async () => {
      const context = await show('column', { ...plainColumn, display: { referenceLines: [{ kind: 'mean' }, { kind: 'value', value: 0.5, label: 'Meta' }] } }, zones, [teatro]);

      const key = byTestId(context.root, 'reference-key')!;
      expect(key.getAttribute('aria-label')).toBe('Reference lines');
      expect(all(context.root, 'reference-key-item')).toHaveLength(2);
      expect(all(context.root, 'reference-key-swatch').map((swatch) => swatch.querySelector('line')!.getAttribute('stroke-dasharray'))).toEqual(['6 4', '2 3']);
      // A sibling of the plot box in the chart's column, after it, that does not shrink: it takes its room before the plot does.
      const host = key.closest('app-reference-key')!;
      expect(host.classList).toContain('shrink-0');
      expect(byTestId(context.root, 'chart-box')!.nextElementSibling).toBe(host);
    });

    it('cuts the plot at its own box while a key follows (Key above the footnote)', async () => {
      const withLine = await show('column', { ...plainColumn, display: { referenceLines: [{ kind: 'mean' }] } }, zones, [teatro]);
      const box = byTestId(withLine.root, 'chart-box')!;
      expect(box.style.overflow).toBe('clip');
      expect(box.style.getPropertyValue('overflow-clip-margin')).not.toBe('');
    });

    it('leaves the plot box as it was in a chart without lines', async () => {
      const without = await show('column', plainColumn, zones, [teatro]);
      const plainBox = byTestId(without.root, 'chart-box')!;
      expect(plainBox.className).toBe('relative min-h-0 flex-1');
      expect(plainBox.getAttribute('style')).toBeNull();
    });

    it('keeps the key above the widget\'s footnote (Key above the footnote)', async () => {
      const context = await show('column', { ...plainColumn, display: { footnote: 'No suman 100 %: respuesta múltiple.', referenceLines: [{ kind: 'mean' }] } }, zones, [teatro]);

      const key = byTestId(context.root, 'reference-key')!;
      const footnote = byTestId(context.root, 'widget-footnote')!;
      expect(key.compareDocumentPosition(footnote) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });
  });

  describe('"Show as table" (design D9)', () => {
    it('keeps the table as it is and lists the lines under it (Column chart as a table)', async () => {
      const before = [['Zona 1', '40% de los hogares'], ['Zona 2', '50% de los hogares'], ['Zona 3', '60% de los hogares'], ['Zona 4', '20% de los hogares']];

      const context = await showAnswer('column', { ...plainColumn, ratio, display: { referenceLines: [{ kind: 'mean' }, { kind: 'overall' }] } }, result(zones, { overall: 0.4599 }), [teatro]);
      await toggle(context);

      expect(tableRows(context)).toEqual(before);
      expect(all(context.root, 'reference-list-item').map(text)).toEqual(['Mean: 42.5% de los hogares', 'Overall: 45.99% de los hogares']);
      expect(all(context.root, 'reference-key')).toHaveLength(0);
      const table = byTestId(context.root, 'widget-table')!;
      expect(table.compareDocumentPosition(byTestId(context.root, 'reference-list')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('lists them under the cross table of a chart with a series', async () => {
      const answer = seriesResult([cell('Cali', 'Piped', 30, 3), cell('Cali', 'Well', 10, 2), cell('Palmira', 'Piped', 20, 1)], ['Piped', 'Well']);
      const context = await showAnswer('column', { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', layout: 'grouped', ratio, display: { referenceLines: [{ kind: 'overall' }] } }, answer, [households]);
      await toggle(context);

      expect(all(context.root, 'reference-list-item').map(text)).toEqual(['Overall: 60 households']);
    });

    it('lists nothing for a chart without lines', async () => {
      const context = await show('column', plainColumn, zones, [teatro]);
      await toggle(context);

      expect(all(context.root, 'reference-list')).toHaveLength(0);
    });
  });

  describe('step lines (design D6)', () => {
    const rounds = [row('1', 0.9803), row('2', 0.6), row('3', 0.4), row('4', 0.2)];
    const demand = { indicator: 'teatro_hoy', groupBy: 'precio' } as const;
    const pathOf = (context: Context) => all(context.root, 'chart-line')[0].getAttribute('d')!;

    it('holds each value until the next point and then moves straight to the next value (Demand curve with steps)', async () => {
      const context = await show('line', { ...demand, display: { interpolation: 'step' } }, rounds, [teatro]);

      const path = pathOf(context);
      expect(path.match(/[MLHV]/g)).toEqual(['M', 'H', 'V', 'H', 'V', 'H', 'V']);
      expect(path).not.toContain('L');
      // It runs level from the first point to the second before it drops.
      const [first, level, drop] = [/^M([\d.]+) ([\d.]+)/.exec(path)!, /H([\d.]+)/.exec(path)!, /V([\d.]+)/.exec(path)!];
      expect(Number(level[1])).toBeGreaterThan(Number(first[1]));
      expect(Number(drop[1])).toBeGreaterThan(Number(first[2]));
      // The dots stay on the points, and steps query nothing more.
      expect(all(context.root, 'chart-dot')).toHaveLength(4);
      expect(context.query.config).not.toHaveProperty('display');
    });

    it.each([
      ['no display', undefined],
      ['a color', { color: 2 }],
      ['a saved linear', { interpolation: 'linear' as const }],
    ])('joins the points by straight segments by default, with %s (Straight segments by default)', async (_name, display) => {
      const context = await show('line', { ...demand, ...(display ? { display } : {}) }, rounds, [teatro]);

      expect(pathOf(context).match(/[MLHV]/g)).toEqual(['M', 'L', 'L', 'L']);
    });

    it('draws each series with steps, in its color, and keeps the gaps (Steps per series)', async () => {
      const answer = seriesResult(
        [cell('1', 'Cali', 0.9), cell('2', 'Cali', 0.6), cell('3', 'Cali', 0.5), cell('1', 'Buga', 0.8), cell('3', 'Buga', 0.4), cell('4', 'Buga', 0.3)],
        ['Cali', 'Buga'],
      );
      const context = await showAnswer('line', { ...demand, seriesBy: 'comuna', display: { interpolation: 'step' } }, answer, [teatro]);

      const [cali, buga] = all(context.root, 'chart-line');
      expect(cali.getAttribute('d')!.match(/[MLHV]/g)).toEqual(['M', 'H', 'V', 'H', 'V']);
      expect(cali.getAttribute('style')).toContain('var(--tmz-pal-tamiza-1)');
      // Buga has no value for round 2: two runs of one point each have no segment, so only the run of rounds 3 and 4 is drawn.
      expect(buga.getAttribute('d')!.match(/[MLHV]/g)).toEqual(['M', 'H', 'V']);
      expect(buga.getAttribute('style')).toContain('var(--tmz-pal-tamiza-2)');
    });

  });

  describe('data labels (design D7)', () => {
    const places = [row('Centro cultural o casa de la cultura', 0.2422), row('Teatro', 0.1), row('Cine', 0.05)];
    const bars = { indicator: 'teatro_hoy', groupBy: 'lugar', display: { dataLabels: true } } as const;

    it('writes the value at the end of each bar, as its tooltip does (Value at the end of a bar)', async () => {
      const context = await show('bar', bars, places, [teatro]);

      expect(dataTexts(context)).toEqual(['24.22% de los hogares', '10% de los hogares', '5% de los hogares']);
      const [label] = dataLabels(context);
      const mark = marks(context)[0];
      expect(num(label, 'x')).toBeCloseTo(num(mark, 'x') + num(mark, 'width') + 4, 6);
      expect(label.getAttribute('text-anchor')).toBe('start');
      expect((await tips(context))[0]).toBe('Centro cultural o casa de la cultura: 24.22% de los hogares');
    });

    it('hides the labels from assistive technology, since the table and the tooltips carry the values', async () => {
      const context = await show('bar', bars, places, [teatro]);

      expect(dataLabels(context).every((label) => label.getAttribute('aria-hidden') === 'true')).toBe(true);
    });

    it('keeps the room past the longest bar for its label', async () => {
      const context = await show('bar', bars, places, [teatro]);

      expect(Math.max(...gridOf(context).map((l) => num(l, 'x1')))).toBe(480 - 14 - 4 - textWidthOf('24.22% de los hogares'));
    });

    it('keeps the right edge of a bar chart without labels where it was', async () => {
      const context = await show('bar', { ...bars, display: {} }, places, [teatro]);

      expect(Math.max(...gridOf(context).map((l) => num(l, 'x1')))).toBe(466);
    });

    it('writes the value above each column, and below a negative one', async () => {
      const context = await show('column', { ...plainColumn, display: { dataLabels: true } }, [row('Zona 1', 0.4), row('Zona 2', -0.1), row('Zona 3', -0.25)], [teatro]);

      const [up, down] = dataLabels(context);
      const [first, second] = marks(context);
      expect(num(up, 'y')).toBe(num(first, 'y') - 4);
      expect(num(down, 'y')).toBe(num(second, 'y') + num(second, 'height') + 14);
      expect(dataTexts(context)).toEqual(['40% de los hogares', '-10% de los hogares', '-25% de los hogares']);
      expect(Math.min(...gridOf(context).map((l) => num(l, 'y1')))).toBeCloseTo(22, 6);
    });

    it('leaves out the label of a negative column that reaches the axis minimum, since it would cover the category names, and keeps the column (Negative values)', async () => {
      // The axis runs from -0.2 to 0.4, so the second column ends on its foot and its label would fall on the names of the groups.
      const context = await show('column', { ...plainColumn, display: { dataLabels: true } }, [row('Zona 1', 0.4), row('Zona 2', -0.2)], [teatro]);

      expect(marks(context)).toHaveLength(2);
      expect(dataTexts(context)).toEqual(['40% de los hogares']);
    });

    it('writes the label to the left of a negative bar, and leaves it out when the bar reaches the axis minimum (Negative values)', async () => {
      const counts = { indicator: 'households_visited', groupBy: 'lugar', display: { dataLabels: true } } as const;
      // The axis runs from -60 to 50: the bar at -20 has room on its left for its label, and the one at -45 has not.
      const context = await show('bar', counts, [row('A', 50), row('B', -20), row('C', -45)], [households]);

      expect(marks(context)).toHaveLength(3);
      expect(dataTexts(context)).toEqual(['50 households', '-20 households']);
      expect(dataLabels(context)[1].getAttribute('text-anchor')).toBe('end');
    });

    it('leaves out the label of a negative bar that ends on the axis minimum (Negative values)', async () => {
      const edge = await show('bar', { indicator: 'households_visited', groupBy: 'lugar', display: { dataLabels: true } }, [row('A', 50), row('B', -40)], [households]);

      expect(marks(edge)).toHaveLength(2);
      expect(dataTexts(edge)).toEqual(['50 households']);
    });

    it('leaves out the labels of negative grouped marks that reach the axis minimum (Negative values)', async () => {
      const answer = seriesResult([cell('Cali', 'A', 35), cell('Cali', 'B', -20)], ['A', 'B']);
      const column = await showAnswer('column', { indicator: 'attendance', groupBy: 'municipality', seriesBy: 'water_source', layout: 'grouped', display: { dataLabels: true } }, answer, [indicator('attendance')]);

      expect(marks(column)).toHaveLength(2);
      expect(dataTexts(column)).toEqual(['35']);
    });

    it('leaves out the label of a negative grouped bar that reaches the axis minimum (Negative values)', async () => {
      const answer = seriesResult([cell('Cali', 'A', 35), cell('Cali', 'B', -20)], ['A', 'B']);
      const bar = await showAnswer('bar', { indicator: 'attendance', groupBy: 'municipality', seriesBy: 'water_source', layout: 'grouped', display: { dataLabels: true } }, answer, [indicator('attendance')]);

      expect(marks(bar)).toHaveLength(2);
      expect(dataTexts(bar)).toEqual(['35']);
    });

    it.each([
      ['no display', undefined],
      ['a saved false', { dataLabels: false }],
      ['a color', { color: 2 }],
    ])('draws none and keeps the margins of a chart without them, with %s (Off by default)', async (_name, display) => {
      const context = await show('column', { ...plainColumn, ...(display ? { display } : {}) }, zones, [teatro]);

      expect(dataLabels(context)).toHaveLength(0);
      expect(Math.min(...gridOf(context).map((l) => num(l, 'y1')))).toBeCloseTo(8, 6);
    });

    it('leaves out the labels of columns narrower than their text and keeps the columns (Columns too narrow)', async () => {
      const many = Array.from({ length: 24 }, (_, i) => row(`Zona ${i + 1}`, 0.1 + i / 100));
      const context = await show('column', { ...plainColumn, display: { dataLabels: true } }, many, [teatro]);

      expect(marks(context)).toHaveLength(24);
      expect(dataLabels(context)).toHaveLength(0);
      expect((await tips(context))[0]).toBe('Zona 1: 10% de los hogares');
    });

    it('leaves out the labels of grouped columns narrower than their text and keeps their marks (Columns too narrow)', async () => {
      const cells = Array.from({ length: 12 }, (_, i) => [cell(`G${i}`, 'Piped', 35, 3), cell(`G${i}`, 'Well', 15, 2)]).flat();
      const context = await showAnswer('column', { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', layout: 'grouped', display: { dataLabels: true } }, seriesResult(cells, ['Piped', 'Well']), [households]);

      expect(dataLabels(context)).toHaveLength(0);
      expect(marks(context)).toHaveLength(24);
      expect(hitsOf(context)).toHaveLength(24);
      // The top margin grows while data labels are on.
      expect(Math.min(...gridOf(context).map((l) => num(l, 'y1')))).toBeCloseTo(22, 6);
    });

    it('writes the labels of grouped columns that fit their slot', async () => {
      const context = await showAnswer(
        'column',
        { indicator: 'attendance', groupBy: 'municipality', seriesBy: 'water_source', layout: 'grouped', display: { dataLabels: true } },
        seriesResult([cell('Cali', 'A', 35), cell('Cali', 'B', 15)], ['A', 'B']),
        [indicator('attendance')],
      );

      expect(dataTexts(context)).toEqual(['35', '15']);
    });

    it('writes the value at the end of each grouped bar and keeps the room for the longest', async () => {
      const answer = seriesResult([cell('Cali', 'Piped', 30, 3), cell('Cali', 'Well', 10, 2), cell('Palmira', 'Piped', 20, 1)], ['Piped', 'Well']);
      const context = await showAnswer('bar', { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', layout: 'grouped', display: { dataLabels: true } }, answer, [households]);

      expect(dataTexts(context)).toEqual(['30 households', '10 households', '20 households']);
      expect(Math.max(...gridOf(context).map((l) => num(l, 'x1')))).toBeLessThan(466 - textWidthOf('30 households'));
    });

    it.each(['stacked', 'percent'] as const)('writes none on a %s layout, whatever is saved', async (layout) => {
      const answer = seriesResult([cell('Cali', 'Piped', 30, 3), cell('Cali', 'Well', 10, 2)], ['Piped', 'Well']);
      const context = await showAnswer('bar', { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', layout, display: { dataLabels: true } }, answer, [households]);

      expect(dataLabels(context)).toHaveLength(0);
    });

    it.each([
      ['straight segments', { dataLabels: true }],
      ['steps', { dataLabels: true, interpolation: 'step' as const }],
    ])('writes the value above each point of a line with %s', async (_name, display) => {
      const context = await show('line', { indicator: 'teatro_hoy', groupBy: 'precio', display }, [row('1', 0.9), row('2', 0.6), row('3', 0.3)], [teatro]);

      expect(dataTexts(context)).toEqual(['90% de los hogares', '60% de los hogares', '30% de los hogares']);
      const dots = all(context.root, 'chart-dot');
      dataLabels(context).forEach((labelEl, i) => expect(num(labelEl, 'y')).toBe(num(dots[i], 'cy') - 8));
      expect(Math.min(...gridOf(context).map((l) => num(l, 'y1')))).toBeCloseTo(22, 6);
    });

    it('writes one label where two series have the same point, and each of the others', async () => {
      const answer = seriesResult(
        [cell('1', 'Cali', 60), cell('1', 'Buga', 60), cell('2', 'Cali', 70), cell('2', 'Buga', 90)],
        ['Cali', 'Buga'],
      );
      const context = await showAnswer('line', { indicator: 'households_visited', groupBy: 'ronda', seriesBy: 'municipality', display: { dataLabels: true } }, answer, [households]);

      expect(dataTexts(context)).toEqual(['60 households', '70 households', '90 households']);
    });

    it('writes a label that would cover a reference label nowhere', async () => {
      const context = await show('column', { ...plainColumn, display: { dataLabels: true, referenceLines: [{ kind: 'value', value: 0.6, label: 'Meta' }] } }, [row('Zona 1', 0.2), row('Zona 2', 0.6)], [teatro]);

      // The second column ends on the line, where the line's text sits; its label is the one left out.
      expect(referenceTexts(context)).toEqual(['Meta: 60% de los hogares']);
      expect(dataTexts(context)).toEqual(['20% de los hogares']);
    });
  });

  describe('data labels on a pie (design D7)', () => {
    const settings = { indicator: 'households_visited', display: { dataLabels: true } } as const;
    const slicesOf = (context: Context) => all(context.root, 'pie-slice');

    it('writes each slice\'s value on it, leaving out a slice too thin for its text (Thin slice)', async () => {
      const context = await show('pie', settings, [row('Cali', 70), row('Bogotá', 25), row('Pasto', 1)], [households]);

      expect(slicesOf(context)).toHaveLength(3);
      expect(dataTexts(context)).toEqual(['70 households', '25 households']);
      expect(dataLabels(context).every((label) => label.getAttribute('aria-hidden') === 'true')).toBe(true);
    });

    it('writes the label of a pie of a single slice in its center (One slice)', async () => {
      const context = await show('pie', settings, [row('Cali', 90)], [households]);
      const svg = svgOf(context);

      const [label] = dataLabels(context);
      expect(dataTexts(context)).toEqual(['90 households']);
      expect(num(label, 'x')).toBe(num(svg, 'width') / 2);
      expect(num(label, 'y')).toBe(num(svg, 'height') / 2 + 4);
      expect(label.getAttribute('text-anchor')).toBe('middle');
    });

    it('leaves out a label that would leave the circle, whichever way its slice points (Label wider than its room)', async () => {
      // Two halves point sideways: their labels sit 0.62 of the radius from the center, and a text this wide would pass the circle's edge
      // although it is shorter than the chord across the slice.
      const wide = indicator('households_visited', { unit: 'viviendas en total' });
      const halves = await show('pie', settings, [row('Cali', 50), row('Bogotá', 50)], [wide]);
      expect(slicesOf(halves)).toHaveLength(2);
      expect(dataLabels(halves)).toHaveLength(0);
    });

    it('writes the label of slices pointing sideways when the text stays inside the circle', async () => {
      const narrow = await show('pie', settings, [row('Cali', 50), row('Bogotá', 50)], [indicator('households_visited', { unit: 'viviendas' })]);

      expect(dataTexts(narrow)).toEqual(['50 viviendas', '50 viviendas']);
    });

    it('leaves out the label of a pie of a single slice when its text is wider than the pie', async () => {
      const long = indicator('households_visited', { unit: 'viviendas visitadas por la encuesta del trimestre pasado' });
      const context = await show('pie', settings, [row('Cali', 90)], [long]);

      expect(slicesOf(context)).toHaveLength(1);
      expect(dataLabels(context)).toHaveLength(0);
    });

    it.each([
      ['no display', undefined],
      ['a saved false', { dataLabels: false }],
    ])('draws none unless it is asked for, with %s', async (_name, display) => {
      const context = await show('pie', { indicator: 'households_visited', ...(display ? { display } : {}) }, [row('Cali', 90), row('Bogotá', 10)], [households]);

      expect(dataLabels(context)).toHaveLength(0);
    });
  });
});

/** The width the charts reserve for a text. */
function textWidthOf(text: string): number {
  return text.length * 6.5;
}

const hitsOf = (context: Context) => all(context.root, 'chart-hit');
