import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { FormField, ImportPreview, ProjectsService, SamplingFrame, SamplingFrameInput } from '../../core/projects/projects.service';
import { Alert, Button, Card, Table, TableContainer, ToastService, tableCell, tableHead, tableHeaderCell, tableRow } from '../../shared/ui';
import { ProjectContext } from './project-context';

interface EditableDimension {
  name: string;
  field: string;
}

interface EditableRow {
  values: string[];
  target: string;
}

const metadataTypes = new Set(['start', 'end', 'today', 'deviceid', 'username', 'audit', 'note']);
const pageSize = 50;
const cellInput =
  'block w-full min-w-28 min-h-10 rounded-base border bg-neutral-secondary-medium px-2.5 py-2 text-sm text-heading focus:border-brand focus:ring-brand';

@Component({
  selector: 'app-project-sampling-frame',
  imports: [TranslocoDirective, Alert, Button, Card, Table, TableContainer],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t">
      @if (!loaded()) {
        <p class="text-sm text-body" aria-busy="true">{{ t('common.loading') }}</p>
      } @else if (!canEdit()) {
        @if (frame(); as frame) {
          <tmz-table-container>
            <table tmzTable data-testid="frame-table">
              <thead [class]="tableHead">
                <tr>
                  @for (dimension of frame.dimensions; track dimension.name) {
                    <th scope="col" [class]="tableHeaderCell">{{ dimension.name }}</th>
                  }
                  <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('frame.target') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (target of frame.targets; track $index) {
                  <tr [class]="tableRow">
                    @for (value of target.values; track $index) {
                      <td [class]="tableCell">{{ value }}</td>
                    }
                    <td [class]="tableCell + ' text-end tabular-nums'">{{ target.target }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </tmz-table-container>
        } @else {
          <tmz-card data-testid="frame-empty"><p class="text-body">{{ t('frame.emptyViewer') }}</p></tmz-card>
        }
      } @else {
        <tmz-card [heading]="t('frame.importTitle')">
          <p class="mb-4 text-sm text-body">{{ t('frame.importIntro') }}</p>
          <!-- The native file input shows text in the browser's language; a styled label keeps the UI's own text. -->
          <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
            <input
              id="frameFile"
              type="file"
              accept=".csv,.xlsx"
              class="peer sr-only"
              (change)="importFile($any($event.target))"
              data-testid="import-file"
            />
            <label
              for="frameFile"
              class="inline-flex min-h-10 cursor-pointer items-center justify-center rounded-base bg-brand px-4 py-2.5 text-sm font-medium text-white shadow-xs hover:bg-brand-strong peer-focus-visible:ring-4 peer-focus-visible:ring-brand-medium"
              >{{ t('frame.chooseFile') }}</label
            >
            <span class="text-sm text-body break-all" data-testid="file-name">{{ fileName() ?? t('frame.noFile') }}</span>
          </div>
          @if (importError(); as message) {
            <tmz-alert tone="danger" class="mt-4">{{ message }}</tmz-alert>
          }
          @if (preview(); as current) {
            <div class="mt-5 flex flex-col gap-4 border-t border-default pt-5" data-testid="import-preview">
              <p class="text-sm text-heading">{{ t('frame.previewSummary', { rows: current.rows.length, dimensions: current.dimensions.length }) }}</p>
              @if (current.errors.length) {
                <tmz-alert tone="danger" [heading]="t('frame.previewErrors')" data-testid="import-errors">
                  <ul class="mt-1 list-disc ps-5">
                    @for (error of current.errors.slice(0, 20); track $index) {
                      <li>{{ t('frame.rowError', { row: error.row, message: error.message }) }}</li>
                    }
                  </ul>
                </tmz-alert>
              } @else {
                <div class="grid gap-3 sm:grid-cols-2">
                  @for (name of current.dimensions; track $index) {
                    <label class="block">
                      <span class="mb-1 block text-sm font-medium text-heading">{{ t('frame.mapField', { name }) }}</span>
                      <select [class]="cellInput + ' border-default-medium'" (change)="mapImported($index, $any($event.target).value)" [attr.data-testid]="'map-' + $index">
                        <option value="">{{ t('frame.chooseField') }}</option>
                        @for (field of fields(); track field.xpath) {
                          <option [value]="field.xpath" [selected]="importFields()[$index] === field.xpath">{{ field.label ?? field.name }} ({{ field.xpath }})</option>
                        }
                      </select>
                    </label>
                  }
                </div>
                <div class="flex flex-col gap-3 sm:flex-row sm:justify-end">
                  <button tmzButton variant="secondary" type="button" (click)="preview.set(null)">{{ t('common.cancel') }}</button>
                  <button tmzButton type="button" (click)="applyImport()" data-testid="apply-import">{{ t('frame.applyImport') }}</button>
                </div>
              }
            </div>
          }
        </tmz-card>

        <tmz-card [heading]="t('frame.dimensionsTitle')">
          <p class="mb-4 text-sm text-body">{{ t('frame.dimensionsIntro') }}</p>
          @if (errors()['dimensions']; as message) {
            <tmz-alert tone="danger" class="mb-4">{{ message }}</tmz-alert>
          }
          <ul class="flex flex-col gap-3">
            @for (dimension of dimensions(); track $index; let i = $index) {
              <li class="grid gap-2 sm:grid-cols-[1fr_1fr_auto] sm:items-start" [attr.data-testid]="'dimension-' + i">
                <div>
                  <input [class]="cellClass('dimensions[' + i + '].name')" [value]="dimension.name" (input)="setDimension(i, 'name', $any($event.target).value)" [attr.aria-label]="t('frame.dimensionName', { n: i + 1 })" />
                  @if (errors()['dimensions[' + i + '].name']; as message) {
                    <p class="mt-1 text-sm text-fg-danger-strong">{{ message }}</p>
                  }
                </div>
                <div>
                  <select [class]="cellClass('dimensions[' + i + '].field')" (change)="setDimension(i, 'field', $any($event.target).value)" [attr.aria-label]="t('frame.dimensionField', { n: i + 1 })">
                    <option value="">{{ t('frame.chooseField') }}</option>
                    @for (field of fields(); track field.xpath) {
                      <option [value]="field.xpath" [selected]="field.xpath === dimension.field">{{ field.label ?? field.name }} ({{ field.xpath }})</option>
                    }
                  </select>
                  @if (errors()['dimensions[' + i + '].field']; as message) {
                    <p class="mt-1 text-sm text-fg-danger-strong">{{ message }}</p>
                  }
                </div>
                <button tmzButton variant="ghost" size="sm" type="button" (click)="removeDimension(i)">{{ t('frame.remove') }}</button>
              </li>
            }
          </ul>
          <button tmzButton variant="secondary" size="sm" class="mt-4" type="button" [disabled]="dimensions().length >= 10" (click)="addDimension()" data-testid="add-dimension">
            {{ t('frame.addDimension') }}
          </button>
        </tmz-card>

        <tmz-card [heading]="t('frame.targetsTitle')">
          @if (errors()['targets']; as message) {
            <tmz-alert tone="danger" class="mb-4">{{ message }}</tmz-alert>
          }
          @if (dimensions().length) {
            <tmz-table-container>
              <table tmzTable data-testid="targets-grid">
                <thead [class]="tableHead">
                  <tr>
                    <th scope="col" [class]="tableHeaderCell">#</th>
                    @for (dimension of dimensions(); track $index) {
                      <th scope="col" [class]="tableHeaderCell">{{ dimension.name || t('frame.unnamed') }}</th>
                    }
                    <th scope="col" [class]="tableHeaderCell">{{ t('frame.target') }}</th>
                    <th scope="col" [class]="tableHeaderCell"><span class="sr-only">{{ t('frame.actions') }}</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of pageRows(); track row.index) {
                    <tr [class]="tableRow">
                      <td [class]="tableCell + ' text-body tabular-nums'">{{ row.index + 1 }}</td>
                      @for (dimension of dimensions(); track $index; let j = $index) {
                        <td class="px-2 py-2">
                          <input [class]="cellClass('targets[' + row.index + '].values')" [value]="row.values[j] ?? ''" (input)="setValue(row.index, j, $any($event.target).value)" [attr.aria-label]="t('frame.cellLabel', { name: dimension.name, row: row.index + 1 })" />
                        </td>
                      }
                      <td class="px-2 py-2">
                        <input [class]="cellClass('targets[' + row.index + '].target') + ' text-end'" inputmode="numeric" [value]="row.target" (input)="setTarget(row.index, $any($event.target).value)" [attr.aria-label]="t('frame.targetLabel', { row: row.index + 1 })" />
                      </td>
                      <td class="px-2 py-2 text-end">
                        <button tmzButton variant="ghost" size="sm" type="button" (click)="removeRow(row.index)" [attr.aria-label]="t('frame.removeRow', { row: row.index + 1 })">×</button>
                      </td>
                    </tr>
                    @if (rowError(row.index); as message) {
                      <tr><td [attr.colspan]="dimensions().length + 3" class="px-4 pb-3 text-sm text-fg-danger-strong">{{ t('frame.rowError', { row: row.index + 1, message }) }}</td></tr>
                    }
                  }
                </tbody>
              </table>
            </tmz-table-container>
            <div class="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <button tmzButton variant="secondary" size="sm" type="button" (click)="addRow()" data-testid="add-row">{{ t('frame.addRow') }}</button>
              @if (rows().length > pageSize) {
                <div class="flex items-center gap-3 text-sm text-body">
                  <button tmzButton variant="ghost" size="sm" type="button" [disabled]="page() === 0" (click)="page.set(page() - 1)">{{ t('frame.previous') }}</button>
                  <span>{{ t('frame.pageInfo', { from: page() * pageSize + 1, to: pageEnd(), total: rows().length }) }}</span>
                  <button tmzButton variant="ghost" size="sm" type="button" [disabled]="pageEnd() >= rows().length" (click)="page.set(page() + 1)">{{ t('frame.next') }}</button>
                </div>
              }
            </div>
          } @else {
            <p class="text-sm text-body">{{ t('frame.addDimensionFirst') }}</p>
          }
        </tmz-card>

        <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-end">
          @if (errorCount(); as count) {
            <p class="text-sm text-fg-danger-strong" role="alert">{{ t('frame.errorCount', { count }) }}</p>
          }
          <button tmzButton type="button" [disabled]="saving()" (click)="save()" data-testid="save-frame">{{ t('frame.save') }}</button>
        </div>
      }
    </div>
  `,
})
export class ProjectSamplingFrame {
  private readonly service = inject(ProjectsService);
  private readonly errorsService = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly context = inject(ProjectContext);

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;
  protected readonly cellInput = cellInput;
  protected readonly pageSize = pageSize;

  protected readonly loaded = signal(false);
  protected readonly frame = signal<SamplingFrame | null>(null);
  protected readonly dimensions = signal<EditableDimension[]>([]);
  protected readonly rows = signal<EditableRow[]>([]);
  protected readonly page = signal(0);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly saving = signal(false);
  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly importFields = signal<string[]>([]);
  protected readonly importError = signal<string | null>(null);
  protected readonly fileName = signal<string | null>(null);

  protected readonly canEdit = computed(() => this.context.can('analyst'));
  protected readonly fields = computed<FormField[]>(() => (this.context.formFields() ?? []).filter((f) => !metadataTypes.has(f.type)));
  protected readonly pageRows = computed(() =>
    this.rows()
      .slice(this.page() * pageSize, (this.page() + 1) * pageSize)
      .map((row, offset) => ({ ...row, index: this.page() * pageSize + offset })),
  );
  protected readonly pageEnd = computed(() => Math.min((this.page() + 1) * pageSize, this.rows().length));
  protected readonly errorCount = computed(() => Object.keys(this.errors()).length);

  constructor() {
    this.context.loadFormFields();
    this.service.samplingFrame(this.context.project()!.id).subscribe({
      next: (frame) => {
        this.setFrame(frame ?? null);
        this.loaded.set(true);
      },
      error: (error) => {
        this.errorsService.report(error);
        this.loaded.set(true);
      },
    });
  }

  protected cellClass(path: string): string {
    return `${cellInput} ${this.errors()[path] ? 'border-danger-subtle bg-danger-soft' : 'border-default-medium'}`;
  }

  protected rowError(index: number): string | null {
    const e = this.errors();
    return e[`targets[${index}]`] ?? e[`targets[${index}].values`] ?? e[`targets[${index}].target`] ?? null;
  }

  protected addDimension(): void {
    this.dimensions.update((d) => [...d, { name: '', field: '' }]);
    this.rows.update((rows) => rows.map((r) => ({ ...r, values: [...r.values, ''] })));
  }

  protected removeDimension(index: number): void {
    this.dimensions.update((d) => d.filter((_, i) => i !== index));
    this.rows.update((rows) => rows.map((r) => ({ ...r, values: r.values.filter((_, i) => i !== index) })));
  }

  protected setDimension(index: number, key: keyof EditableDimension, value: string): void {
    this.dimensions.update((d) => d.map((dim, i) => (i === index ? { ...dim, [key]: value } : dim)));
  }

  protected addRow(): void {
    this.rows.update((rows) => [...rows, { values: this.dimensions().map(() => ''), target: '' }]);
    this.page.set(Math.floor((this.rows().length - 1) / pageSize));
  }

  protected removeRow(index: number): void {
    this.rows.update((rows) => rows.filter((_, i) => i !== index));
    if (this.page() * pageSize >= this.rows().length && this.page() > 0) {
      this.page.set(this.page() - 1);
    }
  }

  protected setValue(rowIndex: number, dimensionIndex: number, value: string): void {
    this.rows.update((rows) => rows.map((r, i) => (i === rowIndex ? { ...r, values: r.values.map((v, j) => (j === dimensionIndex ? value : v)) } : r)));
  }

  protected setTarget(rowIndex: number, value: string): void {
    this.rows.update((rows) => rows.map((r, i) => (i === rowIndex ? { ...r, target: value } : r)));
  }

  protected save(): void {
    const body: SamplingFrameInput = {
      dimensions: this.dimensions(),
      targets: this.rows().map((r) => ({ values: r.values, target: r.target.trim() === '' ? null : Number(r.target) })),
    };
    this.saving.set(true);
    this.errors.set({});
    this.service.saveSamplingFrame(this.context.project()!.id, body).subscribe({
      next: (frame) => {
        this.setFrame(frame);
        this.saving.set(false);
        this.toasts.success(this.transloco.translate('frame.saved'));
      },
      error: (error) => {
        this.errors.set(this.errorsService.report(error).fieldErrors);
        this.saving.set(false);
      },
    });
  }

  protected importFile(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    this.fileName.set(file.name);
    this.importError.set(null);
    this.preview.set(null);
    this.service.importSamplingFrame(this.context.project()!.id, file).subscribe({
      next: (preview) => {
        this.importFields.set(preview.dimensions.map((name) => this.guessField(name)));
        this.preview.set(preview);
      },
      error: (error) => {
        const parsed = this.errorsService.parse(error as HttpErrorResponse);
        this.importError.set(parsed.fieldErrors['file'] ?? parsed.message);
      },
    });
  }

  protected mapImported(index: number, field: string): void {
    this.importFields.update((fields) => fields.map((f, i) => (i === index ? field : f)));
  }

  /** Loads the preview into the editor; nothing is saved until the user presses Save. */
  protected applyImport(): void {
    const preview = this.preview();
    if (!preview) {
      return;
    }
    this.dimensions.set(preview.dimensions.map((name, i) => ({ name, field: this.importFields()[i] ?? '' })));
    this.rows.set(preview.rows.map((r) => ({ values: [...r.values], target: r.target === null ? '' : String(r.target) })));
    this.page.set(0);
    this.errors.set({});
    this.preview.set(null);
    this.toasts.info(this.transloco.translate('frame.importApplied'));
  }

  private setFrame(frame: SamplingFrame | null): void {
    this.frame.set(frame);
    this.dimensions.set(frame ? frame.dimensions.map((d) => ({ ...d })) : []);
    this.rows.set(frame ? frame.targets.map((t) => ({ values: [...t.values], target: String(t.target) })) : []);
    this.page.set(0);
  }

  private guessField(name: string): string {
    const wanted = name.trim().toLowerCase();
    return this.fields().find((f) => [f.name, f.xpath, f.label ?? ''].some((v) => v.toLowerCase() === wanted))?.xpath ?? '';
  }
}
