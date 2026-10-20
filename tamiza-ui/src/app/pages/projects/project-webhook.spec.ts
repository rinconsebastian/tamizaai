import { vi } from 'vitest';
import { byTestId, openProjectSection, projectId, settle } from '../../../testing/project-harness';
import { tick } from '../../../testing/cdk';

describe('ProjectWebhook', () => {
  const webhookUrl = `/api/v1/projects/${projectId}/webhook`;
  const ingestionUrl = `/api/v1/projects/${projectId}/ingestion`;
  const settings = { url: 'https://tamiza.test/api/v1/webhooks/kobo/p1', username: 'tamiza', secret: 'first-secret', secretUnreadable: false };

  async function open(lastWebhookAt: string | null = null, webhook: object = settings) {
    const opened = await openProjectSection('webhook', 'admin');
    opened.http.expectOne(webhookUrl).flush(webhook);
    opened.http.expectOne(ingestionUrl).flush({ lastWebhookAt });
    await settle(opened.harness);
    return opened;
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('shows the URL and username, masks the secret and copies the URL and the secret', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const { root } = await open();

    expect(byTestId(root, 'webhook-url')?.textContent?.trim()).toBe(settings.url);
    expect(byTestId(root, 'webhook-username')?.textContent?.trim()).toBe('tamiza');
    expect(byTestId(root, 'webhook-secret')?.textContent).not.toContain('first-secret');
    expect(byTestId(root, 'copy-username')).toBeNull();

    byTestId<HTMLButtonElement>(root, 'copy-secret')!.click();
    expect(writeText).toHaveBeenCalledWith('first-secret');
    byTestId<HTMLButtonElement>(root, 'copy-url')!.click();
    expect(writeText).toHaveBeenCalledWith(settings.url);
  });

  it('puts the mask behind a screen-reader text and reveals the secret on request', async () => {
    const { root, harness } = await open();
    const secret = byTestId(root, 'webhook-secret')!;

    expect(secret.querySelector('[aria-hidden="true"]')?.textContent).toContain('•');
    expect(secret.querySelector('.sr-only')?.textContent).toBe('Hidden');
    byTestId<HTMLButtonElement>(root, 'toggle-secret')!.click();
    await settle(harness);
    expect(secret.textContent?.trim()).toBe('first-secret');
    expect(secret.querySelector('.sr-only')).toBeNull();
  });

  it('copies with icon-only buttons that name what they copy', async () => {
    const { root } = await open();
    const button = byTestId(root, 'copy-url')!;

    expect(button.textContent?.trim()).toBe('');
    expect(button.getAttribute('aria-label')).toBe('Copy: Endpoint URL');
  });

  it('puts the delivery status first and keeps the setup steps in the page', async () => {
    const { root } = await open();
    const card = root.querySelector('tmz-card')!;

    expect(card.querySelector('h2')!.nextElementSibling).toBe(byTestId(card as HTMLElement, 'last-delivery'));
    expect(root.textContent).toContain('Keep JSON as the type');
    expect(root.textContent).toContain('Kobo calls it the password');
    expect(root.textContent).not.toContain('later Tamiza release');
  });

  it('opens the setup steps and links to Submissions while no delivery has arrived', async () => {
    const { root } = await open();

    expect(byTestId(root, 'last-delivery')?.textContent).toContain('No delivery received yet');
    expect(byTestId(root, 'deliveries-link')!.getAttribute('href')).toBe(`/projects/${projectId}/data`);
    expect(byTestId(root, 'webhook-steps')!.querySelector('details')!.open).toBe(true);
  });

  it('shows when the last delivery arrived and closes the setup steps', async () => {
    const { root } = await open('2026-10-02T15:30:00Z');

    expect(byTestId(root, 'last-delivery')?.textContent).toContain('Last delivery received: Oct 2, 2026, ');
    expect(byTestId(root, 'deliveries-link')).toBeNull();
    expect(byTestId(root, 'webhook-steps')!.querySelector('details')!.open).toBe(false);
    expect(byTestId(root, 'webhook-steps')!.textContent).toContain('Keep JSON as the type');
  });

  it('shows a one-row skeleton for the status while it loads', async () => {
    const { http, harness, root } = await openProjectSection('webhook', 'admin');
    http.expectOne(webhookUrl).flush(settings);
    await settle(harness);

    expect(byTestId(root, 'delivery-loading')?.getAttribute('aria-busy')).toBe('true');
    expect(byTestId(root, 'webhook-steps')).toBeNull();
    http.expectOne(ingestionUrl).flush({ lastWebhookAt: null });
    await settle(harness);
    expect(byTestId(root, 'delivery-loading')).toBeNull();
  });

  it('regenerates the secret only after confirmation and shows the new one', async () => {
    const { root, http, harness } = await open();

    byTestId<HTMLButtonElement>(root, 'regenerate')!.click();
    await settle(harness);
    http.expectNone(`${webhookUrl}/secret`);
    (document.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    http.expectOne(`${webhookUrl}/secret`).flush({ ...settings, secret: 'second-secret' });
    await settle(harness);

    expect(byTestId(root, 'webhook-secret')?.textContent?.trim()).toBe('second-secret');
  });

  it('offers Regenerate inside the alert, with no mask, Show or Copy, when the secret can no longer be read', async () => {
    const { root, http, harness } = await open(null, { ...settings, secret: null, secretUnreadable: true });
    const alert = byTestId(root, 'secret-unreadable')!;

    expect(alert.textContent).toContain('can no longer be read');
    expect(alert.querySelector('[data-testid="regenerate"]')).not.toBeNull();
    expect(root.querySelectorAll('[data-testid="regenerate"]').length).toBe(1);
    expect(byTestId(root, 'webhook-secret')).toBeNull();
    expect(byTestId(root, 'toggle-secret')).toBeNull();
    expect(byTestId(root, 'copy-secret')).toBeNull();
    expect(byTestId(root, 'webhook-url')).not.toBeNull();

    alert.querySelector<HTMLButtonElement>('[data-testid="regenerate"]')!.click();
    await settle(harness);
    (document.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    http.expectOne(`${webhookUrl}/secret`).flush(settings);
    await settle(harness);
    expect(byTestId(root, 'secret-unreadable')).toBeNull();
    expect(byTestId(root, 'webhook-secret')).not.toBeNull();
  });

  it('shows an alert instead of a skeleton when the settings cannot be loaded', async () => {
    const { http, harness, root } = await openProjectSection('webhook', 'admin');
    http.expectOne(webhookUrl).flush(null, { status: 500, statusText: 'Server Error' });
    http.expectOne(ingestionUrl).flush({ lastWebhookAt: null });
    await settle(harness);

    expect(byTestId(root, 'webhook-load-error')?.textContent).toContain('could not be loaded');
    expect(root.querySelector('[aria-busy="true"]')).toBeNull();
  });

  it('still shows the credentials and opens the steps when the delivery status cannot be read', async () => {
    const { http, harness, root } = await openProjectSection('webhook', 'admin');
    http.expectOne(webhookUrl).flush(settings);
    http.expectOne(ingestionUrl).flush(null, { status: 500, statusText: 'Server Error' });
    await settle(harness);

    expect(byTestId(root, 'webhook-url')).not.toBeNull();
    expect(byTestId(root, 'last-delivery')).toBeNull();
    expect(byTestId(root, 'delivery-loading')).toBeNull();
    expect(byTestId(root, 'webhook-steps')!.querySelector('details')!.open).toBe(true);
  });

  it('tells an analyst that only admins see the webhook settings, without asking the API', async () => {
    const { http, harness, root } = await openProjectSection('webhook', 'analyst');
    await settle(harness);
    expect(byTestId(root, 'webhook-admins-only')?.textContent).toContain('Only project admins');
    http.expectNone(webhookUrl);
    http.expectNone(ingestionUrl);
  });
});
