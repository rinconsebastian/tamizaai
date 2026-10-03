import { TestBed } from '@angular/core/testing';
import { INGESTION_POLL_MS, IngestionStatus, SyncItem, SyncPage } from '../../core/projects/ingestion.service';
import { ProjectRole } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
import { tick } from '../../../testing/cdk';
import { byTestId, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';

const statusUrl = `/api/v1/projects/${projectId}/ingestion`;
const syncsUrl = `/api/v1/projects/${projectId}/syncs`;

function sync(overrides: Partial<SyncItem> = {}): SyncItem {
  return {
    id: 's1',
    kind: 'incremental',
    trigger: 'schedule',
    status: 'succeeded',
    requestedBy: null,
    queuedAt: '2026-10-02T10:00:00Z',
    startedAt: '2026-10-02T10:00:01Z',
    finishedAt: '2026-10-02T10:00:09Z',
    createdCount: 4,
    updatedCount: 2,
    deletedCount: 0,
    errorCode: null,
    error: null,
    ...overrides,
  };
}

function status(overrides: Partial<IngestionStatus> = {}): IngestionStatus {
  return {
    submissionCount: 117,
    deletedCount: 3,
    lastWebhookAt: null,
    lastSync: sync(),
    activeSync: null,
    nextSyncAt: '2026-10-02T22:00:01Z',
    nextReconcileAt: '2026-10-03T10:00:01Z',
    syncIntervalHours: 12,
    reconcileIntervalHours: 24,
    ...overrides,
  };
}

describe('ProjectData', () => {
  async function open(role: ProjectRole, current = status(), log: SyncPage = { items: [sync()], total: 1 }, pollMs?: number) {
    const providers = pollMs ? [{ provide: INGESTION_POLL_MS, useValue: pollMs }] : [];
    const opened = await openProjectSection('data', role, {}, providers);
    opened.http.expectOne(statusUrl).flush(current);
    opened.http.expectOne((r) => r.url === syncsUrl && r.method === 'GET').flush(log);
    await settle(opened.harness);
    return opened;
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('shows the Data tab and the status to viewers, without actions and with a read-only schedule', async () => {
    const { root } = await open('viewer');

    expect(root.querySelector(`a[href="/projects/${projectId}/data"]`)?.textContent).toContain('Data');
    expect(byTestId(root, 'submission-count')?.textContent?.trim()).toBe('117');
    expect(byTestId(root, 'deleted-count')?.textContent?.trim()).toBe('3');
    expect(byTestId(root, 'last-webhook')?.textContent).toContain('None received yet');
    expect(byTestId(root, 'last-sync')?.textContent).toContain('Succeeded');
    expect(byTestId(root, 'sync-now')).toBeNull();
    expect(byTestId(root, 'full-sync')).toBeNull();
    expect(byTestId(root, 'schedule-form')).toBeNull();
    expect(byTestId(root, 'schedule-read-only')?.textContent).toContain('Every 12 hours');
  });

  it('lets analysts start a sync, then disables the actions while it is active', async () => {
    const { root, http, harness } = await open('analyst');
    expect(byTestId(root, 'schedule-form')).toBeNull();

    byTestId<HTMLButtonElement>(root, 'sync-now')!.click();
    const request = http.expectOne((r) => r.url === syncsUrl && r.method === 'POST');
    expect(request.request.body).toEqual({ kind: 'incremental' });
    request.flush(sync({ id: 's2', status: 'queued', trigger: 'manual', startedAt: null, finishedAt: null }));
    http.expectOne((r) => r.url === syncsUrl && r.method === 'GET').flush({ items: [], total: 0 });
    await settle(harness);

    expect(byTestId(root, 'active-sync')?.textContent).toContain('Queued');
    expect(byTestId<HTMLButtonElement>(root, 'sync-now')!.disabled).toBe(true);
    expect(byTestId<HTMLButtonElement>(root, 'full-sync')!.disabled).toBe(true);
  });

  it('starts a full resync only after confirmation', async () => {
    const { root, http, harness } = await open('admin');

    byTestId<HTMLButtonElement>(root, 'full-sync')!.click();
    await settle(harness);
    http.expectNone((r) => r.url === syncsUrl && r.method === 'POST');
    expect(document.body.textContent).toContain('Re-read every submission?');
    (document.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await tick();
    await settle(harness);

    expect(http.expectOne((r) => r.url === syncsUrl && r.method === 'POST').request.body).toEqual({ kind: 'full' });
  });

  it('explains that a synchronization is already in progress', async () => {
    const { root, http, harness } = await open('analyst');

    byTestId<HTMLButtonElement>(root, 'sync-now')!.click();
    http
      .expectOne((r) => r.url === syncsUrl && r.method === 'POST')
      .flush({ title: 'Conflict', detail: 'busy', code: 'sync.in_progress' }, { status: 409, statusText: 'Conflict' });
    await settle(harness);

    expect(TestBed.inject(ToastService).toasts()[0].message).toContain('already in progress');
    http.expectOne(statusUrl).flush(status({ activeSync: sync({ status: 'running' }) }));
    await settle(harness);
    expect(byTestId(root, 'active-sync')?.textContent).toContain('Running');
  });

  it('saves the schedule for admins and shows range errors next to the fields', async () => {
    const { root, http, harness } = await open('admin');
    typeInto(root.querySelector<HTMLInputElement>('#syncIntervalHours')!, '0');

    byTestId<HTMLFormElement>(root, 'schedule-form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne((r) => r.url === statusUrl && r.method === 'PATCH');
    expect(request.request.body).toEqual({ syncIntervalHours: 0, reconcileIntervalHours: 24 });
    request.flush(
      { title: 'One or more validation errors occurred.', errors: { syncIntervalHours: ['Enter a whole number of hours from 1 to 168.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(harness);

    expect(byTestId(root, 'field-error')?.textContent).toContain('from 1 to 168');

    typeInto(root.querySelector<HTMLInputElement>('#syncIntervalHours')!, '6');
    byTestId<HTMLFormElement>(root, 'schedule-form')!.dispatchEvent(new Event('submit'));
    http.expectOne((r) => r.url === statusUrl && r.method === 'PATCH').flush(status({ syncIntervalHours: 6 }));
    await settle(harness);

    expect(byTestId(root, 'field-error')).toBeNull();
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toBe('Schedule saved.');
  });

  it('renders each status in the log with the translated error of failed runs', async () => {
    const items = [
      sync({ id: 'a', status: 'queued', startedAt: null }),
      sync({ id: 'b', status: 'running', kind: 'full', trigger: 'manual', requestedBy: { id: 'u1', name: 'Ada' } }),
      sync({ id: 'c', status: 'failed', errorCode: 'kobo.unauthorized', error: 'Kobo rejected the API token.' }),
      sync({ id: 'd', status: 'failed', errorCode: 'kobo.something_new', error: 'A message from the API.' }),
      sync({ id: 'e', status: 'succeeded', kind: 'reconciliation', deletedCount: 2 }),
    ];
    const { root } = await open('viewer', status(), { items, total: items.length });
    const table = byTestId(root, 'log-table')!;

    for (const state of ['queued', 'running', 'failed', 'succeeded']) {
      expect(byTestId(table, `log-row-${state}`)).not.toBeNull();
    }
    const errors = [...table.querySelectorAll('[data-testid="log-error"]')].map((e) => e.textContent?.trim());
    expect(errors).toEqual([
      'Kobo rejected the API token, or the token cannot read this form and its submissions.',
      'A message from the API.',
    ]);
    expect(table.textContent).toContain('Full resync');
    expect(table.textContent).toContain('Deletion check');
    expect(table.textContent).toContain('Ada');
    expect(byTestId(root, 'log-cards')?.children.length).toBe(5);
  });

  it('shows an empty log and pages through a long one', async () => {
    const first = await open('viewer', status({ lastSync: null }), { items: [], total: 0 });
    expect(byTestId(first.root, 'log-empty')).not.toBeNull();
    expect(byTestId(first.root, 'last-sync')?.textContent).toContain('Never');
    TestBed.resetTestingModule();

    const { root, http, harness } = await open('viewer', status(), { items: Array.from({ length: 10 }, (_, i) => sync({ id: `s${i}` })), total: 25 });
    expect(byTestId(root, 'log-page-info')?.textContent).toContain('1–10 of 25');

    byTestId<HTMLButtonElement>(root, 'log-next')!.click();
    const page2 = http.expectOne((r) => r.url === syncsUrl && r.method === 'GET');
    expect(page2.request.params.get('page')).toBe('2');
    expect(page2.request.params.get('pageSize')).toBe('10');
    page2.flush({ items: [sync()], total: 25 });
    await settle(harness);

    expect(byTestId(root, 'log-page-info')?.textContent).toContain('11–20 of 25');
  });

  it('polls while a run is active and reloads the log when it ends', async () => {
    const { http, harness } = await open('viewer', status({ activeSync: sync({ status: 'running' }) }), undefined, 30);

    await new Promise((resolve) => setTimeout(resolve, 60));
    http.expectOne(statusUrl).flush(status({ activeSync: sync({ status: 'running' }) }));
    await settle(harness);
    await new Promise((resolve) => setTimeout(resolve, 60));
    http.expectOne(statusUrl).flush(status());
    await settle(harness);
    http.expectOne((r) => r.url === syncsUrl && r.method === 'GET').flush({ items: [sync()], total: 1 });

    // The run ended: no more polling.
    await new Promise((resolve) => setTimeout(resolve, 60));
    http.expectNone(statusUrl);
  });

  it('stops polling when the page is destroyed while a run is active', async () => {
    const { http, harness } = await open('viewer', status({ activeSync: sync({ status: 'queued' }) }), undefined, 30);

    harness.fixture.destroy();
    await new Promise((resolve) => setTimeout(resolve, 60));

    http.expectNone(statusUrl);
  });
});
