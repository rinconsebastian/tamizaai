import { byTestId, settle } from '../../../../testing/project-harness';
import { pressKey } from '../../../../testing/cdk';
import { ChartType, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { Context, all, clearOverlay, config, indicator, isQuery, open, overlay, result, row, text, widget } from './indicators-testing';

// The tooltips of the charts with no series (add-small-sample-warnings, design D9, D10): a bar, a column, a point of a line, a bar of a
// histogram and a slice of a pie give the tooltip and the keyboard a chart with a series gives, with the base figures of their rows.

const ratio = { numerator: 'numerator', denominator: 'denominator' };
const teatro = indicator('teatro_hoy', {
  label: 'Teatro hoy',
  format: 'percent',
  columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' } },
});

/** The 16 and 121 households of two comunas, as "2.1. Asistió, por comuna" has them. */
const comunas = [
  row('COMUNA 7', 0.5, null, { numerator: 8, denominator: 16, n: 16 }),
  row('COMUNA 1', 56 / 121, null, { numerator: 56, denominator: 121, n: 121 }),
  row('SIN NOMBRE', 0.25, null, { numerator: 5, denominator: 20, n: 20 }),
];

async function show(chartType: ChartType, settings: Partial<WidgetConfig>, answer: QueryResult, indicators = [teatro], at: Parameters<typeof open>[4] = {}) {
  const context = await open('viewer', [widget('w1', { chartType, config: config(settings) })], null, { indicators }, at);
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(answer);
  await settle(context.harness);
  return context;
}

const chartOf = (context: Context) => byTestId(context.root, 'chart-svg') as unknown as HTMLElement;
const hits = (context: Context) => all(context.root, 'chart-hit');
const tooltipHeading = () => text(overlay().querySelector('[data-testid="tooltip-heading"]'));
const tooltipLines = () => Array.from(overlay().querySelectorAll('[data-testid="tooltip-line"]')).map(text);
const tooltipFooter = () => text(overlay().querySelector('[data-testid="tooltip-footer"]'));
const spoken = (context: Context) => text(byTestId(context.root, 'marks-announcement')).replaceAll('​', '');
const isOpen = () => !!overlay().querySelector('[data-testid="chart-tooltip"]');

async function press(context: Context, key: string) {
  pressKey(chartOf(context), key);
  await settle(context.harness);
}

async function hover(context: Context, target: Element) {
  target.dispatchEvent(new MouseEvent('pointerenter', { clientX: 40, clientY: 30 }));
  await settle(context.harness);
}

/** A touch on a mark: the pointer event of a finger, which a mouse is not. */
async function tap(context: Context, target: Element) {
  target.dispatchEvent(Object.assign(new MouseEvent('pointerdown', { clientX: 40, clientY: 30, bubbles: true }), { pointerType: 'touch' }));
  await settle(context.harness);
}

describe('mark tooltips of charts with no series (add-small-sample-warnings, design D9)', () => {
  afterEach(clearOverlay);

  describe('column chart', () => {
    const settings = { indicator: 'teatro_hoy', groupBy: 'comuna', ratio };

    it('gives a column its group, its value and its numerator and denominator under their names (Column tooltip without a series)', async () => {
      const context = await show('column', settings, result(comunas));

      await hover(context, hits(context)[0]);

      expect(tooltipHeading()).toBe('COMUNA 7');
      expect(tooltipLines()).toEqual(['Teatro hoy: 50%', 'Hogares que asistieron: 8', 'Hogares: 16']);
    });

    it('gives the rate of a comuna with no n line when the n is the denominator (Rate of a comuna)', async () => {
      const context = await show('column', settings, result(comunas));

      await hover(context, hits(context)[1]);

      expect(tooltipHeading()).toBe('COMUNA 1');
      expect(tooltipLines()).toEqual(['Teatro hoy: 46.28%', 'Hogares que asistieron: 56', 'Hogares: 121']);
    });

    it('writes the figures as counts, whatever the indicator\'s format and unit (Figures of a percent indicator)', async () => {
      const percent = indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', unit: 'de los hogares', columns: teatro.columns });
      const context = await show('column', settings, result(comunas), [percent]);

      await hover(context, hits(context)[1]);

      const [value, numerator, denominator] = tooltipLines();
      expect(value).toBe('Teatro hoy: 46.28% de los hogares');
      expect(numerator).toBe('Hogares que asistieron: 56');
      expect(denominator).toBe('Hogares: 121');
      expect(`${numerator}${denominator}`).not.toMatch(/%|5,600|de los hogares/);
    });

    it('gives the n under the name of the declared base column when it is not the denominator (Declared base in a tooltip)', async () => {
      const gastos = indicator('comercio_gastos_resumen', { label: 'Gasto', format: 'count', columns: { hogares: { label: 'hogares', base: true } } });
      const context = await show('column', { indicator: 'comercio_gastos_resumen', groupBy: 'estrato' }, result([row('Estrato 2', 1200000, null, { n: 154 })]), [gastos]);

      await hover(context, hits(context)[0]);

      expect(tooltipHeading()).toBe('Estrato 2');
      expect(tooltipLines()).toEqual(['Gasto: 1,200,000', 'n (hogares): 154']);
    });

    it('gives the value only for a mark that has no figures, as the script editor\'s test view does', async () => {
      const context = await show('column', { indicator: 'coverage', groupBy: 'municipality' }, result([row('Cali', 75)]), [indicator('coverage')]);

      await hover(context, hits(context)[0]);

      expect(tooltipHeading()).toBe('Cali');
      expect(tooltipLines()).toEqual(['Coverage: 75']);
      expect(tooltipFooter()).toBe('');
    });

    it('names the value after the own caption text of the widget when it has one', async () => {
      const context = await show('column', { ...settings, display: { caption: { text: '% de quienes asistieron' } } }, result(comunas));

      await hover(context, hits(context)[0]);

      expect(tooltipLines()[0]).toBe('% de quienes asistieron: 50%');
    });

    it('is one Tab stop that steps from column to column with the arrows, shows each tooltip and announces it (Stepping through columns)', async () => {
      const context = await show('column', settings, result(comunas));
      const chart = chartOf(context);
      expect(chart.getAttribute('role')).toBe('application');
      expect(chart.getAttribute('tabindex')).toBe('0');
      expect(chart.getAttribute('aria-roledescription')).toBe('chart');
      expect(text(document.getElementById(chart.getAttribute('aria-describedby')!))).toBe(
        'Left and right arrows move between the groups. Home and End go to the first and the last, and Escape leaves the marks.',
      );

      chart.focus();
      await press(context, 'ArrowRight');
      await press(context, 'ArrowRight');

      expect(tooltipHeading()).toBe('COMUNA 1');
      expect(spoken(context)).toBe('COMUNA 1. Teatro hoy: 46.28%. Hogares que asistieron: 56. Hogares: 121');
      expect(all(context.root, 'chart-mark').map((mark) => mark.classList.contains('stroke-heading'))).toEqual([false, true, false]);
    });

    it('goes to the first and the last column with Home and End, and leaves the marks with Escape', async () => {
      const context = await show('column', settings, result(comunas));
      const chart = chartOf(context);
      chart.focus();

      await press(context, 'End');
      expect(tooltipHeading()).toBe('SIN NOMBRE');
      await press(context, 'Home');
      expect(tooltipHeading()).toBe('COMUNA 7');
      await press(context, 'Escape');

      expect(isOpen()).toBe(false);
      expect(document.activeElement).toBe(chart);
    });

    it('says again a mark it is told again, and stops at the ends', async () => {
      const context = await show('column', settings, result(comunas));
      chartOf(context).focus();

      await press(context, 'End');
      await press(context, 'ArrowRight');

      expect(tooltipHeading()).toBe('SIN NOMBRE');
    });
  });

  describe('bar chart', () => {
    it('runs the groups down: the down arrow reaches the first bar and the next ones (Bars run down)', async () => {
      const context = await show('bar', { indicator: 'teatro_hoy', groupBy: 'comuna', ratio }, result(comunas));
      const chart = chartOf(context);
      expect(text(document.getElementById(chart.getAttribute('aria-describedby')!))).toBe(
        'Up and down arrows move between the groups. Home and End go to the first and the last, and Escape leaves the marks.',
      );

      chart.focus();
      await press(context, 'ArrowDown');
      expect(tooltipHeading()).toBe('COMUNA 7');
      expect(all(context.root, 'chart-mark')[0].classList.contains('stroke-heading')).toBe(true);
      await press(context, 'ArrowDown');
      expect(tooltipHeading()).toBe('COMUNA 1');
      await press(context, 'ArrowUp');
      expect(tooltipHeading()).toBe('COMUNA 7');
    });

    it('leaves the arrows across alone: a bar chart with no series has no series to move between', async () => {
      const context = await show('bar', { indicator: 'teatro_hoy', groupBy: 'comuna', ratio }, result(comunas));
      chartOf(context).focus();

      await press(context, 'ArrowDown');
      await press(context, 'ArrowRight');

      expect(tooltipHeading()).toBe('COMUNA 7');
    });
  });

  describe('line chart', () => {
    const rounds = [row('1', 0.6, null, { numerator: 6, denominator: 10, n: 10 }), row('2', 0.7, null, { numerator: 14, denominator: 20, n: 20 }), row('3', 0.8, null, { numerator: 24, denominator: 30, n: 30 })];

    it('gives each point its tooltip on hover, with a band around the point as its hit area', async () => {
      const context = await show('line', { indicator: 'teatro_hoy', groupBy: 'ronda', ratio }, result(rounds));
      expect(hits(context)).toHaveLength(3);
      expect(all(context.root, 'chart-dot')).toHaveLength(3);

      await hover(context, hits(context)[1]);

      expect(tooltipHeading()).toBe('2');
      expect(tooltipLines()).toEqual(['Teatro hoy: 70%', 'Hogares que asistieron: 14', 'Hogares: 20']);
      expect(all(context.root, 'chart-guide')).toHaveLength(1);
    });

    it('steps from point to point with the arrows and says each', async () => {
      const context = await show('line', { indicator: 'teatro_hoy', groupBy: 'ronda', ratio }, result(rounds));
      chartOf(context).focus();

      await press(context, 'ArrowRight');
      await press(context, 'ArrowRight');
      await press(context, 'ArrowRight');

      expect(spoken(context)).toBe('3. Teatro hoy: 80%. Hogares que asistieron: 24. Hogares: 30');
      expect(text(document.getElementById(chartOf(context).getAttribute('aria-describedby')!))).toContain('Left and right arrows move between the groups');
    });
  });

  describe('histogram', () => {
    const values = [1, 2, 3, 4, 5, 6, 7, 8].map((value) => row(null, value));

    it('gives a bar its range and how many rows fall in it, and no numerator, denominator or n (Histogram bar)', async () => {
      const context = await show('histogram', { indicator: 'coverage', buckets: 4, groupBy: null }, result(values), [indicator('coverage')]);
      expect(hits(context)).toHaveLength(4);

      await hover(context, hits(context)[0]);

      expect(tooltipHeading()).toBe('1–2.75');
      expect(tooltipLines()).toEqual(['Rows: 2']);
      expect(tooltipFooter()).toBe('');
    });

    it('gives a histogram of ratio rows the same: its bars have no n, whatever the rows hold', async () => {
      const withFigures = values.map((value) => ({ ...value, numerator: 1, denominator: 2, n: 2 }));
      const context = await show('histogram', { indicator: 'teatro_hoy', buckets: 4, groupBy: null }, result(withFigures));

      await hover(context, hits(context)[3]);

      expect(tooltipLines()).toEqual(['Rows: 2']);
    });
  });

  describe('pie chart', () => {
    const slices = [
      row('Culturama (escuelas de formación)', 0.6, null, { numerator: 60, denominator: 100, n: 100 }),
      row('Teatro', 0.3, null, { numerator: 30, denominator: 100, n: 100 }),
      row('Cine', 0.1, null, { numerator: 10, denominator: 100, n: 100 }),
    ];
    const settings = { indicator: 'teatro_hoy', groupBy: 'categoria', ratio };

    it('keeps the tooltip of a tapped slice open until the next tap elsewhere (Tapping a slice)', async () => {
      const context = await show('pie', settings, result(slices));
      const slice = all(context.root, 'pie-slice')[0];

      await tap(context, slice);
      expect(tooltipHeading()).toBe('Culturama (escuelas de formación)');
      slice.dispatchEvent(new MouseEvent('pointerleave'));
      await settle(context.harness);
      expect(isOpen()).toBe(true);

      document.body.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
      await settle(context.harness);
      expect(isOpen()).toBe(false);
    });

    it('gives a slice its value, its share and its figures under their names', async () => {
      const context = await show('pie', settings, result(slices));

      await hover(context, all(context.root, 'pie-slice')[1]);

      expect(tooltipHeading()).toBe('Teatro');
      expect(tooltipLines()).toEqual(['Teatro hoy: 30%', 'Share: 30%', 'Hogares que asistieron: 30', 'Hogares: 100']);
    });

    it('is one Tab stop that steps from slice to slice with the arrows, the largest first (Stepping through slices)', async () => {
      const context = await show('pie', settings, result(slices));
      const chart = chartOf(context);
      expect(chart.getAttribute('role')).toBe('application');
      expect(chart.getAttribute('tabindex')).toBe('0');
      expect(text(document.getElementById(chart.getAttribute('aria-describedby')!))).toContain('Left and right arrows move between the groups');

      chart.focus();
      await press(context, 'ArrowRight');
      expect(tooltipHeading()).toBe('Culturama (escuelas de formación)');
      await press(context, 'ArrowRight');
      expect(tooltipHeading()).toBe('Teatro');
      expect(all(context.root, 'pie-slice').map((slice) => slice.classList.contains('stroke-heading'))).toEqual([false, true, false]);
      await press(context, 'End');
      expect(tooltipHeading()).toBe('Cine');
      await press(context, 'Escape');
      expect(isOpen()).toBe(false);
    });

    it('gives the "Other" slice no figures and no n (Other slice)', async () => {
      const many = Array.from({ length: 10 }, (_, i) => row(`G${i + 1}`, 100 - i * 5, null, { numerator: 10, denominator: 20, n: 20 }));
      const context = await show('pie', { indicator: 'teatro_hoy', groupBy: 'categoria', ratio }, result(many));

      const other = all(context.root, 'pie-slice').at(-1)!;
      await hover(context, other);

      expect(tooltipHeading()).toBe('Other');
      expect(tooltipLines().map((line) => line.split(':')[0])).toEqual(['Teatro hoy', 'Share']);
    });

    it('anchors the tooltip of a pie of a single group, a whole circle, at its center', async () => {
      const context = await show('pie', settings, result([slices[0]]));

      chartOf(context).focus();
      await press(context, 'ArrowRight');

      expect(tooltipHeading()).toBe('Culturama (escuelas de formación)');
      expect(all(context.root, 'pie-slice')[0].tagName.toLowerCase()).toBe('circle');
    });
  });
});

describe('the overall line over answers (add-small-sample-warnings, design D16)', () => {
  afterEach(clearOverlay);

  const lines = [{ kind: 'mean' as const }, { kind: 'overall' as const }];
  const places = [
    row('Teatro', 0.3, null, { numerator: 30, denominator: 100, n: 100 }),
    row('Cine', 0.5, null, { numerator: 50, denominator: 100, n: 100 }),
    row('Museo', 0.7, null, { numerator: 70, denominator: 100, n: 100 }),
  ];
  const reference = (context: Context) => all(context.root, 'reference-line');

  async function showPlaces(columns: NonNullable<typeof teatro.columns>, settings: Partial<WidgetConfig> = {}) {
    const entry = indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', columns });
    const context = await open('viewer', [widget('w1', { chartType: 'bar', config: config({ indicator: 'teatro_hoy', ratio, groupBy: 'categoria', display: { referenceLines: lines }, ...settings }) })], null, { indicators: [entry] });
    const requests = context.http.match(isQuery);
    requests[0].flush(result(places, { overall: 0.5 }));
    await settle(context.harness);
    return context;
  }

  it('draws the mean line only, and says under the chart that its bars are the answers of one question (Overall line over places)', async () => {
    const context = await showPlaces({ categoria: { categories: 'single' } });

    expect(reference(context)).toHaveLength(1);
    expect(all(context.root, 'reference-label').map(text)).toEqual(['Mean: 50%']);
    expect(text(byTestId(context.root, 'overall-note'))).toBe('No overall line: the bars are the answers of one question.');
  });

  it('draws both lines and says nothing when the indicator declares no answer column (Overall line over comunas)', async () => {
    const context = await showPlaces({ otra: { categories: 'single' } });

    expect(reference(context)).toHaveLength(2);
    expect(byTestId(context.root, 'overall-note')).toBeNull();
  });

  it('draws it for a chart grouped by a column that is not an answer column, whatever else the indicator declares', async () => {
    const context = await showPlaces({ categoria: { categories: 'single' } }, { groupBy: 'comuna' });

    expect(reference(context)).toHaveLength(2);
    expect(byTestId(context.root, 'overall-note')).toBeNull();
  });

  it('says nothing about a widget that has no overall line to leave out', async () => {
    const context = await showPlaces({ categoria: { categories: 'single' } }, { display: { referenceLines: [{ kind: 'mean' }] } });

    expect(reference(context)).toHaveLength(1);
    expect(byTestId(context.root, 'overall-note')).toBeNull();
  });
});
