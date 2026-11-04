import { LOCALE_ID, Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ChartType, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
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
    headers: [names(config.groupBy ?? ''), ...table.columns.map((column) => seriesName(column, t)), total],
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
  /** The kind of widget the table is the view of: it decides the layout of the cells (shares or values) and the order of the groups. */
  readonly chartType = input.required<ChartType>();

  protected readonly what = computed(() => plotted(this.config(), this.label(), this.names()));
  protected readonly groupShown = computed(() => named(this.names(), this.config().groupBy));
  protected readonly seriesShown = computed(() => named(this.names(), this.config().seriesBy));
  protected readonly table = computed(() => buildSeriesTable(this.answer(), this.chartType(), this.config().layout));
  protected readonly notesKind = computed(() => (this.chartType() === 'pyramid' ? 'pyramid' : this.chartType() === 'likert' ? 'likert' : null));

  // The cross table is the whole of a cross table widget and the table view of every widget with a series (add-widget-export).
  constructor() {
    registerExportParts({
      plot: () => null,
      settle: () => undefined,
      parts: () => {
        const t: Translate = (key, params) => this.transloco.translate(key, params);
        const table = this.table();
        return {
          caption: t('indicators.series.caption', { what: this.what(), group: this.groupShown(), series: this.seriesShown() }),
          legend: null,
          notes: seriesNoteTexts({ negativeLeftOut: table.negativeLeftOut, leftOut: table.leftOut, kind: this.notesKind() }, t, this.transloco.getActiveLang()),
          stat: null,
          table: this.model(t),
        };
      },
    });
  }

  protected model(t: Translate): TableModel {
    return crossTableModel(this.table(), { type: this.chartType(), config: this.config(), format: this.format(), locale: this.locale, t, names: this.names() });
  }
}
