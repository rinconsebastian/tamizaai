import { Component, computed, effect, input, output } from '@angular/core';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartBases, isSmallBase, noChartBases, smallBaseOpacity } from './base-figures';
import { registerExportParts } from './export-parts';
import { ColumnNamer, ValueFormatter, captionShown, plotted, rawNames } from './format';

/**
 * A stat tile: one figure for its indicator, formatted as the indicator says. The server sums the measure over every row the
 * query matches, or divides the sum of the numerator by the sum of the denominator for a ratio, and answers with that one row.
 */
@Component({
  selector: 'app-stat-widget',
  template: `
    <div class="flex h-full min-h-0 flex-col gap-1" [class.justify-center]="!hasTexts()" [class.justify-center-safe]="hasTexts()" data-testid="stat">
      <p class="break-all text-4xl font-semibold text-heading" [style.opacity]="small() ? smallOpacity : null" [attr.data-small]="small() ? 'true' : null" data-testid="stat-value">{{ value() }}</p>
      <p class="break-words text-sm text-body" [class.sr-only]="!captionShown(config())" data-testid="stat-label">{{ description() }}</p>
    </div>
  `,
  host: { class: 'block h-full min-h-0' },
})
export class StatWidget {
  readonly rows = input.required<QueryRow[]>();
  readonly config = input.required<WidgetConfig>();
  /** Whether the line that names what the widget plots is seen: a hidden one stays for screen readers (add-widget-context, design D3). */
  protected readonly captionShown = captionShown;
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();
  /** The name shown for each column (add-indicator-column-metadata); the columns' own names when none is given. */
  readonly names = input<ColumnNamer>(rawNames);
  /** The threshold in force: a figure whose n is below it is drawn at reduced opacity (add-small-sample-warnings, design D10). */
  readonly bases = input<ChartBases>(noChartBases);
  /** 1 while the figure is dimmed for a small base, so the widget can explain the dimming under the tile (design D11). */
  readonly dimmed = output<number>();
  protected readonly smallOpacity = smallBaseOpacity;

  /**
   * The tile has a subtitle or a footnote, which take room from it: its column is then centered while it fits and starts at the top when
   * it does not, so the figure stays whole and the label is the part cut (add-widget-context, design D5).
   */
  protected readonly hasTexts = computed(() => !!(this.config().display?.subtitle?.trim() || this.config().display?.footnote?.trim()));

  protected readonly value = computed(() => {
    const row = this.rows()[0];
    return row ? this.format()(row.value) : '—';
  });
  protected readonly description = computed(() => plotted(this.config(), this.label(), this.names()));
  /** The row of a tile is one mark: its n is the sum of the denominator or of the base column over its rows (design D2). */
  protected readonly small = computed(() => isSmallBase(this.rows()[0]?.n, this.bases().minN));

  // What an image of the tile says (add-widget-export): the figure and what it measures.
  constructor() {
    effect(() => this.dimmed.emit(this.small() ? 1 : 0));
    registerExportParts({
      plot: () => null,
      settle: () => undefined,
      parts: () => ({ caption: null, legend: null, notes: [], stat: { value: this.value(), description: this.description(), ...(this.small() ? { dimmed: true } : {}) }, table: null }),
    });
  }
}
