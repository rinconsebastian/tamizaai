import { Component, inject, input } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';

export interface TabItem {
  label: string;
  link: string;
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
        <option [value]="item.link" [selected]="isActive(item.link)">{{ item.label }}</option>
      }
    </select>
    <nav class="hidden md:block text-sm font-medium text-center text-body border-b border-default" [attr.aria-label]="label()">
      <ul class="flex flex-wrap -mb-px">
        @for (item of items(); track item.link) {
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
