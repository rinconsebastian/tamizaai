import { dateText, dateTimeText, numberText, percentText, timestampText } from './formats';

/** ICU versions differ in which space they put between the time and "p. m."; compare with plain spaces. */
const plain = (text: string | null) => text?.replace(/[  ]/g, ' ') ?? null;

const moment = '2026-09-02T02:30:05Z'; // 21:30 on 1 September in Bogotá

describe('dateTimeText', () => {
  const bogota = (locale: string) => plain(dateText(moment, locale, 'dateTime', 'America/Bogota'));

  it('writes the date and the time to the minute in each locale', () => {
    expect(bogota('en-US')).toBe('Sep 1, 2026, 9:30 PM');
    expect(bogota('en-GB')).toBe('1 Sept 2026, 21:30');
    expect(bogota('es-CO')).toBe('1 de sept de 2026, 9:30 p. m.');
    expect(bogota('es-ES')).toBe('1 sept 2026, 21:30');
  });

  it('writes a time to the minute, without seconds', () => {
    expect(plain(dateText('2026-10-02T22:00:01Z', 'en-US', 'dateTime', 'UTC'))).toBe('Oct 2, 2026, 10:00 PM');
  });

  it('writes a date alone and a time alone', () => {
    expect(plain(dateText(moment, 'en-US', 'date', 'America/Bogota'))).toBe('Sep 1, 2026');
    expect(plain(dateText(moment, 'en-US', 'time', 'America/Bogota'))).toBe('9:30 PM');
    expect(plain(dateText(moment, 'es-ES', 'time', 'America/Bogota'))).toBe('21:30');
  });

  it('keeps a calendar day on its own day whatever the viewer zone', () => {
    expect(dateText('2026-09-01', 'en-US', 'date')).toBe('Sep 1, 2026');
  });

  it('gives null for an empty value and returns text that is not a date as it is', () => {
    expect(dateText(null, 'en-US')).toBeNull();
    expect(dateText(undefined, 'en-US')).toBeNull();
    expect(dateText('', 'en-US')).toBeNull();
    expect(dateText('not a date', 'en-US')).toBe('not a date');
  });

  it('formats in the viewer zone by default', () => {
    expect(dateTimeText(moment, 'en-US')).toBe(dateText(moment, 'en-US', 'dateTime'));
  });
});

describe('timestampText', () => {
  it('shows the wall time of the offset the value carries, whatever zone the browser is in', () => {
    expect(plain(timestampText('2026-09-01T21:30:05-05:00', 'en-US'))).toBe('Sep 1, 2026, 9:30 PM');
    expect(plain(timestampText('2026-09-01T21:30:05+05:30', 'en-US'))).toBe('Sep 1, 2026, 9:30 PM');
    expect(plain(timestampText('2026-09-01T21:30:05-0500', 'en-US'))).toBe('Sep 1, 2026, 9:30 PM');
    expect(plain(timestampText('2026-09-01T21:30:05Z', 'en-US'))).toBe('Sep 1, 2026, 9:30 PM');
  });

  it('reads a value with no offset as the wall time it says', () => {
    expect(plain(timestampText('2026-09-01T21:30:05', 'en-US'))).toBe('Sep 1, 2026, 9:30 PM');
  });

  it('follows the locale', () => {
    expect(plain(timestampText('2026-09-01T21:30:05-05:00', 'es-CO'))).toBe('1 de sept de 2026, 9:30 p. m.');
    expect(plain(timestampText('2026-09-01T21:30:05-05:00', 'es-ES'))).toBe('1 sept 2026, 21:30');
  });

  it('returns text that is not a timestamp as it is', () => {
    expect(timestampText('not a time', 'en-US')).toBe('not a time');
    expect(timestampText('2026-13-45T10:00:00Z', 'en-US')).toBe('2026-13-45T10:00:00Z');
  });
});

describe('numberText', () => {
  it('uses the separators of the locale', () => {
    expect(numberText(12345.5, 'en-US')).toBe('12,345.5');
    expect(numberText(12345.5, 'es-MX')).toBe('12,345.5');
    expect(numberText(12345.5, 'es-CO')).toBe('12.345,5');
    expect(numberText(12345.5, 'es-419')).toBe('12,345.5');
  });

  it('writes at most three decimals unless told otherwise', () => {
    expect(numberText(1.23456, 'en-US')).toBe('1.235');
    expect(numberText(0.123456, 'en-US', '1.0-1')).toBe('0.1');
    expect(numberText(3, 'es-CO', '1.1-1')).toBe('3,0');
  });

  it('accepts numeric text and gives null for empty or non-numeric values', () => {
    expect(numberText('1234', 'en-US')).toBe('1,234');
    expect(numberText(null, 'en-US')).toBeNull();
    expect(numberText(undefined, 'en-US')).toBeNull();
    expect(numberText('', 'en-US')).toBeNull();
    expect(numberText('x', 'en-US')).toBeNull();
  });
});

describe('percentText', () => {
  it('writes a fraction as a percentage', () => {
    expect(plain(percentText(0.45, 'en-US'))).toBe('45%');
    expect(plain(percentText(0.4567, 'en-US', '1.0-1'))).toBe('45.7%');
    expect(plain(percentText(0.4567, 'es-CO', '1.0-1'))).toBe('45,7%');
    expect(plain(percentText(0.45, 'es-ES'))).toBe('45 %');
    expect(percentText(null, 'en-US')).toBeNull();
  });
});
