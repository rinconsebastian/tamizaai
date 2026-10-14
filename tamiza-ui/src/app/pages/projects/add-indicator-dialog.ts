import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { IndicatorMetadata, indicatorNamePattern } from '../../core/projects/pipeline.service';
import { Button, FormField, Input } from '../../shared/ui';
import { IndicatorMetadataFields } from './indicator-metadata-fields';

export interface AddIndicatorData {
  /** The names the project's indicators already have. */
  existing: string[];
}

/** The name and metadata of a new indicator. The indicator exists once its script is saved. */
export interface NewIndicator extends IndicatorMetadata {
  name: string;
}

/** Asks for the name and metadata of a new indicator, checking the name against the identifier rule as it is typed. */
@Component({
  selector: 'app-add-indicator-dialog',
  imports: [TranslocoDirective, Button, FormField, Input, IndicatorMetadataFields],
  template: `
    <form
      class="max-h-[calc(100vh-2rem)] w-[min(36rem,calc(100vw-2rem))] overflow-y-auto rounded-base border border-default bg-neutral-primary-soft p-4 shadow-sm md:p-6"
      novalidate
      (submit)="$event.preventDefault(); submit()"
      *transloco="let t"
    >
      <h2 id="add-indicator-title" class="text-lg font-medium text-heading">{{ t('scripts.addTitle') }}</h2>
      <p class="mt-2 text-sm text-body">{{ t('scripts.addIntro') }}</p>

      <tmz-form-field class="mt-4" [label]="t('scripts.fields.name')" fieldId="indicator-name" [error]="nameError()" [hint]="t('scripts.fields.nameHint')">
        <input
          tmzInput
          id="indicator-name"
          autocomplete="off"
          spellcheck="false"
          [value]="name()"
          (input)="name.set($any($event.target).value)"
          data-testid="indicator-name"
        />
      </tmz-form-field>
      <app-indicator-metadata-fields class="mt-3 block" idPrefix="add" [value]="metadata()" [errors]="errors()" (valueChange)="metadata.set($event)" />

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" data-testid="indicator-add-confirm">{{ t('scripts.addConfirm') }}</button>
      </div>
    </form>
  `,
})
export class AddIndicatorDialog {
  private readonly data = inject<AddIndicatorData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<NewIndicator>>(DialogRef);
  private readonly transloco = inject(TranslocoService);

  protected readonly name = signal('');
  protected readonly metadata = signal<IndicatorMetadata>({ label: '', description: '', unit: '', format: 'decimal' });
  protected readonly errors = signal<Record<string, string>>({});
  private readonly submitted = signal(false);

  /** Shown as soon as something is typed; an empty name is only an error once the form is submitted. */
  protected readonly nameError = computed(() => {
    const name = this.name();
    const t = (key: string) => this.transloco.translate(key);
    if (!name) {
      return this.submitted() ? t('scripts.nameErrors.required') : null;
    }
    if (!indicatorNamePattern.test(name)) {
      return t('scripts.nameErrors.invalid');
    }
    return this.data.existing.includes(name) ? t('scripts.nameErrors.taken') : null;
  });

  protected submit(): void {
    this.submitted.set(true);
    const label = this.metadata().label.trim();
    this.errors.set(label ? {} : { label: this.transloco.translate('scripts.labelRequired') });
    if (!this.name() || this.nameError() || !label) {
      return;
    }
    const { description, unit, format } = this.metadata();
    this.ref.close({ name: this.name(), label, description: description.trim(), unit: unit.trim(), format });
  }
}
