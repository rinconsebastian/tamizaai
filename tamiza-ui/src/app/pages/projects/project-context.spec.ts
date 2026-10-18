import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DashboardEntry, FormChoice, ProjectDetails } from '../../core/projects/projects.service';
import { details, projectId } from '../../../testing/project-harness';
import { ProjectContext } from './project-context';

describe('ProjectContext', () => {
  const url = `/api/v1/projects/${projectId}/form-choices`;
  const choices: FormChoice[] = [{ listName: 'municipalities', name: '05001', label: 'Medellín' }];

  function setup() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ProjectContext] });
    const context = TestBed.inject(ProjectContext);
    context.update(details('analyst'));
    return { context, http: TestBed.inject(HttpTestingController) };
  }

  it('reads the form choices once per form check', () => {
    const { context, http } = setup();

    context.loadFormChoices();
    context.loadFormChoices();
    http.expectOne(url).flush(choices);
    context.loadFormChoices();

    http.expectNone(url);
    expect(context.formChoices()).toEqual(choices);
  });

  it('reads them again after a new form check, and not after the same one', () => {
    const { context, http } = setup();
    context.loadFormChoices();
    http.expectOne(url).flush(choices);

    context.update(details('analyst'));
    context.loadFormChoices();
    http.expectNone(url);

    context.update(details('analyst', { formCheckedAt: '2026-10-03T08:00:00Z' }));
    expect(context.formChoices()).toBeNull();
    context.loadFormChoices();
    http.expectOne(url).flush([]);
    expect(context.formChoices()).toEqual([]);
  });

  it('drops a read started before a new form check', () => {
    const { context, http } = setup();
    context.loadFormChoices();
    const stale = http.expectOne(url);

    context.update(details('analyst', { formCheckedAt: '2026-10-03T08:00:00Z' }));
    context.loadFormChoices();
    const fresh = http.match(url);
    stale.flush(choices);

    expect(context.formChoices()).toBeNull();
    expect(fresh.length).toBe(1);
    fresh[0].flush([]);
    expect(context.formChoices()).toEqual([]);
  });

  it('can try again after a failed read', () => {
    const { context, http } = setup();
    context.loadFormChoices();
    http.expectOne(url).flush(null, { status: 500, statusText: 'Server Error' });
    expect(context.formChoicesFailed()).toBe(true);
    expect(context.formChoices()).toBeNull();

    context.loadFormChoices();
    expect(context.formChoicesFailed()).toBe(false);
    http.expectOne(url).flush(choices);

    expect(context.formChoices()).toEqual(choices);
  });

  describe('the dashboards', () => {
    const detailsUrl = `/api/v1/projects/${projectId}`;
    const three: DashboardEntry[] = [
      { id: 'd1', name: 'Housing' },
      { id: 'd2', name: 'Health' },
      { id: 'd3', name: 'Field' },
    ];

    it('lists the project\'s dashboards from its details, none before they are read', () => {
      TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ProjectContext] });
      const context = TestBed.inject(ProjectContext);
      expect(context.dashboards()).toEqual([]);
      expect(context.notice()).toBeNull();

      context.update(details('viewer', { dashboards: three }));

      expect(context.dashboards()).toEqual(three);
    });

    it('replaces the list in the details themselves, keeping the rest of them', () => {
      const { context, http } = setup();
      const before = context.project()!;

      context.setDashboards([three[1], three[0]]);

      expect(context.dashboards()).toEqual([three[1], three[0]]);
      expect(context.project()).toEqual({ ...before, dashboards: [three[1], three[0]] });
      http.expectNone(detailsUrl);
    });

    it('reads the details again, applies them, then emits them to the subscriber', () => {
      const { context, http } = setup();
      const fresh = details('analyst', { name: 'Renamed', dashboards: three });
      const seen: { emitted: ProjectDetails; applied: DashboardEntry[] }[] = [];

      context.reload().subscribe((project) => seen.push({ emitted: project, applied: context.dashboards() }));
      const request = http.expectOne(detailsUrl);
      expect(request.request.method).toBe('GET');
      expect(seen).toEqual([]);
      request.flush(fresh);

      expect(seen).toEqual([{ emitted: fresh, applied: three }]);
      expect(context.project()).toEqual(fresh);
      http.verify();
    });

    it('lets the subscriber know a reload failed, and keeps the details shown', () => {
      const { context, http } = setup();
      const before = context.project();
      let failed = false;

      context.reload().subscribe({ error: () => (failed = true) });
      http.expectOne(detailsUrl).flush(null, { status: 500, statusText: 'Server Error' });

      expect(failed).toBe(true);
      expect(context.project()).toBe(before);
    });

    it('still refreshes the details with no one waiting for them', () => {
      const { context, http } = setup();

      context.refresh();
      http.expectOne(detailsUrl).flush(details('analyst', { dashboards: three }));

      expect(context.dashboards()).toEqual(three);
      http.verify();
    });
  });
});
