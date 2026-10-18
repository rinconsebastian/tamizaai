import { TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import es from '../../../../public/i18n/es.json';
import { Downloader } from '../../core/download.service';
import { InterfaceLanguage } from '../../core/i18n/interface-language';
import { DataColumn, DataExport, DataFilter, DataOverview, DataPage } from '../../core/projects/data.service';
import { ProjectRole } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
import { pressKey, tick } from '../../../testing/cdk';
import { byTestId, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';
import { encodeFilters } from './data-view';
import { valueSearchDelayMs } from './data-filter-dialog';

const dataUrl = `/api/v1/projects/${projectId}/data`;
const isOverview = (r: { url: string; method: string }) => r.method === 'GET' && r.url === dataUrl;
const isQuery = (r: { url: string; method: string }) => r.method === 'POST' && r.url.endsWith('/query');
const isValues = (r: { url: string; method: string }) => r.method === 'GET' && r.url.includes('/columns/');
const isExport = (r: { url: string; method: string }) => r.method === 'POST' && r.url.endsWith('/exports');

const columns: DataColumn[] = [
  { name: 'name', type: 'text', label: 'Full name' },
  { name: 'age', type: 'integer', label: null },
  { name: 'income', type: 'decimal', label: null },
  { name: 'consent', type: 'boolean', label: null },
  { name: 'born', type: 'date', label: null },
  { name: 'at', type: 'timestamp', label: null },
];

function overview(overrides: Partial<DataOverview> = {}): DataOverview {
  return {
    sources: [
      {
        source: 'output',
        publishedAt: '2026-10-02T12:00:00Z',
        runId: 'abcdef12-0000-0000-0000-000000000001',
        tables: [
          { name: 'persons', rowCount: 4830 },
          { name: 'households', rowCount: 1200 },
        ],
      },
      { source: 'clean', publishedAt: '2026-10-02T11:59:00Z', runId: 'abcdef12-0000-0000-0000-000000000001', tables: [{ name: 'main', rowCount: 1200 }] },
    ],
    defaultSource: 'output',
    timeZone: 'America/Bogota',
    ...overrides,
  };
}

function page(overrides: Partial<DataPage> = {}): DataPage {
  return {
    columns,
    rows: [
      ['Ana', 31, 10.5, true, '2026-09-01', '2026-09-01T21:30:00-05:00'],
      ['Beto', null, 20, false, null, null],
    ],
    cutCells: [],
    total: 2,
    page: 1,
    pageSize: 50,
    publishedAt: '2026-10-02T12:00:00Z',
    runId: 'abcdef12-0000-0000-0000-000000000001',
    timeZone: 'America/Bogota',
    ...overrides,
  };
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;
const downloader = { start: vi.fn() };

async function open(
  role: ProjectRole,
  options: { section?: string; overview?: DataOverview; first?: DataPage | null; lang?: InterfaceLanguage } = {},
) {
  const context = await openProjectSection(options.section ?? 'clean-data', role, {}, [{ provide: Downloader, useValue: downloader }], options.lang);
  context.http.expectOne(isOverview).flush(options.overview ?? overview());
  await settle(context.harness);
  if (options.first !== null && (options.overview ?? overview()).sources.length) {
    context.http.expectOne(isQuery).flush(options.first ?? page());
    await settle(context.harness);
  }
  return context;
}

type Context = Awaited<ReturnType<typeof open>>;

function body(request: TestRequest): { filters: DataFilter[]; sort: unknown; page: number; pageSize: number } {
  return request.request.body;
}

async function click(context: Context, element: HTMLElement | null): Promise<void> {
  element!.click();
  await settle(context.harness);
}

async function nextQuery(context: Context, answer: DataPage = page()): Promise<TestRequest> {
  const request = context.http.expectOne(isQuery);
  request.flush(answer);
  await settle(context.harness);
  return request;
}

const headerButtons = (root: HTMLElement, testId: string) => Array.from(root.querySelectorAll<HTMLButtonElement>(`[data-testid="${testId}"]`));
const address = () => TestBed.inject(Router).url;

describe('ProjectCleanData', () => {
  beforeEach(() => downloader.start.mockClear());
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  describe('sources and tables', () => {
    it.each<ProjectRole>(['viewer', 'analyst', 'admin'])('opens on the output tables with a source switch, for a %s', async (role) => {
      const context = await open(role);
      const request = context.http.match(isQuery);

      expect(request).toHaveLength(0);
      expect(byTestId(context.root, 'source-switch')).not.toBeNull();
      expect(byTestId(context.root, 'source-output')!.getAttribute('aria-pressed')).toBe('true');
      expect(byTestId(context.root, 'source-clean')!.getAttribute('aria-pressed')).toBe('false');
      expect(byTestId<HTMLSelectElement>(context.root, 'table-select')!.value).toBe('persons');
    });

    it('asks for the first page of the default table with no filters', async () => {
      const context = await openProjectSection('clean-data', 'viewer', {}, [{ provide: Downloader, useValue: downloader }]);
      context.http.expectOne(isOverview).flush(overview());
      await settle(context.harness);

      const request = context.http.expectOne(isQuery);
      expect(request.request.url).toBe(`${dataUrl}/output/persons/query`);
      expect(body(request)).toEqual({ filters: [], sort: null, page: 1, pageSize: 50 });
      request.flush(page());
    });

    it('lists the tables of the source with their row counts', async () => {
      const { root } = await open('viewer');

      const options = Array.from(root.querySelectorAll('[data-testid="table-select"] option')).map((o) => text(o));
      expect(options).toEqual(['persons (4,830 rows)', 'households (1,200 rows)']);
    });

    it('uses the singular for a table with one row', async () => {
      const single = overview({ sources: [{ source: 'output', publishedAt: '2026-10-02T12:00:00Z', runId: 'abcdef12-0000-0000-0000-000000000001', tables: [{ name: 'persons', rowCount: 1 }, { name: 'households', rowCount: 1200 }] }] });
      const { root } = await open('viewer', { overview: single });

      const options = Array.from(root.querySelectorAll('[data-testid="table-select"] option')).map((o) => text(o));
      expect(options).toEqual(['persons (1 row)', 'households (1,200 rows)']);
    });

    it('shows the clean tables without a switch when the project has no script 2', async () => {
      const without = overview({ sources: [overview().sources[1]], defaultSource: 'clean' });
      const context = await open('viewer', { overview: without });

      expect(byTestId(context.root, 'source-switch')).toBeNull();
      expect(byTestId<HTMLSelectElement>(context.root, 'table-select')!.value).toBe('main');
      expect(address()).toBe(`/projects/${projectId}/clean-data`);
    });

    it('switches source, starting on its first table with a clean view', async () => {
      const context = await open('viewer');
      await click(context, byTestId(context.root, 'source-clean'));

      const request = context.http.expectOne(isQuery);
      expect(request.request.url).toBe(`${dataUrl}/clean/main/query`);
      expect(body(request)).toEqual({ filters: [], sort: null, page: 1, pageSize: 50 });
      request.flush(page());
      await settle(context.harness);
      expect(address()).toContain('source=clean');
      expect(address()).toContain('table=main');
    });

    it('selects another table and drops the filters of the first', async () => {
      const context = await open('viewer');
      typeInto(byTestId<HTMLSelectElement>(context.root, 'table-select')!, 'households');
      await settle(context.harness);

      const request = context.http.expectOne(isQuery);
      expect(request.request.url).toBe(`${dataUrl}/output/households/query`);
      expect(body(request).filters).toEqual([]);
      request.flush(page());
    });

    it('says when and by which run the tables were published, and in which time zone times are shown', async () => {
      const { root } = await open('viewer');

      expect(text(byTestId(root, 'published'))).toMatch(/^Published .*2026.* by run abcdef12/);
      expect(text(byTestId(root, 'zone'))).toBe('Times in America/Bogota.');
    });

    it.each<[ProjectRole, boolean]>([
      ['analyst', true],
      ['admin', true],
      ['viewer', false],
    ])('links the run to the runs list for a %s: %s', async (role, hasLink) => {
      const { root } = await open(role);

      expect(text(byTestId(root, 'published'))).toContain('by run abcdef12'); // the run id is text for every role
      const link = byTestId(root, 'published')!.querySelector('a');
      expect(!!link).toBe(hasLink);
      if (link) {
        expect(link.getAttribute('href')).toBe('/projects/p1/scripts#runs');
      }
    });

    it.each<[ProjectRole, boolean]>([
      ['analyst', true],
      ['admin', true],
      ['viewer', false],
    ])('explains that nothing is published, for a %s', async (role, hasLink) => {
      const { root } = await open(role, { overview: overview({ sources: [], defaultSource: null }) });

      expect(byTestId(root, 'data-empty')).not.toBeNull();
      expect(byTestId(root, 'data-empty-link') !== null).toBe(hasLink);
      expect(byTestId(root, 'data-empty-viewer') !== null).toBe(!hasLink);
    });

    it('shows the error when the tables cannot be listed', async () => {
      const { http, harness, root } = await openProjectSection('clean-data', 'viewer', {}, [{ provide: Downloader, useValue: downloader }]);
      http.expectOne(isOverview).flush({ code: 'data.timeout', title: 'x' }, { status: 503, statusText: 'Service Unavailable' });
      await settle(harness);

      expect(text(byTestId(root, 'data-error'))).toContain('took too long');
    });
  });

  describe('the table', () => {
    it('shows each column with its name, question label and type, and the values', async () => {
      const { root } = await open('viewer');

      const columnHeaders = Array.from(root.querySelectorAll('[data-testid="column-header"]'));
      expect(columnHeaders.map((h) => text(h.querySelector('[data-testid="sort-button"]')))).toEqual(['name', 'age', 'income', 'consent', 'born', 'at']);
      expect(text(columnHeaders[0].querySelector('[data-testid="column-label"]'))).toBe('Full name');
      expect(columnHeaders[1].querySelector('[data-testid="column-label"]')).toBeNull();
      expect(columnHeaders.map((h) => text(h.querySelector('[data-testid="column-type"]')))).toEqual(['text', 'integer', 'decimal', 'boolean', 'date', 'timestamp']);
      const cells = Array.from(root.querySelectorAll('[data-testid="data-row"]')[0].querySelectorAll('td')).map((td) => text(td));
      expect(cells).toEqual(['Ana', '31', '10.5', 'true', '2026-09-01', '2026-09-01 21:30:00']);
    });

    it('shows empty values as a dash that screen readers read as empty', async () => {
      const { root } = await open('viewer');

      const beto = root.querySelectorAll('[data-testid="data-row"]')[1].querySelectorAll('td');
      expect(text(beto[1])).toBe('—empty');
      expect(beto[1].querySelector('[aria-hidden="true"]')?.textContent).toBe('—');
      expect(beto[1].querySelector('.sr-only')?.textContent).toBe('empty');
    });

    it('marks text that was cut at 500 characters', async () => {
      const cut = page({ rows: [[`${'x'.repeat(500)}`, 1, 1, true, null, null]], cutCells: [[0, 0]], total: 1 });
      const { root } = await open('viewer', { first: cut });

      const cell = root.querySelectorAll('[data-testid="data-row"]')[0].querySelectorAll('td')[0];
      expect(cell.querySelector('.sr-only')?.textContent).toBe('(cut at 500 characters)');
      expect(cell.textContent).toContain('…');
      expect(root.querySelectorAll('[data-testid="data-row"]')[0].querySelectorAll('td')[1].textContent).not.toContain('…');
    });

    it('keeps long tables inside their own scrolling container', async () => {
      const { root } = await open('viewer');

      const container = root.querySelector('tmz-table-container')!;
      expect(container.className).toContain('overflow-x-auto');
      expect(container.className).toContain('overflow-y-auto');
      expect(container.contains(byTestId(root, 'data-table'))).toBe(true);
    });

    it('shows the number of rows', async () => {
      const { root } = await open('viewer', { first: page({ total: 4830 }) });

      expect(text(byTestId(root, 'row-count'))).toBe('4,830 rows');
    });

    it('uses the singular for one row', async () => {
      const { root } = await open('viewer', { first: page({ total: 1, rows: [['Ana', 31, 10.5, true, '2026-09-01', '2026-09-01T21:30:00-05:00']] }) });

      expect(text(byTestId(root, 'row-count'))).toBe('1 row');
    });

    it('says when a table has no rows', async () => {
      const { root } = await open('viewer', { first: page({ rows: [], total: 0 }) });

      expect(text(byTestId(root, 'no-rows'))).toBe('This table has no rows.');
      expect(byTestId(root, 'page-info')).toBeNull();
    });
  });

  describe('layout', () => {
    it('has no card around the toolbar, and puts the row count and Export on one line', async () => {
      const { root } = await open('viewer');
      expect(byTestId(root, 'table-select')!.closest('tmz-card')).toBeNull();
      const line = byTestId(root, 'row-count')!.parentElement!;
      expect(line.contains(byTestId(root, 'export-button'))).toBe(true);
    });

    it('keeps Export usable before the first page arrives', async () => {
      const { root } = await open('viewer', { first: null });
      expect(byTestId<HTMLButtonElement>(root, 'export-button')!.disabled).toBe(false);
      expect(text(byTestId(root, 'row-count'))).toBe('');
    });

    it('puts the label and the type of a column on one line, the type always shown', async () => {
      const { root } = await open('viewer');
      const header = root.querySelector('[data-testid="column-header"]')!;
      expect(header.querySelector('[data-testid="column-label"]')!.parentElement).toBe(header.querySelector('[data-testid="column-type"]')!.parentElement);
      expect(text(header.querySelector('[data-testid="column-type"]'))).toBe('text');
    });

    it('says when and by which run in one line with the zone', async () => {
      const { root } = await open('viewer');
      expect(byTestId(root, 'published')!.parentElement).toBe(byTestId(root, 'zone')!.parentElement);
    });

    it('hides the paging controls on a short table and shows them on a long one', async () => {
      const short = await open('viewer', { first: page({ total: 25 }) });
      expect(short.root.querySelector('nav[aria-label="Pages"]')).toBeNull();
      expect(byTestId(short.root, 'page-size')).toBeNull();
    });

    it('shows the paging controls when there are more rows than the smallest page', async () => {
      const { root } = await open('viewer', { first: page({ total: 26 }) });
      expect(byTestId(root, 'page-size')).not.toBeNull();
    });
  });

  describe('paging', () => {
    it('shows the range and moves to the next page through the address', async () => {
      const context = await open('viewer', { first: page({ total: 120 }) });
      expect(text(byTestId(context.root, 'page-info'))).toBe('1–50 of 120');
      expect(byTestId<HTMLButtonElement>(context.root, 'previous-page')!.disabled).toBe(true);

      await click(context, byTestId(context.root, 'next-page'));
      const request = await nextQuery(context, page({ total: 120, page: 2 }));

      expect(body(request).page).toBe(2);
      expect(address()).toContain('page=2');
      expect(text(byTestId(context.root, 'page-info'))).toBe('51–100 of 120');
      expect(byTestId<HTMLButtonElement>(context.root, 'previous-page')!.disabled).toBe(false);
    });

    it('disables Next on the last page', async () => {
      const { root } = await open('viewer', { first: page({ total: 120, page: 3 }) });

      expect(text(byTestId(root, 'page-info'))).toBe('101–120 of 120');
      expect(byTestId<HTMLButtonElement>(root, 'next-page')!.disabled).toBe(true);
    });

    it('changes the page size and goes back to the first page', async () => {
      const context = await open('viewer', { section: 'clean-data?page=3', first: page({ total: 500, page: 3 }) });
      typeInto(byTestId<HTMLSelectElement>(context.root, 'page-size')!, '100');
      await settle(context.harness);

      const request = await nextQuery(context, page({ total: 500, pageSize: 100 }));
      expect(body(request)).toMatchObject({ page: 1, pageSize: 100 });
      expect(address()).toContain('size=100');
      expect(address()).not.toContain('page=');
    });
  });

  describe('sorting', () => {
    it('cycles a column through ascending, descending and not sorted', async () => {
      const context = await open('viewer');
      const sortButton = () => headerButtons(context.root, 'sort-button')[1];
      const header = () => context.root.querySelectorAll('[data-testid="column-header"]')[1];
      expect(header().getAttribute('aria-sort')).toBe('none');

      await click(context, sortButton());
      let request = await nextQuery(context);
      expect(body(request).sort).toEqual({ column: 'age', direction: 'asc' });
      expect(header().getAttribute('aria-sort')).toBe('ascending');
      expect(sortButton().getAttribute('aria-label')).toContain('sorted ascending');

      await click(context, sortButton());
      request = await nextQuery(context);
      expect(body(request).sort).toEqual({ column: 'age', direction: 'desc' });
      expect(header().getAttribute('aria-sort')).toBe('descending');

      await click(context, sortButton());
      request = await nextQuery(context);
      expect(body(request).sort).toBeNull();
      expect(header().getAttribute('aria-sort')).toBe('none');
      expect(address()).not.toContain('sort=');
    });

    it('goes back to the first page when the sort changes', async () => {
      const context = await open('viewer', { section: 'clean-data?page=2', first: page({ total: 120, page: 2 }) });

      await click(context, headerButtons(context.root, 'sort-button')[0]);
      const request = await nextQuery(context);

      expect(body(request)).toMatchObject({ page: 1, sort: { column: 'name', direction: 'asc' } });
    });
  });

  describe('filters', () => {
    async function openDialog(context: Context, column: number) {
      await click(context, headerButtons(context.root, 'filter-button')[column]);
      return overlay();
    }

    it('filters a text column with contains, checks it in the dialog and shows it as a chip', async () => {
      const context = await open('viewer');
      const dialog = await openDialog(context, 0);
      expect(text(dialog.querySelector('h2'))).toBe('Filter name');
      expect(Array.from(dialog.querySelectorAll('[data-testid="filter-op"] option')).map((o) => text(o))).toEqual([
        'contains', 'is exactly', 'is one of', 'is empty', 'is not empty',
      ]);
      context.http.expectNone(isValues); // the list of values is only fetched for "is one of"

      typeInto(byTestId<HTMLInputElement>(dialog, 'filter-value')!, 'well');
      await click(context, byTestId(dialog, 'filter-apply'));
      const check = context.http.expectOne(isQuery);
      expect(body(check)).toEqual({ filters: [{ column: 'name', op: 'contains', value: 'well' }], sort: null, page: 1, pageSize: 50 });
      check.flush(page({ total: 1, rows: [['Eve WELL', 40, 1, true, null, null]] }));
      await settle(context.harness);

      context.http.expectNone(isQuery); // the checked page is used; it is not asked for again
      expect(overlay().querySelector('[data-testid="filter-dialog"]')).toBeNull();
      expect(text(byTestId(context.root, 'filter-chip'))).toBe('name contains “well”');
      expect(text(byTestId(context.root, 'row-count'))).toBe('1 matching row');
      expect(text(context.root.querySelector('[data-testid="data-row"]'))).toContain('Eve WELL');
      expect(address()).toMatch(/[?&]f=[A-Za-z0-9_-]+/);
    });

    it('explains that the list of values may be incomplete only when it is full', async () => {
      const context = await open('viewer');
      const dialog = await openDialog(context, 0);
      typeInto(byTestId<HTMLSelectElement>(dialog, 'filter-op')!, 'in');
      await settle(context.harness);

      context.http.expectOne(isValues).flush({ values: Array.from({ length: 50 }, (_, i) => ({ value: 'value ' + i, count: 50 - i })) });
      await settle(context.harness);
      expect(text(byTestId(dialog, 'filter-values-help'))).toContain('50 most frequent values are listed');
    });

    it('lists the most frequent values for "is one of", searches once typing stops, and filters by the chosen ones', async () => {
      const context = await open('viewer');
      const dialog = await openDialog(context, 0);
      typeInto(byTestId<HTMLSelectElement>(dialog, 'filter-op')!, 'in');
      await settle(context.harness);

      const first = context.http.expectOne(isValues);
      expect(first.request.url).toBe(`${dataUrl}/output/persons/columns/name/values`);
      expect(first.request.params.has('search')).toBe(false);
      first.flush({ values: [{ value: 'Cali', count: 830 }, { value: 'Palmira', count: 120 }, { value: '', count: 3 }] });
      await settle(context.harness);
      const labels = Array.from(dialog.querySelectorAll('[data-testid="filter-option"]')).map((o) => text(o.closest('label')));
      expect(labels).toEqual(['Cali830', 'Palmira120', '(empty text)3']);
      expect(byTestId(dialog, 'filter-values-help')).toBeNull(); // the list is complete, so there is nothing to explain

      const search = byTestId<HTMLInputElement>(dialog, 'filter-search')!;
      for (const typed of ['p', 'pa', 'pal']) {
        typeInto(search, typed);
        await tick();
      }
      context.http.expectNone(isValues);
      await new Promise((resolve) => setTimeout(resolve, valueSearchDelayMs + 100));
      const searched = context.http.expectOne(isValues);
      expect(searched.request.params.get('search')).toBe('pal');
      searched.flush({ values: [{ value: 'Palmira', count: 120 }] });
      await settle(context.harness);

      byTestId<HTMLInputElement>(dialog, 'filter-option')!.click();
      await settle(context.harness);
      expect(text(byTestId(dialog, 'filter-selected'))).toBe('1 selected');
      await click(context, byTestId(dialog, 'filter-apply'));
      const check = context.http.expectOne(isQuery);
      expect(body(check).filters).toEqual([{ column: 'name', op: 'in', values: ['Palmira'] }]);
      check.flush(page({ total: 120 }));
      await settle(context.harness);
      expect(text(byTestId(context.root, 'filter-chip'))).toBe('name is “Palmira”');
    });

    it('needs a value before it asks the server', async () => {
      const context = await open('viewer');
      const dialog = await openDialog(context, 0);

      await click(context, byTestId(dialog, 'filter-apply'));

      context.http.expectNone(isQuery);
      expect(text(byTestId(dialog, 'field-error'))).toBe('Enter a value.');
    });

    it('filters an integer column by a range and a date column by days', async () => {
      const context = await open('viewer');
      let dialog = await openDialog(context, 1);
      expect(Array.from(dialog.querySelectorAll('[data-testid="filter-op"] option')).map((o) => text(o))).toEqual(['is between', 'is empty', 'is not empty']);
      expect(byTestId<HTMLInputElement>(dialog, 'filter-min')!.type).toBe('number');
      typeInto(byTestId<HTMLInputElement>(dialog, 'filter-min')!, '18');
      typeInto(byTestId<HTMLInputElement>(dialog, 'filter-max')!, '29');
      await click(context, byTestId(dialog, 'filter-apply'));
      let check = context.http.expectOne(isQuery);
      expect(body(check).filters).toEqual([{ column: 'age', op: 'range', min: '18', max: '29' }]);
      check.flush(page({ total: 1 }));
      await settle(context.harness);
      expect(text(byTestId(context.root, 'filter-chip'))).toBe('age from 18 to 29');

      dialog = await openDialog(context, 5);
      expect(byTestId<HTMLInputElement>(dialog, 'filter-min')!.type).toBe('date');
      expect(text(byTestId(dialog, 'filter-days'))).toBe('Days in America/Bogota; the last day is included.');
      typeInto(byTestId<HTMLInputElement>(dialog, 'filter-max')!, '2026-09-01');
      await click(context, byTestId(dialog, 'filter-apply'));
      check = context.http.expectOne(isQuery);
      expect(body(check).filters).toEqual([
        { column: 'age', op: 'range', min: '18', max: '29' },
        { column: 'at', op: 'range', max: '2026-09-01' },
      ]);
      check.flush(page({ total: 1 }));
      await settle(context.harness);
      expect(Array.from(context.root.querySelectorAll('[data-testid="filter-chip"]')).map((c) => text(c))).toEqual(['age from 18 to 29', 'at up to 2026-09-01']);
    });

    it('needs a minimum or a maximum', async () => {
      const context = await open('viewer');
      const dialog = await openDialog(context, 2);

      await click(context, byTestId(dialog, 'filter-apply'));

      context.http.expectNone(isQuery);
      expect(text(byTestId(dialog, 'field-error'))).toBe('Enter a minimum, a maximum or both.');
    });

    it('filters a true/false column by its two values and an empty condition needs nothing else', async () => {
      const context = await open('viewer');
      let dialog = await openDialog(context, 3);
      expect(Array.from(dialog.querySelectorAll('[data-testid="filter-option"]')).map((o) => text(o.closest('label')))).toEqual(['true', 'false']);
      byTestId<HTMLInputElement>(dialog, 'filter-option')!.click();
      await settle(context.harness);
      await click(context, byTestId(dialog, 'filter-apply'));
      let check = context.http.expectOne(isQuery);
      expect(body(check).filters).toEqual([{ column: 'consent', op: 'in', values: ['true'] }]);
      check.flush(page());
      await settle(context.harness);

      dialog = await openDialog(context, 4);
      typeInto(byTestId<HTMLSelectElement>(dialog, 'filter-op')!, 'empty');
      await settle(context.harness);
      await click(context, byTestId(dialog, 'filter-apply'));
      check = context.http.expectOne(isQuery);
      expect(body(check).filters.at(-1)).toEqual({ column: 'born', op: 'empty' });
      check.flush(page());
      await settle(context.harness);
      expect(Array.from(context.root.querySelectorAll('[data-testid="filter-chip"]')).map((c) => text(c))).toEqual(['consent is “true”', 'born is empty']);
    });

    it('shows the server’s validation message in the dialog and keeps it open', async () => {
      const context = await open('viewer');
      const dialog = await openDialog(context, 1);
      typeInto(byTestId<HTMLInputElement>(dialog, 'filter-min')!, '5');
      await click(context, byTestId(dialog, 'filter-apply'));

      context.http
        .expectOne(isQuery)
        .flush({ errors: { 'filters[0]': ["Column 'age': the minimum is greater than the maximum."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(byTestId(dialog, 'field-error'))).toContain('minimum is greater than the maximum');
      expect(byTestId(dialog, 'field-error')?.getAttribute('lang')).toBe('en');
      expect(overlay().querySelector('[data-testid="filter-dialog"]')).not.toBeNull();
      expect(byTestId(context.root, 'filter-chip')).toBeNull();
    });

    it('marks the alert of the filter dialog as English when the API wrote its message', async () => {
      const context = await open('viewer');
      const dialog = await openDialog(context, 1);
      typeInto(byTestId<HTMLInputElement>(dialog, 'filter-min')!, '5');
      await click(context, byTestId(dialog, 'filter-apply'));

      context.http.expectOne(isQuery).flush({ title: 'The filter cannot be checked right now.' }, { status: 500, statusText: 'Server Error' });
      await settle(context.harness);

      expect(byTestId(dialog, 'filter-error')?.getAttribute('lang')).toBe('en');
    });

    it('edits and removes a filter from its chip and clears them all', async () => {
      const filters = encodeFilters([
        { column: 'name', op: 'contains', value: 'a' },
        { column: 'age', op: 'range', min: '18' },
      ]);
      const context = await open('viewer', { section: `clean-data?f=${filters}` });
      expect(Array.from(context.root.querySelectorAll('[data-testid="filter-chip"]')).map((c) => text(c))).toEqual(['name contains “a”', 'age from 18']);

      await click(context, byTestId(context.root, 'chip-edit'));
      const dialog = overlay();
      expect(byTestId<HTMLInputElement>(dialog, 'filter-value')!.value).toBe('a');
      typeInto(byTestId<HTMLInputElement>(dialog, 'filter-value')!, 'b');
      await click(context, byTestId(dialog, 'filter-apply'));
      let check = context.http.expectOne(isQuery);
      expect(body(check).filters).toEqual([{ column: 'name', op: 'contains', value: 'b' }, { column: 'age', op: 'range', min: '18' }]);
      check.flush(page());
      await settle(context.harness);

      await click(context, context.root.querySelectorAll<HTMLElement>('[data-testid="chip-remove"]')[0]);
      check = context.http.expectOne(isQuery);
      expect(body(check).filters).toEqual([{ column: 'age', op: 'range', min: '18' }]);
      check.flush(page());
      await settle(context.harness);

      await click(context, byTestId(context.root, 'clear-filters'));
      check = context.http.expectOne(isQuery);
      expect(body(check).filters).toEqual([]);
      check.flush(page());
      await settle(context.harness);
      expect(byTestId(context.root, 'filter-chips')).toBeNull();
      expect(address()).not.toContain('f=');
    });

    it('removes the filter of a column from its dialog', async () => {
      const context = await open('viewer', { section: `clean-data?f=${encodeFilters([{ column: 'age', op: 'range', min: '18' }])}` });
      const button = headerButtons(context.root, 'filter-button')[1];
      expect(button.getAttribute('aria-label')).toBe('Filter on age, active');

      await click(context, button);
      await click(context, byTestId(overlay(), 'filter-remove'));
      const request = context.http.expectOne(isQuery);

      expect(body(request).filters).toEqual([]);
      request.flush(page());
    });

    it('goes back to the first page when a filter is applied', async () => {
      const context = await open('viewer', { section: 'clean-data?page=3', first: page({ total: 500, page: 3 }) });
      const dialog = await openDialog(context, 0);
      typeInto(byTestId<HTMLInputElement>(dialog, 'filter-value')!, 'a');
      await click(context, byTestId(dialog, 'filter-apply'));

      const check = context.http.expectOne(isQuery);
      expect(body(check).page).toBe(1);
      check.flush(page());
    });

    it('says there is no match and offers to clear the filters', async () => {
      const filters = encodeFilters([{ column: 'name', op: 'equals', value: 'nobody' }]);
      const context = await open('viewer', { section: `clean-data?f=${filters}`, first: page({ rows: [], total: 0 }) });

      expect(text(byTestId(context.root, 'no-rows'))).toContain('No rows match the filters.');
      expect(context.root.querySelector('[data-testid="no-rows"] button')).toBeNull(); // the chips row already offers it, once
      expect(context.root.querySelectorAll('[data-testid="clear-filters"]')).toHaveLength(1);
      await click(context, byTestId(context.root, 'clear-filters'));
      const request = context.http.expectOne(isQuery);
      expect(body(request).filters).toEqual([]);
      request.flush(page());
    });
  });

  describe('the address', () => {
    it('opens the view the address describes, so a shared link shows the same rows', async () => {
      const filters = encodeFilters([{ column: 'name', op: 'in', values: ['Cali', 'Bogotá, D.C.'] }]);
      const { http, harness } = await openProjectSection(`clean-data?source=clean&table=main&sort=age:desc&page=2&size=25&f=${filters}`, 'viewer', {}, [
        { provide: Downloader, useValue: downloader },
      ]);
      http.expectOne(isOverview).flush(overview());
      await settle(harness);

      const request = http.expectOne(isQuery);
      expect(request.request.url).toBe(`${dataUrl}/clean/main/query`);
      expect(body(request)).toEqual({
        filters: [{ column: 'name', op: 'in', values: ['Cali', 'Bogotá, D.C.'] }],
        sort: { column: 'age', direction: 'desc' },
        page: 2,
        pageSize: 25,
      });
      request.flush(page({ page: 2, pageSize: 25, total: 100 }));
    });

    it('writes every change to the address', async () => {
      const context = await open('viewer');
      expect(address()).toBe(`/projects/${projectId}/clean-data`);

      await click(context, headerButtons(context.root, 'sort-button')[1]);
      await nextQuery(context);

      expect(address()).toContain('sort=age:asc');
    });

    it('ignores an address with an unknown source, table or page size', async () => {
      const context = await open('viewer', { section: 'clean-data?source=raw&table=salaries&size=7&page=x' });

      expect(byTestId<HTMLSelectElement>(context.root, 'table-select')!.value).toBe('persons');
      expect(byTestId(context.root, 'source-output')!.getAttribute('aria-pressed')).toBe('true');
    });

    it('drops a filter on a column the table no longer has, says so, and goes on with the rest', async () => {
      const filters = encodeFilters([
        { column: 'name', op: 'contains', value: 'a' },
        { column: 'salary', op: 'range', min: '1' },
      ]);
      const { http, harness, root } = await openProjectSection(`clean-data?f=${filters}&sort=salary:asc`, 'viewer', {}, [
        { provide: Downloader, useValue: downloader },
      ]);
      http.expectOne(isOverview).flush(overview());
      await settle(harness);

      http.expectOne(isQuery).flush({ errors: { 'filters[1]': ["The table has no column 'salary'."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);
      const bare = http.expectOne(isQuery); // the table without the address’s filters, to learn its columns
      expect(body(bare)).toMatchObject({ filters: [], sort: null });
      bare.flush(page());
      await settle(harness);
      const cleaned = http.expectOne(isQuery);
      expect(body(cleaned)).toMatchObject({ filters: [{ column: 'name', op: 'contains', value: 'a' }], sort: null, page: 1 });
      cleaned.flush(page({ total: 1 }));
      await settle(harness);

      expect(text(byTestId(root, 'dropped'))).toContain('The filter on salary was removed.');
      expect(text(byTestId(root, 'dropped'))).toContain('The sort on salary was removed.');
      expect(text(byTestId(root, 'filter-chip'))).toBe('name contains “a”');
      expect(address()).not.toContain('salary');
      expect(byTestId(root, 'dropped')!.getAttribute('role')).toBe('status'); // announced to screen readers

      // The notice is gone as soon as the member acts on the view.
      root.querySelector<HTMLElement>('[data-testid="sort-button"]')!.click();
      await settle(harness);
      http.expectOne(isQuery).flush(page({ total: 1 }));
      await settle(harness);
      expect(byTestId(root, 'dropped')).toBeNull();
    });

    it('drops every filter when the server refuses a value although every column exists', async () => {
      const filters = encodeFilters([{ column: 'age', op: 'range', min: 'adult' }]);
      const { http, harness, root } = await openProjectSection(`clean-data?f=${filters}`, 'viewer', {}, [{ provide: Downloader, useValue: downloader }]);
      http.expectOne(isOverview).flush(overview());
      await settle(harness);

      http.expectOne(isQuery).flush({ errors: { 'filters[0]': ["Column 'age': 'adult' is not a valid minimum."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);
      http.expectOne(isQuery).flush(page());
      await settle(harness);
      const cleaned = http.expectOne(isQuery);
      expect(body(cleaned).filters).toEqual([]);
      cleaned.flush(page());
      await settle(harness);

      expect(text(byTestId(root, 'dropped'))).toContain('The filter on age was removed.');
    });
  });

  describe('republished tables', () => {
    it('says so when a later page comes from tables published again', async () => {
      const context = await open('viewer', { first: page({ total: 120 }) });
      expect(byTestId(context.root, 'republished')).toBeNull();

      await click(context, byTestId(context.root, 'next-page'));
      await nextQuery(context, page({ total: 120, page: 2, publishedAt: '2026-10-02T13:30:00Z' }));

      expect(text(byTestId(context.root, 'republished'))).toMatch(/^The tables were republished at .*2026/);
    });

    it('says nothing when the tables are the same', async () => {
      const context = await open('viewer', { first: page({ total: 120 }) });

      await click(context, byTestId(context.root, 'next-page'));
      await nextQuery(context, page({ total: 120, page: 2 }));

      expect(byTestId(context.root, 'republished')).toBeNull();
    });

    it('reloads the list when a run removed the table, and shows the next one', async () => {
      const context = await open('viewer', { first: page({ total: 120 }) });
      await click(context, byTestId(context.root, 'next-page'));

      context.http.expectOne(isQuery).flush({ code: 'data.tableNotFound', title: 'Not found' }, { status: 404, statusText: 'Not Found' });
      await settle(context.harness);
      const reloaded = overview({
        sources: [{ source: 'output', publishedAt: '2026-10-02T13:30:00Z', runId: 'abcdef12-0000-0000-0000-000000000002', tables: [{ name: 'households', rowCount: 1200 }] }],
      });
      context.http.expectOne(isOverview).flush(reloaded);
      await settle(context.harness);
      const request = context.http.expectOne(isQuery);
      expect(request.request.url).toBe(`${dataUrl}/output/households/query`);
      request.flush(page());
      await settle(context.harness);

      expect(text(byTestId(context.root, 'table-gone'))).toBe('The table persons is no longer published. Showing households instead.');
      expect(byTestId<HTMLSelectElement>(context.root, 'table-select')!.value).toBe('households');
      expect(byTestId(context.root, 'source-switch')).toBeNull();
    });

    it('says a table is gone when the address names one that is not published', async () => {
      const context = await open('viewer', { section: 'clean-data?table=salaries' });

      expect(text(byTestId(context.root, 'table-gone'))).toBe('The table salaries is no longer published. Showing persons instead.');
    });

    it('shows the empty state when a run removed every table', async () => {
      const context = await open('viewer', { first: page({ total: 120 }) });
      await click(context, byTestId(context.root, 'next-page'));
      context.http.expectOne(isQuery).flush({ code: 'data.tableNotFound', title: 'Not found' }, { status: 404, statusText: 'Not Found' });
      await settle(context.harness);

      context.http.expectOne(isOverview).flush(overview({ sources: [], defaultSource: null }));
      await settle(context.harness);

      expect(byTestId(context.root, 'data-empty')).not.toBeNull();
    });
  });

  describe('errors', () => {
    it('shows the message when a page takes too long, and keeps the rows already shown', async () => {
      const context = await open('viewer');
      await click(context, headerButtons(context.root, 'sort-button')[0]);

      context.http.expectOne(isQuery).flush({ code: 'data.timeout', title: 'x' }, { status: 503, statusText: 'Service Unavailable' });
      await settle(context.harness);

      expect(text(byTestId(context.root, 'data-error'))).toContain('took too long');
      expect(byTestId(context.root, 'data-table')).not.toBeNull();
      expect(byTestId(context.root, 'data-error')?.getAttribute('lang')).toBeNull();
    });

    it('marks a message the API wrote as English', async () => {
      const context = await open('viewer');
      await click(context, headerButtons(context.root, 'sort-button')[0]);

      context.http.expectOne(isQuery).flush({ title: 'The table cannot be read right now.' }, { status: 500, statusText: 'Server Error' });
      await settle(context.harness);

      expect(text(byTestId(context.root, 'data-error'))).toContain('The table cannot be read right now.');
      expect(byTestId(context.root, 'data-error')?.getAttribute('lang')).toBe('en');
    });
  });

  describe('export', () => {
    const exported = (overrides: Partial<DataExport> = {}): DataExport => ({
      downloadUrl: '/api/v1/data-exports/token123',
      fileName: 'household-survey_persons_2026-10-02.csv',
      rowCount: 830,
      cutCells: 0,
      expiresAt: '2026-10-02T12:05:00Z',
      ...overrides,
    });

    async function chooseFormat(context: Context, id: 'export-csv' | 'export-xlsx') {
      const trigger = byTestId<HTMLButtonElement>(context.root, 'export-button')!;
      trigger.focus();
      pressKey(trigger, 'ArrowDown');
      await settle(context.harness);
      await click(context, byTestId(overlay(), id));
    }

    it.each<[ProjectRole, 'export-csv' | 'export-xlsx', string]>([
      ['viewer', 'export-csv', 'csv'],
      ['analyst', 'export-xlsx', 'xlsx'],
      ['admin', 'export-csv', 'csv'],
    ])('lets a %s export with the current filters and sort (%s)', async (role, id, format) => {
      const filters = encodeFilters([{ column: 'name', op: 'contains', value: 'a' }]);
      const context = await open(role, { section: `clean-data?sort=age:desc&page=3&f=${filters}`, first: page({ total: 500, page: 3 }) });

      await chooseFormat(context, id);
      const request = context.http.expectOne(isExport);

      expect(request.request.url).toBe(`${dataUrl}/output/persons/exports`);
      expect(request.request.body).toEqual({
        format,
        filters: [{ column: 'name', op: 'contains', value: 'a' }],
        sort: { column: 'age', direction: 'desc' },
      });
      request.flush(exported());
    });

    it('starts the download from the link and tells how many rows the file has', async () => {
      const context = await open('viewer');

      await chooseFormat(context, 'export-csv');
      const exportButton = byTestId<HTMLButtonElement>(context.root, 'export-button')!;
      expect(exportButton.disabled).toBe(true);
      expect(exportButton.getAttribute('aria-busy')).toBe('true');
      expect(text(exportButton)).toBe('Preparing the export…'); // the busy state is on the button, with no second message
      expect(TestBed.inject(ToastService).toasts()).toHaveLength(0);
      context.http.expectOne(isExport).flush(exported());
      await settle(context.harness);

      expect(downloader.start).toHaveBeenCalledExactlyOnceWith('/api/v1/data-exports/token123');
      expect(TestBed.inject(ToastService).toasts()).toHaveLength(1); // one message for one export
      expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toBe('Export ready: 830 rows. The download starts now.');
      expect(byTestId<HTMLButtonElement>(context.root, 'export-button')!.disabled).toBe(false);
    });

    it('reports the cells that were cut for Excel', async () => {
      const context = await open('viewer');

      await chooseFormat(context, 'export-xlsx');
      context.http.expectOne(isExport).flush(exported({ cutCells: 3 }));
      await settle(context.harness);

      expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('3 cells were longer than Excel allows and were cut.');
    });

    it('uses the singular for a file with one row and one cut cell', async () => {
      const context = await open('viewer');

      await chooseFormat(context, 'export-xlsx');
      context.http.expectOne(isExport).flush(exported({ rowCount: 1, cutCells: 1 }));
      await settle(context.harness);

      const message = TestBed.inject(ToastService).toasts().at(-1)?.message;
      expect(message).toContain('Export ready: 1 row. The download starts now.');
      expect(message).toContain('1 cell was longer than Excel allows and was cut.');
    });

    it('uses the singular in Spanish for a file with one row and one cut cell (One row)', async () => {
      const context = await open('viewer', { lang: 'es' });

      await chooseFormat(context, 'export-xlsx');
      context.http.expectOne(isExport).flush(exported({ rowCount: 1, cutCells: 1 }));
      await settle(context.harness);

      const message = TestBed.inject(ToastService).toasts().at(-1)?.message;
      expect(message).toContain(es.cleanData.export.readyOne.replace('{{ rows }}', '1'));
      expect(message).toContain(es.cleanData.export.readyCutOne.replace('{{ cut }}', '1'));
      expect(message).not.toContain('1 filas');
      expect(message).not.toContain('Export ready');
    });

    it.each([
      ['data.xlsxTooManyRows', 422, 'Export to CSV, or add a filter'],
      ['data.exportTooLarge', 422, 'Add a filter to export fewer rows'],
      ['data.exportBusy', 503, 'Other exports are being prepared'],
      ['data.timeout', 503, 'took too long'],
      ['data.tableNotFound', 404, 'no longer published'],
    ])('explains the error %s and starts no download', async (code, status, message) => {
      const context = await open('viewer');

      await chooseFormat(context, 'export-xlsx');
      context.http.expectOne(isExport).flush({ code, title: 'x' }, { status, statusText: 'Error' });
      await settle(context.harness);

      expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain(message);
      expect(downloader.start).not.toHaveBeenCalled();
      expect(byTestId<HTMLButtonElement>(context.root, 'export-button')!.disabled).toBe(false);
    });
  });

  describe('navigation', () => {
    it('is the second tab, after Performance', async () => {
      const { root } = await open('viewer');

      const tabs = Array.from(root.querySelectorAll('nav a')).map((a) => a.textContent?.trim());
      expect(tabs.slice(0, 3)).toEqual(['Performance', 'Clean data', 'Indicators']);
      expect(root.querySelectorAll('nav a')[1].getAttribute('href')).toBe(`/projects/${projectId}/clean-data`);
      expect(root.querySelector('nav a[aria-current="page"]')?.textContent?.trim()).toBe('Clean data');
    });
  });
});

