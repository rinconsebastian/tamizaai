import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { BasemapList, Basemaps } from './basemaps.service';

const url = '/api/v1/basemaps';
const isList = (r: { url: string; method: string }) => r.method === 'GET' && r.url === url;

const google = (refreshIn = 1980): BasemapList => ({
  provider: 'google',
  basemaps: (['streets', 'satellite'] as const).map((kind) => ({
    kind,
    minZoom: 0,
    maxZoom: 22,
    tiles: `/api/v1/basemap-tiles/link-${kind}-${refreshIn}/{z}/{x}/{y}`,
    linksExpireAt: '2026-10-17T15:15:00Z',
    linksRefreshInSeconds: refreshIn,
    attribution: { source: 'google', text: null, url: null },
  })),
});

const osm: BasemapList = {
  provider: 'osm',
  basemaps: [
    {
      kind: 'streets',
      minZoom: 0,
      maxZoom: 19,
      tiles: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
      linksExpireAt: null,
      linksRefreshInSeconds: null,
      attribution: { source: 'osm', text: '© OpenStreetMap contributors', url: 'https://www.openstreetmap.org/copyright' },
    },
  ],
};

describe('Basemaps', () => {
  let http: HttpTestingController;
  let service: Basemaps;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(Basemaps);
  });

  afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(document, 'visibilityState');
  });

  /** Loads the list and answers it. */
  function load(list: BasemapList) {
    service.load();
    http.expectOne(isList).flush(list);
  }

  const seconds = (n: number) => vi.advanceTimersByTime(n * 1000);

  describe('the list', () => {
    it('is not there until the first answer, and is asked for once per page', () => {
      expect(service.list()).toBeNull();

      service.load();
      service.load();
      http.expectOne(isList);
      service.load();

      http.expectNone(isList);
    });

    it('holds what the server answered', () => {
      load(google());

      expect(service.list()!.provider).toBe('google');
      expect(service.list()!.basemaps.map((entry) => entry.kind)).toEqual(['streets', 'satellite']);
    });

    it('is none when the request fails, so a map without a basemap is the fallback', () => {
      service.load();
      http.expectOne(isList).flush({}, { status: 503, statusText: 'Service Unavailable' });

      expect(service.list()).toEqual({ provider: 'none', basemaps: [] });
    });

    it('is none for a user who is not signed in or has no project (an empty list)', () => {
      load({ provider: 'google', basemaps: [] });

      expect(service.list()!.basemaps).toEqual([]);
    });

    it('is not asked for again after it failed: a later map does not retry until a layer asks to refresh', () => {
      service.load();
      http.expectOne(isList).flush({}, { status: 500, statusText: 'Server Error' });

      service.load();

      http.expectNone(isList);
    });
  });

  describe('fresh links', () => {
    it('refreshes the list when the server said its links need it, while a Google layer is mounted', () => {
      load(google(1980));
      service.watch();

      seconds(1979);
      http.expectNone(isList);
      seconds(1);

      http.expectOne(isList).flush(google(1980));
    });

    it('refreshes at least a minute after arrival, whatever the server asks', () => {
      load(google(5));
      service.watch();

      seconds(59);
      http.expectNone(isList);
      seconds(1);

      http.expectOne(isList).flush(google(5));
    });

    it('keeps refreshing: each answer starts the next wait', () => {
      load(google(120));
      service.watch();

      seconds(120);
      http.expectOne(isList).flush(google(120));
      seconds(119);
      http.expectNone(isList);
      seconds(1);

      http.expectOne(isList).flush(google(120));
    });

    it.each([3600, -3600])('waits the same time however far the clock of this machine is off (%i seconds)', (skew) => {
      load(google(1980));
      service.watch();

      vi.setSystemTime(Date.now() + skew * 1000);
      seconds(1979);
      http.expectNone(isList);
      seconds(1);

      http.expectOne(isList).flush(google(1980));
    });

    it('refreshes after a sleep that skipped the timer, as soon as the machine is awake and the tab is seen', () => {
      load(google(1980));
      service.watch();
      Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });

      // The machine slept for two hours: the timers did not run, but the clock moved.
      vi.setSystemTime(Date.now() + 2 * 3600 * 1000);
      document.dispatchEvent(new Event('visibilitychange'));

      http.expectOne(isList).flush(google(1980));
    });

    it('refreshes when the timer fires late: hours after it was due, and still only once', () => {
      load(google(1980));
      service.watch();

      seconds(3 * 3600);

      const requests = http.match(isList);
      expect(requests).toHaveLength(1);
      requests[0].flush(google(1980));
      expect(service.linksDue()).toBe(false);
    });

    it('does not refresh a list nobody shows tiles from', () => {
      load(google(1980));

      seconds(4 * 3600);

      http.expectNone(isList);
      expect(service.linksDue()).toBe(true);
    });

    it('refreshes at once when a Google layer mounts after the links were due', () => {
      load(google(1980));
      seconds(3600);
      http.expectNone(isList);

      service.watch();

      http.expectOne(isList).flush(google(1980));
    });

    it('stops refreshing when the last layer is gone', () => {
      load(google(1980));
      const first = service.watch();
      const second = service.watch();
      first();
      first(); // releasing twice counts once

      seconds(1980);
      http.expectOne(isList).flush(google(1980));
      second();
      seconds(1980);

      http.expectNone(isList);
    });

    it('does not refresh OpenStreetMap, whose tiles need no links', () => {
      load(osm);
      service.watch();

      seconds(4 * 3600);

      http.expectNone(isList);
      expect(service.linksDue()).toBe(false);
    });

    it('says the links are due once their time has passed, and not before', () => {
      load(google(1980));
      expect(service.linksDue()).toBe(false);

      seconds(1979);
      expect(service.linksDue()).toBe(false);
      seconds(1);

      expect(service.linksDue()).toBe(true);
    });

    it('keeps the list it has when a refresh fails, since its links may still work', () => {
      load(google(1980));
      service.watch();

      seconds(1980);
      http.expectOne(isList).flush({}, { status: 503, statusText: 'Service Unavailable' });

      expect(service.list()!.provider).toBe('google');
      expect(service.list()!.basemaps).toHaveLength(2);
    });

    it('asks one refresh at a time', () => {
      load(google(1980));

      void service.refresh();
      void service.refresh();

      http.expectOne(isList).flush(google(1980));
    });

    it('lets the widget see a provider change: the list it holds is the new one', () => {
      load(google(1980));
      void service.refresh();

      http.expectOne(isList).flush(osm);

      expect(service.list()!.provider).toBe('osm');
      expect(service.list()!.basemaps[0].attribution.source).toBe('osm');
    });
  });

  describe('credits', () => {
    it('asks for the credits and the imagery depth of the area on screen', () => {
      service.credits('satellite', { zoom: 14, north: 3.5, south: 3.4, east: -76.4, west: -76.6 }).subscribe();

      const request = http.expectOne((r) => r.url === '/api/v1/basemaps/satellite/credits');
      expect(request.request.method).toBe('GET');
      expect(request.request.params.keys()).toEqual(['zoom', 'north', 'south', 'east', 'west']);
      expect(request.request.params.get('zoom')).toBe('14');
      expect(request.request.params.get('west')).toBe('-76.6');
      request.flush({ text: 'Imagery ©2026 Maxar', maxZoom: 19 });
    });
  });
});
