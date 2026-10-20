import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { DataColumn, DataColumnType, DataExport, DataFilter, DataValue, DataValueCount, ExportFormat } from './data.service';
import { IndicatorFormat } from './pipeline.service';
import { DashboardEntry } from './projects.service';

// Mirrors Tamiza.Api/Indicators/*. A widget is one row of tamiza.dashboard_widgets: a title, a chart type, a query over the
// table one indicator script published (config) and a place on the 12-column grid (layout). It belongs to one of the project's
// own dashboards (tamiza.dashboards), each a grid of its own.

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
/** A dashboard's name, once trimmed (`DashboardValidation.MaxNameLength`). */
export const maxDashboardNameLength = 40;
/** The most dashboards a project can hold (`IndicatorDashboardOptions.MaxDashboards`), until the overview says so. */
export const maxDashboards = 7;
export const maxTooltipItems = 5;
export const maxTooltipLabel = 80;
/** The most filters a dashboard has (`DashboardFilterRules.MaxFilters`), and so the most a widget ignores. */
export const maxDashboardFilters = 8;
/** A dashboard filter's label, once trimmed (`DashboardFilterRules.MaxLabelLength`). */
export const maxDashboardFilterLabelLength = 40;

/**
 * The kinds of dashboard filter (`DashboardFilterKinds`). Each narrows a family of column types: text, true/false, integer or decimal
 * (number), date or timestamp (date). Text and true/false filters choose values; number and date filters take a range.
 */
export type DashboardFilterKind = 'text' | 'boolean' | 'number' | 'date';

export const dashboardFilterKinds: readonly DashboardFilterKind[] = ['text', 'boolean', 'number', 'date'];

/** A value of a dashboard filter: `values` for a text or true/false filter, a range (either end optional) for a number or date filter. */
export interface DashboardFilterValue {
  values?: string[] | null;
  min?: string | null;
  max?: string | null;
}

/** One of a dashboard's filters. `column` is unique in the dashboard; the label is shown as saved, never translated. */
export interface DashboardFilter {
  column: string;
  label: string;
  kind: DashboardFilterKind;
  /** In force when the dashboard opens without values in its address. */
  default: DashboardFilterValue | null;
}

/** A dashboard filter's value in force on the member's screen, sent with a widget's query when the widget does not ignore it. */
export interface AppliedDashboardFilter extends DashboardFilterValue {
  column: string;
  kind: DashboardFilterKind;
}

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

/** How a numeric tooltip column is written, as the indicator formats do; a column that is not a number takes none. */
export type TooltipFormat = 'count' | 'decimal' | 'percent';

/** A column of the table a map shows in its tooltip, in the order listed, with an optional label in place of the column's name. */
export interface WidgetTooltipItem {
  column: string;
  label: string | null;
  format: TooltipFormat | null;
}

/** Which background map a map widget draws under its shapes: a kind, not a provider, so a widget keeps its meaning when the operator changes provider. */
export type BasemapKind = 'streets' | 'satellite';

export const basemapKinds: readonly BasemapKind[] = ['streets', 'satellite'];

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
  /** Maps only: up to `maxTooltipItems` columns shown in the tooltip. A query sends their names only (see `queryFor`). */
  tooltip: WidgetTooltipItem[];
  /** Maps only; null draws none. Saved with the widget, never part of the query. */
  basemap: BasemapKind | null;
  /**
   * The columns of the dashboard filters the widget ignores, kept by column on any dashboard. Saved with the widget, never part of the
   * query: the page leaves those filters out of what it sends. (A config from an API that predates it has none.)
   */
  ignoredDashboardFilters: string[];
}

export interface Widget {
  id: string;
  /** The dashboard the widget is on. */
  dashboardId: string;
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

/** What every dashboard of the project shows: the indicators, and the limits (`maxWidgets` counts the widgets of one dashboard). */
export interface IndicatorsOverview {
  indicators: IndicatorEntry[];
  limits: { maxWidgets: number; maxQueryRows: number; maxDashboards: number };
}

/** One of the project's dashboards with its filters, in order, and its widgets, ordered top to bottom then left to right. */
export interface Dashboard extends DashboardEntry {
  filters: DashboardFilter[];
  widgets: Widget[];
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
  /** Maps only: how many rows of the table the group combines. */
  rows?: number;
  /** Maps only: the value of each tooltip column, in the widget's order (see `QueryResult.tooltipTypes`). */
  tooltip?: DataValue[];
}

export interface QueryResult {
  published: boolean;
  rows: QueryRow[];
  rowLimitReached: boolean;
  /** What the widget names that the published table lacks (design D8). */
  missing: { indicator: boolean; columns: string[] };
  /** Maps only: the type of each tooltip column, so a value is written without asking the catalog. */
  tooltipTypes?: DataColumnType[];
  /**
   * The columns of the dashboard filters sent that did not narrow the rows: the table has no such column, or has it with another kind
   * of type. Only when the query sent dashboard filters and drew something.
   */
  notFiltered?: string[];
}

export interface WidgetDraft {
  title: string;
  chartType: ChartType;
  config: WidgetConfig;
  layout?: Layout;
}

/** A `dashboardId` other than the widget's moves it there, below that dashboard's widgets; a layout sent with it is ignored. */
export type WidgetPatch = Partial<Pick<Widget, 'title' | 'chartType' | 'config' | 'layout' | 'dashboardId'>>;

export function emptyConfig(indicator = ''): WidgetConfig {
  return {
    indicator,
    measure: null,
    ratio: null,
    groupBy: null,
    filters: [],
    geometryColumn: null,
    buckets: null,
    sort: null,
    tooltip: [],
    basemap: null,
    ignoredDashboardFilters: [],
  };
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

  /**
   * Renders a saved widget, or previews a draft: the same query either way. The chart type lets a histogram plot a value per row. The
   * dashboard filters in force that the widget does not ignore narrow the rows too; they are sent only when there are some.
   */
  query(projectId: string, config: WidgetConfig, chartType?: ChartType, dashboardFilters: AppliedDashboardFilter[] = []): Observable<QueryResult> {
    return this.http.post<QueryResult>(`${this.url(projectId)}/query`, {
      config,
      ...(chartType ? { chartType } : {}),
      ...(dashboardFilters.length ? { dashboardFilters } : {}),
    });
  }

  /** Prepares a file with the whole published table of one indicator; the result has the single-use link that downloads it. */
  export(projectId: string, indicator: string, format: ExportFormat): Observable<DataExport> {
    return this.http.post<DataExport>(`${this.url(projectId)}/${encodeURIComponent(indicator)}/exports`, { format });
  }

  // Dashboards. A dashboard the project no longer has is a 404 with the code `dashboards.notFound`, on its own routes and its widgets'.

  dashboard(projectId: string, dashboardId: string): Observable<Dashboard> {
    return this.http.get<Dashboard>(this.dashboardUrl(projectId, dashboardId));
  }

  /** Placed after the project's other dashboards. */
  addDashboard(projectId: string, name: string): Observable<DashboardEntry> {
    return this.http.post<DashboardEntry>(this.dashboardsUrl(projectId), { name });
  }

  renameDashboard(projectId: string, dashboardId: string, name: string): Observable<DashboardEntry> {
    return this.http.patch<DashboardEntry>(this.dashboardUrl(projectId, dashboardId), { name });
  }

  /** Removes the dashboard with its widgets; the project's only dashboard is refused (`dashboards.lastOne`). */
  removeDashboard(projectId: string, dashboardId: string): Observable<void> {
    return this.http.delete<void>(this.dashboardUrl(projectId, dashboardId));
  }

  /**
   * Puts the dashboards in the order of `dashboardIds` and answers the new list. `expectedIds` is the order the page shows: when the
   * project's order differs, nothing changes and the answer is a 409 `dashboards.orderChanged`.
   */
  reorderDashboards(projectId: string, expectedIds: string[], dashboardIds: string[]): Observable<DashboardEntry[]> {
    return this.http.put<DashboardEntry[]>(`${this.dashboardsUrl(projectId)}/order`, { expectedIds, dashboardIds });
  }

  /** Copies the dashboard, its filters and its widgets under a new name, right after the original. */
  duplicateDashboard(projectId: string, dashboardId: string, name: string): Observable<DashboardEntry> {
    return this.http.post<DashboardEntry>(`${this.dashboardUrl(projectId, dashboardId)}/copies`, { name });
  }

  /** Replaces the dashboard's filters with `filters`, in their order, and answers them as saved (labels trimmed). */
  saveDashboardFilters(projectId: string, dashboardId: string, filters: DashboardFilter[]): Observable<DashboardFilter[]> {
    return this.http
      .put<{ filters: DashboardFilter[] }>(`${this.dashboardUrl(projectId, dashboardId)}/filters`, { filters })
      .pipe(map((answer) => answer.filters));
  }

  /**
   * The most frequent values of a text column across the indicator tables of the dashboard's widgets, optionally only those that
   * contain `search`. Empty when no table has the column as text.
   */
  dashboardValues(projectId: string, dashboardId: string, column: string, search?: string): Observable<{ values: DataValueCount[] }> {
    let params = new HttpParams().set('column', column);
    if (search) {
      params = params.set('search', search);
    }
    return this.http.get<{ values: DataValueCount[] }>(`${this.dashboardUrl(projectId, dashboardId)}/values`, { params });
  }

  // Widgets, on the dashboard they belong to.

  create(projectId: string, dashboardId: string, draft: WidgetDraft): Observable<Widget> {
    return this.http.post<Widget>(this.widgetsUrl(projectId, dashboardId), draft);
  }

  /** Partial: a drag sends only the layout. */
  update(projectId: string, dashboardId: string, widgetId: string, patch: WidgetPatch): Observable<Widget> {
    return this.http.patch<Widget>(`${this.widgetsUrl(projectId, dashboardId)}/${widgetId}`, patch);
  }

  remove(projectId: string, dashboardId: string, widgetId: string): Observable<void> {
    return this.http.delete<void>(`${this.widgetsUrl(projectId, dashboardId)}/${widgetId}`);
  }

  private url(projectId: string): string {
    return `${base}/${projectId}/indicators`;
  }

  private dashboardsUrl(projectId: string): string {
    return `${base}/${projectId}/dashboards`;
  }

  private dashboardUrl(projectId: string, dashboardId: string): string {
    return `${this.dashboardsUrl(projectId)}/${dashboardId}`;
  }

  private widgetsUrl(projectId: string, dashboardId: string): string {
    return `${this.dashboardUrl(projectId, dashboardId)}/widgets`;
  }
}
