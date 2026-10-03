import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { createOidcMock, OidcMock } from '../testing/oidc-mock';
import { simulateSecureContext } from '../testing/secure-context';
import { translocoTesting } from '../testing/transloco-testing';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { CurrentUser } from './core/current-user.service';
import { HomePage } from './pages/home/home';

describe('App shell', () => {
  let oidc: OidcMock;
  let backend: HttpTestingController;

  beforeEach(async () => {
    simulateSecureContext();
    oidc = createOidcMock();
    await TestBed.configureTestingModule({
      imports: [App, translocoTesting()],
      providers: [
        provideRouter([{ path: '', component: HomePage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OidcSecurityService, useValue: oidc },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    await TestBed.inject(AuthService).initialize();
  });

  async function renderWithUser(user: CurrentUser) {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    backend.expectOne('/api/v1/me').flush(user);
    await fixture.whenStable();
    return fixture;
  }

  const ada: CurrentUser = { id: '0192f0c4-0000-7000-8000-000000000001', name: 'Ada Lovelace', email: 'ada@example.org', isSuperAdmin: false };

  it('shows the signed-in user name from /api/v1/me', async () => {
    const fixture = await renderWithUser(ada);
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.user-name')?.textContent).toBe('Ada Lovelace');
    expect(element.querySelector('.brand')?.textContent).toBe('Tamiza');
  });

  it('signs out through the OIDC provider', async () => {
    const fixture = await renderWithUser(ada);
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.sign-out');
    expect(button?.textContent).toBe('Sign out');

    button!.click();

    expect(oidc.logoff).toHaveBeenCalledTimes(1);
  });
});
