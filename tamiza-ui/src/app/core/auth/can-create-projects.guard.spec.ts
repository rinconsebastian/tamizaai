import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { me } from '../../../testing/project-harness';
import { CurrentUserService } from '../current-user.service';
import { canCreateProjectsGuard } from './can-create-projects.guard';

describe('canCreateProjectsGuard', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  const run = () =>
    TestBed.runInInjectionContext(() => canCreateProjectsGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)) as Promise<boolean | UrlTree>;

  const urlOf = (result: boolean | UrlTree) => TestBed.inject(Router).serializeUrl(result as UrlTree);

  it('waits for the profile and lets a user who may create projects through', async () => {
    const result = run();
    http.expectOne('/api/v1/me').flush(me({ canCreateProjects: true }));

    expect(await result).toBe(true);
  });

  it('sends a user who may not create projects to the list', async () => {
    const result = run();
    http.expectOne('/api/v1/me').flush(me({ canCreateProjects: false }));

    const resolved = await result;
    expect(resolved).toBeInstanceOf(UrlTree);
    expect(urlOf(resolved)).toBe('/projects');
  });

  it('sends the user to the list when the profile cannot be read', async () => {
    const result = run();
    http.expectOne('/api/v1/me').flush({}, { status: 500, statusText: 'Server Error' });

    expect(urlOf(await result)).toBe('/projects');
  });

  it('shares the request of a profile already on its way', async () => {
    TestBed.inject(CurrentUserService).load();
    const result = run();
    http.expectOne('/api/v1/me').flush(me());

    expect(await result).toBe(true);
    http.verify();
  });

  it('uses the profile already loaded', async () => {
    TestBed.inject(CurrentUserService).user.set(me());

    expect(await run()).toBe(true);
    http.verify();
  });
});
