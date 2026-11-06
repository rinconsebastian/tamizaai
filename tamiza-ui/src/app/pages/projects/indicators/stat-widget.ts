import { Component, ElementRef, LOCALE_ID, computed, effect, inject, input, output, viewChild } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartBases, figureNames, isSmallBase, nLine, noChartBases, smallBaseNote, smallBaseOpacity } from './base-figures';
import { registerExportParts } from './export-parts';
import { ColumnNamer, ValueFormatter, captionShown, plotted, rawNames } from './format';
import { clampFigure, figureSize } from './stat-figure';

/**
 * A stat tile: one figure for its indicator, formatted as the indicator says. The server sums the measure over every row the
 * query matches, or divides the sum of the numerator by the sum of the denominator for a ratio, or takes the tile's statistic of the
 * rows' values, and answers with that one row. Under what it plots it can show up to two lines, its base n and its value without the
 * dashboard filters (improve-stat-cards, design D3), in that order, which it draws itself so the n line is drawn once.
 */
@Component({
  selector: 'app-stat-widget',
  template: `
    <div class="flex h-full min-h-0 flex-col justify-center-safe" style="container-type: size" data-testid="stat">
      <p
        #figure
        class="font-semibold text-heading"
        style="overflow-x: clip; text-wrap: balance"
        [style.font-size]="figureSize"
        [style.line-height]="1"
        [style.opacity]="small() ? smallOpacity : null"
        [attr.data-small]="small() ? 'true' : null"
        [attr.title]="value()"
        data-testid="stat-value"
      >{{ value() }}</p>
      <p
        class="break-words text-sm text-body"
        [class.sr-only]="!captionShown(config())"
        [class.line-clamp-2]="hasLines()"
        [attr.title]="hasLines() ? description() : null"
        data-testid="stat-label"
      >{{ description() }}</p>
      @if (smallNote(); as note) {
        <p class="truncate text-xs text-body" [attr.title]="note" data-testid="widget-small-note">{{ note }}</p>
      }
      @for (line of lines(); track $index) {
        <p class="truncate text-xs text-body" [attr.title]="line" data-testid="stat-line">{{ line }}</p>
      }
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
  /** The widget's base n, as the query answered it (null when the widget has none). */
  readonly n = input<number | null | undefined>(null);
  /** The value without the dashboard filters, as the query answered it when the tile asked for it and a filter narrowed it. */
  readonly unfiltered = input<number | null | undefined>(null);
  /** 1 while the figure is dimmed for a small base, so the widget can explain the dimming under the tile (design D11). */
  readonly dimmed = output<number>();
  protected readonly smallOpacity = smallBaseOpacity;
  /** The figure's size: container units, clamped to 18..30 px (design D1). */
  protected readonly figureSize = figureSize;
  private readonly figure = viewChild<ElementRef<HTMLElement>>('figure');
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  /**
   * The note that the figure is dimmed for a small base (add-small-sample-warnings, design D11), drawn inside the tile, above its other
   * lines, so it takes its room from the label and the lines and never from the figure (improve-stat-cards, design D1). The widget does
   * not draw it under a stat tile.
   */
  protected readonly smallNote = computed(() => {
    const min = this.bases().minN;
    return this.small() && min !== null && min !== undefined ? smallBaseNote(min, this.locale, (key, params) => this.transloco.translate(key, params)) : null;
  });
  /** Whether the tile has a line under its label. */
  protected readonly hasLines = computed(() => this.lines().length > 0 || this.smallNote() !== null);

  protected readonly value = computed(() => {
    const row = this.rows()[0];
    return row ? this.format()(row.value) : '—';
  });
  /** What the tile plots, naming its statistic when it has one ("Median of Pagó por la última boleta (E7): value"). */
  protected readonly description = computed(() => {
    const what = plotted(this.config(), this.label(), this.names());
    const statistic = this.config().statistic;
    return statistic ? this.transloco.translate(`indicators.stat.of.${statistic}`, { what }) : what;
  });
  /** The tile's n: the response's, else its row's. */
  private readonly tileN = computed(() => this.n() ?? this.rows()[0]?.n);
  /**
   * The n line, "n = 476 Hogares" (add-small-sample-warnings, design D11), when the tile chooses it or the base n line is in force for it
   * (improve-stat-cards, design D3), and it has an n. A tile with a statistic names the base column or nothing ("n = 37"): its n counts
   * values; one without names the base column, else the denominator.
   */
  private readonly nText = computed(() => {
    if (!this.config().display?.secondary?.includes('n') && !this.bases().showN) {
      return null;
    }
    const baseColumn = this.bases().baseColumn;
    const name = this.config().statistic ? (baseColumn ? this.names()(baseColumn) : null) : figureNames(this.config(), this.names(), baseColumn).base;
    return nLine(this.tileN(), name, this.locale);
  });
  /** "Without filters: 46.85% de los hogares", when the tile chose it and the query gave the figure. */
  private readonly withoutFilters = computed(() => {
    const value = this.unfiltered();
    return this.config().display?.secondary?.includes('unfiltered') && typeof value === 'number'
      ? this.transloco.translate('indicators.stat.withoutFilters', { value: this.format()(value) })
      : null;
  });
  /** The lines under what the tile plots, in order: the n line first. */
  protected readonly lines = computed(() => [this.nText(), this.withoutFilters()].filter((line): line is string => !!line));
  /** The row of a tile is one mark: its n is the sum of the denominator or of the base column over its rows, or the number of values of its statistic (design D2). */
  protected readonly small = computed(() => isSmallBase(this.tileN(), this.bases().minN));

  /** The size the figure has now, which the picture of the tile writes it at: read from the page, since the browser resolves the container units. */
  private size(): number {
    const element = this.figure()?.nativeElement;
    return clampFigure(element ? parseFloat(getComputedStyle(element).fontSize) : NaN);
  }

  // What an image of the tile says (add-widget-export): the figure and what it measures.
  constructor() {
    effect(() => this.dimmed.emit(this.small() ? 1 : 0));
    registerExportParts({
      plot: () => null,
      settle: () => undefined,
      parts: () => ({ caption: null, legend: null, notes: [], stat: { value: this.value(), description: this.description(), size: this.size(), secondary: this.lines(), ...(this.small() ? { dimmed: true } : {}) }, table: null }),
    });
  }
}
