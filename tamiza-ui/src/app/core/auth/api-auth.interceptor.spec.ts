import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { apiAuthInterceptor } from './api-auth.interceptor';
import { AuthService, AuthStatus } from './auth.service';

describe('apiAuthInterceptor', () => {
  const status = signal<AuthStatus>('authenticated');
  const login = vi.fn();
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    login.mockReset();
    status.set('authenticated');
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiAuthInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { status, login, accessToken: () => of('token-123') } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('attaches the bearer token to same-origin API calls', () => {
    http.get('/api/v1/me').subscribe();
    expect(backend.expectOne('/api/v1/me').request.headers.get('Authorization')).toBe('Bearer token-123');
  });

  it('does not attach the token to other origins', () => {
    http.get('https://evil.example.org/api/v1/me').subscribe();
    expect(backend.expectOne('https://evil.example.org/api/v1/me').request.headers.has('Authorization')).toBe(false);
  });

  it('does not attach the token to non-API paths', () => {
    http.get('/i18n/en.json').subscribe();
    expect(backend.expectOne('/i18n/en.json').request.headers.has('Authorization')).toBe(false);
  });

  it('signs in again when the API rejects the session', () => {
    http.get('/api/v1/me').subscribe({ error: () => undefined });
    backend.expectOne('/api/v1/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(login).toHaveBeenCalledTimes(1);
  });
});
