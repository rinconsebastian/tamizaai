import { Component, contentChild, effect, input } from '@angular/core';
import { ApiLang } from './api-text';
import { Input } from './input';

/** Label, control and error text. The error is announced through aria-describedby on the control. */
@Component({
  selector: 'tmz-form-field',
  imports: [ApiLang],
  template: `
    <label [attr.for]="fieldId()" class="block mb-2 text-sm font-medium text-heading">{{ label() }}</label>
    <ng-content />
    @if (error(); as error) {
      <p [id]="fieldId() + '-error'" class="mt-2 text-sm text-fg-danger-strong" data-testid="field-error" [tmzApiLang]="error">{{ error }}</p>
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
  /** Another element that describes the control, such as a notice the form shows beside it: named after the error, which comes first. */
  readonly alsoDescribedBy = input<string | null>(null);

  private readonly control = contentChild(Input);

  constructor() {
    effect(() => {
      const control = this.control();
      const hasError = !!this.error();
      control?.invalid.set(hasError);
      const ids = [hasError ? `${this.fieldId()}-error` : null, this.alsoDescribedBy()].filter((id): id is string => !!id);
      control?.describedBy.set(ids.length ? ids.join(' ') : null);
    });
  }
}
