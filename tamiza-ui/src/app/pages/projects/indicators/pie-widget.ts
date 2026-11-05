import { LOCALE_ID, Component, ElementRef, computed, inject, input, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { QueryRow, QuerySeries, WidgetConfig } from '../../../core/projects/indicators.service';
import { CategoryOrders, arrange, entryOf } from './category-order';
import { ChartColors, SeriesKey } from './chart-colors';
import { registerExportParts } from './export-parts';
import { observeSize, pivot } from './chart-data';
import { Box, labelGap, textWidth } from './chart-annotations';
import { seriesCount, slicePath } from './chart-scales';
import { ColumnNamer, ValueFormatter, captionShown, named, plotted, rawNames } from './format';
import { Label, writeDataLabel } from './series-plot';
import { TableModel, WidgetTable } from './widget-table';
import { PctPipe, percentText } from '../../../shared/ui';
import { countKey } from '../../../core/i18n/plural';

type Translate = (key: string, params?: Record<string, unknown>) => string;

interface Slice {
  name: string;
  value: number;
  share: number;
  color: string;
  /** Null for a slice that is the whole circle. */
  d: string | null;
}

/** What a slice stands for: a group value, the missing one (`none`), or the group values that did not get a color of their own (`other`). */
interface Part {
  id: string;
  /** The group value, null for the missing one: what a declared order is matched by. */
  group: string | null;
  name: string;
  value: number;
}

/** The identity of a group value, of the missing one and of "Other", for matching a row to the domain. */
const idOf = (group: string | null): string => (group === null ? 'none' : `value:${group}`);
const idOfKey = (key: SeriesKey): string => (key.kind === 'other' ? 'other' : idOf(key.kind === 'none' ? null : key.value));
const otherKey: SeriesKey = { value: null, kind: 'other' };

/**
 * A pie chart (design D5): one slice per group value of the indicator, sized by its share of the total. Slices run from the
 * largest. Values at or below zero have no share and are only counted, and listed in the table.
 *
 * Colors and "Other" come from the widget's `domain` (add-dashboard-palettes, design D8), the group values of its own rows, not the
 * dashboard filters': a slice is colored by its value, as a series is, and every group value outside the domain is summed into
 * "Other", in the neutral color. So a dashboard filter that reorders the slices repaints none of them. Without a domain (an API that
 * predates it) the slices are colored in the order of their size, the smallest past the eighth color folding into "Other".
 */
@Component({
  selector: 'app-pie-widget',
  imports: [PctPipe, TranslocoDirective, WidgetTable],
  template: `
    <ng-container *transloco="let t">
      <p class="shrink-0 text-xs text-body" [class.sr-only]="!captionShown(config())" data-testid="chart-caption">{{ t('indicators.chart.caption', { what: what(), group: groupShown() }) }}</p>
      @if (asTable()) {
        <app-widget-table class="flex-1 overflow-auto" [model]="table(t)" />
      } @else {
        <!-- A wide tile puts the legend beside the pie, so the pie keeps the tile's height. -->
        <div class="@container flex min-h-0 flex-1 flex-col gap-2 @md:flex-row">
        <div #box class="relative min-h-0 flex-1" data-testid="chart-box">
          <svg #pieSvg role="img" class="tmz-chart-svg absolute inset-0 block" [attr.width]="geometry().width" [attr.height]="geometry().height" [attr.viewBox]="'0 0 ' + geometry().width + ' ' + geometry().height" [attr.aria-label]="summary(t)" data-testid="chart-svg">
            @for (slice of slices(); track slice.name) {
              @if (slice.d) {
                <path class="stroke-neutral-primary-soft hover:opacity-80" stroke-width="2" [attr.d]="slice.d" [style.fill]="slice.color" data-testid="pie-slice">
                  <title>{{ sliceTitle(slice) }}</title>
                </path>
              } @else {
                <circle class="stroke-neutral-primary-soft hover:opacity-80" stroke-width="2" [attr.cx]="geometry().cx" [attr.cy]="geometry().cy" [attr.r]="geometry().radius" [style.fill]="slice.color" data-testid="pie-slice">
                  <title>{{ sliceTitle(slice) }}</title>
                </circle>
              }
            }
            @for (label of dataLabels(); track $index) {
              <text class="fill-heading stroke-neutral-primary-soft" font-size="11" stroke-width="3" stroke-linejoin="round" paint-order="stroke" aria-hidden="true" [attr.x]="label.x" [attr.y]="label.y" [attr.text-anchor]="label.anchor" data-testid="data-label">{{ label.text }}</text>
            }
          </svg>
        </div>
        <!-- On paper the whole legend shows, never a part of it to scroll; the pie is drawn in the room left (dashboard printing). -->
        <ul class="max-h-28 shrink-0 overflow-y-auto text-xs text-body @md:max-h-full @md:w-48 paper:max-h-none paper:overflow-visible" data-testid="chart-legend">
          @for (slice of slices(); track slice.name) {
            <li class="flex items-center gap-1.5 py-0.5" data-testid="legend-item">
              <span aria-hidden="true" class="inline-block size-2.5 shrink-0 rounded-sm" [style.background]="slice.color"></span>
              <span class="min-w-0 flex-1 break-all">{{ slice.name }}</span>
              <span class="tabular-nums">{{ format()(slice.value) }} · {{ slice.share | pct: '1.0-1' }}</span>
            </li>
          }
        </ul>
        </div>
        @if (notDrawn() > 0) {
          <p class="shrink-0 text-xs text-body" data-testid="pie-skipped">{{ t(key('indicators.pie.skipped', notDrawn()), { count: notDrawn() }) }}</p>
        }
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class PieWidget {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  readonly rows = input.required<QueryRow[]>();
  readonly config = input.required<WidgetConfig>();
  /** Whether the line that names what the widget plots is seen: a hidden one stays for screen readers (add-widget-context, design D3). */
  protected readonly captionShown = captionShown;
  /** The indicator's label and the formatter of its values. */
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();
  /** The name shown for each column (add-indicator-column-metadata); the columns' own names when none is given. */
  readonly names = input<ColumnNamer>(rawNames);
  /** What the widget declares for the order of its values and the text they are shown with (add-declared-category-order, design D8). */
  readonly orders = input<CategoryOrders | null>(null);
  readonly asTable = input(false);
  /** The colors of the widget, from the dashboard's palette and its colors kept by value. */
  readonly colors = input.required<ChartColors>();
  /** The group values of the widget's own rows (design D8); null or empty for an API that sends none. */
  readonly domain = input<readonly QuerySeries[] | null | undefined>(null);

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  private readonly size = observeSize(() => this.box());
  private readonly svg = viewChild<ElementRef<SVGSVGElement>>('pieSvg');

  protected readonly what = computed(() => plotted(this.config(), this.label(), this.names()));
  /** The group column under its name shown; its colors stay keyed by the column itself. */
  protected readonly groupShown = computed(() => named(this.names(), this.config().groupBy));

  private readonly all = computed(() => {
    const data = pivot(this.rows(), this.transloco.translate('indicators.chart.noGroup'), 'listed', this.orders());
    return data.categories.map((name, i) => ({ name, group: data.groups[i], value: data.values[i] }));
  });

  protected readonly notDrawn = computed(() => this.all().filter((entry) => entry.value <= 0).length);

  /**
   * The group values that decide the colors, in order, and which values fold into "Other": the response's domain, or, from an API that
   * sends none, the filtered rows ranked by size with the smallest past the eighth color folded.
   */
  private readonly colorDomain = computed<readonly SeriesKey[]>(() => {
    const declared = this.orders()?.groups ?? null;
    const given = this.domain();
    if (given?.length) {
      return arrange(given.map(({ value, kind }): SeriesKey => ({ value, kind })), (key) => key, declared);
    }
    const ranked = this.all()
      .filter((entry) => entry.value > 0)
      .sort((a, b) => b.value - a.value);
    const folds = ranked.length > seriesCount;
    const kept = (folds ? ranked.slice(0, seriesCount - 1) : ranked).map((entry): SeriesKey => ({ value: entry.group, kind: entry.group === null ? 'none' : 'value' }));
    return arrange(folds ? [...kept, otherKey] : kept, (key) => key, declared);
  });

  /**
   * The parts of the pie in the order they are drawn: the groups of the domain that have a value above zero, from the largest, then
   * "Other" with the sum of every other group. The others (at or below zero) are not parts: they are counted and listed in the table.
   */
  private readonly parts = computed<Part[]>(() => {
    const inDomain = new Set(this.colorDomain().map(idOfKey));
    const named: Part[] = [];
    let rest = 0;
    for (const entry of this.all().filter((entry) => entry.value > 0)) {
      if (inDomain.has(idOf(entry.group))) {
        named.push({ id: idOf(entry.group), group: entry.group, name: entry.name, value: entry.value });
      } else {
        rest += entry.value;
      }
    }
    // Largest first, or in the order the widget declares for its groups (add-declared-category-order, design D4); "Other" is last either way.
    const declared = this.orders()?.groups ?? null;
    const drawn = declared ? arrange(named, (part) => entryOf(part.group), declared) : named.sort((a, b) => b.value - a.value);
    return rest > 0 ? [...drawn, { id: 'other', group: null, name: this.transloco.translate('indicators.pie.other'), value: rest }] : drawn;
  });

  /**
   * The color of each part, by the value it stands for: the order of the whole domain gives the colors, so a slice keeps its color
   * whichever of the others the filtered rows leave out.
   */
  private readonly partColors = computed<Map<string, string>>(() => {
    const keys = this.colorDomain().filter((key) => key.kind !== 'other');
    const colors = this.colors().forValues(this.config().groupBy ?? '', [...keys, otherKey]);
    const byId = new Map(keys.map((key, i) => [idOfKey(key), colors[i]]));
    return byId.set('other', colors[keys.length]);
  });

  protected readonly slices = computed<Slice[]>(() => {
    const parts = this.parts();
    const total = parts.reduce((sum, part) => sum + part.value, 0);
    if (!total) {
      return [];
    }
    const colors = this.partColors();
    const { cx, cy, radius } = this.geometry();
    let angle = 0;
    return parts.map((part) => {
      const share = part.value / total;
      const next = angle + share * 2 * Math.PI;
      const d = parts.length === 1 ? null : slicePath(cx, cy, radius, angle, next);
      angle = next;
      return { name: part.name, value: part.value, share, color: colors.get(part.id) ?? colors.get('other')!, d };
    });
  });

  /**
   * The value of each slice written on it when the widget asks for data labels (add-chart-annotations, design D7): at the slice's middle
   * angle, at 0.62 of the radius. A slice whose chord at that radius is shorter than its text plus 4 px, or whose text would cover one
   * already placed, or whose text would leave the circle, has none; a pie of a single slice puts its label in the center, unless the text is
   * wider than the pie.
   */
  protected readonly dataLabels = computed<Label[]>(() => {
    if (this.config().display?.dataLabels !== true) {
      return [];
    }
    const { cx, cy, radius } = this.geometry();
    const taken: Box[] = [];
    const written: Label[] = [];
    const around = radius * 0.62;
    // Half the height of a text of 11 px (its box runs 10 px above the baseline to 2 below; the label's baseline is 4 px under its center).
    const halfLine = 6;
    let angle = 0;
    for (const slice of this.slices()) {
      const sweep = slice.share * 2 * Math.PI;
      const middle = angle + sweep / 2;
      angle += sweep;
      const text = this.format()(slice.value);
      const width = textWidth(text);
      if (!slice.d) {
        // The text is horizontal: its corners, half its width and half a text line from the center, must stay inside the circle.
        if (width * width / 4 + halfLine * halfLine <= radius * radius) {
          writeDataLabel({ x: cx, y: cy + 4, text, anchor: 'middle' }, taken, written);
        }
        continue;
      }
      // The chord across a slice of half the circle or more is as wide as the pie.
      const chord = sweep >= Math.PI ? 2 * around : 2 * around * Math.sin(sweep / 2);
      // The text is horizontal, so for a slice pointing sideways it reaches further than the chord says: its farthest corner, half its width
      // and half a text line beyond the label's center, must stay inside the circle.
      const reach = Math.hypot(Math.abs(around * Math.sin(middle)) + width / 2, Math.abs(around * Math.cos(middle)) + halfLine);
      if (chord >= width + labelGap && reach <= radius) {
        writeDataLabel({ x: cx + around * Math.sin(middle), y: cy - around * Math.cos(middle) + 4, text, anchor: 'middle' }, taken, written);
      }
    }
    return written;
  });

  protected readonly geometry = computed(() => {
    const width = Math.max(120, this.size().width);
    const height = Math.max(100, this.size().height);
    return { width, height, cx: width / 2, cy: height / 2, radius: Math.max(20, Math.min(width, height) / 2 - 6) };
  });

  // What an image of the pie says (add-widget-export): the caption, the whole legend with each slice's value and share, what was not drawn.
  constructor() {
    registerExportParts({
      plot: () => (this.asTable() ? null : (this.svg()?.nativeElement ?? null)),
      settle: () => undefined,
      parts: () => {
        const t: Translate = (key, params) => this.transloco.translate(key, params);
        const caption = t('indicators.chart.caption', { what: this.what(), group: this.groupShown() });
        if (this.asTable()) {
          return { caption, legend: null, notes: [], stat: null, table: this.table(t) };
        }
        const skipped = this.notDrawn();
        return {
          caption,
          legend: {
            kind: 'entries' as const,
            entries: this.slices().map((slice) => ({
              label: slice.name,
              color: slice.color,
              detail: `${this.format()(slice.value)} · ${percentText(slice.share, this.locale, '1.0-1')}`,
            })),
          },
          notes: skipped > 0 ? [t(this.key('indicators.pie.skipped', skipped), { count: skipped })] : [],
          stat: null,
          table: null,
        };
      },
    });
  }

  protected sliceTitle(slice: Slice): string {
    return `${slice.name}: ${this.format()(slice.value)} (${new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 }).format(slice.share)})`;
  }

  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
  }

  protected summary(t: Translate): string {
    const slices = this.slices();
    if (!slices.length) {
      return t('indicators.chart.summaryEmpty', { what: this.what() });
    }
    // The largest slice, whatever the order the slices are drawn in (a declared order is not ranked by size).
    const largest = slices.reduce((top, slice) => (slice.value > top.value ? slice : top));
    return t(countKey('indicators.chart.summary.pie', slices.length, this.transloco.getActiveLang()), {
      what: this.what(),
      group: this.groupShown(),
      count: slices.length,
      top: largest.name,
      share: new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 }).format(largest.share),
    });
  }

  protected table(t: Translate): TableModel {
    const total = this.all().reduce((sum, entry) => sum + Math.max(0, entry.value), 0);
    return {
      headers: [this.names()(this.config().groupBy ?? ''), this.what(), t('indicators.pie.share')],
      rows: this.all().map((entry) => [
        entry.name,
        this.format()(entry.value),
        entry.value > 0 && total ? new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 }).format(entry.value / total) : null,
      ]),
    };
  }
}
