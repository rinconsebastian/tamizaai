import { DataColumnType, DataValue } from '../../../core/projects/data.service';
import { QueryRow, WidgetRatio, WidgetTooltipItem } from '../../../core/projects/indicators.service';
import { ChartTooltipContent, dateText, timestampText } from '../../../shared/ui';
import { ChartBases, figureNames, figureTooltip } from './base-figures';
import { shown } from './category-order';
import { ColumnNamer, ValueFormatter, rawNames, valueFormatter } from './format';

// What a map's tooltip says (design D6, D9): the group as its heading, the plotted value with the indicator's format and unit, the
// widget's tooltip columns each under its label, and how many rows the group combines. Pure, so "Show as table" writes the same
// strings. A tooltip column never uses the indicator's format: it has its own, and only a number takes one.

type Translate = (key: string, params?: Record<string, unknown>) => string;

export interface TooltipContext {
  /** The widget's tooltip columns, in order. */
  items: readonly WidgetTooltipItem[];
  /** The type of each of them, from the answer; absent from an answer that has no tooltip columns. */
  types: readonly DataColumnType[] | undefined;
  /** The indicator's label and the formatter of its values, for the plotted value. */
  label: string;
  format: ValueFormatter;
  locale: string;
  t: Translate;
  /** The name shown for a column that has no label of its own (add-indicator-column-metadata); the column's name when left out. */
  names?: ColumnNamer;
  /** The text each group value is shown with when its column hides numbered prefixes (add-declared-category-order, design D5). */
  groupText?: ReadonlyMap<string, string> | null;
  /**
   * The widget's ratio and the threshold in force with the indicator's base column (add-small-sample-warnings, design D9): after the value
   * the tooltip gives the numerator, the denominator and the n that are not among its own columns, and the note of a small base.
   */
  ratio?: WidgetRatio | null;
  bases?: ChartBases;
}

const numeric = (type: DataColumnType | undefined) => type === 'integer' || type === 'decimal';

/** What a tooltip column is called: its own label, else the name shown for the column (the indicator's label, else the column's name). */
export function tooltipLabel(item: WidgetTooltipItem, names: ColumnNamer = rawNames): string {
  return item.label?.trim() || names(item.column);
}

/**
 * One value of a tooltip column, written for its type. A format chosen for the column applies only while the column is a number;
 * one saved on a column that has since become text, a date or true/false is ignored. No value reads as a dash.
 */
export function tooltipValue(value: DataValue | undefined, type: DataColumnType | undefined, item: WidgetTooltipItem, locale: string, t: Translate): string {
  if (value === null || value === undefined) {
    return '—';
  }
  if (typeof value === 'number') {
    if (item.format && (numeric(type) || type === undefined)) {
      return valueFormatter({ format: item.format, unit: null }, locale)(value);
    }
    return new Intl.NumberFormat(locale, { maximumFractionDigits: type === 'integer' ? 0 : 2 }).format(value);
  }
  if (typeof value === 'boolean') {
    return t(value ? 'indicators.map.yes' : 'indicators.map.no');
  }
  if (type === 'date') {
    return dateText(value, locale, 'date') ?? value;
  }
  if (type === 'timestamp') {
    return timestampText(value, locale);
  }
  return value;
}

/** The value of each of the widget's tooltip columns for one row, as text. */
export function tooltipValues(row: QueryRow, context: TooltipContext): string[] {
  return context.items.map((item, i) => tooltipValue(row.tooltip?.[i], context.types?.[i], item, context.locale, context.t));
}

export function tooltipContent(row: QueryRow, context: TooltipContext): ChartTooltipContent {
  const { items, types, t } = context;
  const values = tooltipValues(row, context);
  // A numerator or a denominator that is one of the widget's own tooltip columns is shown there, once; the n when it is the denominator is not repeated.
  const own = new Set(items.map((item) => item.column));
  const figures = context.bases
    ? figureTooltip(row, { names: figureNames({ ratio: context.ratio ?? null }, context.names ?? rawNames, context.bases.baseColumn), locale: context.locale, t }, context.bases.minN, {
        omit: { numerator: !!context.ratio && own.has(context.ratio.numerator), denominator: !!context.ratio && own.has(context.ratio.denominator) },
      })
    : { lines: [], footer: null };
  const combines = (row.rows ?? 1) > 1 ? t('indicators.map.combines', { count: row.rows }) : null;
  return {
    heading: shown(context.groupText, row.group) ?? t('indicators.chart.noGroup'),
    lines: [
      { label: context.label, value: context.format(row.value), align: 'end' },
      ...figures.lines,
      ...items.map((item, i) => ({ label: tooltipLabel(item, context.names), value: values[i], align: numeric(types?.[i]) || typeof row.tooltip?.[i] === 'number' ? ('end' as const) : ('start' as const) })),
    ],
    footer: [combines, figures.footer].filter((note): note is string => !!note).join(' · ') || null,
  };
}

/** What a screen reader says for the shape the keyboard is on: the whole tooltip as one sentence-like text. */
export function tooltipSpeech(content: ChartTooltipContent): string {
  return [content.heading, ...content.lines.map((line) => `${line.label}: ${line.value}`), ...(content.footer ? [content.footer] : [])].join('. ');
}
