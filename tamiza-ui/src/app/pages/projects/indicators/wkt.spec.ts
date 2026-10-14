import { parseWkt, positionsOf } from './wkt';

describe('parseWkt', () => {
  describe('POINT', () => {
    it('reads a point', () => {
      expect(parseWkt('POINT (-76.5 3.45)')).toEqual({ type: 'point', position: [-76.5, 3.45] });
    });

    it('is case-insensitive and tolerates uneven spacing, exponents and signs', () => {
      expect(parseWkt('  point(  1e2   -2.5E-1 )  ')).toEqual({ type: 'point', position: [100, -0.25] });
      expect(parseWkt('POINT(+1 .5)')).toEqual({ type: 'point', position: [1, 0.5] });
    });

    it('drops a Z or M value and accepts the dimension marker and an SRID prefix', () => {
      expect(parseWkt('POINT Z (1 2 3)')).toEqual({ type: 'point', position: [1, 2] });
      expect(parseWkt('POINT ZM (1 2 3 4)')).toEqual({ type: 'point', position: [1, 2] });
      expect(parseWkt('SRID=4326;POINT (1 2)')).toEqual({ type: 'point', position: [1, 2] });
    });
  });

  describe('POLYGON', () => {
    it('reads the outline', () => {
      const geometry = parseWkt('POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))');

      expect(geometry).toEqual({ type: 'polygon', rings: [[[0, 0], [4, 0], [4, 4], [0, 4], [0, 0]]] });
    });

    it('reads the holes as further rings', () => {
      const geometry = parseWkt('POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 4 2, 4 4, 2 2), (6 6, 8 6, 8 8, 6 6))');

      expect(geometry?.type).toBe('polygon');
      expect(geometry?.type === 'polygon' && geometry.rings.map((ring) => ring.length)).toEqual([5, 4, 4]);
    });
  });

  describe('MULTIPOLYGON', () => {
    it('reads several polygons, each with its own holes', () => {
      const geometry = parseWkt('MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 9 5, 9 9, 5 5), (6 6, 7 6, 7 7, 6 6)))');

      expect(geometry?.type).toBe('multipolygon');
      if (geometry?.type === 'multipolygon') {
        expect(geometry.polygons.map((polygon) => polygon.length)).toEqual([1, 2]);
        expect(geometry.polygons[1][1][0]).toEqual([6, 6]);
      }
    });
  });

  describe('unparseable text', () => {
    it.each([
      ['empty text', ''],
      ['only spaces', '   '],
      ['an unsupported type', 'LINESTRING (0 0, 1 1)'],
      ['a multi-point', 'MULTIPOINT ((0 0), (1 1))'],
      ['a collection', 'GEOMETRYCOLLECTION (POINT (1 2))'],
      ['an empty geometry', 'POINT EMPTY'],
      ['an unclosed parenthesis', 'POLYGON ((0 0, 1 0, 1 1, 0 0)'],
      ['an extra parenthesis', 'POINT (1 2))'],
      ['trailing text', 'POINT (1 2) x'],
      ['a letter in a coordinate', 'POINT (1 a)'],
      ['a missing coordinate', 'POINT (1)'],
      ['too many ordinates', 'POINT (1 2 3 4 5)'],
      ['a comma between ordinates', 'POINT (1, 2)'],
      ['a ring with fewer than three positions', 'POLYGON ((0 0, 1 1))'],
      ['a polygon with a point ring', 'POLYGON (0 0, 1 0, 1 1)'],
      ['not-a-number coordinates', 'POINT (NaN 2)'],
      ['a number too large to be finite', 'POINT (1e999 2)'],
      ['plain prose', 'Cali, Valle del Cauca'],
    ])('answers null for %s', (_name, text) => {
      expect(parseWkt(text)).toBeNull();
    });

    it('answers null for a missing value and never throws', () => {
      expect(parseWkt(null)).toBeNull();
      expect(parseWkt(undefined)).toBeNull();
      expect(parseWkt(42 as unknown as string)).toBeNull();
    });
  });

  it('lists every position of a geometry', () => {
    expect(positionsOf(parseWkt('POINT (1 2)')!)).toEqual([[1, 2]]);
    expect(positionsOf(parseWkt('POLYGON ((0 0, 1 0, 1 1, 0 0))')!)).toHaveLength(4);
    expect(positionsOf(parseWkt('MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 9 5, 9 9, 5 5)))')!)).toHaveLength(8);
  });
});
