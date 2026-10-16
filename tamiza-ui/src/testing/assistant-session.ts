import { WritableSignal, signal } from '@angular/core';
import { vi } from 'vitest';
import type { ApiError } from '../app/core/api-error';
import type { AssistantRequest } from '../app/core/projects/assistant.service';
import type {
  AssistantScope,
  AssistantSessionState,
  FixTarget,
  PendingRequest,
  ProposalView,
  RequestFrame,
  ThreadTurn,
} from '../app/pages/projects/script-editor/assistant-state';

/** A session whose state the test sets directly and whose operations are spies; it stands in for the page-scoped one. */
export interface FakeAssistantSession extends AssistantSessionState {
  readonly thread: WritableSignal<readonly ThreadTurn[]>;
  readonly text: WritableSignal<string>;
  readonly scope: WritableSignal<AssistantScope>;
  readonly proposal: WritableSignal<ProposalView | null>;
  readonly failure: WritableSignal<ApiError | null>;
  readonly pending: WritableSignal<PendingRequest | null>;
  readonly fixTarget: WritableSignal<FixTarget | null>;
  readonly setText: ReturnType<typeof vi.fn<(text: string) => void>>;
  readonly setScope: ReturnType<typeof vi.fn<(scope: AssistantScope) => void>>;
  readonly dismissFailure: ReturnType<typeof vi.fn<() => void>>;
  readonly requestBody: ReturnType<typeof vi.fn<(frame: RequestFrame) => AssistantRequest>>;
  readonly send: ReturnType<typeof vi.fn<(frame: RequestFrame) => void>>;
  readonly cancel: ReturnType<typeof vi.fn<() => void>>;
  readonly newConversation: ReturnType<typeof vi.fn<() => void>>;
}

export function fakeAssistantSession(): FakeAssistantSession {
  const thread = signal<readonly ThreadTurn[]>([]);
  const text = signal('');
  const scope = signal<AssistantScope>('script');
  const proposal = signal<ProposalView | null>(null);
  const failure = signal<ApiError | null>(null);
  const pending = signal<PendingRequest | null>(null);
  const fixTarget = signal<FixTarget | null>(null);
  return {
    thread,
    text,
    scope,
    proposal,
    failure,
    pending,
    fixTarget,
    setText: vi.fn((value: string) => text.set(value)),
    setScope: vi.fn((value: AssistantScope) => scope.set(value)),
    dismissFailure: vi.fn(() => failure.set(null)),
    requestBody: vi.fn(
      (frame: RequestFrame): AssistantRequest => ({
        kind: 'indicator',
        name: 'piped_water_rate',
        action: frame.action,
        messages: [{ role: 'user', content: text() }],
        draft: frame.draft,
        ...(frame.cell ? { focus: { line: frame.cell.line } } : {}),
        ...(frame.details ? { details: frame.details } : {}),
      }),
    ),
    send: vi.fn(),
    cancel: vi.fn(),
    newConversation: vi.fn(() => {
      thread.set([]);
      proposal.set(null);
    }),
  };
}
