import { Dialog } from '@angular/cdk/dialog';
import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { FormField, ProjectsService, SamplingFrame, SamplingFrameInput } from '../../core/projects/projects.service';
import { Alert, Button, Card, ConfirmDialog, Skeleton, Table, TableContainer, ToastService, dateTimeFormat, tableCell, tableHead, tableHeaderCell, tableRow } from '../../shared/ui';
import { ImportFrameData, ImportFrameDialog, ImportedFrame } from './import-frame-dialog';
import { ProjectContext } from './project-context';
import { fieldIssue, frameFields } from './sampling-frame-fields';

interface EditableDimension {
  name: string;
  field: string;
}

interface EditableRow {
  values: string[];
  target: string;
}

const pageSize = 50;
const cellInput =
  'block w-full min-w-28 min-h-10 rounded-base border bg-neutral-secondary-medium px-2.5 py-2 text-sm text-heading focus:border-brand focus:ring-brand';

/**
 * The project's sampling frame. Everyone reads it as a summary line and a paginated table; an analyst or admin changes it from
 * the "Edit" and "Import from file" actions, and the editor goes away again once the frame is saved. A project without a frame
 * opens its editor straight away for those who can edit it.
 */
@Component({
  selector: 'app-project-sampling-frame',
  imports: [DatePipe, NgTemplateOutlet, TranslocoDirective, Alert, Button, Card, Skeleton, Table, TableContainer],
  template: `
    <section *transloco="let t">
      <div class="mb-3 flex flex-wrap items-center justify-between gap-3">
        <h2 class="text-lg font-semibold text-heading">{{ t('frame.title') }}</h2>
        @if (loaded() && canEdit()) {
          <div class="flex flex-wrap gap-2">
            @if (!editing()) {
              <button tmzButton size="sm" type="button" (click)="edit()" data-testid="edit-frame">{{ t('frame.edit') }}</button>
            }
            <button tmzButton [variant]="frame() || dimensions().length ? 'secondary' : 'primary'" size="sm" type="button" (click)="openImport()" data-testid="open-import">
              {{ t('frame.import') }}
            </button>
          </div>
        }
      </div>

      <ng-template #pager let-total="total" let-label="label">
        @if (total > pageSize) {
          <nav class="flex flex-wrap items-center justify-between gap-3 text-sm text-body" [attr.aria-label]="label">
            <span data-testid="frame-page-info">{{ t('frame.pageInfo', { from: page() * pageSize + 1, to: pageEnd(total), total }) }}</span>
            <div class="flex gap-2">
              <button tmzButton variant="secondary" size="sm" type="button" [disabled]="page() === 0" (click)="page.set(page() - 1)" data-testid="frame-previous">{{ t('frame.previous') }}</button>
              <button tmzButton variant="secondary" size="sm" type="button" [disabled]="pageEnd(total) >= total" (click)="page.set(page() + 1)" data-testid="frame-next">{{ t('frame.next') }}</button>
            </div>
          </nav>
        }
      </ng-template>

      @if (!loaded()) {
        <div aria-busy="true" class="flex flex-col gap-3" data-testid="frame-loading"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
      } @else if (!editing()) {
        @if (frame(); as saved) {
          <p class="mb-3 text-sm text-body" data-testid="frame-summary">
            {{ t('frame.summary', { dimensions: count('dimensions', saved.dimensions.length), combinations: count('combinations', saved.targets.length), date: (saved.updatedAt | date: dateTimeFormat) }) }}
          </p>
          <tmz-table-container>
            <table tmzTable data-testid="frame-table">
              <thead [class]="tableHead">
                <tr>
                  @for (dimension of saved.dimensions; track dimension.name) {
                    <th scope="col" [class]="tableHeaderCell">{{ dimension.name }}</th>
                  }
                  <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('frame.target') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (target of readRows(); track target.index) {
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
          <div class="mt-3"><ng-container [ngTemplateOutlet]="pager" [ngTemplateOutletContext]="{ total: saved.targets.length, label: t('frame.title') }" /></div>
        } @else {
          <p class="text-sm text-body" data-testid="frame-empty">{{ t('frame.emptyViewer') }}</p>
        }
      } @else {
        <tmz-card>
          <h3 class="text-base font-medium text-heading">{{ t('frame.editorTitle') }}</h3>
          <p class="mt-1 text-sm text-body">{{ t('frame.dimensionsIntro') }}</p>
          @if (errors()['dimensions']; as message) {
            <tmz-alert tone="danger" class="mt-4">{{ message }}</tmz-alert>
          }
          @if (errors()['targets']; as message) {
            <tmz-alert tone="danger" class="mt-4">{{ message }}</tmz-alert>
          }

          <ul class="mt-4 flex flex-col gap-4">
            @for (dimension of dimensions(); track $index; let i = $index) {
              <li class="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-start" [attr.data-testid]="'dimension-' + i">
                <div>
                  <label class="mb-1 block text-sm font-medium text-heading" [attr.for]="'dimension-name-' + i">{{ t('frame.dimensionName') }}</label>
                  <input [id]="'dimension-name-' + i" [class]="cellClass('dimensions[' + i + '].name')" [value]="dimension.name" (input)="setDimension(i, 'name', $any($event.target).value)" />
                  @if (errors()['dimensions[' + i + '].name']; as message) {
                    <p class="mt-1 text-sm text-fg-danger-strong">{{ message }}</p>
                  }
                </div>
                <div>
                  <label class="mb-1 block text-sm font-medium text-heading" [attr.for]="'dimension-field-' + i">{{ t('frame.dimensionField') }}</label>
                  <select [id]="'dimension-field-' + i" [class]="cellClass('dimensions[' + i + '].field')" (change)="setDimension(i, 'field', $any($event.target).value)">
                    <option value="">{{ t('frame.chooseField') }}</option>
                    @for (field of fields(); track field.xpath) {
                      <option [value]="field.xpath" [selected]="field.xpath === dimension.field">{{ optionLabel(field) }}</option>
                    }
                  </select>
                  @if (errors()['dimensions[' + i + '].field']; as message) {
                    <p class="mt-1 text-sm text-fg-danger-strong">{{ message }}</p>
                  } @else if (warning(dimension.field); as message) {
                    <p class="mt-1 text-sm text-body" [attr.data-testid]="'dimension-warning-' + i">{{ message }}</p>
                  }
                </div>
                <button tmzButton variant="ghost" size="sm" type="button" class="sm:mt-7" (click)="removeDimension(i)" [attr.aria-label]="t('frame.removeDimension', { n: i + 1 })">{{ t('frame.remove') }}</button>
              </li>
            }
          </ul>
          <button tmzButton variant="secondary" size="sm" class="mt-4" type="button" [disabled]="dimensions().length >= 10" (click)="addDimension()" data-testid="add-dimension">
            {{ t('frame.addDimension') }}
          </button>

          @if (dimensions().length) {
            <div class="mt-6">
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
              @if (!rows().length) {
                <p class="mt-3 text-sm text-body" data-testid="frame-no-rows">{{ t('frame.noRows') }}</p>
              }
              <div class="mt-3 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div><button tmzButton variant="secondary" size="sm" type="button" (click)="addRow()" data-testid="add-row">{{ t('frame.addRow') }}</button></div>
                <ng-container [ngTemplateOutlet]="pager" [ngTemplateOutletContext]="{ total: rows().length, label: t('frame.targetsTitle') }" />
              </div>
            </div>
          } @else {
            <p class="mt-6 text-sm text-body" data-testid="frame-add-dimension-first">{{ t('frame.addDimensionFirst') }}</p>
          }
        </tmz-card>

        <div class="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-end">
          @if (errorCount(); as count) {
            <p class="text-sm text-fg-danger-strong" role="alert" data-testid="error-count">
              {{ firstErrorRow() === null ? t('frame.errorCount', { count }) : t('frame.errorCountFirst', { count, row: firstErrorRow()! + 1 }) }}
            </p>
          }
          @if (frame() || dirty()) {
            <button tmzButton variant="secondary" type="button" (click)="cancel()" data-testid="cancel-edit">{{ t('common.cancel') }}</button>
          }
          <button tmzButton type="button" [disabled]="saving()" (click)="save()" data-testid="save-frame">{{ t('frame.save') }}</button>
        </div>
      }
    </section>
  `,
})
export class ProjectSamplingFrame {
  private readonly service = inject(ProjectsService);
  private readonly errorsService = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly context = inject(ProjectContext);
  private readonly dialog = inject(Dialog);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly decimal = new DecimalPipe(inject(LOCALE_ID));

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;
  protected readonly dateTimeFormat = dateTimeFormat;
  protected readonly pageSize = pageSize;

  protected readonly loaded = signal(false);
  protected readonly frame = signal<SamplingFrame | null>(null);
  protected readonly editing = signal(false);
  protected readonly dimensions = signal<EditableDimension[]>([]);
  protected readonly rows = signal<EditableRow[]>([]);
  protected readonly page = signal(0);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly saving = signal(false);

  protected readonly canEdit = computed(() => this.context.can('analyst'));
  protected readonly fields = computed<FormField[]>(() => frameFields(this.context.formFields()));
  protected readonly pageRows = computed(() =>
    this.rows()
      .slice(this.page() * pageSize, (this.page() + 1) * pageSize)
      .map((row, offset) => ({ ...row, index: this.page() * pageSize + offset })),
  );
  protected readonly readRows = computed(() =>
    (this.frame()?.targets ?? [])
      .slice(this.page() * pageSize, (this.page() + 1) * pageSize)
      .map((target, offset) => ({ ...target, index: this.page() * pageSize + offset })),
  );
  protected readonly errorCount = computed(() => Object.keys(this.errors()).length);
  /** The first row, in order, that the API refused; the counter names it and the grid opens on its page. */
  protected readonly firstErrorRow = computed(() => {
    const rows = Object.keys(this.errors())
      .map((key) => /^targets\[(\d+)\]/.exec(key))
      .filter((match): match is RegExpExecArray => !!match)
      .map((match) => Number(match[1]));
    return rows.length ? Math.min(...rows) : null;
  });
  /** The editor holds edits that are not the saved frame (or, without a frame, anything at all). */
  protected readonly dirty = computed(() => this.editing() && JSON.stringify({ dimensions: this.dimensions(), rows: this.rows() }) !== this.baseline());
  private readonly baseline = computed(() => JSON.stringify(snapshot(this.frame())));

  constructor() {
    this.context.loadFormFields();
    this.service.samplingFrame(this.context.project()!.id).subscribe({
      next: (frame) => {
        this.setFrame(frame ?? null);
        // Nobody can read a frame that is not there; whoever can write one is taken to the place to do it.
        this.editing.set(!frame && this.canEdit());
        this.loaded.set(true);
      },
      error: (error) => {
        this.errorsService.report(error);
        this.editing.set(this.canEdit());
        this.loaded.set(true);
      },
    });
  }

  /** Asks before discarding unsaved edits; used by the route guard and before an import replaces them. */
  async confirmDiscard(): Promise<boolean> {
    if (!this.dirty()) {
      return true;
    }
    const t = (key: string) => this.transloco.translate(key);
    return this.confirmDialog.confirm({
      title: t('frame.discardTitle'),
      message: t('frame.discardMessage'),
      confirmLabel: t('frame.discard'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
  }

  protected count(kind: 'dimensions' | 'combinations', n: number): string {
    return this.transloco.translate(`frame.${kind}${n === 1 ? 'One' : 'Many'}`, { count: this.decimal.transform(n) });
  }

  protected pageEnd(total: number): number {
    return Math.min((this.page() + 1) * pageSize, total);
  }

  /** A question the dashboard cannot count reliably says so in the list, and under the select once it is chosen. */
  protected optionLabel(field: FormField): string {
    const issue = fieldIssue(field);
    return `${field.label ?? field.name} (${field.xpath})${issue ? ' ' + this.transloco.translate('frame.issue.' + issue) : ''}`;
  }

  protected warning(xpath: string): string | null {
    const issue = fieldIssue(this.fields().find((field) => field.xpath === xpath));
    return issue ? this.transloco.translate('frame.warning.' + issue) : null;
  }

  protected cellClass(path: string): string {
    return `${cellInput} ${this.errors()[path] ? 'border-danger-subtle bg-danger-soft' : 'border-default-medium'}`;
  }

  protected rowError(index: number): string | null {
    const e = this.errors();
    return e[`targets[${index}]`] ?? e[`targets[${index}].values`] ?? e[`targets[${index}].target`] ?? null;
  }

  protected edit(): void {
    this.errors.set({});
    this.editing.set(true);
  }

  /** Back to the saved frame, with whatever was typed or imported discarded. */
  protected cancel(): void {
    this.setFrame(this.frame());
    this.errors.set({});
    this.editing.set(!this.frame());
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
    this.clampPage(this.rows().length);
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
        this.editing.set(false);
        this.saving.set(false);
        // The Form check on General depends on the frame; the details are loaded once per page.
        this.context.refresh();
        this.toasts.success(this.transloco.translate('frame.saved'));
      },
      error: (error) => {
        this.errors.set(this.errorsService.report(error).fieldErrors);
        this.saving.set(false);
        const first = this.firstErrorRow();
        if (first !== null) {
          this.page.set(Math.floor(first / pageSize));
        }
      },
    });
  }

  protected async openImport(): Promise<void> {
    if (!(await this.confirmDiscard())) {
      return;
    }
    const ref = this.dialog.open<ImportedFrame, ImportFrameData>(ImportFrameDialog, {
      data: { projectId: this.context.project()!.id, fields: this.fields() },
      ariaLabelledBy: 'import-frame-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
    ref.closed.subscribe((imported) => imported && this.load(imported));
  }

  /** Puts the imported rows into the editor; nothing is saved until the user presses Save. */
  private load(imported: ImportedFrame): void {
    this.dimensions.set(imported.dimensions);
    this.rows.set(imported.rows);
    this.page.set(0);
    this.errors.set({});
    this.editing.set(true);
    this.toasts.info(this.transloco.translate('frame.importApplied'));
  }

  /** Makes the editor and the read view show the given frame, keeping the page where it still exists. */
  private setFrame(frame: SamplingFrame | null): void {
    this.frame.set(frame);
    const saved = snapshot(frame);
    this.dimensions.set(saved.dimensions.map((d) => ({ ...d })));
    this.rows.set(saved.rows.map((r) => ({ values: [...r.values], target: r.target })));
    this.clampPage(saved.rows.length);
  }

  private clampPage(total: number): void {
    const last = Math.max(0, Math.ceil(total / pageSize) - 1);
    if (this.page() > last) {
      this.page.set(last);
    }
  }
}

/** The frame as the editor holds it. */
function snapshot(frame: SamplingFrame | null): { dimensions: EditableDimension[]; rows: EditableRow[] } {
  return {
    dimensions: (frame?.dimensions ?? []).map((d) => ({ name: d.name, field: d.field })),
    rows: (frame?.targets ?? []).map((t) => ({ values: [...t.values], target: String(t.target) })),
  };
}
