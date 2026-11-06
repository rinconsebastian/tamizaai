import { LOCALE_ID, Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ChartType, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartBases, isSmallBase, markSmallCells, noChartBases } from './base-figures';
import { CategoryOrders, arrangeColumns, groupTexts, seriesTexts, shown } from './category-order';
import { ColumnNamer, ValueFormatter, captionShown, named, plotted, rawNames } from './format';
import { registerExportParts } from './export-parts';
import { SeriesNotes, seriesName, seriesNoteTexts } from './series-legend';
import { SeriesTable, buildSeriesTable, chartOrder, hasShares } from './series-table';
import { TableModel, WidgetTable } from './widget-table';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/** What the cross table of a widget with a series needs to write its cells. */
export interface CrossTableContext {
  type: ChartType;
  config: Pick<WidgetConfig, 'groupBy' | 'layout'>;
  format: ValueFormatter;
  locale: string;
  t: Translate;
  /** The name shown for the group column (the header of its column). */
  names: ColumnNamer;
  /** What the widget declares for its values (add-declared-category-order): their order, and whether their numbers are hidden. */
  orders?: CategoryOrders | null;
  /** The threshold in force: a cell with a base below it is marked, and the table says what the mark means (add-small-sample-warnings, design D11). */
  minN?: number | null;
}

/**
 * The cross table of a widget with a series (design D4, D5): the groups as rows in the chart's order, the series as columns in theirs,
 * and the totals as the server computed them: a "Total" column, and a "Total" row unless the cells are shares. A bar or column chart
 * stacked to 100% and a Likert widget show each cell's share, and the group's total as its own column. A pair with no value is
 * null, which the table writes as a dash. Written for the "Show as table" view of every widget with a series, and for a cross table
 * widget itself.
 */
export function crossTableModel(table: SeriesTable, context: CrossTableContext): TableModel {
  const { type, config, format, locale, t, names } = context;
  const orders = context.orders ?? null;
  const groupText = groupTexts(table, orders);
  const seriesText = seriesTexts(table, orders);
  const shares = hasShares(type, config.layout);
  const percent = new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 });
  const value = (cell: number | null) => (cell === null ? null : format(cell));
  const total = t('indicators.series.total');
  const cellText = (row: SeriesTable['rows'][number], i: number) => {
    if (!shares) {
      return value(row.cells[i]);
    }
    const share = row.shares?.[i];
    return share === null || share === undefined ? null : percent.format(share);
  };
  const ordered = chartOrder(table, type, orders);
  const rows = ordered.map((row) => [shown(groupText, row.group) ?? t('indicators.chart.noGroup'), ...table.columns.map((_, i) => cellText(row, i)), value(row.total)]);
  const model: TableModel = {
    headers: [names(config.groupBy ?? ''), ...table.columns.map((column) => seriesName(column, t, seriesText)), total],
    align: ['start', ...table.columns.map(() => 'end' as const), 'end'],
    rows: shares ? rows : [...rows, [total, ...table.columns.map((column) => value(column.total)), value(table.grandTotal)]],
    totals: { row: !shares, column: true },
  };
  // A cell with a small base is followed by "*", and the table says what it means (design D11); a table with none is as it was.
  const minN = context.minN ?? null;
  return markSmallCells(model, ordered.map((row) => row.cells.map((cell, i) => cell !== null && isSmallBase(row.figures?.[i]?.n, minN))), minN, locale, t);
}

/**
 * A cross table widget: the pairs of group and series value of its indicator as a table, with totals (design D5). The view every other
 * widget with a series switches to when "Show as table" is chosen. It scrolls inside its widget with the group column in view, and on
 * paper prints the rows and the columns that fit and says how many it left out (see `WidgetTable`).
 */
@Component({
  selector: 'app-crosstab-widget',
  imports: [TranslocoDirective, SeriesNotes, WidgetTable],
  template: `
    <ng-container *transloco="let t">
      <p class="shrink-0 text-xs text-body" [class.sr-only]="!captionShown(config())" data-testid="chart-caption">{{ t('indicators.series.caption', { what: what(), group: groupShown(), series: seriesShown() }) }}</p>
      <app-widget-table class="min-h-0 flex-1" [model]="model(t)" />
      <app-series-notes [negativeLeftOut]="table().negativeLeftOut" [leftOut]="table().leftOut" [kind]="notesKind()" />
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class CrosstabWidget {
  private readonly locale = inject(LOCALE_ID);
  private readonly transloco = inject(TranslocoService);

  readonly answer = input.required<QueryResult>();
  readonly config = input.required<WidgetConfig>();
  /** Whether the line that names what the widget plots is seen: a hidden one stays for screen readers (add-widget-context, design D3). */
  protected readonly captionShown = captionShown;
  /** The indicator's label and the formatter of its values. */
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();
  /** The name shown for each column (add-indicator-column-metadata); the columns' own names when none is given. */
  readonly names = input<ColumnNamer>(rawNames);
  /** What the widget declares for the order of its values and the text they are shown with (add-declared-category-order). */
  readonly orders = input<CategoryOrders | null>(null);
  /** The kind of widget the table is the view of: it decides the layout of the cells (shares or values) and the order of the groups. */
  readonly chartType = input.required<ChartType>();
  /** The threshold in force, for the cells with a small base (add-small-sample-warnings, design D11). */
  readonly bases = input<ChartBases>(noChartBases);

  protected readonly what = computed(() => plotted(this.config(), this.label(), this.names()));
  protected readonly groupShown = computed(() => named(this.names(), this.config().groupBy));
  protected readonly seriesShown = computed(() => named(this.names(), this.config().seriesBy));
  protected readonly table = computed(() => arrangeColumns(buildSeriesTable(this.answer(), this.chartType(), this.config().layout), this.orders()));
  protected readonly notesKind = computed(() => (this.chartType() === 'pyramid' ? 'pyramid' : this.chartType() === 'likert' ? 'likert' : null));

  // The cross table is the whole of a cross table widget and the table view of every widget with a series (add-widget-export).
  constructor() {
    registerExportParts({
      plot: () => null,
      settle: () => undefined,
      parts: () => {
        const t: Translate = (key, params) => this.transloco.translate(key, params);
        const table = this.table();
        const model = this.model(t);
        return {
          caption: t('indicators.series.caption', { what: this.what(), group: this.groupShown(), series: this.seriesShown() }),
          legend: null,
          notes: [...seriesNoteTexts({ negativeLeftOut: table.negativeLeftOut, leftOut: table.leftOut, kind: this.notesKind() }, t, this.transloco.getActiveLang()), ...(model.note ? [model.note] : [])],
          stat: null,
          table: model,
        };
      },
    });
  }

  protected model(t: Translate): TableModel {
    return crossTableModel(this.table(), { type: this.chartType(), config: this.config(), format: this.format(), locale: this.locale, t, names: this.names(), orders: this.orders(), minN: this.bases().minN });
  }
}
