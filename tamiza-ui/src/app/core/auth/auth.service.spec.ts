import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of, throwError } from 'rxjs';
import { createOidcMock, OidcMock } from '../../../testing/oidc-mock';
import { simulateSecureContext } from '../../../testing/secure-context';
import { AuthService, isSafeReturnUrl } from './auth.service';

describe('AuthService', () => {
  let oidc: OidcMock;

  function setup(overrides: Parameters<typeof createOidcMock>[0] = {}, document?: Partial<Document>) {
    oidc = createOidcMock(overrides);
    TestBed.configureTestingModule({
      providers: [
        { provide: OidcSecurityService, useValue: oidc },
        ...(document ? [{ provide: DOCUMENT, useValue: document }] : []),
      ],
    });
    return TestBed.inject(AuthService);
  }

  beforeEach(() => {
    simulateSecureContext();
    sessionStorage.clear();
    window.history.replaceState(null, '', '/');
  });

  it('is authenticated after a successful check', async () => {
    const auth = setup();
    await auth.initialize();
    expect(auth.status()).toBe('authenticated');
  });

  it('is anonymous when there is no session', async () => {
    const auth = setup({ checkAuth: () => of({ isAuthenticated: false, userData: null, accessToken: '', idToken: '' }) });
    await auth.initialize();
    expect(auth.status()).toBe('anonymous');
  });

  it('is unavailable when the identity provider cannot be reached', async () => {
    const auth = setup({ preloadAuthWellKnownDocument: () => throwError(() => new Error('network')) });
    await auth.initialize();
    expect(auth.status()).toBe('unavailable');
    expect(auth.unavailableReason()).toBe('unreachable');
  });

  it('is unavailable when the login callback fails', async () => {
    const auth = setup({
      checkAuth: () => of({ isAuthenticated: false, userData: null, accessToken: '', idToken: '', errorMessage: 'bad state' }),
    });
    await auth.initialize();
    expect(auth.unavailableReason()).toBe('unreachable');
  });

  it('is unavailable outside a secure context, without contacting the provider', async () => {
    const auth = setup({}, { defaultView: { isSecureContext: false, crypto: {} } as unknown as Window & typeof globalThis });
    await auth.initialize();
    expect(auth.unavailableReason()).toBe('insecure-context');
    expect(oidc.checkAuth).not.toHaveBeenCalled();
  });

  it('remembers the requested route and returns to it after sign-in', async () => {
    const auth = setup();
    auth.login('/projects/42');
    expect(oidc.authorize).toHaveBeenCalled();

    await auth.initialize();

    expect(window.location.pathname).toBe('/projects/42');
    expect(sessionStorage.length).toBe(0);
  });

  it('ignores return URLs that point to another site', async () => {
    const auth = setup();
    auth.login('//evil.example.org');
    await auth.initialize();
    expect(window.location.pathname).toBe('/');
  });

  it('signs in again when the session ends without a sign-out', async () => {
    const auth = setup();
    await auth.initialize();

    oidc.isAuthenticated$.next({ isAuthenticated: false, allConfigsAuthenticated: [] });

    expect(auth.status()).toBe('anonymous');
    expect(oidc.authorize).toHaveBeenCalledTimes(1);
  });

  it('signs out through the provider without starting a new sign-in', async () => {
    const auth = setup();
    await auth.initialize();

    auth.logout();
    oidc.isAuthenticated$.next({ isAuthenticated: false, allConfigsAuthenticated: [] });

    expect(oidc.logoff).toHaveBeenCalled();
    expect(oidc.authorize).not.toHaveBeenCalled();
  });

  it.each([
    ['/projects', true],
    ['/', true],
    ['//evil.example.org', false],
    ['/\\evil.example.org', false],
    ['https://evil.example.org', false],
  ])('treats %s as a safe return URL: %s', (url, expected) => {
    expect(isSafeReturnUrl(url)).toBe(expected);
  });
});
