import { TestRequest } from '@angular/common/http/testing';
import { vi } from 'vitest';
import { Downloader } from '../../../core/download.service';
import {
  IndicatorColumns,
  IndicatorEntry,
  IndicatorsOverview,
  QueryResult,
  QueryRow,
  Widget,
  WidgetConfig,
  emptyConfig,
} from '../../../core/projects/indicators.service';
import { ProjectRole } from '../../../core/projects/projects.service';
import { openProjectSection, projectId, settle } from '../../../../testing/project-harness';

// Shared by the specs of the indicators dashboard: factories for the API's answers and a way to open the page.

export const indicatorsUrl = `/api/v1/projects/${projectId}/indicators`;
export const isOverview = (r: { url: string; method: string }) => r.method === 'GET' && r.url === indicatorsUrl;
export const isColumns = (r: { url: string; method: string }) => r.method === 'GET' && r.url.startsWith(`${indicatorsUrl}/`) && r.url.endsWith('/columns');
export const isQuery = (r: { url: string; method: string }) => r.method === 'POST' && r.url === `${indicatorsUrl}/query`;
export const isPatch = (r: { url: string; method: string }) => r.method === 'PATCH' && r.url.startsWith(`${indicatorsUrl}/widgets/`);

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

export function overview(widgets: Widget[], overrides: Partial<IndicatorsOverview> = {}): IndicatorsOverview {
  return {
    indicators: defaultIndicators,
    widgets,
    limits: { maxWidgets: 50, maxQueryRows: 5000 },
    ...overrides,
  };
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

export function row(group: string | null, value: number, geometry: string | null = null): QueryRow {
  return { group, value, geometry };
}

export const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
export const all = (root: ParentNode, id: string) => Array.from(root.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

/** What answers a widget's query: the rows for a request body, or one answer for everything. */
export type Answer = QueryResult | ((body: { config: WidgetConfig; chartType?: string }) => QueryResult);

export async function open(role: ProjectRole, widgets: Widget[], answer: Answer | null, extra: Partial<IndicatorsOverview> = {}) {
  const context = await openProjectSection('indicators', role, {}, [{ provide: Downloader, useValue: downloader }]);
  context.http.expectOne(isOverview).flush(overview(widgets, extra));
  await settle(context.harness);
  if (answer) {
    await answerQueries(context, answer);
  }
  return context;
}

export type Context = Awaited<ReturnType<typeof open>>;

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
export const clearOverlay = () => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
export const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
