import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CurrentUserService } from '../../core/current-user.service';
import { InterfaceLanguage } from '../../core/i18n/interface-language';
import { Member, ProjectRole } from '../../core/projects/projects.service';
import { SystemConfigService } from '../../core/system-config';
import { ToastService } from '../../shared/ui';
import { byTestId, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';
import { tick } from '../../../testing/cdk';

describe('ProjectMembers', () => {
  const members: Member[] = [
    { userId: 'u1', name: 'Ada', email: 'ada@example.org', role: 'admin' },
    { userId: 'u2', name: 'Grace', email: 'grace@example.org', role: 'viewer' },
  ];
  const twoAdmins: Member[] = [{ ...members[0] }, { userId: 'u3', name: 'Linus', email: 'linus@example.org', role: 'admin' }, members[1]];
  const invitation = { id: 'i1', email: 'later@example.org', role: 'analyst', createdAt: '2026-10-02T00:00:00Z' };
  const base = `/api/v1/projects/${projectId}`;
  const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;

  /** A deployment whose identity provider sends account emails. */
  const emailsOn = [{ provide: SystemConfigService, useValue: { accountEmailsEnabled: signal(true), assistantEnabled: signal(false) } }];

  async function open(
    role: ProjectRole,
    list: Member[] = members,
    invitations: unknown[] = [invitation],
    providers: Parameters<typeof openProjectSection>[3] = [],
    lang: InterfaceLanguage = 'en',
  ) {
    const opened = await openProjectSection('members', role, {}, providers, lang);
    opened.http.expectOne(`${base}/members`).flush(list);
    if (role === 'admin') {
      opened.http.expectOne(`${base}/invitations`).flush(invitations);
    }
    await settle(opened.harness);
    return opened;
  }

  async function reload(opened: Awaited<ReturnType<typeof open>>, list: Member[] = members, invitations: unknown[] = []) {
    await settle(opened.harness);
    opened.http.expectOne(`${base}/members`).flush(list);
    opened.http.expectOne(`${base}/invitations`).flush(invitations);
    await settle(opened.harness);
  }

  /** Opens the actions menu of a row and returns its items. */
  async function openMenu(opened: Awaited<ReturnType<typeof open>>, row: string) {
    byTestId(opened.root, row)!.querySelector<HTMLButtonElement>('[data-testid="menu-trigger"]')!.click();
    await settle(opened.harness);
    return Array.from(document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));
  }

  async function openDialog(opened: Awaited<ReturnType<typeof open>>) {
    byTestId<HTMLButtonElement>(opened.root, 'add-member')!.click();
    await settle(opened.harness);
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('gives non-admins a read-only list', async () => {
    const { root } = await open('analyst');

    expect(byTestId(root, 'member-list')?.textContent).toContain('Grace');
    expect(byTestId(root, 'add-member')).toBeNull();
    expect(root.querySelectorAll('[data-testid^="invitation-row-"]')).toHaveLength(0);
    expect(byTestId(root, 'menu-trigger')).toBeNull();
  });

  it('shows a skeleton until the list arrives, with the heading in place', async () => {
    const opened = await openProjectSection('members', 'viewer');
    expect(byTestId(opened.root, 'members-loading')).not.toBeNull();
    expect(text(opened.root.querySelector('h2'))).toBe('Members');
    opened.http.expectOne(`${base}/members`).flush(members);
    await settle(opened.harness);
    expect(byTestId(opened.root, 'members-loading')).toBeNull();
  });

  it('is one list at every width, with no table and no separate phone cards', async () => {
    const { root } = await open('admin');
    expect(root.querySelector('table')).toBeNull();
    expect(byTestId(root, 'member-cards')).toBeNull();
    expect(root.querySelectorAll('[data-testid^="member-row-"]')).toHaveLength(2);
  });

  it('leaves out the email line of a member who has none', async () => {
    const { root } = await open('viewer', [{ userId: 'u9', name: 'No Mail', email: null, role: 'viewer' }]);
    expect(byTestId(root, 'member-row-u9')!.querySelectorAll('p')).toHaveLength(1);
  });

  describe('adding a person', () => {
    it('has a button for admins that opens a dialog with the email, the role and a short hint', async () => {
      const opened = await open('admin');
      await openDialog(opened);
      const form = byTestId(overlay(), 'invite-form')!;
      expect(form.getAttribute('novalidate')).not.toBeNull();
      expect(text(form)).toContain('Tamiza sends no email');
      expect(form.querySelector<HTMLSelectElement>('#inviteRole')!.value).toBe('analyst');
      expect(byTestId(opened.root, 'invite-form')).toBeNull(); // not on the page
    });

    it('invites by email and role, closes the dialog and reloads the list', async () => {
      const opened = await open('admin');
      const { http, harness } = opened;
      await openDialog(opened);
      typeInto(overlay().querySelector<HTMLInputElement>('#inviteEmail')!, 'new@example.org');
      typeInto(overlay().querySelector<HTMLSelectElement>('#inviteRole')!, 'viewer');
      byTestId<HTMLFormElement>(overlay(), 'invite-form')!.dispatchEvent(new Event('submit'));
      await settle(harness);

      const request = http.expectOne(`${base}/members`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ email: 'new@example.org', role: 'viewer', language: 'en' });
      request.flush({ kind: 'invitation', member: null, invitation: { id: 'i2', email: 'new@example.org', role: 'viewer', createdAt: '' } });
      await tick();
      await reload(opened, members, [invitation, { id: 'i2', email: 'new@example.org', role: 'viewer', createdAt: '' }]);

      expect(TestBed.inject(ToastService).toasts()[0].message).toContain('becomes a member on their first sign-in');
      expect(byTestId(overlay(), 'invite-form')).toBeNull();
      expect(byTestId(opened.root, 'invitation-row-i2')?.textContent).toContain('new@example.org');
    });

    it('says a person who is already a member was added at once', async () => {
      const opened = await open('admin');
      await openDialog(opened);
      typeInto(overlay().querySelector<HTMLInputElement>('#inviteEmail')!, 'known@example.org');
      byTestId<HTMLFormElement>(overlay(), 'invite-form')!.dispatchEvent(new Event('submit'));
      await settle(opened.harness);
      opened.http.expectOne(`${base}/members`).flush({ kind: 'member', member: { userId: 'u4', name: 'Known', email: 'known@example.org', role: 'analyst' }, invitation: null });
      await tick();
      await reload(opened);
      expect(TestBed.inject(ToastService).toasts()[0].message).toContain('known@example.org is now a member');
    });

    it('explains in the dialog, with the email kept, when the person is already a member', async () => {
      const opened = await open('admin');
      await openDialog(opened);
      typeInto(overlay().querySelector<HTMLInputElement>('#inviteEmail')!, 'grace@example.org');
      byTestId<HTMLFormElement>(overlay(), 'invite-form')!.dispatchEvent(new Event('submit'));
      await settle(opened.harness);
      opened.http.expectOne(`${base}/members`).flush({ code: 'member.exists' }, { status: 409, statusText: 'Conflict' });
      await settle(opened.harness);

      expect(byTestId(overlay(), 'invite-error')?.textContent).toContain('already a member');
      expect(overlay().querySelector<HTMLInputElement>('#inviteEmail')!.value).toBe('grace@example.org');
    });

    it('shows a message for the email next to the field', async () => {
      const opened = await open('admin');
      await openDialog(opened);
      byTestId<HTMLFormElement>(overlay(), 'invite-form')!.dispatchEvent(new Event('submit'));
      await settle(opened.harness);
      opened.http.expectOne(`${base}/members`).flush({ errors: { email: ['Enter an email address.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(opened.harness);

      expect(text(overlay().querySelector('#inviteEmail-error'))).toBe('Enter an email address.');
      expect(byTestId(overlay(), 'invite-error')).toBeNull();
    });
  });

  it('lists the people invited as rows of the same list, with the role they are invited as', async () => {
    const { root } = await open('admin');
    const row = byTestId(root, 'invitation-row-i1')!;
    expect(byTestId(root, 'member-list')!.contains(row)).toBe(true);
    expect(text(row)).toContain('later@example.org');
    expect(text(row)).toContain('Invited as Analyst');
    expect(root.textContent).not.toContain('No pending invitations');
  });

  it('shows no row for invitations when there are none', async () => {
    const { root } = await open('admin', members, []);
    expect(root.querySelectorAll('[data-testid^="invitation-row-"]')).toHaveLength(0);
  });

  describe('a member’s row', () => {
    it('marks the signed-in person', async () => {
      const { root, harness } = await open('viewer');
      TestBed.inject(CurrentUserService).user.set({ id: 'u2', name: 'Grace', email: 'grace@example.org', isSuperAdmin: false });
      await settle(harness);
      expect(byTestId(byTestId(root, 'member-row-u2')!, 'you-badge')?.textContent?.trim()).toBe('You');
      expect(byTestId(root, 'member-row-u1')!.querySelector('[data-testid="you-badge"]')).toBeNull();
    });

    it('marks no one until the profile is known', async () => {
      const { root } = await open('viewer');
      expect(root.querySelectorAll('[data-testid="you-badge"]')).toHaveLength(0);
    });

    it('offers no demotion or removal for the only admin, and the other roles to the others', async () => {
      const opened = await open('admin');
      expect(byTestId(opened.root, 'member-row-u1')!.querySelector('[data-testid="menu-trigger"]')).toBeNull();
      const items = await openMenu(opened, 'member-row-u2');
      expect(items.map((i) => text(i))).toEqual(['Make admin', 'Make analyst', 'Remove from project']);
    });

    it('changes a role from the actions menu and reports the last-admin rule when the list was out of date', async () => {
      const opened = await open('admin', twoAdmins);
      const { http, harness } = opened;
      const trigger = byTestId(opened.root, 'member-row-u1')!.querySelector<HTMLButtonElement>('[data-testid="menu-trigger"]')!;
      expect(trigger.getAttribute('aria-label')).toBe('Actions for Ada');
      const items = await openMenu(opened, 'member-row-u1');
      expect(items.map((i) => text(i))).toEqual(['Make analyst', 'Make viewer', 'Remove from project']);
      items[1].click();
      await settle(harness);
      const request = http.expectOne(`${base}/members/u1`);
      expect(request.request.body).toEqual({ role: 'viewer' });
      request.flush({ code: 'project.last_admin' }, { status: 409, statusText: 'Conflict' });
      await settle(harness);

      expect(TestBed.inject(ToastService).toasts()[0].message).toContain('at least one admin');
    });

    it('removes a member after confirmation', async () => {
      const opened = await open('admin');
      const { http, harness } = opened;
      const items = await openMenu(opened, 'member-row-u2');
      items.at(-1)!.click();
      await settle(harness);
      expect(overlay().textContent).toContain('Remove Grace?');
      expect(overlay().textContent).toContain('They lose access to this project immediately');
      (document.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
      await tick();
      await settle(harness);
      const removal = http.expectOne(`${base}/members/u2`);
      expect(removal.request.method).toBe('DELETE');
      removal.flush(null);
      await reload(opened, [members[0]]);

      expect(byTestId(opened.root, 'member-row-u2')).toBeNull();
    });

    it('asks the person removing themselves in the second person', async () => {
      const opened = await open('admin', twoAdmins);
      TestBed.inject(CurrentUserService).user.set({ id: 'u3', name: 'Linus', email: 'linus@example.org', isSuperAdmin: false });
      await settle(opened.harness);
      const items = await openMenu(opened, 'member-row-u3');
      items.at(-1)!.click();
      await settle(opened.harness);
      expect(overlay().textContent).toContain('Remove yourself?');
      expect(overlay().textContent).toContain('You lose access to this project immediately');
    });
  });

  it('revokes an invitation from its menu, after confirmation', async () => {
    const opened = await open('admin');
    const { http, harness } = opened;
    const items = await openMenu(opened, 'invitation-row-i1');
    expect(items.map((i) => text(i))).toEqual(['Revoke']);
    items[0].click();
    await settle(harness);
    expect(overlay().textContent).toContain('Revoke the invitation for later@example.org?');
    (document.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    const request = http.expectOne(`${base}/invitations/i1`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    await reload(opened);

    expect(byTestId(opened.root, 'invitation-row-i1')).toBeNull();
  });

  describe('account emails', () => {
    const emailed = { ...invitation, lastEmailAt: '2026-10-18T14:30:00Z' };
    const lastToast = () => TestBed.inject(ToastService).toasts().at(-1)!;

    async function chooseItem(opened: Awaited<ReturnType<typeof open>>, row: string, label: string) {
      const items = await openMenu(opened, row);
      items.find((item) => text(item) === label)!.click();
      await settle(opened.harness);
    }

    it('tells the admin in the dialog that a person without an account gets an email', async () => {
      const opened = await open('admin', members, [invitation], emailsOn);
      await openDialog(opened);

      const form = text(byTestId(overlay(), 'invite-form'));

      expect(form).toContain('they get an email to set a password');
      expect(form).not.toContain('Tamiza sends no email');
    });

    it('still says that Tamiza sends no email when the deployment cannot send them', async () => {
      const opened = await open('admin');
      await openDialog(opened);

      expect(text(byTestId(overlay(), 'invite-form'))).toContain('Tamiza sends no email');
    });

    it('sends the interface language with the invitation', async () => {
      const opened = await open('admin', members, [invitation], emailsOn, 'es');
      await openDialog(opened);
      typeInto(overlay().querySelector<HTMLInputElement>('#inviteEmail')!, 'ana@example.org');
      byTestId<HTMLFormElement>(overlay(), 'invite-form')!.dispatchEvent(new Event('submit'));
      await settle(opened.harness);

      const request = opened.http.expectOne(`${base}/members`);

      expect(request.request.body).toEqual({ email: 'ana@example.org', role: 'analyst', language: 'es' });
    });

    it.each([
      ['sent', 'success', 'was emailed a link to set a password'],
      ['recentlySent', 'success', 'already emailed a link a few minutes ago'],
      ['existingAccount', 'info', 'already has an account, so no email was sent: let them know'],
      ['rateLimited', 'danger', 'the hourly limit is reached'],
      ['failed', 'danger', 'the email could not be sent'],
    ])('says what happened to the invitation email when it is %s', async (outcome, tone, words) => {
      const opened = await open('admin', members, [invitation], emailsOn);
      await openDialog(opened);
      typeInto(overlay().querySelector<HTMLInputElement>('#inviteEmail')!, 'ana@example.org');
      byTestId<HTMLFormElement>(overlay(), 'invite-form')!.dispatchEvent(new Event('submit'));
      await settle(opened.harness);
      opened.http.expectOne(`${base}/members`).flush({
        kind: 'invitation',
        member: null,
        invitation: { id: 'i9', email: 'ana@example.org', role: 'analyst', createdAt: '', lastEmailAt: null },
        emailOutcome: outcome,
      });
      await tick();
      await reload(opened);

      expect(lastToast().tone).toBe(tone);
      expect(lastToast().message).toContain('Invitation saved');
      expect(lastToast().message).toContain(words);
      expect(lastToast().message).toContain('ana@example.org');
    });

    it('shows when the last invitation email went out and offers to resend it', async () => {
      const opened = await open('admin', members, [emailed], emailsOn);

      expect(text(byTestId(opened.root, 'email-sent-i1'))).toMatch(/^Email sent .*2026/);
      expect((await openMenu(opened, 'invitation-row-i1')).map((i) => text(i))).toEqual(['Resend invitation email', 'Revoke']);
    });

    it('shows no email line for an invitation that was never emailed', async () => {
      const opened = await open('admin', members, [{ ...invitation, lastEmailAt: null }], emailsOn);

      expect(byTestId(opened.root, 'email-sent-i1')).toBeNull();
    });

    it('offers only Revoke, and no password reset, when the deployment cannot send emails', async () => {
      const opened = await open('admin', members, [emailed]);

      expect((await openMenu(opened, 'invitation-row-i1')).map((i) => text(i))).toEqual(['Revoke']);
      document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
      expect((await openMenu(opened, 'member-row-u2')).map((i) => text(i))).not.toContain('Send password reset email');
    });

    it('resends the invitation email, says so and reloads the list', async () => {
      const opened = await open('admin', members, [emailed], emailsOn);

      await chooseItem(opened, 'invitation-row-i1', 'Resend invitation email');
      const request = opened.http.expectOne(`${base}/invitations/i1/email`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ language: 'en' });
      request.flush({ ...emailed, lastEmailAt: '2026-10-19T08:00:00Z' });
      await tick();
      await reload(opened, members, [{ ...emailed, lastEmailAt: '2026-10-19T08:00:00Z' }]);

      expect(lastToast().tone).toBe('success');
      expect(lastToast().message).toBe('Invitation email sent to later@example.org.');
      expect(text(byTestId(opened.root, 'email-sent-i1'))).toContain('2026');
    });

    it('offers the password reset to the admin for each member, before removal, even for the only admin', async () => {
      const opened = await open('admin', members, [invitation], emailsOn);

      expect((await openMenu(opened, 'member-row-u2')).map((i) => text(i))).toEqual([
        'Make admin',
        'Make analyst',
        'Send password reset email',
        'Remove from project',
      ]);
      document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
      expect((await openMenu(opened, 'member-row-u1')).map((i) => text(i))).toEqual(['Send password reset email']);
    });

    it('asks for the password reset and says the email is on its way', async () => {
      const opened = await open('admin', members, [invitation], emailsOn);

      await chooseItem(opened, 'member-row-u2', 'Send password reset email');
      const request = opened.http.expectOne(`${base}/members/u2/password-reset`);
      expect(request.request.method).toBe('POST');
      request.flush(null, { status: 204, statusText: 'No Content' });
      await settle(opened.harness);

      expect(lastToast().tone).toBe('success');
      expect(lastToast().message).toBe('Password reset email on its way to grace@example.org.');
    });

    it.each([
      [300, 'Try again in 5 minutes'],
      [61, 'Try again in 2 minutes'],
      [60, 'Try again in 1 minute.'],
      [5, 'Try again in 1 minute.'],
    ])('says to try again later when the API asks to wait %s seconds, for both actions', async (seconds, words) => {
      const opened = await open('admin', members, [emailed], emailsOn);
      const refusal = { code: 'accountEmail.rateLimited', retryAfter: seconds };

      await chooseItem(opened, 'invitation-row-i1', 'Resend invitation email');
      opened.http.expectOne(`${base}/invitations/i1/email`).flush(refusal, { status: 429, statusText: 'Too Many Requests' });
      await settle(opened.harness);
      expect(lastToast().tone).toBe('danger');
      expect(lastToast().message).toContain('cannot be sent yet');
      expect(lastToast().message).toContain(words);

      document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
      await chooseItem(opened, 'member-row-u2', 'Send password reset email');
      opened.http.expectOne(`${base}/members/u2/password-reset`).flush(refusal, { status: 429, statusText: 'Too Many Requests' });
      await settle(opened.harness);
      expect(lastToast().message).toContain(words);
    });

    it('reports any other refusal with the message of its code', async () => {
      const opened = await open('admin', members, [emailed], emailsOn);

      await chooseItem(opened, 'member-row-u2', 'Send password reset email');
      opened.http.expectOne(`${base}/members/u2/password-reset`).flush({ code: 'accountEmail.noAccount' }, { status: 409, statusText: 'Conflict' });
      await settle(opened.harness);

      expect(lastToast().tone).toBe('danger');
      expect(lastToast().message).toContain('no active account');
    });

    it('reports a failure to send with the message of its code', async () => {
      const opened = await open('admin', members, [emailed], emailsOn);

      await chooseItem(opened, 'invitation-row-i1', 'Resend invitation email');
      opened.http.expectOne(`${base}/invitations/i1/email`).flush({ code: 'accountEmail.failed' }, { status: 502, statusText: 'Bad Gateway' });
      await settle(opened.harness);

      expect(lastToast().message).toBe('The email could not be sent. Try again later.');
    });
  });
});
