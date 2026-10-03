import { ProjectRole } from '../../core/projects/projects.service';
import { byTestId, details, openProjectSection, projectId, settle } from '../../../testing/project-harness';

describe('ProjectOverview', () => {
  const controls = (element: HTMLElement) => ({
    recheck: !!byTestId(element, 'recheck'),
    replaceToken: !!byTestId(element, 'replace-token-toggle'),
    enumerator: !!byTestId(element, 'enumerator-form'),
    rename: !!byTestId(element, 'rename-form'),
  });

  it.each<[ProjectRole, ReturnType<typeof controls>]>([
    ['viewer', { recheck: false, replaceToken: false, enumerator: false, rename: false }],
    ['analyst', { recheck: true, replaceToken: false, enumerator: false, rename: false }],
    ['admin', { recheck: true, replaceToken: true, enumerator: true, rename: true }],
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

  it('warns admins when the saved token cannot be read', async () => {
    const { root } = await openProjectSection('overview', 'admin', { tokenUnreadable: true });
    expect(byTestId(root, 'token-unreadable')).not.toBeNull();
  });
});
