import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { IndicatorFormat, IndicatorMetadata, indicatorFormats } from '../../core/projects/pipeline.service';
import { FormField, Input } from '../../shared/ui';

const maxLabelLength = 120;
const maxDescriptionLength = 1000;
const maxUnitLength = 32;

/** The label, description, unit and format of an indicator, in the add dialog and above the script's editor. */
@Component({
  selector: 'app-indicator-metadata-fields',
  imports: [TranslocoDirective, FormField, Input],
  template: `
    <div class="grid gap-3 sm:grid-cols-2" *transloco="let t">
      <tmz-form-field class="sm:col-span-2" [label]="t('scripts.fields.label')" [fieldId]="idPrefix() + '-label'" [error]="errors()['label']">
        <input
          tmzInput
          [id]="idPrefix() + '-label'"
          autocomplete="off"
          [attr.maxlength]="maxLabelLength"
          [readOnly]="readonly()"
          [value]="value().label"
          (input)="change({ label: $any($event.target).value })"
          data-testid="meta-label"
        />
      </tmz-form-field>
      <tmz-form-field class="sm:col-span-2" [label]="t('scripts.fields.description')" [fieldId]="idPrefix() + '-description'" [error]="errors()['description']">
        <textarea
          tmzInput
          rows="2"
          [id]="idPrefix() + '-description'"
          [attr.maxlength]="maxDescriptionLength"
          [readOnly]="readonly()"
          [value]="value().description"
          (input)="change({ description: $any($event.target).value })"
          data-testid="meta-description"
        ></textarea>
      </tmz-form-field>
      <tmz-form-field [label]="t('scripts.fields.unit')" [fieldId]="idPrefix() + '-unit'" [error]="errors()['unit']" [hint]="t('scripts.fields.unitHint')">
        <input
          tmzInput
          [id]="idPrefix() + '-unit'"
          autocomplete="off"
          [attr.maxlength]="maxUnitLength"
          [readOnly]="readonly()"
          [value]="value().unit"
          (input)="change({ unit: $any($event.target).value })"
          data-testid="meta-unit"
        />
      </tmz-form-field>
      <tmz-form-field [label]="t('scripts.fields.format')" [fieldId]="idPrefix() + '-format'" [error]="errors()['format']" [hint]="t('scripts.formatHints.' + value().format)">
        <select
          tmzInput
          [id]="idPrefix() + '-format'"
          [disabled]="readonly()"
          (change)="change({ format: $any($event.target).value })"
          data-testid="meta-format"
        >
          @for (format of formats; track format) {
            <option [value]="format" [selected]="format === value().format">{{ t('scripts.formats.' + format) }}</option>
          }
        </select>
      </tmz-form-field>
    </div>
  `,
})
export class IndicatorMetadataFields {
  readonly value = input.required<IndicatorMetadata>();
  readonly readonly = input(false);
  /** Messages by field: label, description, unit and format. */
  readonly errors = input<Record<string, string>>({});
  /** Keeps the ids of two forms on one screen apart. */
  readonly idPrefix = input('meta');
  readonly valueChange = output<IndicatorMetadata>();

  protected readonly formats: IndicatorFormat[] = indicatorFormats;
  protected readonly maxLabelLength = maxLabelLength;
  protected readonly maxDescriptionLength = maxDescriptionLength;
  protected readonly maxUnitLength = maxUnitLength;

  protected change(patch: Partial<IndicatorMetadata>): void {
    this.valueChange.emit({ ...this.value(), ...patch });
  }
}
