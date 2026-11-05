import { ChartType, WidgetConfig } from '../../../core/projects/indicators.service';
import { overOnAnswers } from './base-figures';
import { ReferenceAnswer, referenceLineValues } from './reference-lines';
import fixture from './testing/reference-lines.json';

// The same cases the API's ReferenceLinesTests read (add-chart-annotations, design D3): the lines a widget draws are derived from its
// config and the answer of its query, and both sides must give the same kinds, labels and values.

interface Expected {
  kind: string;
  label: string | null;
  value: number;
}

describe('referenceLineValues', () => {
  it('reads the shared cases', () => {
    expect(fixture.cases.length).toBeGreaterThanOrEqual(15);
    expect(fixture.tolerance).toBe(1e-12);
  });

  it.each(fixture.cases.map((c) => [c.name, c] as const))('gives the lines the shared case expects: %s', (_name, c) => {
    const answerColumns = (c as { answerColumns?: string[] }).answerColumns ?? [];
    const lines = referenceLineValues(c.chartType as ChartType, c.config as unknown as WidgetConfig, c.answer as unknown as ReferenceAnswer, answerColumns);
    const expected = c.expected as Expected[];

    expect(lines.map((line) => [line.kind, line.label])).toEqual(expected.map((line) => [line.kind, line.label]));
    expect(lines).toHaveLength(expected.length);
    lines.forEach((line, i) => expect(Math.abs(line.value - expected[i].value)).toBeLessThanOrEqual(fixture.tolerance));
  });

  it('adds in the answer\'s order, so a sum that depends on the order gives the same mean as the API', () => {
    const c = fixture.cases.find((entry) => entry.name === 'Sums that depend on the order of addition')!;

    const [mean, median] = referenceLineValues(c.chartType as ChartType, c.config as unknown as WidgetConfig, c.answer as unknown as ReferenceAnswer);

    expect(mean.value).toBe(0);
    expect(median.value).toBe(1);
  });

  it('gives no line for a pie, a stat tile or any other kind, whatever its display holds', () => {
    const c = fixture.cases.find((entry) => entry.name === 'Four bars')!;
    for (const type of ['pie', 'stat', 'histogram', 'map', 'heatmap', 'pyramid', 'likert', 'crosstab', 'boxplot'] as ChartType[]) {
      expect(referenceLineValues(type, c.config as unknown as WidgetConfig, c.answer as unknown as ReferenceAnswer), type).toEqual([]);
    }
  });

  it('gives no line for a widget with no lines, and none for an empty list', () => {
    const answer = { rows: [{ group: 'Zona 1', value: 0.4, geometry: null }] };
    const config = { indicator: 'encuesta_hoy', groupBy: 'zona' } as WidgetConfig;

    expect(referenceLineValues('column', config, answer)).toEqual([]);
    expect(referenceLineValues('column', { ...config, display: { referenceLines: [] } }, answer)).toEqual([]);
  });

  it('leaves out a non-finite value', () => {
    const config = { indicator: 'encuesta_hoy', groupBy: 'zona', display: { referenceLines: [{ kind: 'overall' }, { kind: 'value', value: Number.NaN }] } } as WidgetConfig;

    expect(referenceLineValues('column', config, { rows: [], overall: Number.POSITIVE_INFINITY })).toEqual([]);
  });
});

describe('referenceLineValues over the answer columns of an indicator (add-small-sample-warnings, design D16)', () => {
  const answer: ReferenceAnswer = {
    rows: [
      { group: 'Teatro', value: 0.3, geometry: null },
      { group: 'Cine', value: 0.5, geometry: null },
      { group: 'Museo', value: 0.7, geometry: null },
    ],
    overall: 0.5,
  };
  const lines = [{ kind: 'mean' as const }, { kind: 'overall' as const }, { kind: 'value' as const, value: 0.4, label: 'Meta' }];
  const config = { indicator: 'teatro_hoy', ratio: { numerator: 'numerator', denominator: 'denominator' }, groupBy: 'categoria', display: { referenceLines: lines } } as WidgetConfig;

  it('draws the overall line as it always did when the indicator declares no answer column (Overall line over comunas)', () => {
    expect(referenceLineValues('bar', config, answer).map((line) => line.kind)).toEqual(['mean', 'overall', 'value']);
    expect(referenceLineValues('bar', config, answer, []).map((line) => line.kind)).toEqual(['mean', 'overall', 'value']);
    expect(referenceLineValues('bar', config, answer, ['variable', 'orden']).map((line) => line.kind)).toEqual(['mean', 'overall', 'value']);
  });

  it('leaves out the overall line of a chart grouped by an answer column, and draws the others as before (Overall line over places)', () => {
    const drawn = referenceLineValues('bar', config, answer, ['variable', 'categoria']);

    expect(drawn.map((line) => [line.kind, line.label])).toEqual([
      ['mean', null],
      ['value', 'Meta'],
    ]);
    expect(drawn[0].value).toBeCloseTo(0.5, 12);
  });

  it('keeps the place of each line, so a line keeps its dash when another is not drawn', () => {
    expect(referenceLineValues('bar', config, answer, ['categoria']).map((line) => line.place)).toEqual([0, 2]);
  });

  it('leaves it out for a chart with a series split by an answer column too, and not for one split by another', () => {
    const grouped = { ...config, groupBy: 'comuna', seriesBy: 'categoria', display: { referenceLines: [{ kind: 'overall' as const }] } } as WidgetConfig;
    const withSeries: ReferenceAnswer = { rows: [], totals: { groups: [], series: [], all: { value: 0.46, rows: 1 } } };

    expect(referenceLineValues('column', grouped, withSeries, ['categoria'])).toEqual([]);
    expect(referenceLineValues('column', grouped, withSeries, ['otra']).map((line) => line.kind)).toEqual(['overall']);
  });

  it('says when a widget is over answers: its group or its series column is one, and nothing is declared otherwise', () => {
    expect(overOnAnswers({ groupBy: 'categoria' }, ['categoria'])).toBe(true);
    expect(overOnAnswers({ groupBy: 'comuna', seriesBy: 'categoria' }, ['categoria'])).toBe(true);
    expect(overOnAnswers({ groupBy: 'comuna', seriesBy: 'sexo' }, ['categoria'])).toBe(false);
    expect(overOnAnswers({ groupBy: null, seriesBy: null }, ['categoria'])).toBe(false);
    expect(overOnAnswers({ groupBy: 'categoria' }, [])).toBe(false);
    expect(overOnAnswers({ groupBy: 'categoria' }, undefined)).toBe(false);
  });
});
