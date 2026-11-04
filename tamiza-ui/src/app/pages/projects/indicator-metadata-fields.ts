import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { IndicatorFormat, IndicatorMetadata, indicatorFormats } from '../../core/projects/pipeline.service';
import { ApiLang, FormField, Input } from '../../shared/ui';
import { maxColumnLabel } from '../../core/projects/indicators.service';

const maxLabelLength = 120;
const maxDescriptionLength = 1000;
const maxUnitLength = 32;

/** One row of the Column names section: a column of the table, or one that only has a saved label. */
interface ColumnRow {
  column: string;
  label: string;
  /** The column has a saved label but is not in the table the form lists. */
  stale: boolean;
}

/**
 * The label, description, unit and format of an indicator, in the add dialog and above the script's editor, and under them its
 * **Column names** (add-indicator-column-metadata, design D8): one row per column of the table, with the facts the indicator keeps
 * about it (today the name shown for the column; later changes add their fields to the row).
 */
@Component({
  selector: 'app-indicator-metadata-fields',
  imports: [TranslocoDirective, ApiLang, FormField, Input],
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

      <section class="sm:col-span-2" [attr.aria-labelledby]="idPrefix() + '-columns-title'" data-testid="meta-columns">
        <h3 [id]="idPrefix() + '-columns-title'" class="text-sm font-medium text-heading">{{ t('scripts.columns.title') }}</h3>
        <p class="mt-1 text-sm text-body">{{ t('scripts.columns.hint') }}</p>
        @if (errors()['columns']; as error) {
          <p class="mt-2 text-sm text-fg-danger-strong" data-testid="columns-error" [tmzApiLang]="error">{{ error }}</p>
        }
        @if (rows().length === 0) {
          <p class="mt-2 text-sm text-body" data-testid="columns-empty">{{ t('scripts.columns.afterTest') }}</p>
        } @else {
          <div class="mt-3 grid gap-3 sm:grid-cols-2">
            @for (row of rows(); track row.column) {
              <tmz-form-field
                [label]="row.column"
                [fieldId]="idPrefix() + '-column-' + row.column"
                [error]="errors()['columns.' + row.column + '.label']"
                [hint]="row.stale ? t('scripts.columns.notInTable') : null"
              >
                <input
                  tmzInput
                  [id]="idPrefix() + '-column-' + row.column"
                  autocomplete="off"
                  [attr.maxlength]="maxColumnLabel"
                  [readOnly]="readonly()"
                  [value]="row.label"
                  [attr.data-column]="row.column"
                  (input)="setLabel(row.column, $any($event.target).value)"
                  data-testid="meta-column-label"
                />
              </tmz-form-field>
            }
          </div>
        }
      </section>
    </div>
  `,
})
export class IndicatorMetadataFields {
  readonly value = input.required<IndicatorMetadata>();
  readonly readonly = input(false);
  /**
   * The columns of the indicator's table to offer a row for, in the table's order: the latest test's, else the published table's.
   * A column with a saved label that this list lacks follows them, marked as not in the table.
   */
  readonly columns = input<readonly string[]>([]);
  /** Messages by field: label, description, unit and format, then `columns` and `columns.{column}.label`. */
  readonly errors = input<Record<string, string>>({});
  /** Keeps the ids of two forms on one screen apart. */
  readonly idPrefix = input('meta');
  readonly valueChange = output<IndicatorMetadata>();

  protected readonly formats: IndicatorFormat[] = indicatorFormats;
  protected readonly maxLabelLength = maxLabelLength;
  protected readonly maxDescriptionLength = maxDescriptionLength;
  protected readonly maxUnitLength = maxUnitLength;
  protected readonly maxColumnLabel = maxColumnLabel;

  protected readonly rows = computed<ColumnRow[]>(() => {
    const drafts = this.value().columns;
    const listed = this.columns();
    const stale = Object.keys(drafts).filter((column) => !listed.includes(column) && drafts[column].label.trim());
    return [...listed.map((column) => ({ column, label: drafts[column]?.label ?? '', stale: false })), ...stale.map((column) => ({ column, label: drafts[column].label, stale: true }))];
  });

  protected setLabel(column: string, label: string): void {
    this.change({ columns: { ...this.value().columns, [column]: { label } } });
  }

  protected change(patch: Partial<IndicatorMetadata>): void {
    this.valueChange.emit({ ...this.value(), ...patch });
  }
}
