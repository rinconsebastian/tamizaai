import { DatePipe } from '@angular/common';
import { dateTimeFormat, timestampText } from './date-format';

describe('dateTimeFormat', () => {
  it('writes a time to the minute, without seconds', () => {
    const text = new DatePipe('en-US').transform('2026-10-02T22:00:01Z', dateTimeFormat, 'UTC');
    expect(text).toBe('Oct 2, 2026, 10:00 PM');
  });
});

describe('timestampText', () => {
  it('shows the wall time of the offset the value carries, whatever zone the browser is in', () => {
    expect(timestampText('2026-09-01T21:30:05-05:00', 'en-US')).toBe('Sep 1, 2026, 9:30 PM');
    expect(timestampText('2026-09-01T21:30:05+05:30', 'en-US')).toBe('Sep 1, 2026, 9:30 PM');
    expect(timestampText('2026-09-01T21:30:05-0500', 'en-US')).toBe('Sep 1, 2026, 9:30 PM');
    expect(timestampText('2026-09-01T21:30:05Z', 'en-US')).toBe('Sep 1, 2026, 9:30 PM');
  });

  it('reads a value with no offset as the wall time it says', () => {
    expect(timestampText('2026-09-01T21:30:05', 'en-US')).toBe('Sep 1, 2026, 9:30 PM');
  });

  it('returns text that is not a timestamp as it is', () => {
    expect(timestampText('not a time', 'en-US')).toBe('not a time');
  });
});
