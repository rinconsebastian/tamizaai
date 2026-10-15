import { DatePipe } from '@angular/common';
import { dateTimeFormat } from './date-format';

describe('dateTimeFormat', () => {
  it('writes a time to the minute, without seconds', () => {
    const text = new DatePipe('en-US').transform('2026-10-02T22:00:01Z', dateTimeFormat, 'UTC');
    expect(text).toBe('Oct 2, 2026, 10:00 PM');
  });
});
