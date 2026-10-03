import { Component, contentChild, effect, input } from '@angular/core';
import { Input } from './input';

/** Label, control and error text. The error is announced through aria-describedby on the control. */
@Component({
  selector: 'tmz-form-field',
  template: `
    <label [attr.for]="fieldId()" class="block mb-2 text-sm font-medium text-heading">{{ label() }}</label>
    <ng-content />
    @if (error(); as error) {
      <p [id]="fieldId() + '-error'" class="mt-2 text-sm text-fg-danger-strong" data-testid="field-error">{{ error }}</p>
    } @else if (hint()) {
      <p class="mt-2 text-sm text-body">{{ hint() }}</p>
    }
  `,
  host: { class: 'block' },
})
export class FormField {
  readonly label = input.required<string>();
  readonly fieldId = input.required<string>();
  readonly error = input<string | null | undefined>(null);
  readonly hint = input<string | null>(null);

  private readonly control = contentChild(Input);

  constructor() {
    effect(() => {
      const control = this.control();
      const hasError = !!this.error();
      control?.invalid.set(hasError);
      control?.describedBy.set(hasError ? `${this.fieldId()}-error` : null);
    });
  }
}
