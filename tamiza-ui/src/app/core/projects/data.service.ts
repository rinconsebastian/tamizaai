import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Mirrors Tamiza.Api/CleanData/*. Days are `yyyy-MM-dd` strings and timestamps are ISO 8601 strings in the project's time
// zone, never converted to dates, so no browser zone can move them. Numbers and true/false arrive as JSON numbers and booleans.

export type DataSource = 'output' | 'clean';
export type DataColumnType = 'integer' | 'decimal' | 'boolean' | 'date' | 'timestamp' | 'text';

export interface DataTableInfo {
  name: string;
  /** What the run that published it recorded; null when no run did. */
  rowCount: number | null;
}

export interface DataSourceInfo {
  source: DataSource;
  publishedAt: string | null;
  runId: string | null;
  tables: DataTableInfo[];
}

export interface DataOverview {
  /** Output first. Empty when no run has published clean or output tables. */
  sources: DataSourceInfo[];
  defaultSource: DataSource | null;
  timeZone: string;
}

export interface DataColumn {
  name: string;
  type: DataColumnType;
  /** The question's label, for a flattened column that has one. */
  label: string | null;
}

export type DataValue = string | number | boolean | null;

export interface DataPage {
  columns: DataColumn[];
  rows: DataValue[][];
  /** `[row, column]` of each text value cut at 500 characters. */
  cutCells: [number, number][];
  /** The rows that match the filters. */
  total: number;
  page: number;
  pageSize: number;
  publishedAt: string | null;
  runId: string | null;
  timeZone: string;
}

export type FilterOp = 'contains' | 'equals' | 'in' | 'range' | 'empty' | 'notEmpty';

export interface DataFilter {
  column: string;
  op: FilterOp;
  value?: string;
  values?: string[];
  min?: string;
  max?: string;
}

export interface DataSort {
  column: string;
  direction: 'asc' | 'desc';
}

export interface DataQuery {
  filters: DataFilter[];
  sort: DataSort | null;
  page: number;
  pageSize: number;
}

export interface DataValueCount {
  value: string;
  count: number;
}

export type ExportFormat = 'csv' | 'xlsx';

export interface DataExport {
  downloadUrl: string;
  fileName: string;
  rowCount: number;
  cutCells: number;
  expiresAt: string;
}

export const pageSizes = [25, 50, 100] as const;
export const defaultPageSize = 50;

const base = '/api/v1/projects';

@Injectable({ providedIn: 'root' })
export class DataService {
  private readonly http = inject(HttpClient);

  overview(projectId: string): Observable<DataOverview> {
    return this.http.get<DataOverview>(`${base}/${projectId}/data`);
  }

  /** One page of a published table with the filters and sort applied on the server. */
  query(projectId: string, source: DataSource, table: string, query: DataQuery): Observable<DataPage> {
    return this.http.post<DataPage>(`${this.tableUrl(projectId, source, table)}/query`, {
      filters: query.filters,
      sort: query.sort,
      page: query.page,
      pageSize: query.pageSize,
    });
  }

  /** The most frequent values of a text or true/false column, optionally only those that contain `search`. */
  values(projectId: string, source: DataSource, table: string, column: string, search?: string): Observable<{ values: DataValueCount[] }> {
    let params = new HttpParams();
    if (search) {
      params = params.set('search', search);
    }
    return this.http.get<{ values: DataValueCount[] }>(`${this.tableUrl(projectId, source, table)}/columns/${encodeURIComponent(column)}/values`, { params });
  }

  /** Prepares a file with the whole table, filters and sort applied; the result has the single-use link that downloads it. */
  export(projectId: string, source: DataSource, table: string, format: ExportFormat, query: Pick<DataQuery, 'filters' | 'sort'>): Observable<DataExport> {
    return this.http.post<DataExport>(`${this.tableUrl(projectId, source, table)}/exports`, { format, filters: query.filters, sort: query.sort });
  }

  private tableUrl(projectId: string, source: DataSource, table: string): string {
    return `${base}/${projectId}/data/${source}/${encodeURIComponent(table)}`;
  }
}
