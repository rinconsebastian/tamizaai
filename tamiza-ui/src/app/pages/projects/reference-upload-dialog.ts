import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { ReferenceService, ReferenceTableUpload, maxReferenceFileBytes, suggestedTableName } from '../../core/projects/reference.service';
import { Alert, Button, FormField, Input } from '../../shared/ui';

export interface ReferenceUploadData {
  projectId: string;
}

/** Chooses a CSV or XLSX file and a name, and uploads it as a new reference table. Closes with the upload result. */
@Component({
  selector: 'app-reference-upload-dialog',
  imports: [TranslocoDirective, Alert, Button, FormField, Input],
  template: `
    <form
      class="w-[min(32rem,calc(100vw-2rem))] rounded-base border border-default bg-neutral-primary-soft p-4 shadow-sm md:p-6"
      novalidate
      (submit)="$event.preventDefault(); submit()"
      *transloco="let t"
    >
      <h2 id="reference-upload-title" class="text-lg font-medium text-heading">{{ t('reference.uploadTitle') }}</h2>
      <p class="mt-2 text-sm text-body">{{ t('reference.uploadIntro') }}</p>

      <div class="mt-4 flex flex-col gap-2 sm:flex-row sm:items-center">
        <label tmzButton variant="secondary" class="cursor-pointer">
          {{ t('reference.chooseFile') }}
          <input type="file" accept=".csv,.xlsx" class="sr-only" (change)="choose($any($event.target))" data-testid="reference-file" />
        </label>
        <span class="break-all text-sm text-body" data-testid="reference-file-name">{{ file()?.name ?? t('frame.noFile') }}</span>
      </div>
      @if (fileError(); as message) {
        <p class="mt-2 text-sm text-fg-danger-strong" data-testid="reference-file-error">{{ message }}</p>
      }

      <tmz-form-field class="mt-4" [label]="t('reference.name')" fieldId="referenceName" [error]="nameError()" [hint]="t('reference.nameHint')">
        <input tmzInput id="referenceName" autocomplete="off" [value]="name()" (input)="name.set($any($event.target).value)" data-testid="reference-name" />
      </tmz-form-field>

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="reference-upload-error">{{ message }}</tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" [disabled]="busy()" data-testid="reference-upload">{{ t('reference.upload') }}</button>
      </div>
    </form>
  `,
})
export class ReferenceUploadDialog {
  protected readonly data = inject<ReferenceUploadData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<ReferenceTableUpload>>(DialogRef);
  private readonly service = inject(ReferenceService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);

  protected readonly file = signal<File | null>(null);
  protected readonly name = signal('');
  protected readonly busy = signal(false);
  protected readonly fileError = signal<string | null>(null);
  protected readonly nameError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected choose(input: HTMLInputElement): void {
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) {
      return;
    }
    this.file.set(file);
    this.fileError.set(null);
    if (!this.name()) {
      this.name.set(suggestedTableName(file.name));
    }
  }

  protected submit(): void {
    const file = this.file();
    this.fileError.set(null);
    this.nameError.set(null);
    this.error.set(null);
    if (!file) {
      this.fileError.set(this.transloco.translate('reference.fileRequired'));
      return;
    }
    if (file.size > maxReferenceFileBytes) {
      this.fileError.set(this.transloco.translate('reference.fileTooLarge'));
      return;
    }
    this.busy.set(true);
    this.service.upload(this.data.projectId, this.name().trim(), file).subscribe({
      next: (upload) => this.ref.close(upload),
      error: (error) => {
        this.busy.set(false);
        const parsed = this.errors.parse(error);
        this.fileError.set(parsed.fieldErrors['file'] ?? null);
        this.nameError.set(parsed.fieldErrors['name'] ?? null);
        if (!parsed.fieldErrors['file'] && !parsed.fieldErrors['name']) {
          this.error.set(parsed.message);
        }
      },
    });
  }
}
