import esJson from '../../../../../public/i18n/es.json';
import { pressKey } from '../../../../testing/cdk';
import { byTestId, settle } from '../../../../testing/project-harness';
import { DashboardColors, IndicatorEntry, PaletteId, QueryBox, QueryResult, ValueColor, WidgetConfig } from '../../../core/projects/indicators.service';
import { BoxplotPlotInput, BoxplotWords, boxDomain, boxTableModel, boxplotPlot } from './boxplot-chart';
import { Context, all, clearOverlay, config, indicator, isQuery, open, overlay, result, text, toggleTable, widget } from './indicators-testing';
import { boxFormat, valueFormatter } from './format';

// The box plot (design D5 of add-boxplot-widget). The plot is pure geometry over the boxes the API computed; the component is looked at
// through the dashboard, as the other kinds are. Each case is one of the scenarios of dashboard-indicators.

/** A box: the five figures are given, the whiskers default to the minimum and the maximum, and the mean to the median. */
function box(group: string | null, f: Partial<QueryBox> & Pick<QueryBox, 'q1' | 'median' | 'q3'>): QueryBox {
  const min = f.min ?? f.q1 - 1;
  const max = f.max ?? f.q3 + 1;
  return {
    group,
    values: 10,
    min,
    max,
    mean: f.median,
    lowerWhisker: min,
    upperWhisker: max,
    outlierCount: 0,
    outliers: [],
    ...f,
  };
}

const words: BoxplotWords = {
  values: 'Values',
  maximum: 'Maximum',
  upperWhisker: 'Upper whisker',
  q3: 'Q3',
  median: 'Median',
  mean: 'Mean',
  q1: 'Q1',
  lowerWhisker: 'Lower whisker',
  minimum: 'Minimum',
  outliers: 'Outliers',
  outlier: 'Outlier',
  notDrawn: (count) => `${count} outliers not drawn`,
};

const decimal = valueFormatter({ format: 'decimal', unit: null }, 'en-US');
const whole = (value: number) => new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 }).format(value);

function plot(boxes: QueryBox[], overrides: Partial<BoxplotPlotInput> = {}) {
  return boxplotPlot({
    boxes,
    orientation: 'vertical',
    width: 480,
    height: 280,
    color: () => 'var(--tmz-pal-tamiza-1)',
    groupLabel: (group) => group ?? '(none)',
    value: decimal,
    whole,
    words,
    zeroPad: 1,
    ...overrides,
  });
}

const three = () => [
  box('Cali', { q1: 10, median: 20, q3: 30, min: 0, max: 40 }),
  box('Buga', { q1: 20, median: 30, q3: 40, min: 10, max: 50 }),
  box('Palmira', { q1: 30, median: 40, q3: 50, min: 20, max: 60 }),
];

describe('boxplotPlot', () => {
  it('draws the first box on the left and larger values higher up when vertical', () => {
    const p = plot(three());

    const xs = p.boxes.map((b) => b.rect!.x);
    expect(xs).toEqual([...xs].sort((a, b) => a - b));
    const medians = p.boxes.map((b) => b.median.y1);
    expect(medians[0]).toBeGreaterThan(medians[1]);
    expect(medians[1]).toBeGreaterThan(medians[2]);
    // A vertical box's median runs across the box.
    expect(p.boxes[0].median.x2 - p.boxes[0].median.x1).toBeCloseTo(p.boxes[0].rect!.width, 6);
    expect(p.labels.map((l) => l.text)).toEqual(['Cali', 'Buga', 'Palmira']);
  });

  it('draws the first box at the top and larger values further right when horizontal, with the names on the left', () => {
    const p = plot(three(), { orientation: 'horizontal' });

    const ys = p.boxes.map((b) => b.rect!.y);
    expect(ys).toEqual([...ys].sort((a, b) => a - b));
    const medians = p.boxes.map((b) => b.median.x1);
    expect(medians[0]).toBeLessThan(medians[1]);
    expect(medians[1]).toBeLessThan(medians[2]);
    expect(p.boxes[0].median.y2 - p.boxes[0].median.y1).toBeCloseTo(p.boxes[0].rect!.height, 6);
    expect(p.labels.every((l) => l.anchor === 'end' && l.x < p.boxes[0].rect!.x)).toBe(true);
  });

  it('spans a box from its first to its third quartile, with whiskers to their ends and the mean as a cross', () => {
    const p = plot([box('Cali', { q1: 10, median: 20, q3: 30, min: 0, max: 40, mean: 22 })]);
    const [b] = p.boxes;

    // 4 whisker marks: a stem and a cap at each end.
    expect(b.whiskers).toHaveLength(4);
    expect(b.mean).toHaveLength(2);
    expect(b.rect!.height).toBeGreaterThan(0);
    // The scale is one: the box spans 20 units and the upper stem 10.
    const unit = b.rect!.height / 20;
    const upperStem = b.whiskers[0];
    expect(Math.abs(upperStem.y2 - upperStem.y1)).toBeCloseTo(10 * unit, 5);
  });

  it('starts its scale where the values start and not at zero', () => {
    const p = plot([box('A', { q1: 55, median: 60, q3: 70, min: 40, max: 90 })]);

    expect(p.axis.min).toBe(40);
    expect(p.axis.max).toBe(90);
    expect(p.ticks.map((t) => t.text).length).toBeGreaterThan(2);
  });

  it('draws a box of one value as its median line only', () => {
    const p = plot([box('A', { q1: 7, median: 7, q3: 7, min: 7, max: 7, values: 1 })]);
    const [b] = p.boxes;

    expect(b.rect).toBeNull();
    expect(b.mean).toEqual([]);
    expect(b.whiskers).toEqual([]);
    expect(b.median).toBeDefined();
    expect(p.outliers).toEqual([]);
  });

  it('draws each outlier as a point, and counts the ones the boxes did not draw', () => {
    const drawn = Array.from({ length: 100 }, (_, i) => 1000 + i);
    const p = plot([box('A', { q1: 1, median: 4, q3: 6, min: 0, max: 1200, lowerWhisker: 0, upperWhisker: 9, outlierCount: 130, outliers: drawn })]);

    expect(p.outliers).toHaveLength(100);
    expect(p.notDrawn).toBe(30);
    expect(p.outliers[0].heading).toBe('A');
    expect(p.outliers[0].text).toBe('1,000.00');
    expect(plot(three()).notDrawn).toBe(0);
  });

  it('keeps a mean below the lower whisker inside the scale when only high outliers are drawn', () => {
    const outliers = Array.from({ length: 100 }, (_, i) => 1000 + i);
    const b = box('A', { q1: 0, median: 5, q3: 10, min: -990, max: 1100, lowerWhisker: 0, upperWhisker: 10, mean: -183.8, outlierCount: 600, outliers });

    const axis = boxDomain([b], 1);
    const p = plot([b]);

    expect(axis.min).toBeLessThanOrEqual(-183.8);
    expect(axis.max).toBeGreaterThanOrEqual(1099);
    const meanY = p.boxes[0].mean[0].y1;
    // The plot area runs from 8 px down to the labels, 26 px above the bottom.
    expect(meanY).toBeLessThanOrEqual(280 - 26);
    expect(meanY).toBeGreaterThanOrEqual(8);
  });

  it('pads a flat scale in proportion to its value so that its ticks differ', () => {
    const formatter = valueFormatter({ format: 'decimal', unit: null }, 'en-US');
    const p = plot([box('A', { q1: 1_000_000, median: 1_000_000, q3: 1_000_000, min: 1_000_000, max: 1_000_000, values: 1 })], { value: formatter });

    const texts = p.ticks.map((t) => t.text);
    expect(new Set(texts).size).toBe(texts.length);
    expect(p.axis.min).toBeLessThan(1_000_000);
    expect(p.axis.max).toBeGreaterThan(1_000_000);
  });

  it('does not give a rate of 0 a negative tick', () => {
    const percent = valueFormatter({ format: 'percent', unit: null }, 'en-US');
    const p = plot([box('A', { q1: 0, median: 0, q3: 0, min: 0, max: 0, mean: 0, values: 1 })], { value: percent, zeroPad: 0.01 });

    expect(p.axis.min).toBe(0);
    expect(p.ticks.map((t) => t.text).some((text) => text.startsWith('-'))).toBe(false);
  });

  it('cuts a long group name and keeps the whole name in a title', () => {
    const name = 'Municipality of a very long name indeed';
    const p = plot([box(name, { q1: 1, median: 2, q3: 3 })], { orientation: 'horizontal', width: 300 });

    expect(p.labels[0].text.endsWith('…')).toBe(true);
    expect(p.labels[0].title).toBe(name);
  });

  it('gives each box one mark with the figures from the top, and a footer for the outliers it did not draw', () => {
    const p = plot([box('A', { q1: 1, median: 4, q3: 6, min: 0, max: 9, outlierCount: 3, outliers: [9] })]);

    const mark = p.marks[0][0]!;
    expect(mark.content.heading).toBe('A');
    expect(mark.content.lines.map((l) => l.label)).toEqual(['Values', 'Maximum', 'Upper whisker', 'Q3', 'Median', 'Mean', 'Q1', 'Lower whisker', 'Minimum', 'Outliers']);
    expect(mark.content.lines[0].value).toBe('10');
    expect(mark.content.footer).toBe('2 outliers not drawn');
    // Only the median line has no swatch: it is drawn in ink, and the box color would say it is the box.
    expect(mark.content.lines.every((l) => l.color === undefined)).toBe(true);
  });
});

describe('boxTableModel', () => {
  it('lists a box per row with its figures, in the formatter it is given, and whole numbers for its counts', () => {
    const count = valueFormatter({ format: boxFormat('count'), unit: 'people' }, 'en-US');
    const model = boxTableModel(
      [box('Cali', { q1: 3.75, median: 4.5, q3: 5.25, min: 3, max: 6, mean: 4.5, values: 1200, outlierCount: 2 }), box(null, { q1: 1, median: 2, q3: 3, min: 0, max: 4 })],
      'municipality',
      words,
      (group) => group ?? '(none)',
      count,
      whole,
    );

    expect(model.headers).toEqual(['municipality', 'Values', 'Minimum', 'Q1', 'Median', 'Q3', 'Maximum', 'Mean', 'Outliers']);
    expect(model.rows[0]).toEqual(['Cali', '1,200', '3.00 people', '3.75 people', '4.50 people', '5.25 people', '6.00 people', '4.50 people', '2']);
    expect(model.rows[1][0]).toBe('(none)');
  });

  it('writes a count with two decimals and keeps every other format as it is', () => {
    expect(boxFormat('count')).toBe('decimal');
    expect(boxFormat('percent')).toBe('percent');
    expect(boxFormat('decimal')).toBe('decimal');
    expect(boxFormat('currency')).toBe('currency');
  });
});

describe('box plot widget', () => {
  afterEach(clearOverlay);

  const size = (): IndicatorEntry => indicator('household_size', { label: 'Household size', format: 'count', unit: 'people' });

  const boxes = (): QueryBox[] => [
    box('Cali', { q1: 3.75, median: 4.5, q3: 5.25, min: 3, max: 40, upperWhisker: 6, mean: 10.5, values: 1200, outlierCount: 3, outliers: [40, 38, 31] }),
    box('Buga', { q1: 1, median: 2, q3: 3, min: 0, max: 4 }),
    box('Palmira', { q1: 2, median: 3, q3: 4, min: 1, max: 5 }),
  ];

  /** The answer of a box plot: its boxes, and one row per box (its median and its number of values). */
  const answer = (list: QueryBox[] = boxes()): QueryResult =>
    result(
      list.map((b) => ({ group: b.group, value: b.median, geometry: null, rows: b.values })),
      { boxes: list },
    );

  async function show(settings: Partial<WidgetConfig> = {}, list?: QueryBox[], colors?: DashboardColors) {
    const context = await open(
      'viewer',
      [widget('w1', { chartType: 'boxplot', config: config({ indicator: 'household_size', groupBy: 'municipality', ...settings }) })],
      null,
      { indicators: [size()] },
      colors ? { colors } : {},
    );
    const requests = context.http.match(isQuery);
    expect(requests).toHaveLength(1);
    requests[0].flush(answer(list));
    await settle(context.harness);
    return { ...context, query: requests[0].request.body as { chartType: string; config: WidgetConfig } };
  }

  const svg = (context: Context) => byTestId(context.root, 'chart-svg')!;
  const hits = (context: Context) => all(context.root, 'chart-hit');
  const tooltipLines = () => Array.from(overlay().querySelectorAll('[data-testid="tooltip-line"]')).map(text);
  const tooltipHeading = () => text(overlay().querySelector('[data-testid="tooltip-heading"]'));
  const spoken = (context: Context) => text(byTestId(context.root, 'marks-announcement')).replaceAll('​', '');
  const colorOf = (element: Element | null) => /var\(--[\w-]+\)/.exec(element?.getAttribute('style') ?? '')?.[0] ?? null;
  const palette = (name: PaletteId, valueColors: ValueColor[] = []): DashboardColors => ({ palette: name, valueColors });

  it('asks for the boxes with the box plot chart type, and draws one box for each group', async () => {
    const context = await show();

    expect(context.query.chartType).toBe('boxplot');
    expect(all(context.root, 'chart-mark').map((m) => m.getAttribute('data-group'))).toEqual(['Cali', 'Buga', 'Palmira']);
    expect(svg(context).getAttribute('role')).toBe('application');
    expect(svg(context).getAttribute('aria-roledescription')).toBe('box plot');
    expect(text(byTestId(context.root, 'boxplot-key'))).toContain('Middle half (Q1 to Q3)');
    expect(text(byTestId(context.root, 'boxplot-key'))).toContain('Whiskers: up to 1.5 × the box beyond it');
  });

  it('shows the figures of a box on hover, a count with two decimals and its numbers of values whole', async () => {
    const context = await show();

    hits(context)[0].dispatchEvent(new MouseEvent('pointerenter', { clientX: 40, clientY: 30 }));
    await settle(context.harness);

    expect(tooltipHeading()).toBe('Cali');
    expect(tooltipLines()).toEqual([
      'Values: 1,200',
      'Maximum: 40.00 people',
      'Upper whisker: 6.00 people',
      'Q3: 5.25 people',
      'Median: 4.50 people',
      'Mean: 10.50 people',
      'Q1: 3.75 people',
      'Lower whisker: 3.00 people',
      'Minimum: 3.00 people',
      'Outliers: 3',
    ]);
  });

  it('steps through the boxes with the right arrow when vertical, entering at the first (Stepping through boxes)', async () => {
    const context = await show();
    const chart = svg(context);

    chart.focus();
    pressKey(chart, 'ArrowRight');
    await settle(context.harness);
    expect(tooltipHeading()).toBe('Cali');
    pressKey(chart, 'ArrowRight');
    pressKey(chart, 'ArrowRight');
    await settle(context.harness);

    expect(tooltipHeading()).toBe('Palmira');
    expect(spoken(context)).toContain('Palmira');
    expect(spoken(context)).toContain('Median: 3.00 people');
    pressKey(chart, 'Home');
    await settle(context.harness);
    expect(tooltipHeading()).toBe('Cali');
    pressKey(chart, 'End');
    await settle(context.harness);
    expect(tooltipHeading()).toBe('Palmira');
    pressKey(chart, 'Escape');
    await settle(context.harness);
    expect(overlay().querySelector('[data-testid="chart-tooltip"]')).toBeNull();
    expect(document.activeElement).toBe(chart);
  });

  it('steps through the boxes with the down arrow when horizontal (Horizontal keys)', async () => {
    const context = await show({ display: { orientation: 'horizontal' } });
    const chart = svg(context);

    chart.focus();
    pressKey(chart, 'ArrowDown');
    pressKey(chart, 'ArrowDown');
    await settle(context.harness);

    expect(tooltipHeading()).toBe('Buga');
    expect(text(context.root.querySelector('[id^="marks-help"]'))).toContain('Up and down arrows move between the groups');
    // The left and right arrows do nothing across the groups of a horizontal chart.
    pressKey(chart, 'ArrowRight');
    await settle(context.harness);
    expect(tooltipHeading()).toBe('Buga');
  });

  it('shows an outlier on the pointer and never lands the keyboard on one (Outliers are not keyboard stops)', async () => {
    const context = await show();
    const dots = all(context.root, 'outlier-hit');
    expect(dots).toHaveLength(3);

    dots[0].dispatchEvent(new MouseEvent('pointerenter', { clientX: 50, clientY: 20 }));
    await settle(context.harness);
    expect(tooltipHeading()).toBe('Cali');
    expect(tooltipLines()).toEqual(['Outlier: 40.00 people']);
    dots[0].dispatchEvent(new MouseEvent('pointerleave'));
    await settle(context.harness);
    expect(overlay().querySelector('[data-testid="chart-tooltip"]')).toBeNull();

    const chart = svg(context);
    chart.focus();
    const reached: string[] = [];
    for (let i = 0; i < 6; i++) {
      pressKey(chart, 'ArrowRight');
      await settle(context.harness);
      reached.push(tooltipHeading());
    }
    // Only boxes are reached, one step each: Cali has three outliers, and a stop on one would repeat its heading.
    expect(reached).toEqual(['Cali', 'Buga', 'Palmira', 'Palmira', 'Palmira', 'Palmira']);
    // No mark of the chart is an outlier: the announcement is a whole box's tooltip.
    expect(spoken(context)).toContain('Values: 10');
  });

  it('says how many outliers it did not draw, in the tooltip and under the plot', async () => {
    const list = [box('Cali', { q1: 1, median: 4, q3: 6, min: 0, max: 1200, upperWhisker: 9, outlierCount: 101, outliers: Array.from({ length: 100 }, (_, i) => 1000 + i) })];
    const context = await show({}, list);

    expect(text(byTestId(context.root, 'boxplot-not-drawn'))).toBe('1 outlier not drawn');
    hits(context)[0].dispatchEvent(new MouseEvent('pointerenter', { clientX: 40, clientY: 30 }));
    await settle(context.harness);
    expect(text(overlay().querySelector('[data-testid="tooltip-footer"]'))).toBe('1 outlier not drawn');
  });

  it('writes the outliers not drawn in Spanish with the right plural', () => {
    const es = (JSON.parse(JSON.stringify(esJson)) as typeof esJson).indicators.boxplot;

    expect(es.notDrawnOne.replace('{{ count }}', '1')).toBe('1 atípico sin dibujar');
    expect(es.notDrawn.replace('{{ count }}', '30')).toBe('30 atípicos sin dibujar');
  });

  it('says 30 outliers not drawn for a box with 130', async () => {
    const list = [box('Cali', { q1: 1, median: 4, q3: 6, min: 0, max: 1200, upperWhisker: 9, outlierCount: 130, outliers: Array.from({ length: 100 }, (_, i) => 1000 + i) })];
    const context = await show({}, list);

    expect(text(byTestId(context.root, 'boxplot-not-drawn'))).toBe('30 outliers not drawn');
  });

  it('draws a box in the color kept for its group value and the others in the widget color', async () => {
    const context = await show({ display: { color: 3 } }, undefined, palette('tamiza', [{ column: 'municipality', value: 'Buga', color: 5 }]));

    const colors = all(context.root, 'box-rect').map((r) => colorOf(r));
    expect(colors).toEqual(['var(--tmz-pal-tamiza-3)', 'var(--tmz-pal-tamiza-5)', 'var(--tmz-pal-tamiza-3)']);
    // Its whiskers and its outliers are drawn in the same color as its box.
    const first = all(context.root, 'chart-mark')[0];
    expect(Array.from(first.querySelectorAll('line:not([data-testid])')).every((line) => colorOf(line) === 'var(--tmz-pal-tamiza-3)')).toBe(true);
    expect(colorOf(all(context.root, 'box-outlier')[0])).toBe('var(--tmz-pal-tamiza-3)');
  });

  it('is drawn vertical by default: its median runs across the box', async () => {
    const context = await show();
    const median = byTestId(context.root, 'box-median')!;

    expect(median.getAttribute('y1')).toBe(median.getAttribute('y2'));
    expect(median.getAttribute('x1')).not.toBe(median.getAttribute('x2'));
  });

  it('is drawn horizontal when its display says so: its median runs across the box, up and down', async () => {
    const context = await show({ display: { orientation: 'horizontal' } });
    const median = byTestId(context.root, 'box-median')!;

    expect(median.getAttribute('x1')).toBe(median.getAttribute('x2'));
    expect(median.getAttribute('y1')).not.toBe(median.getAttribute('y2'));
  });

  it('describes itself for assistive technology', async () => {
    const context = await show();

    expect(svg(context).getAttribute('aria-label')).toBe('Box plot of Household size by municipality: 3 boxes, medians from 2.00 people to 4.50 people.');
  });

  it('says the medians once when they are all equal', async () => {
    const same = [box('A', { q1: 3, median: 4, q3: 5 }), box('B', { q1: 3, median: 4, q3: 5 })];
    const context = await show({}, same);

    expect(svg(context).getAttribute('aria-label')).toBe('Box plot of Household size by municipality: 2 boxes, all with the median 4.00 people.');
  });

  it('says the limit was reached in boxes, not rows', async () => {
    const context = await open('viewer', [widget('w1', { chartType: 'boxplot', config: config({ indicator: 'household_size', groupBy: 'municipality' }) })], null, { indicators: [size()] });
    context.http.match(isQuery)[0].flush({ ...answer(), rowLimitReached: true });
    await settle(context.harness);

    expect(text(byTestId(context.root, 'widget-row-limit'))).toBe('Only the first 3 boxes are shown: the query matches more. Add a filter to narrow it down.');
  });

  it('lists the same numbers as a table', async () => {
    const context = await show();

    await toggleTable(context);
    await settle(context.harness);

    const headers = Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text);
    expect(headers).toEqual(['municipality', 'Values', 'Minimum', 'Q1', 'Median', 'Q3', 'Maximum', 'Mean', 'Outliers']);
    const rows = all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
    expect(rows[0]).toEqual(['Cali', '1,200', '3.00 people', '3.75 people', '4.50 people', '5.25 people', '40.00 people', '10.50 people', '3']);
    expect(rows).toHaveLength(3);
  });

  it('labels the one box of a widget with no group column "All rows"', async () => {
    const context = await show({ groupBy: null }, [box(null, { q1: 1, median: 2, q3: 3 })]);

    expect(all(context.root, 'chart-mark')).toHaveLength(1);
    await toggleTable(context);
    await settle(context.harness);
    expect(Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text)[0]).toBe('Group');
    expect(all(context.root, 'widget-table-row').map((r) => text(r.children[0]))).toEqual(['All rows']);
  });
});
