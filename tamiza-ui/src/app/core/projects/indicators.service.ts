import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { DataColumn, DataExport, DataFilter, ExportFormat } from './data.service';

// Mirrors Tamiza.Api/Indicators/*. A widget is one row of tamiza.dashboard_widgets: a title, a chart type, a query over the
// published indicator table (config) and a place on the 12-column grid (layout).

export type ChartType = 'stat' | 'bar' | 'column' | 'line' | 'histogram' | 'pie' | 'map';
export type ValueField = 'value' | 'numerator' | 'denominator';

export const chartTypes: readonly ChartType[] = ['stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map'];
export const valueFields: readonly ValueField[] = ['value', 'numerator', 'denominator'];

/** Stat tiles, pie charts and maps plot exactly one indicator; the others one or more (design D9). */
export function takesOneIndicator(type: ChartType): boolean {
  return type === 'stat' || type === 'pie' || type === 'map';
}

export const gridColumns = 12;
export const maxWidgetHeight = 40;
export const maxBuckets = 100;
export const defaultBuckets = 10;
export const maxFilters = 50;
export const maxTitleLength = 200;

/** A place on the grid, in grid units: `x + w` never passes the 12 columns. */
export interface Layout {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface WidgetConfig {
  indicators: string[];
  groupBy: string;
  valueField: ValueField;
  filters: DataFilter[];
  /** Maps only. */
  geometryColumn: string | null;
  /** Histograms only; 10 when null. */
  buckets: number | null;
}

export interface Widget {
  id: string;
  title: string;
  chartType: ChartType;
  config: WidgetConfig;
  layout: Layout;
  updatedAt: string;
}

export interface IndicatorsOverview {
  published: boolean;
  publishedAt: string | null;
  runId: string | null;
  widgets: Widget[];
  limits: { maxWidgets: number; maxQueryRows: number };
}

export interface IndicatorsColumns {
  published: boolean;
  columns: DataColumn[];
  indicators: string[];
}

export interface QueryRow {
  indicator: string;
  group: string | null;
  value: number;
  geometry: string | null;
}

export interface QueryResult {
  published: boolean;
  rows: QueryRow[];
  rowLimitReached: boolean;
  /** What the widget names that the published table lacks (design D8). */
  missing: { indicators: string[]; columns: string[] };
}

export interface WidgetDraft {
  title: string;
  chartType: ChartType;
  config: WidgetConfig;
  layout?: Layout;
}

export type WidgetPatch = Partial<Pick<Widget, 'title' | 'chartType' | 'config' | 'layout'>>;

export function emptyConfig(): WidgetConfig {
  return { indicators: [], groupBy: 'disaggregation', valueField: 'value', filters: [], geometryColumn: null, buckets: null };
}

const base = '/api/v1/projects';

@Injectable({ providedIn: 'root' })
export class IndicatorsService {
  private readonly http = inject(HttpClient);

  overview(projectId: string): Observable<IndicatorsOverview> {
    return this.http.get<IndicatorsOverview>(this.url(projectId));
  }

  columns(projectId: string): Observable<IndicatorsColumns> {
    return this.http.get<IndicatorsColumns>(`${this.url(projectId)}/columns`);
  }

  /** Renders a saved widget, or previews a draft: the same query either way. */
  query(projectId: string, config: WidgetConfig): Observable<QueryResult> {
    return this.http.post<QueryResult>(`${this.url(projectId)}/query`, config);
  }

  create(projectId: string, draft: WidgetDraft): Observable<Widget> {
    return this.http.post<Widget>(`${this.url(projectId)}/widgets`, draft);
  }

  /** Partial: a move sends only the layout. */
  update(projectId: string, widgetId: string, patch: WidgetPatch): Observable<Widget> {
    return this.http.patch<Widget>(`${this.url(projectId)}/widgets/${widgetId}`, patch);
  }

  remove(projectId: string, widgetId: string): Observable<void> {
    return this.http.delete<void>(`${this.url(projectId)}/widgets/${widgetId}`);
  }

  /** Prepares a file with the whole published table; the result has the single-use link that downloads it. */
  export(projectId: string, format: ExportFormat): Observable<DataExport> {
    return this.http.post<DataExport>(`${this.url(projectId)}/exports`, { format });
  }

  private url(projectId: string): string {
    return `${base}/${projectId}/indicators`;
  }
}
