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
  downloader,
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

const answer = result([row('coverage', 'Cali', 75), row('coverage', 'Bogotá', 60)]);
const two = [widget('w1', { title: 'Coverage by municipality' }), widget('w2', { title: 'Response rate', chartType: 'stat', layout: { x: 6, y: 0, w: 3, h: 2 } })];

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
      expect(text(byTestId(root, 'indicators-published'))).toMatch(/^Published .*2026.* by run abcdef12/);
      expect(text(byTestId(root, 'indicators-count'))).toBe('2 of 50 widgets');
    });

    it('asks for the rows of each widget with its own query', async () => {
      const wide = widget('w3', { chartType: 'map', config: config({ geometryColumn: 'geometry', buckets: 20 }) });
      const context = await open('viewer', [widget('w1', { chartType: 'histogram', config: config({ buckets: 12, valueField: 'numerator' }) }), wide], null);

      const requests = context.http.match(isQuery);
      expect(requests).toHaveLength(2);
      // Settings of another chart type are not sent.
      expect(requests[0].request.body).toEqual({ ...config({ valueField: 'numerator' }), buckets: 12, geometryColumn: null });
      expect(requests[1].request.body).toEqual({ ...config(), geometryColumn: 'geometry', buckets: null });
      requests.forEach((r) => r.flush(result([])));
    });

    it.each<[ProjectRole, boolean]>([
      ['viewer', false],
      ['analyst', true],
      ['admin', true],
    ])('gives editing controls to the roles that may edit, for a %s', async (role, editor) => {
      const { root } = await open(role, two, answer);

      for (const id of ['widget-add', 'widget-move', 'widget-resize', 'widget-edit', 'widget-remove']) {
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
      second.flush(result([row('response_rate', 'A', 5)]));
      await settle(context.harness);

      expect(text(byTestId(context.root, 'view-error'))).toBe('The filter does not fit the column.');
      expect(text(byTestId(context.root, 'stat-value'))).toBe('5');
    });

    it('shows every widget as a table at once, in reading order, and back', async () => {
      const wide = [widget('w1', { title: 'Lower', layout: { x: 0, y: 4, w: 6, h: 4 } }), widget('w2', { title: 'Upper right', layout: { x: 6, y: 0, w: 6, h: 4 } }), widget('w3', { title: 'Upper left', layout: { x: 0, y: 0, w: 6, h: 4 } })];
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

  describe('nothing published yet', () => {
    const unpublished = { published: false, publishedAt: null, runId: null };

    it('says so with instructions, still lists the configured widgets and does not ask for rows', async () => {
      const context = await open('viewer', two, null, unpublished);

      expect(byTestId(context.root, 'indicators-empty')).not.toBeNull();
      expect(text(byTestId(context.root, 'indicators-empty-widgets'))).toBe('2 widgets are already configured; they draw once the indicators are published.');
      expect(all(context.root, 'widget-title').map(text)).toEqual(['Coverage by municipality', 'Response rate']);
      expect(all(context.root, 'view-idle').map(text)[0]).toContain('Nothing is published yet');
      expect(context.http.match(isQuery)).toHaveLength(0);
      expect(byTestId(context.root, 'indicators-published')).toBeNull();
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

    it('turns the export off', async () => {
      const { root } = await open('viewer', [], null, unpublished);

      expect(byTestId<HTMLButtonElement>(root, 'indicators-export')!.disabled).toBe(true);
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

  describe('stale configuration', () => {
    it('names the removed group column instead of the chart (column removed by a script change)', async () => {
      const { root } = await open('viewer', [widget('w1', { config: config({ groupBy: 'year' }) })], result([], { missing: { indicators: [], columns: ['year'] } }));

      expect(text(byTestId(root, 'widget-missing'))).toContain('no longer has the columns: year');
      expect(byTestId(root, 'missing-indicators')).toBeNull();
      expect(all(root, 'chart-svg')).toHaveLength(0);
    });

    it('names a removed indicator', async () => {
      const { root } = await open('viewer', [widget('w1')], result([], { missing: { indicators: ['coverage', 'target'], columns: [] } }));

      expect(text(byTestId(root, 'missing-indicators'))).toBe('The published table no longer has the indicators: coverage, target.');
      expect(all(root, 'chart-svg')).toHaveLength(0);
    });

    it('names a removed geometry column', async () => {
      const { root } = await open('viewer', [widget('w1', { chartType: 'map', config: config({ geometryColumn: 'boundary' }) })], result([], { missing: { indicators: [], columns: ['boundary'] } }));

      expect(text(byTestId(root, 'missing-columns'))).toBe('The published table no longer has the columns: boundary.');
      expect(all(root, 'map-region')).toHaveLength(0);
    });

    it('keeps the other widgets drawing', async () => {
      const context = await open('viewer', two, null);
      const [first, second] = context.http.match(isQuery);
      first.flush(result([], { missing: { indicators: ['coverage'], columns: [] } }));
      second.flush(result([row('response_rate', 'A', 5)]));
      await settle(context.harness);

      expect(all(context.root, 'widget-missing')).toHaveLength(1);
      expect(text(byTestId(context.root, 'stat-value'))).toBe('5');
    });
  });

  describe('exporting', () => {
    const exported = { downloadUrl: '/api/v1/data-exports/token', fileName: 'indicators.xlsx', rowCount: 120, cutCells: 0, expiresAt: '2026-10-02T12:05:00Z' };

    it.each<[ProjectRole, 'csv' | 'xlsx']>([
      ['viewer', 'xlsx'],
      ['analyst', 'csv'],
      ['admin', 'xlsx'],
    ])('lets a %s export the whole table as %s through a single-use link', async (role, format) => {
      const { http, harness, root } = await open(role, [], null);

      byTestId(root, 'indicators-export')!.click();
      await settle(harness);
      (overlay().querySelector(`[data-testid="indicators-export-${format}"]`) as HTMLElement).click();
      await settle(harness);

      const request = http.expectOne(`${indicatorsUrl}/exports`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ format });
      request.flush(exported);
      await settle(harness);
      expect(downloader.start).toHaveBeenCalledWith('/api/v1/data-exports/token');
    });

    it('reports a refused export', async () => {
      const { http, harness, root } = await open('viewer', [], null);
      const toasts = TestBed.inject(ToastService);

      byTestId(root, 'indicators-export')!.click();
      await settle(harness);
      (overlay().querySelector('[data-testid="indicators-export-csv"]') as HTMLElement).click();
      await settle(harness);
      http.expectOne(`${indicatorsUrl}/exports`).flush({ code: 'data.exportBusy', title: 'x' }, { status: 429, statusText: 'Too Many Requests' });
      await settle(harness);

      expect(downloader.start).not.toHaveBeenCalled();
      expect(toasts.toasts().some((t) => t.message.includes('Other exports are being prepared'))).toBe(true);
    });
  });

  describe('widgets: add, edit, remove', () => {
    async function openEditor(widgets = two) {
      const context = await open('analyst', widgets, answer);
      return context;
    }

    async function openDialog(context: Context, button: string, index = 0) {
      all(context.root, button)[index].click();
      await settle(context.harness);
      if (button === 'widget-add' || button === 'widget-edit') {
        context.http.expectOne(isColumns).flush(catalog());
        await settle(context.harness);
      }
    }

    it('adds a widget and shows it', async () => {
      const context = await openEditor();
      await openDialog(context, 'widget-add');

      expect(byTestId(overlay(), 'widget-dialog')).not.toBeNull();
      const dialog = overlay();
      const title = byTestId<HTMLInputElement>(dialog, 'dialog-title')!;
      title.value = 'Coverage by municipality';
      title.dispatchEvent(new Event('input'));
      (all(dialog, 'indicator-option')[1] as HTMLInputElement).click();
      await settle(context.harness);
      byTestId(dialog, 'dialog-save')!.click();
      await settle(context.harness);

      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === `${indicatorsUrl}/widgets`);
      expect(request.request.body).toEqual({ title: 'Coverage by municipality', chartType: 'column', config: { ...config(), buckets: null, geometryColumn: null } });
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
      byTestId(dialog, 'dialog-save')!.click();
      await settle(context.harness);

      const request = context.http.expectOne((r) => r.method === 'PATCH' && r.url === `${indicatorsUrl}/widgets/w1`);
      expect(request.request.body).toEqual({ title: 'Coverage', chartType: 'column', config: { ...config(), buckets: null, geometryColumn: null } });
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
});
