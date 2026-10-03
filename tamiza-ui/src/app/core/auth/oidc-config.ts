import { HttpClient } from '@angular/common/http';
import { LogLevel, OpenIdConfiguration, StsConfigHttpLoader, StsConfigLoader } from 'angular-auth-oidc-client';
import { map } from 'rxjs';
import { SYSTEM_CONFIG_URL, SystemConfig } from '../system-config';

/** Builds the OIDC client settings from the deployment's public config. */
export function buildOpenIdConfiguration(config: SystemConfig, origin: string): OpenIdConfiguration {
  return {
    authority: config.oidc.authority,
    clientId: config.oidc.clientId,
    scope: config.oidc.scope,
    redirectUrl: `${origin}/`,
    postLogoutRedirectUri: `${origin}/`,
    responseType: 'code',
    silentRenew: true,
    useRefreshToken: true,
    renewTimeBeforeTokenExpiresInSeconds: 30,
    ignoreNonceAfterRefresh: true,
    startCheckSession: false,
    autoUserInfo: false,
    // Without this the library navigates to its postLoginRoute after the callback, overriding the
    // requested route that AuthService restores.
    triggerAuthorizationResultEvent: true,
    logLevel: LogLevel.Warn,
  };
}

/** Loads the OIDC settings at runtime, so one UI image serves any installation. */
export function createStsConfigLoader(http: HttpClient): StsConfigLoader {
  return new StsConfigHttpLoader(
    http
      .get<SystemConfig>(SYSTEM_CONFIG_URL)
      .pipe(map((config) => buildOpenIdConfiguration(config, window.location.origin))),
  );
}
