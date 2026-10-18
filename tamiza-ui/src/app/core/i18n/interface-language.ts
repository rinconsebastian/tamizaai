/** The languages the UI is translated into. Adding one means a `public/i18n/<lang>.json` file and an entry here. */
export const interfaceLanguages = ['en', 'es'] as const;
export type InterfaceLanguage = (typeof interfaceLanguages)[number];

export interface InterfaceLocale {
  /** The translation file to load. */
  lang: InterfaceLanguage;
  /** The browser entry that chose it (`es-CO`), which decides numbers, dates and times. */
  locale: string;
}

/** What the UI uses when no entry of the browser's list is a supported language. */
const fallback: InterfaceLocale = { lang: 'en', locale: 'en-US' };

/**
 * Picks the UI language from the browser's preferred languages, in order: the first entry whose language is supported, of any
 * region, decides, and its full tag becomes the locale for formats. No entry matching means English with American formats.
 * Entries that are not valid language tags are skipped.
 */
export function resolveInterfaceLanguage(languages: readonly string[]): InterfaceLocale {
  for (const entry of languages) {
    try {
      const [locale] = Intl.getCanonicalLocales(entry);
      const language = new Intl.Locale(locale).language;
      const lang = interfaceLanguages.find((supported) => supported === language);
      if (lang) {
        return { lang, locale };
      }
    } catch {
      // Not a language tag: look at the next entry.
    }
  }

  return fallback;
}

/** The browser's preferred languages in order, or its one language when it does not list them. */
export function browserLanguages(browser: Pick<Navigator, 'language' | 'languages'>): readonly string[] {
  return browser.languages?.length ? browser.languages : browser.language ? [browser.language] : [];
}
