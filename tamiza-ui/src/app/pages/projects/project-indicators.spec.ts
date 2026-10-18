import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { Downloader } from '../../core/download.service';
import { Widget } from '../../core/projects/indicators.service';
import { DashboardEntry, ProjectRole } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
import { pressKey } from '../../../testing/cdk';
import { byTestId, configureApp, details, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';
import {
  Context,
  all,
  answerLoad,
  answerQueries,
  catalog,
  clearOverlay,
  config,
  dashboard,
  dashboardUrlOf,
  defaultIndicators,
  downloader,
  indicator,
  indicatorsUrl,
  isColumns,
  isDashboard,
  isDashboardOf,
  isOverview,
  isPatch,
  isQuery,
  open,
  overlay,
  overview,
  result,
  row,
  text,
  wait,
  widget,
  widgetUrl,
  widgetsUrl,
} from './indicators/indicators-testing';
import { ProjectContext } from './project-context';
import { layoutSaveDelayMs } from './project-indicators';

const answer = result([row('Cali', 75), row('Bogotá', 60)]);
/** A project whose dashboards are "Indicators" (`d1`, the one `open()` opens) and "Field". */
const indicatorsAndField = [{ id: 'd1', name: 'Indicators' }, { id: 'd2', name: 'Field' }];

/** Opens another dashboard of the project, as its tab would; the page then reads it. */
async function go(context: Pick<Context, 'harness'>, dashboardId: string): Promise<void> {
  await TestBed.inject(Router).navigateByUrl(`/projects/${projectId}/dashboards/${dashboardId}`);
  await settle(context.harness);
}
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
      expect(root.querySelectorAll('nav a')[2].getAttribute('href')).toBe(`/projects/${projectId}/dashboards/d1`);
      expect(root.querySelector('nav a[aria-current="page"]')?.textContent?.trim()).toBe('Indicators');
      expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/dashboards/d1`);
    });
  });

  describe('one dashboard at a time', () => {
    const left = widget('w1', { title: 'Left', layout: { x: 0, y: 0, w: 6, h: 4 } });
    const fieldWidget = widget('w9', { dashboardId: 'd2', title: 'Field progress' });
    const titles = (root: HTMLElement) => all(root, 'widget-title').map(text);
    const moveDown = async (context: Context, id: string) => {
      const grip = context.root.querySelector<HTMLElement>(`[data-widget-id="${id}"] [data-testid="widget-move"]`)!;
      grip.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
      await settle(context.harness);
    };

    it('shows only the widgets of the dashboard opened last', async () => {
      const context = await open('viewer', two, answer, {}, { dashboards: indicatorsAndField });
      expect(text(byTestId(context.root, 'dashboard-heading'))).toBe('Indicators');

      await go(context, 'd2');
      await answerLoad(context, [fieldWidget], {}, 'd2');
      await answerQueries(context, answer);

      expect(titles(context.root)).toEqual(['Field progress']);
      expect(text(byTestId(context.root, 'dashboard-heading'))).toBe('Field');
    });

    it('drops the answer for a dashboard left before it came', async () => {
      const context = await openProjectSection('dashboards/d1', 'viewer', { dashboards: indicatorsAndField });
      const staleOverview = context.http.expectOne(isOverview);
      const stale = context.http.expectOne(isDashboard);

      await go(context, 'd2');
      await answerLoad(context, [fieldWidget], {}, 'd2');
      // The answers for Indicators arrive last: had they been waited for, they would draw its widgets over Field's.
      if (!staleOverview.cancelled) {
        staleOverview.flush(overview());
      }
      if (!stale.cancelled) {
        stale.flush(dashboard(two));
      }
      await settle(context.harness);
      await answerQueries(context, answer);

      expect(titles(context.root)).toEqual(['Field progress']);
      expect(text(byTestId(context.root, 'dashboard-heading'))).toBe('Field');
    });

    it('saves a move made just before switching to the dashboard it was made on', async () => {
      const context = await open('analyst', [left], answer, {}, { dashboards: indicatorsAndField });
      await moveDown(context, 'w1');
      expect(context.http.match(isPatch)).toHaveLength(0); // still waiting for the pause after the last key

      await go(context, 'd2');

      const patches = context.http.match(isPatch);
      expect(patches).toHaveLength(1);
      expect(patches[0].request.url).toBe(widgetUrl('w1'));
      expect(patches[0].request.body).toEqual({ layout: { x: 0, y: 1, w: 6, h: 4 } });
      patches[0].flush(widget('w1', { layout: { x: 0, y: 1, w: 6, h: 4 } }));
      await answerLoad(context, [fieldWidget], {}, 'd2');
      await wait(layoutSaveDelayMs + 100);
      expect(context.http.match(isPatch)).toHaveLength(0); // sent once
    }, 10000);

    it('reports a failed save of the dashboard left and keeps the open one\'s widgets', async () => {
      const context = await open('analyst', [left], answer, {}, { dashboards: indicatorsAndField });
      await moveDown(context, 'w1');
      await go(context, 'd2');
      const [patch] = context.http.match(isPatch);
      await answerLoad(context, [fieldWidget], {}, 'd2');
      await answerQueries(context, answer);

      patch.flush({ title: 'The widget cannot be saved.' }, { status: 403, statusText: 'Forbidden' });
      await settle(context.harness);

      expect(context.http.match((r) => r.method === 'GET' && (isOverview(r) || r.url.startsWith(`/api/v1/projects/${projectId}/dashboards/`)))).toHaveLength(0);
      expect(titles(context.root)).toEqual(['Field progress']);
      expect(TestBed.inject(ToastService).toasts().map((t) => t.message)).toContain('The widget cannot be saved.');
    });

    it('only reads the project again when the dashboard left was removed meanwhile', async () => {
      const context = await open('analyst', [left], answer, {}, { dashboards: indicatorsAndField });
      await moveDown(context, 'w1');
      await go(context, 'd2');
      const [patch] = context.http.match(isPatch);
      await answerLoad(context, [fieldWidget], {}, 'd2');
      await answerQueries(context, answer);

      patch.flush({ title: 'x', code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' });
      await settle(context.harness);

      context.http.expectOne(`/api/v1/projects/${projectId}`).flush(details('analyst', { dashboards: [{ id: 'd2', name: 'Field' }] }));
      await settle(context.harness);
      expect(context.http.match(isOverview)).toHaveLength(0);
      expect(titles(context.root)).toEqual(['Field progress']);
      expect(TestBed.inject(ToastService).toasts()).toHaveLength(0);
      expect(Array.from(context.root.querySelectorAll('nav a')).map((a) => text(a))).not.toContain('Indicators');
    });

    it('shows neither "No widgets yet" nor Add widget while a dashboard loads', async () => {
      const context = await openProjectSection('dashboards/d1', 'analyst', { dashboards: indicatorsAndField });
      expect(byTestId(context.root, 'no-widgets')).toBeNull();
      expect(byTestId(context.root, 'widget-add')).toBeNull();
      expect(byTestId(context.root, 'indicators-loading')?.getAttribute('aria-busy')).toBe('true');
      expect(text(byTestId(context.root, 'dashboard-heading'))).toBe('Indicators'); // the header does not wait
      await answerLoad(context, []);
      expect(byTestId(context.root, 'no-widgets')).not.toBeNull();

      await go(context, 'd2');

      expect(byTestId(context.root, 'no-widgets')).toBeNull();
      expect(byTestId(context.root, 'widget-add')).toBeNull();
      expect(byTestId(context.root, 'indicators-loading')?.getAttribute('aria-busy')).toBe('true');
      await answerLoad(context, [], {}, 'd2');
      expect(byTestId(context.root, 'indicators-loading')).toBeNull();
      expect(byTestId(context.root, 'no-widgets')).not.toBeNull();
    });

    it('does not read the open dashboard again when the list of dashboards changes', async () => {
      const context = await open('analyst', two, answer, {}, { dashboards: indicatorsAndField });
      const project = context.harness.fixture.debugElement.query(By.css('app-project-indicators')).injector.get(ProjectContext);

      project.setDashboards([{ id: 'd1', name: 'Coverage' }, { id: 'd2', name: 'Field' }]);
      await settle(context.harness);
      expect(text(byTestId(context.root, 'dashboard-heading'))).toBe('Coverage'); // the heading follows the list
      project.reload().subscribe();
      context.http.expectOne(`/api/v1/projects/${projectId}`).flush(details('analyst', { dashboards: [{ id: 'd1', name: 'Coverage' }] }));
      await settle(context.harness);
      project.refresh();
      context.http.expectOne(`/api/v1/projects/${projectId}`).flush(details('analyst', { dashboards: [{ id: 'd1', name: 'Coverage' }] }));
      await settle(context.harness);

      expect(context.http.match(isDashboard)).toHaveLength(0);
      expect(context.http.match(isOverview)).toHaveLength(0);
      expect(context.http.match(isQuery)).toHaveLength(0);
      expect(titles(context.root)).toEqual(['Coverage by municipality', 'Response rate']);
    });

    it('keeps focus on the tab a dashboard is opened from (Dashboard opened from its tab)', async () => {
      const context = await open('viewer', two, answer, {}, { dashboards: indicatorsAndField });
      const tab = byTestId(context.root, 'dashboard-tab-d2')!;

      tab.focus();
      tab.click();
      await settle(context.harness);
      await answerLoad(context, [fieldWidget], {}, 'd2');
      await answerQueries(context, answer);

      expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/dashboards/d2`);
      expect(titles(context.root)).toEqual(['Field progress']);
      expect(document.activeElement).toBe(byTestId(context.root, 'dashboard-tab-d2'));
    });

    it('shows the name the project now lists under a bookmarked address (Bookmark after a rename)', async () => {
      const { root } = await open('viewer', [], null, {}, { dashboardId: 'd2', dashboards: [{ id: 'd2', name: 'Salud' }, { id: 'd1', name: 'Indicators' }] });

      const heading = byTestId(root, 'dashboard-heading')!;
      expect(heading.tagName).toBe('H2');
      expect(text(heading)).toBe('Salud');
      expect(byTestId(root, 'dashboard-notice')).toBeNull();
      expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/dashboards/d2`);
    });
  });

  describe('a dashboard the project does not have', () => {
    const healthAndField = [{ id: 'd4', name: 'Health' }, { id: 'd5', name: 'Field' }];
    const missing = 'The dashboard you opened does not exist. Showing Health instead.';
    const status = (root: HTMLElement) => text(byTestId(root, 'dashboard-status')).replace(/​/g, '');

    /** Opens the address of a removed dashboard step by step, so the live region can be seen before the notice is written into it. */
    async function openRemoved() {
      const http = configureApp([{ provide: Downloader, useValue: downloader }]);
      const harness = await RouterTestingHarness.create();
      const navigation = harness.navigateByUrl(`/projects/${projectId}/dashboards/removed`);
      await settle(harness);
      http.expectOne(`/api/v1/projects/${projectId}`).flush(details('viewer', { dashboards: healthAndField }));
      await navigation;
      harness.fixture.detectChanges();
      const root = harness.fixture.nativeElement as HTMLElement;
      // The page's first render: the region is there, and empty.
      const region = byTestId(root, 'dashboard-status');
      const emptyAtFirst = region !== null && text(region) === '';
      await settle(harness);
      const context = { http, harness, root };
      await answerLoad(context, [widget('w1', { dashboardId: 'd4', title: 'Clinics' })], {}, 'd4');
      await answerQueries(context, answer);
      return { ...context, region, emptyAtFirst };
    }

    it('opens the first dashboard in its place, with a notice naming it (Bookmark to a removed dashboard)', async () => {
      const { http, root, region, emptyAtFirst } = await openRemoved();
      const router = TestBed.inject(Router);

      expect(router.url).toBe(`/projects/${projectId}/dashboards/d4`);
      expect(router.lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
      expect(root.querySelector('nav a[aria-current="page"]')).toBe(byTestId(root, 'dashboard-tab-d4'));
      expect(text(byTestId(root, 'dashboard-heading'))).toBe('Health');
      expect(all(root, 'widget-title').map(text)).toEqual(['Clinics']);
      expect(text(byTestId(root, 'dashboard-notice-text'))).toBe(missing);
      expect(byTestId(root, 'dashboard-notice')!.getAttribute('role')).toBeNull(); // the header's region announces it, once
      expect(http.match(isDashboardOf('removed'))).toHaveLength(0);
      // Announced through a region that was there, empty, before the text was written into it, without moving focus.
      expect(emptyAtFirst).toBe(true);
      expect(byTestId(root, 'dashboard-status')).toBe(region);
      expect(region!.getAttribute('role')).toBe('status');
      expect(status(root)).toBe(missing);
      expect(document.activeElement).not.toBe(byTestId(root, 'dashboard-heading'));
    });

    it('drops the notice once another dashboard opens (Notice cleared)', async () => {
      const context = await openRemoved();

      await go(context, 'd5');
      await answerLoad(context, [], {}, 'd5');
      expect(byTestId(context.root, 'dashboard-notice')).toBeNull();
      expect(status(context.root)).toBe('');

      await go(context, 'd4');
      await answerLoad(context, [], {}, 'd4');
      expect(text(byTestId(context.root, 'dashboard-heading'))).toBe('Health');
      expect(byTestId(context.root, 'dashboard-notice')).toBeNull();
      expect(status(context.root)).toBe('');
    });

    it('puts focus on the heading when the notice is dismissed (Notice dismissed)', async () => {
      const { root, harness } = await openRemoved();

      byTestId(root, 'dashboard-notice-dismiss')!.click();
      await settle(harness);

      expect(byTestId(root, 'dashboard-notice')).toBeNull();
      expect(document.activeElement).toBe(byTestId(root, 'dashboard-heading'));
      expect(text(document.activeElement)).toBe('Health');
    });

    it('drops the dashboard still being read when the next address names none of the project\'s', async () => {
      const context = await openProjectSection('dashboards/d5', 'viewer', { dashboards: healthAndField }, [{ provide: Downloader, useValue: downloader }]);
      const staleOverview = context.http.expectOne(isOverview);
      const stale = context.http.expectOne(isDashboardOf('d5'));
      // The redirect is held back, so only the address it would replace is open when Field's answers come.
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      await go(context, 'removed');
      expect(navigate).toHaveBeenCalledWith(['/projects', projectId, 'dashboards', 'd4'], { replaceUrl: true });
      if (!staleOverview.cancelled) {
        staleOverview.flush(overview());
      }
      if (!stale.cancelled) {
        stale.flush(dashboard([widget('w9', { dashboardId: 'd5', title: 'Field progress' })], { id: 'd5' }));
      }
      await settle(context.harness);

      expect(all(context.root, 'widget-title')).toHaveLength(0);
      expect(byTestId(context.root, 'indicators-loading')?.getAttribute('aria-busy')).toBe('true');
      expect(context.http.match(isDashboardOf('removed'))).toHaveLength(0);
    });
  });

  describe('what every dashboard shows', () => {
    const fieldWidget = widget('w9', { dashboardId: 'd2', title: 'Field progress' });

    it('shows a viewer the widgets of the dashboard opened (Viewer opens a published dashboard)', async () => {
      const { root } = await open('viewer', [fieldWidget], answer, {}, { dashboardId: 'd2', dashboards: indicatorsAndField });

      expect(all(root, 'widget-title').map(text)).toEqual(['Field progress']);
      expect(all(root, 'chart-svg')).toHaveLength(1);
      expect(byTestId(root, 'indicators-empty')).toBeNull();
    });

    it('lists the project\'s indicators after the widgets, behind a closed disclosure (Indicator list behind a disclosure)', async () => {
      const indicators = [indicator('coverage'), indicator('households_visited'), indicator('target', { published: false, publishedAt: null, runId: null, rows: null })];
      const { root, harness } = await open('viewer', [fieldWidget], answer, { indicators }, { dashboardId: 'd2', dashboards: indicatorsAndField });

      const card = byTestId(root, 'indicator-table-card')!;
      expect(byTestId(root, 'widget-grid')!.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      const details = card.querySelector('details') as HTMLDetailsElement;
      expect(details.open).toBe(false);
      expect(text(card.querySelector('summary'))).toContain('Indicators (3)');
      expect(text(card.querySelector('summary'))).toContain('· 2 published');

      card.querySelector('summary')!.click();
      await settle(harness);

      expect(details.open).toBe(true);
      const rows = indicators.map((i) => byTestId(root, `indicator-row-${i.name}`)!);
      expect(rows.map((r) => text(byTestId(r, 'indicator-label')))).toEqual(['Coverage', 'Households visited', 'Target']);
      expect(rows.map((r) => text(byTestId(r, 'indicator-format')))).toEqual(['Count', 'Count · households', 'Count']);
      expect(rows.map((r) => byTestId(r, 'indicator-published') !== null)).toEqual([true, true, false]);
      expect(text(byTestId(rows[2], 'indicator-unpublished'))).toBe('Not published yet');
    });

    it('says on each dashboard that nothing has published, and lists the widgets of each (No indicator has published)', async () => {
      const context = await open('analyst', two, null, unpublished, { dashboards: indicatorsAndField });
      const check = (titles: string[]) => {
        const empty = byTestId(context.root, 'indicators-empty')!;
        expect(text(empty)).toContain('No indicators published yet');
        expect(text(empty)).toContain('Add an indicator on the scripts page');
        expect(empty.contains(byTestId(context.root, 'indicator-table'))).toBe(true);
        expect(context.root.querySelector('details')).toBeNull(); // expanded, as part of the message
        expect(all(context.root, 'widget-title').map(text)).toEqual(titles);
      };
      check(['Coverage by municipality', 'Response rate']);

      await go(context, 'd2');
      await answerLoad(context, [fieldWidget], unpublished, 'd2');

      check(['Field progress']);
      expect(context.http.match(isQuery)).toHaveLength(0);
    });

    it('lists the same indicators, each with its export, on every dashboard (Same indicators on every dashboard)', async () => {
      const indicators = [indicator('coverage'), indicator('households_visited')];
      const context = await open('viewer', [], null, { indicators }, { dashboards: [{ id: 'd1', name: 'Health' }, { id: 'd2', name: 'Field' }] });
      const listed = () => all(context.root, 'indicator-label').map(text);
      const exports = () => Array.from(context.root.querySelectorAll('[data-testid^="indicator-export-"]')).map((b) => b.getAttribute('data-testid'));
      expect(listed()).toEqual(['Coverage', 'Households visited']);
      expect(exports()).toEqual(['indicator-export-coverage', 'indicator-export-households_visited']);

      await go(context, 'd2');
      await answerLoad(context, [], { indicators }, 'd2');

      expect(text(byTestId(context.root, 'dashboard-heading'))).toBe('Field');
      expect(listed()).toEqual(['Coverage', 'Households visited']);
      expect(exports()).toEqual(['indicator-export-coverage', 'indicator-export-households_visited']);
    });

    it('links to the project\'s scripts from any dashboard (Link to the scripts from any dashboard)', async () => {
      const { root } = await open('analyst', [], null, unpublished, { dashboardId: 'd2', dashboards: indicatorsAndField });

      expect(byTestId(root, 'indicators-empty-link')!.getAttribute('href')).toBe(`/projects/${projectId}/scripts`);
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
        expect(text(byTestId(root, 'indicators-count'))).toBe('2 of 50 widgets on this dashboard');
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
      const { http, harness, root } = await openProjectSection('dashboards/d1', 'viewer');
      http.expectOne(isOverview).flush({}, { status: 500, statusText: 'Server Error' });
      await settle(harness);

      expect(text(byTestId(root, 'indicators-error'))).toBe('Something went wrong. Try again.');
    });

    it('shows the same error when only the dashboard\'s widgets cannot be read', async () => {
      const { http, harness, root } = await openProjectSection('dashboards/d1', 'viewer');
      http.expectOne(isOverview).flush(overview());
      http.expectOne(isDashboard).flush({}, { status: 500, statusText: 'Server Error' });
      await settle(harness);

      expect(text(byTestId(root, 'indicators-error'))).toBe('Something went wrong. Try again.');
      expect(byTestId(root, 'no-widgets')).toBeNull(); // no dashboard to call empty
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
      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === widgetsUrl);
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

      const request = context.http.expectOne((r) => r.method === 'PATCH' && r.url === widgetUrl('w1'));
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
      expect(request.request.url).toBe(widgetUrl('w2'));
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
      const { root } = await open('analyst', two, answer, { limits: { maxWidgets: 2, maxQueryRows: 5000, maxDashboards: 7 } });

      expect(byTestId<HTMLButtonElement>(root, 'widget-add')!.disabled).toBe(true);
      expect(text(byTestId(root, 'widget-limit'))).toContain('(2)');
    });
  });

  describe('moving a widget to another dashboard (design D9)', () => {
    const health: DashboardEntry = { id: 'd1', name: 'Health' };
    const field: DashboardEntry = { id: 'd2', name: 'Field' };
    const housing: DashboardEntry = { id: 'd3', name: 'Housing' };
    const projectUrl = `/api/v1/projects/${projectId}`;
    const full = 'A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first.';
    const refusal = (message: string) => [{ title: 'x', errors: { dashboardId: [message] } }, { status: 400, statusText: 'Bad Request' }] as const;
    const titles = (root: HTMLElement) => all(root, 'widget-title').map(text);
    const tabs = (root: HTMLElement) => Array.from(root.querySelectorAll('nav [data-testid^="dashboard-tab-"]')).map(text);
    const heading = (root: HTMLElement) => byTestId(root, 'dashboard-heading')!;
    const toasts = () => TestBed.inject(ToastService).toasts().map((t) => t.message);
    const form = () => byTestId(overlay(), 'widget-dialog');
    const select = () => byTestId<HTMLSelectElement>(overlay(), 'dialog-dashboard')!;
    const left = widget('w1', { title: 'Left', layout: { x: 0, y: 0, w: 6, h: 4 } });

    async function moveDown(context: Context, id: string): Promise<void> {
      const grip = context.root.querySelector<HTMLElement>(`[data-widget-id="${id}"] [data-testid="widget-move"]`)!;
      grip.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
      await settle(context.harness);
    }

    /** Chooses Edit in a widget's actions menu; the form opens at once unless a layout of the widget is still to be saved. */
    async function chooseEdit(context: Context, index = 0): Promise<void> {
      all(context.root, 'menu-trigger')[index].click();
      await settle(context.harness);
      document.querySelector<HTMLElement>('[data-testid="widget-edit"]')!.click();
      await settle(context.harness);
    }

    /** Answers what the open form reads: the columns of the widget's indicator. */
    async function answerForm(context: Context): Promise<void> {
      context.http.expectOne(isColumns).flush(catalog());
      await settle(context.harness);
    }

    /** Chooses another dashboard in the form and saves; returns the request sent. */
    async function moveTo(context: Context, dashboardId: string) {
      typeInto(select(), dashboardId);
      await settle(context.harness);
      byTestId(overlay(), 'dialog-save')!.click();
      await settle(context.harness);
      return context.http.expectOne(isPatch);
    }

    it('takes the widget off this dashboard, says where it went and puts focus on the heading (Widget moved to another dashboard)', async () => {
      const context = await open('analyst', two, answer, {}, { dashboards: [health, field] });
      await chooseEdit(context);
      await answerForm(context);

      const request = await moveTo(context, 'd2');
      expect(request.request.url).toBe(widgetUrl('w1'));
      expect(request.request.body).toEqual({ dashboardId: 'd2' });
      const moved = widget('w1', { title: 'Coverage by municipality', dashboardId: 'd2', layout: { x: 0, y: 4, w: 6, h: 4 } });
      request.flush(moved);
      await settle(context.harness);

      expect(form()).toBeNull();
      expect(titles(context.root)).toEqual(['Response rate']);
      expect(toasts()).toEqual(['Widget Coverage by municipality moved to Field.']);
      expect(document.activeElement).toBe(heading(context.root));
      expect(text(heading(context.root))).toBe('Health');

      // Field shows it below its own widgets, where the API placed it.
      await go(context, 'd2');
      await answerLoad(context, [widget('w9', { dashboardId: 'd2', title: 'Field progress', layout: { x: 0, y: 0, w: 12, h: 4 } }), moved], {}, 'd2');
      await answerQueries(context, answer);
      expect(titles(context.root)).toEqual(['Field progress', 'Coverage by municipality']);
    });

    it('keeps a widget saved on its own dashboard in place, with no dashboard in the patch', async () => {
      const context = await open('analyst', two, answer, {}, { dashboards: [health, field] });
      await chooseEdit(context);
      await answerForm(context);

      typeInto(byTestId<HTMLInputElement>(overlay(), 'dialog-title')!, 'Coverage');
      await settle(context.harness);
      byTestId(overlay(), 'dialog-save')!.click();
      await settle(context.harness);
      const request = context.http.expectOne(isPatch);
      expect(request.request.body).toEqual({ title: 'Coverage', chartType: 'column', config: config() });
      request.flush(widget('w1', { title: 'Coverage' }));
      await settle(context.harness);

      expect(titles(context.root)).toEqual(['Coverage', 'Response rate']);
      expect(toasts()).toEqual(['Widget Coverage saved.']);
    });

    it('moves a widget whose group column is gone, which still names it on its new dashboard (Stale widget moved)', async () => {
      const stale = widget('w1', { title: 'Coverage by district', config: config({ groupBy: 'district' }) });
      const missing = result([], { missing: { indicator: false, columns: ['district'] } });
      const context = await open('analyst', [stale], missing, {}, { dashboards: [health, field] });
      expect(text(byTestId(context.root, 'missing-columns'))).toBe('The published table no longer has the columns: district.');
      await chooseEdit(context);
      await answerForm(context);

      const request = await moveTo(context, 'd2');
      expect(request.request.body).toEqual({ dashboardId: 'd2' });
      const moved: Widget = { ...stale, dashboardId: 'd2' };
      request.flush(moved);
      await settle(context.harness);
      expect(titles(context.root)).toEqual([]);

      await go(context, 'd2');
      await answerLoad(context, [moved], {}, 'd2');
      await answerQueries(context, missing);
      expect(titles(context.root)).toEqual(['Coverage by district']);
      expect(text(byTestId(context.root, 'missing-columns'))).toBe('The published table no longer has the columns: district.');
    });

    it('shows a full dashboard\'s refusal next to the select and keeps the widget (Full dashboard)', async () => {
      const context = await open('analyst', two, answer, {}, { dashboards: [health, field] });
      await chooseEdit(context);
      await answerForm(context);

      (await moveTo(context, 'd2')).flush(...refusal(full));
      await settle(context.harness);
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [health, field] }));
      await settle(context.harness);

      expect(form()).not.toBeNull();
      expect(text(overlay().querySelector('#widgetDashboard-error'))).toBe(full);
      expect(titles(context.root)).toEqual(['Coverage by municipality', 'Response rate']);
      expect(toasts()).toEqual([]);
    });

    it('drops a dashboard removed meanwhile from the form and the tab row, and keeps the widget (Target removed meanwhile)', async () => {
      const context = await open('analyst', two, answer, {}, { dashboards: [health, housing, field] });
      await chooseEdit(context);
      await answerForm(context);

      (await moveTo(context, 'd2')).flush(...refusal('The dashboard does not exist.'));
      await settle(context.harness);
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [health, housing] }));
      await settle(context.harness);

      expect(form()).not.toBeNull();
      expect(text(overlay().querySelector('#widgetDashboard-error'))).toBe('The dashboard does not exist.');
      expect(Array.from(select().options).map((o) => text(o))).toEqual(['Health', 'Housing']);
      expect(tabs(context.root)).toEqual(['Health', 'Housing']);
      expect(titles(context.root)).toEqual(['Coverage by municipality', 'Response rate']);
      expect(toasts()).toEqual([]);
      expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/dashboards/d1`);
    });

    it('opens the form once the widget\'s layout waiting for the pause is saved', async () => {
      const context = await open('analyst', [left], answer, {}, { dashboards: [health, field] });
      await moveDown(context, 'w1');
      expect(context.http.match(isPatch)).toHaveLength(0);

      await chooseEdit(context);
      const [layout] = context.http.match(isPatch);
      expect(layout.request.body).toEqual({ layout: { x: 0, y: 1, w: 6, h: 4 } });
      expect(form()).toBeNull();
      context.http.expectNone(isColumns);

      layout.flush(widget('w1', { title: 'Left', layout: { x: 0, y: 1, w: 6, h: 4 } }));
      await settle(context.harness);
      expect(form()).not.toBeNull();
      await answerForm(context);
      const move = await moveTo(context, 'd2');
      expect(move.request.body).toEqual({ dashboardId: 'd2' });
      move.flush(widget('w1', { title: 'Left', dashboardId: 'd2', layout: { x: 0, y: 0, w: 6, h: 4 } }));
      await settle(context.harness);

      await wait(layoutSaveDelayMs + 100);
      expect(context.http.match(isPatch)).toHaveLength(0); // no layout follows the widget to Field
    }, 10000);

    it('opens the form once the widget\'s layout save on its way is answered', async () => {
      const context = await open('analyst', [left], answer, {}, { dashboards: [health, field] });
      await moveDown(context, 'w1');
      await wait(layoutSaveDelayMs + 50);
      const [layout] = context.http.match(isPatch);

      await chooseEdit(context);
      expect(form()).toBeNull();
      context.http.expectNone(isColumns);

      layout.flush(widget('w1', { title: 'Left', layout: { x: 0, y: 1, w: 6, h: 4 } }));
      await settle(context.harness);
      expect(form()).not.toBeNull();
      await answerForm(context);
      expect(context.http.match(isPatch)).toHaveLength(0); // sent once
    }, 10000);

    it('opens one form when Edit is chosen again while the layout save is on its way', async () => {
      const context = await open('analyst', [left], answer, {}, { dashboards: [health, field] });
      await moveDown(context, 'w1');
      await wait(layoutSaveDelayMs + 50);
      const [layout] = context.http.match(isPatch);

      await chooseEdit(context);
      await chooseEdit(context);
      layout.flush(widget('w1', { title: 'Left', layout: { x: 0, y: 1, w: 6, h: 4 } }));
      await settle(context.harness);

      expect(overlay().querySelectorAll('[data-testid="widget-dialog"]')).toHaveLength(1);
      await answerForm(context); // the columns are read once
    }, 10000);

    it('adds a widget to the dashboard open, offering no dashboard to choose (New widget)', async () => {
      const context = await open('analyst', [], null, {}, { dashboardId: 'd2', dashboards: [health, field] });
      byTestId(context.root, 'widget-add')!.click();
      await settle(context.harness);
      await answerForm(context);
      expect(byTestId(overlay(), 'dialog-dashboard')).toBeNull();

      typeInto(byTestId<HTMLInputElement>(overlay(), 'dialog-title')!, 'Clinics');
      await settle(context.harness);
      byTestId(overlay(), 'dialog-save')!.click();
      await settle(context.harness);
      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === `${dashboardUrlOf('d2')}/widgets`);
      expect(request.request.body.dashboardId).toBeUndefined();
      request.flush(widget('w5', { dashboardId: 'd2', title: 'Clinics' }), { status: 201, statusText: 'Created' });
      await settle(context.harness);

      expect(titles(context.root)).toEqual(['Clinics']);
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
      const { root } = await open('analyst', two, answer, { limits: { maxWidgets: 2, maxQueryRows: 5000, maxDashboards: 7 } });
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

    it('returns focus to the widget\'s menu button when its Edit form is closed', async () => {
      const context = await open('analyst', two, answer);
      const trigger = all(context.root, 'menu-trigger')[1];

      trigger.focus();
      pressKey(trigger, 'ArrowDown');
      await settle(context.harness);
      const edit = document.querySelector<HTMLElement>('[data-testid="widget-edit"]')!;
      expect(document.activeElement).toBe(edit);
      pressKey(edit, 'Enter');
      await settle(context.harness);
      context.http.expectOne(isColumns).flush(catalog());
      context.http.match(isQuery).forEach((r) => r.flush(answer)); // the preview of the saved query
      await settle(context.harness);
      expect(byTestId(overlay(), 'widget-dialog')).not.toBeNull();

      byTestId(overlay(), 'dialog-cancel')!.click();
      await settle(context.harness);

      expect(byTestId(overlay(), 'widget-dialog')).toBeNull();
      expect(document.activeElement).toBe(trigger); // not the menu item, which the menu's overlay removed
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

  describe('managing dashboards', () => {
    const housing: DashboardEntry = { id: 'd1', name: 'Housing' };
    const health: DashboardEntry = { id: 'd2', name: 'Health' };
    const field: DashboardEntry = { id: 'd3', name: 'Field' };
    const education: DashboardEntry = { id: 'd4', name: 'Education' };
    const seven = Array.from({ length: 7 }, (_, i): DashboardEntry => ({ id: `d${i + 1}`, name: `Dashboard ${i + 1}` }));
    const dashboardsUrl = `/api/v1/projects/${projectId}/dashboards`;
    const projectUrl = `/api/v1/projects/${projectId}`;
    const orderUrl = `${dashboardsUrl}/order`;
    const conflict = (code: string) => [{ title: 'x', code }, { status: 409, statusText: 'Conflict' }] as const;
    const removedMeanwhile = [{ title: 'x', code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' }] as const;

    const tabs = (root: HTMLElement) => Array.from(root.querySelectorAll('nav [data-testid^="dashboard-tab-"]')).map(text);
    const heading = (root: HTMLElement) => byTestId(root, 'dashboard-heading')!;
    const status = (root: HTMLElement) => text(byTestId(root, 'dashboard-status')).replace(/​/g, '');
    const toasts = () => TestBed.inject(ToastService).toasts().map((t) => t.message);
    const item = (testId: string) => document.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
    const nameDialog = () => document.querySelector<HTMLElement>('[data-testid="dashboard-name-dialog"]');
    const nameInput = () => nameDialog()!.querySelector<HTMLInputElement>('[data-testid="dashboard-name"]')!;
    const url = () => TestBed.inject(Router).url;

    /** Opens the dashboard's actions with a click and chooses one. */
    async function choose(context: Pick<Context, 'root' | 'harness'>, testId: string): Promise<void> {
      byTestId(context.root, 'dashboard-menu')!.click();
      await settle(context.harness);
      item(testId)!.click();
      await settle(context.harness);
    }

    /** Opens the dashboard's actions from the keyboard and leaves them open, focus on the first item. */
    async function openMenuByKeyboard(context: Pick<Context, 'root' | 'harness'>): Promise<HTMLElement> {
      const trigger = byTestId(context.root, 'dashboard-menu')!;
      trigger.focus();
      pressKey(trigger, 'ArrowDown');
      await settle(context.harness);
      return trigger;
    }

    async function nameAndSave(context: Pick<Context, 'harness'>, name: string | null): Promise<void> {
      if (name !== null) {
        typeInto(nameInput(), name);
      }
      byTestId(nameDialog()!, 'dashboard-name-save')!.click();
      await settle(context.harness);
    }

    it('adds a dashboard, placed last, and opens it with no widgets (Analyst adds a dashboard)', async () => {
      const context = await open('analyst', two, answer, {}, { dashboards: [{ id: 'd1', name: 'Indicators' }] });

      byTestId(context.root, 'dashboard-add')!.click();
      await settle(context.harness);
      expect(nameDialog()!.querySelector('h2')!.textContent!.trim()).toBe('New dashboard');
      await nameAndSave(context, 'Field progress');
      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === dashboardsUrl);
      expect(request.request.body).toEqual({ name: 'Field progress' });
      request.flush({ id: 'd9', name: 'Field progress' }, { status: 201, statusText: 'Created' });
      await settle(context.harness);
      await answerLoad(context, [], {}, 'd9');

      expect(url()).toBe(`/projects/${projectId}/dashboards/d9`);
      expect(tabs(context.root)).toEqual(['Indicators', 'Field progress']);
      expect(text(heading(context.root))).toBe('Field progress');
      expect(all(context.root, 'widget-title')).toHaveLength(0);
      expect(byTestId(context.root, 'no-widgets')).not.toBeNull();
      expect(byTestId(context.root, 'dashboard-notice')).toBeNull();
      context.http.expectNone(projectUrl); // the list the API answered with is enough
    });

    it('keeps "New dashboard" reachable at the limit, saying why it is unavailable, and turns Duplicate off (Add action at the limit)', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: seven });
      const add = byTestId<HTMLButtonElement>(context.root, 'dashboard-add')!;

      // Still in the tab order: not disabled, so Tab reaches it and its description can be heard.
      expect(add.disabled).toBe(false);
      expect(add.tabIndex).toBe(0);
      add.focus();
      expect(document.activeElement).toBe(add);
      expect(add.getAttribute('aria-disabled')).toBe('true');
      const hint = document.getElementById(add.getAttribute('aria-describedby')!);
      expect(text(hint)).toBe('The project has the most dashboards it can hold (7).');
      expect(hint).toBe(byTestId(context.root, 'dashboard-limit-hint')); // shown, under the header

      add.click();
      await settle(context.harness);
      expect(nameDialog()).toBeNull();

      byTestId(context.root, 'dashboard-menu')!.click();
      await settle(context.harness);
      const duplicate = item('dashboard-duplicate')!;
      expect(duplicate.getAttribute('aria-disabled')).toBe('true');
      expect(text(document.getElementById(duplicate.getAttribute('aria-describedby')!))).toBe('A project can have at most 7 dashboards.');
      duplicate.click();
      await settle(context.harness);
      expect(nameDialog()).toBeNull();
    });

    it('renames a dashboard, keeping its widgets and its place, and says so (Analyst renames a dashboard)', async () => {
      const context = await open('analyst', two, answer, {}, { dashboards: [{ id: 'd1', name: 'Indicators' }, field] });

      await choose(context, 'dashboard-rename');
      expect(nameInput().value).toBe('Indicators');
      await nameAndSave(context, 'Coverage');
      const request = context.http.expectOne((r) => r.method === 'PATCH' && r.url === dashboardUrlOf('d1'));
      expect(request.request.body).toEqual({ name: 'Coverage' });
      request.flush({ id: 'd1', name: 'Coverage' });
      await settle(context.harness);

      expect(toasts()).toEqual(['Renamed to "Coverage".']);
      expect(tabs(context.root)).toEqual(['Coverage', 'Field']);
      expect(text(heading(context.root))).toBe('Coverage');
      expect(all(context.root, 'widget-title').map(text)).toEqual(['Coverage by municipality', 'Response rate']);
      expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
      expect(context.http.match(isDashboard)).toHaveLength(0); // nothing read again
    });

    it('moves a dashboard right by sending the order shown and the order wanted (Move right)', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field] });

      await choose(context, 'dashboard-move-right');
      const request = context.http.expectOne((r) => r.method === 'PUT' && r.url === orderUrl);
      expect(request.request.body).toEqual({ expectedIds: ['d1', 'd2', 'd3'], dashboardIds: ['d2', 'd1', 'd3'] });
      request.flush([health, housing, field]);
      await settle(context.harness);

      expect(tabs(context.root)).toEqual(['Health', 'Housing', 'Field']);
      expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
      expect(text(heading(context.root))).toBe('Housing');
      expect(toasts()).toEqual([]);
    });

    it('ends third of 4 after two moves right from the first place (Two moves in a row)', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field, education] });

      await choose(context, 'dashboard-move-right');
      context.http.expectOne(orderUrl).flush([health, housing, field, education]);
      await settle(context.harness);
      await choose(context, 'dashboard-move-right');
      const second = context.http.expectOne(orderUrl);
      expect(second.request.body).toEqual({ expectedIds: ['d2', 'd1', 'd3', 'd4'], dashboardIds: ['d2', 'd3', 'd1', 'd4'] });
      second.flush([health, field, housing, education]);
      await settle(context.harness);

      expect(tabs(context.root)).toEqual(['Health', 'Field', 'Housing', 'Education']);
      expect(status(context.root)).toBe('Housing moved to position 3 of 4');
    });

    it('turns the moves off while a move is being saved', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field] });
      await choose(context, 'dashboard-move-right');
      const request = context.http.expectOne(orderUrl);

      byTestId(context.root, 'dashboard-menu')!.click();
      await settle(context.harness);
      for (const id of ['dashboard-move-left', 'dashboard-move-right']) {
        expect(item(id)!.getAttribute('aria-disabled'), id).toBe('true');
        expect(text(document.getElementById(item(id)!.getAttribute('aria-describedby')!)), id).toBe('The new order is being saved.');
      }
      item('dashboard-move-right')!.click();
      await settle(context.harness);
      context.http.expectNone(orderUrl); // one move at a time
      pressKey(item('dashboard-move-right')!, 'Escape'); // the menu stayed open
      await settle(context.harness);

      request.flush([health, housing, field]);
      await settle(context.harness);
      byTestId(context.root, 'dashboard-menu')!.click();
      await settle(context.harness);
      expect(item('dashboard-move-left')!.getAttribute('aria-disabled')).toBeNull();
      expect(item('dashboard-move-right')!.getAttribute('aria-disabled')).toBeNull();
    });

    it('offers no move left on the first dashboard, and a move right when there is another (First dashboard)', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing, health] });

      byTestId(context.root, 'dashboard-menu')!.click();
      await settle(context.harness);

      const left = item('dashboard-move-left')!;
      expect(left.getAttribute('aria-disabled')).toBe('true');
      expect(text(document.getElementById(left.getAttribute('aria-describedby')!))).toBe('It is already the first dashboard.');
      expect(item('dashboard-move-right')!.getAttribute('aria-disabled')).toBeNull();
      expect(item('dashboard-remove')!.getAttribute('aria-disabled')).toBeNull();
    });

    it('offers no move right on the last dashboard', async () => {
      const context = await open('analyst', [], null, {}, { dashboardId: 'd2', dashboards: [housing, health] });

      byTestId(context.root, 'dashboard-menu')!.click();
      await settle(context.harness);

      expect(item('dashboard-move-left')!.getAttribute('aria-disabled')).toBeNull();
      const right = item('dashboard-move-right')!;
      expect(right.getAttribute('aria-disabled')).toBe('true');
      expect(text(document.getElementById(right.getAttribute('aria-describedby')!))).toBe('It is already the last dashboard.');
    });

    it('announces the new position of a dashboard moved right from second of 4 (Position announced)', async () => {
      const context = await open('analyst', [], null, {}, { dashboardId: 'd2', dashboards: [housing, health, field, education] });
      const region = byTestId(context.root, 'dashboard-status')!;
      expect(text(region)).toBe('');

      await choose(context, 'dashboard-move-right');
      context.http.expectOne(orderUrl).flush([housing, field, health, education]);
      await settle(context.harness);

      expect(byTestId(context.root, 'dashboard-status')).toBe(region); // the region that was there, so it is read
      expect(region.getAttribute('role')).toBe('status');
      expect(status(context.root)).toBe('Health moved to position 3 of 4');
    });

    it('duplicates a dashboard right after it and opens the copy with its widgets (Dashboard duplicated)', async () => {
      const eight = Array.from({ length: 8 }, (_, i) => widget(`w${i + 1}`, { title: `Widget ${i + 1}`, layout: { x: (i % 2) * 6, y: Math.floor(i / 2) * 4, w: 6, h: 4 } }));
      const context = await open('analyst', eight, answer, {}, { dashboards: [housing, health] });

      await choose(context, 'dashboard-duplicate');
      expect(nameDialog()!.querySelector('h2')!.textContent!.trim()).toBe('Duplicate Housing');
      expect(nameInput().value).toBe('Housing (copy)');
      await nameAndSave(context, 'Housing 2026');
      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === `${dashboardUrlOf('d1')}/copies`);
      expect(request.request.body).toEqual({ name: 'Housing 2026' });
      request.flush({ id: 'd5', name: 'Housing 2026' }, { status: 201, statusText: 'Created' });
      await settle(context.harness);
      await answerLoad(context, eight.map((w, i) => ({ ...w, id: `c${i + 1}`, dashboardId: 'd5' })), {}, 'd5');
      await answerQueries(context, answer);

      expect(url()).toBe(`/projects/${projectId}/dashboards/d5`);
      expect(tabs(context.root)).toEqual(['Housing', 'Housing 2026', 'Health']);
      expect(text(heading(context.root))).toBe('Housing 2026');
      expect(document.activeElement).toBe(heading(context.root));
      expect(all(context.root, 'widget-title')).toHaveLength(8);
    });

    describe('a layout saved just before a duplicate (Recent move included)', () => {
      const left = widget('w1', { title: 'Left', layout: { x: 0, y: 0, w: 6, h: 4 } });
      const moveDown = async (context: Context) => {
        const grip = context.root.querySelector<HTMLElement>('[data-widget-id="w1"] [data-testid="widget-move"]')!;
        grip.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
        await settle(context.harness);
      };

      it('is sent at once when Duplicate is chosen before the pause, and the copy waits for its answer', async () => {
        const context = await open('analyst', [left], answer, {}, { dashboards: [housing, health] });
        await moveDown(context);
        expect(context.http.match(isPatch)).toHaveLength(0);

        await choose(context, 'dashboard-duplicate');
        const [patch] = context.http.match(isPatch);
        expect(patch.request.body).toEqual({ layout: { x: 0, y: 1, w: 6, h: 4 } });
        expect(nameDialog()).toBeNull();

        patch.flush(widget('w1', { layout: { x: 0, y: 1, w: 6, h: 4 } }));
        await settle(context.harness);
        expect(nameDialog()).not.toBeNull();
        context.http.expectNone(`${dashboardUrlOf('d1')}/copies`);
        await nameAndSave(context, null);
        expect(context.http.expectOne(`${dashboardUrlOf('d1')}/copies`).request.body).toEqual({ name: 'Housing (copy)' });
      });

      it('is waited for when it is already on its way', async () => {
        const context = await open('analyst', [left], answer, {}, { dashboards: [housing, health] });
        await moveDown(context);
        await wait(layoutSaveDelayMs + 50);
        const [patch] = context.http.match(isPatch);

        await choose(context, 'dashboard-duplicate');
        expect(nameDialog()).toBeNull();
        context.http.expectNone(`${dashboardUrlOf('d1')}/copies`);

        patch.flush(widget('w1', { layout: { x: 0, y: 1, w: 6, h: 4 } }));
        await settle(context.harness);
        expect(nameDialog()).not.toBeNull();
        await nameAndSave(context, null);
        context.http.expectOne(`${dashboardUrlOf('d1')}/copies`);
      }, 10000);
    });

    it('asks with the number of widgets the server has, then opens the next dashboard (Dashboard with widgets removed, Next dashboard opens)', async () => {
      const shown = [widget('w1', { dashboardId: 'd2' }), widget('w2', { dashboardId: 'd2' }), widget('w3', { dashboardId: 'd2' })];
      const context = await open('analyst', shown, answer, {}, { dashboardId: 'd2', dashboards: [housing, health, field] });

      await choose(context, 'dashboard-remove');
      // Two widgets were added since the page opened: the question counts them.
      const five = Array.from({ length: 5 }, (_, i) => widget(`w${i + 1}`, { dashboardId: 'd2' }));
      context.http.expectOne(isDashboardOf('d2')).flush(dashboard(five, { id: 'd2', name: 'Health' }));
      await settle(context.harness);
      expect(text(overlay().querySelector('#tmz-confirm-message'))).toBe('Remove Health and its 5 widgets? This cannot be undone.');
      (overlay().querySelector('[data-testid="confirm"]') as HTMLElement).click();
      await settle(context.harness);
      const request = context.http.expectOne((r) => r.method === 'DELETE' && r.url === dashboardUrlOf('d2'));
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle(context.harness);
      await answerLoad(context, [], {}, 'd3');

      expect(url()).toBe(`/projects/${projectId}/dashboards/d3`);
      expect(tabs(context.root)).toEqual(['Housing', 'Field']);
      expect(text(heading(context.root))).toBe('Field');
      expect(document.activeElement).toBe(heading(context.root));
      expect(toasts()).toEqual(['"Health" and its 5 widgets were removed.']);
      // Field opens as any dashboard would: no notice, and its tab alone is the current page.
      expect(byTestId(context.root, 'dashboard-notice')).toBeNull();
      const current = Array.from(context.root.querySelectorAll('nav a[aria-current="page"]'));
      expect(current).toEqual([byTestId(context.root, 'dashboard-tab-d3')]);
      expect(context.http.match(isDashboardOf('d2'))).toHaveLength(0);
    });

    it('keeps the dashboard when the removal is not confirmed', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing, health] });

      await choose(context, 'dashboard-remove');
      context.http.expectOne(isDashboardOf('d1')).flush(dashboard([], { name: 'Housing' }));
      await settle(context.harness);
      expect(text(overlay().querySelector('#tmz-confirm-message'))).toBe('Remove Housing? It has no widgets. This cannot be undone.');
      (overlay().querySelector('button[data-testid="confirm"]')!.previousElementSibling as HTMLElement).click();
      await settle(context.harness);

      context.http.expectNone((r) => r.method === 'DELETE');
      expect(tabs(context.root)).toEqual(['Housing', 'Health']);
    });

    it('opens the new last dashboard after removing the last one (Last dashboard removed)', async () => {
      const context = await open('analyst', [], null, {}, { dashboardId: 'd3', dashboards: [housing, health, field] });

      await choose(context, 'dashboard-remove');
      context.http.expectOne(isDashboardOf('d3')).flush(dashboard([widget('w1', { dashboardId: 'd3' })], { id: 'd3', name: 'Field' }));
      await settle(context.harness);
      expect(text(overlay().querySelector('#tmz-confirm-message'))).toBe('Remove Field and its widget? This cannot be undone.');
      (overlay().querySelector('[data-testid="confirm"]') as HTMLElement).click();
      await settle(context.harness);
      context.http.expectOne(dashboardUrlOf('d3')).flush(null, { status: 204, statusText: 'No Content' });
      await settle(context.harness);
      await answerLoad(context, [], {}, 'd2');

      expect(url()).toBe(`/projects/${projectId}/dashboards/d2`);
      expect(text(heading(context.root))).toBe('Health');
      expect(toasts()).toEqual(['"Field" and its widget were removed.']);
    });

    it('offers no removal of the only dashboard, and says why (Only dashboard)', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing] });

      byTestId(context.root, 'dashboard-menu')!.click();
      await settle(context.harness);

      const remove = item('dashboard-remove')!;
      expect(remove.getAttribute('aria-disabled')).toBe('true');
      expect(text(document.getElementById(remove.getAttribute('aria-describedby')!))).toBe('A project needs at least one dashboard.');
      expect(item('dashboard-move-left')!.getAttribute('aria-disabled')).toBe('true');
      expect(item('dashboard-move-right')!.getAttribute('aria-disabled')).toBe('true');
      remove.click();
      await settle(context.harness);
      context.http.expectNone(isDashboard);
    });

    it('says why when the API refuses to remove what has become the only dashboard, and reads the project again', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing, health] });

      await choose(context, 'dashboard-remove');
      context.http.expectOne(isDashboardOf('d1')).flush(dashboard([], { name: 'Housing' }));
      await settle(context.harness);
      (overlay().querySelector('[data-testid="confirm"]') as HTMLElement).click();
      await settle(context.harness);
      context.http.expectOne(dashboardUrlOf('d1')).flush(...conflict('dashboards.lastOne'));
      await settle(context.harness);
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing] }));
      await settle(context.harness);

      expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('A project needs at least one dashboard.');
      expect(status(context.root)).toBe('A project needs at least one dashboard.');
      expect(toasts()).toEqual([]);
      expect(tabs(context.root)).toEqual(['Housing']);
      expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
    });

    it.each<ProjectRole>(['viewer'])('shows a %s the heading and none of the actions (No actions for a viewer)', async (role) => {
      const { root } = await open(role, two, answer, {}, { dashboards: [housing, health] });

      expect(text(heading(root))).toBe('Housing');
      expect(byTestId(root, 'dashboard-actions')).toBeNull();
      expect(byTestId(root, 'dashboard-add')).toBeNull();
      expect(byTestId(root, 'dashboard-menu')).toBeNull();
    });

    it.each<ProjectRole>(['analyst', 'admin'])('gives a %s the actions after the heading', async (role) => {
      const { root } = await open(role, [], null, {}, { dashboards: [housing, health] });

      const header = byTestId(root, 'dashboard-header')!;
      expect(text(byTestId(header, 'dashboard-add'))).toBe('New dashboard');
      expect(byTestId(header, 'dashboard-menu')!.getAttribute('aria-label')).toBe('Dashboard actions');
      expect(byTestId(header, 'dashboard-add')!.getAttribute('aria-disabled')).toBeNull();
      expect(byTestId(root, 'dashboard-limit-hint')).toBeNull();
    });

    it.each(['performance', 'clean-data'])('offers no dashboard actions on %s (Permanent dashboards)', async (section) => {
      const { root } = await openProjectSection(section, 'analyst', { dashboards: [housing, health] });

      expect(byTestId(root, 'dashboard-header')).toBeNull();
      expect(byTestId(root, 'dashboard-add')).toBeNull();
      expect(byTestId(root, 'dashboard-menu')).toBeNull();
    });

    describe('a dashboard another member removed', () => {
      it('closes the widget form and opens the first dashboard, read once (Widget added to a removed dashboard)', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health] });
        byTestId(context.root, 'widget-add')!.click();
        await settle(context.harness);
        context.http.expectOne(isColumns).flush(catalog());
        await settle(context.harness);
        typeInto(byTestId<HTMLInputElement>(overlay(), 'dialog-title')!, 'Clinics');
        await settle(context.harness);
        byTestId(overlay(), 'dialog-save')!.click();
        await settle(context.harness);

        context.http.expectOne((r) => r.method === 'POST' && r.url === widgetsUrl).flush(...removedMeanwhile);
        await settle(context.harness);
        expect(byTestId(overlay(), 'widget-dialog')).toBeNull();
        expect(tabs(context.root)).toEqual(['Health']); // at once, before the project is read again
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [health] }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd2'); // exactly one read of Health

        expect(url()).toBe(`/projects/${projectId}/dashboards/d2`);
        expect(TestBed.inject(Router).lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
        expect(text(heading(context.root))).toBe('Health');
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Housing" no longer exists. Showing Health instead.');
        expect(status(context.root)).toBe('"Housing" no longer exists. Showing Health instead.');
        expect(tabs(context.root)).toEqual(['Health']);
        expect(document.activeElement).toBe(heading(context.root));
        expect(toasts()).toEqual([]);
        expect(context.http.match(isDashboardOf('d2'))).toHaveLength(0);
        expect(context.http.match(isDashboardOf('d1'))).toHaveLength(0);
      });

      it('opens the first dashboard after a refused layout save (Widget edited on a removed dashboard)', async () => {
        const context = await open('analyst', [widget('w1', { dashboardId: 'd2' })], answer, {}, { dashboardId: 'd2', dashboards: [housing, health] });
        const grip = context.root.querySelector<HTMLElement>('[data-widget-id="w1"] [data-testid="widget-move"]')!;
        grip.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
        await settle(context.harness);
        await wait(layoutSaveDelayMs + 50);

        const [patch] = context.http.match(isPatch);
        expect(patch.request.url).toBe(`${dashboardUrlOf('d2')}/widgets/w1`);
        patch.flush(...removedMeanwhile);
        await settle(context.harness);
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing] }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd1');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Health" no longer exists. Showing Housing instead.');
        expect(toasts()).toEqual([]);
        expect(context.http.match(isDashboardOf('d2'))).toHaveLength(0); // not read again
      }, 10000);

      it('opens the first dashboard after a refused widget removal (Widget edited on a removed dashboard)', async () => {
        const context = await open('analyst', [widget('w1', { dashboardId: 'd2', title: 'Clinics' })], answer, {}, { dashboardId: 'd2', dashboards: [housing, health] });
        all(context.root, 'menu-trigger')[0].click();
        await settle(context.harness);
        item('widget-remove')!.click();
        await settle(context.harness);
        (overlay().querySelector('[data-testid="confirm"]') as HTMLElement).click();
        await settle(context.harness);

        context.http.expectOne((r) => r.method === 'DELETE' && r.url === `${dashboardUrlOf('d2')}/widgets/w1`).flush(...removedMeanwhile);
        await settle(context.harness);
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing] }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd1');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Health" no longer exists. Showing Housing instead.');
        expect(toasts()).toEqual([]);
      });

      it('opens the first dashboard of the page\'s own list when the project cannot be read again', async () => {
        const context = await open('analyst', [], null, {}, { dashboardId: 'd2', dashboards: [housing, health, field] });

        await choose(context, 'dashboard-rename');
        await nameAndSave(context, 'Salud');
        context.http.expectOne(dashboardUrlOf('d2')).flush(...removedMeanwhile);
        await settle(context.harness);
        context.http.expectOne(projectUrl).flush({}, { status: 500, statusText: 'Server Error' });
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd1');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
        expect(tabs(context.root)).toEqual(['Housing', 'Field']);
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Health" no longer exists. Showing Housing instead.');
        expect(toasts()).toEqual(['Something went wrong. Try again.']); // the failed read, not the refusal
      });

      it('says the dashboard no longer exists when the page knew no other and the project cannot be read again', async () => {
        const context = await open('analyst', [widget('w1', { title: 'Clinics' })], answer, {}, { dashboards: [housing] });
        all(context.root, 'menu-trigger')[0].click();
        await settle(context.harness);
        item('widget-remove')!.click();
        await settle(context.harness);
        (overlay().querySelector('[data-testid="confirm"]') as HTMLElement).click();
        await settle(context.harness);

        context.http.expectOne((r) => r.method === 'DELETE' && r.url === `${dashboardUrlOf('d1')}/widgets/w1`).flush(...removedMeanwhile);
        await settle(context.harness);
        context.http.expectOne(projectUrl).flush({}, { status: 500, statusText: 'Server Error' });
        await settle(context.harness);

        expect(text(byTestId(context.root, 'indicators-error'))).toBe('The dashboard no longer exists.');
        expect(all(context.root, 'widget-title')).toHaveLength(0);
        expect(toasts()).toEqual(['Something went wrong. Try again.']);
      });

      it('opens the first dashboard when the one opened from its tab was removed meanwhile, saying so once', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field] });

        await go(context, 'd3');
        context.http.expectOne(isOverview).flush(overview());
        context.http.expectOne(isDashboardOf('d3')).flush(...removedMeanwhile);
        await settle(context.harness);
        // Still loading, not an error: the notice alone will say what happened.
        expect(byTestId(context.root, 'indicators-error')).toBeNull();
        expect(byTestId(context.root, 'indicators-loading')?.getAttribute('aria-busy')).toBe('true');
        expect(tabs(context.root)).toEqual(['Housing', 'Health']);
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing, health] }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd1');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
        expect(TestBed.inject(Router).lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Field" no longer exists. Showing Housing instead.');
        expect(status(context.root)).toBe('"Field" no longer exists. Showing Housing instead.');
        expect(byTestId(context.root, 'indicators-error')).toBeNull();
        expect(toasts()).toEqual([]);
        expect(context.http.match(isDashboardOf('d3'))).toHaveLength(0);
      });

      it('opens the first dashboard, asking nothing, when the dashboard to remove was removed meanwhile', async () => {
        const context = await open('analyst', [], null, {}, { dashboardId: 'd2', dashboards: [housing, health] });

        await choose(context, 'dashboard-remove');
        context.http.expectOne(isDashboardOf('d2')).flush(...removedMeanwhile);
        await settle(context.harness);
        expect(overlay().querySelector('#tmz-confirm-message')).toBeNull();
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing] }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd1');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Health" no longer exists. Showing Housing instead.');
        expect(document.activeElement).toBe(heading(context.root));
        expect(toasts()).toEqual([]);
        context.http.expectNone((r) => r.method === 'DELETE');
      });

      it('opens the first dashboard when a refused move finds the open dashboard removed meanwhile', async () => {
        const context = await open('analyst', [], null, {}, { dashboardId: 'd2', dashboards: [housing, health, field] });

        await choose(context, 'dashboard-move-right');
        context.http.expectOne(orderUrl).flush(...conflict('dashboards.orderChanged'));
        await settle(context.harness);
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing, field] }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd1');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
        expect(TestBed.inject(Router).lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
        expect(tabs(context.root)).toEqual(['Housing', 'Field']);
        expect(text(heading(context.root))).toBe('Housing');
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Health" no longer exists. Showing Housing instead.');
        expect(document.activeElement).toBe(heading(context.root));
        expect(toasts()).toEqual([]);
      });

      it('opens the first dashboard when the project read after a refused add no longer has the open one, the form keeping the focus', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: seven.slice(0, 6) });
        const add = byTestId(context.root, 'dashboard-add')!;
        add.focus();
        add.click();
        await settle(context.harness);
        await nameAndSave(context, 'Field progress');

        context.http.expectOne(dashboardsUrl).flush(...conflict('dashboards.limit'));
        await settle(context.harness);
        // Another member removed "Dashboard 1", the one open, and added two.
        const fresh = [...seven.slice(1), { id: 'd8', name: 'Dashboard 8' }];
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: fresh }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd2');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d2`);
        expect(TestBed.inject(Router).lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
        expect(text(heading(context.root))).toBe('Dashboard 2');
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Dashboard 1" no longer exists. Showing Dashboard 2 instead.');
        expect(tabs(context.root)).toEqual(fresh.map((d) => d.name));
        expect(toasts()).toEqual([]);
        // The form stays open with its message, and keeps the focus the heading would otherwise take.
        expect(text(byTestId(nameDialog()!, 'dashboard-name-error'))).toBe('A project can have at most 7 dashboards. Remove one before adding another.');
        expect(nameDialog()!.contains(document.activeElement)).toBe(true);

        byTestId(nameDialog()!, 'dashboard-name-cancel')!.click();
        await settle(context.harness);
        expect(document.activeElement).toBe(byTestId(context.root, 'dashboard-add'));
        context.http.expectNone(isDashboardOf('d1'));
      });

      it('opens the first dashboard when a save refused on a dashboard left shows the open one removed too', async () => {
        const context = await open('analyst', [widget('w1', { title: 'Left' })], answer, {}, { dashboards: [housing, health, field] });
        const grip = context.root.querySelector<HTMLElement>('[data-widget-id="w1"] [data-testid="widget-move"]')!;
        grip.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
        await settle(context.harness);
        await go(context, 'd2');
        const [patch] = context.http.match(isPatch);
        await answerLoad(context, [], {}, 'd2');

        // Housing, where the layout was saved, and Health, the one open, were both removed.
        patch.flush(...removedMeanwhile);
        await settle(context.harness);
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [field] }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd3');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d3`);
        expect(text(heading(context.root))).toBe('Field');
        expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('"Health" no longer exists. Showing Field instead.');
        expect(document.activeElement).toBe(heading(context.root));
        expect(toasts()).toEqual([]);
      });

      it('stays on the page the member went to when a layout save sent as the dashboard was left finds it removed', async () => {
        const context = await open('analyst', [widget('w1', { title: 'Left' })], answer, {}, { dashboards: [housing, health] });
        const grip = context.root.querySelector<HTMLElement>('[data-widget-id="w1"] [data-testid="widget-move"]')!;
        grip.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
        await settle(context.harness);
        expect(context.http.match(isPatch)).toHaveLength(0); // still waiting for the pause

        await TestBed.inject(Router).navigateByUrl(`/projects/${projectId}/performance`);
        await settle(context.harness);
        const [patch] = context.http.match(isPatch);
        expect(patch.request.url).toBe(widgetUrl('w1')); // sent as the page went
        patch.flush(...removedMeanwhile);
        await settle(context.harness);
        // Only the project is read again, so the tab row drops the removed dashboard.
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [health] }));
        await settle(context.harness);

        expect(url()).toBe(`/projects/${projectId}/performance`);
        expect(TestBed.inject(Router).lastSuccessfulNavigation()?.extras.replaceUrl).toBeFalsy();
        expect(tabs(context.root)).toEqual(['Health']);
        expect(context.harness.fixture.debugElement.query(By.css('app-project-indicators'))).toBeNull();
        expect(byTestId(context.root, 'dashboard-notice')).toBeNull();
        expect(toasts()).toEqual([]);
        expect(context.http.match(isOverview)).toHaveLength(0);
        expect(context.http.match(isDashboardOf('d2'))).toHaveLength(0);
      });

      it('asks nothing when another dashboard was opened before the widget count of the one to remove came', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field] });

        await choose(context, 'dashboard-remove');
        const count = context.http.expectOne(isDashboardOf('d1'));
        await go(context, 'd3');
        await answerLoad(context, [], {}, 'd3');
        count.flush(dashboard([widget('w1')], { name: 'Housing' }));
        await settle(context.harness);

        expect(overlay().querySelector('#tmz-confirm-message')).toBeNull();
        context.http.expectNone((r) => r.method === 'DELETE');
        expect(url()).toBe(`/projects/${projectId}/dashboards/d3`);
        expect(tabs(context.root)).toEqual(['Housing', 'Health', 'Field']);
      });

      it('only drops from the tab row a dashboard to remove that was removed meanwhile, once another one is open', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field] });

        await choose(context, 'dashboard-remove');
        const count = context.http.expectOne(isDashboardOf('d1'));
        await go(context, 'd3');
        await answerLoad(context, [], {}, 'd3');
        count.flush(...removedMeanwhile);
        await settle(context.harness);
        expect(tabs(context.root)).toEqual(['Health', 'Field']); // at once
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [health, field] }));
        await settle(context.harness);

        expect(url()).toBe(`/projects/${projectId}/dashboards/d3`);
        expect(text(heading(context.root))).toBe('Field');
        expect(byTestId(context.root, 'dashboard-notice')).toBeNull();
        expect(overlay().querySelector('#tmz-confirm-message')).toBeNull();
        expect(toasts()).toEqual([]);
        expect(context.http.match(isDashboardOf('d2'))).toHaveLength(0);
      });

      it('stays on the dashboard opened while the removal was on its way', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field] });

        await choose(context, 'dashboard-remove');
        context.http.expectOne(isDashboardOf('d1')).flush(dashboard([], { name: 'Housing' }));
        await settle(context.harness);
        (overlay().querySelector('[data-testid="confirm"]') as HTMLElement).click();
        await settle(context.harness);
        const removal = context.http.expectOne((r) => r.method === 'DELETE' && r.url === dashboardUrlOf('d1'));
        await go(context, 'd3');
        await answerLoad(context, [], {}, 'd3');
        removal.flush(null, { status: 204, statusText: 'No Content' });
        await settle(context.harness);

        expect(url()).toBe(`/projects/${projectId}/dashboards/d3`);
        expect(text(heading(context.root))).toBe('Field');
        expect(tabs(context.root)).toEqual(['Health', 'Field']);
        expect(toasts()).toEqual(['"Housing" was removed.']);
        expect(context.http.match(isDashboardOf('d2'))).toHaveLength(0);
      });
    });

    it('refuses a move from an order that missed an added dashboard, and shows the current ones (Order changed meanwhile)', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing, health] });

      await choose(context, 'dashboard-move-right');
      const request = context.http.expectOne(orderUrl);
      expect(request.request.body).toEqual({ expectedIds: ['d1', 'd2'], dashboardIds: ['d2', 'd1'] });
      request.flush(...conflict('dashboards.orderChanged'));
      await settle(context.harness);
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing, health, field] }));
      await settle(context.harness);

      expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('The project’s dashboards have changed. Reload and try again.');
      expect(tabs(context.root)).toEqual(['Housing', 'Health', 'Field']);
      expect(toasts()).toEqual([]);
      expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
    });

    it('refuses a move from an order another member changed, and shows that order (Moved meanwhile)', async () => {
      const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field] });

      await choose(context, 'dashboard-move-right');
      const request = context.http.expectOne(orderUrl);
      expect(request.request.body).toEqual({ expectedIds: ['d1', 'd2', 'd3'], dashboardIds: ['d2', 'd1', 'd3'] });
      request.flush(...conflict('dashboards.orderChanged'));
      await settle(context.harness);
      // Field was moved left meanwhile, and keeps its new place.
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing, field, health] }));
      await settle(context.harness);

      expect(tabs(context.root)).toEqual(['Housing', 'Field', 'Health']);
      expect(text(byTestId(context.root, 'dashboard-notice-text'))).toBe('The project’s dashboards have changed. Reload and try again.');
      expect(toasts()).toEqual([]);
    });

    it('keeps the limit in the name dialog and reads the project again, turning "New dashboard" off', async () => {
      const six = seven.slice(0, 6);
      const context = await open('analyst', [], null, {}, { dashboards: six });

      byTestId(context.root, 'dashboard-add')!.click();
      await settle(context.harness);
      await nameAndSave(context, 'Field progress');
      context.http.expectOne(dashboardsUrl).flush(...conflict('dashboards.limit'));
      await settle(context.harness);
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: seven }));
      await settle(context.harness);

      expect(text(byTestId(nameDialog()!, 'dashboard-name-error'))).toBe('A project can have at most 7 dashboards. Remove one before adding another.');
      expect(toasts()).toEqual([]);
      expect(tabs(context.root)).toEqual(seven.map((d) => d.name));
      expect(byTestId(context.root, 'dashboard-add')!.getAttribute('aria-disabled')).toBe('true');
    });

    it.each(['dashboards.notFound', 'dashboards.limit', 'dashboards.lastOne', 'dashboards.orderChanged'])(
      'raises no toast for a %s refusal, and reads the project again',
      async (code) => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health] });

        await choose(context, 'dashboard-move-right');
        context.http.expectOne(orderUrl).flush({ title: 'x', code }, { status: code === 'dashboards.notFound' ? 404 : 409, statusText: 'Refused' });
        await settle(context.harness);
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [health] }));
        await settle(context.harness);
        context.http.match(isDashboardOf('d2')).forEach((r) => r.flush(dashboard([], { id: 'd2', name: 'Health' })));
        context.http.match(isOverview).forEach((r) => r.flush(overview()));
        await settle(context.harness);

        expect(toasts()).toEqual([]);
        expect(tabs(context.root)).toEqual(['Health']);
      },
    );

    describe('from the keyboard', () => {
      it('opens the dashboard added and puts focus on its heading (Keyboard user adds a dashboard)', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing] });
        const add = byTestId(context.root, 'dashboard-add')!;

        add.focus();
        add.click();
        await settle(context.harness);
        expect(document.activeElement).toBe(nameInput());
        typeInto(nameInput(), 'Field progress');
        (nameDialog() as HTMLFormElement).requestSubmit(); // Enter in the field
        await settle(context.harness);
        context.http.expectOne(dashboardsUrl).flush({ id: 'd9', name: 'Field progress' }, { status: 201, statusText: 'Created' });
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd9');

        expect(text(heading(context.root))).toBe('Field progress');
        expect(document.activeElement).toBe(heading(context.root));
      });

      it('returns focus to the actions button after a move, so it can be repeated (Keyboard user moves a dashboard)', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health, field] });
        const trigger = await openMenuByKeyboard(context);
        expect(document.activeElement).toBe(item('dashboard-rename'));

        // Rename, Duplicate, Move left (unavailable, still reached), Move right.
        for (let i = 0; i < 3; i++) {
          pressKey(document.activeElement!, 'ArrowDown');
        }
        expect(document.activeElement).toBe(item('dashboard-move-right'));
        pressKey(document.activeElement!, 'Enter');
        await settle(context.harness);
        context.http.expectOne(orderUrl).flush([health, housing, field]);
        await settle(context.harness);

        expect(document.activeElement).toBe(trigger);
        expect(tabs(context.root)).toEqual(['Health', 'Housing', 'Field']);

        pressKey(trigger, 'ArrowDown');
        await settle(context.harness);
        for (let i = 0; i < 3; i++) {
          pressKey(document.activeElement!, 'ArrowDown');
        }
        pressKey(document.activeElement!, 'Enter');
        await settle(context.harness);
        expect(context.http.expectOne(orderUrl).request.body).toEqual({ expectedIds: ['d2', 'd1', 'd3'], dashboardIds: ['d2', 'd3', 'd1'] });
      });

      it('returns focus to the actions button after a rename (Keyboard user renames a dashboard)', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health] });
        const trigger = await openMenuByKeyboard(context);

        pressKey(item('dashboard-rename')!, 'Enter');
        await settle(context.harness);
        expect(document.activeElement).toBe(nameInput());
        typeInto(nameInput(), 'Vivienda');
        (nameDialog() as HTMLFormElement).requestSubmit();
        await settle(context.harness);
        context.http.expectOne(dashboardUrlOf('d1')).flush({ id: 'd1', name: 'Vivienda' });
        await settle(context.harness);

        expect(nameDialog()).toBeNull();
        expect(document.activeElement).toBe(trigger);
        expect(text(heading(context.root))).toBe('Vivienda');
      });

      it('returns focus to the actions button when the name dialog is cancelled (Name dialog cancelled)', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing, health] });
        const trigger = await openMenuByKeyboard(context);

        pressKey(document.activeElement!, 'ArrowDown');
        expect(document.activeElement).toBe(item('dashboard-duplicate'));
        pressKey(document.activeElement!, 'Enter');
        await settle(context.harness);
        expect(document.activeElement).toBe(nameInput());
        pressKey(nameInput(), 'Escape');
        await settle(context.harness);

        expect(nameDialog()).toBeNull();
        expect(document.activeElement).toBe(trigger);
        context.http.expectNone((r) => r.method === 'POST');
      });

      it('returns focus to "New dashboard" when the name dialog it opened is cancelled', async () => {
        const context = await open('analyst', [], null, {}, { dashboards: [housing] });
        const add = byTestId(context.root, 'dashboard-add')!;

        add.focus();
        add.click();
        await settle(context.harness);
        byTestId(nameDialog()!, 'dashboard-name-cancel')!.click();
        await settle(context.harness);

        expect(document.activeElement).toBe(add);
      });

      it('opens the first dashboard and puts focus on its heading after a refusal (Focus after a refusal)', async () => {
        const context = await open('analyst', [], null, {}, { dashboardId: 'd2', dashboards: [housing, health] });
        await openMenuByKeyboard(context);

        pressKey(item('dashboard-rename')!, 'Enter');
        await settle(context.harness);
        typeInto(nameInput(), 'Salud');
        (nameDialog() as HTMLFormElement).requestSubmit();
        await settle(context.harness);
        context.http.expectOne(dashboardUrlOf('d2')).flush(...removedMeanwhile);
        await settle(context.harness);
        context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: [housing] }));
        await settle(context.harness);
        await answerLoad(context, [], {}, 'd1');

        expect(url()).toBe(`/projects/${projectId}/dashboards/d1`);
        expect(document.activeElement).toBe(heading(context.root));
        expect(text(document.activeElement)).toBe('Housing');
      });
    });
  });
});
