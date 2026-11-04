import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { TableModel } from './widget-table';

// What a widget shows around its chart, as data, so an image of it (add-widget-export, design D5) is drawn from the same signals the
// widget's template reads and never from its DOM. Each chart and tile registers with the `WidgetExportHost` of the `WidgetView` it
// sits in; a chart without one (the script editor's view of an indicator) simply registers nowhere.

/**
 * How a legend entry's mark looks: a square swatch, a short stroke (a line chart), a square with a hairline (a Likert neutral), or a short
 * dashed stroke in the heading color (a reference line, add-chart-annotations design D9), whose pattern is the entry's `dash`.
 */
export type ExportSwatch = 'square' | 'line' | 'outlined' | 'dashed';

export interface ExportLegendEntry {
  label: string;
  /** A CSS color (`var(--tmz-pal-tamiza-1)` or a plain one): the image resolves it in the light theme. */
  color: string;
  swatch?: ExportSwatch;
  /** The place in the widget's list of reference lines whose dash pattern a `dashed` swatch is drawn in (`dashOf`). */
  dash?: number;
  /** Written after the label, in the body color: a pie slice's value and share. */
  detail?: string;
}

/** The marks a box plot's key explains, in the order it lists them. */
export type ExportKeyGlyph = 'box' | 'median' | 'mean' | 'whiskers' | 'outliers';

export type ExportLegend =
  /** A swatch and a name for each series, slice or Likert step. */
  | { kind: 'entries'; entries: ExportLegendEntry[] }
  /** A heat map's or a map's scale: the smallest value, steps from light to dark, the largest value, and optionally what an empty cell is. */
  | { kind: 'scale'; low: string; high: string; steps: string[]; stepOpacity?: number; empty?: string | null }
  /** A box plot's key; its marks are drawn in the widget's `accent` color. */
  | { kind: 'key'; accent: string; items: { glyph: ExportKeyGlyph; label: string }[] };

export interface ExportParts {
  /** The line under the title that says what is plotted; null where the kind has none. */
  caption: string | null;
  legend: ExportLegend | null;
  /** What the widget says under its chart: rows left out, negatives set aside, outliers or groups not drawn. */
  notes: string[];
  /** A stat tile's figure and what it measures; the description is null in a picture of a widget that hides its caption (add-widget-context, design D8). */
  stat: { value: string; description: string | null } | null;
  /** The table the widget shows instead of a chart (a cross table always); null while it draws a chart. */
  table: TableModel | null;
}

export interface ExportRegistration {
  parts(): ExportParts;
  /** The chart's live `<svg>`, or null for a stat tile, a table and a map (whose picture is made by its engine). */
  plot(): SVGElement | null;
  /** Closes the tooltips and clears the marks pointed at, so the picture shows the chart at rest. */
  settle(): void;
}

/**
 * Where the charts of one `WidgetView` register. The innermost registration is the current one: a chart that shows a cross table as
 * its table view contains a `CrosstabWidget`, which registers while it is there and takes the chart's place until it goes.
 */
@Injectable()
export class WidgetExportHost {
  private readonly stack = signal<ExportRegistration[]>([]);

  /** What the widget registered last and has not released, or null before a chart exists. */
  readonly current = computed(() => this.stack().at(-1) ?? null);

  register(registration: ExportRegistration): () => void {
    this.stack.update((stack) => [...stack, registration]);
    return () => this.stack.update((stack) => stack.filter((entry) => entry !== registration));
  }
}

/**
 * Registers a chart or tile with the widget around it, if there is one, and releases it when the chart goes. Call it from the
 * component's constructor.
 */
export function registerExportParts(registration: ExportRegistration): void {
  const host = inject(WidgetExportHost, { optional: true });
  if (host) {
    inject(DestroyRef).onDestroy(host.register(registration));
  }
}
