import { DecimalPipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { TableContainer, Table, tableCell, tableHead, tableHeaderCell, tableRow } from '../../../shared/ui';

/** What a chart plots, as a table: the "Show as table" view every widget offers (a text alternative to the SVG). */
export interface TableModel {
  headers: string[];
  /** Cells are text, or numbers shown with the locale's separators. */
  rows: (string | number | null)[][];
}

@Component({
  selector: 'app-widget-table',
  imports: [DecimalPipe, Table, TableContainer],
  template: `
    <tmz-table-container class="max-h-full overflow-y-auto">
      <table tmzTable data-testid="widget-table">
        <thead [class]="tableHead + ' sticky top-0'">
          <tr>
            @for (header of model().headers; track $index) {
              <th scope="col" [class]="tableHeaderCell + ($index > 0 ? ' text-end' : '')">{{ header }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of model().rows; track $index) {
            <tr [class]="tableRow" data-testid="widget-table-row">
              @for (cell of row; track $index) {
                @if ($index === 0) {
                  <th scope="row" [class]="tableCell + ' max-w-xs break-words font-normal text-heading'">{{ cell }}</th>
                } @else {
                  <td [class]="tableCell + ' text-end tabular-nums'">{{ cell === null ? '—' : isNumber(cell) ? (cell | number: '1.0-2') : cell }}</td>
                }
              }
            </tr>
          }
        </tbody>
      </table>
    </tmz-table-container>
  `,
  host: { class: 'block min-h-0' },
})
export class WidgetTable {
  readonly model = input.required<TableModel>();

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  protected isNumber(value: unknown): value is number {
    return typeof value === 'number';
  }
}
