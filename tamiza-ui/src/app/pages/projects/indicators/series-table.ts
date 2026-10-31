import { ChartType, QueryResult, SeriesKind, SeriesLayout } from '../../../core/projects/indicators.service';
import { naturalCompare } from './chart-scales';

// The grid every consumer of a widget with a series reads (design D4, D12): the chart's tooltips and marks, "Show as table", and so
// the cross table. Twin of `SeriesTable` in tamiza-api (emails and MCP); both are tested against the same cases in
// testing/series-tables.json, so change them together.

/** One series, in the order of `QueryResult.series`; its total is the response's, never added up from the cells. */
export interface SeriesColumn {
  series: string | null;
  kind: SeriesKind;
  total: number | null;
  rows: number;
}

/** One group, in the order of `QueryResult.totals.groups`; `cells[i]` and `shares[i]` belong to `columns[i]`. */
export interface SeriesRow {
  group: string | null;
  total: number | null;
  rows: number;
  /** The row's value for each series, or null when the pair has no value. */
  cells: (number | null)[];
  /** Each cell over the sum of the row's non-negative cells; null unless the table has shares (see `hasShares`). */
  shares: (number | null)[] | null;
}

export interface SeriesTable {
  columns: SeriesColumn[];
  rows: SeriesRow[];
  grandTotal: number | null;
  grandRows: number;
  /** Cells below zero that a table of shares leaves out of them. */
  negativeLeftOut: number;
  /** Rows a pyramid or a Likert widget left out (its sides or its scale did not hold them). */
  leftOut: number;
  rowLimitReached: boolean;
}

/** What `buildSeriesTable` reads of an answer. */
export type SeriesAnswer = Pick<QueryResult, 'series' | 'rows' | 'totals' | 'leftOut'> & Partial<Pick<QueryResult, 'rowLimitReached'>>;

/** Whether the table has shares: a Likert widget, and a bar or column chart stacked to 100%. */
export function hasShares(type: ChartType, layout: SeriesLayout | null | undefined): boolean {
  return type === 'likert' || ((type === 'bar' || type === 'column') && layout === 'percent');
}

/** A cell's place among the series: a value by its text, the other two kinds by their kind. */
const slotOf = (kind: SeriesKind | undefined, series: string | null | undefined): string => ((kind ?? 'value') === 'value' ? `value:${series ?? ''}` : (kind as string));

/** The grid of an answer with a series: one row per group, one column per series, the totals as the server computed them. */
export function buildSeriesTable(answer: SeriesAnswer, type: ChartType, layout: SeriesLayout | null | undefined): SeriesTable {
  const series = answer.series ?? [];
  const totals = answer.totals;
  const columns: SeriesColumn[] = series.map((entry, i) => ({
    series: entry.value,
    kind: entry.kind,
    total: totals?.series[i]?.value ?? null,
    rows: totals?.series[i]?.rows ?? 0,
  }));
  const columnOf = new Map(series.map((entry, i) => [slotOf(entry.kind, entry.value), i]));
  const groups = totals?.groups ?? [];
  const rowOf = new Map<string | null, number>(groups.map((entry, i) => [entry.group, i]));
  const rows: SeriesRow[] = groups.map((entry) => ({ group: entry.group, total: entry.value, rows: entry.rows, cells: columns.map(() => null), shares: null }));
  for (const cell of answer.rows) {
    const row = rowOf.get(cell.group);
    const column = columnOf.get(slotOf(cell.seriesKind, cell.series));
    if (row !== undefined && column !== undefined) {
      rows[row].cells[column] = cell.value;
    }
  }
  let negativeLeftOut = 0;
  if (hasShares(type, layout)) {
    for (const row of rows) {
      const whole = row.cells.reduce<number>((sum, cell) => sum + (cell !== null && cell >= 0 ? cell : 0), 0);
      negativeLeftOut += row.cells.filter((cell) => cell !== null && cell < 0).length;
      row.shares = row.cells.map((cell) => (cell !== null && cell >= 0 && whole > 0 ? cell / whole : null));
    }
  }
  return {
    columns,
    rows,
    grandTotal: totals?.all.value ?? null,
    grandRows: totals?.all.rows ?? 0,
    negativeLeftOut,
    leftOut: answer.leftOut ?? 0,
    rowLimitReached: answer.rowLimitReached ?? false,
  };
}

/**
 * The groups in the order the chart draws them: the table's own for a bar or a column (an analyst can order those), and natural order
 * (2 before 10, a missing group last) for the kinds that cannot be ordered: a line chart, a heat map, a population pyramid, Likert
 * bars and a cross table. A stable sort, so equal groups keep the table's order. Twin of `NaturalGroups` in the API's SeriesQueryBuilder.
 */
export function chartOrder(table: SeriesTable, type: ChartType): SeriesRow[] {
  if (type === 'bar' || type === 'column') {
    return table.rows;
  }
  return [...table.rows].sort((a, b) => (a.group === null ? (b.group === null ? 0 : 1) : b.group === null ? -1 : naturalCompare(a.group, b.group)));
}

/** The table of an answer with its groups in the order the chart draws them (see {@link chartOrder}). */
export function orderedSeriesTable(answer: SeriesAnswer, type: ChartType, layout: SeriesLayout | null | undefined): SeriesTable {
  const table = buildSeriesTable(answer, type, layout);
  return { ...table, rows: chartOrder(table, type) };
}

/** True when an answer is that of a widget with a series: the server sent the series domain and the totals. */
export function isSeriesAnswer(answer: Pick<QueryResult, 'series' | 'totals'> | null | undefined): boolean {
  return !!answer?.series && !!answer.totals;
}
