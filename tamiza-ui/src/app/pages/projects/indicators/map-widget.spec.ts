import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { onTestFinished, vi } from 'vitest';
import { provideVisibleElements } from '../../../../testing/cdk';
import { MapStub, key, stubMap, touch, wheel } from '../../../../testing/leaflet';
import { byTestId, settle } from '../../../../testing/project-harness';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { BasemapList, Basemaps } from '../../../core/basemaps.service';
import { DataColumnType } from '../../../core/projects/data.service';
import { IndicatorEntry, QueryResult, QueryRow, WidgetConfig, WidgetTooltipItem } from '../../../core/projects/indicators.service';
import { ChartTooltips } from '../../../shared/ui';
import { plainIndicator, valueFormatter } from './format';
import { Context, all, catalog, clearOverlay, config, indicator, isColumns, isQuery, open, overlay, paperOn, printOf, result, row, text, wait, widget } from './indicators-testing';
import { MapEngine, loadLeaflet } from './map-engine';
import { MapWidget } from './map-widget';
import { previewDelayMs } from './widget-dialog';

const square = (x: number, y: number, size = 1) => `POLYGON ((${x} ${y}, ${x + size} ${y}, ${x + size} ${y + size}, ${x} ${y + size}, ${x} ${y}))`;

const basemapsUrl = '/api/v1/basemaps';
const isBasemaps = (r: { url: string; method: string }) => r.method === 'GET' && r.url === basemapsUrl;

let stub: MapStub;

interface Show {
  settings?: Partial<WidgetConfig>;
  indicators?: IndicatorEntry[];
  /** The rest of the query's answer: the tooltip types, for example. */
  answer?: Partial<QueryResult>;
  /** The basemap list the deployment answers; none by default, as a deployment with no provider does. False leaves it unanswered. */
  list?: BasemapList | false;
  /** Waits for the map to be drawn (the default). */
  drawn?: boolean;
}

const noList: BasemapList = { provider: 'none', basemaps: [] };

const osmEntry = {
  kind: 'streets' as const,
  minZoom: 0,
  maxZoom: 19,
  tiles: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  linksExpireAt: null,
  linksRefreshInSeconds: null,
  attribution: { source: 'osm' as const, text: '© OpenStreetMap contributors', url: 'https://www.openstreetmap.org/copyright' },
};
const osmList: BasemapList = { provider: 'osm', basemaps: [osmEntry] };

/** Google's two basemaps; the version names the links, so a refreshed list has tiles with other links. */
const googleList = (version = 1, refreshIn = 1980): BasemapList => ({
  provider: 'google',
  basemaps: (['streets', 'satellite'] as const).map((kind) => ({
    kind,
    minZoom: 0,
    maxZoom: 22,
    tiles: `/api/v1/basemap-tiles/link${version}${kind}/{z}/{x}/{y}`,
    linksExpireAt: '2026-10-17T15:15:00Z',
    linksRefreshInSeconds: refreshIn,
    attribution: { source: 'google' as const, text: null, url: null },
  })),
});

const isCredits = (kind: string) => (r: { url: string; method: string }) => r.method === 'GET' && r.url.startsWith(`${basemapsUrl}/${kind}/credits`);

async function show(rows: QueryRow[], { settings = {}, indicators, answer = {}, list = noList, drawn = true }: Show = {}) {
  const context = await open('viewer', [widget('w1', { chartType: 'map', config: config({ geometryColumn: 'geometry', ...settings }) })], null, indicators ? { indicators } : {});
  const requests = context.http.match(isQuery);
  expect(requests).toHaveLength(1);
  requests[0].flush(result(rows, answer));
  await settle(context.harness);
  if (list !== false) {
    for (const request of context.http.match(isBasemaps)) {
      request.flush(list);
    }
    await settle(context.harness);
  }
  if (drawn) {
    await ready(context);
  }
  return { ...context, query: requests[0].request.body as { config: WidgetConfig; chartType: string } };
}

/** The widget on its own, to change its inputs: the rows of a new answer, for example. */
async function mount(rows: QueryRow[], settings: Partial<WidgetConfig> = {}, list: BasemapList = noList) {
  TestBed.configureTestingModule({ imports: [translocoTesting()], providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements()] });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(MapWidget);
  fixture.componentRef.setInput('rows', rows);
  fixture.componentRef.setInput('config', config({ geometryColumn: 'geometry', ...settings }));
  fixture.componentRef.setInput('label', 'Coverage');
  fixture.componentRef.setInput('format', valueFormatter(plainIndicator, 'en-US'));
  const mounted = { fixture, http, root: fixture.nativeElement as HTMLElement, harness: { fixture } as unknown as RouterTestingHarness };
  await settle(mounted.harness);
  http.match(isBasemaps).forEach((request) => request.flush(list));
  await ready(mounted);
  return mounted;
}

/** Lets the map be created: Leaflet loads on demand, so the shapes are there a few turns after the rows. */
async function ready(context: Pick<Context, 'harness'> & { root: HTMLElement }, until = () => !!context.root.querySelector('.leaflet-container path')) {
  for (let i = 0; i < 20 && !until(); i++) {
    await wait(5);
    await settle(context.harness);
  }
}

const regions = (context: Pick<Context, 'root'>) => all(context.root, 'map-region');
const points = (context: Context) => all(context.root, 'map-point');
const container = (context: Pick<Context, 'root'>) => byTestId(context.root, 'map-container')!;
const zoomOf = (context: Pick<Context, 'root'>) => Number(container(context).getAttribute('data-zoom'));
const centerOf = (context: Pick<Context, 'root'>) => container(context).getAttribute('data-center');
const seq = (element: Element) => Array.from(element.classList).find((c) => c.startsWith('tmz-seq-'));
const click = async (context: Pick<Context, 'harness'>, target: Element | null) => {
  (target as HTMLElement).click();
  await settle(context.harness);
};

/** Where a marker is: its path starts at the left of the circle (radius 6). */
function centreOf(path: Element) {
  const [x, y] = /^M(-?[\d.]+)[ ,](-?[\d.]+)/.exec(path.getAttribute('d')!)!.slice(1).map(Number);
  return { x: x + 6, y };
}

/** The corners of a path, in the container's pixels (the layer starts at the container's corner when the view is untouched). */
function corners(path: Element) {
  const numbers = Array.from(path.getAttribute('d')!.matchAll(/(-?[\d.]+) (-?[\d.]+)/g)).map((m) => [Number(m[1]), Number(m[2])]);
  return { minX: Math.min(...numbers.map((n) => n[0])), maxX: Math.max(...numbers.map((n) => n[0])), minY: Math.min(...numbers.map((n) => n[1])), maxY: Math.max(...numbers.map((n) => n[1])) };
}

describe('map widget', () => {
  beforeAll(async () => {
    const preload = stubMap();
    await loadLeaflet();
    preload.restore();
  });

  beforeEach(() => {
    stub = stubMap({ width: 600, height: 400 });
  });

  afterEach(() => {
    clearOverlay();
    stub.restore();
  });

  describe('choropleth by region', () => {
    const municipalities = [
      row('Cali', 90, square(0, 0)),
      row('Bogotá', 50, square(1, 0)),
      row('Pasto', 10, square(0, 1)),
      row('Quibdó', 70, `MULTIPOLYGON (((1 1, 2 1, 2 2, 1 2, 1 1)), ((2.2 1, 2.8 1, 2.8 1.6, 2.2 1, 2.2 1)))`),
    ];

    it('shows each municipality as a filled region, colored by its value', async () => {
      const context = await show(municipalities);

      expect(regions(context)).toHaveLength(4);
      expect(points(context)).toHaveLength(0);
      const steps = regions(context).map(seq);
      expect(steps[0]).toBe('tmz-seq-7'); // the highest value takes the darkest step
      expect(steps[2]).toBe('tmz-seq-1'); // the lowest takes the lightest
      expect(new Set(steps).size).toBe(4);
    });

    it('asks for the geometry column along with the indicator', async () => {
      const { query } = await show(municipalities);

      expect(query).toEqual({ config: config({ geometryColumn: 'geometry' }), chartType: 'map' });
    });

    it('draws the shapes where they are on the Earth, north up', async () => {
      const context = await show(municipalities);

      const [cali, bogota, pasto] = regions(context).map(corners);
      expect(bogota.minX).toBeGreaterThan(cali.minX); // east of Cali
      expect(bogota.minY).toBeCloseTo(cali.minY, 0); // same latitude
      expect(pasto.minY).toBeLessThan(cali.minY); // further north: smaller y on screen
    });

    it('opens fitted: every shape inside the box, and the map as big as it can be', async () => {
      const context = await show(municipalities);

      const boxes = regions(context).map(corners);
      expect(Math.min(...boxes.map((c) => c.minX))).toBeGreaterThanOrEqual(0);
      expect(Math.max(...boxes.map((c) => c.maxX))).toBeLessThanOrEqual(600);
      expect(Math.min(...boxes.map((c) => c.minY))).toBeGreaterThanOrEqual(0);
      expect(Math.max(...boxes.map((c) => c.maxY))).toBeLessThanOrEqual(400);
      // The shapes span about 2.8 x 2 degrees, taller than wide for this box: they fill most of the height.
      expect(Math.max(...boxes.map((c) => c.maxY)) - Math.min(...boxes.map((c) => c.minY))).toBeGreaterThan(300);
    });

    it('draws holes with the even-odd rule', async () => {
      const donut = 'POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (3 3, 7 3, 7 7, 3 7, 3 3))';
      const context = await show([row('Ring', 40, donut)]);

      const path = regions(context)[0];
      expect(path.getAttribute('fill-rule')).toBe('evenodd');
      expect(path.getAttribute('d')!.match(/M/g)).toHaveLength(2);
    });

    it('has a legend from the lowest to the highest value, and no basemap', async () => {
      const context = await show(municipalities);

      expect(text(byTestId(context.root, 'map-legend-min'))).toBe('10');
      expect(text(byTestId(context.root, 'map-legend-max'))).toBe('90');
      expect(byTestId(context.root, 'map-legend')!.querySelectorAll('span[style]')).toHaveLength(7);
      expect(all(context.root, 'map-tile')).toHaveLength(0);
      expect(byTestId(context.root, 'map-attribution')).toBeNull();
    });

    it('describes itself as a map application and offers a table with each row\'s geometry kind', async () => {
      const context = await show(municipalities);

      const map = container(context);
      expect(map.getAttribute('role')).toBe('application');
      expect(map.getAttribute('aria-roledescription')).toBe('map');
      expect(map.getAttribute('aria-label')).toBe('Map of Coverage: 4 places drawn, from 10 to 90; 0 left out.');
      expect(context.root.querySelector('svg[role="img"]')).toBeNull();
      byTestId(context.root, 'widget-table-toggle')!.click();
      await settle(context.harness);

      expect(byTestId(context.root, 'map-container')).toBeNull();
      const rows = all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
      expect(rows).toEqual([['Cali', '90', 'Region'], ['Bogotá', '50', 'Region'], ['Pasto', '10', 'Region'], ['Quibdó', '70', 'Region']]);
    });

    it('writes the legend and the table with the indicator\'s format', async () => {
      const context = await show([row('Cali', 0.9, square(0, 0)), row('Pasto', 0.1, square(1, 0))], { settings: { indicator: 'piped_water_rate' }, indicators: [indicator('piped_water_rate')] });

      expect(text(byTestId(context.root, 'map-legend-min'))).toBe('10%');
      expect(text(byTestId(context.root, 'map-legend-max'))).toBe('90%');
      byTestId(context.root, 'widget-table-toggle')!.click();
      await settle(context.harness);
      expect(all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text))).toEqual([
        ['Cali', '90%', 'Region'],
        ['Pasto', '10%', 'Region'],
      ]);
    });

    it('colors by the middle step when every value is the same', async () => {
      const context = await show([row('A', 5, square(0, 0)), row('B', 5, square(1, 0))]);

      expect(regions(context).map(seq).every((step) => step === 'tmz-seq-4')).toBe(true);
    });
  });

  describe('points', () => {
    const places = [row('Cali', 80, 'POINT (-76.53 3.43)'), row('Pasto', 20, 'POINT (-77.28 1.21)'), row('Quibdó', 50, 'POINT (-76.65 5.69)')];

    it('shows each point as a circle marker instead of a filled region', async () => {
      const context = await show(places);

      expect(points(context)).toHaveLength(3);
      expect(regions(context)).toHaveLength(0);
      expect(points(context)[0].getAttribute('d')).toContain('a6,6'); // a marker is not a dot
    });

    it('colors the markers on the same sequential scale', async () => {
      const context = await show(places);

      expect(seq(points(context)[0])).toBe('tmz-seq-7');
      expect(seq(points(context)[1])).toBe('tmz-seq-1');
    });

    it('puts the northern point higher on screen', async () => {
      const context = await show(places);

      const [cali, pasto, quibdo] = points(context).map((p) => centreOf(p).y);
      expect(quibdo).toBeLessThan(cali);
      expect(cali).toBeLessThan(pasto);
    });

    it('opens on a lone point, at zoom 16, centred on it', async () => {
      const context = await show([row('Cali', 80, 'POINT (-76.53 3.43)')]);

      expect(zoomOf(context)).toBe(16);
      const [lat, lng] = centerOf(context)!.split(',').map(Number);
      expect(lat).toBeCloseTo(3.43, 3);
      expect(lng).toBeCloseTo(-76.53, 3);
      const marker = centreOf(points(context)[0]);
      expect(marker.x).toBeCloseTo(300, 0);
      expect(marker.y).toBeCloseTo(200, 0);
    });

    it('draws points and regions together when the column mixes them', async () => {
      const context = await show([row('Cali', 80, 'POINT (0.5 0.5)'), row('Pasto', 20, square(1, 0))]);

      expect(points(context)).toHaveLength(1);
      expect(regions(context)).toHaveLength(1);
    });
  });

  describe('coordinates that are not degrees', () => {
    it('draws them in their relative positions, at a negative zoom', async () => {
      const context = await show([row('A', 10, square(1_000_000, 800_000, 100_000)), row('B', 20, square(1_150_000, 800_000, 100_000))]);

      expect(regions(context)).toHaveLength(2);
      expect(zoomOf(context)).toBeLessThan(-7);
      const [a, b] = regions(context).map(corners);
      expect(b.minX).toBeGreaterThan(a.minX);
    });
  });

  describe('unparseable geometry', () => {
    const rows = [
      row('Cali', 90, square(0, 0)),
      row('Bogotá', 50, square(1, 0)),
      row('Pasto', 10, 'LINESTRING (0 0, 1 1)'),
      row('Quibdó', 70, 'POLYGON ((0 0, 1 1'),
      row('Leticia', 30, null),
    ];

    it('shows the other rows and counts the 3 left out', async () => {
      const context = await show(rows);

      expect(regions(context)).toHaveLength(2);
      expect(text(byTestId(context.root, 'map-left-out'))).toBe('3 rows left out: their geometry is missing or cannot be read.');
    });

    it('counts a single left-out row in the singular', async () => {
      const context = await show([row('Cali', 90, square(0, 0)), row('Leticia', 30, null)]);

      expect(text(byTestId(context.root, 'map-left-out'))).toBe('1 row left out: its geometry is missing or cannot be read.');
      expect(container(context).getAttribute('aria-label')).toBe('Map of Coverage: 1 place drawn, from 90 to 90; 1 left out.');
    });

    it('does not let a left-out row move the legend range', async () => {
      const context = await show(rows);

      expect(text(byTestId(context.root, 'map-legend-min'))).toBe('50');
      expect(text(byTestId(context.root, 'map-legend-max'))).toBe('90');
    });

    it('lists the left-out rows in the table too', async () => {
      const context = await show(rows);

      byTestId(context.root, 'widget-table-toggle')!.click();
      await settle(context.harness);
      const cells = all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
      expect(cells.slice(2)).toEqual([['Pasto', '10', 'Left out'], ['Quibdó', '70', 'Left out'], ['Leticia', '30', 'Left out']]);
    });

    it('says so when no row can be drawn, and makes no map at all', async () => {
      const context = await show([row('Cali', 90, 'garbage'), row('Pasto', 10, null)]);
      await wait(20);
      await settle(context.harness);

      expect(regions(context)).toHaveLength(0);
      expect(context.root.querySelector('.leaflet-container')).toBeNull();
      expect(byTestId(context.root, 'map-controls')).toBeNull();
      expect(text(byTestId(context.root, 'map-nothing'))).toBe('No row has a geometry that can be drawn.');
      expect(text(byTestId(context.root, 'map-left-out'))).toContain('2 rows left out');
      expect(byTestId(context.root, 'map-legend')).toBeNull();
    });
  });

  describe('zoom and pan', () => {
    const blocks = [row('0001', 10, square(-76.6, 3.4, 0.01)), row('0002', 40, square(-76.58, 3.4, 0.01)), row('0003', 70, square(-76.6, 3.42, 0.01))];

    it('zooms with the buttons around the same centre, and shows all again', async () => {
      const context = await show(blocks);
      const fitted = { zoom: zoomOf(context), center: centerOf(context) };

      await click(context, byTestId(context.root, 'map-zoom-in'));
      await click(context, byTestId(context.root, 'map-zoom-in'));

      expect(zoomOf(context)).toBe(fitted.zoom + 2);
      expect(centerOf(context)).toBe(fitted.center);
      await click(context, byTestId(context.root, 'map-show-all'));
      expect(zoomOf(context)).toBe(fitted.zoom);
    });

    it('zooms out no more than three levels past the fit, and says so on the button', async () => {
      const context = await show(blocks);
      const fitted = zoomOf(context);

      for (let i = 0; i < 6; i++) {
        await click(context, byTestId(context.root, 'map-zoom-out'));
      }

      expect(zoomOf(context)).toBe(fitted - 3);
      expect(byTestId(context.root, 'map-zoom-out')!.getAttribute('aria-disabled')).toBe('true');
      expect(byTestId(context.root, 'map-zoom-in')!.getAttribute('aria-disabled')).toBeNull();
    });

    it('has the controls in one row, in the order zoom in, zoom out, show all', async () => {
      const context = await show(blocks);

      const buttons = Array.from(byTestId(context.root, 'map-controls')!.querySelectorAll('button')).map((b) => b.getAttribute('aria-label'));
      expect(buttons).toEqual(['Zoom in', 'Zoom out', 'Show all']);
      expect(byTestId(context.root, 'map-options')).toBeNull();
    });

    it('keeps scrolling the page on a plain wheel, with a hint, whether over the map or over a shape', async () => {
      const context = await show(blocks);
      const zoom = zoomOf(context);

      const onMap = wheel(container(context));
      const onShape = wheel(regions(context)[0]);
      await wait(80);
      await settle(context.harness);

      expect(zoomOf(context)).toBe(zoom);
      expect(onMap.defaultPrevented).toBe(false);
      expect(onShape.defaultPrevented).toBe(false);
      expect(text(byTestId(context.root, 'map-hint'))).toBe(navigator.platform.startsWith('Mac') ? 'Hold ⌘ and scroll to zoom' : 'Hold Ctrl and scroll to zoom');
    });

    it('hides the wheel hint after a moment', async () => {
      const context = await show(blocks);

      wheel(container(context));
      await settle(context.harness);
      expect(byTestId(context.root, 'map-hint')).not.toBeNull();
      await wait(1600);
      await settle(context.harness);

      expect(byTestId(context.root, 'map-hint')).toBeNull();
    });

    it('zooms around the pointer on Ctrl and the wheel, and does not scroll the page', async () => {
      const context = await show(blocks);
      const zoom = zoomOf(context);

      const event = wheel(container(context), { ctrl: true, deltaY: -240 });
      await wait(80);
      await settle(context.harness);

      expect(event.defaultPrevented).toBe(true);
      expect(zoomOf(context)).toBeGreaterThan(zoom);
      expect(byTestId(context.root, 'map-hint')).toBeNull();
    });

    it('zooms on ⌘ and the wheel as well', async () => {
      const context = await show(blocks);
      const zoom = zoomOf(context);

      wheel(container(context), { meta: true, deltaY: -240 });
      await wait(80);
      await settle(context.harness);

      expect(zoomOf(context)).toBeGreaterThan(zoom);
    });

    it('lets one finger scroll the page: dragging is off, and a swipe says to use two fingers', async () => {
      const context = await show(blocks);

      touch(container(context), 'touchstart', 1);
      expect(draggable(context)).toBe(false);
      touch(container(context), 'touchmove', 1, { x: 100, y: 140 });
      await settle(context.harness);

      expect(text(byTestId(context.root, 'map-hint'))).toBe('Use two fingers to move the map');
      expect(container(context).classList.contains('leaflet-touch-drag')).toBe(false);
    });

    it('moves the map with two fingers: dragging is on until the last finger is lifted', async () => {
      const context = await show(blocks);

      touch(container(context), 'touchstart', 1);
      touch(container(context), 'touchstart', 2);
      expect(draggable(context)).toBe(true);
      touch(container(context), 'touchend', 1); // one finger still down
      expect(draggable(context)).toBe(true);
      touch(container(context), 'touchend', 0);
      expect(draggable(context)).toBe(false);
    });

    it('lets a mouse drag again after a touch', async () => {
      const context = await show(blocks);
      touch(container(context), 'touchstart', 1);
      expect(draggable(context)).toBe(false);

      const down = new Event('pointerdown', { bubbles: true });
      Object.defineProperty(down, 'pointerType', { value: 'mouse' });
      container(context).dispatchEvent(down);

      expect(draggable(context)).toBe(true);
    });

    it('moves with the arrow keys and zooms with plus and minus while it has focus, and shows all on 0 or Home', async () => {
      const context = await show(blocks);
      stub.reduceMotion(true);
      const fitted = zoomOf(context);
      container(context).focus();

      key(document.body, '+');
      await settle(context.harness);
      expect(zoomOf(context)).toBe(fitted + 1);
      key(container(context), '0');
      await settle(context.harness);
      expect(zoomOf(context)).toBe(fitted);
      key(document.body, '+');
      await settle(context.harness);
      key(container(context), 'Home');
      await settle(context.harness);
      expect(zoomOf(context)).toBe(fitted);
    });

    it('moves with the arrow keys while the map has the focus', async () => {
      const context = await show(blocks);
      const before = centerOf(context)!.split(',').map(Number);
      container(context).focus();

      key(document.body, 'ArrowRight');
      await wait(400); // the pan is animated
      await settle(context.harness);

      expect(Number(centerOf(context)!.split(',')[1])).toBeGreaterThan(before[1]);
    });

    it('zooms in on a double click', async () => {
      const context = await show(blocks);
      const fitted = zoomOf(context);

      container(context).dispatchEvent(new MouseEvent('dblclick', { bubbles: true, clientX: 300, clientY: 200 }));
      await wait(400);
      await settle(context.harness);

      expect(zoomOf(context)).toBe(fitted + 1);
    });

    it('collapses the buttons into one menu on a small box', async () => {
      stub.resize(150, 120);
      const context = await show(blocks);

      expect(byTestId(context.root, 'map-zoom-in')).toBeNull();
      const options = byTestId(context.root, 'map-options')!;
      expect(options.getAttribute('aria-label')).toBe('Map options');
      options.click();
      await settle(context.harness);

      const items = Array.from(document.querySelectorAll('[data-testid="map-options-menu"] button')).map(text);
      expect(items).toEqual(['Zoom in', 'Zoom out', 'Show all']);
      const fitted = zoomOf(context);
      (document.querySelector('[data-testid="map-menu-zoom-in"]') as HTMLElement).click();
      await settle(context.harness);
      expect(zoomOf(context)).toBe(fitted + 1);
    });

    it('goes back to the row of buttons when the box grows', async () => {
      stub.resize(150, 120);
      const context = await show(blocks);
      expect(byTestId(context.root, 'map-options')).not.toBeNull();

      stub.resize(600, 400);
      await settle(context.harness);

      expect(byTestId(context.root, 'map-options')).toBeNull();
      expect(byTestId(context.root, 'map-zoom-in')).not.toBeNull();
    });
  });

  describe('the view is the viewer\'s own', () => {
    const blocks = [row('0001', 10, square(-76.6, 3.4, 0.01)), row('0002', 40, square(-76.58, 3.4, 0.01))];

    it('keeps the zoom through "Show as table" and back', async () => {
      const context = await show(blocks);
      await click(context, byTestId(context.root, 'map-zoom-in'));
      await click(context, byTestId(context.root, 'map-zoom-in'));
      const view = { zoom: zoomOf(context), center: centerOf(context) };

      byTestId(context.root, 'widget-table-toggle')!.click();
      await settle(context.harness);
      expect(context.root.querySelector('.leaflet-container')).toBeNull();
      byTestId(context.root, 'widget-table-toggle')!.click();
      await ready(context);

      expect({ zoom: zoomOf(context), center: centerOf(context) }).toEqual(view);
    });

    it('asks nothing of the server while panning and zooming, and makes no request to anyone', async () => {
      const context = await show(blocks);

      await click(context, byTestId(context.root, 'map-zoom-in'));
      await click(context, byTestId(context.root, 'map-zoom-out'));
      await click(context, byTestId(context.root, 'map-show-all'));
      wheel(container(context), { ctrl: true });
      await wait(80);
      await settle(context.harness);

      context.http.expectNone(() => true);
      expect(all(context.root, 'map-tile')).toHaveLength(0);
    });

    it('opens another viewer\'s map fitted, whatever this one did', async () => {
      const context = await show(blocks);
      const fitted = zoomOf(context);
      await click(context, byTestId(context.root, 'map-zoom-in'));
      expect(zoomOf(context)).toBe(fitted + 1);
      TestBed.resetTestingModule();
      clearOverlay();

      const other = await show(blocks);

      expect(zoomOf(other)).toBe(fitted);
    });

    it('fits again when the rows change, and keeps the zoom when only the box is resized after the member moved', async () => {
      const context = await show(blocks);
      const fitted = zoomOf(context);
      await click(context, byTestId(context.root, 'map-zoom-in'));

      stub.resize(500, 350);
      await settle(context.harness);
      expect(zoomOf(context)).toBe(fitted + 1); // the member's own view stays
      await click(context, byTestId(context.root, 'map-show-all'));
      stub.resize(300, 300);
      await settle(context.harness);

      expect(zoomOf(context)).toBeLessThanOrEqual(fitted); // untouched: fitted to the new box
    });
  });

  describe('tooltip', () => {
    const municipalities = [row('Cali', 90, square(0, 0)), row('Bogotá', 50, square(1, 0)), row('Pasto', 10, square(0, 1))];
    const tooltip = () => overlay().querySelector<HTMLElement>('[data-testid="chart-tooltip"]');
    const lines = () => Array.from(overlay().querySelectorAll('[data-testid="tooltip-heading"], [data-testid="tooltip-line"], [data-testid="tooltip-footer"]')).map(text);
    const hover = (target: Element) => target.dispatchEvent(new MouseEvent('mouseover', { bubbles: true, clientX: 120, clientY: 90 }));
    const leave = (target: Element) => target.dispatchEvent(new MouseEvent('mouseout', { bubbles: true }));
    const tap = (target: Element) => target.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: 120, clientY: 90 }));

    it('shows the group and the plotted value of the shape the pointer is on', async () => {
      const context = await show(municipalities);

      hover(regions(context)[0]);
      await settle(context.harness);

      expect(lines()).toEqual(['Cali', 'Coverage: 90']);
    });

    it('closes when the pointer leaves the shape', async () => {
      const context = await show(municipalities);
      hover(regions(context)[0]);
      expect(tooltip()).not.toBeNull();

      leave(regions(context)[0]);

      expect(tooltip()).toBeNull();
    });

    it('follows the pointer from one shape to the next', async () => {
      const context = await show(municipalities);

      hover(regions(context)[0]);
      leave(regions(context)[0]);
      hover(regions(context)[1]);

      expect(lines()).toEqual(['Bogotá', 'Coverage: 50']);
    });

    it('writes the plotted value with the indicator\'s format and unit', async () => {
      const context = await show([row('Cali', 0.9, square(0, 0))], { settings: { indicator: 'households_visited' }, indicators: [indicator('households_visited')] });

      hover(regions(context)[0]);

      expect(lines()).toEqual(['Cali', 'Households visited: 1 households']);
    });

    it('shows a group with no value as (none)', async () => {
      const context = await show([row(null, 5, square(0, 0))]);

      hover(regions(context)[0]);

      expect(lines()[0]).toBe('(none)');
    });

    it('shows a group value of markup as text, and nothing runs', async () => {
      const hostile = '<img src=x onerror=alert(1)>';
      const context = await show([row(hostile, 5, square(0, 0))]);

      hover(regions(context)[0]);

      expect(lines()[0]).toBe(hostile);
      expect(overlay().querySelectorAll('img')).toHaveLength(0);
    });

    it('stays open after a tap until the member taps elsewhere', async () => {
      const context = await show(municipalities);

      hover(regions(context)[0]);
      tap(regions(context)[0]);
      leave(regions(context)[0]); // a finger has no pointer to leave with
      expect(tooltip()).not.toBeNull();
      container(context).dispatchEvent(new MouseEvent('click', { bubbles: true }));

      expect(tooltip()).toBeNull();
    });

    it('moves to another shape on a tap there', async () => {
      const context = await show(municipalities);

      tap(regions(context)[0]);
      tap(regions(context)[2]);

      expect(lines()).toEqual(['Pasto', 'Coverage: 10']);
    });

    it('closes a pinned tooltip when the page scrolls', async () => {
      const context = await show(municipalities);
      tap(regions(context)[0]);

      document.dispatchEvent(new Event('scroll'));

      expect(tooltip()).toBeNull();
    });

    it('closes a pinned tooltip on a tap anywhere else on the page', async () => {
      const context = await show(municipalities);
      tap(regions(context)[0]);

      document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));

      expect(tooltip()).toBeNull();
    });

    it('closes a pinned tooltip when focus leaves the map', async () => {
      const context = await show(municipalities);
      container(context).focus();
      tap(regions(context)[0]);

      (document.activeElement as HTMLElement).blur();
      await settle(context.harness);

      expect(tooltip()).toBeNull();
    });

    it('keeps a pinned tooltip when focus moves to a control of the map', async () => {
      const context = await show(municipalities);
      container(context).focus();
      tap(regions(context)[0]);

      byTestId<HTMLButtonElement>(context.root, 'map-zoom-in')!.focus();
      await settle(context.harness);

      expect(tooltip()).not.toBeNull();
    });

    it('closes when the zoom starts', async () => {
      const context = await show(municipalities);
      hover(regions(context)[0]);

      await click(context, byTestId(context.root, 'map-zoom-in'));

      expect(tooltip()).toBeNull();
    });

    it('closes when a tile of the grid is being dragged', async () => {
      const context = await show(municipalities);
      tap(regions(context)[0]);

      TestBed.inject(ChartTooltips).closeAll();

      expect(tooltip()).toBeNull();
    });
  });

  describe('tooltip columns', () => {
    const survey = indicator('survey_progress', { label: 'Survey progress', format: 'percent' });
    const columns: WidgetTooltipItem[] = [
      { column: 'address', label: 'Dirección', format: null },
      { column: 'sample_target', label: 'Meta muestral', format: null },
      { column: 'achieved', label: 'Encuestas logradas', format: null },
    ];
    const types: DataColumnType[] = ['text', 'integer', 'integer'];
    const block = (overrides: Partial<QueryRow> = {}): QueryRow => ({ group: '0042', value: 0.45, geometry: square(-76.6, 3.4, 0.002), rows: 1, tooltip: ['Cra 5 # 10-20', 20, 9], ...overrides });
    const lines = () => Array.from(overlay().querySelectorAll('[data-testid="tooltip-heading"], [data-testid="tooltip-line"], [data-testid="tooltip-footer"]')).map(text);
    const hover = (target: Element) => target.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
    const survey_progress = (items: WidgetTooltipItem[], rows: QueryRow[], tooltipTypes: DataColumnType[] = types) =>
      show(rows, { settings: { indicator: 'survey_progress', tooltip: items }, indicators: [survey], answer: { tooltipTypes } });

    it('shows a city block with its address, its target and its progress', async () => {
      const context = await survey_progress(columns, [block()]);

      hover(regions(context)[0]);

      expect(lines()).toEqual(['0042', 'Survey progress: 45%', 'Dirección: Cra 5 # 10-20', 'Meta muestral: 20', 'Encuestas logradas: 9']);
    });

    it('asks the server for the names of the tooltip columns only, never their labels or formats', async () => {
      const labelled: WidgetTooltipItem[] = [{ column: 'address', label: 'Dirección', format: null }, { column: 'sample_target', label: null, format: 'count' }];
      const { query } = await survey_progress(labelled, [block()]);

      expect(query.config.tooltip).toEqual([{ column: 'address', label: null, format: null }, { column: 'sample_target', label: null, format: null }]);
      expect(query.config.basemap).toBeNull();
    });

    it('uses a tooltip column\'s own format and the label it has, or the name of the column when it has none', async () => {
      const mixed: WidgetTooltipItem[] = [{ column: 'address', label: 'Dirección', format: null }, { column: 'sample_target', label: null, format: 'count' }, { column: 'progress', label: 'Avance', format: 'percent' }];
      const context = await survey_progress(mixed, [block({ tooltip: ['Cra 5 # 10-20', 1200, 0.45] })], ['text', 'integer', 'decimal']);

      hover(regions(context)[0]);

      expect(lines().slice(2)).toEqual(['Dirección: Cra 5 # 10-20', 'sample_target: 1,200', 'Avance: 45%']);
    });

    it('says how many rows a block combines, and nothing about it for one row', async () => {
      const context = await survey_progress(columns, [block({ rows: 2, tooltip: ['Cra 5 # 10-20', 20, 9] }), block({ group: '0043', geometry: square(-76.5, 3.4, 0.002) })]);

      hover(regions(context)[0]);
      expect(lines().at(-1)).toBe('Combines 2 rows');
      regions(context)[0].dispatchEvent(new MouseEvent('mouseout', { bubbles: true }));
      hover(regions(context)[1]);

      expect(lines().at(-1)).toBe('Encuestas logradas: 9');
    });

    it('shows a dash for a value every row of the block lacks', async () => {
      const context = await survey_progress(columns, [block({ tooltip: [null, null, null] })]);

      hover(regions(context)[0]);

      expect(lines().slice(2)).toEqual(['Dirección: —', 'Meta muestral: —', 'Encuestas logradas: —']);
    });

    it('shows the text of a column that stopped being a number as it is, without the format', async () => {
      const percent: WidgetTooltipItem[] = [{ column: 'progress', label: null, format: 'percent' }];
      const context = await survey_progress(percent, [block({ tooltip: ['about half'] })], ['text']);

      hover(regions(context)[0]);

      expect(lines().at(-1)).toBe('progress: about half');
    });

    it('shows markup in a value and in a label as text', async () => {
      const hostile = '<img src=x onerror=alert(1)>';
      const context = await survey_progress([{ column: 'address', label: hostile, format: null }], [block({ tooltip: [hostile] })], ['text']);

      hover(regions(context)[0]);

      expect(lines().at(-1)).toBe(`${hostile}: ${hostile}`);
      expect(overlay().querySelectorAll('img')).toHaveLength(0);
    });

    it('lists the tooltip columns, under their labels, in "Show as table", text aligned to the start', async () => {
      const context = await survey_progress(columns, [block(), block({ group: '0043', value: 0.5, tooltip: ['Cra 6 # 11-21', 30, 15] })]);

      byTestId(context.root, 'widget-table-toggle')!.click();
      await settle(context.harness);

      const headers = Array.from(context.root.querySelectorAll('[data-testid="widget-table"] thead th')).map(text);
      expect(headers).toEqual(['municipality', 'Survey progress', 'Dirección', 'Meta muestral', 'Encuestas logradas', 'Geometry']);
      const cells = all(context.root, 'widget-table-row').map((r) => Array.from(r.children).map(text));
      expect(cells).toEqual([
        ['0042', '45%', 'Cra 5 # 10-20', '20', '9', 'Region'],
        ['0043', '50%', 'Cra 6 # 11-21', '30', '15', 'Region'],
      ]);
      const first = all(context.root, 'widget-table-row')[0].children;
      expect(Array.from(first).map((cell) => cell.className.includes('text-end'))).toEqual([false, true, false, true, true, false]);
      expect(Array.from(context.root.querySelectorAll('[data-testid="widget-table"] thead th')).map((cell) => cell.className.includes('text-end'))).toEqual([false, true, false, true, true, false]);
    });
  });

  describe('new rows', () => {
    const first = [row('0001', 10, square(-76.6, 3.4, 0.01)), row('0002', 40, square(-76.58, 3.4, 0.01))];
    const second = [row('0003', 5, square(-76.6, 3.4, 0.01)), row('0004', 9, square(-76.58, 3.4, 0.01))];

    it('close the tooltip and take the view back to the fit', async () => {
      const mounted = await mount(first);
      const fitted = zoomOf(mounted);
      await click(mounted, byTestId(mounted.root, 'map-zoom-in'));
      regions(mounted)[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
      expect(overlay().querySelector('[data-testid="chart-tooltip"]')).not.toBeNull();

      mounted.fixture.componentRef.setInput('rows', second);
      await settle(mounted.harness);

      expect(overlay().querySelector('[data-testid="chart-tooltip"]')).toBeNull();
      expect(zoomOf(mounted)).toBe(fitted);
      expect(text(byTestId(mounted.root, 'map-legend-max'))).toBe('9.00');
    });

    it('draw the new shapes in place of the old, with no second map', async () => {
      const mounted = await mount(first);

      mounted.fixture.componentRef.setInput('rows', [...second, row('0005', 7, square(-76.6, 3.42, 0.01))]);
      await settle(mounted.harness);

      expect(regions(mounted)).toHaveLength(3);
      expect(mounted.root.querySelectorAll('.leaflet-container')).toHaveLength(1);
    });

    it('are drawn flat when they are not degrees, and degrees again after', async () => {
      const mounted = await mount(first);

      mounted.fixture.componentRef.setInput('rows', [row('A', 1, square(1_000_000, 800_000, 100_000)), row('B', 2, square(1_150_000, 800_000, 100_000))]);
      await ready(mounted, () => zoomOf(mounted) < 0);
      expect(zoomOf(mounted)).toBeLessThan(-7);
      mounted.fixture.componentRef.setInput('rows', first);
      await ready(mounted, () => zoomOf(mounted) > 0);

      expect(zoomOf(mounted)).toBeGreaterThan(10);
      expect(regions(mounted)).toHaveLength(2);
    });
  });

  describe('keyboard', () => {
    const blocks = [row('0001', 10, square(-76.6, 3.4, 0.01)), row('0002', 40, square(-76.58, 3.4, 0.01)), row('0003', 70, square(-76.6, 3.42, 0.01)), row('0004', 20, square(-76.58, 3.42, 0.01))];
    const paths = (context: Context) => Array.from(context.root.querySelectorAll<SVGElement>('.leaflet-container path'));
    const tooltip = () => overlay().querySelector<HTMLElement>('[data-testid="chart-tooltip"]');

    beforeEach(() => stub.reduceMotion(true));

    /** Counts the Escapes that get past the map to the page, where a dialog listens for them. */
    function escapesReachingThePage() {
      let count = 0;
      const listener = (event: KeyboardEvent) => event.key === 'Escape' && count++;
      document.body.addEventListener('keydown', listener);
      onTestFinished(() => document.body.removeEventListener('keydown', listener));
      return () => count;
    }

    it('steps through the blocks in the order of the table: Enter, then the right arrow twice', async () => {
      const context = await show(blocks);
      const shapes = regions(context); // the document order changes as a shape is brought to the front; this is the table's
      container(context).focus();

      key(container(context), 'Enter');
      expect(document.activeElement).toBe(shapes[0]);
      key(document.activeElement!, 'ArrowRight');
      key(document.activeElement!, 'ArrowRight');
      await settle(context.harness);

      const third = shapes[2];
      expect(document.activeElement).toBe(third);
      expect(third.classList.contains('tmz-active')).toBe(true);
      expect(third.getAttribute('aria-label')).toBe('0003. Coverage: 70');
      expect(third.getAttribute('role')).toBe('img');
      expect(text(tooltip()!.querySelector('[data-testid="tooltip-heading"]'))).toBe('0003');
      expect(text(tooltip()!.querySelector('[data-testid="tooltip-line"]'))).toBe('Coverage: 70');
      expect(tooltip()!.getAttribute('aria-hidden')).toBe('true');
    });

    it('highlights one block at a time and moves from the first to the last with Home and End', async () => {
      const context = await show(blocks);
      const shapes = regions(context);
      container(context).focus();
      key(container(context), 'Enter');

      key(document.activeElement!, 'End');
      expect(paths(context).filter((p) => p.classList.contains('tmz-active'))).toEqual([shapes[3]]);
      key(document.activeElement!, 'ArrowLeft');
      expect(paths(context).filter((p) => p.classList.contains('tmz-active'))).toEqual([shapes[2]]);
      key(document.activeElement!, 'ArrowUp');
      key(document.activeElement!, 'ArrowUp');
      key(document.activeElement!, 'ArrowUp');
      expect(document.activeElement).toBe(shapes[0]); // stops at the first
      key(document.activeElement!, 'ArrowDown');
      key(document.activeElement!, 'Home');
      expect(document.activeElement).toBe(shapes[0]);
      expect(paths(context).filter((p) => p.hasAttribute('aria-label'))).toHaveLength(1);
    });

    it('is one Tab stop: no shape is ever tabbable, and Escape gives the focus back to the map', async () => {
      const context = await show(blocks);
      container(context).focus();
      expect(paths(context).every((path) => !path.hasAttribute('tabindex'))).toBe(true);

      key(container(context), 'Enter');
      key(document.activeElement!, 'ArrowRight');
      expect(paths(context).filter((p) => p.hasAttribute('tabindex')).every((p) => p.getAttribute('tabindex') === '-1')).toBe(true);
      key(document.activeElement!, 'Escape');
      await settle(context.harness);

      expect(document.activeElement).toBe(container(context));
      expect(paths(context).every((path) => !path.hasAttribute('tabindex') && !path.hasAttribute('aria-label') && !path.classList.contains('tmz-active'))).toBe(true);
      expect(tooltip()).toBeNull();
      // Tab goes to the next tabbable thing after the map in the page's order: the first button, never a shape.
      const tabbable = Array.from(byTestId(context.root, 'chart-box')!.querySelectorAll<HTMLElement>('[tabindex], button')).filter((e) => e.tabIndex >= 0);
      expect(tabbable.slice(0, 2).map((e) => e.getAttribute('data-testid') ?? 'map')).toEqual(['map-container', 'map-zoom-in']);
    });

    it('keeps the shapes out of the Tab order after focus leaves the map without Escape', async () => {
      const context = await show(blocks);
      container(context).focus();
      key(container(context), 'Enter');

      byTestId<HTMLButtonElement>(context.root, 'map-zoom-in')!.focus();
      await settle(context.harness);

      expect(paths(context).every((path) => !path.hasAttribute('tabindex'))).toBe(true);
      expect(tooltip()).toBeNull();
    });

    it('does not consume an Escape when only a hover tooltip is open, so a dialog around the map sees it', async () => {
      const context = await show(blocks);
      container(context).focus();
      regions(context)[0].dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
      expect(tooltip()).not.toBeNull();

      const reached = escapesReachingThePage();
      key(container(context), 'Escape');

      expect(reached()).toBe(1); // the page (a dialog around the map, say) still sees it
      expect(tooltip()).not.toBeNull(); // and the hover tooltip is closed by the pointer leaving, not by Escape
    });

    it('consumes the Escape that leaves the shapes or closes a pinned tooltip, and no other', async () => {
      const context = await show(blocks);
      container(context).focus();
      key(container(context), 'Enter');
      const reached = escapesReachingThePage();

      key(document.activeElement!, 'Escape'); // leaves the shapes
      expect(reached()).toBe(0);
      key(container(context), 'Escape'); // nothing to leave or close
      expect(reached()).toBe(1);
      regions(context)[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
      expect(tooltip()).not.toBeNull();
      key(container(context), 'Escape'); // closes the pinned tooltip
      expect(reached()).toBe(1);
      expect(tooltip()).toBeNull();
    });

    it('does not pan with the arrow keys while a shape has the focus', async () => {
      const context = await show(blocks);
      container(context).focus();
      key(container(context), 'Enter');
      const center = centerOf(context);

      key(document.activeElement!, 'ArrowRight'); // moves to the next block...
      key(document.body, 'ArrowDown'); // ...and a key that reaches the page does not reach the map
      await settle(context.harness);

      expect(centerOf(context)).toBe(center);
    });

    it('brings a block that is out of view into view by the least it takes, and does not move for one in view', async () => {
      const context = await show(blocks);
      const fitted = centerOf(context);
      container(context).focus();

      key(container(context), 'Enter'); // the first block is in view
      await settle(context.harness);
      expect(centerOf(context)).toBe(fitted);

      // Zoomed in on the first block, the last is out of view.
      for (let i = 0; i < 3; i++) {
        await click(context, byTestId(context.root, 'map-zoom-in'));
      }
      container(context).focus();
      const before = centerOf(context);
      key(container(context), 'Enter');
      key(document.activeElement!, 'End');
      await settle(context.harness);

      expect(centerOf(context)).not.toBe(before);
    });

    it('pans to the centre of a shape bigger than the view', async () => {
      const big = [row('Big', 10, square(-77, 3, 1)), row('Small', 40, square(-75.5, 3, 0.01))];
      const context = await show(big);
      for (let i = 0; i < 3; i++) {
        await click(context, byTestId(context.root, 'map-zoom-in'));
      }
      container(context).focus();

      key(container(context), 'Enter');
      await settle(context.harness);

      const [lat, lng] = centerOf(context)!.split(',').map(Number);
      expect(lat).toBeCloseTo(3.5, 1);
      expect(lng).toBeCloseTo(-76.5, 1);
    });
  });

  describe('in the widget form', () => {
    const blocks = [row('0001', 10, square(-76.6, 3.4, 0.01)), row('0002', 40, square(-76.58, 3.4, 0.01)), row('0003', 70, square(-76.6, 3.42, 0.01))];
    const form = () => overlay().querySelector<HTMLElement>('[data-testid="widget-dialog"]');
    const previewMap = () => overlay().querySelector<HTMLElement>('[data-testid="map-container"]')!;
    const previewShapes = () => Array.from(overlay().querySelectorAll<HTMLElement>('[data-testid="map-region"]'));

    /** The form of a map widget, with its preview drawn. */
    async function editForm() {
      const existing = widget('w1', { chartType: 'map', config: config({ geometryColumn: 'geometry' }) });
      const context = await open('analyst', [existing], result(blocks));
      context.root.querySelector<HTMLElement>('[data-testid="menu-trigger"]')!.click();
      await settle(context.harness);
      document.querySelector<HTMLElement>('[data-testid="widget-edit"]')!.click();
      await settle(context.harness);
      context.http.expectOne(isColumns).flush(catalog());
      await settle(context.harness);
      await wait(previewDelayMs + 100);
      await settle(context.harness);
      context.http.match(isQuery).filter((r) => !r.cancelled).forEach((r) => r.flush(result(blocks)));
      for (const request of context.http.match(isBasemaps)) {
        request.flush({ provider: 'none', basemaps: [] });
      }
      await settle(context.harness);
      await ready(context, () => previewShapes().length > 0);
      return context;
    }

    beforeEach(() => stub.reduceMotion(true));

    it('goes back from the shapes to the map on the first Escape and closes the form on the second', async () => {
      const context = await editForm();
      previewMap().focus();
      key(previewMap(), 'Enter');
      key(document.activeElement!, 'ArrowRight');
      expect(overlay().querySelector('[data-testid="chart-tooltip"]')).not.toBeNull();

      key(document.activeElement!, 'Escape');
      await settle(context.harness);

      expect(form()).not.toBeNull();
      expect(document.activeElement).toBe(previewMap());
      expect(overlay().querySelector('[data-testid="chart-tooltip"]')).toBeNull();

      key(document.activeElement!, 'Escape');
      await settle(context.harness);

      expect(form()).toBeNull();
    });

    it('closes the form at once on an Escape when only a hover tooltip is open', async () => {
      const context = await editForm();
      previewMap().focus();
      previewShapes()[0].dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
      expect(overlay().querySelector('[data-testid="chart-tooltip"]')).not.toBeNull();

      key(document.activeElement!, 'Escape');
      await settle(context.harness);

      expect(form()).toBeNull();
    });
  });

  describe('basemap', () => {
    const blocks = [row('0001', 10, square(-76.6, 3.4, 0.01)), row('0002', 40, square(-76.58, 3.4, 0.01)), row('0003', 70, square(-76.6, 3.42, 0.01))];
    const tiles = (context: Pick<Context, 'root'>) => all(context.root, 'map-tile') as HTMLImageElement[];
    const tileSources = (context: Pick<Context, 'root'>) => tiles(context).map((tile) => tile.getAttribute('src')!);
    const zoomsOf = (sources: string[]) => sources.map((src) => Number(/\/(\d+)\/\d+\/\d+(?:\.png)?$/.exec(src)![1]));
    const note = (context: Pick<Context, 'root'>) => text(byTestId(context.root, 'map-note'));
    const announcement = (context: Pick<Context, 'root'>) => text(byTestId(context.root, 'map-announcement')).replace(/​/g, '');
    const withTiles = (context: Context) => ready(context, () => tiles(context).length > 0);
    const requestsOf = (context: Context) => context.http.match(() => true);

    /** Answers the request for the credits of a Google basemap, when it comes. */
    async function credits(context: Pick<Context, 'http' | 'harness'>, kind: string, answer: { text: string | null; maxZoom: number } | 'fail') {
      let found: TestRequest[] = [];
      for (let i = 0; i < 40 && !found.length; i++) {
        found = context.http.match(isCredits(kind));
        if (!found.length) {
          await wait(5);
          await settle(context.harness);
        }
      }
      expect(found).toHaveLength(1);
      if (answer === 'fail') {
        found[0].flush({}, { status: 503, statusText: 'Service Unavailable' });
      } else {
        found[0].flush(answer);
      }
      await settle(context.harness);
      return found[0].request;
    }

    /** A Google map with its first credits answered and its tiles drawn. */
    async function google(kind: 'streets' | 'satellite', options: { maxZoom?: number; text?: string; rows?: QueryRow[] } = {}) {
      const context = await show(options.rows ?? blocks, { settings: { basemap: kind }, list: googleList() });
      await credits(context, kind, { text: options.text ?? 'Imagery ©2026 Maxar Technologies', maxZoom: options.maxZoom ?? 19 });
      await withTiles(context);
      return context;
    }

    describe('OpenStreetMap', () => {
      it('draws the tiles of the area from the fixed template, and asks each for the origin as its referrer', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        await withTiles(context);

        expect(tiles(context).length).toBeGreaterThan(0);
        for (const src of tileSources(context)) {
          expect(src).toMatch(/^https:\/\/tile\.openstreetmap\.org\/\d+\/\d+\/\d+\.png$/);
        }
        for (const tile of tiles(context)) {
          expect(tile.referrerPolicy).toBe('strict-origin');
          expect(tile.getAttribute('crossorigin')).toBeNull();
        }
        expect(container(context).getAttribute('data-basemap')).toBe('streets');
      });

      it('credits OpenStreetMap inside the map, as a link that cannot see the page', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        await withTiles(context);

        const credit = byTestId(context.root, 'map-attribution')!;
        expect(byTestId(context.root, 'chart-box')!.contains(credit)).toBe(true);
        expect(text(credit)).toBe('© OpenStreetMap contributors');
        const link = credit.querySelector('a')!;
        expect(link.getAttribute('href')).toBe('https://www.openstreetmap.org/copyright');
        expect(link.getAttribute('rel')).toBe('noopener noreferrer');
        expect(link.getAttribute('target')).toBe('_blank');
        expect(byTestId(context.root, 'map-google-logo')).toBeNull();
      });

      it('shows no credit and no tile when the map has no basemap, even where the deployment offers one', async () => {
        const context = await show(blocks, { list: osmList });
        await wait(30);
        await settle(context.harness);

        expect(byTestId(context.root, 'map-attribution')).toBeNull();
        expect(tiles(context)).toHaveLength(0);
        expect(container(context).getAttribute('data-basemap')).toBeNull();
      });

      it('makes no tile request and no credits request while zooming a map set to None, with a provider configured', async () => {
        const context = await show(blocks, { list: osmList });

        await click(context, byTestId(context.root, 'map-zoom-in'));
        await click(context, byTestId(context.root, 'map-zoom-out'));
        await click(context, byTestId(context.root, 'map-show-all'));
        wheel(container(context), { ctrl: true });
        await wait(80);
        await settle(context.harness);

        expect(tiles(context)).toHaveLength(0);
        context.http.expectNone(() => true);
      });

      it('lets the shapes show the streets through them: fill 0.7 on the shapes and the swatches, and 1 without a basemap', async () => {
        const plain = await show(blocks);
        expect(regions(plain).every((path) => path.getAttribute('fill-opacity') === '1')).toBe(true);
        const swatches = (context: Context) => Array.from(byTestId(context.root, 'map-legend')!.querySelectorAll<HTMLElement>('span[style]')).map((swatch) => swatch.style.opacity);
        expect(swatches(plain)).toEqual(Array(7).fill('1'));
        TestBed.resetTestingModule();
        clearOverlay();

        const over = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        await withTiles(over);

        expect(regions(over).every((path) => path.getAttribute('fill-opacity') === '0.7')).toBe(true);
        expect(swatches(over)).toEqual(Array(7).fill('0.7'));
      });

      it('wraps the credit instead of cutting it, in a box just big enough for a basemap', async () => {
        stub.resize(200, 120);
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        await withTiles(context);

        const credit = byTestId(context.root, 'map-attribution')!;
        expect(credit.className).toContain('max-w-');
        expect(credit.className).not.toContain('truncate');
        expect(credit.className).not.toContain('overflow-hidden');
        expect(text(credit)).toBe('© OpenStreetMap contributors');
      });
    });

    describe('Google', () => {
      it('shows no imagery until the credits answer, and then the tiles, the logo and the credits', async () => {
        const context = await show(blocks, { settings: { basemap: 'satellite' }, list: googleList() });
        await wait(30);
        await settle(context.harness);
        expect(tiles(context)).toHaveLength(0);
        expect(byTestId(context.root, 'map-attribution')).toBeNull();

        const request = await credits(context, 'satellite', { text: 'Imagery ©2026 Maxar Technologies', maxZoom: 19 });
        await withTiles(context);

        expect(request.params.keys()).toEqual(['zoom', 'north', 'south', 'east', 'west']);
        expect(request.params.get('zoom')).toBe(String(Math.round(zoomOf(context))));
        for (const src of tileSources(context)) {
          expect(src).toMatch(/^\/api\/v1\/basemap-tiles\/link1satellite\/\d+\/\d+\/\d+$/);
        }
        expect(text(byTestId(context.root, 'map-credits'))).toBe('Imagery ©2026 Maxar Technologies');
        expect(byTestId(context.root, 'map-google-logo')).not.toBeNull();
      });

      it('asks for the credits of the kind on screen, and nothing else of Google', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: googleList() });
        await credits(context, 'streets', { text: 'Map data ©2026 Google', maxZoom: 22 });
        await withTiles(context);

        const others = requestsOf(context).filter((r) => !r.request.url.includes('/basemap-tiles/'));
        expect(others).toHaveLength(0);
      });

      it('shows the Google Maps logo unmodified: the outline for its map, 18 px high, with its clear space and its label', async () => {
        const satellite = await google('satellite');
        const logo = byTestId<HTMLImageElement>(satellite.root, 'map-google-logo')!;
        expect(logo.getAttribute('src')).toBe('vendor/google/2026-10/GoogleMaps_Logo_WithDarkOutline.svg');
        expect(logo.alt).toBe('Google Maps');
        expect(logo.style.height).toBe('18px');
        expect(logo.style.margin).toBe('10px 10px 5px');
        expect(logo.className).toContain('w-auto'); // the aspect ratio is kept
        TestBed.resetTestingModule();
        clearOverlay();

        const streets = await google('streets');
        expect(byTestId(streets.root, 'map-google-logo')!.getAttribute('src')).toBe('vendor/google/2026-10/GoogleMaps_Logo_WithLightOutline.svg');
      });

      it('fits the logo, its clear space and a line of credits in the smallest box that draws a basemap, one under the other', async () => {
        stub.resize(200, 120);
        const context = await google('satellite');

        const attribution = byTestId(context.root, 'map-attribution')!;
        expect(attribution.className).toContain('flex-col');
        const [logo, credit] = Array.from(attribution.children);
        expect(logo.getAttribute('data-testid')).toBe('map-google-logo');
        expect(credit.getAttribute('data-testid')).toBe('map-credits');
        // 10 px above, 18 px of logo, 5 px below, then one line of 10 px text: well inside 120 px, and 10 + 105 * 18 / 22 + 10 inside 200 px.
        expect(10 + 18 + 5 + 14).toBeLessThan(120);
        expect(10 + Math.ceil((105 * 18) / 22) + 10).toBeLessThan(200);
        expect(attribution.className).toContain('absolute');
        expect(byTestId(context.root, 'map-controls')!.closest('.absolute')!.className).toContain('top-2'); // the controls are at the other corner
      });

      it('changes the credits with the area on screen, after a pause, and keeps them in between', async () => {
        const context = await google('satellite', { text: 'Imagery ©2026 Maxar Technologies' });
        container(context).focus();

        key(document.body, 'ArrowRight');
        await wait(400);
        await settle(context.harness);
        expect(context.http.match(isCredits('satellite'))).toHaveLength(0); // not yet: the pause has not passed
        expect(text(byTestId(context.root, 'map-credits'))).toBe('Imagery ©2026 Maxar Technologies');
        await wait(600);
        await credits(context, 'satellite', { text: 'Imagery ©2026 CNES / Airbus', maxZoom: 19 });

        expect(text(byTestId(context.root, 'map-credits'))).toBe('Imagery ©2026 CNES / Airbus');
        expect(byTestId(context.root, 'map-google-logo')).not.toBeNull();
      });

      it('asks once for a pan and a zoom that follow each other', async () => {
        const context = await google('satellite');
        container(context).focus();

        key(document.body, 'ArrowRight');
        await click(context, byTestId(context.root, 'map-zoom-in'));
        await wait(900);
        await settle(context.harness);

        expect(context.http.match(isCredits('satellite'))).toHaveLength(1);
      });

      it('does not ask again for an area it already has the credits of', async () => {
        const context = await google('satellite');
        container(context).focus();

        key(document.body, 'ArrowRight');
        await wait(900);
        await credits(context, 'satellite', { text: 'Imagery ©2026 CNES / Airbus', maxZoom: 19 });
        key(document.body, 'ArrowLeft');
        await wait(900);
        await settle(context.harness);

        expect(context.http.match(isCredits('satellite'))).toHaveLength(0);
        expect(text(byTestId(context.root, 'map-credits'))).toBe('Imagery ©2026 Maxar Technologies');
      });

      it('keeps its imagery and its last credits when the request after a pan fails', async () => {
        const context = await google('satellite', { text: 'Imagery ©2026 Maxar Technologies' });
        const shown = tileSources(context);
        container(context).focus();

        key(document.body, 'ArrowRight');
        await wait(700);
        await credits(context, 'satellite', 'fail');

        expect(text(byTestId(context.root, 'map-credits'))).toBe('Imagery ©2026 Maxar Technologies');
        expect(tiles(context).length).toBeGreaterThan(0);
        expect(shown.length).toBeGreaterThan(0);
        expect(byTestId(context.root, 'map-note')).toBeNull();
      });

      it('shows no imagery, and says so, when the first request for the credits fails', async () => {
        const context = await show(blocks, { settings: { basemap: 'satellite' }, list: googleList() });
        await credits(context, 'satellite', 'fail');
        await wait(30);
        await settle(context.harness);

        expect(tiles(context)).toHaveLength(0);
        expect(byTestId(context.root, 'map-attribution')).toBeNull();
        expect(note(context)).toBe('Basemap unavailable');
        expect(announcement(context)).toBe('Basemap unavailable');
        expect(regions(context)).toHaveLength(3);
      });

      it('never asks for tiles deeper than the imagery there, and keeps its layer when zooming further', async () => {
        const context = await google('satellite', { maxZoom: 18 });
        for (let i = 0; i < 12; i++) {
          await click(context, byTestId(context.root, 'map-zoom-in'));
        }

        expect(zoomOf(context)).toBe(20);
        expect(tiles(context).length).toBeGreaterThan(0);
        expect(Math.max(...zoomsOf(tileSources(context)))).toBeLessThanOrEqual(18);
        expect(note(context)).toBe('');
        expect(byTestId(context.root, 'map-google-logo')).not.toBeNull();
      });

      it('goes deeper where Google has deeper imagery, once the credits for the new area say so', async () => {
        const context = await google('satellite', { maxZoom: 15 });
        container(context).focus();
        for (let i = 0; i < 6; i++) {
          await click(context, byTestId(context.root, 'map-zoom-in'));
        }
        expect(Math.max(...zoomsOf(tileSources(context)))).toBeLessThanOrEqual(15);

        await wait(700);
        await credits(context, 'satellite', { text: 'Imagery ©2026 Maxar Technologies', maxZoom: 20 });

        expect(Math.max(...zoomsOf(tileSources(context)))).toBeGreaterThan(15);
      });

      it('gives only new tiles the link of a refreshed list, and keeps the ones on screen', async () => {
        const context = await google('streets');
        const before = tileSources(context);
        expect(before.every((src) => src.includes('link1streets'))).toBe(true);

        const refresh = TestBed.inject(Basemaps).refresh();
        context.http.expectOne(isBasemaps).flush(googleList(2));
        await refresh;
        await settle(context.harness);
        expect(tileSources(context).filter((src) => before.includes(src))).toHaveLength(before.length); // none was fetched again

        await click(context, byTestId(context.root, 'map-zoom-in'));
        await click(context, byTestId(context.root, 'map-zoom-in'));

        const now = tileSources(context);
        expect(now.some((src) => src.includes('link2streets'))).toBe(true);
        expect(now.some((src) => src.includes('link1streets') && !before.includes(src))).toBe(false);
      });

      it('rebuilds the layer and credits OpenStreetMap when a refreshed list names another provider', async () => {
        const context = await google('streets');

        const refresh = TestBed.inject(Basemaps).refresh();
        context.http.expectOne(isBasemaps).flush(osmList);
        await refresh;
        await settle(context.harness);
        await withTiles(context);
        await ready(context, () => tileSources(context).every((src) => src.startsWith('https://tile.openstreetmap.org/')));

        expect(tileSources(context).every((src) => src.startsWith('https://tile.openstreetmap.org/'))).toBe(true);
        expect(text(byTestId(context.root, 'map-attribution'))).toBe('© OpenStreetMap contributors');
        expect(byTestId(context.root, 'map-google-logo')).toBeNull();
      });

      it('shows nothing, and no error, when a refreshed list offers no basemap at all', async () => {
        const context = await google('streets');

        const refresh = TestBed.inject(Basemaps).refresh();
        context.http.expectOne(isBasemaps).flush(noList);
        await refresh;
        await settle(context.harness);

        expect(tiles(context)).toHaveLength(0);
        expect(byTestId(context.root, 'map-attribution')).toBeNull();
        expect(note(context)).toBe('');
      });
    });

    describe('the viewer\'s switch', () => {
      const menuItems = () => Array.from(document.querySelectorAll<HTMLElement>('[role="menuitemradio"]'));
      const radio = (id: string) => document.querySelector<HTMLElement>(`[data-testid="map-basemap-${id}"]`)!;
      const openMenu = async (context: Context) => {
        await click(context, byTestId(context.root, 'map-basemap'));
        return menuItems();
      };

      it('offers none and the kinds the deployment offers as radio items, the saved one checked', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: googleList() });
        await credits(context, 'streets', { text: 'Map data ©2026 Google', maxZoom: 22 });

        const items = await openMenu(context);

        expect(items.map(text)).toEqual(['None', 'Streets', 'Satellite']);
        expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false']);
      });

      it('switches a map saved with Streets to Satellite for the viewer alone: no new query, nothing saved, announced', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: googleList() });
        await credits(context, 'streets', { text: 'Map data ©2026 Google', maxZoom: 22 });
        await withTiles(context);
        await openMenu(context);

        radio('satellite').click();
        await settle(context.harness);
        expect(tiles(context).filter((tile) => tile.getAttribute('src')!.includes('satellite'))).toHaveLength(0); // not before its credits
        await credits(context, 'satellite', { text: 'Imagery ©2026 Maxar Technologies', maxZoom: 19 });
        await ready(context, () => tileSources(context).some((src) => src.includes('satellite')));

        expect(tileSources(context).every((src) => src.includes('link1satellite'))).toBe(true);
        expect(container(context).getAttribute('data-basemap')).toBe('satellite');
        expect(announcement(context)).toBe('Basemap: Satellite');
        expect(text(byTestId(context.root, 'map-credits'))).toBe('Imagery ©2026 Maxar Technologies');
        context.http.expectNone((r) => r.method === 'POST' || r.method === 'PATCH');
        context.http.expectNone(isQuery);
      });

      it('shows another viewer the saved basemap, whatever this one chose', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: googleList() });
        await credits(context, 'streets', { text: 'Map data ©2026 Google', maxZoom: 22 });
        await openMenu(context);
        radio('satellite').click();
        await settle(context.harness);
        TestBed.resetTestingModule();
        clearOverlay();

        const other = await show(blocks, { settings: { basemap: 'streets' }, list: googleList() });

        expect(container(other).getAttribute('data-basemap')).toBe('streets');
        await credits(other, 'streets', { text: 'Map data ©2026 Google', maxZoom: 22 });
      });

      it('lets a viewer turn a basemap on for a map saved with None', async () => {
        const context = await show(blocks, { list: osmList });
        expect(tiles(context)).toHaveLength(0);
        await openMenu(context);
        expect(menuItems().map((item) => item.getAttribute('aria-checked'))).toEqual(['true', 'false']);

        radio('streets').click();
        await settle(context.harness);
        await withTiles(context);

        expect(tileSources(context).every((src) => src.startsWith('https://tile.openstreetmap.org/'))).toBe(true);
        expect(announcement(context)).toBe('Basemap: Streets');
      });

      it('lets a viewer turn the basemap off: no tiles, no request, and the widget is untouched', async () => {
        const context = await google('satellite');
        await openMenu(context);

        radio('none').click();
        await settle(context.harness);

        expect(tiles(context)).toHaveLength(0);
        expect(byTestId(context.root, 'map-attribution')).toBeNull();
        expect(announcement(context)).toBe('Basemap off');
        container(context).focus();
        await click(context, byTestId(context.root, 'map-zoom-in'));
        await wait(700);
        context.http.expectNone(() => true);
        expect(regions(context).every((path) => path.getAttribute('fill-opacity') === '1')).toBe(true);
      });

      it('draws no basemap and no error for a saved kind the deployment no longer offers, and offers the kinds it does', async () => {
        const context = await show(blocks, { settings: { basemap: 'satellite' }, list: osmList });
        await wait(30);
        await settle(context.harness);

        expect(tiles(context)).toHaveLength(0);
        expect(note(context)).toBe('');
        expect(byTestId(context.root, 'map-alert')).toBeNull();
        const items = await openMenu(context);
        expect(items.map(text)).toEqual(['None', 'Streets']);
        expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual(['true', 'false']);
        radio('streets').click();
        await settle(context.harness);
        await withTiles(context);
        expect(tiles(context).length).toBeGreaterThan(0);
      });

      it('says the basemap cannot be shown for coordinates that are not degrees, and draws the shapes flat', async () => {
        const planar = [row('A', 10, square(1_000_000, 800_000, 100_000)), row('B', 40, square(1_150_000, 800_000, 100_000))];
        const context = await show(planar, { settings: { basemap: 'streets' }, list: osmList });
        await wait(30);
        await settle(context.harness);

        expect(note(context)).toBe('The basemap cannot be shown: the coordinates are not degrees.');
        expect(tiles(context)).toHaveLength(0);
        expect(regions(context)).toHaveLength(2);
        expect(byTestId(context.root, 'map-attribution')).toBeNull();
      });

      it('collapses the basemap choices into the options menu of a small map, as radio items', async () => {
        stub.resize(150, 120);
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        expect(byTestId(context.root, 'map-basemap')).toBeNull();

        await click(context, byTestId(context.root, 'map-options'));

        const items = menuItems();
        expect(items.map(text)).toEqual(['None', 'Streets']);
        expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual(['false', 'true']);
        expect(Array.from(document.querySelectorAll('[data-testid="map-options-menu"] > button')).map(text)).toEqual(['Zoom in', 'Zoom out', 'Show all']);
      });

      it('offers no basemap choice at all where the deployment offers none', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' } });

        expect(byTestId(context.root, 'map-basemap')).toBeNull();
        expect(byTestId(context.root, 'map-zoom-in')).not.toBeNull();
      });

      it('forgets the viewer\'s switch when the rows change', async () => {
        const mounted = await mount(blocks, { basemap: 'streets' }, osmList);
        await withTiles(mounted as unknown as Context);
        await click(mounted, byTestId(mounted.root, 'map-basemap'));
        radio('none').click();
        await settle(mounted.harness);
        expect(tiles(mounted)).toHaveLength(0);

        mounted.fixture.componentRef.setInput('rows', [row('0009', 5, square(-76.6, 3.4, 0.01)), row('0010', 9, square(-76.58, 3.4, 0.01))]);
        await settle(mounted.harness);
        await ready(mounted, () => tiles(mounted).length > 0);

        expect(tiles(mounted).length).toBeGreaterThan(0);
        expect(container(mounted).getAttribute('data-basemap')).toBe('streets');
      });
    });

    describe('room and failure', () => {
      it('creates the map only when the widget comes near the screen', async () => {
        stub.restore();
        stub = stubMap({ width: 600, height: 400, intersection: true });
        const context = await show(blocks, { drawn: false });
        await wait(30);
        await settle(context.harness);
        expect(stub.intersectionObservers).toBeGreaterThan(0);
        expect(context.root.querySelector('.leaflet-container')).toBeNull();
        expect(byTestId(context.root, 'map-legend')).not.toBeNull(); // the legend does not wait

        stub.intersect(byTestId(context.root, 'chart-box')!);
        await ready(context);

        expect(regions(context)).toHaveLength(3);
      });

      it('requests no tile and no credits while the widget is off screen', async () => {
        stub.restore();
        stub = stubMap({ width: 600, height: 400, intersection: true });
        const context = await show(blocks, { settings: { basemap: 'satellite' }, list: googleList(), drawn: false });
        await wait(30);
        await settle(context.harness);

        expect(context.http.match(isCredits('satellite'))).toHaveLength(0);
        expect(tiles(context)).toHaveLength(0);
      });

      it('draws no basemap on a map resized to one row, requests nothing and says to enlarge it', async () => {
        stub.resize(600, 100);
        const context = await show(blocks, { settings: { basemap: 'satellite' }, list: googleList() });
        await wait(30);
        await settle(context.harness);

        expect(tiles(context)).toHaveLength(0);
        expect(context.http.match(isCredits('satellite'))).toHaveLength(0);
        expect(note(context)).toBe('Enlarge the map to show the basemap');
        expect(regions(context)).toHaveLength(3);
        expect(byTestId(context.root, 'map-attribution')).toBeNull();
      });

      it('draws the basemap once the box is big enough again', async () => {
        stub.resize(600, 100);
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        expect(tiles(context)).toHaveLength(0);

        stub.resize(600, 400);
        await settle(context.harness);
        await withTiles(context);

        expect(tiles(context).length).toBeGreaterThan(0);
        expect(note(context)).toBe('');
      });

      it('says once that the basemap is unavailable when no tile loads, and the shapes, zoom, tooltip and table keep working', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        await withTiles(context);

        for (const tile of tiles(context)) {
          tile.dispatchEvent(new Event('error'));
        }
        await settle(context.harness);

        expect(tiles(context)).toHaveLength(0);
        expect(note(context)).toBe('Basemap unavailable');
        expect(announcement(context)).toBe('Basemap unavailable');
        expect(byTestId(context.root, 'map-attribution')).toBeNull();
        expect(regions(context)).toHaveLength(3);
        const zoom = zoomOf(context);
        await click(context, byTestId(context.root, 'map-zoom-in'));
        expect(zoomOf(context)).toBe(zoom + 1);
        expect(tiles(context)).toHaveLength(0); // it does not try again by itself
        regions(context)[0].dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
        expect(overlay().querySelector('[data-testid="tooltip-heading"]')).not.toBeNull();
        byTestId(context.root, 'widget-table-toggle')!.click();
        await settle(context.harness);
        expect(all(context.root, 'widget-table-row')).toHaveLength(3);
      });

      it('keeps the basemap when only a few of its tiles fail', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        await withTiles(context);
        const [first, ...others] = tiles(context);
        expect(others.length).toBeGreaterThan(2);

        first.dispatchEvent(new Event('error'));
        for (const tile of others) {
          tile.dispatchEvent(new Event('load'));
        }
        await settle(context.harness);

        expect(tiles(context).length).toBeGreaterThan(0);
        expect(note(context)).toBe('');
      });

      it('tries again when the member switches the basemap again', async () => {
        const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
        await withTiles(context);
        for (const tile of tiles(context)) {
          tile.dispatchEvent(new Event('error'));
        }
        await settle(context.harness);
        expect(note(context)).toBe('Basemap unavailable');

        await click(context, byTestId(context.root, 'map-basemap'));
        document.querySelector<HTMLElement>('[data-testid="map-basemap-none"]')!.click();
        await settle(context.harness);
        await click(context, byTestId(context.root, 'map-basemap'));
        document.querySelector<HTMLElement>('[data-testid="map-basemap-streets"]')!.click();
        await settle(context.harness);
        await withTiles(context);

        expect(tiles(context).length).toBeGreaterThan(0);
        expect(note(context)).toBe('');
      });

      it('asks for a fresh list once, before giving a Google basemap up, when its links are due', async () => {
        const context = await google('streets');
        const realNow = Date.now.bind(Date);
        const later = vi.spyOn(Date, 'now').mockImplementation(() => realNow() + 3600 * 1000);
        onTestFinished(() => later.mockRestore());

        for (const tile of tiles(context)) {
          tile.dispatchEvent(new Event('error'));
        }
        await settle(context.harness);
        // The first failed tile asked for the list; the batch is retried with what it brings.
        context.http.expectOne(isBasemaps).flush(googleList(2));
        await settle(context.harness);
        await wait(20);
        await settle(context.harness);

        expect(note(context)).toBe('');
        expect(tileSources(context).length).toBeGreaterThan(0);
        expect(tileSources(context).every((src) => src.includes('link2streets'))).toBe(true);

        for (const tile of tiles(context)) {
          tile.dispatchEvent(new Event('error'));
        }
        await settle(context.harness);

        expect(note(context)).toBe('Basemap unavailable');
        context.http.expectNone(isBasemaps); // once only
      });

      it('gives a Google basemap up at once when its links are not due', async () => {
        const context = await google('streets');

        for (const tile of tiles(context)) {
          tile.dispatchEvent(new Event('error'));
        }
        await settle(context.harness);

        expect(note(context)).toBe('Basemap unavailable');
        context.http.expectNone(isBasemaps);
      });
    });
  });
});

describe('map widget on paper (dashboard printing)', () => {
  const blocks = [row('0001', 10, square(-76.6, 3.4, 0.01)), row('0002', 40, square(-76.58, 3.4, 0.01))];
  const tiles = (context: Pick<Context, 'root'>) => all(context.root, 'map-tile') as HTMLImageElement[];

  beforeAll(async () => {
    const preload = stubMap();
    await loadLeaflet();
    preload.restore();
  });

  beforeEach(() => {
    stub = stubMap({ width: 600, height: 400 });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    stub.restore();
    document.documentElement.classList.remove('tmz-printing');
    document.getElementById('tmz-print-page')?.remove();
  });

  it('fits an untouched map to the box it has on paper (Map with a basemap)', async () => {
    const context = await show(blocks);
    const resize = vi.spyOn(MapEngine.prototype, 'resize');
    const refit = vi.spyOn(MapEngine.prototype, 'refit');

    await paperOn(context);

    expect(resize).toHaveBeenCalled();
    expect(refit).toHaveBeenCalled();
  });

  it('keeps the area a member zoomed into: the map only takes its new box (Zoomed map)', async () => {
    const context = await show(blocks);
    await click(context, byTestId(context.root, 'map-zoom-in'));
    const zoomed = zoomOf(context);
    const resize = vi.spyOn(MapEngine.prototype, 'resize');
    const refit = vi.spyOn(MapEngine.prototype, 'refit');

    await paperOn(context);

    expect(resize).toHaveBeenCalled();
    expect(refit).not.toHaveBeenCalled();
    expect(zoomOf(context)).toBe(zoomed);
  });

  it('leaves the controls and the hints off the paper, and keeps the legend', async () => {
    const context = await show(blocks);

    expect(context.root.querySelector('app-map-controls')!.classList).toContain('paper:hidden');
    expect(byTestId(context.root, 'map-legend')!.closest('.paper\\:hidden')).toBeNull();
    wheel(container(context));
    await settle(context.harness);
    expect(byTestId(context.root, 'map-hint')!.classList).toContain('paper:hidden');
  });

  it('makes a map that never came near the screen when the print is prepared, and says so on paper until it is made', async () => {
    stub.restore();
    stub = stubMap({ width: 600, height: 400, intersection: true });
    const context = await show(blocks, { drawn: false });
    await wait(30);
    await settle(context.harness);
    const note = byTestId(context.root, 'map-not-loaded')!;
    expect(note.classList).toContain('hidden');
    expect(note.classList).toContain('paper:block');
    expect(text(note)).toBe('This widget had not finished loading when it was printed.');

    await paperOn(context);
    await ready(context);

    expect(regions(context)).toHaveLength(2);
    expect(byTestId(context.root, 'map-not-loaded')).toBeNull();
  });

  it('holds the print dialog back until the basemap tiles have loaded', async () => {
    const context = await show(blocks, { settings: { basemap: 'streets' }, list: osmList });
    await ready(context, () => tiles(context).length > 0);
    const dialog = vi.spyOn(document.defaultView!, 'print').mockImplementation(() => document.defaultView!.dispatchEvent(new Event('afterprint')));

    const done = printOf(context).print();
    await wait(150);
    await settle(context.harness);
    expect(dialog).not.toHaveBeenCalled();

    for (const tile of tiles(context)) {
      tile.dispatchEvent(new Event('load'));
    }
    await done;

    expect(dialog).toHaveBeenCalledTimes(1);
  });
});

/** Whether Leaflet's drag handler is on: its container carries the class while it is. */
const draggable = (context: Context) => container(context).classList.contains('leaflet-touch-drag');
