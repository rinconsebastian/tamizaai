import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { createOidcMock, OidcMock } from '../testing/oidc-mock';
import { pressKey, provideVisibleElements, tick } from '../testing/cdk';
import { simulateSecureContext } from '../testing/secure-context';
import { translocoTesting } from '../testing/transloco-testing';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { CurrentUser } from './core/current-user.service';

@Component({ template: '' })
class Blank {}

describe('App shell', () => {
  let oidc: OidcMock;
  let backend: HttpTestingController;

  beforeEach(async () => {
    simulateSecureContext();
    oidc = createOidcMock();
    await TestBed.configureTestingModule({
      imports: [App, translocoTesting()],
      providers: [
        provideRouter([{ path: '', component: Blank }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OidcSecurityService, useValue: oidc },
        provideVisibleElements(),
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
    expect(element.querySelector('[data-testid="user-name"]')?.textContent).toBe('Ada Lovelace');
    expect(element.querySelector('[data-testid="brand"]')?.textContent).toBe('Tamiza');
  });

  it('signs out through the OIDC provider', async () => {
    const fixture = await renderWithUser(ada);
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="sign-out"]');
    expect(button?.textContent?.trim()).toBe('Sign out');

    button!.click();

    expect(oidc.logoff).toHaveBeenCalledTimes(1);
  });

  describe('narrow-screen menu', () => {
    afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

    async function openMenu() {
      const fixture = await renderWithUser(ada);
      const menuButton = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="menu-button"]')!;
      expect(menuButton.getAttribute('aria-label')).toBe('Open menu');
      menuButton.focus();
      // Browsers activate a focused button on Enter or Space by firing click; jsdom does not, so the test clicks.
      menuButton.click();
      fixture.detectChanges();
      await tick();
      return { fixture, menuButton, drawer: document.querySelector('[data-testid="drawer"]') as HTMLElement };
    }

    it('opens a drawer with the navigation, the user name and sign-out', async () => {
      const { menuButton, drawer } = await openMenu();

      expect(menuButton.getAttribute('aria-expanded')).toBe('true');
      expect(drawer.textContent).toContain('Projects');
      expect(drawer.textContent).toContain('Ada Lovelace');
      expect(drawer.querySelector('[data-testid="drawer-sign-out"]')).not.toBeNull();
      expect(drawer.contains(document.activeElement)).toBe(true);
    });

    it('closes on Escape and returns focus to the menu button', async () => {
      const { fixture, menuButton } = await openMenu();

      pressKey(document.querySelector('[role="dialog"]')!, 'Escape');
      fixture.detectChanges();
      await tick();

      expect(document.querySelector('[data-testid="drawer"]')).toBeNull();
      expect(menuButton.getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(menuButton);
    });
  });
});
