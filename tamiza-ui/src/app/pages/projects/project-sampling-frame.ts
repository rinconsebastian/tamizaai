import { Dialog } from '@angular/cdk/dialog';
import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import { FormField, ProjectsService, SamplingFrame, SamplingFrameInput } from '../../core/projects/projects.service';
import {
  Alert,
  Button,
  Card,
  ConfirmDialog,
  Disclosure,
  Skeleton,
  Table,
  TableContainer,
  ToastService,
  dateTimeFormat,
  tableCell,
  tableHead,
  tableHeaderCell,
  tableRow,
} from '../../shared/ui';
import { FrameFileGuide } from './frame-file-guide';
import { FrameQuestionsData, FrameQuestionsDialog, FrameQuestionsResult, FrameRowsFromQuestions } from './frame-questions-dialog';
import { ImportFrameData, ImportFrameDialog, ImportFrameResult, ImportedFrame } from './import-frame-dialog';
import { ProjectContext } from './project-context';
import { choiceList, choiceLists, chosenCodes, fieldIssue, frameFields, maxFrameQuestions, templateIssue, trimCode } from './sampling-frame-fields';

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

/** Dialogs that fill the screen on phones and float from `md` up. */
const fullScreenOnPhones = { maxWidth: '100vw', maxHeight: '100dvh' };

/**
 * The project's sampling frame. Everyone reads it as a summary line and a paginated table, and can download it as a workbook.
 * An analyst or admin changes it from the "Edit" and "Import from file" actions, and the editor goes away again once the frame is
 * saved. A project without a frame offers them three ways to start: choose questions, import a file or type it in.
 */
@Component({
  selector: 'app-project-sampling-frame',
  imports: [DatePipe, NgTemplateOutlet, TranslocoDirective, Alert, Button, Card, Disclosure, FrameFileGuide, Skeleton, Table, TableContainer],
  template: `
    <section *transloco="let t">
      <div class="mb-3 flex flex-wrap items-center justify-between gap-3">
        <h2 class="text-lg font-semibold text-heading">{{ t('frame.title') }}</h2>
        @if (loaded() && !loadError() && (frame() || editing())) {
          <div class="flex flex-wrap gap-2">
            @if (frame() && !editing()) {
              @if (canEdit()) {
                <button tmzButton size="sm" type="button" (click)="edit()" data-testid="edit-frame">{{ t('frame.edit') }}</button>
              }
              <button tmzButton variant="secondary" size="sm" type="button" [disabled]="downloading()" [attr.aria-busy]="downloading() || null" (click)="download()" data-testid="download-frame">
                {{ downloading() ? t('frame.download.downloading') : t('frame.download.action') }}
              </button>
            }
            @if (canEdit()) {
              <button tmzButton variant="secondary" size="sm" type="button" (click)="openImport()" data-testid="open-import">{{ t('frame.import') }}</button>
            }
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
      } @else if (loadError(); as message) {
        <tmz-alert tone="danger" [heading]="t('frame.loadFailed')" data-testid="frame-load-failed">
          {{ message }}
          <div class="mt-3"><button tmzButton variant="secondary" size="sm" type="button" (click)="loadFrame()" data-testid="frame-retry">{{ t('frame.retry') }}</button></div>
        </tmz-alert>
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
        } @else if (canEdit()) {
          <tmz-card data-testid="frame-start">
            <h3 class="text-base font-medium text-heading">{{ t('frame.start.heading') }}</h3>
            <p class="mt-1 text-sm text-body">{{ t('frame.start.intro') }}</p>
            <div class="mt-4 flex flex-col gap-3 sm:flex-row sm:flex-wrap">
              <button tmzButton type="button" [disabled]="!context.formFields()" (click)="openPicker()" data-testid="start-questions">{{ t('frame.start.questions') }}</button>
              <button tmzButton variant="secondary" type="button" (click)="openImport()" data-testid="start-import">{{ t('frame.start.import') }}</button>
              <button tmzButton variant="secondary" type="button" (click)="typeIn()" data-testid="start-type">{{ t('frame.start.type') }}</button>
            </div>
            <tmz-disclosure class="mt-5 border-t border-default pt-3" [heading]="t('frame.guide.heading')" data-testid="start-guide">
              <app-frame-file-guide [fields]="context.formFields()" [choices]="context.formChoices()" [templateLink]="!!context.formFields()" (template)="openPicker()" />
            </tmz-disclosure>
          </tmz-card>
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
          <div class="mt-4 flex flex-col gap-3 sm:flex-row sm:flex-wrap">
            <button tmzButton variant="secondary" size="sm" type="button" [disabled]="dimensions().length >= maxDimensions" (click)="addDimension()" data-testid="add-dimension">
              {{ t('frame.addDimension') }}
            </button>
            <button tmzButton variant="secondary" size="sm" type="button" [disabled]="!context.formFields()" (click)="openPicker(true)" data-testid="create-rows-from-questions">
              {{ t('frame.editor.createRows') }}
            </button>
          </div>

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
                <div class="flex flex-col gap-3 sm:flex-row sm:flex-wrap">
                  <button tmzButton variant="secondary" size="sm" type="button" (click)="addRow()" data-testid="add-row">{{ t('frame.addRow') }}</button>
                  @if (rowsWithoutTarget(); as count) {
                    <button tmzButton variant="secondary" size="sm" type="button" (click)="removeRowsWithoutTarget()" data-testid="remove-empty-targets">
                      {{ t('frame.editor.removeEmpty', { count: format(count) }) }}
                    </button>
                  }
                </div>
                <ng-container [ngTemplateOutlet]="pager" [ngTemplateOutletContext]="{ total: rows().length, label: t('frame.targetsTitle') }" />
              </div>
            </div>
          } @else {
            <p class="mt-6 text-sm text-body" data-testid="frame-add-dimension-first">{{ t('frame.addDimensionFirst') }}</p>
          }
        </tmz-card>

        <div class="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-end">
          @if (errorCount(); as count) {
            <p class="text-sm text-fg-danger-strong" role="alert" data-testid="error-count">{{ errorCountText(count) }}</p>
          }
          <button tmzButton variant="secondary" type="button" (click)="cancel()" data-testid="cancel-edit">{{ t('common.cancel') }}</button>
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
  protected readonly context = inject(ProjectContext);
  private readonly dialog = inject(Dialog);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly downloader = inject(Downloader);
  private readonly decimal = new DecimalPipe(inject(LOCALE_ID));

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;
  protected readonly dateTimeFormat = dateTimeFormat;
  protected readonly pageSize = pageSize;
  protected readonly maxDimensions = maxFrameQuestions;

  protected readonly loaded = signal(false);
  /** Why the frame could not be read; nothing that edits the frame is offered until a read succeeds, since Save replaces it. */
  protected readonly loadError = signal<string | null>(null);
  protected readonly frame = signal<SamplingFrame | null>(null);
  protected readonly editing = signal(false);
  protected readonly dimensions = signal<EditableDimension[]>([]);
  protected readonly rows = signal<EditableRow[]>([]);
  protected readonly page = signal(0);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly saving = signal(false);
  protected readonly downloading = signal(false);

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
  protected readonly rowsWithoutTarget = computed(() => this.rows().filter((row) => row.target.trim() === '').length);
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
    this.loadFrame();
  }

  protected loadFrame(): void {
    this.loaded.set(false);
    this.loadError.set(null);
    this.service.samplingFrame(this.context.project()!.id).subscribe({
      next: (frame) => {
        this.setFrame(frame ?? null);
        this.loaded.set(true);
      },
      error: (error) => {
        this.loadError.set(this.errorsService.parse(error).message);
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

  protected format(value: number): string {
    return this.decimal.transform(value) ?? String(value);
  }

  protected errorCountText(count: number): string {
    const first = this.firstErrorRow();
    const plural = count === 1 ? 'One' : 'Many';
    return first === null
      ? this.transloco.translate(`frame.errorCount${plural}`, { count: this.format(count) })
      : this.transloco.translate(`frame.errorCountFirst${plural}`, { count: this.format(count), row: this.format(first + 1) });
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

  /** The empty editor, from the start panel. */
  protected typeIn(): void {
    this.edit();
  }

  /** Back to the saved frame (or to the start panel without one), with whatever was typed or imported discarded. */
  protected cancel(): void {
    this.setFrame(this.frame());
    this.errors.set({});
    this.editing.set(false);
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

  /** Rows nobody gave a target are not planned; saving needs a target in every row, so they can go in one step. */
  protected removeRowsWithoutTarget(): void {
    this.rows.update((rows) => rows.filter((row) => row.target.trim() !== ''));
    this.errors.set({});
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

  /** Prepares the saved frame as a workbook and starts its download; the action ends with one success or one error message. */
  protected download(): void {
    if (this.downloading()) {
      return;
    }
    this.downloading.set(true);
    this.service.exportSamplingFrame(this.context.project()!.id).subscribe({
      next: (file) => {
        this.downloading.set(false);
        this.downloader.start(file.downloadUrl);
        this.toasts.success(this.transloco.translate('frame.download.ready'));
      },
      error: (error) => {
        this.downloading.set(false);
        this.errorsService.report(error);
      },
    });
  }

  protected async openImport(): Promise<void> {
    if (!(await this.confirmDiscard())) {
      return;
    }
    const ref = this.dialog.open<ImportFrameResult, ImportFrameData>(ImportFrameDialog, {
      data: {
        projectId: this.context.project()!.id,
        fields: this.fields(),
        snapshot: this.context.formFields(),
        choices: this.context.formChoices(),
        savedDimensions: this.frame()?.dimensions ?? [],
      },
      ariaLabelledBy: 'import-frame-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
      ...fullScreenOnPhones,
    });
    ref.closed.subscribe((result) => {
      if (result?.kind === 'rows') {
        this.load(result);
      } else if (result?.kind === 'template') {
        // The import dialog has closed; the picker opens in its place, never on top of it.
        this.openPicker();
      }
    });
  }

  /** Opens the question picker; from the editor, with the editor's questions and values ticked. */
  protected openPicker(fromEditor = false): void {
    const context = this.context;
    const initial = fromEditor
      ? this.dimensions()
          .map((dimension, i) => ({ field: dimension.field, values: this.rows().map((row) => row.values[i] ?? '') }))
          .filter((dimension) => dimension.field)
      : undefined;
    const ref = this.dialog.open<FrameQuestionsResult, FrameQuestionsData>(FrameQuestionsDialog, {
      data: {
        projectId: context.project()!.id,
        snapshot: context.formFields() ?? [],
        choices: context.formChoices,
        choicesFailed: context.formChoicesFailed,
        loadChoices: () => context.loadFormChoices(),
        initial,
      },
      ariaLabelledBy: 'frame-questions-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
      ...fullScreenOnPhones,
    });
    ref.closed.subscribe((result) => result?.kind === 'rows' && void this.createRows(result));
  }

  /**
   * Loads rows made from questions into the editor (design D9). A question the editor already maps keeps that dimension's name.
   * Targets carry over only when the questions are exactly the ones the editor maps; replacing rows asks first, naming what is lost.
   */
  private async createRows(result: FrameRowsFromQuestions): Promise<void> {
    const dimensions = this.dimensions();
    const rows = this.rows();
    const mapped = dimensions.map((d) => d.field);
    const chosen = result.questions.map((q) => q.field);
    const sameQuestions =
      mapped.length === chosen.length && new Set(mapped).size === mapped.length && mapped.every((field) => field && chosen.includes(field));

    if (rows.length && !(await this.confirmReplace(sameQuestions))) {
      return;
    }

    // Each new row keeps the target of the first editor row with the same value for every question, trimmed and ignoring case.
    const targets = new Map<string, string>();
    if (sameQuestions) {
      const positions = chosen.map((field) => mapped.indexOf(field));
      for (const row of rows) {
        const key = rowKey(positions.map((i) => row.values[i] ?? ''));
        if (!targets.has(key)) {
          targets.set(key, row.target);
        }
      }
    }

    this.dimensions.set(
      result.questions.map((question) => ({ name: dimensions.find((d) => d.field === question.field)?.name || question.header, field: question.field })),
    );
    this.rows.set(result.rows.map((values) => ({ values: [...values], target: targets.get(rowKey(values)) ?? '' })));
    this.page.set(0);
    this.errors.set({});
    this.editing.set(true);
    this.toasts.info(this.transloco.translate('frame.editor.rowsCreated'));
  }

  /** Asks before rows made from questions replace the editor's rows, saying what will not be kept. */
  private confirmReplace(sameQuestions: boolean): Promise<boolean> {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const snapshot = this.context.formFields() ?? [];
    const lists = choiceLists(this.context.formChoices());
    const parts = [t('frame.editor.replaceRows', { count: this.format(this.rows().length) })];
    if (!sameQuestions) {
      parts.push(t('frame.editor.targetsNotKept'));
    }

    // Dimensions the picker cannot offer: unmapped, gone from the form, multiple-choice or in a repeat group.
    const fields = this.dimensions().map((dimension) => snapshot.find((f) => f.xpath === dimension.field));
    const lost = this.dimensions()
      .map((dimension, i) => {
        const field = fields[i];
        return !field || templateIssue(field) !== null ? dimension.name || `${t('frame.unnamed')} ${i + 1}` : null;
      })
      .filter((name): name is string => name !== null);
    if (lost.length) {
      parts.push(t('frame.editor.lostDimensions', { names: lost.join(', ') }));
    }

    const columnChoices = fields.map((field) => (field && !templateIssue(field) ? choiceList(field, lists) : null));
    const notChoices = this.rows().filter((row) =>
      columnChoices.some((list, i) => list !== null && trimCode(row.values[i] ?? '') !== '' && !chosenCodes(list, [row.values[i]]).length),
    ).length;
    if (notChoices) {
      parts.push(t(notChoices === 1 ? 'frame.editor.notChoicesOne' : 'frame.editor.notChoicesMany', { count: this.format(notChoices) }));
    }

    return this.confirmDialog.confirm({
      title: t('frame.editor.replaceTitle'),
      message: parts.join(' '),
      confirmLabel: t('frame.editor.replace'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
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

/** Values compared as the dashboard compares them: trimmed and ignoring case. */
function rowKey(values: string[]): string {
  return values.map((value) => trimCode(value).toLowerCase()).join('\u001f');
}
