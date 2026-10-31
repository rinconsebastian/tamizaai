import { LikertStep, WidgetConfig, WidgetSort, WidgetTooltipItem, chartTypes, emptyConfig } from '../../../core/projects/indicators.service';
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
  };

  it('keeps, for each of the eleven kinds, exactly the settings of the second dimension that kind uses', () => {
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
