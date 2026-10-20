import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, computed, input, output, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Badge, Button, Input } from '../../../shared/ui';

export interface IndicatorMenuItem {
  name: string;
  label: string;
  /** The number of the current version; null for an indicator whose script is not saved yet. */
  version: number | null;
}

/** More indicators than this get a filter at the top of the menu. */
export const indicatorFilterThreshold = 10;

/**
 * The third script picker: a button that names the selected indicator (or the number of indicators) and opens a menu of them,
 * each with its label, its name and its version. A project can hold a hundred, so a long list gets a filter. The menu is a CDK
 * menu: arrow keys move between items, Escape closes it and returns focus to the button.
 */
@Component({
  selector: 'app-indicator-menu',
  imports: [CdkMenuTrigger, CdkMenu, CdkMenuItem, TranslocoDirective, Badge, Button, Input],
  template: `
    <ng-container *transloco="let t">
      <button
        tmzButton
        [variant]="selected() ? 'primary' : 'secondary'"
        size="sm"
        type="button"
        [cdkMenuTriggerFor]="menu"
        [attr.aria-pressed]="!!selected()"
        (cdkMenuOpened)="filter.set('')"
        data-testid="indicator-menu"
      >
        <span class="max-w-48 truncate">{{ selectedItem()?.label ?? t('scripts.indicatorsMenu', { count: items().length }) }}</span>
        <svg aria-hidden="true" class="size-4 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path d="M5.5 7.5 10 12l4.5-4.5z" /></svg>
      </button>
      <ng-template #menu>
        <div
          cdkMenu
          [attr.aria-label]="t('scripts.indicators')"
          class="z-10 w-80 max-w-[calc(100vw-2rem)] bg-neutral-primary-medium border border-default-medium rounded-base py-2 shadow-lg"
          data-testid="indicator-list"
        >
          @if (items().length > filterThreshold) {
            <div class="px-3 pb-2">
              <label class="sr-only" for="indicator-filter">{{ t('scripts.indicatorFilter') }}</label>
              <input
                tmzInput
                id="indicator-filter"
                type="search"
                autocomplete="off"
                [placeholder]="t('scripts.indicatorFilter')"
                [value]="filter()"
                (input)="filter.set($any($event.target).value)"
                (keydown)="$event.stopPropagation()"
                data-testid="indicator-filter"
              />
            </div>
          }
          @for (item of shown(); track item.name) {
            <button
              cdkMenuItem
              type="button"
              (cdkMenuItemTriggered)="chosen.emit(item.name)"
              class="flex w-full items-center gap-2 px-4 py-2 min-h-10 text-left hover:bg-neutral-tertiary-medium focus:bg-neutral-tertiary-medium focus:outline-none"
              [class.bg-neutral-secondary-medium]="item.name === selected()"
              [attr.aria-current]="item.name === selected() ? 'true' : null"
              [attr.data-testid]="'indicator-' + item.name"
            >
              <span class="min-w-0 flex-1">
                <span class="block truncate text-sm font-medium text-heading">{{ item.label }}</span>
                <code class="text-xs text-body">{{ item.name }}</code>
              </span>
              @if (item.version === null) {
                <tmz-badge tone="brand">{{ t('scripts.newBadge') }}</tmz-badge>
              } @else {
                <tmz-badge tone="success">{{ t('scripts.versionShort', { number: item.version }) }}</tmz-badge>
              }
            </button>
          } @empty {
            <p class="px-4 py-2 text-sm text-body" data-testid="no-indicators">
              {{ items().length ? t('scripts.inputs.noMatch') : t('scripts.noIndicatorsEditor') }}
            </p>
          }
        </div>
      </ng-template>
    </ng-container>
  `,
})
export class IndicatorMenu {
  readonly items = input.required<IndicatorMenuItem[]>();
  /** The name of the indicator whose script is selected; null while script 1 or script 2 is. */
  readonly selected = input<string | null>(null);
  readonly chosen = output<string>();

  protected readonly filterThreshold = indicatorFilterThreshold;
  protected readonly filter = signal('');
  protected readonly selectedItem = computed(() => this.items().find((item) => item.name === this.selected()) ?? null);
  protected readonly shown = computed(() => {
    const needle = this.filter().trim().toLowerCase();
    return needle ? this.items().filter((item) => item.name.toLowerCase().includes(needle) || item.label.toLowerCase().includes(needle)) : this.items();
  });
}
