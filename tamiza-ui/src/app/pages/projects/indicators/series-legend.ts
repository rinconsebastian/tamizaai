import { Component, inject, input } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { countKey } from '../../../core/i18n/plural';
import { SeriesKind } from '../../../core/projects/indicators.service';
import { shown } from './category-order';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/** How a series is named in a legend, a tooltip or a table: its value, or "(none)" and "Other" for the two kinds that have none. */
export function seriesName(column: { series: string | null; kind: SeriesKind }, t: Translate, texts: ReadonlyMap<string, string> | null = null): string {
  return column.kind === 'value' ? (shown(texts, column.series) ?? '') : t(column.kind === 'none' ? 'indicators.series.none' : 'indicators.series.other');
}

export interface LegendEntry {
  name: string;
  /** The mark's color, resolved by `chart-colors.ts` (the legend never picks one). */
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

/** One reference line of a chart, as the key and the list under a table write it: its text and the dash pattern it is drawn in. */
export interface ReferenceEntry {
  text: string;
  dash: string;
}

/**
 * The key of the reference lines a chart draws (add-chart-annotations, design D5): a short swatch in each line's dash pattern, then its
 * text, `{label}: {value}`. It lists every line drawn, whether or not its text fits at the line. It belongs to the chart, so it sits
 * under the plot as a sibling of its box (`shrink-0`, taking its room before the plot does), and it wraps rather than scrolls, so on
 * paper it is whole.
 */
@Component({
  selector: 'app-reference-key',
  imports: [TranslocoDirective],
  template: `
    <ul *transloco="let t" class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-body" [attr.aria-label]="t('indicators.referenceLines.key')" data-testid="reference-key">
      @for (entry of entries(); track $index) {
        <li class="flex min-w-0 items-center gap-1.5" data-testid="reference-key-item">
          <svg aria-hidden="true" class="shrink-0" width="16" height="4" data-testid="reference-key-swatch">
            <line class="stroke-heading" x1="0" y1="2" x2="16" y2="2" stroke-width="1.5" [attr.stroke-dasharray]="entry.dash" />
          </svg>
          <span class="min-w-0 break-words" data-testid="reference-key-text">{{ entry.text }}</span>
        </li>
      }
    </ul>
  `,
  host: { class: 'block shrink-0' },
})
export class ReferenceKey {
  readonly entries = input.required<readonly ReferenceEntry[]>();
}

/**
 * The reference lines of a widget shown as a table (add-chart-annotations, design D9): one line of text for each, in the widget's order,
 * under the table and above the widget's footnote. The table itself is untouched.
 */
@Component({
  selector: 'app-reference-list',
  imports: [TranslocoDirective],
  template: `
    <ul *transloco="let t" class="text-xs text-body" [attr.aria-label]="t('indicators.referenceLines.key')" data-testid="reference-list">
      @for (entry of entries(); track $index) {
        <li data-testid="reference-list-item">{{ entry.text }}</li>
      }
    </ul>
  `,
  host: { class: 'block shrink-0' },
})
export class ReferenceList {
  readonly entries = input.required<readonly ReferenceEntry[]>();
}

/** The texts `app-series-notes` shows, for a picture of the widget: negatives left out of shares, then rows a pyramid or Likert left out. */
export function seriesNoteTexts(
  notes: { negativeLeftOut: number; leftOut: number; kind: 'pyramid' | 'likert' | null },
  t: Translate,
  lang: string,
): string[] {
  const texts: string[] = [];
  if (notes.negativeLeftOut > 0) {
    texts.push(t(countKey('indicators.series.negativeLeftOut', notes.negativeLeftOut, lang), { count: notes.negativeLeftOut }));
  }
  if (notes.leftOut > 0 && notes.kind) {
    texts.push(t(countKey(`indicators.${notes.kind}.leftOut`, notes.leftOut, lang), { count: notes.leftOut }));
  }
  return texts;
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
