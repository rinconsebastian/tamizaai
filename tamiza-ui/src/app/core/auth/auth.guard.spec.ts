import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { authGuard } from './auth.guard';
import { AuthService, AuthStatus } from './auth.service';

describe('authGuard', () => {
  const status = signal<AuthStatus>('pending');
  const login = vi.fn();

  beforeEach(() => {
    login.mockReset();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { status, login } }],
    });
  });

  function run(url: string) {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
  }

  it('lets a signed-in user through', () => {
    status.set('authenticated');
    expect(run('/projects')).toBe(true);
    expect(login).not.toHaveBeenCalled();
  });

  it('sends a user without a session to sign-in, keeping the requested route', () => {
    status.set('anonymous');
    expect(run('/projects/42?tab=data')).toBe(false);
    expect(login).toHaveBeenCalledWith('/projects/42?tab=data');
  });

  it('routes to the error page when authentication is unavailable', () => {
    status.set('unavailable');
    const result = run('/projects');
    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/auth-unavailable');
    expect(login).not.toHaveBeenCalled();
  });
});
