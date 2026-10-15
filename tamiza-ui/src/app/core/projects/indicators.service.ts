import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { DataColumn, DataExport, DataFilter, ExportFormat } from './data.service';
import { IndicatorFormat } from './pipeline.service';

// Mirrors Tamiza.Api/Indicators/*. A widget is one row of tamiza.dashboard_widgets: a title, a chart type, a query over the
// table one indicator script published (config) and a place on the 12-column grid (layout).

export type ChartType = 'stat' | 'bar' | 'column' | 'line' | 'histogram' | 'pie' | 'map';

export const chartTypes: readonly ChartType[] = ['stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map'];

/** Bar, column, line, pie and map widgets plot one value per group, so they need a column to group by; a stat tile and a histogram do not. */
export function needsGroup(type: ChartType): boolean {
  return type !== 'stat' && type !== 'histogram';
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

/** The ratio of the sums of two numeric columns, such as a rate: `sum(numerator) / sum(denominator)`. */
export interface WidgetRatio {
  numerator: string;
  denominator: string;
}

/**
 * The order of a bar or column widget: by the plotted value or by a column, ascending or descending. `compareAs` says how a text,
 * integer or decimal column is compared, and is null for other types and for the plotted value (null on a saved widget: the
 * type's own way).
 */
export interface WidgetSort {
  by: 'value' | 'column';
  column: string | null;
  compareAs: 'text' | 'number' | null;
  direction: 'asc' | 'desc';
}

/** A widget's query. At most one of `measure` and `ratio` is set; with neither, the table's default measure is plotted. */
export interface WidgetConfig {
  indicator: string;
  measure: string | null;
  ratio: WidgetRatio | null;
  groupBy: string | null;
  filters: DataFilter[];
  /** Maps only. */
  geometryColumn: string | null;
  /** Histograms only; 10 when null. */
  buckets: number | null;
  /** Bar and column only; null is the group column's own order. */
  sort: WidgetSort | null;
}

export interface Widget {
  id: string;
  title: string;
  chartType: ChartType;
  config: WidgetConfig;
  layout: Layout;
  updatedAt: string;
}

/** An indicator of the project with its metadata and whether its table has been published. */
export interface IndicatorEntry {
  name: string;
  label: string;
  description: string | null;
  unit: string | null;
  format: IndicatorFormat;
  published: boolean;
  publishedAt: string | null;
  runId: string | null;
  rows: number | null;
}

export interface IndicatorsOverview {
  indicators: IndicatorEntry[];
  widgets: Widget[];
  limits: { maxWidgets: number; maxQueryRows: number };
}

/** The columns of an indicator's published table; `published` is false (and `columns` empty) while it has none. */
export interface IndicatorColumns {
  published: boolean;
  columns: DataColumn[];
  /** The columns a widget can plot as a measure or in a ratio. */
  numeric: string[];
  /** What a new widget starts with: `value` or the first numeric column, and the first column that is not numeric. */
  defaults: { measure: string | null; groupBy: string | null };
}

export interface QueryRow {
  group: string | null;
  value: number;
  geometry: string | null;
}

export interface QueryResult {
  published: boolean;
  rows: QueryRow[];
  rowLimitReached: boolean;
  /** What the widget names that the published table lacks (design D8). */
  missing: { indicator: boolean; columns: string[] };
}

export interface WidgetDraft {
  title: string;
  chartType: ChartType;
  config: WidgetConfig;
  layout?: Layout;
}

export type WidgetPatch = Partial<Pick<Widget, 'title' | 'chartType' | 'config' | 'layout'>>;

export function emptyConfig(indicator = ''): WidgetConfig {
  return { indicator, measure: null, ratio: null, groupBy: null, filters: [], geometryColumn: null, buckets: null, sort: null };
}

const base = '/api/v1/projects';

@Injectable({ providedIn: 'root' })
export class IndicatorsService {
  private readonly http = inject(HttpClient);

  overview(projectId: string): Observable<IndicatorsOverview> {
    return this.http.get<IndicatorsOverview>(this.url(projectId));
  }

  columns(projectId: string, indicator: string): Observable<IndicatorColumns> {
    return this.http.get<IndicatorColumns>(`${this.url(projectId)}/${encodeURIComponent(indicator)}/columns`);
  }

  /** Renders a saved widget, or previews a draft: the same query either way. The chart type lets a histogram plot a value per row. */
  query(projectId: string, config: WidgetConfig, chartType?: ChartType): Observable<QueryResult> {
    return this.http.post<QueryResult>(`${this.url(projectId)}/query`, chartType ? { config, chartType } : { config });
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

  /** Prepares a file with the whole published table of one indicator; the result has the single-use link that downloads it. */
  export(projectId: string, indicator: string, format: ExportFormat): Observable<DataExport> {
    return this.http.post<DataExport>(`${this.url(projectId)}/${encodeURIComponent(indicator)}/exports`, { format });
  }

  private url(projectId: string): string {
    return `${base}/${projectId}/indicators`;
  }
}
