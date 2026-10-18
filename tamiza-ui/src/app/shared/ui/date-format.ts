import { formatDate } from '@angular/common';

/**
 * The one format for a date with a time. It stops at the minute: seconds were the largest source of visual noise on the
 * synchronization log and the tables, and no screen needs them. A date without a time uses Angular's `mediumDate`.
 */
export const dateTimeFormat = 'MMM d, y, h:mm a';

/**
 * A timestamp as the API sends it (ISO 8601 with the project's offset, `2026-09-01T21:30:05-05:00`) in `dateTimeFormat`, shown in
 * the offset it carries: it reads as the project's wall time whatever zone the browser is in. A value with no offset is read as
 * the wall time it says; text that is not a timestamp is returned as it is.
 */
export function timestampText(value: string, locale: string): string {
  const offset = /(Z|[+-]\d{2}(?::?\d{2})?)$/.exec(value.trim())?.[1];
  const zone = offset === 'Z' ? 'UTC' : offset?.replace(/^([+-]\d{2})$/, '$100');
  try {
    return formatDate(value, dateTimeFormat, locale, zone);
  } catch {
    return value;
  }
}
