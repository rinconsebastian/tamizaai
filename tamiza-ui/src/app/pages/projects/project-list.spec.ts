import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TestBed } from '@angular/core/testing';
import { CurrentUser } from '../../core/current-user.service';
import { byTestId, configureApp, flushMe, settle } from '../../../testing/project-harness';

describe('ProjectListPage', () => {
  /** Opens the list; `profile` answers `/api/v1/me`, or null leaves it pending. */
  async function openList(projects: unknown[], profile: Partial<CurrentUser> | null = {}) {
    const http = configureApp();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/projects');
    await settle(harness);
    http.expectOne('/api/v1/projects').flush(projects);
    await navigation;
    if (profile) {
      flushMe(http, profile);
    }
    await settle(harness);
    return { http, harness, page: harness.routeNativeElement as HTMLElement };
  }

  const twoProjects = [
    { id: 'p1', name: 'Household survey 2026', formName: 'Household survey', myRole: 'admin' },
    { id: 'p2', name: 'Market study', formName: 'Market form', myRole: 'viewer' },
  ];

  it('lists projects with their form and the user role', async () => {
    const { page } = await openList(twoProjects);

    const cards = page.querySelectorAll('[data-testid="project-card"]');
    expect(cards).toHaveLength(2);
    expect(cards[0].textContent).toContain('Household survey 2026');
    expect(cards[0].textContent).toContain('Admin');
    expect(cards[1].getAttribute('href')).toBe('/projects/p2');
    expect(byTestId(page, 'new-project')).not.toBeNull();
  });

  it('shows an empty state that invites creating a project to a user who may', async () => {
    const { page } = await openList([]);

    expect(byTestId(page, 'empty-state')?.textContent).toContain('No projects yet');
    expect(byTestId(page, 'empty-state')?.textContent).toContain('Create one to start');
    expect(byTestId(page, 'empty-state')?.querySelector('a')?.getAttribute('href')).toBe('/projects/new');
  });

  it('offers no New project to a user who may not create projects', async () => {
    const { page } = await openList(twoProjects, { canCreateProjects: false });

    expect(page.querySelectorAll('[data-testid="project-card"]')).toHaveLength(2);
    expect(byTestId(page, 'new-project')).toBeNull();
  });

  it('tells a user who may not create projects to ask a project admin, with no link', async () => {
    const { page } = await openList([], { canCreateProjects: false });

    const empty = byTestId(page, 'empty-state')!;
    expect(empty.textContent).toContain('No projects yet');
    expect(empty.textContent).toContain('Ask a project admin to invite you');
    expect(empty.querySelector('a')).toBeNull();
    expect(page.querySelector('a[href="/projects/new"]')).toBeNull();
  });

  it('offers a superadmin to create the first project', async () => {
    const { page } = await openList([], { isSuperAdmin: true, canCreateProjects: true });

    expect(byTestId(page, 'empty-state')?.querySelector('a')?.getAttribute('href')).toBe('/projects/new');
  });

  it('offers no way to create a project until the profile arrives', async () => {
    const { http, harness, page } = await openList(twoProjects, null);

    expect(byTestId(page, 'new-project')).toBeNull();

    flushMe(http);
    await settle(harness);
    expect(byTestId(page, 'new-project')).not.toBeNull();
  });

  it('says nothing about creating a project in an empty list until the profile arrives', async () => {
    const { http, harness, page } = await openList([], null);

    const empty = byTestId(page, 'empty-state')!;
    expect(empty.textContent).not.toContain('Ask a project admin');
    expect(empty.textContent).not.toContain('Create one to start');
    expect(page.querySelector('a[href="/projects/new"]')).toBeNull();

    flushMe(http);
    await settle(harness);
    expect(page.querySelector('a[href="/projects/new"]')).not.toBeNull();
  });

  it('offers no way to create a project when the profile cannot be read', async () => {
    const { http, harness, page } = await openList([], null);
    http.expectOne('/api/v1/me').flush({}, { status: 500, statusText: 'Server Error' });
    await settle(harness);

    expect(page.querySelector('a[href="/projects/new"]')).toBeNull();
    expect(byTestId(page, 'empty-state')?.textContent).toContain('Ask a project admin');
  });

  it('reads the profile again each time it opens and shows a role change', async () => {
    const first = await openList(twoProjects, { canCreateProjects: false });
    expect(byTestId(first.page, 'new-project')).toBeNull();

    // Made admin elsewhere, then back to the list.
    await first.harness.navigateByUrl('/nowhere');
    const navigation = first.harness.navigateByUrl('/projects');
    await settle(first.harness);
    first.http.expectOne('/api/v1/projects').flush(twoProjects);
    await navigation;
    flushMe(first.http, { canCreateProjects: true });
    await settle(first.harness);

    expect(byTestId(first.harness.routeNativeElement as HTMLElement, 'new-project')).not.toBeNull();
  });

  it('is the default route', async () => {
    const http = configureApp();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/');
    await settle(harness);
    http.expectOne('/api/v1/projects').flush([]);
    await navigation;

    expect(TestBed.inject(Router).url).toBe('/projects');
  });
});
