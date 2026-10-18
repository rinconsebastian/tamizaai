import { HttpErrorResponse } from '@angular/common/http';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, DestroyRef, LOCALE_ID, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { FormChoice, FormField, IgnoredColumn, ImportPreview, ProjectsService, SamplingDimension } from '../../core/projects/projects.service';
import { Alert, ApiLang, Button, Disclosure, Input, numberText } from '../../shared/ui';
import { FrameFileGuide } from './frame-file-guide';
import { FieldGuess, fieldIssue, guessField } from './sampling-frame-fields';
import { isSingular, pluralKey } from '../../core/i18n/plural';

export interface ImportFrameData {
  projectId: string;
  /** The form's questions a dimension can map to. */
  fields: FormField[];
  /** The form's fields, unfiltered, and its choices when loaded: the guide's example is built from them. */
  snapshot: FormField[] | null;
  choices: FormChoice[] | null;
  /** The saved frame's dimensions, which preselect the question of a column of the same name; empty without a frame. */
  savedDimensions: SamplingDimension[];
}

/** What the dialog hands the editor: dimensions mapped to form fields and the file's rows, as the editor holds them. */
export interface ImportedFrame {
  dimensions: { name: string; field: string }[];
  rows: { values: string[]; target: string }[];
}

/** The dialog closes with the imported rows, or with the user's request for a template made from the form's questions. */
export type ImportFrameResult = ({ kind: 'rows' } & ImportedFrame) | { kind: 'template' };

/** Stands for a text written by the API inside a translated message, so the message can be cut around it. */
export const detailMark = '\uE000';

/** A message with a text of the API in the middle: what the UI wrote before and after it. */
export interface AroundDetail {
  before: string;
  after: string;
}

/** Cuts a translated message built with `detailMark` as the text of the API into what comes before and after it. */
export function aroundDetail(translated: string): AroundDetail {
  const at = translated.indexOf(detailMark);
  return at < 0 ? { before: translated, after: '' } : { before: translated.slice(0, at), after: translated.slice(at + detailMark.length) };
}

/** Rows with a problem listed before the rest are counted ("and N more"). */
const shownErrors = 20;

/** Ranges of left-out rows listed before the rest are counted. */
const shownRanges = 20;

/**
 * The row numbers as ranges of consecutive numbers ("2–41", "43"), the first `limit` of them, and how many rows the rest hold.
 */
export function rowRanges(rows: number[], limit = shownRanges): { ranges: [number, number][]; more: number } {
  const ranges: [number, number][] = [];
  for (const row of [...rows].sort((a, b) => a - b)) {
    const last = ranges.at(-1);
    if (last && row === last[1] + 1) {
      last[1] = row;
    } else if (!last || row > last[1]) {
      ranges.push([row, row]);
    }
  }
  const more = ranges.slice(limit).reduce((sum, [from, to]) => sum + to - from + 1, 0);
  return { ranges: ranges.slice(0, limit), more };
}

/**
 * Reads a CSV or XLSX file into a preview and maps its dimensions to form fields. Nothing is saved here: the dialog closes with the
 * mapped rows, which the editor loads for review. A refused file or a file with row errors is explained in the dialog, and what the
 * import left out (rows without a target, note columns) is listed. Full screen on phones, a panel from `md` up.
 */
@Component({
  selector: 'app-import-frame-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, Disclosure, FrameFileGuide, Input],
  template: `
    <div
      class="flex h-dvh w-screen flex-col overflow-y-auto bg-neutral-primary-soft p-4 md:h-auto md:max-h-[calc(100dvh-2rem)] md:w-[min(40rem,calc(100vw-2rem))] md:rounded-base md:border md:border-default md:p-6 md:shadow-sm"
      *transloco="let t"
      data-testid="import-dialog"
    >
      <h2 id="import-frame-title" class="text-lg font-medium text-heading">{{ t('frame.importTitle') }}</h2>
      <p class="mt-2 text-sm text-body">{{ t('frame.importIntro') }}</p>

      <tmz-disclosure class="mt-4" [heading]="t('frame.guide.heading')" [open]="!data.savedDimensions.length" data-testid="import-guide">
        <app-frame-file-guide [fields]="data.snapshot" [choices]="data.choices" [templateLink]="true" (template)="ref.close({ kind: 'template' })" />
      </tmz-disclosure>

      <!-- The native file input shows text in the browser's language; a styled label keeps the UI's own text. -->
      <div class="mt-4 flex flex-col gap-2 sm:flex-row sm:items-center">
        <input id="frameFile" type="file" accept=".csv,.xlsx" class="peer sr-only" (change)="importFile($any($event.target))" data-testid="import-file" />
        <label
          for="frameFile"
          class="inline-flex min-h-10 cursor-pointer items-center justify-center rounded-base border border-default-medium bg-neutral-secondary-medium px-4 py-2.5 text-sm font-medium text-heading shadow-xs hover:bg-neutral-tertiary-medium peer-focus-visible:ring-4 peer-focus-visible:ring-neutral-tertiary"
          [attr.aria-busy]="reading() || null"
          data-testid="import-file-button"
          >{{ reading() ? t('frame.preview.reading') : t('frame.chooseFile') }}</label
        >
        <span class="break-all text-sm text-body" data-testid="file-name">{{ fileName() ?? t('frame.noFile') }}</span>
      </div>
      @if (importError(); as message) {
        <tmz-alert tone="danger" class="mt-4" [tmzApiLang]="message" data-testid="import-error">{{ message }}</tmz-alert>
      }

      <!-- Only the counts are announced; the rows and columns they refer to are listed below. -->
      <div aria-live="polite" class="mt-5 flex flex-col gap-1 text-sm text-heading empty:hidden" data-testid="import-live">
        @if (preview(); as current) {
          <p data-testid="import-summary">{{ summary(current) }}</p>
          @if (current.rowsWithoutTarget.length; as count) {
            <p data-testid="left-out-count">{{ plural('frame.preview.rowsWithoutTarget', count) }}</p>
          }
          @if (current.ignoredColumns.length; as count) {
            <p data-testid="ignored-count">{{ plural('frame.preview.ignored', count) }}</p>
          }
        }
      </div>

      @if (preview(); as current) {
        <div class="mt-4 flex flex-col gap-4 border-t border-default pt-4" data-testid="import-preview">
          @if (current.rowsWithoutTarget.length || current.ignoredColumns.length) {
            <tmz-alert tone="neutral" data-testid="import-left-out">
              @if (current.rowsWithoutTarget.length) {
                <p data-testid="left-out-rows">{{ leftOutRows(current.rowsWithoutTarget) }}</p>
              }
              @if (current.ignoredColumns.length) {
                <ul class="mt-1 list-disc ps-5" data-testid="ignored-columns">
                  @for (column of current.ignoredColumns; track column.column) {
                    <li>{{ ignoredText(column) }}</li>
                  }
                </ul>
              }
            </tmz-alert>
          }
          @if (current.errors.length) {
            <tmz-alert tone="danger" [heading]="t('frame.previewErrors')" data-testid="import-errors">
              <ul class="mt-1 list-disc ps-5">
                @for (error of current.errors.slice(0, shownErrors); track $index) {
                  @let around = rowErrorAround(error.row);
                  <li>{{ around.before }}<span lang="en">{{ error.message }}</span>{{ around.after }}</li>
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
                  @if (!mapping()[i] && guesses()[i]?.conflict; as conflict) {
                    <span class="mt-1 block text-sm text-body" [attr.data-testid]="'map-conflict-' + i">{{ t('frame.preview.mappingConflict', conflict) }}</span>
                  } @else if (warning(i); as message) {
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
  protected readonly ref = inject<DialogRef<ImportFrameResult>>(DialogRef);
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  protected readonly shownErrors = shownErrors;
  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly mapping = signal<string[]>([]);
  protected readonly guesses = signal<FieldGuess[]>([]);
  protected readonly importError = signal<string | null>(null);
  protected readonly fileName = signal<string | null>(null);
  protected readonly reading = signal(false);

  /** The read of the file chosen last; choosing another file cancels it, so a late answer never replaces a newer one. */
  private reader: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.reader?.unsubscribe());
  }

  protected importFile(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    this.reader?.unsubscribe();
    this.fileName.set(file.name);
    this.importError.set(null);
    this.preview.set(null);
    this.reading.set(true);
    this.reader = this.service.importSamplingFrame(this.data.projectId, file).subscribe({
      next: (response) => {
        // An API older than the left-out lists answers without them.
        const preview: ImportPreview = { ...response, rowsWithoutTarget: response.rowsWithoutTarget ?? [], ignoredColumns: response.ignoredColumns ?? [] };
        const guesses = preview.dimensions.map((name) => guessField(this.data.fields, name, this.data.savedDimensions));
        this.guesses.set(guesses);
        this.mapping.set(guesses.map((guess) => guess.field));
        this.preview.set(preview);
        this.reading.set(false);
      },
      error: (error) => {
        const parsed = this.errors.parse(error as HttpErrorResponse);
        this.importError.set(parsed.fieldErrors['file'] ?? parsed.message);
        this.reading.set(false);
      },
    });
  }

  /** "Row 3: " and what follows the row's message, which is written by the API and sits between them. */
  protected rowErrorAround(row: number): AroundDetail {
    return aroundDetail(this.transloco.translate('frame.rowError', { row, message: detailMark }));
  }

  protected map(index: number, field: string): void {
    this.mapping.update((fields) => fields.map((current, i) => (i === index ? field : current)));
  }

  protected summary(preview: ImportPreview): string {
    const dimensions = this.plural('frame.dimensions', preview.dimensions.length);
    return this.plural('frame.preview.summary', preview.rows.length, { dimensions });
  }

  protected leftOutRows(rows: number[]): string {
    const { ranges, more } = rowRanges(rows);
    const text = ranges.map(([from, to]) => (from === to ? this.format(from) : `${this.format(from)}–${this.format(to)}`)).join(', ');
    const listed = this.transloco.translate(isSingular(rows.length, this.transloco.getActiveLang()) ? 'frame.preview.rowRange' : 'frame.preview.rowRanges', { ranges: text });
    return more ? `${listed} ${this.plural('frame.preview.moreRows', more)}` : listed;
  }

  protected ignoredText(column: IgnoredColumn): string {
    return column.reason === 'note'
      ? this.transloco.translate('frame.preview.ignoredNote', { column: column.column, name: column.name })
      : this.transloco.translate('frame.preview.ignoredUnnamed', { column: column.column });
  }

  /** The `One` or `Many` form of a message, with the count formatted for the locale. */
  protected plural(key: string, count: number, params: Record<string, unknown> = {}): string {
    return this.transloco.translate(pluralKey(key, count, this.transloco.getActiveLang()), { ...params, count: this.format(count) });
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
      kind: 'rows',
      dimensions: preview.dimensions.map((name, i) => ({ name, field: this.mapping()[i] ?? '' })),
      rows: preview.rows.map((row) => ({ values: [...row.values], target: row.target === null ? '' : String(row.target) })),
    });
  }

  private format(value: number): string {
    return numberText(value, this.locale) ?? String(value);
  }
}
