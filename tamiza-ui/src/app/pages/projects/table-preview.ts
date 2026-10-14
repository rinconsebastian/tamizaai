import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ColumnType } from '../../core/projects/pipeline.service';
import { Table, TableContainer, tableCell, tableHead, tableHeaderCell, tableRow } from '../../shared/ui';

/** The first rows of a table with each column's type, scrolling inside its own box. Cut text values are marked. */
@Component({
  selector: 'app-table-preview',
  imports: [TranslocoDirective, Table, TableContainer],
  template: `
    <tmz-table-container *transloco="let t" class="max-h-96 overflow-y-auto">
      <table tmzTable data-testid="preview-table">
        <thead [class]="tableHead + ' sticky top-0'">
          <tr>
            @for (column of columns(); track $index) {
              <th scope="col" [class]="tableHeaderCell">
                {{ column.name }}
                @if (column.label) {
                  <span class="block max-w-xs truncate text-xs font-normal text-body" data-testid="column-label" [attr.title]="column.label">{{ column.label }}</span>
                }
                <span class="block text-xs font-normal text-body" data-testid="column-type">{{ t('preview.types.' + column.type) }}</span>
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track $index; let r = $index) {
            <tr [class]="tableRow">
              @for (value of row; track $index; let c = $index) {
                <td [class]="tableCell + ' max-w-xs truncate whitespace-nowrap'" [attr.title]="value === null || value === undefined ? null : value">
                  @if (value === null || value === undefined) {
                    <span aria-hidden="true">—</span>
                  } @else {
                    {{ value }}
                    @if (isCut(r, c)) {
                      <span class="sr-only">{{ t('preview.cut') }}</span>
                    }
                  }
                </td>
              }
            </tr>
          }
        </tbody>
      </table>
    </tmz-table-container>
  `,
})
export class TablePreview {
  readonly columns = input.required<{ name: string; type: ColumnType | string; label?: string | null }[]>();
  readonly rows = input.required<unknown[][]>();
  readonly cutCells = input<number[][] | null>(null);

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  private readonly cut = computed(() => new Set((this.cutCells() ?? []).map(([r, c]) => `${r}:${c}`)));

  protected isCut(row: number, column: number): boolean {
    return this.cut().has(`${row}:${column}`);
  }
}
