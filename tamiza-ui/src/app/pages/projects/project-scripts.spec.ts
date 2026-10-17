import { TestRequest } from '@angular/common/http/testing';
import { Provider, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { EditorView } from '@codemirror/view';
import { vi } from 'vitest';
import { Downloader } from '../../core/download.service';
import { StoredConversation, conversationKey } from '../../core/projects/assistant-storage';
import type { AssistantAnswer } from '../../core/projects/assistant.service';
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
  ScriptRef,
  ScriptTest,
  ShownPreview,
  ScriptVersionDetails,
  ScriptVersionSummary,
  TestStep,
} from '../../core/projects/pipeline.service';
import { ProjectRole } from '../../core/projects/projects.service';
import { SYSTEM_CONFIG_URL, SystemConfigService } from '../../core/system-config';
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
  /** Whether the deployment's system configuration enables the assistant; absent means the configuration request is left unanswered. */
  assistant?: boolean;
  /** More providers, for example a stand-in for the assistant session. */
  providers?: Provider[];
}

const downloader = { start: vi.fn() };

async function open(role: ProjectRole, seed: Seed = {}) {
  const current = version(2);
  const providers = [
    provideVisibleElements(),
    { provide: Downloader, useValue: downloader },
    ...(seed.pollMs ? [{ provide: PIPELINE_POLL_MS, useValue: seed.pollMs }] : []),
    ...(seed.testPollMs ? [{ provide: SCRIPT_TEST_POLL_MS, useValue: seed.testPollMs }] : []),
    ...(seed.providers ?? []),
  ];
  const opened = await openProjectSection('scripts', role, {}, providers);
  const { http, harness } = opened;
  if (seed.assistant !== undefined) {
    http
      .match(SYSTEM_CONFIG_URL)
      .forEach((r) => r.flush({ oidc: { authority: 'https://kc.example.org', clientId: 'tamiza-ui', scope: 'openid' }, version: '0.1.0', assistant: { enabled: seed.assistant } }));
  }
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

type Opened = Awaited<ReturnType<typeof open>>;
const textOf = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

/** Each item of the open indicator menu as "label name version". */
function indicatorEntries(): string[] {
  return [...document.querySelectorAll('[data-testid="indicator-list"] [role="menuitem"]')].map((item) =>
    [...item.querySelectorAll('span.truncate, code, tmz-badge')].map((e) => e.textContent!.trim()).join(' '),
  );
}

async function openIndicatorMenu(opened: Opened) {
  byTestId<HTMLButtonElement>(opened.root, 'indicator-menu')!.click();
  await settle(opened.harness);
}

/** Opens the indicator menu and chooses an indicator from it. */
async function pickIndicator(opened: Opened, name: string) {
  await openIndicatorMenu(opened);
  document.querySelector<HTMLElement>(`[data-testid="indicator-${name}"]`)!.click();
  await settle(opened.harness);
}

/** Opens the "more actions" menu (Upload and Remove) and chooses an item. */
async function moreItem(opened: Opened, id: string) {
  byTestId<HTMLButtonElement>(opened.root, 'menu-trigger')!.click();
  await settle(opened.harness);
  document.querySelector<HTMLElement>(`[data-testid="${id}"]`)!.click();
  await settle(opened.harness);
}

async function openMore(opened: Opened) {
  byTestId<HTMLButtonElement>(opened.root, 'menu-trigger')!.click();
  await settle(opened.harness);
}

async function openVersions(opened: Opened) {
  byTestId<HTMLButtonElement>(opened.root, 'side-tab-versions')!.click();
  await settle(opened.harness);
}

/** Opens the fields of the selected indicator's details. */
async function editDetails(opened: Opened) {
  byTestId<HTMLButtonElement>(opened.root, 'edit-details')!.click();
  await settle(opened.harness);
}

/** Selects an indicator and answers the request for its versions. */
async function selectIndicator(opened: Opened, current: ScriptVersionDetails, versions = [summary(current)]) {
  await pickIndicator(opened, current.name!);
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
    for (const id of ['save', 'remove', 'upload', 'template', 'run-now', 'test-on-sample', 'sample-size']) {
      expect(byTestId(root, id)).toBeNull();
    }
    expect(byTestId(root, 'script-clean')?.textContent).toContain('v2');
    expect(byTestId(root, 'script-output')?.textContent).not.toContain('not set'); // the badge shows on the selected script only
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
    await pickIndicator(opened, 'coverage');
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
    const opened = await open('admin');
    const { root, http, harness } = opened;

    await moreItem(opened, 'remove');
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
    await openMore(opened);
    expect(document.querySelector('[data-testid="remove"]')).toBeNull(); // nothing left to remove
    expect(document.querySelector('[data-testid="upload-item"]')).not.toBeNull();
  });

  it('shows an older version read-only and restores it', async () => {
    const opened = await open('analyst');
    const { root, http, harness, editor } = opened;
    await openVersions(opened);

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
    expect(table.textContent).not.toContain('v2 / —'); // the versions are in the run details now
    expect(table.querySelectorAll('thead th')).toHaveLength(5);
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
    const { root, http } = opened;

    expect(textOf(byTestId(root, 'indicator-menu'))).toBe('Indicators (2)');
    await openIndicatorMenu(opened);
    expect(indicatorEntries()).toEqual(['Attendance attendance v1', 'Label of coverage coverage v3']);
    document.querySelector<HTMLElement>('[data-testid="indicator-list"] [role="menuitem"]')!.click(); // closes the menu again
    await settle(opened.harness);
    expect(byTestId(root, 'add-indicator')).toBeNull();

    // The details are text, not inputs that look editable.
    http.expectOne(`${scriptsUrl}/indicators/attendance/versions`).flush([summary(attendance)]);
    await selectIndicator(opened, coverage);
    const details = textOf(byTestId(root, 'indicator-meta-readonly'));
    expect(details).toContain('Label of coverage');
    expect(details).toContain('What it measures');
    expect(details).toContain('Count · households');
    expect(byTestId(root, 'meta-label')).toBeNull();
    expect(byTestId(root, 'edit-details')).toBeNull();
    for (const id of ['save', 'remove', 'template', 'test-on-sample']) {
      expect(byTestId(root, id)).toBeNull();
    }
  });

  it('says so when the project has no indicators, inside the menu rather than above the editor', async () => {
    const opened = await open('analyst');
    const { root } = opened;

    expect(byTestId(root, 'no-indicators')).toBeNull();
    await openIndicatorMenu(opened);
    expect(textOf(document.querySelector('[data-testid="no-indicators"]'))).toContain('no indicators yet');
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
    expect(textOf(byTestId(root, 'indicator-menu'))).toContain('Households with piped water');
    expect(byTestId(root, 'indicator-meta-name')?.textContent).toBe('piped_water_rate');
    expect(textOf(byTestId(root, 'indicator-meta-summary'))).toContain('Percentage');
    await openIndicatorMenu(opened);
    expect(textOf(document.querySelector('[data-testid="indicator-piped_water_rate"]'))).toContain('new');
    document.querySelector<HTMLElement>('[data-testid="indicator-piped_water_rate"]')!.click();
    await settle(harness);
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

    await openIndicatorMenu(opened);
    expect(indicatorEntries()).toEqual(['Label of coverage coverage v1', 'Households with piped water piped_water_rate v1']);
    document.querySelector<HTMLElement>('[data-testid="indicator-coverage"]')!.click(); // closes the menu
    await settle(harness);
    http.match((r) => r.url.includes('/indicators/coverage/versions')).forEach((r) => r.flush([]));
    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('Version 1 saved');
  });

  it('marks the form unsaved when only the metadata changes, and saves the metadata with the code', async () => {
    const coverage = indicatorVersion('coverage', 2);
    const opened = await open('analyst', { scripts: withIndicators(coverage) });
    const { root, http, harness } = opened;
    await selectIndicator(opened, coverage);
    expect(byTestId<HTMLButtonElement>(root, 'save')!.disabled).toBe(true);
    expect(byTestId(root, 'unsaved')).toBeNull();
    expect(byTestId(root, 'meta-label')).toBeNull(); // a line, not a form, until the details are opened
    await editDetails(opened);
    expect(byTestId(root, 'edit-details')!.getAttribute('aria-expanded')).toBe('true');

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

    await openIndicatorMenu(opened);
    expect(textOf(document.querySelector('[data-testid="indicator-coverage"]'))).toContain('Coverage of piped water');
    expect(textOf(document.querySelector('[data-testid="indicator-coverage"]'))).toContain('v3');
    expect(byTestId(root, 'unsaved')).toBeNull();
    expect(byTestId(root, 'field-error')).toBeNull();
  });

  it('asks before leaving an indicator with unsaved changes and shows the other indicator when confirmed', async () => {
    const attendance = indicatorVersion('attendance', 1, { label: 'Attendance', unit: 'people' });
    const coverage = indicatorVersion('coverage', 2);
    const opened = await open('analyst', { scripts: withIndicators(attendance, coverage) });
    const { root, http, harness, editor } = opened;
    await selectIndicator(opened, coverage);
    await editDetails(opened);
    typeInto(byTestId<HTMLInputElement>(root, 'meta-label')!, 'Changed label');
    await settle(harness);

    await pickIndicator(opened, 'attendance');
    expect(document.body.textContent).toContain('Discard unsaved changes?');
    (document.querySelector('.cdk-overlay-container button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
    await tick();
    await settle(harness);

    http.expectNone(`${scriptsUrl}/indicators/attendance/versions`);
    expect(byTestId<HTMLInputElement>(root, 'meta-label')!.value).toBe('Changed label'); // unsaved edits keep the details open
    expect(byTestId(root, 'indicator-menu')?.getAttribute('aria-pressed')).toBe('true');
    expect(textOf(byTestId(root, 'indicator-menu'))).toContain('Label of coverage'); // the menu names the saved indicator
    expect(textOf(byTestId(root, 'indicator-meta-summary'))).toContain('Changed label');

    await pickIndicator(opened, 'attendance');
    confirmDialog()!.click();
    await tick();
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/attendance/versions`).flush([summary(attendance)]);
    await settle(harness);

    expect(textOf(byTestId(root, 'indicator-menu'))).toContain('Attendance');
    expect(textOf(byTestId(root, 'indicator-meta-summary'))).toContain('Attendance');
    expect(textOf(byTestId(root, 'indicator-meta-summary'))).toContain('people');
    expect(byTestId(root, 'meta-label')).toBeNull(); // the details close when another script is shown
    expect(editor().text()).toContain('# attendance v1');
    expect(byTestId(root, 'unsaved')).toBeNull();
  });

  it('shows the metadata of an older version read-only and brings it back with the code when restored', async () => {
    const current = indicatorVersion('coverage', 2);
    const older = indicatorVersion('coverage', 1, { label: 'Old label', unit: 'cases', format: 'decimal' });
    const opened = await open('analyst', { scripts: withIndicators(current) });
    const { root, http, harness, editor } = opened;
    await selectIndicator(opened, current, [summary(current), summary(older)]);
    await openVersions(opened);

    byTestId<HTMLButtonElement>(root, 'version-1')!.click();
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/coverage/versions/1`).flush(older);
    await settle(harness);
    const shown = textOf(byTestId(root, 'indicator-meta-readonly')); // a list, not inputs that look editable
    expect(shown).toContain('Old label');
    expect(shown).toContain('Decimal · cases');
    expect(byTestId(root, 'meta-label')).toBeNull();
    expect(editor().text()).toContain('# coverage v1');

    byTestId<HTMLButtonElement>(root, 'restore')!.click();
    http
      .expectOne((r) => r.url === `${scriptsUrl}/indicators/coverage/versions/1/restore` && r.method === 'POST')
      .flush(indicatorVersion('coverage', 3, { code: older.code, label: 'Old label', unit: 'cases', format: 'decimal' }));
    await settle(harness);
    http.expectOne(`${scriptsUrl}/indicators/coverage/versions`).flush([]);
    await settle(harness);

    expect(textOf(byTestId(root, 'indicator-menu'))).toContain('Old label');
    const summaryLine = textOf(byTestId(root, 'indicator-meta-summary')); // editable again, as a line
    expect(summaryLine).toContain('Old label');
    expect(summaryLine).toContain('cases');
    expect(byTestId(root, 'edit-details')).not.toBeNull();
    expect(editor().text()).toContain('# coverage v1');
  });

  it('removes an indicator only after confirmation and goes back to script 1', async () => {
    const coverage = indicatorVersion('coverage', 2);
    const attendance = indicatorVersion('attendance', 1);
    const opened = await open('analyst', { scripts: withIndicators(attendance, coverage) });
    const { root, http, harness } = opened;
    await selectIndicator(opened, coverage);

    await moreItem(opened, 'remove');
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

    await openIndicatorMenu(opened);
    expect(document.querySelector('[data-testid="indicator-coverage"]')).toBeNull();
    expect(document.querySelector('[data-testid="indicator-attendance"]')).not.toBeNull();
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
    expect(textOf(byTestId(root, 'indicator-menu'))).toContain('One too many');
    await openIndicatorMenu(opened);
    expect(textOf(document.querySelector('[data-testid="indicator-one_too_many"]'))).toContain('new');
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

    expect(byTestId(panel, 'inputs-published')?.textContent).toMatch(/^\s*Flattened tables · published Oct 14, 2026/);
    expect(byTestId(panel, 'inputs-status')?.textContent).not.toContain('A saved script changes them only from the next run'); // that note is in the Reference tab now
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

    await pickIndicator(opened, 'coverage');
    http.expectOne(`${scriptsUrl}/indicators/coverage/versions`).flush([summary(coverage)]);
    http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'indicator').flush(
      scriptInputs({ source: 'clean', tables: [{ name: 'main', rows: 2, columns: [{ name: 'municipality', type: 'text' }] }] }),
    );
    await settle(harness);

    expect(byTestId(root, 'inputs-published')?.textContent).toContain('Clean tables · published');
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
    expect(problems.textContent).toContain('saving and testing are never blocked');
    expect(problems.textContent).toContain('Checks');
    byTestId<HTMLButtonElement>(root, 'problem')!.click();
    expect(document.activeElement).toBe(root.querySelector('.cm-content'));

    byTestId<HTMLButtonElement>(root, 'save')!.click();
    const save = http.expectOne((r) => r.url === `${scriptsUrl}/clean` && r.method === 'PUT');
    expect(save.request.body).toEqual({ code: 'def clean(raw, ctx):\n    x = (\n' });
  });

  it('warns about an import the worker does not have and still saves the script as a new version', async () => {
    const { root, http, harness, editor } = await open('analyst');
    const code = 'import statsmodels.api as sm\n\n\ndef clean(raw, ctx):\n    return raw\n';

    editor().replace(code);
    await new Promise((resolve) => setTimeout(resolve, 700));
    await settle(harness);

    const problems = byTestId(root, 'problems')!;
    expect(problems.textContent).toContain('statsmodels is not available to scripts');
    expect(problems.textContent).toContain('shapely');

    byTestId<HTMLButtonElement>(root, 'save')!.click();
    const save = http.expectOne((r) => r.url === `${scriptsUrl}/clean` && r.method === 'PUT');
    expect(save.request.body).toEqual({ code });
    save.flush(version(3, code));
    await settle(harness);
    http.expectOne(`${scriptsUrl}/clean/versions`).flush([summary(version(3)), summary(version(2)), summary(version(1))]);
    await settle(harness);

    expect(byTestId(root, 'script-clean')?.textContent).toContain('v3');
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
      const opened = await open('analyst');
      const { root, http, harness, editor } = opened;
      editor().replace(cellScript);
      await settle(harness);
      expect(root.querySelectorAll('.cm-run-cell')).toHaveLength(3);

      await openVersions(opened);
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

    await editDetails(opened);
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
    expect(tables).toContain('Flattened: main (320 rows), members (871 rows)');
    expect(tables).toContain('Clean v2: main (318 rows)');
    expect(tables).toContain('Reference: population (42 rows)');
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

    expect(byTestId(opened.root, 'download-tables')?.textContent).toContain('No script 1');
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

  // The restructured page

  describe('the picker', () => {
    it('shows script 1, script 2 and one indicators menu in a single row', async () => {
      const { root } = await open('analyst', { scripts: withIndicators(indicatorVersion('coverage', 1)) });
      const row = byTestId(root, 'script-clean')!.parentElement!;
      expect(row.contains(byTestId(root, 'script-output'))).toBe(true);
      expect(row.contains(byTestId(root, 'indicator-menu'))).toBe(true);
      expect(row.contains(byTestId(root, 'add-indicator'))).toBe(true);
      expect(textOf(byTestId(root, 'indicator-menu'))).toBe('Indicators (1)');
    });

    it('puts the version badge on the selected script only', async () => {
      const { root } = await open('analyst');
      expect(byTestId(root, 'script-clean')!.querySelector('tmz-badge')).not.toBeNull();
      expect(byTestId(root, 'script-output')!.querySelector('tmz-badge')).toBeNull();
    });

    it('filters a long list of indicators in the menu', async () => {
      const many = Array.from({ length: 12 }, (_, i) => indicatorVersion(`indicator_${String(i).padStart(2, '0')}`, 1));
      const opened = await open('viewer', { scripts: withIndicators(...many) });
      await openIndicatorMenu(opened);
      expect(indicatorEntries()).toHaveLength(12);

      const filter = document.querySelector<HTMLInputElement>('[data-testid="indicator-filter"]')!;
      typeInto(filter, 'indicator_07');
      await settle(opened.harness);
      expect(indicatorEntries()).toHaveLength(1);
      typeInto(filter, 'zzz');
      await settle(opened.harness);
      expect(document.querySelector('[data-testid="no-indicators"]')?.textContent).toContain('Nothing matches the filter');
    });

    it('offers no filter for a short list', async () => {
      const opened = await open('viewer', { scripts: withIndicators(indicatorVersion('coverage', 1)) });
      await openIndicatorMenu(opened);
      expect(document.querySelector('[data-testid="indicator-filter"]')).toBeNull();
    });
  });

  describe('around the editor', () => {
    it('shows the signature the script defines instead of a paragraph about its contract', async () => {
      const { root } = await open('analyst');
      expect(textOf(byTestId(root, 'contract-signature'))).toBe('clean(raw, ctx)');
      expect(root.textContent).not.toContain('receives the flattened tables and returns the cleaned ones');
    });

    it.each<[ProjectRole, boolean]>([
      ['analyst', true],
      ['viewer', false],
    ])('tells a %s about the cell shortcut: %s', async (role, shown) => {
      const { root } = await open(role);
      const keys = textOf(byTestId(root, 'editor-keys'));
      expect(keys).toContain('Press Esc, then Tab, to leave the editor');
      expect(keys.includes('Ctrl+Enter')).toBe(shown);
    });

    it('keeps save, the sample size, test and download in one row, with one hint', async () => {
      const { root } = await open('analyst');
      const form = byTestId(root, 'save')!.closest('form')!;
      for (const id of ['sample-size', 'test-on-sample', 'download-sample', 'template']) {
        expect(form.contains(byTestId(root, id)), id).toBe(true);
      }
      expect(root.querySelectorAll('[data-testid="test-hint"]')).toHaveLength(1);
      expect(root.textContent).not.toContain('prepares a zip with a sample');
    });

    it('keeps upload and remove in the more-actions menu', async () => {
      const opened = await open('analyst');
      expect(byTestId(opened.root, 'remove')).toBeNull();
      await openMore(opened);
      const items = [...document.querySelectorAll('[role="menuitem"]')].map((item) => textOf(item));
      expect(items).toEqual(['Upload .py', 'Remove script']);
    });

    it('opens the file picker from the upload item', async () => {
      const opened = await open('analyst');
      const input = byTestId<HTMLInputElement>(opened.root, 'upload')!;
      const click = vi.spyOn(input, 'click');
      await moreItem(opened, 'upload-item');
      expect(click).toHaveBeenCalledOnce();
    });

    it('shows the test result right under the editor card, before the side panel', async () => {
      const opened = await open('analyst');
      await startTest(opened);
      opened.http.expectOne((r) => r.url === testsUrl && r.method === 'POST').flush(scriptTest());
      await settle(opened.harness);

      const panel = byTestId(opened.root, 'test-panel')!;
      const editor = opened.root.querySelector('tmz-code-editor')!;
      const side = byTestId(opened.root, 'side-panel')!;
      expect(editor.compareDocumentPosition(panel) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(panel.compareDocumentPosition(side) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('does not repeat in words that a test succeeded', async () => {
      const opened = await open('analyst');
      await startTest(opened);
      opened.http
        .expectOne((r) => r.url === testsUrl && r.method === 'POST')
        .flush(scriptTest({ status: 'succeeded', steps: [flattenStep, { ...flattenStep, step: 'clean' }] }));
      await settle(opened.harness);
      expect(byTestId(opened.root, 'test-status')?.textContent).toContain('Succeeded');
      expect(byTestId(opened.root, 'test-success')).toBeNull();
    });

    it('no longer shows when the scripts run: that is set on the Submissions page', async () => {
      const { root } = await open('admin');
      expect(byTestId(root, 'pipeline-settings-form')).toBeNull();
    });
  });

  describe('the last run', () => {
    it('shows the newest run and, to editors, a button that starts another', async () => {
      const opened = await open('analyst');
      const { root, http, harness } = opened;
      const strip = byTestId(root, 'last-run')!;
      expect(textOf(byTestId(strip, 'last-run-status'))).toBe('Succeeded');
      expect(textOf(byTestId(strip, 'last-run-time'))).toMatch(/2026/);
      expect(byTestId(strip, 'last-run-failed')).toBeNull();
      expect(byTestId(strip, 'last-run-link')!.getAttribute('href')).toContain('#runs');

      byTestId<HTMLButtonElement>(strip, 'run-now')!.click();
      await settle(harness);
      http.expectOne((r) => r.url === runsUrl && r.method === 'POST').flush(run({ id: 'r2', status: 'queued' }));
      await settle(harness);
    });

    it('gives a viewer the run but not the button', async () => {
      const { root } = await open('viewer');
      expect(byTestId(root, 'last-run-status')).not.toBeNull();
      expect(byTestId(root, 'run-now')).toBeNull();
    });

    it('names the step that failed', async () => {
      const failed = run({
        id: 'f',
        status: 'failed',
        errorCode: 'step.indicator',
        error: "attendance: KeyError: 'age'",
        steps: [
          { step: 'flatten', name: '', status: 'succeeded', inputRows: 3, outputRows: 9 },
          { step: 'indicator', name: 'attendance', status: 'failed', inputRows: 8, outputRows: null },
        ],
      });
      const { root } = await open('viewer', { runs: { items: [failed], total: 1 } });
      expect(textOf(byTestId(root, 'last-run-status'))).toBe('Failed');
      expect(textOf(byTestId(root, 'last-run-failed'))).toBe('attendance failed');
    });

    it('says when there is no run yet', async () => {
      const { root } = await open('viewer', { runs: { items: [], total: 0 } });
      expect(textOf(byTestId(root, 'last-run-none'))).toBe('No runs yet.');
    });

    it('shows no step badges for a run where every step succeeded, and the badges for one that did not', async () => {
      const clean = run({
        id: 'ok',
        steps: [
          { step: 'flatten', name: '', status: 'succeeded', inputRows: 3, outputRows: 9 },
          { step: 'clean', name: '', status: 'succeeded', inputRows: 9, outputRows: 8 },
        ],
      });
      const bad = run({ id: 'bad', status: 'failed', errorCode: 'step.clean', error: 'x' });
      const { root } = await open('viewer', { runs: { items: [clean, bad], total: 2 } });
      const rows = byTestId(root, 'run-table')!.querySelectorAll('tbody tr');
      expect(textOf(rows[0])).toContain('All succeeded');
      expect(rows[0].querySelector('tmz-badge[data-testid="run-indicators"]')).toBeNull();
      expect(rows[1].querySelectorAll('tmz-badge').length).toBeGreaterThan(2);
    });

    it('names the versions of script 1 and script 2 in the details of a run', async () => {
      const { root, http, harness } = await open('viewer');
      byTestId<HTMLButtonElement>(root, 'run-details-r1')!.click();
      await settle(harness);
      http.expectOne(`${runsUrl}/r1`).flush({ run: run(), steps: [], referenceTables: [] });
      await settle(harness);
      expect(textOf(byTestId(document.querySelector('.cdk-overlay-container')!, 'run-script-versions'))).toBe('Script 1 v2');
    });
  });

  describe('the side panel', () => {
    it('has the versions as its third tab, not as a card of their own', async () => {
      const opened = await open('analyst');
      expect([...opened.root.querySelectorAll('[role="tab"]')].map((tab) => textOf(tab))).toEqual(['Tables', 'Reference', 'Versions']);
      expect(byTestId(opened.root, 'versions')).toBeNull();
      await openVersions(opened);
      expect([...byTestId(opened.root, 'versions')!.querySelectorAll('li')]).toHaveLength(2);
    });

    it('offers analysts a link to manage the reference tables, and viewers none', async () => {
      const analyst = await open('analyst');
      expect(byTestId(analyst.root, 'inputs-manage-reference')!.getAttribute('href')).toBe(`/projects/${projectId}/reference`);
      TestBed.resetTestingModule();

      const viewer = await open('viewer');
      expect(byTestId(viewer.root, 'inputs-manage-reference')).toBeNull();
    });

    it('keeps the questions in a closed disclosure that a filter opens', async () => {
      const { root, harness } = await open('analyst');
      const questions = byTestId(root, 'inputs-questions')!;
      expect((questions.querySelector('details') as HTMLDetailsElement).open).toBe(false);

      typeInto(byTestId<HTMLInputElement>(root, 'inputs-filter')!, 'municipality');
      await settle(harness);
      expect((questions.querySelector('details') as HTMLDetailsElement).open).toBe(true);
    });

    it('says in the Reference tab that the tables lag a saved script until the next run', async () => {
      const { root, harness } = await open('analyst');
      byTestId<HTMLButtonElement>(root, 'side-tab-reference')!.click();
      await settle(harness);
      expect(textOf(byTestId(root, 'reference-tables-note'))).toContain('only from the next run');
    });

    it('puts a link to the reference data next to a check about a missing reference table', async () => {
      const opened = await open('analyst', { inputs: scriptInputs({ referenceTables: [] }) });
      opened.editor().replace('def clean(raw, ctx):\n    return ctx.reference["population"]\n');
      await new Promise((resolve) => setTimeout(resolve, 700));
      await settle(opened.harness);
      expect(byTestId(opened.root, 'problem-reference-link')?.getAttribute('href')).toBe(`/projects/${projectId}/reference`);
    });
  });

  describe('the Assistant tab', () => {
    const tabNames = (root: HTMLElement) => [...root.querySelectorAll('[role="tab"]')].map((tab) => textOf(tab));

    it('is not there when the deployment has no assistant (Not configured)', async () => {
      const opened = await open('analyst', { assistant: false });
      expect(tabNames(opened.root)).toEqual(['Tables', 'Reference', 'Versions']);
      expect(byTestId(opened.root, 'side-tab-assistant')).toBeNull();
    });

    it('is not there while the configuration has not arrived, or when it cannot be read', async () => {
      const waiting = await open('analyst');
      expect(tabNames(waiting.root)).toEqual(['Tables', 'Reference', 'Versions']);
      TestBed.resetTestingModule();

      const failed = await open('analyst');
      failed.http.match(SYSTEM_CONFIG_URL).forEach((r) => r.flush(null, { status: 500, statusText: 'Server Error' }));
      await settle(failed.harness);
      expect(tabNames(failed.root)).toEqual(['Tables', 'Reference', 'Versions']);
    });

    it('is the fourth tab for an analyst (Analyst)', async () => {
      const opened = await open('analyst', { assistant: true });
      await settle(opened.harness);
      expect(tabNames(opened.root)).toEqual(['Tables', 'Reference', 'Versions', 'Assistant']);
    });

    it('is not shown to a viewer, even when the deployment has the assistant (Viewer)', async () => {
      const opened = await open('viewer', { assistant: true });
      await settle(opened.harness);
      expect(tabNames(opened.root)).toEqual(['Tables', 'Reference', 'Versions']);
    });

    it('keeps Versions working as its own tab with the assistant present', async () => {
      const opened = await open('analyst', { assistant: true });
      await openVersions(opened);
      expect([...byTestId(opened.root, 'versions')!.querySelectorAll('li')]).toHaveLength(2);
      expect(byTestId(opened.root, 'assistant-panel')).toBeNull();
    });

    it('shows the panel when the tab is chosen, and Tables again when the tab disappears while selected', async () => {
      const assistantEnabled = signal(true);
      const opened = await open('analyst', { providers: [{ provide: SystemConfigService, useValue: { assistantEnabled } }] });
      await settle(opened.harness);
      byTestId<HTMLButtonElement>(opened.root, 'side-tab-assistant')!.click();
      await settle(opened.harness);
      opened.http.expectOne(`/api/v1/projects/${projectId}/assistant`).flush({ model: 'm', host: 'h' });
      await settle(opened.harness);
      expect(byTestId(opened.root, 'assistant-panel')).not.toBeNull();
      expect(byTestId(opened.root, 'side-tab-assistant')!.getAttribute('aria-selected')).toBe('true');

      assistantEnabled.set(false);
      await settle(opened.harness);

      expect(tabNames(opened.root)).toEqual(['Tables', 'Reference', 'Versions']);
      expect(byTestId(opened.root, 'assistant-panel')).toBeNull();
      expect(byTestId(opened.root, 'side-tab-inputs')!.getAttribute('aria-selected')).toBe('true');
      expect(byTestId(opened.root, 'inputs-panel')).not.toBeNull();
    });
  });

  describe('the sample download strip', () => {
    it('explains what the zip is while it is prepared', async () => {
      const opened = await open('analyst');
      await requestDownload(opened);
      opened.http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST').flush(sampleDownload());
      await settle(opened.harness);
      expect(textOf(byTestId(opened.root, 'download-explanation'))).toContain('small package to check scripts on your own computer');
    });

    it('lists the tables of the zip on one line', async () => {
      const opened = await open('analyst');
      await requestDownload(opened);
      opened.http.expectOne((r) => r.url === downloadsUrl && r.method === 'POST').flush(readyDownload());
      await settle(opened.harness);
      expect(byTestId(opened.root, 'download-tables')!.tagName).toBe('P');
      expect(byTestId(opened.root, 'download-tables')!.querySelectorAll('li')).toHaveLength(0);
    });
  });

  describe('the Assistant session on the page', () => {
    const assistantUrl = `/api/v1/projects/${projectId}/assistant`;
    const completionsUrl = `${assistantUrl}/completions`;
    const outcomeUrl = (callId: string) => `${assistantUrl}/calls/${callId}/outcome`;

    /** A script of two cells: `Rate` holds the line `main["x"] = 1`, `Out` the line that returns. */
    const cellScript = ['def clean(raw, ctx):', '    main = raw["main"]', '    # %% Rate', '    main["x"] = 1', '    # %% Out', '    return raw', ''].join('\n');
    const rateCell = '    # %% Rate\n    main["x"] = 1';
    const newRate = '    # %% Rate\n    main["x"] = 2';
    const proposedScript = 'def clean(raw, ctx):\n    return raw  # proposed\n';
    const savedWith = (code: string): ScriptList => ({ clean: { kind: 'clean', current: version(2, code) }, output: { kind: 'output', current: null }, indicators: [] });

    beforeEach(() => sessionStorage.clear());
    afterEach(() => sessionStorage.clear());

    function answer(overrides: Partial<AssistantAnswer> = {}): AssistantAnswer {
      return {
        callId: 'c1',
        explanation: 'Here is the script. [proposed code: 2 lines]',
        blocks: [{ code: proposedScript, complete: true }],
        truncated: false,
        testExpired: false,
        omitted: [],
        usage: { promptTokens: 10, completionTokens: 5, totalTokens: 15, estimated: false },
        ...overrides,
      };
    }

    /** Opens the Scripts page of an analyst with the Assistant tab on view. */
    async function openAssistant(seed: Seed = {}) {
      const opened = await open('analyst', { assistant: true, ...seed });
      await settle(opened.harness);
      await showAssistantTab(opened);
      return opened;
    }

    async function showAssistantTab(opened: Opened) {
      byTestId<HTMLButtonElement>(opened.root, 'side-tab-assistant')!.click();
      await settle(opened.harness);
      opened.http.match(assistantUrl).forEach((r) => r.flush({ model: 'gemini-3.8-flash', host: 'generativelanguage.googleapis.com' }));
      await settle(opened.harness);
    }

    async function showTab(opened: Opened, tab: 'inputs' | 'assistant') {
      if (tab === 'assistant') {
        await showAssistantTab(opened);
      } else {
        byTestId<HTMLButtonElement>(opened.root, `side-tab-${tab}`)!.click();
        await settle(opened.harness);
      }
    }

    /** Types a request and sends it; returns the request the page made. */
    async function ask(opened: Opened, text: string) {
      typeInto(byTestId<HTMLTextAreaElement>(opened.root, 'assistant-request') as unknown as HTMLInputElement, text);
      await settle(opened.harness);
      byTestId<HTMLButtonElement>(opened.root, 'assistant-send')!.click();
      await settle(opened.harness);
      return opened.http.expectOne(completionsUrl);
    }

    async function reply(opened: Opened, request: TestRequest, body: AssistantAnswer) {
      request.flush(body);
      await settle(opened.harness);
    }

    async function fail(opened: Opened, request: TestRequest, status: number, code: string) {
      request.flush({ title: 'x', status, code }, { status, statusText: 'Failed' });
      await settle(opened.harness);
    }

    async function chooseCell(opened: Opened, line = 'main["x"] = 1') {
      opened.editor().goTo(opened.editor().text().indexOf(line));
      await settle(opened.harness);
      byTestId<HTMLButtonElement>(opened.root, 'assistant-scope-cell')!.click();
      await settle(opened.harness);
    }

    const box = (opened: Opened) => byTestId<HTMLTextAreaElement>(opened.root, 'assistant-request')!;
    const click = async (opened: Opened, id: string) => {
      byTestId<HTMLButtonElement>(opened.root, id)!.click();
      await settle(opened.harness);
    };
    const confirmApply = async (opened: Opened) => {
      confirmDialog()!.click();
      await tick();
      await settle(opened.harness);
    };
    const outcomeOf = (opened: Opened, callId: string) => {
      const request = opened.http.expectOne(outcomeUrl(callId));
      expect(request.request.method).toBe('PUT');
      request.flush(null, { status: 204, statusText: 'No Content' });
      return request.request.body;
    };

    function undoInEditor() {
      document.querySelector('.cm-content')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, cancelable: true }));
    }

    /** Stores a conversation as an earlier page of the same tab would have left it. */
    function keepConversation(script: ScriptRef, conversation: Partial<StoredConversation>) {
      const entry = { v: 1, touched: 1, thread: [], text: '', scope: 'script', proposal: null, ...conversation };
      sessionStorage.setItem(conversationKey('user-1', projectId, script), JSON.stringify(entry));
    }

    describe('applying a proposal', () => {
      it('replaces the script as one edit that undo reverts, and records how (Apply and undo)', async () => {
        const opened = await openAssistant();
        const saved = opened.editor().text();
        await reply(opened, await ask(opened, 'write the script'), answer());

        await click(opened, 'assistant-apply-script-0');
        expect(opened.editor().text()).toBe(proposedScript);
        expect(outcomeOf(opened, 'c1')).toEqual({ applied: 'script' });

        undoInEditor();
        await settle(opened.harness);
        expect(opened.editor().text()).toBe(saved);
      });

      it('leaves the script unsaved and the last test out of date, and saves and tests nothing (Not saved, not tested)', async () => {
        const opened = await openAssistant();
        await startTest(opened);
        opened.http.expectOne((r) => r.url === testsUrl && r.method === 'POST').flush(scriptTest({ status: 'succeeded', steps: [flattenStep] }));
        await settle(opened.harness);
        expect(byTestId(opened.root, 'test-stale')).toBeNull();
        expect(byTestId(opened.root, 'unsaved')).not.toBeNull();

        await reply(opened, await ask(opened, 'write the script'), answer());
        await click(opened, 'assistant-apply-script-0');
        outcomeOf(opened, 'c1');

        expect(byTestId(opened.root, 'unsaved')).not.toBeNull();
        expect(byTestId(opened.root, 'test-stale')).not.toBeNull();
        opened.http.expectNone((r) => r.method !== 'GET' && r.url !== outcomeUrl('c1'));
      });

      it('offers only insertion for code that does not define the contract function, and inserts it at the cursor (Partial proposal)', async () => {
        const opened = await openAssistant();
        const fragment = 'rate = 1\n';
        await reply(opened, await ask(opened, 'compute the rate'), answer({ blocks: [{ code: fragment, complete: true }] }));

        expect(byTestId(opened.root, 'assistant-apply-script-0')).toBeNull();
        expect(textOf(byTestId(opened.root, 'assistant-block-summary'))).toContain('Does not define clean(raw, ctx)');
        const saved = opened.editor().text();
        opened.editor().goTo(0);
        await click(opened, 'assistant-apply-insert-0');
        expect(opened.editor().text()).toBe(fragment + saved);
        expect(outcomeOf(opened, 'c1')).toEqual({ applied: 'insert' });
      });

      it('says an answer was cut off and offers its code only for insertion (Answer cut off)', async () => {
        const opened = await openAssistant();
        await reply(opened, await ask(opened, 'write it'), answer({ truncated: true, blocks: [{ code: 'def clean(raw, ctx):', complete: false }] }));

        expect(byTestId(opened.root, 'assistant-note-cutOff')).not.toBeNull();
        expect(byTestId(opened.root, 'assistant-apply-script-0')).toBeNull();
        expect(byTestId(opened.root, 'assistant-apply-insert-0')).not.toBeNull();
      });

      it('offers each of several blocks only for insertion, with a note (Several blocks)', async () => {
        const opened = await openAssistant();
        await reply(
          opened,
          await ask(opened, 'write it'),
          answer({
            blocks: [
              { code: proposedScript, complete: true },
              { code: 'def clean(raw, ctx):\n    return raw  # second\n', complete: true },
            ],
          }),
        );

        expect(byTestId(opened.root, 'assistant-note-notOneScript')).not.toBeNull();
        for (const block of [0, 1]) {
          expect(byTestId(opened.root, `assistant-apply-script-${block}`)).toBeNull();
          expect(byTestId(opened.root, `assistant-apply-insert-${block}`)).not.toBeNull();
        }
      });

      it('asks for confirmation when the text changed while the answer was written, and applies only when confirmed (Edited while waiting)', async () => {
        const opened = await openAssistant();
        const request = await ask(opened, 'write the script');
        opened.editor().replace(opened.editor().text() + '# typed while waiting\n');
        await settle(opened.harness);
        await reply(opened, request, answer());

        await click(opened, 'assistant-apply-script-0');
        expect(document.body.textContent).toContain('Apply the proposed code?');
        expect(opened.editor().text()).toContain('# typed while waiting');
        (document.querySelector('.cdk-overlay-container button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
        await tick();
        await settle(opened.harness);
        expect(opened.editor().text()).toContain('# typed while waiting');
        opened.http.expectNone(outcomeUrl('c1'));

        await click(opened, 'assistant-apply-script-0');
        await confirmApply(opened);
        expect(opened.editor().text()).toBe(proposedScript);
        expect(outcomeOf(opened, 'c1')).toEqual({ applied: 'script' });
      });

      it('does not ask when the text is the one the request was sent with', async () => {
        const opened = await openAssistant();
        await reply(opened, await ask(opened, 'write the script'), answer());
        await click(opened, 'assistant-apply-script-0');
        expect(confirmDialog()).toBeNull();
      });

      it('shows the checks on the proposal, such as an unavailable import, and still lets it be applied (Proposal with an unavailable import)', async () => {
        const opened = await openAssistant();
        const withScipy = 'import scipy.stats\n\ndef clean(raw, ctx):\n    return raw\n';
        await reply(opened, await ask(opened, 'test it'), answer({ blocks: [{ code: withScipy, complete: true }] }));

        const marks = textOf(byTestId(opened.root, 'assistant-marks'));
        expect(marks).toContain('scipy is not available to scripts');
        expect(marks).toContain('Line 1');
        await click(opened, 'assistant-apply-script-0');
        expect(opened.editor().text()).toBe(withScipy);
      });
    });

    describe('rewriting a cell', () => {
      it('replaces only the cell at the cursor and records it (Rewrite a cell)', async () => {
        const opened = await openAssistant({ scripts: savedWith(cellScript) });
        await chooseCell(opened);
        const request = await ask(opened, 'set it to two');
        expect(request.request.body).toMatchObject({ action: 'cell', focus: { line: 3 }, draft: cellScript });
        await reply(opened, request, answer({ blocks: [{ code: newRate, complete: true }] }));

        expect(byTestId(opened.root, 'assistant-apply-cell-0')).not.toBeNull();
        await click(opened, 'assistant-apply-cell-0');
        expect(opened.editor().text()).toBe(cellScript.replace(rateCell, newRate));
        expect(outcomeOf(opened, 'c1')).toEqual({ applied: 'cell' });

        undoInEditor();
        await settle(opened.harness);
        expect(opened.editor().text()).toBe(cellScript);
      });

      it('offers only insertion when the answer to a cell request is not one cell (Answer that is not one cell)', async () => {
        const opened = await openAssistant({ scripts: savedWith(cellScript) });
        await chooseCell(opened);
        const two = `${newRate}\n    # %% Other\n    main["y"] = 1`;
        await reply(opened, await ask(opened, 'rewrite'), answer({ blocks: [{ code: two, complete: true }] }));

        expect(byTestId(opened.root, 'assistant-note-notOneCell')).not.toBeNull();
        expect(byTestId(opened.root, 'assistant-apply-cell-0')).toBeNull();
        expect(byTestId(opened.root, 'assistant-apply-insert-0')).not.toBeNull();
      });

      it('withdraws Replace cell when the cell was edited while the answer was written (Cell edited while waiting)', async () => {
        const opened = await openAssistant({ scripts: savedWith(cellScript) });
        await chooseCell(opened);
        const request = await ask(opened, 'rewrite');
        opened.editor().replace(cellScript.replace('main["x"] = 1', 'main["x"] = 1  # edited'));
        await settle(opened.harness);
        await reply(opened, request, answer({ blocks: [{ code: newRate, complete: true }] }));

        expect(byTestId(opened.root, 'assistant-note-cellChanged')).not.toBeNull();
        expect(byTestId(opened.root, 'assistant-apply-cell-0')).toBeNull();
        expect(byTestId(opened.root, 'assistant-apply-insert-0')).not.toBeNull();
      });

      it('withdraws Replace cell when the marker was removed while the answer was written', async () => {
        const opened = await openAssistant({ scripts: savedWith(cellScript) });
        await chooseCell(opened);
        const request = await ask(opened, 'rewrite');
        opened.editor().replace(cellScript.replace('    # %% Rate\n', ''));
        await settle(opened.harness);
        await reply(opened, request, answer({ blocks: [{ code: newRate, complete: true }] }));

        expect(byTestId(opened.root, 'assistant-note-cellChanged')).not.toBeNull();
        expect(byTestId(opened.root, 'assistant-apply-cell-0')).toBeNull();
      });

      it('still replaces the cell after a line was added above it (Line added above while waiting)', async () => {
        const opened = await openAssistant({ scripts: savedWith(cellScript) });
        await chooseCell(opened);
        const request = await ask(opened, 'rewrite');
        const withImport = 'import pandas as pd\n' + cellScript;
        opened.editor().replace(withImport);
        await settle(opened.harness);
        await reply(opened, request, answer({ blocks: [{ code: newRate, complete: true }] }));

        expect(byTestId(opened.root, 'assistant-apply-cell-0')).not.toBeNull();
        await click(opened, 'assistant-apply-cell-0');
        await confirmApply(opened);
        expect(opened.editor().text()).toBe(withImport.replace(rateCell, newRate));
      });
    });

    describe('while a request is being written', () => {
      it('keeps writing while another side tab is shown, and offers the answer that arrived meanwhile (Another side tab while writing)', async () => {
        const opened = await openAssistant();
        const request = await ask(opened, 'write the script');
        await showTab(opened, 'inputs');
        expect(byTestId(opened.root, 'assistant-panel')).toBeNull();

        await showTab(opened, 'assistant');
        expect(byTestId(opened.root, 'assistant-writing')).not.toBeNull();
        expect(textOf(byTestId(opened.root, 'assistant-turn-pending'))).toContain('write the script');

        await showTab(opened, 'inputs');
        await reply(opened, request, answer());
        await showTab(opened, 'assistant');
        expect(byTestId(opened.root, 'assistant-writing')).toBeNull();
        expect(byTestId(opened.root, 'assistant-turn-assistant')).not.toBeNull();
        expect(byTestId(opened.root, 'assistant-apply-script-0')).not.toBeNull();
      });

      it('keeps the typed request and the scope across side tabs (Typed request kept across side tabs)', async () => {
        const opened = await openAssistant({ scripts: savedWith(cellScript) });
        await chooseCell(opened);
        typeInto(box(opened) as unknown as HTMLInputElement, 'look at the age column');
        await settle(opened.harness);

        await showTab(opened, 'inputs');
        await showTab(opened, 'assistant');

        expect(box(opened).value).toBe('look at the age column');
        expect(byTestId(opened.root, 'assistant-scope-cell')!.getAttribute('aria-pressed')).toBe('true');
      });

      it('says a failure on the script it was sent for, and puts its text back, whichever side tab is shown (Failure while away)', async () => {
        const opened = await openAssistant();
        const request = await ask(opened, 'write the script');
        await showTab(opened, 'inputs');
        await fail(opened, request, 504, 'assistant.timeout');
        await showTab(opened, 'assistant');

        expect(textOf(byTestId(opened.root, 'assistant-failure-message'))).toContain('did not answer in time');
        expect(box(opened).value).toBe('write the script');
        expect(byTestId(opened.root, 'assistant-turn-user')).toBeNull();
      });

      it('writes the answer into the script it was sent for and shows the other script that a request is in progress (Another script while writing)', async () => {
        const opened = await openAssistant();
        const request = await ask(opened, 'write script one');
        await click(opened, 'script-output');
        opened.http.expectOne(`${scriptsUrl}/output/versions`).flush([]);
        opened.http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'output').flush(scriptInputs({ source: 'clean' }));
        await settle(opened.harness);

        expect(textOf(byTestId(opened.root, 'assistant-pending-elsewhere'))).toContain('A request for script 1 is in progress');
        expect(byTestId<HTMLButtonElement>(opened.root, 'assistant-send')!.disabled).toBe(true);
        expect(box(opened).value).toBe('');

        await reply(opened, request, answer());
        expect(byTestId(opened.root, 'assistant-thread')).toBeNull();
        expect(byTestId(opened.root, 'assistant-proposal')).toBeNull();

        await click(opened, 'script-clean');
        opened.http.expectOne(`${scriptsUrl}/clean/versions`).flush([]);
        await settle(opened.harness);
        expect(textOf(byTestId(opened.root, 'assistant-turn-user'))).toContain('write script one');
        expect(byTestId(opened.root, 'assistant-apply-script-0')).not.toBeNull();
      });

      it('puts the text of a request that failed while another script is shown back in the first script only', async () => {
        const opened = await openAssistant();
        const request = await ask(opened, 'write script one');
        await click(opened, 'script-output');
        opened.http.expectOne(`${scriptsUrl}/output/versions`).flush([]);
        opened.http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'output').flush(scriptInputs({ source: 'clean' }));
        await settle(opened.harness);
        typeInto(box(opened) as unknown as HTMLInputElement, 'script two text');
        await settle(opened.harness);

        await fail(opened, request, 504, 'assistant.timeout');
        expect(box(opened).value).toBe('script two text');
        expect(byTestId(opened.root, 'assistant-failure')).toBeNull();

        await click(opened, 'script-clean');
        opened.http.expectOne(`${scriptsUrl}/clean/versions`).flush([]);
        await settle(opened.harness);
        expect(box(opened).value).toBe('write script one');
        expect(byTestId(opened.root, 'assistant-failure')).not.toBeNull();
      });

      it('cancels the request when the Scripts page is left (Leaving the Scripts page while writing)', async () => {
        const opened = await openAssistant();
        const request = await ask(opened, 'write the script');
        expect(request.cancelled).toBe(false);

        opened.harness.fixture.destroy();

        expect(request.cancelled).toBe(true);
      });

      it('puts the text back and does not wait any more after Cancel', async () => {
        const opened = await openAssistant();
        const request = await ask(opened, 'write the script');
        await click(opened, 'assistant-cancel');

        expect(request.cancelled).toBe(true);
        expect(byTestId(opened.root, 'assistant-writing')).toBeNull();
        expect(box(opened).value).toBe('write the script');
        expect(byTestId(opened.root, 'assistant-turn-user')).toBeNull();
      });

      it('does not offer a new conversation while the answer is written (New conversation while writing)', async () => {
        const opened = await openAssistant();
        await reply(opened, await ask(opened, 'first'), answer());
        const request = await ask(opened, 'second');

        expect(byTestId<HTMLButtonElement>(opened.root, 'assistant-new-conversation')!.disabled).toBe(true);
        await reply(opened, request, answer({ callId: 'c2' }));
        expect(byTestId<HTMLButtonElement>(opened.root, 'assistant-new-conversation')!.disabled).toBe(false);
      });
    });

    describe('the turns of a request', () => {
      it('adds nothing to the thread when a request fails, so the same message sent again appears once (Failed request)', async () => {
        const opened = await openAssistant();
        await fail(opened, await ask(opened, 'write the script'), 504, 'assistant.timeout');
        expect(byTestId(opened.root, 'assistant-thread')).toBeNull();
        expect(box(opened).value).toBe('write the script');

        const again = await (async () => {
          byTestId<HTMLButtonElement>(opened.root, 'assistant-send')!.click();
          await settle(opened.harness);
          return opened.http.expectOne(completionsUrl);
        })();
        expect(again.request.body.messages).toEqual([{ role: 'user', content: 'write the script' }]);
        await reply(opened, again, answer());

        expect(opened.root.querySelectorAll('[data-testid="assistant-turn-user"]')).toHaveLength(1);
        expect(opened.root.querySelectorAll('[data-testid="assistant-turn-assistant"]')).toHaveLength(1);
        expect(byTestId(opened.root, 'assistant-failure')).toBeNull();
      });

      it('sends the explanations of earlier answers, with the note in place of their code (Earlier answers)', async () => {
        const opened = await openAssistant();
        await reply(opened, await ask(opened, 'first'), answer({ callId: 'c1', explanation: 'First answer. [proposed code: 2 lines]' }));
        await reply(opened, await ask(opened, 'second'), answer({ callId: 'c2', explanation: 'Second answer. [proposed code: 2 lines]' }));
        const third = await ask(opened, 'third');

        expect(third.request.body.messages).toEqual([
          { role: 'user', content: 'first' },
          { role: 'assistant', content: 'First answer. [proposed code: 2 lines]' },
          { role: 'user', content: 'second' },
          { role: 'assistant', content: 'Second answer. [proposed code: 2 lines]' },
          { role: 'user', content: 'third' },
        ]);
      });

      it('leaves out the oldest pairs of a long conversation and starts with a turn of the analyst (Long conversation)', async () => {
        const turns: StoredConversation['thread'] = Array.from({ length: 50 }, (_, i) => ({ role: i % 2 === 0 ? 'user' : 'assistant', content: `turn ${i}` }));
        keepConversation({ kind: 'clean' }, { thread: turns });
        const opened = await openAssistant();
        expect(opened.root.querySelectorAll('[data-testid="assistant-thread"] li')).toHaveLength(50);

        const request = await ask(opened, 'one more');
        const messages = request.request.body.messages as { role: string; content: string }[];

        expect(messages).toHaveLength(39);
        expect(messages[0]).toEqual({ role: 'user', content: 'turn 12' });
        expect(messages.at(-1)).toEqual({ role: 'user', content: 'one more' });
        expect(messages.map((m) => m.role)).toEqual(Array.from({ length: 39 }, (_, i) => (i % 2 === 0 ? 'user' : 'assistant')));

        await reply(opened, request, answer());
        expect(opened.root.querySelectorAll('[data-testid="assistant-thread"] li')).toHaveLength(52);
      });

      it('leaves out pairs until the turns fit in 64 KB', async () => {
        const big = 'x'.repeat(15 * 1024);
        const turns: StoredConversation['thread'] = Array.from({ length: 10 }, (_, i) => ({ role: i % 2 === 0 ? 'user' : 'assistant', content: `${i}${big}` }));
        keepConversation({ kind: 'clean' }, { thread: turns });
        const opened = await openAssistant();

        const messages = (await ask(opened, 'next')).request.body.messages as { role: string; content: string }[];

        expect(messages[0].role).toBe('user');
        expect(new TextEncoder().encode(messages.map((m) => m.content).join('')).length).toBeLessThanOrEqual(64 * 1024);
        expect(messages.length).toBeLessThan(11);
        expect(messages.at(-1)!.content).toBe('next');
      });

      it('starts a new conversation without an earlier turn or a proposal (New conversation)', async () => {
        const opened = await openAssistant();
        await reply(opened, await ask(opened, 'first'), answer());
        expect(byTestId(opened.root, 'assistant-proposal')).not.toBeNull();

        await click(opened, 'assistant-new-conversation');
        expect(byTestId(opened.root, 'assistant-thread')).toBeNull();
        expect(byTestId(opened.root, 'assistant-proposal')).toBeNull();

        const next = await ask(opened, 'second');
        expect(next.request.body.messages).toEqual([{ role: 'user', content: 'second' }]);
      });
    });

    describe('the conversation kept in the browser session', () => {
      it('shows the conversation and its proposal again after a reload, and applies it as before (Reload)', async () => {
        const first = await openAssistant();
        await reply(first, await ask(first, 'write the script'), answer({ callId: 'c7' }));
        TestBed.resetTestingModule();

        const opened = await openAssistant();
        expect(textOf(byTestId(opened.root, 'assistant-turn-user'))).toContain('write the script');
        expect(textOf(byTestId(opened.root, 'assistant-turn-assistant'))).toContain('Here is the script.');
        await click(opened, 'assistant-apply-script-0');

        expect(confirmDialog()).toBeNull();
        expect(opened.editor().text()).toBe(proposedScript);
        expect(outcomeOf(opened, 'c7')).toEqual({ applied: 'script' });
      });

      it('asks before applying after a reload when the restored text is not the text the request was sent with', async () => {
        const first = await openAssistant();
        first.editor().replace(proposedScript + '# unsaved\n');
        await settle(first.harness);
        await reply(first, await ask(first, 'write the script'), answer({ callId: 'c8' }));
        TestBed.resetTestingModule();

        const opened = await openAssistant();
        await click(opened, 'assistant-apply-script-0');
        expect(document.body.textContent).toContain('Apply the proposed code?');
        await confirmApply(opened);
        expect(outcomeOf(opened, 'c8')).toEqual({ applied: 'script' });
      });

      it('finds the cell again after a reload, or withdraws Replace cell with a note when it is gone', async () => {
        const first = await openAssistant({ scripts: savedWith(cellScript) });
        await chooseCell(first);
        await reply(first, await ask(first, 'rewrite'), answer({ callId: 'c9', blocks: [{ code: newRate, complete: true }] }));
        TestBed.resetTestingModule();

        const found = await openAssistant({ scripts: savedWith(cellScript) });
        await click(found, 'assistant-apply-cell-0');
        expect(found.editor().text()).toBe(cellScript.replace(rateCell, newRate));
        expect(outcomeOf(found, 'c9')).toEqual({ applied: 'cell' });
        TestBed.resetTestingModule();

        const gone = await openAssistant({ scripts: savedWith(cellScript.replace('main["x"] = 1', 'main["x"] = 5')) });
        expect(byTestId(gone.root, 'assistant-note-cellChanged')).not.toBeNull();
        expect(byTestId(gone.root, 'assistant-apply-cell-0')).toBeNull();
        expect(byTestId(gone.root, 'assistant-apply-insert-0')).not.toBeNull();
      });

      it('keeps the request box and the cell scope after a reload', async () => {
        const first = await openAssistant({ scripts: savedWith(cellScript) });
        await chooseCell(first);
        typeInto(box(first) as unknown as HTMLInputElement, 'half written');
        await settle(first.harness);
        TestBed.resetTestingModule();

        const opened = await openAssistant({ scripts: savedWith(cellScript) });
        expect(box(opened).value).toBe('half written');
        opened.editor().goTo(opened.editor().text().indexOf('main["x"] = 1'));
        await settle(opened.harness);
        expect(byTestId(opened.root, 'assistant-scope-cell')!.getAttribute('aria-pressed')).toBe('true');
      });

      it('keeps the text of a request still being written when the page is reloaded', async () => {
        const first = await openAssistant();
        await ask(first, 'write the script');
        TestBed.resetTestingModule();

        const opened = await openAssistant();
        expect(box(opened).value).toBe('write the script');
      });

      it('keeps a conversation per script (Another script)', async () => {
        const opened = await openAssistant();
        await reply(opened, await ask(opened, 'about script one'), answer());

        await click(opened, 'script-output');
        opened.http.expectOne(`${scriptsUrl}/output/versions`).flush([]);
        opened.http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'output').flush(scriptInputs({ source: 'clean' }));
        await settle(opened.harness);
        expect(byTestId(opened.root, 'assistant-thread')).toBeNull();
        expect(byTestId(opened.root, 'assistant-proposal')).toBeNull();
        await reply(opened, await ask(opened, 'about script two'), answer({ callId: 'c2', explanation: 'Two. [proposed code: 2 lines]' }));
        expect(textOf(byTestId(opened.root, 'assistant-thread'))).not.toContain('about script one');

        await click(opened, 'script-clean');
        opened.http.expectOne(`${scriptsUrl}/clean/versions`).flush([]);
        await settle(opened.harness);
        expect(textOf(byTestId(opened.root, 'assistant-thread'))).toContain('about script one');
        expect(textOf(byTestId(opened.root, 'assistant-thread'))).not.toContain('about script two');
        expect([...Object.keys(sessionStorage)].filter((key) => key.startsWith('tamiza.assistant.')).sort()).toEqual([
          `tamiza.assistant.user-1.${projectId}.clean`,
          `tamiza.assistant.user-1.${projectId}.output`,
        ]);
      });

      it('removes a conversation that was started anew from the stored keys', async () => {
        const opened = await openAssistant();
        await reply(opened, await ask(opened, 'first'), answer());
        expect(sessionStorage.getItem(`tamiza.assistant.user-1.${projectId}.clean`)).not.toBeNull();

        await click(opened, 'assistant-new-conversation');

        expect(sessionStorage.getItem(`tamiza.assistant.user-1.${projectId}.clean`)).toBeNull();
      });

      it('keeps the conversation in memory when the store is full', async () => {
        const opened = await openAssistant();
        const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
          throw new DOMException('full', 'QuotaExceededError');
        });
        try {
          await reply(opened, await ask(opened, 'write the script'), answer());
          expect(textOf(byTestId(opened.root, 'assistant-turn-user'))).toContain('write the script');
          expect(byTestId(opened.root, 'assistant-apply-script-0')).not.toBeNull();
        } finally {
          setItem.mockRestore();
        }
      });

      it('leaves other keys of the tab, such as the sign-in library state, alone', async () => {
        sessionStorage.setItem('0-tamiza-ui', '{"authnResult":"x"}');
        const opened = await openAssistant();
        await reply(opened, await ask(opened, 'write the script'), answer());

        expect(sessionStorage.getItem('0-tamiza-ui')).toBe('{"authnResult":"x"}');
      });
    });

    describe('fixing from the test result', () => {
      const coverage = indicatorVersion('coverage', 1, { format: 'percent', unit: '' });
      const other = indicatorVersion('other', 1, { format: 'percent', unit: '' });
      const cleanStep: TestStep = { ...flattenStep, step: 'clean', tables: [], log: '' };
      const failedStep = (step: TestStep['step'], error: string): TestStep => ({
        ...flattenStep,
        step,
        status: 'failed',
        tables: [],
        error,
        log: 'INFO Dropped 3 test submissions of Ana Gomez',
      });
      const keyError = "KeyError: 'water_source'\n\nTraceback (most recent call last):\n  File \"script.py\", line 4, in compute";
      /** The facts of a dashboard view whose largest value is 87.5 and that holds 3 duplicate rows. */
      const facts = {
        measures: ['value'],
        defaultMeasure: 'value',
        ratio: false,
        dimensions: ['municipality'],
        defaultDimension: 'municipality',
        geometry: [],
        rows: 4,
        defaultMeasureNulls: 0,
        defaultMeasureMax: 87.5,
        totals: { value: 120.5 },
        duplicateRows: 3,
        charts: { municipality: { values: 1, groups: [{ group: 'Cali', value: 87.5, numerator: null, denominator: null }] } },
      };
      const indicatorTest = (overrides: Partial<ScriptTest>): ScriptTest =>
        scriptTest({ kind: 'indicator', name: 'coverage', code: draftCode, ...overrides });
      const scrolled = vi.fn();
      const askButtons = (root: HTMLElement) => [...root.querySelectorAll('[data-testid="test-step-ask"], [data-testid="view-warning-ask"]')];
      const sentence = (step = 'indicator coverage') => `The last test failed in ${step}. Please fix the error.`;

      beforeEach(() => {
        scrolled.mockClear();
        Element.prototype.scrollIntoView = scrolled;
      });

      /** Selects the indicator `coverage`, tests it and has the API answer with `test`; the Assistant tab stays on view. */
      async function testedIndicator(test: Partial<ScriptTest>, scripts = withIndicators(coverage)) {
        const opened = await openAssistant({ scripts });
        await selectIndicator(opened, coverage);
        opened.http.expectOne((r) => r.url === inputsUrl && r.params.get('kind') === 'indicator').flush(scriptInputs({ source: 'clean' }));
        await settle(opened.harness);
        opened.editor().replace(draftCode);
        await settle(opened.harness);
        await click(opened, 'test-on-sample');
        opened.http.expectOne((r) => r.url === testsUrl && r.method === 'POST').flush(indicatorTest(test));
        await settle(opened.harness);
        return opened;
      }

      const failedIndicator = () => testedIndicator({ status: 'failed', errorCode: 'step.indicator', error: keyError, steps: [flattenStep, cleanStep, failedStep('indicator', keyError)] });
      const warnedIndicator = () => testedIndicator({ status: 'succeeded', steps: [flattenStep, cleanStep, { ...flattenStep, step: 'indicator', tables: [], dashboard: facts }] });

      it('opens the Assistant with the Fix scope for the failed step, scrolled into view with the focus on the box (Fix a failed test)', async () => {
        const opened = await failedIndicator();
        await showTab(opened, 'inputs');
        scrolled.mockClear();

        await click(opened, 'test-step-ask');
        await tick();
        await settle(opened.harness);

        expect(byTestId(opened.root, 'side-tab-assistant')!.getAttribute('aria-selected')).toBe('true');
        expect(byTestId(opened.root, 'assistant-scope-fix')!.getAttribute('aria-pressed')).toBe('true');
        expect(box(opened).value).toBe(sentence());
        expect(document.activeElement).toBe(box(opened));
        expect(scrolled).toHaveBeenCalledTimes(1);
        expect(scrolled.mock.contexts[0]).toBe(opened.root.querySelector('app-script-side-panel'));
        expect(scrolled).toHaveBeenCalledWith({ block: 'start' });
      });

      it('sends the fixed sentence with the test, and nothing of the error or the log (Fix with nothing typed)', async () => {
        const opened = await failedIndicator();
        await click(opened, 'test-step-ask');

        const request = await (async () => {
          await click(opened, 'assistant-send');
          return opened.http.expectOne(completionsUrl);
        })();

        expect(request.request.body).toMatchObject({ action: 'fix', testId: 't1', messages: [{ role: 'user', content: sentence() }] });
        expect(request.request.body.warning).toBeUndefined();
        const sent = JSON.stringify(request.request.body.messages);
        expect(sent).not.toContain('KeyError');
        expect(sent).not.toContain('water_source');
        expect(sent).not.toContain('Ana');
      });

      it('offers the button on the failed step of script 1 for a test of script 1', async () => {
        const opened = await openAssistant();
        await startTest(opened);
        opened.http
          .expectOne((r) => r.url === testsUrl && r.method === 'POST')
          .flush(scriptTest({ status: 'failed', errorCode: 'step.clean', error: keyError, steps: [flattenStep, failedStep('clean', keyError)] }));
        await settle(opened.harness);

        await click(opened, 'test-step-ask');

        expect(box(opened).value).toBe(sentence('script 1'));
        expect(byTestId(opened.root, 'assistant-scope-fix')!.getAttribute('aria-pressed')).toBe('true');
      });

      it('names the warning asked about and keeps its figures out of the request (Dashboard warning)', async () => {
        const opened = await warnedIndicator();
        expect(askButtons(opened.root)).toHaveLength(2);

        (askButtons(opened.root)[0] as HTMLButtonElement).click();
        await settle(opened.harness);
        expect(box(opened).value).toContain('values of the default measure are above 1');
        await click(opened, 'assistant-send');
        const request = opened.http.expectOne(completionsUrl);

        expect(request.request.body).toMatchObject({ action: 'fix', testId: 't1', warning: { key: 'percentAboveOne', params: {} } });
        expect(JSON.stringify(request.request.body)).not.toContain('87.5');
      });

      it('keeps the counts a warning names and drops the rest', async () => {
        const opened = await warnedIndicator();

        (askButtons(opened.root)[1] as HTMLButtonElement).click();
        await settle(opened.harness);
        await click(opened, 'assistant-send');

        expect(opened.http.expectOne(completionsUrl).request.body.warning).toEqual({ key: 'duplicateRows', params: { count: 3 } });
      });

      it('offers nothing on the saved script 1 step, because the indicator did not run (Saved script 1 failed)', async () => {
        const opened = await testedIndicator({ status: 'failed', errorCode: 'step.clean', error: keyError, steps: [flattenStep, failedStep('clean', keyError)] });

        expect(byTestId(opened.root, 'test-step-error')).not.toBeNull();
        expect(askButtons(opened.root)).toHaveLength(0);
      });

      it('withdraws the buttons and says to test again when the text changed since the test (Text changed since the test)', async () => {
        const failed = await failedIndicator();
        expect(askButtons(failed.root)).toHaveLength(1);
        failed.editor().replace(draftCode + '# edited\n');
        await settle(failed.harness);
        expect(askButtons(failed.root)).toHaveLength(0);
        expect(textOf(byTestId(failed.root, 'test-stale'))).toContain('Test again before asking the assistant for a fix.');
        TestBed.resetTestingModule();

        const warned = await warnedIndicator();
        expect(askButtons(warned.root)).toHaveLength(2);
        warned.editor().replace(draftCode + '# edited\n');
        await settle(warned.harness);
        expect(askButtons(warned.root)).toHaveLength(0);
      });

      it('leaves the Fix scope and says to test again when the text is edited after asking (Edited after asking)', async () => {
        const opened = await failedIndicator();
        await click(opened, 'test-step-ask');
        expect(byTestId(opened.root, 'assistant-scope-fix')).not.toBeNull();

        opened.editor().replace(draftCode + '# edited\n');
        await settle(opened.harness);

        expect(byTestId(opened.root, 'assistant-scope-fix')).toBeNull();
        expect(byTestId(opened.root, 'assistant-test-again')).not.toBeNull();
        await click(opened, 'assistant-send');
        expect(opened.http.expectOne(completionsUrl).request.body.action).toBe('script');
      });

      it('offers nothing for the test of another script while this one is selected (Test of another script)', async () => {
        const two = await testedIndicator(
          { status: 'failed', errorCode: 'step.indicator', error: keyError, steps: [flattenStep, cleanStep, failedStep('indicator', keyError)] },
          withIndicators(coverage, other),
        );
        expect(askButtons(two.root)).toHaveLength(1);
        await pickIndicator(two, 'other');
        confirmDialog()!.click();
        await tick();
        await settle(two.harness);
        two.http.expectOne(`${scriptsUrl}/indicators/other/versions`).flush([summary(other)]);
        await settle(two.harness);

        expect(byTestId(two.root, 'test-step-error')).not.toBeNull();
        expect(askButtons(two.root)).toHaveLength(0);
      });

      it('offers nothing while an older version is on view (Ask while an older version is on view)', async () => {
        const opened = await openAssistant();
        await startTest(opened);
        opened.http
          .expectOne((r) => r.url === testsUrl && r.method === 'POST')
          .flush(scriptTest({ status: 'failed', errorCode: 'step.clean', error: keyError, steps: [flattenStep, failedStep('clean', keyError)] }));
        await settle(opened.harness);
        expect(askButtons(opened.root)).toHaveLength(1);

        await openVersions(opened);
        await click(opened, 'version-1');
        confirmDialog()!.click();
        await tick();
        await settle(opened.harness);
        opened.http.expectOne(`${scriptsUrl}/clean/versions/1`).flush(version(1));
        await settle(opened.harness);

        expect(byTestId(opened.root, 'viewing-version')).not.toBeNull();
        expect(askButtons(opened.root)).toHaveLength(0);
      });

      it('leaves the Fix scope when the details change so that the warning disappears (Details changed after asking)', async () => {
        const opened = await warnedIndicator();
        (askButtons(opened.root)[0] as HTMLButtonElement).click();
        await settle(opened.harness);
        expect(byTestId(opened.root, 'assistant-scope-fix')!.getAttribute('aria-pressed')).toBe('true');

        await editDetails(opened);
        typeInto(byTestId<HTMLSelectElement>(opened.root, 'meta-format')!, 'decimal');
        await settle(opened.harness);

        expect(askButtons(opened.root)).toHaveLength(1);
        expect(byTestId(opened.root, 'assistant-scope-fix')).toBeNull();
        expect(byTestId(opened.root, 'assistant-test-again')).not.toBeNull();
      });

      it('shows the Assistant again with the focus on its box after another tab was chosen (Asking again)', async () => {
        const opened = await warnedIndicator();
        (askButtons(opened.root)[0] as HTMLButtonElement).click();
        await settle(opened.harness);
        const first = box(opened).value;

        await showTab(opened, 'inputs');
        expect(byTestId(opened.root, 'assistant-panel')).toBeNull();
        (askButtons(opened.root)[1] as HTMLButtonElement).click();
        await tick();
        await settle(opened.harness);
        opened.http.match(assistantUrl).forEach((r) => r.flush({ model: 'm', host: 'h' }));
        await settle(opened.harness);

        expect(byTestId(opened.root, 'side-tab-assistant')!.getAttribute('aria-selected')).toBe('true');
        expect(document.activeElement).toBe(box(opened));
        expect(box(opened).value).not.toBe(first);
        expect(box(opened).value).toContain('share the same values in every dimension');
        expect(byTestId(opened.root, 'assistant-scope-fix')!.getAttribute('aria-pressed')).toBe('true');
      });

      it('focuses the box again when the Assistant is already on view', async () => {
        const opened = await failedIndicator();
        await click(opened, 'test-step-ask');
        box(opened).blur();
        expect(document.activeElement).not.toBe(box(opened));

        await click(opened, 'test-step-ask');

        expect(document.activeElement).toBe(box(opened));
        expect(box(opened).value).toBe(sentence());
      });

      it('keeps what the analyst typed before the sentence, and replaces a sentence it put there earlier', async () => {
        const opened = await failedIndicator();
        typeInto(box(opened) as unknown as HTMLInputElement, 'it worked before the form changed');
        await settle(opened.harness);

        await click(opened, 'test-step-ask');
        expect(box(opened).value).toBe(`it worked before the form changed\n\n${sentence()}`);
      });

      it('offers nothing when the deployment has no assistant', async () => {
        const opened = await open('analyst', { assistant: false });
        await startTest(opened);
        opened.http
          .expectOne((r) => r.url === testsUrl && r.method === 'POST')
          .flush(scriptTest({ status: 'failed', errorCode: 'step.clean', error: keyError, steps: [flattenStep, failedStep('clean', keyError)] }));
        await settle(opened.harness);

        expect(byTestId(opened.root, 'test-step-error')).not.toBeNull();
        expect(askButtons(opened.root)).toHaveLength(0);
        expect(byTestId(opened.root, 'test-stale')).toBeNull();
      });
    });
  });
});
