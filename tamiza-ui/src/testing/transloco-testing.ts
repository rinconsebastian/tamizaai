import { LOCALE_ID, Provider } from '@angular/core';
import { TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../public/i18n/en.json';
import es from '../../public/i18n/es.json';
import { InterfaceLanguage } from '../app/core/i18n/interface-language';

/** The Transloco module for specs: both real translation files, with `lang` active (English unless a spec asks otherwise). */
export function translocoTesting(lang: InterfaceLanguage = 'en') {
  return TranslocoTestingModule.forRoot({
    langs: { en, es },
    translocoConfig: { availableLangs: ['en', 'es'], defaultLang: lang, fallbackLang: 'en' },
    preloadLangs: true,
  });
}

/** The locale that formats numbers and dates in a spec: `en-US` (jsdom's own) unless a spec asks otherwise. */
export function localeTesting(locale = 'en-US'): Provider {
  return { provide: LOCALE_ID, useValue: locale };
}
