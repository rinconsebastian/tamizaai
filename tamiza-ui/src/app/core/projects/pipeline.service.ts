import { HttpClient, HttpParams } from '@angular/common/http';
import { InjectionToken, Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type ScriptKind = 'clean' | 'output' | 'indicator';

/** The two scripts a project has at most once; the indicator scripts are named. */
export type FixedScriptKind = 'clean' | 'output';
export const fixedScriptKinds: FixedScriptKind[] = ['clean', 'output'];

/** Which script: script 1, script 2, or one indicator by name. */
export type ScriptRef = { kind: FixedScriptKind } | { kind: 'indicator'; name: string };

export type IndicatorFormat = 'count' | 'decimal' | 'percent' | 'currency';
export const indicatorFormats: IndicatorFormat[] = ['count', 'decimal', 'percent', 'currency'];

/** At most this many indicators with a current version per project. */
export const maxIndicators = 100;

/** Lowercase letters, digits and underscores, starting with a letter, at most 63 characters: the rule for table names. */
export const indicatorNamePattern = /^[a-z][a-z0-9_]{0,62}$/;

export interface Person {
  id: string;
  name: string;
}

/** Where a version was written: in the editor (`app`, which includes restoring a version there) or by an AI client through MCP. */
export type ScriptVersionOrigin = 'app' | 'mcp';

export interface ScriptVersionSummary {
  versionId: string;
  kind: ScriptKind;
  /** Set for indicators. */
  name: string | null;
  label: string | null;
  number: number;
  removed: boolean;
  author: Person;
  createdAt: string;
  origin: ScriptVersionOrigin;
}

/** An indicator's metadata, saved with each version of its script. */
export interface IndicatorMetadata {
  label: string;
  description: string;
  unit: string;
  format: IndicatorFormat;
}

/** The code of a version and, for an indicator, its metadata; all null for a removal. */
export interface ScriptVersionDetails extends ScriptVersionSummary {
  code: string | null;
  description: string | null;
  unit: string | null;
  format: IndicatorFormat | null;
}

export interface ProjectScript {
  kind: FixedScriptKind;
  current: ScriptVersionDetails | null;
}

/** An indicator with the metadata and code of its current version. */
export interface IndicatorScript {
  name: string;
  label: string;
  description: string | null;
  unit: string | null;
  format: IndicatorFormat;
  current: ScriptVersionDetails;
}

export interface ScriptList {
  clean: ProjectScript;
  output: ProjectScript;
  indicators: IndicatorScript[];
}

export type RunStatus = 'queued' | 'running' | 'succeeded' | 'failed';
export type RunStepName = 'flatten' | 'clean' | 'output' | 'indicator';
export type RunStepStatus = 'succeeded' | 'failed' | 'skipped';

export interface StepSummary {
  step: RunStepName;
  /** The indicator's name for an indicator step; empty for the three fixed steps. */
  name: string;
  status: RunStepStatus;
  inputRows: number | null;
  outputRows: number | null;
}

export interface RunSummary {
  id: string;
  trigger: 'manual' | 'sync' | 'schedule';
  status: RunStatus;
  requestedBy: Person | null;
  queuedAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  versions: { clean: number | null; output: number | null; indicators: { name: string; number: number }[] };
  steps: StepSummary[];
  errorCode: string | null;
  error: string | null;
}

export interface StepDetails extends StepSummary {
  /** The indicator's label in the version the run used. */
  label: string | null;
  startedAt: string | null;
  finishedAt: string | null;
  tables: { name: string; rows: number }[];
  log: string;
  error: string | null;
}

/** A reference table as a run read it when it started. */
export interface RunReferenceTable {
  name: string;
  rows: number;
  updatedAt: string;
}

export interface RunDetails {
  run: RunSummary;
  steps: StepDetails[];
  referenceTables: RunReferenceTable[];
}

/** Column types of previews and reference tables. */
export type ColumnType = 'integer' | 'decimal' | 'boolean' | 'date' | 'timestamp' | 'text';

/**
 * A table a test step produced. Only the first 20 tables of a step have `columns` and a `preview` of their first rows;
 * `cutCells` lists the [row, column] of text values cut at 500 characters.
 */
export interface TestTable {
  name: string;
  rows: number;
  columns: { name: string; type: ColumnType; label?: string | null }[] | null;
  previewRows: number;
  preview: unknown[][] | null;
  cutCells: number[][] | null;
}

/** What a draft passed to `ctx.show`: a preview of a table as it was at the call, or text for any other value. */
export interface ShownPreview {
  name: string;
  /** The line of the script that called `ctx.show`; 0 when it is not known. */
  line: number;
  rows: number;
  table: TestTable | null;
  text: string | null;
}

/** The sums of an indicator table per value of one dimension, for the dashboard view's bar chart. */
export interface DimensionChart {
  values: number;
  /** Null when the dimension has more than 50 values. */
  groups: { group: string | null; value: number | null; numerator: number | null; denominator: number | null }[] | null;
}

/** Facts about an indicator draft's table, from which the dashboard view and its warnings are built. */
export interface IndicatorTestView {
  measures: string[];
  defaultMeasure: string | null;
  ratio: boolean;
  dimensions: string[];
  defaultDimension: string | null;
  geometry: string[];
  rows: number;
  defaultMeasureNulls: number;
  defaultMeasureMax: number | null;
  totals: Record<string, number>;
  duplicateRows: number;
  charts: Record<string, DimensionChart>;
}

export interface TestStep {
  step: RunStepName;
  /** `stopped`: a draft run through a cell reached the end of it. */
  status: 'succeeded' | 'failed' | 'stopped';
  startedAt: string | null;
  finishedAt: string | null;
  inputRows: number | null;
  outputRows: number | null;
  tables: TestTable[];
  log: string;
  error: string | null;
  /** The result came from an earlier test with the same inputs. */
  reused: boolean;
  stoppedAtLine: number | null;
  shown: ShownPreview[] | null;
  dashboard: IndicatorTestView | null;
}

/** A test of a draft script on a sample of submissions; `waitingFor` says what a queued test waits for. */
export interface ScriptTest {
  id: string;
  kind: ScriptKind;
  /** The indicator tested; null for script 1 and script 2. */
  name: string | null;
  status: RunStatus;
  sampleSize: number;
  /** The line of the `# %%` marker the draft was run through; null for a whole test. */
  stopAtLine: number | null;
  code: string;
  waitingFor: 'run' | 'test' | 'download' | null;
  cleanVersion: number | null;
  queuedAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  errorCode: string | null;
  error: string | null;
  steps: TestStep[];
}

export const minSampleSize = 1;
export const maxSampleSize = 5000;
export const defaultSampleSize = 500;

export interface SampleDownloadTable {
  name: string;
  rows: number;
}

/** What a ready download's zip holds, as the worker recorded it (the manifest without the library versions). */
export interface SampleDownloadContents {
  sample: { requested: number; submissions: number };
  raw: { tables: SampleDownloadTable[] };
  /** `none`: the project has no script 1; `failed`: it raised on the sample, so there are no clean tables. */
  clean: { status: 'succeeded' | 'failed' | 'none'; version: number | null; error: string | null; tables: SampleDownloadTable[] };
  reference: { name: string; rows: number; updatedAt: string }[];
}

/** A sample of the project's data to work on a computer; `waitingFor` says what a queued download waits for. */
export interface SampleDownload {
  id: string;
  status: RunStatus;
  sampleSize: number;
  waitingFor: 'run' | 'test' | 'download' | null;
  queuedAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  /** When a ready download stops being available. */
  expiresAt: string | null;
  sizeBytes: number | null;
  contents: SampleDownloadContents | null;
  errorCode: string | null;
  error: string | null;
}

/** A single-use link to a ready download's zip. */
export interface SampleDownloadLink {
  downloadUrl: string;
  fileName: string;
  sizeBytes: number;
  expiresAt: string;
}

export interface RunPage {
  items: RunSummary[];
  total: number;
}

export interface PipelineSettings {
  runAfterSync: boolean;
  runIntervalHours: number | null;
  nextRunAt: string | null;
}

export interface InputColumn {
  name: string;
  /** `integer`, `decimal`, `boolean`, `date`, `timestamp` or `text` for a published table; `integer`, `decimal`, `date` or `text` for a reference table. */
  type: ColumnType;
  /** The question's label, for a flattened column that has one. */
  label?: string | null;
}

export interface InputTable {
  name: string;
  rows: number | null;
  columns: InputColumn[];
}

export interface InputReferenceTable {
  name: string;
  columns: InputColumn[];
}

export interface InputQuestion {
  name: string;
  path: string;
  type: string;
  label: string | null;
}

/** What the editor shows beside a script: `raw` tables for script 1, `clean` tables for script 2 and the indicators. */
export interface ScriptInputs {
  source: 'raw' | 'clean';
  publishedAt: string | null;
  runId: string | null;
  tables: InputTable[];
  referenceTables: InputReferenceTable[];
  questions: InputQuestion[];
}

/** How often the Scripts section refreshes while a run is queued or running. */
export const PIPELINE_POLL_MS = new InjectionToken<number>('PIPELINE_POLL_MS', { factory: () => 5000 });

/** How often a script test is refreshed while it is queued or running. */
export const SCRIPT_TEST_POLL_MS = new InjectionToken<number>('SCRIPT_TEST_POLL_MS', { factory: () => 2000 });

const base = '/api/v1/projects';

function scriptUrl(projectId: string, script: ScriptRef): string {
  const path = script.kind === 'indicator' ? `indicators/${script.name}` : script.kind;
  return `${base}/${projectId}/scripts/${path}`;
}

@Injectable({ providedIn: 'root' })
export class PipelineService {
  private readonly http = inject(HttpClient);

  scripts(projectId: string): Observable<ScriptList> {
    return this.http.get<ScriptList>(`${base}/${projectId}/scripts`);
  }

  /** Saves script 1 or 2 with `{ code }`, or an indicator with its metadata too; the first save of a name creates the indicator. */
  save(projectId: string, script: ScriptRef, body: { code: string } | ({ code: string } & IndicatorMetadata)): Observable<ScriptVersionDetails> {
    return this.http.put<ScriptVersionDetails>(scriptUrl(projectId, script), body);
  }

  remove(projectId: string, script: ScriptRef): Observable<ScriptVersionDetails> {
    return this.http.delete<ScriptVersionDetails>(scriptUrl(projectId, script));
  }

  versions(projectId: string, script: ScriptRef): Observable<ScriptVersionSummary[]> {
    return this.http.get<ScriptVersionSummary[]>(`${scriptUrl(projectId, script)}/versions`);
  }

  version(projectId: string, script: ScriptRef, number: number): Observable<ScriptVersionDetails> {
    return this.http.get<ScriptVersionDetails>(`${scriptUrl(projectId, script)}/versions/${number}`);
  }

  restore(projectId: string, script: ScriptRef, number: number): Observable<ScriptVersionDetails> {
    return this.http.post<ScriptVersionDetails>(`${scriptUrl(projectId, script)}/versions/${number}/restore`, null);
  }

  startRun(projectId: string): Observable<RunSummary> {
    return this.http.post<RunSummary>(`${base}/${projectId}/runs`, null);
  }

  runs(projectId: string, page: number, pageSize: number): Observable<RunPage> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<RunPage>(`${base}/${projectId}/runs`, { params });
  }

  run(projectId: string, runId: string): Observable<RunDetails> {
    return this.http.get<RunDetails>(`${base}/${projectId}/runs/${runId}`);
  }

  /**
   * Tests unsaved code as script 1, script 2 or, with its name, an indicator; with `stopAtLine`, only up to the
   * `# %%` marker on that line.
   */
  startTest(projectId: string, script: ScriptRef, code: string, sampleSize: number, stopAtLine?: number): Observable<ScriptTest> {
    const target = script.kind === 'indicator' ? { kind: script.kind, name: script.name } : { kind: script.kind };
    return this.http.post<ScriptTest>(`${base}/${projectId}/script-tests`, {
      ...target,
      code,
      sampleSize,
      ...(stopAtLine === undefined ? {} : { stopAtLine }),
    });
  }

  /** The tables a script receives as the last run published them, with the reference tables and the form's questions. */
  scriptInputs(projectId: string, kind: ScriptKind): Observable<ScriptInputs> {
    return this.http.get<ScriptInputs>(`${base}/${projectId}/script-inputs`, { params: new HttpParams().set('kind', kind) });
  }

  test(projectId: string, testId: string): Observable<ScriptTest> {
    return this.http.get<ScriptTest>(`${base}/${projectId}/script-tests/${testId}`);
  }

  /** Asks for a sample of the project's data to work on a computer; the worker prepares it as a zip. */
  startSampleDownload(projectId: string, sampleSize: number): Observable<SampleDownload> {
    return this.http.post<SampleDownload>(`${base}/${projectId}/sample-downloads`, { sampleSize });
  }

  sampleDownload(projectId: string, downloadId: string): Observable<SampleDownload> {
    return this.http.get<SampleDownload>(`${base}/${projectId}/sample-downloads/${downloadId}`);
  }

  /** A single-use link to the zip of a ready download; ask again for another after one was used. */
  sampleDownloadLink(projectId: string, downloadId: string): Observable<SampleDownloadLink> {
    return this.http.post<SampleDownloadLink>(`${base}/${projectId}/sample-downloads/${downloadId}/link`, null);
  }

  settings(projectId: string): Observable<PipelineSettings> {
    return this.http.get<PipelineSettings>(`${base}/${projectId}/pipeline`);
  }

  saveSettings(projectId: string, settings: { runAfterSync: boolean; runIntervalHours: number | null }): Observable<PipelineSettings> {
    return this.http.put<PipelineSettings>(`${base}/${projectId}/pipeline`, settings);
  }
}
