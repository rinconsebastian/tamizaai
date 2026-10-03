import { TestBed } from '@angular/core/testing';
import { ToastService } from '../../shared/ui';
import { ProjectRole } from '../../core/projects/projects.service';
import { byTestId, openProjectSection, projectId, settle } from '../../../testing/project-harness';
import { pressKey, tick } from '../../../testing/cdk';

describe('ProjectMembers', () => {
  const members = [
    { userId: 'u1', name: 'Ada', email: 'ada@example.org', role: 'admin' },
    { userId: 'u2', name: 'Grace', email: 'grace@example.org', role: 'viewer' },
  ];
  const base = `/api/v1/projects/${projectId}`;

  async function open(role: ProjectRole) {
    const opened = await openProjectSection('members', role);
    opened.http.expectOne(`${base}/members`).flush(members);
    if (role === 'admin') {
      opened.http.expectOne(`${base}/invitations`).flush([{ id: 'i1', email: 'later@example.org', role: 'analyst', createdAt: '2026-10-02T00:00:00Z' }]);
    }
    await settle(opened.harness);
    return opened;
  }

  async function reload(opened: Awaited<ReturnType<typeof open>>) {
    await settle(opened.harness);
    opened.http.expectOne(`${base}/members`).flush(members);
    opened.http.expectOne(`${base}/invitations`).flush([]);
    await settle(opened.harness);
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('gives non-admins a read-only list', async () => {
    const { root } = await open('analyst');

    expect(byTestId(root, 'member-table')?.textContent).toContain('Grace');
    expect(byTestId(root, 'invite-form')).toBeNull();
    expect(byTestId(root, 'invitations')).toBeNull();
    expect(byTestId(root, 'menu-trigger')).toBeNull();
  });

  it('invites by email and role and reloads the list', async () => {
    const opened = await open('admin');
    const { root, http, harness } = opened;
    (root.querySelector('#inviteEmail') as HTMLInputElement).value = 'new@example.org';
    (root.querySelector('#inviteRole') as HTMLSelectElement).value = 'viewer';

    byTestId<HTMLFormElement>(root, 'invite-form')!.dispatchEvent(new Event('submit'));
    await settle(harness);
    const request = http.expectOne(`${base}/members`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'new@example.org', role: 'viewer' });
    request.flush({ kind: 'invitation', member: null, invitation: { id: 'i2', email: 'new@example.org', role: 'viewer', createdAt: '' } });
    await reload(opened);

    expect(TestBed.inject(ToastService).toasts()[0].message).toContain('becomes a member on their first sign-in');
  });

  it('explains when the person is already a member', async () => {
    const { root, http, harness } = await open('admin');
    (root.querySelector('#inviteEmail') as HTMLInputElement).value = 'grace@example.org';

    byTestId<HTMLFormElement>(root, 'invite-form')!.dispatchEvent(new Event('submit'));
    await settle(harness);
    http.expectOne(`${base}/members`).flush({ code: 'member.exists' }, { status: 409, statusText: 'Conflict' });
    await settle(harness);

    expect(byTestId(root, 'invite-error')?.textContent).toContain('already a member');
  });

  it('changes a role from the actions menu and reports the last-admin rule', async () => {
    const { root, http, harness } = await open('admin');
    const trigger = byTestId(root, 'member-table')!.querySelector<HTMLButtonElement>('[data-testid="menu-trigger"]')!;
    expect(trigger.getAttribute('aria-label')).toBe('Actions for Ada');

    trigger.focus();
    pressKey(trigger, 'ArrowDown');
    await settle(harness);
    const items = Array.from(document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));
    expect(items.map((i) => i.textContent?.trim())).toEqual(['Make analyst', 'Make viewer', 'Remove from project']);
    items[1].click();
    await settle(harness);
    const request = http.expectOne(`${base}/members/u1`);
    expect(request.request.body).toEqual({ role: 'viewer' });
    request.flush({ code: 'project.last_admin' }, { status: 409, statusText: 'Conflict' });
    await settle(harness);

    expect(TestBed.inject(ToastService).toasts()[0].message).toContain('at least one admin');
  });

  it('removes a member after confirmation and revokes invitations', async () => {
    const opened = await open('admin');
    const { root, http, harness } = opened;
    const trigger = byTestId(root, 'member-table')!.querySelectorAll<HTMLButtonElement>('[data-testid="menu-trigger"]')[1];
    trigger.focus();
    pressKey(trigger, 'ArrowDown');
    await settle(harness);
    Array.from(document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')).at(-1)!.click();
    await settle(harness);
    (document.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    const removal = http.expectOne(`${base}/members/u2`);
    expect(removal.request.method).toBe('DELETE');
    removal.flush(null);
    await reload(opened);

    expect(byTestId(root, 'invitations')?.textContent).toContain('No pending invitations');
  });
});
