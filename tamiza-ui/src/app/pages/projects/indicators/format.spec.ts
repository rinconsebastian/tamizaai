import { IndicatorUnit, isCurrencyCode, plotted, valueFormatter } from './format';

const format = (indicator: IndicatorUnit) => valueFormatter(indicator, 'en-US');

describe('the indicator formatter', () => {
  describe('count', () => {
    it('writes a whole number with thousands separators', () => {
      expect(format({ format: 'count', unit: null })(12450)).toBe('12,450');
      expect(format({ format: 'count', unit: null })(12450.6)).toBe('12,451');
      expect(format({ format: 'count', unit: null })(0)).toBe('0');
    });

    it('puts the unit after the value', () => {
      expect(format({ format: 'count', unit: 'households' })(12450)).toBe('12,450 households');
    });
  });

  describe('decimal', () => {
    it('writes two decimals', () => {
      expect(format({ format: 'decimal', unit: null })(3.5)).toBe('3.50');
      expect(format({ format: 'decimal', unit: null })(1234.567)).toBe('1,234.57');
    });

    it('puts any unit after the value', () => {
      expect(format({ format: 'decimal', unit: 'kg' })(3.5)).toBe('3.50 kg');
    });
  });

  describe('percent', () => {
    it('writes the value times 100 followed by %, so a fraction reads as a percentage', () => {
      const percent = format({ format: 'percent', unit: null });
      expect(percent(0.42)).toBe('42%');
      expect(percent(0.4237)).toBe('42.37%');
      expect(percent(1)).toBe('100%');
      expect(percent(0.07)).toBe('7%'); // not 7.000000000000001%
    });

    it('does not repeat the percent sign when the unit is %', () => {
      expect(format({ format: 'percent', unit: '%' })(0.42)).toBe('42%');
      expect(format({ format: 'percent', unit: 'of households' })(0.42)).toBe('42% of households');
    });
  });

  describe('currency', () => {
    it('writes a currency code as the currency with two decimals', () => {
      expect(format({ format: 'currency', unit: 'USD' })(1234.5)).toBe('$1,234.50');
      expect(format({ format: 'currency', unit: 'COP' })(1200)).toMatch(/^COP\s1,200\.00$/);
    });

    it('puts a unit that is not a currency code before the value', () => {
      expect(format({ format: 'currency', unit: 'pesos' })(1234.5)).toBe('pesos 1,234.50');
      expect(format({ format: 'currency', unit: '$' })(1234.5)).toBe('$1,234.50');
    });

    it('writes only two decimals when there is no unit', () => {
      expect(format({ format: 'currency', unit: null })(1234.5)).toBe('1,234.50');
      expect(format({ format: 'currency', unit: '  ' })(1234.5)).toBe('1,234.50');
    });
  });

  it('knows ISO currency codes', () => {
    expect(['COP', 'USD', 'EUR'].every(isCurrencyCode)).toBe(true);
    expect(['pesos', 'cop', 'XYZ1', '', '%'].some(isCurrencyCode)).toBe(false);
  });

  describe('without a unit, for the ends of a range', () => {
    it('keeps up to two decimals and leaves the unit out', () => {
      expect(format({ format: 'count', unit: 'households' }).plain(12450)).toBe('12,450');
      expect(format({ format: 'count', unit: 'households' }).plain(2.5)).toBe('2.5');
      expect(format({ format: 'decimal', unit: 'kg' }).plain(3.456)).toBe('3.46');
      expect(format({ format: 'percent', unit: 'of households' }).plain(0.42)).toBe('42%');
      expect(format({ format: 'currency', unit: 'USD' }).plain(1200)).toBe('1,200');
    });
  });

  describe('for an axis tick', () => {
    it('is short and leaves the unit out', () => {
      expect(format({ format: 'count', unit: 'households' }).tick(12450)).toBe('12.5K');
      expect(format({ format: 'count', unit: null }).tick(40)).toBe('40');
      expect(format({ format: 'percent', unit: null }).tick(0.4)).toBe('40%');
      expect(format({ format: 'decimal', unit: 'kg' }).tick(2.5)).toBe('2.5');
    });
  });

  it('uses the locale\'s separators', () => {
    expect(valueFormatter({ format: 'count', unit: null }, 'de-DE')(12450)).toBe('12.450');
  });
});

describe('plotted', () => {
  it('is the label alone for the default measure', () => {
    expect(plotted({ measure: null, ratio: null }, 'Coverage')).toBe('Coverage');
  });

  it('names the measure or the two columns of a ratio', () => {
    expect(plotted({ measure: 'count', ratio: null }, 'Coverage')).toBe('Coverage: count');
    expect(plotted({ measure: null, ratio: { numerator: 'numerator', denominator: 'denominator' } }, 'Coverage')).toBe('Coverage: numerator / denominator');
  });
});
