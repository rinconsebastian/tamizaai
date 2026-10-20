import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { me } from '../../testing/project-harness';
import { CurrentUserService } from './current-user.service';

describe('CurrentUserService', () => {
  let http: HttpTestingController;
  let service: CurrentUserService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(CurrentUserService);
  });

  afterEach(() => http.verify());

  it('reads the profile once', () => {
    service.load();
    service.load();
    http.expectOne('/api/v1/me').flush(me());
    service.load();

    expect(service.user()?.name).toBe('Ada Lovelace');
  });

  it('reads the profile again on refresh and shows what changed', () => {
    service.load();
    http.expectOne('/api/v1/me').flush(me({ canCreateProjects: true }));
    expect(service.canCreateProjects()).toBe(true);

    service.refresh();
    http.expectOne('/api/v1/me').flush(me({ canCreateProjects: false }));

    expect(service.canCreateProjects()).toBe(false);
  });

  it('shares a request in flight between load, refresh and profile', async () => {
    service.load();
    service.refresh();
    const profile = service.profile();
    http.expectOne('/api/v1/me').flush(me());

    expect((await profile)?.email).toBe('ada@example.org');
  });

  it('answers profile from the loaded user without a request', async () => {
    service.user.set(me());

    expect((await service.profile())?.id).toBe('user-1');
  });

  it('cannot create projects while the profile loads', () => {
    expect(service.canCreateProjects()).toBe(false);

    service.load();
    expect(service.canCreateProjects()).toBe(false);

    http.expectOne('/api/v1/me').flush(me());
    expect(service.canCreateProjects()).toBe(true);
  });

  it('cannot create projects when the API does not report it', () => {
    service.load();
    const { canCreateProjects: _omitted, ...withoutFlag } = me();
    http.expectOne('/api/v1/me').flush(withoutFlag);

    expect(service.user()?.name).toBe('Ada Lovelace');
    expect(service.canCreateProjects()).toBe(false);
  });

  it('cannot create projects after a failed read, and reads again on the next request', async () => {
    service.load();
    http.expectOne('/api/v1/me').flush({}, { status: 500, statusText: 'Server Error' });

    expect(service.user()).toBeNull();
    expect(service.canCreateProjects()).toBe(false);

    const profile = service.profile();
    http.expectOne('/api/v1/me').flush(me());
    expect((await profile)?.canCreateProjects).toBe(true);
    expect(service.canCreateProjects()).toBe(true);
  });

  it('resolves profile with null when the read fails', async () => {
    const profile = service.profile();
    http.expectOne('/api/v1/me').flush({}, { status: 500, statusText: 'Server Error' });

    expect(await profile).toBeNull();
  });

  it('hides the right after a refresh fails until a read succeeds', () => {
    service.load();
    http.expectOne('/api/v1/me').flush(me());

    service.refresh();
    http.expectOne('/api/v1/me').flush({}, { status: 500, statusText: 'Server Error' });
    expect(service.user()?.name).toBe('Ada Lovelace');
    expect(service.canCreateProjects()).toBe(false);

    service.refresh();
    http.expectOne('/api/v1/me').flush(me());
    expect(service.canCreateProjects()).toBe(true);
  });
});
