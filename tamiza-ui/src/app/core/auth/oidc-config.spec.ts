import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SystemConfig } from '../system-config';
import { buildOpenIdConfiguration, createStsConfigLoader } from './oidc-config';

const systemConfig: SystemConfig = {
  oidc: { authority: 'https://kc.example.org/realms/tamiza', clientId: 'tamiza-ui', scope: 'openid profile email' },
  version: '0.1.0',
};

describe('OIDC configuration', () => {
  it('maps the system config to a code + PKCE client with refresh tokens', () => {
    const config = buildOpenIdConfiguration(systemConfig, 'https://tamiza.example.org');

    expect(config).toMatchObject({
      authority: 'https://kc.example.org/realms/tamiza',
      clientId: 'tamiza-ui',
      scope: 'openid profile email',
      redirectUrl: 'https://tamiza.example.org/',
      postLogoutRedirectUri: 'https://tamiza.example.org/',
      responseType: 'code',
      useRefreshToken: true,
      silentRenew: true,
      triggerAuthorizationResultEvent: true,
    });
  });

  it('loads the settings from the API at runtime', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const loader = createStsConfigLoader(TestBed.inject(HttpClient));
    const http = TestBed.inject(HttpTestingController);

    const configs = firstValueFrom(loader.loadConfigs());
    http.expectOne('/api/v1/system/config').flush(systemConfig);

    const [config] = await configs;
    expect(config.authority).toBe(systemConfig.oidc.authority);
    expect(config.clientId).toBe('tamiza-ui');
    expect(config.redirectUrl).toBe(`${window.location.origin}/`);
    http.verify();
  });
});
