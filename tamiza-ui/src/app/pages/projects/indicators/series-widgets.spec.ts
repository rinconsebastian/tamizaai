import en from '../../../../../public/i18n/en.json';
import es from '../../../../../public/i18n/es.json';
import { pressKey } from '../../../../testing/cdk';
import { byTestId, settle } from '../../../../testing/project-harness';
import { ChartType, IndicatorEntry, LikertStep, QueryResult, WidgetConfig, chartTypes } from '../../../core/projects/indicators.service';
import { Context, all, cell, clearOverlay, config, indicator, isQuery, open, overlay, result, seriesResult, text, widget } from './indicators-testing';
import { armSteps, scaleColors } from './likert-chart';

// The new kinds (design D5): heat map, population pyramid, diverging Likert bars and cross table, over the answer of a widget with a
// series (design D12). Each scenario is a case of dashboard-indicators.

interface Query {
  config: WidgetConfig;
  chartType: ChartType;
}

async function show(chartType: ChartType, settings: Partial<WidgetConfig>, answer: QueryResult, indicators?: IndicatorEntry[]) {
  const context = await open('viewer', [widget('w1', { chartType, config: config(settings) })], null, indicators ? { indicators } : {});
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(answer);
  await settle(context.harness);
  return { ...context, query: requests[0].request.body as Query };
}

const num = (element: Element, name: string) => Number(element.getAttribute(name));
const marks = (context: Context) => all(context.root, 'chart-mark');
const hits = (context: Context) => all(context.root, 'chart-hit');
const svg = (context: Context) => byTestId(context.root, 'chart-svg')!;
const label = (context: Context) => svg(context).getAttribute('aria-label');
const legend = (context: Context) => all(context.root, 'legend-name').map(text);
const svgTexts = (context: Context) => Array.from(context.root.querySelectorAll('[data-testid="chart-svg"] text')).map(text);
const tableHeaders = (context: Context) => Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text);
const tableRows = (context: Context) => all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
const tooltipLines = () => Array.from(overlay().querySelectorAll('[data-testid="tooltip-line"]')).map(text);
const tooltipHeading = () => text(overlay().querySelector('[data-testid="tooltip-heading"]'));
const spoken = (context: Context) => text(byTestId(context.root, 'marks-announcement')).replaceAll('​', '');
const hover = async (context: Context, target: Element) => {
  target.dispatchEvent(new MouseEvent('pointerenter', { clientX: 40, clientY: 30 }));
  await settle(context.harness);
};
async function toggle(context: Context) {
  byTestId(context.root, 'widget-table-toggle')!.click();
  await settle(context.harness);
}

const households = indicator('households_visited');
const sources = { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source' } as const;

describe('heat map', () => {
  afterEach(clearOverlay);

  /** Cali is the largest at 100 Piped and the smallest at 5 River; Buga has no River. */
  const grid = () =>
    seriesResult(
      [cell('Cali', 'Piped', 100), cell('Cali', 'Well', 20), cell('Cali', 'River', 5), cell('Buga', 'Piped', 40), cell('Buga', 'Well', 60)],
      ['Piped', 'River', 'Well'],
    );

  it('shows a row per group, a column per series, and the largest cell darkest (Cells shaded)', async () => {
    const context = await show('heatmap', sources, grid(), [households]);

    expect(all(context.root, 'heat-row-label').map(text)).toEqual(['Cali', 'Buga']);
    expect(all(context.root, 'heat-column-label').map(text)).toEqual(['Piped', 'River', 'Well']);
    expect(marks(context)).toHaveLength(6);
    const steps = marks(context).map((m) => m.getAttribute('data-step'));
    expect(steps[0]).toBe('7'); // Cali, Piped: 100, the largest
    expect(steps[1]).toBe('1'); // Cali, River: 5, the smallest
    expect(Math.max(...steps.filter((s) => s !== null).map(Number))).toBe(7);
    expect(marks(context)[0].getAttribute('style')).toContain('var(--tmz-seq-7)');
    expect(context.query).toEqual({ config: { ...config(sources), seriesBy: 'water_source' }, chartType: 'heatmap' });
  });

  it('shades by the sequential tokens, which the dark theme swaps, never by a literal color', async () => {
    const context = await show('heatmap', sources, grid(), [households]);

    for (const mark of marks(context).filter((m) => !m.hasAttribute('data-empty'))) {
      expect(mark.getAttribute('style')).toMatch(/var\(--tmz-seq-[1-7]\)/);
    }
    // The written value and its legend: the scale runs from the smallest to the largest value shown.
    expect(text(byTestId(context.root, 'heatmap-legend-min'))).toBe('5 households');
    expect(text(byTestId(context.root, 'heatmap-legend-max'))).toBe('100 households');
    expect(byTestId(context.root, 'heatmap-legend')!.getAttribute('aria-label')).toBe('Scale from 5 households to 100 households');
    expect(byTestId(context.root, 'heatmap-legend')!.querySelectorAll('[style*="--tmz-seq-"]')).toHaveLength(7);
  });

  it('writes the value in a cell that has room, in the ink of its step', async () => {
    const context = await show('heatmap', sources, grid(), [households]);

    expect(all(context.root, 'heat-value').map(text)).toEqual(['100', '5', '20', '40', '60']);
    expect(all(context.root, 'heat-value')[0].getAttribute('style')).toContain('var(--tmz-seq-ink-7)');
    expect(all(context.root, 'heat-value')[1].getAttribute('style')).toContain('var(--tmz-seq-ink-1)');
  });

  it('writes no value in cells too narrow for it, and leaves them to the tooltip', async () => {
    const names = Array.from({ length: 30 }, (_, i) => `S${String(i).padStart(2, '0')}`);
    const wide = seriesResult(names.map((name, i) => cell('Cali', name, 1000 + i)), names);
    const context = await show('heatmap', sources, wide, [households]);

    expect(marks(context)).toHaveLength(30);
    expect(all(context.root, 'heat-value')).toHaveLength(0);
    await hover(context, hits(context)[3]);
    expect(tooltipLines()).toEqual(['S03: 1,003 households']);
  });

  it('labels only as many columns and rows as fit, however many there are (Heat map within its limit)', async () => {
    const names = Array.from({ length: 30 }, (_, i) => `Source ${String(i).padStart(2, '0')}`);
    const places = Array.from({ length: 24 }, (_, i) => `Place ${String(i).padStart(2, '0')}`);
    const big = seriesResult(places.flatMap((p, g) => names.map((n, i) => cell(p, n, g + i + 1))), names);
    const context = await show('heatmap', sources, big, [households]);

    expect(marks(context)).toHaveLength(720);
    const columns = all(context.root, 'heat-column-label');
    expect(columns.length).toBeGreaterThan(0);
    expect(columns.length).toBeLessThan(30);
    expect(all(context.root, 'heat-row-label').length).toBeLessThan(24);
    expect(all(context.root, 'heat-value')).toHaveLength(0);
    // Every cell still has its tooltip.
    expect(hits(context)).toHaveLength(720);
    await toggle(context);
    expect(tableHeaders(context)).toHaveLength(32); // the group, 30 series and the total
  }, 20000);

  it('draws a cell with no value empty, hatched and outlined, and says so in its tooltip (Empty cell)', async () => {
    const context = await show('heatmap', sources, grid(), [households]);

    const empty = marks(context).filter((m) => m.hasAttribute('data-empty'));
    expect(empty).toHaveLength(1);
    expect(empty[0].getAttribute('fill')).toMatch(/^url\(#heat-hatch-\d+\)$/);
    expect(empty[0].getAttribute('class')).toContain('stroke-default-strong');
    expect(context.root.querySelector(empty[0].getAttribute('fill')!.slice(4, -1))).not.toBeNull(); // the pattern is defined
    // Buga is the second row, River the second column.
    await hover(context, hits(context)[4]);
    expect(tooltipHeading()).toBe('Buga');
    expect(tooltipLines()).toEqual(['River: No value']);
    expect(byTestId(context.root, 'heatmap-legend-empty')).not.toBeNull();
  });

  it('draws a cell whose value is 0 as a value, not as empty', async () => {
    const zero = seriesResult([cell('Cali', 'Piped', 0), cell('Cali', 'Well', 10)], ['Piped', 'Well']);
    const context = await show('heatmap', sources, zero, [households]);

    expect(marks(context).filter((m) => m.hasAttribute('data-empty'))).toHaveLength(0);
    expect(marks(context)[0].getAttribute('data-step')).toBe('1');
    expect(byTestId(context.root, 'heatmap-legend-empty')).toBeNull();
  });

  it('shows the tooltip of a cell on the keyboard as on hover, one cell at a time (Keyboard)', async () => {
    const context = await show('heatmap', sources, grid(), [households]);
    const chart = svg(context);

    expect(chart.getAttribute('role')).toBe('application');
    expect(chart.getAttribute('tabindex')).toBe('0');
    await hover(context, hits(context)[0]);
    const onHover = { heading: tooltipHeading(), lines: tooltipLines() };
    hits(context)[0].dispatchEvent(new MouseEvent('pointerleave'));
    await settle(context.harness);

    chart.focus();
    pressKey(chart, 'Enter');
    await settle(context.harness);
    expect({ heading: tooltipHeading(), lines: tooltipLines() }).toEqual(onHover);
    expect(spoken(context)).toBe('Cali. Piped: 100 households');
    expect(all(context.root, 'heat-active')).toHaveLength(1);

    pressKey(chart, 'ArrowRight'); // the next series of the same group
    await settle(context.harness);
    expect(spoken(context)).toBe('Cali. River: 5 households');
    pressKey(chart, 'ArrowDown'); // the next group
    await settle(context.harness);
    expect(spoken(context)).toBe('Buga. River: No value');
    pressKey(chart, 'Escape');
    await settle(context.harness);
    expect(overlay().querySelector('[data-testid="chart-tooltip"]')).toBeNull();
    expect(document.activeElement).toBe(chart);
  });

  it('describes itself for assistive technology, and lists the same numbers as a cross table', async () => {
    const context = await show('heatmap', sources, grid(), [households]);

    expect(label(context)).toBe('Heat map of Households visited, municipality by water_source: 2 groups, 3 series, from 5 households to 100 households. Highest: Cali, Piped.');
    await toggle(context);
    expect(tableHeaders(context)).toEqual(['municipality', 'Piped', 'River', 'Well', 'Total']);
    expect(tableRows(context)).toEqual([
      ['Cali', '100 households', '5 households', '20 households', '125 households'],
      ['Buga', '40 households', '—', '60 households', '100 households'],
      ['Total', '140 households', '5 households', '80 households', '225 households'],
    ]);
  });
});

describe('population pyramid', () => {
  afterEach(clearOverlay);

  const population = { indicator: 'households_visited', groupBy: 'age_band', seriesBy: 'sex', pyramid: { left: 'Female', right: 'Male' } } as const;
  const rows = () =>
    seriesResult(
      [
        cell('0-4', 'Female', 100),
        cell('0-4', 'Male', 100),
        cell('5-9', 'Female', 80),
        cell('5-9', 'Male', 60),
        cell('10-14', 'Female', 40),
        cell('10-14', 'Male', 70),
      ],
      ['Female', 'Male'],
      { leftOut: 0 },
    );
  const center = (context: Context) => num(all(context.root, 'chart-svg')[0].querySelector('line[class*="stroke-default-medium"]')!, 'x1');

  it('draws the first band at the bottom, the left value to the left and the right value to the right, on one scale (Age bands by sex)', async () => {
    const context = await show('pyramid', population, rows(), [households]);

    // The marks are listed from the top row down, the left bar before the right one: the last band first.
    const [female1014, male1014, female59, male59, female04, male04] = marks(context);
    const middle = center(context);
    expect(num(female04, 'x') + num(female04, 'width')).toBeCloseTo(middle, 1); // the left bar ends at the axis
    expect(num(male04, 'x')).toBeCloseTo(middle, 1); // the right bar starts at it
    // One scale: equal values are equal lengths on both sides, and 80 is 0.8 of 100.
    expect(num(female04, 'width')).toBeCloseTo(num(male04, 'width'), 1);
    expect(num(female59, 'width') / num(female04, 'width')).toBeCloseTo(0.8, 2);
    expect(num(male59, 'width') / num(male04, 'width')).toBeCloseTo(0.6, 2);
    expect(num(male1014, 'width') / num(male04, 'width')).toBeCloseTo(0.7, 2);
    // The first band, 0-4, is the bottom row.
    expect(num(female04, 'y')).toBeGreaterThan(num(female59, 'y'));
    expect(num(female59, 'y')).toBeGreaterThan(num(female1014, 'y'));
    expect(num(male1014, 'y')).toBe(num(female1014, 'y'));
    expect(all(context.root, 'chart-svg')[0].textContent).toContain('0-4');
    expect(context.query.config).toMatchObject({ seriesBy: 'sex', pyramid: { left: 'Female', right: 'Male' } });
    expect(context.query.chartType).toBe('pyramid');
  });

  it('colors the left in series color 1 and the right in color 2, and names them in a legend', async () => {
    const context = await show('pyramid', population, rows(), [households]);

    expect(legend(context)).toEqual(['Female', 'Male']);
    expect(marks(context).map((m) => m.getAttribute('style'))).toEqual([
      expect.stringContaining('var(--tmz-series-1)'),
      expect.stringContaining('var(--tmz-series-2)'),
      expect.stringContaining('var(--tmz-series-1)'),
      expect.stringContaining('var(--tmz-series-2)'),
      expect.stringContaining('var(--tmz-series-1)'),
      expect.stringContaining('var(--tmz-series-2)'),
    ]);
  });

  it('labels the axis on both sides without signs', async () => {
    const context = await show('pyramid', population, rows(), [households]);

    const ticks = svgTexts(context);
    expect(ticks).toEqual(expect.arrayContaining(['0', '20', '100']));
    expect(ticks.some((t) => t.startsWith('-'))).toBe(false);
    expect(ticks.filter((t) => t === '20')).toHaveLength(2); // once on each side
    expect(ticks.filter((t) => t === '0')).toHaveLength(1);
  });

  it('draws no bar for a third value or a missing band, and says how many rows were left out (Third value and missing values left out)', async () => {
    const context = await show('pyramid', population, { ...rows(), leftOut: 15 }, [households]);

    expect(marks(context)).toHaveLength(6);
    expect(text(byTestId(context.root, 'series-left-out'))).toBe('15 rows left out: they have no group or series value, or a series value other than the two sides.');
  });

  it('says nothing about rows left out when it left none out', async () => {
    const context = await show('pyramid', population, rows(), [households]);

    expect(byTestId(context.root, 'series-left-out')).toBeNull();
  });

  it('writes a percent indicator\'s values as the indicator does (Percent indicator)', async () => {
    const share = indicator('population_share', { format: 'percent' });
    const answer = seriesResult([cell('0-4', 'Female', 0.052), cell('0-4', 'Male', 0.05)], ['Female', 'Male']);
    const context = await show('pyramid', { ...population, indicator: 'population_share' }, answer, [share]);

    await hover(context, hits(context)[0]);
    expect(tooltipHeading()).toBe('0-4');
    expect(tooltipLines()).toEqual(['Female: 5.2%']);
    expect(all(context.root, 'pyramid-value').map(text)).toEqual(['5.2%', '5%']);
    expect(svgTexts(context)).toEqual(expect.arrayContaining(['0%', '2%', '4%']));
  });

  it('draws an empty right side, and no message about missing columns, when no row holds it (Pyramid side with no rows)', async () => {
    const onlyFemale = seriesResult([cell('0-4', 'Female', 100), cell('5-9', 'Female', 80)], ['Female', 'Male']);
    const context = await show('pyramid', population, onlyFemale, [households]);

    expect(marks(context)).toHaveLength(2);
    expect(legend(context)).toEqual(['Female', 'Male']);
    expect(byTestId(context.root, 'widget-missing')).toBeNull();
    expect(byTestId(context.root, 'chart-svg')).not.toBeNull();
  });

  it('draws no bar for a row with no group value', async () => {
    const answer = seriesResult([cell(null, 'Female', 9), cell('0-4', 'Female', 100), cell('0-4', 'Male', 90)], ['Female', 'Male']);
    const context = await show('pyramid', population, answer, [households]);

    expect(marks(context)).toHaveLength(2);
  });

  it('shows the group, the side and the value in the tooltip, on hover and on the keyboard', async () => {
    const context = await show('pyramid', population, rows(), [households]);

    await hover(context, hits(context)[5]); // the bottom row, the right bar
    expect(tooltipHeading()).toBe('0-4');
    expect(tooltipLines()).toEqual(['Male: 100 households']);

    hits(context)[5].dispatchEvent(new MouseEvent('pointerleave'));
    svg(context).focus();
    pressKey(svg(context), 'Enter');
    await settle(context.harness);
    // The keyboard starts at the top row, the last band; groups run down, so down is the next band toward the bottom.
    expect(spoken(context)).toBe('10-14. Female: 40 households');
    pressKey(svg(context), 'ArrowRight');
    await settle(context.harness);
    expect(spoken(context)).toBe('10-14. Male: 70 households');
    pressKey(svg(context), 'ArrowDown');
    await settle(context.harness);
    expect(spoken(context)).toBe('5-9. Male: 60 households');
  });

  it('says the rows it left out once in the table view too, not twice', async () => {
    const context = await show('pyramid', population, { ...rows(), leftOut: 15 }, [households]);

    await toggle(context);
    expect(all(context.root, 'series-left-out')).toHaveLength(1);
    expect(text(byTestId(context.root, 'series-left-out'))).toContain('15 rows left out');
  });

  it('lists the left and right columns and their total as a table, the bands in natural order', async () => {
    const context = await show('pyramid', population, rows(), [households]);

    await toggle(context);
    expect(tableHeaders(context)).toEqual(['age_band', 'Female', 'Male', 'Total']);
    expect(tableRows(context).map((r) => r[0])).toEqual(['0-4', '5-9', '10-14', 'Total']);
    expect(tableRows(context)[0]).toEqual(['0-4', '100 households', '100 households', '200 households']);
    expect(byTestId(context.root, 'series-left-out')).toBeNull();
  });

  it('describes itself for assistive technology', async () => {
    const context = await show('pyramid', population, rows(), [households]);

    expect(label(context)).toBe('Population pyramid of Households visited by age_band: Female on the left and Male on the right, 3 groups, up to 100 households. Largest: 0-4, Female.');
  });
});

describe('diverging Likert bars', () => {
  afterEach(clearOverlay);

  const scale: LikertStep[] = [
    { value: 'Strongly disagree', side: 'negative' },
    { value: 'Disagree', side: 'negative' },
    { value: 'Neither', side: 'neutral' },
    { value: 'Agree', side: 'positive' },
    { value: 'Strongly agree', side: 'positive' },
  ];
  const names = scale.map((step) => step.value);
  const likert: Partial<WidgetConfig> = { indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'opinion', scale };
  const answer = (extra: Partial<QueryResult> = {}) =>
    seriesResult(
      [
        cell('Cali', 'Strongly disagree', 10),
        cell('Cali', 'Disagree', 20),
        cell('Cali', 'Neither', 20),
        cell('Cali', 'Agree', 30),
        cell('Cali', 'Strongly agree', 20),
        cell('Buga', 'Agree', 5),
        cell('Buga', 'Strongly agree', 5),
      ],
      names,
      extra,
    );
  const zero = (context: Context) => num(svg(context).querySelector('line[class*="stroke-default-medium"]')!, 'x1');
  const extent = (rects: Element[]) => ({ left: Math.min(...rects.map((r) => num(r, 'x'))), right: Math.max(...rects.map((r) => num(r, 'x') + num(r, 'width'))) });

  it('centers each bar on the line between negative and positive, the neutral split across it (Composition around the center)', async () => {
    const context = await show('likert', likert, answer(), [households]);

    const cali = marks(context).slice(0, 5);
    const middle = zero(context);
    const { left, right } = extent(cali);
    expect(middle - left).toBeCloseTo(((right - left) * 40) / 100, 0); // 30% of negatives and half of the 20% neutral to the left
    expect(right - middle).toBeCloseTo(((right - left) * 60) / 100, 0);
    const neutral = cali[2];
    expect(num(neutral, 'x')).toBeLessThan(middle);
    expect(num(neutral, 'x') + num(neutral, 'width')).toBeGreaterThan(middle);
    expect(cali.map((r) => r.getAttribute('data-side'))).toEqual(['negative', 'negative', 'neutral', 'positive', 'positive']);
    expect(all(context.root, 'likert-end').map(text).slice(0, 2)).toEqual(['40%', '60%']);
    expect(context.query.config).toMatchObject({ seriesBy: 'opinion', scale });
    expect(context.query.chartType).toBe('likert');
  });

  it('puts the negatives nearest-first to the left and the positives nearest-first to the right, two pixels apart', async () => {
    const context = await show('likert', likert, answer(), [households]);

    const [strongly, disagree, neither, agree, stronglyAgree] = marks(context).slice(0, 5);
    expect(num(strongly, 'x')).toBeLessThan(num(disagree, 'x'));
    expect(num(disagree, 'x')).toBeLessThan(num(neither, 'x'));
    expect(num(agree, 'x')).toBeLessThan(num(stronglyAgree, 'x'));
    expect(num(disagree, 'x') - (num(strongly, 'x') + num(strongly, 'width'))).toBeCloseTo(2, 1);
    expect(num(stronglyAgree, 'x') - (num(agree, 'x') + num(agree, 'width'))).toBeCloseTo(2, 1);
  });

  it('draws a group that has only some of the scale from the center, and a group of one side to one side', async () => {
    const context = await show('likert', likert, answer(), [households]);

    const buga = marks(context).slice(5);
    expect(buga).toHaveLength(2);
    expect(num(buga[0], 'x')).toBeCloseTo(zero(context), 0); // only positives: the bar starts at the line
    expect(all(context.root, 'likert-end').map(text).slice(2)).toEqual(['100%']);
  });

  it('colors the arms by distance from the neutral: red to the left, blue to the right, the neutral in its midpoint tone', async () => {
    const context = await show('likert', likert, answer(), [households]);

    expect(marks(context).slice(0, 5).map((m) => m.getAttribute('style'))).toEqual([
      expect.stringContaining('var(--tmz-div-neg-4)'),
      expect.stringContaining('var(--tmz-div-neg-2)'),
      expect.stringContaining('var(--tmz-div-neutral)'),
      expect.stringContaining('var(--tmz-div-pos-2)'),
      expect.stringContaining('var(--tmz-div-pos-4)'),
    ]);
    expect(legend(context)).toEqual(names);
    expect(all(context.root, 'legend-swatch').map((s) => s.getAttribute('style'))).toEqual(marks(context).slice(0, 5).map((m) => m.getAttribute('style')?.match(/fill: ([^;]+);/)?.[0].replace('fill', 'background')));
  });

  it('gives each segment a tooltip with its share, its value and the side it is on', async () => {
    const context = await show('likert', likert, answer(), [households]);

    await hover(context, hits(context)[3]);
    expect(tooltipHeading()).toBe('Cali');
    expect(tooltipLines()).toEqual(['Agree: 30%', 'Households visited: 30 households']);
    expect(text(overlay().querySelector('[data-testid="tooltip-footer"]'))).toBe('Positive');
  });

  it('moves the keyboard through the bars: down between groups, right between the values of the scale', async () => {
    const context = await show('likert', likert, answer(), [households]);
    const chart = svg(context);

    chart.focus();
    pressKey(chart, 'Enter');
    await settle(context.harness);
    expect(spoken(context)).toBe('Cali. Strongly disagree: 10%. Households visited: 10 households. Negative');
    pressKey(chart, 'ArrowRight');
    await settle(context.harness);
    expect(spoken(context)).toContain('Cali. Disagree: 20%');
    pressKey(chart, 'ArrowDown');
    await settle(context.harness);
    expect(spoken(context)).toContain('Buga. Agree: 50%');
  });

  it('leaves out the values outside the scale and says how many rows it left out (Value outside the scale)', async () => {
    const context = await show('likert', likert, answer({ leftOut: 7 }), [households]);

    expect(text(byTestId(context.root, 'series-left-out'))).toBe('7 rows left out: they have no group or answer, or an answer outside the scale.');
    expect(marks(context)).toHaveLength(7);
  });

  it('lists one share per value of the scale, in its order, and the group\'s total (Likert as a table)', async () => {
    const context = await show('likert', likert, answer(), [households]);

    await toggle(context);
    expect(tableHeaders(context)).toEqual(['municipality', ...names, 'Total']);
    expect(tableRows(context)).toEqual([
      ['Cali', '10%', '20%', '20%', '30%', '20%', '100 households'],
      ['Buga', '—', '—', '—', '50%', '50%', '10 households'],
    ]);
  });

  it('says the rows it left out once in the table view too, not twice', async () => {
    const context = await show('likert', likert, answer({ leftOut: 7 }), [households]);

    await toggle(context);
    expect(all(context.root, 'series-left-out')).toHaveLength(1);
  });

  it('describes itself for assistive technology', async () => {
    const context = await show('likert', likert, answer(), [households]);

    expect(label(context)).toBe('Diverging Likert bars of Households visited by municipality: 2 groups, 5 series on the scale. Most positive: Buga, 100%.');
  });
});

describe('the diverging scale', () => {
  it('spreads the values of one side over the five steps of its arm, nearest the neutral first', () => {
    expect([0, 1, 2, 3, 4, 5].map(armSteps)).toEqual([[], [3], [2, 4], [1, 3, 5], [1, 2, 4, 5], [1, 2, 3, 4, 5]]);
  });

  it('colors a seven-point scale with three steps on each arm and the neutral between', () => {
    expect(scaleColors(['negative', 'negative', 'negative', 'neutral', 'positive', 'positive', 'positive'])).toEqual([
      'var(--tmz-div-neg-5)',
      'var(--tmz-div-neg-3)',
      'var(--tmz-div-neg-1)',
      'var(--tmz-div-neutral)',
      'var(--tmz-div-pos-1)',
      'var(--tmz-div-pos-3)',
      'var(--tmz-div-pos-5)',
    ]);
  });

  it('colors a scale with no neutral value, or with one side only', () => {
    expect(scaleColors(['negative', 'positive'])).toEqual(['var(--tmz-div-neg-3)', 'var(--tmz-div-pos-3)']);
    expect(scaleColors(['positive', 'positive'])).toEqual(['var(--tmz-div-pos-2)', 'var(--tmz-div-pos-4)']);
  });
});

describe('cross table widget', () => {
  afterEach(clearOverlay);

  const grid = () => seriesResult([cell('Cali', 'Piped', 100, 10), cell('Cali', 'Well', 20, 2), cell('Buga', 'Piped', 40, 4), cell('Buga', 'Well', 60, 6)], ['Piped', 'Well']);

  it('shows the groups as rows, the series as columns, and the totals in a last row and column (Contingency table)', async () => {
    const context = await show('crosstab', sources, grid(), [households]);

    expect(tableHeaders(context)).toEqual(['municipality', 'Piped', 'Well', 'Total']);
    expect(tableRows(context)).toEqual([
      ['Cali', '100 households', '20 households', '120 households'],
      ['Buga', '40 households', '60 households', '100 households'],
      ['Total', '140 households', '80 households', '220 households'],
    ]);
    expect(context.query).toEqual({ config: config(sources), chartType: 'crosstab' });
    expect(text(byTestId(context.root, 'chart-caption'))).toBe('Households visited by municipality and water_source');
  });

  it('writes a dash for a pair with no rows and counts only the rows there are (Pair with no rows)', async () => {
    const answer = seriesResult([cell('Cali', 'Piped', 100), cell('Cali', 'River', 5), cell('Buga', 'Piped', 40)], ['Piped', 'River']);
    const context = await show('crosstab', sources, answer, [households]);

    expect(tableRows(context)).toEqual([
      ['Cali', '100 households', '5 households', '105 households'],
      ['Buga', '40 households', '—', '40 households'],
      ['Total', '140 households', '5 households', '145 households'],
    ]);
  });

  it('writes the totals of a ratio as the server computed them, not as sums or means of the cells (Ratio totals)', async () => {
    const answer = seriesResult([cell('Cali', 'Female', 0.5, 2), cell('Cali', 'Male', 0.25, 1)], ['Female', 'Male'], {
      totals: {
        groups: [{ group: 'Cali', value: 0.4, rows: 3 }],
        series: [
          { series: 'Female', kind: 'value', value: 0.5, rows: 2 },
          { series: 'Male', kind: 'value', value: 0.25, rows: 1 },
        ],
        all: { value: 0.4, rows: 3 },
      },
    });
    const ratio = { numerator: 'numerator', denominator: 'denominator' };
    const context = await show('crosstab', { ...sources, indicator: 'piped_water_rate', measure: null, ratio }, answer, [indicator('piped_water_rate')]);

    expect(tableRows(context)).toEqual([
      ['Cali', '50%', '25%', '40%'],
      ['Total', '50%', '25%', '40%'],
    ]);
  });

  it('keeps the group column in view when the table scrolls, and writes the totals in bold', async () => {
    const context = await show('crosstab', sources, grid(), [households]);

    const first = context.root.querySelector('[data-testid="widget-table"] tbody th')!;
    expect(first.className).toContain('sticky');
    expect(first.className).toContain('start-0');
    expect(context.root.querySelector('tmz-table-container')!.className).toContain('overflow-x-auto');
    const total = all(context.root, 'widget-table-row').at(-1)!;
    expect(total.className).toContain('font-semibold');
    expect(total.querySelector('th')!.className).toContain('font-semibold');
    expect(all(context.root, 'widget-table-row')[0].querySelector('td:last-child')!.className).toContain('font-semibold');
    expect(all(context.root, 'widget-table-row')[0].querySelector('td:first-of-type')!.className).not.toContain('font-semibold');
  });

  it('has no switch to a chart, being a table already', async () => {
    const context = await show('crosstab', sources, grid(), [households]);

    expect(byTestId(context.root, 'widget-table-toggle')).toBeNull();
    expect(byTestId(context.root, 'chart-toggle')).toBeNull();
  });

  it('says that the limit was reached', async () => {
    const context = await show('crosstab', sources, { ...grid(), rowLimitReached: true }, [households]);

    expect(byTestId(context.root, 'widget-row-limit')).not.toBeNull();
  });

  it('writes "(none)" and "Other" columns by their names', async () => {
    const answer = seriesResult(
      [cell('Cali', 'Piped', 10), cell('Cali', null, 2, 1, 'none'), cell('Cali', null, 3, 1, 'other')],
      ['Piped', { value: null, kind: 'none' }, { value: null, kind: 'other' }],
    );
    const context = await show('crosstab', sources, answer, [households]);

    expect(tableHeaders(context)).toEqual(['municipality', 'Piped', '(none)', 'Other', 'Total']);
  });
});

describe('a widget whose series column a script removed (Series column removed by a script change)', () => {
  afterEach(clearOverlay);

  it.each(['column', 'heatmap', 'pyramid', 'likert', 'crosstab'] as const)('shows a message naming the column in place of a %s chart', async (type) => {
    const context = await show(type, { ...sources, layout: type === 'column' ? 'stacked' : undefined }, result([], { missing: { indicator: false, columns: ['water_source'] } }), [households]);

    expect(text(byTestId(context.root, 'missing-columns'))).toBe('The published table no longer has the columns: water_source.');
    expect(byTestId(context.root, 'chart-svg')).toBeNull();
    expect(byTestId(context.root, 'widget-table')).toBeNull();
  });
});

describe('every kind of chart', () => {
  afterEach(clearOverlay);

  const answer = () => seriesResult([cell('Cali', 'Piped', 100), cell('Cali', 'Well', 20), cell('Buga', 'Piped', 40)], ['Piped', 'Well']);
  const scale: LikertStep[] = [
    { value: 'Piped', side: 'negative' },
    { value: 'Well', side: 'positive' },
  ];
  const settings: Partial<Record<ChartType, Partial<WidgetConfig>>> = {
    heatmap: sources,
    pyramid: { ...sources, pyramid: { left: 'Piped', right: 'Well' } },
    likert: { ...sources, scale },
    crosstab: sources,
    bar: sources,
    column: sources,
    line: sources,
  };
  /** The component that draws each kind. Every kind is listed: none is drawn by a default (design D6). */
  const drawnBy: Record<ChartType, string> = {
    stat: 'app-stat-widget',
    bar: 'app-axis-chart',
    column: 'app-axis-chart',
    line: 'app-axis-chart',
    histogram: 'app-axis-chart',
    pie: 'app-pie-widget',
    map: 'app-map-widget',
    heatmap: 'app-heatmap-chart',
    pyramid: 'app-pyramid-chart',
    likert: 'app-likert-chart',
    crosstab: 'app-crosstab-widget',
  };

  it('lists the eleven kinds', () => {
    expect(Object.keys(drawnBy)).toEqual([...chartTypes]);
  });

  it.each(chartTypes)('draws a %s widget with its own component and no other', async (type) => {
    const plain = type === 'stat' || type === 'histogram' || type === 'pie' || type === 'map';
    const context = await show(type, settings[type] ?? {}, plain ? result([{ group: type === 'stat' ? null : 'Cali', value: 5, geometry: null }]) : answer(), [households]);
    context.http.match((r) => r.method === 'GET' && r.url === '/api/v1/basemaps').forEach((r) => r.flush({ provider: 'none', basemaps: [] }));
    await settle(context.harness);

    for (const component of new Set(Object.values(drawnBy))) {
      expect(context.root.querySelector(component) !== null, `${type} drawn by ${component}`).toBe(component === drawnBy[type]);
    }
  });
});

describe('the new kinds in both languages', () => {
  afterEach(clearOverlay);

  it('names each of the eleven kinds in English and in Spanish', () => {
    for (const type of chartTypes) {
      expect(en.indicators.chartTypes[type], `en ${type}`).toBeTruthy();
      expect(es.indicators.chartTypes[type], `es ${type}`).toBeTruthy();
    }
    expect(Object.keys(en.indicators.chartTypes)).toEqual([...chartTypes]);
    expect(Object.keys(es.indicators.chartTypes)).toEqual([...chartTypes]);
  });

  it('writes a stacked chart\'s legend, summary and cross table in Spanish', async () => {
    const answer = seriesResult(
      [cell('Cali', 'Piped', 35), cell('Cali', null, 2, 1, 'none'), cell('Cali', null, 3, 1, 'other')],
      ['Piped', { value: null, kind: 'none' }, { value: null, kind: 'other' }],
    );
    const context = await open('viewer', [widget('w1', { chartType: 'column', config: config({ ...sources, layout: 'stacked' }) })], null, { indicators: [households] }, { lang: 'es' });
    context.http.match(isQuery)[0].flush(answer);
    await settle(context.harness);

    expect(legend(context)).toEqual(['Piped', '(ninguno)', 'Otros']);
    expect(label(context)).toBe('Gráfico de columnas apiladas de Households visited por municipality y water_source: 1 grupo, 3 series, de 2 households a 35 households. Más alto: Cali, Piped.');
    await toggle(context);
    expect(tableHeaders(context)).toEqual(['municipality', 'Piped', '(ninguno)', 'Otros', 'Total']);
  });

  it('writes the notes of a pyramid and the labels of a heat map in Spanish', async () => {
    const heat = await open('viewer', [widget('w1', { chartType: 'heatmap', config: config(sources) })], null, { indicators: [households] }, { lang: 'es' });
    heat.http.match(isQuery)[0].flush(seriesResult([cell('Cali', 'Piped', 5), cell('Cali', 'Well', 7), cell('Buga', 'Piped', 9)], ['Piped', 'Well']));
    await settle(heat.harness);

    expect(byTestId(heat.root, 'heatmap-legend')!.getAttribute('aria-label')).toBe('Escala de 5 households a 9 households');
    expect(text(byTestId(heat.root, 'heatmap-legend-empty'))).toBe('Sin valor');
  });
});
