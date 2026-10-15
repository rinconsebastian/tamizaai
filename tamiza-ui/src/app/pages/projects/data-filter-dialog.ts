import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Observable, Subject, catchError, debounceTime, distinctUntilChanged, merge, of, switchMap, tap } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { DataColumn, DataFilter, FilterOp } from '../../core/projects/data.service';
import { Alert, Button, FormField, Input } from '../../shared/ui';
import { operationsFor } from './data-view';

export interface DataFilterDialogData {
  column: DataColumn;
  /** The column's current filter, to edit; null for a new one. */
  filter: DataFilter | null;
  timeZone: string;
  /** The most frequent values of a text column, only those that contain `search` when it is not empty. */
  loadValues: (search: string) => Observable<{ value: string; count: number | null }[]>;
  /** Asks the server whether the filter is accepted; it fails with the API's error when it is not. */
  check: (filter: DataFilter) => Observable<unknown>;
  /** For tables that cannot list a column's values: the text typed in the search box is offered as a value to choose. */
  allowTypedValues?: boolean;
}

/** What the dialog closes with: the filter to apply, or the order to remove the column's filter. */
export type DataFilterDialogResult = { filter: DataFilter } | { remove: true };

/** Debounce for the value search: one request once typing stops. */
export const valueSearchDelayMs = 300;

/** Chooses the condition and values of one column's filter. Full screen on phones, a panel from `md` up. */
@Component({
  selector: 'app-data-filter-dialog',
  imports: [TranslocoDirective, Alert, Button, FormField, Input],
  template: `
    <form
      class="flex h-dvh w-screen flex-col overflow-y-auto bg-neutral-primary-soft p-4 md:h-auto md:max-h-[calc(100dvh-2rem)] md:w-[min(34rem,calc(100vw-2rem))] md:rounded-base md:border md:border-default md:p-6 md:shadow-sm"
      novalidate
      (submit)="$event.preventDefault(); apply()"
      *transloco="let t"
      data-testid="filter-dialog"
    >
      <h2 id="data-filter-title" class="break-words text-lg font-medium text-heading">{{ t('cleanData.filter.title', { name: data.column.name }) }}</h2>
      @if (data.column.label) {
        <p class="mt-1 break-words text-sm text-body">{{ data.column.label }}</p>
      }

      <tmz-form-field class="mt-4" [label]="t('cleanData.filter.operation')" fieldId="filterOp">
        <select tmzInput id="filterOp" [value]="op()" (change)="setOp($any($event.target).value)" data-testid="filter-op">
          @for (option of operations; track option) {
            <option [value]="option" [selected]="option === op()">{{ t('cleanData.filter.ops.' + option) }}</option>
          }
        </select>
      </tmz-form-field>

      @switch (op()) {
        @case ('contains') {
          <tmz-form-field class="mt-4" [label]="t('cleanData.filter.value')" fieldId="filterValue" [error]="fieldError()">
            <input tmzInput id="filterValue" autocomplete="off" maxlength="500" [value]="text()" (input)="text.set($any($event.target).value)" data-testid="filter-value" />
          </tmz-form-field>
        }
        @case ('equals') {
          <tmz-form-field class="mt-4" [label]="t('cleanData.filter.value')" fieldId="filterValue" [error]="fieldError()">
            <input tmzInput id="filterValue" autocomplete="off" maxlength="500" [value]="text()" (input)="text.set($any($event.target).value)" data-testid="filter-value" />
          </tmz-form-field>
        }
        @case ('in') {
          <fieldset class="mt-4 min-w-0" data-testid="filter-values">
            <legend class="mb-2 text-sm font-medium text-heading">{{ t('cleanData.filter.values') }}</legend>
            @if (isText()) {
              <input
                tmzInput
                type="search"
                autocomplete="off"
                [attr.aria-label]="t('cleanData.filter.search')"
                [placeholder]="t('cleanData.filter.search')"
                [value]="search()"
                (input)="searchTyped($any($event.target).value)"
                data-testid="filter-search"
              />
              @if (listed().length >= maxValues) {
                <p class="mt-2 text-xs text-body" data-testid="filter-values-help">{{ t('cleanData.filter.valuesHelp', { count: maxValues }) }}</p>
              }
            }
            <ul class="mt-2 max-h-60 divide-y divide-default overflow-y-auto rounded-base border border-default" data-testid="filter-value-list">
              @if (loading()) {
                <li class="px-3 py-2 text-sm text-body">{{ t('cleanData.filter.loadingValues') }}</li>
              } @else if (!options().length) {
                <li class="px-3 py-2 text-sm text-body" data-testid="filter-no-values">{{ t('cleanData.filter.noValues') }}</li>
              }
              @for (option of options(); track option.value) {
                <li>
                  <label class="flex min-h-10 cursor-pointer items-start gap-3 px-3 py-2 text-sm">
                    <input type="checkbox" class="mt-0.5 size-4 shrink-0" [checked]="isSelected(option.value)" (change)="toggle(option.value)" data-testid="filter-option" />
                    <span class="min-w-0 flex-1 break-words text-heading">{{ optionLabel(option.value, t) }}</span>
                    @if (option.count !== null) {
                      <span class="shrink-0 tabular-nums text-body">{{ option.count }}</span>
                    }
                  </label>
                </li>
              }
            </ul>
            <p class="mt-2 text-sm text-body" data-testid="filter-selected">{{ t('cleanData.filter.selected', { count: selected().length }) }}</p>
            @if (fieldError(); as message) {
              <p class="mt-1 text-sm text-fg-danger-strong" data-testid="field-error">{{ message }}</p>
            }
          </fieldset>
        }
        @case ('range') {
          <div class="mt-4 grid gap-4 sm:grid-cols-2">
            <tmz-form-field [label]="t('cleanData.filter.min')" fieldId="filterMin" [error]="fieldError()">
              <input tmzInput id="filterMin" [type]="rangeInputType()" [attr.step]="rangeStep()" [value]="min()" (input)="min.set($any($event.target).value)" data-testid="filter-min" />
            </tmz-form-field>
            <tmz-form-field [label]="t('cleanData.filter.max')" fieldId="filterMax">
              <input tmzInput id="filterMax" [type]="rangeInputType()" [attr.step]="rangeStep()" [value]="max()" (input)="max.set($any($event.target).value)" data-testid="filter-max" />
            </tmz-form-field>
          </div>
          @if (data.column.type === 'timestamp') {
            <p class="mt-2 text-sm text-body" data-testid="filter-days">{{ t('cleanData.filter.days', { zone: data.timeZone }) }}</p>
          }
        }
      }

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="filter-error">{{ message }}</tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()">{{ t('common.cancel') }}</button>
        @if (data.filter) {
          <button tmzButton variant="secondary" type="button" (click)="ref.close({ remove: true })" data-testid="filter-remove">{{ t('cleanData.filter.remove') }}</button>
        }
        <button tmzButton type="submit" [disabled]="busy()" data-testid="filter-apply">{{ busy() ? t('cleanData.filter.checking') : t('cleanData.filter.apply') }}</button>
      </div>
    </form>
  `,
})
export class DataFilterDialog {
  protected readonly data = inject<DataFilterDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DataFilterDialogResult>>(DialogRef);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);

  protected readonly maxValues = 50;
  protected readonly operations: FilterOp[] = operationsFor(this.data.column);
  protected readonly isText = computed(() => this.data.column.type === 'text');

  protected readonly op = signal<FilterOp>(this.data.filter?.op ?? this.operations[0]);
  protected readonly text = signal(this.data.filter?.value ?? '');
  protected readonly min = signal(this.data.filter?.min ?? '');
  protected readonly max = signal(this.data.filter?.max ?? '');
  protected readonly selected = signal<string[]>(this.data.filter?.values ?? []);
  protected readonly search = signal('');

  protected readonly loading = signal(false);
  protected readonly listed = signal<{ value: string; count: number | null }[]>([]);
  protected readonly busy = signal(false);
  protected readonly fieldError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  private readonly first = new Subject<string>();
  private readonly searches = new Subject<string>();
  private valuesRequested = false;

  /** What the list shows: a text column's frequent values, a true/false column's two values; chosen values the list lacks stay on top. */
  protected readonly options = computed<{ value: string; count: number | null }[]>(() => {
    if (this.data.column.type === 'boolean') {
      return [{ value: 'true', count: null }, { value: 'false', count: null }];
    }
    const listed = this.listed();
    const known = new Set(listed.map((v) => v.value));
    const missing = this.search() ? [] : this.selected().filter((v) => !known.has(v)).map((value) => ({ value, count: null }));
    const typed = (this.search() || '').trim();
    const offered = this.data.allowTypedValues && typed && !known.has(typed) ? [{ value: typed, count: null }] : [];
    return [...offered, ...missing, ...listed];
  });

  constructor() {
    if (this.data.column.type === 'text') {
      // The first list comes at once, when "is one of" is first chosen; later ones once typing stops, and only the last search counts.
      merge(this.first, this.searches.pipe(debounceTime(valueSearchDelayMs), distinctUntilChanged()))
        .pipe(
          tap(() => this.loading.set(true)),
          switchMap((search) => this.data.loadValues(search).pipe(catchError(() => of<{ value: string; count: number | null }[]>([])))),
          takeUntilDestroyed(inject(DestroyRef)),
        )
        .subscribe((values) => {
          this.listed.set(values);
          this.loading.set(false);
        });
    }
    if (this.op() === 'in') {
      this.requestValues();
    }
  }

  private requestValues(): void {
    if (this.data.column.type === 'text' && !this.valuesRequested) {
      this.valuesRequested = true;
      this.first.next('');
    }
  }

  protected setOp(value: string): void {
    this.op.set(value as FilterOp);
    if (value === 'in') {
      this.requestValues();
    }
    this.fieldError.set(null);
    this.error.set(null);
  }

  protected searchTyped(value: string): void {
    this.search.set(value);
    this.searches.next(value.trim());
  }

  protected isSelected(value: string): boolean {
    return this.selected().includes(value);
  }

  protected toggle(value: string): void {
    this.selected.update((list) => (list.includes(value) ? list.filter((v) => v !== value) : [...list, value]));
  }

  protected optionLabel(value: string, t: (key: string) => string): string {
    if (this.data.column.type === 'boolean') {
      return t(`cleanData.${value}`);
    }
    return value === '' ? t('cleanData.filter.emptyText') : value;
  }

  protected rangeInputType(): string {
    return this.data.column.type === 'integer' || this.data.column.type === 'decimal' ? 'number' : 'date';
  }

  protected rangeStep(): string | null {
    return this.data.column.type === 'integer' ? '1' : this.data.column.type === 'decimal' ? 'any' : null;
  }

  protected apply(): void {
    this.fieldError.set(null);
    this.error.set(null);
    const filter = this.build();
    if (!filter) {
      return;
    }
    this.busy.set(true);
    this.data.check(filter).subscribe({
      next: () => this.ref.close({ filter }),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        const message = Object.entries(parsed.fieldErrors).find(([field]) => field.startsWith('filters['))?.[1];
        if (message) {
          this.fieldError.set(message);
        } else {
          this.error.set(parsed.message);
        }
      },
    });
  }

  /** The filter the inputs describe, or null (with the reason shown next to the field) when they are incomplete. */
  private build(): DataFilter | null {
    const column = this.data.column.name;
    const t = (key: string) => this.transloco.translate(key);
    switch (this.op()) {
      case 'contains':
      case 'equals': {
        if (this.text() === '') {
          this.fieldError.set(t('cleanData.filter.needValue'));
          return null;
        }
        return { column, op: this.op(), value: this.text() };
      }
      case 'in': {
        if (!this.selected().length) {
          this.fieldError.set(t('cleanData.filter.needValues'));
          return null;
        }
        return { column, op: 'in', values: [...this.selected()] };
      }
      case 'range': {
        if (this.min() === '' && this.max() === '') {
          this.fieldError.set(t('cleanData.filter.needRange'));
          return null;
        }
        return { column, op: 'range', ...(this.min() !== '' ? { min: this.min() } : {}), ...(this.max() !== '' ? { max: this.max() } : {}) };
      }
      default:
        return { column, op: this.op() };
    }
  }
}
