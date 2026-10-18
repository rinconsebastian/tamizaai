import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';
import { BasemapKind } from './projects/indicators.service';

// Mirrors Tamiza.Api/Maps/*. The basemaps a deployment offers to the dashboard's maps (design D13, D16): which provider, and for
// each kind how the browser gets its tiles and what it must credit. The map widget reads this; nothing else does.

export type BasemapProvider = 'none' | 'osm' | 'google';

export interface BasemapAttribution {
  source: 'osm' | 'google';
  /** The text to show; null for Google, whose credits come for the area on screen (see `BasemapCredits`). */
  text: string | null;
  url: string | null;
}

export interface BasemapEntry {
  kind: BasemapKind;
  minZoom: number;
  maxZoom: number;
  /** A tile template with `{z}`, `{x}` and `{y}`: the provider's own for OpenStreetMap, the `api`'s expiring tile links for Google. */
  tiles: string;
  /** Google only: when the links in `tiles` stop working, and in how many seconds a fresh list is due. Null for OpenStreetMap. */
  linksExpireAt: string | null;
  linksRefreshInSeconds: number | null;
  attribution: BasemapAttribution;
}

export interface BasemapList {
  provider: BasemapProvider;
  basemaps: BasemapEntry[];
}

/** Google's data credits for an area, and the deepest zoom it has imagery for there. */
export interface BasemapCredits {
  text: string | null;
  maxZoom: number;
}

export interface CreditsArea {
  zoom: number;
  north: number;
  south: number;
  east: number;
  west: number;
}

export const noBasemaps: BasemapList = { provider: 'none', basemaps: [] };

/** A fresh list is never asked for sooner than this after the last one, whatever the server says. */
const minRefreshSeconds = 60;

/**
 * The list of basemaps, loaded once per page and kept fresh while a Google layer is on screen. It answers none when the request
 * fails, so a map without a basemap is the fallback for every failure. Tile links last at most an hour; the server says in how many
 * seconds a fresh list is due, and that is waited out with a timer, not read from a date, so a client clock that is off changes
 * nothing. The wall clock is only a second opinion, for a machine that slept through its timer.
 */
@Injectable({ providedIn: 'root' })
export class Basemaps {
  private readonly http = inject(HttpClient);

  /** The list; null until the first answer. */
  readonly list = signal<BasemapList | null>(null);

  private started = false;
  private arrivedAt = 0;
  /** The timer for this list ran out. */
  private due = false;
  private watchers = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private inflight: Promise<void> | null = null;
  private readonly onVisible = () => {
    if (document.visibilityState === 'visible' && this.linksDue()) {
      void this.refresh();
    }
  };

  /** Starts the page's one load. Calling it again does nothing. */
  load(): void {
    if (!this.started) {
      this.started = true;
      void this.fetch(true);
    }
  }

  /** Asks again, once at a time. A failed refresh keeps the list there is, since its links may still work. */
  refresh(): Promise<void> {
    return this.fetch(false);
  }

  /** True when a Google list is as old as the server said its links could be trusted. */
  linksDue(): boolean {
    const wait = this.refreshMs();
    return wait > 0 && (this.due || Date.now() - this.arrivedAt >= wait);
  }

  /** A Google layer is mounted: the list is refreshed on schedule until the returned function is called. */
  watch(): () => void {
    this.watchers++;
    if (this.watchers === 1) {
      document.addEventListener('visibilitychange', this.onVisible);
      if (this.linksDue()) {
        void this.refresh();
      }
    }
    let released = false;
    return () => {
      if (released) {
        return;
      }
      released = true;
      this.watchers--;
      if (this.watchers === 0) {
        document.removeEventListener('visibilitychange', this.onVisible);
      }
    };
  }

  /** Google's data credits and imagery depth for the area on screen. */
  credits(kind: BasemapKind, area: CreditsArea): Observable<BasemapCredits> {
    const params = new HttpParams().set('zoom', area.zoom).set('north', area.north).set('south', area.south).set('east', area.east).set('west', area.west);
    return this.http.get<BasemapCredits>(`/api/v1/basemaps/${kind}/credits`, { params });
  }

  private fetch(first: boolean): Promise<void> {
    if (this.inflight) {
      return this.inflight;
    }
    this.inflight = new Promise<void>((resolve) => {
      this.http
        .get<BasemapList>('/api/v1/basemaps')
        .pipe(catchError(() => of(first ? noBasemaps : null)))
        .subscribe((list) => {
          this.inflight = null;
          if (list) {
            this.arrivedAt = Date.now();
            this.due = false;
            this.list.set(list);
            this.schedule();
          }
          resolve();
        });
    });
    return this.inflight;
  }

  /** The wait from one list to the next: the shortest the server gave, and at least a minute; 0 when there are no links to renew. */
  private refreshMs(): number {
    const given = (this.list()?.basemaps ?? []).map((entry) => entry.linksRefreshInSeconds).filter((seconds): seconds is number => seconds !== null && seconds > 0);
    return given.length ? Math.max(minRefreshSeconds, Math.min(...given)) * 1000 : 0;
  }

  private schedule(): void {
    clearTimeout(this.timer);
    this.timer = undefined;
    const wait = this.refreshMs();
    if (wait > 0) {
      this.timer = setTimeout(() => {
        this.due = true;
        // Only a Google layer on screen has a use for fresh links; otherwise they are asked for when one mounts.
        if (this.watchers > 0) {
          void this.refresh();
        }
      }, wait);
    }
  }
}
