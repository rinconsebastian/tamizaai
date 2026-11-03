import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { DataColumn, DataColumnType, DataExport, DataFilter, DataValue, DataValueCount, ExportFormat } from './data.service';
import { IndicatorFormat, Person } from './pipeline.service';
import { DashboardEntry } from './projects.service';

// Mirrors Tamiza.Api/Indicators/*. A widget is one row of tamiza.dashboard_widgets: a title, a chart type, a query over the
// table one indicator script published (config) and a place on the 12-column grid (layout). It belongs to one of the project's
// own dashboards (tamiza.dashboards), each a grid of its own.

export type ChartType = 'stat' | 'bar' | 'column' | 'line' | 'histogram' | 'pie' | 'map' | 'heatmap' | 'pyramid' | 'likert' | 'crosstab' | 'boxplot';

export const chartTypes: readonly ChartType[] = ['stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map', 'heatmap', 'pyramid', 'likert', 'crosstab', 'boxplot'];

/**
 * Every kind but a stat tile, a histogram and a box plot plots one value per group (or per pair of group and series), so it needs a
 * column to group by. Those three may take the rows as they are: a box plot without a group is one box over every row.
 */
export function needsGroup(type: ChartType): boolean {
  return type !== 'stat' && type !== 'histogram' && type !== 'boxplot';
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

/** How a box plot is drawn: its first box on the left (the default) or at the top (`BoxOrientations`). */
export type BoxOrientation = 'vertical' | 'horizontal';

export const boxOrientations: readonly BoxOrientation[] = ['vertical', 'horizontal'];

/** The most outliers a box draws: the ones farthest from its median, the rest only counted (`WidgetConfig.MaxBoxOutliers`). */
export const maxBoxOutliers = 100;

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

/** A dashboard filter's value in force on the member's screen, sent to export a widget. `ignored` when the widget ignores that filter. */
export interface WidgetExportFilter extends AppliedDashboardFilter {
  ignored: boolean;
}

/**
 * What the screen sends to get a widget's workbook (add-widget-export): the settings it shows, not the widget's id, so a widget of a
 * version the dashboard no longer has can be exported. `version` is the dashboard version being viewed; left out on the current one.
 */
export interface WidgetExportRequest {
  format: 'xlsx';
  title: string;
  chartType: ChartType;
  /** The widget's query, as `queryFor` builds it. */
  config: WidgetConfig;
  filters: WidgetExportFilter[];
  /** The interface's language and region, such as `es-CO`: the workbook's own words and formats follow it. */
  locale: string;
  version?: number;
}

/** A prepared widget workbook: the single-use link that downloads it, and what the file read. */
export interface WidgetExportResult {
  downloadUrl: string;
  fileName: string;
  rowCount: number;
  /** When the widget's indicator was last published, as the file read it; null when it has not been. */
  publishedAt: string | null;
  expiresAt: string;
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

// Mirrors Tamiza.Api/Indicators/Palettes.cs (add-dashboard-palettes, design D2, D3): a dashboard has one palette of eight categorical
// colors, a widget names its color by its place in it (1 to 8) and a map or a heat map names a sequential scale. Neither is a color code:
// the colors themselves are the CSS custom properties of styles.css, which `chart-colors.ts` names.

/** The palettes a dashboard offers, `tamiza` being today's colors and the default (`Palettes.All`). */
export type PaletteId = 'tamiza' | 'institutional' | 'earth' | 'vivid';

export const paletteIds: readonly PaletteId[] = ['tamiza', 'institutional', 'earth', 'vivid'];

/** The sequential scales a map or a heat map can be drawn on (`Palettes.Scales`). */
export type ScaleId = 'blue' | 'teal' | 'green' | 'orange' | 'red' | 'purple' | 'gray';

export const scaleIds: readonly ScaleId[] = ['blue', 'teal', 'green', 'orange', 'red', 'purple', 'gray'];

/** The categorical colors of a palette, numbered 1 to `paletteSize`; a color kept by value or a widget's color is one of these numbers. */
export const paletteSize = 8;
/** The most colors kept by value a dashboard holds (`Palettes.MaxValueColors`). */
export const maxValueColors = 64;

/** A color kept for a value of a column: wherever that column decides a color on the dashboard, the value has color `color` (1 to 8). */
export interface ValueColor {
  column: string;
  value: string;
  color: number;
}

/** What a dashboard colors its widgets with: its palette, and the colors kept by value, in the order they were saved. */
export interface DashboardColors {
  palette: PaletteId;
  valueColors: ValueColor[];
}

/**
 * A widget's own color setting (design D3), kept apart from its query so that changing it never queries again. `color` is the place in
 * the dashboard's palette (1 to 8; 1 when unset) of a bar, column or line chart with no series, of a histogram and of a box plot;
 * `ramp` the sequential scale of a map or a heat map (the palette's own scale when unset); `orientation` the direction of a box plot
 * (vertical when unset; a saved `vertical` is kept as it is). A key with no value is left out.
 */
export interface WidgetDisplay {
  color?: number;
  ramp?: ScaleId;
  orientation?: BoxOrientation;
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
  /** Bar and column (by the plotted value or a column), and box plot (by its median only, with a group column); null is the group column's own order. */
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
   * The widget's color and sequential scale (add-dashboard-palettes, design D3). Saved with the widget for the kinds that take them,
   * never part of the query: the page leaves it out of what it sends, so a color change never queries. (A config from an API that
   * predates palettes has none.)
   */
  display?: WidgetDisplay | null;
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

/**
 * One of the project's dashboards with its filters, in order, its colors (absent from an API that predates palettes, add-dashboard-palettes
 * design D5: read as `tamiza` and none) and its widgets, ordered top to bottom then left to right.
 */
export interface Dashboard extends DashboardEntry, Partial<DashboardColors> {
  filters: DashboardFilter[];
  widgets: Widget[];
  /** The number of its newest version, null while it has none (add-dashboard-versions, design D9). */
  version: number | null;
}

// Mirrors the version routes of Tamiza.Api/Indicators/DashboardEndpoints.cs (add-dashboard-versions, design D2, D9): every change saved to a dashboard
// records it as it is after the change (tamiza.dashboard_versions), and a dashboard keeps its 10 newest versions, the current one
// included (`DashboardVersions.Kept`).

/**
 * What a change to a dashboard was (design D2): `initial` holds the state before version history, `unrecorded` a state a restore
 * found that no version held, `restored` a restore of the version it names.
 */
export type DashboardChangeKind =
  | 'initial'
  | 'created'
  | 'duplicated'
  | 'renamed'
  | 'filtersChanged'
  | 'colorsChanged'
  | 'widgetAdded'
  | 'widgetChanged'
  | 'widgetRemoved'
  | 'widgetMovedOut'
  | 'widgetMovedIn'
  | 'layoutChanged'
  | 'restored'
  | 'unrecorded';

export const dashboardChangeKinds: readonly DashboardChangeKind[] = [
  'initial',
  'created',
  'duplicated',
  'renamed',
  'filtersChanged',
  'colorsChanged',
  'widgetAdded',
  'widgetChanged',
  'widgetRemoved',
  'widgetMovedOut',
  'widgetMovedIn',
  'layoutChanged',
  'restored',
  'unrecorded',
];

/**
 * One thing a version changed. Names and titles are as they were at the time, and are member text, shown as text: `widget` the
 * widget's title (before a removal, after any other change), `dashboard` the other dashboard of a move, the original of a duplicate
 * or the new name of a rename, `version` the number a restore brought back. A field the kind has not is left out.
 */
export interface DashboardChange {
  kind: DashboardChangeKind;
  widget?: string;
  dashboard?: string;
  version?: number;
}

/** Where a version was made: in Tamiza (`app`, restoring from the page included) or by an AI client through MCP. */
export type DashboardVersionOrigin = 'app' | 'mcp';

/** A kept version of a dashboard, as the version history lists it. */
export interface DashboardVersionSummary {
  number: number;
  /** The newest version: the dashboard as it is. */
  current: boolean;
  name: string;
  widgetCount: number;
  filterCount: number;
  /** At most 20, in the order they were made; `moreChanges` counts the ones left out. */
  changes: DashboardChange[];
  moreChanges: number;
  /** The version a restore brought back, null for any other version. */
  restoredFrom: number | null;
  origin: DashboardVersionOrigin;
  /** The request the AI client gave (its `change`); null for `app`. */
  note: string | null;
  author: Person;
  /** The first change the version holds. */
  createdAt: string;
  /** The last change the version holds: the time the page shows for a version (design D11). */
  savedAt: string;
}

/** The versions a dashboard keeps, newest first, and the number of the current one. */
export interface DashboardVersionList {
  current: number;
  versions: DashboardVersionSummary[];
}

/** A widget as a version holds it: the widget's id when the version was made, and its settings then. */
export interface VersionWidget {
  id: string;
  title: string;
  chartType: ChartType;
  /** Read as a saved widget's config is, so a widget view draws it unchanged. */
  config: WidgetConfig;
  layout: Layout;
}

/**
 * A kept version with what it holds: its name, its filters as a dashboard returns them, its colors (absent from an API that predates
 * palettes: `tamiza` and none) and its widgets in reading order.
 */
export interface DashboardVersionDetails extends Omit<DashboardVersionSummary, 'widgetCount' | 'filterCount'>, Partial<DashboardColors> {
  filters: DashboardFilter[];
  widgets: VersionWidget[];
}

/** A widget of the version restored that was on another dashboard by then: it came back as a copy, and that dashboard keeps it. */
export interface RestoredCopy {
  originalId: string;
  copyId: string;
  title: string;
  /** The dashboard that still holds the original. */
  dashboardId: string;
  dashboardName: string;
}

/**
 * What a restore did (design D7). `restored` is false when the dashboard already was as in the version: nothing changed, and
 * `version` is the current one. Otherwise `version` is the new version the restore made. `nameKept` says another dashboard has the
 * version's name, so the dashboard kept its own; `dashboard` is the dashboard as `dashboard()` reads it.
 */
export interface DashboardRestore {
  restored: boolean;
  version: DashboardVersionSummary;
  nameKept: boolean;
  copies: RestoredCopy[];
  dashboard: Dashboard;
}

/** A widget's new place, as one entry of a dashboard's layout save (design D8). */
export interface WidgetLayoutItem {
  id: string;
  layout: Layout;
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

/** One box of a box plot, computed over every value of its group (`IndicatorBox` in tamiza-api). */
export interface QueryBox {
  /** The group's value; null for the rows with no value in it, and for the one box of a widget with no group column. */
  group: string | null;
  values: number;
  min: number;
  q1: number;
  median: number;
  q3: number;
  max: number;
  mean: number;
  lowerWhisker: number;
  upperWhisker: number;
  outlierCount: number;
  /** At most `maxBoxOutliers` of them, the ones farthest from the median, in ascending order. */
  outliers: number[];
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
  /** A box plot only: its boxes in display order. `rows` then holds one row per box, its value the median and its `rows` the box's number of values. */
  boxes?: QueryBox[];
  /** A widget with a series only (design D12): the series in display order, the totals, and for a pyramid or a Likert widget the rows left out. */
  series?: QuerySeries[];
  /**
   * A pie only (add-dashboard-palettes, design D8): the groups of the widget's own rows, not the dashboard filters', that have a total above
   * zero, largest total first with the missing group (`none`) in its place; at most 8 entries, so with more groups the largest 7 and then
   * `other`. It decides each slice's color and which slices fold into "Other". Absent from an API that predates it.
   */
  domain?: QuerySeries[];
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

  /**
   * Prepares the workbook of one widget on a dashboard, with the dashboard filters in force, and answers the single-use link that
   * downloads it. The dashboard's filters may have changed since the page was opened: that is a 409 `widgetExports.filtersChanged`.
   */
  exportWidget(projectId: string, dashboardId: string, request: WidgetExportRequest): Observable<WidgetExportResult> {
    return this.http.post<WidgetExportResult>(`${this.dashboardUrl(projectId, dashboardId)}/widget-exports`, request);
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
   * Replaces the dashboard's palette and colors kept by value, the whole list, and answers them as saved (add-dashboard-palettes,
   * design D5). Saving what is stored changes nothing and makes no version.
   */
  saveColors(projectId: string, dashboardId: string, colors: DashboardColors): Observable<DashboardColors> {
    return this.http.put<DashboardColors>(`${this.dashboardUrl(projectId, dashboardId)}/colors`, { palette: colors.palette, valueColors: colors.valueColors });
  }

  /**
   * The most frequent values of a text column across the indicator tables of the dashboard's widgets, optionally only those that
   * contain `search`. Empty when no table has the column as text. `indicators`, when there are some, are the tables read instead:
   * a version shown names those of its own widgets, which the dashboard may no longer have (add-dashboard-versions, design D11).
   */
  dashboardValues(
    projectId: string,
    dashboardId: string,
    column: string,
    search?: string,
    indicators?: readonly string[],
  ): Observable<{ values: DataValueCount[] }> {
    let params = new HttpParams().set('column', column);
    if (search) {
      params = params.set('search', search);
    }
    for (const indicator of indicators ?? []) {
      params = params.append('indicator', indicator);
    }
    return this.http.get<{ values: DataValueCount[] }>(`${this.dashboardUrl(projectId, dashboardId)}/values`, { params });
  }

  /**
   * Saves the places of several widgets of the dashboard in one request, so a drag and the widgets it pushed are one change (design
   * D8), and answers the widgets as saved. A widget that is not on the dashboard saves none of them.
   */
  saveLayout(projectId: string, dashboardId: string, widgets: WidgetLayoutItem[]): Observable<Widget[]> {
    return this.http.put<Widget[]>(`${this.dashboardUrl(projectId, dashboardId)}/layout`, { widgets });
  }

  // A dashboard's versions, for analysts and admins (design D9). A version no longer kept is a 404 `dashboards.versionNotFound`.

  listVersions(projectId: string, dashboardId: string): Observable<DashboardVersionList> {
    return this.http.get<DashboardVersionList>(this.versionsUrl(projectId, dashboardId));
  }

  getVersion(projectId: string, dashboardId: string, number: number): Observable<DashboardVersionDetails> {
    return this.http.get<DashboardVersionDetails>(`${this.versionsUrl(projectId, dashboardId)}/${number}`);
  }

  /**
   * Makes the dashboard's name, filters, palette, colors kept by value and widgets those of the version, as a new version (201), or
   * changes nothing when it already is so (200, `restored` false). A version over the widget limit is refused as a duplicate is (400).
   */
  restoreVersion(projectId: string, dashboardId: string, number: number): Observable<DashboardRestore> {
    return this.http.post<DashboardRestore>(`${this.versionsUrl(projectId, dashboardId)}/${number}/restore`, null);
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

  /** Partial: only what changed. The page saves a drag's layouts with `saveLayout`; a layout here stays for other clients. */
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

  private versionsUrl(projectId: string, dashboardId: string): string {
    return `${this.dashboardUrl(projectId, dashboardId)}/versions`;
  }

  private reportUrl(projectId: string, dashboardId: string): string {
    return `${this.dashboardUrl(projectId, dashboardId)}/report`;
  }

  private widgetsUrl(projectId: string, dashboardId: string): string {
    return `${this.dashboardUrl(projectId, dashboardId)}/widgets`;
  }
}
