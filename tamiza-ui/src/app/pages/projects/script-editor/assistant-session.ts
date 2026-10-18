import { DestroyRef, Injectable, LOCALE_ID, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { EditorState } from '@codemirror/state';
import { TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';
import { ApiError, ApiErrors } from '../../../core/api-error';
import { AuthService } from '../../../core/auth/auth.service';
import { AssistantStorage, StoredConversation, StoredProposal, conversationKey } from '../../../core/projects/assistant-storage';
import { AssistantApplied, AssistantMessage, AssistantRequest, AssistantService } from '../../../core/projects/assistant.service';
import type { ScriptRef } from '../../../core/projects/pipeline.service';
import { classifyProposal } from './assistant-proposal';
import {
  ApplyRequest,
  AssistantScope,
  AssistantSessionState,
  FixTarget,
  PendingRequest,
  ProposalMark,
  ProposalView,
  RequestFrame,
  ThreadTurn,
  maxRequestBytes,
  requestBytes,
} from './assistant-state';
import { AssistOptions, lintProblems } from './script-assist';
import type { ScriptInputsInfo } from './script-analysis';

/** What the API takes of a conversation: at most 38 earlier turns, each at most 16 KB, and 64 KB with the new request (design D3). */
const maxEarlierTurns = 38;
const maxTotalBytes = 64 * 1024;

/** What the session needs from the page that provides it. */
export interface AssistantHost {
  projectId(): string;
  /** The selected script; the session shows and keeps the state of this one. */
  script: Signal<ScriptRef>;
  /** The editor's text, saved or not. */
  draft: Signal<string>;
  /** The tables and names the editor's checks use; null until they have loaded. */
  inputs: Signal<ScriptInputsInfo | null>;
  /**
   * Whether a fix target still matches the page: the latest test is the selected script's, ran the editor's text, no older version is
   * on view and, for a warning, the dashboard view still shows it. Read where it is used, so the answer follows the page's signals.
   * Without it a target stays valid until it is replaced or cleared.
   */
  fixValid?: (target: FixTarget) => boolean;
}

/** What applying a block needs, and what the page must do with it: replace, replace a cell or insert; then report the outcome. */
export interface ApplyPlan {
  callId: string;
  action: AssistantApplied;
  /** The block's code. */
  code: string;
  /** The editor's text when the request was sent; applying asks first when the text is not that any more. */
  sentDraft: string;
  /** For a cell: the text of the cell that was targeted, to find it again. */
  cellText: string | null;
}

/** The request being written. Only one exists at a time, for any script of the page. */
interface ActiveRequest {
  conversation: Conversation;
  text: string;
  startedAt: number;
  subscription: Subscription | null;
}

/** The state of one script's conversation. Signals, so the tab follows them whichever script and side tab is on view. */
class Conversation {
  readonly thread: WritableSignal<readonly ThreadTurn[]>;
  readonly text: WritableSignal<string>;
  readonly scope: WritableSignal<AssistantScope>;
  readonly answer: WritableSignal<StoredProposal | null>;
  readonly failure = signal<ApiError | null>(null);
  readonly fix = signal<FixTarget | null>(null);

  constructor(
    readonly script: ScriptRef,
    /** Where the conversation is kept; null when the user's subject is unknown, which keeps it in memory only. */
    readonly storageKey: string | null,
    stored: StoredConversation | null,
  ) {
    // Created with their values: a conversation is made while a computed reads it, where writing a signal is not allowed.
    this.thread = signal(stored?.thread ?? []);
    this.text = signal(stored?.text ?? '');
    this.scope = signal(stored?.scope ?? 'script');
    this.answer = signal(stored?.proposal ?? null);
  }
}

function scriptId(script: ScriptRef): string {
  return script.kind === 'indicator' ? `indicator.${script.name}` : script.kind;
}

/**
 * The assistant's state for a Scripts page (design D11, "State outside the tab"). The side panel destroys a hidden tab's component,
 * so nothing that must outlive the tab lives in it: this page-scoped service keeps, per script, the thread, the latest answer, the
 * request box, the scope, the last failure and the fix target, and the one request being written, bound to the script it was sent
 * for. The conversation of each script is also kept in the browser tab's `sessionStorage`, under its user, project and script.
 */
@Injectable()
export class AssistantSession implements AssistantSessionState {
  private readonly service = inject(AssistantService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  private readonly storage = inject(AssistantStorage);
  private readonly auth = inject(AuthService);

  private host: AssistantHost | null = null;
  private readonly conversations = new Map<string, Conversation>();
  private readonly active = signal<ActiveRequest | null>(null);

  private readonly current = computed(() => this.conversation(this.connected().script()));
  readonly thread = computed(() => this.current().thread());
  readonly text = computed(() => this.current().text());
  readonly scope = computed(() => this.current().scope());
  readonly failure = computed(() => this.current().failure());
  readonly pending = computed<PendingRequest | null>(() => {
    const request = this.active();
    if (!request) {
      return null;
    }
    return {
      here: request.conversation === this.current(),
      scriptLabel: this.label(request.conversation.script),
      text: request.text,
      startedAt: request.startedAt,
    };
  });
  readonly fixTarget = computed<FixTarget | null>(() => {
    const target = this.current().fix();
    return target && (this.host?.fixValid?.(target) ?? true) ? target : null;
  });
  /** The latest answer of the selected script, classified for the actions the tab offers; null when there is none. */
  readonly proposal = computed<ProposalView | null>(() => {
    const answer = this.current().answer();
    const host = this.connected();
    return answer ? classifyProposal(answer, host.script().kind, host.draft(), (text, ignore) => this.check(text, ignore)) : null;
  });

  constructor() {
    // Leaving the Scripts page ends the request being written, which records it as cancelled.
    inject(DestroyRef).onDestroy(() => this.cancel());
  }

  /** The page tells the session where the selected script, the editor's text and the checks' inputs are. */
  connect(host: AssistantHost): void {
    this.host = host;
  }

  setText(text: string): void {
    const conversation = this.current();
    conversation.text.set(text);
    this.persist(conversation);
  }

  setScope(scope: AssistantScope): void {
    const conversation = this.current();
    conversation.scope.set(scope);
    this.persist(conversation);
  }

  dismissFailure(): void {
    this.current().failure.set(null);
  }

  /** Sets what a fix of the selected script refers to; null clears it. It lives in memory only: after a reload there is no target. */
  setFixTarget(target: FixTarget | null): void {
    this.current().fix.set(target);
  }

  requestBody(frame: RequestFrame): AssistantRequest {
    const script = this.connected().script();
    const conversation = this.current();
    const target = this.fixTarget();
    const action = frame.action === 'fix' && !target ? 'script' : frame.action === 'cell' && !frame.cell ? 'script' : frame.action;
    const details = frame.details;
    return {
      kind: script.kind,
      ...(script.kind === 'indicator' ? { name: script.name } : {}),
      action,
      messages: this.messages(conversation.thread(), conversation.text()),
      draft: frame.draft,
      ...(action === 'cell' && frame.cell ? { focus: { line: frame.cell.line } } : {}),
      ...(details ? { details: { ...details, label: details.label.trim(), description: details.description.trim(), unit: details.unit.trim() } } : {}),
      ...(action === 'fix' && target ? { testId: target.testId, ...(target.warning ? { warning: target.warning } : {}) } : {}),
    };
  }

  send(frame: RequestFrame): void {
    const host = this.connected();
    const conversation = this.current();
    const text = conversation.text();
    // The API answers `assistant.busy` to a second request, so the tab does not make one.
    if (this.active() || !text.trim() || requestBytes(text) > maxRequestBytes) {
      return;
    }
    if (conversation.scope() === 'fix' && !this.fixTarget()) {
      conversation.scope.set('script');
    }
    const body = this.requestBody(frame);
    const request: ActiveRequest = { conversation, text, startedAt: Date.now(), subscription: null };
    conversation.failure.set(null);
    // The text moves to the thread with its answer; if the request fails or is cancelled it comes back to the box.
    conversation.text.set('');
    this.active.set(request);
    request.subscription = this.service.complete(host.projectId(), body).subscribe({
      next: (answer) => {
        if (this.active() !== request) {
          return;
        }
        this.active.set(null);
        conversation.thread.update((thread) => [
          ...thread,
          { role: 'user', content: text },
          { role: 'assistant', content: answer.explanation, ...(answer.testExpired ? { testExpired: true } : {}) },
        ]);
        conversation.answer.set({
          callId: answer.callId,
          action: body.action,
          truncated: answer.truncated,
          blocks: answer.blocks,
          draft: frame.draft,
          cell: body.action === 'cell' && frame.cell ? { text: frame.cell.text } : null,
        });
        this.persist(conversation);
      },
      error: (error: unknown) => {
        if (this.active() !== request) {
          return;
        }
        this.active.set(null);
        conversation.failure.set(this.errors.parse(error));
        this.putBack(request);
      },
    });
  }

  cancel(): void {
    const request = this.active();
    if (!request) {
      return;
    }
    request.subscription?.unsubscribe();
    this.active.set(null);
    this.putBack(request);
  }

  newConversation(): void {
    const conversation = this.current();
    // The thread of a script cannot change while its answer is being written.
    if (this.active()?.conversation === conversation) {
      return;
    }
    conversation.thread.set([]);
    conversation.answer.set(null);
    conversation.failure.set(null);
    this.persist(conversation);
  }

  /** What the page needs to apply a block of the selected script's proposal in the given way; null when it is not offered. */
  applyPlan(request: ApplyRequest): ApplyPlan | null {
    const answer = this.current().answer();
    const block = answer?.blocks[request.block];
    if (!answer || !block || !this.proposal()?.blocks[request.block]?.offers.includes(request.action)) {
      return null;
    }
    return { callId: answer.callId, action: request.action, code: block.code, sentDraft: answer.draft, cellText: answer.cell?.text ?? null };
  }

  /** Tells the usage record how the analyst applied the answer. Bookkeeping: a failure is not shown. */
  recordOutcome(plan: ApplyPlan): void {
    this.service.recordOutcome(this.connected().projectId(), plan.callId, plan.action).subscribe({ error: () => undefined });
  }

  private connected(): AssistantHost {
    if (!this.host) {
      throw new Error('The assistant session is not connected to a page.');
    }
    return this.host;
  }

  private conversation(script: ScriptRef): Conversation {
    const id = scriptId(script);
    let conversation = this.conversations.get(id);
    if (!conversation) {
      const subject = this.auth.subject();
      const key = subject === null ? null : conversationKey(subject, this.connected().projectId(), script);
      conversation = new Conversation(script, key, key === null ? null : this.storage.load(key));
      this.conversations.set(id, conversation);
    }
    return conversation;
  }

  private label(script: ScriptRef): string {
    return this.transloco.translate(`scripts.assistant.scriptLabels.${script.kind}`, script.kind === 'indicator' ? { name: script.name } : {});
  }

  /** The turns of a request: the thread as it was answered, the oldest pairs left out until the API accepts it, and the box's text last. */
  private messages(thread: readonly ThreadTurn[], text: string): AssistantMessage[] {
    let earlier = thread.map(({ role, content }): AssistantMessage => ({ role, content }));
    const sizes = earlier.map((turn) => requestBytes(turn.content));
    const last = requestBytes(text);
    let from = 0;
    const fits = () => {
      const kept = sizes.slice(from);
      return kept.length <= maxEarlierTurns && kept.every((size) => size <= maxRequestBytes) && kept.reduce((sum, size) => sum + size, last) <= maxTotalBytes;
    };
    // Pairs go together, so the request starts with a turn of the analyst.
    while (from < earlier.length && !fits()) {
      from += 2;
    }
    earlier = earlier.slice(from);
    while (earlier.length && earlier[0].role !== 'user') {
      earlier = earlier.slice(1);
    }
    return [...earlier, { role: 'user', content: text }];
  }

  /** The editor's checks on a text, as marks; `ignore` names a check to leave out. */
  private check(text: string, ignore?: string): ProposalMark[] {
    const host = this.connected();
    const options: AssistOptions = {
      kind: host.script().kind,
      inputs: host.inputs(),
      translate: (key, params) => this.transloco.translate(key, params),
      locale: this.locale,
      canRun: false,
      runCell: () => undefined,
      problems: () => undefined,
    };
    return lintProblems(EditorState.create({ doc: text }), options)
      .filter((problem) => problem.key !== ignore)
      .map(({ line, severity, message }) => ({ line, severity, message }));
  }

  /** A request that failed or was cancelled adds nothing to the thread; its text goes back in front of anything typed since. */
  private putBack(request: ActiveRequest): void {
    const conversation = request.conversation;
    const typed = conversation.text();
    conversation.text.set(typed.trim() ? `${request.text}\n\n${typed}` : request.text);
    this.persist(conversation);
  }

  private persist(conversation: Conversation): void {
    if (conversation.storageKey === null) {
      return;
    }
    const sending = this.active()?.conversation === conversation ? this.active() : null;
    const typed = conversation.text();
    // While a request is being written its text is not in the box, but a reload must not lose it.
    const text = sending ? (typed.trim() ? `${sending.text}\n\n${typed}` : sending.text) : typed;
    const scope = conversation.scope();
    this.storage.save(conversation.storageKey, {
      thread: [...conversation.thread()],
      text,
      scope: scope === 'fix' ? 'script' : scope,
      proposal: conversation.answer(),
    });
  }
}
