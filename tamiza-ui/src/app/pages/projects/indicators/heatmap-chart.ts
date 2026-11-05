import { Component, ElementRef, LOCALE_ID, computed, effect, inject, input, output, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { countKey } from '../../../core/i18n/plural';
import { QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartBases, isSmallBase, noChartBases, seriesFigures, smallBaseDash, smallBaseStroke, summaryWithSmall } from './base-figures';
import { observeSize } from './chart-data';
import { ChartColors } from './chart-colors';
import { registerExportParts } from './export-parts';
import { charWidth, sequentialStep, sequentialSteps, truncate } from './chart-scales';
import { CrosstabWidget } from './crosstab-widget';
import { ColumnNamer, ValueFormatter, captionShown, named, plotted, rawNames } from './format';
import { MarkPoint, markTooltips } from './mark-tooltips';
import { CategoryOrders, groupTexts, seriesTexts, shown } from './category-order';
import { seriesName } from './series-legend';
import { Hit, Label } from './series-plot';
import { SeriesTable, buildSeriesTable, orderedSeriesTable } from './series-table';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/** One cell of the grid: its place, its step on the sequential scale (0 for a cell with no value), and a label when it fits. */
export interface HeatCell {
  x: number;
  y: number;
  width: number;
  height: number;
  g: number;
  s: number;
  value: number | null;
  step: number;
  label: string | null;
  /** A cell with a small base keeps its shade and is hatched with a dashed outline (add-small-sample-warnings, design D10). */
  small: boolean;
}

export interface HeatmapPlot {
  width: number;
  height: number;
  cells: HeatCell[];
  rowLabels: Label[];
  columnLabels: Label[];
  hits: Hit[];
  /** The smallest and the largest value shown, which the scale and its legend run between. */
  range: { min: number; max: number } | null;
}

export interface HeatmapPlotInput {
  table: SeriesTable;
  width: number;
  height: number;
  groupLabel: (group: string | null) => string;
  seriesLabel: (index: number) => string;
  /** A short value for inside a cell. */
  short: (value: number) => string;
  /** The threshold in force: a cell whose n is below it is a small base. Null or left out for none. */
  minN?: number | null;
}

const gap = 2;
const right = 6;
/** The room above the cells for column labels written across them: one line of small text. */
const headerRoom = 22;
const minCellText = 14;
/** A column label longer than this is cut, whole in its tooltip. */
const maxColumnChars = 14;

/**
 * The grid of a heat map (design D5): groups as rows, series as columns, a 2-pixel surface gap between cells, labels only where they fit.
 * A column is a category, not a point on a scale, so every column keeps its label: written across it when the longest fits, otherwise
 * every one of them down its column, in a taller header. Cell values are written in every cell or in none, so the cells that are labelled are
 * not the ones with the shortest numbers.
 */
export function heatmapPlot(input: HeatmapPlotInput): HeatmapPlot {
  const { table, width, height } = input;
  const rows = table.rows;
  const columns = table.columns;
  const values = rows.flatMap((row) => row.cells.filter((cell): cell is number => cell !== null));
  const range = values.length ? { min: Math.min(...values), max: Math.max(...values) } : null;
  const rowTexts = rows.map((row) => input.groupLabel(row.group));
  const columnTexts = columns.map((_, i) => input.seriesLabel(i));
  const longest = Math.max(0, ...rowTexts.map((text) => text.length));
  const labelChars = Math.max(3, Math.min(Math.floor((width * 0.3) / charWidth), longest));
  const left = labelChars * charWidth + 10;
  const cellWidth = Math.max(1, (width - left - right) / Math.max(1, columns.length));
  const longestColumn = Math.max(0, ...columnTexts.map((text) => Math.min(text.length, maxColumnChars)));
  const rotate = longestColumn * charWidth + 6 > cellWidth;
  // Upward labels need a taller header, but never more than the cells can spare; what does not fit it is cut, whole in its tooltip.
  const header = rotate ? Math.min(longestColumn * charWidth + 12, Math.max(headerRoom, height * 0.45)) : headerRoom;
  const columnChars = rotate ? Math.max(3, Math.min(maxColumnChars, Math.floor((header - 12) / charWidth))) : maxColumnChars;
  const cellHeight = Math.max(1, (height - header - 4) / Math.max(1, rows.length));
  const rowStride = Math.max(1, Math.ceil(12 / cellHeight));

  const rowLabels: Label[] = [];
  rowTexts.forEach((text, g) => {
    if (g % rowStride === 0) {
      rowLabels.push({ x: left - 6, y: header + g * cellHeight + cellHeight / 2 + 4, text: truncate(text, labelChars), anchor: 'end', title: truncate(text, labelChars) === text ? undefined : text });
    }
  });
  const columnLabels: Label[] = columnTexts.map((text, s) => {
    const shown = truncate(text, columnChars);
    const x = left + s * cellWidth + cellWidth / 2;
    return rotate
      ? { x, y: header - 4, text: shown, anchor: 'end', title: shown === text ? undefined : text, rotate: true }
      : { x, y: header - 8, text: shown, anchor: 'middle', title: shown === text ? undefined : text };
  });

  // A value is written in a cell when the longest of them fits it, so the cells that are labelled are all of them or none.
  const texts = rows.map((row) => row.cells.map((value) => (value === null ? null : input.short(value))));
  const longestText = Math.max(0, ...texts.flatMap((row) => row.map((text) => text?.length ?? 0)));
  const labelled = cellHeight - gap >= minCellText && cellWidth - gap >= longestText * charWidth * 0.9 + 8;

  const cells: HeatCell[] = [];
  const hits: Hit[] = [];
  rows.forEach((row, g) => {
    row.cells.forEach((value, s) => {
      const x = left + s * cellWidth + gap / 2;
      const y = header + g * cellHeight + gap / 2;
      const w = Math.max(1, cellWidth - gap);
      const h = Math.max(1, cellHeight - gap);
      const text = texts[g][s];
      cells.push({
        x,
        y,
        width: w,
        height: h,
        g,
        s,
        value,
        step: value === null || !range ? 0 : sequentialStep(value, range.min, range.max),
        label: labelled ? text : null,
        small: value !== null && isSmallBase(row.figures?.[s]?.n, input.minN),
      });
      hits.push({ x: x - gap / 2, y: y - gap / 2, width: w + gap, height: h + gap, at: { g, s } });
    });
  });
  return { width, height, cells, rowLabels, columnLabels, hits, range };
}

let hatchIds = 0;

/**
 * A heat map (design D5): the groups of the indicator as rows and its series as columns, each cell shaded by its value on the
 * sequential scale the maps use, from the smallest to the largest value shown, under a legend of that range. The scale is the
 * widget's own or its dashboard palette's (add-dashboard-palettes, design D1): the component sets the `--tmz-seq-n` its marks read on
 * its host, so the cells, their labels and the legend swatches take it. A cell with no value is
 * drawn in the surface color with a hatch and an outline, so it never reads as the lightest step; its tooltip says it has no value.
 * Every cell has a tooltip on hover and on the keyboard (one Tab stop, as the map), and "Show as table" is the cross table.
 */
@Component({
  selector: 'app-heatmap-chart',
  imports: [TranslocoDirective, CrosstabWidget],
  template: `
    <ng-container *transloco="let t">
      <p class="shrink-0 text-xs text-body" [class.sr-only]="!captionShown(config())" data-testid="chart-caption">{{ t('indicators.series.caption', { what: what(), group: groupShown(), series: seriesShown() }) }}</p>
      @if (asTable()) {
        <app-crosstab-widget class="min-h-0 flex-1" [answer]="answer()" [config]="config()" [label]="label()" [format]="format()" [names]="names()" [orders]="orders()" [bases]="bases()" chartType="heatmap" />
      } @else {
        <div #box class="relative min-h-0 flex-1" data-testid="chart-box">
          <svg
            #chartSvg
            role="application"
            class="tmz-chart-svg absolute inset-0 block focus:outline-none focus-visible:ring-4 focus-visible:ring-brand-medium"
            [attr.width]="plot().width"
            [attr.height]="plot().height"
            [attr.viewBox]="'0 0 ' + plot().width + ' ' + plot().height"
            [attr.aria-label]="summary(t)"
            [attr.aria-roledescription]="t('indicators.series.roleDescription')"
            [attr.aria-describedby]="marks.helpId"
            tabindex="0"
            (keydown)="marks.keydown($event)"
            (focusout)="marks.focusout($event)"
            data-testid="chart-svg"
          >
            <defs>
              <!-- An empty cell: the surface with diagonal hairlines, so it is not mistaken for the lightest step. -->
              <pattern [attr.id]="hatchId" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
                <rect width="6" height="6" class="fill-neutral-primary-soft" />
                <line x1="0" y1="0" x2="0" y2="6" class="stroke-default-strong" stroke-width="1" />
              </pattern>
              <!-- A small base: one diagonal from the top left corner of a 6 px tile to its bottom right one, in the heading ink, over the cell's own shade. The empty cell's runs the other way. -->
              <pattern [attr.id]="smallHatchId" width="6" height="6" patternUnits="userSpaceOnUse">
                <path d="M0 0 L6 6" class="stroke-heading" stroke-width="1" fill="none" />
              </pattern>
            </defs>
            @for (label of plot().columnLabels; track $index) {
              <text class="fill-body" font-size="11" [attr.x]="label.x" [attr.y]="label.y" [attr.text-anchor]="label.anchor" [attr.writing-mode]="label.rotate ? 'vertical-rl' : null" data-testid="heat-column-label">
                @if (label.title) {
                  <title>{{ label.title }}</title>
                }
                {{ label.text }}
              </text>
            }
            @for (label of plot().rowLabels; track $index) {
              <text class="fill-body" font-size="11" [attr.x]="label.x" [attr.y]="label.y" [attr.text-anchor]="label.anchor" data-testid="heat-row-label">
                @if (label.title) {
                  <title>{{ label.title }}</title>
                }
                {{ label.text }}
              </text>
            }
            @for (cell of plot().cells; track $index) {
              @if (cell.value === null) {
                <rect class="stroke-default-strong" rx="2" stroke-width="1" [attr.x]="cell.x" [attr.y]="cell.y" [attr.width]="cell.width" [attr.height]="cell.height" [attr.fill]="'url(#' + hatchId + ')'" data-testid="chart-mark" data-empty="true" />
              } @else {
                <rect
                  rx="2"
                  [attr.x]="cell.x"
                  [attr.y]="cell.y"
                  [attr.width]="cell.width"
                  [attr.height]="cell.height"
                  [style.fill]="color(cell.step)"
                  [attr.data-step]="cell.step"
                  [attr.opacity]="marks.isLifted(cell.g, cell.s) ? 0.8 : null"
                  data-testid="chart-mark"
                />
                @if (cell.small) {
                  <rect class="stroke-heading" rx="2" [attr.stroke-width]="smallStroke" [attr.stroke-dasharray]="smallDash" [attr.x]="cell.x" [attr.y]="cell.y" [attr.width]="cell.width" [attr.height]="cell.height" [attr.fill]="'url(#' + smallHatchId + ')'" pointer-events="none" data-testid="heat-small" />
                }
                @if (cell.label) {
                  <text font-size="11" text-anchor="middle" [attr.x]="cell.x + cell.width / 2" [attr.y]="cell.y + cell.height / 2 + 4" [style.fill]="ink(cell.step)" data-testid="heat-value">{{ cell.label }}</text>
                }
              }
              @if (marks.active()?.g === cell.g && marks.active()?.s === cell.s) {
                <rect class="stroke-heading" fill="none" stroke-width="2" rx="2" [attr.x]="cell.x" [attr.y]="cell.y" [attr.width]="cell.width" [attr.height]="cell.height" data-testid="heat-active" />
              }
            }
            @for (hit of plot().hits; track $index) {
              <rect
                fill="transparent"
                [attr.x]="hit.x"
                [attr.y]="hit.y"
                [attr.width]="hit.width"
                [attr.height]="hit.height"
                (pointerenter)="marks.hover(hit.at, $event)"
                (pointermove)="marks.move(hit.at, $event)"
                (pointerleave)="marks.leave(hit.at)"
                (pointerdown)="marks.tap(hit.at, $event)"
                data-testid="chart-hit"
              />
            }
          </svg>
          <p [id]="marks.helpId" class="sr-only">{{ t('indicators.series.keyHelpDown') }}</p>
          <p class="sr-only" role="status" aria-live="polite" data-testid="marks-announcement">{{ marks.announcement() }}</p>
        </div>
        @if (plot().range; as range) {
          <!-- The scale runs from the smallest to the largest value shown, light to dark (dark to light in the dark theme). -->
          <div class="flex shrink-0 flex-wrap items-center gap-2 text-xs text-body" role="group" [attr.aria-label]="t('indicators.heatmap.scale', { min: format()(range.min), max: format()(range.max) })" data-testid="heatmap-legend">
            <span class="tabular-nums" data-testid="heatmap-legend-min">{{ format()(range.min) }}</span>
            <span class="flex" aria-hidden="true">
              @for (step of steps; track step) {
                <span class="h-2.5 w-5 first:rounded-s-sm last:rounded-e-sm" [style.background]="color(step)"></span>
              }
            </span>
            <span class="tabular-nums" data-testid="heatmap-legend-max">{{ format()(range.max) }}</span>
            @if (hasEmpty()) {
              <span class="ms-2 flex items-center gap-1.5" data-testid="heatmap-legend-empty">
                <span aria-hidden="true" class="inline-block h-2.5 w-5 rounded-sm border border-default-strong" [style.background]="hatchSwatch"></span>
                {{ t('indicators.series.noValue') }}
              </span>
            }
          </div>
        }
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2', '[style]': 'colors().rampStyle' },
})
export class HeatmapChart {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  readonly answer = input.required<QueryResult>();
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
  /** The colors of the widget: its sequential scale, from the widget or from its dashboard's palette. */
  readonly colors = input.required<ChartColors>();
  /** The threshold in force and the indicator's base column, for the figures and the small bases of the cells (add-small-sample-warnings). */
  readonly bases = input<ChartBases>(noChartBases);
  /** How many cells the chart hatches for a small base, so the widget can explain the hatching under it (design D10, D11). */
  readonly dimmed = output<number>();
  protected readonly smallStroke = smallBaseStroke;
  protected readonly smallDash = smallBaseDash;

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  private readonly svg = viewChild<ElementRef<SVGElement>>('chartSvg');
  protected readonly size = observeSize(() => this.box());

  protected readonly steps = Array.from({ length: sequentialSteps }, (_, i) => i + 1);
  protected readonly hatchId = `heat-hatch-${++hatchIds}`;
  /** The pattern of a small base, with an id of its own in this chart, so an image of two charts on one page never mixes them up. */
  protected readonly smallHatchId = `${this.hatchId}-small`;
  protected readonly hatchSwatch = 'repeating-linear-gradient(45deg, transparent 0 3px, var(--color-default-strong) 3px 4px)';
  protected readonly what = computed(() => plotted(this.config(), this.label(), this.names()));
  /** The group and series columns under their names shown (their colors stay keyed by the columns themselves). */
  protected readonly groupShown = computed(() => named(this.names(), this.config().groupBy));
  protected readonly seriesShown = computed(() => named(this.names(), this.config().seriesBy));
  private readonly table = computed(() => orderedSeriesTable(this.answer(), 'heatmap', null, this.orders()));
  /** The text each group and each series is shown with when its column hides numbered prefixes (design D5). */
  private readonly groupText = computed(() => groupTexts(this.table(), this.orders()));
  private readonly seriesText = computed(() => seriesTexts(this.table(), this.orders()));
  protected readonly hasEmpty = computed(() => this.table().rows.some((row) => row.cells.some((cell) => cell === null)));

  private readonly tr = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);

  protected readonly plot = computed(() => {
    const table = this.table();
    return heatmapPlot({
      table,
      width: Math.max(160, this.size().width),
      height: Math.max(120, this.size().height),
      groupLabel: (group) => shown(this.groupText(), group) ?? this.tr('indicators.chart.noGroup'),
      seriesLabel: (i) => seriesName(table.columns[i], this.tr, this.seriesText()),
      short: (value) => this.format().tick(value),
      minN: this.bases().minN,
    });
  });
  private readonly smallCount = computed(() => this.plot().cells.filter((cell) => cell.small).length);

  /** A cell's tooltip: its group, its series and its value, or that it has no value. */
  private readonly marksGrid = computed<(MarkPoint | null)[][]>(() => {
    const table = this.table();
    const plot = this.plot();
    const grid: (MarkPoint | null)[][] = table.rows.map((row) => row.cells.map(() => null));
    const figures = seriesFigures(this.config(), this.names(), this.bases(), this.locale, this.tr);
    for (const cell of plot.cells) {
      const column = table.columns[cell.s];
      // After the value: the numerator, the denominator and the n under their names, and the note of a small base (design D9).
      const tip = cell.value === null ? { lines: [], footer: null } : figures.tip(table.rows[cell.g].figures?.[cell.s]);
      grid[cell.g][cell.s] = {
        content: {
          heading: shown(this.groupText(), table.rows[cell.g].group) ?? this.tr('indicators.chart.noGroup'),
          lines: [{ label: seriesName(column, this.tr, this.seriesText()), value: cell.value === null ? this.tr('indicators.series.noValue') : this.format()(cell.value), align: 'end' }, ...tip.lines],
          footer: tip.footer,
        },
        x: cell.x + cell.width / 2,
        y: cell.y + cell.height / 2,
      };
    }
    return grid;
  });

  protected readonly marks = markTooltips({ host: () => this.svg()?.nativeElement, marks: () => this.marksGrid(), groupsAcross: () => false });

  // What an image of the heat map says around its grid (add-widget-export): its caption and its scale, from the smallest value to the largest.
  constructor() {
    effect(() => this.dimmed.emit(this.smallCount()));
    registerExportParts({
      plot: () => (this.asTable() ? null : (this.svg()?.nativeElement ?? null)),
      settle: () => this.marks.reset(),
      parts: () => {
        const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
        const caption = t('indicators.series.caption', { what: this.what(), group: this.groupShown(), series: this.seriesShown() });
        const range = this.plot().range;
        return {
          caption,
          legend:
            this.asTable() || !range
              ? null
              : {
                  kind: 'scale' as const,
                  low: this.format()(range.min),
                  high: this.format()(range.max),
                  steps: this.steps.map((step) => this.color(step)),
                  empty: this.hasEmpty() ? t('indicators.series.noValue') : null,
                },
          notes: [],
          stat: null,
          table: null,
        };
      },
    });
  }

  /** The fill of a step: the variable the host sets to the step of the widget's scale (it is the blue scale where none is set). */
  protected color(step: number): string {
    return `var(--tmz-seq-${step})`;
  }

  /** The label on a step is white or near-black, whichever reads better on it, and swaps with the theme as the steps do. */
  protected ink(step: number): string {
    return `var(--tmz-seq-ink-${step})`;
  }

  protected summary(t: Translate): string {
    const table = this.table();
    const range = this.plot().range;
    if (!range) {
      return t('indicators.chart.summaryEmpty', { what: this.what() });
    }
    const format = this.format();
    const lang = this.transloco.getActiveLang();
    const top = table.rows.flatMap((row) => row.cells.flatMap((cell, i) => (cell === null ? [] : [{ cell, group: row.group, column: table.columns[i] }]))).reduce((best, entry) => (entry.cell > best.cell ? entry : best));
    const words = t('indicators.heatmap.summary', {
      what: this.what(),
      group: this.groupShown(),
      series: this.seriesShown(),
      groups: t(countKey('indicators.series.groupsCount', table.rows.length, lang), { count: table.rows.length }),
      seriesCount: t(countKey('indicators.series.seriesCount', table.columns.length, lang), { count: table.columns.length }),
      min: format(range.min),
      max: format(range.max),
      top: `${shown(this.groupText(), top.group) ?? t('indicators.chart.noGroup')}, ${seriesName(top.column, t, this.seriesText())}`,
    });
    return summaryWithSmall(words, this.smallCount(), t);
  }
}
