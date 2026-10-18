import { MapStub, key, stubMap } from '../../../../testing/leaflet';
import { EngineView, MapEngine, MapEvents, ShapeInput } from './map-engine';
import { parseWkt } from './wkt';

const none: MapEvents = { hover: () => undefined, move: () => undefined, leave: () => undefined, pick: () => undefined, blank: () => undefined, motion: () => undefined, settled: () => undefined };

const shape = (wkt: string, step = 4): ShapeInput => ({ geometry: parseWkt(wkt)!, step });
const square = (x: number, y: number, size: number) => `POLYGON ((${x} ${y}, ${x + size} ${y}, ${x + size} ${y + size}, ${x} ${y + size}, ${x} ${y}))`;

describe('map engine', () => {
  let stub: MapStub;
  let host: HTMLElement;
  let created: MapEngine | null = null;

  beforeEach(() => {
    stub = stubMap({ width: 600, height: 400 });
    host = document.createElement('div');
    document.body.appendChild(host);
  });

  afterEach(() => {
    created?.destroy();
    created = null;
    host.remove();
    stub.restore();
  });

  async function open(shapes: ShapeInput[], events: Partial<MapEvents> = {}, view: EngineView | null = null) {
    created = await MapEngine.create(host, { kind: 'degrees', shapes, view, reducedMotion: true, events: { ...none, ...events } });
    return created;
  }

  async function openPlanar(shapes: ShapeInput[]) {
    created = await MapEngine.create(host, { kind: 'planar', shapes, view: null, reducedMotion: true, events: none });
    return created;
  }

  it('links the stylesheet once, answers its load, and makes a Leaflet container of the host', async () => {
    await open([shape(square(-77, 3, 1))]);

    expect(host.classList.contains('leaflet-container')).toBe(true);
    const links = document.head.querySelectorAll('link[href="vendor/leaflet/1.9.4/leaflet.css"]');
    expect(links).toHaveLength(1);
  });

  describe('shapes', () => {
    it('draws a polygon as a region and a point as a marker, with the class of their step and the base options', async () => {
      await open([shape(square(-77, 3, 1), 7), shape('POINT (-76.5 3.5)', 2)]);

      const region = host.querySelector('[data-testid="map-region"]')!;
      const point = host.querySelector('[data-testid="map-point"]')!;
      expect(region.tagName.toLowerCase()).toBe('path');
      expect(region.classList.contains('tmz-seq-7')).toBe(true);
      expect(region.getAttribute('stroke-width')).toBe('1');
      expect(region.getAttribute('fill-opacity')).toBe('1');
      expect(region.getAttribute('fill-rule')).toBe('evenodd');
      expect(point.classList.contains('tmz-seq-2')).toBe(true);
      expect(point.getAttribute('stroke-width')).toBe('2');
      expect(point.getAttribute('fill-opacity')).toBe('1');
      expect(point.getAttribute('d')).toContain('a6,6'); // a circle of radius 6, drawn as an arc
    });

    it('draws a hole as a second ring of the same path', async () => {
      await open([shape('POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (3 3, 7 3, 7 7, 3 7, 3 3))')]);

      expect(host.querySelector('[data-testid="map-region"]')!.getAttribute('d')!.match(/M/g)).toHaveLength(2);
    });

    it('lowers the fill when there is a basemap under the shapes, and raises it again', async () => {
      const engine = await open([shape(square(-77, 3, 1))]);

      engine.setBackdrop(true);
      expect(host.querySelector('[data-testid="map-region"]')!.getAttribute('fill-opacity')).toBe('0.7');
      engine.setBackdrop(false);
      expect(host.querySelector('[data-testid="map-region"]')!.getAttribute('fill-opacity')).toBe('1');
    });
  });

  describe('fit', () => {
    it('fits metre coordinates of about 100 km at a negative zoom, filling the box', async () => {
      const engine = await openPlanar([shape(square(1_000_000, 800_000, 100_000)), shape(square(1_050_000, 850_000, 50_000))]);

      const view = engine.view();
      expect(view.zoom).toBeLessThanOrEqual(-7);
      expect(view.zoom).toBeGreaterThan(-12);
      // The 100 km square fills the box: about as high as the box is (400 px), not a few pixels.
      const d = host.querySelector('[data-testid="map-region"]')!.getAttribute('d')!;
      const pairs = Array.from(d.matchAll(/(-?[\d.]+) (-?[\d.]+)/g));
      const xs = pairs.map((m) => Number(m[1]));
      const ys = pairs.map((m) => Number(m[2]));
      expect(Math.max(...ys) - Math.min(...ys)).toBeGreaterThan(300);
      expect(Math.max(...xs) - Math.min(...xs)).toBeGreaterThan(300);
      expect(view.minZoom).toBe(view.zoom - 3);
      expect(view.maxZoom).toBe(view.zoom + 10);
    });

    it('opens a map of degrees at the zoom that fits, three levels from its minimum', async () => {
      const engine = await open([shape(square(-77.5, 3, 1))]);

      const view = engine.view();
      expect(view.zoom).toBeGreaterThan(6);
      expect(view.zoom).toBeLessThanOrEqual(16);
      expect(view.minZoom).toBe(Math.max(0, view.zoom - 3));
      expect(view.maxZoom).toBe(20);
      expect(view.lng).toBeCloseTo(-77, 1);
      expect(view.lat).toBeCloseTo(3.5, 1);
    });

    it('opens a lone point at zoom 16, centred on it', async () => {
      const engine = await open([shape('POINT (-76.53 3.43)')]);

      const view = engine.view();
      expect(view.zoom).toBe(16);
      expect(view.lat).toBeCloseTo(3.43, 4);
      expect(view.lng).toBeCloseTo(-76.53, 4);
    });

    it('can pan away from a lone point: the room to move is not the point\'s empty box', async () => {
      const engine = await open([shape('POINT (-76.53 3.43)')]);
      host.focus();
      const before = engine.view();

      key(document.body, 'ArrowRight');
      await new Promise((resolve) => setTimeout(resolve, 400)); // the pan is animated

      expect(engine.view().lng).toBeGreaterThan(before.lng);
    });

    it('can pan away from points on one line', async () => {
      const engine = await open([shape('POINT (-76.6 3.43)'), shape('POINT (-76.4 3.43)')]);
      host.focus();
      const before = engine.view();

      key(document.body, 'ArrowDown');
      await new Promise((resolve) => setTimeout(resolve, 400));

      expect(engine.view().lat).toBeLessThan(before.lat);
    });

    it('shows all again with the fitted view, whatever the member did, and says it was not the member', async () => {
      const calls: boolean[] = [];
      const engine = await open([shape(square(-77.5, 3, 1))], { settled: (_view, byUser) => calls.push(byUser) });
      const fitted = engine.view();

      engine.zoomIn();
      engine.zoomIn();
      expect(engine.view().zoom).toBeGreaterThan(fitted.zoom);
      engine.showAll();

      expect(engine.view().zoom).toBe(fitted.zoom);
      expect(calls.at(-1)).toBe(false);
      expect(calls.slice(-3, -1).every(Boolean)).toBe(true);
    });

    it('never zooms out more than three levels past the fit', async () => {
      const engine = await open([shape(square(-77.5, 3, 1))]);
      const fitted = engine.view();

      for (let i = 0; i < 10; i++) {
        engine.zoomOut();
      }

      expect(engine.view().zoom).toBe(fitted.zoom - 3);
    });

    it('opens at a view it is given instead of the fit', async () => {
      const fitted = await open([shape(square(-77.5, 3, 1))]);
      const restore = { ...fitted.view(), zoom: fitted.view().zoom + 2 };
      fitted.destroy();

      const engine = await open([shape(square(-77.5, 3, 1))], {}, restore);

      expect(engine.view().zoom).toBe(restore.zoom);
    });
  });

  describe('tiles', () => {
    const sources = () => Array.from(host.querySelectorAll('[data-testid="map-tile"]')).map((tile) => tile.getAttribute('src')!);
    const zooms = () => sources().map((src) => Number(/\/(\d+)\/\d+\/\d+$/.exec(src)![1]));
    const template = '/tiles/{z}/{x}/{y}';

    it('enlarges the deepest tiles where the provider has no deeper ones, and goes deeper when told it can', async () => {
      const engine = await open([shape(square(-77.5, 3, 0.03))]);
      engine.setTiles({ template, maxNativeZoom: 15 });
      for (let i = 0; i < 8; i++) {
        engine.zoomIn();
      }
      expect(engine.view().zoom).toBe(20);
      expect(sources().length).toBeGreaterThan(0);
      expect(Math.max(...zooms())).toBe(15);

      engine.updateTiles({ maxNativeZoom: 20 });

      expect(sources().length).toBeGreaterThan(0);
      expect(Math.max(...zooms())).toBe(20);
    });

    it('keeps the tiles on screen when only the link changes', async () => {
      const engine = await open([shape(square(-77.5, 3, 0.03))]);
      engine.setTiles({ template: '/a/{z}/{x}/{y}', maxNativeZoom: 19 });
      const before = sources();

      engine.updateTiles({ template: '/b/{z}/{x}/{y}' });

      expect(sources()).toEqual(before);
      expect(engine.tileTemplate()).toBe('/b/{z}/{x}/{y}');
    });
  });

  describe('events', () => {
    it('tells the widget which shape the pointer is on, and where in the container', async () => {
      const seen: string[] = [];
      await open([shape(square(-77.5, 3, 1)), shape(square(-76, 3, 1))], {
        hover: (index, at) => seen.push(`hover ${index} ${typeof at.x}`),
        leave: (index) => seen.push(`leave ${index}`),
        pick: (index) => seen.push(`pick ${index}`),
        blank: () => seen.push('blank'),
      });
      const [first, second] = Array.from(host.querySelectorAll('[data-testid="map-region"]'));

      second.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
      second.dispatchEvent(new MouseEvent('mouseout', { bubbles: true }));
      first.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      host.dispatchEvent(new MouseEvent('click', { bubbles: true }));

      expect(seen).toEqual(['hover 1 number', 'leave 1', 'pick 0', 'blank']);
    });
  });
});
