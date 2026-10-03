import { vi } from 'vitest';
import { byTestId, openProjectSection, projectId, settle } from '../../../testing/project-harness';
import { tick } from '../../../testing/cdk';

describe('ProjectWebhook', () => {
  const settings = { url: 'https://tamiza.test/api/v1/webhooks/kobo/p1', username: 'tamiza', secret: 'first-secret', secretUnreadable: false };

  async function open() {
    const opened = await openProjectSection('webhook', 'admin');
    opened.http.expectOne(`/api/v1/projects/${projectId}/webhook`).flush(settings);
    await settle(opened.harness);
    return opened;
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('shows the URL and username, masks the secret and copies values', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const { root } = await open();

    expect(byTestId(root, 'webhook-url')?.textContent?.trim()).toBe(settings.url);
    expect(byTestId(root, 'webhook-username')?.textContent?.trim()).toBe('tamiza');
    expect(byTestId(root, 'webhook-secret')?.textContent).not.toContain('first-secret');

    byTestId<HTMLButtonElement>(root, 'copy-secret')!.click();
    expect(writeText).toHaveBeenCalledWith('first-secret');
  });

  it('regenerates the secret only after confirmation and shows the new one', async () => {
    const { root, http, harness } = await open();

    byTestId<HTMLButtonElement>(root, 'regenerate')!.click();
    await settle(harness);
    http.expectNone(`/api/v1/projects/${projectId}/webhook/secret`);
    (document.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    http.expectOne(`/api/v1/projects/${projectId}/webhook/secret`).flush({ ...settings, secret: 'second-secret' });
    await settle(harness);

    expect(byTestId(root, 'webhook-secret')?.textContent?.trim()).toBe('second-secret');
  });
});
