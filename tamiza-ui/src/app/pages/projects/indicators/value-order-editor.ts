import { Component, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, Input } from '../../../shared/ui';
import { maxOrderValues } from '../../../core/projects/indicators.service';

let editors = 0;

/**
 * An ordered list of values edited with buttons (add-declared-category-order, design D9), made like the Likert scale editor: one row per
 * value with Move up, Move down and Remove (targets of 40 px that work from the keyboard, each named with its value), and a field that
 * adds a value, with a `<datalist>` of the values on offer. It is controlled: the parent holds the list and receives the new one in
 * `changed`. Values are exact text and are shown in full, with their numbers.
 *
 * `shown` is the values the chart draws. A listed value that is not among them (a script changed its values, or a dashboard filter hides
 * it) is listed apart as not shown, with only Remove, so the order never keeps a value unseen; the others move among themselves.
 */
@Component({
  selector: 'app-value-order-editor',
  imports: [TranslocoDirective, Button, Input],
  template: `
    <ng-container *transloco="let t">
      <div class="flex flex-col gap-2" [attr.data-testid]="testId()">
        <ul class="flex max-h-72 flex-col gap-2 overflow-y-auto" role="list">
          @for (item of visible(); track item.value; let first = $first; let last = $last) {
            <li class="flex items-center gap-2 rounded-base border border-default p-2" data-testid="order-item">
              <span class="min-w-0 flex-1 break-words text-sm text-heading" data-testid="order-value">{{ item.value }}</span>
              <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary disabled:cursor-not-allowed disabled:opacity-40" [disabled]="first" [attr.aria-label]="t('indicators.valueOrder.up', { value: item.value })" (click)="move(item.value, -1)" data-testid="order-up">
                <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 15 6-6 6 6" /></svg>
              </button>
              <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary disabled:cursor-not-allowed disabled:opacity-40" [disabled]="last" [attr.aria-label]="t('indicators.valueOrder.down', { value: item.value })" (click)="move(item.value, 1)" data-testid="order-down">
                <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6" /></svg>
              </button>
              <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary" [attr.aria-label]="t('indicators.valueOrder.remove', { value: item.value })" (click)="remove(item.value)" data-testid="order-remove">
                <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
              </button>
            </li>
          }
        </ul>
        @if (apart().length) {
          <div class="flex flex-col gap-2" data-testid="order-apart">
            <p class="text-sm text-body">{{ t('indicators.valueOrder.notShown') }}</p>
            <ul class="flex flex-col gap-2" role="list">
              @for (item of apart(); track item.value) {
                <li class="flex items-center gap-2 rounded-base border border-dashed border-default p-2" data-testid="order-apart-item">
                  <span class="min-w-0 flex-1 break-words text-sm text-body" data-testid="order-apart-value">{{ item.value }}</span>
                  <button type="button" class="inline-flex min-h-10 min-w-10 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary" [attr.aria-label]="t('indicators.valueOrder.remove', { value: item.value })" (click)="remove(item.value)" data-testid="order-apart-remove">
                    <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
                  </button>
                </li>
              }
            </ul>
          </div>
        }
        <div class="flex flex-col gap-2 sm:flex-row sm:items-end">
          <div class="min-w-0 sm:w-64">
            <label [attr.for]="fieldId" class="mb-2 block text-sm text-body">{{ t('indicators.valueOrder.addLabel') }}</label>
            <input tmzInput autocomplete="off" [attr.id]="fieldId" [attr.list]="listId" [placeholder]="t('indicators.valueOrder.addPlaceholder')" [value]="typed()" (input)="typed.set($any($event.target).value)" (keydown.enter)="$event.preventDefault(); add()" data-testid="order-new" />
            <datalist [attr.id]="listId">
              @for (value of suggestions(); track value) {
                <option [value]="value"></option>
              }
            </datalist>
          </div>
          <button tmzButton variant="secondary" type="button" [disabled]="full()" (click)="add()" data-testid="order-add">{{ t('indicators.valueOrder.add') }}</button>
        </div>
        @if (full()) {
          <p class="text-sm text-body" data-testid="order-full">{{ t('indicators.valueOrder.full', { count: max }) }}</p>
        }
      </div>
    </ng-container>
  `,
})
export class ValueOrderEditor {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** The values of the order, in order, in full. */
  readonly values = input.required<readonly string[]>();
  /** The values the chart draws; null while they are not known, when every listed value moves. */
  readonly shown = input<readonly string[] | null>(null);
  /** The values the data holds, offered when adding one; any other can be typed. */
  readonly offered = input<readonly string[]>([]);
  readonly testId = input('value-order');
  readonly changed = output<string[]>();

  protected readonly max = maxOrderValues;
  private readonly id = ++editors;
  protected readonly fieldId = `valueOrderNew${this.id}`;
  protected readonly listId = `valueOrderList${this.id}`;
  protected readonly typed = signal('');

  private readonly entries = computed(() => {
    const shown = this.shown() ? new Set(this.shown()) : null;
    return this.values().map((value) => ({ value, apart: shown !== null && !shown.has(value) }));
  });
  /** The listed values the chart draws (all of them while the drawn values are not known), in order. */
  protected readonly visible = computed(() => this.entries().filter((entry) => !entry.apart));
  /** The listed values the chart does not draw. */
  protected readonly apart = computed(() => this.entries().filter((entry) => entry.apart));
  protected readonly full = computed(() => this.values().length >= maxOrderValues);
  protected readonly suggestions = computed(() => {
    const listed = new Set(this.values());
    return this.offered().filter((value) => !listed.has(value));
  });

  /** Moves a value one place among the values the chart draws; the values not drawn keep their places. */
  protected move(value: string, by: -1 | 1): void {
    const values = [...this.values()];
    const drawn = this.visible().map((entry) => entry.value);
    const at = drawn.indexOf(value);
    const other = drawn[at + by];
    if (at === -1 || other === undefined) {
      return;
    }
    const from = values.indexOf(value);
    const to = values.indexOf(other);
    [values[from], values[to]] = [values[to], values[from]];
    this.changed.emit(values);
    // The rows are redrawn in place, so the keyboard would stay on the row and act on whatever value moved into it: it follows the value
    // instead, on the same button, or on the other one when the value reached an end and that button is off.
    this.focus(value, by > 0 ? ['order-down', 'order-up'] : ['order-up', 'order-down']);
  }

  protected remove(value: string): void {
    const values = this.values();
    const at = this.visible().findIndex((entry) => entry.value === value);
    this.changed.emit(values.filter((item) => item !== value));
    // The button that was pressed is gone with its row: the keyboard goes to the remove button of the value that took its place, or of
    // the one before it, and with none left to the field that adds one.
    const drawn = this.visible().map((entry) => entry.value).filter((item) => item !== value);
    this.focus(drawn[Math.min(Math.max(at, 0), drawn.length - 1)], ['order-remove']);
  }

  protected add(): void {
    const value = this.typed();
    if (value === '' || this.values().includes(value) || this.full()) {
      return;
    }
    this.typed.set('');
    // The bound value only reaches the field when a render saw it change, so the field is cleared itself as well.
    const field = this.host.nativeElement.querySelector<HTMLInputElement>('#' + this.fieldId);
    if (field) {
      field.value = '';
    }
    this.changed.emit([...this.values(), value]);
  }

  /** Once the list is drawn again, puts the keyboard on the first enabled control of `testIds` in the row of `value`, or on the field that adds one. */
  private focus(value: string | undefined, testIds: readonly string[]): void {
    afterNextRender(
      () => {
        const rows = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[data-testid="order-item"]'));
        const row = value === undefined ? undefined : rows.find((candidate) => candidate.querySelector('[data-testid="order-value"]')?.textContent === value);
        for (const id of testIds) {
          const control = row?.querySelector<HTMLButtonElement>(`[data-testid="${id}"]`);
          if (control && !control.disabled) {
            control.focus();
            return;
          }
        }
        this.host.nativeElement.querySelector<HTMLElement>(`#${this.fieldId}`)?.focus();
      },
      { injector: this.injector },
    );
  }
}
