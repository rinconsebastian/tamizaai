import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';
import { DateStyle, dateText, numberText, percentText } from './formats';

/** `value | dateTime` (a date and a time to the minute), `| dateTime: 'date'` or `| dateTime: 'time'`, in the browser's locale. */
@Pipe({ name: 'dateTime' })
export class DateTimePipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: Date | string | number | null | undefined, style: DateStyle = 'dateTime'): string | null {
    return dateText(value, this.locale, style);
  }
}

/** `value | num` (up to three decimals) or `| num: '1.0-1'`, in the browser's locale. */
@Pipe({ name: 'num' })
export class NumberPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: number | string | null | undefined, digits?: string): string | null {
    return numberText(value, this.locale, digits);
  }
}

/** `fraction | pct: '1.0-1'`: a fraction as a percentage, in the browser's locale. */
@Pipe({ name: 'pct' })
export class PctPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: number | string | null | undefined, digits?: string): string | null {
    return percentText(value, this.locale, digits);
  }
}
