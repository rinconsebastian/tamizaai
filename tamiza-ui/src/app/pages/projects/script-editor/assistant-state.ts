import { InjectionToken, Signal } from '@angular/core';
import type { ApiError } from '../../../core/api-error';
import type { AssistantAction, AssistantRequest } from '../../../core/projects/assistant.service';
import type { IndicatorMetadata } from '../../../core/projects/pipeline.service';
import { Cell, cells } from './script-analysis';

/**
 * What the Assistant tab renders and the page-scoped `AssistantSession` keeps (design D11, "State outside the tab"): nothing
 * that must outlive the tab lives in the tab, because the side panel destroys a hidden tab's component. The tab reads the state
 * of the selected script from `ASSISTANT_SESSION` and changes it only through the methods below.
 */

/** A request turn may hold at most this many bytes (UTF-8), the API's limit for one turn. */
export const maxRequestBytes = 16 * 1024;

/** The scope of a request in the tab: the whole script, the cell at the cursor, or a fix of the last test. */
export type AssistantScope = AssistantAction;

/** One turn of a script's thread as the tab shows it. */
export interface ThreadTurn {
  role: 'user' | 'assistant';
  /** The analyst's message, or the answer's explanation (which holds `[proposed code: N lines]` in place of each block). */
  content: string;
  /** On an answer: the test a fix referred to had expired, so the answer was given without it. */
  testExpired?: boolean;
}

/** The request being written, for any script of the page. */
export interface PendingRequest {
  /** Whether it belongs to the script that is selected now. */
  here: boolean;
  /** The script it belongs to, named for the note shown on other scripts, for example `indicator coverage`. */
  scriptLabel: string;
  /** What the analyst sent, shown as their message while the answer is written. */
  text: string;
  /** `Date.now()` when it was sent. */
  startedAt: number;
}

/** What a fix refers to, while it is valid (design D11, "Fix target"). */
export interface FixTarget {
  testId: string;
  /** The warning of the dashboard view the analyst asked about, if the fix is about one. */
  warning?: { key: string; params?: Record<string, unknown> };
}

/** How a proposal's block can be put in the editor. */
export type ProposalOffer = 'script' | 'cell' | 'insert';

/** A mark the editor's checks put on what the editor would hold after applying a block. */
export interface ProposalMark {
  line: number;
  severity: 'error' | 'warning';
  message: string;
}

export interface ProposalBlockView {
  /** The number of lines of the code. */
  lines: number;
  /** Whether it defines the contract function of the script's kind. */
  definesContract: boolean;
  /** In the order they are shown; replacing a script or a cell is offered only when it applies (design D8). */
  offers: ProposalOffer[];
  marks: ProposalMark[];
}

/** Why a proposal is not offered as a whole script or a cell; the tab says each one in a sentence. */
export type ProposalNote = 'notOneScript' | 'notOneCell' | 'cutOff' | 'cellChanged';

/** The latest answer's code of the selected script, classified for the actions the tab offers. */
export interface ProposalView {
  callId: string;
  blocks: ProposalBlockView[];
  notes: ProposalNote[];
}

/** The tab asks the page to put block `block` of the proposal in the editor in the given way. */
export interface ApplyRequest {
  block: number;
  action: ProposalOffer;
}

/**
 * What the tab knows about the editor when the analyst sends a request or looks at the context: the session adds the script, the
 * thread and the box's text, and for a fix the test.
 */
export interface RequestFrame {
  action: AssistantAction;
  /** The editor's text, saved or not. */
  draft: string;
  /** For `cell`: the marker line of the cell at the cursor and its text as it is now (from the marker to the line before the next one). */
  cell: { line: number; text: string } | null;
  /** The indicator's details as edited; null for script 1 and script 2. */
  details: IndicatorMetadata | null;
}

/** The state of the selected script, and the operations on it. */
export interface AssistantSessionState {
  /** The thread of the selected script: the analyst's messages and the explanations that were answered. */
  readonly thread: Signal<readonly ThreadTurn[]>;
  /** The request box's text of the selected script. */
  readonly text: Signal<string>;
  /** The scope chosen for the selected script; the tab shows another one while the chosen one is not available. */
  readonly scope: Signal<AssistantScope>;
  /** The latest proposal of the selected script, or null when there is none or it is not offered any more. */
  readonly proposal: Signal<ProposalView | null>;
  /** The last failure of a request of the selected script, until the next send or until it is dismissed. */
  readonly failure: Signal<ApiError | null>;
  /** The request being written, whichever script it belongs to. */
  readonly pending: Signal<PendingRequest | null>;
  /** The fix target while it is valid for the selected script; null when there is none or it stopped being valid. */
  readonly fixTarget: Signal<FixTarget | null>;

  setText(text: string): void;
  setScope(scope: AssistantScope): void;
  dismissFailure(): void;
  /** The body of `POST …/assistant/context` and `…/completions`: the same turns the next send would carry, with the box's text last. */
  requestBody(frame: RequestFrame): AssistantRequest;
  /** Sends the box's text with the frame; the request stays bound to the selected script until it ends. */
  send(frame: RequestFrame): void;
  /** Cancels the request being written, wherever it belongs. */
  cancel(): void;
  /** Empties the thread and the proposal of the selected script. */
  newConversation(): void;
}

/**
 * The page provides the session; the Assistant tab injects it. It is an interface behind a token so the tab does not depend on
 * how the page keeps the state.
 */
export const ASSISTANT_SESSION = new InjectionToken<AssistantSessionState>('ASSISTANT_SESSION');

/**
 * The cell of `text` whose text is `cellText`, wherever it now is: the same range as `cellRange`. Null when no cell has that text or
 * more than one has (the cell was edited, split or lost its marker), so a replacement never lands on a guess.
 */
export function findCell(text: string, cellText: string): { from: number; to: number; text: string } | null {
  const all = cells(text);
  const found = all.map((_, index) => cellRange(text, all, index)).filter((range) => range.text === cellText);
  return found.length === 1 ? found[0] : null;
}

/**
 * What goes where a cell was: the proposal's code without leading blank lines and trailing whitespace, followed by the blank lines
 * the replaced cell ended with, so the spacing before the next cell (or the end of the file) stays as it was.
 */
export function cellReplacement(code: string, replaced: string): string {
  const tail = /\n*$/.exec(replaced)?.[0] ?? '';
  return code.replace(/^(?:[ \t]*\r?\n)+/, '').replace(/\s+$/, '') + tail;
}

/** The UTF-8 size of a request box's text. */
export function requestBytes(text: string): number {
  return new TextEncoder().encode(text).length;
}

/**
 * The text of cell `index` of `all` (the cells of `text`): from its marker line to the line before the next marker, or to the end of
 * the text for the last cell. `from` and `to` are offsets into `text`, so `replaceRange(from, to, code)` rewrites exactly the cell.
 */
export function cellRange(text: string, all: Cell[], index: number): { from: number; to: number; text: string } {
  const from = all[index].from;
  const next = all[index + 1];
  const to = next ? next.from - 1 : text.length;
  return { from, to, text: text.slice(from, to) };
}
