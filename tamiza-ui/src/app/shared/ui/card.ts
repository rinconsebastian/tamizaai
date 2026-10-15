import { Component, input } from '@angular/core';

/**
 * Chrome rule: a card frames a group of controls that belong together (a form, an action row with its status) or headline
 * figures. Lists, tables, logs and toolbars are plain sections with an h2 or a flex row, and a stat is a label and a value, never
 * a bordered box inside a card. A card inside a card is a sign the page is carrying more chrome than information.
 */
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
