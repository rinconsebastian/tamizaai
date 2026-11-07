import { ExportParts } from '../export-parts';
import { ImageInput, ImageTheme, composeImage, wrap, wrapWords } from './image-layout';
import { fitText, maxImageRows, maxImageWidth } from './svg-table';

// 6.5 pixels a character, as the design's tests do: the layout is the same in any environment.
const measure = (text: string, size = 12) => text.length * 6.5 * (size / 12);
const theme: ImageTheme = { heading: '#111', body: '#444', border: '#ddd', shade: '#f4f4f4', surface: '#fff', strong: '#999', warning: '#a60' };
const tableLabels = {
  rowsNotShown: (count: number) => (count === 1 ? '1 row not shown in the image.' : `${count} rows not shown in the image.`),
  columnsNotShown: (count: number) => (count === 1 ? '1 column not shown in the image.' : `${count} columns not shown in the image.`),
  inWorkbook: 'The Excel workbook has them all.',
};
const empty: ExportParts = { caption: null, legend: null, notes: [], stat: null, table: null };
/** A stat tile's parts: the figure's size as its tile gave it on screen (improve-stat-cards, design D1), and no secondary lines. */
const stat = (value: string, description: string | null, size = 24, secondary: string[] = []): NonNullable<ExportParts['stat']> => ({ value, description, size, secondary });

function compose(overrides: Partial<ImageInput> = {}, parts: Partial<ExportParts> = {}) {
  const layout = composeImage({
    width: 400,
    title: 'Coverage by municipality',
    subtitle: null,
    footnote: null,
    notesAbove: [],
    notesBelow: [],
    parts: { ...empty, ...parts },
    plot: null,
    notesExtra: [],
    footer: [],
    fontFamily: 'system-ui, sans-serif',
    theme,
    tableLabels,
    resolve: (color) => (color.startsWith('var(--c') ? `rgb(${color.length}, 0, 0)` : color),
    measure,
    ...overrides,
  });
  const texts = Array.from(layout.svg.querySelectorAll('text')).map((t) => ({ text: t.textContent ?? '', y: Number(t.getAttribute('y')), size: Number(t.getAttribute('font-size')) }));
  return { ...layout, texts, markup: new XMLSerializer().serializeToString(layout.svg) };
}

const rowsOf = (count: number, columns = 2) => Array.from({ length: count }, (_, i) => Array.from({ length: columns }, (_c, c) => (c === 0 ? `Group ${i + 1}` : String(i * 10))));
const names = (texts: { text: string }[]) => texts.map((t) => t.text);

describe('wrap', () => {
  const at = (text: string) => measure(text);

  it('breaks at words and keeps a short text on one line', () => {
    expect(wrap('Coverage by municipality', 400, at)).toEqual(['Coverage by municipality']);
    expect(wrap('aaa bbb ccc', 26, at)).toEqual(['aaa', 'bbb', 'ccc']);
  });

  it('breaks a word wider than a line at a letter', () => {
    expect(wrap('abcdefghij', 32.5, at).join('')).toBe('abcdefghij');
    expect(wrap('abcdefghij', 32.5, at).every((line) => at(line) <= 32.5)).toBe(true);
  });
});

describe('wrapWords', () => {
  const at = (text: string) => measure(text);

  it('breaks between words like wrap', () => {
    expect(wrapWords('aaa bbb ccc', 26, at)).toEqual(['aaa', 'bbb', 'ccc']);
    expect(wrapWords('46.85% de los hogares', 400, at)).toEqual(['46.85% de los hogares']);
  });

  it('never cuts a word wider than a line: it stays whole on a line of its own', () => {
    expect(wrapWords('abcdefghij', 32.5, at)).toEqual(['abcdefghij']);
    expect(wrapWords('1,016,279 pesos', 32.5, at)).toEqual(['1,016,279', 'pesos']);
    expect(wrapWords('', 100, at)).toEqual(['']);
  });
});

describe('fitText', () => {
  it('keeps a text that fits, and cuts another with an ellipsis', () => {
    expect(fitText('Cali', 100, measure)).toBe('Cali');
    const cut = fitText('Buenaventura district', 70, measure);
    expect(cut.endsWith('…')).toBe(true);
    expect(measure(cut)).toBeLessThanOrEqual(70);
  });
});

describe('composeImage', () => {
  it('is a white picture of the width asked, in the system font, with the title first', () => {
    const { svg, width, markup, texts } = compose();

    expect(svg.getAttribute('viewBox')).toBe(`0 0 ${width} ${svg.getAttribute('height')}`);
    expect(width).toBe(400);
    expect(svg.getAttribute('font-family')).toBe('system-ui, sans-serif');
    expect(svg.firstElementChild!.getAttribute('fill')).toBe('#fff');
    expect(texts[0]).toMatchObject({ text: 'Coverage by municipality', size: 14 });
    expect(markup).not.toMatch(/class=|style=|<script|<foreignObject|href=/);
  });

  it('writes a long title whole over several lines, and cuts it with an ellipsis after three', () => {
    const long = 'Households visited by municipality and by round of the survey '.repeat(2).trim();
    const { texts } = compose({ title: long, width: 400 });
    const lines = texts.filter((t) => t.size === 14);

    expect(lines.length).toBeGreaterThan(1);
    expect(lines.map((l) => l.text).join(' ')).toBe(long);

    const tooLong = compose({ title: 'word '.repeat(200).trim(), width: 400 });
    const cut = tooLong.texts.filter((t) => t.size === 14);
    expect(cut).toHaveLength(3);
    expect(cut[2].text.endsWith('…')).toBe(true);
  });

  it('stacks title, notes above, caption, legend, notes below and footer from top to bottom', () => {
    const { texts } = compose(
      { notesAbove: ['Not filtered by: Round'], notesExtra: ['The basemap is not included in the image.'], footer: ['Census 2026 · Housing', 'Exported'] },
      { caption: 'Coverage: value', legend: { kind: 'entries', entries: [{ label: 'Piped', color: 'var(--c1)' }] }, notes: ['2 rows were left out.'] },
    );

    expect(names(texts)).toEqual([
      'Coverage by municipality',
      'Not filtered by: Round',
      'Coverage: value',
      'Piped',
      '2 rows were left out.',
      'The basemap is not included in the image.',
      'Census 2026 · Housing',
      'Exported',
    ]);
    const ys = texts.map((t) => t.y);
    expect(ys).toEqual([...ys].sort((a, b) => a - b));
  });

  it('writes the whole subtitle at 12 px under the title and the whole footnote after the notes, before the footer (Subtitle and footnote)', () => {
    const subtitle = 'Where have you seen theatre in the last year? (E4) '.repeat(4).trim();
    const footnote = 'Does not add up to 100 %: multiple answers. '.repeat(10).trim();
    const { texts } = compose(
      { subtitle, footnote, notesAbove: ['Not filtered by: Round'], notesExtra: ['The basemap is not included in the image.'], footer: ['Census 2026 · Housing', 'Exported'] },
      { caption: 'Coverage: value', legend: { kind: 'entries', entries: [{ label: 'Piped', color: 'var(--c1)' }] }, notes: ['2 rows were left out.'] },
    );

    const titleAt = texts.findIndex((t) => t.text === 'Coverage by municipality');
    const notesAt = texts.findIndex((t) => t.text === 'Not filtered by: Round');
    const subtitleLines = texts.slice(titleAt + 1, notesAt);
    expect(titleAt).toBe(0);
    expect(subtitleLines.length).toBeGreaterThan(1); // wrapped to the width, never cut
    expect(subtitleLines.every((t) => t.size === 12)).toBe(true);
    expect(subtitleLines.map((t) => t.text).join(' ')).toBe(subtitle);

    const lastNote = texts.findIndex((t) => t.text === 'The basemap is not included in the image.');
    const footerAt = texts.findIndex((t) => t.text === 'Census 2026 · Housing');
    const footnoteLines = texts.slice(lastNote + 1, footerAt);
    expect(footnoteLines.length).toBeGreaterThan(1);
    expect(footnoteLines.every((t) => t.size === 12)).toBe(true);
    expect(footnoteLines.map((t) => t.text).join(' ')).toBe(footnote);
    expect(names(texts).join('')).not.toContain('…');
    const ys = texts.map((t) => t.y);
    expect(ys).toEqual([...ys].sort((a, b) => a - b));
  });

  it('draws nothing more for a widget without a subtitle or a footnote: the picture is the one made before', () => {
    const base = compose({ notesAbove: ['Not filtered by: Round'], footer: ['Census 2026 · Housing'] }, { caption: 'Coverage: value', notes: ['2 rows were left out.'] });
    const blanks = compose({ subtitle: '', footnote: '', notesAbove: ['Not filtered by: Round'], footer: ['Census 2026 · Housing'] }, { caption: 'Coverage: value', notes: ['2 rows were left out.'] });

    expect(blanks.markup).toBe(base.markup);
    expect(names(base.texts)).toEqual(['Coverage by municipality', 'Not filtered by: Round', 'Coverage: value', '2 rows were left out.', 'Census 2026 · Housing']);
  });

  it('writes the whole legend of a pie, with each slice\'s value and share, wrapped to the width', () => {
    const entries = Array.from({ length: 12 }, (_, i) => ({ label: `Municipality ${i + 1}`, color: 'var(--c1)', detail: `${(i + 1) * 10} · 8.3%` }));

    const { texts, svg } = compose({ width: 320 }, { legend: { kind: 'entries', entries } });

    expect(texts.slice(1)).toHaveLength(12);
    expect(texts[1].text).toBe('Municipality 1  10 · 8.3%');
    expect(new Set(texts.slice(1).map((t) => t.y)).size).toBeGreaterThan(1);
    expect(Number(svg.getAttribute('width'))).toBe(320);
  });

  it('draws a short stroke for a line chart\'s key and a hairline round an outlined swatch', () => {
    const { svg } = compose(
      {},
      { legend: { kind: 'entries', entries: [{ label: 'A', color: 'var(--c1)', swatch: 'line' }, { label: 'Neutral', color: '#fff', swatch: 'outlined' }] } },
    );

    expect(svg.querySelectorAll('line[stroke-linecap="round"]')).toHaveLength(1);
    expect(svg.querySelectorAll('rect[stroke="#999"]')).toHaveLength(1);
  });

  it('draws the legend under the plot, above the footnote and the footer (Lines above the footnote)', () => {
    const plot = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    plot.setAttribute('width', '300');
    plot.setAttribute('height', '120');
    const { texts } = compose(
      { plot, footnote: 'No suman 100 %.', footer: ['Household survey'] },
      { legend: { kind: 'entries', entries: [{ label: 'Piped', color: 'var(--c1)', swatch: 'square' }] } },
    );

    const at = (text: string) => texts.find((t) => t.text === text)!.y;
    expect(at('Piped')).toBeGreaterThan(Number(plot.getAttribute('y')) + 120 - 1);
    expect(at('No suman 100 %.')).toBeGreaterThan(at('Piped'));
    expect(at('Household survey')).toBeGreaterThan(at('No suman 100 %.'));
  });

  it('draws a scale from the smallest to the largest value in seven steps, with what an empty cell is', () => {
    const steps = Array.from({ length: 7 }, (_, i) => `#${i}${i}${i}`);

    const { texts, svg } = compose({}, { legend: { kind: 'scale', low: '2', high: '75', steps, empty: 'No value', stepOpacity: 1 } });

    expect(names(texts).slice(1)).toEqual(['2', '75', 'No value']);
    expect(Array.from(svg.querySelectorAll('rect[fill-opacity="1"]')).map((r) => r.getAttribute('fill'))).toEqual(steps);
  });

  it('draws the five marks of a box plot\'s key in the widget\'s color', () => {
    const items = (['box', 'median', 'mean', 'whiskers', 'outliers'] as const).map((glyph) => ({ glyph, label: glyph }));

    const { texts, svg } = compose({}, { legend: { kind: 'key', accent: '#0a0', items } });

    expect(names(texts).slice(1)).toEqual(['box', 'median', 'mean', 'whiskers', 'outliers']);
    expect(svg.querySelectorAll('[stroke="#0a0"]').length).toBeGreaterThanOrEqual(5);
    expect(svg.querySelectorAll('circle')).toHaveLength(1);
  });

  it('writes a stat tile\'s figure large with what it measures under it', () => {
    const { texts } = compose({}, { stat: stat('1,234', 'Households visited') });

    expect(texts[1]).toMatchObject({ text: '1,234', size: 24 });
    expect(texts[2]).toMatchObject({ text: 'Households visited', size: 14 });
  });

  it('writes the figure at the size its tile has on screen, never at 36 px, and keeps it between 18 and 30 px (Smaller value)', () => {
    const sizes = [22.5, 30, 18].map((size) => compose({}, { stat: stat('46.85% de los hogares', 'Teatro hoy', size) }).texts[1].size);
    const guarded = [48, 36, 4, NaN].map((size) => compose({}, { stat: stat('46.85%', null, size) }).texts[1].size);

    expect(sizes).toEqual([22.5, 30, 18]);
    expect(guarded).toEqual([30, 30, 18, 24]);
    expect([...sizes, ...guarded]).not.toContain(36);
    expect(compose({}, { stat: stat('46.85%', null, 30) }).markup).not.toContain('font-size="36"');
  });

  it('breaks the figure between words and never inside one: a word wider than the picture widens it (Long figure)', () => {
    // 1,016,279 at 18 px is 9 characters of 9.75 px: 87.75 px, wider than the 68 px of a picture 100 px wide.
    const narrow = compose({ width: 100 }, { stat: stat('1,016,279 pesos', 'Gasto', 18) });

    const lines = narrow.texts.filter((t) => t.size === 18).map((t) => t.text);
    expect(lines).toEqual(['1,016,279', 'pesos']);
    expect(narrow.width).toBe(Math.ceil(measure('1,016,279', 18)) + 32);
    expect(narrow.width).toBeGreaterThan(100);
    expect(names(narrow.texts).at(-1)).toBe('Gasto');
    // A picture wide enough keeps both words on one line and its width.
    const wide = compose({ width: 400 }, { stat: stat('1,016,279 pesos', 'Gasto', 18) });
    expect(wide.texts.filter((t) => t.size === 18).map((t) => t.text)).toEqual(['1,016,279 pesos']);
    expect(wide.width).toBe(400);
  });

  it('writes a figure of several words on lines of their own, moving what follows down (value wrapped between words)', () => {
    const one = compose({ width: 400 }, { stat: stat('46.85% de los hogares', 'Teatro hoy', 30) });
    const two = compose({ width: 160 }, { stat: stat('46.85% de los hogares', 'Teatro hoy', 30) });

    const figure = (layout: ReturnType<typeof compose>) => layout.texts.filter((t) => t.size === 30);
    expect(figure(one).map((t) => t.text)).toEqual(['46.85% de los hogares']);
    expect(figure(two).length).toBeGreaterThan(1);
    expect(figure(two).map((t) => t.text).join(' ')).toBe('46.85% de los hogares');
    expect(figure(two).every((t) => measure(t.text, 30) <= 160 - 32 || !t.text.includes(' '))).toBe(true);
    expect(two.texts.find((t) => t.text === 'Teatro hoy')!.y).toBeGreaterThan(one.texts.find((t) => t.text === 'Teatro hoy')!.y);
  });

  it('writes the secondary lines at 12 px under the description, or under the figure when the caption is hidden, before the notes below and the footnote (Lines above the footnote)', () => {
    const lines = ['n = 476 Hogares', 'Without filters: 46.85% de los hogares'];
    const shown = compose({ footnote: 'Sin ponderar.', notesBelow: ['A note.'], footer: ['Tamiza'] }, { stat: stat('46.85%', 'Teatro hoy', 24, lines) });
    const hidden = compose({}, { stat: stat('46.85%', null, 24, lines) });

    expect(names(shown.texts)).toEqual(['Coverage by municipality', '46.85%', 'Teatro hoy', ...lines, 'A note.', 'Sin ponderar.', 'Tamiza']);
    expect(shown.texts.slice(3, 5).every((t) => t.size === 12)).toBe(true);
    expect(names(hidden.texts)).toEqual(['Coverage by municipality', '46.85%', ...lines]);
  });

  it('draws the figure of a stat tile alone when its description is null (Caption hidden)', () => {
    const { texts } = compose({}, { stat: stat('1,234', null) });

    expect(names(texts)).toEqual(['Coverage by municipality', '1,234']);
    expect(texts[1]).toMatchObject({ text: '1,234', size: 24 });
  });

  it('puts the chart where the text leaves room, and counts its height', () => {
    const plot = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    plot.setAttribute('width', '368');
    plot.setAttribute('height', '240');

    const withPlot = compose({ plot });
    const without = compose();

    expect(plot.getAttribute('x')).toBe('16');
    expect(Number(plot.getAttribute('y'))).toBeGreaterThan(30);
    expect(withPlot.height - without.height).toBe(240 + 8);
  });

  describe('a table', () => {
    it('holds the header and the rows, and nothing is said about what it left out when all fit', () => {
      const table = { headers: ['municipality', 'value'], rows: rowsOf(3) };

      const { texts } = compose({}, { table });

      expect(names(texts)).toEqual(['Coverage by municipality', 'municipality', 'value', 'Group 1', '0', 'Group 2', '10', 'Group 3', '20']);
    });

    it('holds 100 rows of 128 and says 28 are not shown and that the workbook has them all', () => {
      const { texts } = compose({}, { table: { headers: ['municipality', 'value'], rows: rowsOf(128) } });

      expect(texts.filter((t) => /^Group \d+$/.test(t.text))).toHaveLength(maxImageRows);
      expect(names(texts).slice(-2)).toEqual(['28 rows not shown in the image.', 'The Excel workbook has them all.']);
    });

    it('says "1 row" for one row over', () => {
      const { texts } = compose({}, { table: { headers: ['municipality', 'value'], rows: rowsOf(maxImageRows + 1) } });

      expect(names(texts)).toContain('1 row not shown in the image.');
    });

    it('keeps the group column and the columns that fit 2,400 pixels, and counts the others', () => {
      const headers = ['municipality', ...Array.from({ length: 30 }, (_, i) => `Series with a long name ${i + 1}`)];
      const rows = [['Cali', ...Array.from({ length: 30 }, (_, i) => String(i))]];

      const { texts, width } = compose({}, { table: { headers, rows } });

      const shownHeaders = texts.filter((t) => t.text === 'municipality' || t.text.startsWith('Series with'));
      expect(shownHeaders[0].text).toBe('municipality');
      expect(shownHeaders.length).toBeLessThan(31);
      expect(width).toBeLessThanOrEqual(maxImageWidth + 32);
      expect(width).toBeGreaterThan(400);
      expect(names(texts)).toContain(`${31 - shownHeaders.length} columns not shown in the image.`);
    });

    it('cuts a cell wider than its column with an ellipsis, and writes a missing value as a dash', () => {
      const { texts } = compose({}, { table: { headers: ['a', 'b'], rows: [['x'.repeat(100), null]] } });

      expect(texts.find((t) => t.text.startsWith('xxx'))!.text.endsWith('…')).toBe(true);
      expect(names(texts)).toContain('—');
    });

    it('writes the totals in bold and lines the numbers up at their end', () => {
      const { svg } = compose({}, { table: { headers: ['g', 'Total'], rows: [['Cali', '5'], ['Total', '5']], align: ['start', 'end'], totals: { row: true, column: true } } });

      const total = Array.from(svg.querySelectorAll('text')).filter((t) => t.textContent === 'Total');
      expect(total.every((t) => t.getAttribute('font-weight') === '600')).toBe(true);
      expect(svg.querySelectorAll('text[text-anchor="end"]').length).toBeGreaterThan(0);
    });
  });
});

// The legend and the table of an image are drawn in the order the widget gives them, which is the order of the screen
// (add-declared-category-order, Image in the declared order): the layout adds none of its own.
describe('an image in a declared order', () => {
  it('writes the legend entries in the order given, with their text as given', () => {
    const entries = [{ label: 'Sí', color: 'var(--c1)' }, { label: 'No', color: 'var(--c2)' }, { label: 'No sabe', color: 'var(--c3)' }];

    const { texts } = compose({ width: 600 }, { legend: { kind: 'entries', entries } });

    const written = names(texts).filter((text) => ['Sí', 'No', 'No sabe'].includes(text));
    expect(written).toEqual(['Sí', 'No', 'No sabe']);
  });

  it('writes the rows of a table in the order given', () => {
    const rows = [['COMUNA 2', '5'], ['COMUNA 1', '7'], ['Sin dato', '1']];

    const { texts } = compose({ width: 600 }, { table: { headers: ['Comuna', 'Hogares'], rows } });

    const written = names(texts).filter((text) => text.startsWith('COMUNA') || text === 'Sin dato');
    expect(written).toEqual(['COMUNA 2', 'COMUNA 1', 'Sin dato']);
  });
});

describe('an image with small bases (add-small-sample-warnings, design D10, D11)', () => {
  it('draws the lines of the bases after the chart\'s own notes and before the footnote, which stays last (Lines in an image)', () => {
    const { texts } = compose(
      {
        footnote: 'No suman 100 %: respuesta múltiple.',
        notesAbove: ['Not filtered by: Round'],
        notesBelow: ['Marks with a base below 30 are dimmed.', 'n = 223 Hogares que asistieron', 'No sabe: 2.24%'],
        notesExtra: ['The basemap is not included in the image.'],
        footer: ['Census 2026 · Housing'],
      },
      { caption: 'Coverage: value', notes: ['2 rows were left out.'] },
    );

    expect(names(texts)).toEqual([
      'Coverage by municipality',
      'Not filtered by: Round',
      'Coverage: value',
      '2 rows were left out.',
      'The basemap is not included in the image.',
      'Marks with a base below 30 are dimmed.',
      'n = 223 Hogares que asistieron',
      'No sabe: 2.24%',
      'No suman 100 %: respuesta múltiple.',
      'Census 2026 · Housing',
    ]);
    const ys = texts.map((t) => t.y);
    expect(ys).toEqual([...ys].sort((a, b) => a - b));
  });

  it('draws the lines at 12 px, whole, in the body color', () => {
    const long = 'n = 223 ' + 'Hogares que asistieron '.repeat(6).trim();
    const { texts, markup } = compose({ notesBelow: [long] });

    const lines = texts.filter((t) => t.size === 12);
    expect(lines.length).toBeGreaterThan(1);
    expect(lines.map((t) => t.text).join(' ')).toBe(long);
    expect(markup).toContain('fill="#444"');
  });

  it('adds nothing when the widget has no line', () => {
    const plain = compose({}, { caption: 'Coverage: value' });
    const none = compose({ notesBelow: [] }, { caption: 'Coverage: value' });

    expect(names(none.texts)).toEqual(names(plain.texts));
    expect(none.height).toBe(plain.height);
  });

  it('writes the figure of a stat tile at full contrast, whatever its base, at the size its tile gave it (Small base in the image)', () => {
    const whole = compose({}, { stat: stat('40%', 'Teatro hoy', 24) });

    const figure = (svg: SVGSVGElement) => Array.from(svg.querySelectorAll('text')).find((node) => node.textContent === '40%')!;
    expect(figure(whole.svg).hasAttribute('fill-opacity')).toBe(false);
    expect(figure(whole.svg).getAttribute('font-size')).toBe('24');
  });

  it('keeps the note of a table with small bases among the notes below it', () => {
    const { texts } = compose({}, { table: { headers: ['Comuna', 'Teatro hoy', 'n'], rows: [['COMUNA 7', '50% *', '16']] }, notes: ['* Base below 30.'] });

    expect(names(texts)).toEqual(expect.arrayContaining(['50% *', '* Base below 30.']));
  });
});
