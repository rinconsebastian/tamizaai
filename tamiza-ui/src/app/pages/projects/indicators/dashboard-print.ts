import { ApplicationRef, DOCUMENT, DestroyRef, Injectable, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';

/** The longest the Print action waits for widgets and basemap tiles before it opens the dialog anyway (design D6). */
export const printDeadlineMs = 20_000;
/** How often the wait looks at what is still pending. */
const pollMs = 50;
/** Narrow margins, only while one of the project's dashboards prints; the member picks the orientation in the dialog (design D9). */
const pageRule = '@page { margin: 10mm; }';
const pageStyleId = 'tmz-print-page';
export const printingClass = 'tmz-printing';

/**
 * The page's title while a dashboard prints: the project, the dashboard and the day, in the project's time zone (design D10). The
 * browser offers it as the PDF's file name and replaces any character a file name cannot hold.
 */
export function printTitle(project: string, dashboard: string, now: Date, timeZone: string): string {
  return `${project} – ${dashboard} – ${isoDay(now, timeZone)}`;
}

/** `yyyy-mm-dd` of the instant in the zone; an unknown zone falls back to UTC. */
function isoDay(now: Date, timeZone: string): string {
  let parts: Intl.DateTimeFormatPart[];
  try {
    parts = new Intl.DateTimeFormat('en-US', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
  } catch {
    parts = new Intl.DateTimeFormat('en-US', { timeZone: 'UTC', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
  }
  const part = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  return `${part('year')}-${part('month')}-${part('day')}`;
}

/** Waits for the next painted frame; where there is none (jsdom without it), for about one. */
function frame(): Promise<void> {
  return new Promise((resolve) => (typeof requestAnimationFrame === 'function' ? requestAnimationFrame(() => resolve()) : setTimeout(resolve, 16)));
}

/**
 * Paper mode of one of the project's dashboards (design D1). `prepare()` lays the dashboard out as it prints, on screen too, so
 * every chart, map and table measures the box it has on paper; `restore()` undoes it. The Print action prepares, waits for the
 * widgets and tiles still pending, then opens the browser's dialog; printing from the browser's own menu prepares in `beforeprint`,
 * which cannot wait. Provided by the dashboard page, so a widget anywhere else (the widget form's preview) finds none.
 */
@Injectable()
export class DashboardPrint {
  private readonly app = inject(ApplicationRef);
  private readonly document = inject(DOCUMENT);
  private readonly view = this.document.defaultView;

  /** The dashboard is laid out as it prints. */
  readonly printing = signal(false);
  /** When the dashboard was prepared for print, for the print header; null on screen. */
  readonly printedAt = signal<Date | null>(null);
  /** Paper mode has ended; `fromAction` when it began with the Print action, whose button then takes focus back. */
  readonly restored = new Subject<{ fromAction: boolean }>();
  /** The page's title while printing; the dashboard page sets it (design D10). */
  titleSource: () => string = () => this.document.title;

  private readonly measures = new Set<() => void>();
  private readonly pending = new Set<() => boolean>();
  private savedTitle = '';
  private fromAction = false;
  private readonly disarm: (() => void)[] = [];

  constructor() {
    const view = this.view;
    if (view) {
      const before = () => {
        if (!this.printing()) {
          this.prepare();
        }
      };
      const after = () => this.restore();
      view.addEventListener('beforeprint', before);
      view.addEventListener('afterprint', after);
      // A browser that never sends afterprint still turns the print media query off once it is done (design, risks).
      const media = typeof view.matchMedia === 'function' ? view.matchMedia('print') : null;
      const changed = (event: MediaQueryListEvent) => {
        if (!event.matches) {
          this.restore();
        }
      };
      media?.addEventListener?.('change', changed);
      inject(DestroyRef).onDestroy(() => {
        view.removeEventListener('beforeprint', before);
        view.removeEventListener('afterprint', after);
        media?.removeEventListener?.('change', changed);
        this.restore();
      });
    }
  }

  /**
   * Registers what a widget does to fit the box it has on paper: read its box and redraw (design D5). Called by `prepare()` after
   * the paper layout is in place, so it also runs in `beforeprint`, where no ResizeObserver fires. Returns the unregistration.
   */
  registerMeasure(measure: () => void): () => void {
    this.measures.add(measure);
    return () => this.measures.delete(measure);
  }

  /**
   * Registers work the Print action waits for while it answers true: a query in flight, basemap tiles loading (design D6). It is
   * asked again and again while the action waits, so it can read state that is not a signal (Leaflet's tiles).
   */
  registerPending(pending: () => boolean): () => void {
    this.pending.add(pending);
    return () => this.pending.delete(pending);
  }

  /**
   * Lays the dashboard out as it prints, synchronously: the class that turns the `paper:` styles on, the page rule, the
   * title and the print time; then renders, forces the layout and has every widget measure its box, and renders again.
   */
  prepare(): void {
    if (this.printing()) {
      return;
    }
    const root = this.document.documentElement;
    this.printing.set(true);
    this.printedAt.set(new Date());
    root.classList.add(printingClass);
    const style = this.document.createElement('style');
    style.id = pageStyleId;
    style.textContent = pageRule;
    this.document.head.appendChild(style);
    this.savedTitle = this.document.title;
    this.document.title = this.titleSource();
    this.app.tick();
    // Reading a size makes the browser lay the paper styles out now, before the widgets read their boxes.
    void root.getBoundingClientRect();
    for (const measure of [...this.measures]) {
      measure();
    }
    this.app.tick();
  }

  /** Back to the screen: every step of `prepare()` undone; the widgets' observers redraw them for their screen boxes. */
  restore(): void {
    if (!this.printing()) {
      return;
    }
    this.disarm.splice(0).forEach((off) => off());
    this.document.documentElement.classList.remove(printingClass);
    this.document.getElementById(pageStyleId)?.remove();
    this.document.title = this.savedTitle;
    this.printing.set(false);
    this.printedAt.set(null);
    const fromAction = this.fromAction;
    this.fromAction = false;
    this.restored.next({ fromAction });
  }

  /**
   * Resolves true once nothing registered is pending and two frames have been painted, or once the deadline passes; false when
   * paper mode ended meanwhile (the page left), so nothing is printed.
   */
  async whenReady(deadlineMs = printDeadlineMs): Promise<boolean> {
    const started = Date.now();
    while (this.printing() && this.anyPending() && Date.now() - started < deadlineMs) {
      await new Promise((resolve) => setTimeout(resolve, pollMs));
    }
    if (!this.printing()) {
      return false;
    }
    await frame();
    await frame();
    return this.printing();
  }

  /**
   * The Print action: prepares, waits, then opens the browser's dialog; `opening` runs just before it (the action stops being
   * busy). A browser whose dialog does not block, and never says it closed, is caught by the first pointer or key on the page.
   */
  async print(opening?: () => void): Promise<void> {
    if (this.printing()) {
      return;
    }
    this.prepare();
    this.fromAction = true;
    if (!(await this.whenReady())) {
      return;
    }
    opening?.();
    this.view?.print();
    if (this.printing()) {
      this.armBackstop();
    }
  }

  private anyPending(): boolean {
    return [...this.pending].some((pending) => pending());
  }

  private armBackstop(): void {
    const view = this.view;
    if (!view) {
      return;
    }
    const back = () => this.restore();
    for (const type of ['pointerdown', 'keydown'] as const) {
      view.addEventListener(type, back, { capture: true, once: true });
      this.disarm.push(() => view.removeEventListener(type, back, { capture: true }));
    }
  }
}
