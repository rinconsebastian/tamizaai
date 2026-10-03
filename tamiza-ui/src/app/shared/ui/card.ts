import { Component, input } from '@angular/core';

@Component({
  selector: 'tmz-card',
  template: `
    @if (heading()) {
      <h2 class="mb-3 text-lg font-semibold text-heading">{{ heading() }}</h2>
    }
    <ng-content />
  `,
  host: { class: 'block bg-neutral-primary-soft p-4 sm:p-6 border border-default rounded-base shadow-xs' },
})
export class Card {
  readonly heading = input<string | null>(null);
}
