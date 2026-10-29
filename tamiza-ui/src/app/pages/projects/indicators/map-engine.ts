import type { FeatureGroup, LatLng, LatLngBounds, LeafletMouseEvent, Map as LeafletMap, Path, TileLayer } from 'leaflet';
import { Box, MapKind, boxOf, fitZoomOf, provisionalLimits, roomAround, zoomLimits } from './map-geometry';
import { Geometry, Position, positionsOf } from './wkt';

// The only file that imports Leaflet (design D1, D2). Leaflet is the engine: it keeps the tile grid, the gestures, the zoom and
// the pan. Everything a member reads or presses (controls, tooltip, attribution, notices) is Angular's, so nothing here ever
// calls a Leaflet API that writes markup (bindTooltip, bindPopup, setContent, the control classes). A later Leaflet 2 migration
// touches this file and nothing else.

type Leaflet = typeof import('leaflet');

/** Leaflet's stylesheet is too big for a component's style budget, so it is copied as a versioned asset and linked once, with the map. */
const stylesheet = 'vendor/leaflet/1.9.4/leaflet.css';

let styles: Promise<void> | undefined;
let engine: Promise<Leaflet> | undefined;

function loadStyles(): Promise<void> {
  styles ??= new Promise<void>((resolve) => {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = stylesheet;
    // A stylesheet that cannot load leaves the map ugly, not missing: it is never waited for past its answer.
    link.addEventListener('load', () => resolve(), { once: true });
    link.addEventListener('error', () => resolve(), { once: true });
    document.head.appendChild(link);
  });
  return styles;
}

/** Leaflet and its stylesheet, loaded once and on demand: the ES build, so the bundler can put it in a chunk of its own. */
export function loadLeaflet(): Promise<Leaflet> {
  engine ??= Promise.all([import('leaflet/dist/leaflet-src.esm.js'), loadStyles()]).then(([module]) => module as unknown as Leaflet);
  engine.catch(() => (engine = undefined)); // a failed load can be tried again
  return engine;
}

/** A point in the map container's pixels. */
export interface MapPoint {
  x: number;
  y: number;
}

/** Where the map is, and how far it may zoom. `lat` and `lng` are the view's centre (y and x for a planar map). */
export interface EngineView {
  lat: number;
  lng: number;
  zoom: number;
  minZoom: number;
  maxZoom: number;
}

export interface ShapeInput {
  geometry: Geometry;
  /** The step (1 to 7) of the sequential scale, as a class. */
  step: number;
}

/** What happens on the map, told to the widget. Every call is made after Leaflet has settled its own state. */
export interface MapEvents {
  /** The pointer entered a shape, or moved over it. */
  hover(index: number, at: MapPoint): void;
  move(index: number, at: MapPoint): void;
  leave(index: number): void;
  /** A click or a tap on a shape. */
  pick(index: number, at: MapPoint): void;
  /** A click or a tap on the map where no shape is. */
  blank(): void;
  /** A drag or a zoom starts, by the member or by the widget. */
  motion(): void;
  /** The view settled after a move or a zoom; `byUser` is false for the widget's own fit. */
  settled(view: EngineView, byUser: boolean): void;
}

export interface MapInit {
  kind: MapKind;
  shapes: ShapeInput[];
  /** A view to open at instead of the fitted one (coming back from "Show as table"). */
  view: EngineView | null;
  reducedMotion: boolean;
  events: MapEvents;
}

export interface TileSpec {
  template: string;
  /** The deepest zoom the provider has tiles for; deeper views enlarge these. */
  maxNativeZoom: number;
  referrerPolicy?: ReferrerPolicy;
}

/** How a tile layer is doing: told once a batch of tiles has settled, and at every failed tile. */
export interface TileHooks {
  onError(): void;
  onSettled(batch: { loaded: number; failed: number }): void;
}

export interface Viewport {
  zoom: number;
  box: Box;
}

interface Shape {
  layer: Path;
  /** The pixel half-size added around a marker, which has a radius and no extent. */
  point: boolean;
}

const maxTileZoom = 20;
const polygonStyle = { weight: 1, fillOpacity: 1 };
const markerStyle = { radius: 6, weight: 2, fillOpacity: 1 };
/** Over a basemap the fill lets the streets show through. */
const backdropOpacity = 0.7;
/** The room kept between a shape brought into view and the edge of the view. */
const revealPadding = 16;

/** One Leaflet map and the shapes drawn on it. Create it with `MapEngine.create`; `destroy` before dropping it. */
export class MapEngine {
  private shapes: Shape[] = [];
  private group: FeatureGroup | null = null;
  private tiles: TileLayer | null = null;
  private template: string | null = null;
  private box: Box = { south: 0, west: 0, north: 0, east: 0 };
  private fit: { lat: number; lng: number; zoom: number } = { lat: 0, lng: 0, zoom: 0 };
  private active: number | null = null;
  private backdrop = false;
  /** While the widget moves the view itself, its `moveend` is not the member's. */
  private own = 0;

  private constructor(
    private readonly L: Leaflet,
    readonly kind: MapKind,
    private readonly map: LeafletMap,
    private readonly events: MapEvents,
    private readonly reducedMotion: boolean,
  ) {}

  static async create(host: HTMLElement, init: MapInit): Promise<MapEngine> {
    const L = await loadLeaflet();
    const limits = provisionalLimits(init.kind);
    const motion = !init.reducedMotion;
    const map = L.map(host, {
      crs: init.kind === 'degrees' ? L.CRS.EPSG3857 : L.CRS.Simple,
      zoomControl: false,
      attributionControl: false,
      // The cost of a fractional fit is none: it makes a municipality fill its box instead of leaving most of it empty.
      zoomSnap: 0.25,
      minZoom: limits.minZoom,
      maxZoom: limits.maxZoom,
      maxBoundsViscosity: 0.8,
      zoomAnimation: motion,
      fadeAnimation: motion,
      markerZoomAnimation: motion,
      inertia: motion,
      // The widget resizes the map itself, from its own box.
      trackResize: false,
      renderer: L.svg({ padding: 0.5 }),
    });
    const created = new MapEngine(L, init.kind, map, init.events, init.reducedMotion);
    created.listen();
    created.setData(init.shapes, init.view);
    return created;
  }

  /** The numbers for the controls and the attributes the tests read. */
  view(): EngineView {
    const center = this.map.getCenter();
    return { lat: center.lat, lng: center.lng, zoom: this.map.getZoom(), minZoom: this.map.getMinZoom(), maxZoom: this.map.getMaxZoom() };
  }

  /** The area on screen and its zoom, for a provider's data credits. */
  viewport(): Viewport {
    const bounds = this.map.getBounds();
    return { zoom: this.map.getZoom(), box: { south: bounds.getSouth(), west: bounds.getWest(), north: bounds.getNorth(), east: bounds.getEast() } };
  }

  /**
   * Draws these shapes in place of the ones drawn, and shows all of them: the zoom that fits their box (a lone point opens at 16),
   * the limits that follow from it, then the view. `view` restores a view instead of the fit.
   */
  setData(shapes: ShapeInput[], view: EngineView | null = null): void {
    const { L, map } = this;
    this.deactivate();
    this.group?.remove();
    this.shapes = [];
    const layers: Path[] = [];
    for (const shape of shapes) {
      const className = `tmz-seq-${shape.step}`;
      const fillOpacity = this.backdrop ? backdropOpacity : 1;
      if (shape.geometry.type === 'point') {
        const [x, y] = shape.geometry.position;
        const layer = L.circleMarker([y, x], { ...markerStyle, fillOpacity, className, bubblingMouseEvents: false });
        this.shapes.push({ layer, point: true });
        layers.push(layer);
      } else {
        const polygons = shape.geometry.type === 'polygon' ? [shape.geometry.rings] : shape.geometry.polygons;
        const toLatLngs = (ring: Position[]) => ring.map(([x, y]) => L.latLng(y, x));
        const layer = L.polygon(polygons.map((rings) => rings.map(toLatLngs)), { ...polygonStyle, fillOpacity, className, bubblingMouseEvents: false });
        this.shapes.push({ layer, point: false });
        layers.push(layer);
      }
    }
    const group = L.featureGroup(layers);
    group.on('mouseover', (e) => this.events.hover(this.indexOf(e), this.pointOf(e)));
    group.on('mousemove', (e) => this.events.move(this.indexOf(e), this.pointOf(e)));
    group.on('mouseout', (e) => this.events.leave(this.indexOf(e)));
    group.on('click', (e) => this.events.pick(this.indexOf(e), this.pointOf(e)));
    group.addTo(map);
    this.group = group;
    this.box = boxOf(shapes.flatMap((shape) => positionsOf(shape.geometry))) ?? this.box;
    this.refit(view);
    // What the specs look shapes up by: a polygon is a region, a marker a point. (A layer has its element once the map has a view.)
    for (const shape of this.shapes) {
      shape.layer.getElement()?.setAttribute('data-testid', shape.point ? 'map-point' : 'map-region');
    }
  }

  /** Shows all the shapes again: the fitted view, and the limits and the room to move that go with it. */
  refit(restore: EngineView | null = null): void {
    const { L, map, box, kind } = this;
    this.quiet(() => {
      const provisional = provisionalLimits(kind);
      map.setMaxBounds(L.latLngBounds([]));
      map.setMinZoom(provisional.minZoom);
      map.setMaxZoom(provisional.maxZoom);
      const bounds: LatLngBounds = L.latLngBounds([box.south, box.west], [box.north, box.east]);
      // `getBoundsZoom` clamps to the limits in force, which is why they were widened first.
      const fitZoom = fitZoomOf(kind, map.getBoundsZoom(bounds, false, L.point(10, 10)), box);
      const center = bounds.getCenter();
      this.fit = { lat: center.lat, lng: center.lng, zoom: fitZoom };
      // `setView`, not `fitBounds`: it doubles the padding and ignores the cap, so a lone point would open at zoom 20.
      map.setView(center, fitZoom, { animate: false });
      const limits = zoomLimits(kind, fitZoom);
      map.setMinZoom(limits.minZoom);
      map.setMaxZoom(limits.maxZoom);
      const shown = map.getBounds();
      const room = roomAround(kind, box, { south: shown.getSouth(), west: shown.getWest(), north: shown.getNorth(), east: shown.getEast() });
      map.setMaxBounds(L.latLngBounds([room.south, room.west], [room.north, room.east]));
      if (restore) {
        map.setView([restore.lat, restore.lng], restore.zoom, { animate: false });
      }
    });
  }

  /** The box changed size: Leaflet reads it again, and keeps the centre where it is. */
  resize(): void {
    this.quiet(() => this.map.invalidateSize({ pan: false }));
  }

  zoomIn(): void {
    this.map.zoomIn();
  }

  zoomOut(): void {
    this.map.zoomOut();
  }

  /** The fitted view again (not the member's). */
  showAll(): void {
    this.quiet(() => this.map.setView([this.fit.lat, this.fit.lng], this.fit.zoom, { animate: false }));
  }

  /** One finger must scroll the page and two must move the map (design D5), so the widget switches dragging with the touches. */
  setDragging(on: boolean): void {
    if (on) {
      this.map.dragging.enable();
    } else {
      this.map.dragging.disable();
    }
  }

  isDragging(): boolean {
    return this.map.dragging.enabled();
  }

  /** Over a basemap the fill is 0.7, so the streets show through the shapes. */
  setBackdrop(on: boolean): void {
    this.backdrop = on;
    for (const { layer } of this.shapes) {
      layer.setStyle({ fillOpacity: on ? backdropOpacity : 1 });
    }
  }

  /** Puts a tile layer under the shapes in place of the one there, or none. */
  setTiles(spec: TileSpec | null, hooks: TileHooks | null = null): void {
    const { L, map } = this;
    this.tiles?.remove();
    this.tiles = null;
    this.template = null;
    if (!spec) {
      return;
    }
    const layer = L.tileLayer(spec.template, {
      maxZoom: maxTileZoom,
      maxNativeZoom: spec.maxNativeZoom,
      ...(spec.referrerPolicy ? { referrerPolicy: spec.referrerPolicy } : {}),
    });
    let loaded = 0;
    let failed = 0;
    layer.on('tileloadstart', (e) => e.tile.setAttribute('data-testid', 'map-tile'));
    layer.on('tileload', () => loaded++);
    layer.on('tileerror', () => {
      failed++;
      hooks?.onError();
    });
    layer.on('loading', () => {
      loaded = 0;
      failed = 0;
    });
    layer.on('load', () => {
      if (this.tiles === layer) {
        hooks?.onSettled({ loaded, failed });
      }
    });
    layer.addTo(map);
    this.tiles = layer;
    this.template = spec.template;
  }

  /** New links for the tiles still to come, or a new depth: tiles on screen stay, unless `redraw` asks to fetch them again. */
  updateTiles(update: { template?: string; maxNativeZoom?: number; redraw?: boolean }): void {
    const layer = this.tiles;
    if (!layer) {
      return;
    }
    if (update.maxNativeZoom !== undefined && update.maxNativeZoom !== layer.options.maxNativeZoom) {
      layer.options.maxNativeZoom = update.maxNativeZoom;
      // `redraw()` keeps the tile grid of the old depth (Leaflet resets it only with the view), so tiles of a deeper zoom would all
      // fall outside it and none would be drawn: the layer is taken off the map and put back instead.
      layer.remove();
      layer.addTo(this.map);
    }
    if (update.template !== undefined) {
      layer.setUrl(update.template, !update.redraw);
      this.template = update.template;
    }
  }

  /** The link the tile layer asks its tiles from now, or null without a layer. */
  tileTemplate(): string | null {
    return this.template;
  }

  /** The tile layer is still fetching the tiles of the view shown; false without a layer. */
  tilesLoading(): boolean {
    return this.tiles?.isLoading() ?? false;
  }

  /**
   * Makes a shape the one the keyboard is on. The order matters: `bringToFront` re-inserts the node, and a node that moved loses
   * the focus, so the class and the focus come after it, and the pan after those.
   */
  activate(index: number, label: string): void {
    const shape = this.shapes[index];
    if (!shape) {
      return;
    }
    if (this.active === index) {
      shape.layer.getElement()?.setAttribute('aria-label', label);
      this.reveal(shape);
      return;
    }
    const previous = this.active;
    this.active = index;
    shape.layer.bringToFront();
    const element = shape.layer.getElement();
    if (element) {
      element.classList.add('tmz-active');
      element.setAttribute('tabindex', '-1');
      element.setAttribute('role', 'img');
      element.setAttribute('aria-label', label);
      (element as SVGElement).focus({ preventScroll: true });
    }
    // The previous shape is cleared after the focus moved on, so it is never a focused element that stops being focusable.
    this.clearShape(previous);
    this.reveal(shape);
  }

  /** The shape is no longer the keyboard's: its class and attributes go, so no shape is ever a stop of its own. */
  deactivate(): void {
    this.clearActive();
    this.active = null;
  }

  activeIndex(): number | null {
    return this.active;
  }

  /** Moves focus back to the map itself (Escape from the shapes). */
  focusMap(): void {
    this.map.getContainer().focus({ preventScroll: true });
  }

  /** Where a shape is, in the container's pixels: its centre, or its marker. */
  anchorOf(index: number): MapPoint | null {
    const shape = this.shapes[index];
    if (!shape) {
      return null;
    }
    const at = this.map.latLngToContainerPoint(this.boundsOf(shape).getCenter());
    return { x: at.x, y: at.y };
  }

  elementOf(index: number): SVGElement | null {
    return (this.shapes[index]?.layer.getElement() as SVGElement | undefined) ?? null;
  }

  size(): MapPoint {
    const size = this.map.getSize();
    return { x: size.x, y: size.y };
  }

  destroy(): void {
    this.clearActive();
    this.tiles = null;
    this.map.remove();
  }

  private listen(): void {
    const { map, events } = this;
    map.on('moveend', () => events.settled(this.view(), this.own === 0));
    map.on('movestart zoomstart', () => events.motion());
    map.on('click', () => events.blank());
  }

  private quiet(run: () => void): void {
    this.own++;
    try {
      run();
    } finally {
      this.own--;
    }
  }

  private indexOf(event: { propagatedFrom?: unknown }): number {
    return this.shapes.findIndex((shape) => shape.layer === event.propagatedFrom);
  }

  private pointOf(event: unknown): MapPoint {
    const at = (event as LeafletMouseEvent).containerPoint;
    return { x: at.x, y: at.y };
  }

  private boundsOf(shape: Shape): LatLngBounds {
    const { L } = this;
    if (shape.point) {
      const at = (shape.layer as unknown as { getLatLng(): LatLng }).getLatLng();
      return L.latLngBounds(at, at);
    }
    return (shape.layer as unknown as { getBounds(): LatLngBounds }).getBounds();
  }

  private clearActive(): void {
    this.clearShape(this.active);
  }

  private clearShape(index: number | null): void {
    if (index === null) {
      return;
    }
    const element = this.shapes[index]?.layer.getElement();
    if (element) {
      element.classList.remove('tmz-active');
      element.removeAttribute('tabindex');
      element.removeAttribute('role');
      element.removeAttribute('aria-label');
    }
  }

  /** Pans by the least that brings the shape into view, or to its centre when it is bigger than the view (Leaflet's `panInside` takes a point). */
  private reveal(shape: Shape): void {
    const { map } = this;
    const bounds = this.boundsOf(shape);
    const zoom = map.getZoom();
    let nw = map.project(bounds.getNorthWest(), zoom);
    let se = map.project(bounds.getSouthEast(), zoom);
    if (shape.point) {
      nw = nw.subtract([markerStyle.radius + markerStyle.weight, markerStyle.radius + markerStyle.weight]);
      se = se.add([markerStyle.radius + markerStyle.weight, markerStyle.radius + markerStyle.weight]);
    }
    const view = map.getPixelBounds();
    const left = view.min!.x + revealPadding;
    const top = view.min!.y + revealPadding;
    const right = view.max!.x - revealPadding;
    const bottom = view.max!.y - revealPadding;
    const animate = !this.reducedMotion;
    if (se.x - nw.x > right - left || se.y - nw.y > bottom - top) {
      map.panTo(bounds.getCenter(), { animate });
      return;
    }
    const dx = nw.x < left ? nw.x - left : se.x > right ? se.x - right : 0;
    const dy = nw.y < top ? nw.y - top : se.y > bottom ? se.y - bottom : 0;
    if (dx !== 0 || dy !== 0) {
      map.panBy([dx, dy], { animate });
    }
  }
}
