import { DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { ChartTooltip, ChartTooltipContent, ChartTooltips } from '../../../shared/ui';
import { tooltipSpeech } from './map-tooltip';

// The tooltip and the keyboard of the marks of a chart with a series (design D5): the map's pattern, in one helper. Hovering a mark
// shows its tooltip, and a tap keeps it; the chart is one Tab stop (`role="application"`), arrow keys step from mark to mark, Home and
// End go to the ends, Escape leaves the marks, and a live region says what the keyboard is on. The marks are not focused one by
// one: the chart keeps the focus and this helper keeps which mark is the keyboard's, so a chart of hundreds of marks is one stop.

/** One mark: what its tooltip says, and the point of the chart's box (its pixels) that a keyboard anchors the tooltip to. */
export interface MarkPoint {
  content: ChartTooltipContent;
  x: number;
  y: number;
}

/** A mark's place among the marks: the index of its group and of its series. */
export interface MarkAt {
  g: number;
  s: number;
}

/** The marks by group, then by series; null where the pair has no mark. */
export type MarkGrid = readonly (readonly (MarkPoint | null)[])[];

export interface MarkOptions {
  /** The focusable element that holds the marks: the chart's SVG. */
  host: () => Element | undefined;
  marks: () => MarkGrid;
  /**
   * True when the groups run across the chart (columns, lines): left and right arrows move between groups and up and down between
   * series. False when they run down it (bars, the heat map, the pyramid, the Likert bars): the other way round.
   */
  groupsAcross: () => boolean;
}

type Mode = 'hover' | 'tap' | 'key';

let helpIds = 0;

const same = (a: MarkAt | null, b: MarkAt | null) => !!a && !!b && a.g === b.g && a.s === b.s;

export class MarkTooltips {
  readonly helpId = `marks-help-${++helpIds}`;
  /** The mark the keyboard is on. */
  readonly active = signal<MarkAt | null>(null);
  /** The mark the pointer is on. */
  readonly hovered = signal<MarkAt | null>(null);
  /** What a screen reader is told: the keyboard's mark, as its tooltip says it. */
  readonly announcement = signal('');

  private tip: { at: MarkAt; mode: Mode } | null = null;
  private watch: AbortController | null = null;
  private announced = false;
  private frame: number | null = null;
  private next = { x: 0, y: 0 };

  constructor(
    private readonly tooltip: ChartTooltip,
    private readonly options: MarkOptions,
  ) {}

  /** Whether a mark is lifted: under the pointer or the keyboard's. */
  isLifted(g: number, s: number): boolean {
    const at = { g, s };
    return same(this.hovered(), at) || same(this.active(), at);
  }

  // --- The pointer -------------------------------------------------------------------------------------------------------

  hover(at: MarkAt, event: MouseEvent): void {
    if (this.tip?.mode === 'tap') {
      return;
    }
    this.active.set(null);
    this.hovered.set(at);
    this.show(at, { x: event.clientX, y: event.clientY }, 'hover');
  }

  move(at: MarkAt, event: MouseEvent): void {
    if (this.tip?.mode !== 'hover' || !same(this.tip.at, at)) {
      return;
    }
    this.next = { x: event.clientX, y: event.clientY };
    if (this.frame === null) {
      const move = () => {
        this.frame = null;
        if (this.tip?.mode === 'hover') {
          this.tooltip.move(this.next);
        }
      };
      this.frame = typeof requestAnimationFrame === 'function' ? requestAnimationFrame(move) : (setTimeout(move, 16) as unknown as number);
    }
  }

  leave(at: MarkAt): void {
    if (same(this.hovered(), at)) {
      this.hovered.set(null);
    }
    if (this.tip?.mode === 'hover' && same(this.tip.at, at)) {
      this.close();
    }
  }

  /** A tap, or a pen: the tooltip stays until the member touches elsewhere or scrolls. */
  tap(at: MarkAt, event: PointerEvent): void {
    if (event.pointerType === 'mouse') {
      return;
    }
    this.active.set(null);
    this.hovered.set(at);
    this.show(at, { x: event.clientX, y: event.clientY }, 'tap');
    this.watchOutside();
  }

  // --- The keyboard ------------------------------------------------------------------------------------------------------

  keydown(event: KeyboardEvent): void {
    const host = this.options.host();
    if (!host || !(event.target instanceof Node) || !host.contains(event.target) || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    if (event.key === 'Escape') {
      // Only an Escape that does something is consumed, so a dialog around the chart still sees the others.
      if (this.active() || this.tip?.mode === 'tap' || this.tip?.mode === 'key') {
        this.leaveMarks();
        this.consume(event);
      }
      return;
    }
    const from = this.active();
    const target = from ? this.step(from, event.key) : this.enter(event.key);
    if (target === undefined) {
      return;
    }
    this.consume(event);
    if (target) {
      this.activate(target);
    }
  }

  focusout(event: FocusEvent): void {
    const host = this.options.host();
    if (host && event.relatedTarget instanceof Node && host.contains(event.relatedTarget)) {
      return;
    }
    this.active.set(null);
    if (this.tip?.mode === 'key') {
      this.close();
    }
  }

  /** The keyboard goes to a mark: its tooltip is shown and stays, and a screen reader reads the same words. */
  activate(at: MarkAt): void {
    const mark = this.options.marks()[at.g]?.[at.s];
    const host = this.options.host();
    if (!mark || !host) {
      return;
    }
    this.active.set(at);
    this.hovered.set(null);
    const box = host.getBoundingClientRect();
    this.show(at, { x: box.left + mark.x, y: box.top + mark.y }, 'key');
    // The same words twice in a row would not be read again; a zero-width mark makes them a change.
    this.announced = !this.announced;
    const speech = tooltipSpeech(mark.content);
    this.announcement.set(this.announced ? speech : `${speech}\u200b`);
  }

  /** Leaves the marks and goes back to the chart itself, which keeps the focus. */
  leaveMarks(): void {
    this.active.set(null);
    this.hovered.set(null);
    this.close();
    const host = this.options.host();
    if (host instanceof HTMLElement || host instanceof SVGElement) {
      host.focus();
    }
  }

  /** The marks changed (new rows): whatever pointed at the old ones goes. */
  reset(): void {
    this.active.set(null);
    this.hovered.set(null);
    this.announcement.set('');
    this.close();
  }

  destroy(): void {
    this.close();
    this.tooltip.destroy();
  }

  // --- Moving ------------------------------------------------------------------------------------------------------------

  /** Where a navigation key takes the keyboard from nowhere: undefined for a key that is not one. */
  private enter(key: string): MarkAt | null | undefined {
    const marks = this.reading();
    if (key === 'End') {
      return marks.at(-1) ?? null;
    }
    return ['Enter', 'Home', 'ArrowRight', 'ArrowLeft', 'ArrowDown', 'ArrowUp'].includes(key) ? (marks[0] ?? null) : undefined;
  }

  /** Where a key takes the keyboard from a mark: the mark, null when it stays, undefined for a key that is not a navigation key. */
  private step(from: MarkAt, key: string): MarkAt | null | undefined {
    const marks = this.reading();
    if (key === 'Home') {
      return marks[0] ?? null;
    }
    if (key === 'End') {
      return marks.at(-1) ?? null;
    }
    const direction = key === 'ArrowRight' || key === 'ArrowDown' ? 1 : key === 'ArrowLeft' || key === 'ArrowUp' ? -1 : 0;
    if (!direction) {
      return undefined;
    }
    const horizontal = key === 'ArrowRight' || key === 'ArrowLeft';
    const betweenGroups = this.options.groupsAcross() ? horizontal : !horizontal;
    const grid = this.options.marks();
    if (betweenGroups) {
      for (let g = from.g + direction; g >= 0 && g < grid.length; g += direction) {
        const s = this.nearest(grid[g], from.s);
        if (s !== null) {
          return { g, s };
        }
      }
      return null;
    }
    const row = grid[from.g] ?? [];
    for (let s = from.s + direction; s >= 0 && s < row.length; s += direction) {
      if (row[s]) {
        return { g: from.g, s };
      }
    }
    return null;
  }

  /** The marks in reading order: group by group, series by series. */
  private reading(): MarkAt[] {
    return this.options.marks().flatMap((row, g) => row.flatMap((mark, s) => (mark ? [{ g, s }] : [])));
  }

  /** The series of a group that has a mark and is nearest to `s`, or null when it has none. */
  private nearest(row: readonly (MarkPoint | null)[], s: number): number | null {
    for (let distance = 0; distance < row.length; distance++) {
      for (const candidate of [s - distance, s + distance]) {
        if (row[candidate]) {
          return candidate;
        }
      }
    }
    return null;
  }

  // --- The tooltip -------------------------------------------------------------------------------------------------------

  private show(at: MarkAt, origin: { x: number; y: number }, mode: Mode): void {
    const mark = this.options.marks()[at.g]?.[at.s];
    if (!mark) {
      return;
    }
    this.tip = { at, mode };
    this.tooltip.open(origin, mark.content);
  }

  private close(): void {
    this.tip = null;
    this.tooltip.close();
    this.watch?.abort();
    this.watch = null;
  }

  /** While a tooltip is kept by a tap: a touch elsewhere on the page, or a scroll, closes it. */
  private watchOutside(): void {
    if (this.watch) {
      return;
    }
    const abort = new AbortController();
    this.watch = abort;
    const { signal } = abort;
    document.addEventListener(
      'pointerdown',
      (event) => {
        const host = this.options.host();
        if (!host || !(event.target instanceof Node) || !host.contains(event.target)) {
          this.hovered.set(null);
          this.close();
        }
      },
      { capture: true, passive: true, signal },
    );
    document.addEventListener(
      'scroll',
      () => {
        this.hovered.set(null);
        this.close();
      },
      { capture: true, passive: true, signal },
    );
  }

  private consume(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
  }
}

/**
 * The tooltips and the keyboard of a chart's marks, to be made in the chart's constructor or field initializers (it injects). It
 * closes its tooltip when the marks change and when the chart is destroyed.
 */
export function markTooltips(options: MarkOptions): MarkTooltips {
  const marks = new MarkTooltips(inject(ChartTooltips).create(), options);
  effect(() => {
    options.marks();
    untracked(() => marks.reset());
  });
  inject(DestroyRef).onDestroy(() => marks.destroy());
  return marks;
}
