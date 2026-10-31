import { Component, inject, input } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { countKey } from '../../../core/i18n/plural';
import { SeriesKind } from '../../../core/projects/indicators.service';
import { seriesColor } from './chart-scales';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/** How a series is named in a legend, a tooltip or a table: its value, or "(none)" and "Other" for the two kinds that have none. */
export function seriesName(column: { series: string | null; kind: SeriesKind }, t: Translate): string {
  return column.kind === 'value' ? (column.series ?? '') : t(column.kind === 'none' ? 'indicators.series.none' : 'indicators.series.other');
}

/**
 * The color of a series: its place in the answer's `series` (design D4, rule d), never the order the rows came in, so a dashboard
 * filter that leaves a series without rows does not repaint the others. "Other" has its own neutral.
 */
export function seriesFill(kind: SeriesKind, index: number): string {
  return kind === 'other' ? 'var(--tmz-series-other)' : seriesColor(index);
}

export interface LegendEntry {
  name: string;
  color: string;
  /** A swatch that is as light as the surface (a Likert scale's neutral) gets a hairline, or it would not show. */
  outlined?: boolean;
}

/**
 * The legend of a chart with two or more series (design D5): a swatch in the series' color beside its name, in the text colors; the
 * color is the mark's, the name is what identifies it. A line chart's keys are short strokes, as its marks are. It wraps rather than
 * scrolls, so on paper it is whole.
 */
@Component({
  selector: 'app-series-legend',
  imports: [TranslocoDirective],
  template: `
    <ul *transloco="let t" class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-body" [attr.aria-label]="t('indicators.series.legend')" data-testid="series-legend">
      @for (entry of entries(); track $index) {
        <li class="flex min-w-0 items-center gap-1.5" data-testid="legend-item">
          <span aria-hidden="true" class="inline-block shrink-0" [class]="(shape() === 'line' ? 'h-0.5 w-4 rounded-full' : 'size-2.5 rounded-sm') + (entry.outlined ? ' border border-default-strong' : '')" [style.background]="entry.color" data-testid="legend-swatch"></span>
          <span class="min-w-0 break-words" data-testid="legend-name">{{ entry.name }}</span>
        </li>
      }
    </ul>
  `,
  host: { class: 'block shrink-0' },
})
export class SeriesLegend {
  readonly entries = input.required<readonly LegendEntry[]>();
  readonly shape = input<'square' | 'line'>('square');
}

/**
 * What a chart or a table with a series left out, said under it: negative values that a table of shares left out of the shares, and the
 * rows a pyramid or a Likert widget left out (design D4, D5). `kind` picks the words for the rows.
 */
@Component({
  selector: 'app-series-notes',
  imports: [TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      @if (negativeLeftOut() > 0) {
        <p class="shrink-0 text-xs text-body" data-testid="series-negative-left-out">{{ t(key('indicators.series.negativeLeftOut', negativeLeftOut()), { count: negativeLeftOut() }) }}</p>
      }
      @if (leftOut() > 0 && kind(); as kind) {
        <p class="shrink-0 text-xs text-fg-warning" data-testid="series-left-out">{{ t(key('indicators.' + kind + '.leftOut', leftOut()), { count: leftOut() }) }}</p>
      }
    </ng-container>
  `,
  host: { class: 'contents' },
})
export class SeriesNotes {
  private readonly transloco = inject(TranslocoService);

  readonly negativeLeftOut = input(0);
  readonly leftOut = input(0);
  /** Whose words say the rows left out: a pyramid's or a Likert widget's; null for a chart that leaves none out. */
  readonly kind = input<'pyramid' | 'likert' | null>(null);

  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
  }
}
