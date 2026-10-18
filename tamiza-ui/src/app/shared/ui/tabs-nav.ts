import { Component, DestroyRef, ElementRef, Injector, afterNextRender, afterRenderEffect, computed, inject, input, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IsActiveMatchOptions, NavigationEnd, Router, RouterLink, isActive } from '@angular/router';
import { filter } from 'rxjs';

export interface TabItem {
  label: string;
  link: string;
  /**
   * Makes the item a group: a trailing tab that stands for its children. It is current while any child is open, and below `md`
   * its children are listed in the select under a group label. The children's own navigation is the page's to render.
   */
  children?: TabItem[];
  /** A label the page does not choose, such as a dashboard's name: cut to fit the tab, whole in its `title` and while it has keyboard focus. */
  truncate?: boolean;
  /** Lets tests and scripts find the tab. */
  testId?: string;
}

/** A link is open when the address starts with it, whatever the query string, fragment or matrix parameters. */
const openMatch: Partial<IsActiveMatchOptions> = { paths: 'subset', queryParams: 'ignored', fragment: 'ignored', matrixParams: 'ignored' };

/** The width of the fade and chevron over each edge that hides tabs: a tab brought into view clears it. */
const edgeWidth = 56;

const tabBase =
  'inline-block p-4 border-b-2 rounded-t-base whitespace-nowrap hover:text-fg-brand hover:border-brand focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand';

/**
 * Section navigation: a native select below `md`; from `md` up, Flowbite underline tabs in one row that scrolls sideways inside
 * itself when they do not fit (design D11 of add-multiple-dashboards). A group (Settings) sits after the row, so it stays visible;
 * each side that hides tabs shows a fade and a chevron that scrolls the row, and the open tab is brought into view horizontally,
 * never by scrolling the page.
 */
@Component({
  selector: 'tmz-tabs-nav',
  imports: [RouterLink],
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
              <option [value]="child.link" [selected]="active(child.link)">{{ child.label }}</option>
            }
          </optgroup>
        } @else {
          <option [value]="item.link" [selected]="active(item.link)">{{ item.label }}</option>
        }
      }
    </select>
    <nav class="hidden md:flex text-sm font-medium text-center text-body border-b border-default" [attr.aria-label]="label()">
      <div class="relative min-w-0 flex-1">
        <!-- No visible scrollbar: a classic one would lift these tabs' underlines above the group's. The chevrons and Tab scroll it. -->
        <ul
          #list
          class="relative flex flex-nowrap overflow-x-auto -mb-px [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
          (scroll)="measure()"
          (focusin)="reveal($event)"
          data-testid="tabs-list"
        >
          @for (item of plain(); track item.link) {
            <!-- A flex item, not an inline block: a cut tab (overflow hidden) would sit on the text's baseline, its underline lifted. -->
            <li class="me-2 flex shrink-0">
              <a
                [routerLink]="item.link"
                [attr.aria-current]="active(item.link) ? 'page' : null"
                [attr.title]="item.truncate ? item.label : null"
                [attr.data-testid]="item.testId ?? null"
                [class]="tabClasses(item)"
                >{{ item.label }}</a
              >
            </li>
          }
        </ul>
        <!-- Pointer only: Tab already brings a focused tab into view, so the chevrons stay out of the tab order and the accessibility tree. -->
        @if (hiddenStart()) {
          <div class="pointer-events-none absolute inset-y-0 start-0 flex items-center bg-linear-to-r from-neutral-secondary-soft from-60% to-transparent pe-4">
            <button type="button" aria-hidden="true" tabindex="-1" [attr.title]="scrollStartLabel()" class="pointer-events-auto inline-flex h-full min-w-10 items-center justify-center text-body hover:text-heading focus:outline-none" (click)="scrollBy(-1)" data-testid="tabs-scroll-start">
              <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="m15 6-6 6 6 6" />
              </svg>
            </button>
          </div>
        }
        @if (hiddenEnd()) {
          <div class="pointer-events-none absolute inset-y-0 end-0 flex items-center bg-linear-to-l from-neutral-secondary-soft from-60% to-transparent ps-4">
            <button type="button" aria-hidden="true" tabindex="-1" [attr.title]="scrollEndLabel()" class="pointer-events-auto inline-flex h-full min-w-10 items-center justify-center text-body hover:text-heading focus:outline-none" (click)="scrollBy(1)" data-testid="tabs-scroll-end">
              <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="m9 6 6 6-6 6" />
              </svg>
            </button>
          </div>
        }
      </div>
      @for (item of groups(); track item.link) {
        @if (item.children; as children) {
          <div class="ms-2 -mb-px flex shrink-0">
            <a [routerLink]="item.link" [attr.aria-current]="groupActive(children) ? 'true' : null" [class]="groupClasses(children)" data-testid="settings-tab">
              <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="3" />
                <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" />
              </svg>
              {{ item.label }}
            </a>
          </div>
        }
      }
    </nav>
  `,
})
export class TabsNav {
  private static nextId = 0;
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  readonly items = input.required<TabItem[]>();
  readonly label = input.required<string>();
  /** Tooltips of the chevrons, which are hidden from assistive technology. */
  readonly scrollStartLabel = input<string | null>(null);
  readonly scrollEndLabel = input<string | null>(null);

  protected readonly selectId = `tmz-tabs-${TabsNav.nextId++}`;
  protected readonly plain = computed(() => this.items().filter((item) => !item.children));
  protected readonly groups = computed(() => this.items().filter((item) => item.children));
  /** Whether the row hides tabs before (start) or after (end) the ones it shows. */
  protected readonly hiddenStart = signal(false);
  protected readonly hiddenEnd = signal(false);

  /** Whether each link is open, one signal of the router's state per link, built again only when the items change. */
  private readonly openLinks = computed(() => {
    const links = this.items().flatMap((item) => [item.link, ...(item.children ?? []).map((child) => child.link)]);
    return new Map(links.map((link) => [link, isActive(link, this.router, openMatch)]));
  });

  private readonly list = viewChild.required<ElementRef<HTMLElement>>('list');
  /** The path of the last navigation: one that changes only the query string or the fragment leaves the row where it is. */
  private path = pathOf(this.router.url);

  constructor() {
    // After the first render with items (the page may open on any tab) and after every change to them (added, moved, renamed).
    afterRenderEffect(() => {
      this.items();
      untracked(() => {
        this.bringOpenTabIntoView();
        this.measure();
      });
    });

    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => {
        const path = pathOf(event.urlAfterRedirects);
        if (path !== this.path) {
          this.path = path;
          afterNextRender(() => this.bringOpenTabIntoView(), { injector: this.injector });
        }
      });

    afterNextRender(() => {
      // Not every browser or test environment has it; the scroll events still keep the chevrons right there.
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      const list = this.list().nativeElement;
      let shown = list.clientWidth > 0;
      const observer = new ResizeObserver(() => {
        // Below `md` the row is hidden; shown again (a phone turned sideways), it would otherwise open on its start.
        if (!shown && list.clientWidth > 0) {
          this.bringOpenTabIntoView();
        }
        shown = list.clientWidth > 0;
        this.measure();
      });
      observer.observe(list);
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  protected active(link: string): boolean {
    return this.openLinks().get(link)?.() ?? false;
  }

  /** A group is current while any of its children is open; a link to the group would only match its first child. */
  protected groupActive(children: TabItem[]): boolean {
    return children.some((child) => this.active(child.link));
  }

  protected tabClasses(item: TabItem): string {
    const state = this.active(item.link) ? 'text-fg-brand border-brand' : 'border-transparent';
    return `${tabBase} ${state}${item.truncate ? ' max-w-48 truncate focus-visible:max-w-none' : ''}`;
  }

  protected groupClasses(children: TabItem[]): string {
    const base = `${tabBase} inline-flex items-center gap-2`;
    return `${base} ${this.groupActive(children) ? 'text-fg-brand border-brand' : 'border-transparent'}`;
  }

  protected go(link: string): void {
    void this.router.navigateByUrl(link);
  }

  /** Shows the chevron of each side that hides tabs. */
  protected measure(): void {
    const list = this.list().nativeElement;
    // A pixel of slack absorbs the fractions of a zoomed page.
    this.hiddenStart.set(list.scrollLeft > 1);
    this.hiddenEnd.set(list.scrollLeft + list.clientWidth < list.scrollWidth - 1);
  }

  /** A chevron scrolls the row by its own width. */
  protected scrollBy(direction: -1 | 1): void {
    const list = this.list().nativeElement;
    this.scrollTo(list, list.scrollLeft + direction * list.clientWidth);
  }

  /**
   * Brings a tab reached with Tab into view, clear of the fades, as the open tab is. The browser scrolls a focused element only when
   * it is wholly hidden, so a tab half under a fade would stay there, and it measures a cut tab before its focus style widens it to
   * the whole name. Reading the tab's size here applies that style first.
   */
  protected reveal(event: FocusEvent): void {
    const tab = (event.target as Element | null)?.closest('a');
    if (tab) {
      this.bringIntoView(this.list().nativeElement, tab);
    }
  }

  private bringOpenTabIntoView(): void {
    const list = this.list().nativeElement;
    const tab = list.querySelector<HTMLElement>('a[aria-current="page"]');
    if (tab) {
      this.bringIntoView(list, tab);
    }
  }

  /**
   * Scrolls the row, and only the row, so the tab lies inside it, clear of the edges' fades. The list is the tabs' offset parent,
   * so their `offsetLeft` does not move as it scrolls. `scrollIntoView` would scroll the page too.
   */
  private bringIntoView(list: HTMLElement, tab: HTMLElement): void {
    const start = tab.offsetLeft - edgeWidth;
    const end = tab.offsetLeft + tab.offsetWidth + edgeWidth;
    if (start < list.scrollLeft) {
      this.scrollTo(list, start);
    } else if (end > list.scrollLeft + list.clientWidth) {
      this.scrollTo(list, end - list.clientWidth);
    }
  }

  private scrollTo(list: HTMLElement, left: number): void {
    list.scrollLeft = Math.max(0, Math.min(left, list.scrollWidth - list.clientWidth));
    this.measure();
  }
}

function pathOf(url: string): string {
  return url.split(/[?#]/)[0];
}
