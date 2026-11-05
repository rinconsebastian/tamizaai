import { Component, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, Input } from '../../shared/ui';

let editors = 0;

/**
 * A short list of values edited as chips (add-small-sample-warnings, design D1): the values that mean no data in a column. Each value is a
 * chip with a button that removes it (a target of 40 px, named with its value), and a field adds one when Enter is pressed or Add is
 * chosen. Values are exact text, shown in full: nothing is trimmed, a value already listed or an empty one is not added, and no more than
 * `max` fit. It is controlled: the parent holds the list and receives the new one in `changed`.
 */
@Component({
  selector: 'app-value-chips',
  imports: [TranslocoDirective, Button, Input],
  template: `
    <ng-container *transloco="let t">
      <div class="flex flex-col gap-2" [attr.data-testid]="testId()">
        @if (values().length) {
          <ul class="flex flex-wrap items-center gap-2" role="list">
            @for (value of values(); track value) {
              <li class="inline-flex max-w-full items-center rounded-full border border-brand-subtle bg-brand-softer text-sm text-fg-brand-strong" data-testid="chip">
                <span class="min-w-0 break-words px-3 py-1 [overflow-wrap:anywhere]" data-testid="chip-value">{{ value }}</span>
                @if (!readonly()) {
                  <button
                    type="button"
                    class="min-h-9 min-w-9 rounded-e-full px-2 hover:bg-brand-soft focus:outline-none focus:ring-4 focus:ring-brand-medium"
                    [attr.aria-label]="t('scripts.columns.chips.remove', { value })"
                    (click)="remove(value)"
                    data-testid="chip-remove"
                  >
                    <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
                  </button>
                }
              </li>
            }
          </ul>
        }
        @if (!readonly()) {
          <div class="flex flex-col gap-2 sm:flex-row sm:items-end">
            <div class="min-w-0 sm:w-64">
              <label [attr.for]="fieldId" class="mb-2 block text-sm text-body">{{ t('scripts.columns.chips.addLabel') }}</label>
              <input
                tmzInput
                autocomplete="off"
                [attr.id]="fieldId"
                [attr.maxlength]="maxLength()"
                [placeholder]="t('scripts.columns.chips.addPlaceholder')"
                [value]="typed()"
                (input)="typed.set($any($event.target).value)"
                (keydown.enter)="$event.preventDefault(); add()"
                data-testid="chip-new"
              />
            </div>
            <button tmzButton variant="secondary" type="button" [disabled]="full()" (click)="add()" data-testid="chip-add">{{ t('scripts.columns.chips.add') }}</button>
          </div>
        }
        @if (full()) {
          <p class="text-sm text-body" data-testid="chips-full">{{ t('scripts.columns.chips.full', { count: max() }) }}</p>
        }
      </div>
    </ng-container>
  `,
})
export class ValueChips {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** The values listed, in the order they were added. */
  readonly values = input.required<readonly string[]>();
  /** The most values the list holds. */
  readonly max = input.required<number>();
  /** The longest value, in characters. */
  readonly maxLength = input.required<number>();
  readonly readonly = input(false);
  readonly testId = input('value-chips');
  readonly changed = output<string[]>();

  private readonly id = ++editors;
  protected readonly fieldId = `valueChipsNew${this.id}`;
  protected readonly typed = signal('');
  protected readonly full = computed(() => this.values().length >= this.max());

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

  protected remove(value: string): void {
    const values = this.values();
    const at = values.indexOf(value);
    this.changed.emit(values.filter((item) => item !== value));
    // The button that was pressed goes with its chip: the keyboard moves to the remove button of the chip that took its place, or of the one
    // before it, and with none left to the field that adds one.
    const left = values.filter((item) => item !== value);
    const next = left[Math.min(Math.max(at, 0), left.length - 1)];
    afterNextRender(
      () => {
        const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[data-testid="chip"]'));
        const chip = next === undefined ? undefined : chips.find((candidate) => candidate.querySelector('[data-testid="chip-value"]')?.textContent === next);
        const button = chip?.querySelector<HTMLButtonElement>('[data-testid="chip-remove"]');
        (button ?? this.host.nativeElement.querySelector<HTMLElement>('#' + this.fieldId))?.focus();
      },
      { injector: this.injector },
    );
  }
}
