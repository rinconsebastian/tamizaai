import { DataColumn, DataColumnType, DataFilter, FilterOp } from '../../../core/projects/data.service';
import { AppliedDashboardFilter, DashboardFilter, DashboardFilterKind, DashboardFilterValue } from '../../../core/projects/indicators.service';
import { decodeFilters, encodeFilters } from '../data-view';

// A dashboard's filters on the member's screen (dashboard filters, design D7): the value of each, kept in the page address and never
// saved, and what each widget's query is sent of them.

/** The value in force of each filter of the dashboard, by column; null is "All". */
export type FilterValues = Record<string, DashboardFilterValue | null>;

/** A filter with a value in force, in the bar's order, and whether a widget ignores it. */
export interface ActiveDashboardFilter {
  filter: DashboardFilter;
  value: DashboardFilterValue;
  ignored: boolean;
}

/** The most values an "is one of" filter takes, and the longest one (`DataQueryBuilder`). */
const maxValues = 100;
const maxValueLength = 500;

/** The column the filter dialog is given: a number filter takes any decimal, a date filter takes days. */
export function columnFor(filter: Pick<DashboardFilter, 'column' | 'kind'>): DataColumn {
  const types: Record<DashboardFilterKind, DataColumnType> = { text: 'text', boolean: 'boolean', number: 'decimal', date: 'date' };
  return { name: filter.column, type: types[filter.kind], label: null };
}

/** The one condition of a kind: text and true/false filters choose values, number and date filters take a range. */
export function operationOf(kind: DashboardFilterKind): FilterOp {
  return kind === 'text' || kind === 'boolean' ? 'in' : 'range';
}

/** The kind a published column's type belongs to (`DashboardFilterKinds.Of`). */
export function kindOf(type: DataColumnType): DashboardFilterKind {
  switch (type) {
    case 'boolean':
      return 'boolean';
    case 'integer':
    case 'decimal':
      return 'number';
    case 'date':
    case 'timestamp':
      return 'date';
    default:
      return 'text';
  }
}

export function toDataFilter(column: string, kind: DashboardFilterKind, value: DashboardFilterValue): DataFilter {
  return operationOf(kind) === 'in'
    ? { column, op: 'in', values: [...(value.values ?? [])] }
    : { column, op: 'range', ...(value.min ? { min: value.min } : {}), ...(value.max ? { max: value.max } : {}) };
}

/** The value a clean data filter describes for a filter of this kind, or null when it is not one (another condition, or a bad value). */
export function fromDataFilter(kind: DashboardFilterKind, filter: DataFilter): DashboardFilterValue | null {
  if (filter.op !== operationOf(kind)) {
    return null;
  }
  const value: DashboardFilterValue = filter.op === 'in' ? { values: [...(filter.values ?? [])] } : { min: filter.min || null, max: filter.max || null };
  return isValueOf(kind, value) ? value : null;
}

const numberShape = /^[-+]?(\d+(\.\d*)?|\.\d+)$/;
const dayShape = /^\d{4}-\d{2}-\d{2}$/;

function isDay(text: string): boolean {
  if (!dayShape.test(text)) {
    return false;
  }
  const date = new Date(`${text}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(text);
}

/** What the API checks of a value (`DashboardFilterKinds.CheckValue`), so a value from the address is dropped instead of refused. */
export function isValueOf(kind: DashboardFilterKind, value: DashboardFilterValue): boolean {
  if (operationOf(kind) === 'in') {
    const values = value.values ?? [];
    if (!values.length || values.length > maxValues || value.min || value.max) {
      return false;
    }
    return kind === 'boolean' ? values.every((v) => v === 'true' || v === 'false') : values.every((v) => typeof v === 'string' && v.length <= maxValueLength);
  }
  if (value.values?.length) {
    return false;
  }
  const { min, max } = value;
  if (!min && !max) {
    return false;
  }
  const valid = (text: string | null | undefined) => !text || (kind === 'number' ? numberShape.test(text) : isDay(text));
  if (!valid(min) || !valid(max)) {
    return false;
  }
  // Days in this form compare as text; numbers as numbers.
  return !min || !max || (kind === 'number' ? Number(min) <= Number(max) : min <= max);
}

function sameValue(a: DashboardFilterValue | null | undefined, b: DashboardFilterValue | null | undefined): boolean {
  if (!a || !b) {
    return !a && !b;
  }
  const values = (v: DashboardFilterValue) => [...(v.values ?? [])].sort().join('\u0000');
  return values(a) === values(b) && (a.min || null) === (b.min || null) && (a.max || null) === (b.max || null);
}

export function defaultsOf(filters: DashboardFilter[]): FilterValues {
  return Object.fromEntries(filters.map((f) => [f.column, f.default ?? null]));
}

export function sameValues(filters: DashboardFilter[], a: FilterValues, b: FilterValues): boolean {
  return filters.every((f) => sameValue(a[f.column], b[f.column]));
}

/**
 * The values the address says. Without `f`, every filter is at its default; with it, exactly what it holds, so a link can say a
 * filter was cleared. An entry for a filter the dashboard no longer has, or whose value its kind cannot take, is dropped and named
 * (by its label, or by its column when the filter is gone).
 */
export function readAddress(f: string | null | undefined, filters: DashboardFilter[]): { values: FilterValues; dropped: string[] } {
  if (f === null || f === undefined) {
    return { values: defaultsOf(filters), dropped: [] };
  }
  const values: FilterValues = Object.fromEntries(filters.map((filter) => [filter.column, null]));
  const dropped: string[] = [];
  for (const entry of decodeFilters(f)) {
    const filter = filters.find((candidate) => candidate.column === entry.column);
    const value = filter ? fromDataFilter(filter.kind, entry) : null;
    if (filter && value) {
      values[filter.column] = value;
    } else {
      dropped.push(filter?.label ?? entry.column);
    }
  }
  return { values, dropped };
}

/** What `f` holds for these values: null at the defaults, so a plain link and a link at the defaults are the same. */
export function writeAddress(filters: DashboardFilter[], values: FilterValues): string | null {
  if (sameValues(filters, values, defaultsOf(filters))) {
    return null;
  }
  return encodeFilters(filters.filter((f) => values[f.column]).map((f) => toDataFilter(f.column, f.kind, values[f.column]!)));
}

/** The filters with a value in force, in the bar's order, each marked as ignored or not by a widget. */
export function activeFilters(filters: DashboardFilter[], values: FilterValues, ignored: readonly string[] = []): ActiveDashboardFilter[] {
  return filters.filter((f) => values[f.column]).map((filter) => ({ filter, value: values[filter.column]!, ignored: ignored.includes(filter.column) }));
}

/** What a widget's query is sent: the active filters it does not ignore. */
export function appliedOf(active: ActiveDashboardFilter[]): AppliedDashboardFilter[] {
  return active
    .filter((a) => !a.ignored)
    .map(({ filter, value }) =>
      operationOf(filter.kind) === 'in'
        ? { column: filter.column, kind: filter.kind, values: [...(value.values ?? [])] }
        : { column: filter.column, kind: filter.kind, min: value.min ?? null, max: value.max ?? null },
    );
}

type Translate = (key: string, params?: Record<string, unknown>) => string;

/** A filter's value as the bar shows it: "All" without one, the values chosen, or the range. */
export function describeValue(filter: DashboardFilter, value: DashboardFilterValue | null | undefined, t: Translate): string {
  if (!value) {
    return t('dashboardFilters.all');
  }
  if (operationOf(filter.kind) === 'in') {
    return (value.values ?? [])
      .map((v) => (filter.kind === 'boolean' ? t(`cleanData.${v}`) : v === '' ? t('cleanData.filter.emptyText') : v))
      .join(', ');
  }
  if (value.min && value.max) {
    return value.min === value.max ? value.min : t('dashboardFilters.range.both', { min: value.min, max: value.max });
  }
  return value.min ? t('dashboardFilters.range.min', { min: value.min }) : t('dashboardFilters.range.max', { max: value.max });
}
