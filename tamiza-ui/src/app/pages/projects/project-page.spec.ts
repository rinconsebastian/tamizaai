import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ProjectRole } from '../../core/projects/projects.service';
import { RouterTestingHarness } from '@angular/router/testing';
import { byTestId, configureApp, details, openProjectSection, projectId, settle } from '../../../testing/project-harness';

const tabs = (root: HTMLElement) => Array.from(root.querySelectorAll('nav a')).map((a) => a.textContent?.trim());
const performanceUrl = `/api/v1/projects/${projectId}/performance`;

describe('ProjectPage tabs', () => {
  it.each<[ProjectRole, string[]]>([
    ['viewer', ['Performance', 'Clean data', 'Overview', 'Data', 'Scripts', 'Reference data', 'Members', 'Sampling frame']],
    ['analyst', ['Performance', 'Clean data', 'Overview', 'Data', 'Scripts', 'Reference data', 'Members', 'Sampling frame']],
    ['admin', ['Performance', 'Clean data', 'Overview', 'Data', 'Scripts', 'Reference data', 'Members', 'Sampling frame', 'Webhook']],
  ])('puts Performance first for a %s, and the Webhook tab only for admins', async (role, expected) => {
    const { root } = await openProjectSection('overview', role);
    expect(tabs(root)).toEqual(expected);
  });

  it('links the first tab to the performance dashboard', async () => {
    const { root } = await openProjectSection('overview', 'viewer');
    expect(root.querySelector('nav a')?.getAttribute('href')).toBe(`/projects/${projectId}/performance`);
    expect(root.querySelector('select option')?.getAttribute('value')).toBe(`/projects/${projectId}/performance`);
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
