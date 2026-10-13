import { Dialog } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { ReferenceService, ReferenceTable, ReferenceTableUpload, maxReferenceFileBytes } from '../../core/projects/reference.service';
import {
  Alert,
  Button,
  Card,
  ConfirmDialog,
  Skeleton,
  Table,
  TableContainer,
  ToastService,
  tableCell,
  tableHead,
  tableHeaderCell,
  tableRow,
} from '../../shared/ui';
import { ProjectContext } from './project-context';
import { ReferencePreviewData, ReferencePreviewDialog } from './reference-preview-dialog';
import { ReferenceUploadData, ReferenceUploadDialog } from './reference-upload-dialog';

/** The project's reference tables: every member lists and previews them; analysts and admins upload, replace and delete. */
@Component({
  selector: 'app-project-reference',
  imports: [DatePipe, TranslocoDirective, Alert, Button, Card, Skeleton, Table, TableContainer],
  template: `
    <section class="flex flex-col gap-4" *transloco="let t">
      <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h2 class="text-lg font-semibold text-heading">{{ t('reference.title') }}</h2>
          <p class="mt-1 text-sm text-body">{{ t('reference.intro') }}</p>
        </div>
        @if (canEdit()) {
          <button tmzButton type="button" class="shrink-0" (click)="openUpload()" data-testid="reference-add">{{ t('reference.add') }}</button>
        }
      </div>

      @if (renamed(); as renamed) {
        <tmz-alert tone="brand" data-testid="reference-renamed">
          {{ t('reference.renamed', { name: renamed.table.name }) }}
          <ul class="mt-1 list-inside list-disc">
            @for (column of renamed.renamed; track column.name) {
              <li>
                {{ column.header || t('reference.emptyHeader') }} → <code>{{ column.name }}</code>
              </li>
            }
          </ul>
        </tmz-alert>
      }

      <!-- One hidden picker for "Replace"; the row it replaces is remembered while the picker is open. -->
      <input type="file" accept=".csv,.xlsx" class="sr-only" tabindex="-1" aria-hidden="true" #replaceInput (change)="replaceChosen($any($event.target))" data-testid="reference-replace-file" />

      @if (tables(); as tables) {
        @if (tables.length === 0) {
          <tmz-card><p class="text-sm text-body" data-testid="reference-empty">{{ t(canEdit() ? 'reference.emptyEditor' : 'reference.emptyViewer') }}</p></tmz-card>
        } @else {
          <ul class="flex flex-col gap-3 md:hidden" data-testid="reference-cards">
            @for (table of tables; track table.name) {
              <li class="rounded-base border border-default bg-neutral-primary-soft p-4 text-sm">
                <p class="font-medium text-heading"><code>{{ table.name }}</code></p>
                <p class="mt-1 break-all text-body">{{ table.fileName }}</p>
                <p class="mt-1 text-body">{{ t('reference.size', { rows: table.rowCount, columns: table.columns.length }) }}</p>
                <p class="mt-1 text-body">{{ t('reference.updated', { name: table.updatedBy.name, date: (table.updatedAt | date: 'medium') }) }}</p>
                <div class="mt-3 flex flex-wrap gap-2">
                  <button tmzButton variant="secondary" size="sm" type="button" (click)="openPreview(table)">{{ t('reference.preview') }}</button>
                  @if (canEdit()) {
                    <button tmzButton variant="secondary" size="sm" type="button" (click)="startReplace(table, replaceInput)">{{ t('reference.replace') }}</button>
                    <button tmzButton variant="danger" size="sm" type="button" (click)="remove(table)">{{ t('reference.delete') }}</button>
                  }
                </div>
              </li>
            }
          </ul>
          <tmz-table-container class="hidden md:block">
            <table tmzTable data-testid="reference-table">
              <thead [class]="tableHead">
                <tr>
                  <th scope="col" [class]="tableHeaderCell">{{ t('reference.columns.name') }}</th>
                  <th scope="col" [class]="tableHeaderCell">{{ t('reference.columns.file') }}</th>
                  <th scope="col" [class]="tableHeaderCell">{{ t('reference.columns.size') }}</th>
                  <th scope="col" [class]="tableHeaderCell">{{ t('reference.columns.updated') }}</th>
                  <th scope="col" [class]="tableHeaderCell"><span class="sr-only">{{ t('reference.columns.actions') }}</span></th>
                </tr>
              </thead>
              <tbody>
                @for (table of tables; track table.name) {
                  <tr [class]="tableRow" [attr.data-testid]="'reference-row-' + table.name">
                    <td [class]="tableCell"><code class="text-heading">{{ table.name }}</code></td>
                    <td [class]="tableCell + ' max-w-xs break-all'">{{ table.fileName }}</td>
                    <td [class]="tableCell + ' whitespace-nowrap'">{{ t('reference.size', { rows: table.rowCount, columns: table.columns.length }) }}</td>
                    <td [class]="tableCell">
                      {{ table.updatedAt | date: 'medium' }}
                      <span class="block text-xs">{{ table.updatedBy.name }}</span>
                    </td>
                    <td [class]="tableCell">
                      <span class="flex flex-wrap justify-end gap-2">
                        <button tmzButton variant="secondary" size="sm" type="button" (click)="openPreview(table)" [attr.data-testid]="'reference-preview-' + table.name">
                          {{ t('reference.preview') }}
                        </button>
                        @if (canEdit()) {
                          <button tmzButton variant="secondary" size="sm" type="button" (click)="startReplace(table, replaceInput)" [attr.data-testid]="'reference-replace-' + table.name">
                            {{ t('reference.replace') }}
                          </button>
                          <button tmzButton variant="danger" size="sm" type="button" (click)="remove(table)" [attr.data-testid]="'reference-delete-' + table.name">
                            {{ t('reference.delete') }}
                          </button>
                        }
                      </span>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </tmz-table-container>
        }
      } @else {
        <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-full" /></div>
      }
    </section>
  `,
})
export class ProjectReference {
  private readonly service = inject(ReferenceService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly dialog = inject(Dialog);
  private readonly context = inject(ProjectContext);

  protected readonly canEdit = computed(() => this.context.can('analyst'));
  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  protected readonly tables = signal<ReferenceTable[] | null>(null);
  /** The last upload or replacement, when it renamed columns. */
  protected readonly renamed = signal<ReferenceTableUpload | null>(null);
  private replacing: ReferenceTable | null = null;

  constructor() {
    this.load();
  }

  protected openUpload(): void {
    const ref = this.dialog.open<ReferenceTableUpload, ReferenceUploadData>(ReferenceUploadDialog, {
      data: { projectId: this.projectId() },
      ariaLabelledBy: 'reference-upload-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
    ref.closed.subscribe((upload) => {
      if (upload) {
        this.saved(upload, 'reference.uploaded');
      }
    });
  }

  protected openPreview(table: ReferenceTable): void {
    this.dialog.open<void, ReferencePreviewData>(ReferencePreviewDialog, {
      data: { projectId: this.projectId(), name: table.name },
      ariaLabelledBy: 'reference-preview-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
  }

  protected startReplace(table: ReferenceTable, input: HTMLInputElement): void {
    this.replacing = table;
    input.click();
  }

  protected async replaceChosen(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    const table = this.replacing;
    this.replacing = null;
    if (!file || !table) {
      return;
    }
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    if (file.size > maxReferenceFileBytes) {
      this.toasts.error(t('reference.fileTooLarge'));
      return;
    }
    const confirmed = await this.confirmDialog.confirm({
      title: t('reference.replaceTitle', { name: table.name }),
      message: t('reference.replaceMessage', { name: table.name, file: file.name }),
      confirmLabel: t('reference.replace'),
      cancelLabel: t('common.cancel'),
    });
    if (!confirmed) {
      return;
    }
    this.service.replace(this.projectId(), table.name, file).subscribe({
      next: (upload) => this.saved(upload, 'reference.replaced'),
      error: (error) => {
        const parsed = this.errors.parse(error);
        this.toasts.error(parsed.fieldErrors['file'] ?? parsed.message);
      },
    });
  }

  protected async remove(table: ReferenceTable): Promise<void> {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const confirmed = await this.confirmDialog.confirm({
      title: t('reference.deleteTitle', { name: table.name }),
      message: t('reference.deleteMessage', { name: table.name }),
      confirmLabel: t('reference.delete'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (!confirmed) {
      return;
    }
    this.service.remove(this.projectId(), table.name).subscribe({
      next: () => {
        this.toasts.success(t('reference.deleted', { name: table.name }));
        this.renamed.set(null);
        this.load();
      },
      error: (error) => this.errors.report(error),
    });
  }

  private saved(upload: ReferenceTableUpload, messageKey: string): void {
    this.toasts.success(this.transloco.translate(messageKey, { name: upload.table.name }));
    this.renamed.set(upload.renamed.length ? upload : null);
    this.load();
  }

  private load(): void {
    this.service.list(this.projectId()).subscribe({
      next: (tables) => this.tables.set(tables),
      error: (error) => this.errors.report(error),
    });
  }

  private projectId(): string {
    return this.context.project()!.id;
  }
}
