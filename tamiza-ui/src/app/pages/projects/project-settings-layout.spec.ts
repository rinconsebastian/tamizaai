import { By } from '@angular/platform-browser';
import { TestBed } from '@angular/core/testing';
import { NavigationCancel, NavigationError, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ProjectRole } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
import { byTestId, configureApp, details, openProjectSection, projectId, settingsRequests, settle } from '../../../testing/project-harness';
import { ProjectContext } from './project-context';

const pill = (root: HTMLElement, label: string) =>
  Array.from(root.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Settings"] a')).find((a) => a.textContent?.trim() === label)!;
const later = () => new Promise<void>((resolve) => setTimeout(resolve, 20));

/** The configuration addresses, and the element each one's page puts on the screen. */
const sections: [string, string][] = [
  ['overview', 'app-project-overview'],
  ['data', 'app-project-data'],
  ['scripts', 'app-project-scripts'],
  ['reference', 'app-project-reference'],
  ['sampling-frame', 'app-project-sampling-frame'],
  ['members', 'app-project-members'],
  ['webhook', 'app-project-webhook'],
];

/** Opens a configuration address as the router would from a bookmark, keeping every navigation that fails or is cancelled. */
async function openAddress(section: string, role: ProjectRole) {
  const http = configureApp();
  const harness = await RouterTestingHarness.create();
  const router = TestBed.inject(Router);
  const failures: string[] = [];
  router.events.subscribe((event) => {
    if (event instanceof NavigationError || event instanceof NavigationCancel) {
      failures.push(`${event.constructor.name}: ${event.url}`);
    }
  });
  const navigation = harness.navigateByUrl(`/projects/${projectId}/${section}`);
  await settle(harness);
  http.expectOne(`/api/v1/projects/${projectId}`).flush(details(role));
  await navigation;
  await settle(harness);
  return { http, harness, router, failures, root: harness.fixture.nativeElement as HTMLElement };
}

describe('ProjectSettingsLayout', () => {
  it('does not move focus when a configuration section is opened from its address', async () => {
    const { root } = await openProjectSection('members', 'admin');
    await later();
    expect(root.contains(document.activeElement) && document.activeElement?.closest('nav[aria-label="Settings"]')).toBeFalsy();
  });

  it('moves focus to the newly current pill after a navigation inside the page', async () => {
    const { http, harness, root } = await openProjectSection('overview', 'admin');
    pill(root, 'Members').click();
    await settle(harness);
    http.match((r) => r.url.includes('/members') || r.url.includes('/invitations')).forEach((r) => r.flush([]));
    await settle(harness);
    await later();
    expect(document.activeElement).toBe(pill(root, 'Members'));
    expect(pill(root, 'Members').getAttribute('aria-current')).toBe('page');
    expect(pill(root, 'Members').getAttribute('href')).toBe(`/projects/${projectId}/members`);
  });

  it.each<ProjectRole>(['analyst', 'admin'])('still draws the pills and the section for an %s', async (role) => {
    const { root, harness } = await openProjectSection('reference', role);

    expect(root.querySelector('nav[aria-label="Settings"]')).not.toBeNull();
    expect(root.querySelector('app-project-reference')).not.toBeNull();
    expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/reference`);
    harness.fixture.destroy();
  });

  describe('for a viewer', () => {
    it.each(sections)('takes the address of %s to the performance dashboard in its place, asking for none of its data (Viewer opens a configuration bookmark)', async (section, element) => {
      const { http, harness, router, failures, root } = await openAddress(section, 'viewer');

      expect(router.url).toBe(`/projects/${projectId}/performance`);
      expect(router.lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
      expect(root.querySelector(element)).toBeNull();
      expect(root.querySelector('nav[aria-label="Settings"]')).toBeNull();
      expect(root.querySelector('nav a[aria-current="page"]')?.textContent?.trim()).toBe('Performance');
      expect(root.querySelector('app-project-performance')).not.toBeNull();
      expect(settingsRequests(http)).toEqual([]);
      expect(TestBed.inject(ToastService).toasts()).toEqual([]);
      expect(failures).toEqual([]);
      harness.fixture.destroy();
    });

    it('replaces the configuration address in the history, so going back reaches the page before it (Going back after the redirect)', async () => {
      const http = configureApp();
      const harness = await RouterTestingHarness.create();
      const router = TestBed.inject(Router);
      await harness.navigateByUrl('/nowhere');
      const navigation = harness.navigateByUrl(`/projects/${projectId}/members`);
      await settle(harness);
      http.expectOne(`/api/v1/projects/${projectId}`).flush(details('viewer'));
      await navigation;
      await settle(harness);

      expect(router.url).toBe(`/projects/${projectId}/performance`);
      // replaceUrl swaps the history entry of the members address for this one, so going back skips the members address.
      expect(router.lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
      expect(router.lastSuccessfulNavigation()?.previousNavigation?.finalUrl?.toString()).toBe(`/projects/${projectId}/members`);
    });
  });

  it('takes an admin who lowers their own role to viewer to the performance dashboard', async () => {
    const { http, harness, root } = await openProjectSection('members', 'admin');
    http.match((r) => r.url.endsWith('/members') || r.url.endsWith('/invitations')).forEach((r) => r.flush([]));
    await settle(harness);
    const context = harness.fixture.debugElement.query(By.css('app-project-members')).injector.get(ProjectContext);

    context.update(details('viewer'));
    await settle(harness);

    const router = TestBed.inject(Router);
    expect(router.url).toBe(`/projects/${projectId}/performance`);
    expect(router.lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
    expect(root.querySelector('app-project-members')).toBeNull();
    expect(byTestId(root, 'settings-tab')).toBeNull();
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
  });
});
