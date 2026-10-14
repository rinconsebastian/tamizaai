import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import {
  PIPELINE_POLL_MS,
  PipelineSettings,
  ProjectScript,
  RunPage,
  RunSummary,
  SCRIPT_TEST_POLL_MS,
  ScriptTest,
  ScriptVersionDetails,
  ScriptVersionSummary,
  TestStep,
} from '../../core/projects/pipeline.service';
import { ProjectRole } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
import { CodeEditor } from '../../shared/ui/code-editor';
import { tick } from '../../../testing/cdk';
import { byTestId, openProjectSection, projectId, settle } from '../../../testing/project-harness';

// jsdom has no layout; CodeMirror measures text ranges after edits.
Range.prototype.getClientRects ??= () => [] as unknown as DOMRectList;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();

const scriptsUrl = `/api/v1/projects/${projectId}/scripts`;
const runsUrl = `/api/v1/projects/${projectId}/runs`;
const settingsUrl = `/api/v1/projects/${projectId}/pipeline`;
const ada = { id: 'u1', name: 'Ada' };

function version(number: number, code: string | null = `def clean(raw, ctx):\n    return raw  # v${number}\n`): ScriptVersionDetails {
  return { versionId: `v${number}`, kind: 'clean', number, removed: code === null, code, author: ada, createdAt: '2026-10-02T10:00:00Z' };
}

function summary(v: ScriptVersionDetails): ScriptVersionSummary {
  const { code: _code, ...rest } = v;
  return rest;
}

function run(overrides: Partial<RunSummary> = {}): RunSummary {
  return {
    id: 'r1',
    trigger: 'sync',
    status: 'succeeded',
    requestedBy: null,
    queuedAt: '2026-10-02T10:00:00Z',
    startedAt: '2026-10-02T10:00:01Z',
    finishedAt: '2026-10-02T10:00:09Z',
    versions: { clean: 2, output: null, indicators: 1 },
    steps: [
      { step: 'flatten', status: 'succeeded', inputRows: 3, outputRows: 9 },
      { step: 'clean', status: 'succeeded', inputRows: 9, outputRows: 8 },
      { step: 'output', status: 'skipped', inputRows: null, outputRows: null },
      { step: 'indicators', status: 'succeeded', inputRows: 8, outputRows: 1 },
    ],
    errorCode: null,
    error: null,
    ...overrides,
  };
}

interface Seed {
  scripts?: ProjectScript[];
  versions?: ScriptVersionSummary[];
  settings?: PipelineSettings;
  runs?: RunPage;
  pollMs?: number;
  testPollMs?: number;
}

async function open(role: ProjectRole, seed: Seed = {}) {
  const current = version(2);
  const providers = [
    ...(seed.pollMs ? [{ provide: PIPELINE_POLL_MS, useValue: seed.pollMs }] : []),
    ...(seed.testPollMs ? [{ provide: SCRIPT_TEST_POLL_MS, useValue: seed.testPollMs }] : []),
  ];
  const opened = await openProjectSection('scripts', role, {}, providers);
  const { http, harness } = opened;
  http.expectOne((r) => r.url === settingsUrl).flush(seed.settings ?? { runAfterSync: true, runIntervalHours: null, nextRunAt: null });
  http.expectOne((r) => r.url === runsUrl && r.method === 'GET').flush(seed.runs ?? { items: [run()], total: 1 });
  http
    .expectOne(scriptsUrl)
    .flush(seed.scripts ?? [{ kind: 'clean', current }, { kind: 'output', current: null }, { kind: 'indicators', current: null }]);
  await settle(harness);
  http.expectOne(`${scriptsUrl}/clean/versions`).flush(seed.versions ?? [summary(current), summary(version(1))]);
  await settle(harness);
  const editor = () => harness.fixture.debugElement.query(By.directive(CodeEditor)).componentInstance as CodeEditor;
  return { ...opened, editor };
}

const testsUrl = `/api/v1/projects/${projectId}/script-tests`;
const draftCode = 'def clean(raw, ctx):\n    return raw  # draft\n';

function scriptTest(overrides: Partial<ScriptTest> = {}): ScriptTest {
  return {
    id: 't1',
    kind: 'clean',
    status: 'queued',
    sampleSize: 500,
    code: draftCode,
    waitingFor: null,
    cleanVersion: null,
    queuedAt: '2026-10-03T10:00:00Z',
    startedAt: null,
    finishedAt: null,
    errorCode: null,
    error: null,
    steps: [],
    ...overrides,
  };
}

const flattenStep: TestStep = {
  step: 'flatten',
  status: 'succeeded',
  startedAt: '2026-10-03T10:00:01Z',
  finishedAt: '2026-10-03T10:00:02Z',
  inputRows: 120,
  outputRows: 360,
  tables: [
    {
      name: 'main',
      rows: 120,
      columns: [
        { name: '_id', type: 'integer' },
        { name: 'note', type: 'text' },
      ],
      previewRows: 2,
      preview: [
        [1, 'short'],
        [2, null],
      ],
      cutCells: [],
    },
    {
      name: 'members',
      rows: 240,
      columns: [{ name: 'member_name', type: 'text' }],
      previewRows: 100,
      preview: [['m1']],
      cutCells: [[0, 0]],
    },
  ],
  log: 'Sample: 120 submissions.',
  error: null,
};

async function startTest(opened: Awaited<ReturnType<typeof open>>, size = '500') {
  const { root, harness, editor } = opened;
  editor().replace(draftCode);
  await settle(harness);
  const input = byTestId<HTMLInputElement>(root, 'sample-size')!;
  input.value = size;
  input.dispatchEvent(new Event('input'));
  await settle(harness);
  byTestId<HTMLButtonElement>(root, 'test-on-sample')!.click();
  await settle(harness);
}

function confirmDialog(): HTMLButtonElement | null {
  return document.querySelector('[data-testid="confirm"]');
}

describe('ProjectScripts', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('shows the current script read-only to viewers, without editing actions', async () => {
    const { root, editor } = await open('viewer');

    expect(editor().text()).toContain('# v2');
    expect(root.querySelector('.cm-content')?.getAttribute('contenteditable')).toBe('false');
    for (const id of ['save', 'remove', 'upload', 'template', 'run-now', 'pipeline-settings-form', 'test-on-sample', 'sample-size']) {
      expect(byTestId(root, id)).toBeNull();
    }
    expect(byTestId(root, 'pipeline-settings-read-only')?.textContent).toContain('No schedule');
    expect(byTestId(root, 'script-clean')?.textContent).toContain('v2');
    expect(byTestId(root, 'script-output')?.textContent).toContain('not set');
  });

  it('saves edits as a new version and reloads the history', async () => {
    const { root, http, harness, editor } = await open('analyst');
    expect(byTestId<HTMLButtonElement>(root, 'save')!.disabled).toBe(true);

    editor().replace('def clean(raw, ctx):\n    return raw  # edited\n');
    await settle(harness);
    expect(byTestId(root, 'unsaved')).not.toBeNull();
    byTestId<HTMLButtonElement>(root, 'save')!.click();

    const request = http.expectOne((r) => r.url === `${scriptsUrl}/clean` && r.method === 'PUT');
    expect(request.request.body).toEqual({ code: 'def clean(raw, ctx):\n    return raw  # edited\n' });
    request.flush(version(3, 'def clean(raw, ctx):\n    return raw  # edited\n'));
    await settle(harness);
    http.expectOne(`${scriptsUrl}/clean/versions`).flush([summary(version(3)), summary(version(2)), summary(version(1))]);
    await settle(harness);

    expect(byTestId(root, 'unsaved')).toBeNull();
    expect(byTestId(root, 'script-clean')?.textContent).toContain('v3');
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('Version 3 saved');
  });

  it('inserts the template of each script', async () => {
    const { root, http, harness, editor } = await open('analyst');

    byTestId<HTMLButtonElement>(root, 'template')!.click();
    expect(editor().text()).toContain('def clean(raw, ctx):');
    editor().replace(editor().text()); // keep it unsaved
    byTestId<HTMLButtonElement>(root, 'script-indicators')!.click();
    await settle(harness);
    confirmDialog()!.click();
    await tick();
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/versions`).flush([]);
    await settle(harness);

    expect(editor().text()).toBe('');
    byTestId<HTMLButtonElement>(root, 'template')!.click();
    expect(editor().text()).toContain('def compute_indicators(cleaned, ctx):');
    expect(editor().text()).toContain('denominator');
  });

  it('asks before discarding unsaved edits when switching scripts', async () => {
    const { root, http, harness, editor } = await open('analyst');
    editor().replace('# unsaved');
    await settle(harness);

    byTestId<HTMLButtonElement>(root, 'script-output')!.click();
    await settle(harness);
    expect(document.body.textContent).toContain('Discard unsaved changes?');
    (document.querySelector('.cdk-overlay-container button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
    await tick();
    await settle(harness);

    http.expectNone(`${scriptsUrl}/output/versions`);
    expect(editor().text()).toBe('# unsaved');
    expect(byTestId(root, 'script-clean')?.getAttribute('aria-pressed')).toBe('true');
  });

  it('removes a script only after confirmation', async () => {
    const { root, http, harness } = await open('admin');

    byTestId<HTMLButtonElement>(root, 'remove')!.click();
    await settle(harness);
    http.expectNone((r) => r.method === 'DELETE');
    expect(document.body.textContent).toContain('the flattened tables are published as the clean data');
    confirmDialog()!.click();
    await tick();
    await settle(harness);

    http.expectOne((r) => r.url === `${scriptsUrl}/clean` && r.method === 'DELETE').flush(version(3, null));
    await settle(harness);
    http.expectOne(`${scriptsUrl}/clean/versions`).flush([summary(version(3, null)), summary(version(2))]);
    await settle(harness);
    expect(byTestId(root, 'script-clean')?.textContent).toContain('removed');
    expect(byTestId(root, 'remove')).toBeNull();
  });

  it('shows an older version read-only and restores it', async () => {
    const { root, http, harness, editor } = await open('analyst');

    byTestId<HTMLButtonElement>(root, 'version-1')!.click();
    await settle(harness);
    http.expectOne(`${scriptsUrl}/clean/versions/1`).flush(version(1));
    await settle(harness);

    expect(byTestId(root, 'viewing-version')?.textContent).toContain('Viewing version 1 by Ada');
    expect(editor().text()).toContain('# v1');
    expect(root.querySelector('.cm-content')?.getAttribute('contenteditable')).toBe('false');
    byTestId<HTMLButtonElement>(root, 'restore')!.click();
    http.expectOne((r) => r.url === `${scriptsUrl}/clean/versions/1/restore` && r.method === 'POST').flush(version(3, version(1).code));
    await settle(harness);
    http.expectOne(`${scriptsUrl}/clean/versions`).flush([]);
    await settle(harness);

    expect(byTestId(root, 'viewing-version')).toBeNull();
    expect(editor().text()).toContain('# v1');
    expect(byTestId(root, 'script-clean')?.textContent).toContain('v3');
  });

  it('asks before leaving the section with unsaved edits', async () => {
    const { harness, editor } = await open('analyst');
    editor().replace('# unsaved');
    await settle(harness);

    const navigation = harness.navigateByUrl(`/projects/${projectId}/members`);
    // Not settle(): the pending navigation keeps the app unstable until the dialog is answered.
    for (let i = 0; i < 3; i++) {
      harness.fixture.detectChanges();
      await tick();
    }

    expect(document.body.textContent).toContain('Discard unsaved changes?');
    (document.querySelector('.cdk-overlay-container button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
    await tick();
    await navigation;

    expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/scripts`);
  });

  // 7.3 — runs and settings

  it('queues a run and explains when one is already waiting', async () => {
    const { root, http, harness } = await open('analyst');

    byTestId<HTMLButtonElement>(root, 'run-now')!.click();
    http.expectOne((r) => r.url === runsUrl && r.method === 'POST').flush(run({ status: 'queued' }));
    await settle(harness);
    http.expectOne((r) => r.url === runsUrl && r.method === 'GET').flush({ items: [run({ status: 'queued' })], total: 1 });
    await settle(harness);
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toBe('Run queued. It starts within seconds.');

    byTestId<HTMLButtonElement>(root, 'run-now')!.click();
    http
      .expectOne((r) => r.url === runsUrl && r.method === 'POST')
      .flush({ title: 'Conflict', detail: 'x', code: 'run.already_queued' }, { status: 409, statusText: 'Conflict' });
    await settle(harness);
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toBe('A run of this project is already waiting to start.');
  });

  it('renders each run status with its translated error', async () => {
    const runs = [
      run({ id: 'a', status: 'queued', startedAt: null, finishedAt: null, steps: [] }),
      run({ id: 'b', status: 'running', finishedAt: null }),
      run({ id: 'c', status: 'failed', errorCode: 'step.clean', error: "KeyError: 'age'" }),
      run({ id: 'd', status: 'failed', errorCode: 'run.interrupted', error: 'The run was interrupted.' }),
      run({ id: 'e' }),
    ];
    const { root } = await open('viewer', { runs: { items: runs, total: 5 } });
    const table = byTestId(root, 'run-table')!;

    for (const status of ['queued', 'running', 'failed', 'succeeded']) {
      expect(byTestId(table, `run-row-${status}`)).not.toBeNull();
    }
    const errors = [...table.querySelectorAll('[data-testid="run-error"]')].map((e) => e.textContent?.trim());
    expect(errors).toEqual([
      "Script 1 (clean) failed: KeyError: 'age'",
      'The run was interrupted because the analytics worker stopped. The next run proceeds normally.',
    ]);
    expect(table.textContent).toContain('v2 / — / v1');
    expect(table.textContent).toContain('8.0 s');
    expect(byTestId(root, 'run-cards')?.children.length).toBe(5);
  });

  it('opens the details of a run with each step', async () => {
    const failed = run({ id: 'c', status: 'failed', errorCode: 'step.clean', error: "KeyError: 'age'" });
    const { root, http, harness } = await open('viewer', { runs: { items: [failed], total: 1 } });

    byTestId<HTMLButtonElement>(root, 'run-details-c')!.click();
    await settle(harness);
    http.expectOne(`${runsUrl}/c`).flush({
      run: failed,
      steps: [
        { step: 'flatten', status: 'succeeded', startedAt: '2026-10-02T10:00:01Z', finishedAt: '2026-10-02T10:00:03Z', inputRows: 3, outputRows: 9, tables: [{ name: 'main', rows: 3 }, { name: 'members', rows: 6 }], log: 'Flattened 3 submissions', error: null },
        { step: 'clean', status: 'failed', startedAt: null, finishedAt: null, inputRows: null, outputRows: null, tables: [], log: 'INFO starting', error: "KeyError: 'age'\n\nTraceback..." },
        { step: 'output', status: 'skipped', startedAt: null, finishedAt: null, inputRows: null, outputRows: null, tables: [], log: 'Not run: the clean step failed.', error: null },
      ],
      referenceTables: [{ name: 'population', rows: 1123, updatedAt: '2026-10-01T12:00:00Z' }],
    });
    await settle(harness);

    const dialog = document.querySelector('.cdk-overlay-container')!;
    expect(byTestId(dialog, 'step-flatten')?.textContent).toContain('2.0 s');
    expect(byTestId(dialog, 'step-flatten')?.textContent).toContain('3 rows in, 9 out');
    expect(byTestId(byTestId(dialog, 'step-flatten')!, 'step-tables')?.textContent).toContain('members (6)');
    expect(byTestId(byTestId(dialog, 'step-clean')!, 'step-error')?.textContent).toContain("KeyError: 'age'");
    expect(byTestId(dialog, 'step-output')?.textContent).toContain('Skipped');
    expect(byTestId(dialog, 'run-reference')?.textContent).toContain('population (1123 rows, updated Oct 1, 2026');
  });

  it('says when a run used no reference tables', async () => {
    const { root, http, harness } = await open('viewer');

    byTestId<HTMLButtonElement>(root, 'run-details-r1')!.click();
    await settle(harness);
    http.expectOne(`${runsUrl}/r1`).flush({ run: run(), steps: [], referenceTables: [] });
    await settle(harness);

    expect(byTestId(document.querySelector('.cdk-overlay-container')!, 'run-reference')?.textContent).toContain('This run used no reference tables.');
  });

  it('polls while a run is active and stops when the page is destroyed', async () => {
    const { http, harness } = await open('viewer', { runs: { items: [run({ status: 'running' })], total: 1 }, pollMs: 30 });

    await new Promise((resolve) => setTimeout(resolve, 60));
    http.expectOne((r) => r.url === runsUrl && r.method === 'GET').flush({ items: [run({ status: 'running' })], total: 1 });
    await settle(harness);
    harness.fixture.destroy();
    await new Promise((resolve) => setTimeout(resolve, 60));

    http.expectNone((r) => r.url === runsUrl);
  });

  it('stops polling when no run is active', async () => {
    const { http } = await open('viewer', { runs: { items: [run()], total: 1 }, pollMs: 30 });

    await new Promise((resolve) => setTimeout(resolve, 60));

    http.expectNone((r) => r.url === runsUrl);
  });

  it('saves the settings for admins and shows field errors', async () => {
    const { root, http, harness } = await open('admin');
    const form = byTestId<HTMLFormElement>(root, 'pipeline-settings-form')!;
    (root.querySelector('#runIntervalHours') as HTMLInputElement).value = '500';
    byTestId<HTMLInputElement>(root, 'run-after-sync')!.checked = false;

    form.dispatchEvent(new Event('submit'));
    const invalid = http.expectOne((r) => r.url === settingsUrl && r.method === 'PUT');
    expect(invalid.request.body).toEqual({ runAfterSync: false, runIntervalHours: 500 });
    invalid.flush({ errors: { runIntervalHours: ['Enter a whole number of hours from 1 to 168, or no schedule.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(harness);
    expect(byTestId(root, 'field-error')?.textContent).toContain('1 to 168');

    (root.querySelector('#runIntervalHours') as HTMLInputElement).value = '';
    form.dispatchEvent(new Event('submit'));
    const cleared = http.expectOne((r) => r.url === settingsUrl && r.method === 'PUT');
    expect(cleared.request.body).toEqual({ runAfterSync: false, runIntervalHours: null });
    cleared.flush({ runAfterSync: false, runIntervalHours: null, nextRunAt: null });
    await settle(harness);
    expect(byTestId(root, 'field-error')).toBeNull();
  });

  // 6.1 — tests on a sample

  it('tests the draft on a sample and follows the test until it ends', async () => {
    const opened = await open('analyst', { testPollMs: 20 });
    const { root, http, harness } = opened;

    await startTest(opened);
    const request = http.expectOne((r) => r.url === testsUrl && r.method === 'POST');
    expect(request.request.body).toEqual({ kind: 'clean', code: draftCode, sampleSize: 500 });
    request.flush(scriptTest({ waitingFor: 'run' }));
    await settle(harness);
    expect(byTestId(root, 'test-progress')?.textContent).toContain('Waiting for a run to finish');
    expect(byTestId(root, 'unsaved')).not.toBeNull(); // nothing was saved

    await new Promise((resolve) => setTimeout(resolve, 40));
    http.expectOne(`${testsUrl}/t1`).flush(scriptTest({ status: 'running', startedAt: '2026-10-03T10:00:01Z', steps: [flattenStep] }));
    await settle(harness);
    expect(byTestId(root, 'test-progress')?.textContent).toContain('Running');
    expect(byTestId(root, 'test-step-flatten')?.textContent).toContain('120 rows in, 360 out');

    await new Promise((resolve) => setTimeout(resolve, 40));
    const clean: TestStep = {
      ...flattenStep,
      step: 'clean',
      inputRows: 360,
      outputRows: 300,
      tables: [{ ...flattenStep.tables[0], rows: 100 }],
      log: 'INFO Dropped 20 test submissions',
    };
    http.expectOne(`${testsUrl}/t1`).flush(scriptTest({ status: 'succeeded', finishedAt: '2026-10-03T10:00:04Z', steps: [flattenStep, clean] }));
    await settle(harness);

    expect(byTestId(root, 'test-status')?.textContent).toContain('Succeeded');
    expect(byTestId(root, 'test-progress')).toBeNull();
    const step = byTestId(root, 'test-step-clean')!;
    expect(step.textContent).toContain('1. Clean (draft)');
    expect(byTestId(step, 'test-preview-note')?.textContent).toContain('First 2 of 100 rows');
    expect([...step.querySelectorAll('[data-testid="column-type"]')].map((e) => e.textContent?.trim())).toEqual(['integer', 'text']);
    expect(step.querySelector('tbody')?.textContent).toContain('short');
    expect(byTestId(step, 'test-step-log')?.textContent).toContain('Dropped 20 test submissions');
    expect(byTestId(root, 'test-stale')).toBeNull();

    await new Promise((resolve) => setTimeout(resolve, 40));
    http.expectNone(`${testsUrl}/t1`);
  });

  it('shows a failed step with its traceback and lets the earlier tables be browsed', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await startTest(opened);

    const failed: TestStep = {
      ...flattenStep,
      step: 'clean',
      status: 'failed',
      tables: [],
      log: '',
      error: "KeyError: 'age'\n\nTraceback (most recent call last):\n  File \"script.py\", line 2, in clean",
    };
    http
      .expectOne((r) => r.url === testsUrl && r.method === 'POST')
      .flush(scriptTest({ status: 'failed', errorCode: 'step.clean', error: "KeyError: 'age'", steps: [flattenStep, failed] }));
    await settle(harness);

    expect(byTestId(root, 'test-error')?.textContent).toContain("Script 1 (clean) failed: KeyError: 'age'");
    expect(byTestId(byTestId(root, 'test-step-clean')!, 'test-step-error')?.textContent).toContain('line 2, in clean');
    const flatten = byTestId(root, 'test-step-flatten')!;
    const select = byTestId<HTMLSelectElement>(flatten, 'test-table-select-flatten')!;
    expect([...select.options].map((o) => o.textContent?.trim())).toEqual(['main (120 rows)', 'members (240 rows)']);
    select.value = 'members';
    select.dispatchEvent(new Event('change'));
    await settle(harness);
    expect(flatten.querySelector('tbody')?.textContent).toContain('m1');
    expect(flatten.querySelector('tbody')?.textContent).toContain('cut at 500 characters');
  });

  it('shows the question label of a column as a cut line with the full text in its title, as text', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await startTest(opened);

    const water = 'Does the household have running water?';
    const long = 'How many people who usually live in this household were away for more than six months during the last year? '.repeat(4).trim();
    const markup = '<b>x</b> **bold** [link](http://example.org)';
    const labeled: TestStep = {
      ...flattenStep,
      tables: [
        {
          ...flattenStep.tables[0],
          columns: [
            { name: '_id', type: 'integer', label: null },
            { name: 'has_water', type: 'text', label: water },
            { name: 'away', type: 'text', label: long },
            { name: 'markup', type: 'text', label: markup },
            { name: 'empty', type: 'text', label: '' },
            { name: 'old', type: 'text' },
          ],
          preview: [[1, 'yes', 'no', 'a', 'b', 'c']],
          previewRows: 1,
          cutCells: [],
        },
      ],
    };
    http
      .expectOne((r) => r.url === testsUrl && r.method === 'POST')
      .flush(scriptTest({ status: 'succeeded', steps: [labeled] }));
    await settle(harness);

    const headers = [...byTestId(root, 'test-step-flatten')!.querySelectorAll('thead th')];
    expect(headers.length).toBe(6);
    const labelOf = (th: Element) => th.querySelector<HTMLElement>('[data-testid="column-label"]');

    const have = labelOf(headers[1])!;
    expect(have.textContent?.trim()).toBe(water);
    expect(have.getAttribute('title')).toBe(water);
    expect(have.classList.contains('truncate')).toBe(true);
    expect(have.classList.contains('max-w-xs')).toBe(true);
    // The label sits between the name and the type.
    expect(have.nextElementSibling?.getAttribute('data-testid')).toBe('column-type');

    expect(labelOf(headers[2])!.getAttribute('title')).toBe(long);
    expect(labelOf(headers[2])!.textContent?.trim()).toBe(long);

    const literal = labelOf(headers[3])!;
    expect(literal.textContent?.trim()).toBe(markup);
    expect(literal.getAttribute('title')).toBe(markup);
    expect(literal.querySelector('b, a')).toBeNull();

    // No line at all for a null, empty or missing label; the type line stays.
    for (const index of [0, 4, 5]) {
      expect(labelOf(headers[index])).toBeNull();
      expect(headers[index].querySelector('[data-testid="column-type"]')).not.toBeNull();
    }
    expect(root.querySelectorAll('[data-testid="column-label"]').length).toBe(3);
  });

  it('names the saved script 1 a test of script 2 ran first, and says when the editor changed', async () => {
    const opened = await open('analyst');
    const { root, http, harness, editor } = opened;
    await startTest(opened);

    const savedClean: TestStep = { ...flattenStep, step: 'clean' };
    const output: TestStep = { ...flattenStep, step: 'output', tables: [{ ...flattenStep.tables[0], name: 'households', columns: null, preview: null, previewRows: 0, cutCells: null }] };
    http
      .expectOne((r) => r.url === testsUrl && r.method === 'POST')
      .flush(scriptTest({ kind: 'output', status: 'succeeded', cleanVersion: 3, steps: [flattenStep, savedClean, output] }));
    await settle(harness);

    expect(byTestId(root, 'test-step-clean')?.textContent).toContain('Clean (saved version 3)');
    expect(byTestId(root, 'test-step-output')?.textContent).toContain('2. Output (draft)');
    expect(byTestId(root, 'test-no-preview')?.textContent).toContain('Only the first 20 tables');

    editor().replace(draftCode + '# more\n');
    await settle(harness);
    expect(byTestId(root, 'test-stale')?.textContent).toContain('The editor has changed since this test');
  });

  it('says when a queued test waits for another test', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await startTest(opened);

    http.expectOne((r) => r.url === testsUrl && r.method === 'POST').flush(scriptTest({ waitingFor: 'test' }));
    await settle(harness);

    expect(byTestId(root, 'test-progress')?.textContent).toContain('Waiting for another test');
  });

  it('checks the sample size before asking the API', async () => {
    const opened = await open('analyst');
    const { root, http } = opened;

    await startTest(opened, '10000');

    http.expectNone((r) => r.url === testsUrl);
    expect(byTestId(root, 'field-error')?.textContent).toContain('from 1 to 5,000');
  });
});
