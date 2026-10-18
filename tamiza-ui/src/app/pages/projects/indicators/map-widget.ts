import { Component, DestroyRef, ElementRef, LOCALE_ID, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { BasemapCredits, BasemapEntry, Basemaps, CreditsArea } from '../../../core/basemaps.service';
import { DataColumnType } from '../../../core/projects/data.service';
import { BasemapKind, QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartTooltip, ChartTooltips } from '../../../shared/ui';
import { sequentialColor, sequentialStep, sequentialSteps } from './chart-scales';
import { ValueFormatter, plotted } from './format';
import { BasemapChoice, MapControls } from './map-controls';
import { EngineView, MapEngine, MapEvents, MapPoint, ShapeInput, TileHooks } from './map-engine';
import { kindOf } from './map-geometry';
import { TooltipContext, tooltipContent, tooltipLabel, tooltipSpeech, tooltipValues } from './map-tooltip';
import { countKey } from '../../../core/i18n/plural';
import { Geometry, parseWkt, positionsOf } from './wkt';
import { TableModel, WidgetTable } from './widget-table';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/** A row with a geometry that can be drawn; `index` is its place among the shapes, in the order of the table. */
interface Drawn {
  index: number;
  row: QueryRow;
  geometry: Geometry;
  step: number;
}

/** What the member chose for this map on their own screen: no basemap, a kind, or (undefined) nothing yet. */
type Switch = 'none' | BasemapKind | undefined;
type Hint = 'wheel' | 'touch';

/** The room a tile layer needs for the attribution to show in full (design D17). */
const minTileBox = { width: 200, height: 120 };
/** Below this box the zoom and basemap buttons collapse into one menu. */
const compactBox = { width: 160, height: 140 };
const hintMs = 1500;
const creditsDelayMs = 500;
/** Google serves up to this zoom; deeper views enlarge the deepest tiles it reports for the area. */
const googleZoom = 22;
/**
 * Google's outlined logos, unmodified (design D15; the source is recorded in the design's verification results), at a versioned path:
 * nginx caches images as immutable, so a new logo is a new path. The outline is the one that contrasts with the map under it.
 */
export const googleLogos: Record<BasemapKind, string> = {
  streets: 'vendor/google/2026-10/GoogleMaps_Logo_WithLightOutline.svg',
  satellite: 'vendor/google/2026-10/GoogleMaps_Logo_WithDarkOutline.svg',
};
/** Between 16 and 19 px high, as Google asks. */
export const googleLogoHeight = 18;

let helpIds = 0;

/**
 * A map widget (design D1 to D6): the rows' own geometry (WKT text) drawn by Leaflet, on a Web Mercator map when the coordinates are
 * degrees and flat otherwise. Polygons are filled regions and points are markers, both colored by the plotted field on a
 * sequential scale of seven steps. A row whose geometry is missing or cannot be read is left out and counted.
 *
 * Leaflet is only the engine (see `map-engine.ts`); the controls, the tooltip, the attribution, the hints and the notices are this
 * template's. The view (zoom and position) and the member's choice of basemap live here, in signals, and are never saved.
 */
@Component({
  selector: 'app-map-widget',
  imports: [TranslocoDirective, WidgetTable, MapControls],
  template: `
    <ng-container *transloco="let t">
      <p class="shrink-0 text-xs text-body" data-testid="chart-caption">{{ t('indicators.map.caption', { what: what(), group: config().groupBy }) }}</p>
      @if (asTable()) {
        <app-widget-table class="flex-1 overflow-auto" [model]="table(t)" />
      } @else {
        <div #box class="relative isolate min-h-0 flex-1" data-testid="chart-box" (keydown)="onKeydown($event)" (focusout)="onFocusOut($event)">
          @if (drawn().length) {
            <div
              #host
              class="tmz-map absolute inset-0 isolate focus:outline-none focus-visible:ring-4 focus-visible:ring-brand-medium"
              role="application"
              [attr.aria-roledescription]="t('indicators.map.roleDescription')"
              [attr.aria-label]="summary(t)"
              [attr.aria-describedby]="helpId"
              [attr.data-zoom]="current()?.zoom"
              [attr.data-center]="center()"
              [attr.data-basemap]="layerKey() ? entry()?.kind : null"
              data-testid="map-container"
            ></div>
            <p [id]="helpId" class="sr-only">{{ t('indicators.map.keyHelp') }}</p>
            <app-map-controls
              class="absolute end-2 top-2 z-10"
              [compact]="compact()"
              [choices]="choices(t)"
              [chosen]="chosen()"
              [canZoomIn]="canZoomIn()"
              [canZoomOut]="canZoomOut()"
              (zoomIn)="zoomIn()"
              (zoomOut)="zoomOut()"
              (showAll)="showAll()"
              (basemap)="chooseBasemap($event, t)"
            />
            @if (hint(); as hint) {
              <p class="pointer-events-none absolute inset-x-2 top-1/2 z-10 -translate-y-1/2 text-center" aria-hidden="true" data-testid="map-hint">
                <span class="rounded-base bg-neutral-primary-soft px-2 py-1 text-xs text-heading shadow-sm">{{ hint === 'wheel' ? t(mac ? 'indicators.map.hintWheelMac' : 'indicators.map.hintWheel') : t('indicators.map.hintTouch') }}</span>
              </p>
            }
            @if (tilesShown(); as source) {
              <div class="absolute bottom-0 start-0 z-10 flex max-w-[calc(100%-2.5rem)] flex-col items-start" [attr.aria-label]="t('indicators.map.attribution.label')" role="group" data-testid="map-attribution">
                @if (source === 'google') {
                  <img [src]="logo()" [alt]="t('indicators.map.attribution.logo')" [style.height.px]="logoHeight" class="block w-auto max-w-full" style="margin: 10px 10px 5px 10px" data-testid="map-google-logo" />
                  @if (credits()) {
                    <p class="rounded-se-base bg-neutral-primary-soft px-1.5 py-0.5 text-[10px] leading-tight text-heading" data-testid="map-credits">{{ credits() }}</p>
                  }
                } @else {
                  <p class="rounded-se-base bg-neutral-primary-soft px-1.5 py-0.5 text-[11px] leading-tight text-heading" data-testid="map-credits">
                    @if (osmLink(); as link) {
                      <a [href]="link" target="_blank" rel="noopener noreferrer" class="underline hover:no-underline">{{ osmText() }}</a>
                    } @else {
                      {{ osmText() }}
                    }
                  </p>
                }
              </div>
            }
            <p class="sr-only" role="status" aria-live="polite" data-testid="map-announcement">{{ announcement() }}</p>
          }
        </div>
        @if (note(t); as note) {
          <p class="shrink-0 text-xs text-body" data-testid="map-note">{{ note }}</p>
        }
        @if (drawn().length) {
          <div class="flex shrink-0 flex-wrap items-center gap-2 text-xs text-body" data-testid="map-legend">
            <span class="tabular-nums" data-testid="map-legend-min">{{ format()(range().min) }}</span>
            <span class="flex" aria-hidden="true">
              @for (step of steps; track step) {
                <span class="h-2.5 w-5 first:rounded-s-sm last:rounded-e-sm" [style.background]="color(step)" [style.opacity]="swatchOpacity()"></span>
              }
            </span>
            <span class="tabular-nums" data-testid="map-legend-max">{{ format()(range().max) }}</span>
          </div>
        }
        @if (leftOut() > 0) {
          <p class="shrink-0 text-xs text-fg-warning" data-testid="map-left-out">{{ t(key('indicators.map.leftOut', leftOut()), { count: leftOut() }) }}</p>
        }
        @if (!drawn().length) {
          <p class="shrink-0 text-sm text-body" data-testid="map-nothing">{{ t('indicators.map.nothing') }}</p>
        }
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class MapWidget {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  private readonly basemaps = inject(Basemaps);
  private readonly tooltip: ChartTooltip = inject(ChartTooltips).create();

  readonly rows = input.required<QueryRow[]>();
  readonly config = input.required<WidgetConfig>();
  /** The indicator's label and the formatter of its values. */
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();
  /** The type of each tooltip column, from the query's answer. */
  readonly tooltipTypes = input<readonly DataColumnType[] | undefined>(undefined);
  readonly asTable = input(false);

  protected readonly steps = Array.from({ length: sequentialSteps }, (_, i) => i + 1);
  protected readonly helpId = `map-help-${++helpIds}`;
  protected readonly logoHeight = googleLogoHeight;
  /** ⌘ instead of Ctrl in the wheel hint. */
  protected readonly mac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  private readonly host = viewChild<ElementRef<HTMLElement>>('host');
  protected readonly what = computed(() => plotted(this.config(), this.label()));

  // --- The rows ---------------------------------------------------------------------------------------------------------

  private readonly parsed = computed(() => this.rows().map((row) => ({ row, geometry: parseWkt(row.geometry) })));
  protected readonly leftOut = computed(() => this.parsed().filter((entry) => entry.geometry === null).length);

  protected readonly range = computed(() => {
    const values = this.parsed()
      .filter((entry) => entry.geometry !== null)
      .map((entry) => entry.row.value);
    return values.length ? { min: Math.min(...values), max: Math.max(...values) } : { min: 0, max: 0 };
  });

  /** The shapes to draw, in the order of the table. */
  protected readonly drawn = computed<Drawn[]>(() => {
    const { min, max } = this.range();
    return this.parsed()
      .filter((entry): entry is { row: QueryRow; geometry: Geometry } => entry.geometry !== null)
      .map(({ row, geometry }, index) => ({ index, row, geometry, step: sequentialStep(row.value, min, max) }));
  });

  /** Degrees are drawn on Web Mercator and can take a basemap; any other coordinates are drawn flat. */
  private readonly kind = computed(() => (this.drawn().length ? kindOf(this.drawn().flatMap((entry) => positionsOf(entry.geometry))) : null));

  // --- The box ----------------------------------------------------------------------------------------------------------

  /** jsdom has no layout, and a hidden box has none either: both read as this, which is roomy. */
  private readonly size = signal({ width: 480, height: 280 });
  protected readonly compact = computed(() => this.size().width < compactBox.width || this.size().height < compactBox.height);
  private readonly roomy = computed(() => this.size().width >= minTileBox.width && this.size().height >= minTileBox.height);
  /** The map is created when its widget first comes near the screen; where there is no observer it is created at once. */
  private readonly visible = signal(typeof IntersectionObserver === 'undefined');

  // --- The view (design D4): never saved, never queried ------------------------------------------------------------------

  protected readonly engine = signal<MapEngine | null>(null);
  protected readonly current = signal<EngineView | null>(null);
  /** The member moved or zoomed the map: a resize no longer re-fits it. */
  private readonly touched = signal(false);
  private readonly loadFailed = signal(false);
  protected readonly center = computed(() => {
    const view = this.current();
    return view ? `${view.lat.toFixed(5)},${view.lng.toFixed(5)}` : null;
  });
  protected readonly canZoomIn = computed(() => !this.current() || this.current()!.zoom < this.current()!.maxZoom);
  protected readonly canZoomOut = computed(() => !this.current() || this.current()!.zoom > this.current()!.minZoom);
  protected readonly hint = signal<Hint | null>(null);
  protected readonly announcement = signal('');
  private hintTimer: ReturnType<typeof setTimeout> | undefined;
  private announced = false;
  private drawnOnEngine: Drawn[] | null = null;
  /** The rows the last view that was settled belongs to. */
  private viewOf: Drawn[] | null = null;

  // --- The basemap (design D11, D13 to D17) ------------------------------------------------------------------------------

  /** What the member chose on their own screen. Reset by new rows; never saved. */
  private readonly viewerBasemap = signal<Switch>(undefined);
  private readonly offered = computed<BasemapEntry[]>(() => this.basemaps.list()?.basemaps ?? []);
  /** The basemap the map should have: the member's choice, else the widget's own. Not yet filtered by what is offered or fits. */
  private readonly wanted = computed<BasemapKind | null>(() => {
    const chosen = this.viewerBasemap();
    return chosen === undefined ? (this.config().basemap ?? null) : chosen === 'none' ? null : chosen;
  });
  protected readonly entry = computed<BasemapEntry | null>(() => this.offered().find((entry) => entry.kind === this.wanted()) ?? null);
  protected readonly chosen = computed(() => {
    const chosen = this.viewerBasemap();
    if (chosen !== undefined) {
      return chosen;
    }
    return this.entry()?.kind ?? 'none';
  });
  /** The tile layer to draw, or null: none wanted or offered, coordinates that are not degrees, a box too small, or tiles that failed. */
  protected readonly layerKey = computed(() => {
    const entry = this.entry();
    return entry && this.kind() === 'degrees' && this.roomy() && !this.failed() ? `${entry.kind}|${entry.attribution.source}` : null;
  });
  private readonly failed = signal(false);
  protected readonly tilesShown = signal<'osm' | 'google' | null>(null);
  protected readonly credits = signal<string | null>(null);
  protected readonly logo = computed(() => googleLogos[this.entry()?.kind ?? 'streets']);
  protected readonly swatchOpacity = computed(() => (this.tilesShown() ? 0.7 : 1));
  protected readonly osmText = computed(() => this.entry()?.attribution.text ?? '© OpenStreetMap contributors');
  /** Only an https link is followed from the credit; anything else would be shown as text. */
  protected readonly osmLink = computed(() => {
    const url = this.entry()?.attribution.url;
    return url && /^https:\/\//i.test(url) ? url : null;
  });

  private layerToken = 0;
  private releaseWatch: (() => void) | null = null;
  private retried = false;
  private creditsTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly creditsCache = new Map<string, BasemapCredits>();

  // --- The tooltip (design D6) -------------------------------------------------------------------------------------------

  private tip: { index: number; mode: 'hover' | 'pinned' } | null = null;
  private pinWatch: AbortController | null = null;
  private moveFrame: number | null = null;
  private nextPoint: MapPoint = { x: 0, y: 0 };

  private readonly events: MapEvents = {
    hover: (index, at) => this.onHover(index, at),
    move: (index, at) => this.onMove(index, at),
    leave: (index) => this.onLeave(index),
    pick: (index, at) => this.pin(index, at),
    blank: () => this.closePinned(),
    motion: () => this.closeHover(),
    settled: (view, byUser) => this.onSettled(view, byUser),
  };

  private readonly tiles: TileHooks = {
    onError: () => {
      // The first failed tile after the links are due is the sign to renew them, without waiting for the timer.
      if (this.tilesShown() === 'google' && this.basemaps.linksDue()) {
        void this.basemaps.refresh();
      }
    },
    onSettled: (batch) => void this.onTilesSettled(batch),
  };

  constructor() {
    this.basemaps.load();

    // The box: its size, kept up to date, and the gestures that need to see an event before Leaflet's own handlers do.
    effect((onCleanup) => {
      const box = this.box()?.nativeElement;
      if (!box) {
        return;
      }
      untracked(() => this.measure());
      if (typeof ResizeObserver !== 'undefined') {
        const observer = new ResizeObserver(() => {
          this.measure();
          const engine = this.engine();
          if (engine) {
            engine.resize();
            if (!this.touched()) {
              engine.refit();
            }
          }
        });
        observer.observe(box);
        onCleanup(() => observer.disconnect());
      }
      const abort = new AbortController();
      const { signal } = abort;
      // A plain wheel scrolls the page: stopped before it reaches Leaflet (never prevented), with a hint to hold Ctrl or ⌘.
      box.addEventListener(
        'wheel',
        (event) => {
          if (!event.ctrlKey && !event.metaKey) {
            event.stopPropagation();
            this.showHint('wheel');
          }
        },
        { capture: true, passive: true, signal },
      );
      // One finger scrolls the page and two move the map: Leaflet drags on one finger and calls preventDefault on its touchmove,
      // which no touch-action can override, so dragging is switched with the number of fingers (design D5).
      box.addEventListener('touchstart', (event) => this.engine()?.setDragging(event.touches.length >= 2), { capture: true, passive: true, signal });
      box.addEventListener(
        'touchmove',
        (event) => {
          if (event.touches.length === 1 && this.engine() && !this.engine()!.isDragging()) {
            this.showHint('touch');
          }
        },
        { capture: true, passive: true, signal },
      );
      const lifted = (event: TouchEvent) => {
        if (event.touches.length === 0) {
          this.engine()?.setDragging(false);
        }
      };
      box.addEventListener('touchend', lifted, { capture: true, passive: true, signal });
      box.addEventListener('touchcancel', lifted, { capture: true, passive: true, signal });
      // A mouse on a touch screen still drags.
      box.addEventListener('pointerdown', (event) => event.pointerType === 'mouse' && this.engine()?.setDragging(true), { capture: true, passive: true, signal });
      onCleanup(() => abort.abort());
    });

    // The widget comes near the screen: only then is the map (and its tiles) made.
    effect((onCleanup) => {
      const box = this.box()?.nativeElement;
      if (!box || this.visible()) {
        return;
      }
      const observer = new IntersectionObserver(
        (entries) => {
          if (entries.some((entry) => entry.isIntersecting)) {
            this.visible.set(true);
            observer.disconnect();
          }
        },
        { rootMargin: '200px' },
      );
      observer.observe(box);
      onCleanup(() => observer.disconnect());
    });

    // The map itself: created once there is a host to put it in, and again when the kind of coordinates changes.
    effect((onCleanup) => {
      const host = this.host()?.nativeElement;
      const kind = this.kind();
      if (!host || !kind || !this.visible()) {
        return;
      }
      const drawn = untracked(() => this.drawn());
      // Coming back from "Show as table" the member's own view returns; for other rows it is no view at all.
      const restore = untracked(() => (this.touched() && this.viewOf === drawn ? this.current() : null));
      let live = true;
      let created: MapEngine | null = null;
      onCleanup(() => {
        live = false;
        this.closeTip();
        created?.destroy();
        created = null;
        this.engine.set(null);
        this.drawnOnEngine = null;
      });
      this.drawnOnEngine = drawn;
      this.viewOf = drawn;
      MapEngine.create(host, { kind, shapes: shapesOf(drawn), view: restore, reducedMotion: prefersReducedMotion(), events: this.events }).then(
        (engine) => {
          if (!live) {
            engine.destroy();
            return;
          }
          created = engine;
          this.current.set(engine.view());
          this.engine.set(engine);
        },
        () => this.loadFailed.set(true),
      );
    });

    // New rows replace the shapes and take the view and the member's switch back to where a fresh map starts.
    effect(() => {
      const drawn = this.drawn();
      const engine = this.engine();
      if (!engine || drawn === this.drawnOnEngine) {
        return;
      }
      untracked(() => {
        this.drawnOnEngine = drawn;
        this.viewOf = drawn;
        this.touched.set(false);
        this.viewerBasemap.set(undefined);
        this.failed.set(false);
        this.closeTip();
        engine.setData(shapesOf(drawn));
      });
    });

    // The basemap layer: rebuilt when the kind, the provider or the room for it changes.
    effect((onCleanup) => {
      const engine = this.engine();
      const key = this.layerKey();
      if (!engine) {
        return;
      }
      const token = ++this.layerToken;
      onCleanup(() => {
        this.layerToken++;
        this.stopWatching();
        clearTimeout(this.creditsTimer);
      });
      untracked(() => {
        engine.setTiles(null);
        engine.setBackdrop(false);
        this.tilesShown.set(null);
        this.credits.set(null);
        this.creditsCache.clear();
        this.retried = false;
        const entry = this.entry();
        if (!key || !entry) {
          return;
        }
        if (entry.attribution.source === 'osm') {
          engine.setTiles({ template: entry.tiles, maxNativeZoom: entry.maxZoom, referrerPolicy: 'strict-origin' }, this.tiles);
          engine.setBackdrop(true);
          this.tilesShown.set('osm');
        } else {
          void this.startGoogle(engine, entry.kind, token);
        }
      });
    });

    // Fresh links from a refreshed list reach the tiles still to come; tiles on screen stay.
    effect(() => {
      const engine = this.engine();
      const entry = this.entry();
      const shown = this.tilesShown();
      if (!engine || !entry || !shown || shown !== entry.attribution.source) {
        return;
      }
      untracked(() => {
        if (engine.tileTemplate() !== entry.tiles) {
          engine.updateTiles({ template: entry.tiles });
        }
      });
    });

    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.hintTimer);
      clearTimeout(this.creditsTimer);
      this.closeTip();
      this.tooltip.destroy();
      this.stopWatching();
    });
  }

  // --- Template helpers -------------------------------------------------------------------------------------------------

  protected color(step: number): string {
    return sequentialColor(step);
  }

  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
  }

  protected summary(t: Translate): string {
    const { min, max } = this.range();
    return t(this.key('indicators.map.summary', this.drawn().length), {
      what: this.what(),
      count: this.drawn().length,
      min: this.format()(min),
      max: this.format()(max),
      left: this.leftOut(),
    });
  }

  /** None and the kinds the deployment offers; empty when it offers none. */
  protected choices(t: Translate): BasemapChoice[] {
    const offered = this.offered();
    return offered.length ? [{ id: 'none', label: t('indicators.map.basemaps.none') }, ...offered.map((entry) => ({ id: entry.kind, label: t(`indicators.map.basemaps.${entry.kind}`) }))] : [];
  }

  /** The one thing a map says about its basemap when it has none to show. */
  protected note(t: Translate): string | null {
    if (this.loadFailed()) {
      return t('indicators.map.notes.loadFailed');
    }
    if (this.failed()) {
      return t('indicators.map.notes.unavailable');
    }
    if (!this.entry() || !this.kind()) {
      return null;
    }
    if (this.kind() !== 'degrees') {
      return t('indicators.map.notes.notDegrees');
    }
    return this.roomy() ? null : t('indicators.map.notes.enlarge');
  }

  protected table(t: Translate): TableModel {
    const items = this.config().tooltip ?? [];
    const types = this.tooltipTypes();
    const context = this.context();
    return {
      headers: [this.config().groupBy ?? '', this.what(), ...items.map(tooltipLabel), t('indicators.map.geometry')],
      align: ['start', 'end', ...items.map((_, i) => (types?.[i] === 'integer' || types?.[i] === 'decimal' ? ('end' as const) : ('start' as const))), 'start'],
      rows: this.parsed().map(({ row, geometry }) => [
        row.group ?? t('indicators.chart.noGroup'),
        this.format()(row.value),
        ...tooltipValues(row, context),
        geometry === null ? t('indicators.map.kind.none') : t(`indicators.map.kind.${geometry.type === 'point' ? 'point' : 'polygon'}`),
      ]),
    };
  }

  // --- Controls ---------------------------------------------------------------------------------------------------------

  protected zoomIn(): void {
    this.engine()?.zoomIn();
  }

  protected zoomOut(): void {
    this.engine()?.zoomOut();
  }

  protected showAll(): void {
    this.engine()?.showAll();
    this.touched.set(false);
  }

  /** The member's own switch: it applies to this map on this screen only, and is announced. */
  protected chooseBasemap(id: string, t: Translate): void {
    this.viewerBasemap.set(id as Switch);
    this.failed.set(false);
    const name = id === 'none' ? t('indicators.map.announce.none') : t('indicators.map.announce.basemap', { name: t(`indicators.map.basemaps.${id}`) });
    this.announce(name);
  }

  private announce(text: string): void {
    // The same words twice in a row would not be read again; a zero-width mark makes them a change.
    this.announced = !this.announced;
    this.announcement.set(this.announced ? text : `${text}​`);
  }

  private showHint(hint: Hint): void {
    this.hint.set(hint);
    clearTimeout(this.hintTimer);
    this.hintTimer = setTimeout(() => this.hint.set(null), hintMs);
  }

  private measure(): void {
    const box = this.box()?.nativeElement;
    if (box && box.clientWidth > 0 && box.clientHeight > 0) {
      this.size.set({ width: box.clientWidth, height: box.clientHeight });
    }
  }

  // --- Keyboard (design D5, D6) -----------------------------------------------------------------------------------------

  protected onKeydown(event: KeyboardEvent): void {
    const engine = this.engine();
    const host = this.host()?.nativeElement;
    if (!engine || !host || !(event.target instanceof Node) || !host.contains(event.target) || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    const active = engine.activeIndex();
    const count = this.drawn().length;
    if (event.key === 'Escape') {
      // Only an Escape that does something is consumed; a hover tooltip never is, so a dialog around the map still sees it.
      if (active !== null) {
        this.leaveShapes();
        this.consume(event);
      } else if (this.tip?.mode === 'pinned') {
        this.closeTip();
        this.consume(event);
      }
      return;
    }
    if (active !== null) {
      const next =
        event.key === 'ArrowRight' || event.key === 'ArrowDown'
          ? Math.min(count - 1, active + 1)
          : event.key === 'ArrowLeft' || event.key === 'ArrowUp'
            ? Math.max(0, active - 1)
            : event.key === 'Home'
              ? 0
              : event.key === 'End'
                ? count - 1
                : null;
      if (next !== null) {
        this.consume(event);
        this.step(next);
      }
      return;
    }
    if (event.target !== host) {
      return;
    }
    if (event.key === '0' || event.key === 'Home') {
      this.consume(event);
      this.showAll();
    } else if (event.key === 'Enter' && count > 0) {
      this.consume(event);
      this.step(0);
    }
  }

  private consume(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
  }

  /** Focus is on a shape and goes to another: its tooltip is shown and stays, and a screen reader reads the same text. */
  private step(index: number): void {
    const engine = this.engine();
    const entry = this.drawn()[index];
    if (!engine || !entry) {
      return;
    }
    engine.activate(index, tooltipSpeech(this.contentOf(entry)));
    const at = engine.anchorOf(index);
    if (at) {
      this.show(index, at, 'pinned');
    }
  }

  private leaveShapes(): void {
    // The map takes the focus before the shape loses its attributes, so a focused element never stops being focusable.
    this.engine()?.focusMap();
    this.engine()?.deactivate();
    this.closeTip();
  }

  protected onFocusOut(event: FocusEvent): void {
    const box = this.box()?.nativeElement;
    const host = this.host()?.nativeElement;
    const next = event.relatedTarget;
    // From one shape to another the keyboard is still on the shapes.
    if (host && next instanceof Node && host.contains(next) && next !== host) {
      return;
    }
    // The focus left the shapes (for the map, a control or elsewhere): no shape is the keyboard's any more, and the tooltip it pinned
    // has nothing left to point at. A tooltip pinned by a tap stays while the member uses the controls, and goes once the focus leaves
    // the whole map.
    const onShape = this.engine()?.activeIndex() != null;
    this.engine()?.deactivate();
    if (this.tip?.mode === 'pinned' && (onShape || !(box && next instanceof Node && box.contains(next)))) {
      this.closeTip();
    }
  }

  // --- Tooltip ----------------------------------------------------------------------------------------------------------

  private context(): TooltipContext {
    return {
      items: this.config().tooltip ?? [],
      types: this.tooltipTypes(),
      label: this.label(),
      format: this.format(),
      locale: this.locale,
      t: (key, params) => this.transloco.translate(key, params),
    };
  }

  private contentOf(entry: Drawn) {
    return tooltipContent(entry.row, this.context());
  }

  /** A point in the container's pixels, as a point in the viewport's. */
  private page(at: MapPoint): MapPoint {
    const rect = this.host()?.nativeElement.getBoundingClientRect();
    return { x: (rect?.left ?? 0) + at.x, y: (rect?.top ?? 0) + at.y };
  }

  private show(index: number, at: MapPoint, mode: 'hover' | 'pinned'): void {
    const entry = this.drawn()[index];
    if (!entry) {
      return;
    }
    this.tip = { index, mode };
    this.tooltip.open(this.page(at), this.contentOf(entry));
    if (mode === 'pinned') {
      this.watchPinned();
    }
  }

  private onHover(index: number, at: MapPoint): void {
    if (!this.tip || this.tip.mode === 'hover') {
      this.show(index, at, 'hover');
    }
  }

  private onMove(index: number, at: MapPoint): void {
    if (this.tip?.mode !== 'hover' || this.tip.index !== index) {
      return;
    }
    this.nextPoint = at;
    if (this.moveFrame === null) {
      const move = () => {
        this.moveFrame = null;
        if (this.tip?.mode === 'hover') {
          this.tooltip.move(this.page(this.nextPoint));
        }
      };
      this.moveFrame = typeof requestAnimationFrame === 'function' ? requestAnimationFrame(move) : (setTimeout(move, 16) as unknown as number);
    }
  }

  private onLeave(index: number): void {
    if (this.tip?.mode === 'hover' && this.tip.index === index) {
      this.closeTip();
    }
  }

  /** A tap, a click or the keyboard keeps a tooltip open until something else is done. */
  private pin(index: number, at: MapPoint): void {
    this.show(index, at, 'pinned');
  }

  private closeHover(): void {
    if (this.tip?.mode === 'hover') {
      this.closeTip();
    }
  }

  private closePinned(): void {
    if (this.tip?.mode === 'pinned') {
      this.closeTip();
    }
  }

  private closeTip(): void {
    this.tip = null;
    this.tooltip.close();
    this.pinWatch?.abort();
    this.pinWatch = null;
  }

  /** While a tooltip is pinned: a pointer elsewhere on the page, or a scroll, closes it. */
  private watchPinned(): void {
    if (this.pinWatch) {
      return;
    }
    const abort = new AbortController();
    this.pinWatch = abort;
    const { signal } = abort;
    document.addEventListener(
      'pointerdown',
      (event) => {
        const host = this.host()?.nativeElement;
        if (!host || !(event.target instanceof Node) || !host.contains(event.target)) {
          this.engine()?.deactivate();
          this.closeTip();
        }
      },
      { capture: true, passive: true, signal },
    );
    document.addEventListener('scroll', () => this.closeTip(), { capture: true, passive: true, signal });
  }

  /** The view settled: a pinned tooltip follows its shape. */
  private onSettled(view: EngineView, byUser: boolean): void {
    this.current.set(view);
    if (byUser) {
      this.touched.set(true);
    }
    if (this.tip?.mode === 'pinned') {
      const at = this.engine()?.anchorOf(this.tip.index);
      if (at) {
        const size = this.engine()!.size();
        this.tooltip.move(this.page({ x: Math.min(Math.max(at.x, 0), size.x), y: Math.min(Math.max(at.y, 0), size.y) }));
      }
    }
    this.scheduleCredits();
  }

  // --- Tiles ------------------------------------------------------------------------------------------------------------

  private stopWatching(): void {
    this.releaseWatch?.();
    this.releaseWatch = null;
  }

  /**
   * A Google layer is added only after its credits answer (imagery is never shown without them), at the depth Google has for the
   * area, so tiles above the local imagery are never asked for.
   */
  private async startGoogle(engine: MapEngine, kind: BasemapKind, token: number): Promise<void> {
    this.releaseWatch ??= this.basemaps.watch();
    try {
      if (this.basemaps.linksDue()) {
        await this.basemaps.refresh();
      }
      const entry = this.offered().find((candidate) => candidate.kind === kind);
      if (token !== this.layerToken || !entry || entry.attribution.source !== 'google') {
        return;
      }
      const area = areaOf(engine);
      const credits = await firstValueFrom(this.basemaps.credits(kind, area));
      if (token !== this.layerToken) {
        return;
      }
      this.creditsCache.set(areaKey(area), credits);
      engine.setTiles({ template: entry.tiles, maxNativeZoom: Math.min(googleZoom, credits.maxZoom) }, this.tiles);
      engine.setBackdrop(true);
      this.credits.set(credits.text);
      this.tilesShown.set('google');
    } catch {
      if (token === this.layerToken) {
        this.fail();
      }
    }
  }

  private scheduleCredits(): void {
    if (this.tilesShown() !== 'google') {
      return;
    }
    clearTimeout(this.creditsTimer);
    this.creditsTimer = setTimeout(() => void this.updateCredits(), creditsDelayMs);
  }

  /** The credits and the imagery depth follow the view; a failed request keeps the last ones. */
  private async updateCredits(): Promise<void> {
    const engine = this.engine();
    const kind = this.entry()?.kind;
    const token = this.layerToken;
    if (!engine || !kind || this.tilesShown() !== 'google') {
      return;
    }
    const area = areaOf(engine);
    const key = areaKey(area);
    try {
      const credits = this.creditsCache.get(key) ?? (await firstValueFrom(this.basemaps.credits(kind, area)));
      if (token !== this.layerToken) {
        return;
      }
      this.creditsCache.set(key, credits);
      this.credits.set(credits.text);
      engine.updateTiles({ maxNativeZoom: Math.min(googleZoom, credits.maxZoom) });
    } catch {
      // Keeps the credits it has.
    }
  }

  /** A batch of tiles settled. More than half failed: renew due links once and try again, else give the basemap up. */
  private async onTilesSettled(batch: { loaded: number; failed: number }): Promise<void> {
    const total = batch.loaded + batch.failed;
    if (total === 0 || batch.failed * 2 <= total) {
      this.retried = false;
      return;
    }
    const engine = this.engine();
    if (this.tilesShown() === 'google' && !this.retried && this.basemaps.linksDue() && engine) {
      this.retried = true;
      const token = this.layerToken;
      await this.basemaps.refresh();
      const entry = this.entry();
      if (token === this.layerToken && entry) {
        engine.updateTiles({ template: entry.tiles, redraw: true });
        return;
      }
    }
    this.fail();
  }

  /** The tiles cannot be had: the basemap goes, once, and says so. The shapes, the zoom, the tooltip and the table never waited. */
  private fail(): void {
    if (this.failed()) {
      return;
    }
    this.failed.set(true);
    this.announce(this.transloco.translate('indicators.map.notes.unavailable'));
  }
}

function shapesOf(drawn: readonly Drawn[]): ShapeInput[] {
  return drawn.map(({ geometry, step }) => ({ geometry, step }));
}

function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** The area on screen, rounded, for Google's credits: a whole zoom and bounds kept inside the world. */
function areaOf(engine: MapEngine): CreditsArea {
  const { zoom, box } = engine.viewport();
  const round = (value: number, limit: number) => Math.round(Math.max(-limit, Math.min(limit, value)) * 1000) / 1000;
  return { zoom: Math.max(0, Math.min(googleZoom, Math.round(zoom))), north: round(box.north, 85), south: round(box.south, 85), east: round(box.east, 180), west: round(box.west, 180) };
}

const areaKey = (area: CreditsArea) => `${area.zoom}|${area.north}|${area.south}|${area.east}|${area.west}`;
