import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { EditorView } from '@codemirror/view';
import { vi } from 'vitest';
import { Downloader } from '../../core/download.service';
import {
  PIPELINE_POLL_MS,
  IndicatorScript,
  PipelineSettings,
  RunDetails,
  RunPage,
  RunSummary,
  SCRIPT_TEST_POLL_MS,
  SampleDownload,
  SampleDownloadContents,
  ScriptInputs,
  ScriptList,
  ScriptTest,
  ShownPreview,
  ScriptVersionDetails,
  ScriptVersionSummary,
  TestStep,
} from '../../core/projects/pipeline.service';
import { ProjectRole } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
import { CodeEditor } from '../../shared/ui/code-editor';
import { pressKey, provideVisibleElements, tick } from '../../../testing/cdk';
import { byTestId, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';

// jsdom has no layout; CodeMirror measures text ranges after edits.
Range.prototype.getClientRects ??= () => [] as unknown as DOMRectList;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();

const scriptsUrl = `/api/v1/projects/${projectId}/scripts`;
const runsUrl = `/api/v1/projects/${projectId}/runs`;
const settingsUrl = `/api/v1/projects/${projectId}/pipeline`;
const inputsUrl = `/api/v1/projects/${projectId}/script-inputs`;
const ada = { id: 'u1', name: 'Ada' };

function version(number: number, code: string | null = `def clean(raw, ctx):\n    return raw  # v${number}\n`): ScriptVersionDetails {
  return {
    versionId: `v${number}`,
    kind: 'clean',
    name: null,
    number,
    removed: code === null,
    code,
    label: null,
    description: null,
    unit: null,
    format: null,
    author: ada,
    createdAt: '2026-10-02T10:00:00Z',
  };
}

/** A version of the indicator `name`; its metadata is a label, a description, the unit households and the count format. */
function indicatorVersion(name: string, number: number, overrides: Partial<ScriptVersionDetails> = {}): ScriptVersionDetails {
  const details: ScriptVersionDetails = {
    ...version(number, `def compute(cleaned, ctx):\n    return None  # ${name} v${number}\n`),
    versionId: `${name}-v${number}`,
    kind: 'indicator',
    name,
    label: `Label of ${name}`,
    description: 'What it measures',
    unit: 'households',
    format: 'count',
    ...overrides,
  };
  return { ...details, removed: details.code === null };
}

function indicator(current: ScriptVersionDetails): IndicatorScript {
  return {
    name: current.name!,
    label: current.label!,
    description: current.description,
    unit: current.unit,
    format: current.format!,
    current,
  };
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
    versions: { clean: 2, output: null, indicators: [{ name: 'coverage', number: 1 }] },
    steps: [
      { step: 'flatten', name: '', status: 'succeeded', inputRows: 3, outputRows: 9 },
      { step: 'clean', name: '', status: 'succeeded', inputRows: 9, outputRows: 8 },
      { step: 'output', name: '', status: 'skipped', inputRows: null, outputRows: null },
      { step: 'indicator', name: 'coverage', status: 'succeeded', inputRows: 8, outputRows: 1 },
    ],
    errorCode: null,
    error: null,
    ...overrides,
  };
}

/** The flattened tables of the last run: a main table with a labeled question, a repeat table and a reference table. */
function scriptInputs(overrides: Partial<ScriptInputs> = {}): ScriptInputs {
  return {
    source: 'raw',
    publishedAt: '2026-10-14T10:00:00Z',
    runId: 'r1',
    tables: [
      {
        name: 'main',
        rows: 480,
        columns: [
          { name: 'municipality', type: 'text', label: 'Municipality' },
          { name: 'has_water', type: 'text', label: 'Does the household have running water?' },
        ],
      },
      { name: 'members', rows: 1200, columns: [{ name: 'age', type: 'integer', label: null }] },
    ],
    referenceTables: [{ name: 'population', columns: [{ name: 'population_2026', type: 'integer' }] }],
    questions: [{ name: 'municipality', path: 'municipality', type: 'select_one', label: 'Municipality' }],
    ...overrides,
  };
}

interface Seed {
  inputs?: ScriptInputs;
  scripts?: ScriptList;
  versions?: ScriptVersionSummary[];
  settings?: PipelineSettings;
  runs?: RunPage;
  pollMs?: number;
  testPollMs?: number;
}

const downloader = { start: vi.fn() };

async function open(role: ProjectRole, seed: Seed = {}) {
  const current = version(2);
  const providers = [
    provideVisibleElements(),
    { provide: Downloader, useValue: downloader },
    ...(seed.pollMs ? [{ provide: PIPELINE_POLL_MS, useValue: seed.pollMs }] : []),
    ...(seed.testPollMs ? [{ provide: SCRIPT_TEST_POLL_MS, useValue: seed.testPollMs }] : []),
  ];
  const opened = await openProjectSection('scripts', role, {}, providers);
  const { http, harness } = opened;
  http.expectOne((r) => r.url === settingsUrl).flush(seed.settings ?? { runAfterSync: true, runIntervalHours: null, nextRunAt: null });
  http.expectOne((r) => r.url === runsUrl && r.method === 'GET').flush(seed.runs ?? { items: [run()], total: 1 });
  http
    .expectOne(scriptsUrl)
    .flush(seed.scripts ?? { clean: { kind: 'clean', current }, output: { kind: 'output', current: null }, indicators: [] });
  http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'clean').flush(seed.inputs ?? scriptInputs());
  await settle(harness);
  http.expectOne(`${scriptsUrl}/clean/versions`).flush(seed.versions ?? [summary(current), summary(version(1))]);
  await settle(harness);
  const editor = () => harness.fixture.debugElement.query(By.directive(CodeEditor)).componentInstance as CodeEditor;
  return { ...opened, editor };
}

/** The scripts as the API lists them: script 1 at version 2, no script 2 and the given indicators, ordered by name. */
function withIndicators(...indicators: ScriptVersionDetails[]): ScriptList {
  return { clean: { kind: 'clean', current: version(2) }, output: { kind: 'output', current: null }, indicators: indicators.map(indicator) };
}

/** Each entry of the indicator list as "label name version". */
function indicatorEntries(root: ParentNode): string[] {
  return [...byTestId(root, 'indicator-list')!.querySelectorAll('li')].map((li) =>
    [...li.querySelectorAll('span.truncate, code, tmz-badge')].map((e) => e.textContent!.trim()).join(' '),
  );
}

/** Selects an indicator and answers the request for its versions. */
async function selectIndicator(opened: Awaited<ReturnType<typeof open>>, current: ScriptVersionDetails, versions = [summary(current)]) {
  byTestId<HTMLButtonElement>(opened.root, `indicator-${current.name}`)!.click();
  await settle(opened.harness);
  opened.http.expectOne(`${scriptsUrl}/indicators/${current.name}/versions`).flush(versions);
  await settle(opened.harness);
}

const testsUrl = `/api/v1/projects/${projectId}/script-tests`;
const draftCode = 'def clean(raw, ctx):\n    return raw  # draft\n';

function scriptTest(overrides: Partial<ScriptTest> = {}): ScriptTest {
  return {
    id: 't1',
    kind: 'clean',
    name: null,
    status: 'queued',
    sampleSize: 500,
    stopAtLine: null,
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
  reused: false,
  stoppedAtLine: null,
  shown: null,
  dashboard: null,
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

const downloadsUrl = `/api/v1/projects/${projectId}/sample-downloads`;

function sampleDownload(overrides: Partial<SampleDownload> = {}): SampleDownload {
  return {
    id: 'd1',
    status: 'queued',
    sampleSize: 500,
    waitingFor: null,
    queuedAt: '2026-10-03T10:00:00Z',
    startedAt: null,
    finishedAt: null,
    expiresAt: null,
    sizeBytes: null,
    contents: null,
    errorCode: null,
    error: null,
    ...overrides,
  };
}

const downloadContents: SampleDownloadContents = {
  sample: { requested: 500, submissions: 320 },
  raw: {
    tables: [
      { name: 'main', rows: 320 },
      { name: 'members', rows: 871 },
    ],
  },
  clean: { status: 'succeeded', version: 2, error: null, tables: [{ name: 'main', rows: 318 }] },
  reference: [{ name: 'population', rows: 42, updatedAt: '2026-10-02T10:00:00Z' }],
};

/** A download the worker finished: the zip is 12.3 MB and available for an hour. */
function readyDownload(overrides: Partial<SampleDownload> = {}, contents: Partial<SampleDownloadContents> = {}): SampleDownload {
  return sampleDownload({
    status: 'succeeded',
    startedAt: '2026-10-03T10:00:01Z',
    finishedAt: '2026-10-03T10:00:09Z',
    expiresAt: '2026-10-03T11:00:09Z',
    sizeBytes: 12_900_000,
    contents: { ...downloadContents, ...contents },
    ...overrides,
  });
}

/** Types the sample size and clicks "Download sample". */
async function requestDownload(opened: Awaited<ReturnType<typeof open>>, size = '300') {
  const input = byTestId<HTMLInputElement>(opened.root, 'sample-size')!;
  input.value = size;
  input.dispatchEvent(new Event('input'));
  await settle(opened.harness);
  byTestId<HTMLButtonElement>(opened.root, 'download-sample')!.click();
  await settle(opened.harness);
}

describe('ProjectScripts', () => {
  beforeEach(() => downloader.start.mockClear());
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

  /** Opens the template menu and chooses a template. */
  async function chooseTemplate(opened: Awaited<ReturnType<typeof open>>, id: string) {
    byTestId<HTMLButtonElement>(opened.root, 'template')!.click();
    await settle(opened.harness);
    await tick();
    (document.querySelector(`[data-testid="template-${id}"]`) as HTMLButtonElement).click();
    await settle(opened.harness);
    await tick();
  }

  it('inserts a template of script 1 and of an indicator', async () => {
    const coverage = indicatorVersion('coverage', 1);
    const opened = await open('analyst', { scripts: withIndicators(coverage) });
    const { root, http, harness, editor } = opened;

    await chooseTemplate(opened, 'drop-test-submissions');
    expect(editor().text()).toContain('def clean(raw, ctx):');
    expect(editor().text()).toContain('# %% Drop test submissions and duplicates');
    byTestId<HTMLButtonElement>(root, 'indicator-coverage')!.click();
    await settle(harness);
    confirmDialog()!.click();
    await tick();
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/coverage/versions`).flush([summary(coverage)]);
    await settle(harness);

    expect(editor().text()).toContain('# coverage v1');
    await chooseTemplate(opened, 'rate-by-group');
    expect(editor().text()).toContain('def compute(cleaned, ctx):');
    expect(editor().text()).toContain('denominator');
    expect(editor().text()).not.toContain('compute_indicators');
  });

  it('lists each template of an indicator with its name and description', async () => {
    const coverage = indicatorVersion('coverage', 1);
    const opened = await open('analyst', { scripts: withIndicators(coverage) });
    await selectIndicator(opened, coverage);

    byTestId<HTMLButtonElement>(opened.root, 'template')!.click();
    await settle(opened.harness);
    await tick();

    const items = [...document.querySelectorAll('[data-testid="template-menu"] [role="menuitem"]')].map((item) =>
      [...item.querySelectorAll('span')].map((span) => span.textContent!.trim()).join(' '),
    );
    expect(items).toEqual([
      'Count by group How many submissions there are in each group.',
      'Rate by group The share that meets a condition in each group, with numerator and denominator.',
      'Monthly series Submissions per month, for a line chart.',
    ]);
  });

  it('offers the templates of script 2 on script 2', async () => {
    const opened = await open('analyst');
    byTestId<HTMLButtonElement>(opened.root, 'script-output')!.click();
    await settle(opened.harness);
    opened.http.expectOne(`${scriptsUrl}/output/versions`).flush([]);
    await settle(opened.harness);

    byTestId<HTMLButtonElement>(opened.root, 'template')!.click();
    await settle(opened.harness);
    await tick();

    expect([...document.querySelectorAll('[data-testid="template-menu"] [role="menuitem"] .font-medium')].map((n) => n.textContent)).toEqual([
      'Columns for the dashboard',
      'Join a repeat group to main',
    ]);
  });

  it('keeps unsaved edits when the replacement is not confirmed, and replaces them when it is', async () => {
    const opened = await open('analyst');
    const { harness, editor } = opened;
    editor().replace('# unsaved');
    await settle(harness);

    await chooseTemplate(opened, 'fix-types-and-recode');
    expect(document.body.textContent).toContain('Replace the code?');
    (document.querySelector('.cdk-overlay-container button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    expect(editor().text()).toBe('# unsaved');

    await chooseTemplate(opened, 'fix-types-and-recode');
    confirmDialog()!.click();
    await tick();
    await settle(harness);
    expect(editor().text()).toContain('# Script 1: give columns their real types');
  });

  it('replaces the saved code without asking, as one edit that can be undone', async () => {
    const opened = await open('analyst');
    const { harness, editor } = opened;
    const saved = editor().text();

    await chooseTemplate(opened, 'drop-test-submissions');
    expect(document.body.textContent).not.toContain('Replace the code?');
    expect(editor().text()).not.toBe(saved);

    const content = document.querySelector('.cm-content') as HTMLElement;
    content.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, cancelable: true }));
    await settle(harness);

    expect(editor().text()).toBe(saved);
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
    expect(table.textContent).toContain('v2 / — / 1 indicator');
    expect(byTestId(table, 'run-indicators')?.textContent).toContain('Indicators 1/1');
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
        { step: 'flatten', name: '', label: null, status: 'succeeded', startedAt: '2026-10-02T10:00:01Z', finishedAt: '2026-10-02T10:00:03Z', inputRows: 3, outputRows: 9, tables: [{ name: 'main', rows: 3 }, { name: 'members', rows: 6 }], log: 'Flattened 3 submissions', error: null },
        { step: 'clean', name: '', label: null, status: 'failed', startedAt: null, finishedAt: null, inputRows: null, outputRows: null, tables: [], log: 'INFO starting', error: "KeyError: 'age'\n\nTraceback..." },
        { step: 'output', name: '', label: null, status: 'skipped', startedAt: null, finishedAt: null, inputRows: null, outputRows: null, tables: [], log: 'Not run: the clean step failed.', error: null },
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

  // Indicator scripts

  it('lists the indicators with their label, name and version, read-only to viewers', async () => {
    const attendance = indicatorVersion('attendance', 1, { label: 'Attendance' });
    const coverage = indicatorVersion('coverage', 3);
    const opened = await open('viewer', { scripts: withIndicators(attendance, coverage) });
    const { root } = opened;

    const entries = indicatorEntries(root);
    expect(entries).toEqual(['Attendance attendance v1', 'Label of coverage coverage v3']);
    expect(byTestId(root, 'indicator-count')?.textContent).toContain('2');
    expect(byTestId(root, 'add-indicator')).toBeNull();

    await selectIndicator(opened, coverage);
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.value).toBe('Label of coverage');
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.readOnly).toBe(true);
    expect(byTestId<HTMLTextAreaElement>(root, 'meta-description')!.value).toBe('What it measures');
    expect(byTestId<HTMLInputElement>(root, 'meta-unit')!.value).toBe('households');
    expect(byTestId<HTMLSelectElement>(root, 'meta-format')!.value).toBe('count');
    expect(byTestId<HTMLSelectElement>(root, 'meta-format')!.disabled).toBe(true);
    for (const id of ['save', 'remove', 'template', 'test-on-sample']) {
      expect(byTestId(root, id)).toBeNull();
    }
  });

  it('says so when the project has no indicators', async () => {
    const { root } = await open('analyst');

    expect(byTestId(root, 'no-indicators')?.textContent).toContain('no indicators yet');
    expect(byTestId(root, 'indicator-meta')).toBeNull();
  });

  it('creates an indicator from the add dialog, checking the name as it is typed, and saves it with its metadata', async () => {
    const opened = await open('analyst', { scripts: withIndicators(indicatorVersion('coverage', 1)) });
    const { root, http, harness, editor } = opened;

    byTestId<HTMLButtonElement>(root, 'add-indicator')!.click();
    await settle(harness);
    const dialog = document.querySelector('.cdk-overlay-container')!;
    const name = byTestId<HTMLInputElement>(dialog, 'indicator-name')!;

    typeInto(name, 'Piped Water');
    await settle(harness);
    expect(byTestId(dialog, 'field-error')?.textContent).toContain('lowercase letters, digits and underscores');
    typeInto(name, '1piped');
    await settle(harness);
    expect(byTestId(dialog, 'field-error')?.textContent).toContain('starting with a letter');
    typeInto(name, 'coverage');
    await settle(harness);
    expect(byTestId(dialog, 'field-error')?.textContent).toContain('already has an indicator');
    typeInto(name, 'piped_water_rate');
    await settle(harness);
    expect(byTestId(dialog, 'field-error')).toBeNull();

    byTestId<HTMLButtonElement>(dialog, 'indicator-add-confirm')!.click();
    await settle(harness);
    expect(byTestId(dialog, 'field-error')?.textContent).toContain('Enter a label');

    typeInto(byTestId<HTMLInputElement>(dialog, 'meta-label')!, 'Households with piped water');
    await settle(harness);
    typeInto(byTestId<HTMLSelectElement>(dialog, 'meta-format')!, 'percent');
    await settle(harness);
    expect(dialog.textContent).toContain('0.42 is shown as 42%');
    byTestId<HTMLButtonElement>(dialog, 'indicator-add-confirm')!.click();
    await tick();
    await settle(harness);

    // Nothing exists until the script is saved: no request yet, and the form is unsaved.
    http.expectNone((r) => r.url.includes('/piped_water_rate'));
    expect(byTestId(root, 'indicator-piped_water_rate')?.textContent).toContain('new');
    expect(byTestId(root, 'indicator-meta-name')?.textContent).toBe('piped_water_rate');
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.value).toBe('Households with piped water');
    expect(byTestId<HTMLSelectElement>(root, 'meta-format')!.value).toBe('percent');
    expect(editor().text()).toContain('def compute(cleaned, ctx):');
    expect(byTestId(root, 'unsaved')).not.toBeNull();
    expect(byTestId(root, 'remove')).toBeNull();

    const code = editor().text();
    byTestId<HTMLButtonElement>(root, 'save')!.click();
    const request = http.expectOne((r) => r.url === `${scriptsUrl}/indicators/piped_water_rate` && r.method === 'PUT');
    expect(request.request.body).toEqual({ code, label: 'Households with piped water', description: '', unit: '', format: 'percent' });
    request.flush(indicatorVersion('piped_water_rate', 1, { code, label: 'Households with piped water', description: null, unit: null, format: 'percent' }));
    await settle(harness);
    const created = indicatorVersion('piped_water_rate', 1);
    http.expectOne(`${scriptsUrl}/indicators/piped_water_rate/versions`).flush([summary(created)]);
    await settle(harness);

    const entries = indicatorEntries(root);
    expect(entries).toEqual(['Label of coverage coverage v1', 'Households with piped water piped_water_rate v1']);
    expect(byTestId(root, 'unsaved')).toBeNull();
    expect(byTestId(root, 'remove')).not.toBeNull();
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('Version 1 saved');
  });

  it('marks the form unsaved when only the metadata changes, and saves the metadata with the code', async () => {
    const coverage = indicatorVersion('coverage', 2);
    const opened = await open('analyst', { scripts: withIndicators(coverage) });
    const { root, http, harness } = opened;
    await selectIndicator(opened, coverage);
    expect(byTestId<HTMLButtonElement>(root, 'save')!.disabled).toBe(true);
    expect(byTestId(root, 'unsaved')).toBeNull();

    typeInto(byTestId<HTMLInputElement>(root, 'meta-label')!, 'Coverage of piped water');
    await settle(harness);
    expect(byTestId(root, 'unsaved')).not.toBeNull();
    expect(byTestId<HTMLButtonElement>(root, 'save')!.disabled).toBe(false);

    // Back to what is saved: nothing to save.
    typeInto(byTestId<HTMLInputElement>(root, 'meta-label')!, 'Label of coverage');
    await settle(harness);
    expect(byTestId(root, 'unsaved')).toBeNull();

    typeInto(byTestId<HTMLInputElement>(root, 'meta-unit')!, '%');
    await settle(harness);
    typeInto(byTestId<HTMLSelectElement>(root, 'meta-format')!, 'percent');
    await settle(harness);
    expect(byTestId(root, 'unsaved')).not.toBeNull();

    // The API's message for a field is shown next to it.
    typeInto(byTestId<HTMLInputElement>(root, 'meta-label')!, '   ');
    await settle(harness);
    byTestId<HTMLButtonElement>(root, 'save')!.click();
    http
      .expectOne((r) => r.url === `${scriptsUrl}/indicators/coverage` && r.method === 'PUT')
      .flush({ errors: { label: ['Enter a label of 1 to 120 characters.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(harness);
    expect(byTestId(byTestId(root, 'indicator-meta')!, 'field-error')?.textContent).toContain('1 to 120 characters');
    expect(byTestId(root, 'save-error')).toBeNull();

    typeInto(byTestId<HTMLInputElement>(root, 'meta-label')!, 'Coverage of piped water');
    await settle(harness);
    byTestId<HTMLButtonElement>(root, 'save')!.click();
    const request = http.expectOne((r) => r.url === `${scriptsUrl}/indicators/coverage` && r.method === 'PUT');
    expect(request.request.body).toEqual({
      code: coverage.code,
      label: 'Coverage of piped water',
      description: 'What it measures',
      unit: '%',
      format: 'percent',
    });
    request.flush(indicatorVersion('coverage', 3, { label: 'Coverage of piped water', unit: '%', format: 'percent' }));
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/coverage/versions`).flush([]);
    await settle(harness);

    expect(byTestId(root, 'indicator-coverage')?.textContent).toContain('Coverage of piped water');
    expect(byTestId(root, 'indicator-coverage')?.textContent).toContain('v3');
    expect(byTestId(root, 'unsaved')).toBeNull();
    expect(byTestId(root, 'field-error')).toBeNull();
  });

  it('asks before leaving an indicator with unsaved changes and shows the other indicator when confirmed', async () => {
    const attendance = indicatorVersion('attendance', 1, { label: 'Attendance', unit: 'people' });
    const coverage = indicatorVersion('coverage', 2);
    const opened = await open('analyst', { scripts: withIndicators(attendance, coverage) });
    const { root, http, harness, editor } = opened;
    await selectIndicator(opened, coverage);
    typeInto(byTestId<HTMLInputElement>(root, 'meta-label')!, 'Changed label');
    await settle(harness);

    byTestId<HTMLButtonElement>(root, 'indicator-attendance')!.click();
    await settle(harness);
    expect(document.body.textContent).toContain('Discard unsaved changes?');
    (document.querySelector('.cdk-overlay-container button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
    await tick();
    await settle(harness);

    http.expectNone(`${scriptsUrl}/indicators/attendance/versions`);
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.value).toBe('Changed label');
    expect(byTestId(root, 'indicator-coverage')?.getAttribute('aria-pressed')).toBe('true');

    byTestId<HTMLButtonElement>(root, 'indicator-attendance')!.click();
    await settle(harness);
    confirmDialog()!.click();
    await tick();
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/attendance/versions`).flush([summary(attendance)]);
    await settle(harness);

    expect(byTestId(root, 'indicator-attendance')?.getAttribute('aria-pressed')).toBe('true');
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.value).toBe('Attendance');
    expect(byTestId<HTMLInputElement>(root, 'meta-unit')!.value).toBe('people');
    expect(editor().text()).toContain('# attendance v1');
    expect(byTestId(root, 'unsaved')).toBeNull();
  });

  it('shows the metadata of an older version read-only and brings it back with the code when restored', async () => {
    const current = indicatorVersion('coverage', 2);
    const older = indicatorVersion('coverage', 1, { label: 'Old label', unit: 'cases', format: 'decimal' });
    const opened = await open('analyst', { scripts: withIndicators(current) });
    const { root, http, harness, editor } = opened;
    await selectIndicator(opened, current, [summary(current), summary(older)]);

    byTestId<HTMLButtonElement>(root, 'version-1')!.click();
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/coverage/versions/1`).flush(older);
    await settle(harness);
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.value).toBe('Old label');
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.readOnly).toBe(true);
    expect(byTestId<HTMLSelectElement>(root, 'meta-format')!.value).toBe('decimal');
    expect(editor().text()).toContain('# coverage v1');

    byTestId<HTMLButtonElement>(root, 'restore')!.click();
    http
      .expectOne((r) => r.url === `${scriptsUrl}/indicators/coverage/versions/1/restore` && r.method === 'POST')
      .flush(indicatorVersion('coverage', 3, { code: older.code, label: 'Old label', unit: 'cases', format: 'decimal' }));
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/coverage/versions`).flush([]);
    await settle(harness);

    expect(byTestId(root, 'indicator-coverage')?.textContent).toContain('Old label');
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.value).toBe('Old label');
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.readOnly).toBe(false);
    expect(byTestId<HTMLInputElement>(root, 'meta-unit')!.value).toBe('cases');
    expect(editor().text()).toContain('# coverage v1');
  });

  it('removes an indicator only after confirmation and goes back to script 1', async () => {
    const coverage = indicatorVersion('coverage', 2);
    const attendance = indicatorVersion('attendance', 1);
    const opened = await open('analyst', { scripts: withIndicators(attendance, coverage) });
    const { root, http, harness } = opened;
    await selectIndicator(opened, coverage);

    byTestId<HTMLButtonElement>(root, 'remove')!.click();
    await settle(harness);
    http.expectNone((r) => r.method === 'DELETE');
    expect(document.body.textContent).toContain('this indicator’s table is no longer published');
    confirmDialog()!.click();
    await tick();
    await settle(harness);

    http
      .expectOne((r) => r.url === `${scriptsUrl}/indicators/coverage` && r.method === 'DELETE')
      .flush(indicatorVersion('coverage', 3, { code: null, label: null, description: null, unit: null, format: null }));
    await settle(harness);
    http.expectOne(`${scriptsUrl}/clean/versions`).flush([summary(version(2))]);
    await settle(harness);

    expect(byTestId(root, 'indicator-coverage')).toBeNull();
    expect(byTestId(root, 'indicator-attendance')).not.toBeNull();
    expect(byTestId(root, 'script-clean')?.getAttribute('aria-pressed')).toBe('true');
    expect(byTestId(root, 'indicator-meta')).toBeNull();
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('Indicator removed');
  });

  it('tests an indicator under its name and names it in the result', async () => {
    const coverage = indicatorVersion('coverage', 2);
    const opened = await open('analyst', { scripts: withIndicators(coverage) });
    const { root, http, harness } = opened;
    await selectIndicator(opened, coverage);
    await startTest(opened);

    const request = http.expectOne((r) => r.url === testsUrl && r.method === 'POST');
    expect(request.request.body).toEqual({ kind: 'indicator', name: 'coverage', code: draftCode, sampleSize: 500 });
    const published = { ...flattenStep, step: 'indicator' as const, tables: [{ ...flattenStep.tables[0], name: 'coverage' }] };
    request.flush(
      scriptTest({ kind: 'indicator', name: 'coverage', status: 'succeeded', cleanVersion: 2, steps: [flattenStep, { ...flattenStep, step: 'clean' }, published] }),
    );
    await settle(harness);

    expect(byTestId(root, 'test-panel')?.textContent).toContain('Test of indicator coverage on 500 submissions');
    expect(byTestId(root, 'test-step-clean')?.textContent).toContain('Clean (saved version 2)');
    expect(byTestId(root, 'test-step-indicator')?.textContent).toContain('indicator coverage (draft)');
    expect(byTestId(root, 'unsaved')).not.toBeNull(); // nothing was saved
  });

  it('tests a new indicator before it is saved', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    byTestId<HTMLButtonElement>(root, 'add-indicator')!.click();
    await settle(harness);
    const dialog = document.querySelector('.cdk-overlay-container')!;
    typeInto(byTestId<HTMLInputElement>(dialog, 'indicator-name')!, 'piped_water_rate');
    await settle(harness);
    typeInto(byTestId<HTMLInputElement>(dialog, 'meta-label')!, 'Households with piped water');
    await settle(harness);
    byTestId<HTMLButtonElement>(dialog, 'indicator-add-confirm')!.click();
    await tick();
    await settle(harness);

    await startTest(opened);

    const request = http.expectOne((r) => r.url === testsUrl && r.method === 'POST');
    expect(request.request.body).toEqual({ kind: 'indicator', name: 'piped_water_rate', code: draftCode, sampleSize: 500 });
  });

  it('names the limit when the project already has 100 indicators', async () => {
    const hundred = Array.from({ length: 100 }, (_, i) => indicatorVersion(`indicator_${String(i).padStart(3, '0')}`, 1));
    const { root } = await open('analyst', { scripts: withIndicators(...hundred) });

    expect(byTestId<HTMLButtonElement>(root, 'add-indicator')!.disabled).toBe(true);
    expect(byTestId(root, 'indicator-limit')?.textContent).toContain('the most indicators it can hold (100)');
  });

  it('shows the limit message the API gives when another analyst took the last place', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    byTestId<HTMLButtonElement>(root, 'add-indicator')!.click();
    await settle(harness);
    const dialog = document.querySelector('.cdk-overlay-container')!;
    typeInto(byTestId<HTMLInputElement>(dialog, 'indicator-name')!, 'one_too_many');
    await settle(harness);
    typeInto(byTestId<HTMLInputElement>(dialog, 'meta-label')!, 'One too many');
    await settle(harness);
    byTestId<HTMLButtonElement>(dialog, 'indicator-add-confirm')!.click();
    await tick();
    await settle(harness);

    byTestId<HTMLButtonElement>(root, 'save')!.click();
    http
      .expectOne((r) => r.url === `${scriptsUrl}/indicators/one_too_many` && r.method === 'PUT')
      .flush({ errors: { name: ['A project can have at most 100 indicators. Remove one before adding another.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(harness);

    expect(byTestId(root, 'save-error')?.textContent).toContain('at most 100 indicators');
    expect(byTestId(root, 'unsaved')).not.toBeNull();
    expect(byTestId(root, 'indicator-one_too_many')?.textContent).toContain('new');
  });

  it('lists the indicator steps of a run by label and name, with the version used and the error of a failed one', async () => {
    const failed = run({
      id: 'f',
      status: 'failed',
      errorCode: 'step.indicator',
      error: "attendance: KeyError: 'age'",
      versions: {
        clean: 2,
        output: null,
        indicators: [
          { name: 'attendance', number: 1 },
          { name: 'coverage', number: 2 },
        ],
      },
      steps: [
        { step: 'flatten', name: '', status: 'succeeded', inputRows: 3, outputRows: 9 },
        { step: 'clean', name: '', status: 'succeeded', inputRows: 9, outputRows: 8 },
        { step: 'output', name: '', status: 'skipped', inputRows: null, outputRows: null },
        { step: 'indicator', name: 'attendance', status: 'failed', inputRows: 8, outputRows: null },
        { step: 'indicator', name: 'coverage', status: 'succeeded', inputRows: 8, outputRows: 12 },
      ],
    });
    const { root, http, harness } = await open('viewer', { runs: { items: [failed], total: 1 } });

    const table = byTestId(root, 'run-table')!;
    expect(byTestId(table, 'run-error')?.textContent?.trim()).toBe("An indicator script failed: attendance: KeyError: 'age'");
    expect(byTestId(table, 'run-indicators')?.textContent).toContain('Indicators 1/2');
    expect(table.textContent).toContain('v2 / — / 2 indicators');

    byTestId<HTMLButtonElement>(root, 'run-details-f')!.click();
    await settle(harness);
    const stepBase = { startedAt: '2026-10-02T10:00:01Z', finishedAt: '2026-10-02T10:00:03Z', tables: [], log: '', error: null };
    const details: RunDetails = {
      run: failed,
      steps: [
        { ...stepBase, step: 'flatten', name: '', label: null, status: 'succeeded', inputRows: 3, outputRows: 9 },
        { ...stepBase, step: 'clean', name: '', label: null, status: 'succeeded', inputRows: 9, outputRows: 8 },
        { ...stepBase, step: 'output', name: '', label: null, status: 'skipped', inputRows: null, outputRows: null },
        {
          ...stepBase,
          step: 'indicator',
          name: 'attendance',
          label: 'Attendance of enumerators',
          status: 'failed',
          inputRows: 8,
          outputRows: null,
          error: "KeyError: 'age'\n\nTraceback...",
        },
        {
          ...stepBase,
          step: 'indicator',
          name: 'coverage',
          label: 'Coverage',
          status: 'succeeded',
          inputRows: 8,
          outputRows: 12,
          tables: [{ name: 'coverage', rows: 12 }],
        },
      ],
      referenceTables: [],
    };
    http.expectOne(`${runsUrl}/f`).flush(details);
    await settle(harness);

    const dialog = document.querySelector('.cdk-overlay-container')!;
    const attendance = byTestId(dialog, 'step-indicator-attendance')!;
    expect(attendance.querySelector('h3')?.textContent).toContain('Attendance of enumerators');
    expect(byTestId(attendance, 'step-name')?.textContent).toBe('attendance');
    expect(attendance.textContent).toContain('v1');
    expect(attendance.textContent).toContain('Failed');
    expect(byTestId(attendance, 'step-error')?.textContent).toContain("KeyError: 'age'");
    const coverage = byTestId(dialog, 'step-indicator-coverage')!;
    expect(coverage.querySelector('h3')?.textContent).toContain('Coverage');
    expect(byTestId(coverage, 'step-name')?.textContent).toBe('coverage');
    expect(coverage.textContent).toContain('v2');
    expect(byTestId(coverage, 'step-tables')?.textContent).toContain('coverage (12)');
  });

  // The side panel, the editor's help and the cell actions

  it('lists the published tables beside the editor with their rows, types and question labels', async () => {
    const { root } = await open('analyst');
    const panel = byTestId(root, 'inputs-panel')!;

    expect(byTestId(panel, 'inputs-intro')?.textContent).toContain('flattened tables script 1 receives');
    expect(byTestId(panel, 'inputs-published')?.textContent).toContain('Published');
    const main = byTestId(panel, 'input-table-main')!;
    expect(main.textContent).toContain('480 rows');
    expect(main.textContent).toContain('has_water');
    expect(main.textContent).toContain('Does the household have running water?');
    expect(byTestId(panel, 'input-table-members')?.textContent).toContain('1,200 rows');
    expect(byTestId(panel, 'input-reference-population')?.textContent).toContain('population_2026');
    expect(panel.textContent).toContain('Municipality');
  });

  it('says the tables appear after the first run, and the editor still works', async () => {
    const { root, editor } = await open('analyst', { inputs: scriptInputs({ publishedAt: null, runId: null, tables: [] }) });

    expect(byTestId(root, 'inputs-none')?.textContent).toContain('appear after the first run');
    expect(byTestId(root, 'inputs-published')).toBeNull();
    editor().replace('# still works');
    expect(editor().text()).toBe('# still works');
  });

  it('shows the same tables to a viewer, who copies a name instead of inserting it', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const { root, editor } = await open('viewer');
    const before = editor().text();

    expect(byTestId(root, 'input-table-main')).not.toBeNull();
    expect(byTestId(root, 'inputs-panel')?.textContent).toContain('Click a name to copy it');
    const name = [...byTestId(root, 'input-table-main')!.querySelectorAll<HTMLButtonElement>('[data-testid="input-name"]')].find((b) => b.textContent === 'has_water')!;
    name.click();

    expect(writeText).toHaveBeenCalledWith('has_water');
    expect(editor().text()).toBe(before);
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('Copied has_water');
  });

  it('inserts a column name as a quoted string at the cursor of the editor', async () => {
    const { root, harness, editor } = await open('analyst');
    editor().replace('main[');
    const view = EditorView.findFromDOM(root.querySelector('.cm-editor') as HTMLElement)!;
    view.dispatch({ selection: { anchor: 5 } });
    await settle(harness);

    const name = [...byTestId(root, 'input-table-main')!.querySelectorAll<HTMLButtonElement>('[data-testid="input-name"]')].find((b) => b.textContent === 'has_water')!;
    name.click();

    expect(editor().text()).toBe('main["has_water"');
    expect(view.state.selection.main.head).toBe(16);
  });

  it('filters tables, columns and questions by name or label', async () => {
    const { root, harness } = await open('analyst');
    const filter = byTestId<HTMLInputElement>(root, 'inputs-filter')!;

    typeInto(filter, 'water');
    await settle(harness);

    const main = byTestId(root, 'input-table-main')!;
    expect(main.textContent).toContain('has_water');
    expect(main.textContent).not.toContain('municipality');
    expect(byTestId(root, 'input-table-members')).toBeNull();
    expect(byTestId(root, 'input-reference-population')).toBeNull();

    typeInto(filter, 'nothing like this');
    await settle(harness);
    expect(byTestId(root, 'inputs-no-match')).not.toBeNull();
  });

  it('reads the clean tables when an indicator is selected, and reads them again on refresh', async () => {
    const coverage = indicatorVersion('coverage', 1);
    const opened = await open('analyst', { scripts: withIndicators(coverage) });
    const { root, http, harness } = opened;

    byTestId<HTMLButtonElement>(root, 'indicator-coverage')!.click();
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/coverage/versions`).flush([summary(coverage)]);
    http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'indicator').flush(
      scriptInputs({ source: 'clean', tables: [{ name: 'main', rows: 2, columns: [{ name: 'municipality', type: 'text' }] }] }),
    );
    await settle(harness);

    expect(byTestId(root, 'inputs-intro')?.textContent).toContain('clean tables this script receives');
    expect(byTestId(root, 'input-table-main')?.textContent).toContain('2 rows');
    expect(byTestId(root, 'input-table-members')).toBeNull();

    byTestId<HTMLButtonElement>(root, 'inputs-refresh')!.click();
    http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'indicator').flush(
      scriptInputs({ source: 'clean', tables: [{ name: 'main', rows: 5, columns: [] }] }),
    );
    await settle(harness);
    expect(byTestId(root, 'input-table-main')?.textContent).toContain('5 rows');
  });

  it('puts the side panel after the editor so that it sits below it on a phone', async () => {
    const { root } = await open('analyst');

    const editorCard = root.querySelector('tmz-code-editor')!;
    const panel = byTestId(root, 'side-panel')!;

    expect(editorCard.compareDocumentPosition(panel) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(panel.closest('.grid')?.className).toContain('lg:grid-cols-4');
  });

  it('shows the problems found while editing, jumps to one, and still saves', async () => {
    const { root, http, harness, editor } = await open('analyst');

    editor().replace('def clean(raw, ctx):\n    x = (\n');
    await new Promise((resolve) => setTimeout(resolve, 700));
    await settle(harness);

    const problems = byTestId(root, 'problems')!;
    expect(problems.textContent).toContain('Syntax error');
    expect(problems.textContent).toContain('Saving and testing are never blocked');
    byTestId<HTMLButtonElement>(root, 'problem')!.click();
    expect(document.activeElement).toBe(root.querySelector('.cm-content'));

    byTestId<HTMLButtonElement>(root, 'save')!.click();
    const save = http.expectOne((r) => r.url === `${scriptsUrl}/clean` && r.method === 'PUT');
    expect(save.request.body).toEqual({ code: 'def clean(raw, ctx):\n    x = (\n' });
  });

  it('tells the keys of the editor next to it', async () => {
    const { root } = await open('analyst');

    expect(byTestId(root, 'editor-keys')?.textContent).toContain('Press Esc, then Tab, to leave the editor');
  });

  it('shows a viewer the reference of the selected indicator: its contract and what it returns', async () => {
    const coverage = indicatorVersion('coverage', 1);
    const opened = await open('viewer', { scripts: withIndicators(coverage) });
    await selectIndicator(opened, coverage);
    opened.http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'indicator');

    byTestId<HTMLButtonElement>(opened.root, 'side-tab-reference')!.click();
    await settle(opened.harness);

    const reference = byTestId(opened.root, 'ctx-reference')!;
    expect(byTestId(reference, 'reference-signature')?.textContent).toBe('compute(cleaned, ctx)');
    expect(byTestId(reference, 'reference-returns')?.textContent).toContain('at least one numeric column');
    expect(byTestId(reference, 'reference-returns')?.textContent).toContain('numerator and denominator');
    for (const member of ['project_name', 'show', 'form.label', 'form.column_label', 'log.info', 'reference.get']) {
      const path = `ctx.${member}`;
      expect(byTestId(reference, `reference-${path}`), path).not.toBeNull();
    }
    expect(byTestId(reference, 'reference-ctx.form.label')?.textContent).toContain('ctx.form.label(question, code)');
  });

  describe('cells', () => {
    const cellScript = 'def clean(raw, ctx):\n    # %% Load\n    main = raw["main"]\n    # %% Rate\n    rate = 1\n    # %% Return\n    return raw\n';

    async function openWithCells(role: ProjectRole) {
      const opened = await open(role);
      opened.editor().replace(cellScript);
      await settle(opened.harness);
      return opened;
    }

    it('runs a test through the first cell, stopping at the next marker', async () => {
      const { root, http, harness } = await openWithCells('analyst');

      const buttons = root.querySelectorAll<HTMLButtonElement>('.cm-run-cell');
      expect(buttons).toHaveLength(3);
      buttons[0].click();

      const request = http.expectOne((r) => r.url === testsUrl && r.method === 'POST');
      expect(request.request.body).toEqual({ kind: 'clean', code: cellScript, sampleSize: 500, stopAtLine: 4 });
      request.flush(scriptTest({ code: cellScript, stopAtLine: 4 }));
      await settle(harness);
      expect(byTestId(root, 'test-panel')).not.toBeNull();
    });

    it('runs a whole test for the last cell', async () => {
      const { root, http } = await openWithCells('analyst');

      root.querySelectorAll<HTMLButtonElement>('.cm-run-cell')[2].click();

      const request = http.expectOne((r) => r.url === testsUrl && r.method === 'POST');
      expect(request.request.body).toEqual({ kind: 'clean', code: cellScript, sampleSize: 500 });
    });

    it('runs the cell the cursor is in with Ctrl+Enter, using the sample size of the field', async () => {
      const { root, http, harness } = await openWithCells('analyst');
      const size = byTestId<HTMLInputElement>(root, 'sample-size')!;
      typeInto(size, '120');
      const view = EditorView.findFromDOM(root.querySelector('.cm-editor') as HTMLElement)!;
      view.dispatch({ selection: { anchor: view.state.doc.line(5).from + 2 } });
      await settle(harness);

      const event = new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true, cancelable: true });
      Object.defineProperty(event, 'keyCode', { value: 13 });
      view.contentDOM.dispatchEvent(event);

      const request = http.expectOne((r) => r.url === testsUrl && r.method === 'POST');
      expect(request.request.body).toEqual({ kind: 'clean', code: cellScript, sampleSize: 120, stopAtLine: 6 });
    });

    it('offers no cell actions to a viewer', async () => {
      const { root, http } = await open('viewer');

      expect(root.querySelectorAll('.cm-run-cell')).toHaveLength(0);
      http.expectNone((r) => r.url === testsUrl);
    });

    it('offers no cell actions while an older version is on view', async () => {
      const { root, http, harness, editor } = await open('analyst');
      editor().replace(cellScript);
      await settle(harness);
      expect(root.querySelectorAll('.cm-run-cell')).toHaveLength(3);

      byTestId<HTMLButtonElement>(root, 'version-1')!.click();
      await settle(harness);
      confirmDialog()!.click();
      await tick();
      await settle(harness);
      http.expectOne(`${scriptsUrl}/clean/versions/1`).flush(version(1));
      await settle(harness);

      expect(root.querySelectorAll('.cm-run-cell')).toHaveLength(0);
    });
  });


  // The result of a test: what the script showed, a stop at a cell, reused steps

  describe('test result', () => {
    const markedCode = 'def clean(raw, ctx):\n    # %% Load\n    main = raw["main"]\n    # %% Rate\n    rate = 1\n    # %% Return\n    return raw\n';
    const shownMain: ShownPreview = {
      name: 'main',
      line: 3,
      rows: 120,
      table: { name: 'main', rows: 120, columns: [{ name: '_id', type: 'integer' }], previewRows: 2, preview: [[1], [2]], cutCells: [] },
      text: null,
    };
    const shownRate: ShownPreview = {
      name: 'rate',
      line: 5,
      rows: 2,
      table: {
        name: 'rate',
        rows: 2,
        columns: [
          { name: 'municipality', type: 'text' },
          { name: 'value', type: 'decimal' },
        ],
        previewRows: 2,
        preview: [
          ['Cali', 0.5],
          ['Buga', 0.25],
        ],
        cutCells: [],
      },
      text: null,
    };
    const shownCount: ShownPreview = { name: 'shown 3', line: 6, rows: 1, table: null, text: '120' };

    /** Starts a test from the editor and has the API answer with `test`. */
    async function testWith(test: ScriptTest) {
      const opened = await open('analyst');
      await startTest(opened);
      opened.http.expectOne((r) => r.url === testsUrl && r.method === 'POST').flush(test);
      await settle(opened.harness);
      return opened;
    }

    const step = (overrides: Partial<TestStep>): TestStep => ({ ...flattenStep, step: 'clean', tables: [], ...overrides });

    it('lists what the script showed in call order with each name and line, and shows the one chosen', async () => {
      const { root, harness } = await testWith(
        scriptTest({ status: 'succeeded', steps: [flattenStep, step({ shown: [shownMain, shownRate, shownCount] })] }),
      );
      const shown = byTestId(root, 'test-shown')!;

      expect([...shown.querySelectorAll('button')].map((b) => [...b.querySelectorAll('span')].map((s) => s.textContent).join(' '))).toEqual([
        'main line 3',
        'rate line 5',
        'shown 3 line 6',
      ]);
      // The last one shown is chosen at first.
      expect(byTestId(shown, 'test-shown-text')?.textContent).toBe('120');
      expect(byTestId(shown, 'test-shown-note')?.textContent).toContain('shown 3, line 6');

      byTestId<HTMLButtonElement>(shown, 'test-shown-1')!.click();
      await settle(harness);
      expect(byTestId(root, 'test-shown-note')?.textContent).toContain('rate, line 5: first 2 of 2 rows.');
      expect(byTestId(root, 'test-shown')?.textContent).toContain('Cali');
      expect(byTestId(root, 'test-shown')?.textContent).toContain('0.25');
      expect(byTestId(root, 'test-shown-1')?.getAttribute('aria-pressed')).toBe('true');

      byTestId<HTMLButtonElement>(root, 'test-shown-0')!.click();
      await settle(harness);
      expect(byTestId(root, 'test-shown-note')?.textContent).toContain('main, line 3: first 2 of 120 rows.');
    });

    it('names the cell a test through a cell stopped at, and says nothing was validated', async () => {
      const { root } = await testWith(
        scriptTest({
          code: markedCode,
          stopAtLine: 6,
          status: 'succeeded',
          steps: [flattenStep, step({ status: 'stopped', stoppedAtLine: 6, shown: [shownRate], inputRows: 120, outputRows: null })],
        }),
      );

      expect(byTestId(root, 'test-step-clean')?.textContent).toContain('Stopped at a cell');
      expect(byTestId(root, 'test-stopped')?.textContent).toContain('Ran through the cell Rate and stopped at line 6');
      expect(byTestId(root, 'test-success')?.textContent).toContain('ran to the end of the cell');
      expect(byTestId(root, 'test-shown')?.textContent).toContain('rate');
    });

    it('numbers a cell that has no name', async () => {
      const { root } = await testWith(
        scriptTest({
          code: 'def clean(raw, ctx):\n    # %%\n    x = 1\n    # %%\n    return raw\n',
          stopAtLine: 4,
          status: 'succeeded',
          steps: [flattenStep, step({ status: 'stopped', stoppedAtLine: 4 })],
        }),
      );

      expect(byTestId(root, 'test-stopped')?.textContent).toContain('Ran through the cell 1 and stopped at line 4');
    });

    it('keeps the previews of a script that then failed, next to its traceback', async () => {
      const { root } = await testWith(
        scriptTest({
          status: 'failed',
          errorCode: 'step.clean',
          error: "KeyError: 'age'",
          steps: [flattenStep, step({ status: 'failed', error: "KeyError: 'age'\n\nTraceback (most recent call last):", shown: [shownMain] })],
        }),
      );

      expect(byTestId(root, 'test-step-error')?.textContent).toContain("KeyError: 'age'");
      expect(byTestId(root, 'test-shown')?.textContent).toContain('main');
      expect(byTestId(root, 'test-shown-note')?.textContent).toContain('main, line 3');
    });

    it('marks a step reused and shows the log first recorded', async () => {
      const { root } = await testWith(
        scriptTest({
          status: 'succeeded',
          steps: [{ ...flattenStep, reused: true, log: 'Sample: 120 submissions.\nReused from an earlier test.' }, step({})],
        }),
      );

      expect(byTestId(byTestId(root, 'test-step-flatten')!, 'step-reused')?.textContent).toBe('Reused');
      expect(byTestId(root, 'test-step-flatten')?.textContent).toContain('Sample: 120 submissions.');
      expect(byTestId(root, 'test-step-flatten')?.textContent).toContain('Reused from an earlier test.');
      expect(byTestId(byTestId(root, 'test-step-clean')!, 'step-reused')).toBeNull();
    });

    it('shows no preview list for a step that showed nothing', async () => {
      const { root } = await testWith(scriptTest({ status: 'succeeded', steps: [flattenStep, step({ shown: [] })] }));

      expect(byTestId(root, 'test-shown')).toBeNull();
    });
  });


  it('shows an indicator test its table as the dashboard will see it, written in the format being edited', async () => {
    const coverage = indicatorVersion('coverage', 1, { format: 'count', unit: 'households' });
    const opened = await open('analyst', { scripts: withIndicators(coverage) });
    await selectIndicator(opened, coverage);
    opened.http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'indicator').flush(scriptInputs({ source: 'clean' }));
    const { root, http, harness } = opened;
    opened.editor().replace(draftCode);
    await settle(harness);
    byTestId<HTMLButtonElement>(root, 'test-on-sample')!.click();
    await settle(harness);
    const facts = {
      measures: ['value'],
      defaultMeasure: 'value',
      ratio: false,
      dimensions: ['municipality'],
      defaultDimension: 'municipality',
      geometry: [],
      rows: 2,
      defaultMeasureNulls: 0,
      defaultMeasureMax: 0.75,
      totals: { value: 0.75 },
      duplicateRows: 0,
      charts: { municipality: { values: 2, groups: [{ group: 'Cali', value: 0.5, numerator: null, denominator: null }, { group: 'Buga', value: 0.25, numerator: null, denominator: null }] } },
    };
    const indicatorStep: TestStep = { ...flattenStep, step: 'indicator', tables: [], dashboard: facts };
    http
      .expectOne((r) => r.url === testsUrl && r.method === 'POST')
      .flush(scriptTest({ kind: 'indicator', name: 'coverage', code: draftCode, status: 'succeeded', steps: [flattenStep, indicatorStep] }));
    await settle(harness);

    const stat = () => byTestId(byTestId(root, 'indicator-view')!, 'stat-value')?.textContent;
    expect(byTestId(root, 'indicator-view')).not.toBeNull();
    expect(stat()).toBe('1 households');

    const format = byTestId<HTMLSelectElement>(root, 'meta-format')!;
    typeInto(format, 'percent');
    await settle(harness);

    expect(stat()).toBe('75% households');
    expect(byTestId(root, 'test-panel')?.textContent).toContain('75%');
  });

  it('shows no dashboard view for a test of script 1', async () => {
    const opened = await open('analyst');
    await startTest(opened);
    opened.http
      .expectOne((r) => r.url === testsUrl && r.method === 'POST')
      .flush(scriptTest({ status: 'succeeded', steps: [flattenStep, { ...flattenStep, step: 'clean' }] }));
    await settle(opened.harness);

    expect(byTestId(opened.root, 'indicator-view')).toBeNull();
  });

  // 7.1 — sample downloads

  it('prepares a sample of the chosen size, follows it and downloads the zip through a new link', async () => {
    const opened = await open('analyst', { testPollMs: 20 });
    const { root, http, harness } = opened;

    await requestDownload(opened, '300');
    const request = http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST');
    expect(request.request.body).toEqual({ sampleSize: 300 });
    request.flush(sampleDownload({ sampleSize: 300, waitingFor: 'run' }));
    await settle(harness);
    expect(byTestId(root, 'download-progress')?.textContent).toContain('Waiting for a run to finish');
    expect(byTestId(root, 'unsaved')).toBeNull(); // nothing was saved, tested or run

    await new Promise((resolve) => setTimeout(resolve, 40));
    http.expectOne(`${downloadsUrl}/d1`).flush(sampleDownload({ status: 'running', startedAt: '2026-10-03T10:00:01Z' }));
    await settle(harness);
    expect(byTestId(root, 'download-progress')?.textContent).toContain('Preparing the sample');

    await new Promise((resolve) => setTimeout(resolve, 40));
    http.expectOne(`${downloadsUrl}/d1`).flush(readyDownload({ sampleSize: 300 }, { sample: { requested: 300, submissions: 300 } }));
    await settle(harness);

    expect(byTestId(root, 'download-progress')).toBeNull();
    const ready = byTestId(root, 'download-ready')!;
    expect(ready.textContent).toContain('The sample holds 300 submissions (you asked for 300)');
    const tables = byTestId(ready, 'download-tables')!.textContent!.replace(/\s+/g, ' ');
    expect(tables).toContain('main (320 rows), members (871 rows)');
    expect(tables).toContain('Clean tables, what script 1 (version 2) returns: main (318 rows)');
    expect(tables).toContain('population (42 rows)');
    expect(byTestId(ready, 'download-size')?.textContent).toContain('Size: 12.3 MB');
    expect(byTestId(ready, 'download-notice')?.textContent).toContain("contains the project's submissions");

    byTestId<HTMLButtonElement>(ready, 'download-button')!.click();
    await settle(harness);
    const link = http.expectOne(`${downloadsUrl}/d1/link`);
    expect(link.request.method).toBe('POST');
    link.flush({ downloadUrl: '/api/v1/data-exports/token1', fileName: 'survey_sample_2026-10-03.zip', sizeBytes: 12_900_000, expiresAt: '2026-10-03T10:05:00Z' });
    await settle(harness);
    expect(downloader.start).toHaveBeenCalledExactlyOnceWith('/api/v1/data-exports/token1');

    await new Promise((resolve) => setTimeout(resolve, 40));
    http.expectNone(`${downloadsUrl}/d1`); // a ready download is not polled
  });

  it('asks for a new link each time the zip is downloaded', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await requestDownload(opened);
    http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST').flush(readyDownload());
    await settle(harness);

    for (const token of ['one', 'two']) {
      byTestId<HTMLButtonElement>(root, 'download-button')!.click();
      await settle(harness);
      http
        .expectOne(`${downloadsUrl}/d1/link`)
        .flush({ downloadUrl: `/api/v1/data-exports/${token}`, fileName: 'x.zip', sizeBytes: 1, expiresAt: '2026-10-03T10:05:00Z' });
      await settle(harness);
    }

    expect(downloader.start.mock.calls.map((call) => call[0])).toEqual(['/api/v1/data-exports/one', '/api/v1/data-exports/two']);
  });

  it('does not offer sample downloads to a viewer', async () => {
    const { root } = await open('viewer');

    expect(byTestId(root, 'download-sample')).toBeNull();
    expect(byTestId(root, 'sample-download')).toBeNull();
  });

  it('checks the sample size before asking, as a test does', async () => {
    const opened = await open('analyst');

    await requestDownload(opened, '0');

    opened.http.expectNone((r) => r.url === downloadsUrl);
    expect(opened.root.textContent).toContain('Enter a whole number of submissions from 1 to 5,000');
  });

  it('shows the API reason when the request is rejected', async () => {
    const opened = await open('analyst');
    await requestDownload(opened);

    opened.http
      .expectOne((r) => r.url === downloadsUrl && r.method === 'POST')
      .flush({ title: 'One or more validation errors occurred.', errors: { sampleSize: ['Enter a sample of 1 to 5,000 submissions.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(opened.harness);

    expect(opened.root.textContent).toContain('Enter a sample of 1 to 5,000 submissions.');
    expect(byTestId(opened.root, 'sample-download')).toBeNull();
  });

  it('warns when script 1 failed on the sample and says the zip holds only the flattened tables', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await requestDownload(opened);

    http
      .expectOne((r) => r.url === downloadsUrl && r.method === 'POST')
      .flush(readyDownload({}, { clean: { status: 'failed', version: 4, error: "KeyError: 'age'", tables: [] } }));
    await settle(harness);

    const warning = byTestId(root, 'download-script1-warning')!;
    expect(warning.textContent).toContain('Script 1 (version 4) failed on this sample');
    expect(warning.textContent).toContain("KeyError: 'age'");
    expect(warning.textContent).toContain('only the flattened tables');
    expect(byTestId(root, 'download-tables')?.textContent).not.toContain('Clean tables');
    expect(byTestId(root, 'download-button')).not.toBeNull(); // the flattened tables can still be downloaded
  });

  it('says the project has no script 1 when the clean tables are the flattened ones', async () => {
    const opened = await open('analyst');
    await requestDownload(opened);

    opened.http
      .expectOne((r) => r.url === downloadsUrl && r.method === 'POST')
      .flush(readyDownload({}, { clean: { status: 'none', version: null, error: null, tables: [] } }));
    await settle(opened.harness);

    expect(byTestId(opened.root, 'download-tables')?.textContent).toContain('no script 1');
    expect(byTestId(opened.root, 'download-script1-warning')).toBeNull();
  });

  it('says a download expired when its link is gone and prepares it again with the same size', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await requestDownload(opened, '250');
    http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST').flush(readyDownload({ sampleSize: 250 }));
    await settle(harness);

    byTestId<HTMLButtonElement>(root, 'download-button')!.click();
    await settle(harness);
    http.expectOne(`${downloadsUrl}/d1/link`).flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    await settle(harness);

    expect(byTestId(root, 'download-expired')?.textContent).toContain('no longer available');
    expect(byTestId(root, 'download-ready')).toBeNull();
    expect(downloader.start).not.toHaveBeenCalled();

    byTestId<HTMLButtonElement>(root, 'download-again')!.click();
    await settle(harness);
    const again = http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST');
    expect(again.request.body).toEqual({ sampleSize: 250 });
    again.flush(sampleDownload({ id: 'd2', sampleSize: 250 }));
    await settle(harness);
    expect(byTestId(root, 'download-expired')).toBeNull();
    expect(byTestId(root, 'download-progress')).not.toBeNull();
  });

  it('says a download is gone when following it finds it replaced or expired', async () => {
    const opened = await open('analyst', { testPollMs: 20 });
    const { root, http, harness } = opened;
    await requestDownload(opened);
    http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST').flush(sampleDownload());
    await settle(harness);

    await new Promise((resolve) => setTimeout(resolve, 40));
    http.expectOne(`${downloadsUrl}/d1`).flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    await settle(harness);

    expect(byTestId(root, 'download-expired')).not.toBeNull();
    await new Promise((resolve) => setTimeout(resolve, 40));
    http.expectNone(`${downloadsUrl}/d1`);
  });

  it('shows why a link could not be issued without losing the download', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await requestDownload(opened);
    http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST').flush(readyDownload());
    await settle(harness);

    byTestId<HTMLButtonElement>(root, 'download-button')!.click();
    await settle(harness);
    http
      .expectOne(`${downloadsUrl}/d1/link`)
      .flush({ title: 'Busy', code: 'sampleDownload.busy', detail: 'Other files are being prepared.' }, { status: 503, statusText: 'Service Unavailable' });
    await settle(harness);

    expect(byTestId(root, 'download-link-error')?.textContent).toContain('Other files are being prepared. Try again in a moment.');
    expect(byTestId(root, 'download-ready')).not.toBeNull();
    expect(byTestId(root, 'download-expired')).toBeNull();
  });

  it('shows why a download failed and prepares it again', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await requestDownload(opened, '1000');

    http
      .expectOne((r) => r.url === downloadsUrl && r.method === 'POST')
      .flush(
        sampleDownload({
          status: 'failed',
          sampleSize: 1000,
          finishedAt: '2026-10-03T10:00:09Z',
          errorCode: 'download.tooLarge',
          error: 'The sample is 130 MB, more than the 100 MB this server allows. Choose a smaller sample.',
        }),
      );
    await settle(harness);

    expect(byTestId(root, 'download-error')?.textContent).toContain('The sample is 130 MB, more than the 100 MB this server allows');
    byTestId<HTMLButtonElement>(root, 'download-again')!.click();
    await settle(harness);
    expect(http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST').request.body).toEqual({ sampleSize: 1000 });
  });

  it('tells a test that it waits for a sample download', async () => {
    const opened = await open('analyst');
    await startTest(opened);

    opened.http.expectOne((r) => r.url === testsUrl && r.method === 'POST').flush(scriptTest({ waitingFor: 'download' }));
    await settle(opened.harness);

    expect(byTestId(opened.root, 'test-progress')?.textContent).toContain('Waiting for a sample download to be prepared');
  });

  it('keeps the download action in the form that stacks on a phone, with its panel after the editor', async () => {
    const opened = await open('analyst');
    const { root, http, harness } = opened;
    await requestDownload(opened);
    http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST').flush(readyDownload());
    await settle(harness);

    const form = byTestId(root, 'download-sample')!.closest('form')!;
    expect(form.className).toContain('flex-col');
    expect(form.className).toContain('sm:flex-row');
    expect(form.contains(byTestId(root, 'sample-size'))).toBe(true);
    expect(root.querySelector('tmz-code-editor')!.compareDocumentPosition(byTestId(root, 'sample-download')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
