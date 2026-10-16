import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { IndicatorMetadata, ScriptKind } from './pipeline.service';

/** What the analyst asks for: a whole script, the cell at the cursor, or a fix of the last test's result. */
export type AssistantAction = 'script' | 'cell' | 'fix';

/** How an analyst applied a proposal, reported once so the usage record holds it. */
export type AssistantApplied = 'script' | 'cell' | 'insert';

/** One turn of a conversation as the API takes it; an assistant turn is the earlier explanation, with a note in place of its code. */
export interface AssistantMessage {
  role: 'user' | 'assistant';
  content: string;
}

/** The warnings of an indicator's dashboard view that a fix can name; the API keeps the params each one needs. */
export interface AssistantWarning {
  key: string;
  params?: Record<string, unknown>;
}

/** The body of `POST …/assistant/context` and `POST …/assistant/completions`. */
export interface AssistantRequest {
  kind: ScriptKind;
  /** The indicator's name; absent for script 1 and script 2. */
  name?: string;
  action: AssistantAction;
  messages: AssistantMessage[];
  /** The editor's text, saved or not. */
  draft: string;
  /** The 1-based line of the `# %%` marker of the cell, for `cell`. */
  focus?: { line: number };
  /** The indicator's details as edited, saved or not. */
  details?: IndicatorMetadata;
  /** The analyst's own sample test a `fix` refers to. */
  testId?: string;
  warning?: AssistantWarning;
}

/** `GET …/assistant`: the model and the provider's host, for the disclosure line. */
export interface AssistantSettings {
  model: string;
  host: string;
}

/** `POST …/assistant/context`: the exact text a request would send, and what the API left out to fit. */
export interface AssistantContext {
  model: string;
  host: string;
  text: string;
  bytes: number;
  omitted: string[];
}

/** A fenced block of an answer; `complete` is false when the closing fence is missing (an answer cut off). */
export interface AssistantBlock {
  code: string;
  complete: boolean;
}

export interface AssistantUsage {
  promptTokens: number | null;
  completionTokens: number | null;
  totalTokens: number | null;
  estimated: boolean;
}

/** `POST …/assistant/completions`: the explanation (each block replaced in place by a one-line note) and the code blocks in order. */
export interface AssistantAnswer {
  callId: string;
  explanation: string;
  blocks: AssistantBlock[];
  truncated: boolean;
  /** The test a fix referred to was no longer available, so the answer was given without it. */
  testExpired: boolean;
  omitted: string[];
  usage: AssistantUsage;
}

const base = '/api/v1/projects';

/** The script assistant's routes. Unsubscribing from `complete` aborts the request, which ends the API's request to the provider. */
@Injectable({ providedIn: 'root' })
export class AssistantService {
  private readonly http = inject(HttpClient);

  settings(projectId: string): Observable<AssistantSettings> {
    return this.http.get<AssistantSettings>(`${base}/${projectId}/assistant`);
  }

  /** The text the next request would send, without calling the provider. */
  context(projectId: string, request: AssistantRequest): Observable<AssistantContext> {
    return this.http.post<AssistantContext>(`${base}/${projectId}/assistant/context`, request);
  }

  complete(projectId: string, request: AssistantRequest): Observable<AssistantAnswer> {
    return this.http.post<AssistantAnswer>(`${base}/${projectId}/assistant/completions`, request);
  }

  /** Tells the usage record how the analyst applied the answer of a call. */
  recordOutcome(projectId: string, callId: string, applied: AssistantApplied): Observable<void> {
    return this.http.put<void>(`${base}/${projectId}/assistant/calls/${callId}/outcome`, { applied });
  }
}
