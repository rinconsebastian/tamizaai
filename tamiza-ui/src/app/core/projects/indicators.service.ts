import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { DataColumn, DataColumnType, DataExport, DataFilter, DataValue, DataValueCount, ExportFormat } from './data.service';
import { IndicatorFormat } from './pipeline.service';
import { DashboardEntry } from './projects.service';

// Mirrors Tamiza.Api/Indicators/*. A widget is one row of tamiza.dashboard_widgets: a title, a chart type, a query over the
// table one indicator script published (config) and a place on the 12-column grid (layout). It belongs to one of the project's
// own dashboards (tamiza.dashboards), each a grid of its own.

export type ChartType = 'stat' | 'bar' | 'column' | 'line' | 'histogram' | 'pie' | 'map' | 'heatmap' | 'pyramid' | 'likert' | 'crosstab';

export const chartTypes: readonly ChartType[] = ['stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map', 'heatmap', 'pyramid', 'likert', 'crosstab'];

/** Every kind but a stat tile and a histogram plots one value per group (or per pair of group and series), so it needs a column to group by. */
export function needsGroup(type: ChartType): boolean {
  return type !== 'stat' && type !== 'histogram';
}

/** Bar, column and line widgets may have a series column (`ChartTypes.TakesSeries` in tamiza-api). */
export function takesSeries(type: ChartType): boolean {
  return type === 'bar' || type === 'column' || type === 'line';
}

/** Heat map, population pyramid, diverging Likert and cross table widgets cannot be drawn without one (`ChartTypes.NeedsSeries`). */
export function needsSeries(type: ChartType): boolean {
  return type === 'heatmap' || type === 'pyramid' || type === 'likert' || type === 'crosstab';
}

/** Whether a widget of this kind has a series column at all, optional or required. */
export function hasSeries(type: ChartType): boolean {
  return takesSeries(type) || needsSeries(type);
}

/** How the bars or columns of a widget with a series are laid out: side by side, end to end, or end to end as shares of 100% (`SeriesLayouts`). */
export type SeriesLayout = 'grouped' | 'stacked' | 'percent';

export const seriesLayouts: readonly SeriesLayout[] = ['grouped', 'stacked', 'percent'];

/** Which side of the center of a Likert widget a value of its scale falls on (`LikertSides`). */
export type LikertSide = 'negative' | 'neutral' | 'positive';

export const likertSides: readonly LikertSide[] = ['negative', 'neutral', 'positive'];

/** The most and the fewest values a Likert scale has, the most on one side, and the most series a heat map or a cross table shows (design D2, D3). */
export const maxLikertValues = 11;
export const minLikertValues = 2;
export const maxLikertSide = 5;
export const maxSeriesAxis = 8;
export const maxSeriesGrid = 30;

/** A value of a Likert scale, as text (it is compared with the series column's text), and the side it falls on. */
export interface LikertStep {
  value: string;
  side: LikertSide;
}

/** The two values of the series column a population pyramid draws, one to the left and one to the right of its axis. */
export interface WidgetPyramid {
  left: string;
  right: string;
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
  /**
   * The second dimension (design D1, D12): the column whose values are the series of bar, column and line widgets (optional) and of heat
   * map, pyramid, Likert and cross table widgets (required). The four fields below are left out of a query and of a saved widget that
   * has none, so a widget without a series sends and saves exactly what it did before.
   */
  seriesBy?: string | null;
  /** Bar and column with a series only; null is `grouped`. */
  layout?: SeriesLayout | null;
  /** Population pyramids only. */
  pyramid?: WidgetPyramid | null;
  /** Diverging Likert widgets only: the scale's values in order. */
  scale?: LikertStep[];
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

/**
 * What a series stands for in the response (design D3, D12): a `value` of the series column, `none` for the rows that have no value
 * there ("(none)") or `other` for the series folded past the limit ("Other").
 */
export type SeriesKind = 'value' | 'none' | 'other';

/** One series of a widget with a series, in display order: natural order, then `none`, then `other`. `value` is null for the last two. */
export interface QuerySeries {
  value: string | null;
  kind: SeriesKind;
}

/** The totals of a widget with a series, computed from the rows (design D12): never a sum or a mean of the plotted cells. */
export interface QueryTotals {
  /** One per group, and the authority on the order of the groups. */
  groups: { group: string | null; value: number | null; rows: number }[];
  /** One per entry of `QueryResult.series`, in the same order; `value` null and `rows` 0 for a series with no rows. */
  series: { series: string | null; kind: SeriesKind; value: number | null; rows: number }[];
  all: { value: number | null; rows: number };
}

export interface QueryRow {
  group: string | null;
  value: number;
  geometry: string | null;
  /** A widget with a series only: the cell's series value (null with `seriesKind` none or other) and its kind. */
  series?: string | null;
  seriesKind?: SeriesKind;
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
  /** A widget with a series only (design D12): the series in display order, the totals, and for a pyramid or a Likert widget the rows left out. */
  series?: QuerySeries[];
  totals?: QueryTotals;
  leftOut?: number;
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

// Mirrors Tamiza.Api/Reports/ReportEndpoints.cs: a dashboard's scheduled email (tamiza.dashboard_reports), at most one per dashboard.

/** The most recipients a scheduled email has (`ck_dashboard_reports_recipients`). */
export const maxReportRecipients = 20;

/** What a scheduled email is sent to, and when. */
export interface DashboardReportSettings {
  /** 1 to 20 addresses of any domain; the API keeps once those that differ only in case. */
  recipients: string[];
  /** ISO weekdays, 1 Monday to 7 Sunday, ascending. */
  days: number[];
  /** `HH:mm`, in the project's time zone. */
  time: string;
  /** False while paused: no scheduled email is sent, sending it now still works. */
  active: boolean;
  /** The interface locale of the member who saved the settings: the email's language and regional format. */
  locale: string;
}

/**
 * Why the last send failed: an outcome of the SMTP server (`email.unavailable`, `email.authFailed`, `email.rejected`, with the
 * refused address when a recipient was refused), `reports.nothingToSend` or `reports.failed`. Never the server's own words.
 */
export interface DashboardReportFailure {
  code: string;
  at: string;
  address: string | null;
}

/** A dashboard's scheduled email: its settings (null when it has none) and the results of its sends. */
export interface DashboardReport {
  settings: DashboardReportSettings | null;
  /** The project's IANA time zone, which the time is in. */
  timeZone: string;
  /** The member who saved the settings last: replies go to them. */
  updatedBy: { name: string } | null;
  updatedAt: string | null;
  lastSentAt: string | null;
  /** The last attempt's failure; a later successful send clears it. */
  lastFailure: DashboardReportFailure | null;
}

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

  // A dashboard's scheduled email, for analysts and admins. Saving or sending it while email is off is a 503 `reports.unavailable`.

  getReport(projectId: string, dashboardId: string): Observable<DashboardReport> {
    return this.http.get<DashboardReport>(this.reportUrl(projectId, dashboardId));
  }

  /** Creates or replaces the dashboard's scheduled email, and answers it as `getReport` does. */
  saveReport(projectId: string, dashboardId: string, settings: DashboardReportSettings): Observable<DashboardReport> {
    return this.http.put<DashboardReport>(this.reportUrl(projectId, dashboardId), settings);
  }

  removeReport(projectId: string, dashboardId: string): Observable<void> {
    return this.http.delete<void>(this.reportUrl(projectId, dashboardId));
  }

  /**
   * Queues the saved email to be sent now, whatever its schedule (202). Refused with a 409 `reports.none` when there are no settings,
   * and `reports.tooSoon` within 10 minutes of the last request.
   */
  sendReportNow(projectId: string, dashboardId: string): Observable<void> {
    return this.http.post<void>(`${this.reportUrl(projectId, dashboardId)}/send`, null);
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

  private reportUrl(projectId: string, dashboardId: string): string {
    return `${this.dashboardUrl(projectId, dashboardId)}/report`;
  }

  private widgetsUrl(projectId: string, dashboardId: string): string {
    return `${this.dashboardUrl(projectId, dashboardId)}/widgets`;
  }
}
