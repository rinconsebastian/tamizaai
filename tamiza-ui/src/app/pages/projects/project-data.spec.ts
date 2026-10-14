import { TestBed } from '@angular/core/testing';
import { INGESTION_POLL_MS, IngestionStatus, SyncItem, SyncPage } from '../../core/projects/ingestion.service';
import { FormVersionItem, FormVersionList, ProjectRole } from '../../core/projects/projects.service';
import { vi } from 'vitest';
import { ToastService } from '../../shared/ui';
import { tick } from '../../../testing/cdk';
import { byTestId, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';

const statusUrl = `/api/v1/projects/${projectId}/ingestion`;
const syncsUrl = `/api/v1/projects/${projectId}/syncs`;
const versionsUrl = `/api/v1/projects/${projectId}/form-versions`;

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
  async function open(
    role: ProjectRole,
    current = status(),
    log: SyncPage = { items: [sync()], total: 1 },
    pollMs?: number,
    versions: FormVersionList = { versions: [], withoutVersion: 0 },
  ) {
    const providers = pollMs ? [{ provide: INGESTION_POLL_MS, useValue: pollMs }] : [];
    const opened = await openProjectSection('data', role, {}, providers);
    opened.http.expectOne(statusUrl).flush(current);
    opened.http.expectOne((r) => r.url === syncsUrl && r.method === 'GET').flush(log);
    opened.http.expectOne(versionsUrl).flush(versions);
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

  describe('form versions', () => {
    function version(overrides: Partial<FormVersionItem> = {}): FormVersionItem {
      return {
        uid: 'vAbCdEfGhIjKlMnOpQrStU',
        current: false,
        deployedAt: '2026-09-01T10:00:00Z',
        submissions: 120,
        status: 'available',
        errorCode: null,
        error: null,
        nextAttemptAt: null,
        ...overrides,
      };
    }

    const mixed: FormVersionList = {
      withoutVersion: 3,
      versions: [
        version({ uid: 'vCurrentCurrentCurrent1', current: true, submissions: 120 }),
        version({ uid: 'vFailedFailedFailedFa2', status: 'failed', submissions: 15, errorCode: 'kobo.unreachable', error: 'The Kobo server could not be reached.', nextAttemptAt: '2026-10-02T13:00:00Z', deployedAt: null }),
        version({ uid: 'vPendingPendingPendin3', status: 'pending', submissions: 4, deployedAt: null }),
        version({ uid: 'vMissingMissingMissin4', status: 'not_found', submissions: 2, errorCode: 'kobo.version_not_found', error: 'Kobo has no version.', deployedAt: null }),
      ],
    };

    it.each<ProjectRole>(['viewer', 'analyst', 'admin'])('shows the card to %s', async (role) => {
      const { root } = await open(role, status(), undefined, undefined, mixed);

      expect(byTestId(root, 'form-versions')?.textContent).toContain('Form versions');
      expect(root.querySelectorAll('[data-testid="versions-table"] tbody tr').length).toBe(4);
    });

    it('shows each status with its count, reason and next attempt', async () => {
      const { root } = await open('viewer', status(), undefined, undefined, mixed);
      const table = byTestId(root, 'versions-table')!;

      const available = byTestId(table, 'version-row-available')!;
      expect(available.textContent).toContain('Current');
      expect(available.textContent).toContain('Labels available');
      expect(byTestId(available, 'version-submissions')?.textContent?.trim()).toBe('120');
      expect(byTestId(available, 'version-error')).toBeNull();

      const failed = byTestId(table, 'version-row-failed')!;
      expect(failed.textContent).toContain('Failed');
      expect(byTestId(failed, 'version-error')?.textContent).toContain('The Kobo server could not be reached. Check the address and try again.');
      expect(byTestId(failed, 'version-next-attempt')?.textContent).toContain('Next attempt:');
      expect(byTestId(failed, 'version-submissions')?.textContent?.trim()).toBe('15');

      const pending = byTestId(table, 'version-row-pending')!;
      expect(pending.textContent).toContain('Pending');
      expect(byTestId(pending, 'version-error')).toBeNull();
      expect(byTestId(pending, 'version-next-attempt')).toBeNull();

      const notFound = byTestId(table, 'version-row-not_found')!;
      expect(notFound.textContent).toContain('Not found');
      expect(byTestId(notFound, 'version-error')?.textContent).toContain('Kobo has no such version of this form.');
      expect(byTestId(notFound, 'version-next-attempt')).toBeNull();
    });

    it('lists the versions as cards for phones too', async () => {
      const { root } = await open('viewer', status(), undefined, undefined, mixed);

      const cards = byTestId(root, 'versions-cards')!;
      expect(cards.children.length).toBe(4);
      expect(cards.textContent).toContain('Labels available');
      expect(cards.textContent).toContain('Next attempt:');
    });

    it('shortens the version id, keeps the full one in the tooltip and copies it', async () => {
      const writeText = vi.fn(() => Promise.resolve());
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      const { root } = await open('viewer', status(), undefined, undefined, { versions: [version()], withoutVersion: 0 });

      const uid = byTestId<HTMLButtonElement>(root, 'version-uid')!;
      expect(uid.textContent?.trim()).toBe('vAbCdEfGh…rStU');
      expect(uid.getAttribute('title')).toBe('vAbCdEfGhIjKlMnOpQrStU');
      uid.click();
      expect(writeText).toHaveBeenCalledWith('vAbCdEfGhIjKlMnOpQrStU');
    });

    it('says how many submissions have no form version', async () => {
      const { root } = await open('viewer', status(), undefined, undefined, mixed);
      expect(byTestId(root, 'without-version')?.textContent).toContain('3 submissions have no form version');
      TestBed.resetTestingModule();

      const single = await open('viewer', status(), undefined, undefined, { versions: [version()], withoutVersion: 1 });
      expect(byTestId(single.root, 'without-version')?.textContent).toContain('1 submission has no form version');
      TestBed.resetTestingModule();

      const none = await open('viewer', status(), undefined, undefined, { versions: [version()], withoutVersion: 0 });
      expect(byTestId(none.root, 'without-version')).toBeNull();
    });

    it('points to the re-check only when a version failed or was not found', async () => {
      const { root } = await open('viewer', status(), undefined, undefined, mixed);
      const hint = byTestId(root, 'versions-retry-hint')!;
      expect(hint.textContent).toContain('Re-checking the form retries failed and not found versions.');
      expect(hint.querySelector('a')?.getAttribute('href')).toBe(`/projects/${projectId}/overview`);
      TestBed.resetTestingModule();

      const healthy = await open('viewer', status(), undefined, undefined, { versions: [version(), version({ uid: 'vOther', status: 'pending' })], withoutVersion: 0 });
      expect(byTestId(healthy.root, 'versions-retry-hint')).toBeNull();
    });

    it('explains an empty list', async () => {
      const { root } = await open('viewer');

      expect(byTestId(root, 'versions-empty')?.textContent).toContain('No form versions yet');
      expect(byTestId(root, 'versions-table')).toBeNull();
    });

    it('polls while a version is pending and stops when none is left', async () => {
      const pending: FormVersionList = { versions: [version({ status: 'pending' })], withoutVersion: 0 };
      const { http, harness, root } = await open('viewer', status(), undefined, 30, pending);

      await new Promise((resolve) => setTimeout(resolve, 60));
      http.expectOne(versionsUrl).flush({ ...pending }); // a response is a new object, as in production
      await settle(harness);
      await new Promise((resolve) => setTimeout(resolve, 60));
      http.expectOne(versionsUrl).flush({ versions: [version()], withoutVersion: 0 });
      await settle(harness);

      expect(byTestId(root, 'version-row-available')).not.toBeNull();
      await new Promise((resolve) => setTimeout(resolve, 80));
      http.expectNone(versionsUrl);
    });

    it('does not poll when every version has its labels and no run is active', async () => {
      const { http } = await open('viewer', status(), undefined, 30, { versions: [version()], withoutVersion: 0 });

      await new Promise((resolve) => setTimeout(resolve, 80));

      http.expectNone(versionsUrl);
    });

    it('reloads the versions when a synchronization ends, because it fetched them', async () => {
      const waiting: FormVersionList = { versions: [version({ status: 'pending' })], withoutVersion: 0 };
      const { http, harness, root } = await open('viewer', status({ activeSync: sync({ status: 'running' }) }), undefined, 30, waiting);

      await new Promise((resolve) => setTimeout(resolve, 60));
      http.match(versionsUrl).forEach((request) => request.flush({ ...waiting }));
      http.expectOne(statusUrl).flush(status());
      await settle(harness);
      http.match((r) => r.url === syncsUrl && r.method === 'GET').forEach((request) => request.flush({ items: [sync()], total: 1 }));
      http.match(versionsUrl).forEach((request) => request.flush({ versions: [version()], withoutVersion: 0 }));
      await settle(harness);

      expect(byTestId(root, 'version-row-available')).not.toBeNull();
      expect(byTestId(root, 'version-row-pending')).toBeNull();
    });
  });
});
