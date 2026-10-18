import { Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { countKey } from '../../../core/i18n/plural';
import type { InputColumn, InputQuestion, ScriptInputs } from '../../../core/projects/pipeline.service';
import { Alert, Button, Disclosure, Input, Skeleton, DateTimePipe, NumberPipe } from '../../../shared/ui';

interface TableView {
  name: string;
  rows: number | null;
  columns: InputColumn[];
  /** Whether the filter matched the table's own name, which shows every column. */
  open: boolean;
}

function matches(filter: string, ...texts: (string | null | undefined)[]): boolean {
  return texts.some((text) => !!text && text.toLowerCase().includes(filter));
}

/**
 * The tables a script receives, as the last run published them, with the project's reference tables and the form's
 * questions. Names are buttons: activating one inserts it into the editor or, when the editor is read-only, copies it.
 */
@Component({
  selector: 'app-script-inputs-panel',
  imports: [DateTimePipe, NumberPipe, RouterLink, TranslocoDirective, Alert, Button, Disclosure, Input, Skeleton],
  template: `
    <div class="flex flex-col gap-3 text-sm" *transloco="let t" data-testid="inputs-panel">
      <div class="flex items-start justify-between gap-2">
        <p class="text-body" data-testid="inputs-status">
          @if (inputs() === null || failed()) {
            {{ t('scripts.inputs.title') }}
          } @else if (inputs()!.publishedAt; as published) {
            <span data-testid="inputs-published">{{ t(inputs()!.source === 'clean' ? 'scripts.inputs.cleanPublished' : 'scripts.inputs.rawPublished', { when: (published | dateTime) }) }}</span>
          } @else {
            <span data-testid="inputs-none">{{ t('scripts.inputs.nothingPublished') }}</span>
          }
        </p>
        <button
          tmzButton
          variant="ghost"
          size="sm"
          type="button"
          class="shrink-0"
          [attr.aria-label]="t('scripts.inputs.refresh')"
          [attr.title]="t('scripts.inputs.refresh')"
          (click)="refresh.emit()"
          data-testid="inputs-refresh"
        >
          <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7" /></svg>
        </button>
      </div>

      @if (failed()) {
        <tmz-alert tone="danger" data-testid="inputs-error">{{ t('scripts.inputs.loadError') }}</tmz-alert>
      } @else if (inputs() === null) {
        <div aria-busy="true" class="flex flex-col gap-2"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
      } @else {
        <div>
          <label class="sr-only" for="inputs-filter">{{ t('scripts.inputs.filterLabel') }}</label>
          <input tmzInput id="inputs-filter" type="search" [value]="filter()" [placeholder]="t('scripts.inputs.filterPlaceholder')" (input)="filter.set($any($event.target).value)" data-testid="inputs-filter" />
        </div>
        <p class="text-xs text-body">{{ canInsert() ? t('scripts.inputs.insertHint') : t('scripts.inputs.copyHint') }}</p>

        @if (tables().length) {
          <section aria-labelledby="inputs-tables">
            <h3 id="inputs-tables" class="mb-1 font-medium text-heading">{{ t('scripts.inputs.tables') }}</h3>
            <ul class="flex flex-col gap-1">
              @for (table of tables(); track table.name) {
                <li>
                  <details class="rounded-base border border-default px-2 py-1" [open]="table.open || !!filter()" [attr.data-testid]="'input-table-' + table.name">
                    <summary class="cursor-pointer">
                      <button type="button" class="font-medium text-heading hover:underline" (click)="pick.emit(table.name); $event.preventDefault()" data-testid="input-name">{{ table.name }}</button>
                      @if (table.rows !== null) {
                        <span class="ms-1 text-body">{{ t(key('scripts.inputs.rows', table.rows), { count: (table.rows | num) }) }}</span>
                      }
                    </summary>
                    <ul class="mt-1 flex max-h-72 flex-col gap-0.5 overflow-y-auto">
                      @for (column of table.columns; track column.name) {
                        <li class="flex flex-wrap items-baseline gap-x-2">
                          <button type="button" class="text-left font-mono text-xs text-heading hover:underline" (click)="pick.emit(column.name)" data-testid="input-name">{{ column.name }}</button>
                          <span class="text-xs text-body">{{ column.type }}</span>
                          @if (column.label) {
                            <span class="min-w-0 basis-full truncate text-xs text-body" [title]="column.label">{{ column.label }}</span>
                          }
                        </li>
                      }
                    </ul>
                  </details>
                </li>
              }
            </ul>
          </section>
        }

        @if (referenceTables().length) {
          <section aria-labelledby="inputs-reference">
            <div class="mb-1 flex items-baseline justify-between gap-2">
              <h3 id="inputs-reference" class="font-medium text-heading">{{ t('scripts.inputs.referenceTables') }}</h3>
              @if (manageLink(); as link) {
                <a [routerLink]="link" class="text-xs text-fg-brand hover:underline" data-testid="inputs-manage-reference">{{ t('scripts.inputs.manage') }}</a>
              }
            </div>
            <ul class="flex flex-col gap-1">
              @for (table of referenceTables(); track table.name) {
                <li>
                  <details class="rounded-base border border-default px-2 py-1" [open]="table.open || !!filter()" [attr.data-testid]="'input-reference-' + table.name">
                    <summary class="cursor-pointer">
                      <button type="button" class="font-medium text-heading hover:underline" (click)="pick.emit(table.name); $event.preventDefault()" data-testid="input-name">{{ table.name }}</button>
                    </summary>
                    <ul class="mt-1 flex flex-col gap-0.5">
                      @for (column of table.columns; track column.name) {
                        <li class="flex flex-wrap items-baseline gap-x-2">
                          <button type="button" class="text-left font-mono text-xs text-heading hover:underline" (click)="pick.emit(column.name)" data-testid="input-name">{{ column.name }}</button>
                          <span class="text-xs text-body">{{ column.type }}</span>
                        </li>
                      }
                    </ul>
                  </details>
                </li>
              }
            </ul>
          </section>
        }

        @if (questions().length) {
          <tmz-disclosure [heading]="t('scripts.inputs.questions')" [status]="'(' + questions().length + ')'" [open]="!!filter()" data-testid="inputs-questions">
            <ul class="flex max-h-72 flex-col gap-0.5 overflow-y-auto">
              @for (question of questions(); track question.path) {
                <li class="flex flex-wrap items-baseline gap-x-2">
                  <button type="button" class="text-left font-mono text-xs text-heading hover:underline" (click)="pick.emit(question.name)" data-testid="input-name">{{ question.name }}</button>
                  <span class="text-xs text-body">{{ question.type }}</span>
                  @if (question.label) {
                    <span class="min-w-0 basis-full truncate text-xs text-body" [title]="question.label">{{ question.label }}</span>
                  }
                </li>
              }
            </ul>
          </tmz-disclosure>
        }

        @if (filter() && !tables().length && !referenceTables().length && !questions().length) {
          <p class="text-body" data-testid="inputs-no-match">{{ t('scripts.inputs.noMatch') }}</p>
        }
      }
    </div>
  `,
})
export class ScriptInputsPanel {
  /** Null while loading. */
  readonly inputs = input<ScriptInputs | null>(null);
  readonly failed = input(false);
  /** Whether activating a name inserts it (the editor can be edited) or copies it. */
  readonly canInsert = input(false);
  /** Where the reference tables are managed, shown as a link for whoever may; null for everyone else. */
  readonly manageLink = input<string[] | null>(null);
  readonly pick = output<string>();
  readonly refresh = output<void>();

  private readonly transloco = inject(TranslocoService);

  protected readonly filter = signal('');
  private readonly needle = computed(() => this.filter().trim().toLowerCase());

  /** The tables whose name, or a column's name or label, matches the filter; a table that matches by name shows every column. */
  protected readonly tables = computed<TableView[]>(() => this.narrow(this.inputs()?.tables ?? [], true));
  protected readonly referenceTables = computed<TableView[]>(() =>
    this.narrow((this.inputs()?.referenceTables ?? []).map((table) => ({ ...table, rows: null })), false),
  );
  protected readonly questions = computed<InputQuestion[]>(() => {
    const filter = this.needle();
    return (this.inputs()?.questions ?? []).filter((question) => !filter || matches(filter, question.name, question.label, question.path));
  });

  /** The key of a message about a count: the singular form when the count is 1. */
  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
  }

  private narrow(tables: { name: string; rows: number | null; columns: InputColumn[] }[], withLabels: boolean): TableView[] {
    const filter = this.needle();
    if (!filter) {
      return tables.map((table) => ({ ...table, open: false }));
    }
    return tables.flatMap((table) => {
      if (matches(filter, table.name)) {
        return [{ ...table, open: true }];
      }
      const columns = table.columns.filter((column) => matches(filter, column.name, withLabels ? column.label : null));
      return columns.length ? [{ ...table, columns, open: true }] : [];
    });
  }
}
