import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FormChoice } from '../../core/projects/projects.service';
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
});
