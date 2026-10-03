import { Directive, computed, input } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
export type ButtonSize = 'sm' | 'md';

// Flowbite button styles (MIT). Kept on native <button>/<a> elements for keyboard and screen-reader support.
const base =
  'inline-flex items-center justify-center gap-2 box-border border font-medium leading-5 rounded-base shadow-xs ' +
  'focus:outline-none focus:ring-4 disabled:cursor-not-allowed disabled:opacity-50 min-h-10';
const variants: Record<ButtonVariant, string> = {
  primary: 'text-white bg-brand border-transparent hover:bg-brand-strong focus:ring-brand-medium',
  secondary:
    'text-body bg-neutral-secondary-medium border-default-medium hover:bg-neutral-tertiary-medium hover:text-heading focus:ring-neutral-tertiary',
  danger: 'text-white bg-danger border-transparent hover:bg-danger-strong focus:ring-danger-medium',
  ghost: 'text-body bg-transparent border-transparent shadow-none hover:bg-neutral-secondary-medium hover:text-heading focus:ring-neutral-tertiary',
};
const sizes: Record<ButtonSize, string> = { sm: 'text-sm px-3 py-2', md: 'text-sm px-4 py-2.5' };

@Directive({
  selector: 'button[tmzButton], a[tmzButton]',
  host: { '[class]': 'classes()' },
})
export class Button {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  protected readonly classes = computed(() => `${base} ${variants[this.variant()]} ${sizes[this.size()]}`);
}
