import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { TabItem, TabsNav } from './tabs-nav';

@Component({ template: '' })
class Blank {}

const longName = 'Cobertura de vacunación por departamento';
const dashboardNames = ['Housing', 'Health', 'Field', 'Education', 'Water', 'Roads', longName];

/** Performance, Clean data, seven dashboards (`d1` to `d7`, the last with a long name) and the Settings group. */
function projectTabs(): TabItem[] {
  return [
    { label: 'Performance', link: '/projects/p1/performance' },
    { label: 'Clean data', link: '/projects/p1/clean-data' },
    ...dashboardNames.map((name, i) => ({ label: name, link: `/projects/p1/dashboards/d${i + 1}`, truncate: true, testId: `dashboard-tab-d${i + 1}` })),
    {
      label: 'Settings',
      link: '/projects/p1/overview',
      children: [
        { label: 'General', link: '/projects/p1/overview' },
        { label: 'Members', link: '/projects/p1/members' },
      ],
    },
  ];
}

@Component({
  imports: [TabsNav],
  template: `<tmz-tabs-nav [items]="items()" label="Project sections" scrollStartLabel="Show earlier tabs" scrollEndLabel="Show more tabs" />`,
})
class Host {
  readonly items = signal(projectTabs());
}

const tabWidth = 150;
const listOf = (element: Element) => element.closest('[data-testid="tabs-list"]');
const tabsOf = (list: Element) => Array.from(list.querySelectorAll('a'));

/**
 * jsdom has no layout: every tab of the list is 150 pixels wide, side by side from its start, and the list shows `width` pixels of
 * them. A width of 0 is the row hidden below `md`, where nothing has a size. jsdom keeps whatever `scrollLeft` is set to, so the
 * tests read where the list was scrolled.
 */
function stubLayout(layout: { width: number }): void {
  const shown = () => layout.width > 0;
  vi.spyOn(Element.prototype, 'clientWidth', 'get').mockImplementation(function (this: Element) {
    return this.matches('[data-testid="tabs-list"]') ? layout.width : 0;
  });
  vi.spyOn(Element.prototype, 'scrollWidth', 'get').mockImplementation(function (this: Element) {
    return shown() && this.matches('[data-testid="tabs-list"]') ? tabsOf(this).length * tabWidth : 0;
  });
  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockImplementation(function (this: HTMLElement) {
    return shown() && this.matches('a') && listOf(this) ? tabWidth : 0;
  });
  vi.spyOn(HTMLElement.prototype, 'offsetLeft', 'get').mockImplementation(function (this: HTMLElement) {
    const list = listOf(this);
    return shown() && list && this.matches('a') ? tabsOf(list).indexOf(this as HTMLAnchorElement) * tabWidth : 0;
  });
}

/** Replaces the browser's `ResizeObserver`, which jsdom lacks, with one whose callback the test calls. */
function stubResizeObserver() {
  const observers: { callback: () => void; observed: Element[] }[] = [];
  vi.stubGlobal(
    'ResizeObserver',
    class {
      readonly observed: Element[] = [];
      constructor(readonly callback: () => void) {
        observers.push(this);
      }
      observe(element: Element) {
        this.observed.push(element);
      }
      disconnect() {}
    },
  );
  return observers;
}

/** The tab lies whole inside the part of the list on view. */
const inView = (list: HTMLElement, tab: HTMLElement) =>
  tab.offsetLeft >= list.scrollLeft && tab.offsetLeft + tab.offsetWidth <= list.scrollLeft + list.clientWidth;

/** The width of the fade and chevron over each edge of the list that hides tabs. */
const fadeWidth = 56;

/** The tab lies whole inside the part of the list on view, clear of the fade over each edge that hides tabs. */
const clearOfFades = (list: HTMLElement, tab: HTMLElement) => {
  const start = list.scrollLeft > 1 ? fadeWidth : 0;
  const end = list.scrollLeft + list.clientWidth < list.scrollWidth - 1 ? fadeWidth : 0;
  return tab.offsetLeft >= list.scrollLeft + start && tab.offsetLeft + tab.offsetWidth <= list.scrollLeft + list.clientWidth - end;
};

describe('TabsNav', () => {
  const layout = { width: 400 };
  /** Whatever would move the page: none of it may be called. */
  const pageScrolls = vi.fn<() => void>();
  const scrollIntoView = Element.prototype.scrollIntoView;

  beforeEach(() => {
    layout.width = 400;
    stubLayout(layout);
    pageScrolls.mockClear();
    vi.spyOn(window, 'scrollTo').mockImplementation(() => pageScrolls());
    vi.spyOn(window, 'scroll').mockImplementation(() => pageScrolls());
    vi.spyOn(window, 'scrollBy').mockImplementation(() => pageScrolls());
    // The member has scrolled down the page. jsdom does not scroll, so the page's offset is the root element's, which the code
    // could still set.
    document.documentElement.scrollTop = 640;
    vi.spyOn(window, 'scrollY', 'get').mockImplementation(() => document.documentElement.scrollTop);
    Element.prototype.scrollIntoView = () => pageScrolls();
    TestBed.configureTestingModule({ providers: [provideRouter([{ path: '**', component: Blank }])] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    Element.prototype.scrollIntoView = scrollIntoView;
    document.documentElement.scrollTop = 0;
  });

  /** Opens `url`, then creates the tabs, as the project page does once the project has loaded. */
  async function create(url: string) {
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const navigate = async (to: string) => {
      await router.navigateByUrl(to);
      await fixture.whenStable();
    };
    return { fixture, root, list: root.querySelector<HTMLElement>('[data-testid="tabs-list"]')!, navigate };
  }

  const tab = (root: HTMLElement, id: string) => root.querySelector<HTMLElement>(`[data-testid="dashboard-tab-${id}"]`)!;
  const chevrons = (root: HTMLElement) =>
    ['tabs-scroll-start', 'tabs-scroll-end'].filter((id) => root.querySelector(`[data-testid="${id}"]`) !== null);

  it('keeps Settings outside the scrolling list, at the end of the row', async () => {
    const { root, list } = await create('/projects/p1/performance');
    const nav = root.querySelector('nav')!;

    expect(list.querySelector('[data-testid="settings-tab"]')).toBeNull();
    expect(nav.querySelector('[data-testid="settings-tab"]')).not.toBeNull();
    expect(Array.from(nav.querySelectorAll('a')).map((a) => a.textContent?.trim())).toEqual(['Performance', 'Clean data', ...dashboardNames, 'Settings']);
    expect(list.className).toContain('flex-nowrap');
    expect(list.className).toContain('overflow-x-auto');
  });

  it('cuts a long name on screen only: the link keeps its whole text, and its title shows it on hover (Long name)', async () => {
    const { root } = await create('/projects/p1/performance');
    const long = tab(root, 'd7');

    expect(long.textContent?.trim()).toBe(longName);
    expect(long.getAttribute('title')).toBe(longName);
    expect(long.className).toContain('max-w-48');
    expect(long.className).toContain('truncate');
    expect(long.className).toContain('focus-visible:max-w-none'); // the whole name on keyboard focus
    // The fixed tabs are never cut, so they need no title.
    expect(root.querySelector('nav a')!.getAttribute('title')).toBeNull();
    expect(root.querySelector('nav a')!.className).not.toContain('truncate');
  });

  it('keeps every tab on one line, with a focus ring drawn inside it that the list cannot clip', async () => {
    const { root } = await create('/projects/p1/performance');
    for (const link of Array.from(root.querySelectorAll('nav a'))) {
      expect(link.className).toContain('whitespace-nowrap');
      expect(link.className).toContain('focus-visible:ring-inset');
    }
  });

  it('brings the open tab into view when the row is created on it, by scrolling the list (Open tab brought into view)', async () => {
    const { root, list } = await create('/projects/p1/dashboards/d7');

    expect(list.scrollLeft).toBeGreaterThan(0);
    expect(inView(list, tab(root, 'd7'))).toBe(true);
    expect(pageScrolls).not.toHaveBeenCalled();
  });

  it('brings the tab of another path into view, moving only the list, never the page', async () => {
    const { root, list, navigate } = await create('/projects/p1/performance');
    expect(list.scrollLeft).toBe(0);

    await navigate('/projects/p1/dashboards/d6');

    expect(list.scrollLeft).toBeGreaterThan(0);
    expect(inView(list, tab(root, 'd6'))).toBe(true);
    expect(list.scrollTop).toBe(0);
    expect(pageScrolls).not.toHaveBeenCalled();
    expect(window.scrollY).toBe(640);
  });

  it('leaves the list and the page where they are on a navigation that changes only the query string or the fragment (Page stays in place)', async () => {
    const { root, list, navigate } = await create('/projects/p1/dashboards/d7');
    // The member scrolls the row back to its start, away from the open tab.
    list.scrollLeft = 0;
    list.dispatchEvent(new Event('scroll'));

    await navigate('/projects/p1/dashboards/d7?page=2');
    expect(list.scrollLeft).toBe(0);
    await navigate('/projects/p1/dashboards/d7?page=2#pager');
    expect(list.scrollLeft).toBe(0);

    expect(inView(list, tab(root, 'd7'))).toBe(false);
    expect(pageScrolls).not.toHaveBeenCalled();
    expect(window.scrollY).toBe(640);
  });

  it('brings the open tab into view again when the tabs change', async () => {
    const { fixture, root, list } = await create('/projects/p1/dashboards/d7');
    list.scrollLeft = 0;
    list.dispatchEvent(new Event('scroll'));

    fixture.componentInstance.items.update((items) => items.map((item) => (item.label === 'Housing' ? { ...item, label: 'Vivienda' } : item)));
    await fixture.whenStable();

    expect(inView(list, tab(root, 'd7'))).toBe(true);
  });

  it('brings a tab reached with the keyboard into view, clear of the fades, moving only the list (Long name, focus)', async () => {
    const { fixture, root, list } = await create('/projects/p1/performance');
    const focus = async (link: HTMLElement) => {
      link.focus();
      await fixture.whenStable();
    };
    // Nine tabs of 150 pixels in a list of 400: Housing (300 to 450) is half on view and the end's fade covers the rest.
    expect(list.scrollLeft).toBe(0);

    await focus(tab(root, 'd1'));
    expect(clearOfFades(list, tab(root, 'd1'))).toBe(true);

    // The last dashboard, wholly hidden: the list scrolls to its end, where no fade is left.
    await focus(tab(root, 'd7'));
    expect(list.scrollLeft).toBe(9 * tabWidth - 400);
    expect(clearOfFades(list, tab(root, 'd7'))).toBe(true);
    expect(chevrons(root)).toEqual(['tabs-scroll-start']);

    // Shift+Tab back to a tab under the start's fade.
    await focus(root.querySelectorAll<HTMLElement>('[data-testid="tabs-list"] a')[1]);
    expect(clearOfFades(list, root.querySelectorAll<HTMLElement>('[data-testid="tabs-list"] a')[1])).toBe(true);

    expect(pageScrolls).not.toHaveBeenCalled();
    expect(window.scrollY).toBe(640);
  });

  it('shows the chevron of a side only while that side hides tabs, out of the tab order and hidden from assistive technology', async () => {
    const { fixture, root, list } = await create('/projects/p1/performance');
    const scrolled = async (left: number) => {
      list.scrollLeft = left;
      list.dispatchEvent(new Event('scroll'));
      await fixture.whenStable();
    };
    // Nine tabs of 150 pixels in a list of 400: the end hides tabs, the start does not.
    expect(chevrons(root)).toEqual(['tabs-scroll-end']);

    await scrolled(300);
    expect(chevrons(root)).toEqual(['tabs-scroll-start', 'tabs-scroll-end']);

    await scrolled(9 * tabWidth - 400);
    expect(chevrons(root)).toEqual(['tabs-scroll-start']);

    const start = root.querySelector<HTMLButtonElement>('[data-testid="tabs-scroll-start"]')!;
    expect(start.getAttribute('aria-hidden')).toBe('true');
    expect(start.tabIndex).toBe(-1);
    expect(start.getAttribute('title')).toBe('Show earlier tabs');
  });

  it('scrolls the list by its width from a chevron', async () => {
    const { fixture, root, list } = await create('/projects/p1/performance');

    root.querySelector<HTMLElement>('[data-testid="tabs-scroll-end"]')!.click();
    await fixture.whenStable();
    expect(list.scrollLeft).toBe(400);
    expect(chevrons(root)).toEqual(['tabs-scroll-start', 'tabs-scroll-end']);

    root.querySelector<HTMLElement>('[data-testid="tabs-scroll-start"]')!.click();
    await fixture.whenStable();
    expect(list.scrollLeft).toBe(0);
    expect(chevrons(root)).toEqual(['tabs-scroll-end']);
    expect(pageScrolls).not.toHaveBeenCalled();
  });

  it('shows no chevron while every tab fits', async () => {
    layout.width = 2000;
    const { root } = await create('/projects/p1/dashboards/d7');
    expect(chevrons(root)).toEqual([]);
  });

  it('updates the chevrons when the list is resized', async () => {
    const observers = stubResizeObserver();
    const { fixture, root, list } = await create('/projects/p1/performance');
    expect(observers.map((o) => o.observed)).toEqual([[list]]);
    expect(chevrons(root)).toEqual(['tabs-scroll-end']);

    layout.width = 2000; // the window grew
    observers[0].callback();
    await fixture.whenStable();
    expect(chevrons(root)).toEqual([]);
  });

  it('brings the open tab into view when the row shows after being hidden on a narrow screen, and only then', async () => {
    const observers = stubResizeObserver();
    layout.width = 0; // below md, where the select stands in for the row
    const { fixture, root, list } = await create('/projects/p1/dashboards/d7');
    expect(list.scrollLeft).toBe(0);

    layout.width = 400; // the phone is turned sideways
    observers[0].callback();
    await fixture.whenStable();
    expect(inView(list, tab(root, 'd7'))).toBe(true);
    expect(chevrons(root)).toEqual(['tabs-scroll-start']);

    // A row already on view keeps where the member scrolled it when it is resized.
    list.scrollLeft = 0;
    list.dispatchEvent(new Event('scroll'));
    layout.width = 500;
    observers[0].callback();
    await fixture.whenStable();
    expect(list.scrollLeft).toBe(0);
    expect(pageScrolls).not.toHaveBeenCalled();
  });

  it('marks the open tab as the page, and the group as current while one of its children is open', async () => {
    const { root, navigate } = await create('/projects/p1/dashboards/d2');
    expect(Array.from(root.querySelectorAll('[aria-current="page"]'))).toEqual([tab(root, 'd2')]);
    expect(root.querySelector('[data-testid="settings-tab"]')!.getAttribute('aria-current')).toBeNull();

    await navigate('/projects/p1/members');
    expect(root.querySelectorAll('[aria-current="page"]')).toHaveLength(0);
    expect(root.querySelector('[data-testid="settings-tab"]')!.getAttribute('aria-current')).toBe('true');
    expect(root.querySelector<HTMLSelectElement>('select')!.selectedOptions[0].textContent?.trim()).toBe('Members');
  });

  it('still lists the group last in the select, after the tabs, with its children under its label', async () => {
    const { root } = await create('/projects/p1/dashboards/d3');
    const select = root.querySelector('select')!;
    const children = Array.from(select.children);

    expect(children.slice(0, -1).map((c) => c.textContent?.trim())).toEqual(['Performance', 'Clean data', ...dashboardNames]);
    expect(children.slice(0, -1).every((c) => c.tagName === 'OPTION')).toBe(true);
    const group = children.at(-1) as HTMLOptGroupElement;
    expect(group.tagName).toBe('OPTGROUP');
    expect(group.label).toBe('Settings');
    expect(Array.from(group.querySelectorAll('option')).map((o) => o.textContent?.trim())).toEqual(['General', 'Members']);
    expect(select.selectedOptions[0].value).toBe('/projects/p1/dashboards/d3');
  });
});
