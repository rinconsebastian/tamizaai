// Scales, SVG path helpers and colors shared by the indicator widgets (design D5). Everything here is plain math on numbers, so
// every chart is a computed() layout over inline SVG, testable under jsdom without a canvas or a chart library.

export interface Point {
  x: number;
  y: number;
}

/** Maps a numeric domain onto a pixel range; a flat domain maps to the middle of the range. */
export interface LinearScale {
  (value: number): number;
  readonly domain: readonly [number, number];
  readonly range: readonly [number, number];
}

export function linearScale(domain: readonly [number, number], range: readonly [number, number]): LinearScale {
  const [d0, d1] = domain;
  const [r0, r1] = range;
  const scale = (value: number) => (d1 === d0 ? (r0 + r1) / 2 : r0 + ((value - d0) / (d1 - d0)) * (r1 - r0));
  return Object.assign(scale, { domain, range });
}

export interface NiceAxis {
  min: number;
  max: number;
  step: number;
  ticks: number[];
}

/** A "nice" axis (1, 2, 2.5 or 5 times a power of ten) covering `[min, max]`, with about `count` ticks. */
export function niceAxis(min: number, max: number, count = 5): NiceAxis {
  if (!Number.isFinite(min) || !Number.isFinite(max)) {
    return { min: 0, max: 1, step: 0.25, ticks: [0, 0.25, 0.5, 0.75, 1] };
  }
  if (min === max) {
    min = min - 1;
    max = max + 1;
  }
  const rough = (max - min) / Math.max(1, count);
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const fraction = rough / magnitude;
  const step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10) * magnitude;
  const low = Math.floor(min / step + 1e-9) * step;
  const high = Math.ceil(max / step - 1e-9) * step;
  const ticks: number[] = [];
  for (let value = low, i = 0; value <= high + step / 1e6 && i < 1000; value += step, i++) {
    ticks.push(Number(value.toPrecision(12)));
  }
  return { min: low, max: high, step, ticks };
}

/** Evenly spaced bands for categories: where each starts and how wide it is, with a gap `padding` (0..1) of the step between. */
export interface BandScale {
  step: number;
  bandwidth: number;
  start(index: number): number;
}

export function bandScale(count: number, range: readonly [number, number], padding = 0.2): BandScale {
  const length = Math.abs(range[1] - range[0]);
  const step = count > 0 ? length / count : length;
  const bandwidth = step * (1 - padding);
  const origin = Math.min(range[0], range[1]);
  return { step, bandwidth, start: (index) => origin + index * step + (step - bandwidth) / 2 };
}

const fixed = (n: number) => Number(n.toFixed(2));

/** `M x y L x y ...`; an empty list gives an empty path. */
export function linePath(points: readonly Point[]): string {
  return points.map((p, i) => `${i === 0 ? 'M' : 'L'}${fixed(p.x)} ${fixed(p.y)}`).join(' ');
}

/** A closed polygon path of rings; with even-odd filling the later rings are holes. */
export function ringsPath(rings: readonly (readonly Point[])[]): string {
  return rings
    .filter((ring) => ring.length)
    .map((ring) => `${linePath(ring)} Z`)
    .join(' ');
}

/** A pie slice from angle `a0` to `a1` (radians, 0 at 12 o'clock, clockwise). A full circle needs a `<circle>` instead. */
export function slicePath(cx: number, cy: number, radius: number, a0: number, a1: number): string {
  const at = (angle: number): Point => ({ x: cx + radius * Math.sin(angle), y: cy - radius * Math.cos(angle) });
  const start = at(a0);
  const end = at(a1);
  const large = a1 - a0 > Math.PI ? 1 : 0;
  return `M${fixed(cx)} ${fixed(cy)} L${fixed(start.x)} ${fixed(start.y)} A${fixed(radius)} ${fixed(radius)} 0 ${large} 1 ${fixed(end.x)} ${fixed(end.y)} Z`;
}

// Colors come from the CSS custom properties in styles.css, which swap for the dark theme: eight categorical hues in a fixed
// order (never cycled; a ninth series is drawn in the neutral) and one sequential ramp of seven steps.

export const seriesCount = 8;
export const sequentialSteps = 7;

/** The color of the n-th series (0-based): a fixed order, then a neutral for anything past the eighth. */
export function seriesColor(index: number): string {
  return index < seriesCount ? `var(--tmz-series-${index + 1})` : 'var(--tmz-series-other)';
}

/** The step (1..7) of the sequential ramp for a value between `min` and `max`; a flat range uses the middle step. */
export function sequentialStep(value: number, min: number, max: number): number {
  if (max <= min) {
    return Math.ceil(sequentialSteps / 2);
  }
  const ratio = (value - min) / (max - min);
  return Math.min(sequentialSteps, Math.max(1, Math.floor(ratio * sequentialSteps) + 1));
}

export function sequentialColor(step: number): string {
  return `var(--tmz-seq-${step})`;
}

/** Natural order: 2 before 10, and text by the browser's rules. */
export const naturalCompare = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' }).compare;

/** Cuts text to at most `max` characters with an ellipsis. */
export function truncate(text: string, max: number): string {
  if (max < 1) {
    return '';
  }
  return text.length <= max ? text : `${text.slice(0, Math.max(1, max - 1))}…`;
}

/** Rough width of 12px text, for deciding how many characters of a label fit. */
export const charWidth = 6.5;

export function compactNumber(locale: string): (value: number) => string {
  const format = new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 });
  return (value) => format.format(value);
}

export function plainNumber(locale: string): (value: number) => string {
  const format = new Intl.NumberFormat(locale, { maximumFractionDigits: 2 });
  return (value) => format.format(value);
}
