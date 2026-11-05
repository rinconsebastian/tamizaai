import { ColumnMetadataMap, IndicatorFormat } from '../../../core/projects/pipeline.service';
import { Layout, WidgetConfig, WidgetDisplay } from '../../../core/projects/indicators.service';
import { gap, rowHeight } from './grid-layout';

// How an indicator's values are written (design D6): the indicator says what a number is, so a stat tile, an axis, a legend, a
// tooltip and a "Show as table" cell all use one formatter.

export interface IndicatorUnit {
  format: IndicatorFormat;
  unit: string | null;
}

export interface ValueFormatter {
  /** A value with its unit: `12,450 households`, `42%`, `COP 1,200.50`. */
  (value: number): string;
  /** A value without its unit and with up to two decimals, for the ends of a range of values. */
  plain(value: number): string;
  /** A short value for an axis tick (`12K`, `40%`), without a unit. */
  tick(value: number): string;
}

/**
 * The format a box plot writes its figures in: a count is written as a decimal, because quartiles, medians and means can fall between
 * whole numbers. A box's numbers of values and of outliers are always whole. Twin of `BoxFormat` in tamiza-api (`ReportDataBuilder`).
 */
export function boxFormat(format: IndicatorFormat): IndicatorFormat {
  return format === 'count' ? 'decimal' : format;
}

/** The indicator as a box plot writes its figures. */
export function boxIndicator(indicator: IndicatorUnit): IndicatorUnit {
  return { ...indicator, format: boxFormat(indicator.format) };
}

/** What a widget shows for an indicator the project does not list (a removed one): numbers with two decimals. */
export const plainIndicator: IndicatorUnit = { format: 'decimal', unit: null };

const currencyCodes = new Set<string>(
  typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('currency') : ['COP', 'USD', 'EUR', 'GBP', 'MXN', 'BRL', 'PEN', 'CLP', 'ARS'],
);

/** True for an ISO 4217 currency code, such as COP or USD. */
export function isCurrencyCode(unit: string): boolean {
  return currencyCodes.has(unit);
}

/**
 * The formatter of an indicator:
 * - `count`: a whole number with thousands separators, the unit after it;
 * - `decimal`: two decimals, the unit after it;
 * - `percent`: the value times 100 followed by `%` (a rate stored as a fraction reads as a percentage), a unit after it;
 * - `currency`: two decimals, written as a currency when the unit is a currency code, else with the unit before it.
 */
export function valueFormatter(indicator: IndicatorUnit, locale: string): ValueFormatter {
  const unit = indicator.unit?.trim() ?? '';
  const fixed = (digits: { min: number; max: number }) =>
    new Intl.NumberFormat(locale, { minimumFractionDigits: digits.min, maximumFractionDigits: digits.max });
  const compact = (max: number) => new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: max });

  let figure: (value: number) => string;
  let withUnit: (value: number) => string;
  let tick: (value: number) => string;
  const trimmed = new Intl.NumberFormat(locale, { maximumFractionDigits: 2 });
  let plain: (value: number) => string = (value) => trimmed.format(value);
  const after = (text: string, suffix: string) => (suffix ? `${text} ${suffix}` : text);

  switch (indicator.format) {
    case 'count': {
      const whole = fixed({ min: 0, max: 0 });
      const short = compact(1);
      figure = (value) => whole.format(value);
      withUnit = (value) => after(figure(value), unit);
      tick = (value) => short.format(value);
      break;
    }
    case 'percent': {
      const percent = new Intl.NumberFormat(locale, { style: 'percent', minimumFractionDigits: 0, maximumFractionDigits: 2 });
      const short = new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 });
      figure = (value) => percent.format(value);
      plain = figure;
      // A unit of "%" would only repeat the sign.
      withUnit = (value) => after(figure(value), unit === '%' ? '' : unit);
      tick = (value) => short.format(value);
      break;
    }
    case 'currency': {
      const two = fixed({ min: 2, max: 2 });
      const short = compact(2);
      const code = isCurrencyCode(unit) ? unit : null;
      const money = code ? new Intl.NumberFormat(locale, { style: 'currency', currency: code, minimumFractionDigits: 2, maximumFractionDigits: 2 }) : null;
      figure = (value) => two.format(value);
      // A symbol such as $ sits against the number; a word such as pesos is set apart.
      withUnit = (value) => (money ? money.format(value) : !unit ? figure(value) : /\p{L}/u.test(unit) ? `${unit} ${figure(value)}` : `${unit}${figure(value)}`);
      tick = (value) => short.format(value);
      break;
    }
    default: {
      const two = fixed({ min: 2, max: 2 });
      const short = compact(2);
      figure = (value) => two.format(value);
      withUnit = (value) => after(figure(value), unit);
      tick = (value) => short.format(value);
    }
  }

  return Object.assign((value: number) => withUnit(value), { plain, tick });
}

/**
 * The formatter of the base figures (add-small-sample-warnings, design D9): a numerator, a denominator or an n, as a whole number with
 * thousands separators and up to two decimals when it is not whole (a decimal numerator, such as 0.5), never in the indicator's format or
 * with its unit. Twin of `ReportFormat.Figure` in tamiza-api.
 */
export function figureFormatter(locale: string): (value: number) => string {
  const figure = new Intl.NumberFormat(locale, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
  return (value) => figure.format(value);
}

/** The name shown for a column of a table. */
export type ColumnNamer = (column: string) => string;

/** Every column under its own name: what a chart shows when nothing gives it labels, and the default of every chart's `names` input. */
export const rawNames: ColumnNamer = (column) => column;

/**
 * The name shown for a column (add-indicator-column-metadata, design D6): the widget's own label, else the indicator's, else the
 * column's own name. A blank label is none. Twin of `ColumnLabels.For` in tamiza-api.
 */
export function columnLabels(
  indicator: { columns?: ColumnMetadataMap | null } | null | undefined,
  display: Pick<WidgetDisplay, 'labels'> | null | undefined,
): ColumnNamer {
  const widget = display?.labels;
  const own = indicator?.columns;
  if (!widget && !own) {
    return rawNames;
  }
  return (column) => {
    // Own keys only, so a column called `constructor` is not read from the prototype.
    const named = widget && Object.hasOwn(widget, column) ? widget[column]?.trim() : '';
    if (named) {
      return named;
    }
    const labelled = own && Object.hasOwn(own, column) ? own[column]?.label?.trim() : '';
    return labelled || column;
  };
}

/** The name shown for a column that may be unset: an unset column stays as it is, so a caption reads as it did. */
export function named(names: ColumnNamer, column: string | null | undefined): string | null | undefined {
  return column ? names(column) : column;
}

/** The widget's own caption text, trimmed, or null while it has none (a blank text is none). */
export function ownCaption(config: Pick<WidgetConfig, 'display'>): string | null {
  return config.display?.caption?.text?.trim() || null;
}

/**
 * What a widget plots, for captions and descriptions: the widget's own caption text when it has one (add-widget-context, design D3: it
 * replaces the label and the column names together), else the indicator's label, with the measure or the ratio's two columns when the
 * widget names them, each under its name shown (`names`; the columns' own names when none is given). A blank own text is none. Twin of
 * `ReportDataBuilder.Plotted` in tamiza-api.
 */
export function plotted(config: Pick<WidgetConfig, 'measure' | 'ratio' | 'display'>, label: string, names: ColumnNamer = rawNames): string {
  const own = ownCaption(config);
  if (own) {
    return own;
  }
  if (config.ratio) {
    return `${label}: ${names(config.ratio.numerator)} / ${names(config.ratio.denominator)}`;
  }
  return config.measure ? `${label}: ${names(config.measure)}` : label;
}

/**
 * Whether the line that names what a widget plots is seen (add-widget-context, design D3): false only for a caption set to hidden. A
 * hidden line stays in the page for screen readers, and tooltips, tables and exports keep naming what is plotted.
 */
export function captionShown(config: Pick<WidgetConfig, 'display'>): boolean {
  return config.display?.caption?.hidden !== true;
}

/**
 * The indicator as one widget shows it (add-widget-context, design D4): with the widget's own unit when it has one, else its own. The
 * formatters are built from the result, so the `%` rule (a unit of `%` is not repeated after a number) applies to a widget's unit as to
 * an indicator's. Twin of the unit chosen in `ReportDataBuilder.MetaOf` (tamiza-api).
 */
export function withWidgetUnit<T extends IndicatorUnit>(indicator: T, config: Pick<WidgetConfig, 'display'> | null | undefined): T {
  const unit = config?.display?.unit?.trim();
  return unit ? { ...indicator, unit } : indicator;
}

/** About how many characters of a widget's 12 px text a line holds, per column of its tile (add-widget-context, design D5). */
const charactersPerColumn = 15;
/** The height of one line of those texts, and the gap that sets one text off from the next, in pixels. */
const textLine = 16;
const textGap = 4;

/**
 * Whether a widget's subtitle and footnote are likely to crowd its tile (add-widget-context, design D5), for the widget form's hint.
 * A tile two rows high or less with either text always does; a taller one does when the texts, printed whole (about 15 characters per
 * grid column per line, 16 px per line and 4 px per text), would take half of its height or more. A blank text is none.
 */
export function textsCrowd(layout: Pick<Layout, 'w' | 'h'>, subtitle: string | null | undefined, footnote: string | null | undefined): boolean {
  const texts = [subtitle, footnote].map((text) => text?.trim() ?? '').filter((text) => text.length > 0);
  if (!texts.length) {
    return false;
  }
  if (layout.h <= 2) {
    return true;
  }
  const perLine = Math.max(1, layout.w) * charactersPerColumn;
  const used = texts.reduce((total, text) => total + Math.ceil(text.length / perLine) * textLine + textGap, 0);
  return used >= (layout.h * rowHeight + (layout.h - 1) * gap) / 2;
}
