import { TestBed } from '@angular/core/testing';
import { settle } from '../../../../testing/project-harness';
import { ToastService } from '../../../shared/ui';
import { DashboardFilter } from '../../../core/projects/indicators.service';
import { Context, all, clearOverlay, config, dashboardUrl, downloader, open, result, row, text, wait, widget } from './indicators-testing';

const rows = result([row('Cali', 75), row('Pasto', 40)]);
const coverage = widget('w1', { title: 'Coverage by municipality', layout: { x: 0, y: 0, w: 6, h: 4 } });
const exportsUrl = `${dashboardUrl}/widget-exports`;
const isExport = (r: { url: string; method: string }) => r.method === 'POST' && r.url === exportsUrl;
const answer = { downloadUrl: '/api/v1/data-exports/t0k3n', fileName: 'x.xlsx', rowCount: 32, publishedAt: '2026-10-02T12:00:00Z', expiresAt: '2026-10-02T12:05:00Z' };
const toasts = () => TestBed.inject(ToastService).toasts().map((toast) => String(toast.message));

async function choose(context: Context, format: 'png' | 'svg' | 'xlsx', index = 0) {
  all(context.root, 'widget-menu')[index].click();
  await settle(context.harness);
  document.querySelector<HTMLElement>(`[data-testid="widget-export-${format}"]`)!.click();
  await settle(context.harness);
}

// Making a widget's files from the dashboard page (add-widget-export: Widget workbook, Feedback while a widget export runs).
describe('exporting a widget from the dashboard', () => {
  beforeEach(() => {
    downloader.start.mockClear();
    downloader.save.mockClear();
  });
  afterEach(() => {
    clearOverlay();
  });

  describe('the workbook', () => {
    it('sends the widget\'s query, the interface\'s locale and no version, downloads the link and ends with one message', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      const request = context.http.expectOne(isExport);

      expect(request.request.body).toEqual({
        format: 'xlsx',
        title: 'Coverage by municipality',
        chartType: 'column',
        config: expect.objectContaining({ indicator: 'coverage', groupBy: 'municipality', ignoredDashboardFilters: [] }),
        filters: [],
        locale: expect.stringMatching(/^[a-z]{2}(-[A-Z]{2})?$/),
      });
      expect('version' in request.request.body).toBe(false);
      request.flush(answer);
      await settle(context.harness);

      expect(downloader.start).toHaveBeenCalledWith('/api/v1/data-exports/t0k3n');
      expect(toasts()).toEqual(['The workbook of “Coverage by municipality” is ready: 32 rows. The download starts now.']);
    });

    it('says only that widget is busy until the file is ready', async () => {
      const second = widget('w2', { title: 'Second', layout: { x: 6, y: 0, w: 6, h: 4 } });
      const context = await open('viewer', [coverage, second], rows);

      await choose(context, 'xlsx');
      const [busy, idle] = all(context.root, 'widget-menu');
      expect(busy.getAttribute('aria-busy')).toBe('true');
      expect(idle.getAttribute('aria-busy')).toBeNull();

      context.http.expectOne(isExport).flush(answer);
      await settle(context.harness);
      expect(all(context.root, 'widget-menu')[0].getAttribute('aria-busy')).toBeNull();
    });

    it('says "1 row" for one row', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      context.http.expectOne(isExport).flush({ ...answer, rowCount: 1 });
      await settle(context.harness);

      expect(toasts()).toEqual(['The workbook of “Coverage by municipality” is ready: 1 row. The download starts now.']);
    });

    it('sends the dashboard filters in force, marking the ones the widget ignores', async () => {
      const filters: DashboardFilter[] = [
        { column: 'municipality', label: 'Municipality', kind: 'text', default: { values: ['Cali'] } },
        { column: 'round', label: 'Round', kind: 'number', default: { min: '3' } },
      ];
      const ignoring = widget('w2', { title: 'Ignoring', layout: { x: 6, y: 0, w: 6, h: 4 }, config: config({ ignoredDashboardFilters: ['round'] }) });
      const context = await open('viewer', [coverage, ignoring], rows, {}, { filters });

      await choose(context, 'xlsx', 1);

      expect(context.http.expectOne(isExport).request.body.filters).toEqual([
        { column: 'municipality', kind: 'text', values: ['Cali'], ignored: false },
        { column: 'round', kind: 'number', min: '3', ignored: true },
      ]);
    });

    it('adds that the file holds data published after the page was loaded', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      context.http.expectOne(isExport).flush({ ...answer, publishedAt: '2026-10-02T15:41:00Z' });
      await settle(context.harness);

      expect(toasts()[0]).toMatch(/is ready: 32 rows\. The download starts now\. It holds data published .+, after this page was loaded\. Reload the page to see it\.$/);
    });

    it('tells the member to reload when the dashboard\'s filters changed, and opens again', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      context.http.expectOne(isExport).flush({ code: 'widgetExports.filtersChanged', title: 'Conflict' }, { status: 409, statusText: 'Conflict' });
      await settle(context.harness);

      expect(downloader.start).not.toHaveBeenCalled();
      expect(toasts()).toEqual(['The dashboard’s filters changed since the page was opened. Reload the dashboard and try again.']);
      expect(all(context.root, 'widget-menu')[0].getAttribute('aria-busy')).toBeNull();
    });

    it('shows the API\'s own words when a column is gone', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      context.http.expectOne(isExport).flush({ code: 'widgetExports.missingColumns', detail: 'The published table no longer has the columns: municipality.' }, { status: 422, statusText: 'Unprocessable' });
      await settle(context.harness);

      expect(toasts()).toEqual(['The published table no longer has the columns: municipality.']);
    });
  });

  describe('an image', () => {
    it('saves an SVG under the name of the project, the dashboard, the widget and the day, and says so once', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'svg');
      await wait(300);
      await settle(context.harness);

      expect(downloader.save).toHaveBeenCalledTimes(1);
      const [blob, name] = downloader.save.mock.calls[0] as [Blob, string];
      expect(name).toMatch(/^[a-z0-9-]+_indicators_coverage-by-municipality_\d{4}-\d{2}-\d{2}\.svg$/);
      expect(blob.type).toBe('image/svg+xml;charset=utf-8');
      const svg = await blob.text();
      expect(svg).toContain('>Coverage by municipality<');
      expect(svg).not.toMatch(/data-testid|class=|<script|href=/);
      expect(toasts()).toEqual(['“Coverage by municipality” was saved as an SVG image.']);
      expect(context.http.match((r) => r.method === 'POST' && r.url.endsWith('/widget-exports'))).toHaveLength(0);
    });

    it('sends no request to make an image, and makes no version', async () => {
      const context = await open('analyst', [coverage], rows);

      await choose(context, 'svg');
      await wait(300);
      await settle(context.harness);

      expect(context.http.match((r) => r.method !== 'GET')).toHaveLength(0);
    });

    it('says the PNG could not be made, and suggests the SVG, where the browser cannot draw it', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'png');
      await wait(300);
      await settle(context.harness);

      expect(downloader.save).not.toHaveBeenCalled();
      expect(toasts()).toEqual(['The PNG image could not be made. Try the SVG image.']);
      expect(all(context.root, 'widget-menu')[0].getAttribute('aria-busy')).toBeNull();
    });
  });
});

