import { ChartType, Layout, gridColumns, maxWidgetHeight } from '../../../core/projects/indicators.service';

/** Pixel measures of the grid; the styles of the grid use the same numbers. */
export const rowHeight = 72;
export const gap = 16;

export interface Placed {
  id: string;
  layout: Layout;
}

/** The size a new widget gets when the server is not told one (design D7). */
export function defaultSize(type: ChartType): { w: number; h: number } {
  switch (type) {
    case 'stat':
      return { w: 3, h: 2 };
    case 'map':
      return { w: 6, h: 6 };
    default:
      return { w: 6, h: 4 };
  }
}

/** The layout kept inside the grid: whole numbers, `1 <= w <= 12`, `x + w <= 12`, `y >= 0`, `1 <= h <= 40`. */
export function clampLayout(layout: Layout): Layout {
  const w = Math.min(gridColumns, Math.max(1, Math.round(layout.w)));
  const h = Math.min(maxWidgetHeight, Math.max(1, Math.round(layout.h)));
  return { x: Math.min(gridColumns - w, Math.max(0, Math.round(layout.x))), y: Math.max(0, Math.round(layout.y)), w, h };
}

export function overlaps(a: Layout, b: Layout): boolean {
  return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
}

export function sameLayout(a: Layout, b: Layout): boolean {
  return a.x === b.x && a.y === b.y && a.w === b.w && a.h === b.h;
}

/** Top to bottom, then left to right: the order keyboards, screen readers and the "show every table" view follow. */
export function readingOrder<T extends { layout: Layout }>(items: readonly T[]): T[] {
  return [...items].sort((a, b) => a.layout.y - b.layout.y || a.layout.x - b.layout.x);
}

/**
 * Puts one widget at a new place and moves the others out of its way, downward (design D7): each widget, taken in reading order,
 * that overlaps one already settled is pushed below the lowest one it overlaps. Widgets that do not overlap stay where they are.
 */
export function reflow(items: readonly Placed[], movedId: string, next: Layout): Placed[] {
  const moved: Placed = { id: movedId, layout: clampLayout(next) };
  const settled: Placed[] = [moved];
  for (const item of readingOrder(items.filter((i) => i.id !== movedId).map((i) => ({ ...i, layout: { ...i.layout } })))) {
    let hits = settled.filter((s) => overlaps(item.layout, s.layout));
    while (hits.length) {
      item.layout.y = Math.max(...hits.map((s) => s.layout.y + s.layout.h));
      hits = settled.filter((s) => overlaps(item.layout, s.layout));
    }
    settled.push(item);
  }
  const byId = new Map(settled.map((s) => [s.id, s.layout]));
  return items.map((item) => ({ id: item.id, layout: byId.get(item.id)! }));
}

/** The place a widget dragged by `columns` and `rows` grid units, or resized by them, would take. */
export function movedBy(layout: Layout, columns: number, rows: number): Layout {
  return clampLayout({ ...layout, x: layout.x + columns, y: layout.y + rows });
}

export function resizedBy(layout: Layout, columns: number, rows: number): Layout {
  // The widget grows from its corner: it stops at the edge of the grid instead of sliding left.
  const w = Math.min(layout.w + columns, gridColumns - layout.x);
  return clampLayout({ ...layout, w, h: layout.h + rows });
}

/** Whole grid units for a pointer distance in pixels, given the width of the grid. */
export function unitsFor(distance: { x: number; y: number }, gridWidth: number): { columns: number; rows: number } {
  const column = (gridWidth - gap * (gridColumns - 1)) / gridColumns;
  return { columns: Math.round(distance.x / (column + gap)) || 0, rows: Math.round(distance.y / (rowHeight + gap)) || 0 };
}

/** The layouts that differ from the earlier ones. */
export function changed(before: readonly Placed[], after: readonly Placed[]): Placed[] {
  const old = new Map(before.map((p) => [p.id, p.layout]));
  return after.filter((p) => {
    const was = old.get(p.id);
    return was !== undefined && !sameLayout(was, p.layout);
  });
}
