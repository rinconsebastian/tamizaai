import { TestRequest } from '@angular/common/http/testing';
import { vi } from 'vitest';
import { Downloader } from '../../../core/download.service';
import {
  IndicatorsColumns,
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
export const isColumns = (r: { url: string; method: string }) => r.method === 'GET' && r.url === `${indicatorsUrl}/columns`;
export const isQuery = (r: { url: string; method: string }) => r.method === 'POST' && r.url === `${indicatorsUrl}/query`;
export const isPatch = (r: { url: string; method: string }) => r.method === 'PATCH' && r.url.startsWith(`${indicatorsUrl}/widgets/`);

export const downloader = { start: vi.fn() };

export function widget(id: string, overrides: Partial<Widget> = {}): Widget {
  return {
    id,
    title: `Widget ${id}`,
    chartType: 'column',
    config: { ...emptyConfig(), indicators: ['coverage'] },
    layout: { x: 0, y: 0, w: 6, h: 4 },
    updatedAt: '2026-10-02T12:00:00Z',
    ...overrides,
  };
}

export function config(overrides: Partial<WidgetConfig> = {}): WidgetConfig {
  return { ...emptyConfig(), indicators: ['coverage'], ...overrides };
}

export function overview(widgets: Widget[], overrides: Partial<IndicatorsOverview> = {}): IndicatorsOverview {
  return {
    published: true,
    publishedAt: '2026-10-02T12:00:00Z',
    runId: 'abcdef12-0000-0000-0000-000000000001',
    widgets,
    limits: { maxWidgets: 50, maxQueryRows: 5000 },
    ...overrides,
  };
}

export function catalog(overrides: Partial<IndicatorsColumns> = {}): IndicatorsColumns {
  return {
    published: true,
    columns: [
      { name: 'indicator', type: 'text', label: null },
      { name: 'disaggregation', type: 'text', label: null },
      { name: 'value', type: 'decimal', label: null },
      { name: 'numerator', type: 'decimal', label: null },
      { name: 'denominator', type: 'decimal', label: null },
      { name: 'round', type: 'integer', label: null },
      { name: 'geometry', type: 'text', label: 'Boundary' },
    ],
    indicators: ['attendance', 'coverage', 'response_rate', 'target'],
    ...overrides,
  };
}

export function result(rows: QueryRow[], overrides: Partial<QueryResult> = {}): QueryResult {
  return { published: true, rows, rowLimitReached: false, missing: { indicators: [], columns: [] }, ...overrides };
}

export function row(indicator: string, group: string | null, value: number, geometry: string | null = null): QueryRow {
  return { indicator, group, value, geometry };
}

export const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
export const all = (root: ParentNode, id: string) => Array.from(root.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

/** What answers a widget's query: the rows for a request body, by indicator or as one answer for everything. */
export type Answer = QueryResult | ((config: WidgetConfig) => QueryResult);

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
