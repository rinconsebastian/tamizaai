import { Box, boxOf, fitZoomOf, isDegrees, isLone, kindOf, maxFitZoom, pad, provisionalLimits, roomAround, union, zoomLimits } from './map-geometry';

describe('map geometry', () => {
  describe('kind of coordinates', () => {
    it('knows degrees from the ranges of the values', () => {
      expect(isDegrees([[-76.53, 3.43], [-77.28, 1.21]])).toBe(true);
      expect(isDegrees([[-180, -90], [180, 90]])).toBe(true);
      expect(kindOf([[-76.53, 3.43]])).toBe('degrees');
    });

    it('takes anything outside ±180 and ±90 for metres', () => {
      expect(isDegrees([[1000000, 1000000]])).toBe(false);
      expect(isDegrees([[10, 91]])).toBe(false);
      expect(isDegrees([[181, 0]])).toBe(false);
      expect(kindOf([[-76, 3], [1_050_000, 870_000]])).toBe('planar');
    });

    it('has no kind for nothing to draw', () => {
      expect(isDegrees([])).toBe(false);
      expect(boxOf([])).toBeNull();
    });

    it('boxes the positions with x as west-east and y as south-north', () => {
      expect(boxOf([[1, 5], [-3, 2], [4, 9]])).toEqual({ south: 2, west: -3, north: 9, east: 4 });
    });
  });

  describe('zoom limits', () => {
    it('lets a map of degrees zoom out three levels from its fit, and never below the world', () => {
      expect(zoomLimits('degrees', 12)).toEqual({ minZoom: 9, maxZoom: 20 });
      expect(zoomLimits('degrees', 2)).toEqual({ minZoom: 0, maxZoom: 20 });
    });

    it('lets a planar map go below zero and zoom in ten levels past its fit', () => {
      expect(zoomLimits('planar', -9)).toEqual({ minZoom: -12, maxZoom: 1 });
      expect(zoomLimits('planar', 0.5)).toEqual({ minZoom: -2.5, maxZoom: 10.5 });
    });

    it('starts with limits wide enough not to clamp the fit', () => {
      expect(provisionalLimits('degrees')).toEqual({ minZoom: 0, maxZoom: 20 });
      expect(provisionalLimits('planar')).toEqual({ minZoom: -30, maxZoom: 20 });
    });
  });

  describe('fit zoom', () => {
    const area: Box = { south: 1, west: -77, north: 5, east: -76 };

    it('keeps the zoom that fits the shapes', () => {
      expect(fitZoomOf('degrees', 7.25, area)).toBe(7.25);
      expect(fitZoomOf('planar', -9.5, area)).toBe(-9.5);
    });

    it('opens a lone point of degrees at zoom 16, however close the fit would go', () => {
      const lone: Box = { south: 3.43, west: -76.53, north: 3.43, east: -76.53 };

      expect(isLone(lone)).toBe(true);
      expect(fitZoomOf('degrees', 20, lone)).toBe(maxFitZoom);
      expect(maxFitZoom).toBe(16);
    });

    it('caps any fit of degrees at 16, a few points a metre apart included', () => {
      expect(fitZoomOf('degrees', 19.5, area)).toBe(16);
    });

    it('opens a lone planar point at zero, since there is no extent to fit', () => {
      expect(fitZoomOf('planar', 20, { south: 5, west: 5, north: 5, east: 5 })).toBe(0);
    });

    it('does not trust a fit that is not a number (a box with no size)', () => {
      expect(fitZoomOf('degrees', -Infinity, area)).toBe(0);
      expect(fitZoomOf('planar', NaN, area)).toBe(0);
    });
  });

  describe('room to move', () => {
    const view: Box = { south: 3.3, west: -76.6, north: 3.5, east: -76.4 };

    it('pads a box by its own size on every side', () => {
      expect(pad({ south: 0, west: 0, north: 2, east: 4 }, 1)).toEqual({ south: -2, west: -4, north: 4, east: 8 });
      expect(union({ south: 0, west: 0, north: 1, east: 1 }, { south: -1, west: 0.5, north: 0.5, east: 3 })).toEqual({ south: -1, west: 0, north: 1, east: 3 });
    });

    it('has room around a lone point, whose own box has no size', () => {
      const lone: Box = { south: 3.4, west: -76.5, north: 3.4, east: -76.5 };

      const room = roomAround('degrees', lone, view);

      expect(room.north).toBeGreaterThan(room.south);
      expect(room.east).toBeGreaterThan(room.west);
      expect(room.west).toBeLessThan(view.west);
      expect(room.east).toBeGreaterThan(view.east);
    });

    it('has room around points on one line', () => {
      const row: Box = { south: 3.4, west: -76.7, north: 3.4, east: -76.3 };

      const room = roomAround('degrees', row, view);

      expect(room.north - room.south).toBeGreaterThan(0);
      expect(room.east).toBeGreaterThan(row.east);
    });

    it('stays inside the Web Mercator world for degrees', () => {
      const room = roomAround('degrees', { south: -80, west: -170, north: 80, east: 170 }, { south: -80, west: -170, north: 80, east: 170 });

      expect(room.south).toBe(-85);
      expect(room.north).toBe(85);
      expect(room.west).toBe(-180);
      expect(room.east).toBe(180);
    });

    it('does not clamp a planar map', () => {
      const room = roomAround('planar', { south: 0, west: 0, north: 1e6, east: 1e6 }, { south: 0, west: 0, north: 1e6, east: 1e6 });

      expect(room).toEqual({ south: -1e6, west: -1e6, north: 2e6, east: 2e6 });
    });
  });
});
