import { WidgetConfig, WidgetDisplay, WidgetSort, chartTypes, emptyConfig } from '../../../core/projects/indicators.service';
import { ColumnMetadata, ColumnMetadataMap } from '../../../core/projects/pipeline.service';
import { CategoryOrders, OrderEntry, PlaceOrder, arrange, arrangeColumns, ordersFor, shown, shownValues, stripPrefix } from './category-order';
import { SeriesTable } from './series-table';
import fixture from './testing/category-orders.json';

// The same cases the API's CategoryOrderTests read (design D2): the rule that orders the values of a place and hides their numbers is
// worked out by hand in one file, and both sides must give the same lists.

type Entry = string | null | { kind: 'other' };

const entryOf = (entry: Entry): OrderEntry => (entry === null ? { value: null, kind: 'none' } : typeof entry === 'string' ? { value: entry, kind: 'value' } : { value: null, kind: 'other' });
const entryNode = (entry: OrderEntry): Entry => (entry.kind === 'none' ? null : entry.kind === 'other' ? { kind: 'other' } : entry.value);

describe('the shared cases of the declared category order', () => {
  it('lists the cases the design names', () => {
    expect(fixture.arrange).toHaveLength(24);
    expect(fixture.shown).toHaveLength(8);
    expect(fixture.prefixes).toHaveLength(29);
  });

  it.each(fixture.arrange.map((c) => [c.name, c] as const))('arranges as worked out by hand: %s', (_name, c) => {
    const declared: PlaceOrder | null = c.declared ? { order: c.declared.order, last: c.declared.last, sorted: c.declared.sorted } : null;

    const arranged = arrange((c.entries as Entry[]).map(entryOf), (entry) => entry, declared);

    expect(arranged.map(entryNode)).toEqual(c.expected);
  });

  it.each(fixture.shown.map((c) => [c.name, c] as const))('shows the values as worked out by hand: %s', (_name, c) => {
    const map = shownValues(c.values, c.strip);

    expect(c.values.map((value) => shown(map, value))).toEqual(c.expected);
  });

  it.each(fixture.prefixes.map((c) => [JSON.stringify(c.value), c] as const))('hides a leading number only when the rule says so: %s', (_name, c) => {
    expect(stripPrefix(c.value)).toBe(c.expected);
  });

  it('gives back the same list, not the same array, when nothing applies', () => {
    const entries = ['B', 'A'].map(entryOf);

    expect(arrange(entries, (entry) => entry, null)).not.toBe(entries);
    expect(arrange(entries, (entry) => entry, { order: [], last: [], sorted: false })).toEqual(entries);
  });
});

describe('what a widget declares', () => {
  const answers = ['1. Sí', '2. Tal vez', '3. No'];
  const meta = (entries: Record<string, ColumnMetadata>): ColumnMetadataMap => entries;
  const widget = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({ ...emptyConfig('teatro_hoy'), groupBy: 'comuna', ...overrides });
  const byValue: WidgetSort = { by: 'value', column: null, compareAs: null, direction: 'desc' };
  const byOrden: WidgetSort = { by: 'column', column: 'orden', compareAs: 'text', direction: 'asc' };

  it('is nothing when there is no chart type, no metadata or only labels', () => {
    const labelled = meta({ comuna: { label: 'Comuna' }, respuesta: { label: 'Respuesta' } });

    expect(ordersFor(null, widget(), meta({ comuna: { order: ['a'] } }))).toBeNull();
    expect(ordersFor('column', widget(), null)).toBeNull();
    expect(ordersFor('column', widget(), undefined)).toBeNull();
    expect(ordersFor('column', widget(), {})).toBeNull();
    for (const type of chartTypes) {
      expect(ordersFor(type, widget({ seriesBy: 'respuesta' }), labelled), type).toBeNull();
    }
  });

  it('is nothing for a column the widget does not show', () => {
    expect(ordersFor('column', widget({ seriesBy: 'respuesta' }), meta({ otra: { order: ['a'], last: ['b'], stripPrefix: true } }))).toBeNull();
  });

  it('gives the groups of a bar the indicator\'s order and values kept last', () => {
    const orders = ordersFor('bar', widget(), meta({ comuna: { order: answers, last: ['Sin dato'] } }))!;

    expect(orders.groups).toEqual({ order: answers, last: ['Sin dato'], sorted: false });
    expect(orders.series).toBeNull();
    expect(orders.hideGroupPrefixes).toBe(false);
  });

  it('lets a widget\'s own order replace the indicator\'s order, never its values kept last', () => {
    const display: WidgetDisplay = { order: { series: ['4. No sabe', '1. Sí'] } };

    const orders = ordersFor('column', widget({ seriesBy: 'respuesta', display }), meta({ respuesta: { order: answers, last: ['4. No sabe'] } }))!;

    expect(orders.series).toEqual({ order: ['4. No sabe', '1. Sí'], last: ['4. No sabe'], sorted: false });
    expect(orders.groups).toBeNull();
  });

  it('reads an empty own order as none, so the indicator\'s applies', () => {
    const orders = ordersFor('column', widget({ display: { order: { group: [] } } }), meta({ comuna: { order: answers } }))!;

    expect(orders.groups!.order).toEqual(answers);
  });

  it.each(['bar', 'column'] as const)('keeps only the values kept last of a sorted %s', (type) => {
    const columns = meta({ comuna: { order: answers, last: ['Otro'] } });

    expect(ordersFor(type, widget({ sort: byValue }), columns)!.groups).toEqual({ order: answers, last: ['Otro'], sorted: true });
    expect(ordersFor(type, widget({ sort: byOrden }), columns)!.groups!.sorted).toBe(true);
  });

  it('declares nothing for the groups of a sort with only an order', () => {
    expect(ordersFor('column', widget({ sort: byValue }), meta({ comuna: { order: answers } }))).toBeNull();
  });

  it('does not touch the series with a sort', () => {
    const orders = ordersFor('bar', widget({ seriesBy: 'respuesta', sort: byValue }), meta({ respuesta: { order: answers } }))!;

    expect(orders.groups).toBeNull();
    expect(orders.series).toEqual({ order: answers, last: [], sorted: false });
  });

  it('sorts a box plot only by median and with a group column', () => {
    const columns = meta({ vehiculo: { order: ['Carro', 'Moto'], last: ['Otro'] } });

    expect(ordersFor('boxplot', widget({ groupBy: 'vehiculo' }), columns)!.groups).toEqual({ order: ['Carro', 'Moto'], last: ['Otro'], sorted: false });
    expect(ordersFor('boxplot', widget({ groupBy: 'vehiculo', sort: byValue }), columns)!.groups).toEqual({ order: ['Carro', 'Moto'], last: ['Otro'], sorted: true });
    expect(ordersFor('boxplot', widget({ groupBy: null, sort: byValue }), columns)).toBeNull();
  });

  it.each(['line', 'heatmap', 'crosstab', 'bar', 'column'] as const)('arranges both groups and series of a %s', (type) => {
    const columns = meta({ comuna: { last: ['Sin dato'] }, respuesta: { order: answers, stripPrefix: true } });

    const orders = ordersFor(type, widget({ seriesBy: 'respuesta' }), columns)!;

    expect(orders.groups!.last).toEqual(['Sin dato']);
    expect(orders.series!.order).toEqual(answers);
    expect(orders.hideGroupPrefixes).toBe(false);
    expect(orders.hideSeriesPrefixes).toBe(true);
  });

  it.each(['pyramid', 'likert'] as const)('leaves the sides of a pyramid and the scale of a Likert widget where they are, but hides their numbers (%s)', (type) => {
    const columns = meta({ comuna: { order: ['B', 'A'] }, respuesta: { order: answers, stripPrefix: true } });

    const orders = ordersFor(type, widget({ seriesBy: 'respuesta' }), columns)!;

    expect(orders.groups!.order).toEqual(['B', 'A']);
    expect(orders.series).toBeNull();
    expect(orders.hideSeriesPrefixes).toBe(true);
  });

  it('arranges the groups of a pie and has no series', () => {
    const columns = meta({ comuna: { order: ['B', 'A'], stripPrefix: true }, respuesta: { order: answers, stripPrefix: true } });

    const orders = ordersFor('pie', widget({ seriesBy: 'respuesta' }), columns)!;

    expect(orders.groups!.order).toEqual(['B', 'A']);
    expect(orders.hideGroupPrefixes).toBe(true);
    expect(orders.series).toBeNull();
    expect(orders.hideSeriesPrefixes).toBe(false);
  });

  it.each(['stat', 'histogram'] as const)('has no groups to order or to show on a %s', (type) => {
    expect(ordersFor(type, widget(), meta({ comuna: { order: ['B', 'A'], last: ['C'], stripPrefix: true } }))).toBeNull();
  });

  it('shows the groups of a map without their numbers but does not order them', () => {
    const orders = ordersFor('map', widget(), meta({ comuna: { order: ['B', 'A'], last: ['C'], stripPrefix: true } }))!;

    expect(orders).toEqual({ groups: null, series: null, hideGroupPrefixes: true, hideSeriesPrefixes: false });
  });

  it('declares no order when prefixes are the only thing hidden', () => {
    expect(ordersFor('column', widget(), meta({ comuna: { stripPrefix: true } }))).toEqual({ groups: null, series: null, hideGroupPrefixes: true, hideSeriesPrefixes: false });
  });
});

describe('the grid of a widget with a series, arranged', () => {
  const grid = (withShares = true): SeriesTable => ({
    columns: [
      { series: '4. No sabe', kind: 'value', total: 40, rows: 4 },
      { series: '1. Sí', kind: 'value', total: 10, rows: 1 },
      { series: null, kind: 'none', total: 5, rows: 1 },
      { series: '2. No', kind: 'value', total: 20, rows: 2 },
      { series: null, kind: 'other', total: 7, rows: 1 },
    ],
    rows: [
      { group: 'Sin dato', total: 11, rows: 3, cells: [1, 2, 3, 4, 5], shares: withShares ? [0.1, 0.2, 0.3, 0.4, 0.5] : null },
      { group: 'COMUNA 1', total: 22, rows: 3, cells: [10, null, 30, 40, 50], shares: withShares ? [0.01, null, 0.03, 0.04, 0.05] : null },
      { group: null, total: 33, rows: 3, cells: [100, 200, 300, 400, 500], shares: withShares ? [0.9, 0.8, 0.7, 0.6, 0.5] : null },
      { group: 'COMUNA 2', total: 44, rows: 3, cells: [7, 8, 9, 10, 11], shares: withShares ? [0.7, 0.8, 0.9, 0.1, 0.11] : null },
    ],
    grandTotal: 66,
    grandRows: 12,
    negativeLeftOut: 0,
    leftOut: 0,
    rowLimitReached: false,
  });
  const place = (order: string[], last: string[], sorted = false): PlaceOrder => ({ order, last, sorted });
  const orders = (groups: PlaceOrder | null, series: PlaceOrder | null): CategoryOrders => ({ groups, series, hideGroupPrefixes: false, hideSeriesPrefixes: false });

  it('is returned as it is when no place is declared', () => {
    const table = grid();

    expect(arrangeColumns(table, null)).toBe(table);
    expect(arrangeColumns(table, { groups: null, series: null, hideGroupPrefixes: true, hideSeriesPrefixes: true })).toBe(table);
  });

  it('keeps a column\'s cells, shares and total with it when the series are arranged', () => {
    const arranged = arrangeColumns(grid(), orders(null, place(['1. Sí', '2. No'], ['4. No sabe'])));

    expect(arranged.columns.map((c) => c.series)).toEqual(['1. Sí', '2. No', '4. No sabe', null, null]);
    expect(arranged.columns.map((c) => c.kind)).toEqual(['value', 'value', 'value', 'none', 'other']);
    expect(arranged.columns.map((c) => c.total)).toEqual([10, 20, 40, 5, 7]);
    expect(arranged.rows.map((r) => r.group)).toEqual(['Sin dato', 'COMUNA 1', null, 'COMUNA 2']);
    expect(arranged.rows[0].cells).toEqual([2, 4, 1, 3, 5]);
    expect(arranged.rows[1].cells).toEqual([null, 40, 10, 30, 50]);
    expect(arranged.rows[0].shares).toEqual([0.2, 0.4, 0.1, 0.3, 0.5]);
    expect(arranged.rows[1].shares).toEqual([null, 0.04, 0.01, 0.03, 0.05]);
    expect(arranged.rows.map((r) => r.total)).toEqual([11, 22, 33, 44]);
    expect(arranged.grandTotal).toBe(66);
  });

  it('keeps a row\'s cells and total with it when the groups are arranged, and none goes last', () => {
    const arranged = arrangeColumns(grid(), orders(place(['COMUNA 1', 'COMUNA 2'], ['Sin dato']), null));

    expect(arranged.rows.map((r) => r.group)).toEqual(['COMUNA 1', 'COMUNA 2', 'Sin dato', null]);
    expect(arranged.rows.map((r) => r.total)).toEqual([22, 44, 11, 33]);
    expect(arranged.rows[0].cells).toEqual([10, null, 30, 40, 50]);
    expect(arranged.rows[3].cells).toEqual([100, 200, 300, 400, 500]);
    expect(arranged.columns).toEqual(grid().columns);
  });

  it('keeps no shares where there were none, and a sorted place moves only the values kept last', () => {
    const arranged = arrangeColumns(grid(false), orders(place(['COMUNA 2'], ['COMUNA 1'], true), null));

    expect(arranged.rows.every((row) => row.shares === null)).toBe(true);
    expect(arranged.rows.map((r) => r.group)).toEqual(['Sin dato', 'COMUNA 2', 'COMUNA 1', null]);
  });

  it('keeps every cell with its column and its row, whatever the declaration', () => {
    const table = grid();
    const arranged = arrangeColumns(table, orders(place(['COMUNA 2', 'Sin dato'], ['COMUNA 1']), place(['2. No', '1. Sí'], ['4. No sabe'])));
    const cellOf = (t: SeriesTable, group: string | null, series: string | null, kind: string) =>
      t.rows.find((r) => r.group === group)!.cells[t.columns.findIndex((c) => c.series === series && c.kind === kind)];

    for (const row of table.rows) {
      for (const column of table.columns) {
        expect(cellOf(arranged, row.group, column.series, column.kind)).toBe(cellOf(table, row.group, column.series, column.kind));
      }
    }
    expect(arranged.rows).toHaveLength(table.rows.length);
    expect(arranged.columns).toHaveLength(table.columns.length);
  });
});
