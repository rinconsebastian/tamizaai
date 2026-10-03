import { Component, computed, input } from '@angular/core';

export type Tone = 'brand' | 'neutral' | 'success' | 'warning' | 'danger';

const tones: Record<Tone, string> = {
  brand: 'bg-brand-softer text-fg-brand-strong border-brand-subtle',
  neutral: 'bg-neutral-secondary-medium text-heading border-default',
  success: 'bg-success-soft text-fg-success-strong border-success-subtle',
  warning: 'bg-warning-soft text-fg-warning border-warning-subtle',
  danger: 'bg-danger-soft text-fg-danger-strong border-danger-subtle',
};

@Component({
  selector: 'tmz-badge',
  template: '<ng-content />',
  host: { '[class]': 'classes()' },
})
export class Badge {
  readonly tone = input<Tone>('neutral');
  protected readonly classes = computed(
    () => `inline-flex items-center border text-xs font-medium px-2 py-0.5 rounded-sm ${tones[this.tone()]}`,
  );
}
