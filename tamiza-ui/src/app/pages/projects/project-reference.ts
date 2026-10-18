import { Dialog } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { ReferenceService, ReferenceTable, ReferenceTableUpload } from '../../core/projects/reference.service';
import { Alert, Button, Card, ConfirmDialog, Disclosure, DropdownMenu, MenuItem, Skeleton, Table, TableContainer, ToastService, tableCell, tableHead, tableHeaderCell, tableRow, DateTimePipe, NumberPipe } from '../../shared/ui';
import { countKey } from '../../core/i18n/plural';
import { ProjectContext } from './project-context';
import { ReferencePreviewData, ReferencePreviewDialog } from './reference-preview-dialog';
import { ReferenceUploadData, ReferenceUploadDialog } from './reference-upload-dialog';

/**
 * The project's reference tables: every member lists and previews them; analysts and admins upload, replace and delete. Preview
 * is the one action a row shows; replacing and deleting wait in the row's menu.
 */
@Component({
  selector: 'app-project-reference',
  imports: [DateTimePipe, NumberPipe, TranslocoDirective, Alert, Button, Card, Disclosure, DropdownMenu, Skeleton, Table, TableContainer],
  template: `
    <section class="flex flex-col gap-4" *transloco="let t">
      <div class="flex items-center justify-between gap-3">
        <h2 class="text-lg font-semibold text-heading">{{ t('reference.title') }}</h2>
        @if (canEdit() && tables()?.length) {
          <button tmzButton type="button" class="shrink-0" (click)="openUpload()" data-testid="reference-add">{{ t('reference.add') }}</button>
        }
      </div>

      @if (renamed(); as renamed) {
        <tmz-alert tone="brand" [announce]="true" data-testid="reference-renamed">
          <div class="flex items-start justify-between gap-3">
            <p>{{ t('reference.renamedSummary', { name: renamed.table.name, count: renamed.renamed.length }) }}</p>
            <button tmzButton variant="ghost" size="sm" type="button" class="shrink-0" (click)="dismissRenamed()" data-testid="reference-renamed-dismiss">{{ t('reference.dismiss') }}</button>
          </div>
          <tmz-disclosure class="mt-1" [heading]="t('reference.renamedList')" [open]="renamed.renamed.length <= 5" data-testid="reference-renamed-list">
            <ul class="list-inside list-disc">
              @for (column of renamed.renamed; track column.name) {
                <li>
                  {{ column.header || t('reference.emptyHeader') }} → <code>{{ column.name }}</code>
                </li>
              }
            </ul>
          </tmz-disclosure>
        </tmz-alert>
      }

      @if (tables(); as tables) {
        @if (tables.length === 0) {
          <div class="flex flex-col items-start gap-3" data-testid="reference-empty">
            <p class="text-sm text-body">{{ t(canEdit() ? 'reference.emptyEditor' : 'reference.emptyViewer') }}</p>
            @if (canEdit()) {
              <button tmzButton type="button" (click)="openUpload()" data-testid="reference-add-empty">{{ t('reference.add') }}</button>
            }
          </div>
        } @else {
          <!-- Phones: a name and one line about the table. md and up: a table. -->
          <ul class="flex flex-col gap-3 md:hidden" data-testid="reference-cards">
            @for (table of tables; track table.name) {
              <li class="rounded-base border border-default bg-neutral-primary-soft p-4 text-sm">
                <div class="flex items-start justify-between gap-2">
                  <p class="min-w-0 font-medium text-heading"><code class="break-all">{{ table.name }}</code></p>
                  <div class="flex shrink-0 items-center gap-1">
                    <button tmzButton variant="secondary" size="sm" type="button" (click)="openPreview(table)">{{ t('reference.preview') }}</button>
                    @if (canEdit()) {
                      <tmz-dropdown-menu [label]="t('reference.actionsFor', { name: table.name })" [items]="actions(table)" (selected)="act(table, $event)" />
                    }
                  </div>
                </div>
                <p class="mt-1 break-words text-body">
                  {{ table.fileName }} · {{ t('reference.size', { rows: t(key('reference.rowCount', table.rowCount), { count: (table.rowCount | num) }), columns: t(key('reference.columnCount', table.columns.length), { count: table.columns.length }) }) }} ·
                  {{ t('reference.updated', { name: table.updatedBy.name, date: (table.updatedAt | dateTime) }) }}
                </p>
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
                    <td [class]="tableCell + ' whitespace-nowrap'">{{ t('reference.size', { rows: t(key('reference.rowCount', table.rowCount), { count: (table.rowCount | num) }), columns: t(key('reference.columnCount', table.columns.length), { count: table.columns.length }) }) }}</td>
                    <td [class]="tableCell">{{ t('reference.updated', { name: table.updatedBy.name, date: (table.updatedAt | dateTime) }) }}</td>
                    <td [class]="tableCell">
                      <span class="flex items-center justify-end gap-1">
                        <button tmzButton variant="secondary" size="sm" type="button" (click)="openPreview(table)" [attr.data-testid]="'reference-preview-' + table.name">
                          {{ t('reference.preview') }}
                        </button>
                        @if (canEdit()) {
                          <tmz-dropdown-menu [label]="t('reference.actionsFor', { name: table.name })" [items]="actions(table)" (selected)="act(table, $event)" />
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

  /** The key of a message about a count: its singular form for one. */
  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
  }

  protected readonly canEdit = computed(() => this.context.can('analyst'));
  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  protected readonly tables = signal<ReferenceTable[] | null>(null);
  /** The last upload or replacement, when it renamed columns that the member has not dismissed. */
  protected readonly renamed = signal<ReferenceTableUpload | null>(null);

  constructor() {
    this.load();
  }

  /** Replace and Delete: rarer than looking at a table, so they wait in the row's menu. */
  protected actions(table: ReferenceTable): MenuItem[] {
    const t = (key: string) => this.transloco.translate(key);
    return [
      { id: 'replace', label: t('reference.replace'), testId: 'reference-replace-' + table.name },
      { id: 'delete', label: t('reference.delete'), danger: true, testId: 'reference-delete-' + table.name },
    ];
  }

  protected act(table: ReferenceTable, action: string): void {
    if (action === 'replace') {
      this.openReplace(table);
    } else if (action === 'delete') {
      void this.remove(table);
    }
  }

  protected openUpload(): void {
    this.openUploadDialog({ projectId: this.projectId(), replace: null }, 'reference.uploaded');
  }

  /** Replacing uses the same dialog as uploading, with the name fixed, so a refused file is explained where the file is chosen. */
  protected openReplace(table: ReferenceTable): void {
    this.openUploadDialog({ projectId: this.projectId(), replace: table.name }, 'reference.replaced');
  }

  private openUploadDialog(data: ReferenceUploadData, messageKey: string): void {
    const ref = this.dialog.open<ReferenceTableUpload, ReferenceUploadData>(ReferenceUploadDialog, {
      data,
      ariaLabelledBy: 'reference-upload-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
    ref.closed.subscribe((upload) => {
      if (upload) {
        this.saved(upload, messageKey, data.replace);
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

  protected dismissRenamed(): void {
    this.renamed.set(null);
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

  private saved(upload: ReferenceTableUpload, messageKey: string, replaced: string | null): void {
    this.toasts.success(this.transloco.translate(messageKey, { name: upload.table.name }));
    this.renamed.set(upload.renamed.length && !(replaced && this.sameRenames(upload)) ? upload : null);
    this.load();
  }

  /** A replacement whose file renames the headers the table already had renamed tells nothing new, so it says nothing. */
  private sameRenames(upload: ReferenceTableUpload): boolean {
    const before = this.tables()?.find((table) => table.name === upload.table.name);
    if (!before) {
      return false;
    }
    const known = new Set(before.columns.filter((column) => column.header !== column.name).map((column) => `${column.header}\u0000${column.name}`));
    return known.size === upload.renamed.length && upload.renamed.every((rename) => known.has(`${rename.header}\u0000${rename.name}`));
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
