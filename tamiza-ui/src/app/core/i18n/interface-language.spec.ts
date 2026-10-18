import { browserLanguages, resolveInterfaceLanguage } from './interface-language';

describe('resolveInterfaceLanguage', () => {
  it('uses Spanish for a Spanish browser', () => {
    expect(resolveInterfaceLanguage(['es-CO', 'es'])).toEqual({ lang: 'es', locale: 'es-CO' });
  });

  it('skips unsupported languages before the first supported one', () => {
    expect(resolveInterfaceLanguage(['fr-FR', 'es-ES', 'en'])).toEqual({ lang: 'es', locale: 'es-ES' });
  });

  it('lets English win when it comes before Spanish', () => {
    expect(resolveInterfaceLanguage(['en-US', 'es-CO'])).toEqual({ lang: 'en', locale: 'en-US' });
  });

  it('falls back to English with American formats when no entry is supported', () => {
    expect(resolveInterfaceLanguage(['de-DE', 'pt-BR'])).toEqual({ lang: 'en', locale: 'en-US' });
    expect(resolveInterfaceLanguage([])).toEqual({ lang: 'en', locale: 'en-US' });
  });

  it('keeps a bare language and the region of a British English entry', () => {
    expect(resolveInterfaceLanguage(['es'])).toEqual({ lang: 'es', locale: 'es' });
    expect(resolveInterfaceLanguage(['en-GB'])).toEqual({ lang: 'en', locale: 'en-GB' });
  });

  it('canonicalizes the case of a tag', () => {
    expect(resolveInterfaceLanguage(['es-co'])).toEqual({ lang: 'es', locale: 'es-CO' });
  });

  it('keeps Latin-American region codes', () => {
    expect(resolveInterfaceLanguage(['es-419'])).toEqual({ lang: 'es', locale: 'es-419' });
  });

  it('skips an entry that is not a language tag', () => {
    expect(resolveInterfaceLanguage(['not a tag', '', 'es-MX'])).toEqual({ lang: 'es', locale: 'es-MX' });
  });

  it('does not take a language that merely starts with the letters of a supported one', () => {
    expect(resolveInterfaceLanguage(['est', 'ess-ES'])).toEqual({ lang: 'en', locale: 'en-US' });
  });
});

describe('browserLanguages', () => {
  it('lists the preferred languages in order', () => {
    expect(browserLanguages({ language: 'fr', languages: ['fr', 'es-CO'] })).toEqual(['fr', 'es-CO']);
  });

  it('falls back to the one language when the browser lists none', () => {
    expect(browserLanguages({ language: 'es-CO', languages: [] })).toEqual(['es-CO']);
    expect(browserLanguages({ language: 'es-CO', languages: undefined as unknown as readonly string[] })).toEqual([
      'es-CO',
    ]);
  });

  it('is empty when the browser says nothing', () => {
    expect(browserLanguages({ language: '', languages: [] })).toEqual([]);
  });
});
