import { DashboardBases, QueryFigures, QueryNoData, WidgetConfig, WidgetDisplay } from '../../../core/projects/indicators.service';
import { ColumnMetadataMap } from '../../../core/projects/pipeline.service';
import { ChartTooltipContent } from '../../../shared/ui';
import { ColumnNamer, ValueFormatter, figureFormatter } from './format';
import type { CellFigures, SeriesTable } from './series-table';
import type { TableModel } from './widget-table';

// The base figures of a widget (add-small-sample-warnings, design D7, D9, D11): the settings in force, the small-base rule, the lines a
// tooltip adds after its value, the n line, the no-data line and the notes about small bases. Pure, so a chart, "Show as table", an image
// and a print all write the same words. Twin of `BaseFigures` in tamiza-api (emails, workbooks and the MCP tools); both are tested against
// the cases in testing/base-figures.json, so change them together.

type Translate = (key: string, params?: Record<string, unknown>) => string;

export { smallBaseDash, smallBaseOpacity, smallBaseStroke } from './small-base-style';

/** The base settings that decide what a widget shows: the threshold in force (null for off) and whether it shows its n line. */
export interface BaseSettings {
  minN: number | null;
  showN: boolean;
}

/** No small base and no n line: what a widget without base settings draws. */
export const noBaseSettings: BaseSettings = { minN: null, showN: false };

/**
 * The settings in force for a widget (design D7): its own over the dashboard's, or the shown version's. An own threshold of 0 turns it off
 * for the widget; an absent one follows the dashboard.
 */
export function settingsInForce(display: Pick<WidgetDisplay, 'minN' | 'showN'> | null | undefined, dashboard: Partial<DashboardBases> | null | undefined): BaseSettings {
  const own = display?.minN;
  const minN = typeof own === 'number' ? (own >= 1 ? own : null) : (dashboard?.minN ?? null);
  const showN = typeof display?.showN === 'boolean' ? display.showN : dashboard?.showN === true;
  return { minN, showN };
}

/** Whether a value has a small base: its n is below the threshold. A value with no n, or with no threshold in force, never has one. */
export function isSmallBase(n: number | null | undefined, minN: number | null | undefined): boolean {
  return typeof minN === 'number' && minN >= 1 && typeof n === 'number' && n < minN;
}

/**
 * What a chart is given to write the base figures of its marks (design D9, D10): the threshold in force (null for off) and the column the
 * indicator declares as its base (null for none). A chart given neither draws and says what it always did.
 */
export interface ChartBases {
  minN: number | null;
  baseColumn: string | null;
  /** Whether the widget shows its n line: its table then has an n column (design D11). */
  showN?: boolean;
  /** The columns the indicator declares as answers: a widget over one of them draws no overall line (design D16). */
  answerColumns?: readonly string[];
}

export const noChartBases: ChartBases = { minN: null, baseColumn: null };

/** The column the indicator declares as its base, or null (a base is declared on at most one column). */
export function baseColumnOf(columns: ColumnMetadataMap | null | undefined): string | null {
  const declared = Object.entries(columns ?? {}).find(([, facts]) => facts?.base === true);
  return declared ? declared[0] : null;
}

/** The columns the indicator declares as answers (`categories` single or multiple), in the order the map lists them. */
export function answerColumnsOf(columns: ColumnMetadataMap | null | undefined): string[] {
  return Object.entries(columns ?? {})
    .filter(([, facts]) => facts?.categories === 'single' || facts?.categories === 'multiple')
    .map(([column]) => column);
}

/**
 * Whether a widget groups or splits by a column its indicator declares as answers (add-small-sample-warnings, design D16): its bars are the
 * answers of one question, so an `overall` line over them is only the mean of the bars and is not drawn.
 */
export function overOnAnswers(config: Partial<Pick<WidgetConfig, 'groupBy' | 'seriesBy'>>, answerColumns: readonly string[] | null | undefined): boolean {
  const answers = answerColumns ?? [];
  return (!!config.groupBy && answers.includes(config.groupBy)) || (!!config.seriesBy && answers.includes(config.seriesBy));
}

/** The values the indicator declares as meaning no data for each column, by column name. */
export function noDataValuesOf(columns: ColumnMetadataMap | null | undefined): Record<string, string[]> {
  return Object.fromEntries(
    Object.entries(columns ?? {})
      .filter(([, facts]) => (facts?.missing?.length ?? 0) > 0)
      .map(([column, facts]) => [column, [...facts.missing!]]),
  );
}

/** The names a widget gives its figures: the columns of its ratio, and the column its n comes from. Null where the widget has none. */
export interface FigureNames {
  numerator: string | null;
  denominator: string | null;
  /** The declared base column, else the denominator: what the n counts. */
  base: string | null;
}

/** The names under which a widget's figures are written (design D9): the dashboard's names for columns, the widget's own labels first. */
export function figureNames(config: Pick<WidgetConfig, 'ratio'>, names: ColumnNamer, baseColumn: string | null): FigureNames {
  const ratio = config.ratio;
  return {
    numerator: ratio ? names(ratio.numerator) : null,
    denominator: ratio ? names(ratio.denominator) : null,
    base: baseColumn ? names(baseColumn) : ratio ? names(ratio.denominator) : null,
  };
}

/** Everything the texts of the figures need besides the numbers. */
export interface FigureContext {
  names: FigureNames;
  locale: string;
  t: Translate;
}

/** The tooltip lines after a value's own line, and its note: what `figureTooltip` returns. */
export interface FigureTooltip {
  lines: ChartTooltipContent['lines'];
  /** The small-base note, or null. */
  footer: string | null;
}

/**
 * What a tooltip says after the value (design D9): a ratio's numerator and denominator under their names, and the n under the name of
 * the column it counts when it is not the denominator shown; then, for a small base, "Small base: n = 16 (below 30)" as the footer. A
 * mark with no figures adds nothing. `omit` leaves out a figure that the caller shows elsewhere (a map's own tooltip columns).
 */
export function figureTooltip(
  figures: QueryFigures | null | undefined,
  context: FigureContext,
  minN: number | null | undefined,
  options: { omit?: { numerator?: boolean; denominator?: boolean } } = {},
): FigureTooltip {
  const figure = figureFormatter(context.locale);
  const { names, t } = context;
  const lines: ChartTooltipContent['lines'] = [];
  if (figures?.numerator != null && !options.omit?.numerator) {
    lines.push({ label: names.numerator ?? t('indicators.bases.numerator'), value: figure(figures.numerator), align: 'end' });
  }
  if (figures?.denominator != null && !options.omit?.denominator) {
    lines.push({ label: names.denominator ?? t('indicators.bases.denominator'), value: figure(figures.denominator), align: 'end' });
  }
  if (figures?.n != null && !(figures.denominator != null && figures.n === figures.denominator)) {
    lines.push({ label: names.base ? t('indicators.bases.nName', { name: names.base }) : 'n', value: figure(figures.n), align: 'end' });
  }
  return { lines, footer: smallBaseTooltip(figures?.n, minN, context.locale, t) };
}

/** "Small base: n = 16 (below 30)" for a value with a small base, else null. */
export function smallBaseTooltip(n: number | null | undefined, minN: number | null | undefined, locale: string, t: Translate): string | null {
  if (!isSmallBase(n, minN)) {
    return null;
  }
  const figure = figureFormatter(locale);
  return t('indicators.bases.smallTooltip', { n: figure(n!), min: figure(minN!) });
}

/**
 * The value of one series in the tooltip of a group that lists several (a line chart with a series, design D9): the series' own n only,
 * "50% (n = 16)"; a value with no n is written as it is.
 */
export function seriesValueWithN(value: string, n: number | null | undefined, locale: string): string {
  return typeof n === 'number' ? `${value} (n = ${figureFormatter(locale)(n)})` : value;
}

/** The note of a tooltip that lists several series when one of them has a small base. */
export function smallBaseSome(minN: number, locale: string, t: Translate): string {
  return t('indicators.bases.smallSome', { min: figureFormatter(locale)(minN) });
}

/** A histogram's bar in a tooltip: its range as the heading and its count only (design D9), no numerator, denominator or n. */
export function histogramTooltip(range: string, count: number, locale: string, t: Translate): ChartTooltipContent {
  return { heading: range, lines: [{ label: t('indicators.bases.rows'), value: figureFormatter(locale)(count), align: 'end' }], footer: null };
}

/** The base n line under a widget (design D11): "n = 223 Hogares que asistieron"; null when the widget has no n. */
export function nLine(n: number | null | undefined, name: string | null | undefined, locale: string): string | null {
  if (typeof n !== 'number') {
    return null;
  }
  const text = `n = ${figureFormatter(locale)(n)}`;
  return name ? `${text} ${name}` : text;
}

/**
 * A mark drawn dimmed for a small base, as a chart tells the widget (improve-widget-text-hierarchy, design D5): its name as the widget
 * shows it (a group, or "group · series" for a cell of a chart with a series) and its n, so the small-base chip can name it.
 */
export interface SmallMark {
  name: string;
  n: number;
}

/**
 * A stat tile's n line, "n = 476 Hogares" (add-small-sample-warnings, design D11), when the tile chooses it (`display.secondary` holds
 * `n`) or the base n line is in force for it (improve-stat-cards, design D3), and it has an n; null otherwise. A tile with a statistic
 * names the base column or nothing ("n = 37": a number of values); one without names the base column, else the denominator. It was the
 * tile's own, and is the n chip of the widget's foot now (improve-widget-text-hierarchy, design D6).
 */
export function statNLine(
  config: Pick<WidgetConfig, 'ratio' | 'statistic' | 'display'>,
  names: ColumnNamer,
  bases: Pick<ChartBases, 'showN' | 'baseColumn'>,
  n: number | null | undefined,
  locale: string,
): string | null {
  if (!config.display?.secondary?.includes('n') && !bases.showN) {
    return null;
  }
  const name = config.statistic ? (bases.baseColumn ? names(bases.baseColumn) : null) : figureNames(config, names, bases.baseColumn ?? null).base;
  return nLine(n, name, locale);
}

/** The note under a chart while a mark is dimmed: "Marks with a base below 30 are dimmed." */
export function smallBaseNote(minN: number, locale: string, t: Translate): string {
  return t('indicators.bases.smallNote', { min: figureFormatter(locale)(minN) });
}

/** The words a chart's summary adds while marks are dimmed: ", 2 with a small base". Empty while none is. */
export function smallBaseSummary(count: number, t: Translate): string {
  return count > 0 ? t('indicators.bases.summaryCount', { count }) : '';
}

/** A chart's summary with the count of its dimmed marks added before its closing full stop (design D10); as it is while none is dimmed. */
export function summaryWithSmall(words: string, count: number, t: Translate): string {
  const extra = smallBaseSummary(count, t);
  return extra ? `${words.replace(/\.$/, '')}${extra}.` : words;
}

/** The note under a table that marks small bases: "* Base below 30." */
export function smallBaseTableNote(minN: number, locale: string, t: Translate): string {
  return t('indicators.bases.tableNote', { min: figureFormatter(locale)(minN) });
}

/** How many dimmed marks the small-base chip names before it says how many more there are (improve-widget-text-hierarchy, design D5). */
export const maxSmallNamed = 5;

/**
 * What the small-base chip opens: "Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed." It names at
 * most five marks, in the chart's order, each with its n, then says how many more there are.
 */
export function smallBaseMarksText(marks: readonly SmallMark[], minN: number, locale: string, t: Translate): string {
  const number = figureFormatter(locale);
  const named = marks.slice(0, maxSmallNamed).map((mark) => `${mark.name} (n = ${number(mark.n)})`).join(', ');
  const more = marks.length - maxSmallNamed;
  return t('indicators.foot.smallText', { min: number(minN), marks: more > 0 ? `${named} ${t('indicators.foot.more', { count: more })}` : named });
}

/** How many values the no-data line names before it ends with an ellipsis. */
export const maxNoDataNamed = 3;

/**
 * The line under a widget that left out the values its indicator declares as no data (design D6): `{values}: {value}`, with ` ({share})`
 * for a measure and ` · n = {n}` while the base n line shows. At most 3 values are named, then "…". `format` writes the value as the widget
 * writes values, with its own unit when it has one; the share is a percent with at most one decimal. Null when nothing was left out.
 */
export function noDataLine(noData: QueryNoData | null | undefined, format: ValueFormatter, showN: boolean, locale: string): string | null {
  if (!noData || !noData.values?.length) {
    return null;
  }
  const named = noData.values.length > maxNoDataNamed ? `${noData.values.slice(0, maxNoDataNamed).join(', ')}, …` : noData.values.join(', ');
  const value = typeof noData.value === 'number' ? `: ${format(noData.value)}` : '';
  const share = typeof noData.share === 'number' ? ` (${new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(noData.share)})` : '';
  const n = showN && typeof noData.n === 'number' ? ` · n = ${figureFormatter(locale)(noData.n)}` : '';
  return `${named}${value}${share}${n}`;
}

/**
 * What the charts of a widget with a series write after each mark's value and how they mark a small base (design D9, D10): the lines and
 * the note of a cell's tooltip, the n of a series in the tooltip of a group that lists several, and the threshold in force.
 */
export interface SeriesFigures {
  /** The threshold in force; null for off. */
  minN: number | null;
  /** The lines and the small-base note a cell's tooltip adds after its value. */
  tip: (figures: CellFigures | null | undefined) => FigureTooltip;
  /** A value with its n, for the tooltip of a group that lists several series (a line chart). */
  withN: (value: string, n: number | null | undefined) => string;
  /** The note of such a tooltip when one of its series has a small base. */
  some: string | null;
}

/** The figures of a chart with a series, from the widget's names for its columns and the threshold in force. */
export function seriesFigures(config: Pick<WidgetConfig, 'ratio'>, names: ColumnNamer, bases: ChartBases, locale: string, t: Translate): SeriesFigures {
  const context: FigureContext = { names: figureNames(config, names, bases.baseColumn), locale, t };
  return {
    minN: bases.minN,
    tip: (figures) => figureTooltip(figures, context, bases.minN),
    withN: (value, n) => seriesValueWithN(value, n, locale),
    some: bases.minN === null ? null : smallBaseSome(bases.minN, locale, t),
  };
}

/** How many cells of a grid with a value have a small base: what the chart's summary counts. */
export function smallCells(table: SeriesTable, minN: number | null | undefined): number {
  return table.rows.reduce((total, row) => total + row.cells.filter((cell, s) => cell !== null && isSmallBase(row.figures?.[s]?.n, minN)).length, 0);
}

/**
 * Whether the "Show as table" view of a widget with no series gets an n column (design D11): while its n line shows or a threshold is in
 * force, and its values have an n. A widget with neither setting, or whose values have no n, keeps the columns it had.
 */
export function hasNColumn(bases: ChartBases, figures: readonly (QueryFigures | null | undefined)[]): boolean {
  return (bases.showN === true || bases.minN !== null) && figures.some((entry) => typeof entry?.n === 'number');
}

/** The text of the n column for one value: the n as a figure, or null for a value with no n. */
export function nCell(figures: QueryFigures | null | undefined, locale: string): string | null {
  return typeof figures?.n === 'number' ? figureFormatter(locale)(figures.n) : null;
}

/**
 * Marks the small-base values of a table (design D11): the cell of each marked row in `column` is followed by "*", and the table says what
 * the mark means under it. `marked[i]` is whether row `i` has a small base; a table with none is returned as it is.
 */
export function markSmall(model: TableModel, column: number, marked: readonly boolean[], minN: number | null, locale: string, t: Translate): TableModel {
  if (minN === null || !marked.some(Boolean)) {
    return model;
  }
  return {
    ...model,
    rows: model.rows.map((row, i) => (marked[i] ? row.map((cell, c) => (c === column && cell !== null ? `${cell} *` : cell)) : row)),
    marks: model.rows.map((row, i) => row.map((_, c) => !!marked[i] && c === column)),
    note: smallBaseTableNote(minN, locale, t),
  };
}

/**
 * Marks the small-base cells of a grid (a cross table, design D11): `marked[row][column]` says which cells of the table's body (the group
 * column is the first of each row, and a totals row has no entry) are followed by "*". A table with none is returned as it is.
 */
export function markSmallCells(model: TableModel, marked: readonly (readonly boolean[])[], minN: number | null, locale: string, t: Translate): TableModel {
  if (minN === null || !marked.some((row) => row.some(Boolean))) {
    return model;
  }
  const at = (row: number, column: number) => marked[row]?.[column - 1] === true;
  return {
    ...model,
    rows: model.rows.map((row, i) => row.map((cell, c) => (c > 0 && cell !== null && at(i, c) ? `${cell} *` : cell))),
    marks: model.rows.map((row, i) => row.map((_, c) => c > 0 && at(i, c))),
    note: smallBaseTableNote(minN, locale, t),
  };
}
