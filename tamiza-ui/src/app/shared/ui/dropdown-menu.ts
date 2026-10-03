import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, input, output } from '@angular/core';

export interface MenuItem {
  id: string;
  label: string;
  danger?: boolean;
}

/** Flowbite dropdown markup on the CDK menu: arrow keys move between items, Escape closes and returns focus. */
@Component({
  selector: 'tmz-dropdown-menu',
  imports: [CdkMenuTrigger, CdkMenu, CdkMenuItem],
  template: `
    <button
      type="button"
      [cdkMenuTriggerFor]="menu"
      [attr.aria-label]="label()"
      class="inline-flex items-center justify-center min-h-10 min-w-10 rounded-base text-body hover:bg-neutral-secondary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
      data-testid="menu-trigger"
    >
      <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="currentColor">
        <circle cx="12" cy="5" r="2" /><circle cx="12" cy="12" r="2" /><circle cx="12" cy="19" r="2" />
      </svg>
    </button>
    <ng-template #menu>
      <div cdkMenu class="z-10 min-w-44 py-2 bg-neutral-primary-medium border border-default-medium rounded-base shadow-lg">
        @for (item of items(); track item.id) {
          <button
            cdkMenuItem
            type="button"
            (cdkMenuItemTriggered)="selected.emit(item.id)"
            [class]="
              'block w-full text-left px-4 py-2 text-sm min-h-10 hover:bg-neutral-tertiary-medium focus:bg-neutral-tertiary-medium focus:outline-none ' +
              (item.danger ? 'text-fg-danger' : 'text-body hover:text-heading')
            "
          >
            {{ item.label }}
          </button>
        }
      </div>
    </ng-template>
  `,
})
export class DropdownMenu {
  readonly label = input.required<string>();
  readonly items = input.required<MenuItem[]>();
  readonly selected = output<string>();
}
