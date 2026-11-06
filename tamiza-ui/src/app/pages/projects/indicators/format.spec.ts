import { WidgetConfig, WidgetDisplay } from '../../../core/projects/indicators.service';
import { ColumnMetadataMap } from '../../../core/projects/pipeline.service';
import { IndicatorUnit, captionShown, columnLabels, figureFormatter, isCurrencyCode, plotted, rawNames, textsCrowd, valueFormatter, withWidgetUnit } from './format';
import columnCases from './testing/column-labels.json';

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

describe('the caption of a widget (add-widget-context, design D3)', () => {
  const ratio = { numerator: 'numerator', denominator: 'denominator' };

  it('is the own text instead of the label and the names of the columns, trimmed', () => {
    const names = columnLabels({ columns: { numerator: { label: 'Hogares que asistieron' } } }, null);

    expect(plotted({ measure: null, ratio, display: { caption: { text: '  % de quienes asistieron ' } } }, 'Teatro hoy', names)).toBe('% de quienes asistieron');
    expect(plotted({ measure: 'value', ratio: null, display: { caption: { text: 'Total propio' } } }, 'Teatro hoy')).toBe('Total propio');
    expect(plotted({ measure: null, ratio: null, display: { caption: { text: 'Total propio' } } }, '')).toBe('Total propio');
  });

  it('is what it was while the caption has no text of its own: unset, hidden or blank', () => {
    for (const display of [undefined, null, {}, { caption: {} }, { caption: { hidden: true as const } }, { caption: { text: '   ' } }, { unit: 'hogares', subtitle: 'Pregunta' }]) {
      expect(plotted({ measure: null, ratio, display }, 'Teatro hoy')).toBe('Teatro hoy: numerator / denominator');
    }
  });
});

describe('captionShown', () => {
  it('is false only for a caption set to hidden', () => {
    expect(captionShown({ display: { caption: { hidden: true } } })).toBe(false);
    for (const display of [undefined, null, {}, { caption: {} }, { caption: { text: 'Mi línea' } }, { subtitle: 'Pregunta' }, { caption: { hidden: undefined } }]) {
      expect(captionShown({ display })).toBe(true);
    }
  });
});

describe('withWidgetUnit (add-widget-context, design D4)', () => {
  const households: IndicatorUnit = { format: 'percent', unit: 'de los hogares' };

  it('is the indicator itself while the widget has no unit of its own', () => {
    for (const display of [undefined, null, {}, { unit: undefined }, { unit: '   ' }, { color: 2 }]) {
      expect(withWidgetUnit(households, { display })).toBe(households);
    }
    expect(withWidgetUnit(households, null)).toBe(households);
    expect(withWidgetUnit(households, undefined)).toBe(households);
    expect(format(withWidgetUnit(households, { display: null }))(0.1121)).toBe('11.21% de los hogares');
  });

  it('replaces only the unit, trimmed, and keeps the rest of the indicator', () => {
    const indicator = { format: 'count' as const, unit: 'households', label: 'Coverage' };

    expect(withWidgetUnit(indicator, { display: { unit: ' de quienes asistieron ' } })).toEqual({ format: 'count', unit: 'de quienes asistieron', label: 'Coverage' });
    expect(withWidgetUnit({ format: 'decimal', unit: null }, { display: { unit: 'pesos' } })).toEqual({ format: 'decimal', unit: 'pesos' });
  });

  it('gives the formatters the unit of the widget (Base of a subgroup)', () => {
    expect(format(withWidgetUnit(households, { display: { unit: 'de quienes asistieron' } }))(0.1121)).toBe('11.21% de quienes asistieron');
    expect(format(withWidgetUnit({ format: 'count', unit: 'households' }, { display: { unit: 'households with children' } }))(12450)).toBe('12,450 households with children');
  });

  it('does not repeat a unit of % after the number of a percent indicator', () => {
    expect(format(withWidgetUnit(households, { display: { unit: '%' } }))(0.1121)).toBe('11.21%');
    expect(format(withWidgetUnit(households, { display: { unit: '%' } })).plain(0.1121)).toBe('11.21%');
  });
});

describe('the name shown for a column', () => {
  interface Case {
    name: string;
    indicator: ColumnMetadataMap | null;
    display: WidgetDisplay | null;
    config: Partial<Pick<WidgetConfig, 'measure' | 'ratio'>>;
    label: string;
    plotted: string;
    names: Record<string, string>;
  }

  // The same cases the API's ColumnLabels tests read (design D6): the widget's label, else the indicator's, else the column's own name.
  const shared = columnCases.cases as unknown as Case[];

  it('reads the shared cases', () => {
    expect(shared.length).toBeGreaterThanOrEqual(8);
  });

  it.each(shared.map((c) => [c.name, c] as const))('names the columns as the shared case says: %s', (_name, c) => {
    const names = columnLabels(c.indicator ? { columns: c.indicator } : null, c.display);

    expect(plotted({ measure: c.config.measure ?? null, ratio: c.config.ratio ?? null, display: c.display }, c.label, names)).toBe(c.plotted);
    for (const [column, shown] of Object.entries(c.names)) {
      expect(names(column)).toBe(shown);
    }
  });

  it('is the column itself when nothing gives it a label', () => {
    expect(columnLabels(null, null)('comuna')).toBe('comuna');
    expect(columnLabels({ columns: null }, { labels: undefined })('comuna')).toBe('comuna');
    expect(rawNames('comuna')).toBe('comuna');
  });

  it('trims a label and treats a blank one as none', () => {
    const names = columnLabels({ columns: { comuna: { label: '  Comuna  ' }, estrato: { label: ' ' } } }, { labels: { zona: '  Zona  ', barrio: '' } });

    expect(names('comuna')).toBe('Comuna');
    expect(names('estrato')).toBe('estrato');
    expect(names('zona')).toBe('Zona');
    expect(names('barrio')).toBe('barrio');
  });

  it('does not read a label from the prototype of a map', () => {
    const names = columnLabels({ columns: { comuna: { label: 'Comuna' } } }, { labels: { zona: 'Zona' } });

    expect(names('constructor')).toBe('constructor');
    expect(names('toString')).toBe('toString');
  });

  it('keeps an entry of the indicator that holds no label as the column itself', () => {
    expect(columnLabels({ columns: { comuna: {} } }, null)('comuna')).toBe('comuna');
  });
});

describe('textsCrowd (add-widget-context, design D5)', () => {
  const text = (length: number) => 'x'.repeat(length);

  it('is false when the widget has no subtitle and no footnote, whatever its tile', () => {
    expect(textsCrowd({ w: 3, h: 2 }, null, null)).toBe(false);
    expect(textsCrowd({ w: 3, h: 1 }, undefined, undefined)).toBe(false);
    expect(textsCrowd({ w: 3, h: 2 }, '', '   ')).toBe(false);
  });

  it('is true for a text on a tile two rows high or less, however short and however wide', () => {
    expect(textsCrowd({ w: 3, h: 2 }, null, 'Sin ponderar.')).toBe(true);
    expect(textsCrowd({ w: 12, h: 2 }, 'Hogares', null)).toBe(true);
    expect(textsCrowd({ w: 12, h: 1 }, 'Hogares', 'Sin ponderar.')).toBe(true);
  });

  it('reads a blank text as none', () => {
    expect(textsCrowd({ w: 4, h: 3 }, '   ', null)).toBe(false);
    expect(textsCrowd({ w: 3, h: 2 }, '   ', ' ')).toBe(false);
  });

  it('is false for the texts of a tall tile that stay under half of its height (a subtitle of 80 characters on a 6×4 tile)', () => {
    expect(textsCrowd({ w: 6, h: 4 }, text(80), null)).toBe(false);
    // The longest legal texts still fit a 6×4 tile: 3 + 6 lines of 16 px and 8 px between them make 152 of its 168.
    expect(textsCrowd({ w: 6, h: 4 }, text(200), text(500))).toBe(false);
  });

  it('is true when a footnote of 500 characters fills a tile 4 columns wide and 3 rows high', () => {
    expect(textsCrowd({ w: 4, h: 3 }, null, text(500))).toBe(true);
  });

  // 4 columns hold 60 characters a line; a 3-row tile is 248 px tall, and half of it is 124 px: 7 lines and a gap take 116, 8 take 132.
  it('changes at half of the tile height', () => {
    expect(textsCrowd({ w: 4, h: 3 }, null, text(420))).toBe(false);
    expect(textsCrowd({ w: 4, h: 3 }, null, text(421))).toBe(true);
  });

  it('counts the subtitle and the footnote together, a gap after each', () => {
    // 3 columns hold 45 characters a line: 5 lines and 3 lines, 128 px and 8 px of gaps, of the 124 px that half of 248 allows.
    expect(textsCrowd({ w: 3, h: 3 }, text(200), text(100))).toBe(true);
    expect(textsCrowd({ w: 3, h: 3 }, text(200), null)).toBe(false);
    expect(textsCrowd({ w: 3, h: 3 }, null, text(100))).toBe(false);
  });

  it('is true at exactly half of the tile height, and false under it', () => {
    // A 6×4 tile is 336 px tall. Ten lines (160 px) and two gaps (8 px) are exactly half; nine lines are under it.
    expect(textsCrowd({ w: 6, h: 4 }, text(200), text(630))).toBe(true);
    expect(textsCrowd({ w: 6, h: 4 }, text(200), text(540))).toBe(false);
  });

  it('trims a text before it counts it', () => {
    expect(textsCrowd({ w: 4, h: 3 }, null, ` ${text(420)} `)).toBe(false);
  });
});

describe('figureFormatter (add-small-sample-warnings, design D9)', () => {
  it('writes a whole number with thousands separators and up to two decimals when it is not whole', () => {
    const figure = figureFormatter('en-US');

    expect(figure(16)).toBe('16');
    expect(figure(12450)).toBe('12,450');
    expect(figure(0.5)).toBe('0.5');
    expect(figure(1.236)).toBe('1.24');
    expect(figure(0)).toBe('0');
  });

  it('follows the locale, and never writes a percent or a unit', () => {
    expect(figureFormatter('es-CO')(12450.5)).toBe('12.450,5');
    expect(figureFormatter('en-US')(5600)).toBe('5,600');
  });
});
