import { ChartType, QueryRow, SeriesLayout } from '../../../core/projects/indicators.service';
import cases from './testing/series-tables.json';
import { SeriesAnswer, buildSeriesTable, chartOrder, hasShares, isSeriesAnswer } from './series-table';

// The same cases the API's SeriesTable tests read (design D12): the grid is derived from an answer, a chart type and a layout, and
// both sides must give the same rows, totals, shares and notes.

describe('buildSeriesTable', () => {
  it('reads the shared cases', () => {
    expect(cases.cases.length).toBeGreaterThanOrEqual(8);
  });

  it.each(cases.cases.map((c) => [c.name, c] as const))('gives the expected grid: %s', (_name, c) => {
    const table = buildSeriesTable(c.response as SeriesAnswer, c.chartType as ChartType, c.layout as SeriesLayout | null);

    expect(table).toEqual(c.expected);
  });

  it('puts a cell in the column of its series by the series value, and "(none)" and "Other" by their kind', () => {
    const c = cases.cases.find((entry) => entry.name === 'missing series value and Other')!;

    const table = buildSeriesTable(c.response as SeriesAnswer, 'column', 'stacked');

    expect(table.columns.map((column) => column.kind)).toEqual(['value', 'value', 'none', 'other']);
    expect(table.rows[0].cells).toEqual([10, null, 2, 3]);
  });

  it('reads a cell of "(none)" and "Other" the same whether its series is null or absent, as the API sends it', () => {
    const c = cases.cases.find((entry) => entry.name === 'missing series value and Other')!;
    const answer = c.response as SeriesAnswer;
    // The API leaves `series` out of the JSON of a cell whose kind is none or other; the shared case writes it as null.
    const absent: SeriesAnswer = { ...answer, rows: answer.rows.map(({ series, ...rest }) => (rest.seriesKind === 'value' ? { series, ...rest } : rest)) };

    expect(absent.rows.filter((r) => 'series' in r)).toHaveLength(2);
    expect(buildSeriesTable(absent, 'column', 'stacked')).toEqual(buildSeriesTable(answer, 'column', 'stacked'));
    expect(buildSeriesTable(absent, 'column', 'stacked')).toEqual(c.expected);
  });

  it('keeps the groups in the order of the totals, whatever the order of the rows', () => {
    const c = cases.cases[0];
    const reversed = { ...(c.response as SeriesAnswer), rows: [...(c.response.rows as QueryRow[])].reverse() };

    expect(buildSeriesTable(reversed, 'column', 'stacked').rows.map((row) => row.group)).toEqual(['Cali', 'Palmira']);
  });

  it('keeps a missing group as a row of its own', () => {
    const table = buildSeriesTable(
      {
        series: [{ value: 'A', kind: 'value' }],
        rows: [{ group: null, series: 'A', seriesKind: 'value', value: 4, geometry: null }],
        totals: { groups: [{ group: null, value: 4, rows: 1 }], series: [{ series: 'A', kind: 'value', value: 4, rows: 1 }], all: { value: 4, rows: 1 } },
      },
      'crosstab',
      null,
    );

    expect(table.rows).toEqual([{ group: null, total: 4, rows: 1, cells: [4], shares: null }]);
  });

  it('counts each negative cell of a table of shares once, even in a row whose other cells sum to zero', () => {
    const table = buildSeriesTable(
      {
        series: [
          { value: 'A', kind: 'value' },
          { value: 'B', kind: 'value' },
        ],
        rows: [
          { group: 'G', series: 'A', seriesKind: 'value', value: -3, geometry: null },
          { group: 'G', series: 'B', seriesKind: 'value', value: 0, geometry: null },
        ],
        totals: {
          groups: [{ group: 'G', value: -3, rows: 2 }],
          series: [
            { series: 'A', kind: 'value', value: -3, rows: 1 },
            { series: 'B', kind: 'value', value: 0, rows: 1 },
          ],
          all: { value: -3, rows: 2 },
        },
      },
      'column',
      'percent',
    );

    expect(table.negativeLeftOut).toBe(1);
    expect(table.rows[0].shares).toEqual([null, null]);
  });

  it('has shares only for a Likert widget and for a bar or column chart stacked to 100%', () => {
    expect(hasShares('likert', null)).toBe(true);
    expect(hasShares('bar', 'percent')).toBe(true);
    expect(hasShares('column', 'percent')).toBe(true);
    for (const layout of ['grouped', 'stacked', null] as const) {
      expect(hasShares('column', layout)).toBe(false);
    }
    for (const type of ['line', 'heatmap', 'pyramid', 'crosstab'] as const) {
      expect(hasShares(type, 'percent')).toBe(false);
    }
  });

  it('recognizes the answer of a widget with a series', () => {
    expect(isSeriesAnswer(cases.cases[0].response as SeriesAnswer)).toBe(true);
    expect(isSeriesAnswer({})).toBe(false);
    expect(isSeriesAnswer(null)).toBe(false);
  });
});

describe('chartOrder', () => {
  const table = buildSeriesTable(
    {
      series: [{ value: 'A', kind: 'value' }],
      rows: [],
      totals: {
        groups: ['10', null, '2', '1'].map((group) => ({ group, value: 1, rows: 1 })),
        series: [{ series: 'A', kind: 'value', value: 4, rows: 4 }],
        all: { value: 4, rows: 4 },
      },
    },
    'line',
    null,
  );

  it('puts the groups of the charts nobody can order in natural order, 2 before 10, and a missing group last', () => {
    for (const type of ['line', 'heatmap', 'pyramid', 'likert', 'crosstab'] as const) {
      expect(chartOrder(table, type).map((row) => row.group)).toEqual(['1', '2', '10', null]);
    }
  });

  it('keeps the order of the table for a bar and a column, which an analyst can order', () => {
    for (const type of ['bar', 'column'] as const) {
      expect(chartOrder(table, type).map((row) => row.group)).toEqual(['10', null, '2', '1']);
    }
  });
});
