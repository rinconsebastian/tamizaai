import { ChartType, WidgetConfig } from '../../../core/projects/indicators.service';
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
    const lines = referenceLineValues(c.chartType as ChartType, c.config as unknown as WidgetConfig, c.answer as unknown as ReferenceAnswer);
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
