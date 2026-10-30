import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Invitation, InviteResult, ProjectsService } from './projects.service';

describe('ProjectsService: people and account emails', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { http: TestBed.inject(HttpTestingController), service: TestBed.inject(ProjectsService) };
  }

  const invitation: Invitation = { id: 'i1', email: 'ana@example.org', role: 'analyst', createdAt: '2026-10-18T09:00:00Z', lastEmailAt: '2026-10-18T09:00:01Z' };

  it('invites with the email, the role and the interface language', () => {
    const { http, service } = setup();
    let result: InviteResult | undefined;

    service.invite('p1', 'ana@example.org', 'analyst', 'es').subscribe((r) => (result = r));
    const request = http.expectOne('/api/v1/projects/p1/members');
    request.flush({ kind: 'invitation', member: null, invitation, emailOutcome: 'sent' });

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'ana@example.org', role: 'analyst', language: 'es' });
    expect(result?.emailOutcome).toBe('sent');
    expect(result?.invitation?.lastEmailAt).toBe('2026-10-18T09:00:01Z');
  });

  it('invites without a language when none is given', () => {
    const { http, service } = setup();

    service.invite('p1', 'ana@example.org', 'viewer').subscribe();
    const request = http.expectOne('/api/v1/projects/p1/members');
    request.flush({ kind: 'member', member: null, invitation: null });

    expect(request.request.body).toEqual({ email: 'ana@example.org', role: 'viewer', language: undefined });
  });

  it('asks for the invitation email again and gets the invitation back', () => {
    const { http, service } = setup();
    let result: Invitation | undefined;

    service.resendInvitationEmail('p1', 'i1', 'en').subscribe((r) => (result = r));
    const request = http.expectOne('/api/v1/projects/p1/invitations/i1/email');
    request.flush(invitation);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ language: 'en' });
    expect(result).toEqual(invitation);
  });

  it('asks for a password reset email for a member', () => {
    const { http, service } = setup();
    let done = false;

    service.sendPasswordReset('p1', 'u7').subscribe({ complete: () => (done = true) });
    const request = http.expectOne('/api/v1/projects/p1/members/u7/password-reset');
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    expect(done).toBe(true);
  });
});

describe('ProjectsService: access for AI clients', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { http: TestBed.inject(HttpTestingController), service: TestBed.inject(ProjectsService) };
  }

  it('patches only the level when an admin chooses one', () => {
    const { http, service } = setup();
    let level: string | null | undefined;

    service.update('p1', { mcpAccess: 'aggregates' }).subscribe((project) => (level = project.mcpAccess));
    const request = http.expectOne('/api/v1/projects/p1');
    request.flush({ id: 'p1', mcpAccess: 'aggregates', mcpMaxAccess: 'rows' });

    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ mcpAccess: 'aggregates' });
    expect(level).toBe('aggregates');
  });

  it('surfaces the refusal of rows above the deployment maximum as a field error on mcpAccess', () => {
    const { http, service } = setup();
    let failure: { status: number; error: { errors: Record<string, string[]> } } | undefined;

    service.update('p1', { mcpAccess: 'rows' }).subscribe({ error: (error) => (failure = error) });
    http
      .expectOne('/api/v1/projects/p1')
      .flush({ errors: { mcpAccess: ['The operator of this installation allows summaries only.'] } }, { status: 400, statusText: 'Bad Request' });

    expect(failure?.status).toBe(400);
    expect(failure?.error.errors['mcpAccess']).toEqual(['The operator of this installation allows summaries only.']);
  });
});
