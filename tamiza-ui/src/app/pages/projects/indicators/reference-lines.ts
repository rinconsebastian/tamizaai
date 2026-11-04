import { ChartType, QueryResult, ReferenceLineKind, WidgetConfig } from '../../../core/projects/indicators.service';

/** A reference line a chart can draw (add-chart-annotations, design D3): its kind, the widget's own label (null for the kind's default) and its value. */
export interface ReferenceLineValue {
  kind: ReferenceLineKind;
  label: string | null;
  value: number;
  /** The line's place in the widget's list, drawn or not: it picks the line's dash, so a line keeps its pattern when another has no value. */
  place: number;
}

/** What the lines need of the answer of the widget's query: its rows, the overall value of a widget with no series, the totals of one with a series. */
export type ReferenceAnswer = Pick<QueryResult, 'rows'> & Partial<Pick<QueryResult, 'overall' | 'totals'>>;

const finite = (value: number | null | undefined): number | null => (typeof value === 'number' && Number.isFinite(value) ? value : null);

/** The sum added one value after another in the order given, over the count: floating-point sums depend on the order, so the twin does the same. */
function mean(values: readonly number[]): number {
  let sum = 0;
  for (const value of values) {
    sum += value;
  }
  return sum / values.length;
}

/** The middle of the sorted values, or the mean of the two middle ones when there is an even number, as pandas computes it. */
function median(values: readonly number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/**
 * The lines of a bar, column or line widget that have a value, in the order the widget lists them; every other kind has none (design D3).
 * `mean` is the mean of the values the chart draws (the answer's rows, in its order), `median` their median, `overall` the answer's
 * `overall` or, with a series, its grand total, and `value` its own number. A line with no value (no bars, an overall of no rows) is
 * left out, and so is a mean or a median of a widget with a series, which the API refuses. A null line or a line of an unknown kind is
 * left out too. Twin of `ReferenceLines.Of` in tamiza-api (the case file `testing/reference-lines.json` is shared); change both together.
 */
export function referenceLineValues(type: ChartType, config: WidgetConfig, answer: ReferenceAnswer): ReferenceLineValue[] {
  const lines = config.display?.referenceLines;
  if ((type !== 'bar' && type !== 'column' && type !== 'line') || !lines?.length) {
    return [];
  }
  const withSeries = !!config.seriesBy;
  const values = withSeries ? [] : answer.rows.map((row) => row.value);
  const kept: ReferenceLineValue[] = [];
  lines.forEach((line, place) => {
    const value = valueOf(line?.kind, line?.value, withSeries, values, answer);
    if (line && value !== null) {
      kept.push({ kind: line.kind, label: line.label?.trim() || null, value, place });
    }
  });
  return kept;
}

function valueOf(kind: string | undefined, own: number | undefined, withSeries: boolean, values: readonly number[], answer: ReferenceAnswer): number | null {
  switch (kind) {
    case 'mean':
      return !withSeries && values.length ? mean(values) : null;
    case 'median':
      return !withSeries && values.length ? median(values) : null;
    case 'overall':
      return finite(withSeries ? answer.totals?.all.value : answer.overall);
    case 'value':
      return finite(own);
    default:
      return null;
  }
}
