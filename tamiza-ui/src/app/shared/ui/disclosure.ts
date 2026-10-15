import { Component, input, output } from '@angular/core';

/**
 * A section that opens on request, on native `<details>`: Enter and Space toggle it from the summary and the browser exposes the
 * expanded state. The heading sits in the summary inside an `h3`, so heading navigation lists the groups.
 *
 * `open` sets the starting state. Pages that open a group because it needs attention decide that once, when their data first
 * arrives, and never on a refresh, so a group does not close or open while someone is reading it.
 */
@Component({
  selector: 'tmz-disclosure',
  template: `
    <details class="group" [open]="open()" (toggle)="toggled.emit($any($event.target).open)">
      <summary
        class="flex min-h-10 cursor-pointer list-none items-center gap-2 rounded-base text-heading focus:outline-none focus-visible:ring-4 focus-visible:ring-neutral-tertiary [&::-webkit-details-marker]:hidden"
      >
        <svg aria-hidden="true" class="size-4 shrink-0 text-body transition-transform group-open:rotate-90" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="m9 6 6 6-6 6" />
        </svg>
        <h3 class="text-base font-medium">{{ heading() }}</h3>
        @if (status()) {
          <span class="text-sm font-normal text-body">{{ status() }}</span>
        }
      </summary>
      <div class="pt-3">
        <ng-content />
      </div>
    </details>
  `,
  host: { class: 'block' },
})
export class Disclosure {
  readonly heading = input.required<string>();
  /** A short line after the heading that says what is inside, so the closed group is still informative. */
  readonly status = input<string | null>(null);
  readonly open = input(false);
  readonly toggled = output<boolean>();
}
