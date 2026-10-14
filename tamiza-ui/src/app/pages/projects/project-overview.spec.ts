import { MetricAvailability, ProjectRole } from '../../core/projects/projects.service';
import { byTestId, details, openProjectSection, projectId, settle } from '../../../testing/project-harness';

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

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
    enumerator: !!byTestId(element, 'enumerator-form'),
    rename: !!byTestId(element, 'rename-form'),
    timeZone: !!byTestId(element, 'time-zone-form'),
  });

  it.each<[ProjectRole, ReturnType<typeof controls>]>([
    ['viewer', { recheck: false, replaceToken: false, enumerator: false, rename: false, timeZone: false }],
    ['analyst', { recheck: true, replaceToken: false, enumerator: false, rename: false, timeZone: false }],
    ['admin', { recheck: true, replaceToken: true, enumerator: true, rename: true, timeZone: true }],
  ])('shows %s the controls for that role', async (role, expected) => {
    const { root } = await openProjectSection('overview', role);
    expect(controls(root)).toEqual(expected);
  });

  it('renders unavailable metrics with the missing fields', async () => {
    const { root } = await openProjectSection('overview', 'viewer');

    const completion = byTestId(root, 'metric-completionTime')!;
    expect(completion.textContent).toContain('Unavailable');
    expect(completion.textContent).toContain('Missing in the form: start');
    expect(byTestId(root, 'metric-dailyEvolution')!.textContent).toContain('Available');
  });

  it('lets an admin pick the enumerator question from the form fields', async () => {
    const { root, http, harness } = await openProjectSection('overview', 'admin');
    const select = root.querySelector<HTMLSelectElement>('#enumeratorField')!;
    expect(Array.from(select.options).map((o) => o.value)).toEqual(['', 'municipality', 'sex', 'enumerator']);

    select.value = 'enumerator';
    byTestId<HTMLFormElement>(root, 'enumerator-form')!.dispatchEvent(new Event('submit'));
    await settle(harness);
    const request = http.expectOne(`/api/v1/projects/${projectId}`);

    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ enumeratorField: 'enumerator' });
    request.flush(details('admin', { enumeratorField: 'enumerator' }));
  });

  describe('form check', () => {
    it('is titled Form check and links to the performance dashboard', async () => {
      const { root } = await openProjectSection('overview', 'viewer');
      const headings = Array.from(root.querySelectorAll('h2')).map((h) => text(h));
      expect(headings).toContain('Form check');
      expect(headings).not.toContain('Performance dashboard');
      expect(byTestId(root, 'dashboard-link')?.getAttribute('href')).toBe(`/projects/${projectId}/performance`);
    });

    it('shows no hint for an available metric', async () => {
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck: check({}) });
      expect(root.querySelectorAll('[data-testid^="hint-"]')).toHaveLength(0);
      expect(byTestId(root, 'metric-coverage')!.textContent).toContain('Available');
    });

    describe.each<[ProjectRole, boolean]>([
      ['analyst', true],
      ['admin', true],
      ['viewer', false],
    ])('without a sampling frame, for a %s', (role, canEdit) => {
      it(canEdit ? 'invites them to define it, with a link' : 'tells them whom to ask, without a link', async () => {
        const fieldCheck = check({ coverage: { available: false, hint: 'defineSamplingFrame' } });
        const { root } = await openProjectSection('overview', role, { fieldCheck });

        const hint = byTestId(root, 'hint-coverage')!;
        expect(byTestId(root, 'metric-coverage')!.textContent).toContain('Unavailable');
        if (canEdit) {
          expect(text(hint)).toContain('Define a sampling frame to see coverage.');
          expect(byTestId(hint, 'hint-link')?.getAttribute('href')).toBe(`/projects/${projectId}/sampling-frame`);
        } else {
          expect(text(hint)).toBe('Ask an analyst or admin to define the sampling frame.');
          expect(byTestId(root, 'hint-link')).toBeNull();
        }
      });
    });

    it('names the missing frame fields and suggests updating the frame', async () => {
      const fieldCheck = check({ coverage: { available: false, missingFields: ['municipality'], hint: 'updateSamplingFrame' } });
      const { root } = await openProjectSection('overview', 'analyst', { fieldCheck });
      expect(text(byTestId(root, 'metric-coverage'))).toContain('Missing in the form: municipality');
      expect(text(byTestId(root, 'hint-coverage'))).toContain('Update the frame');
      expect(byTestId(root, 'hint-link')?.getAttribute('href')).toBe(`/projects/${projectId}/sampling-frame`);
    });

    it('tells a viewer to ask when the frame needs updating', async () => {
      const fieldCheck = check({ coverage: { available: false, missingFields: ['municipality'], hint: 'updateSamplingFrame' } });
      const { root } = await openProjectSection('overview', 'viewer', { fieldCheck });
      expect(text(byTestId(root, 'hint-coverage'))).toContain('Ask an analyst or admin to update it.');
      expect(byTestId(root, 'hint-link')).toBeNull();
    });

    it('tells an admin to select the enumerator question below', async () => {
      const fieldCheck = check({ perEnumerator: { available: false, missingFields: ['username'], hint: 'selectEnumeratorField' } });
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck });
      expect(text(byTestId(root, 'hint-perEnumerator'))).toContain('Select the question that identifies the enumerator below.');
      expect(byTestId(root, 'enumerator-form')).not.toBeNull();
    });

    it.each<ProjectRole>(['analyst', 'viewer'])('tells a %s to ask an admin to select the enumerator question', async (role) => {
      const fieldCheck = check({ perEnumerator: { available: false, missingFields: ['username'], hint: 'selectEnumeratorField' } });
      const { root } = await openProjectSection('overview', role, { fieldCheck });
      expect(text(byTestId(root, 'hint-perEnumerator'))).toBe('Ask a project admin to select the question that identifies the enumerator.');
      expect(byTestId(root, 'enumerator-form')).toBeNull();
    });
  });

  describe('enumerator question', () => {
    it('is offered to admins even when the metric is available and nothing is selected', async () => {
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck: check({}), enumeratorField: null });
      expect(byTestId(root, 'metric-perEnumerator')!.textContent).toContain('Available');
      expect(byTestId(root, 'enumerator-form')).not.toBeNull();
      expect(root.querySelector<HTMLSelectElement>('#enumeratorField')!.value).toBe('');
    });

    it('shows the selected question', async () => {
      const { root } = await openProjectSection('overview', 'admin', { fieldCheck: check({}), enumeratorField: 'enumerator' });
      expect(root.querySelector<HTMLSelectElement>('#enumeratorField')!.value).toBe('enumerator');
    });

    it.each<ProjectRole>(['analyst', 'viewer'])('is never offered to a %s', async (role) => {
      const { root } = await openProjectSection('overview', role, { fieldCheck: check({}) });
      expect(byTestId(root, 'enumerator-form')).toBeNull();
    });
  });

  describe('time zone', () => {
    const select = (root: HTMLElement) => root.querySelector<HTMLSelectElement>('#timeZone')!;

    it("offers admins UTC and the browser's zones, with the project's selected", async () => {
      const { root } = await openProjectSection('overview', 'admin', { timeZone: 'America/Bogota' });
      const options = Array.from(select(root).options).map((o) => o.value);
      expect(options[0]).toBe('UTC');
      expect(options).toContain('America/Bogota');
      expect(options.filter((o) => o === 'UTC')).toHaveLength(1);
      expect(select(root).value).toBe('America/Bogota');
    });

    it('keeps a zone the browser does not list', async () => {
      const { root } = await openProjectSection('overview', 'admin', { timeZone: 'Etc/Unlisted-Zone' });
      expect(select(root).value).toBe('Etc/Unlisted-Zone');
    });

    it('saves the zone', async () => {
      const { root, http, harness } = await openProjectSection('overview', 'admin');
      expect(select(root).value).toBe('UTC');

      select(root).value = 'America/Bogota';
      byTestId<HTMLFormElement>(root, 'time-zone-form')!.dispatchEvent(new Event('submit'));
      await settle(harness);
      const request = http.expectOne(`/api/v1/projects/${projectId}`);
      expect(request.request.method).toBe('PATCH');
      expect(request.request.body).toEqual({ timeZone: 'America/Bogota' });
      request.flush(details('admin', { timeZone: 'America/Bogota' }));
      await settle(harness);

      expect(select(root).value).toBe('America/Bogota');
    });

    it('shows the API message when the zone is refused', async () => {
      const { root, http, harness } = await openProjectSection('overview', 'admin');
      select(root).value = 'Africa/Abidjan';
      byTestId<HTMLFormElement>(root, 'time-zone-form')!.dispatchEvent(new Event('submit'));
      await settle(harness);
      http.expectOne(`/api/v1/projects/${projectId}`).flush(
        { errors: { timeZone: ['The time zone "Africa/Abidjan" is not known.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle(harness);

      expect(text(root.querySelector('#timeZone-error'))).toBe('The time zone "Africa/Abidjan" is not known.');
    });
  });

  it('warns admins when the saved token cannot be read', async () => {
    const { root } = await openProjectSection('overview', 'admin', { tokenUnreadable: true });
    expect(byTestId(root, 'token-unreadable')).not.toBeNull();
  });

  describe('unavailable form versions', () => {
    it('shows nothing when every version has its labels', async () => {
      const { root } = await openProjectSection('overview', 'viewer', { unavailableFormVersions: 0 });
      expect(byTestId(root, 'unavailable-versions')).toBeNull();
    });

    it.each<[number, string]>([
      [1, '1 form version has no labels yet'],
      [3, '3 form versions have no labels yet'],
    ])('shows %i unavailable to every role, with a link to the Data section', async (count, text) => {
      const { root } = await openProjectSection('overview', 'viewer', { unavailableFormVersions: count });

      const notice = byTestId(root, 'unavailable-versions')!;
      expect(notice.textContent).toContain(text);
      expect(notice.querySelector('a')?.getAttribute('href')).toBe(`/projects/${projectId}/data`);
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
