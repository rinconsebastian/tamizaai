import {
  ChartType,
  DashboardColors,
  PaletteId,
  ScaleId,
  SeriesKind,
  WidgetConfig,
  paletteIds,
  paletteSize,
  scaleIds,
} from '../../../core/projects/indicators.service';
import { sequentialSteps } from './chart-scales';

// The colors of one widget (add-dashboard-palettes, design D7), resolved in one place so that a chart, its legend, its tooltips and the
// widget form's preview agree. A color is never a code here: it is the CSS custom property styles.css defines for a place in a palette,
// `var(--tmz-pal-earth-3)`, which resolves to the same color wherever it is used, the tooltips in the CDK overlay included. A
// sequential scale is the exception that is scoped: a map or a heat map sets the steps its marks read (`--tmz-seq-n`) on its own
// container, from `rampStyle` (design D1).

/** The dashboard's colors until it saves any: the palette Tamiza, and no color kept by value. */
export const defaultColors: DashboardColors = { palette: 'tamiza', valueColors: [] };

/**
 * The colors of a dashboard as its API answer, or one of its versions, holds them. An API that predates palettes sends neither field:
 * that dashboard has the palette Tamiza and no colors kept by value. A palette this app does not know is read as Tamiza too, since it
 * has no tokens for it.
 */
export function colorsOf(view: Partial<DashboardColors> | null | undefined): DashboardColors {
  const palette = view?.palette;
  return { palette: palette && paletteIds.includes(palette) ? palette : 'tamiza', valueColors: view?.valueColors ?? [] };
}

/** The sequential scale a map or a heat map is drawn on when its widget names none (`Palettes.DefaultScale`). */
export function defaultScale(palette: PaletteId): ScaleId {
  switch (palette) {
    case 'tamiza':
      return 'blue';
    case 'institutional':
      return 'teal';
    case 'earth':
      return 'orange';
    case 'vivid':
      return 'purple';
  }
}

/** The CSS value of the n-th (1 to 8) color of a palette. */
export function paletteColor(palette: PaletteId, n: number): string {
  return `var(--tmz-pal-${palette}-${n})`;
}

/** A sequential scale from its lightest step to its darkest as a CSS gradient: the swatch of a scale in the pickers. It reads the scale's own tokens. */
export function rampGradient(scale: ScaleId): string {
  const steps = Array.from({ length: sequentialSteps }, (_, i) => `var(--tmz-ramp-${scale}-${i + 1})`);
  return `linear-gradient(to right, ${steps.join(', ')})`;
}

/** The neutral of "Other", the same in every palette (and the color of a value past the eighth, which a domain never holds). */
const otherColor = 'var(--tmz-series-other)';

/** What a mark stands for, in the order a legend lists it: a value of a column, the missing value ("(none)") or "Other". */
export interface SeriesKey {
  value: string | null;
  kind: SeriesKind;
}

/** The colors of one widget. */
export interface ChartColors {
  /** The color of a chart with no series and of a histogram: the widget's own color, or color 1. */
  accent: string;
  /** The sequential scale of a map or a heat map: the widget's own, or its palette's default. */
  ramp: ScaleId;
  /** The `--tmz-seq-n` and `--tmz-seq-ink-n` (n = 1 to 7) a map or a heat map sets on its container so its marks read `ramp`. */
  rampStyle: Record<string, string>;
  /**
   * One color per value, in the order given: the series of a chart, the sides of a pyramid, the slices of a pie (design D7). A value
   * with a color kept for it in `column` takes it; those colors are set aside, and the others take the remaining colors in order.
   * "(none)" never has a color kept for it and "Other" is always the neutral.
   */
  forValues(column: string, values: readonly SeriesKey[]): string[];
  /**
   * The color kept for a value of `column`, or null: what a chart with no series draws a bar or a column in, other bars staying in
   * `accent`. Only a bar or a column chart with no series uses colors kept by value that way; any other kind gets null.
   */
  kept(column: string, value: string | null): string | null;
}

/** A place in a palette is a whole number from 1 to 8. */
function isPlace(n: unknown): n is number {
  return typeof n === 'number' && Number.isInteger(n) && n >= 1 && n <= paletteSize;
}

/**
 * The colors a widget of `type` and `config` is drawn in on a dashboard with `colors`. The widget's `display.color` and `display.ramp`
 * are read as saved: a place outside the palette or a scale this app does not know is read as unset, as the API would have refused it.
 */
export function chartColors(colors: DashboardColors, type: ChartType, config: WidgetConfig): ChartColors {
  const palette = colorsOf(colors).palette;
  const display = config.display;
  const place = isPlace(display?.color) ? display.color : 1;
  const ramp = display?.ramp && scaleIds.includes(display.ramp) ? display.ramp : defaultScale(palette);

  const rampStyle: Record<string, string> = {};
  for (let step = 1; step <= sequentialSteps; step++) {
    rampStyle[`--tmz-seq-${step}`] = `var(--tmz-ramp-${ramp}-${step})`;
    rampStyle[`--tmz-seq-ink-${step}`] = `var(--tmz-ramp-${ramp}-ink-${step})`;
  }

  // The colors kept by value, by column then exact text (case included), the first entry of a pair winning.
  const keptBy = new Map<string, Map<string, number>>();
  for (const entry of colors.valueColors ?? []) {
    if (!isPlace(entry.color)) {
      continue;
    }
    const values = keptBy.get(entry.column) ?? new Map<string, number>();
    if (!values.has(entry.value)) {
      values.set(entry.value, entry.color);
    }
    keptBy.set(entry.column, values);
  }
  const placeKept = (column: string, value: string | null): number | null => (value === null ? null : (keptBy.get(column)?.get(value) ?? null));
  const keepsByValue = ((type === 'bar' || type === 'column') && !config.seriesBy) || type === 'boxplot';

  return {
    accent: paletteColor(palette, place),
    ramp,
    rampStyle,
    forValues(column, values) {
      // 1. Each value with a color kept takes it, unless an earlier value of the widget already has that color (a column keeps each
      // color once, so this only guards a list that breaks the rule). 2. Those colors are set aside. 3. The others take what is left,
      // in order.
      const taken = new Set<number>();
      const own = values.map((key) => {
        const kept = key.kind === 'value' ? placeKept(column, key.value) : null;
        if (kept === null || taken.has(kept)) {
          return null;
        }
        taken.add(kept);
        return kept;
      });
      const free = Array.from({ length: paletteSize }, (_, i) => i + 1).filter((n) => !taken.has(n));
      let next = 0;
      return values.map((key, i) => {
        if (key.kind === 'other') {
          return otherColor;
        }
        const n = own[i] ?? free[next++];
        return n === undefined ? otherColor : paletteColor(palette, n);
      });
    },
    kept(column, value) {
      const n = keepsByValue ? placeKept(column, value) : null;
      return n === null ? null : paletteColor(palette, n);
    },
  };
}
