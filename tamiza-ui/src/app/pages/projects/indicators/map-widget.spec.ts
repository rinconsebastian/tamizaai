import { byTestId, settle } from '../../../../testing/project-harness';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { Context, all, clearOverlay, config, isQuery, open, result, row, text, widget } from './indicators-testing';

const square = (x: number, y: number, size = 1) => `POLYGON ((${x} ${y}, ${x + size} ${y}, ${x + size} ${y + size}, ${x} ${y + size}, ${x} ${y}))`;

async function show(rows: QueryRow[], settings: Partial<WidgetConfig> = {}) {
  const context = await open('viewer', [widget('w1', { chartType: 'map', config: config({ geometryColumn: 'geometry', ...settings }) })], null);
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(result(rows));
  await settle(context.harness);
  return { ...context, query: requests[0].request.body as WidgetConfig };
}

const regions = (context: Context) => all(context.root, 'map-region');
const points = (context: Context) => all(context.root, 'map-point');
const fill = (element: Element) => element.getAttribute('style') ?? '';
const titleOf = (element: Element) => text(element.querySelector('title'));

describe('map widget', () => {
  afterEach(clearOverlay);

  describe('choropleth by region', () => {
    const municipalities = [
      row('coverage', 'Cali', 90, square(0, 0)),
      row('coverage', 'Bogotá', 50, square(1, 0)),
      row('coverage', 'Pasto', 10, square(0, 1)),
      row('coverage', 'Quibdó', 70, `MULTIPOLYGON (((1 1, 2 1, 2 2, 1 2, 1 1)), ((2.2 1, 2.8 1, 2.8 1.6, 2.2 1, 2.2 1)))`),
    ];

    it('shows each municipality as a filled region, colored by its value', async () => {
      const context = await show(municipalities);

      expect(regions(context)).toHaveLength(4);
      expect(points(context)).toHaveLength(0);
      expect(regions(context).map(titleOf)).toEqual(['Cali: 90', 'Bogotá: 50', 'Pasto: 10', 'Quibdó: 70']);
      const fills = regions(context).map(fill);
      expect(fills[0]).toContain('var(--tmz-seq-7)'); // the highest value takes the darkest step
      expect(fills[2]).toContain('var(--tmz-seq-1)'); // the lowest takes the lightest
      expect(new Set(fills).size).toBe(4);
    });

    it('asks for the geometry column along with the indicator', async () => {
      const { query } = await show(municipalities);

      expect(query).toEqual({ ...config({ geometryColumn: 'geometry' }), buckets: null });
    });

    it('draws the shapes in their relative positions, north up', async () => {
      const context = await show(municipalities);

      const box = (path: Element) => {
        const numbers = Array.from(path.getAttribute('d')!.matchAll(/([\d.]+) ([\d.]+)/g)).map((m) => [Number(m[1]), Number(m[2])]);
        return { minX: Math.min(...numbers.map((n) => n[0])), minY: Math.min(...numbers.map((n) => n[1])) };
      };
      const [cali, bogota, pasto] = regions(context).map(box);
      expect(bogota.minX).toBeGreaterThan(cali.minX); // east of Cali
      expect(bogota.minY).toBeCloseTo(cali.minY, 0); // same latitude
      expect(pasto.minY).toBeLessThan(cali.minY); // further north: smaller y on screen
    });

    it('fits every shape inside the drawing', async () => {
      const context = await show(municipalities);

      const svg = byTestId(context.root, 'chart-svg')!;
      const width = Number(svg.getAttribute('width'));
      const height = Number(svg.getAttribute('height'));
      for (const region of regions(context)) {
        for (const m of region.getAttribute('d')!.matchAll(/([\d.]+) ([\d.]+)/g)) {
          expect(Number(m[1])).toBeGreaterThanOrEqual(0);
          expect(Number(m[1])).toBeLessThanOrEqual(width);
          expect(Number(m[2])).toBeGreaterThanOrEqual(0);
          expect(Number(m[2])).toBeLessThanOrEqual(height);
        }
      }
    });

    it('draws holes with the even-odd rule', async () => {
      const donut = 'POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (3 3, 7 3, 7 7, 3 7, 3 3))';
      const context = await show([row('coverage', 'Ring', 40, donut)]);

      const path = regions(context)[0];
      expect(path.getAttribute('fill-rule')).toBe('evenodd');
      expect(path.getAttribute('d')!.match(/M/g)).toHaveLength(2);
    });

    it('has a legend from the lowest to the highest value, and no basemap', async () => {
      const context = await show(municipalities);

      expect(text(byTestId(context.root, 'map-legend-min'))).toBe('10');
      expect(text(byTestId(context.root, 'map-legend-max'))).toBe('90');
      expect(byTestId(context.root, 'map-legend')!.querySelectorAll('span[style]')).toHaveLength(7);
      expect(context.root.querySelector('[data-testid="chart-svg"] image')).toBeNull();
    });

    it('describes itself and offers a table with each row\'s geometry kind', async () => {
      const context = await show(municipalities);

      expect(byTestId(context.root, 'chart-svg')!.getAttribute('role')).toBe('img');
      expect(byTestId(context.root, 'chart-svg')!.getAttribute('aria-label')).toBe('Map of coverage: 4 places drawn, from 10 to 90; 0 left out.');
      byTestId(context.root, 'chart-toggle')!.click();
      await settle(context.harness);

      expect(byTestId(context.root, 'chart-svg')).toBeNull();
      const rows = all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
      expect(rows).toEqual([['Cali', '90', 'Region'], ['Bogotá', '50', 'Region'], ['Pasto', '10', 'Region'], ['Quibdó', '70', 'Region']]);
    });

    it('colors by the middle step when every value is the same', async () => {
      const context = await show([row('coverage', 'A', 5, square(0, 0)), row('coverage', 'B', 5, square(1, 0))]);

      expect(regions(context).map(fill).every((f) => f.includes('var(--tmz-seq-4)'))).toBe(true);
    });
  });

  describe('points', () => {
    const places = [row('coverage', 'Cali', 80, 'POINT (-76.53 3.43)'), row('coverage', 'Pasto', 20, 'POINT (-77.28 1.21)'), row('coverage', 'Quibdó', 50, 'POINT (-76.65 5.69)')];

    it('shows each point as a marker instead of a filled region', async () => {
      const context = await show(places);

      expect(points(context)).toHaveLength(3);
      expect(regions(context)).toHaveLength(0);
      expect(points(context).map(titleOf)).toEqual(['Cali: 80', 'Pasto: 20', 'Quibdó: 50']);
      expect(Number(points(context)[0].getAttribute('r'))).toBeGreaterThanOrEqual(4); // a marker is not a dot
    });

    it('colors the markers on the same sequential scale', async () => {
      const context = await show(places);

      expect(fill(points(context)[0])).toContain('var(--tmz-seq-7)');
      expect(fill(points(context)[1])).toContain('var(--tmz-seq-1)');
    });

    it('puts the northern point higher on screen', async () => {
      const context = await show(places);

      const [cali, pasto, quibdo] = points(context).map((p) => Number(p.getAttribute('cy')));
      expect(quibdo).toBeLessThan(cali);
      expect(cali).toBeLessThan(pasto);
    });

    it('centers a lone point', async () => {
      const context = await show([row('coverage', 'Cali', 80, 'POINT (-76.53 3.43)')]);

      const svg = byTestId(context.root, 'chart-svg')!;
      const point = points(context)[0];
      expect(Number(point.getAttribute('cx'))).toBeCloseTo(Number(svg.getAttribute('width')) / 2, 0);
      expect(Number(point.getAttribute('cy'))).toBeCloseTo(Number(svg.getAttribute('height')) / 2, 0);
    });

    it('draws points and regions together when the column mixes them', async () => {
      const context = await show([row('coverage', 'Cali', 80, 'POINT (0.5 0.5)'), row('coverage', 'Pasto', 20, square(1, 0))]);

      expect(points(context)).toHaveLength(1);
      expect(regions(context)).toHaveLength(1);
    });
  });

  describe('unparseable geometry', () => {
    const rows = [
      row('coverage', 'Cali', 90, square(0, 0)),
      row('coverage', 'Bogotá', 50, square(1, 0)),
      row('coverage', 'Pasto', 10, 'LINESTRING (0 0, 1 1)'),
      row('coverage', 'Quibdó', 70, 'POLYGON ((0 0, 1 1'),
      row('coverage', 'Leticia', 30, null),
    ];

    it('shows the other rows and counts the 3 left out', async () => {
      const context = await show(rows);

      expect(regions(context)).toHaveLength(2);
      expect(text(byTestId(context.root, 'map-left-out'))).toBe('3 rows left out: their geometry is missing or cannot be read.');
    });

    it('does not let a left-out row move the legend range', async () => {
      const context = await show(rows);

      expect(text(byTestId(context.root, 'map-legend-min'))).toBe('50');
      expect(text(byTestId(context.root, 'map-legend-max'))).toBe('90');
    });

    it('lists the left-out rows in the table too', async () => {
      const context = await show(rows);

      byTestId(context.root, 'chart-toggle')!.click();
      await settle(context.harness);
      const cells = all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
      expect(cells.slice(2)).toEqual([['Pasto', '10', 'Left out'], ['Quibdó', '70', 'Left out'], ['Leticia', '30', 'Left out']]);
    });

    it('says so when no row can be drawn', async () => {
      const context = await show([row('coverage', 'Cali', 90, 'garbage'), row('coverage', 'Pasto', 10, null)]);

      expect(regions(context)).toHaveLength(0);
      expect(text(byTestId(context.root, 'map-nothing'))).toBe('No row has a geometry that can be drawn.');
      expect(text(byTestId(context.root, 'map-left-out'))).toContain('2 rows left out');
      expect(byTestId(context.root, 'map-legend')).toBeNull();
    });
  });
});
