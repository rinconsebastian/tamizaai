import { DataColumnType, DataValue } from '../../../core/projects/data.service';
import { QueryRow, WidgetTooltipItem } from '../../../core/projects/indicators.service';
import { ChartTooltipContent, dateText, timestampText } from '../../../shared/ui';
import { ValueFormatter, valueFormatter } from './format';

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
}

const numeric = (type: DataColumnType | undefined) => type === 'integer' || type === 'decimal';

/** What a tooltip column is called: its label, or the column's name. */
export function tooltipLabel(item: WidgetTooltipItem): string {
  return item.label?.trim() || item.column;
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
  return {
    heading: row.group ?? t('indicators.chart.noGroup'),
    lines: [
      { label: context.label, value: context.format(row.value), align: 'end' },
      ...items.map((item, i) => ({ label: tooltipLabel(item), value: values[i], align: numeric(types?.[i]) || typeof row.tooltip?.[i] === 'number' ? ('end' as const) : ('start' as const) })),
    ],
    footer: (row.rows ?? 1) > 1 ? t('indicators.map.combines', { count: row.rows }) : null,
  };
}

/** What a screen reader says for the shape the keyboard is on: the whole tooltip as one sentence-like text. */
export function tooltipSpeech(content: ChartTooltipContent): string {
  return [content.heading, ...content.lines.map((line) => `${line.label}: ${line.value}`), ...(content.footer ? [content.footer] : [])].join('. ');
}
