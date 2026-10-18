const rules = new Map<string, Intl.PluralRules>();

/**
 * Whether a message about `count` things takes its singular form in `lang`: the plural category `one`, which is exactly 1 in English
 * and Spanish. Zero, fractions and a million (Spanish `many`) take the plural form.
 */
export function isSingular(count: number, lang: string): boolean {
  let pluralRules = rules.get(lang);
  if (!pluralRules) {
    pluralRules = new Intl.PluralRules(lang);
    rules.set(lang, pluralRules);
  }
  return pluralRules.select(count) === 'one';
}

/** The key of the singular or the plural form of a message: `pluralKey('frame.dimensions', 3, 'es')` is `frame.dimensionsMany`. */
export function pluralKey(base: string, count: number, lang: string): string {
  return `${base}${isSingular(count, lang) ? 'One' : 'Many'}`;
}

/**
 * The key of a message about a count: the key itself for the plural, `<key>One` for the singular. It is the convention of every
 * count message that has a singular (`cleanData.total` and `cleanData.totalOne`).
 */
export function countKey(key: string, count: number, lang: string): string {
  return isSingular(count, lang) ? `${key}One` : key;
}
