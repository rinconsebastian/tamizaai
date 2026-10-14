import { HttpClient, HttpParams } from '@angular/common/http';
import { InjectionToken, Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type ScriptKind = 'clean' | 'output' | 'indicators';
export const scriptKinds: ScriptKind[] = ['clean', 'output', 'indicators'];

export interface Person {
  id: string;
  name: string;
}

export interface ScriptVersionSummary {
  versionId: string;
  kind: ScriptKind;
  number: number;
  removed: boolean;
  author: Person;
  createdAt: string;
}

export interface ScriptVersionDetails extends ScriptVersionSummary {
  code: string | null;
}

export interface ProjectScript {
  kind: ScriptKind;
  current: ScriptVersionDetails | null;
}

export type RunStatus = 'queued' | 'running' | 'succeeded' | 'failed';
export type RunStepName = 'flatten' | 'clean' | 'output' | 'indicators';
export type RunStepStatus = 'succeeded' | 'failed' | 'skipped';

export interface StepSummary {
  step: RunStepName;
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
  versions: { clean: number | null; output: number | null; indicators: number | null };
  steps: StepSummary[];
  errorCode: string | null;
  error: string | null;
}

export interface StepDetails extends StepSummary {
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
  columns: { name: string; type: ColumnType }[] | null;
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

@Injectable({ providedIn: 'root' })
export class PipelineService {
  private readonly http = inject(HttpClient);

  scripts(projectId: string): Observable<ProjectScript[]> {
    return this.http.get<ProjectScript[]>(`${base}/${projectId}/scripts`);
  }

  save(projectId: string, kind: ScriptKind, code: string): Observable<ScriptVersionDetails> {
    return this.http.put<ScriptVersionDetails>(`${base}/${projectId}/scripts/${kind}`, { code });
  }

  remove(projectId: string, kind: ScriptKind): Observable<ScriptVersionDetails> {
    return this.http.delete<ScriptVersionDetails>(`${base}/${projectId}/scripts/${kind}`);
  }

  versions(projectId: string, kind: ScriptKind): Observable<ScriptVersionSummary[]> {
    return this.http.get<ScriptVersionSummary[]>(`${base}/${projectId}/scripts/${kind}/versions`);
  }

  version(projectId: string, kind: ScriptKind, number: number): Observable<ScriptVersionDetails> {
    return this.http.get<ScriptVersionDetails>(`${base}/${projectId}/scripts/${kind}/versions/${number}`);
  }

  restore(projectId: string, kind: ScriptKind, number: number): Observable<ScriptVersionDetails> {
    return this.http.post<ScriptVersionDetails>(`${base}/${projectId}/scripts/${kind}/versions/${number}/restore`, null);
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

  startTest(projectId: string, kind: ScriptKind, code: string, sampleSize: number): Observable<ScriptTest> {
    return this.http.post<ScriptTest>(`${base}/${projectId}/script-tests`, { kind, code, sampleSize });
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
    ctx.form.label(question, code) describe the form; ctx.log.info(...) writes to the
    run log. Return a dictionary of table names to DataFrames.
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
  indicators: `import pandas as pd


def compute_indicators(cleaned, ctx):
    """Script 3: compute the indicators.

    Return one DataFrame with the columns indicator, disaggregation, value, numerator
    and denominator (other columns are allowed).
    """
    main = cleaned["main"]
    total = len(main)
    return pd.DataFrame(
        [{"indicator": "submissions", "disaggregation": "total", "value": total, "numerator": total, "denominator": total}]
    )
`,
};
