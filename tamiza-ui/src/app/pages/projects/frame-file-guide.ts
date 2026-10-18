import { Component, LOCALE_ID, computed, inject, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { FormChoice, FormField } from '../../core/projects/projects.service';
import { Table, TableContainer, tableCell, tableHead, tableHeaderCell, tableRow, numberText } from '../../shared/ui';
import {
  choiceList,
  choiceLists,
  frameFields,
  maxFileMegabytes,
  maxFrameQuestions,
  maxFrameRows,
  maxTarget,
  targetColumn,
  templateHeaders,
  templateIssue,
} from './sampling-frame-fields';

interface GuideExample {
  headers: string[];
  rows: string[][];
}

/** Shown until the form's choices are loaded; the page does not read them just for this example. */
const fixedExample: GuideExample = {
  headers: ['municipio', '# municipio label', 'sexo', targetColumn],
  rows: [
    ['05001', 'Medellín', 'f', '120'],
    ['05001', 'Medellín', 'm', '110'],
    ['05088', 'Bello', 'f', ''],
  ],
};

/**
 * How to organize a sampling frame file (design D11): an example table and the rules the import applies. The example uses the
 * form's first question with choices once its choices are loaded. The import dialog and the start panel both show it; where
 * `templateLink` is set, a link asks for the question picker instead.
 */
@Component({
  selector: 'app-frame-file-guide',
  imports: [TranslocoDirective, Table, TableContainer],
  template: `
    <div class="flex flex-col gap-3 text-sm text-body" *transloco="let t">
      <p>{{ t('frame.guide.intro') }}</p>
      <tmz-table-container>
        <table tmzTable data-testid="guide-example" [attr.aria-label]="t('frame.guide.example')">
          <thead [class]="tableHead">
            <tr>
              @for (header of example().headers; track $index) {
                <th scope="col" [class]="tableHeaderCell">{{ header }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of example().rows; track $index) {
              <tr [class]="tableRow">
                @for (value of row; track $index) {
                  <td [class]="tableCell">{{ value }}</td>
                }
              </tr>
            }
          </tbody>
        </table>
      </tmz-table-container>
      <ul class="list-disc space-y-1 ps-5" data-testid="guide-rules">
        <li>{{ t('frame.guide.columns') }}</li>
        <li>{{ t('frame.guide.target', { max: format(maxTarget) }) }}</li>
        <li>{{ t('frame.guide.rows') }}</li>
        <li>{{ t('frame.guide.emptyTarget') }}</li>
        <li>{{ t('frame.guide.notes') }}</li>
        <li>{{ t('frame.guide.text') }}</li>
        <li>{{ t('frame.guide.formats') }}</li>
        <li data-testid="guide-limits">{{ t('frame.guide.limits', { questions: maxFrameQuestions, rows: format(maxFrameRows), size: maxFileMegabytes }) }}</li>
      </ul>
      @if (templateLink()) {
        <p>
          <button type="button" class="min-h-10 rounded-base font-medium text-fg-brand hover:underline focus:outline-none focus:ring-4 focus:ring-neutral-tertiary" (click)="template.emit()" data-testid="guide-template">
            {{ t('frame.guide.templateLink') }}
          </button>
        </p>
      }
    </div>
  `,
})
export class FrameFileGuide {
  private readonly locale = inject(LOCALE_ID);

  /** The form's fields, unfiltered, so the example's column is named as a template would name it. */
  readonly fields = input<FormField[] | null>(null);
  readonly choices = input<FormChoice[] | null>(null);
  readonly templateLink = input(false);
  /** The user asked for a template with the form's codes. */
  readonly template = output<void>();

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;
  protected readonly maxTarget = maxTarget;
  protected readonly maxFrameRows = maxFrameRows;
  protected readonly maxFrameQuestions = maxFrameQuestions;
  protected readonly maxFileMegabytes = maxFileMegabytes;

  protected readonly example = computed<GuideExample>(() => {
    const snapshot = this.fields();
    const choices = this.choices();
    if (!snapshot || !choices) {
      return fixedExample;
    }
    const lists = choiceLists(choices);
    for (const field of frameFields(snapshot)) {
      const list = templateIssue(field) ? null : choiceList(field, lists);
      if (list && list.length) {
        const [header] = templateHeaders(snapshot, [field.xpath]);
        const [first, second] = list;
        return {
          headers: [header, `# ${header} label`, targetColumn],
          rows: [[first.code, first.label ?? '', '120'], ...(second ? [[second.code, second.label ?? '', '']] : [])],
        };
      }
    }
    return fixedExample;
  });

  protected format(value: number): string {
    return numberText(value, this.locale) ?? String(value);
  }
}
