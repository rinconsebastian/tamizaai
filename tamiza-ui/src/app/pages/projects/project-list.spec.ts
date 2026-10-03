import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TestBed } from '@angular/core/testing';
import { byTestId, configureApp, settle } from '../../../testing/project-harness';

describe('ProjectListPage', () => {
  async function openList(projects: unknown[]) {
    const http = configureApp();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/projects');
    await settle(harness);
    http.expectOne('/api/v1/projects').flush(projects);
    await navigation;
    await settle(harness);
    return harness.routeNativeElement as HTMLElement;
  }

  it('lists projects with their form and the user role', async () => {
    const page = await openList([
      { id: 'p1', name: 'Household survey 2026', formName: 'Household survey', myRole: 'admin' },
      { id: 'p2', name: 'Market study', formName: 'Market form', myRole: 'viewer' },
    ]);

    const cards = page.querySelectorAll('[data-testid="project-card"]');
    expect(cards).toHaveLength(2);
    expect(cards[0].textContent).toContain('Household survey 2026');
    expect(cards[0].textContent).toContain('Admin');
    expect(cards[1].getAttribute('href')).toBe('/projects/p2');
    expect(byTestId(page, 'new-project')).not.toBeNull();
  });

  it('shows an empty state that invites creating a project', async () => {
    const page = await openList([]);

    expect(byTestId(page, 'empty-state')?.textContent).toContain('No projects yet');
    expect(byTestId(page, 'empty-state')?.querySelector('a')?.getAttribute('href')).toBe('/projects/new');
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
