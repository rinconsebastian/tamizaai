import { countKey, isSingular, pluralKey } from './plural';

describe('isSingular', () => {
  it.each(['en', 'es', 'es-CO', 'en-GB'])('takes the singular for exactly one in %s', (lang) => {
    expect(isSingular(1, lang)).toBe(true);
    expect(isSingular(0, lang)).toBe(false);
    expect(isSingular(2, lang)).toBe(false);
    expect(isSingular(1.5, lang)).toBe(false);
    expect(isSingular(1000, lang)).toBe(false);
  });

  it('takes the plural for a million in Spanish, whose category is many', () => {
    expect(isSingular(1_000_000, 'es')).toBe(false);
    expect(isSingular(1_000_000, 'en')).toBe(false);
  });
});

describe('pluralKey', () => {
  it('appends One for the singular and Many for the plural', () => {
    expect(pluralKey('frame.dimensions', 1, 'en')).toBe('frame.dimensionsOne');
    expect(pluralKey('frame.dimensions', 0, 'es')).toBe('frame.dimensionsMany');
    expect(pluralKey('frame.dimensions', 7, 'es')).toBe('frame.dimensionsMany');
  });
});

describe('countKey', () => {
  it('keeps the key for the plural and adds One for the singular', () => {
    expect(countKey('cleanData.total', 1, 'en')).toBe('cleanData.totalOne');
    expect(countKey('cleanData.total', 1, 'es-CO')).toBe('cleanData.totalOne');
    expect(countKey('cleanData.total', 0, 'es')).toBe('cleanData.total');
    expect(countKey('cleanData.total', 1200, 'en')).toBe('cleanData.total');
  });
});
