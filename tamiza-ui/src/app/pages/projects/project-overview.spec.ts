import { MetricAvailability, ProjectRole } from '../../core/projects/projects.service';
import { byTestId, details, openProjectSection, projectId, settle } from '../../../testing/project-harness';

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const later = () => new Promise<void>((resolve) => setTimeout(resolve, 20));

/** The four checks, each overridable by metric. */
function check(overrides: Partial<Record<MetricAvailability['metric'], Partial<MetricAvailability>>>): MetricAvailability[] {
  const base: MetricAvailability[] = [
    { metric: 'dailyEvolution', available: true, missingFields: [], hint: null },
    { metric: 'completionTime', available: true, missingFields: [], hint: null },
    { metric: 'perEnumerator', available: true, missingFields: [], hint: null },
    { metric: 'coverage', available: true, missingFields: [], hint: null },
  ];
  return base.map((metric) => ({ ...metric, ...overrides[metric.metric] }));
}

describe('ProjectOverview', () => {
  const controls = (element: HTMLElement) => ({
    recheck: !!byTestId(element, 'recheck'),
    replaceToken: !!byTestId(element, 'replace-token-toggle'),
    enumeratorChange: !!byTestId(element, 'enumerator-change'),
    settings: !!byTestId(element, 'settings-form'),
  });

  it.each<[ProjectRole, ReturnType<typeof controls>]>([
    ['analyst', { recheck: true, replaceToken: false, enumeratorChange: false, settings: false }],
    ['admin', { recheck: true, replaceToken: true, enumeratorChange: true, settings: true }],
  ])('shows %s the controls for that role', async (role, expected) => {
    const { root } = await openProjectSection('overview', role, { fieldCheck: check({}) });
    expect(controls(root)).toEqual(expected);
  });

  it('shows when the form was last checked, and gives an analyst no token control', async () => {
    const { root } = await openProjectSection('overview', 'analyst');
    expect(text(byTestId(root, 'checked-at'))).toMatch(/^Last checked: Oct 2, 2026/);
    expect(byTestId(root, 'replace-token-toggle')).toBeNull();
  });

  it('puts the checked time to the minute, with the Re-check button beside it', async () => {
    const { root } = await openProjectSection('overview', 'analyst');
    expect(text(byTestId(root, 'checked-at'))).toMatch(/\d{1,2}:\d{2} [AP]M$/);
    expect(byTestId(root, 'checked-at')!.parentElement!.contains(byTestId(root, 'recheck'))).toBe(true);
  });

  it('lists the server and the asset UID, not the form name (the header has it)', async () => {
    const { root } = await openProjectSection('overview', 'analyst');
    const labels = Array.from(root.querySelectorAll('dl dt')).map((dt) => text(dt));
    expect(labels).toEqual(['Server', 'Asset UID', 'Time zone']);
  });

  it('shows an analyst the time zone, read-only', async () => {
    const { root } = await openProjectSection('overview', 'analyst', { timeZone: 'America/Bogota' });
    expect(text(byTestId(root, 'time-zone-row'))).toContain('America/Bogota');
    expect(byTestId(root, 'settings-form')).toBeNull();
  });

  it('renders unavailable metrics with the missing fields', async () => {
    const { root } = await openProjectSection('overview', 'analyst');

    const completion = byTestId(root, 'metric-completionTime')!;
    expect(completion.textContent).toContain('Unavailable');
    expect(completion.textContent).toContain('Missing in the form: start');
    expect(byTestId(root, 'metric-dailyEvolution')!.textContent).toContain('Available');
  });

  describe('form check', () => {
    it('is titled Form check, without an introduction or a link paragraph', async () => {
      const { root } = await openProjectSection('overview', 'analyst');
      const headings = Array.from(root.querySelectorAll('h2')).map((h) => text(h));
      expect(headings).toContain('Form check');
      expect(headings).not.toContain('Performance dashboard');
      expect(byTestId(root, 'dashboard-link')).toBeNull();
      expect(root.textContent).not.toContain('Which dashboard metrics the form');
    });

    it('shows no hint for an available metric', async () => {
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck: check({}) });
      expect(root.querySelectorAll('[data-testid^="hint-"]')).toHaveLength(0);
      expect(byTestId(root, 'metric-coverage')!.textContent).toContain('Available');
    });

    it.each<ProjectRole>(['analyst', 'admin'])('invites an %s to define the sampling frame, with a link', async (role) => {
      const fieldCheck = check({ coverage: { available: false, hint: 'defineSamplingFrame' } });
      const { root } = await openProjectSection('overview', role, { fieldCheck });

      const hint = byTestId(root, 'hint-coverage')!;
      expect(byTestId(root, 'metric-coverage')!.textContent).toContain('Unavailable');
      expect(text(hint)).toContain('Define a sampling frame to see coverage.');
      expect(byTestId(hint, 'hint-link')?.getAttribute('href')).toBe(`/projects/${projectId}/sampling-frame`);
    });

    it('names the missing frame fields and suggests updating the frame', async () => {
      const fieldCheck = check({ coverage: { available: false, missingFields: ['municipality'], hint: 'updateSamplingFrame' } });
      const { root } = await openProjectSection('overview', 'analyst', { fieldCheck });
      expect(text(byTestId(root, 'metric-coverage'))).toContain('Missing in the form: municipality');
      expect(text(byTestId(root, 'hint-coverage'))).toContain('Update the frame');
      expect(byTestId(root, 'hint-link')?.getAttribute('href')).toBe(`/projects/${projectId}/sampling-frame`);
    });

    it('tells an admin to select the enumerator question, and opens the select for them', async () => {
      const fieldCheck = check({ perEnumerator: { available: false, missingFields: ['username'], hint: 'selectEnumeratorField' } });
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck });
      expect(text(byTestId(root, 'hint-perEnumerator'))).toContain('Select the question that identifies the enumerator.');
      expect(byTestId(root, 'enumerator-form')).not.toBeNull(); // the check asks for it, so it is open
      expect(byTestId(root, 'enumerator-change')).toBeNull(); // and there is nothing to reveal
    });

    it('tells an analyst to ask an admin to select the enumerator question', async () => {
      const fieldCheck = check({ perEnumerator: { available: false, missingFields: ['username'], hint: 'selectEnumeratorField' } });
      const { root } = await openProjectSection('overview', 'analyst', { fieldCheck });
      expect(text(byTestId(root, 'hint-perEnumerator'))).toBe('Ask a project admin to select the question that identifies the enumerator.');
      expect(byTestId(root, 'enumerator-form')).toBeNull();
      expect(byTestId(root, 'enumerator-summary')).toBeNull();
    });
  });

  describe('enumerator question', () => {
    it('shows the selected question as a value, and reveals the select on request', async () => {
      const { root, harness } = await openProjectSection('overview', 'admin', { fieldCheck: check({}), enumeratorField: 'enumerator' });
      expect(text(byTestId(root, 'enumerator-value'))).toBe('Enumerator (enumerator)');
      expect(byTestId(root, 'enumerator-form')).toBeNull();

      byTestId<HTMLButtonElement>(root, 'enumerator-change')!.click();
      await settle(harness);
      expect(byTestId(root, 'enumerator-change')!.getAttribute('aria-expanded')).toBe('true');
      expect(root.querySelector<HTMLSelectElement>('#enumeratorSelect')!.value).toBe('enumerator');
      await later();
      expect(document.activeElement).toBe(root.querySelector('#enumeratorSelect'));
    });

    it('says username is used when nothing is selected and the form has it', async () => {
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck: check({}), enumeratorField: null });
      expect(text(byTestId(root, 'enumerator-value'))).toBe('username (form metadata)');
    });

    it('says None when nothing identifies the enumerator', async () => {
      const fieldCheck = check({ perEnumerator: { available: false, missingFields: ['username'], hint: null } });
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck, enumeratorField: null });
      expect(text(byTestId(root, 'enumerator-value'))).toBe('None');
    });

    it('explains a saved question the form no longer has', async () => {
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck: check({}), enumeratorField: 'old_question' });
      expect(text(byTestId(root, 'enumerator-value'))).toBe('old_question — not in the current form; username is used when the form has it');
    });

    it('lets an admin pick the enumerator question from the form fields', async () => {
      const { root, http, harness } = await openProjectSection('overview', 'admin', { fieldCheck: check({}) });
      byTestId<HTMLButtonElement>(root, 'enumerator-change')!.click();
      await settle(harness);
      const select = root.querySelector<HTMLSelectElement>('#enumeratorSelect')!;
      expect(Array.from(select.options).map((o) => o.value)).toEqual(['', 'municipality', 'sex', 'enumerator']);

      select.value = 'enumerator';
      byTestId<HTMLFormElement>(root, 'enumerator-form')!.dispatchEvent(new Event('submit'));
      await settle(harness);
      const request = http.expectOne(`/api/v1/projects/${projectId}`);

      expect(request.request.method).toBe('PATCH');
      expect(request.request.body).toEqual({ enumeratorField: 'enumerator' });
      request.flush(details('admin', { enumeratorField: 'enumerator', fieldCheck: check({}) }));
      await settle(harness);
      expect(byTestId(root, 'enumerator-form')).toBeNull(); // it hides again after a save
      expect(text(byTestId(root, 'enumerator-value'))).toBe('Enumerator (enumerator)');
    });

    it('opens the select and puts the cursor in it when the link names the enumerator question', async () => {
      const fieldCheck = check({});
      const { root, harness } = await openProjectSection('overview#enumeratorField', 'admin', { fieldCheck });
      await settle(harness);
      await later();
      expect(byTestId(root, 'enumerator-form')).not.toBeNull();
      expect(document.activeElement).toBe(root.querySelector('#enumeratorSelect'));
    });

    it('can be dismissed without saving', async () => {
      const { root, harness } = await openProjectSection('overview', 'admin', { fieldCheck: check({}) });
      byTestId<HTMLButtonElement>(root, 'enumerator-change')!.click();
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'enumerator-cancel')!.click();
      await settle(harness);
      expect(byTestId(root, 'enumerator-form')).toBeNull();
    });

    it('is never offered to an analyst', async () => {
      const { root, http } = await openProjectSection('overview', 'analyst', { fieldCheck: check({}) });
      expect(byTestId(root, 'enumerator-form')).toBeNull();
      expect(byTestId(root, 'enumerator-change')).toBeNull();
      http.expectNone(`/api/v1/projects/${projectId}/form-fields`); // the questions are only read for the admin's setting
    });
  });

  describe('name and time zone', () => {
    const select = (root: HTMLElement) => root.querySelector<HTMLSelectElement>('#timeZone')!;
    const submit = (root: HTMLElement) => byTestId<HTMLFormElement>(root, 'settings-form')!.dispatchEvent(new Event('submit'));

    it("offers admins UTC first, then the browser's zones by region, with the project's selected", async () => {
      const { root } = await openProjectSection('overview', 'admin', { timeZone: 'America/Bogota' });
      const options = Array.from(select(root).options).map((o) => o.value);
      expect(options[0]).toBe('UTC');
      expect(options).toContain('America/Bogota');
      expect(options.filter((o) => o === 'UTC')).toHaveLength(1);
      expect(select(root).value).toBe('America/Bogota');
      const regions = Array.from(select(root).querySelectorAll('optgroup')).map((g) => g.label);
      expect(regions).toContain('America');
      expect(regions).toContain('Europe');
      expect(select(root).querySelector('optgroup[label="America"] option[value="America/Bogota"]')).not.toBeNull();
    });

    it('keeps a zone the browser does not list', async () => {
      const { root } = await openProjectSection('overview', 'admin', { timeZone: 'Etc/Unlisted-Zone' });
      expect(select(root).value).toBe('Etc/Unlisted-Zone');
    });

    it('selects the browser time zone from a button', async () => {
      const { root, harness } = await openProjectSection('overview', 'admin');
      const browser = Intl.DateTimeFormat().resolvedOptions().timeZone;
      if (browser === 'UTC') {
        expect(byTestId(root, 'use-browser-zone')).toBeNull(); // already what the project uses
        return;
      }
      byTestId<HTMLButtonElement>(root, 'use-browser-zone')!.click();
      await settle(harness);
      expect(select(root).value).toBe(browser);
    });

    it('saves the name and the zone together, with one Save', async () => {
      const { root, http, harness } = await openProjectSection('overview', 'admin');
      expect(select(root).value).toBe('UTC');
      expect(root.querySelectorAll('[data-testid="settings-form"] button[type="submit"]')).toHaveLength(1);

      select(root).value = 'America/Bogota';
      root.querySelector<HTMLInputElement>('#projectName')!.value = 'Household survey 2027';
      submit(root);
      await settle(harness);
      const request = http.expectOne(`/api/v1/projects/${projectId}`);
      expect(request.request.method).toBe('PATCH');
      expect(request.request.body).toEqual({ name: 'Household survey 2027', timeZone: 'America/Bogota' });
      request.flush(details('admin', { name: 'Household survey 2027', timeZone: 'America/Bogota' }));
      await settle(harness);

      expect(select(root).value).toBe('America/Bogota');
    });

    it('shows the API message when the zone is refused, next to the field', async () => {
      const { root, http, harness } = await openProjectSection('overview', 'admin');
      select(root).value = 'Africa/Abidjan';
      submit(root);
      await settle(harness);
      http.expectOne(`/api/v1/projects/${projectId}`).flush(
        { errors: { timeZone: ['The time zone "Africa/Abidjan" is not known.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle(harness);

      expect(text(root.querySelector('#timeZone-error'))).toBe('The time zone "Africa/Abidjan" is not known.');
    });

    it('shows the API message for the name next to the name', async () => {
      const { root, http, harness } = await openProjectSection('overview', 'admin');
      root.querySelector<HTMLInputElement>('#projectName')!.value = '';
      submit(root);
      await settle(harness);
      http.expectOne(`/api/v1/projects/${projectId}`).flush({ errors: { name: ['Enter a name.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);

      expect(text(root.querySelector('#projectName-error'))).toBe('Enter a name.');
      expect(root.querySelector('#timeZone-error')).toBeNull();
    });
  });

  describe('the Kobo token', () => {
    it('warns admins when the saved token cannot be read, with a link to the webhook settings', async () => {
      const { root } = await openProjectSection('overview', 'admin', { tokenUnreadable: true });
      expect(byTestId(root, 'token-unreadable')).not.toBeNull();
      expect(byTestId(root, 'token-unreadable-webhook')?.getAttribute('href')).toBe(`/projects/${projectId}/webhook`);
    });

    it('does not tell an analyst', async () => {
      const { root } = await openProjectSection('overview', 'analyst', { tokenUnreadable: true });
      expect(byTestId(root, 'token-unreadable')).toBeNull();
    });

    it('reveals the replacement form on a ghost toggle, with focus in the field and a Cancel', async () => {
      const { root, harness } = await openProjectSection('overview', 'admin');
      const toggle = byTestId<HTMLButtonElement>(root, 'replace-token-toggle')!;
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(toggle.getAttribute('aria-controls')).toBe('replaceTokenForm');
      expect(toggle.className).toContain('bg-transparent'); // a ghost button: replacing is rare

      toggle.click();
      await settle(harness);
      await later();
      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(document.activeElement).toBe(root.querySelector('#newToken'));

      byTestId<HTMLButtonElement>(root, 'replace-token-cancel')!.click();
      await settle(harness);
      expect(byTestId(root, 'replace-token-form')).toBeNull();
    });

    it('replaces the token and closes the form', async () => {
      const { root, http, harness } = await openProjectSection('overview', 'admin');
      byTestId<HTMLButtonElement>(root, 'replace-token-toggle')!.click();
      await settle(harness);
      root.querySelector<HTMLInputElement>('#newToken')!.value = 'new-secret';
      byTestId<HTMLFormElement>(root, 'replace-token-form')!.dispatchEvent(new Event('submit'));
      await settle(harness);
      const request = http.expectOne(`/api/v1/projects/${projectId}/kobo-token`);
      expect(request.request.body).toEqual({ apiToken: 'new-secret' });
      request.flush(details('admin'));
      await settle(harness);
      expect(byTestId(root, 'replace-token-form')).toBeNull();
    });
  });

  describe('unavailable form versions', () => {
    it('shows nothing when every version has its labels', async () => {
      const { root } = await openProjectSection('overview', 'analyst', { unavailableFormVersions: 0 });
      expect(byTestId(root, 'unavailable-versions')).toBeNull();
    });

    it.each<[number, string]>([
      [1, '1 form version has no labels yet'],
      [3, '3 form versions have no labels yet'],
    ])('shows %i unavailable, with a link to the form versions', async (count, text) => {
      const { root } = await openProjectSection('overview', 'analyst', { unavailableFormVersions: count });

      const notice = byTestId(root, 'unavailable-versions')!;
      expect(notice.textContent).toContain(text);
      expect(notice.querySelector('a')?.getAttribute('href')).toBe(`/projects/${projectId}/data#form-versions`);
    });

    it('shows the new count right after a re-check', async () => {
      const { root, http, harness } = await openProjectSection('overview', 'analyst', { unavailableFormVersions: 3 });
      expect(byTestId(root, 'unavailable-versions')?.textContent).toContain('3 form versions');

      byTestId<HTMLButtonElement>(root, 'recheck')!.click();
      const request = http.expectOne(`/api/v1/projects/${projectId}/kobo-check`);
      expect(request.request.method).toBe('POST');
      request.flush(details('analyst', { unavailableFormVersions: 1 }));
      await settle(harness);
      expect(byTestId(root, 'unavailable-versions')?.textContent).toContain('1 form version has no labels yet');

      byTestId<HTMLButtonElement>(root, 'recheck')!.click();
      http.expectOne(`/api/v1/projects/${projectId}/kobo-check`).flush(details('analyst', { unavailableFormVersions: 0 }));
      await settle(harness);
      expect(byTestId(root, 'unavailable-versions')).toBeNull();
    });
  });
});
