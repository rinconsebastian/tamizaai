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
import { SYSTEM_CONFIG_URL } from './core/system-config';

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

  /** Answers the deployment's configuration, which the shell reads once, with the MCP server on or off. */
  async function answerConfig(fixture: Awaited<ReturnType<typeof renderWithUser>>, mcp: boolean) {
    backend.expectOne(SYSTEM_CONFIG_URL).flush({
      oidc: { authority: 'https://kc.example.org/realms/tamiza', clientId: 'tamiza-ui', scope: 'openid' },
      version: '0.1.0',
      assistant: { enabled: false },
      accountEmails: { enabled: false },
      email: { enabled: false },
      mcp: { enabled: mcp },
    });
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const ada: CurrentUser = { id: '0192f0c4-0000-7000-8000-000000000001', name: 'Ada Lovelace', email: 'ada@example.org', isSuperAdmin: false, canCreateProjects: false };

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

  it('leaves the top bar and the toasts out of printouts, and gives the content the whole sheet (Navigation left out of printouts)', async () => {
    const fixture = await renderWithUser(ada);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('[data-testid="top-bar"]')?.classList).toContain('print:hidden');
    expect(element.querySelector('tmz-toast-region')?.classList).toContain('print:hidden');
    const main = element.querySelector('[data-testid="main"]')!;
    expect(main.classList).toContain('print:max-w-none');
    expect(main.classList).toContain('print:p-0');
    expect(main.classList).not.toContain('print:hidden');
  });

  describe('Connect an AI client', () => {
    afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

    const connection = {
      url: 'https://tamiza.example.org/mcp',
      clientId: 'tamiza-mcp',
      callbackPort: 33418,
      maxDataAccess: 'aggregates',
      accountUrl: 'https://kc.example.org/realms/tamiza/account/#/applications',
    };
    const dialog = () => document.querySelector<HTMLElement>('[data-testid="connect-dialog"]');

    async function openDrawer(fixture: Awaited<ReturnType<typeof renderWithUser>>) {
      const menuButton = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="menu-button"]')!;
      menuButton.focus();
      menuButton.click();
      fixture.detectChanges();
      await tick();
      return { menuButton, drawer: document.querySelector<HTMLElement>('[data-testid="drawer"]')! };
    }

    it('is offered in the top bar, next to Sign out, when the MCP server is enabled', async () => {
      const fixture = await renderWithUser(ada);
      await answerConfig(fixture, true);

      const element = fixture.nativeElement as HTMLElement;
      const button = element.querySelector<HTMLButtonElement>('[data-testid="connect-ai-client"]');
      expect(button?.textContent?.trim()).toBe('Connect an AI client');
      expect(button?.nextElementSibling).toBe(element.querySelector('[data-testid="sign-out"]'));
    });

    it('is offered nowhere when the MCP server is not enabled (Disabled)', async () => {
      const fixture = await renderWithUser(ada);
      await answerConfig(fixture, false);

      expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="connect-ai-client"]')).toBeNull();
      const { drawer } = await openDrawer(fixture);
      expect(drawer.querySelector('[data-testid="drawer-connect-ai-client"]')).toBeNull();
      expect(drawer.querySelector('[data-testid="drawer-sign-out"]')).not.toBeNull();
    });

    it('is not offered before the configuration arrives, nor when it cannot be read', async () => {
      const fixture = await renderWithUser(ada);
      expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="connect-ai-client"]')).toBeNull();

      backend.expectOne(SYSTEM_CONFIG_URL).flush(null, { status: 503, statusText: 'Service Unavailable' });
      await fixture.whenStable();
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="connect-ai-client"]')).toBeNull();
    });

    it('opens the dialog with the address, the client ID and the command, and returns focus to the button when it closes', async () => {
      const fixture = await renderWithUser(ada);
      await answerConfig(fixture, true);
      const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="connect-ai-client"]')!;
      button.focus();

      button.click();
      fixture.detectChanges();
      await tick();
      backend.expectOne('/api/v1/mcp').flush(connection);
      await fixture.whenStable();
      fixture.detectChanges();

      expect(dialog()).not.toBeNull();
      expect(document.querySelector('[role="dialog"]')?.getAttribute('aria-labelledby')).toBe('connect-ai-client-title');
      expect(dialog()!.textContent).toContain('https://tamiza.example.org/mcp');
      expect(dialog()!.textContent).toContain(
        'claude mcp add --transport http --client-id tamiza-mcp --callback-port 33418 tamiza https://tamiza.example.org/mcp',
      );
      expect(dialog()!.contains(document.activeElement)).toBe(true);
      // The CDK's default would cap the panel at 80% of a phone's width.
      expect(document.querySelector<HTMLElement>('.cdk-overlay-pane')?.style.maxWidth).toBe('100vw');

      pressKey(document.querySelector('[role="dialog"]')!, 'Escape');
      fixture.detectChanges();
      await tick();

      expect(dialog()).toBeNull();
      expect(document.activeElement).toBe(button);
    });

    it('is offered in the menu drawer too, which closes when the dialog opens, and focus returns to the menu button', async () => {
      const fixture = await renderWithUser(ada);
      await answerConfig(fixture, true);
      const { menuButton, drawer } = await openDrawer(fixture);

      const connect = drawer.querySelector<HTMLButtonElement>('[data-testid="drawer-connect-ai-client"]')!;
      expect(connect.textContent?.trim()).toBe('Connect an AI client');
      expect(connect.nextElementSibling).toBe(drawer.querySelector('[data-testid="drawer-sign-out"]'));
      connect.click();
      fixture.detectChanges();
      await tick();
      backend.expectOne('/api/v1/mcp').flush(connection);
      await fixture.whenStable();
      fixture.detectChanges();

      expect(document.querySelector('[data-testid="drawer"]')).toBeNull();
      expect(dialog()).not.toBeNull();
      expect(menuButton.getAttribute('aria-expanded')).toBe('false');

      pressKey(document.querySelector('[role="dialog"]')!, 'Escape');
      fixture.detectChanges();
      await tick();

      expect(dialog()).toBeNull();
      expect(document.activeElement).toBe(menuButton);
    });
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
