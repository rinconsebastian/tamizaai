import { TestBed } from '@angular/core/testing';
import { byTestId, settle } from '../../../../testing/project-harness';
import { ChartType, QueryResult, QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { Context, all, cell, clearOverlay, config, indicator, isQuery, movedElements, open, overlay, result, row, seriesResult, text, widget } from './indicators-testing';

// The figures and the small bases of every chart (add-small-sample-warnings, design D9, D10): the widget sets its own threshold in its display,
// which wins over the dashboard's, so each case is one widget over an indicator that publishes the figures of its rows.

const ratio = { numerator: 'numerator', denominator: 'denominator' };
const teatro = indicator('teatro_hoy', {
  label: 'Teatro hoy',
  format: 'percent',
  columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' } },
});

/** A ratio cell of a long table: the figures its rows sum to. */
function figured(group: string | null, series: string, numerator: number, denominator: number, n = denominator): QueryRow {
  return { ...cell(group, series, numerator / denominator), numerator, denominator, n };
}

/** The answer of a widget with a series, whose groups carry their n as the totals of the API do. */
function answerOf(cells: QueryRow[], series: string[], groupN: Record<string, number> = {}): QueryResult {
  const answer = seriesResult(cells, series);
  answer.totals!.groups.forEach((group) => {
    if (group.group !== null && group.group in groupN) {
      group.n = groupN[group.group];
    }
  });
  return answer;
}

/** What the marks of a chart look like, read before the page is opened again for the same chart without a threshold. */
function look(context: Context) {
  return {
    heights: all(context.root, 'chart-mark').map((mark) => mark.getAttribute('height')),
    fills: all(context.root, 'chart-mark').map((mark) => mark.getAttribute('style')?.match(/fill: (var\([^)]*\))/)?.[1]),
    opacities: all(context.root, 'chart-mark').map((mark) => mark.getAttribute('fill-opacity')),
    dashes: all(context.root, 'chart-mark').map((mark) => mark.getAttribute('stroke-dasharray')),
    label: byTestId(context.root, 'chart-svg')!.getAttribute('aria-label')!,
  };
}

async function show(chartType: ChartType, settings: Partial<WidgetConfig>, answer: QueryResult) {
  const context = await open('viewer', [widget('w1', { chartType, config: config({ indicator: 'teatro_hoy', ratio, ...settings }) })], null, { indicators: [teatro] });
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(answer);
  await settle(context.harness);
  return context;
}

const chartOf = (context: Context) => byTestId(context.root, 'chart-svg') as unknown as HTMLElement;
const marks = (context: Context) => all(context.root, 'chart-mark');
const hits = (context: Context) => all(context.root, 'chart-hit');
const tooltipHeading = () => text(overlay().querySelector('[data-testid="tooltip-heading"]'));
const tooltipLines = () => Array.from(overlay().querySelectorAll('[data-testid="tooltip-line"]')).map(text);
const tooltipFooter = () => text(overlay().querySelector('[data-testid="tooltip-footer"]'));
const label = (context: Context) => chartOf(context).getAttribute('aria-label')!;
const opacities = (context: Context) => marks(context).map((mark) => mark.getAttribute('fill-opacity'));
const dashes = (context: Context) => marks(context).map((mark) => mark.getAttribute('stroke-dasharray'));

async function hover(context: Context, target: Element) {
  target.dispatchEvent(new MouseEvent('pointerenter', { clientX: 40, clientY: 30 }));
  await settle(context.harness);
}

/** COMUNA 7 rests on 16 households, COMUNA 1 on 121 and SIN NOMBRE on 20: the study's own figures. */
const comunas = () => [figured('COMUNA 7', 'Sí', 8, 16), figured('COMUNA 1', 'Sí', 56, 121), figured('SIN NOMBRE', 'Sí', 5, 20)];

describe('the figures and small bases of the charts with a series (add-small-sample-warnings, design D9, D10)', () => {
  afterEach(clearOverlay);

  describe('grouped column chart', () => {
    const settings = { groupBy: 'comuna', seriesBy: 'categoria', layout: 'grouped' as const, display: { minN: 30 } };
    // The marks are in the order of the groups, then of the series: COMUNA 7 (Sí, No), COMUNA 1 (Sí, No) and SIN NOMBRE (Sí).
    const answer = () => answerOf([...comunas(), figured('COMUNA 7', 'No', 8, 16), figured('COMUNA 1', 'No', 65, 121)], ['Sí', 'No']);

    it('gives a cell its value, its numerator and denominator and the note of a small base (Cell tooltip with its base)', async () => {
      const context = await show('column', settings, answer());

      await hover(context, hits(context)[0]);

      expect(tooltipHeading()).toBe('COMUNA 7');
      expect(tooltipLines()).toEqual(['Sí: 50%', 'Hogares que asistieron: 8', 'Hogares: 16']);
      expect(tooltipFooter()).toBe('Small base: n = 16 (below 30)');
    });

    it('gives a cell of a large base no note', async () => {
      const context = await show('column', settings, answer());

      await hover(context, hits(context)[2]);

      expect(tooltipHeading()).toBe('COMUNA 1');
      expect(tooltipLines()).toEqual(['Sí: 46.28%', 'Hogares que asistieron: 56', 'Hogares: 121']);
      expect(tooltipFooter()).toBe('');
    });

    it('draws the small cells lighter with a dashed outline in their own color, and keeps their height (Columns dimmed)', async () => {
      const dimmed = await show('column', settings, answer());
      const seen = look(dimmed);

      expect(seen.opacities).toEqual(['0.35', '0.35', null, null, '0.35']);
      expect(seen.dashes).toEqual(['3 2', '3 2', null, null, '3 2']);
      expect(marks(dimmed)[0].getAttribute('stroke-width')).toBe('1.5');
      // The outline is in the cell's own color.
      expect(marks(dimmed)[0].getAttribute('style')).toContain(`stroke: ${seen.fills[0]}`);
      expect(seen.label).toContain(', 3 with a small base');

      // The same chart with no threshold: the same columns at the same height and color, none of them dimmed, and nothing in its summary.
      clearOverlay();
      TestBed.resetTestingModule();
      const plain = look(await show('column', { ...settings, display: {} }, answer()));
      expect(plain.heights).toEqual(seen.heights);
      expect(plain.fills).toEqual(seen.fills);
      expect(plain.opacities.every((value) => value === null)).toBe(true);
      expect(plain.dashes.every((value) => value === null)).toBe(true);
      expect(plain.label).not.toContain('small base');
    });

    it('dims nothing when no threshold is in force, or when no base is below it (Nothing dimmed)', async () => {
      const wide = await show('column', { ...settings, display: { minN: 10 } }, answer());

      expect(opacities(wide).every((value) => value === null)).toBe(true);
      expect(label(wide)).not.toContain('small base');
    });

    it('turns the dimming off for a widget whose own threshold is 0, whatever it says elsewhere', async () => {
      const context = await show('column', { ...settings, display: { minN: 0 } }, answer());

      expect(opacities(context).every((value) => value === null)).toBe(true);
    });
  });

  describe('chart stacked to 100%', () => {
    it('gives each segment the n of its group, since a share is over its group (Share of a group)', async () => {
      const cells = [
        { ...cell('COMUNA 7', 'Sí', 10), n: 16 },
        { ...cell('COMUNA 7', 'No', 6), n: 16 },
        { ...cell('COMUNA 1', 'Sí', 60), n: 121 },
        { ...cell('COMUNA 1', 'No', 61), n: 121 },
      ];
      const context = await show('column', { groupBy: 'comuna', seriesBy: 'categoria', layout: 'percent', ratio: null, measure: 'value', display: { minN: 30 } }, answerOf(cells, ['Sí', 'No'], { 'COMUNA 7': 16, 'COMUNA 1': 121 }));

      expect(opacities(context)).toEqual(['0.35', '0.35', null, null]);
      await hover(context, hits(context)[0]);
      expect(tooltipLines()).toEqual(['Sí: 62.5%', 'Teatro hoy: value: 1,000%', 'n: 16']);
      expect(tooltipFooter()).toBe('Small base: n = 16 (below 30)');
    });
  });

  describe('line chart with a series', () => {
    const answer = () => answerOf([figured('1', 'Sí', 4, 10), figured('1', 'No', 6, 10), figured('2', 'Sí', 60, 100), figured('2', 'No', 40, 100)], ['Sí', 'No']);

    it('writes the n of each series in the tooltip of a group, and one note when any has a small base', async () => {
      const context = await show('line', { groupBy: 'ronda', seriesBy: 'categoria', ratio, display: { minN: 30 } }, answer());

      await hover(context, hits(context)[0]);
      expect(tooltipLines()).toEqual(['Sí: 40% (n = 10)', 'No: 60% (n = 10)']);
      expect(tooltipFooter()).toBe('Small base in some series (below 30)');
      await hover(context, hits(context)[1]);
      expect(tooltipLines()).toEqual(['Sí: 60% (n = 100)', 'No: 40% (n = 100)']);
      expect(tooltipFooter()).toBe('');
    });

    it('draws the points with a small base hollow with a dashed ring, and the line through them as it was', async () => {
      const context = await show('line', { groupBy: 'ronda', seriesBy: 'categoria', ratio, display: { minN: 30 } }, answer());

      const dots = all(context.root, 'chart-dot');
      expect(dots).toHaveLength(4);
      // The points are drawn series by series: the first group of each series has the small base.
      expect(dots.map((dot) => dot.getAttribute('data-small'))).toEqual(['true', null, 'true', null]);
      expect(dots[0].getAttribute('stroke-dasharray')).toBe('2 2');
      expect(dots[0].getAttribute('style')).toContain('fill: none');
      expect(all(context.root, 'chart-line')).toHaveLength(2);
      expect(label(context)).toContain(', 2 with a small base');
    });
  });

  describe('heat map', () => {
    const cells = () => [figured('A', 'x', 1, 10), figured('A', 'y', 50, 100), figured('B', 'x', 40, 100)];

    it('keeps the shade of a small cell and hatches it with a dashed outline, in a pattern of its own (Heat map cell)', async () => {
      const plain = await show('heatmap', { groupBy: 'comuna', seriesBy: 'categoria' }, answerOf(cells(), ['x', 'y']));
      const plainFills = marks(plain).map((mark) => mark.getAttribute('style'));
      clearOverlay();
      TestBed.resetTestingModule();
      const dimmed = await show('heatmap', { groupBy: 'comuna', seriesBy: 'categoria', display: { minN: 30 } }, answerOf(cells(), ['x', 'y']));

      expect(marks(dimmed).map((mark) => mark.getAttribute('style'))).toEqual(plainFills);
      const small = all(dimmed.root, 'heat-small');
      expect(small).toHaveLength(1);
      const pattern = small[0].getAttribute('fill')!.match(/url\(#(.+)\)/)![1];
      expect(small[0].getAttribute('stroke-dasharray')).toBe('3 2');
      expect(small[0].classList).toContain('stroke-heading');
      // One diagonal from the top left corner to the bottom right one, drawn as a path and never rotated.
      const hatch = chartOf(dimmed).querySelector(`pattern[id="${pattern}"]`)!;
      expect(hatch.getAttribute('width')).toBe('6');
      expect(hatch.getAttribute('height')).toBe('6');
      expect(hatch.hasAttribute('patternTransform')).toBe(false);
      expect(hatch.querySelector('path')!.getAttribute('d')).toBe('M0 0 L6 6');
      expect(hatch.querySelector('path')!.classList).toContain('stroke-heading');
      // Not the empty cell's pattern: that one is rotated 45 degrees and drawn with a line.
      const empty = chartOf(dimmed).querySelector('pattern[patternTransform]');
      expect(empty?.getAttribute('id')).not.toBe(pattern);
      expect(label(dimmed)).toContain(', 1 with a small base');
    });

    it('gives a pattern an id of its own in each chart, so two charts on one page never mix them up', async () => {
      const context = await open(
        'viewer',
        ['w1', 'w2'].map((id) => widget(id, { chartType: 'heatmap', config: config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', seriesBy: 'categoria', display: { minN: 30 } }) })),
        null,
        { indicators: [teatro] },
      );
      for (const request of context.http.match(isQuery)) {
        request.flush(answerOf(cells(), ['x', 'y']));
      }
      await settle(context.harness);

      const ids = Array.from(context.root.querySelectorAll('[data-testid="chart-svg"] pattern')).map((pattern) => pattern.id);
      expect(ids).toHaveLength(4);
      expect(new Set(ids).size).toBe(4);
    });

    it('says in the tooltip of a small cell that its base is small, with the figures first', async () => {
      const context = await show('heatmap', { groupBy: 'comuna', seriesBy: 'categoria', display: { minN: 30 } }, answerOf(cells(), ['x', 'y']));

      await hover(context, hits(context)[0]);

      expect(tooltipHeading()).toBe('A');
      expect(tooltipLines()).toEqual(['x: 10%', 'Hogares que asistieron: 1', 'Hogares: 10']);
      expect(tooltipFooter()).toBe('Small base: n = 10 (below 30)');
    });
  });

  describe('population pyramid and Likert bars', () => {
    it('draws the bars of a small base dimmed, and says so in their tooltips', async () => {
      const cells = [figured('0-4', 'Female', 3, 10), figured('0-4', 'Male', 40, 100), figured('5-9', 'Female', 50, 100), figured('5-9', 'Male', 20, 100)];
      const context = await show('pyramid', { groupBy: 'edad', seriesBy: 'sexo', pyramid: { left: 'Male', right: 'Female' }, display: { minN: 30 } }, answerOf(cells, ['Male', 'Female']));

      // Drawn from the bottom: the last group first.
      expect(marks(context)).toHaveLength(4);
      expect(opacities(context).filter((value) => value === '0.35')).toHaveLength(1);
      const dimmedIndex = opacities(context).indexOf('0.35');
      await hover(context, hits(context)[dimmedIndex]);
      expect(tooltipFooter()).toBe('Small base: n = 10 (below 30)');
      expect(label(context)).toContain(', 1 with a small base');
    });

    it('gives a Likert segment the n of its group, so a whole bar is dimmed or none of it is', async () => {
      const cells = [
        { ...cell('Armenia', 'Agree', 30), n: 16 },
        { ...cell('Armenia', 'Disagree', 10), n: 16 },
        { ...cell('Buga', 'Agree', 30), n: 90 },
        { ...cell('Buga', 'Disagree', 10), n: 90 },
      ];
      const scale = [
        { value: 'Disagree', side: 'negative' as const },
        { value: 'Agree', side: 'positive' as const },
      ];
      const context = await show('likert', { groupBy: 'comuna', seriesBy: 'categoria', ratio: null, measure: 'value', scale, display: { minN: 30 } }, answerOf(cells, ['Disagree', 'Agree'], { Armenia: 16, Buga: 90 }));

      expect(opacities(context)).toEqual(['0.35', '0.35', null, null]);
      await hover(context, hits(context)[0]);
      expect(tooltipFooter()).toContain('Small base: n = 16 (below 30)');
    });
  });

  describe('charts with no series', () => {
    const rows = () => [
      row('COMUNA 7', 0.5, null, { numerator: 8, denominator: 16, n: 16 }),
      row('COMUNA 1', 56 / 121, null, { numerator: 56, denominator: 121, n: 121 }),
      row('SIN NOMBRE', 0.25, null, { numerator: 5, denominator: 20, n: 20 }),
    ];

    it('draws COMUNA 7 and SIN NOMBRE lighter with a dashed outline, and keeps their height (Columns dimmed)', async () => {
      const dimmed = look(await show('column', { groupBy: 'comuna', display: { minN: 30 } }, result(rows())));
      clearOverlay();
      TestBed.resetTestingModule();
      const plain = look(await show('column', { groupBy: 'comuna' }, result(rows())));

      expect(dimmed.opacities).toEqual(['0.35', null, '0.35']);
      expect(dimmed.dashes).toEqual(['3 2', null, '3 2']);
      expect(dimmed.heights).toEqual(plain.heights);
      expect(dimmed.label).toContain(', 2 with a small base');
      expect(plain.label).not.toContain('small base');
    });

    it('says in the tooltip of the dimmed column that its base is small (Small base in a tooltip)', async () => {
      const context = await show('column', { groupBy: 'comuna', display: { minN: 30 } }, result(rows()));

      await hover(context, hits(context)[0]);

      expect(tooltipFooter()).toBe('Small base: n = 16 (below 30)');
    });

    it('dims the bars of a bar chart and the slices of a pie the same way', async () => {
      const bar = await show('bar', { groupBy: 'comuna', display: { minN: 30 } }, result(rows()));
      expect(opacities(bar)).toEqual(['0.35', null, '0.35']);
      clearOverlay();
      TestBed.resetTestingModule();

      const pie = await show('pie', { groupBy: 'comuna', display: { minN: 30 } }, result(rows()));
      const slices = all(pie.root, 'pie-slice');
      expect(slices.map((slice) => slice.getAttribute('fill-opacity'))).toEqual(['0.35', null, '0.35']);
      expect(slices.map((slice) => slice.getAttribute('stroke-dasharray'))).toEqual(['3 2', null, '3 2']);
      expect(label(pie)).toContain(', 2 with a small base');
    });

    it('draws a hollow dashed ring for the point of a line chart that has a small base, and keeps the dots of a long line for them', async () => {
      const context = await show('line', { groupBy: 'ronda', display: { minN: 30 } }, result([row('1', 0.5, null, { numerator: 1, denominator: 2, n: 2 }), row('2', 0.6, null, { numerator: 60, denominator: 100, n: 100 })]));

      const dots = all(context.root, 'chart-dot');
      expect(dots.map((dot) => dot.getAttribute('data-small'))).toEqual(['true', null]);
      expect(dots[0].getAttribute('stroke-dasharray')).toBe('2 2');

      clearOverlay();
      TestBed.resetTestingModule();
      const many = Array.from({ length: 45 }, (_, i) => row(String(i + 1), 0.5, null, { numerator: 5, denominator: 10, n: i === 3 ? 4 : 100 }));
      const long = await show('line', { groupBy: 'ronda', display: { minN: 30 } }, result(many));
      expect(all(long.root, 'chart-dot')).toHaveLength(1);
      expect(all(long.root, 'chart-dot')[0].getAttribute('data-small')).toBe('true');
    });

    it('dims the figure of a stat tile whose row has a small base (Stat tile)', async () => {
      const context = await show('stat', { groupBy: null, display: { minN: 30 } }, result([row(null, 0.4, null, { numerator: 2, denominator: 5, n: 5 })]));
      expect(byTestId(context.root, 'stat-value')!.style.opacity).toBe('0.35');
      clearOverlay();
      TestBed.resetTestingModule();

      const plain = await show('stat', { groupBy: null }, result([row(null, 0.4, null, { numerator: 2, denominator: 5, n: 5 })]));
      expect(byTestId(plain.root, 'stat-value')!.style.opacity).toBe('');
    });

    it('never dims a value that has no n, such as the sum of a measure with no base column', async () => {
      const context = await show('column', { ratio: null, measure: 'value', groupBy: 'comuna', display: { minN: 30 } }, result([row('COMUNA 7', 5), row('COMUNA 1', 80)]));

      expect(opacities(context).every((value) => value === null)).toBe(true);
    });

    it('draws no histogram bar dimmed: its bars have no n', async () => {
      const context = await show('histogram', { groupBy: null, buckets: 4, display: { minN: 30 } }, result([row(null, 1), row(null, 2), row(null, 3), row(null, 10)]));

      expect(opacities(context).every((value) => value === null)).toBe(true);
    });
  });

  it('draws nothing rotated, skewed or moved by an attribute in the SVG of any chart that dims small bases (the hatch is a path, never rotated)', async () => {
    const plainRows = [row('COMUNA 7', 0.5, null, { numerator: 8, denominator: 16, n: 16 }), row('COMUNA 1', 0.46, null, { numerator: 56, denominator: 121, n: 121 })];
    const cases: [ChartType, Partial<WidgetConfig>, QueryResult][] = [
      ['column', { groupBy: 'comuna' }, result(plainRows)],
      ['bar', { groupBy: 'comuna' }, result(plainRows)],
      ['line', { groupBy: 'ronda' }, result(plainRows)],
      ['pie', { groupBy: 'comuna' }, result(plainRows)],
      ['heatmap', { groupBy: 'comuna', seriesBy: 'categoria' }, answerOf([figured('A', 'x', 1, 10), figured('A', 'y', 50, 100)], ['x', 'y'])],
    ];
    for (const [type, settings, answer] of cases) {
      const context = await show(type, { ...settings, display: { minN: 30 } }, answer);

      expect(chartOf(context).querySelector(movedElements), type).toBeNull();
      clearOverlay();
      TestBed.resetTestingModule();
    }
  });
});
