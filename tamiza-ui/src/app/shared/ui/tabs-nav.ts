import { Component, inject, input } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';

export interface TabItem {
  label: string;
  link: string;
  /**
   * Makes the item a group: a trailing tab that stands for its children. It is current while any child is open, and below `md`
   * its children are listed in the select under a group label. The children's own navigation is the page's to render.
   */
  children?: TabItem[];
}

/** Section navigation: Flowbite underline tabs from `md` up, a native select below. */
@Component({
  selector: 'tmz-tabs-nav',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <label class="sr-only" [attr.for]="selectId">{{ label() }}</label>
    <select
      [id]="selectId"
      class="md:hidden block w-full bg-neutral-secondary-medium border border-default-medium text-heading text-sm rounded-base px-3 py-2.5 min-h-10"
      (change)="go($any($event.target).value)"
    >
      @for (item of items(); track item.link) {
        @if (item.children; as children) {
          <optgroup [label]="item.label">
            @for (child of children; track child.link) {
              <option [value]="child.link" [selected]="isActive(child.link)">{{ child.label }}</option>
            }
          </optgroup>
        } @else {
          <option [value]="item.link" [selected]="isActive(item.link)">{{ item.label }}</option>
        }
      }
    </select>
    <nav class="hidden md:block text-sm font-medium text-center text-body border-b border-default" [attr.aria-label]="label()">
      <ul class="flex flex-wrap -mb-px">
        @for (item of items(); track item.link) {
          @if (item.children; as children) {
            <li class="ms-auto">
              <a
                [routerLink]="item.link"
                [attr.aria-current]="groupActive(children) ? 'true' : null"
                [class]="groupClasses(children)"
                data-testid="settings-tab"
              >
                <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <circle cx="12" cy="12" r="3" />
                  <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" />
                </svg>
                {{ item.label }}
              </a>
            </li>
          } @else {
            <li class="me-2">
              <a
                [routerLink]="item.link"
                routerLinkActive="text-fg-brand border-brand"
                ariaCurrentWhenActive="page"
                class="inline-block p-4 border-b-2 border-transparent rounded-t-base hover:text-fg-brand hover:border-brand"
                >{{ item.label }}</a
              >
            </li>
          }
        }
      </ul>
    </nav>
  `,
})
export class TabsNav {
  private static nextId = 0;
  private readonly router = inject(Router);

  readonly items = input.required<TabItem[]>();
  readonly label = input.required<string>();
  protected readonly selectId = `tmz-tabs-${TabsNav.nextId++}`;

  /** A group is current while any of its children is open; the router's own active link would only match its first child. */
  protected groupActive(children: TabItem[]): boolean {
    return children.some((child) => this.isActive(child.link));
  }

  protected groupClasses(children: TabItem[]): string {
    const base = 'inline-flex items-center gap-2 p-4 border-b-2 rounded-t-base hover:text-fg-brand hover:border-brand';
    return `${base} ${this.groupActive(children) ? 'text-fg-brand border-brand' : 'border-transparent'}`;
  }

  protected isActive(link: string): boolean {
    return this.router.isActive(this.router.createUrlTree([link]), {
      paths: 'subset',
      queryParams: 'ignored',
      fragment: 'ignored',
      matrixParams: 'ignored',
    });
  }

  protected go(link: string): void {
    void this.router.navigateByUrl(link);
  }
}
