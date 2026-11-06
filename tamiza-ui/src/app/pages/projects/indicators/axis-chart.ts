import { LOCALE_ID, Component, ElementRef, computed, effect, inject, input, output, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { QueryResult, QueryRow, WidgetConfig, defaultBuckets } from '../../../core/projects/indicators.service';

import { CategoryOrders, arrangeColumns, groupTexts, seriesTexts, shown } from './category-order';
import { histogram, observeSize, pivot } from './chart-data';
import { bandScale, charWidth, compactNumber, linePath, linearScale, niceAxis, stepPath, truncate, Point } from './chart-scales';
import { Box, ReferenceMark, dashOf, dataLabelMargin, labelGap, minLabelBar, placeReferences, pointGap, referenceMargin, textWidth, boxOf } from './chart-annotations';
import { ChartBases, overOnAnswers, figureNames, figureTooltip, hasNColumn, histogramTooltip, isSmallBase, markSmall, nCell, noChartBases, seriesFigures, smallBaseStroke, smallBaseDash, smallBaseOpacity, summaryWithSmall } from './base-figures';
import { ChartColors } from './chart-colors';
import { CrosstabWidget, crossTableModel } from './crosstab-widget';
import { registerExportParts } from './export-parts';
import { ColumnNamer, ValueFormatter, captionShown, named, ownCaption, plotted, rawNames } from './format';
import { MarkGrid, MarkPoint, markTooltips } from './mark-tooltips';
import { referenceLineValues } from './reference-lines';
import { LegendEntry, ReferenceEntry, ReferenceKey, ReferenceList, SeriesLegend, SeriesNotes, seriesName, seriesNoteTexts } from './series-legend';
import { Dot, Hit, Label, Line, Path, Rect, SeriesPlot, hitSpan, seriesPlot, writeDataLabel } from './series-plot';
import { SeriesTable, buildSeriesTable, isSeriesAnswer } from './series-table';
import { TableModel, WidgetTable } from './widget-table';
import { countKey, isSingular } from '../../../core/i18n/plural';
import { ChartTooltipContent } from '../../../shared/ui';

export type AxisKind = 'bar' | 'column' | 'line' | 'histogram';

type Translate = (key: string, params?: Record<string, unknown>) => string;

const maxThickness = 40;
const minSlot = 14;

/**
 * Bar (horizontal), column (vertical), line and histogram charts as inline SVG (design D5). Without a series they draw one series: the
 * widget's indicator. Values are written with the indicator's format and unit (design D6). The SVG is drawn at the pixel size of its
 * box, so text keeps one size however the widget is resized. Every mark has a tooltip on hover, on a tap and on the keyboard, and the
 * chart is one Tab stop (`role="application"`, add-small-sample-warnings, design D9): with a series or without; a computed summary and a
 * table of the same numbers stand in for it to assistive technology.
 *
 * A bar, column or line widget with a series (design D5) is drawn grouped, stacked or stacked to 100%, or as one line per series,
 * under a legend of its series; each mark has a tooltip on hover and on the keyboard, and the chart is one Tab stop
 * (`role="application"`, as the map). "Show as table" is then its cross table.
 *
 * Its colors are never chosen here (add-dashboard-palettes, design D7): a series takes the color `colors` gives its value, a chart
 * with no series draws in the widget's own color, and a bar or column with a color kept for its group value in that one.
 *
 * The chart annotations of the widget's display (add-chart-annotations): its reference lines, dashed across the plot with their text at
 * their end and listed in a key under it (design D5), steps for a line (D6) and data labels (D7). None of them is part of the query.
 */
@Component({
  selector: 'app-axis-chart',
  imports: [TranslocoDirective, CrosstabWidget, ReferenceKey, ReferenceList, SeriesLegend, SeriesNotes, WidgetTable],
  template: `
    <ng-container *transloco="let t">
      @if (legend().length > 1) {
        <!-- With a series the legend takes the caption's place: it names what the colors mean. -->
        <app-series-legend [entries]="legend()" [shape]="kind() === 'line' ? 'line' : 'square'" />
      } @else {
        <p class="shrink-0 text-xs text-body" [class.sr-only]="!captionShown(config())" data-testid="chart-caption">{{ caption(t) }}</p>
      }
      @if (asTable()) {
        @if (seriesTable()) {
          <app-crosstab-widget class="min-h-0 flex-1" [answer]="answer()!" [config]="config()" [label]="label()" [format]="format()" [names]="names()" [orders]="orders()" [bases]="bases()" [chartType]="kind()" />
        } @else {
          <app-widget-table class="flex-1 overflow-auto" [model]="table(t)" />
        }
        @if (referenceEntries().length) {
          <app-reference-list [entries]="referenceEntries()" />
        }
        @if (overallLeftOut()) {
          <p class="shrink-0 text-xs text-body" data-testid="overall-note">{{ t('indicators.referenceLines.noOverall') }}</p>
        }
      } @else {
        <!-- With a key under it the plot is cut at its own box, never the key (design D5): the clip margin keeps the focus ring and stays inside the gap. -->
        <div #box class="relative min-h-0 flex-1" [style.overflow]="referenceEntries().length > 0 ? 'clip' : null" [style.overflow-clip-margin]="referenceEntries().length > 0 ? '4px' : null" data-testid="chart-box">
          <svg
            #chartSvg
            role="application"
            class="tmz-chart-svg absolute inset-0 block focus:outline-none focus-visible:ring-4 focus-visible:ring-brand-medium"
            [attr.width]="layout().width"
            [attr.height]="layout().height"
            [attr.viewBox]="'0 0 ' + layout().width + ' ' + layout().height"
            [attr.aria-label]="summary(t)"
            tabindex="0"
            [attr.aria-roledescription]="t('indicators.series.roleDescription')"
            [attr.aria-describedby]="marks.helpId"
            (keydown)="marks.keydown($event)"
            (focusout)="marks.focusout($event)"
            data-testid="chart-svg"
          >
            @for (line of layout().grid; track $index) {
              <line [attr.x1]="line.x1" [attr.y1]="line.y1" [attr.x2]="line.x2" [attr.y2]="line.y2" [class]="line.zero ? 'stroke-default-medium' : 'stroke-default'" stroke-width="1" />
            }
            @for (tick of layout().ticks; track $index) {
              <text class="fill-body" font-size="11" [attr.x]="tick.x" [attr.y]="tick.y" [attr.text-anchor]="tick.anchor">{{ tick.text }}</text>
            }
            @for (label of layout().labels; track $index) {
              <text class="fill-body" font-size="11" [attr.x]="label.x" [attr.y]="label.y" [attr.text-anchor]="label.anchor">
                @if (label.title) {
                  <title>{{ label.title }}</title>
                }
                {{ label.text }}
              </text>
            }
            @for (rect of layout().rects; track $index) {
              <rect
                class="hover:opacity-80"
                rx="2"
                [attr.x]="rect.x"
                [attr.y]="rect.y"
                [attr.width]="rect.width"
                [attr.height]="rect.height"
                [style.fill]="rect.color"
                [attr.opacity]="rect.at && marks.isLifted(rect.at.g, rect.at.s) ? 0.8 : null"
                [class.stroke-heading]="rect.at && marks.active()?.g === rect.at.g && marks.active()?.s === rect.at.s"
                [attr.stroke-width]="rect.at && marks.active()?.g === rect.at.g && marks.active()?.s === rect.at.s ? 2 : rect.dim ? smallStroke : null"
                [attr.fill-opacity]="rect.dim ? smallOpacity : null"
                [style.stroke]="rect.dim && !(rect.at && marks.active()?.g === rect.at.g && marks.active()?.s === rect.at.s) ? rect.color : null"
                [attr.stroke-dasharray]="rect.dim ? smallDash : null"
                data-testid="chart-mark"
              >
                @if (rect.title) {
                  <title>{{ rect.title }}</title>
                }
              </rect>
            }
            @for (path of layout().lines; track $index) {
              <path fill="none" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" [attr.d]="path.d" [style.stroke]="path.color" data-testid="chart-line">
                <title>{{ path.name }}</title>
              </path>
            }
            @for (dot of layout().dots; track $index) {
              <circle r="4" stroke-width="2" class="stroke-neutral-primary-soft" [attr.cx]="dot.cx" [attr.cy]="dot.cy" [style.fill]="dot.hollow ? 'none' : dot.color" [style.stroke]="dot.hollow ? dot.color : null" [attr.stroke-dasharray]="dot.hollow ? '2 2' : null" [attr.data-small]="dot.hollow ? 'true' : null" data-testid="chart-dot">
                @if (dot.title) {
                  <title>{{ dot.title }}</title>
                }
              </circle>
            }
            <!-- The reference lines, the values written on the marks and the texts at the lines' ends: over the marks and under the hit areas. -->
            @for (line of layout().references; track $index) {
              <line
                class="stroke-heading"
                stroke-width="1.5"
                [attr.stroke-dasharray]="line.dash"
                [attr.x1]="line.x1"
                [attr.y1]="line.y1"
                [attr.x2]="line.x2"
                [attr.y2]="line.y2"
                data-testid="reference-line"
              >
                @if (!seriesTable()) {
                  <title>{{ line.text }}</title>
                }
              </line>
            }
            @for (label of layout().dataLabels; track $index) {
              <text class="fill-heading stroke-neutral-primary-soft" font-size="11" stroke-width="3" stroke-linejoin="round" paint-order="stroke" aria-hidden="true" [attr.x]="label.x" [attr.y]="label.y" [attr.text-anchor]="label.anchor" data-testid="data-label">{{ label.text }}</text>
            }
            @for (label of layout().referenceLabels; track $index) {
              <text class="fill-heading stroke-neutral-primary-soft" font-size="11" stroke-width="3" stroke-linejoin="round" paint-order="stroke" [attr.x]="label.x" [attr.y]="label.y" [attr.text-anchor]="label.anchor" data-testid="reference-label">{{ label.text }}</text>
            }
            <!-- The areas that answer the pointer are larger than the marks (the gap between them included) and sit on top of them. -->
            @for (hit of layout().hits; track $index) {
              @if (hit.guide && marks.isLifted(hit.at.g, hit.at.s)) {
                <line class="stroke-default-strong" stroke-width="1" [attr.x1]="hit.guide.x1" [attr.y1]="hit.guide.y1" [attr.x2]="hit.guide.x2" [attr.y2]="hit.guide.y2" data-testid="chart-guide" />
              }
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
          <p [id]="marks.helpId" class="sr-only">{{ t(keyHelp()) }}</p>
          <p class="sr-only" role="status" aria-live="polite" data-testid="marks-announcement">{{ marks.announcement() }}</p>
        </div>
        <!-- The key belongs to the chart: it takes its room from the plot as the caption does, above the widget's footnote (design D5). -->
        @if (referenceEntries().length) {
          <app-reference-key [entries]="referenceEntries()" />
        }
        @if (overallLeftOut()) {
          <p class="shrink-0 text-xs text-body" data-testid="overall-note">{{ t('indicators.referenceLines.noOverall') }}</p>
        }
        @if (seriesTable(); as table) {
          <app-series-notes [negativeLeftOut]="table.negativeLeftOut" />
        }
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class AxisChart {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  readonly kind = input.required<AxisKind>();
  readonly rows = input.required<QueryRow[]>();
  /** The whole answer of the widget's query. A widget with a series is drawn from its series, totals and cells; without it, from `rows`. */
  readonly answer = input<QueryResult | null>(null);
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
  /** The threshold in force and the indicator's base column, for the figures and the small bases of the marks (add-small-sample-warnings). */
  readonly bases = input<ChartBases>(noChartBases);
  /** How many marks the chart draws dimmed for a small base, so the widget can explain the dimming under it (design D10, D11). */
  readonly dimmed = output<number>();
  protected readonly smallStroke = smallBaseStroke;
  protected readonly smallDash = smallBaseDash;
  protected readonly smallOpacity = smallBaseOpacity;
  readonly asTable = input(false);
  /** The colors of the widget, from the dashboard's palette and the widget's own color. */
  readonly colors = input.required<ChartColors>();

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  private readonly svg = viewChild<ElementRef<SVGElement>>('chartSvg');
  protected readonly size = observeSize(() => this.box());

  /** The grid of a widget with a series (design D4), null for a chart without one and for a histogram. */
  protected readonly seriesTable = computed<SeriesTable | null>(() => {
    const kind = this.kind();
    return kind !== 'histogram' && isSeriesAnswer(this.answer()) ? arrangeColumns(buildSeriesTable(this.answer()!, kind, this.config().layout), this.orders()) : null;
  });
  /** The text each group and each series is shown with when its column hides numbered prefixes (design D5); null shows the value as it is. */
  private readonly groupText = computed(() => (this.seriesTable() ? groupTexts(this.seriesTable()!, this.orders()) : null));
  private readonly seriesText = computed(() => (this.seriesTable() ? seriesTexts(this.seriesTable()!, this.orders()) : null));
  /**
   * The series in their order with the names and colors the legend, the tooltips and the marks share. The color follows the series'
   * value (a color kept for it, else the next free color of the palette), and "Other" is the neutral.
   */
  protected readonly legend = computed<LegendEntry[]>(() => {
    const table = this.seriesTable();
    if (!table) {
      return [];
    }
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const colors = this.colors().forValues(
      this.config().seriesBy ?? '',
      table.columns.map((column) => ({ value: column.series, kind: column.kind })),
    );
    const texts = this.seriesText();
    return table.columns.map((column, i) => ({ name: seriesName(column, t, texts), color: colors[i] }));
  });
  /**
   * The reference lines the chart draws, with the text each is written with (add-chart-annotations, design D3): `{label}: {value}`, the
   * label being the widget's own or the kind's default, and the value written as the widget writes values, with the indicator's unit
   * (a share, as the axis of that layout writes it, in a 100% layout). A line with no value is not here.
   */
  protected readonly references = computed<ReferenceMark[]>(() => {
    const kind = this.kind();
    const config = this.config();
    if (kind === 'histogram' || !config.display?.referenceLines?.length) {
      return [];
    }
    const answer = this.answer();
    const lines = referenceLineValues(kind, config, { rows: this.rows(), overall: answer?.overall, totals: answer?.totals }, this.bases().answerColumns ?? []);
    const format = this.format();
    const asShare = kind !== 'line' && !!this.seriesTable() && (config.layout ?? 'grouped') === 'percent';
    const share = new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 });
    return lines.map((line) => {
      const label: string = line.label ?? this.transloco.translate<string>(`indicators.referenceLines.kinds.${line.kind}`);
      const figure = asShare ? share.format(line.value) : format(line.value);
      return { value: line.value, place: line.place, label, figure, text: `${label}: ${figure}` };
    });
  });
  /**
   * Whether the widget lists an overall line that it does not draw because it groups or splits by a column its indicator declares as answers
   * (add-small-sample-warnings, design D16): the chart says so under it, in a note. A line the widget could not have anyway (a measure, a
   * stacked or 100% layout) is not this note's business.
   */
  protected readonly overallLeftOut = computed(() => {
    const config = this.config();
    const kind = this.kind();
    const layout = config.seriesBy && (kind === 'bar' || kind === 'column') ? (config.layout ?? 'grouped') : 'grouped';
    return (
      (kind === 'bar' || kind === 'column' || kind === 'line') &&
      !!config.ratio &&
      layout !== 'stacked' &&
      layout !== 'percent' &&
      !!config.display?.referenceLines?.some((line) => line?.kind === 'overall') &&
      overOnAnswers(config, this.bases().answerColumns)
    );
  });
  /** The key under the plot and the list under the table: every line drawn, with its text and the dash it is drawn in. */
  protected readonly referenceEntries = computed<ReferenceEntry[]>(() => this.references().map((mark) => ({ text: mark.text, dash: dashOf(mark.place) })));
  /** Whether the line chart joins its points by steps (design D6). */
  private readonly steps = computed(() => this.kind() === 'line' && this.config().display?.interpolation === 'step');
  /** Whether the chart writes each value on its marks (design D7). */
  private readonly dataLabels = computed(() => this.kind() !== 'histogram' && this.config().display?.dataLabels === true);

  private readonly seriesMarks = computed<SeriesPlot | null>(() => {
    const table = this.seriesTable();
    const kind = this.kind();
    if (!table || kind === 'histogram') {
      return null;
    }
    const entries = this.legend();
    const share = new Intl.NumberFormat(this.locale, { style: 'percent', maximumFractionDigits: 1 });
    return seriesPlot({
      kind,
      layout: this.config().layout ?? 'grouped',
      table,
      width: Math.max(160, this.size().width),
      height: Math.max(120, this.size().height),
      names: entries.map((entry) => entry.name),
      colors: entries.map((entry) => entry.color),
      groupLabel: (group) => shown(this.groupText(), group) ?? this.transloco.translate('indicators.chart.noGroup'),
      orders: this.orders(),
      value: this.format(),
      share: (value) => share.format(value),
      what: plotted(this.config(), this.label(), this.names()),
      references: this.references(),
      steps: this.steps(),
      dataLabels: this.dataLabels(),
      figures: seriesFigures(this.config(), this.names(), this.bases(), this.locale, (key, params) => this.transloco.translate(key, params)),
    });
  });

  /** How many marks have a small base: the cells of a chart with a series, the bars, columns or points of one without (a histogram has none). */
  protected readonly smallCount = computed(() => {
    if (this.kind() === 'histogram') {
      return 0;
    }
    const marks = this.seriesMarks();
    return marks ? marks.small : this.data().figures.filter((figures) => isSmallBase(figures?.n, this.bases().minN)).length;
  });
  /** The tooltips and the keyboard of the marks of a chart with a series. */
  protected readonly marks = markTooltips({
    host: () => this.svg()?.nativeElement,
    marks: () => this.seriesMarks()?.marks ?? this.plain().marks,
    groupsAcross: () => this.kind() !== 'bar',
  });

  // What an image of the chart says around its plot (add-widget-export): the series legend in the caption's place, or the caption.
  constructor() {
    effect(() => this.dimmed.emit(this.smallCount()));
    registerExportParts({
      plot: () => (this.asTable() ? null : (this.svg()?.nativeElement ?? null)),
      settle: () => this.marks.reset(),
      parts: () => {
        const t: Translate = (key, params) => this.transloco.translate(key, params);
        const legend = this.legend();
        const series = this.seriesTable();
        const withLegend = legend.length > 1;
        // The reference lines (add-chart-annotations, design D9): in the legend, after the series, while the chart is drawn, and in the notes,
        // after the table's own, while it is shown as a table. A chart without lines registers what it always did.
        const lines = this.references();
        // The note that says an overall line is not drawn over the answers of one question is part of what an image says under the chart.
        const overall = this.overallLeftOut() ? [t('indicators.referenceLines.noOverall')] : [];
        const entries = [
          ...(withLegend ? legend.map((entry) => ({ label: entry.name, color: entry.color, swatch: this.kind() === 'line' ? ('line' as const) : ('square' as const) })) : []),
          ...(this.asTable() ? [] : lines.map((mark) => ({ label: mark.text, color: 'var(--color-heading, #111827)', swatch: 'dashed' as const, dash: mark.place }))),
        ];
        const shared = {
          caption: withLegend ? null : this.caption(t),
          legend: entries.length ? { kind: 'entries' as const, entries } : null,
          stat: null,
        };
        if (this.asTable()) {
          const table = series
            ? crossTableModel(series, { type: this.kind(), config: this.config(), format: this.format(), locale: this.locale, t, names: this.names(), orders: this.orders(), minN: this.bases().minN })
            : this.table(t);
          return { ...shared, notes: [...lines.map((mark) => mark.text), ...(table.note ? [table.note] : []), ...overall], table };
        }
        const notes = [...(series ? seriesNoteTexts({ negativeLeftOut: series.negativeLeftOut, leftOut: 0, kind: null }, t, this.transloco.getActiveLang()) : []), ...overall];
        return { ...shared, notes, table: null };
      },
    });
  }

  /** Counts of rows (a histogram's axis) are not values of the indicator, so they are written plainly. */
  private readonly compact = compactNumber(this.locale);
  private readonly whole = new Intl.NumberFormat(this.locale, { maximumFractionDigits: 0 });

  /** Categories and values as the chart plots them: group values for bar, column and line; value ranges for a histogram. */
  protected readonly data = computed(() => {
    const kind = this.kind();
    if (kind === 'histogram') {
      const buckets = histogram(this.rows(), this.config().buckets ?? defaultBuckets);
      const format = this.format();
      return {
        ranges: true,
        categories: buckets.map((b) => `${format.plain(b.from)}–${format.plain(b.to)}`),
        values: buckets.map((b) => b.count),
        // A range is no group value, so no color kept by value can match it.
        groups: buckets.map(() => null),
        // A bar of a histogram has no n (design D2): its tooltip gives its range and its count.
        figures: buckets.map(() => null),
      };
    }
    const empty = this.transloco.translate('indicators.chart.noGroup');
    return { ranges: false, ...pivot(this.rows(), empty, kind === 'line' ? 'natural' : 'listed', this.orders()) };
  });

  /** What the chart draws, one series or many: the plot of a widget with a series, else the plain layout. */
  protected readonly layout = computed(() => {
    const marks = this.seriesMarks();
    if (marks) {
      return { width: Math.max(160, this.size().width), height: Math.max(120, this.size().height), ...marks };
    }
    // The plain layout has hit areas and marks of one series too (design D9); the marks stay out of what it draws.
    const { marks: _marks, ...plain } = this.plain();
    return plain;
  });

  private readonly plain = computed(() => {
    const kind = this.kind();
    const { categories, values, groups, ranges } = this.data();
    const format = this.format();
    const tickText = (value: number) => (ranges ? this.compact(value) : format.tick(value));
    const valueText = (value: number) => (ranges ? this.transloco.translate(isSingular(value, this.transloco.getActiveLang()) ? 'indicators.chart.rowCountOne' : 'indicators.chart.rowCount', { count: this.whole.format(value) }) : format(value));
    const w = Math.max(160, this.size().width);
    const h = Math.max(120, this.size().height);
    const count = categories.length;
    // The value axis reaches every reference line (design D5), and the top margin grows for their texts and for the data labels.
    const references = this.references();
    const labelsOn = this.dataLabels();
    const referenceValues = references.map((reference) => reference.value);
    const low = Math.min(0, ...values, ...referenceValues);
    const high = Math.max(0, ...values, ...referenceValues);
    const axis = low === 0 && high === 0 ? niceAxis(0, 1) : niceAxis(low, high);
    const grid: Line[] = [];
    const ticks: Label[] = [];
    const labels: Label[] = [];
    const rects: Rect[] = [];
    const lines: Path[] = [];
    const dots: Dot[] = [];
    const dataLabels: Label[] = [];
    /** The areas that answer the pointer, and the one mark of each category for the tooltips and the keyboard (design D9). */
    const hits: Hit[] = [];
    const marks: MarkGrid = categories.map((_, i): (MarkPoint | null)[] => [{ content: this.tipContent(i, values[i]), x: 0, y: 0 }]);
    /** The boxes of the texts already placed, which no later text may cover: the reference labels first, then the data labels. */
    const taken: Box[] = [];
    // The widget's own color; a bar or a column whose group value has a color kept for it is drawn in that one (spec "One bar
    // highlighted"). A line and a histogram always use the widget's color.
    const colors = this.colors();
    const color = colors.accent;
    const groupBy = this.config().groupBy ?? '';
    const markColor = (i: number) => (kind === 'bar' || kind === 'column' ? (colors.kept(groupBy, groups[i]) ?? color) : color);
    /** Whether the mark of a category has a small base: a histogram's bar has no n, so never. */
    const small = (i: number) => !ranges && isSmallBase(this.data().figures[i]?.n, this.bases().minN);
    const place = (i: number, at: { x: number; y: number }) => {
      (marks[i] as (MarkPoint | null)[])[0]!.x = at.x;
      (marks[i] as (MarkPoint | null)[])[0]!.y = at.y;
    };
    const top = 8 + (references.length ? referenceMargin : 0) + (labelsOn && kind !== 'bar' ? dataLabelMargin : 0);
    const right = 14;

    if (kind === 'bar') {
      const longest = Math.max(0, ...categories.map((c) => c.length));
      const labelChars = Math.max(3, Math.min(Math.floor((w * 0.35) / charWidth), longest));
      const left = labelChars * charWidth + 14;
      const bottom = 24;
      const bands = bandScale(count, [top, h - bottom], 0.25);
      const thickness = Math.min(maxThickness, Math.max(1, bands.bandwidth));
      // A bar thinner than a text line has no label, and the others keep the room past their end for the longest.
      const barLabels = labelsOn && thickness >= minLabelBar;
      const edge = right + (barLabels ? Math.max(0, ...values.map((value) => textWidth(valueText(value)))) + labelGap : 0);
      const x = linearScale([axis.min, axis.max], [left, w - edge]);
      const placed = placeReferences(references, true, x, { left, right: w - edge, top, bottom: h - bottom }, taken);
      const stride = Math.max(1, Math.ceil(minSlot / (bands.step || 1)));
      for (const tick of axis.ticks) {
        grid.push({ x1: x(tick), y1: top, x2: x(tick), y2: h - bottom, zero: tick === 0 });
        ticks.push({ x: x(tick), y: h - 8, text: tickText(tick), anchor: 'middle' });
      }
      categories.forEach((category, i) => {
        if (i % stride === 0) {
          labels.push({ x: left - 6, y: bands.start(i) + bands.bandwidth / 2 + 4, text: truncate(category, labelChars), anchor: 'end', title: truncate(category, labelChars) === category ? undefined : category });
        }
        const zero = x(0);
        const end = x(values[i]);
        const bar = { x: Math.min(zero, end), y: bands.start(i) + (bands.bandwidth - thickness) / 2, width: Math.max(1, Math.abs(end - zero)), height: thickness };
        rects.push({ ...bar, color: markColor(i), title: '', at: { g: i, s: 0 }, ...(small(i) ? { dim: true } : {}) });
        const span = hitSpan(bar.x, bar.width);
        hits.push({ x: span.start, y: bands.start(i) - (bands.step - bands.bandwidth) / 2, width: span.length, height: bands.step, at: { g: i, s: 0 } });
        place(i, { x: bar.x + bar.width / 2, y: bar.y + bar.height / 2 });
        if (barLabels) {
          const at = { x: values[i] >= 0 ? Math.max(zero, end) + labelGap : Math.min(zero, end) - labelGap, y: bands.start(i) + bands.bandwidth / 2 + 4, text: valueText(values[i]), anchor: values[i] >= 0 ? ('start' as const) : ('end' as const) };
          // A negative bar that reaches the axis minimum has no room on its left: its label would cover the category names.
          if (boxOf(at).x0 >= left) {
            writeDataLabel(at, taken, dataLabels);
          }
        }
      });
      return { width: w, height: h, grid, ticks, labels, rects, lines, dots, hits, marks, references: placed.strokes, referenceLabels: placed.labels, dataLabels };
    }

    const tickTexts = axis.ticks.map((tick) => tickText(tick));
    const left = Math.max(...tickTexts.map((t) => t.length)) * charWidth + 14;
    const bottom = 26;
    const y = linearScale([axis.min, axis.max], [h - bottom, top]);
    const bands = bandScale(count, [left, w - right], kind === 'line' ? 0 : kind === 'histogram' ? 0.06 : 0.3);
    const placed = placeReferences(references, false, y, { left, right: w - right, top, bottom: h - bottom }, taken);
    axis.ticks.forEach((tick, i) => {
      grid.push({ x1: left, y1: y(tick), x2: w - right, y2: y(tick), zero: tick === 0 });
      ticks.push({ x: left - 6, y: y(tick) + 4, text: tickTexts[i], anchor: 'end' });
    });
    const longest = Math.max(0, ...categories.map((c) => c.length));
    const stride = Math.max(1, Math.ceil((Math.min(longest, 14) * charWidth + 6) / (bands.step || 1)));
    const labelChars = Math.max(2, Math.floor((stride * bands.step) / charWidth) - 1);
    categories.forEach((category, i) => {
      if (i % stride === 0) {
        labels.push({ x: bands.start(i) + bands.bandwidth / 2, y: h - 8, text: truncate(category, labelChars), anchor: 'middle', title: truncate(category, labelChars) === category ? undefined : category });
      }
    });

    if (kind === 'line') {
      const points: Point[] = values.map((value, i) => ({ x: bands.start(i) + bands.bandwidth / 2, y: y(value) }));
      points.forEach((point, i) => {
        // A point with a small base is hollow with a dashed ring, and is drawn whatever the number of points (design D10).
        if (count <= 40 || small(i)) {
          dots.push({ cx: point.x, cy: point.y, color, title: '', ...(small(i) ? { hollow: true } : {}) });
        }
        // A line's hit areas are bands around its points, with a hairline at the one the pointer or the keyboard is on (design D9).
        const center = bands.start(i) + bands.bandwidth / 2;
        hits.push({ x: bands.start(i), y: top, width: bands.bandwidth, height: h - bottom - top, at: { g: i, s: 0 }, guide: { x1: center, y1: top, x2: center, y2: h - bottom, zero: false } });
        place(i, { x: center, y: point.y });
        if (labelsOn) {
          // Above each point, left to right: a label off the plot or covering one already placed is left out.
          const at = { x: point.x, y: point.y - pointGap, text: valueText(values[i]), anchor: 'middle' as const };
          const box = boxOf(at);
          if (box.x0 >= left && box.x1 <= w - labelGap) {
            writeDataLabel(at, taken, dataLabels);
          }
        }
      });
      const path = this.steps() ? stepPath : linePath;
      lines.push({ d: points.length > 1 ? path(points) : '', color, name: ownCaption(this.config()) ?? this.label() });
      return { width: w, height: h, grid, ticks, labels, rects, lines, dots, hits, marks, references: placed.strokes, referenceLabels: placed.labels, dataLabels };
    }

    const thickness = Math.min(maxThickness, Math.max(1, bands.bandwidth));
    categories.forEach((category, i) => {
      const zero = y(0);
      const end = y(values[i]);
      const column = { x: bands.start(i) + (bands.bandwidth - thickness) / 2, y: Math.min(zero, end), width: thickness, height: Math.max(1, Math.abs(end - zero)) };
      rects.push({ ...column, color: markColor(i), title: '', at: { g: i, s: 0 }, ...(small(i) ? { dim: true } : {}) });
      const span = hitSpan(column.y, column.height);
      hits.push({ x: bands.start(i) - (bands.step - bands.bandwidth) / 2, y: span.start, width: bands.step, height: span.length, at: { g: i, s: 0 } });
      place(i, { x: column.x + column.width / 2, y: column.y + column.height / 2 });
      // A column label sits above its end, or below a negative column, and is left out when it is wider than the column slot.
      if (labelsOn && textWidth(valueText(values[i])) <= bands.step) {
        const at = { x: bands.start(i) + bands.bandwidth / 2, y: values[i] >= 0 ? Math.min(zero, end) - labelGap : Math.max(zero, end) + labelGap + 10, text: valueText(values[i]), anchor: 'middle' as const };
        // A negative column that reaches the axis minimum has no room below it: its label would cover the category names.
        if (boxOf(at).y1 <= h - bottom + 2) {
          writeDataLabel(at, taken, dataLabels);
        }
      }
    });
    return { width: w, height: h, grid, ticks, labels, rects, lines, dots, hits, marks, references: placed.strokes, referenceLabels: placed.labels, dataLabels };
  });

  /**
   * What a mark of a chart with no series says (design D9): its group as the heading, then the plotted value under the widget's own caption
   * text or the indicator's label, then the numerator, the denominator and the n under their names, and for a small base its note. A
   * histogram's bar says its range and its count only.
   */
  private tipContent(i: number, value: number): ChartTooltipContent {
    const { categories, figures, ranges } = this.data();
    const t: Translate = (key, params) => this.transloco.translate(key, params);
    if (ranges) {
      return histogramTooltip(categories[i], value, this.locale, t);
    }
    const tip = figureTooltip(figures[i], { names: figureNames(this.config(), this.names(), this.bases().baseColumn), locale: this.locale, t }, this.bases().minN);
    return { heading: categories[i], lines: [{ label: ownCaption(this.config()) ?? this.label(), value: this.format()(value), align: 'end' }, ...tip.lines], footer: tip.footer };
  }

  protected caption(t: Translate): string {
    const config = this.config();
    const what = plotted(config, this.label(), this.names());
    return this.kind() === 'histogram'
      ? t('indicators.chart.captionHistogram', { what, buckets: config.buckets ?? defaultBuckets })
      : t('indicators.chart.caption', { what, group: named(this.names(), config.groupBy) });
  }

  protected keyHelp(): string {
    // A chart with no series has no series to move between: only the groups (design D9).
    if (!this.seriesTable()) {
      return this.kind() === 'bar' ? 'indicators.series.keyHelpGroupsDown' : 'indicators.series.keyHelpGroups';
    }
    return this.kind() === 'line' ? 'indicators.series.keyHelpGroups' : this.kind() === 'bar' ? 'indicators.series.keyHelpDown' : 'indicators.series.keyHelpAcross';
  }

  /** The words for a chart with a series: its kind and layout, what it plots by what, how many groups and series, its range and highest cell. */
  private seriesSummary(t: Translate, table: SeriesTable): string {
    const config = this.config();
    const what = plotted(config, this.label(), this.names());
    const cells = table.rows.flatMap((row) => row.cells.flatMap((cell, i) => (cell === null ? [] : [{ cell, group: row.group, column: table.columns[i] }])));
    if (!cells.length) {
      return t('indicators.chart.summaryEmpty', { what });
    }
    const format = this.format();
    const top = cells.reduce((best, entry) => (entry.cell > best.cell ? entry : best));
    const lang = this.transloco.getActiveLang();
    const kind = this.kind();
    return t('indicators.series.summary', {
      kind: t(`indicators.series.kinds.${kind === 'line' ? 'line' : `${kind}${(config.layout ?? 'grouped')[0].toUpperCase()}${(config.layout ?? 'grouped').slice(1)}`}`),
      what,
      group: named(this.names(), config.groupBy),
      series: named(this.names(), config.seriesBy),
      groups: t(countKey('indicators.series.groupsCount', table.rows.length, lang), { count: table.rows.length }),
      seriesCount: t(countKey('indicators.series.seriesCount', table.columns.length, lang), { count: table.columns.length }),
      min: format(Math.min(...cells.map((entry) => entry.cell))),
      max: format(Math.max(...cells.map((entry) => entry.cell))),
      top: `${shown(this.groupText(), top.group) ?? t('indicators.chart.noGroup')}, ${seriesName(top.column, t, this.seriesText())}`,
    });
  }

  /** The chart's words for assistive technology, then the reference lines it draws with their values (design D5). */
  protected summary(t: Translate): string {
    const words = summaryWithSmall(this.chartSummary(t), this.smallCount(), t);
    const references = this.references();
    return references.length ? `${words} ${t('indicators.referenceLines.summary', { lines: references.map((reference) => `${reference.label} ${reference.figure}`).join(', ') })}` : words;
  }

  private chartSummary(t: Translate): string {
    const table = this.seriesTable();
    if (table) {
      return this.seriesSummary(t, table);
    }
    const { categories, values } = this.data();
    const config = this.config();
    const what = plotted(config, this.label(), this.names());
    if (!values.length) {
      return t('indicators.chart.summaryEmpty', { what });
    }
    const format = this.format();
    const top = values.indexOf(Math.max(...values));
    return t(countKey(`indicators.chart.summary.${this.kind()}`, categories.length, this.transloco.getActiveLang()), {
      what,
      group: named(this.names(), config.groupBy),
      count: categories.length,
      min: this.data().ranges ? this.whole.format(Math.min(...values)) : format(Math.min(...values)),
      max: this.data().ranges ? this.whole.format(Math.max(...values)) : format(Math.max(...values)),
      top: categories[top],
    });
  }

  protected table(t: Translate): TableModel {
    const { categories, values, ranges, figures } = this.data();
    const what = plotted(this.config(), this.label(), this.names());
    const format = this.format();
    // While the n line shows or a threshold is in force, the values that have an n get a column for it, after the value (design D11).
    const withN = !ranges && hasNColumn(this.bases(), figures);
    const model: TableModel = {
      headers: [ranges ? t('indicators.chart.range') : this.names()(this.config().groupBy ?? ''), ranges ? t('indicators.chart.rowsOf', { what }) : what, ...(withN ? [t('indicators.bases.nColumn')] : [])],
      rows: categories.map((category, i) => [category, ranges ? this.whole.format(values[i]) : format(values[i]), ...(withN ? [nCell(figures[i], this.locale)] : [])]),
    };
    return ranges ? model : markSmall(model, 1, categories.map((_, i) => isSmallBase(figures[i]?.n, this.bases().minN)), this.bases().minN, this.locale, t);
  }
}
