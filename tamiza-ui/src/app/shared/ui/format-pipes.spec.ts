import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { localeTesting } from '../../../testing/transloco-testing';
import { DateTimePipe, NumberPipe, PctPipe } from './format-pipes';

@Component({
  imports: [DateTimePipe, NumberPipe, PctPipe],
  template: `
    <p data-testid="when">{{ '2026-09-02T02:30:05Z' | dateTime }}</p>
    <p data-testid="day">{{ '2026-09-01' | dateTime: 'date' }}</p>
    <p data-testid="clock">{{ '2026-09-02T02:30:05Z' | dateTime: 'time' }}</p>
    <p data-testid="total">{{ 12345.5 | num }}</p>
    <p data-testid="rounded">{{ 12345.55 | num: '1.0-1' }}</p>
    <p data-testid="share">{{ 0.4567 | pct: '1.0-1' }}</p>
    <p data-testid="empty">[{{ null | num }}][{{ null | dateTime }}]</p>
  `,
})
class Host {}

function render(locale: string) {
  TestBed.configureTestingModule({ providers: [localeTesting(locale)] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const read = (id: string) =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${id}"]`)!.textContent!.replace(/[  ]/g, ' ');
  return read;
}

describe('format pipes', () => {
  it('format with American English separators and order', () => {
    const read = render('en-US');

    expect(read('day')).toBe('Sep 1, 2026');
    expect(read('total')).toBe('12,345.5');
    expect(read('rounded')).toBe('12,345.6');
    expect(read('share')).toBe('45.7%');
    expect(read('empty')).toBe('[][]');
    // The zone of the test process decides the clock, so check only the parts that do not depend on it.
    expect(read('when')).toMatch(/^Sep [12], 2026, \d{1,2}:\d{2} [AP]M$/);
    expect(read('clock')).toMatch(/^\d{1,2}:\d{2} [AP]M$/);
  });

  it('format with Colombian Spanish separators and order', () => {
    const read = render('es-CO');

    expect(read('day')).toBe('1 de sept de 2026');
    expect(read('total')).toBe('12.345,5');
    expect(read('rounded')).toBe('12.345,6');
    expect(read('share')).toBe('45,7%');
    expect(read('when')).toMatch(/^[12] de sept de 2026, \d{1,2}:\d{2} [ap]\. m\.$/);
    expect(read('clock')).toMatch(/^\d{1,2}:\d{2} [ap]\. m\.$/);
  });

  it('format with the 24-hour clock of Spain', () => {
    const read = render('es-ES');

    expect(read('total')).toBe('12.345,5');
    expect(read('when')).toMatch(/^[12] sept 2026, \d{1,2}:\d{2}$/);
  });
});
