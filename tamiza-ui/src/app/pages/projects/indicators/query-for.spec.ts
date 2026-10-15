import { WidgetConfig, WidgetSort, emptyConfig } from '../../../core/projects/indicators.service';
import { queryFor } from './widget-view';

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
