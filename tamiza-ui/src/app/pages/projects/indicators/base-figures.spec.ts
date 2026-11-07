import en from '../../../../../public/i18n/en.json';
import es from '../../../../../public/i18n/es.json';
import { QueryFigures, QueryNoData } from '../../../core/projects/indicators.service';
import {
  FigureContext,
  answerColumnsOf,
  baseColumnOf,
  figureNames,
  figureTooltip,
  histogramTooltip,
  isSmallBase,
  nLine,
  noBaseSettings,
  noDataLine,
  noDataValuesOf,
  seriesValueWithN,
  settingsInForce,
  smallBaseNote,
  smallBaseSome,
  smallBaseSummary,
  smallBaseTableNote,
  smallBaseTooltip,
  smallBaseMarksText,
  statNLine,
} from './base-figures';
import cases from './testing/base-figures.json';
import { IndicatorFormat } from '../../../core/projects/pipeline.service';
import { columnLabels, figureFormatter, valueFormatter } from './format';

// The base figures of a widget (add-small-sample-warnings, design D7, D9, D11) with the words of both languages. The twin of the API's
// BaseFigures reads the same cases in testing/base-figures.json (task 5.4); the cases here are the ones the design names.

type Tree = { [key: string]: string | Tree };
const translator =
  (tree: Tree) =>
  (key: string, params: Record<string, unknown> = {}): string => {
    const text = key.split('.').reduce<string | Tree | undefined>((node, part) => (typeof node === 'object' ? node[part] : undefined), tree);
    return typeof text === 'string' ? text.replace(/\{\{\s*([\w.]+)\s*\}\}/g, (_, name: string) => String(params[name])) : key;
  };
const words = { en: translator(en as Tree), es: translator(es as Tree) };
const locales = { en: 'en-US', es: 'es-CO' } as const;

const names = columnLabels({ columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' }, hogares: { label: 'hogares' } } }, null);
const ratio = { ratio: { numerator: 'numerator', denominator: 'denominator' } };

function context(lang: 'en' | 'es', baseColumn: string | null = null): FigureContext {
  return { names: figureNames(ratio, names, baseColumn), locale: locales[lang], t: words[lang] };
}

describe('the settings in force (Comunas below 30, Turned off for a widget, Own threshold)', () => {
  it('follows the dashboard for what the widget does not set', () => {
    expect(settingsInForce(undefined, { minN: 30, showN: true })).toEqual({ minN: 30, showN: true });
    expect(settingsInForce({}, { minN: 30 })).toEqual({ minN: 30, showN: false });
    expect(settingsInForce(null, null)).toEqual(noBaseSettings);
    expect(settingsInForce({}, { minN: null, showN: false })).toEqual(noBaseSettings);
  });

  it('lets a threshold of 0 turn it off for the widget, and an own threshold replace the dashboard\'s', () => {
    expect(settingsInForce({ minN: 0 }, { minN: 30, showN: false }).minN).toBeNull();
    expect(settingsInForce({ minN: 10 }, { minN: 30, showN: false }).minN).toBe(10);
    expect(settingsInForce({ minN: 10 }, null).minN).toBe(10);
  });

  it('lets a widget show or hide the n line whatever the dashboard says', () => {
    expect(settingsInForce({ showN: false }, { minN: null, showN: true }).showN).toBe(false);
    expect(settingsInForce({ showN: true }, { minN: null, showN: false }).showN).toBe(true);
  });
});

describe('the small-base rule', () => {
  it('is a base below the threshold; COMUNA 7 (16) and SIN NOMBRE (20) under 30, COMUNA 1 (121) not', () => {
    expect(isSmallBase(16, 30)).toBe(true);
    expect(isSmallBase(20, 30)).toBe(true);
    expect(isSmallBase(121, 30)).toBe(false);
  });

  it('is never true at the threshold itself, without a threshold, or for a value with no n (Value with no n)', () => {
    expect(isSmallBase(30, 30)).toBe(false);
    expect(isSmallBase(16, null)).toBe(false);
    expect(isSmallBase(16, undefined)).toBe(false);
    expect(isSmallBase(null, 30)).toBe(false);
    expect(isSmallBase(undefined, 30)).toBe(false);
  });

  it('has the own threshold 10 turn COMUNA 7 (16) into a normal base', () => {
    expect(isSmallBase(16, settingsInForce({ minN: 10 }, { minN: 30, showN: false }).minN)).toBe(false);
  });
});

describe('what the indicator declares for its columns', () => {
  const columns = {
    hogares: { label: 'Hogares', base: true as const },
    variable: { categories: 'multiple' as const },
    categoria: { label: 'Respuesta', categories: 'single' as const },
    comuna: { missing: ['Sin dato'] },
    otra: { label: 'Otra' },
  };

  it('finds the base column and the answer columns', () => {
    expect(baseColumnOf(columns)).toBe('hogares');
    expect(baseColumnOf({ otra: { label: 'Otra' } })).toBeNull();
    expect(baseColumnOf(null)).toBeNull();
    expect(answerColumnsOf(columns)).toEqual(['variable', 'categoria']);
    expect(answerColumnsOf(undefined)).toEqual([]);
  });

  it('lists the values that mean no data by column', () => {
    expect(noDataValuesOf(columns)).toEqual({ comuna: ['Sin dato'] });
    expect(noDataValuesOf(null)).toEqual({});
  });
});

describe('the lines a tooltip adds after its value (Rate of a comuna, Figures of a percent indicator, Declared base in a tooltip)', () => {
  const comuna1: QueryFigures = { numerator: 56, denominator: 121, n: 121 };

  it.each(['en', 'es'] as const)('gives the numerator and the denominator under their names, and no n line when the n is the denominator (%s)', (lang) => {
    const tooltip = figureTooltip(comuna1, context(lang), 30);

    expect(tooltip.lines.map((line) => `${line.label}: ${line.value}`)).toEqual(['Hogares que asistieron: 56', 'Hogares: 121']);
    expect(tooltip.footer).toBeNull();
  });

  it('writes the figures as counts, never in the indicator\'s format or with its unit (56 and 121, never 5,600%)', () => {
    const percent = valueFormatter({ format: 'percent', unit: 'de los hogares' }, 'en-US');

    expect(percent(56)).toBe('5,600% de los hogares');
    expect(figureTooltip(comuna1, context('en'), null).lines.map((line) => line.value)).toEqual(['56', '121']);
  });

  it('writes up to two decimals when a figure is not whole, and thousands separators', () => {
    const tooltip = figureTooltip({ numerator: 0.5, denominator: 12450, n: 12450 }, context('en'), null);

    expect(tooltip.lines.map((line) => line.value)).toEqual(['0.5', '12,450']);
    expect(figureFormatter('es-CO')(12450)).toBe('12.450');
  });

  it('adds the n under the name of the declared base column when it is not the denominator', () => {
    const measure = { names: figureNames({ ratio: null }, names, 'hogares'), locale: 'en-US', t: words.en };

    expect(figureTooltip({ n: 154 }, measure, null).lines).toEqual([{ label: 'n (hogares)', value: '154', align: 'end' }]);
    expect(figureTooltip({ numerator: 80, denominator: 160, n: 154 }, context('en', 'hogares'), null).lines.map((line) => `${line.label}: ${line.value}`)).toEqual([
      'Hogares que asistieron: 80',
      'Hogares: 160',
      'n (hogares): 154',
    ]);
  });

  it('adds the n of a ratio counted once across answers, under the name of its denominator', () => {
    const lines = figureTooltip({ numerator: 20, denominator: 57, n: 19 }, context('en'), null).lines;

    expect(lines.at(-1)).toEqual({ label: 'n (Hogares)', value: '19', align: 'end' });
  });

  it('adds nothing for a mark with no figures', () => {
    expect(figureTooltip({}, context('en'), 30)).toEqual({ lines: [], footer: null });
    expect(figureTooltip(undefined, context('en'), 30)).toEqual({ lines: [], footer: null });
    expect(figureTooltip(null, context('en'), 30).lines).toEqual([]);
  });

  it.each([
    ['en', 'Small base: n = 16 (below 30)'],
    ['es', 'Base pequeña: n = 16 (menos de 30)'],
  ] as const)('ends the tooltip of a small base with the note, in %s', (lang, note) => {
    const tooltip = figureTooltip({ numerator: 8, denominator: 16, n: 16 }, context(lang), 30);

    expect(tooltip.footer).toBe(note);
    expect(smallBaseTooltip(16, 30, locales[lang], words[lang])).toBe(note);
    expect(smallBaseTooltip(121, 30, 'en-US', words.en)).toBeNull();
    expect(smallBaseTooltip(null, 30, 'en-US', words.en)).toBeNull();
  });

  it('writes the n of a series in the value of a group tooltip that lists several, and the note of a small one', () => {
    expect(seriesValueWithN('50%', 16, 'en-US')).toBe('50% (n = 16)');
    expect(seriesValueWithN('50%', null, 'en-US')).toBe('50%');
    expect(smallBaseSome(30, 'en-US', words.en)).toBe('Small base in some series (below 30)');
    expect(smallBaseSome(30, 'es-CO', words.es)).toBe('Base pequeña en algunas series (menos de 30)');
  });

  it('gives a histogram bar its range and its count only (Histogram bar)', () => {
    expect(histogramTooltip('10–20', 12, 'en-US', words.en)).toEqual({ heading: '10–20', lines: [{ label: 'Rows', value: '12', align: 'end' }], footer: null });
    expect(histogramTooltip('10–20', 1200, 'es-CO', words.es).lines).toEqual([{ label: 'Filas', value: '1.200', align: 'end' }]);
  });
});

describe('the n line under a widget (Line shown, Widget without a base n)', () => {
  it('reads "n = " and the n with thousands separators, then the name of the column it counts', () => {
    expect(nLine(223, 'Hogares que asistieron', 'en-US')).toBe('n = 223 Hogares que asistieron');
    expect(nLine(1234, 'Hogares', 'en-US')).toBe('n = 1,234 Hogares');
    expect(nLine(1234, 'Hogares', 'es-CO')).toBe('n = 1.234 Hogares');
    expect(nLine(476, null, 'en-US')).toBe('n = 476');
  });

  it('is none for a widget with no n', () => {
    expect(nLine(null, 'Hogares', 'en-US')).toBeNull();
    expect(nLine(undefined, 'Hogares', 'en-US')).toBeNull();
  });

  it('counts the declared base column, else the denominator', () => {
    expect(figureNames(ratio, names, null).base).toBe('Hogares');
    expect(figureNames(ratio, names, 'hogares').base).toBe('hogares');
    expect(figureNames({ ratio: null }, names, null).base).toBeNull();
  });
});

describe('the notes about small bases', () => {
  it.each([
    ['en', 'Marks with a base below 30 are dimmed.', '* Base below 30.', ', 2 with a small base'],
    ['es', 'Las marcas con una base menor que 30 aparecen atenuadas.', '* Base menor que 30.', ', 2 con base pequeña'],
  ] as const)('are written in %s', (lang, note, table, summary) => {
    expect(smallBaseNote(30, locales[lang], words[lang])).toBe(note);
    expect(smallBaseTableNote(30, locales[lang], words[lang])).toBe(table);
    expect(smallBaseSummary(2, words[lang])).toBe(summary);
    expect(smallBaseSummary(0, words[lang])).toBe('');
  });
});

describe('the no-data line (Rate of the left-out rows, Share of a count, Nothing left out)', () => {
  const percent = valueFormatter({ format: 'percent', unit: null }, 'en-US');
  const households = valueFormatter({ format: 'count', unit: 'hogares' }, 'en-US');

  it('reads the left-out values and their value in the indicator\'s format', () => {
    const noData: QueryNoData = { values: ['No sabe'], value: 5 / 223, numerator: 5, denominator: 223, n: 223, share: null };

    expect(noDataLine(noData, percent, false, 'en-US')).toBe('No sabe: 2.24%');
  });

  it('adds the share of a measure, and the n while the n line shows', () => {
    const noData: QueryNoData = { values: ['4. No sabe'], value: 11, share: 11 / 476, n: 11 };

    expect(noDataLine(noData, households, false, 'en-US')).toBe('4. No sabe: 11 hogares (2.3%)');
    expect(noDataLine(noData, households, true, 'en-US')).toBe('4. No sabe: 11 hogares (2.3%) · n = 11');
    expect(noDataLine({ values: ['Sin dato'], value: 0.042, n: 20 }, percent, true, 'en-US')).toBe('Sin dato: 4.2% · n = 20');
  });

  it('names at most three values, then an ellipsis, and writes a share with the locale\'s separators', () => {
    expect(noDataLine({ values: ['a', 'b', 'c'], value: 1 }, households, false, 'en-US')).toBe('a, b, c: 1 hogares');
    expect(noDataLine({ values: ['a', 'b', 'c', 'd'], value: 1 }, households, false, 'en-US')).toBe('a, b, c, …: 1 hogares');
    expect(noDataLine({ values: ['No sabe'], value: 11, share: 0.0231 }, households, false, 'es-CO')).toBe('No sabe: 11 hogares (2,3%)');
  });

  it('names the values alone when they hold no value, and is none when nothing was left out', () => {
    expect(noDataLine({ values: ['Sin dato'], value: null }, percent, false, 'en-US')).toBe('Sin dato');
    expect(noDataLine(undefined, percent, false, 'en-US')).toBeNull();
    expect(noDataLine(null, percent, false, 'en-US')).toBeNull();
    expect(noDataLine({ values: [] }, percent, false, 'en-US')).toBeNull();
  });
});

// The same cases the API's BaseFigures tests read (add-small-sample-warnings, design D7): both sides give the same settings in force, the same
// small bases, the same tooltip figures and the same sentences, in English and in Spanish.
describe('the shared cases of base-figures.json', () => {
  const languages = ['en', 'es'] as const;
  const localeOf = (lang: (typeof languages)[number]) => cases.locales[lang];

  it('reads the shared cases', () => {
    expect(cases.settings.length).toBeGreaterThanOrEqual(8);
    expect(cases.tooltips.length).toBeGreaterThanOrEqual(8);
    expect(cases.sentences.length).toBeGreaterThanOrEqual(10);
  });

  it.each(cases.settings.map((c) => [c.name, c] as const))('gives the settings in force: %s', (_name, c) => {
    expect(settingsInForce(c.display, c.dashboard)).toEqual(c.expected);
  });

  it.each(cases.small.map((c) => [c.name, c] as const))('tells a small base: %s', (_name, c) => {
    expect(isSmallBase(c.n, c.minN)).toBe(c.expected);
  });

  describe.each(languages)('in %s', (lang) => {
    const t = words[lang];
    const locale = localeOf(lang);

    it.each(cases.tooltips.map((c) => [c.name, c] as const))('writes the tooltip figures: %s', (_name, c) => {
      const named = c.columns as Record<string, string>;
      const names = (column: string) => named[column] ?? column;
      const ratio = c.ratio ? { numerator: 'numerator', denominator: 'denominator' } : null;
      const tooltip = figureTooltip(c.figures, { names: figureNames({ ratio }, names, c.baseColumn), locale, t }, c.minN);

      expect(tooltip.lines.map((line) => [line.label, line.value])).toEqual(c[lang].lines);
      expect(tooltip.footer).toEqual(c[lang].footer);
    });

    it.each(cases.nLines.map((c) => [c.name, c] as const))('writes the n line: %s', (_name, c) => {
      expect(nLine(c.n, c.label, locale)).toEqual(c[lang]);
    });

    it.each(cases.noDataLines.map((c) => [c.name, c] as const))('writes the no-data line: %s', (_name, c) => {
      const format = valueFormatter({ format: c.format as IndicatorFormat, unit: c.unit }, locale);

      expect(noDataLine(c.noData as Parameters<typeof noDataLine>[0], format, c.showN, locale)).toEqual(c[lang]);
    });

    it.each(cases.sentences.filter((c) => !('apiOnly' in c)).map((c) => [c.name, c] as const))('writes the sentence: %s', (_name, c) => {
      const args = c.args as { minN?: number; count?: number; n?: number | null; value?: string };
      const written: Record<string, () => string | null> = {
        smallNote: () => smallBaseNote(args.minN!, locale, t),
        tableNote: () => smallBaseTableNote(args.minN!, locale, t),
        summaryCount: () => smallBaseSummary(args.count!, t),
        smallTooltip: () => smallBaseTooltip(args.n!, args.minN!, locale, t),
        smallSome: () => smallBaseSome(args.minN!, locale, t),
        seriesValueWithN: () => seriesValueWithN(args.value!, args.n!, locale),
        histogramRows: () => histogramTooltip('1–2', 1, locale, t).lines[0].label,
      };

      expect(written[c.kind](), c.kind).toEqual(c[lang]);
    });
  });
});


describe('statNLine (improve-widget-text-hierarchy, design D6, task 5.2)', () => {
  const ratio = { numerator: 'numerator', denominator: 'denominator' };
  const names = (column: string) => ({ denominator: 'Hogares', hogares: 'Hogares encuestados' })[column as 'denominator' | 'hogares'] ?? column;
  const tile = { ratio, statistic: undefined, display: undefined };

  it('is none while neither the tile nor its dashboard shows the n', () => {
    expect(statNLine(tile, names, { showN: false, baseColumn: null }, 476, 'en-US')).toBeNull();
    expect(statNLine({ ...tile, display: { secondary: ['unfiltered'] } }, names, { showN: false, baseColumn: null }, 476, 'en-US')).toBeNull();
  });

  it('names the denominator of a ratio, or the base column the indicator declares', () => {
    expect(statNLine({ ...tile, display: { secondary: ['n'] } }, names, { showN: false, baseColumn: null }, 476, 'en-US')).toBe('n = 476 Hogares');
    expect(statNLine(tile, names, { showN: true, baseColumn: 'hogares' }, 12450, 'en-US')).toBe('n = 12,450 Hogares encuestados');
  });

  it('says a number of values for a statistic, with the name of the base column only when there is one', () => {
    const median = { ratio: null, statistic: 'median' as const, display: { secondary: ['n' as const] } };
    expect(statNLine(median, names, { showN: false, baseColumn: null }, 37, 'en-US')).toBe('n = 37');
    expect(statNLine(median, names, { showN: false, baseColumn: 'hogares' }, 4, 'en-US')).toBe('n = 4 Hogares encuestados');
    // Never the denominator of the ratio a statistic runs over.
    expect(statNLine({ ...median, ratio }, names, { showN: false, baseColumn: null }, 12, 'en-US')).toBe('n = 12');
  });

  it('is none for a tile with no n', () => {
    expect(statNLine({ ...tile, display: { secondary: ['n'] } }, names, { showN: true, baseColumn: null }, null, 'en-US')).toBeNull();
    expect(statNLine({ ...tile, display: { secondary: ['n'] } }, names, { showN: true, baseColumn: null }, undefined, 'en-US')).toBeNull();
  });
});

describe('smallBaseMarksText (improve-widget-text-hierarchy, design D5)', () => {
  const t = (key: string, params?: Record<string, unknown>) => {
    const words = key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], en) as string;
    return words.replace(/\{\{\s*(\w+)\s*\}\}/g, (_, name: string) => String(params?.[name]));
  };
  const marks = (count: number) => Array.from({ length: count }, (_, i) => ({ name: `COMUNA ${i + 1}`, n: 10 + i }));

  it('names every mark of up to five, with its n, and says they are drawn dimmed', () => {
    expect(smallBaseMarksText([{ name: 'COMUNA 7', n: 16 }, { name: 'SIN NOMBRE', n: 20 }], 30, 'en-US', t)).toBe(
      'Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed.',
    );
    expect(smallBaseMarksText(marks(5), 30, 'en-US', t)).not.toContain('more');
  });

  it('names the first five and says how many more', () => {
    expect(smallBaseMarksText(marks(8), 30, 'en-US', t)).toBe(
      'Small base (below 30): COMUNA 1 (n = 10), COMUNA 2 (n = 11), COMUNA 3 (n = 12), COMUNA 4 (n = 13), COMUNA 5 (n = 14) and 3 more. Their marks are drawn dimmed.',
    );
  });

  it('writes the numbers with the thousands separators of the locale', () => {
    expect(smallBaseMarksText([{ name: 'A', n: 1200 }], 1500, 'en-US', t)).toBe('Small base (below 1,500): A (n = 1,200). Their marks are drawn dimmed.');
  });

  it('has the words of the foot in each language', () => {
    for (const words of [en, es]) {
      for (const key of ['small', 'smallText', 'more', 'noData', 'lines', 'detail', 'note', 'smallStat', 'smallStatShort', 'smallChart']) {
        expect((words.indicators.foot as Record<string, string>)[key], key).toBeTruthy();
      }
    }
    expect(es.indicators.foot.note).toBe('Nota');
    expect(es.indicators.foot.detail).toBe('Detalle');
    expect(es.indicators.foot.small).toBe('{{count}} con base pequeña');
  });
});
