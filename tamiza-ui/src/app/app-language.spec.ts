import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import es from '../../public/i18n/es.json';
import { provideVisibleElements } from '../testing/cdk';
import { createOidcMock } from '../testing/oidc-mock';
import { simulateSecureContext } from '../testing/secure-context';
import { localeTesting, translocoTesting } from '../testing/transloco-testing';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { ToastService } from './shared/ui';

@Component({ template: '' })
class Blank {}

describe('App shell in Spanish', () => {
  it('reads its text from es.json', async () => {
    simulateSecureContext();
    await TestBed.configureTestingModule({
      imports: [App, translocoTesting('es')],
      providers: [
        provideRouter([{ path: '', component: Blank }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OidcSecurityService, useValue: createOidcMock() },
        provideVisibleElements(),
        localeTesting('es-CO'),
      ],
    }).compileComponents();
    const backend = TestBed.inject(HttpTestingController);
    await TestBed.inject(AuthService).initialize();

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    backend.expectOne('/api/v1/me').flush({ id: 'u1', name: 'Ada Lovelace', email: 'ada@example.org', isSuperAdmin: false, canCreateProjects: false });
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="sign-out"]')?.textContent?.trim()).toBe(es.shell.signOut);
    expect(element.querySelector('[data-testid="brand"]')?.textContent).toBe(es.app.name);

    // The MCP entry reads its text from es.json too, once the deployment says the server is on.
    backend.expectOne('/api/v1/system/config').flush({
      oidc: { authority: 'https://kc.example.org/realms/tamiza', clientId: 'tamiza-ui', scope: 'openid' },
      version: '0.1.0',
      assistant: { enabled: false },
      accountEmails: { enabled: false },
      email: { enabled: false },
      mcp: { enabled: true },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="connect-ai-client"]')?.textContent?.trim()).toBe(es.shell.connectAiClient);

    // The toast region has no translations of its own: the shell gives it the label of its dismiss button.
    TestBed.inject(ToastService).info('Hola');
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="toast"] button')?.getAttribute('aria-label')).toBe(es.common.dismiss);
  });
});
