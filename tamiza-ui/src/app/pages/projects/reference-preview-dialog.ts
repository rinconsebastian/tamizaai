import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { countKey } from '../../core/i18n/plural';
import { ReferenceService, ReferenceTablePreview } from '../../core/projects/reference.service';
import { Button, Skeleton, NumberPipe } from '../../shared/ui';
import { TablePreview } from './table-preview';

export interface ReferencePreviewData {
  projectId: string;
  name: string;
}

/** A reference table's columns with their types and its first 100 rows. */
@Component({
  selector: 'app-reference-preview-dialog',
  imports: [NumberPipe, TranslocoDirective, Button, Skeleton, TablePreview],
  template: `
    <div
      class="flex max-h-[calc(100vh-2rem)] w-[min(64rem,calc(100vw-2rem))] flex-col rounded-base border border-default bg-neutral-primary-soft shadow-sm"
      *transloco="let t"
    >
      <header class="flex items-start justify-between gap-3 border-b border-default p-4 md:px-6">
        <h2 id="reference-preview-title" class="text-lg font-medium text-heading">
          <code>{{ data.name }}</code>
          @if (preview(); as preview) {
            <span class="block text-sm font-normal text-body" data-testid="reference-preview-note">
              {{ t(key('reference.previewNote', preview.table.rowCount), { shown: preview.rows.length, rows: (preview.table.rowCount | num), file: preview.table.fileName }) }}
            </span>
          }
        </h2>
        <button tmzButton variant="ghost" size="sm" type="button" (click)="ref.close()" data-testid="close-reference-preview">{{ t('runs.close') }}</button>
      </header>
      <div class="min-w-0 overflow-y-auto p-4 md:px-6">
        @if (preview(); as preview) {
          <app-table-preview [columns]="columns(preview)" [rows]="preview.rows" [fill]="true" />
        } @else {
          <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
        }
      </div>
    </div>
  `,
})
export class ReferencePreviewDialog {
  protected readonly data = inject<ReferencePreviewData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<void>>(DialogRef);
  private readonly service = inject(ReferenceService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);

  protected readonly preview = signal<ReferenceTablePreview | null>(null);

  /** The key of a message about a count of rows: its singular form for one row. */
  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
  }

  /** The columns as scripts see them; a column whose header was renamed shows the file's original header beneath its name. */
  protected columns(preview: ReferenceTablePreview) {
    return preview.table.columns.map((column) => ({ name: column.name, type: column.type, label: column.header !== column.name ? column.header : null }));
  }

  constructor() {
    this.service.preview(this.data.projectId, this.data.name).subscribe({
      next: (preview) => this.preview.set(preview),
      error: (error) => {
        this.errors.report(error);
        this.ref.close();
      },
    });
  }
}
