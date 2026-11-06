import { ChartType, WidgetConfig } from '../../../core/projects/indicators.service';
import { ColumnMetadataMap } from '../../../core/projects/pipeline.service';
import type { SeriesTable } from './series-table';

// The declared order of a column's values (add-declared-category-order, design D2, D3 and D5): one rule, applied after the query by every
// consumer. Twin of `CategoryOrder` in tamiza-api (scheduled emails, the widget workbook and the MCP tools); both are tested against the
// same cases in testing/category-orders.json, so change them together.

/** What an entry of an ordered place is: a value (with its text), the missing value shown as "(none)", or the combined "Other". */
export type EntryKind = 'value' | 'none' | 'other';

/** One entry of a place that is ordered: a group axis or a series legend. */
export interface OrderEntry {
  value: string | null;
  kind: EntryKind;
}

/**
 * The order that applies to one place (design D3): the values to list first, in order, the values kept at the end, and whether a sort or
 * an order by median decides the order of the rest, in which case `order` is not used and only the values kept at the end move.
 */
export interface PlaceOrder {
  order: readonly string[];
  last: readonly string[];
  sorted: boolean;
}

/**
 * What a widget declares for its values, resolved: the order of its groups and of its series, and whether the text of each place hides
 * numbered prefixes. A null place is arranged as it is today.
 */
export interface CategoryOrders {
  groups: PlaceOrder | null;
  series: PlaceOrder | null;
  hideGroupPrefixes: boolean;
  hideSeriesPrefixes: boolean;
}

/** An entry for a group or series value: a missing one (null) is "(none)". */
export const entryOf = (value: string | null): OrderEntry => (value === null ? { value: null, kind: 'none' } : { value, kind: 'value' });

const indexOf = (values: readonly string[]): Map<string, number> => {
  const index = new Map<string, number>();
  values.forEach((value, i) => {
    if (!index.has(value)) {
      index.set(value, i);
    }
  });
  return index;
};

/**
 * Arranges the `entries` of one place, which come in the order the place has today (design D3): the entries the order lists, in its
 * order; then the others, as given; then the values kept last, in their own order; then "(none)"; then "Other". Values match as exact
 * text, a listed value that is not an entry is skipped, and a place with no declaration, or whose declaration has nothing that applies, is
 * returned as it came.
 */
export function arrange<T>(entries: readonly T[], keyOf: (entry: T) => OrderEntry, declared: PlaceOrder | null): T[] {
  if (!declared) {
    return [...entries];
  }
  const order = declared.sorted ? [] : declared.order;
  if (order.length === 0 && declared.last.length === 0) {
    return [...entries];
  }
  const listedAt = indexOf(order);
  const keptAt = indexOf(declared.last);
  const listed: { at: number; entry: T }[] = [];
  const kept: { at: number; entry: T }[] = [];
  const rest: T[] = [];
  const none: T[] = [];
  const other: T[] = [];
  for (const entry of entries) {
    const key = keyOf(entry);
    if (key.kind === 'other') {
      other.push(entry);
    } else if (key.kind === 'none' || key.value === null) {
      none.push(entry);
    } else if (listedAt.has(key.value)) {
      listed.push({ at: listedAt.get(key.value)!, entry });
    } else if (keptAt.has(key.value)) {
      kept.push({ at: keptAt.get(key.value)!, entry });
    } else {
      rest.push(entry);
    }
  }
  // `sort` is stable, so entries that stand for the same value keep the order they came in.
  const byPlace = (a: { at: number }, b: { at: number }) => a.at - b.at;
  return [...listed.sort(byPlace).map((l) => l.entry), ...rest, ...kept.sort(byPlace).map((k) => k.entry), ...none, ...other];
}

/**
 * A leading number of 1 to 3 digits, optional spaces, one of `. ) : - –` and at least one space, followed by a rest that is not empty
 * and does not start with a digit or a space (design D5): "1. Sí", "2) No", "3 - Tal vez" lose their number; "10 - 14", "1.5 horas",
 * "1.Sí" and "$10.000" do not.
 */
const prefix = /^[0-9]{1,3} *[.):\-–] +(?=[^0-9\s])/;

/** The text of one value without its leading number (design D5), or the value itself when it has none to hide. */
export function stripPrefix(value: string): string {
  const match = prefix.exec(value);
  return match ? value.slice(match[0].length) : value;
}

/**
 * The text each value of one place is shown with (design D5): without its leading number when `strip` is set, except that two different
 * values that would read alike keep their full text. The map holds every distinct non-null value.
 */
export function shownValues(values: readonly (string | null)[], strip: boolean): Map<string, string> {
  const distinct = [...new Set(values.filter((value): value is string => value !== null))];
  if (!strip) {
    return new Map(distinct.map((value) => [value, value]));
  }
  const stripped = new Map(distinct.map((value) => [value, stripPrefix(value)]));
  const counts = new Map<string, number>();
  for (const text of stripped.values()) {
    counts.set(text, (counts.get(text) ?? 0) + 1);
  }
  return new Map(distinct.map((value) => [value, counts.get(stripped.get(value)!)! > 1 ? value : stripped.get(value)!]));
}

/** The text a value is shown with, from `shownValues`; a missing value stays missing, and a value the map lacks is shown as it is. */
export function shown(map: ReadonlyMap<string, string> | null | undefined, value: string | null): string | null {
  return value !== null && map?.has(value) ? map.get(value)! : value;
}

/** The text each group of a grid is shown with, or null when its column does not hide prefixes (then a group is shown as it is). */
export function groupTexts(table: SeriesTable, orders: CategoryOrders | null | undefined): Map<string, string> | null {
  return orders?.hideGroupPrefixes ? shownValues(table.rows.map((row) => row.group), true) : null;
}

/** The text each series of a grid is shown with, or null when its column does not hide prefixes. */
export function seriesTexts(table: SeriesTable, orders: CategoryOrders | null | undefined): Map<string, string> | null {
  return orders?.hideSeriesPrefixes ? shownValues(table.columns.filter((column) => column.kind === 'value').map((column) => column.series), true) : null;
}

const withSeries: readonly ChartType[] = ['bar', 'column', 'line', 'heatmap', 'pyramid', 'likert', 'crosstab'];

function placeOf(widgetOrder: readonly string[] | undefined, order: readonly string[] | undefined, last: readonly string[] | undefined, sorted: boolean): PlaceOrder | null {
  const listed = widgetOrder?.length ? widgetOrder : (order ?? []);
  const kept = last ?? [];
  return (sorted ? kept.length === 0 : listed.length === 0 && kept.length === 0) ? null : { order: listed, last: kept, sorted };
}

/**
 * What a widget of `type` declares for its groups and series (design D3 and D4), from its own order and its indicator's column
 * metadata: the widget's list replaces the column's order, never its values kept last, and a sort (or a box plot's order by median)
 * keeps only the values kept last. Null when no place has a declaration and no column it shows hides prefixes, so a caller then runs
 * today's code. Twin of `CategoryOrder.For` in tamiza-api.
 */
export function ordersFor(type: ChartType | null | undefined, config: Partial<WidgetConfig> | null | undefined, columns: ColumnMetadataMap | null | undefined): CategoryOrders | null {
  if (!type || !config) {
    return null;
  }
  const display = config.display;
  let groups: PlaceOrder | null = null;
  let hideGroups = false;
  if (config.groupBy && type !== 'stat' && type !== 'histogram') {
    const meta = columns?.[config.groupBy];
    hideGroups = meta?.stripPrefix === true;
    if (type !== 'map') {
      const sorted = ((type === 'bar' || type === 'column') && !!config.sort) || (type === 'boxplot' && config.sort?.by === 'value');
      groups = placeOf(display?.order?.group, meta?.order, meta?.last, sorted);
    }
  }
  let series: PlaceOrder | null = null;
  let hideSeries = false;
  if (config.seriesBy && withSeries.includes(type)) {
    const meta = columns?.[config.seriesBy];
    hideSeries = meta?.stripPrefix === true;
    if (type !== 'pyramid' && type !== 'likert') {
      series = placeOf(display?.order?.series, meta?.order, meta?.last, false);
    }
  }
  return groups || series || hideGroups || hideSeries ? { groups, series, hideGroupPrefixes: hideGroups, hideSeriesPrefixes: hideSeries } : null;
}

/**
 * The grid of a widget with a series arranged (design D4): the rows in the order of the groups, and each column, with its cells, shares,
 * figures and total, in the order of the series. A grid whose places have no declaration is returned as it is.
 */
export function arrangeColumns(table: SeriesTable, orders: CategoryOrders | null | undefined): SeriesTable {
  if (!orders || (!orders.groups && !orders.series)) {
    return table;
  }
  const columns = arrange(
    table.columns.map((_, i) => i),
    (i) => ({ value: table.columns[i].series, kind: table.columns[i].kind }),
    orders.series,
  );
  const rows = arrange(table.rows, (row) => entryOf(row.group), orders.groups).map((row) => ({
    ...row,
    cells: columns.map((i) => row.cells[i]),
    shares: row.shares ? columns.map((i) => row.shares![i]) : null,
    ...(row.figures ? { figures: columns.map((i) => row.figures![i]) } : {}),
  }));
  return { ...table, columns: columns.map((i) => table.columns[i]), rows };
}
