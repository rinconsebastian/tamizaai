import { LikertStep, WidgetConfig, WidgetDisplay, WidgetSort, WidgetTooltipItem, chartTypes, emptyConfig } from '../../../core/projects/indicators.service';
import { queryFor, savedConfig } from './widget-view';

const byColumn: WidgetSort = { by: 'column', column: 'round', compareAs: 'number', direction: 'asc' };
const byValue: WidgetSort = { by: 'value', column: null, compareAs: null, direction: 'desc' };

function config(sort: WidgetSort | null): WidgetConfig {
  return { ...emptyConfig('coverage'), groupBy: 'municipality', geometryColumn: 'geometry', buckets: 20, sort };
}

describe('queryFor sort', () => {
  it('keeps the order of a bar and of a column chart', () => {
    expect(queryFor('bar', config(byColumn)).sort).toEqual(byColumn);
    expect(queryFor('column', config(byColumn)).sort).toEqual(byColumn);
  });

  it('sets the order to null, not leaves it out, for every other chart type', () => {
    for (const type of ['stat', 'line', 'histogram', 'pie', 'map'] as const) {
      const query = queryFor(type, config(byColumn));
      expect(query.sort).toBeNull();
      expect('sort' in query).toBe(true);
    }
  });

  it('does not send a column or a comparison when sorting by the plotted value', () => {
    const sort: WidgetSort = { by: 'value', column: 'round', compareAs: 'text', direction: 'desc' };

    expect(queryFor('column', config(sort)).sort).toEqual(byValue);
  });

  it('keeps no order as no order', () => {
    expect(queryFor('column', config(null)).sort).toBeNull();
  });
});

const items: WidgetTooltipItem[] = [
  { column: 'address', label: 'Dirección', format: null },
  { column: 'sample_target', label: null, format: 'count' },
  { column: 'progress', label: 'Avance', format: 'percent' },
];

function mapConfig(): WidgetConfig {
  return { ...config(null), tooltip: items, basemap: 'satellite' };
}

describe('queryFor tooltip and basemap', () => {
  it('sends the names of the tooltip columns of a map, and never their labels or formats', () => {
    expect(queryFor('map', mapConfig()).tooltip).toEqual([
      { column: 'address', label: null, format: null },
      { column: 'sample_target', label: null, format: null },
      { column: 'progress', label: null, format: null },
    ]);
  });

  it('gives the same query whatever the labels and formats are, so editing them never queries again', () => {
    const relabelled = { ...mapConfig(), tooltip: items.map((item) => ({ ...item, label: 'Otro', format: null })) };

    expect(JSON.stringify(queryFor('map', relabelled))).toBe(JSON.stringify(queryFor('map', mapConfig())));
  });

  it('sends the basemap as null, and the same query whatever the basemap, so choosing one never queries again', () => {
    for (const basemap of [null, 'streets', 'satellite'] as const) {
      const query = queryFor('map', { ...mapConfig(), basemap });
      expect(query.basemap).toBeNull();
      expect('basemap' in query).toBe(true);
    }
    expect(JSON.stringify(queryFor('map', { ...mapConfig(), basemap: null }))).toBe(JSON.stringify(queryFor('map', mapConfig())));
  });

  it('sets the tooltip columns to an empty list, not leaves them out, for every other chart type', () => {
    for (const type of ['stat', 'bar', 'column', 'line', 'histogram', 'pie'] as const) {
      const query = queryFor(type, mapConfig());
      expect(query.tooltip).toEqual([]);
      expect('tooltip' in query).toBe(true);
      expect(query.basemap).toBeNull();
    }
  });

  it('keeps the order of the columns', () => {
    const reordered = { ...mapConfig(), tooltip: [...items].reverse() };

    expect(queryFor('map', reordered).tooltip.map((item) => item.column)).toEqual(['progress', 'sample_target', 'address']);
  });
});

describe('savedConfig', () => {
  it('keeps the whole tooltip items and the basemap of a map, besides the query', () => {
    const saved = savedConfig('map', mapConfig());

    expect(saved.tooltip).toEqual(items);
    expect(saved.basemap).toBe('satellite');
    expect({ ...saved, tooltip: [], basemap: null }).toEqual({ ...queryFor('map', mapConfig()), tooltip: [], basemap: null });
  });

  it('saves a map with no basemap as null', () => {
    expect(savedConfig('map', { ...mapConfig(), basemap: null }).basemap).toBeNull();
  });

  it('saves no tooltip columns and no basemap for any other chart type, as a map turned into a bar is', () => {
    for (const type of ['stat', 'bar', 'column', 'line', 'histogram', 'pie'] as const) {
      const saved = savedConfig(type, mapConfig());
      expect(saved.tooltip).toEqual([]);
      expect(saved.basemap).toBeNull();
    }
  });

  it('is the same for the query whatever the tooltip items and the basemap are', () => {
    const saved = savedConfig('map', mapConfig());

    expect(JSON.stringify(queryFor('map', saved))).toBe(JSON.stringify(queryFor('map', mapConfig())));
  });
});

describe('the dashboard filters a widget ignores', () => {
  const ignoring = (): WidgetConfig => ({ ...emptyConfig('coverage'), groupBy: 'municipality', ignoredDashboardFilters: ['municipality', 'round'] });

  it('are saved with the widget, whatever its chart type', () => {
    for (const type of ['stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map'] as const) {
      expect(savedConfig(type, ignoring()).ignoredDashboardFilters).toEqual(['municipality', 'round']);
    }
  });

  it('are not part of the query, so changing them never queries the widget again', () => {
    expect(queryFor('column', ignoring()).ignoredDashboardFilters).toEqual([]);
    expect(JSON.stringify(queryFor('column', ignoring()))).toBe(JSON.stringify(queryFor('column', { ...ignoring(), ignoredDashboardFilters: [] })));
  });

  it('read as none from a config that predates them', () => {
    const old = { ...emptyConfig('coverage') } as Partial<WidgetConfig>;
    delete old.ignoredDashboardFilters;

    expect(savedConfig('column', old as WidgetConfig).ignoredDashboardFilters).toEqual([]);
  });
});

describe('queryFor and savedConfig with a series (design D1, D7)', () => {
  const scale: LikertStep[] = [
    { value: 'Disagree', side: 'negative' },
    { value: 'Neither', side: 'neutral' },
    { value: 'Agree', side: 'positive' },
  ];
  /** A config with every setting of the second dimension filled in, as a draft that was switched between kinds would hold. */
  const full = (): WidgetConfig => ({ ...config(null), seriesBy: 'water_source', layout: 'stacked', pyramid: { left: 'Female', right: 'Male' }, scale });
  const keys = ['seriesBy', 'layout', 'pyramid', 'scale'] as const;
  /** Which of the four settings each kind keeps. */
  const kept: Record<(typeof chartTypes)[number], readonly (typeof keys)[number][]> = {
    stat: [],
    bar: ['seriesBy', 'layout'],
    column: ['seriesBy', 'layout'],
    line: ['seriesBy'],
    histogram: [],
    pie: [],
    map: [],
    heatmap: ['seriesBy'],
    pyramid: ['seriesBy', 'pyramid'],
    likert: ['seriesBy', 'scale'],
    crosstab: ['seriesBy'],
    boxplot: [],
  };

  it('keeps, for each of the twelve kinds, exactly the settings of the second dimension that kind uses', () => {
    expect(Object.keys(kept)).toEqual([...chartTypes]);
    for (const type of chartTypes) {
      const query = queryFor(type, full());
      expect(keys.filter((key) => key in query)).toEqual(kept[type]);
      for (const key of kept[type]) {
        expect(query[key]).toEqual(full()[key]);
      }
    }
  });

  it('leaves the four settings out, not null, of a widget without a series, so it queries exactly as before', () => {
    for (const type of chartTypes) {
      const query = queryFor(type, config(null));
      expect(keys.filter((key) => key in query)).toEqual([]);
      expect(JSON.stringify(query)).toBe(JSON.stringify({ ...query }));
    }
    expect(Object.keys(queryFor('column', config(null))).sort()).toEqual(Object.keys(emptyConfig()).sort());
  });

  it('leaves out an empty series column, an unset layout and an empty scale', () => {
    const query = queryFor('column', { ...config(null), seriesBy: '', layout: null, pyramid: null, scale: [] });

    expect(keys.filter((key) => key in query)).toEqual([]);
    expect(keys.filter((key) => key in queryFor('likert', { ...config(null), scale: [] }))).toEqual([]);
  });

  it('is the same query whatever the layout of a bar chart, but not the same as a chart without the series', () => {
    const stacked = JSON.stringify(queryFor('bar', { ...full(), layout: 'stacked' }));

    expect(stacked).not.toBe(JSON.stringify(queryFor('bar', { ...full(), layout: 'percent' })));
    expect(stacked).not.toBe(JSON.stringify(queryFor('bar', config(null))));
  });

  it('saves the same settings as it queries, and the tooltip and basemap only for a map', () => {
    for (const type of chartTypes) {
      const saved = savedConfig(type, full());
      expect(keys.filter((key) => key in saved)).toEqual(kept[type]);
      expect(saved.tooltip).toEqual([]);
    }
  });
});

describe('queryFor and savedConfig with a display (add-dashboard-palettes, design D3)', () => {
  /** A config with both fields of the widget's display filled in, as a draft that was switched between kinds would hold. */
  const displayed = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({ ...config(null), display: { color: 3, ramp: 'green' }, ...overrides });
  /** What each kind keeps of the display: a color for bar, column and line with no series, a histogram and a box plot, a scale for a map and a heat map, an orientation for a box plot. */
  const kept: Record<(typeof chartTypes)[number], WidgetDisplay | undefined> = {
    stat: undefined,
    bar: { color: 3 },
    column: { color: 3 },
    line: { color: 3 },
    histogram: { color: 3 },
    pie: undefined,
    map: { ramp: 'green' },
    heatmap: { ramp: 'green' },
    pyramid: undefined,
    likert: undefined,
    crosstab: undefined,
    boxplot: { color: 3 },
  };

  it('leaves the display out of the query of every kind, so choosing a color never queries again', () => {
    expect(Object.keys(kept)).toEqual([...chartTypes]);
    for (const type of chartTypes) {
      const query = queryFor(type, displayed({ seriesBy: type === 'heatmap' ? 'water_source' : undefined }));
      expect('display' in query).toBe(false);
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, { ...displayed({ seriesBy: type === 'heatmap' ? 'water_source' : undefined }), display: { color: 7, ramp: 'gray' } })));
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, { ...displayed({ seriesBy: type === 'heatmap' ? 'water_source' : undefined }), display: undefined })));
    }
  });

  it('keeps, for each of the twelve kinds, exactly the fields of the display that kind takes', () => {
    for (const type of chartTypes) {
      const saved = savedConfig(type, displayed());
      expect(saved.display).toEqual(kept[type]);
      expect('display' in saved).toBe(kept[type] !== undefined);
    }
  });

  it('saves no color for a bar, column or line chart that has a series', () => {
    for (const type of ['bar', 'column', 'line'] as const) {
      const saved = savedConfig(type, displayed({ seriesBy: 'water_source' }));
      expect(saved.seriesBy).toBe('water_source');
      expect('display' in saved).toBe(false);
    }
  });

  it('keeps the color again when the series is cleared', () => {
    expect(savedConfig('column', displayed({ seriesBy: '' })).display).toEqual({ color: 3 });
    expect(savedConfig('column', displayed({ seriesBy: null })).display).toEqual({ color: 3 });
  });

  it('keeps the scale of a heat map, which always has a series', () => {
    expect(savedConfig('heatmap', displayed({ seriesBy: 'water_source' })).display).toEqual({ ramp: 'green' });
  });

  it('keeps the color 1 a member chose, which is not the same as no color', () => {
    expect(savedConfig('column', displayed({ display: { color: 1 } })).display).toEqual({ color: 1 });
  });

  it('saves no display at all, not an empty one, when there is nothing to keep (a widget saved before palettes)', () => {
    for (const type of chartTypes) {
      for (const display of [undefined, null, {}, { color: undefined }]) {
        const saved = savedConfig(type, { ...config(null), display });
        expect('display' in saved).toBe(false);
      }
    }
    expect(Object.keys(savedConfig('column', config(null))).sort()).toEqual(Object.keys(emptyConfig()).sort());
  });

  it('saves the same config as before palettes for a widget saved before them', () => {
    const before = config(null);

    for (const type of chartTypes) {
      const withoutDisplay = JSON.stringify(savedConfig(type, before));
      expect(withoutDisplay).toBe(JSON.stringify(savedConfig(type, { ...before, display: undefined })));
      expect(withoutDisplay).not.toContain('display');
    }
  });
});

describe('queryFor and savedConfig for a box plot (add-boxplot-widget, design D7)', () => {
  const boxed = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({
    ...emptyConfig('household_income'),
    groupBy: 'municipality',
    seriesBy: 'sex',
    sort: byValue,
    display: { color: 3, ramp: 'green', orientation: 'horizontal' },
    ...overrides,
  });

  it('sends an order by median, with a group column, and no series', () => {
    const query = queryFor('boxplot', boxed());

    expect(query.sort).toEqual(byValue);
    expect(query.groupBy).toBe('municipality');
    expect('seriesBy' in query).toBe(false);
    expect('display' in query).toBe(false);
  });

  it('sends no order that is by a column, and none without a group column, so the query is not refused for the form\'s choice', () => {
    expect(queryFor('boxplot', boxed({ sort: byColumn })).sort).toBeNull();
    expect(queryFor('boxplot', boxed({ groupBy: null })).sort).toBeNull();
    expect(queryFor('boxplot', boxed({ sort: null })).sort).toBeNull();
    expect(savedConfig('boxplot', boxed({ groupBy: null })).sort).toBeNull();
  });

  it('does not send a column or a comparison with an order by median', () => {
    const sort: WidgetSort = { by: 'value', column: 'round', compareAs: 'text', direction: 'asc' };

    expect(queryFor('boxplot', boxed({ sort })).sort).toEqual({ by: 'value', column: null, compareAs: null, direction: 'asc' });
  });

  it('saves its color and its orientation, and no scale', () => {
    expect(savedConfig('boxplot', boxed()).display).toEqual({ color: 3, orientation: 'horizontal' });
    expect(savedConfig('boxplot', boxed({ display: { orientation: 'vertical' } })).display).toEqual({ orientation: 'vertical' });
    expect('display' in savedConfig('boxplot', boxed({ display: undefined }))).toBe(false);
  });

  it('is the only kind that saves an orientation, so changing the kind away drops it', () => {
    for (const type of chartTypes.filter((t) => t !== 'boxplot')) {
      const saved = savedConfig(type, boxed({ seriesBy: type === 'heatmap' || type === 'pyramid' || type === 'likert' || type === 'crosstab' ? 'sex' : null }));
      expect(saved.display?.orientation, type).toBeUndefined();
    }
  });

  it('sends the same query whatever its orientation, so switching it never queries again', () => {
    const horizontal = JSON.stringify(queryFor('boxplot', boxed()));

    expect(JSON.stringify(queryFor('boxplot', boxed({ display: { orientation: 'vertical' } })))).toBe(horizontal);
    expect(JSON.stringify(queryFor('boxplot', boxed({ display: undefined })))).toBe(horizontal);
  });
});
