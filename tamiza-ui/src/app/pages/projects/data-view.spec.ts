import { DataColumn, DataQuery } from '../../core/projects/data.service';
import { decodeFilters, emptyQuery, encodeFilters, operationsFor, parseView, sameQuery, sanitize, viewParams } from './data-view';

const columns: DataColumn[] = [
  { name: 'name', type: 'text', label: null },
  { name: 'age', type: 'integer', label: 'Age' },
  { name: 'consent', type: 'boolean', label: null },
  { name: 'at', type: 'timestamp', label: null },
];

describe('data view in the address', () => {
  it('leaves defaults out of the address', () => {
    expect(viewParams('output', 'persons', emptyQuery())).toEqual({
      source: 'output',
      table: 'persons',
      sort: null,
      page: null,
      size: null,
      f: null,
    });
  });

  it('round-trips source, table, sort, page, size and filters', () => {
    const query: DataQuery = {
      filters: [
        { column: 'name', op: 'in', values: ['Cali', 'Bogotá, D.C.', '日本語 😀'] },
        { column: 'age', op: 'range', min: '18', max: '29' },
        { column: 'at', op: 'empty' },
      ],
      sort: { column: 'age', direction: 'desc' },
      page: 3,
      pageSize: 100,
    };

    const params = viewParams('clean', 'main', query);
    const view = parseView(params);

    expect(view).toEqual({ source: 'clean', table: 'main', query });
    expect(params['f']).toMatch(/^[A-Za-z0-9_-]+$/); // safe in a URL without escaping
  });

  it('falls back to the defaults for malformed parameters', () => {
    const view = parseView({ source: 'raw', table: '', sort: 'age', page: 'x', size: '7', f: '%%%not-base64' });

    expect(view).toEqual({ source: null, table: null, query: emptyQuery() });
  });

  it.each(['0', '-2', '1.5', 'NaN', ''])('ignores the page %j', (page) => {
    expect(parseView({ page }).query.page).toBe(1);
  });

  it('accepts only the three page sizes', () => {
    expect(parseView({ size: '25' }).query.pageSize).toBe(25);
    expect(parseView({ size: '100' }).query.pageSize).toBe(100);
    expect(parseView({ size: '200' }).query.pageSize).toBe(50);
  });

  it('reads a sort column that holds a colon', () => {
    expect(parseView({ sort: 'a:b:desc' }).query.sort).toEqual({ column: 'a:b', direction: 'desc' });
    expect(parseView({ sort: ':asc' }).query.sort).toBeNull();
    expect(parseView({ sort: 'age:sideways' }).query.sort).toBeNull();
  });

  it('writes a sort column that holds a colon so it reads back', () => {
    const query = { ...emptyQuery(), sort: { column: 'a:b', direction: 'asc' as const } };

    expect(parseView(viewParams(null, null, query)).query.sort).toEqual({ column: 'a:b', direction: 'asc' });
  });

  describe('filters', () => {
    it('ignores filters that are not a list of known operations', () => {
      const encode = (value: unknown) => encodeFilters(value as never);

      expect(decodeFilters(encode({ column: 'a', op: 'equals' }))).toEqual([]);
      expect(decodeFilters(encode([{ column: 'a', op: 'startsWith' }, { column: 3, op: 'equals' }, null, 'x']))).toEqual([]);
      expect(decodeFilters(encode([{ column: 'a', op: 'equals', value: 'x' }]))).toEqual([{ column: 'a', op: 'equals', value: 'x' }]);
    });

    it('keeps only the fields a filter uses, with the right shapes', () => {
      const encoded = encodeFilters([{ column: 'a', op: 'in', values: ['x', 5 as never, 'y'], value: 7 as never, evil: '<script>' } as never]);

      expect(decodeFilters(encoded)).toEqual([{ column: 'a', op: 'in', values: ['x', 'y'] }]);
    });

    it('reads at most 50 filters', () => {
      const many = Array.from({ length: 80 }, (_, i) => ({ column: `c${i}`, op: 'empty' as const }));

      expect(decodeFilters(encodeFilters(many))).toHaveLength(50);
    });

    it('gives nothing for an empty or broken value', () => {
      expect(decodeFilters(null)).toEqual([]);
      expect(decodeFilters('')).toEqual([]);
      expect(decodeFilters('!!!')).toEqual([]);
      expect(decodeFilters(btoa('not json'))).toEqual([]);
    });
  });

  describe('sanitize', () => {
    it('drops filters and sorts on columns the table does not have, and says which', () => {
      const query: DataQuery = {
        filters: [
          { column: 'age', op: 'range', min: '1' },
          { column: 'salary', op: 'range', min: '1' },
        ],
        sort: { column: 'salary', direction: 'asc' },
        page: 2,
        pageSize: 25,
      };

      const result = sanitize(query, columns);

      expect(result.query.filters).toEqual([{ column: 'age', op: 'range', min: '1' }]);
      expect(result.query.sort).toBeNull();
      expect(result.query.page).toBe(2);
      expect(result.dropped).toEqual(['filter:salary', 'sort:salary']);
    });

    it('drops a filter the columns type does not support, and a second filter on one column', () => {
      const query: DataQuery = {
        filters: [
          { column: 'age', op: 'contains', value: 'x' },
          { column: 'name', op: 'equals', value: 'a' },
          { column: 'name', op: 'contains', value: 'b' },
        ],
        sort: null,
        page: 1,
        pageSize: 50,
      };

      const result = sanitize(query, columns);

      expect(result.query.filters).toEqual([{ column: 'name', op: 'equals', value: 'a' }]);
      expect(result.dropped).toEqual(['filter:age', 'filter:name']);
    });

    it('keeps a valid query as it is', () => {
      const query: DataQuery = { filters: [{ column: 'consent', op: 'in', values: ['true'] }], sort: { column: 'age', direction: 'asc' }, page: 1, pageSize: 50 };

      const result = sanitize(query, columns);

      expect(result.query).toEqual(query);
      expect(result.dropped).toEqual([]);
    });
  });

  it('offers the operations each type supports', () => {
    expect(operationsFor(columns[0])).toEqual(['contains', 'equals', 'in', 'empty', 'notEmpty']);
    expect(operationsFor(columns[1])).toEqual(['range', 'empty', 'notEmpty']);
    expect(operationsFor(columns[2])).toEqual(['in', 'empty', 'notEmpty']);
    expect(operationsFor(columns[3])).toEqual(['range', 'empty', 'notEmpty']);
  });

  it('compares queries by what they ask for', () => {
    expect(sameQuery(emptyQuery(), emptyQuery())).toBe(true);
    expect(sameQuery(emptyQuery(), { ...emptyQuery(), page: 2 })).toBe(false);
  });
});
