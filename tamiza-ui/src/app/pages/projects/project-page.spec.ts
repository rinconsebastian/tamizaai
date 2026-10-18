import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ProjectRole } from '../../core/projects/projects.service';
import { RouterTestingHarness } from '@angular/router/testing';
import { byTestId, configureApp, details, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';

/** The links of the top tab row only; the settings pills sit in a second nav. */
const tabs = (root: HTMLElement) => Array.from(root.querySelector('nav')!.querySelectorAll('a')).map((a) => a.textContent?.trim());
const settingsPills = (root: HTMLElement) =>
  Array.from(root.querySelectorAll('nav[aria-label="Settings"] a')).map((a) => a.textContent?.trim());
const performanceUrl = `/api/v1/projects/${projectId}/performance`;
const sectionsForEveryMember = ['General', 'Submissions', 'Scripts', 'Reference data', 'Sampling frame', 'Members'];

/** The plain options of the sections' select (the page's first), before the Settings group. */
const plainOptions = (root: HTMLElement) =>
  Array.from(root.querySelector('select')!.querySelectorAll(':scope > option')).map((o) => o.textContent?.trim());
const twoDashboards = { dashboards: [{ id: 'd1', name: 'Indicators' }, { id: 'd2', name: 'Field' }] };
const threeDashboards = {
  dashboards: [
    { id: 'd3', name: 'Housing' },
    { id: 'd4', name: 'Health' },
    { id: 'd5', name: 'Field' },
  ],
};

describe('ProjectPage tabs', () => {
  it.each<ProjectRole>(['viewer', 'analyst', 'admin'])(
    'shows Performance, Clean data, the project\'s dashboards and Settings for a %s, nothing else (Viewer at desktop width)',
    async (role) => {
      const { root } = await openProjectSection('overview', role, twoDashboards);
      expect(tabs(root)).toEqual(['Performance', 'Clean data', 'Indicators', 'Field', 'Settings']);
      expect(plainOptions(root)).toEqual(['Performance', 'Clean data', 'Indicators', 'Field']);
      expect(byTestId(root, 'dashboard-tab-d1')?.getAttribute('href')).toBe(`/projects/${projectId}/dashboards/d1`);
      expect(byTestId(root, 'dashboard-tab-d2')?.getAttribute('href')).toBe(`/projects/${projectId}/dashboards/d2`);
    },
  );

  it('offers every dashboard after Clean data, in their order, in the tab row and in the select (Viewer sees every dashboard)', async () => {
    const { root } = await openProjectSection('performance', 'viewer', threeDashboards);
    expect(tabs(root)).toEqual(['Performance', 'Clean data', 'Housing', 'Health', 'Field', 'Settings']);
    expect(plainOptions(root)).toEqual(['Performance', 'Clean data', 'Housing', 'Health', 'Field']);
    expect(Array.from(root.querySelector('select')!.querySelectorAll(':scope > option')).map((o) => o.getAttribute('value'))).toEqual([
      `/projects/${projectId}/performance`,
      `/projects/${projectId}/clean-data`,
      ...['d3', 'd4', 'd5'].map((id) => `/projects/${projectId}/dashboards/${id}`),
    ]);
  });

  it('marks the open dashboard\'s tab, and only it, as the current page (Dashboard marked as current)', async () => {
    const { root } = await openProjectSection('dashboards/d2', 'viewer', twoDashboards);
    const current = root.querySelectorAll('[aria-current="page"]');
    expect(current).toHaveLength(1);
    expect(current[0]).toBe(byTestId(root, 'dashboard-tab-d2'));
    expect(byTestId(root, 'settings-tab')?.getAttribute('aria-current')).toBeNull();
    expect(root.querySelector<HTMLSelectElement>('select')!.selectedOptions[0].value).toBe(`/projects/${projectId}/dashboards/d2`);
  });

  it('opens the first dashboard from the former Indicators address, in place of it (Old bookmark)', async () => {
    const { root } = await openProjectSection('indicators', 'viewer', { dashboards: [{ id: 'd4', name: 'Health' }, { id: 'd5', name: 'Field' }] });
    const router = TestBed.inject(Router);

    expect(router.url).toBe(`/projects/${projectId}/dashboards/d4`);
    expect(router.lastSuccessfulNavigation()?.extras.replaceUrl).toBe(true);
    expect(root.querySelector('nav a[aria-current="page"]')).toBe(byTestId(root, 'dashboard-tab-d4'));
    expect(root.querySelector('app-project-indicators')).not.toBeNull();
  });

  it('shows a dashboard\'s name as typed, never as markup or as a translation (Name shown as typed)', async () => {
    const names = ['<b>Avance</b>', 'project.tabs.settings'];
    const { root } = await openProjectSection('performance', 'viewer', { dashboards: names.map((name, i) => ({ id: `d${i + 1}`, name })) });

    expect(tabs(root)).toEqual(['Performance', 'Clean data', ...names, 'Settings']);
    expect(plainOptions(root)).toEqual(['Performance', 'Clean data', ...names]);
    expect(byTestId(root, 'dashboard-tab-d1')?.getAttribute('title')).toBe('<b>Avance</b>');
    expect(root.querySelector('nav b, select b')).toBeNull();
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

  it('shows the Scripts section opened from its address, with Settings marked as current (Bookmark to a configuration section)', async () => {
    const { element, root } = await openProjectSection('scripts', 'analyst');
    expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/scripts`);
    expect(element.querySelector('app-project-scripts')).not.toBeNull();
    expect(byTestId(root, 'settings-tab')?.getAttribute('aria-current')).toBe('true');
    expect(root.querySelector('nav[aria-label="Settings"] a[aria-current="page"]')?.textContent?.trim()).toBe('Scripts');
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
  ])('lists the configuration sections for a %s as pills, Webhook only for admins (Admin opens a configuration section)', async (role, expected) => {
    const { root } = await openProjectSection('members', role);
    expect(settingsPills(root)).toEqual(expected);
    expect(root.querySelector('nav[aria-label="Settings"] a[aria-current="page"]')?.textContent?.trim()).toBe('Members');
  });

  it('lists the configuration sections without Webhook for an analyst on Scripts (Analyst opens a configuration section)', async () => {
    const { root } = await openProjectSection('scripts', 'analyst');
    expect(settingsPills(root)).toEqual(sectionsForEveryMember);
  });

  it.each(['performance', 'clean-data', 'dashboards/d1', 'dashboards/d2'])('shows no settings navigation on the dashboard %s (Dashboard open)', async (section) => {
    const { root } = await openProjectSection(section, 'admin', twoDashboards);
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

  it('offers every section in one select, with the open one selected (Phone width)', async () => {
    const { root } = await openProjectSection('data', 'analyst');
    const select = root.querySelector('select')!;
    const group = select.querySelector('optgroup')!;

    expect(root.querySelectorAll('select')).toHaveLength(1);
    // Below md the select alone: the tab row and the settings pills show from md up.
    expect(select.classList).toContain('md:hidden');
    const navs = Array.from(root.querySelectorAll('nav'));
    expect(navs).toHaveLength(2);
    expect(navs.every((nav) => nav.classList.contains('hidden'))).toBe(true);
    expect(plainOptions(root)).toEqual(['Performance', 'Clean data', 'Indicators']);
    expect(group.label).toBe('Settings');
    expect(Array.from(group.querySelectorAll('option')).map((o) => o.textContent?.trim())).toEqual(sectionsForEveryMember);
    expect(select.selectedOptions[0].textContent?.trim()).toBe('Submissions');
  });

  it('opens the dashboard chosen in the select (Switching from the select)', async () => {
    const { harness, root } = await openProjectSection('dashboards/d1', 'viewer', twoDashboards);
    typeInto(root.querySelector('select')!, `/projects/${projectId}/dashboards/d2`);
    await settle(harness);

    expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/dashboards/d2`);
    expect(root.querySelector('nav a[aria-current="page"]')).toBe(byTestId(root, 'dashboard-tab-d2'));
    expect(root.querySelector('app-project-indicators')).not.toBeNull();
  });

  it('keeps the address of every configuration section', async () => {
    const { root } = await openProjectSection('overview', 'admin');
    const hrefs = Array.from(root.querySelectorAll('nav[aria-label="Settings"] a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(
      ['overview', 'data', 'scripts', 'reference', 'sampling-frame', 'members', 'webhook'].map((s) => `/projects/${projectId}/${s}`),
    );
  });

  it.each<ProjectRole>(['viewer', 'analyst', 'admin'])('lands a %s on the performance dashboard (Project opens on the performance dashboard)', async (role) => {
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
