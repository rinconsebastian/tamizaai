import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ProjectRole } from '../../core/projects/projects.service';
import { RouterTestingHarness } from '@angular/router/testing';
import { byTestId, configureApp, details, openProjectSection, projectId, settle } from '../../../testing/project-harness';

/** The links of the top tab row only; the settings pills sit in a second nav. */
const tabs = (root: HTMLElement) => Array.from(root.querySelector('nav')!.querySelectorAll('a')).map((a) => a.textContent?.trim());
const settingsPills = (root: HTMLElement) =>
  Array.from(root.querySelectorAll('nav[aria-label="Settings"] a')).map((a) => a.textContent?.trim());
const performanceUrl = `/api/v1/projects/${projectId}/performance`;
const sectionsForEveryMember = ['General', 'Submissions', 'Scripts', 'Reference data', 'Sampling frame', 'Members'];

describe('ProjectPage tabs', () => {
  it.each<ProjectRole>(['viewer', 'analyst', 'admin'])('shows the three dashboards and Settings for a %s, nothing else', async (role) => {
    const { root } = await openProjectSection('overview', role);
    expect(tabs(root)).toEqual(['Performance', 'Clean data', 'Indicators', 'Settings']);
  });

  it('links the first tab to the performance dashboard', async () => {
    const { root } = await openProjectSection('overview', 'viewer');
    expect(root.querySelector('nav a')?.getAttribute('href')).toBe(`/projects/${projectId}/performance`);
    expect(root.querySelector('select option')?.getAttribute('value')).toBe(`/projects/${projectId}/performance`);
  });

  it('opens Settings on General', async () => {
    const { root } = await openProjectSection('performance', 'analyst');
    expect(byTestId(root, 'settings-tab')?.getAttribute('href')).toBe(`/projects/${projectId}/overview`);
  });

  it('marks Settings as current, not as the page, while a configuration section is open', async () => {
    const { root } = await openProjectSection('members', 'viewer');
    const settings = byTestId(root, 'settings-tab')!;
    expect(settings.getAttribute('aria-current')).toBe('true');
    expect(settings.className).toContain('border-brand');
    expect(root.querySelectorAll('[aria-current="page"]')).toHaveLength(1);
  });

  it('does not mark Settings as current on a dashboard', async () => {
    const { root } = await openProjectSection('clean-data', 'viewer');
    expect(byTestId(root, 'settings-tab')?.getAttribute('aria-current')).toBeNull();
    expect(root.querySelector('nav a[aria-current="page"]')?.textContent?.trim()).toBe('Clean data');
  });

  it.each<[ProjectRole, string[]]>([
    ['viewer', sectionsForEveryMember],
    ['analyst', sectionsForEveryMember],
    ['admin', [...sectionsForEveryMember, 'Webhook']],
  ])('lists the configuration sections for a %s as pills, Webhook only for admins', async (role, expected) => {
    const { root } = await openProjectSection('members', role);
    expect(settingsPills(root)).toEqual(expected);
    expect(root.querySelector('nav[aria-label="Settings"] a[aria-current="page"]')?.textContent?.trim()).toBe('Members');
  });

  it('shows no settings navigation on a dashboard', async () => {
    const { root } = await openProjectSection('indicators', 'admin');
    expect(root.querySelector('nav[aria-label="Settings"]')).toBeNull();
  });

  it.each<[ProjectRole, string[]]>([
    ['analyst', sectionsForEveryMember],
    ['admin', [...sectionsForEveryMember, 'Webhook']],
  ])('groups the configuration sections under Settings in the select for a %s', async (role, expected) => {
    const { root } = await openProjectSection('scripts', role);
    const select = root.querySelector('select')!;
    const plain = Array.from(select.querySelectorAll(':scope > option')).map((o) => o.textContent?.trim());
    const group = select.querySelector('optgroup')!;
    expect(plain).toEqual(['Performance', 'Clean data', 'Indicators']);
    expect(group.label).toBe('Settings');
    expect(Array.from(group.querySelectorAll('option')).map((o) => o.textContent?.trim())).toEqual(expected);
    expect(select.selectedOptions[0].value).toBe(`/projects/${projectId}/scripts`);
  });

  it('keeps the address of every configuration section', async () => {
    const { root } = await openProjectSection('overview', 'admin');
    const hrefs = Array.from(root.querySelectorAll('nav[aria-label="Settings"] a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(
      ['overview', 'data', 'scripts', 'reference', 'sampling-frame', 'members', 'webhook'].map((s) => `/projects/${projectId}/${s}`),
    );
  });

  it.each<ProjectRole>(['viewer', 'analyst', 'admin'])('lands a %s on the performance dashboard', async (role) => {
    const http = configureApp();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl(`/projects/${projectId}`);
    await settle(harness);
    http.expectOne(`/api/v1/projects/${projectId}`).flush(details(role));
    await navigation;
    await settle(harness);
    http.expectOne((r) => r.url === performanceUrl);
    const root = harness.fixture.nativeElement as HTMLElement;

    expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/performance`);
    const active = root.querySelector('nav a[aria-current="page"]');
    expect(active?.textContent?.trim()).toBe('Performance');
    expect(byTestId(root, 'range-form')).not.toBeNull();
  });
});

describe('ProjectPage unreadable token banner', () => {
  it('warns an admin on any page, with links to the token and the webhook secret', async () => {
    const { root } = await openProjectSection('clean-data', 'admin', { tokenUnreadable: true });
    const banner = byTestId(root, 'token-unreadable-banner')!;
    expect(banner.textContent).toContain('can no longer be read');
    expect(Array.from(banner.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual([`/projects/${projectId}/overview`, `/projects/${projectId}/webhook`]);
  });

  it('steps aside on General, which shows the warning beside the token', async () => {
    const { root } = await openProjectSection('overview', 'admin', { tokenUnreadable: true });
    expect(byTestId(root, 'token-unreadable-banner')).toBeNull();
    expect(byTestId(root, 'token-unreadable')).not.toBeNull();
  });

  it.each<[ProjectRole, boolean]>([
    ['analyst', true],
    ['viewer', true],
    ['admin', false],
  ])('shows no banner to a %s or when the token is readable (%s)', async (role, unreadable) => {
    const { root } = await openProjectSection('clean-data', role, { tokenUnreadable: unreadable });
    expect(byTestId(root, 'token-unreadable-banner')).toBeNull();
  });
});
