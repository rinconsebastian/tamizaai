import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ProjectRole } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
import { byTestId, openProjectSection, projectId, settle } from '../../../testing/project-harness';
import {
  Context,
  all,
  answerQueries,
  catalog,
  clearOverlay,
  config,
  defaultIndicators,
  downloader,
  indicator,
  indicatorsUrl,
  isColumns,
  isOverview,
  isQuery,
  open,
  overlay,
  result,
  row,
  text,
  widget,
} from './indicators/indicators-testing';

const answer = result([row('Cali', 75), row('Bogotá', 60)]);
const two = [
  widget('w1', { title: 'Coverage by municipality' }),
  widget('w2', { title: 'Response rate', chartType: 'stat', config: config({ indicator: 'response_rate', groupBy: null }), layout: { x: 6, y: 0, w: 3, h: 2 } }),
];
/** Every indicator of the project is listed and none has a table. */
const unpublished = { indicators: defaultIndicators.map((i) => indicator(i.name, { published: false, publishedAt: null, runId: null, rows: null })) };

describe('ProjectIndicators', () => {
  beforeEach(() => downloader.start.mockClear());
  afterEach(clearOverlay);

  describe('the tab', () => {
    it.each<ProjectRole>(['viewer', 'analyst', 'admin'])('is the third tab, after Performance and Clean data, for a %s', async (role) => {
      const { root } = await open(role, [], null);

      const tabs = Array.from(root.querySelectorAll('nav a')).map((a) => a.textContent?.trim());
      expect(tabs.slice(0, 3)).toEqual(['Performance', 'Clean data', 'Indicators']);
      expect(root.querySelectorAll('nav a')[2].getAttribute('href')).toBe(`/projects/${projectId}/indicators`);
      expect(root.querySelector('nav a[aria-current="page"]')?.textContent?.trim()).toBe('Indicators');
      expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/indicators`);
    });
  });

  describe('a published dashboard', () => {
    it.each<ProjectRole>(['viewer', 'analyst', 'admin'])('shows every widget with its title, for a %s', async (role) => {
      const { root } = await open(role, two, answer);

      expect(all(root, 'widget-title').map(text)).toEqual(['Coverage by municipality', 'Response rate']);
      expect(byTestId(root, 'indicators-empty')).toBeNull();
      // Only whoever can add widgets is told how many there are; the limit is theirs to run into.
      expect(byTestId(root, 'indicators-count') === null).toBe(role === 'viewer');
      if (role !== 'viewer') {
        expect(text(byTestId(root, 'indicators-count'))).toBe('2 of 50 widgets');
      }
    });

    it('lists the indicators with their label, name, format, unit and whether they have published', async () => {
      const indicators = [
        indicator('coverage'),
        indicator('households_visited', { description: 'Visits made', rows: 12450 }),
        indicator('target', { published: false, publishedAt: null, runId: null, rows: null }),
      ];
      const { root } = await open('viewer', [], null, { indicators });

      const card = byTestId(root, 'indicator-table-card')!;
      expect(text(card.querySelector('summary h3'))).toBe('Indicators (3)'); // behind a disclosure, after the widgets
      expect(text(card.querySelector('summary'))).toContain('· 2 published');
      expect((card.querySelector('details') as HTMLDetailsElement).open).toBe(false);
      const rows = indicators.map((i) => byTestId(root, `indicator-row-${i.name}`)!);
      expect(rows.map((r) => text(byTestId(r, 'indicator-label')))).toEqual(['Coverage', 'Households visited', 'Target']);
      expect(rows.map((r) => text(byTestId(r, 'indicator-name')))).toEqual(['coverage', 'households_visited', 'target']);
      expect(rows.map((r) => text(byTestId(r, 'indicator-format')))).toEqual(['Count', 'Count · households', 'Count']);
      expect(text(byTestId(rows[0], 'indicator-published'))).toMatch(/^Published .*2026, \d{1,2}:\d{2} [AP]M$/); // to the minute
      expect(text(rows[1])).not.toContain('Visits made'); // the description is the widget dialog's hint, not the list's
      expect(text(rows[1])).toContain('12,450');
      expect(text(byTestId(rows[2], 'indicator-unpublished'))).toBe('Not published yet');
      expect(byTestId(rows[2], 'indicator-published')).toBeNull();
    });

    it('asks for the rows of each widget with its own query and chart type', async () => {
      const wide = widget('w3', { chartType: 'map', config: config({ geometryColumn: 'geometry', buckets: 20 }) });
      const context = await open('viewer', [widget('w1', { chartType: 'histogram', config: config({ buckets: 12, measure: 'numerator', groupBy: null }) }), wide], null);

      const requests = context.http.match(isQuery);
      expect(requests).toHaveLength(2);
      // Settings of another chart type are not sent.
      expect(requests[0].request.body).toEqual({ config: config({ measure: 'numerator', buckets: 12, groupBy: null }), chartType: 'histogram' });
      expect(requests[1].request.body).toEqual({ config: config({ geometryColumn: 'geometry' }), chartType: 'map' });
      requests.forEach((r) => r.flush(result([])));
    });

    it.each<[ProjectRole, boolean]>([
      ['viewer', false],
      ['analyst', true],
      ['admin', true],
    ])('gives editing controls to the roles that may edit, for a %s', async (role, editor) => {
      const { root } = await open(role, two, answer);

      for (const id of ['widget-add', 'widget-move', 'widget-resize', 'menu-trigger']) {
        expect(all(root, id).length > 0, id).toBe(editor);
      }
      expect(all(root, 'widget-move')).toHaveLength(editor ? 2 : 0);
    });

    it('tells a viewer there are no widgets yet, and an analyst how to add one', async () => {
      const viewer = await open('viewer', [], null);
      expect(text(byTestId(viewer.root, 'no-widgets'))).toContain('An analyst or admin can add widgets');
    });

    it('tells an analyst how to add the first widget', async () => {
      const { root } = await open('analyst', [], null);
      expect(text(byTestId(root, 'no-widgets'))).toContain('Add a widget');
    });

    it('shows a notice when a widget\'s query matched more rows than the limit', async () => {
      const { root } = await open('viewer', [widget('w1')], result(answer.rows, { rowLimitReached: true }));

      expect(text(byTestId(root, 'widget-row-limit'))).toContain('Only the first 2 rows are shown');
      expect(all(root, 'chart-svg')).toHaveLength(1); // the chart is still drawn
    });

    it('shows when a widget\'s query matched no rows', async () => {
      const { root } = await open('viewer', [widget('w1')], result([]));

      expect(text(byTestId(root, 'widget-empty'))).toBe('No rows match this widget’s query.');
    });

    it('shows the error of a widget whose query is refused, and keeps the others', async () => {
      const context = await open('viewer', two, null);
      const [first, second] = context.http.match(isQuery);
      first.flush({ title: 'x', errors: { 'filters[0]': ['The filter does not fit the column.'] } }, { status: 400, statusText: 'Bad Request' });
      second.flush(result([row(null, 3.5)]));
      await settle(context.harness);

      expect(text(byTestId(context.root, 'view-error'))).toBe('The filter does not fit the column.');
      expect(text(byTestId(context.root, 'stat-value'))).toBe('3.50');
    });

    it('shows every widget as a table at once, in reading order, and back', async () => {
      const wide = [
        widget('w1', { title: 'Lower', layout: { x: 0, y: 4, w: 6, h: 4 } }),
        widget('w2', { title: 'Upper right', layout: { x: 6, y: 0, w: 6, h: 4 } }),
        widget('w3', { title: 'Upper left', layout: { x: 0, y: 0, w: 6, h: 4 } }),
      ];
      const { root, harness } = await open('viewer', wide, answer);

      byTestId(root, 'tables-toggle')!.click();
      await settle(harness);

      expect(all(root, 'widget-title').map(text)).toEqual(['Upper left', 'Upper right', 'Lower']);
      expect(all(root, 'widget-table')).toHaveLength(3);
      expect(all(root, 'chart-svg')).toHaveLength(0);

      byTestId(root, 'tables-toggle')!.click();
      await settle(harness);
      expect(all(root, 'chart-svg')).toHaveLength(3);
    });
  });

  describe('no indicator has published', () => {
    it('says so with instructions, still lists the configured widgets and does not ask for rows', async () => {
      const context = await open('viewer', two, null, unpublished);

      expect(byTestId(context.root, 'indicators-empty')).not.toBeNull();
      expect(text(byTestId(context.root, 'indicators-empty-widgets'))).toBe('2 widgets are already configured; they draw once their indicators are published.');
      expect(all(context.root, 'widget-title').map(text)).toEqual(['Coverage by municipality', 'Response rate']);
      expect(all(context.root, 'view-not-published').map(text)).toEqual(['Coverage has not published yet.', 'Response rate has not published yet.']);
      expect(context.http.match(isQuery)).toHaveLength(0);
      expect(all(context.root, 'indicator-unpublished')).toHaveLength(defaultIndicators.length);
    });

    it('says so too when the project has no indicators at all', async () => {
      const { root } = await open('analyst', [], null, { indicators: [] });

      expect(byTestId(root, 'indicators-empty')).not.toBeNull();
      expect(byTestId(root, 'indicator-table')).toBeNull();
      expect(byTestId(root, 'no-widgets')).toBeNull();
      expect(byTestId<HTMLButtonElement>(root, 'widget-add')!.disabled).toBe(true); // a widget needs an indicator
    });

    it('does not say so once one indicator has published', async () => {
      const indicators = [indicator('coverage'), indicator('target', { published: false, publishedAt: null, runId: null, rows: null })];
      const { root } = await open('viewer', [], null, { indicators });

      expect(byTestId(root, 'indicators-empty')).toBeNull();
      expect(text(byTestId(root, 'no-widgets'))).toContain('An analyst or admin can add widgets');
    });

    it.each<[ProjectRole, boolean]>([
      ['analyst', true],
      ['admin', true],
      ['viewer', false],
    ])('points an editor to the scripts, not a viewer, for a %s', async (role, link) => {
      const { root } = await open(role, [], null, unpublished);

      expect(byTestId(root, 'indicators-empty-link') !== null).toBe(link);
      expect(byTestId(root, 'indicators-empty-viewer') !== null).toBe(!link);
      expect(byTestId(root, 'no-widgets')).toBeNull();
    });

    it('turns the export of every indicator off', async () => {
      const { root } = await open('viewer', [], null, unpublished);

      // Nothing has a table, so there is nothing to export: no export control is offered at all.
      expect(all(root, 'indicator-table')).toHaveLength(1);
      expect(root.querySelectorAll('[data-testid^="indicator-export-"]')).toHaveLength(0);
    });
  });

  describe('the overview cannot be read', () => {
    it('shows the error', async () => {
      const { http, harness, root } = await openProjectSection('indicators', 'viewer');
      http.expectOne(isOverview).flush({}, { status: 500, statusText: 'Server Error' });
      await settle(harness);

      expect(text(byTestId(root, 'indicators-error'))).toBe('Something went wrong. Try again.');
    });
  });

  describe('an indicator that has not published', () => {
    const attendance = widget('w1', { title: 'Attendance by round', config: config({ indicator: 'attendance', groupBy: 'round' }) });
    const neverPublished = { indicators: [indicator('coverage'), indicator('attendance', { published: false, publishedAt: null, runId: null, rows: null })] };

    it('says the indicator has not published yet instead of its chart, without asking for rows', async () => {
      const context = await open('viewer', [attendance, widget('w2', { title: 'Coverage' })], null, neverPublished);

      const requests = context.http.match(isQuery);
      expect(requests).toHaveLength(1); // only the coverage widget asks
      expect(requests[0].request.body.config.indicator).toBe('coverage');
      requests[0].flush(answer);
      await settle(context.harness);

      const notices = all(context.root, 'view-not-published');
      expect(notices.map(text)).toEqual(['Attendance has not published yet.']);
      expect(all(context.root, 'chart-svg')).toHaveLength(1);
      expect(byTestId(context.root, 'indicators-empty')).toBeNull(); // another indicator has published
    });

    it.each<[ProjectRole, boolean]>([
      ['analyst', true],
      ['admin', true],
      ['viewer', false],
    ])('links to the scripts for an editor, not for a viewer, for a %s', async (role, link) => {
      const { root } = await open(role, [attendance], null, neverPublished);

      const anchor = byTestId(root, 'view-scripts-link');
      expect(anchor !== null).toBe(link);
      if (anchor) {
        expect(anchor.getAttribute('href')).toBe(`/projects/${projectId}/scripts`);
      }
    });

    it('says so when the query answers that the indicator has no table, as a run may have removed it since', async () => {
      const { root } = await open('analyst', [widget('w1')], result([], { published: false, missing: { indicator: true, columns: [] } }));

      expect(text(byTestId(root, 'view-not-published')!.querySelector('p'))).toBe('Coverage has not published yet.');
      expect(byTestId(root, 'view-scripts-link')).not.toBeNull();
      expect(all(root, 'chart-svg')).toHaveLength(0);
    });

    it('names the indicator when the project no longer lists it', async () => {
      const { root } = await open('viewer', [widget('w1', { config: config({ indicator: 'removed_one' }) })], result([], { published: false, missing: { indicator: true, columns: [] } }));

      expect(text(byTestId(root, 'view-not-published'))).toBe('removed_one has not published yet.');
    });

    it('keeps the other widgets drawing', async () => {
      const context = await open('viewer', two, null);
      const [first, second] = context.http.match(isQuery);
      first.flush(result([], { missing: { indicator: true, columns: [] } }));
      second.flush(result([row(null, 3.5)]));
      await settle(context.harness);

      expect(all(context.root, 'view-not-published')).toHaveLength(1);
      expect(text(byTestId(context.root, 'stat-value'))).toBe('3.50');
    });
  });

  describe('stale configuration', () => {
    it('names the removed group column instead of the chart (column removed by a script change)', async () => {
      const { root } = await open('viewer', [widget('w1', { config: config({ groupBy: 'year' }) })], result([], { missing: { indicator: false, columns: ['year'] } }));

      expect(text(byTestId(root, 'widget-missing'))).toContain('no longer has the columns: year');
      expect(byTestId(root, 'view-not-published')).toBeNull();
      expect(all(root, 'chart-svg')).toHaveLength(0);
    });

    it('names a removed measure, or the columns of a removed ratio', async () => {
      const ratio = { numerator: 'cases', denominator: 'population' };
      const { root } = await open('viewer', [widget('w1', { config: config({ ratio }) })], result([], { missing: { indicator: false, columns: ['cases', 'population'] } }));

      expect(text(byTestId(root, 'missing-columns'))).toBe('The published table no longer has the columns: cases, population.');
    });

    it('names a removed geometry column', async () => {
      const { root } = await open('viewer', [widget('w1', { chartType: 'map', config: config({ geometryColumn: 'boundary' }) })], result([], { missing: { indicator: false, columns: ['boundary'] } }));

      expect(text(byTestId(root, 'missing-columns'))).toBe('The published table no longer has the columns: boundary.');
      expect(all(root, 'map-region')).toHaveLength(0);
    });

    it('keeps the other widgets drawing', async () => {
      const context = await open('viewer', two, null);
      const [first, second] = context.http.match(isQuery);
      first.flush(result([], { missing: { indicator: false, columns: ['municipality'] } }));
      second.flush(result([row(null, 3.5)]));
      await settle(context.harness);

      expect(all(context.root, 'widget-missing')).toHaveLength(1);
      expect(text(byTestId(context.root, 'stat-value'))).toBe('3.50');
    });
  });

  describe('exporting', () => {
    const exported = { downloadUrl: '/api/v1/data-exports/token', fileName: 'coverage.xlsx', rowCount: 120, cutCells: 0, expiresAt: '2026-10-02T12:05:00Z' };

    async function exportAs(context: Context, name: string, format: 'csv' | 'xlsx') {
      byTestId(context.root, `indicator-export-${name}`)!.click();
      await settle(context.harness);
      (overlay().querySelector(`[data-testid="indicators-export-${format}"]`) as HTMLElement).click();
      await settle(context.harness);
    }

    it.each<[ProjectRole, 'csv' | 'xlsx']>([
      ['viewer', 'xlsx'],
      ['analyst', 'csv'],
      ['admin', 'xlsx'],
    ])('lets a %s export the whole table of an indicator as %s through a single-use link', async (role, format) => {
      const context = await open(role, [], null);

      await exportAs(context, 'households_visited', format);

      const request = context.http.expectOne(`${indicatorsUrl}/households_visited/exports`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ format });
      request.flush(exported);
      await settle(context.harness);
      expect(downloader.start).toHaveBeenCalledWith('/api/v1/data-exports/token');
    });

    it('turns the export off for an indicator that has not published, and only for it', async () => {
      const indicators = [indicator('coverage'), indicator('target', { published: false, publishedAt: null, runId: null, rows: null })];
      const { root } = await open('viewer', [], null, { indicators });

      expect(byTestId<HTMLButtonElement>(root, 'indicator-export-coverage')!.disabled).toBe(false);
      expect(byTestId(root, 'indicator-export-target')).toBeNull();
    });

    it('reports a refused export', async () => {
      const context = await open('viewer', [], null);
      const toasts = TestBed.inject(ToastService);

      await exportAs(context, 'coverage', 'csv');
      context.http.expectOne(`${indicatorsUrl}/coverage/exports`).flush({ code: 'data.exportBusy', title: 'x' }, { status: 429, statusText: 'Too Many Requests' });
      await settle(context.harness);

      expect(downloader.start).not.toHaveBeenCalled();
      expect(toasts.toasts().some((t) => t.message.includes('Other exports are being prepared'))).toBe(true);
    });

    it('says the indicator has not published when its table went away since the page loaded', async () => {
      const context = await open('viewer', [], null);
      const toasts = TestBed.inject(ToastService);

      await exportAs(context, 'coverage', 'xlsx');
      context.http.expectOne(`${indicatorsUrl}/coverage/exports`).flush({ code: 'indicators.notPublished', title: 'x' }, { status: 404, statusText: 'Not Found' });
      await settle(context.harness);

      expect(toasts.toasts().some((t) => t.message.includes('has not published a table yet'))).toBe(true);
    });
  });

  describe('widgets: add, edit, remove', () => {
    async function openEditor(widgets = two) {
      return open('analyst', widgets, answer);
    }

    /** Opens the widget dialog, which reads the columns of the indicator it starts on. */
    async function openDialog(context: Context, button: string, index = 0) {
      if (button === 'widget-edit' || button === 'widget-remove') {
        // Edit and Remove are the two items of the widget's actions menu.
        all(context.root, 'menu-trigger')[index].click();
        await settle(context.harness);
        (document.querySelector(`[data-testid="${button}"]`) as HTMLElement).click();
      } else {
        all(context.root, button)[index].click();
      }
      await settle(context.harness);
      if (button === 'widget-add' || button === 'widget-edit') {
        context.http.expectOne(isColumns).flush(catalog());
        await settle(context.harness);
      }
    }

    it('adds a widget and shows it', async () => {
      const context = await openEditor();
      await openDialog(context, 'widget-add');

      const dialog = overlay();
      expect(byTestId(dialog, 'widget-dialog')).not.toBeNull();
      const title = byTestId<HTMLInputElement>(dialog, 'dialog-title')!;
      title.value = 'Coverage by municipality';
      title.dispatchEvent(new Event('input'));
      await settle(context.harness);
      byTestId(dialog, 'dialog-save')!.click();
      await settle(context.harness);

      // A new widget starts on the first indicator that has published, grouped by the table's default group column.
      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === `${indicatorsUrl}/widgets`);
      expect(request.request.body).toEqual({ title: 'Coverage by municipality', chartType: 'column', config: config({ indicator: 'attendance' }) });
      request.flush(widget('w3', { title: 'Coverage by municipality', layout: { x: 0, y: 4, w: 6, h: 4 } }));
      await settle(context.harness);
      await answerQueries(context, answer);

      expect(all(context.root, 'widget-title').map(text)).toEqual(['Coverage by municipality', 'Response rate', 'Coverage by municipality']);
      expect(byTestId(overlay(), 'widget-dialog')).toBeNull();
    });

    it('edits a widget with a patch of its title, type and query', async () => {
      const context = await openEditor();
      await openDialog(context, 'widget-edit');
      context.http.match(isQuery).forEach((r) => r.flush(answer)); // the preview of the saved query

      const dialog = overlay();
      expect(byTestId<HTMLInputElement>(dialog, 'dialog-title')!.value).toBe('Coverage by municipality');
      const title = byTestId<HTMLInputElement>(dialog, 'dialog-title')!;
      title.value = 'Coverage';
      title.dispatchEvent(new Event('input'));
      await settle(context.harness);
      byTestId(dialog, 'dialog-save')!.click();
      await settle(context.harness);

      const request = context.http.expectOne((r) => r.method === 'PATCH' && r.url === `${indicatorsUrl}/widgets/w1`);
      expect(request.request.body).toEqual({ title: 'Coverage', chartType: 'column', config: config() });
      request.flush(widget('w1', { title: 'Coverage' }));
      await settle(context.harness);
      await answerQueries(context, answer);

      expect(all(context.root, 'widget-title').map(text)).toEqual(['Coverage', 'Response rate']);
    });

    it('removes a widget after confirming', async () => {
      const context = await openEditor();
      await openDialog(context, 'widget-remove', 1);

      expect(text(overlay())).toContain('Remove Response rate?');
      (overlay().querySelector('[data-testid="confirm"]') as HTMLElement).click();
      await settle(context.harness);
      const request = context.http.expectOne((r) => r.method === 'DELETE');
      expect(request.request.url).toBe(`${indicatorsUrl}/widgets/w2`);
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle(context.harness);

      expect(all(context.root, 'widget-title').map(text)).toEqual(['Coverage by municipality']);
    });

    it('keeps the widget when the removal is cancelled', async () => {
      const context = await openEditor();
      await openDialog(context, 'widget-remove', 0);

      (overlay().querySelector('button[data-testid="confirm"]')!.previousElementSibling as HTMLElement).click();
      await settle(context.harness);

      context.http.expectNone((r) => r.method === 'DELETE');
      expect(all(context.root, 'widget-title')).toHaveLength(2);
    });

    it('turns Add off, with a notice, when the widget limit is reached', async () => {
      const { root } = await open('analyst', two, answer, { limits: { maxWidgets: 2, maxQueryRows: 5000 } });

      expect(byTestId<HTMLButtonElement>(root, 'widget-add')!.disabled).toBe(true);
      expect(text(byTestId(root, 'widget-limit'))).toContain('(2)');
    });
  });

  describe('the restructured page', () => {
    it('puts the widgets before the indicator list, which waits behind a closed disclosure', async () => {
      const { root } = await open('viewer', two, answer);
      const grid = byTestId(root, 'widget-grid')!;
      const card = byTestId(root, 'indicator-table-card')!;
      expect(grid.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect((card.querySelector('details') as HTMLDetailsElement).open).toBe(false);
    });

    it('shows the indicators expanded inside the empty state while none has published', async () => {
      const { root } = await open('analyst', [], null, unpublished);
      const empty = byTestId(root, 'indicators-empty')!;
      expect(empty.contains(byTestId(root, 'indicator-table'))).toBe(true);
      expect(root.querySelector('details')).toBeNull(); // no disclosure to open: the list is part of the message
    });

    it('has no card around the toolbar and shows a viewer only the switch to tables, with no count', async () => {
      const { root } = await open('viewer', two, answer);
      const toolbar = byTestId(root, 'indicators-toolbar')!;
      expect(toolbar.closest('tmz-card')).toBeNull();
      expect(byTestId(toolbar, 'indicators-count')).toBeNull();
      expect(byTestId(toolbar, 'tables-toggle')).not.toBeNull();
      expect(byTestId(toolbar, 'widget-add')).toBeNull();
    });

    it('merges the count and the limit into one line for an editor', async () => {
      const { root } = await open('analyst', two, answer, { limits: { maxWidgets: 2, maxQueryRows: 5000 } });
      expect(byTestId(root, 'indicators-count')).toBeNull();
      expect(text(byTestId(root, 'widget-limit'))).toContain('(2)');
      expect(byTestId<HTMLButtonElement>(root, 'widget-add')!.disabled).toBe(true);
    });

    it('hides the toolbar when there are no widgets and offers Add inside the empty card', async () => {
      const { root } = await open('analyst', [], null);
      expect(byTestId(root, 'indicators-toolbar')).toBeNull();
      expect(byTestId(byTestId(root, 'no-widgets')!, 'widget-add')).not.toBeNull();
    });

    it('offers the table toggle in the tile header only once the widget has something to draw, and not for a stat tile', async () => {
      const { root } = await open('viewer', two, answer);
      const tiles = all(root, 'widget');
      expect(byTestId(tiles[0], 'widget-table-toggle')).not.toBeNull();
      expect(byTestId(tiles[0], 'chart-toggle')).toBeNull(); // not also in the body
      expect(byTestId(tiles[1], 'widget-table-toggle')).toBeNull(); // a single figure has no plot
    });

    it('does not offer the table toggle while the widget has no rows', async () => {
      const { root } = await open('viewer', [widget('w1')], result([]));
      expect(byTestId(root, 'widget-table-toggle')).toBeNull();
    });

    it('keeps a tile short while its indicator has not published', async () => {
      const { root } = await open('viewer', [widget('w1')], null, unpublished);
      expect(byTestId(root, 'widget')!.style.gridRow).toContain('span 2');
      expect(byTestId(root, 'widget')!.style.gridRow).not.toContain('span 4');
    });

    it('collapses edit and remove into one actions menu named after the widget', async () => {
      const context = await open('analyst', two, answer);
      const trigger = all(context.root, 'menu-trigger')[0];
      expect(trigger.getAttribute('aria-label')).toBe('Actions for Coverage by municipality');
      expect(all(context.root, 'widget-edit')).toHaveLength(0); // the items exist once the menu is open
      trigger.click();
      await settle(context.harness);
      const items = Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]'));
      expect(items.map((i) => text(i))).toEqual(['Edit widget', 'Remove widget']);
    });
  });

  describe('exporting one indicator', () => {
    const exported = { downloadUrl: '/api/v1/data-exports/token', fileName: 'coverage.csv', rowCount: 120, cutCells: 0, expiresAt: '2026-10-02T12:05:00Z' };

    it('shows the busy state on that indicator only, and ends with one message', async () => {
      const context = await open('viewer', [], null);
      byTestId(context.root, 'indicator-export-coverage')!.click();
      await settle(context.harness);
      (overlay().querySelector('[data-testid="indicators-export-csv"]') as HTMLElement).click();
      await settle(context.harness);

      const busy = byTestId<HTMLButtonElement>(context.root, 'indicator-export-coverage')!;
      const other = byTestId<HTMLButtonElement>(context.root, 'indicator-export-households_visited')!;
      expect(busy.disabled).toBe(true);
      expect(busy.getAttribute('aria-busy')).toBe('true');
      expect(busy.getAttribute('aria-label')).toBe('Preparing the export…');
      expect(other.disabled).toBe(false);
      expect(TestBed.inject(ToastService).toasts()).toHaveLength(0);

      context.http.expectOne(`${indicatorsUrl}/coverage/exports`).flush(exported);
      await settle(context.harness);
      expect(busy.disabled).toBe(false);
      expect(TestBed.inject(ToastService).toasts()).toHaveLength(1);
    });
  });
});
