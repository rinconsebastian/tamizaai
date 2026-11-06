// The size of a stat tile's figure (improve-stat-cards, design D1): as large as its tile gives room for, between a floor and a ceiling. The
// tile is a size container, so its own width (`cqi`) and height (`cqh`) decide; the picture and the page share the bounds.

/** The smallest and the largest size of the figure, in pixels. */
export const figureMin = 18;
export const figureMax = 30;

/**
 * The figure's font size: 9 % of the tile's width or 38 % of its height, whichever is smaller, kept between the bounds (1.125 and
 * 1.875 rem). The width coefficient keeps a value of about 20 characters on one line in a tile 3 columns wide; the height one lets a tile
 * of the default 2 rows reach the ceiling. Tuned in design.md's implementation notes.
 */
export const figureSize = 'clamp(1.125rem, min(9cqi, 38cqh), 1.875rem)';

/** The figure's computed size read from the page, kept between the bounds; `fallback` when the page gave none (a tile that is not laid out). */
export function clampFigure(size: number, fallback = 24): number {
  return Math.min(figureMax, Math.max(figureMin, Number.isFinite(size) ? size : fallback));
}
