import { pressKey } from '../../../../testing/cdk';
import { byTestId, settle } from '../../../../testing/project-harness';
import { ChartType, DashboardColors, IndicatorEntry, PaletteId, QueryBox, QueryResult, QueryRow, ValueColor, WidgetConfig } from '../../../core/projects/indicators.service';
import { Context, all, cell, clearOverlay, config, indicator, isQuery, open, result, row, seriesResult, text, toggleTable, widget } from './indicators-testing';

// The declared category order (add-declared-category-order, design D4 and D5) as every kind of chart draws it: the order of the
// groups and of the series, the values kept last, the text without its leading number, and today's order when nothing is declared.
// Each case is a scenario of dashboard-indicators.

async function show(chartType: ChartType, settings: Partial<WidgetConfig>, answer: QueryResult, indicators: IndicatorEntry[], colors?: DashboardColors) {
  const context = await open('viewer', [widget('w1', { chartType, config: config(settings) })], null, { indicators }, colors ? { colors } : {});
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(answer);
  await settle(context.harness);
  return context;
}

const titles = (elements: HTMLElement[]) => elements.map((e) => text(e.querySelector('title')));
const marks = (context: Context) => all(context.root, 'chart-mark');
const legend = (context: Context) => all(context.root, 'legend-name').map(text);
const tableHeaders = (context: Context) => Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text);
const tableRows = (context: Context) => all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
const label = (context: Context) => byTestId(context.root, 'chart-svg')!.getAttribute('aria-label');
async function toggle(context: Context) {
  await toggleTable(context);
  await settle(context.harness);
}

/** The group names of a chart with no series, as the marks' titles write them (`name: value`). */
const groupsOf = (context: Context) => titles(marks(context)).map((title) => title.split(':')[0]);

const stores = (columns: IndicatorEntry['columns']) => [indicator('comercio_lugar_compra', { label: 'Lugar de compra', columns })];
const places: QueryRow[] = [row('Almacenes del barrio', 10), row('Centro de Duitama', 30), row('Otro', 5), row('Vendedores ambulantes', 20)];
const byPlace = { indicator: 'comercio_lugar_compra', groupBy: 'categoria' } as const;

describe('the order of the groups of a bar or column chart', () => {
  afterEach(clearOverlay);

  it('follows the order its indicator declares, the groups not listed after them (Indicator order for every widget)', async () => {
    const columns = { categoria: { order: ['Centro de Duitama', 'Almacenes del barrio'] } };

    const context = await show('column', byPlace, result(places), stores(columns));

    expect(groupsOf(context)).toEqual(['Centro de Duitama', 'Almacenes del barrio', 'Otro', 'Vendedores ambulantes']);
  });

  it('keeps a value last at the end (Declared order without a sort)', async () => {
    const context = await show('bar', byPlace, result(places), stores({ categoria: { last: ['Otro'] } }));

    expect(groupsOf(context)).toEqual(['Almacenes del barrio', 'Centro de Duitama', 'Vendedores ambulantes', 'Otro']);
  });

  it('follows the widget\'s own order over the indicator\'s (Widget order over the indicator\'s)', async () => {
    const own = { ...byPlace, display: { order: { group: ['Vendedores ambulantes', 'Centro de Duitama'] } } };

    const context = await show('column', own, result(places), stores({ categoria: { order: ['Almacenes del barrio'], last: ['Otro'] } }));

    expect(groupsOf(context)).toEqual(['Vendedores ambulantes', 'Centro de Duitama', 'Almacenes del barrio', 'Otro']);
  });

  it('keeps its sort and moves only the values kept last (Sorted bars keep the values kept last at the end)', async () => {
    const sorted = { ...byPlace, sort: { by: 'value', column: null, compareAs: null, direction: 'asc' } } as const;
    const ascending = result([row('Otro', 5), row('Almacenes del barrio', 10), row('Vendedores ambulantes', 20), row('Centro de Duitama', 30)]);

    const context = await show('bar', sorted, ascending, stores({ categoria: { order: ['Centro de Duitama'], last: ['Otro'] } }));

    expect(groupsOf(context)).toEqual(['Almacenes del barrio', 'Vendedores ambulantes', 'Centro de Duitama', 'Otro']);
  });

  it('writes the groups without their numbers and colors them by the full value (Numbered answers)', async () => {
    const numbered = [row('1. Sí', 30), row('2. Tal vez', 10), row('3. No', 20)];

    const context = await show('column', { indicator: 'comercio_lugar_compra', groupBy: 'respuesta' }, result(numbered), [
      indicator('comercio_lugar_compra', { columns: { respuesta: { stripPrefix: true } } }),
    ]);

    expect(groupsOf(context)).toEqual(['Sí', 'Tal vez', 'No']);
    expect(text(byTestId(context.root, 'chart-svg')?.querySelector('title') ?? null)).not.toContain('1.');
  });

  it('draws as before when nothing is declared (Widgets without a declared category order)', async () => {
    const context = await show('column', byPlace, result(places), stores({ categoria: { label: 'Categoría' } }));

    expect(groupsOf(context)).toEqual(['Almacenes del barrio', 'Centro de Duitama', 'Otro', 'Vendedores ambulantes']);
  });
});

describe('the order of a line chart', () => {
  afterEach(clearOverlay);

  const prices = [row('$10.000', 30), row('$20.000', 20), row('$50.000', 5), row('$0', 40)];
  const demand = { indicator: 'teatro_demanda', groupBy: 'precio' } as const;
  const indicators = (columns: IndicatorEntry['columns']) => [indicator('teatro_demanda', { columns })];

  it('keeps its natural order of points without a declaration (Demand curve)', async () => {
    const context = await show('line', demand, result(prices), indicators(undefined));

    const texts = Array.from(context.root.querySelectorAll('[data-testid="chart-svg"] text')).map(text);
    const at = (name: string) => texts.indexOf(name);
    expect(at('$0')).toBeGreaterThanOrEqual(0);
    expect(at('$0')).toBeLessThan(at('$10.000'));
    expect(at('$10.000')).toBeLessThan(at('$20.000'));
    expect(at('$20.000')).toBeLessThan(at('$50.000'));
  });

  it('follows the widget\'s own order of points (Points in a declared order)', async () => {
    const own = { ...demand, display: { order: { group: ['$50.000', '$20.000', '$10.000', '$0'] } } };

    const context = await show('line', own, result(prices), indicators(undefined));

    const texts = Array.from(context.root.querySelectorAll('[data-testid="chart-svg"] text')).map(text);
    const at = (name: string) => texts.indexOf(name);
    expect(at('$50.000')).toBeLessThan(at('$20.000'));
    expect(at('$20.000')).toBeLessThan(at('$10.000'));
    expect(at('$10.000')).toBeLessThan(at('$0'));
  });
});

describe('the order of a widget with a series', () => {
  afterEach(clearOverlay);

  const conteo = (columns: IndicatorEntry['columns']) => [indicator('teatro_conteo', { label: 'Teatro conteo', columns })];
  const settings = { indicator: 'teatro_conteo', groupBy: 'comuna', seriesBy: 'respuesta', measure: 'value' } as const;
  const answer = () =>
    seriesResult(
      [
        cell('COMUNA 1', '1. Sí', 30),
        cell('COMUNA 1', '4. No sabe', 5),
        cell('COMUNA 1', '2. No', 10),
        cell('Sin dato', '1. Sí', 3),
        cell('COMUNA 2', '1. Sí', 20),
        cell('COMUNA 2', '2. No', 25),
      ],
      ['1. Sí', '2. No', '4. No sabe'],
    );

  it('lists the series of a stacked column in the declared order, with their numbers hidden (Legend, segments and table)', async () => {
    const columns = { respuesta: { order: ['1. Sí', '2. No'], last: ['4. No sabe'], stripPrefix: true } };

    const context = await show('column', { ...settings, layout: 'stacked' }, answer(), conteo(columns));

    expect(legend(context)).toEqual(['Sí', 'No', 'No sabe']);
    await toggle(context);
    expect(tableHeaders(context)).toEqual(['comuna', 'Sí', 'No', 'No sabe', 'Total']);
  });

  it('lists the series in a widget\'s own order and keeps the groups as they were', async () => {
    const own: Partial<WidgetConfig> = { ...settings, layout: 'stacked', display: { order: { series: ['4. No sabe', '2. No', '1. Sí'] } } };

    const context = await show('column', own, answer(), conteo(undefined));

    expect(legend(context)).toEqual(['4. No sabe', '2. No', '1. Sí']);
  });

  it('puts a value kept last at the end of the rows of a cross table (Rows in a declared order)', async () => {
    const context = await show('crosstab', settings, answer(), conteo({ comuna: { last: ['Sin dato'] } }));

    expect(tableRows(context).map((r) => r[0])).toEqual(['COMUNA 1', 'COMUNA 2', 'Sin dato', 'Total']);
  });

  it('lists the values not listed after those that are, columns included (Values not listed)', async () => {
    const context = await show('crosstab', settings, answer(), conteo({ respuesta: { order: ['2. No'] } }));

    expect(tableHeaders(context)).toEqual(['comuna', '2. No', '1. Sí', '4. No sabe', 'Total']);
  });

  it('orders the rows and columns of a heat map (Rows in a declared order)', async () => {
    const columns = { comuna: { order: ['COMUNA 2', 'COMUNA 1'], last: ['Sin dato'] }, respuesta: { order: ['4. No sabe'] } };

    const context = await show('heatmap', settings, answer(), conteo(columns));

    expect(all(context.root, 'heat-row-label').map(text)).toEqual(['COMUNA 2', 'COMUNA 1', 'Sin dato']);
    expect(all(context.root, 'heat-column-label').map(text)).toEqual(['4. No sabe', '1. Sí', '2. No']);
  });

  it('keeps the sides of a pyramid where they are and orders its rows (Pyramid sides keep their places)', async () => {
    const pyramid = { ...settings, pyramid: { left: '2. No', right: '1. Sí' } };
    const columns = { comuna: { order: ['COMUNA 2', 'COMUNA 1'] }, respuesta: { order: ['1. Sí', '2. No'], stripPrefix: true } };

    const context = await show('pyramid', pyramid, answer(), conteo(columns));

    await toggle(context);
    expect(tableRows(context).map((r) => r[0]).slice(0, 2)).toEqual(['COMUNA 2', 'COMUNA 1']);
  });
});

describe('the order of a pie chart', () => {
  afterEach(clearOverlay);

  const visits = [row('Piped', 60), row('Well', 30), row('River', 10)];
  const settings = { indicator: 'households_visited', groupBy: 'water_source' } as const;
  const sources = (columns: IndicatorEntry['columns']) => [indicator('households_visited', { columns })];

  it('draws its slices from the largest without a declaration (Largest slice first without a declared order)', async () => {
    const context = await show('pie', settings, result(visits), sources(undefined));

    expect(titles(all(context.root, 'pie-slice')).map((t) => t.split(':')[0])).toEqual(['Piped', 'Well', 'River']);
  });

  it('draws its slices in the widget\'s own order and still names the largest in its summary (Slices in a declared order)', async () => {
    const own = { ...settings, display: { order: { group: ['River', 'Well', 'Piped'] } } };

    const context = await show('pie', own, result(visits), sources(undefined));

    expect(titles(all(context.root, 'pie-slice')).map((t) => t.split(':')[0])).toEqual(['River', 'Well', 'Piped']);
    expect(label(context)).toContain('Piped');
    expect(all(context.root, 'legend-item').map((item) => text(item.querySelector('span:not([aria-hidden])')))).toEqual(['River', 'Well', 'Piped']);
  });
});

describe('the order of a box plot', () => {
  afterEach(clearOverlay);

  const box = (group: string | null, median: number): QueryBox => ({
    group,
    values: 10,
    min: median - 5,
    max: median + 5,
    q1: median - 2,
    median,
    q3: median + 2,
    mean: median,
    lowerWhisker: median - 5,
    upperWhisker: median + 5,
    outlierCount: 0,
    outliers: [],
  });
  const settings = { indicator: 'centro_tarifa_hogar', groupBy: 'vehiculo', measure: 'value' } as const;
  const answered = (boxes: QueryBox[]) => result(boxes.map((b) => ({ group: b.group, value: b.median, geometry: null, rows: b.values })), { boxes });
  const answer = () => answered([box('Carro', 10), box('Moto', 20), box('Todos los vehículos', 30)]);

  it('lists its boxes in the order the widget declares (Boxes in a declared order)', async () => {
    const own = { ...settings, display: { order: { group: ['Todos los vehículos', 'Carro', 'Moto'] } } };

    const context = await show('boxplot', own, answer(), [indicator('centro_tarifa_hogar')]);

    await toggle(context);
    expect(tableRows(context).map((r) => r[0])).toEqual(['Todos los vehículos', 'Carro', 'Moto']);
  });

  it('keeps a value kept last at the end of a box plot ordered by median (Order by median keeps the values kept last at the end)', async () => {
    const byMedian = { ...settings, sort: { by: 'value', column: null, compareAs: null, direction: 'desc' } } as const;
    const boxes = answered([box('Todos los vehículos', 30), box('Moto', 20), box('Carro', 10)]);

    const context = await show('boxplot', byMedian, boxes, [indicator('centro_tarifa_hogar', { columns: { vehiculo: { last: ['Todos los vehículos'] } } })]);

    await toggle(context);
    expect(tableRows(context).map((r) => r[0])).toEqual(['Moto', 'Carro', 'Todos los vehículos']);
  });
});

describe('numbered category prefixes hidden (design D5)', () => {
  afterEach(clearOverlay);

  const conteo = (columns: IndicatorEntry['columns']) => [indicator('teatro_conteo', { label: 'Teatro conteo', columns })];
  const settings = { indicator: 'teatro_conteo', groupBy: 'comuna', seriesBy: 'respuesta', measure: 'value' } as const;
  const numbered = () =>
    seriesResult(
      [cell('COMUNA 1', '1. Sí', 30), cell('COMUNA 1', '2. Tal vez', 10), cell('COMUNA 1', '3. No', 5), cell('COMUNA 1', '4. No sabe', 2), cell('COMUNA 2', '1. Sí', 20)],
      ['1. Sí', '2. Tal vez', '3. No', '4. No sabe'],
    );
  const tooltipLines = () => Array.from(document.querySelectorAll('[data-testid="tooltip-line"]')).map(text);

  it('reads the legend of a 100% bar without the numbers, in the same order (Numbered answers)', async () => {
    const context = await show('bar', { ...settings, layout: 'percent' }, numbered(), conteo({ respuesta: { stripPrefix: true } }));

    expect(legend(context)).toEqual(['Sí', 'Tal vez', 'No', 'No sabe']);
  });

  it('reads the same everywhere the value is shown: its table and its accessible name (Everywhere the value is shown)', async () => {
    const context = await show('column', { ...settings, layout: 'stacked' }, numbered(), conteo({ respuesta: { stripPrefix: true } }));

    await toggle(context);

    expect(tableHeaders(context)).toEqual(['comuna', 'Sí', 'Tal vez', 'No', 'No sabe', 'Total']);
  });

  it('shows a tooltip without the number', async () => {
    const context = await show('column', { ...settings, layout: 'stacked' }, numbered(), conteo({ respuesta: { stripPrefix: true } }));

    const hit = all(context.root, 'chart-hit')[0];
    hit.dispatchEvent(new MouseEvent('pointerenter', { clientX: 40, clientY: 30 }));
    await settle(context.harness);

    expect(tooltipLines().join(' ')).toContain('Sí');
    expect(tooltipLines().join(' ')).not.toContain('1. Sí');
  });

  it('leaves ranges and decimals as they are (Ranges are not prefixes)', async () => {
    const ranges = [row('10 - 14', 5), row('0 - 4', 3), row('1.5 horas', 2)];

    const context = await show('column', { indicator: 'teatro_conteo', groupBy: 'rango' }, result(ranges), [indicator('teatro_conteo', { columns: { rango: { stripPrefix: true } } })]);

    expect(groupsOf(context)).toEqual(['10 - 14', '0 - 4', '1.5 horas']);
  });

  it('keeps the full text of values that would read alike (Values that would read alike)', async () => {
    const alike = [row('1. Otro', 5), row('6. Otro', 3), row('2. Sí', 9)];

    const context = await show('column', { indicator: 'teatro_conteo', groupBy: 'rango' }, result(alike), [indicator('teatro_conteo', { columns: { rango: { stripPrefix: true } } })]);

    expect(groupsOf(context)).toEqual(['1. Otro', '6. Otro', 'Sí']);
  });

  it('keeps the numbers when the column hides nothing (Numbered labels kept)', async () => {
    const context = await show('bar', { ...settings, layout: 'percent' }, numbered(), conteo({ respuesta: { label: 'Respuesta' } }));

    expect(legend(context)).toEqual(['1. Sí', '2. Tal vez', '3. No', '4. No sabe']);
  });
});

describe('colors follow a declared category order (design D10)', () => {
  afterEach(clearOverlay);

  /** The color token a mark is drawn in: the `var(--tmz-…)` of its style, whichever property holds it (fill, stroke, background). */
  const colorOf = (element: Element | null) => /var\(--[\w-]+\)/.exec(element?.getAttribute('style') ?? '')?.[0] ?? null;
  const palette = (name: PaletteId, valueColors: ValueColor[] = []): DashboardColors => ({ palette: name, valueColors });
  const pal = (n: number) => `var(--tmz-pal-tamiza-${n})`;
  const sources = [indicator('households_by_source', { columns: { water_source: { order: ['Well', 'Piped', 'River'] } } })];
  const settings = { indicator: 'households_by_source', groupBy: 'municipality', seriesBy: 'water_source', measure: 'value', layout: 'stacked' } as const;
  const answer = () => seriesResult([cell('Cali', 'Piped', 10), cell('Cali', 'River', 5), cell('Cali', 'Well', 20)], ['Piped', 'River', 'Well']);

  it('hands the palette out in the order of the series (Colors follow the order)', async () => {
    const context = await show('column', settings, answer(), sources, palette('tamiza'));

    expect(legend(context)).toEqual(['Well', 'Piped', 'River']);
    expect(marks(context).map(colorOf)).toEqual([pal(1), pal(2), pal(3)]);
  });

  it('keeps the color kept for a value whatever the order (Color kept whatever the order)', async () => {
    const own: Partial<WidgetConfig> = { ...settings, display: { order: { series: ['River', 'Well', 'Piped'] } } };

    const context = await show('column', own, answer(), sources, palette('tamiza', [{ column: 'water_source', value: 'Piped', color: 7 }]));

    expect(legend(context)).toEqual(['River', 'Well', 'Piped']);
    expect(marks(context).map(colorOf)).toEqual([pal(1), pal(2), pal(7)]);
  });

  it('keeps the color of the full value when the numbers are hidden (Color kept for the full value)', async () => {
    const numbered = [indicator('households_by_source', { columns: { water_source: { stripPrefix: true } } })];
    const cells = seriesResult([cell('Cali', '1. Piped', 10), cell('Cali', '2. Well', 20)], ['1. Piped', '2. Well']);

    const context = await show('column', settings, cells, numbered, palette('tamiza', [{ column: 'water_source', value: '2. Well', color: 7 }]));

    expect(legend(context)).toEqual(['Piped', 'Well']);
    expect(marks(context).map(colorOf)).toEqual([pal(1), pal(7)]);
  });

  it('colors the slices of a pie in the order the widget declares (Slices colored in their declared order)', async () => {
    const visits = [row('Piped', 60), row('Well', 30), row('River', 10)];
    const own: Partial<WidgetConfig> = { indicator: 'households_visited', groupBy: 'water_source', display: { order: { group: ['River', 'Well', 'Piped'] } } };

    const context = await show('pie', own, result(visits, { domain: [{ value: 'Piped', kind: 'value' }, { value: 'Well', kind: 'value' }, { value: 'River', kind: 'value' }] }), [
      indicator('households_visited'),
    ]);

    expect(titles(all(context.root, 'pie-slice')).map((t) => t.split(':')[0])).toEqual(['River', 'Well', 'Piped']);
    expect(all(context.root, 'pie-slice').map(colorOf)).toEqual([pal(1), pal(2), pal(3)]);
  });

  it('keeps the color of a bar whatever its place (Bars keep their color)', async () => {
    const places = [row('Almacenes del barrio', 10), row('Centro de Duitama', 30), row('Vendedores ambulantes', 20)];
    const own: Partial<WidgetConfig> = { ...byPlace, display: { color: 2, order: { group: ['Vendedores ambulantes', 'Centro de Duitama'] } } };

    const context = await show('column', own, result(places), stores(undefined), palette('tamiza', [{ column: 'categoria', value: 'Centro de Duitama', color: 1 }]));

    expect(groupsOf(context)).toEqual(['Vendedores ambulantes', 'Centro de Duitama', 'Almacenes del barrio']);
    expect(marks(context).map(colorOf)).toEqual([pal(2), pal(1), pal(2)]);
  });
});

describe('the keyboard follows a declared order (design D4)', () => {
  afterEach(clearOverlay);

  const spoken = (context: Context) => text(byTestId(context.root, 'marks-announcement')).replaceAll('​', '');
  const conteo = [indicator('teatro_conteo', { columns: { comuna: { order: ['COMUNA 2', 'COMUNA 1'] }, respuesta: { order: ['3. No', '1. Sí'], stripPrefix: true } } })];
  const settings = { indicator: 'teatro_conteo', groupBy: 'comuna', seriesBy: 'respuesta', measure: 'value' } as const;
  const answer = () =>
    seriesResult(
      [cell('COMUNA 1', '1. Sí', 30), cell('COMUNA 1', '3. No', 10), cell('COMUNA 2', '1. Sí', 20), cell('COMUNA 2', '3. No', 25)],
      ['1. Sí', '3. No'],
    );

  it('steps through the cells of a heat map in the order of its rows and columns (Keyboard order)', async () => {
    const context = await show('heatmap', settings, answer(), conteo);
    const chart = byTestId(context.root, 'chart-svg')!;

    chart.focus();
    pressKey(chart, 'Enter');
    await settle(context.harness);
    expect(spoken(context)).toBe('COMUNA 2. No: 25');

    pressKey(chart, 'ArrowRight');
    await settle(context.harness);
    expect(spoken(context)).toBe('COMUNA 2. Sí: 20');

    pressKey(chart, 'ArrowDown');
    await settle(context.harness);
    expect(spoken(context)).toBe('COMUNA 1. Sí: 30');
  });

  it('steps through the segments of a stacked column chart in the order of its series', async () => {
    const context = await show('column', { ...settings, layout: 'stacked' }, answer(), conteo);
    const chart = byTestId(context.root, 'chart-svg')!;

    chart.focus();
    pressKey(chart, 'Enter');
    await settle(context.harness);
    const first = spoken(context);
    pressKey(chart, 'ArrowDown');
    await settle(context.harness);
    const second = spoken(context);

    expect(first).toContain('COMUNA 2');
    expect(first).toContain('No');
    expect(second).toContain('COMUNA 2');
    expect(second).toContain('Sí');
  });
});
