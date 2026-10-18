import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, input, output } from '@angular/core';

export interface MenuItem {
  id: string;
  label: string;
  danger?: boolean;
  /** Announced as unavailable and still reached with the arrow keys, so its description can be read; it cannot be chosen. */
  disabled?: boolean;
  /** A second line under the label, read as the item's description: why a disabled item is unavailable, for instance. */
  description?: string;
  /** Lets tests and scripts find the item in the menu's overlay. */
  testId?: string;
}

const itemBase = 'block w-full text-left px-4 py-2 text-sm min-h-10 focus:bg-neutral-tertiary-medium focus:outline-none aria-disabled:opacity-50 aria-disabled:cursor-not-allowed';

/**
 * Flowbite dropdown markup on the CDK menu: arrow keys move between items, Escape closes and returns focus. The chosen item is
 * emitted once the menu has closed and its trigger has focus again, so a dialog opened on the choice gives focus back to the
 * trigger when it closes, not to the item the menu's overlay removed (design D12 of add-multiple-dashboards).
 */
@Component({
  selector: 'tmz-dropdown-menu',
  imports: [CdkMenuTrigger, CdkMenu, CdkMenuItem],
  template: `
    <button
      type="button"
      [cdkMenuTriggerFor]="menu"
      (cdkMenuClosed)="closed()"
      [attr.aria-label]="label()"
      class="inline-flex items-center justify-center min-h-10 min-w-10 rounded-base text-body hover:bg-neutral-secondary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
      [attr.data-testid]="triggerTestId()"
    >
      <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="currentColor">
        <circle cx="12" cy="5" r="2" /><circle cx="12" cy="12" r="2" /><circle cx="12" cy="19" r="2" />
      </svg>
    </button>
    <ng-template #menu>
      <div cdkMenu class="z-10 min-w-44 max-w-[calc(100vw-2rem)] py-2 bg-neutral-primary-medium border border-default-medium rounded-base shadow-lg">
        @for (item of items(); track item.id) {
          <!-- With a description, the label alone names the item and the second line describes it. -->
          <button
            cdkMenuItem
            type="button"
            [cdkMenuItemDisabled]="!!item.disabled"
            (cdkMenuItemTriggered)="chosen = item.id"
            [attr.aria-label]="item.description ? item.label : null"
            [attr.aria-describedby]="item.description ? descriptionId(item) : null"
            [attr.data-testid]="item.testId ?? null"
            [class]="itemClasses(item)"
          >
            {{ item.label }}
            @if (item.description) {
              <span [id]="descriptionId(item)" class="mt-0.5 block text-xs font-normal text-body" data-testid="menu-item-description">{{ item.description }}</span>
            }
          </button>
        }
      </div>
    </ng-template>
  `,
})
export class DropdownMenu {
  private static nextId = 0;

  readonly label = input.required<string>();
  readonly items = input.required<MenuItem[]>();
  /** Lets tests and scripts tell this menu's button from the other menus' on the page. */
  readonly triggerTestId = input('menu-trigger');
  readonly selected = output<string>();

  /** The item chosen while the menu is open; emitted once it has closed. */
  protected chosen: string | null = null;
  private readonly idPrefix = `tmz-menu-${DropdownMenu.nextId++}`;

  protected descriptionId(item: MenuItem): string {
    return `${this.idPrefix}-${item.id}-description`;
  }

  protected itemClasses(item: MenuItem): string {
    // A disabled item keeps the focus background, as the arrow keys still reach it, and gets no hover colour.
    const hover = item.disabled ? '' : item.danger ? ' hover:bg-neutral-tertiary-medium' : ' hover:bg-neutral-tertiary-medium hover:text-heading';
    return `${itemBase}${hover} ${item.danger ? 'text-fg-danger' : 'text-body'}`;
  }

  protected closed(): void {
    const chosen = this.chosen;
    this.chosen = null;
    if (chosen !== null) {
      // The menu says it closed before it puts focus back on its trigger, in the same task: the choice waits for that.
      queueMicrotask(() => this.selected.emit(chosen));
    }
  }
}
