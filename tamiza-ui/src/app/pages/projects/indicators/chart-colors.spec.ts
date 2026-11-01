import { ChartType, DashboardColors, PaletteId, ScaleId, ValueColor, WidgetConfig, chartTypes, emptyConfig, paletteIds, scaleIds } from '../../../core/projects/indicators.service';
import { SeriesKey, chartColors, colorsOf, defaultColors, defaultScale, paletteColor, rampGradient } from './chart-colors';

const pal = (palette: PaletteId, n: number) => `var(--tmz-pal-${palette}-${n})`;
const tamiza = (n: number) => pal('tamiza', n);
const other = 'var(--tmz-series-other)';

const value = (text: string): SeriesKey => ({ value: text, kind: 'value' });
const none: SeriesKey = { value: null, kind: 'none' };
const rest: SeriesKey = { value: null, kind: 'other' };

const dashboard = (palette: PaletteId, ...valueColors: ValueColor[]): DashboardColors => ({ palette, valueColors });
const kept = (column: string, text: string, color: number): ValueColor => ({ column, value: text, color });

/** A config with the settings that decide how a chart is colored. */
const widget = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({ ...emptyConfig('coverage'), groupBy: 'municipality', ...overrides });

describe('paletteColor', () => {
  it('names the custom property of a place in a palette', () => {
    expect(paletteColor('tamiza', 1)).toBe('var(--tmz-pal-tamiza-1)');
    expect(paletteColor('earth', 8)).toBe('var(--tmz-pal-earth-8)');
  });
});

describe('rampGradient', () => {
  it('runs through the seven steps of a scale, lightest first, as the scale\'s own custom properties', () => {
    expect(rampGradient('green')).toBe(`linear-gradient(to right, ${[1, 2, 3, 4, 5, 6, 7].map((step) => `var(--tmz-ramp-green-${step})`).join(', ')})`);
  });

  it('is different for each scale', () => {
    expect(new Set(scaleIds.map(rampGradient)).size).toBe(scaleIds.length);
  });
});

describe('the colors of a dashboard', () => {
  it('start as the palette Tamiza with no color kept by value', () => {
    expect(defaultColors).toEqual({ palette: 'tamiza', valueColors: [] });
  });

  it('read as those defaults from an API that predates palettes, and from nothing at all', () => {
    expect(colorsOf({})).toEqual(defaultColors);
    expect(colorsOf(null)).toEqual(defaultColors);
    expect(colorsOf(undefined)).toEqual(defaultColors);
  });

  it('read as saved otherwise', () => {
    const saved = dashboard('earth', kept('sex', 'Female', 5));

    expect(colorsOf(saved)).toEqual(saved);
    expect(colorsOf({ palette: 'vivid' })).toEqual({ palette: 'vivid', valueColors: [] });
  });

  it('read a palette this app does not know as Tamiza, which has the tokens', () => {
    expect(colorsOf({ palette: 'neon' as PaletteId, valueColors: [] }).palette).toBe('tamiza');
    expect(chartColors({ palette: 'neon' as PaletteId, valueColors: [] }, 'column', widget()).accent).toBe(tamiza(1));
  });
});

describe('the default scale of each palette', () => {
  it('is blue for Tamiza, teal for Institutional, orange for Earth and purple for Vivid', () => {
    expect(paletteIds.map((palette) => defaultScale(palette))).toEqual(['blue', 'teal', 'orange', 'purple']);
    for (const palette of paletteIds) {
      expect(scaleIds).toContain(defaultScale(palette));
    }
  });
});

describe('chartColors forValues', () => {
  const colors = (valueColors: ValueColor[] = [], palette: PaletteId = 'tamiza') => chartColors(dashboard(palette, ...valueColors), 'column', widget({ seriesBy: 'water_source' }));

  it('gives the values the palette\'s colors in order when none is kept', () => {
    expect(colors().forValues('water_source', [value('Piped'), value('River'), value('Well')])).toEqual([tamiza(1), tamiza(2), tamiza(3)]);
  });

  it('draws in the dashboard\'s palette', () => {
    expect(colors([], 'earth').forValues('water_source', [value('Piped'), value('River')])).toEqual([pal('earth', 1), pal('earth', 2)]);
  });

  it('sets the colors kept aside, so the other values take the remaining ones in order (Colors set aside)', () => {
    const answer = colors([kept('water_source', 'Well', 1)]).forValues('water_source', [value('Piped'), value('River'), value('Well')]);

    expect(answer).toEqual([tamiza(2), tamiza(3), tamiza(1)]);
  });

  it('keeps a color in the middle of the order out of the others\' way', () => {
    const answer = colors([kept('water_source', 'River', 3)]).forValues('water_source', [value('Piped'), value('River'), value('Well'), value('Rain')]);

    expect(answer).toEqual([tamiza(1), tamiza(3), tamiza(2), tamiza(4)]);
  });

  it('gives "Other" the neutral color whatever is kept (Other stays neutral)', () => {
    const seven = ['A', 'B', 'C', 'D', 'E', 'F', 'G'].map(value);

    expect(colors().forValues('water_source', [...seven, rest])).toEqual([1, 2, 3, 4, 5, 6, 7].map(tamiza).concat(other));
    const withKept = colors([kept('water_source', 'G', 8), kept('water_source', 'A', 2)]).forValues('water_source', [...seven, rest]);
    expect(withKept).toEqual([tamiza(2), tamiza(1), tamiza(3), tamiza(4), tamiza(5), tamiza(6), tamiza(8), other]);
    expect(withKept.at(-1)).toBe(other);
  });

  it('never uses the neutral of "Other" for a color kept: a kept color is one of the eight', () => {
    expect(colors([kept('water_source', 'A', 8)]).forValues('water_source', [value('A'), rest])).toEqual([tamiza(8), other]);
  });

  it('gives the same value the same color in any widget of the dashboard (Same value, same color)', () => {
    const dash = dashboard('tamiza', kept('sex', 'Female', 5));
    const stacked = chartColors(dash, 'column', widget({ groupBy: 'municipality', seriesBy: 'sex' })).forValues('sex', [value('Female'), value('Male')]);
    const pie = chartColors(dash, 'pie', widget({ groupBy: 'sex' })).forValues('sex', [value('Male'), value('Female'), value('Unknown')]);
    const pyramid = chartColors(dash, 'pyramid', widget({ groupBy: 'age_band', seriesBy: 'sex' })).forValues('sex', [value('Female'), value('Male')]);

    expect(stacked[0]).toBe(tamiza(5));
    expect(pie[1]).toBe(tamiza(5));
    expect(pyramid[0]).toBe(tamiza(5));
    // The others take what is left, in order: color 5 is set aside in each.
    expect(stacked[1]).toBe(tamiza(1));
    expect(pie.filter((_, i) => i !== 1)).toEqual([tamiza(1), tamiza(2)]);
  });

  it('colors the two sides of a pyramid from their own values (Pyramid sides)', () => {
    const dash = dashboard('tamiza', kept('sex', 'Female', 5), kept('sex', 'Male', 1));
    const left = chartColors(dash, 'pyramid', widget({ seriesBy: 'sex' })).forValues('sex', [value('Female'), value('Male')]);

    expect(left).toEqual([tamiza(5), tamiza(1)]);
  });

  it('does not change anything for a value that is not in the data (Value not in the data)', () => {
    const without = colors().forValues('water_source', [value('Piped'), value('River')]);
    const lake = colors([kept('water_source', 'Lake', 4)]).forValues('water_source', [value('Piped'), value('River')]);

    expect(lake).toEqual(without);
  });

  it('matches the text exactly, case included (Case differs)', () => {
    const answer = colors([kept('water_source', 'piped', 4)]).forValues('water_source', [value('Piped'), value('piped ')]);

    expect(answer).toEqual([tamiza(1), tamiza(2)]);
  });

  it('never gives a color kept to the missing value, whatever text it is kept for', () => {
    const answer = colors([kept('water_source', '(none)', 3), kept('water_source', '', 4)]).forValues('water_source', [value('Piped'), none, rest]);

    expect(answer).toEqual([tamiza(1), tamiza(2), other]);
  });

  it('lets "(none)" take the next free color in its place, as any value with no color kept', () => {
    const answer = colors([kept('water_source', 'Piped', 1)]).forValues('water_source', [value('Piped'), value('Well'), none]);

    expect(answer).toEqual([tamiza(1), tamiza(2), tamiza(3)]);
  });

  it('matches an empty text, which is a group value', () => {
    expect(colors([kept('water_source', '', 6)]).forValues('water_source', [value(''), value('Well')])).toEqual([tamiza(6), tamiza(1)]);
  });

  it('keeps colors by column: the same value of another column takes none', () => {
    const dash = dashboard('tamiza', kept('zone', 'Rural', 5));

    expect(chartColors(dash, 'column', widget({ seriesBy: 'sex' })).forValues('sex', [value('Rural'), value('Urban')])).toEqual([tamiza(1), tamiza(2)]);
    expect(chartColors(dash, 'column', widget({ seriesBy: 'zone' })).forValues('zone', [value('Urban'), value('Rural')])).toEqual([tamiza(1), tamiza(5)]);
  });

  it('keeps the same color for two columns (Same color in two columns)', () => {
    const dash = dashboard('tamiza', kept('sex', 'Female', 5), kept('zone', 'Rural', 5));
    const chart = chartColors(dash, 'column', widget({ seriesBy: 'sex' }));

    expect(chart.forValues('sex', [value('Female')])).toEqual([tamiza(5)]);
    expect(chart.forValues('zone', [value('Rural')])).toEqual([tamiza(5)]);
  });

  it('gives two values of one widget two colors, whatever the list holds', () => {
    // A column keeps each color once, so this only guards a list that breaks the rule: the first value keeps it.
    const answer = colors([kept('water_source', 'A', 2), kept('water_source', 'B', 2)]).forValues('water_source', [value('A'), value('B')]);

    expect(answer).toEqual([tamiza(2), tamiza(1)]);
    expect(new Set(answer).size).toBe(answer.length);
  });

  it('gives eight values eight colors, whichever are kept', () => {
    const eight = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H'].map(value);
    const answer = colors([kept('water_source', 'H', 1), kept('water_source', 'C', 8)]).forValues('water_source', eight);

    expect(new Set(answer).size).toBe(8);
    expect(answer[7]).toBe(tamiza(1));
    expect(answer[2]).toBe(tamiza(8));
  });

  it('ignores a color kept outside the palette', () => {
    expect(colors([kept('water_source', 'Piped', 9), kept('water_source', 'River', 0)]).forValues('water_source', [value('Piped'), value('River')])).toEqual([tamiza(1), tamiza(2)]);
  });

  it('answers an empty list with an empty list', () => {
    expect(colors().forValues('water_source', [])).toEqual([]);
  });
});

describe('chartColors for a chart with no series', () => {
  const dash = dashboard('tamiza', kept('municipality', 'Rionegro', 2));

  it('draws every bar in the widget\'s color, color 1 unless it has another (Widget saved before colors)', () => {
    expect(chartColors(dash, 'column', widget()).accent).toBe(tamiza(1));
    expect(chartColors(dash, 'column', widget({ display: { color: 3 } })).accent).toBe(tamiza(3));
    expect(chartColors(dash, 'column', widget({ display: {} })).accent).toBe(tamiza(1));
    expect(chartColors(dash, 'column', widget({ display: null })).accent).toBe(tamiza(1));
  });

  it('draws the color in the palette of its dashboard (Column chart in color 3, Palette changed)', () => {
    expect(chartColors(dashboard('vivid'), 'column', widget({ display: { color: 3 } })).accent).toBe(pal('vivid', 3));
    expect(chartColors(dashboard('earth'), 'histogram', widget({ display: { color: 8 } })).accent).toBe(pal('earth', 8));
  });

  it('reads a color outside the palette as unset', () => {
    for (const color of [0, 9, -1, 2.5, Number.NaN]) {
      expect(chartColors(dash, 'column', widget({ display: { color } })).accent).toBe(tamiza(1));
    }
  });

  it('highlights only the bars with a color kept, the others staying in the accent (One bar highlighted)', () => {
    const chart = chartColors(dash, 'bar', widget({ display: { color: 1 } }));

    expect(chart.kept('municipality', 'Rionegro')).toBe(tamiza(2));
    expect(chart.kept('municipality', 'Cali')).toBeNull();
    expect(chart.accent).toBe(tamiza(1));
  });

  it('never gives the next free color to a bar: a bar with no color kept is null, to be drawn in the accent', () => {
    const chart = chartColors(dashboard('tamiza'), 'column', widget());

    expect(['Cali', 'Pasto', 'Quibdó'].map((group) => chart.kept('municipality', group))).toEqual([null, null, null]);
  });

  it('keeps by the exact text and the column, and never for the missing group', () => {
    const chart = chartColors(dash, 'column', widget());

    expect(chart.kept('municipality', 'rionegro')).toBeNull();
    expect(chart.kept('region', 'Rionegro')).toBeNull();
    expect(chart.kept('municipality', null)).toBeNull();
  });

  it('keeps the same color a bar has in a pie of the same column', () => {
    const bar = chartColors(dash, 'column', widget()).kept('municipality', 'Rionegro');
    const pie = chartColors(dash, 'pie', widget()).forValues('municipality', [value('Rionegro')])[0];

    expect(bar).toBe(pie);
  });

  it('keeps a color for a bar or a column chart only, not for a line, a histogram or a chart that has a series', () => {
    const answers = Object.fromEntries(chartTypes.map((type) => [type, chartColors(dash, type, widget()).kept('municipality', 'Rionegro')]));

    expect(answers).toEqual({
      stat: null,
      bar: tamiza(2),
      column: tamiza(2),
      line: null,
      histogram: null,
      pie: null,
      map: null,
      heatmap: null,
      pyramid: null,
      likert: null,
      crosstab: null,
    });
    expect(chartColors(dash, 'column', widget({ seriesBy: 'water_source' })).kept('municipality', 'Rionegro')).toBeNull();
  });
});

describe('chartColors ramp', () => {
  const ramped = (palette: PaletteId, type: ChartType, display?: WidgetConfig['display']) => chartColors(dashboard(palette), type, widget({ display }));

  it('uses the scale of the palette when the widget names none (Palette\'s scale)', () => {
    expect(ramped('tamiza', 'heatmap').ramp).toBe('blue');
    expect(ramped('institutional', 'map').ramp).toBe('teal');
    expect(ramped('earth', 'heatmap').ramp).toBe('orange');
    expect(ramped('vivid', 'map').ramp).toBe('purple');
    expect(ramped('earth', 'heatmap', {}).ramp).toBe('orange');
    expect(ramped('earth', 'heatmap', null).ramp).toBe('orange');
  });

  it('uses the scale of the widget when it has one (Map on the green scale)', () => {
    for (const scale of scaleIds) {
      expect(ramped('earth', 'map', { ramp: scale }).ramp).toBe(scale);
    }
  });

  it('reads a scale this app does not know as unset', () => {
    expect(ramped('earth', 'map', { ramp: 'magenta' as ScaleId }).ramp).toBe('orange');
  });

  it('sets the seven steps and their inks that the marks read to the steps of the scale', () => {
    const style = ramped('earth', 'map', { ramp: 'green' }).rampStyle;

    expect(Object.keys(style)).toEqual([
      ...[1, 2, 3, 4, 5, 6, 7].flatMap((step) => [`--tmz-seq-${step}`, `--tmz-seq-ink-${step}`]),
    ]);
    for (let step = 1; step <= 7; step++) {
      expect(style[`--tmz-seq-${step}`]).toBe(`var(--tmz-ramp-green-${step})`);
      expect(style[`--tmz-seq-ink-${step}`]).toBe(`var(--tmz-ramp-green-ink-${step})`);
    }
  });

  it('sets the steps of the palette\'s scale when the widget names none', () => {
    const style = ramped('earth', 'heatmap').rampStyle;

    expect(style['--tmz-seq-1']).toBe('var(--tmz-ramp-orange-1)');
    expect(style['--tmz-seq-ink-7']).toBe('var(--tmz-ramp-orange-ink-7)');
  });
});

describe('every chart type', () => {
  it('gets colors from a config of its own, whatever it holds', () => {
    for (const type of chartTypes) {
      const chart = chartColors(defaultColors, type, emptyConfig());

      expect(chart.accent).toBe(tamiza(1));
      expect(chart.ramp).toBe('blue');
    }
  });
});
