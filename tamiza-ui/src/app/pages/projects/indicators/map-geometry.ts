import { Position } from './wkt';

// The numbers behind the map's view (design D3): what kind of coordinates the shapes use, how far the map may zoom and how far it
// may be moved. Plain math on plain numbers, so it is tested without Leaflet. `x` is longitude (or easting) and `y` latitude (or
// northing); a box is written the way Leaflet writes bounds, south-west to north-east.

/** Degrees are drawn on a Web Mercator map and can take a basemap; any other number is drawn in relative positions, flat. */
export type MapKind = 'degrees' | 'planar';

export interface Box {
  south: number;
  west: number;
  north: number;
  east: number;
}

export interface ZoomLimits {
  minZoom: number;
  maxZoom: number;
}

/** The deepest zoom a map opens at: a lone point shows the streets around it, not a single tile's worth of one house. */
export const maxFitZoom = 16;
/** How many levels beyond the fitted view a map may zoom out. */
export const zoomOutLevels = 3;
/** The deepest zoom of a map of degrees, and how far a planar map may zoom in past its fitted view. */
export const maxDegreesZoom = 20;
export const planarZoomIn = 10;
/** The most a latitude may be on a Web Mercator map. */
const mercatorLimit = 85;

/** The test `map-widget.ts` used to guess degrees from the value ranges: every x within ±180 and every y within ±90. */
export function isDegrees(positions: readonly Position[]): boolean {
  return positions.length > 0 && positions.every(([x, y]) => x >= -180 && x <= 180 && y >= -90 && y <= 90);
}

export function kindOf(positions: readonly Position[]): MapKind {
  return isDegrees(positions) ? 'degrees' : 'planar';
}

/** The box around some positions; null for none. */
export function boxOf(positions: readonly Position[]): Box | null {
  if (!positions.length) {
    return null;
  }
  let south = Infinity;
  let west = Infinity;
  let north = -Infinity;
  let east = -Infinity;
  for (const [x, y] of positions) {
    south = Math.min(south, y);
    north = Math.max(north, y);
    west = Math.min(west, x);
    east = Math.max(east, x);
  }
  return { south, west, north, east };
}

/** True for a box with no extent at all: a lone point, or the same point twice. */
export function isLone(box: Box): boolean {
  return box.south === box.north && box.west === box.east;
}

/**
 * The limits a map is created with, before its fit is known (`minZoom` must be low enough for `getBoundsZoom` not to clamp the
 * fit: a planar map in metres fits at about -9).
 */
export function provisionalLimits(kind: MapKind): ZoomLimits {
  return { minZoom: kind === 'degrees' ? 0 : -30, maxZoom: maxDegreesZoom };
}

/**
 * The zoom a map opens at, from the zoom that fits its box. A map of degrees never opens deeper than `maxFitZoom`; a lone point
 * of a planar map has no extent to fit and opens at zoom 0.
 */
export function fitZoomOf(kind: MapKind, boundsZoom: number, box: Box): number {
  if (kind === 'degrees') {
    return Math.min(maxFitZoom, Number.isFinite(boundsZoom) ? boundsZoom : 0);
  }
  return isLone(box) || !Number.isFinite(boundsZoom) ? 0 : boundsZoom;
}

/** Three levels out from the fit (never below 0 for degrees: the world is one tile there); 20 deep, or ten levels past the fit when planar. */
export function zoomLimits(kind: MapKind, fitZoom: number): ZoomLimits {
  return kind === 'degrees'
    ? { minZoom: Math.max(0, fitZoom - zoomOutLevels), maxZoom: maxDegreesZoom }
    : { minZoom: fitZoom - zoomOutLevels, maxZoom: fitZoom + planarZoomIn };
}

/** The box grown on every side by `ratio` of its own size, as Leaflet's `pad` does. */
export function pad(box: Box, ratio: number): Box {
  const height = Math.abs(box.north - box.south) * ratio;
  const width = Math.abs(box.east - box.west) * ratio;
  return { south: box.south - height, west: box.west - width, north: box.north + height, east: box.east + width };
}

export function union(a: Box, b: Box): Box {
  return { south: Math.min(a.south, b.south), west: Math.min(a.west, b.west), north: Math.max(a.north, b.north), east: Math.max(a.east, b.east) };
}

/**
 * How far the map may be moved: the shapes' box and the fitted view, each grown by its own size. The view is what gives a lone
 * point or a row of points room (their box has no height or width); the shapes keep tiles near the data.
 */
export function roomAround(kind: MapKind, data: Box, view: Box): Box {
  const room = union(pad(data, 1), pad(view, 1));
  if (kind === 'planar') {
    return room;
  }
  return {
    south: Math.max(-mercatorLimit, room.south),
    west: Math.max(-180, room.west),
    north: Math.min(mercatorLimit, room.north),
    east: Math.min(180, room.east),
  };
}
