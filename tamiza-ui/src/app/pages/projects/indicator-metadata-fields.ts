import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ColumnDraft, IndicatorFormat, IndicatorMetadata, indicatorFormats } from '../../core/projects/pipeline.service';
import { ApiLang, Disclosure, FormField, Input } from '../../shared/ui';
import { maxColumnLabel } from '../../core/projects/indicators.service';
import { emptyDraft } from './column-names';
import { ValueOrderEditor } from './indicators/value-order-editor';

const maxLabelLength = 120;
const maxDescriptionLength = 1000;
const maxUnitLength = 32;

/** One row of the Column names section: a column of the table, or one that only has a saved label. */
interface ColumnRow {
  column: string;
  label: string;
  /** The column has a saved label, order, value kept last or prefix choice but is not in the table the form lists. */
  stale: boolean;
  draft: ColumnDraft;
}

/**
 * The label, description, unit and format of an indicator, in the add dialog and above the script's editor, and under them its
 * **Column names** (add-indicator-column-metadata, design D8): one row per column of the table, with the facts the indicator keeps
 * about it (today the name shown for the column; later changes add their fields to the row).
 */
@Component({
  selector: 'app-indicator-metadata-fields',
  imports: [TranslocoDirective, ApiLang, Disclosure, FormField, Input, ValueOrderEditor],
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
              <tmz-disclosure class="sm:col-span-1" [heading]="t('scripts.columns.order.title')" [status]="summary(row.draft, t)" [open]="row.stale || hasError(row.column)" [attr.data-column]="row.column" data-testid="meta-column-order">
                <div class="flex flex-col gap-4 pt-2">
                  @if (readonly()) {
                    <p class="text-sm text-body">{{ summary(row.draft, t) }}</p>
                  } @else {
                    <div class="flex flex-col gap-2">
                      <p class="text-sm font-medium text-heading">{{ t('scripts.columns.order.values') }}</p>
                      <app-value-order-editor [values]="row.draft.order" [offered]="offered()[row.column] ?? []" (changed)="setList(row.column, 'order', $event)" [testId]="'order-editor-' + row.column" />
                      @if (errorOf(row.column, 'order'); as error) {
                        <p class="text-sm text-fg-danger-strong" [tmzApiLang]="error" data-testid="order-error">{{ error }}</p>
                      }
                    </div>
                    <div class="flex flex-col gap-2">
                      <p class="text-sm font-medium text-heading">{{ t('scripts.columns.order.last') }}</p>
                      <p class="text-sm text-body">{{ t('scripts.columns.order.lastHelp') }}</p>
                      <app-value-order-editor [values]="row.draft.last" [offered]="offered()[row.column] ?? []" (changed)="setList(row.column, 'last', $event)" [testId]="'last-editor-' + row.column" />
                      @if (errorOf(row.column, 'last'); as error) {
                        <p class="text-sm text-fg-danger-strong" [tmzApiLang]="error" data-testid="last-error">{{ error }}</p>
                      }
                    </div>
                    <label class="flex min-h-10 cursor-pointer items-start gap-3 text-sm">
                      <input type="checkbox" class="mt-0.5 size-4 shrink-0" [checked]="row.draft.stripPrefix" (change)="setStrip(row.column, $any($event.target).checked)" data-testid="meta-column-strip" />
                      <span class="min-w-0 flex-1 break-words text-heading">{{ t('scripts.columns.order.strip') }}</span>
                    </label>
                    <p class="text-sm text-body">{{ t('scripts.columns.order.note') }}</p>
                  }
                </div>
              </tmz-disclosure>
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
  /**
   * The values of each column the latest test saw (the test's dashboard view lists them for a column of up to 50 values), offered when an
   * order is typed; any other value can be typed.
   */
  readonly offered = input<Record<string, readonly string[]>>({});
  /** Messages by field: label, description, unit and format, then `columns`, `columns.{column}.label`, `.order` and `.last` (an item on its `[i]`). */
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
    // A column the table lacks stays on the form while it has any saved fact, so a save from the form never drops it unseen.
    const stale = Object.keys(drafts).filter((column) => !listed.includes(column) && this.says(drafts[column]));
    return [
      ...listed.map((column) => ({ column, label: drafts[column]?.label ?? '', stale: false, draft: drafts[column] ?? emptyDraft() })),
      ...stale.map((column) => ({ column, label: drafts[column].label, stale: true, draft: drafts[column] })),
    ];
  });

  private says(draft: ColumnDraft | undefined): boolean {
    return !!draft && (!!draft.label.trim() || draft.order.length > 0 || draft.last.length > 0 || draft.stripPrefix);
  }

  private draftOf(column: string): ColumnDraft {
    return this.value().columns[column] ?? emptyDraft();
  }

  protected setLabel(column: string, label: string): void {
    this.update(column, { label });
  }

  protected setList(column: string, list: 'order' | 'last', values: string[]): void {
    this.update(column, { [list]: values });
  }

  protected setStrip(column: string, on: boolean): void {
    this.update(column, { stripPrefix: on });
  }

  private update(column: string, patch: Partial<ColumnDraft>): void {
    this.change({ columns: { ...this.value().columns, [column]: { ...this.draftOf(column), ...patch } } });
  }

  /** What a column's order says in a few words, for the closed disclosure: how many values are ordered or kept at the end, and the prefixes. */
  protected summary(draft: ColumnDraft, t: (key: string, params?: Record<string, unknown>) => string): string {
    const parts = [
      ...(draft.order.length ? [t('scripts.columns.order.summaryOrdered', { count: draft.order.length })] : []),
      ...(draft.last.length ? [t('scripts.columns.order.summaryLast', { count: draft.last.length })] : []),
      ...(draft.stripPrefix ? [t('scripts.columns.order.summaryStrip')] : []),
    ];
    return parts.length ? parts.join(' · ') : t('scripts.columns.order.summaryNone');
  }

  /** The first refusal of a column's order or values kept last: of the list itself or of one of its values. */
  protected errorOf(column: string, list: 'order' | 'last'): string | null {
    const errors = this.errors();
    const own = `columns.${column}.${list}`;
    const key = Object.keys(errors).find((candidate) => candidate === own || candidate.startsWith(`${own}[`));
    return key ? errors[key] : null;
  }

  protected hasError(column: string): boolean {
    return !!this.errorOf(column, 'order') || !!this.errorOf(column, 'last');
  }

  protected change(patch: Partial<IndicatorMetadata>): void {
    this.valueChange.emit({ ...this.value(), ...patch });
  }
}
