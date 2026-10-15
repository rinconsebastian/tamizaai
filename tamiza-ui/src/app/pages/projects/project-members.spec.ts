import { TestBed } from '@angular/core/testing';
import { CurrentUserService } from '../../core/current-user.service';
import { Member, ProjectRole } from '../../core/projects/projects.service';
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

  async function open(role: ProjectRole, list: Member[] = members, invitations: unknown[] = [invitation]) {
    const opened = await openProjectSection('members', role);
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
      expect(request.request.body).toEqual({ email: 'new@example.org', role: 'viewer' });
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
});
