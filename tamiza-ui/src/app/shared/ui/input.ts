import { Directive, computed, signal } from '@angular/core';

const base = 'block w-full border text-heading text-sm rounded-base px-3 py-2.5 shadow-xs placeholder:text-body min-h-10';
const normal = 'bg-neutral-secondary-medium border-default-medium focus:ring-brand focus:border-brand';
const invalid = 'bg-danger-soft border-danger-subtle text-fg-danger-strong focus:ring-danger focus:border-danger';

/** Flowbite input styles for native inputs, selects and textareas. `tmz-form-field` sets the error state. */
@Directive({
  selector: 'input[tmzInput], select[tmzInput], textarea[tmzInput]',
  host: {
    '[class]': 'classes()',
    '[attr.aria-invalid]': 'invalid() ? "true" : null',
    '[attr.aria-describedby]': 'describedBy()',
  },
})
export class Input {
  readonly invalid = signal(false);
  readonly describedBy = signal<string | null>(null);
  protected readonly classes = computed(() => `${base} ${this.invalid() ? invalid : normal}`);
}
