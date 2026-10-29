import { Component, DestroyRef, ElementRef, computed, inject, input, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { countKey } from '../../../core/i18n/plural';
import { TableContainer, Table, tableCell, tableHead, tableHeaderCell, tableRow } from '../../../shared/ui';
import { DashboardPrint } from './dashboard-print';

/** What a chart plots, as a table: the "Show as table" view every widget offers (a text alternative to the SVG). */
export interface TableModel {
  headers: string[];
  /** Cells are text, already written with the indicator's format; a missing value is null. */
  rows: (string | null)[][];
  /** How each column's cells line up; numbers end, text starts. Without it the first column starts and the others end. */
  align?: ('start' | 'end')[];
}

/** The room kept under the rows for the note on the rows left out, about one line of small text. */
const noteRoom = 24;

@Component({
  selector: 'app-widget-table',
  imports: [Table, TableContainer, TranslocoDirective],
  template: `
    <tmz-table-container class="max-h-full overflow-y-auto">
      <table tmzTable data-testid="widget-table">
        <thead [class]="tableHead + ' sticky top-0'">
          <tr>
            @for (header of model().headers; track $index) {
              <th scope="col" [class]="tableHeaderCell + (endAligned($index) ? ' text-end' : '')">{{ header }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of model().rows; track $index) {
            <!-- On paper only the rows that fit the box are printed, never half a row (dashboard printing, design D8). -->
            <tr [class]="tableRow + ($index >= printedRows() ? ' paper:hidden' : '')" data-testid="widget-table-row">
              @for (cell of row; track $index) {
                @if ($index === 0) {
                  <th scope="row" [class]="tableCell + ' max-w-xs break-words font-normal text-heading'">{{ cell }}</th>
                } @else {
                  <td [class]="tableCell + (endAligned($index) ? ' text-end tabular-nums' : '')">{{ cell ?? '—' }}</td>
                }
              }
            </tr>
          }
        </tbody>
      </table>
    </tmz-table-container>
    @if (leftOut(); as count) {
      <p class="mt-1 hidden text-xs text-body paper:block" data-testid="widget-table-left-out" *transloco="let t">{{ t(leftOutKey(count), { count }) }}</p>
    }
  `,
  host: { class: 'block min-h-0' },
})
export class WidgetTable {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly transloco = inject(TranslocoService);
  private readonly print = inject(DashboardPrint, { optional: true });

  readonly model = input.required<TableModel>();

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  /** How many rows fit the box on paper, measured for one preparation of the print (its time); any other print measures again. */
  private readonly measured = signal<{ at: Date; rows: number } | null>(null);
  /** The rows printed: those that fit, while the print they were measured for is on; every row otherwise. */
  protected readonly printedRows = computed(() => {
    const measured = this.measured();
    return measured && measured.at === this.print?.printedAt() ? measured.rows : Infinity;
  });
  /** How many rows the paper leaves out; 0 when every row fits. */
  protected readonly leftOut = computed(() => Math.max(0, this.model().rows.length - this.printedRows()));

  constructor() {
    const print = this.print;
    if (print) {
      inject(DestroyRef).onDestroy(print.registerMeasure(() => this.measure(print.printedAt())));
    }
  }

  protected endAligned(column: number): boolean {
    return (this.model().align?.[column] ?? (column > 0 ? 'end' : 'start')) === 'end';
  }

  protected leftOutKey(count: number): string {
    return countKey('dashboards.print.rowsLeftOut', count, this.transloco.getActiveLang());
  }

  /**
   * Counts the rows whose bottom is inside the box the table has on paper, from the top: a table the member scrolled prints from
   * its first row. When some do not fit, a line is kept for the note that says how many.
   */
  private measure(at: Date | null): void {
    if (!at) {
      return;
    }
    const host = this.host.nativeElement;
    host.scrollTop = 0;
    const container = host.querySelector('tmz-table-container');
    if (container) {
      container.scrollTop = 0;
    }
    const bottom = host.getBoundingClientRect().bottom;
    const rows = Array.from(host.querySelectorAll('tbody > tr')).map((row) => row.getBoundingClientRect().bottom);
    const fitting = (limit: number) => rows.findIndex((rowBottom) => rowBottom > limit + 0.5);
    let fit = fitting(bottom);
    if (fit !== -1) {
      fit = Math.max(0, fitting(bottom - noteRoom));
    }
    this.measured.set({ at, rows: fit === -1 ? rows.length : fit });
  }
}
