import {
  DataColumn,
  DataFilter,
  DataQuery,
  DataSort,
  DataSource,
  defaultPageSize,
  FilterOp,
  pageSizes,
} from '../../core/projects/data.service';

/**
 * The view of the clean data dashboard as it lives in the page address (design D6): source, table, sort, page, page size and
 * the filters. Everything in it comes from the user's address bar, so it is validated before use and anything that no longer
 * applies is dropped and reported rather than failing the page.
 */
export interface DataView {
  source: DataSource | null;
  table: string | null;
  query: DataQuery;
}

export interface DataViewParams {
  source?: string | null;
  table?: string | null;
  sort?: string | null;
  page?: string | null;
  size?: string | null;
  f?: string | null;
}

const ops: FilterOp[] = ['contains', 'equals', 'in', 'range', 'empty', 'notEmpty'];
const maxFilters = 50;

export function emptyQuery(): DataQuery {
  return { filters: [], sort: null, page: 1, pageSize: defaultPageSize };
}

/** Reads the address parameters. Unknown or malformed values fall back to the defaults; filters are checked later against the columns. */
export function parseView(params: DataViewParams): DataView {
  const source = params.source === 'output' || params.source === 'clean' ? params.source : null;
  const size = Number(params.size);
  const page = Number(params.page);
  return {
    source,
    table: params.table ? params.table : null,
    query: {
      filters: decodeFilters(params.f),
      sort: parseSort(params.sort),
      page: Number.isInteger(page) && page >= 1 ? page : 1,
      pageSize: (pageSizes as readonly number[]).includes(size) ? size : defaultPageSize,
    },
  };
}

/** The address parameters of a view; a default is left out so a plain address stays short. */
export function viewParams(source: DataSource | null, table: string | null, query: DataQuery): Record<string, string | null> {
  return {
    source: source,
    table: table,
    sort: query.sort ? `${query.sort.column}:${query.sort.direction}` : null,
    page: query.page > 1 ? String(query.page) : null,
    size: query.pageSize !== defaultPageSize ? String(query.pageSize) : null,
    f: query.filters.length ? encodeFilters(query.filters) : null,
  };
}

function parseSort(text: string | null | undefined): DataSort | null {
  if (!text) {
    return null;
  }
  // A column name may hold a colon; the direction is what follows the last one.
  const at = text.lastIndexOf(':');
  const direction = text.slice(at + 1);
  if (at < 1 || (direction !== 'asc' && direction !== 'desc')) {
    return null;
  }
  return { column: text.slice(0, at), direction };
}

export function encodeFilters(filters: DataFilter[]): string {
  const bytes = new TextEncoder().encode(JSON.stringify(filters));
  let binary = '';
  bytes.forEach((byte) => (binary += String.fromCharCode(byte)));
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function decodeFilters(text: string | null | undefined): DataFilter[] {
  if (!text) {
    return [];
  }
  try {
    const padded = text.replace(/-/g, '+').replace(/_/g, '/');
    const binary = atob(padded + '='.repeat((4 - (padded.length % 4)) % 4));
    const json = new TextDecoder().decode(Uint8Array.from(binary, (c) => c.charCodeAt(0)));
    const parsed: unknown = JSON.parse(json);
    if (!Array.isArray(parsed)) {
      return [];
    }
    return parsed.slice(0, maxFilters).filter(isFilter).map(normalizeFilter);
  } catch {
    return [];
  }
}

function isFilter(value: unknown): value is DataFilter {
  const filter = value as Partial<DataFilter> | null;
  return !!filter && typeof filter.column === 'string' && typeof filter.op === 'string' && (ops as string[]).includes(filter.op);
}

/** Keeps only the fields a filter uses, with the right shapes. */
export function normalizeFilter(filter: DataFilter): DataFilter {
  const clean: DataFilter = { column: filter.column, op: filter.op };
  if (typeof filter.value === 'string') clean.value = filter.value;
  if (Array.isArray(filter.values)) clean.values = filter.values.filter((v): v is string => typeof v === 'string');
  if (typeof filter.min === 'string') clean.min = filter.min;
  if (typeof filter.max === 'string') clean.max = filter.max;
  return clean;
}

/** What the columns of the table allow: the filter operations by column type (design D2). */
export function operationsFor(column: DataColumn): FilterOp[] {
  switch (column.type) {
    case 'text':
      return ['contains', 'equals', 'in', 'empty', 'notEmpty'];
    case 'boolean':
      return ['in', 'empty', 'notEmpty'];
    default:
      return ['range', 'empty', 'notEmpty'];
  }
}

export interface Sanitized {
  query: DataQuery;
  /** Human-readable names of what was dropped: `filter:<column>` or `sort:<column>`. */
  dropped: string[];
}

/**
 * Removes the filters and the sort that name a column the table lacks, and filters whose operation the column's type does not
 * support (the table changed type, or the address was edited). Keeps the first filter of a column.
 */
export function sanitize(query: DataQuery, columns: DataColumn[]): Sanitized {
  const byName = new Map(columns.map((c) => [c.name, c]));
  const dropped: string[] = [];
  const seen = new Set<string>();
  const filters: DataFilter[] = [];
  for (const filter of query.filters) {
    const column = byName.get(filter.column);
    if (!column || !operationsFor(column).includes(filter.op) || seen.has(filter.column)) {
      dropped.push(`filter:${filter.column}`);
      continue;
    }
    seen.add(filter.column);
    filters.push(filter);
  }
  let sort = query.sort;
  if (sort && !byName.has(sort.column)) {
    dropped.push(`sort:${sort.column}`);
    sort = null;
  }
  return { query: { ...query, filters, sort }, dropped };
}

/** True when two queries ask for the same page. */
export function sameQuery(a: DataQuery, b: DataQuery): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}
