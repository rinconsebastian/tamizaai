import { TestBed } from '@angular/core/testing';
import { INGESTION_POLL_MS, IngestionStatus, SyncItem, SyncPage } from '../../core/projects/ingestion.service';
import { PipelineSettings } from '../../core/projects/pipeline.service';
import { FormVersionItem, FormVersionList, ProjectRole } from '../../core/projects/projects.service';
import { vi } from 'vitest';
import { ToastService } from '../../shared/ui';
import { tick } from '../../../testing/cdk';
import { byTestId, details, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';

const statusUrl = `/api/v1/projects/${projectId}/ingestion`;
const syncsUrl = `/api/v1/projects/${projectId}/syncs`;
const versionsUrl = `/api/v1/projects/${projectId}/form-versions`;
const pipelineUrl = `/api/v1/projects/${projectId}/pipeline`;
const runs: PipelineSettings = { runAfterSync: true, runIntervalHours: 24, nextRunAt: '2026-10-03T08:00:00Z' };
const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

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
    pipeline: PipelineSettings = runs,
  ) {
    const providers = pollMs ? [{ provide: INGESTION_POLL_MS, useValue: pollMs }] : [];
    const opened = await openProjectSection('data', role, {}, providers);
    opened.http.expectOne(statusUrl).flush(current);
    opened.http.expectOne((r) => r.url === syncsUrl && r.method === 'GET').flush(log);
    opened.http.expectOne(versionsUrl).flush(versions);
    opened.http.expectOne(pipelineUrl).flush(pipeline);
    await settle(opened.harness);
    return opened;
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('shows the Submissions section and the status to viewers, without actions and with the schedules as a line', async () => {
    const { root } = await open('viewer');

    expect(root.querySelector(`a[href="/projects/${projectId}/data"]`)?.textContent).toContain('Submissions');
    expect(byTestId(root, 'submission-count')?.textContent?.trim()).toBe('117');
    expect(byTestId(root, 'deleted-count')?.textContent?.trim()).toBe('3');
    expect(byTestId(root, 'last-webhook')?.textContent).toContain('None received yet');
    expect(byTestId(root, 'last-sync')?.textContent).toContain('Succeeded');
    expect(byTestId(root, 'sync-now')).toBeNull();
    expect(byTestId(root, 'full-sync')).toBeNull();
    expect(byTestId(root, 'schedule-form')).toBeNull();
    expect(text(byTestId(root, 'schedule-line'))).toBe('Synchronizes every 12 hours · checks for deletions every 24 hours');
    expect(text(byTestId(root, 'runs-line'))).toContain('Scripts run after each synchronization with changes · also run every 24 hours');
    expect(byTestId(root, 'schedule-edit')).toBeNull();
  });

  it('writes an hourly script schedule in the singular', async () => {
    const { root } = await open('viewer', status(), undefined, undefined, undefined, { ...runs, runIntervalHours: 1 });

    expect(text(byTestId(root, 'runs-line'))).toContain('also run every 1 hour');
    expect(text(byTestId(root, 'runs-line'))).not.toContain('1 hours');
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

  it('reads the schedules as a line for an admin, with the inputs behind an Edit button', async () => {
    const { root, harness } = await open('admin');
    expect(byTestId(root, 'schedule-form')).toBeNull();
    expect(byTestId(root, 'pipeline-settings-form')).toBeNull();

    const edit = byTestId<HTMLButtonElement>(root, 'schedule-edit')!;
    expect(edit.getAttribute('aria-expanded')).toBe('false');
    edit.click();
    await settle(harness);
    expect(edit.getAttribute('aria-expanded')).toBe('true');
    expect(root.querySelector<HTMLInputElement>('#syncIntervalHours')!.value).toBe('12');
    expect(root.querySelector<HTMLInputElement>('#runIntervalHours')!.value).toBe('24');
  });

  it('hides the inputs again after a saved schedule, and the line reads the new interval', async () => {
    const { root, http, harness } = await open('admin');
    byTestId<HTMLButtonElement>(root, 'schedule-edit')!.click();
    await settle(harness);
    typeInto(root.querySelector<HTMLInputElement>('#syncIntervalHours')!, '6');
    byTestId<HTMLFormElement>(root, 'schedule-form')!.dispatchEvent(new Event('submit'));
    http.expectOne((r) => r.url === statusUrl && r.method === 'PATCH').flush(status({ syncIntervalHours: 6 }));
    await settle(harness);

    expect(byTestId(root, 'schedule-form')).toBeNull();
    expect(text(byTestId(root, 'schedule-line'))).toContain('every 6 hours');
  });

  it('saves the run settings from the same place and shows their range error next to the field', async () => {
    const { root, http, harness } = await open('admin');
    byTestId<HTMLButtonElement>(root, 'schedule-edit')!.click();
    await settle(harness);
    typeInto(root.querySelector<HTMLInputElement>('#runIntervalHours')!, '500');
    byTestId<HTMLInputElement>(root, 'run-after-sync')!.checked = false;
    byTestId<HTMLFormElement>(root, 'pipeline-settings-form')!.dispatchEvent(new Event('submit'));
    const invalid = http.expectOne((r) => r.url === pipelineUrl && r.method === 'PUT');
    expect(invalid.request.body).toEqual({ runAfterSync: false, runIntervalHours: 500 });
    invalid.flush({ errors: { runIntervalHours: ['Enter a whole number of hours from 1 to 168, or no schedule.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(harness);
    expect(text(root.querySelector('#runIntervalHours-error'))).toContain('1 to 168');
    expect(byTestId(root, 'pipeline-settings-form')).not.toBeNull(); // a refused value keeps the inputs open

    typeInto(root.querySelector<HTMLInputElement>('#runIntervalHours')!, '');
    byTestId<HTMLFormElement>(root, 'pipeline-settings-form')!.dispatchEvent(new Event('submit'));
    http.expectOne((r) => r.url === pipelineUrl && r.method === 'PUT').flush({ runAfterSync: false, runIntervalHours: null, nextRunAt: null });
    await settle(harness);
    expect(byTestId(root, 'pipeline-settings-form')).toBeNull();
    expect(text(byTestId(root, 'runs-line'))).toBe('Scripts do not run after a synchronization · No schedule');
  });

  it('lets an analyst read the schedules but not edit them', async () => {
    const { root } = await open('analyst');
    expect(byTestId(root, 'schedule-summary')).not.toBeNull();
    expect(byTestId(root, 'schedule-edit')).toBeNull();
  });

  it('saves the schedule for admins and shows range errors next to the fields', async () => {
    const { root, http, harness } = await open('admin');
    byTestId<HTMLButtonElement>(root, 'schedule-edit')!.click();
    await settle(harness);
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
    // Only the text the worker stored is English; the one translated from its code follows the interface language.
    expect([...table.querySelectorAll('[data-testid="log-error"]')].map((e) => e.getAttribute('lang'))).toEqual([null, 'en']);
    expect(table.textContent).toContain('Full resync');
    expect(table.textContent).toContain('Deletion check');
    expect(table.textContent).toContain('Ada');
    expect(byTestId(root, 'log-cards')?.children.length).toBe(5);
  });

  it('reads the log in four columns, with the end of each run under its start', async () => {
    const { root } = await open('viewer');
    const headings = Array.from(byTestId(root, 'log-table')!.querySelectorAll('th')).map((th) => text(th));
    expect(headings).toEqual(['Started', 'Kind', 'Status', 'Changes']);
    expect(text(byTestId(root, 'log-ended'))).toMatch(/^Ended .*2026/);
    expect(text(byTestId(root, 'log-table')!.querySelector('tbody td:last-child'))).toBe('4 new, 2 updated, 0 deleted');
  });

  it('shows the requester under the kind of a manual run, and nothing for a scheduled one', async () => {
    const items = [sync({ id: 'a', trigger: 'manual', requestedBy: { id: 'u1', name: 'Ada' } }), sync({ id: 'b' })];
    const { root } = await open('viewer', status(), { items, total: 2 });
    const rows = Array.from(byTestId(root, 'log-table')!.querySelectorAll('tbody tr')).map((row) => text(row.children[1]));
    expect(rows[0]).toContain('Started by Ada');
    expect(rows[1]).not.toContain('Started by');
  });

  it('puts the log before the form versions, and only the stored count in large type', async () => {
    const { root } = await open('viewer');
    const log = byTestId(root, 'log-table')!;
    const versions = byTestId(root, 'form-versions')!;
    expect(log.compareDocumentPosition(versions) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(versions.id).toBe('form-versions'); // the target of the link from General
    expect(byTestId(root, 'submission-count')!.className).toContain('text-2xl');
    expect(byTestId(root, 'deleted-count')!.className).not.toContain('text-2xl');
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

    it('marks the stored reason of a version as English only when it is shown raw', async () => {
      const list: FormVersionList = {
        withoutVersion: 0,
        versions: [
          version({ uid: 'vFailedFailedFailedFa2', status: 'failed', errorCode: 'kobo.unreachable', error: 'The Kobo server could not be reached.', deployedAt: null }),
          version({ uid: 'vOtherOtherOtherOther5', status: 'failed', errorCode: 'kobo.something_new', error: 'A message from the worker.', deployedAt: null }),
        ],
      };
      const { root } = await open('viewer', status(), undefined, undefined, list);
      const table = byTestId(root, 'versions-table')!;

      const errors = [...table.querySelectorAll('[data-testid="version-error"]')];
      expect(errors.map((e) => e.textContent?.trim())).toEqual(['The Kobo server could not be reached. Check the address and try again.', 'A message from the worker.']);
      expect(errors.map((e) => e.getAttribute('lang'))).toEqual([null, 'en']);
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
      const { root } = await open('analyst', status(), undefined, undefined, mixed);
      const hint = byTestId(root, 'versions-retry-hint')!;
      expect(hint.textContent).toContain('Re-checking the form retries failed and not found versions.');
      expect(hint.querySelector('a')).toBeNull(); // no round trip to another page: the action is right here
      TestBed.resetTestingModule();

      const healthy = await open('viewer', status(), undefined, undefined, { versions: [version(), version({ uid: 'vOther', status: 'pending' })], withoutVersion: 0 });
      expect(byTestId(healthy.root, 'versions-retry-hint')).toBeNull();
    });

    it('offers an analyst a Re-check button in place, which retries the versions and lists them again', async () => {
      const { root, http, harness } = await open('analyst', status(), undefined, undefined, mixed);
      byTestId<HTMLButtonElement>(root, 'versions-recheck')!.click();
      await settle(harness);

      const request = http.expectOne(`/api/v1/projects/${projectId}/kobo-check`);
      expect(request.request.method).toBe('POST');
      request.flush(details('analyst', { unavailableFormVersions: 0 }));
      await settle(harness);
      http.expectOne(versionsUrl).flush({ versions: [version({ uid: 'vCurrentCurrentCurrent1', current: true })], withoutVersion: 0 });
      await settle(harness);

      expect(byTestId(root, 'version-row-failed')).toBeNull();
      expect(byTestId(root, 'versions-retry-hint')).toBeNull();
    });

    it('tells a viewer whom to ask, without a button or a link', async () => {
      const { root } = await open('viewer', status(), undefined, undefined, mixed);
      const hint = byTestId(root, 'versions-retry-hint')!;
      expect(text(hint)).toContain('Ask an analyst or admin to re-check the form');
      expect(hint.querySelector('a, button')).toBeNull();
    });

    it('explains the labels in the empty state instead of in an introduction', async () => {
      const { root } = await open('viewer');
      expect(text(byTestId(root, 'versions-empty'))).toContain('use the labels of the form version their submission was made with');
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
