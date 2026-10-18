import { sizeText } from './sample-download-panel';

describe('sizeText', () => {
  it('writes kilobytes as whole numbers below a megabyte', () => {
    expect(sizeText(812 * 1024, 'en-US')).toBe('812 KB');
    expect(sizeText(10, 'es-CO')).toBe('1 KB');
  });

  it('writes megabytes with one decimal in the decimal mark of the locale', () => {
    expect(sizeText(12.34 * 1024 * 1024, 'en-US')).toBe('12.3 MB');
    expect(sizeText(12.34 * 1024 * 1024, 'es-CO')).toBe('12,3 MB');
    expect(sizeText(12.34 * 1024 * 1024, 'es-MX')).toBe('12.3 MB');
  });
});
