import { NgTemplateOutlet } from '@angular/common';
import { CdkMenu, CdkMenuGroup, CdkMenuItem, CdkMenuItemCheckbox, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, computed, input, output } from '@angular/core';

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
  /**
   * Makes the item a checkbox (`menuitemcheckbox`) that is checked or not, such as a view that is on. Choosing it still closes
   * the menu and emits the item's id: the caller flips the state it passes back in.
   */
  checked?: boolean;
}

/** Items that belong together, set apart from the next group by a line and, with a `label`, named by a heading. */
export interface MenuGroup {
  id: string;
  /** Names the group to assistive technology and shows as a heading. The arrow keys never stop on the heading. */
  label?: string;
  items: MenuItem[];
}

const itemBase = 'block w-full text-left px-4 py-2 text-sm min-h-10 focus:bg-neutral-tertiary-medium focus:outline-none aria-disabled:opacity-50 aria-disabled:cursor-not-allowed';

/**
 * Flowbite dropdown markup on the CDK menu: arrow keys move between items, Escape closes and returns focus. The chosen item is
 * emitted once the menu has closed and its trigger has focus again, so a dialog opened on the choice gives focus back to the
 * trigger when it closes, not to the item the menu's overlay removed (design D12 of add-multiple-dashboards).
 *
 * `items` is a flat list. `groups` (instead of `items`) sets the items in named groups with separators between them, and an item
 * with `checked` is a checkbox (design D2 of unify-widget-menu-and-ten-dashboards).
 */
@Component({
  selector: 'tmz-dropdown-menu',
  imports: [CdkMenuTrigger, CdkMenu, CdkMenuGroup, CdkMenuItem, CdkMenuItemCheckbox, NgTemplateOutlet],
  template: `
    <button
      type="button"
      [cdkMenuTriggerFor]="menu"
      (cdkMenuClosed)="closed()"
      [attr.aria-label]="label()"
      [attr.aria-busy]="busy() ? 'true' : null"
      class="inline-flex items-center justify-center min-h-10 min-w-10 rounded-base text-body hover:bg-neutral-secondary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
      [attr.data-testid]="triggerTestId()"
    >
      @if (icon() === 'download') {
        <svg aria-hidden="true" class="size-5" [class.animate-pulse]="busy()" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" data-icon="download">
          <path d="M4 16v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2M12 4v12m0 0 4-4m-4 4-4-4" />
        </svg>
      } @else {
        <svg aria-hidden="true" class="size-5" [class.animate-pulse]="busy()" viewBox="0 0 24 24" fill="currentColor" data-icon="more">
          <circle cx="12" cy="5" r="2" /><circle cx="12" cy="12" r="2" /><circle cx="12" cy="19" r="2" />
        </svg>
      }
    </button>
    <ng-template #menu>
      <div cdkMenu #stack="cdkMenu" class="z-10 min-w-44 max-w-[calc(100vw-2rem)] py-2 bg-neutral-primary-medium border border-default-medium rounded-base shadow-lg">
        <!-- One item. Declared inside the menu, so the CDK finds it wherever it is drawn. -->
        <ng-template #entry let-item>
          <!-- With a description, the label alone names the item and the second line describes it. -->
          @if (item.checked !== undefined) {
            <button
              cdkMenuItemCheckbox
              type="button"
              [cdkMenuItemChecked]="item.checked"
              [cdkMenuItemDisabled]="!!item.disabled"
              (cdkMenuItemTriggered)="choose(item, stack)"
              [attr.aria-label]="item.description ? item.label : null"
              [attr.aria-describedby]="item.description ? descriptionId(item) : null"
              [attr.data-testid]="item.testId ?? null"
              [class]="itemClasses(item) + ' flex items-center gap-2'"
            >
              <svg aria-hidden="true" class="size-4 shrink-0" [class.invisible]="!item.checked" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" data-icon="check">
                <path d="m5 12 5 5L20 7" />
              </svg>
              <span class="min-w-0">
                {{ item.label }}
                @if (item.description) {
                  <span [id]="descriptionId(item)" class="mt-0.5 block text-xs font-normal text-body" data-testid="menu-item-description">{{ item.description }}</span>
                }
              </span>
            </button>
          } @else {
            <button
              cdkMenuItem
              type="button"
              [cdkMenuItemDisabled]="!!item.disabled"
              (cdkMenuItemTriggered)="choose(item, stack)"
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
        </ng-template>

        @if (groups().length) {
          @for (group of shownGroups(); track group.id; let first = $first) {
            @if (!first) {
              <!-- A click on a line is not on an item: it must not take the focus out of the menu, which would close it. -->
              <div role="separator" class="my-1 border-t border-default-medium" (mousedown)="$event.preventDefault()"></div>
            }
            <div cdkMenuGroup [attr.aria-label]="group.label ?? null">
              @if (group.label) {
                <div aria-hidden="true" class="px-4 pb-1 pt-2 text-xs font-medium text-body" (mousedown)="$event.preventDefault()" data-testid="menu-group-heading">{{ group.label }}</div>
              }
              @for (item of group.items; track item.id) {
                <ng-container *ngTemplateOutlet="entry; context: { $implicit: item }" />
              }
            </div>
          }
        } @else {
          @for (item of items(); track item.id) {
            <ng-container *ngTemplateOutlet="entry; context: { $implicit: item }" />
          }
        }
      </div>
    </ng-template>
  `,
})
export class DropdownMenu {
  private static nextId = 0;

  readonly label = input.required<string>();
  /** A flat list of items. Left out when `groups` is given. */
  readonly items = input<MenuItem[]>([]);
  /** Items in named groups. A group with no item is not shown, and a line is drawn between the groups that are. */
  readonly groups = input<MenuGroup[]>([]);
  /** Lets tests and scripts tell this menu's button from the other menus' on the page. */
  readonly triggerTestId = input('menu-trigger');
  /** The trigger's icon: the three dots of an actions menu, or a download arrow for an export menu. */
  readonly icon = input<'more' | 'download'>('more');
  /**
   * An action started from this menu is still running: the trigger pulses and says it is busy. The menu still opens, so what does
   * not depend on that action stays usable; the caller marks the items that do as disabled.
   */
  readonly busy = input(false);
  readonly selected = output<string>();

  protected readonly shownGroups = computed(() => this.groups().filter((group) => group.items.length > 0));

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

  /**
   * Remembers the choice and closes the menu with focus back on the trigger. The CDK does the same on a click or Enter, and on Space
   * in a browser (a button turns it into a click). It keeps the menu open for a Space that is not a click, which only a checkbox
   * would show, so every path closes it here. Not `CdkMenuTrigger.close()`: it leaves the focus on the page's body.
   */
  protected choose(item: MenuItem, menu: CdkMenu): void {
    this.chosen = item.id;
    menu.menuStack.closeAll({ focusParentTrigger: true });
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
