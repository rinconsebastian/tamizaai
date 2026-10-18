/**
 * Dates, times and numbers for the interface, formatted with `Intl` in the locale the browser chose (see
 * `core/i18n/interface-language.ts`). Angular's own locale-data pipes are not used: they throw for a locale that was not
 * registered, and registering `es` would give every Spanish-speaking region the same separators.
 */

export type DateStyle = 'dateTime' | 'date' | 'time';

type Instant = Date | string | number;

const styles: Record<DateStyle, Intl.DateTimeFormatOptions> = {
  // The month is named in every locale: a numeric date (1/09/2026) reads differently from one region to the next.
  dateTime: { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' },
  date: { day: 'numeric', month: 'short', year: 'numeric' },
  time: { hour: 'numeric', minute: '2-digit' },
};

const dateFormats = new Map<string, Intl.DateTimeFormat>();
const numberFormats = new Map<string, Intl.NumberFormat>();

function dateFormat(locale: string, style: DateStyle, timeZone: string | undefined): Intl.DateTimeFormat {
  const key = `${locale}|${style}|${timeZone ?? ''}`;
  let format = dateFormats.get(key);
  if (!format) {
    format = new Intl.DateTimeFormat(locale, { ...styles[style], timeZone });
    dateFormats.set(key, format);
  }
  return format;
}

function toDate(value: Instant): Date {
  // A date without a time is a calendar day, not an instant: read it in the viewer's zone so it does not move back a day.
  const day = typeof value === 'string' ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(value.trim()) : null;
  return day ? new Date(Number(day[1]), Number(day[2]) - 1, Number(day[3])) : new Date(value);
}

/**
 * A date, a time or both, to the minute, in the viewer's time zone (or `timeZone`). Empty values give `null`. Text that is not
 * a date is returned as it is, so an unexpected value from the API stays readable.
 */
export function dateText(
  value: Instant | null | undefined,
  locale: string,
  style: DateStyle = 'dateTime',
  timeZone?: string,
): string | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const date = toDate(value);
  return Number.isNaN(date.getTime()) ? String(value) : dateFormat(locale, style, timeZone).format(date);
}

/** A date with a time to the minute: the one format every screen shares. */
export const dateTimeText = (value: Instant | null | undefined, locale: string) => dateText(value, locale, 'dateTime');

const wallTime = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2}))?/;

/**
 * A timestamp as the API sends it (ISO 8601 with the project's offset, `2026-09-01T21:30:05-05:00`) in the shared format,
 * shown as the wall time it carries: it reads the same whatever zone the browser is in. A value with no offset is read as the
 * wall time it says; text that is not a timestamp is returned as it is.
 */
export function timestampText(value: string, locale: string): string {
  const parts = wallTime.exec(value.trim());
  if (!parts) {
    return value;
  }
  const [year, month, day, hour, minute] = [parts[1], parts[2], parts[3], parts[4] ?? '0', parts[5] ?? '0'].map(Number);
  const wall = new Date(Date.UTC(year, month - 1, day, hour, minute));
  if (wall.getUTCFullYear() !== year || wall.getUTCMonth() !== month - 1 || wall.getUTCDate() !== day) {
    return value;
  }
  return dateFormat(locale, 'dateTime', 'UTC').format(wall);
}

/** Minimum integer digits, then minimum and maximum fraction digits: `'1.0-1'`, as Angular's number pipe reads them. */
function digitsOptions(digits: string | undefined): Intl.NumberFormatOptions {
  const match = digits ? /^(\d+)\.(\d+)-(\d+)$/.exec(digits) : null;
  return match
    ? { minimumIntegerDigits: Number(match[1]), minimumFractionDigits: Number(match[2]), maximumFractionDigits: Number(match[3]) }
    : { minimumFractionDigits: 0, maximumFractionDigits: 3 };
}

function numberFormat(locale: string, style: 'decimal' | 'percent', digits: string | undefined): Intl.NumberFormat {
  const key = `${locale}|${style}|${digits ?? ''}`;
  let format = numberFormats.get(key);
  if (!format) {
    format = new Intl.NumberFormat(locale, { style, ...digitsOptions(digits) });
    numberFormats.set(key, format);
  }
  return format;
}

function parse(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const number = Number(value);
  return Number.isNaN(number) ? null : number;
}

/** A number with the locale's separators; at most three decimals unless `digits` (`'1.0-1'`) says otherwise. */
export function numberText(value: number | string | null | undefined, locale: string, digits?: string): string | null {
  const number = parse(value);
  return number === null ? null : numberFormat(locale, 'decimal', digits).format(number);
}

/** A fraction as a percentage (0.45 → 45%), written the way the locale writes it (`45 %` in some Spanish regions). */
export function percentText(value: number | string | null | undefined, locale: string, digits = '1.0-0'): string | null {
  const number = parse(value);
  return number === null ? null : numberFormat(locale, 'percent', digits).format(number);
}
