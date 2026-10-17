import { DOCUMENT, Injectable, inject } from '@angular/core';
import type { AssistantAction, AssistantBlock } from './assistant.service';
import type { ScriptRef } from './pipeline.service';

/**
 * Where the assistant's conversations live: the browser tab's `sessionStorage`, nowhere else (design D11, "Conversation storage").
 * Keys are `tamiza.assistant.<subject>.<project>.<kind>[.<name>]`. The OIDC library keeps its tokens in the same store, so
 * everything here works on keys with that prefix only, never on `sessionStorage.clear()`, and keeps within a size budget.
 */
export const conversationPrefix = 'tamiza.assistant.';

/** One conversation holds at most this many bytes; past it the oldest turns are left out of the stored copy. */
export const maxEntryBytes = 200 * 1024;

/** All conversations together hold at most this many bytes; the least recently used one is evicted first. */
export const maxTotalBytes = 1024 * 1024;

/** The request box's text is stored only up to this size; a request cannot be longer than 16 KB anyway. */
const maxStoredTextBytes = 64 * 1024;

export interface StoredTurn {
  role: 'user' | 'assistant';
  content: string;
  testExpired?: boolean;
}

/** The latest answer of a conversation with what applying it needs, also after a reload. */
export interface StoredProposal {
  callId: string;
  /** The action the request was sent with. */
  action: AssistantAction;
  truncated: boolean;
  blocks: AssistantBlock[];
  /** The editor's text when the request was sent. */
  draft: string;
  /** For a cell request: the text of the cell that was targeted. */
  cell: { text: string } | null;
}

export interface StoredConversation {
  thread: StoredTurn[];
  text: string;
  scope: 'script' | 'cell';
  proposal: StoredProposal | null;
}

interface StoredEntry extends StoredConversation {
  v: 1;
  /** Orders the conversations by use for the eviction. */
  touched: number;
}

const encoder = new TextEncoder();
const bytes = (text: string) => encoder.encode(text).length;

let lastTouched = 0;
/** Strictly increasing, so two writes in the same millisecond still have an order. */
function nextTouched(): number {
  lastTouched = Math.max(Date.now(), lastTouched + 1);
  return lastTouched;
}

/** A subject as it appears in a key: the dots, which separate the key's parts, are escaped. */
function subjectSegment(subject: string): string {
  return encodeURIComponent(subject).replace(/\./g, '%2E');
}

export function conversationKey(subject: string, projectId: string, script: ScriptRef): string {
  const name = script.kind === 'indicator' ? `.${script.name}` : '';
  return `${conversationPrefix}${subjectSegment(subject)}.${projectId}.${script.kind}${name}`;
}

function conversationKeys(storage: Storage): string[] {
  const keys: string[] = [];
  for (let i = 0; i < storage.length; i++) {
    const key = storage.key(i);
    if (key?.startsWith(conversationPrefix)) {
      keys.push(key);
    }
  }
  return keys;
}

/**
 * Removes the assistant's keys and nothing else. With `keepSubject`, the keys of that subject stay (the same person signing in
 * again keeps their conversations); without it every key goes (sign-out, or a subject that cannot be told).
 */
export function clearConversations(storage: Storage | null | undefined, keepSubject: string | null = null): void {
  if (!storage) {
    return;
  }
  try {
    const keep = keepSubject === null ? null : `${conversationPrefix}${subjectSegment(keepSubject)}.`;
    for (const key of conversationKeys(storage)) {
      if (keep === null || !key.startsWith(keep)) {
        storage.removeItem(key);
      }
    }
  } catch {
    // A store that cannot be read or written holds nothing worth keeping from another person.
  }
}

function isTurn(value: unknown): value is StoredTurn {
  const turn = value as StoredTurn | null;
  return (
    typeof turn === 'object' &&
    turn !== null &&
    (turn.role === 'user' || turn.role === 'assistant') &&
    typeof turn.content === 'string' &&
    (turn.testExpired === undefined || typeof turn.testExpired === 'boolean')
  );
}

function isProposal(value: unknown): value is StoredProposal {
  const proposal = value as StoredProposal | null;
  return (
    typeof proposal === 'object' &&
    proposal !== null &&
    typeof proposal.callId === 'string' &&
    (proposal.action === 'script' || proposal.action === 'cell' || proposal.action === 'fix') &&
    typeof proposal.truncated === 'boolean' &&
    Array.isArray(proposal.blocks) &&
    proposal.blocks.every((block) => typeof block?.code === 'string' && typeof block.complete === 'boolean') &&
    typeof proposal.draft === 'string' &&
    (proposal.cell === null || typeof proposal.cell?.text === 'string')
  );
}

function parseEntry(raw: string): StoredEntry | null {
  try {
    const entry = JSON.parse(raw) as StoredEntry | null;
    if (
      entry?.v === 1 &&
      typeof entry.touched === 'number' &&
      Array.isArray(entry.thread) &&
      entry.thread.every(isTurn) &&
      typeof entry.text === 'string' &&
      (entry.scope === 'script' || entry.scope === 'cell') &&
      (entry.proposal === null || isProposal(entry.proposal))
    ) {
      return entry;
    }
  } catch {
    // Unreadable entries are dropped by the caller.
  }
  return null;
}

/** The store of one browser tab's conversations. Every read and write is guarded: a store that fails keeps the conversation in memory only. */
@Injectable({ providedIn: 'root' })
export class AssistantStorage {
  private readonly document = inject(DOCUMENT);

  private storage(): Storage | null {
    try {
      return this.document.defaultView?.sessionStorage ?? null;
    } catch {
      return null;
    }
  }

  /** The stored conversation, marked as just used; null when there is none or it is unreadable (an unreadable one is removed). */
  load(key: string): StoredConversation | null {
    const storage = this.storage();
    if (!storage) {
      return null;
    }
    try {
      const raw = storage.getItem(key);
      if (raw === null) {
        return null;
      }
      const entry = parseEntry(raw);
      if (!entry) {
        storage.removeItem(key);
        return null;
      }
      storage.setItem(key, JSON.stringify({ ...entry, touched: nextTouched() }));
      const { thread, text, scope, proposal } = entry;
      return { thread, text, scope, proposal };
    } catch {
      return null;
    }
  }

  remove(key: string): void {
    try {
      this.storage()?.removeItem(key);
    } catch {
      // Nothing to do: the entry stays where it is.
    }
  }

  /**
   * Stores a conversation, within the caps: past 200 KB the oldest turns are left out of the stored copy first and the proposal
   * never for turns; a proposal that alone exceeds the cap is not stored at all. After the write, the least recently used
   * conversations are evicted until all of them hold at most 1 MB. False when the conversation stays in memory only.
   */
  save(key: string, conversation: StoredConversation): boolean {
    const storage = this.storage();
    if (!storage) {
      return false;
    }
    const json = this.fit(conversation);
    if (json === null) {
      this.remove(key);
      return true;
    }
    try {
      storage.setItem(key, json);
    } catch {
      // The quota is full: keep the conversation in memory, and drop the older copy so a reload cannot show a stale one.
      this.remove(key);
      return false;
    }
    this.evict(storage, key);
    return true;
  }

  /** The entry's JSON within the entry cap, or null when there is nothing to keep. */
  private fit(conversation: StoredConversation): string | null {
    const touched = nextTouched();
    const text = bytes(conversation.text) > maxStoredTextBytes ? '' : conversation.text;
    const scope = conversation.scope;
    let proposal = conversation.proposal;
    const shell = (kept: string, stored: StoredProposal | null) => bytes(JSON.stringify({ v: 1, touched, thread: [], text: kept, scope, proposal: stored }));

    let size = shell(text, proposal);
    if (proposal && size > maxEntryBytes) {
      proposal = null;
      size = shell(text, null);
    }
    let kept = text;
    if (size > maxEntryBytes) {
      kept = '';
      size = shell('', proposal);
    }

    // Turns come in pairs (a request and its answer): the oldest pairs go first, so a stored thread starts with a user turn.
    const thread = conversation.thread;
    const sizes = thread.map((turn) => bytes(JSON.stringify(turn)));
    let start = 0;
    const total = (from: number) => size + sizes.slice(from).reduce((sum, turnSize) => sum + turnSize, 0) + Math.max(0, thread.length - from - 1);
    while (start < thread.length && total(start) > maxEntryBytes) {
      start += 2;
    }
    let stored = thread.slice(start);
    while (stored.length && stored[0].role !== 'user') {
      stored = stored.slice(1);
    }
    if (!stored.length && !kept && !proposal) {
      return null;
    }
    const entry: StoredEntry = { v: 1, touched, thread: stored, text: kept, scope, proposal };
    return JSON.stringify(entry);
  }

  /** Removes the least recently used conversations, never the one just written, while the total is over its budget. */
  private evict(storage: Storage, written: string): void {
    try {
      const entries = conversationKeys(storage).map((key) => {
        const raw = storage.getItem(key) ?? '';
        return { key, size: bytes(key) + bytes(raw), touched: parseEntry(raw)?.touched ?? 0 };
      });
      let total = entries.reduce((sum, entry) => sum + entry.size, 0);
      const others = entries.filter((entry) => entry.key !== written).sort((a, b) => a.touched - b.touched);
      for (const entry of others) {
        if (total <= maxTotalBytes) {
          break;
        }
        storage.removeItem(entry.key);
        total -= entry.size;
      }
    } catch {
      // Eviction is best effort.
    }
  }
}
