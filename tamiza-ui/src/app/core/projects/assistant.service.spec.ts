import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../../../testing/transloco-testing';
import { ApiError, ApiErrors } from '../api-error';
import { AssistantAnswer, AssistantContext, AssistantRequest, AssistantService } from './assistant.service';

const base = '/api/v1/projects/p1/assistant';

const request: AssistantRequest = {
  kind: 'indicator',
  name: 'piped_water_rate',
  action: 'fix',
  messages: [{ role: 'user', content: 'Fix the KeyError.' }],
  draft: 'def compute(cleaned, ctx):\n    return None\n',
  details: { label: 'Piped water', description: '', unit: 'households', format: 'percent' },
  testId: 't1',
  warning: { key: 'emptyValues', params: { measure: 'value', count: 4 } },
};

describe('AssistantService', () => {
  let service: AssistantService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [translocoTesting()], providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AssistantService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the model and the host of the provider', () => {
    let settings: unknown;
    service.settings('p1').subscribe((value) => (settings = value));

    const call = http.expectOne(base);
    expect(call.request.method).toBe('GET');
    call.flush({ model: 'gemini-3.8-flash', host: 'generativelanguage.googleapis.com' });
    expect(settings).toEqual({ model: 'gemini-3.8-flash', host: 'generativelanguage.googleapis.com' });
  });

  it('posts the request as it is to the context route', () => {
    const context: AssistantContext = { model: 'm', host: 'h', text: '<rules>…</rules>', bytes: 16, omitted: ['choice lists: 3 of 5 left out'] };
    let received: AssistantContext | undefined;
    service.context('p1', request).subscribe((value) => (received = value));

    const call = http.expectOne(`${base}/context`);
    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual(request);
    call.flush(context);
    expect(received).toEqual(context);
  });

  it('posts the request as it is to the completions route', () => {
    const answer: AssistantAnswer = {
      callId: 'c1',
      explanation: 'The column is called water_source.\n[proposed code: 3 lines]',
      blocks: [{ code: 'def compute(cleaned, ctx):\n    return None\n', complete: true }],
      truncated: false,
      testExpired: false,
      omitted: [],
      usage: { promptTokens: 758, completionTokens: 102, totalTokens: 1725, estimated: false },
    };
    let received: AssistantAnswer | undefined;
    service.complete('p1', request).subscribe((value) => (received = value));

    const call = http.expectOne(`${base}/completions`);
    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual(request);
    call.flush(answer);
    expect(received).toEqual(answer);
  });

  it('sends only the request that was given: no fields are added', () => {
    const minimal: AssistantRequest = { kind: 'clean', action: 'script', messages: [], draft: '' };
    service.context('p1', minimal).subscribe();

    expect(http.expectOne(`${base}/context`).request.body).toEqual({ kind: 'clean', action: 'script', messages: [], draft: '' });
  });

  it.each(['script', 'cell', 'insert'] as const)('records how a proposal was applied: %s', (applied) => {
    let done = false;
    service.recordOutcome('p1', 'call-1', applied).subscribe({ complete: () => (done = true) });

    const call = http.expectOne(`${base}/calls/call-1/outcome`);
    expect(call.request.method).toBe('PUT');
    expect(call.request.body).toEqual({ applied });
    call.flush(null, { status: 204, statusText: 'No Content' });
    expect(done).toBe(true);
  });

  it('aborts the request to the API when the caller unsubscribes', () => {
    const subscription = service.complete('p1', request).subscribe();

    const call = http.expectOne(`${base}/completions`);
    subscription.unsubscribe();

    expect(call.cancelled).toBe(true);
  });

  describe('failures', () => {
    let errors: ApiErrors;
    let failure: unknown;

    beforeEach(() => {
      errors = TestBed.inject(ApiErrors);
      failure = undefined;
      service.complete('p1', request).subscribe({ error: (error) => (failure = error) });
    });

    function parsed(): ApiError {
      expect(failure).toBeInstanceOf(HttpErrorResponse);
      return errors.parse(failure);
    }

    it('carries retryAfter of a rate limit to the parsed error', () => {
      http.expectOne(`${base}/completions`).flush({ title: 'Rate limited', status: 429, code: 'assistant.rateLimited', retryAfter: 30 }, { status: 429, statusText: 'Too Many Requests' });

      expect(parsed().code).toBe('assistant.rateLimited');
      expect(parsed().extensions['retryAfter']).toBe(30);
    });

    it('carries renewsAt of a spent budget to the parsed error', () => {
      http
        .expectOne(`${base}/completions`)
        .flush({ title: 'Budget', status: 429, code: 'assistant.budget', renewsAt: '2026-10-17T00:00:00Z' }, { status: 429, statusText: 'Too Many Requests' });

      expect(parsed().code).toBe('assistant.budget');
      expect(parsed().extensions['renewsAt']).toBe('2026-10-17T00:00:00Z');
    });

    it('carries the code of a request already in progress', () => {
      http.expectOne(`${base}/completions`).flush({ status: 409, code: 'assistant.busy' }, { status: 409, statusText: 'Conflict' });

      expect(parsed().code).toBe('assistant.busy');
      expect(parsed().message).toContain('already');
    });
  });
});
