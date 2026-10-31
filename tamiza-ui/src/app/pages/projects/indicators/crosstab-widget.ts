import { LOCALE_ID, Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ChartType, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { ValueFormatter, plotted } from './format';
import { SeriesNotes, seriesName } from './series-legend';
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
}

/**
 * The cross table of a widget with a series (design D4, D5): the groups as rows in the chart's order, the series as columns in theirs,
 * and the totals as the server computed them: a "Total" column, and a "Total" row unless the cells are shares. A bar or column chart
 * stacked to 100% and a Likert widget show each cell's share, and the group's total as its own column. A pair with no value is
 * null, which the table writes as a dash. Written for the "Show as table" view of every widget with a series, and for a cross table
 * widget itself.
 */
export function crossTableModel(table: SeriesTable, context: CrossTableContext): TableModel {
  const { type, config, format, locale, t } = context;
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
  const rows = chartOrder(table, type).map((row) => [row.group ?? t('indicators.chart.noGroup'), ...table.columns.map((_, i) => cellText(row, i)), value(row.total)]);
  return {
    headers: [config.groupBy ?? '', ...table.columns.map((column) => seriesName(column, t)), total],
    align: ['start', ...table.columns.map(() => 'end' as const), 'end'],
    rows: shares ? rows : [...rows, [total, ...table.columns.map((column) => value(column.total)), value(table.grandTotal)]],
    totals: { row: !shares, column: true },
  };
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
      <p class="shrink-0 text-xs text-body" data-testid="chart-caption">{{ t('indicators.series.caption', { what: what(), group: config().groupBy, series: config().seriesBy }) }}</p>
      <app-widget-table class="min-h-0 flex-1" [model]="model(t)" />
      <app-series-notes [negativeLeftOut]="table().negativeLeftOut" [leftOut]="table().leftOut" [kind]="notesKind()" />
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class CrosstabWidget {
  private readonly locale = inject(LOCALE_ID);

  readonly answer = input.required<QueryResult>();
  readonly config = input.required<WidgetConfig>();
  /** The indicator's label and the formatter of its values. */
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();
  /** The kind of widget the table is the view of: it decides the layout of the cells (shares or values) and the order of the groups. */
  readonly chartType = input.required<ChartType>();

  protected readonly what = computed(() => plotted(this.config(), this.label()));
  protected readonly table = computed(() => buildSeriesTable(this.answer(), this.chartType(), this.config().layout));
  protected readonly notesKind = computed(() => (this.chartType() === 'pyramid' ? 'pyramid' : this.chartType() === 'likert' ? 'likert' : null));

  protected model(t: Translate): TableModel {
    return crossTableModel(this.table(), { type: this.chartType(), config: this.config(), format: this.format(), locale: this.locale, t });
  }
}
