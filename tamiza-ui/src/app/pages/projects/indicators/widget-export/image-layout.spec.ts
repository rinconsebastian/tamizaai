import { ExportParts } from '../export-parts';
import { ImageInput, ImageTheme, composeImage, wrap } from './image-layout';
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

function compose(overrides: Partial<ImageInput> = {}, parts: Partial<ExportParts> = {}) {
  const layout = composeImage({
    width: 400,
    title: 'Coverage by municipality',
    subtitle: null,
    footnote: null,
    notesAbove: [],
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

  it('draws a dashed swatch in the heading color and in the pattern of the line it stands for (Mean line in an image)', () => {
    const { svg, texts } = compose(
      {},
      {
        legend: {
          kind: 'entries',
          entries: [
            { label: 'Mean: 34.17% de los hogares', color: 'var(--c1)', swatch: 'dashed', dash: 0 },
            { label: 'Meta: 50%', color: 'var(--c1)', swatch: 'dashed', dash: 1 },
            { label: 'Third: 20%', color: 'var(--c1)', swatch: 'dashed', dash: 2 },
          ],
        },
      },
    );

    const dashed = Array.from(svg.querySelectorAll('line[stroke-dasharray]'));
    expect(dashed.map((line) => line.getAttribute('stroke-dasharray'))).toEqual(['6 4', '2 3', '8 3 2 3']);
    expect(dashed.every((line) => line.getAttribute('stroke') === '#111' && Number(line.getAttribute('x2')) - Number(line.getAttribute('x1')) === 14)).toBe(true);
    expect(names(texts)).toContain('Mean: 34.17% de los hogares');
  });

  it('draws the legend of the lines under the plot, above the footnote and the footer (Lines above the footnote)', () => {
    const plot = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    plot.setAttribute('width', '300');
    plot.setAttribute('height', '120');
    const { texts } = compose(
      { plot, footnote: 'No suman 100 %.', footer: ['Household survey'] },
      { legend: { kind: 'entries', entries: [{ label: 'Mean: 34.17%', color: 'var(--c1)', swatch: 'dashed', dash: 0 }] } },
    );

    const at = (text: string) => texts.find((t) => t.text === text)!.y;
    expect(at('Mean: 34.17%')).toBeGreaterThan(Number(plot.getAttribute('y')) + 120 - 1);
    expect(at('No suman 100 %.')).toBeGreaterThan(at('Mean: 34.17%'));
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
    const { texts } = compose({}, { stat: { value: '1,234', description: 'Households visited' } });

    expect(texts[1]).toMatchObject({ text: '1,234', size: 36 });
    expect(texts[2]).toMatchObject({ text: 'Households visited', size: 14 });
  });

  it('draws the figure of a stat tile alone when its description is null (Caption hidden)', () => {
    const { texts } = compose({}, { stat: { value: '1,234', description: null } });

    expect(names(texts)).toEqual(['Coverage by municipality', '1,234']);
    expect(texts[1]).toMatchObject({ text: '1,234', size: 36 });
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
