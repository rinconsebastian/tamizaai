import { Component, Directive } from '@angular/core';

/** Scrolls a wide table inside its own box so the page never scrolls sideways. */
@Component({
  selector: 'tmz-table-container',
  template: '<ng-content />',
  host: { class: 'block relative overflow-x-auto bg-neutral-primary-soft shadow-xs rounded-base border border-default' },
})
export class TableContainer {}

/** Flowbite table styles; use `th`/`td` with the classes from `tableCell`. */
@Directive({
  selector: 'table[tmzTable]',
  host: { class: 'w-full text-sm text-left text-body' },
})
export class Table {}

export const tableHead = 'text-sm text-body bg-neutral-secondary-soft border-b border-default';
export const tableHeaderCell = 'px-4 py-3 font-medium whitespace-nowrap';
export const tableRow = 'bg-neutral-primary-soft border-b border-default last:border-0';
export const tableCell = 'px-4 py-3';
