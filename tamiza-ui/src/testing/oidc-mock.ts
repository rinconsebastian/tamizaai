import { AuthenticatedResult, LoginResponse, OidcSecurityService } from 'angular-auth-oidc-client';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { vi } from 'vitest';

/** A stand-in for the OIDC client, so tests never contact an identity provider. */
export function createOidcMock(overrides: Partial<Record<keyof OidcSecurityService, unknown>> = {}) {
  const isAuthenticated$ = new BehaviorSubject<AuthenticatedResult>({
    isAuthenticated: true,
    allConfigsAuthenticated: [],
  });
  const loginResponse: LoginResponse = {
    isAuthenticated: true,
    userData: null,
    accessToken: 'access-token',
    idToken: 'id-token',
  };
  const mock = {
    isAuthenticated$,
    preloadAuthWellKnownDocument: vi.fn((): Observable<unknown> => of({})),
    checkAuth: vi.fn((): Observable<LoginResponse> => of(loginResponse)),
    authorize: vi.fn(),
    logoff: vi.fn(() => of(null)),
    getAccessToken: vi.fn(() => of('access-token')),
  };
  return Object.assign(mock, overrides) as typeof mock;
}

export type OidcMock = ReturnType<typeof createOidcMock>;
