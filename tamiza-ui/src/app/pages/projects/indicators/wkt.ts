/** A coordinate pair: x is longitude (or easting), y is latitude (or northing). Any Z or M value is dropped. */
export type Position = [number, number];
/** A closed ring of positions; the first ring of a polygon is its outline, the others are holes. */
export type Ring = Position[];

export type Geometry =
  | { type: 'point'; position: Position }
  | { type: 'polygon'; rings: Ring[] }
  | { type: 'multipolygon'; polygons: Ring[][] };

/**
 * Reads the WKT text the pipeline publishes for a geometry column (design D6). Only `POINT`, `POLYGON` (with holes) and
 * `MULTIPOLYGON` are understood, optionally with a Z/M/ZM marker and an `SRID=n;` prefix. Anything else (other geometry types,
 * `EMPTY`, malformed text) answers null; this never throws.
 */
export function parseWkt(text: string | null | undefined): Geometry | null {
  if (typeof text !== 'string') {
    return null;
  }
  try {
    return new Reader(text).geometry();
  } catch {
    return null;
  }
}

class Reader {
  private at = 0;

  constructor(private readonly text: string) {}

  geometry(): Geometry | null {
    this.skipSrid();
    const word = this.word();
    if (word !== 'POINT' && word !== 'POLYGON' && word !== 'MULTIPOLYGON') {
      return null;
    }
    this.skipDimension();
    let result: Geometry;
    if (word === 'POINT') {
      this.expect('(');
      result = { type: 'point', position: this.position() };
      this.expect(')');
    } else if (word === 'POLYGON') {
      result = { type: 'polygon', rings: this.rings() };
    } else {
      this.expect('(');
      const polygons: Ring[][] = [this.rings()];
      while (this.eat(',')) {
        polygons.push(this.rings());
      }
      this.expect(')');
      result = { type: 'multipolygon', polygons };
    }
    this.space();
    if (this.at !== this.text.length) {
      return null; // trailing text
    }
    return result;
  }

  /** `(ring, ring, ...)` */
  private rings(): Ring[] {
    this.expect('(');
    const rings: Ring[] = [this.ring()];
    while (this.eat(',')) {
      rings.push(this.ring());
    }
    this.expect(')');
    return rings;
  }

  /** `(x y, x y, ...)`, at least three positions. */
  private ring(): Ring {
    this.expect('(');
    const ring: Ring = [this.position()];
    while (this.eat(',')) {
      ring.push(this.position());
    }
    this.expect(')');
    if (ring.length < 3) {
      throw new Error('ring too short');
    }
    return ring;
  }

  /** Two to four numbers; the first two are kept. */
  private position(): Position {
    const numbers = [this.number()];
    while (/[-+.\d]/.test(this.peek())) {
      numbers.push(this.number());
    }
    if (numbers.length < 2 || numbers.length > 4) {
      throw new Error('bad position');
    }
    return [numbers[0], numbers[1]];
  }

  private number(): number {
    this.space();
    const match = /^[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?/.exec(this.text.slice(this.at));
    if (!match) {
      throw new Error('number expected');
    }
    this.at += match[0].length;
    const value = Number(match[0]);
    if (!Number.isFinite(value)) {
      throw new Error('not finite');
    }
    return value;
  }

  private word(): string {
    this.space();
    const match = /^[A-Za-z]+/.exec(this.text.slice(this.at));
    if (!match) {
      return '';
    }
    this.at += match[0].length;
    return match[0].toUpperCase();
  }

  private skipSrid(): void {
    this.space();
    const match = /^SRID=\d+;/i.exec(this.text.slice(this.at));
    if (match) {
      this.at += match[0].length;
    }
  }

  private skipDimension(): void {
    this.space();
    const match = /^(ZM|Z|M)(?=\s*\()/i.exec(this.text.slice(this.at));
    if (match) {
      this.at += match[0].length;
    }
  }

  private expect(char: string): void {
    if (!this.eat(char)) {
      throw new Error(`${char} expected`);
    }
  }

  private eat(char: string): boolean {
    this.space();
    if (this.text[this.at] === char) {
      this.at++;
      return true;
    }
    return false;
  }

  private peek(): string {
    this.space();
    return this.text[this.at] ?? '';
  }

  private space(): void {
    while (this.at < this.text.length && /\s/.test(this.text[this.at])) {
      this.at++;
    }
  }
}

/** Every position of a geometry, for fitting the projection to the rows' own extent. */
export function positionsOf(geometry: Geometry): Position[] {
  switch (geometry.type) {
    case 'point':
      return [geometry.position];
    case 'polygon':
      return geometry.rings.flat();
    default:
      return geometry.polygons.flat(2);
  }
}
