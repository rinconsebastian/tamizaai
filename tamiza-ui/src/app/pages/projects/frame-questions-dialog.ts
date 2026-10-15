import { HttpErrorResponse } from '@angular/common/http';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DecimalPipe } from '@angular/common';
import { Component, LOCALE_ID, Signal, computed, effect, inject, signal, untracked } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import { FormChoice, FormField, FrameTemplateRequest, ProjectsService } from '../../core/projects/projects.service';
import { Alert, Button, Input, Skeleton, ToastService } from '../../shared/ui';
import {
  FrameChoice,
  TemplateIssue,
  choiceList,
  choiceLists,
  chosenCodes,
  combinationCount,
  combinations,
  frameFields,
  isAddedOther,
  maxFrameQuestions,
  maxFrameRows,
  maxValueLength,
  normalizeValues,
  repeatedHeader,
  templateHeaders,
  templateIssue,
} from './sampling-frame-fields';

export interface FrameQuestionsData {
  projectId: string;
  /** The form's fields, unfiltered: column names are chosen against all of them, as the API does. */
  snapshot: FormField[];
  /** The form's choices, read once per form check by the page's context, and how to read them (again). */
  choices: Signal<FormChoice[] | null>;
  choicesFailed: Signal<boolean>;
  loadChoices: () => void;
  /** Questions to start with, each with the values the editor holds for it. */
  initial?: { field: string; values: string[] }[];
}

/** The rows made here, for the editor: each question with its column name, and every combination of the chosen values. */
export interface FrameRowsFromQuestions {
  kind: 'rows';
  questions: { field: string; header: string }[];
  rows: string[][];
}

export type FrameQuestionsResult = FrameRowsFromQuestions | { kind: 'downloaded' };

interface Question {
  field: FormField;
  issue: TemplateIssue | null;
  /** The choices it offers, or null for a question whose values are typed. */
  list: FrameChoice[] | null;
}

interface Column extends Question {
  values: string[];
  /** The values typed for a question without choices include one that is too long. */
  tooLong: boolean;
}

/**
 * Chooses 1 to 10 of the form's questions and, for each, the choices or typed values the sample covers (design D8, D9). The live
 * row count blocks above the frame's limit. The result is an Excel template, prepared by the API, or the rows themselves for the
 * editor. Full screen on phones, a panel from `md` up, with the count and the actions in a footer that stays in view.
 */
@Component({
  selector: 'app-frame-questions-dialog',
  imports: [TranslocoDirective, Alert, Button, Input, Skeleton],
  template: `
    <div
      class="flex h-dvh w-screen flex-col bg-neutral-primary-soft md:h-auto md:max-h-[calc(100dvh-2rem)] md:w-[min(44rem,calc(100vw-2rem))] md:rounded-base md:border md:border-default md:shadow-sm"
      *transloco="let t"
      data-testid="questions-dialog"
    >
      <div class="min-h-0 flex-1 overflow-y-auto p-4 md:p-6">
        @if (view(); as current) {
          <button tmzButton variant="ghost" size="sm" type="button" class="-ms-3" (click)="view.set(null)" data-testid="choices-back">← {{ t('frame.picker.back') }}</button>
          <h2 id="frame-questions-title" class="mt-2 break-words text-lg font-medium text-heading">{{ t('frame.picker.choicesTitle', { question: title(current.field) }) }}</h2>
          <p class="mt-2 text-sm text-body">{{ t('frame.picker.tip') }}</p>
          <input
            tmzInput
            type="search"
            class="mt-4"
            autocomplete="off"
            [attr.aria-label]="t('frame.picker.searchChoices')"
            [placeholder]="t('frame.picker.searchChoices')"
            [value]="choiceSearch()"
            (input)="choiceSearch.set($any($event.target).value)"
            data-testid="choice-search"
          />
          <div class="mt-3 flex flex-wrap items-center gap-2">
            <button tmzButton variant="secondary" size="sm" type="button" (click)="tickShown(current, true)" data-testid="select-all">
              {{ choiceSearch().trim() ? t('frame.picker.selectShown', { count: format(shownChoices().length) }) : t('frame.picker.selectAll') }}
            </button>
            <button tmzButton variant="secondary" size="sm" type="button" (click)="tickShown(current, false)" data-testid="clear-all">
              {{ choiceSearch().trim() ? t('frame.picker.clearShown', { count: format(shownChoices().length) }) : t('frame.picker.clearAll') }}
            </button>
            <span class="text-sm text-body" data-testid="chosen-count">{{ t('frame.picker.chosen', { chosen: format(valuesOf(current).length), total: format(current.list!.length) }) }}</span>
          </div>
          @if (!valuesOf(current).length) {
            <p class="mt-2 text-sm text-fg-danger-strong">{{ t('frame.picker.noChoice') }}</p>
          }
          <ul class="mt-3 flex flex-col" data-testid="choice-list">
            @for (choice of shownChoices(); track choice.code) {
              <li>
                <label class="flex min-h-10 cursor-pointer items-center gap-3 text-sm text-heading">
                  <input type="checkbox" class="size-4 shrink-0" [checked]="isTicked(current, choice)" (change)="tick(current, choice, $any($event.target).checked)" [attr.data-testid]="'choice-' + choice.code" />
                  <span class="break-words">{{ choice.label ?? choice.code }} — {{ choice.code }}</span>
                </label>
              </li>
            }
          </ul>
        } @else {
          <h2 id="frame-questions-title" class="text-lg font-medium text-heading">{{ t('frame.picker.title') }}</h2>
          <p class="mt-2 text-sm text-body">{{ t('frame.picker.intro') }}</p>

          @if (data.choices() === null) {
            @if (data.choicesFailed()) {
              <tmz-alert tone="danger" class="mt-4" data-testid="picker-load-failed">
                {{ t('frame.picker.loadFailed') }}
                <div class="mt-3"><button tmzButton variant="secondary" size="sm" type="button" (click)="data.loadChoices()" data-testid="picker-retry">{{ t('frame.picker.retry') }}</button></div>
              </tmz-alert>
            } @else {
              <div aria-busy="true" class="mt-4 flex flex-col gap-3" data-testid="picker-loading"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /><tmz-skeleton class="w-1/2" /></div>
            }
          } @else {
            <div class="mt-4 flex flex-col gap-2 sm:flex-row sm:items-center">
              <input
                tmzInput
                type="search"
                class="sm:flex-1"
                autocomplete="off"
                [attr.aria-label]="t('frame.picker.search')"
                [placeholder]="t('frame.picker.search')"
                [value]="search()"
                (input)="search.set($any($event.target).value)"
                data-testid="question-search"
              />
              <span class="text-sm text-body" data-testid="question-count">{{ t('frame.picker.selectedCount', { count: selected().length, max: maxQuestions }) }}</span>
            </div>

            @for (group of groups(); track group.key) {
              @if (group.questions.length) {
                <fieldset class="mt-5 min-w-0" [attr.data-testid]="'group-' + group.key">
                  <legend class="mb-2 text-sm font-medium text-heading">{{ t('frame.picker.group.' + group.key) }}</legend>
                  <ul class="flex flex-col divide-y divide-default rounded-base border border-default">
                    @for (question of group.questions; track question.field.xpath) {
                      <li class="p-3" [attr.data-testid]="'question-' + question.field.xpath">
                        <label class="flex cursor-pointer items-start gap-3" [class.cursor-not-allowed]="!!question.issue">
                          <input
                            type="checkbox"
                            class="mt-1 size-4 shrink-0"
                            [checked]="isSelected(question)"
                            [disabled]="!!question.issue || (!isSelected(question) && selected().length >= maxQuestions)"
                            (change)="toggle(question, $any($event.target).checked)"
                            [attr.data-testid]="'pick-' + question.field.xpath"
                          />
                          <span class="min-w-0 text-sm">
                            <span class="block break-words font-medium text-heading">{{ describe(question) }}</span>
                            @if (question.issue) {
                              <span class="block text-body" [attr.data-testid]="'reason-' + question.field.xpath">{{ reason(question.issue) }}</span>
                            }
                          </span>
                        </label>
                        @if (isSelected(question) && column(question); as col) {
                          @if (col.list) {
                            <div class="mt-2 ps-7">
                              <button tmzButton variant="secondary" size="sm" type="button" (click)="openChoices(question)" [attr.data-testid]="'edit-choices-' + question.field.xpath">
                                {{ t('frame.picker.editChoices', { chosen: format(col.values.length), total: format(col.list.length) }) }}
                              </button>
                              @if (!col.values.length) {
                                <p class="mt-1 text-sm text-fg-danger-strong">{{ t('frame.picker.noChoice') }}</p>
                              }
                            </div>
                          } @else {
                            <div class="mt-2 ps-7">
                              <label class="mb-1 block text-sm font-medium text-heading" [attr.for]="'values-' + question.field.xpath">{{ t('frame.picker.values') }}</label>
                              <textarea
                                tmzInput
                                rows="3"
                                [id]="'values-' + question.field.xpath"
                                [value]="typed()[question.field.xpath] ?? ''"
                                (input)="type(question, $any($event.target).value)"
                                [attr.data-testid]="'values-' + question.field.xpath"
                              ></textarea>
                              @if (col.tooLong) {
                                <p class="mt-1 text-sm text-fg-danger-strong" [attr.data-testid]="'too-long-' + question.field.xpath">{{ t('frame.picker.valueTooLong', { max: maxValueLength }) }}</p>
                              }
                            </div>
                          }
                        }
                      </li>
                    }
                  </ul>
                </fieldset>
              }
            }
          }
        }
      </div>

      <div class="sticky bottom-0 border-t border-default bg-neutral-primary-soft p-4 md:rounded-b-base md:px-6" data-testid="picker-footer">
        <p class="text-sm font-medium text-heading" aria-live="polite" data-testid="row-count">{{ countText() }}</p>
        @if (problem(); as message) {
          <p class="mt-1 text-sm text-fg-danger-strong" data-testid="picker-problem">{{ message }}</p>
        }
        @if (requestError(); as message) {
          <tmz-alert tone="danger" class="mt-3" data-testid="template-error">{{ message }}</tmz-alert>
        }
        <div class="mt-3 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
          <button tmzButton variant="secondary" type="button" (click)="ref.close()">{{ t('common.cancel') }}</button>
          <button tmzButton variant="secondary" type="button" [disabled]="blocked() || busy()" (click)="createRows()" data-testid="create-rows">{{ t('frame.picker.createRows') }}</button>
          <button tmzButton type="button" [disabled]="blocked() || busy()" [attr.aria-busy]="busy() || null" (click)="download()" data-testid="download-template">
            {{ busy() ? t('frame.template.downloading') : t('frame.template.download') }}
          </button>
        </div>
      </div>
    </div>
  `,
})
export class FrameQuestionsDialog {
  protected readonly data = inject<FrameQuestionsData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<FrameQuestionsResult>>(DialogRef);
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly downloader = inject(Downloader);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly decimal = new DecimalPipe(inject(LOCALE_ID));

  protected readonly maxQuestions = maxFrameQuestions;
  protected readonly maxValueLength = maxValueLength;

  /** The question whose choices are shown, or null for the list of questions. */
  protected readonly view = signal<Question | null>(null);
  protected readonly search = signal('');
  protected readonly choiceSearch = signal('');
  /** The chosen questions' paths; the file and the rows follow form order. */
  protected readonly selected = signal<string[]>([]);
  /** The ticked codes of each question with choices, kept when it is unticked and ticked again. */
  protected readonly ticked = signal<Record<string, string[]>>({});
  /** The text typed for each question without choices. */
  protected readonly typed = signal<Record<string, string>>({});
  protected readonly busy = signal(false);
  protected readonly requestError = signal<string | null>(null);

  private readonly lists = computed(() => choiceLists(this.data.choices()));
  private readonly questions = computed<Question[]>(() => {
    const lists = this.lists();
    return frameFields(this.data.snapshot).map((field) => {
      const issue = templateIssue(field);
      return { field, issue, list: issue ? null : choiceList(field, lists) };
    });
  });

  protected readonly groups = computed(() => {
    const term = this.search().trim().toLowerCase();
    const matching = this.questions().filter(
      ({ field }) => !term || [field.label ?? '', field.name, field.xpath].some((text) => text.toLowerCase().includes(term)),
    );
    return [
      { key: 'choices', questions: matching.filter((q) => !q.issue && q.list) },
      { key: 'typed', questions: matching.filter((q) => !q.issue && !q.list) },
      { key: 'disabled', questions: matching.filter((q) => q.issue) },
    ];
  });

  private readonly columns = computed<Column[]>(() => {
    const chosen = new Set(this.selected());
    return this.questions()
      .filter((q) => chosen.has(q.field.xpath))
      .map((q) => {
        if (q.list) {
          return { ...q, values: this.valuesOf(q), tooLong: false };
        }
        const values = normalizeValues(this.typed()[q.field.xpath] ?? '');
        return { ...q, values, tooLong: values.some((v) => v.length > maxValueLength) };
      });
  });

  protected readonly count = computed(() => combinationCount(this.columns().map((c) => c.values.length)));
  private readonly headers = computed(() => templateHeaders(this.data.snapshot, this.columns().map((c) => c.field.xpath)));

  /** Why neither action can be used yet, in the footer; problems of one question are shown next to it. */
  protected readonly problem = computed<string | null>(() => {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    if (this.count() > maxFrameRows) {
      return t('frame.picker.tooMany', { count: this.format(this.count()), max: this.format(maxFrameRows) });
    }
    const repeated = repeatedHeader(this.headers());
    return repeated ? t('frame.picker.repeatedHeader', { name: repeated }) : null;
  });

  protected readonly blocked = computed(
    () => !this.columns().length || !!this.problem() || this.columns().some((c) => c.tooLong || (c.list !== null && !c.values.length)),
  );

  protected readonly countText = computed(() => {
    if (!this.columns().length) {
      return this.transloco.translate('frame.picker.noneChosen');
    }
    const lengths = this.columns().map((c) => c.values.length).filter((n) => n > 0);
    const rows = this.transloco.translate(this.count() === 1 ? 'frame.picker.countOne' : 'frame.picker.countMany', { count: this.format(this.count()) });
    return lengths.length > 1 ? this.transloco.translate('frame.picker.formula', { formula: lengths.map((n) => this.format(n)).join(' × '), rows }) : rows;
  });

  protected readonly shownChoices = computed(() => {
    const question = this.view();
    const term = this.choiceSearch().trim().toLowerCase();
    const list = question?.list ?? [];
    return term ? list.filter((c) => c.code.toLowerCase().includes(term) || (c.label ?? '').toLowerCase().includes(term)) : list;
  });

  constructor() {
    this.data.loadChoices();
    // Questions the editor maps start ticked, with its values, once the choices tell which questions have choices.
    let started = false;
    effect(() => {
      const questions = this.questions();
      if (started || this.data.choices() === null) {
        return;
      }
      started = true;
      untracked(() => this.start(questions));
    });
  }

  protected isSelected(question: Question): boolean {
    return this.selected().includes(question.field.xpath);
  }

  protected column(question: Question): Column | undefined {
    return this.columns().find((c) => c.field.xpath === question.field.xpath);
  }

  /** The ticked codes of a question with choices, in form order. */
  protected valuesOf(question: Question): string[] {
    const ticked = new Set(this.ticked()[question.field.xpath] ?? []);
    return (question.list ?? []).filter((c) => ticked.has(c.code)).map((c) => c.code);
  }

  protected isTicked(question: Question, choice: FrameChoice): boolean {
    return (this.ticked()[question.field.xpath] ?? []).includes(choice.code);
  }

  protected toggle(question: Question, checked: boolean): void {
    const xpath = question.field.xpath;
    if (!checked) {
      this.selected.update((paths) => paths.filter((p) => p !== xpath));
      return;
    }
    if (this.selected().length >= maxFrameQuestions || this.isSelected(question)) {
      return;
    }
    this.selected.update((paths) => [...paths, xpath]);
    if (question.list && !(xpath in this.ticked())) {
      this.ticked.update((all) => ({ ...all, [xpath]: this.ownCodes(question) }));
    }
  }

  protected type(question: Question, text: string): void {
    this.typed.update((all) => ({ ...all, [question.field.xpath]: text }));
  }

  protected openChoices(question: Question): void {
    this.choiceSearch.set('');
    this.view.set(question);
  }

  protected tick(question: Question, choice: FrameChoice, checked: boolean): void {
    this.setTicks(question, [choice.code], checked);
  }

  /** Select all / Clear: with a search, only the choices it shows change; choices it hides keep their ticks. */
  protected tickShown(question: Question, checked: boolean): void {
    this.setTicks(question, this.shownChoices().map((c) => c.code), checked);
  }

  protected title(field: FormField): string {
    return field.label ?? field.name;
  }

  /** "Municipality · municipio · 1,100 choices" */
  protected describe(question: Question): string {
    const parts = [this.title(question.field)];
    if (question.field.label && question.field.label !== question.field.name) {
      parts.push(question.field.name);
    }
    if (question.list) {
      const n = question.list.length;
      parts.push(this.transloco.translate(n === 1 ? 'frame.picker.choicesOne' : 'frame.picker.choicesMany', { count: this.format(n) }));
    }
    return parts.join(' · ');
  }

  protected reason(issue: TemplateIssue): string {
    return this.transloco.translate(issue === 'target' ? 'frame.picker.targetName' : 'frame.warning.' + issue);
  }

  protected format(value: number): string {
    return this.decimal.transform(value) ?? String(value);
  }

  protected download(): void {
    if (this.blocked() || this.busy()) {
      return;
    }
    const request: FrameTemplateRequest = {
      dimensions: this.columns().map((c) => ({ field: c.field.xpath, values: c.list && this.isOwnList(c) ? null : c.values })),
    };
    this.busy.set(true);
    this.requestError.set(null);
    this.service.samplingFrameTemplate(this.data.projectId, request).subscribe({
      next: (file) => {
        this.downloader.start(file.downloadUrl);
        this.ref.close({ kind: 'downloaded' });
        this.toasts.success(
          this.transloco.translate(file.rowCount === 1 ? 'frame.template.downloadedOne' : 'frame.template.downloadedMany', { count: this.format(file.rowCount) }),
        );
      },
      error: (error) => {
        const parsed = this.errors.parse(error as HttpErrorResponse);
        this.requestError.set(Object.values(parsed.fieldErrors)[0] ?? parsed.message);
        this.busy.set(false);
      },
    });
  }

  protected createRows(): void {
    if (this.blocked()) {
      return;
    }
    const columns = this.columns();
    const headers = this.headers();
    this.ref.close({
      kind: 'rows',
      questions: columns.map((c, i) => ({ field: c.field.xpath, header: headers[i] })),
      rows: combinations(columns.map((c) => c.values)),
    });
  }

  /** Ticks the questions the editor maps: their codes found in the rows (or every choice when none is), or their typed values. */
  private start(questions: Question[]): void {
    const ticked: Record<string, string[]> = {};
    const typed: Record<string, string> = {};
    const selected: string[] = [];
    for (const { field, values } of this.data.initial ?? []) {
      const question = questions.find((q) => q.field.xpath === field);
      if (!question || question.issue || selected.includes(field) || selected.length >= maxFrameQuestions) {
        continue;
      }
      selected.push(field);
      if (question.list) {
        const codes = chosenCodes(question.list, values);
        ticked[field] = codes.length ? codes : this.ownCodes(question);
      } else {
        typed[field] = normalizeValues(values).join('\n');
      }
    }
    this.selected.set(selected);
    this.ticked.set(ticked);
    this.typed.set(typed);
  }

  private setTicks(question: Question, codes: string[], checked: boolean): void {
    const xpath = question.field.xpath;
    this.ticked.update((all) => {
      const current = new Set(all[xpath] ?? []);
      codes.forEach((code) => (checked ? current.add(code) : current.delete(code)));
      return { ...all, [xpath]: [...current] };
    });
  }

  /** The list's own choices, without the `other` pyxform adds: what a question starts with, and what `null` asks the API for. */
  private ownCodes(question: Question): string[] {
    const lists = this.lists();
    return (question.list ?? []).filter((c) => !isAddedOther(question.field, lists, c)).map((c) => c.code);
  }

  private isOwnList(column: Column): boolean {
    const own = this.ownCodes(column);
    return own.length === column.values.length && own.every((code, i) => code === column.values[i]);
  }
}
