import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { AuthService } from '../../../core/auth/auth.service';
import type { IndicatorMetadata, ScriptRef } from '../../../core/projects/pipeline.service';
import { AssistantSession } from './assistant-session';
import type { FixTarget, RequestFrame } from './assistant-state';

const completionsUrl = '/api/v1/projects/p1/assistant/completions';
const details: IndicatorMetadata = { label: '  Piped water  ', description: ' ', unit: 'households ', format: 'percent' };
const draft = 'def compute(cleaned, ctx):\n    return None\n';

describe('AssistantSession', () => {
  let session: AssistantSession;
  let http: HttpTestingController;
  const script = signal<ScriptRef>({ kind: 'indicator', name: 'piped_water_rate' });
  const fixValid = signal(true);

  const frame = (overrides: Partial<RequestFrame> = {}): RequestFrame => ({ action: 'script', draft, cell: null, details, ...overrides });
  const target: FixTarget = { testId: 't1', warning: { key: 'emptyValues', params: { measure: 'value', count: 4 } } };

  beforeEach(() => {
    sessionStorage.clear();
    script.set({ kind: 'indicator', name: 'piped_water_rate' });
    fixValid.set(true);
    TestBed.configureTestingModule({
      imports: [translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { subject: signal<string | null>('u1') } },
        AssistantSession,
      ],
    });
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(AssistantSession);
    session.connect({ projectId: () => 'p1', script, draft: signal(draft), inputs: signal(null), fixValid: () => fixValid() });
  });
  afterEach(() => sessionStorage.clear());

  it('builds the body from the selected script, the thread and the box, with the details trimmed', () => {
    session.setText('share by municipality');

    expect(session.requestBody(frame())).toEqual({
      kind: 'indicator',
      name: 'piped_water_rate',
      action: 'script',
      messages: [{ role: 'user', content: 'share by municipality' }],
      draft,
      details: { label: 'Piped water', description: '', unit: 'households', format: 'percent' },
    });
  });

  it('names no script for script 1 and sends no details', () => {
    script.set({ kind: 'clean' });
    session.setText('x');
    expect(session.requestBody(frame({ details: null }))).toEqual({ kind: 'clean', action: 'script', messages: [{ role: 'user', content: 'x' }], draft });
  });

  it('gives a blank box a blank last turn, as the context route accepts', () => {
    expect(session.requestBody(frame()).messages).toEqual([{ role: 'user', content: '' }]);
  });

  it('sends the line of the cell for a cell request, and a script request when there is no cell', () => {
    expect(session.requestBody(frame({ action: 'cell', cell: { line: 3, text: '# %% A' } }))).toMatchObject({ action: 'cell', focus: { line: 3 } });
    const body = session.requestBody(frame({ action: 'cell', cell: null }));
    expect(body.action).toBe('script');
    expect(body.focus).toBeUndefined();
  });

  describe('a fix', () => {
    it('adds the test and the warning of the fix target', () => {
      session.setFixTarget(target);
      expect(session.requestBody(frame({ action: 'fix' }))).toMatchObject({ action: 'fix', testId: 't1', warning: target.warning });
      session.setFixTarget({ testId: 't2' });
      const body = session.requestBody(frame({ action: 'fix' }));
      expect(body).toMatchObject({ action: 'fix', testId: 't2' });
      expect(body.warning).toBeUndefined();
    });

    it('is a script request while there is no valid target, and the target follows the page', () => {
      expect(session.fixTarget()).toBeNull();
      expect(session.requestBody(frame({ action: 'fix' })).action).toBe('script');

      session.setFixTarget(target);
      expect(session.fixTarget()).toEqual(target);
      fixValid.set(false);
      expect(session.fixTarget()).toBeNull();
      const body = session.requestBody(frame({ action: 'fix' }));
      expect(body.action).toBe('script');
      expect(body.testId).toBeUndefined();
    });

    it('belongs to the script it was set for', () => {
      session.setFixTarget(target);
      script.set({ kind: 'clean' });
      expect(session.fixTarget()).toBeNull();
      script.set({ kind: 'indicator', name: 'piped_water_rate' });
      expect(session.fixTarget()).toEqual(target);
    });

    it('goes back to the script scope when sending without a valid target', () => {
      session.setScope('fix');
      session.setText('fix it');
      session.send(frame({ action: 'script' }));
      expect(session.scope()).toBe('script');
      http.expectOne(completionsUrl);
    });
  });

  describe('sending', () => {
    it('does nothing for a blank request or one over 16 KB', () => {
      session.setText('   ');
      session.send(frame());
      session.setText('x'.repeat(16 * 1024 + 1));
      session.send(frame());
      http.expectNone(completionsUrl);
      expect(session.pending()).toBeNull();
    });

    it('makes one request at a time, for any script', () => {
      session.setText('first');
      session.send(frame());
      session.setText('second');
      session.send(frame());
      script.set({ kind: 'clean' });
      session.setText('third');
      session.send(frame({ details: null }));

      expect(http.match(completionsUrl)).toHaveLength(1);
    });

    it('marks the request as belonging to the script it was sent for', () => {
      session.setText('first');
      session.send(frame());
      expect(session.pending()).toMatchObject({ here: true, scriptLabel: 'indicator piped_water_rate', text: 'first' });
      expect(session.text()).toBe('');

      script.set({ kind: 'clean' });
      expect(session.pending()).toMatchObject({ here: false, scriptLabel: 'indicator piped_water_rate' });
      expect(session.text()).toBe('');
    });

    it('puts the text back in front of what was typed meanwhile when the request is cancelled', () => {
      session.setText('first');
      session.send(frame());
      session.setText('and then');
      session.cancel();

      expect(session.text()).toBe('first\n\nand then');
      expect(session.pending()).toBeNull();
      expect(http.expectOne(completionsUrl).cancelled).toBe(true);
    });

    it('ignores an answer that arrives after Cancel', () => {
      session.setText('first');
      session.send(frame());
      const request = http.expectOne(completionsUrl);
      session.cancel();
      expect(session.thread()).toEqual([]);
      expect(request.cancelled).toBe(true);
    });
  });
});
