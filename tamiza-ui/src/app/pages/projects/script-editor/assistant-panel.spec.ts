import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { fakeAssistantSession, FakeAssistantSession } from '../../../../testing/assistant-session';
import { provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { ApiError, ApiErrors } from '../../../core/api-error';
import type { IndicatorMetadata, ScriptKind } from '../../../core/projects/pipeline.service';
import { ToastService } from '../../../shared/ui';
import { AssistantPanel } from './assistant-panel';
import { ASSISTANT_SESSION, ApplyRequest, ProposalView } from './assistant-state';

const settingsUrl = '/api/v1/projects/p1/assistant';
const contextUrl = `${settingsUrl}/context`;

const textOf = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const byTestId = <T extends HTMLElement = HTMLElement>(root: ParentNode, id: string) => root.querySelector<T>(`[data-testid="${id}"]`);

/** A script of three cells, `Rate` the second: lines 1-3 hold a cell `Load`, lines 4-6 `Rate` and lines 7-8 a cell without a name. */
const cellScript = [
  '# %% Load',
  'import pandas as pd',
  'df = cleaned["main"]',
  '# %% Rate',
  'rate = df["has_water"].eq("Yes").mean()',
  'ctx.log(rate)',
  '# %%',
  'result = rate',
].join('\n');
const noCellScript = 'def compute(cleaned, ctx):\n    return None\n';

const details: IndicatorMetadata = { label: 'Households with piped water', description: '', unit: 'households', format: 'percent' };

interface Setup {
  kind?: ScriptKind;
  name?: string | null;
  draft?: string;
  cursorLine?: number;
  details?: IndicatorMetadata | null;
  viewing?: boolean;
}

describe('AssistantPanel', () => {
  let session: FakeAssistantSession;
  let http: HttpTestingController;
  let fixture: ComponentFixture<AssistantPanel>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await tick();
    }
  }

  /** Opens the panel for an indicator by default and answers the request for the model and host. */
  async function open(setup: Setup = {}) {
    fixture = TestBed.createComponent(AssistantPanel);
    fixture.componentRef.setInput('projectId', 'p1');
    fixture.componentRef.setInput('kind', setup.kind ?? 'indicator');
    fixture.componentRef.setInput('scriptName', setup.name === undefined ? 'piped_water_rate' : setup.name);
    fixture.componentRef.setInput('draft', setup.draft ?? noCellScript);
    fixture.componentRef.setInput('cursorLine', setup.cursorLine ?? 1);
    fixture.componentRef.setInput('details', setup.details === undefined ? details : setup.details);
    fixture.componentRef.setInput('viewing', setup.viewing ?? false);
    root = fixture.nativeElement as HTMLElement;
    await settle();
    http.expectOne(settingsUrl).flush({ model: 'gemini-3.8-flash', host: 'generativelanguage.googleapis.com' });
    await settle();
  }

  async function setInput(name: string, value: unknown) {
    fixture.componentRef.setInput(name, value);
    await settle();
  }

  /** The error the page would hold for a failed request, parsed as the app parses it. */
  function failureOf(status: number, body: Record<string, unknown>): ApiError {
    return TestBed.inject(ApiErrors).parse(new HttpErrorResponse({ status, error: body }));
  }

  const type = async (text: string) => {
    const box = byTestId<HTMLTextAreaElement>(root, 'assistant-request')!;
    box.value = text;
    box.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  };

  const send = () => byTestId<HTMLButtonElement>(root, 'assistant-send')!;

  beforeEach(() => {
    session = fakeAssistantSession();
    TestBed.configureTestingModule({
      imports: [translocoTesting()],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements(), { provide: ASSISTANT_SESSION, useValue: session }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
    vi.useRealTimers();
  });

  describe('the disclosure', () => {
    it('names the model and the host, and says what is sent, including the saved script 1, and that no rows are', async () => {
      await open();

      const text = textOf(byTestId(root, 'assistant-disclosure'));
      expect(text).toContain('gemini-3.8-flash');
      expect(text).toContain('generativelanguage.googleapis.com');
      expect(text).toContain('the tables and columns with their labels');
      expect(text).toContain('the choice lists');
      expect(text).toContain('the saved script 1');
      expect(text).toContain('the text in the editor');
      expect(text).toContain('the errors of your last test');
      expect(text).toContain('No rows of data are sent');
    });

    it('stands without the model when the settings cannot be read', async () => {
      fixture = TestBed.createComponent(AssistantPanel);
      for (const [name, value] of Object.entries({ projectId: 'p1', kind: 'indicator', draft: noCellScript })) {
        fixture.componentRef.setInput(name, value);
      }
      root = fixture.nativeElement as HTMLElement;
      await settle();
      http.expectOne(settingsUrl).flush(null, { status: 503, statusText: 'Service Unavailable' });
      await settle();

      expect(byTestId(root, 'assistant-model')).toBeNull();
      expect(textOf(byTestId(root, 'assistant-disclosure'))).toContain('No rows of data are sent');
    });
  });

  describe('a draft', () => {
    it('sends the request box with the whole script as the scope (Draft an indicator)', async () => {
      await open();
      await type('share of households with piped water by municipality');

      expect(send().disabled).toBe(false);
      send().click();

      expect(session.send).toHaveBeenCalledTimes(1);
      expect(session.send).toHaveBeenCalledWith({ action: 'script', draft: noCellScript, cell: null, details });
    });

    it('shows the explanation and offers the script of an answer, and reports what the analyst chose', async () => {
      await open();
      const proposal: ProposalView = {
        callId: 'c1',
        notes: [],
        blocks: [{ lines: 34, definesContract: true, offers: ['script', 'insert'], marks: [] }],
      };
      session.thread.set([
        { role: 'user', content: 'share of households with piped water by municipality' },
        { role: 'assistant', content: 'This computes the share by municipality.\n[proposed code: 34 lines]' },
      ]);
      session.proposal.set(proposal);
      await settle();

      expect(textOf(byTestId(root, 'assistant-turn-assistant'))).toContain('This computes the share by municipality.');
      expect(textOf(byTestId(root, 'assistant-block-summary'))).toContain('34 lines');
      expect(textOf(byTestId(root, 'assistant-block-summary'))).toContain('Defines compute(cleaned, ctx).');
      expect(textOf(byTestId(root, 'assistant-apply-script-0'))).toBe('Replace script');
      expect(textOf(byTestId(root, 'assistant-apply-insert-0'))).toBe('Insert at cursor');
      expect(byTestId(root, 'assistant-apply-cell-0')).toBeNull();

      const applied: ApplyRequest[] = [];
      fixture.componentInstance.apply.subscribe((request) => applied.push(request));
      byTestId<HTMLButtonElement>(root, 'assistant-apply-insert-0')!.click();
      byTestId<HTMLButtonElement>(root, 'assistant-apply-script-0')!.click();
      expect(applied).toEqual([
        { block: 0, action: 'insert' },
        { block: 0, action: 'script' },
      ]);
    });

    it('lists the marks of the checks with their lines, and the notes of a proposal that is not whole', async () => {
      await open();
      session.proposal.set({
        callId: 'c1',
        notes: ['notOneScript', 'cutOff'],
        blocks: [
          { lines: 1, definesContract: false, offers: ['insert'], marks: [] },
          {
            lines: 12,
            definesContract: false,
            offers: ['insert'],
            marks: [{ line: 3, severity: 'warning', message: 'scipy is not available to scripts.' }],
          },
        ],
      });
      await settle();

      expect(textOf(byTestId(root, 'assistant-note-notOneScript'))).toContain('not one script');
      expect(textOf(byTestId(root, 'assistant-note-cutOff'))).toContain('cut off');
      expect(textOf(byTestId(root, 'assistant-block-0'))).toContain('Block 1 · 1 line');
      expect(textOf(byTestId(root, 'assistant-block-1'))).toContain('Block 2 · 12 lines');
      expect(textOf(byTestId(root, 'assistant-marks'))).toContain('Line 3');
      expect(textOf(byTestId(root, 'assistant-marks'))).toContain('scipy is not available to scripts.');
      expect(byTestId(root, 'assistant-apply-script-1')).toBeNull();
    });
  });

  describe('Draft from the details', () => {
    it('fills the box from the details and lets the analyst edit it', async () => {
      await open({ details: { label: 'Households with piped water', description: 'Share with a tap', unit: 'households', format: 'percent' } });

      byTestId<HTMLButtonElement>(root, 'assistant-draft-details')!.click();
      await settle();

      const box = byTestId<HTMLTextAreaElement>(root, 'assistant-request')!;
      expect(box.value).toBe(
        'Write the script for the indicator "Households with piped water".\nDescription: Share with a tap\nUnit: households\nFormat: percent',
      );
      expect(session.send).not.toHaveBeenCalled();
      await type(`${box.value}\nBy municipality.`);
      expect(session.text()).toContain('By municipality.');
    });

    it('leaves out the description and the unit when they are blank, and keeps what was typed above it', async () => {
      await open({ details: { label: 'Rate', description: '', unit: '  ', format: 'count' } });
      await type('Only completed interviews.');

      byTestId<HTMLButtonElement>(root, 'assistant-draft-details')!.click();

      expect(session.text()).toBe('Only completed interviews.\n\nWrite the script for the indicator "Rate".\nFormat: count');
    });

    it('uses the details as they are in the form now, saved or not (Unsaved details)', async () => {
      await open({ details: { ...details, format: 'percent' } });
      await setInput('details', { ...details, format: 'decimal' });

      byTestId<HTMLButtonElement>(root, 'assistant-draft-details')!.click();
      await settle();

      expect(byTestId<HTMLTextAreaElement>(root, 'assistant-request')!.value).toContain('Format: decimal');
      expect(byTestId<HTMLTextAreaElement>(root, 'assistant-request')!.value).not.toContain('percent');
    });

    it('switches to the script scope, and is offered only for an indicator', async () => {
      await open();
      session.scope.set('fix');
      byTestId<HTMLButtonElement>(root, 'assistant-draft-details')!.click();
      expect(session.setScope).toHaveBeenCalledWith('script');
      TestBed.resetTestingModule();

      session = fakeAssistantSession();
      TestBed.configureTestingModule({
        imports: [translocoTesting()],
        providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ASSISTANT_SESSION, useValue: session }],
      });
      http = TestBed.inject(HttpTestingController);
      await open({ kind: 'clean', name: null, details: null });
      expect(byTestId(root, 'assistant-draft-details')).toBeNull();
    });
  });

  describe('the scopes', () => {
    it('does not offer This cell when the script has no cell, and says it needs one (Cursor outside any cell)', async () => {
      await open({ draft: noCellScript, cursorLine: 2 });

      expect(byTestId(root, 'assistant-scope-cell')).toBeNull();
      expect(textOf(byTestId(root, 'assistant-needs-cell'))).toContain('A cell starts at a line with # %%');
      expect(byTestId(root, 'assistant-scope-script')!.getAttribute('aria-pressed')).toBe('true');
    });

    it('does not offer This cell while the cursor is above the first cell', async () => {
      await open({ draft: `import pandas as pd\n${cellScript}`, cursorLine: 1 });

      expect(byTestId(root, 'assistant-scope-cell')).toBeNull();
      expect(byTestId(root, 'assistant-needs-cell')).not.toBeNull();
    });

    it('offers This cell, named, as the cursor moves into a cell without any typing (Cursor moved into a cell)', async () => {
      await open({ draft: `import pandas as pd\n${cellScript}`, cursorLine: 1 });

      await setInput('cursorLine', 6);

      expect(textOf(byTestId(root, 'assistant-scope-cell'))).toBe('This cell: Rate');
      expect(byTestId(root, 'assistant-needs-cell')).toBeNull();

      await setInput('cursorLine', 9);
      expect(textOf(byTestId(root, 'assistant-scope-cell'))).toBe('This cell: Cell 3');
    });

    it('sends the cell at the cursor: its marker line and its text up to the line before the next marker', async () => {
      await open({ draft: cellScript, cursorLine: 5 });
      byTestId<HTMLButtonElement>(root, 'assistant-scope-cell')!.click();
      await settle();
      await type('exclude households with no members');

      send().click();

      expect(session.send).toHaveBeenCalledWith({
        action: 'cell',
        draft: cellScript,
        cell: { line: 4, text: '# %% Rate\nrate = df["has_water"].eq("Yes").mean()\nctx.log(rate)' },
        details,
      });
    });

    it('falls back to the script when the chosen cell is left, and takes it up again when the cursor returns', async () => {
      await open({ draft: cellScript, cursorLine: 5 });
      session.scope.set('cell');
      await settle();
      expect(byTestId(root, 'assistant-scope-cell')!.getAttribute('aria-pressed')).toBe('true');

      await setInput('draft', noCellScript);
      expect(byTestId(root, 'assistant-scope-script')!.getAttribute('aria-pressed')).toBe('true');
      await type('anything');
      send().click();
      expect(session.send).toHaveBeenCalledWith(expect.objectContaining({ action: 'script', cell: null }));

      await setInput('draft', cellScript);
      expect(byTestId(root, 'assistant-scope-cell')!.getAttribute('aria-pressed')).toBe('true');
    });

    it('offers the fix only while there is a valid target, and says to test again when it stops being valid', async () => {
      await open();
      expect(byTestId(root, 'assistant-scope-fix')).toBeNull();

      session.fixTarget.set({ testId: 't1' });
      session.scope.set('fix');
      await settle();
      expect(byTestId(root, 'assistant-scope-fix')!.getAttribute('aria-pressed')).toBe('true');
      expect(byTestId(root, 'assistant-fix-note')).not.toBeNull();
      expect(byTestId(root, 'assistant-test-again')).toBeNull();
      await type('fix the KeyError');
      send().click();
      expect(session.send).toHaveBeenLastCalledWith(expect.objectContaining({ action: 'fix' }));

      session.fixTarget.set(null);
      await settle();
      expect(byTestId(root, 'assistant-scope-fix')).toBeNull();
      expect(byTestId(root, 'assistant-scope-script')!.getAttribute('aria-pressed')).toBe('true');
      expect(textOf(byTestId(root, 'assistant-test-again'))).toContain('Test again before asking for a fix');
      expect(byTestId(root, 'assistant-fix-note')).toBeNull();
      send().click();
      expect(session.send).toHaveBeenLastCalledWith(expect.objectContaining({ action: 'script' }));
    });

    it('chooses a scope through the session', async () => {
      await open({ draft: cellScript, cursorLine: 5 });

      byTestId<HTMLButtonElement>(root, 'assistant-scope-cell')!.click();
      expect(session.setScope).toHaveBeenCalledWith('cell');
      byTestId<HTMLButtonElement>(root, 'assistant-scope-script')!.click();
      expect(session.setScope).toHaveBeenLastCalledWith('script');
    });
  });

  describe('the request box', () => {
    it('does not send a blank request', async () => {
      await open();
      expect(send().disabled).toBe(true);

      await type('   \n  ');
      expect(send().disabled).toBe(true);
      send().click();
      expect(session.send).not.toHaveBeenCalled();
    });

    it('does not send a request longer than a turn may be, and says how long it may be', async () => {
      await open();

      await type('x'.repeat(20 * 1024));

      expect(send().disabled).toBe(true);
      expect(textOf(byTestId(root, 'assistant-too-long'))).toBe('A request can be at most 16 KB. This one is 20.0 KB.');
    });

    it('counts bytes, not characters', async () => {
      await open();

      await type('ñ'.repeat(9 * 1024));

      expect(send().disabled).toBe(true);
      expect(byTestId(root, 'assistant-too-long')).not.toBeNull();
    });

    it('shows a counter near the limit and nothing for a short request', async () => {
      await open();
      await type('short');
      expect(byTestId(root, 'assistant-counter')).toBeNull();
      expect(byTestId(root, 'assistant-too-long')).toBeNull();

      await type('x'.repeat(13 * 1024));
      expect(textOf(byTestId(root, 'assistant-counter'))).toBe('13.0 of 16 KB');
      expect(send().disabled).toBe(false);

      await type('x'.repeat(16 * 1024));
      expect(send().disabled).toBe(false);
    });

    it('sends with Ctrl+Enter', async () => {
      await open();
      await type('a request');

      byTestId(root, 'assistant-request')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true, cancelable: true }));

      expect(session.send).toHaveBeenCalledTimes(1);
    });

    it('shows the text and the placeholder of the scope the session holds', async () => {
      session.text.set('what the session holds');
      await open();

      expect(byTestId<HTMLTextAreaElement>(root, 'assistant-request')!.value).toBe('what the session holds');
      expect(byTestId(root, 'assistant-request')!.getAttribute('placeholder')).toContain('Describe the script');
    });

    it('takes the focus when the page asks for it', async () => {
      await open();

      fixture.componentInstance.focusRequest();

      expect(document.activeElement).toBe(byTestId(root, 'assistant-request'));
    });
  });

  describe('waiting', () => {
    const request = (here: boolean, startedAt = Date.now()) => ({ here, scriptLabel: 'indicator coverage', text: 'the question', startedAt });

    it('shows that it is writing, with the seconds and Cancel, and cancels through the session', async () => {
      await open();
      session.pending.set(request(true, Date.now() - 20_000));
      await settle();

      expect(textOf(byTestId(root, 'assistant-writing'))).toContain('Writing…');
      expect(textOf(byTestId(root, 'assistant-elapsed'))).toBe('20 s');
      expect(textOf(byTestId(root, 'assistant-turn-pending'))).toContain('the question');
      expect(send().disabled).toBe(true);

      byTestId<HTMLButtonElement>(root, 'assistant-cancel')!.click();
      expect(session.cancel).toHaveBeenCalledTimes(1);
    });

    it('counts the seconds as time passes', async () => {
      vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
      await open();
      session.pending.set(request(true));
      await settle();
      expect(textOf(byTestId(root, 'assistant-elapsed'))).toBe('0 s');

      vi.advanceTimersByTime(3000);
      await settle();

      expect(textOf(byTestId(root, 'assistant-elapsed'))).toBe('3 s');
    });

    it('does not offer a new conversation while the request for this script is being written', async () => {
      await open();
      session.thread.set([{ role: 'user', content: 'a' }, { role: 'assistant', content: 'b' }]);
      await settle();
      expect(byTestId<HTMLButtonElement>(root, 'assistant-new-conversation')!.disabled).toBe(false);

      session.pending.set(request(true));
      await settle();
      expect(byTestId<HTMLButtonElement>(root, 'assistant-new-conversation')!.disabled).toBe(true);

      session.pending.set(null);
      await settle();
      byTestId<HTMLButtonElement>(root, 'assistant-new-conversation')!.click();
      expect(session.newConversation).toHaveBeenCalledTimes(1);
      await settle();
      expect(byTestId(root, 'assistant-new-conversation')).toBeNull();
      expect(byTestId(root, 'assistant-thread')).toBeNull();
    });

    it('says that a request for another script is in progress and keeps Send disabled', async () => {
      await open();
      await type('a request');
      expect(send().disabled).toBe(false);

      session.pending.set(request(false));
      await settle();

      expect(textOf(byTestId(root, 'assistant-pending-elsewhere'))).toBe(
        'A request for indicator coverage is in progress. Open that script to cancel it, or wait for the answer.',
      );
      expect(byTestId(root, 'assistant-writing')).toBeNull();
      expect(byTestId(root, 'assistant-turn-pending')).toBeNull();
      expect(send().disabled).toBe(true);
    });
  });

  describe('an older version on view', () => {
    it('says that the assistant works on the current text and offers no new request', async () => {
      await open({ viewing: true });

      expect(textOf(byTestId(root, 'assistant-viewing-older'))).toContain('works on the current text');
      for (const id of ['assistant-request', 'assistant-send', 'assistant-scopes', 'assistant-draft-details', 'assistant-show-context', 'assistant-copy-context']) {
        expect(byTestId(root, id)).toBeNull();
      }
    });

    it('keeps the waiting state and Cancel of a request being written, above the message', async () => {
      await open({ viewing: true });
      session.pending.set({ here: true, scriptLabel: 'indicator coverage', text: 'the question', startedAt: Date.now() });
      await settle();

      const writing = byTestId(root, 'assistant-writing')!;
      const message = byTestId(root, 'assistant-viewing-older')!;
      expect(writing).not.toBeNull();
      expect(byTestId(root, 'assistant-cancel')).not.toBeNull();
      expect(writing.compareDocumentPosition(message) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

      byTestId<HTMLButtonElement>(root, 'assistant-cancel')!.click();
      expect(session.cancel).toHaveBeenCalled();
    });

    it('returns to the request form when the current version is back', async () => {
      await open({ viewing: true });

      await setInput('viewing', false);

      expect(byTestId(root, 'assistant-viewing-older')).toBeNull();
      expect(byTestId(root, 'assistant-request')).not.toBeNull();
    });

    it('shows no proposal to apply while the editor is read-only', async () => {
      await open({ viewing: true });
      session.proposal.set({ callId: 'c1', notes: [], blocks: [{ lines: 3, definesContract: true, offers: ['script'], marks: [] }] });
      await settle();

      expect(byTestId(root, 'assistant-proposal')).toBeNull();
    });
  });

  describe('failures of a request', () => {
    async function show(error: ApiError) {
      await open();
      session.failure.set(error);
      await settle();
      return textOf(byTestId(root, 'assistant-failure-message'));
    }

    it('says the assistant is misconfigured after a wrong key (Wrong key)', async () => {
      const message = await show(failureOf(503, { code: 'assistant.misconfigured', detail: 'the provider said: invalid api key sk-123' }));

      expect(message).toBe('The assistant is misconfigured. Contact the administrator.');
      expect(textOf(byTestId(root, 'assistant-failure'))).not.toContain('sk-123');
    });

    it('says the assistant is misconfigured when the account is out of credit (Out of credit)', async () => {
      expect(await show(failureOf(503, { code: 'assistant.misconfigured' }))).toContain('Contact the administrator');
    });

    it('says the provider failed while writing (Failed while writing)', async () => {
      const message = await show(failureOf(502, { code: 'assistant.unavailable', detail: '{"error":"internal"}' }));

      expect(message).toContain('failed while writing');
      expect(message).not.toContain('internal');
      expect(byTestId(root, 'assistant-proposal')).toBeNull();
    });

    it("marks the API's own text as English", async () => {
      await show(failureOf(500, { detail: 'The model returned nothing.' }));

      expect(byTestId(root, 'assistant-failure-message')!.getAttribute('lang')).toBe('en');
    });

    it("leaves the text the UI wrote for a code as the page's language", async () => {
      await show(failureOf(504, { code: 'assistant.timeout' }));

      expect(byTestId(root, 'assistant-failure-message')!.hasAttribute('lang')).toBe(false);
    });

    it('says the provider did not answer in time (Timeout)', async () => {
      expect(await show(failureOf(504, { code: 'assistant.timeout' }))).toBe('The provider did not answer in time. Try again.');
    });

    it('says when to retry if the provider said so (Rate limit)', async () => {
      expect(await show(failureOf(429, { code: 'assistant.rateLimited', retryAfter: 30 }))).toBe(
        'The provider is limiting requests. Try again in 30 seconds.',
      );
    });

    it('says "1 second" in the singular when the provider asks to retry in a second (Rate limit)', async () => {
      expect(await show(failureOf(429, { code: 'assistant.rateLimited', retryAfter: 0.4 }))).toBe('The provider is limiting requests. Try again in 1 second.');
    });

    it('does not name a time when the provider gave none', async () => {
      expect(await show(failureOf(429, { code: 'assistant.rateLimited' }))).toBe('The provider is limiting requests. Try again in a moment.');
    });

    it('says when the budget renews (Budget reached)', async () => {
      const message = await show(failureOf(429, { code: 'assistant.budget', renewsAt: '2026-10-17T00:00:00Z' }));

      expect(message).toContain('You have used your daily allowance of assistant requests. It renews on ');
      expect(message).toMatch(/Oct 1[67], 2026, \d{1,2}:00 [AP]M\.$/);
    });

    it('says the budget renews at midnight UTC when the API gave no time', async () => {
      expect(await show(failureOf(429, { code: 'assistant.budget' }))).toContain('midnight UTC');
    });

    it('says a request is already in progress (Second request at the same time)', async () => {
      expect(await show(failureOf(409, { code: 'assistant.busy' }))).toBe('A request of yours is already in progress. Wait for it to finish, or cancel it.');
    });

    it('says the script is too large for the assistant', async () => {
      expect(await show(failureOf(422, { code: 'assistant.contextTooLarge' }))).toBe('The script is too large for the assistant.');
    });

    it('says the network failed when the server could not be reached', async () => {
      expect(await show(failureOf(0, {}))).toContain('could not reach the server');
    });

    it('can be dismissed, and goes away with the session', async () => {
      await show(failureOf(504, { code: 'assistant.timeout' }));

      byTestId<HTMLButtonElement>(root, 'assistant-failure-dismiss')!.click();
      await settle();

      expect(session.dismissFailure).toHaveBeenCalled();
      expect(byTestId(root, 'assistant-failure')).toBeNull();
    });

    it('shows the message back in the box that the session holds', async () => {
      session.text.set('the message that failed');
      await open();
      session.failure.set(failureOf(504, { code: 'assistant.timeout' }));
      await settle();

      expect(byTestId<HTMLTextAreaElement>(root, 'assistant-request')!.value).toBe('the message that failed');
      expect(send().disabled).toBe(false);
    });
  });

  describe('the thread', () => {
    it('shows the test was no longer available beside the answer it was given without (Expired test)', async () => {
      await open();
      session.thread.set([
        { role: 'user', content: 'fix the KeyError' },
        { role: 'assistant', content: 'Check the column name.', testExpired: true },
      ]);
      await settle();

      expect(textOf(byTestId(root, 'assistant-test-expired'))).toBe('The test was no longer available, so this answer was given without it.');
      expect(root.querySelectorAll('[data-testid="assistant-test-expired"]')).toHaveLength(1);
    });

    it('shows messages and explanations as plain text, never as markup', async () => {
      await open();
      session.thread.set([
        { role: 'user', content: '<img src=x onerror=alert(1)> and line one\nline two' },
        { role: 'assistant', content: '<b>bold</b>' },
      ]);
      await settle();

      expect(root.querySelector('img')).toBeNull();
      expect(root.querySelector('b')).toBeNull();
      expect(byTestId(root, 'assistant-turn-user')!.querySelector('.whitespace-pre-wrap')!.textContent).toContain('line one\nline two');
    });
  });

  describe('what will be sent', () => {
    const context = { model: 'gemini-3.8-flash', host: 'generativelanguage.googleapis.com', text: '<rules>…</rules>\n<schema>main: 480 rows</schema>', bytes: 2048, omitted: ['choice lists: 14 of 22 left out'] };

    it('shows the context of the next request in a dialog, before anything is typed (Before typing)', async () => {
      await open({ draft: cellScript });
      expect(byTestId<HTMLTextAreaElement>(root, 'assistant-request')!.value).toBe('');

      byTestId<HTMLButtonElement>(root, 'assistant-show-context')!.click();
      await settle();
      const call = http.expectOne(contextUrl);
      expect(call.request.method).toBe('POST');
      expect(call.request.body).toEqual({
        kind: 'indicator',
        name: 'piped_water_rate',
        action: 'script',
        messages: [{ role: 'user', content: '' }],
        draft: cellScript,
        details,
      });
      call.flush(context);
      await settle();
      await tick();

      const dialog = document.querySelector('app-assistant-context-dialog')!;
      expect(textOf(byTestId(dialog as HTMLElement, 'context-text'))).toBe('<rules>…</rules> <schema>main: 480 rows</schema>');
      expect(textOf(byTestId(dialog as HTMLElement, 'context-summary'))).toContain('2.0 KB');
      expect(textOf(byTestId(dialog as HTMLElement, 'context-omitted'))).toContain('choice lists: 14 of 22 left out');
      // What the API assembled is English whatever the interface language; the words around it are the UI's.
      expect(byTestId(dialog as HTMLElement, 'context-text')!.getAttribute('lang')).toBe('en');
      expect(dialog.querySelector('[data-testid="context-omitted"] li')!.getAttribute('lang')).toBe('en');
      expect(dialog.querySelector('[data-testid="context-omitted"] p')!.hasAttribute('lang')).toBe(false);
      expect(session.send).not.toHaveBeenCalled();
    });

    it('asks for the context of the scope, the cell and the text typed so far', async () => {
      await open({ draft: cellScript, cursorLine: 5 });
      byTestId<HTMLButtonElement>(root, 'assistant-scope-cell')!.click();
      await settle();
      await type('exclude households with no members');

      byTestId<HTMLButtonElement>(root, 'assistant-show-context')!.click();
      await settle();

      const body = http.expectOne(contextUrl).request.body;
      expect(body.action).toBe('cell');
      expect(body.focus).toEqual({ line: 4 });
      expect(body.messages).toEqual([{ role: 'user', content: 'exclude households with no members' }]);
    });

    it('shows why the context could not be built', async () => {
      await open();

      byTestId<HTMLButtonElement>(root, 'assistant-show-context')!.click();
      await settle();
      http.expectOne(contextUrl).flush({ code: 'assistant.contextTooLarge', status: 422 }, { status: 422, statusText: 'Unprocessable Entity' });
      await settle();

      expect(textOf(document.querySelector('[data-testid="context-error"]'))).toBe('The script is too large for the assistant.');
      // Translated from its code: the UI wrote it.
      expect(document.querySelector('[data-testid="context-error"] [lang]')).toBeNull();
    });

    it('closes the dialog', async () => {
      await open();
      byTestId<HTMLButtonElement>(root, 'assistant-show-context')!.click();
      await settle();
      http.expectOne(contextUrl).flush(context);
      await settle();

      document.querySelector<HTMLButtonElement>('[data-testid="close-context"]')!.click();
      await settle();

      expect(document.querySelector('app-assistant-context-dialog')).toBeNull();
    });

    describe('Copy context', () => {
      const writeText = vi.fn<(text: string) => Promise<void>>();
      beforeEach(() => {
        writeText.mockReset();
        writeText.mockResolvedValue(undefined);
        Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      });

      it('puts the text of the context route on the clipboard and says so', async () => {
        await open();

        byTestId<HTMLButtonElement>(root, 'assistant-copy-context')!.click();
        await settle();
        http.expectOne(contextUrl).flush(context);
        await settle();

        expect(writeText).toHaveBeenCalledWith(context.text);
        expect(TestBed.inject(ToastService).toasts().at(-1)).toEqual(expect.objectContaining({ tone: 'success', message: 'Context copied.' }));
        expect(byTestId<HTMLButtonElement>(root, 'assistant-copy-context')!.disabled).toBe(false);
      });

      it('copies the same context the dialog shows', async () => {
        await open();
        byTestId<HTMLButtonElement>(root, 'assistant-show-context')!.click();
        await settle();
        const shown = http.expectOne(contextUrl);
        const shownBody = shown.request.body;
        shown.flush(context);
        await settle();
        document.querySelector<HTMLButtonElement>('[data-testid="close-context"]')!.click();
        await settle();

        byTestId<HTMLButtonElement>(root, 'assistant-copy-context')!.click();
        await settle();
        const copied = http.expectOne(contextUrl);

        expect(copied.request.body).toEqual(shownBody);
        copied.flush(context);
        await settle();
        expect(writeText).toHaveBeenCalledWith(context.text);
      });

      it('shows the failure instead of copying when the context cannot be built', async () => {
        await open();

        byTestId<HTMLButtonElement>(root, 'assistant-copy-context')!.click();
        await settle();
        http.expectOne(contextUrl).flush({ code: 'assistant.disabled', status: 503 }, { status: 503, statusText: 'Service Unavailable' });
        await settle();

        expect(writeText).not.toHaveBeenCalled();
        expect(TestBed.inject(ToastService).toasts().at(-1)).toEqual(
          expect.objectContaining({ tone: 'danger', message: 'The assistant is not available in this installation.' }),
        );
        expect(byTestId<HTMLButtonElement>(root, 'assistant-copy-context')!.disabled).toBe(false);
      });

      it('says so when the browser refuses the clipboard', async () => {
        writeText.mockRejectedValue(new Error('denied'));
        await open();

        byTestId<HTMLButtonElement>(root, 'assistant-copy-context')!.click();
        await settle();
        http.expectOne(contextUrl).flush(context);
        await settle();

        expect(TestBed.inject(ToastService).toasts().at(-1)?.tone).toBe('danger');
      });
    });
  });
});
