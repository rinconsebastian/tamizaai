import { IndicatorFormat } from '../../../core/projects/pipeline.service';
import { WidgetConfig } from '../../../core/projects/indicators.service';

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

/** What a widget plots, for captions and descriptions: the indicator's label, with the measure or the ratio's two columns when the widget names them. */
export function plotted(config: Pick<WidgetConfig, 'measure' | 'ratio'>, label: string): string {
  if (config.ratio) {
    return `${label}: ${config.ratio.numerator} / ${config.ratio.denominator}`;
  }
  return config.measure ? `${label}: ${config.measure}` : label;
}
