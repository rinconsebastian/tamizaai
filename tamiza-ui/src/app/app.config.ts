import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { StsConfigLoader, provideAuth } from 'angular-auth-oidc-client';
import { routes } from './app.routes';
import { apiAuthInterceptor } from './core/auth/api-auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { createStsConfigLoader } from './core/auth/oidc-config';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([apiAuthInterceptor])),
    provideAuth({
      loader: { provide: StsConfigLoader, useFactory: createStsConfigLoader, deps: [HttpClient] },
    }),
    provideTransloco({
      config: {
        availableLangs: ['en'],
        defaultLang: 'en',
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
    provideAppInitializer(() => inject(AuthService).initialize()),
  ],
};
