import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import {
  AssistantStorage,
  StoredConversation,
  StoredProposal,
  clearConversations,
  conversationKey,
  conversationPrefix,
  maxEntryBytes,
  maxTotalBytes,
} from './assistant-storage';

const bytes = (text: string) => new TextEncoder().encode(text).length;
const storedKeys = () => Object.keys(sessionStorage).filter((key) => key.startsWith(conversationPrefix));

function proposal(overrides: Partial<StoredProposal> = {}): StoredProposal {
  return { callId: 'c1', action: 'script', truncated: false, blocks: [{ code: 'def compute(cleaned, ctx):\n    return None\n', complete: true }], draft: 'draft', cell: null, ...overrides };
}

function conversation(overrides: Partial<StoredConversation> = {}): StoredConversation {
  return { thread: [], text: '', scope: 'script', proposal: null, ...overrides };
}

/** `pairs` requests with their answers, each turn about `size` bytes and starting with its number. */
function thread(pairs: number, size = 100): StoredConversation['thread'] {
  return Array.from({ length: pairs * 2 }, (_, i) => ({ role: i % 2 === 0 ? ('user' as const) : ('assistant' as const), content: `${i}:`.padEnd(size, 'x') }));
}

describe('conversation keys', () => {
  it('name the user, the project and the script', () => {
    expect(conversationKey('u-1', 'p-1', { kind: 'clean' })).toBe('tamiza.assistant.u-1.p-1.clean');
    expect(conversationKey('u-1', 'p-1', { kind: 'indicator', name: 'piped_water_rate' })).toBe('tamiza.assistant.u-1.p-1.indicator.piped_water_rate');
  });

  it('escape the dots of a subject, so one subject is never the prefix of another', () => {
    expect(conversationKey('a.b', 'p', { kind: 'output' })).toBe('tamiza.assistant.a%2Eb.p.output');
  });
});

describe('AssistantStorage', () => {
  let store: AssistantStorage;
  const key = conversationKey('u-1', 'p-1', { kind: 'clean' });

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    store = TestBed.inject(AssistantStorage);
  });
  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  it('keeps and returns a conversation', () => {
    const kept = conversation({ thread: thread(2), text: 'half written', scope: 'cell', proposal: proposal({ cell: { text: '# %% Rate' } }) });
    expect(store.save(key, kept)).toBe(true);
    expect(store.load(key)).toEqual(kept);
  });

  it('returns nothing for an unknown key, and removes an entry it cannot read', () => {
    expect(store.load(key)).toBeNull();
    sessionStorage.setItem(key, '{not json');
    expect(store.load(key)).toBeNull();
    expect(sessionStorage.getItem(key)).toBeNull();
    sessionStorage.setItem(key, JSON.stringify({ v: 1, touched: 1, thread: [{ role: 'user' }], text: '', scope: 'script', proposal: null }));
    expect(store.load(key)).toBeNull();
    expect(sessionStorage.getItem(key)).toBeNull();
  });

  it('removes a conversation that has nothing left in it', () => {
    store.save(key, conversation({ text: 'something' }));
    expect(store.save(key, conversation())).toBe(true);
    expect(sessionStorage.getItem(key)).toBeNull();
  });

  describe('the cap of one conversation', () => {
    it('drops the oldest pairs of turns from the stored copy first, keeping the proposal and the newest turns', () => {
      const kept = proposal({ blocks: [{ code: 'y'.repeat(10 * 1024), complete: true }] });
      const long = thread(40, 10 * 1024);
      store.save(key, conversation({ thread: long, proposal: kept }));

      const loaded = store.load(key)!;
      expect(bytes(sessionStorage.getItem(key)!)).toBeLessThanOrEqual(maxEntryBytes);
      expect(loaded.proposal).toEqual(kept);
      expect(loaded.thread.length).toBeGreaterThan(0);
      expect(loaded.thread.length).toBeLessThan(long.length);
      expect(loaded.thread[0].role).toBe('user');
      expect(loaded.thread.at(-1)).toEqual(long.at(-1));
      // What is kept is the end of the conversation, whole pairs of it.
      expect(loaded.thread).toEqual(long.slice(long.length - loaded.thread.length));
    });

    it('keeps a proposal that alone exceeds the cap in memory only, and stores the rest', () => {
      const huge = proposal({ draft: 'z'.repeat(maxEntryBytes + 1) });
      store.save(key, conversation({ thread: thread(1), text: 'next', proposal: huge }));

      const loaded = store.load(key)!;
      expect(loaded.proposal).toBeNull();
      expect(loaded.thread).toEqual(thread(1));
      expect(loaded.text).toBe('next');
    });

    it('does not store a request box far longer than a request may be', () => {
      store.save(key, conversation({ thread: thread(1), text: 'q'.repeat(100 * 1024) }));
      expect(store.load(key)).toEqual(conversation({ thread: thread(1) }));
    });
  });

  describe('the cap of all conversations', () => {
    const script = (name: string) => conversationKey('u-1', 'p-1', { kind: 'indicator', name });
    const big = () => conversation({ thread: thread(10, 15 * 1024) });

    it('evicts the least recently used conversation first', () => {
      const names = ['a', 'b', 'c', 'd', 'e', 'f', 'g'];
      for (const name of names.slice(0, 5)) {
        store.save(script(name), big());
      }
      expect(storedKeys()).toHaveLength(5);
      store.load(script('a')); // used again: no longer the oldest
      for (const name of names.slice(5)) {
        store.save(script(name), big());
      }

      const total = storedKeys().reduce((sum, stored) => sum + bytes(stored) + bytes(sessionStorage.getItem(stored)!), 0);
      expect(total).toBeLessThanOrEqual(maxTotalBytes);
      expect(storedKeys()).toContain(script('a'));
      expect(storedKeys()).not.toContain(script('b'));
      expect(storedKeys()).toContain(script('g'));
    });

    it('never evicts the conversation just written, and leaves other keys alone', () => {
      sessionStorage.setItem('0-tamiza-ui', 'x'.repeat(600 * 1024));
      for (const name of ['a', 'b', 'c', 'd', 'e', 'f']) {
        store.save(script(name), big());
      }
      expect(storedKeys()).toContain(script('f'));
      expect(sessionStorage.getItem('0-tamiza-ui')).toHaveLength(600 * 1024);
    });
  });

  describe('a store that fails', () => {
    it('keeps the conversation in memory when a write hits the quota, and drops the older stored copy', () => {
      store.save(key, conversation({ text: 'before' }));
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new DOMException('full', 'QuotaExceededError');
      });

      expect(store.save(key, conversation({ text: 'after' }))).toBe(false);
      expect(sessionStorage.getItem(key)).toBeNull();
    });

    it('answers with nothing when the store cannot be read', () => {
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new DOMException('blocked', 'SecurityError');
      });
      expect(store.load(key)).toBeNull();
    });
  });
});

describe('clearConversations', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => sessionStorage.clear());

  it('removes the assistant keys and nothing else', () => {
    sessionStorage.setItem(conversationKey('u-1', 'p-1', { kind: 'clean' }), '{}');
    sessionStorage.setItem(conversationKey('u-2', 'p-1', { kind: 'clean' }), '{}');
    sessionStorage.setItem('0-tamiza-ui', 'oidc state');
    sessionStorage.setItem('tamiza.returnUrl', '/projects');

    clearConversations(sessionStorage);

    expect(Object.keys(sessionStorage).sort()).toEqual(['0-tamiza-ui', 'tamiza.returnUrl']);
  });

  it('keeps the keys of one subject when asked to', () => {
    sessionStorage.setItem(conversationKey('u-1', 'p-1', { kind: 'clean' }), '{}');
    sessionStorage.setItem(conversationKey('u-1', 'p-2', { kind: 'output' }), '{}');
    sessionStorage.setItem(conversationKey('u-10', 'p-1', { kind: 'clean' }), '{}');
    sessionStorage.setItem(conversationKey('u-2', 'p-1', { kind: 'clean' }), '{}');

    clearConversations(sessionStorage, 'u-1');

    expect(storedKeys().sort()).toEqual([conversationKey('u-1', 'p-1', { kind: 'clean' }), conversationKey('u-1', 'p-2', { kind: 'output' })]);
  });

  it('is harmless without a store', () => {
    expect(() => clearConversations(null)).not.toThrow();
  });
});
