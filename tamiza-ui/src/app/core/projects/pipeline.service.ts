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

export interface TestStep {
  step: RunStepName;
  status: 'succeeded' | 'failed';
  startedAt: string | null;
  finishedAt: string | null;
  inputRows: number | null;
  outputRows: number | null;
  tables: TestTable[];
  log: string;
  error: string | null;
}

/** A test of a draft script on a sample of submissions; `waitingFor` says what a queued test waits for. */
export interface ScriptTest {
  id: string;
  kind: ScriptKind;
  /** The indicator tested; null for script 1 and script 2. */
  name: string | null;
  status: RunStatus;
  sampleSize: number;
  code: string;
  waitingFor: 'run' | 'test' | null;
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

export interface RunPage {
  items: RunSummary[];
  total: number;
}

export interface PipelineSettings {
  runAfterSync: boolean;
  runIntervalHours: number | null;
  nextRunAt: string | null;
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

  /** Tests unsaved code as script 1, script 2 or, with its name, an indicator. */
  startTest(projectId: string, script: ScriptRef, code: string, sampleSize: number): Observable<ScriptTest> {
    const target = script.kind === 'indicator' ? { kind: script.kind, name: script.name } : { kind: script.kind };
    return this.http.post<ScriptTest>(`${base}/${projectId}/script-tests`, { ...target, code, sampleSize });
  }

  test(projectId: string, testId: string): Observable<ScriptTest> {
    return this.http.get<ScriptTest>(`${base}/${projectId}/script-tests/${testId}`);
  }

  settings(projectId: string): Observable<PipelineSettings> {
    return this.http.get<PipelineSettings>(`${base}/${projectId}/pipeline`);
  }

  saveSettings(projectId: string, settings: { runAfterSync: boolean; runIntervalHours: number | null }): Observable<PipelineSettings> {
    return this.http.put<PipelineSettings>(`${base}/${projectId}/pipeline`, settings);
  }
}

/** A starting point for each script, with the contract in its docstring. */
export const scriptTemplates: Record<ScriptKind, string> = {
  clean: `def clean(raw, ctx):
    """Script 1: clean the flattened submissions.

    raw is a dictionary of pandas DataFrames: "main" (one row per submission) and one
    table per repeat group. Answers to select questions are choice labels, not codes
    (select_multiple answers are labels joined by "; "); ctx.form.name(question, label)
    gives the code of a label. ctx.form.questions, ctx.form.choices and
    ctx.form.label(question, code) describe the form; ctx.form.columns and
    ctx.form.column_label(table, column) give the question label of each column;
    ctx.log.info(...) writes to the run log. Return a dictionary of table names to DataFrames.
    """
    main = raw["main"]
    ctx.log.info("%d submissions", len(main))
    return {**raw, "main": main}
`,
  output: `def prepare_output(cleaned, ctx):
    """Script 2: shape the cleaned tables for the clean-data dashboard.

    cleaned is the dictionary of DataFrames that script 1 returned. Return a dictionary
    of table names (lowercase letters, digits and underscores) to DataFrames.
    """
    return {"main": cleaned["main"]}
`,
  indicator: `import pandas as pd


def compute(cleaned, ctx):
    """Compute one indicator.

    Return one DataFrame: a column for each dimension to break the figure down by (a
    municipality, a round) and at least one numeric column. By convention the dashboard
    plots value, and plots a ratio of sums, such as a rate, from numerator and denominator.
    """
    main = cleaned["main"]
    total = len(main)
    return pd.DataFrame(
        [{"group": "total", "value": 1.0, "numerator": total, "denominator": total}]
    )
`,
};
