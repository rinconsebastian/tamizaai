import { HttpErrorResponse } from '@angular/common/http';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { FormField, ImportPreview, ProjectsService } from '../../core/projects/projects.service';
import { Alert, Button, Input } from '../../shared/ui';
import { fieldIssue, guessField } from './sampling-frame-fields';

export interface ImportFrameData {
  projectId: string;
  /** The form's questions a dimension can map to. */
  fields: FormField[];
}

/** What the dialog hands the editor: dimensions mapped to form fields and the file's rows, as the editor holds them. */
export interface ImportedFrame {
  dimensions: { name: string; field: string }[];
  rows: { values: string[]; target: string }[];
}

/** Rows with a problem listed before the rest are counted ("and N more"). */
const shownErrors = 20;

/**
 * Reads a CSV or XLSX file into a preview and maps its dimensions to form fields. Nothing is saved here: the dialog closes with the
 * mapped rows, which the editor loads for review. A refused file or a file with row errors is explained in the dialog.
 */
@Component({
  selector: 'app-import-frame-dialog',
  imports: [TranslocoDirective, Alert, Button, Input],
  template: `
    <div
      class="max-h-[calc(100vh-2rem)] w-[min(36rem,calc(100vw-2rem))] overflow-y-auto rounded-base border border-default bg-neutral-primary-soft p-4 shadow-sm md:p-6"
      *transloco="let t"
    >
      <h2 id="import-frame-title" class="text-lg font-medium text-heading">{{ t('frame.importTitle') }}</h2>
      <p class="mt-2 text-sm text-body">{{ t('frame.importIntro') }}</p>

      <!-- The native file input shows text in the browser's language; a styled label keeps the UI's own text. -->
      <div class="mt-4 flex flex-col gap-2 sm:flex-row sm:items-center">
        <input id="frameFile" type="file" accept=".csv,.xlsx" class="peer sr-only" (change)="importFile($any($event.target))" data-testid="import-file" />
        <label
          for="frameFile"
          class="inline-flex min-h-10 cursor-pointer items-center justify-center rounded-base border border-default-medium bg-neutral-secondary-medium px-4 py-2.5 text-sm font-medium text-heading shadow-xs hover:bg-neutral-tertiary-medium peer-focus-visible:ring-4 peer-focus-visible:ring-neutral-tertiary"
          >{{ t('frame.chooseFile') }}</label
        >
        <span class="break-all text-sm text-body" data-testid="file-name">{{ fileName() ?? t('frame.noFile') }}</span>
      </div>
      @if (importError(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="import-error">{{ message }}</tmz-alert>
      }

      @if (preview(); as current) {
        <div class="mt-5 flex flex-col gap-4 border-t border-default pt-5" data-testid="import-preview">
          <p class="text-sm text-heading">{{ t('frame.previewSummary', { rows: current.rows.length, dimensions: current.dimensions.length }) }}</p>
          @if (current.errors.length) {
            <tmz-alert tone="danger" [heading]="t('frame.previewErrors')" data-testid="import-errors">
              <ul class="mt-1 list-disc ps-5">
                @for (error of current.errors.slice(0, shownErrors); track $index) {
                  <li>{{ t('frame.rowError', { row: error.row, message: error.message }) }}</li>
                }
              </ul>
              @if (current.errors.length > shownErrors) {
                <p class="mt-2" data-testid="import-errors-more">{{ t('frame.moreErrors', { count: current.errors.length - shownErrors }) }}</p>
              }
            </tmz-alert>
          } @else {
            <div class="grid gap-3 sm:grid-cols-2">
              @for (name of current.dimensions; track $index; let i = $index) {
                <label class="block">
                  <span class="mb-1 block text-sm font-medium text-heading">{{ t('frame.mapField', { name }) }}</span>
                  <select tmzInput (change)="map(i, $any($event.target).value)" [attr.data-testid]="'map-' + i">
                    <option value="">{{ t('frame.chooseField') }}</option>
                    @for (field of data.fields; track field.xpath) {
                      <option [value]="field.xpath" [selected]="mapping()[i] === field.xpath">{{ optionLabel(field) }}</option>
                    }
                  </select>
                  @if (warning(i); as message) {
                    <span class="mt-1 block text-sm text-body" [attr.data-testid]="'map-warning-' + i">{{ message }}</span>
                  }
                </label>
              }
            </div>
          }
        </div>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()">{{ t('common.cancel') }}</button>
        @if (preview(); as current) {
          @if (!current.errors.length) {
            <button tmzButton type="button" (click)="apply(current)" data-testid="apply-import">{{ t('frame.applyImport') }}</button>
          }
        }
      </div>
    </div>
  `,
})
export class ImportFrameDialog {
  protected readonly data = inject<ImportFrameData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<ImportedFrame>>(DialogRef);
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);

  protected readonly shownErrors = shownErrors;
  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly mapping = signal<string[]>([]);
  protected readonly importError = signal<string | null>(null);
  protected readonly fileName = signal<string | null>(null);

  protected importFile(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    this.fileName.set(file.name);
    this.importError.set(null);
    this.preview.set(null);
    this.service.importSamplingFrame(this.data.projectId, file).subscribe({
      next: (preview) => {
        this.mapping.set(preview.dimensions.map((name) => guessField(this.data.fields, name)));
        this.preview.set(preview);
      },
      error: (error) => {
        const parsed = this.errors.parse(error as HttpErrorResponse);
        this.importError.set(parsed.fieldErrors['file'] ?? parsed.message);
      },
    });
  }

  protected map(index: number, field: string): void {
    this.mapping.update((fields) => fields.map((current, i) => (i === index ? field : current)));
  }

  /** A question the dashboard cannot count reliably says so in the list and, once chosen, under the select. */
  protected optionLabel(field: FormField): string {
    const issue = fieldIssue(field);
    return `${field.label ?? field.name} (${field.xpath})${issue ? ' ' + this.transloco.translate('frame.issue.' + issue) : ''}`;
  }

  protected warning(index: number): string | null {
    const issue = fieldIssue(this.data.fields.find((field) => field.xpath === this.mapping()[index]));
    return issue ? this.transloco.translate('frame.warning.' + issue) : null;
  }

  protected apply(preview: ImportPreview): void {
    this.ref.close({
      dimensions: preview.dimensions.map((name, i) => ({ name, field: this.mapping()[i] ?? '' })),
      rows: preview.rows.map((row) => ({ values: [...row.values], target: row.target === null ? '' : String(row.target) })),
    });
  }
}
