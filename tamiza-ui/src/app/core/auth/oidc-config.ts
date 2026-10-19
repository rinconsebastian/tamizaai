import { HttpClient } from '@angular/common/http';
import { LogLevel, OpenIdConfiguration, StsConfigHttpLoader, StsConfigLoader } from 'angular-auth-oidc-client';
import { map } from 'rxjs';
import { InterfaceLanguage } from '../i18n/interface-language';
import { SYSTEM_CONFIG_URL, SystemConfig } from '../system-config';

/**
 * Builds the OIDC client settings from the deployment's public config. The sign-in request asks the identity provider for the
 * interface language (`ui_locales`), which it uses when it offers that language.
 */
export function buildOpenIdConfiguration(
  config: SystemConfig,
  origin: string,
  lang: InterfaceLanguage = 'en',
): OpenIdConfiguration {
  return {
    authority: config.oidc.authority,
    clientId: config.oidc.clientId,
    scope: config.oidc.scope,
    redirectUrl: `${origin}/`,
    postLogoutRedirectUri: `${origin}/`,
    responseType: 'code',
    customParamsAuthRequest: { ui_locales: lang },
    silentRenew: true,
    useRefreshToken: true,
    // Keycloak issues refresh tokens for the code flow without the offline_access scope; a provider that needs it gets it through OIDC_SCOPE.
    disableRefreshTokenOfflineAccessScopeWarning: true,
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
export function createStsConfigLoader(http: HttpClient, lang: InterfaceLanguage = 'en'): StsConfigLoader {
  return new StsConfigHttpLoader(
    http
      .get<SystemConfig>(SYSTEM_CONFIG_URL)
      .pipe(map((config) => buildOpenIdConfiguration(config, window.location.origin, lang))),
  );
}
