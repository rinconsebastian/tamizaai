import { TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../public/i18n/en.json';

export function translocoTesting() {
  return TranslocoTestingModule.forRoot({
    langs: { en },
    translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
    preloadLangs: true,
  });
}
