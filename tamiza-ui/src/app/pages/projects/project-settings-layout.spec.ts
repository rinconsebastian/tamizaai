import { projectId, openProjectSection, settle } from '../../../testing/project-harness';

const pill = (root: HTMLElement, label: string) =>
  Array.from(root.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Settings"] a')).find((a) => a.textContent?.trim() === label)!;
const later = () => new Promise<void>((resolve) => setTimeout(resolve, 20));

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
});
