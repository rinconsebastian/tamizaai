import { Component, computed, input } from '@angular/core';
import { Tone } from './badge';

const tones: Record<Tone, string> = {
  brand: 'text-fg-brand-strong bg-brand-softer border-brand-subtle',
  neutral: 'text-heading bg-neutral-secondary-medium border-default',
  success: 'text-fg-success-strong bg-success-soft border-success-subtle',
  warning: 'text-fg-warning bg-warning-soft border-warning-subtle',
  danger: 'text-fg-danger-strong bg-danger-soft border-danger-subtle',
};

@Component({
  selector: 'tmz-alert',
  template: `
    @if (heading()) {
      <p class="font-medium">{{ heading() }}</p>
    }
    <div><ng-content /></div>
  `,
  host: { '[class]': 'classes()', '[attr.role]': 'role()' },
})
export class Alert {
  readonly tone = input<Tone>('brand');
  readonly heading = input<string | null>(null);
  /**
   * Set it on a notice the page inserts after an action (a dropped filter, republished tables) so screen readers hear it without
   * focus moving. A danger alert is always announced; a notice that is simply there when the page opens is not.
   */
  readonly announce = input(false);
  protected readonly role = computed(() => (this.tone() === 'danger' ? 'alert' : this.announce() ? 'status' : null));
  protected readonly classes = computed(() => `block p-4 text-sm rounded-base border ${tones[this.tone()]}`);
}
