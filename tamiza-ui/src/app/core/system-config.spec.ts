import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SYSTEM_CONFIG_URL, SystemConfig, SystemConfigService } from './system-config';

function config(enabled: boolean, accountEmails = false): SystemConfig {
  return {
    oidc: { authority: 'https://kc.example.org/realms/tamiza', clientId: 'tamiza-ui', scope: 'openid' },
    version: '0.1.0',
    assistant: { enabled },
    accountEmails: { enabled: accountEmails },
  };
}

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const http = TestBed.inject(HttpTestingController);
  const service = TestBed.inject(SystemConfigService);
  return { http, service };
}

describe('SystemConfigService', () => {
  it('reports the assistant as off until the configuration arrives', () => {
    const { http, service } = setup();

    expect(service.assistantEnabled()).toBe(false);
    http.expectOne(SYSTEM_CONFIG_URL);
  });

  it('reports the assistant as enabled when the deployment configured it', () => {
    const { http, service } = setup();

    http.expectOne(SYSTEM_CONFIG_URL).flush(config(true));

    expect(service.assistantEnabled()).toBe(true);
  });

  it('reports the assistant as disabled when the deployment did not configure it', () => {
    const { http, service } = setup();

    http.expectOne(SYSTEM_CONFIG_URL).flush(config(false));

    expect(service.assistantEnabled()).toBe(false);
  });

  it('treats a failed request as disabled', () => {
    const { http, service } = setup();

    http.expectOne(SYSTEM_CONFIG_URL).flush(null, { status: 503, statusText: 'Service Unavailable' });

    expect(service.assistantEnabled()).toBe(false);
  });

  it('treats a configuration without the assistant key (an older API) as disabled', () => {
    const { http, service } = setup();

    http.expectOne(SYSTEM_CONFIG_URL).flush({ oidc: config(true).oidc, version: '0.0.9' });

    expect(service.assistantEnabled()).toBe(false);
  });

  it('reports account emails as off until the configuration arrives, and when it cannot be read', () => {
    const { http, service } = setup();
    expect(service.accountEmailsEnabled()).toBe(false);

    http.expectOne(SYSTEM_CONFIG_URL).flush(null, { status: 503, statusText: 'Service Unavailable' });

    expect(service.accountEmailsEnabled()).toBe(false);
  });

  it('reports account emails as enabled only when the deployment configured them', () => {
    const on = setup();
    on.http.expectOne(SYSTEM_CONFIG_URL).flush(config(false, true));
    expect(on.service.accountEmailsEnabled()).toBe(true);
    expect(on.service.assistantEnabled()).toBe(false);
  });

  it('reports account emails as disabled when the deployment did not configure them', () => {
    const { http, service } = setup();

    http.expectOne(SYSTEM_CONFIG_URL).flush(config(true, false));

    expect(service.accountEmailsEnabled()).toBe(false);
    expect(service.assistantEnabled()).toBe(true);
  });

  it('treats a configuration without the accountEmails key (an older API) as disabled', () => {
    const { http, service } = setup();

    http.expectOne(SYSTEM_CONFIG_URL).flush({ oidc: config(true).oidc, version: '0.0.9', assistant: { enabled: true } });

    expect(service.accountEmailsEnabled()).toBe(false);
  });

  it('reads the configuration once, however often the service is used', () => {
    const { http, service } = setup();
    http.expectOne(SYSTEM_CONFIG_URL).flush(config(true));

    expect(TestBed.inject(SystemConfigService)).toBe(service);
    service.assistantEnabled();
    http.expectNone(SYSTEM_CONFIG_URL);
  });
});
