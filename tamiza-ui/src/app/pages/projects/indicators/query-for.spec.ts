import { WidgetConfig, WidgetSort, WidgetTooltipItem, emptyConfig } from '../../../core/projects/indicators.service';
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
