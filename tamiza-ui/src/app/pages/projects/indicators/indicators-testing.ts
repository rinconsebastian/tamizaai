import { TestRequest } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { vi } from 'vitest';
import { Downloader } from '../../../core/download.service';
import {
  AppliedDashboardFilter,
  Dashboard,
  DashboardFilter,
  IndicatorColumns,
  IndicatorEntry,
  IndicatorsOverview,
  QueryResult,
  QueryRow,
  Widget,
  WidgetConfig,
  emptyConfig,
} from '../../../core/projects/indicators.service';
import { DashboardEntry, ProjectDetails, ProjectRole } from '../../../core/projects/projects.service';
import { InterfaceLanguage } from '../../../core/i18n/interface-language';
import { openProjectSection, projectId, settle } from '../../../../testing/project-harness';
import { ProjectIndicators } from '../project-indicators';
import { DashboardPrint } from './dashboard-print';

// Shared by the specs of the indicators dashboard: factories for the API's answers and a way to open the page.

export const indicatorsUrl = `/api/v1/projects/${projectId}/indicators`;
/** The project's only dashboard in `details()`, "Indicators". */
export const dashboardId = 'd1';
export const dashboardUrlOf = (id: string) => `/api/v1/projects/${projectId}/dashboards/${id}`;
export const dashboardUrl = dashboardUrlOf(dashboardId);
export const widgetsUrl = `${dashboardUrl}/widgets`;
export const widgetUrl = (id: string) => `${widgetsUrl}/${id}`;
export const isOverview = (r: { url: string; method: string }) => r.method === 'GET' && r.url === indicatorsUrl;
/** The read of one dashboard with its widgets. */
export const isDashboardOf = (id: string) => (r: { url: string; method: string }) => r.method === 'GET' && r.url === dashboardUrlOf(id);
export const isDashboard = isDashboardOf(dashboardId);
export const isColumns = (r: { url: string; method: string }) => r.method === 'GET' && r.url.startsWith(`${indicatorsUrl}/`) && r.url.endsWith('/columns');
export const isQuery = (r: { url: string; method: string }) => r.method === 'POST' && r.url === `${indicatorsUrl}/query`;
/** The values a dashboard's text filter offers. */
export const isDashboardValues = (r: { url: string; method: string }) => r.method === 'GET' && /^\/api\/v1\/projects\/[^/]+\/dashboards\/[^/]+\/values$/.test(r.url);
/** A change to a widget, on whichever dashboard of the project it is. */
export const isPatch = (r: { url: string; method: string }) => r.method === 'PATCH' && /^\/api\/v1\/projects\/[^/]+\/dashboards\/[^/]+\/widgets\/[^/]+$/.test(r.url);

export const downloader = { start: vi.fn() };

/** The indicators the specs use, with the metadata a script gave them. Others are counts with their name as label. */
const presets: Record<string, Pick<IndicatorEntry, 'label' | 'format' | 'unit'>> = {
  attendance: { label: 'Attendance', format: 'count', unit: null },
  coverage: { label: 'Coverage', format: 'count', unit: null },
  households_visited: { label: 'Households visited', format: 'count', unit: 'households' },
  piped_water_rate: { label: 'Piped water rate', format: 'percent', unit: null },
  response_rate: { label: 'Response rate', format: 'decimal', unit: null },
  target: { label: 'Target', format: 'count', unit: null },
};

export function indicator(name: string, overrides: Partial<IndicatorEntry> = {}): IndicatorEntry {
  return {
    name,
    ...(presets[name] ?? { label: name, format: 'count', unit: null }),
    description: null,
    published: true,
    publishedAt: '2026-10-02T12:00:00Z',
    runId: 'abcdef12-0000-0000-0000-000000000001',
    rows: 120,
    ...overrides,
  };
}

export const defaultIndicators = ['attendance', 'coverage', 'households_visited', 'piped_water_rate', 'response_rate', 'target'].map((name) => indicator(name));

export function widget(id: string, overrides: Partial<Widget> = {}): Widget {
  return {
    id,
    dashboardId,
    title: `Widget ${id}`,
    chartType: 'column',
    config: config(),
    layout: { x: 0, y: 0, w: 6, h: 4 },
    updatedAt: '2026-10-02T12:00:00Z',
    ...overrides,
  };
}

/** A query over the indicator `coverage`, grouped by `municipality`. */
export function config(overrides: Partial<WidgetConfig> = {}): WidgetConfig {
  return { ...emptyConfig('coverage'), groupBy: 'municipality', ...overrides };
}

export function overview(overrides: Partial<IndicatorsOverview> = {}): IndicatorsOverview {
  return {
    indicators: defaultIndicators,
    limits: { maxWidgets: 50, maxQueryRows: 5000, maxDashboards: 7 },
    ...overrides,
  };
}

/** The dashboard "Indicators" with these widgets. */
export function dashboard(widgets: Widget[], overrides: Partial<Dashboard> = {}): Dashboard {
  return { id: dashboardId, name: 'Indicators', filters: [], widgets, ...overrides };
}

export function catalog(overrides: Partial<IndicatorColumns> = {}): IndicatorColumns {
  return {
    published: true,
    columns: [
      { name: 'municipality', type: 'text', label: null },
      { name: 'value', type: 'decimal', label: null },
      { name: 'numerator', type: 'decimal', label: null },
      { name: 'denominator', type: 'decimal', label: null },
      { name: 'round', type: 'integer', label: null },
      { name: 'geometry', type: 'text', label: 'Boundary' },
    ],
    numeric: ['value', 'numerator', 'denominator', 'round'],
    defaults: { measure: 'value', groupBy: 'municipality' },
    ...overrides,
  };
}

export function result(rows: QueryRow[], overrides: Partial<QueryResult> = {}): QueryResult {
  return { published: true, rows, rowLimitReached: false, missing: { indicator: false, columns: [] }, ...overrides };
}

/** A row of an answer; a map's carry how many table rows they combine (`rows`) and the values of the tooltip columns (`tooltip`). */
export function row(group: string | null, value: number, geometry: string | null = null, extra: Pick<QueryRow, 'rows' | 'tooltip'> = {}): QueryRow {
  return { group, value, geometry, ...extra };
}

export const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
export const all = (root: ParentNode, id: string) => Array.from(root.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

/** What answers a widget's query: the rows for a request body, or one answer for everything. */
export type Answer = QueryResult | ((body: { config: WidgetConfig; chartType?: string; dashboardFilters?: AppliedDashboardFilter[] }) => QueryResult);

/**
 * Which dashboard `open()` opens, and the project's dashboards when they are not just "Indicators" (`d1`); the dashboard's filters,
 * and a query string for the address (`f=…`, without the `?`); other project details (its time zone), and the interface language.
 */
export interface OpenAt {
  dashboardId?: string;
  dashboards?: DashboardEntry[];
  filters?: DashboardFilter[];
  query?: string;
  project?: Partial<ProjectDetails>;
  lang?: InterfaceLanguage;
}

/** Opens a dashboard by its address, as a bookmark or a tab would, and answers what it reads; `d1` unless `at` says otherwise. */
export async function open(role: ProjectRole, widgets: Widget[], answer: Answer | null, extra: Partial<IndicatorsOverview> = {}, at: OpenAt = {}) {
  const id = at.dashboardId ?? dashboardId;
  const address = `dashboards/${id}${at.query ? `?${at.query}` : ''}`;
  const project = { ...(at.dashboards ? { dashboards: at.dashboards } : {}), ...at.project };
  const context = await openProjectSection(address, role, project, [{ provide: Downloader, useValue: downloader }], at.lang);
  await answerLoad(context, widgets, extra, id, at.filters);
  if (answer) {
    await answerQueries(context, answer);
  }
  return context;
}

export type Context = Awaited<ReturnType<typeof open>>;

/** Answers what the page reads to draw a dashboard (`d1` by default): the project's indicators and the dashboard's filters and widgets. */
export async function answerLoad(
  context: Pick<Context, 'http' | 'harness'>,
  widgets: Widget[],
  extra: Partial<IndicatorsOverview> = {},
  id = dashboardId,
  filters: DashboardFilter[] = [],
): Promise<void> {
  context.http.expectOne(isOverview).flush(overview(extra));
  context.http.expectOne(isDashboardOf(id)).flush(dashboard(widgets, { id, filters }));
  await settle(context.harness);
}

/** Answers every query waiting, then lets the page draw. Returns the requests, in the order they were sent. */
export async function answerQueries(context: Pick<Context, 'http' | 'harness'>, answer: Answer): Promise<TestRequest[]> {
  const requests = context.http.match(isQuery);
  for (const request of requests.filter((r) => !r.cancelled)) {
    request.flush(typeof answer === 'function' ? answer(request.request.body) : answer);
  }
  await settle(context.harness);
  return requests;
}

export const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;
export const clearOverlay = () => document.querySelectorAll('.cdk-overlay-container').forEach((c) => c.replaceChildren());
export const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** The open dashboard's paper mode (dashboard printing, design D1). */
export function printOf(context: Pick<Context, 'harness'>): DashboardPrint {
  return context.harness.fixture.debugElement.query(By.directive(ProjectIndicators)).injector.get(DashboardPrint);
}

/** Lays the open dashboard out as it prints, as the Print action and `beforeprint` do, and lets the page draw. */
export async function paperOn(context: Pick<Context, 'harness'>): Promise<DashboardPrint> {
  const print = printOf(context);
  print.prepare();
  await settle(context.harness);
  return print;
}
