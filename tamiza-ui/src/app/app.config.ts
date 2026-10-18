import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  LOCALE_ID,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { StsConfigLoader, provideAuth } from 'angular-auth-oidc-client';
import { routes } from './app.routes';
import { apiAuthInterceptor } from './core/auth/api-auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { createStsConfigLoader } from './core/auth/oidc-config';
import { browserLanguages, interfaceLanguages, resolveInterfaceLanguage } from './core/i18n/interface-language';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';

// The language is chosen once, before the first render, from the browser's preferred languages (no switcher, nothing stored).
const { lang, locale } = resolveInterfaceLanguage(browserLanguages(navigator));
document.documentElement.lang = lang;

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Fragment links (#runs, #form-versions) scroll to their target.
    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ anchorScrolling: 'enabled' })),
    provideHttpClient(withInterceptors([apiAuthInterceptor])),
    provideAuth({
      loader: { provide: StsConfigLoader, useFactory: (http: HttpClient) => createStsConfigLoader(http, lang), deps: [HttpClient] },
    }),
    provideTransloco({
      config: {
        availableLangs: [...interfaceLanguages],
        defaultLang: lang,
        // Only used when the chosen language's file fails to load; completeness is checked by translations.spec.ts.
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
    { provide: LOCALE_ID, useValue: locale },
    provideAppInitializer(() => inject(AuthService).initialize()),
  ],
};
