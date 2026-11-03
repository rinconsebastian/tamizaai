import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../testing/cdk';
import { DropdownMenu, MenuGroup, MenuItem } from './dropdown-menu';

@Component({
  imports: [DropdownMenu],
  template: `<tmz-dropdown-menu label="Actions for Grace" [items]="items" [groups]="groups" [icon]="icon()" [busy]="busy()" (selected)="choose($event)" />`,
})
class Host {
  readonly icon = signal<'more' | 'download'>('more');
  readonly busy = signal(false);
  items: MenuItem[] = [
    { id: 'analyst', label: 'Make analyst' },
    { id: 'viewer', label: 'Make viewer' },
    { id: 'remove', label: 'Remove', danger: true },
  ];
  groups: MenuGroup[] = [];
  chosen: string | null = null;
  /** What the page would see when it acts on the choice, such as opening a dialog that records where focus is. */
  seen: { focused: Element | null; menuOpen: boolean } | null = null;

  choose(id: string): void {
    this.chosen = id;
    this.seen = { focused: document.activeElement, menuOpen: document.querySelector('[role="menu"]') !== null };
  }
}

const entriesOf = (menu: HTMLElement) => Array.from(menu.querySelectorAll<HTMLElement>('[role="menuitem"], [role="menuitemcheckbox"]'));

describe('DropdownMenu', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [Host], providers: [provideVisibleElements()] }));
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(items?: MenuItem[], groups?: MenuGroup[]) {
    const fixture = TestBed.createComponent(Host);
    if (items) {
      fixture.componentInstance.items = items;
    }
    if (groups) {
      fixture.componentInstance.groups = groups;
    }
    fixture.detectChanges();
    const trigger = fixture.nativeElement.querySelector('[data-testid="menu-trigger"]') as HTMLButtonElement;
    expect(trigger.getAttribute('aria-label')).toBe('Actions for Grace');
    trigger.focus();
    pressKey(trigger, 'ArrowDown');
    await fixture.whenStable();
    await tick();
    const menu = document.querySelector('[role="menu"]') as HTMLElement;
    return { fixture, trigger, menu, items: Array.from(menu.querySelectorAll<HTMLElement>('[role="menuitem"]')) };
  }

  it('opens from the keyboard and moves between items with the arrow keys', async () => {
    const { menu, items } = await open();

    expect(items.map((i) => i.textContent?.trim())).toEqual(['Make analyst', 'Make viewer', 'Remove']);
    expect(document.activeElement).toBe(items[0]);

    pressKey(menu, 'ArrowDown');
    expect(document.activeElement).toBe(items[1]);
    pressKey(menu, 'ArrowUp');
    expect(document.activeElement).toBe(items[0]);
  });

  it('closes on Escape and returns focus to the trigger', async () => {
    const { fixture, trigger, menu } = await open();

    pressKey(menu, 'Escape');
    await fixture.whenStable();
    await tick();

    expect(document.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
    expect(fixture.componentInstance.chosen).toBeNull(); // closing is not choosing
  });

  it('emits the chosen item', async () => {
    const { fixture, items } = await open();

    items[2].click();
    await tick();

    expect(fixture.componentInstance.chosen).toBe('remove');
  });

  it('emits the choice once the menu has closed and the trigger has focus again', async () => {
    const { fixture, trigger, items } = await open();

    pressKey(items[0], 'ArrowDown');
    expect(document.activeElement).toBe(items[1]);
    pressKey(items[1], 'Enter');
    expect(fixture.componentInstance.chosen).toBeNull(); // not while the menu is still closing
    await tick();

    expect(fixture.componentInstance.chosen).toBe('viewer');
    expect(fixture.componentInstance.seen).toEqual({ focused: trigger, menuOpen: false });
  });

  describe('an unavailable item', () => {
    const withDisabled: MenuItem[] = [
      { id: 'rename', label: 'Rename' },
      { id: 'duplicate', label: 'Duplicate', disabled: true, description: 'A project can have at most 10 dashboards.', testId: 'duplicate' },
      { id: 'remove', label: 'Remove', danger: true },
    ];

    it('is announced as unavailable, with its description as its accessible description', async () => {
      const { items } = await open(withDisabled);
      const duplicate = items[1];

      expect(duplicate.getAttribute('aria-disabled')).toBe('true');
      expect(duplicate.getAttribute('aria-label')).toBe('Duplicate'); // the name is the label alone
      const description = document.getElementById(duplicate.getAttribute('aria-describedby')!);
      expect(description?.textContent?.trim()).toBe('A project can have at most 10 dashboards.');
      expect(duplicate.contains(description)).toBe(true); // shown, as a second line
      expect(duplicate.className).toContain('aria-disabled:opacity-50');
      expect(duplicate.className).not.toContain('hover:bg-');
      // The available items keep their plain name and their hover colour, and describe nothing.
      expect(items[0].getAttribute('aria-disabled')).toBeNull();
      expect(items[0].getAttribute('aria-describedby')).toBeNull();
      expect(items[0].getAttribute('aria-label')).toBeNull();
      expect(items[0].className).toContain('hover:bg-neutral-tertiary-medium');
    });

    it('is still reached with the arrow keys', async () => {
      const { menu, items } = await open(withDisabled);

      pressKey(menu, 'ArrowDown');
      expect(document.activeElement).toBe(items[1]);
      pressKey(menu, 'ArrowDown');
      expect(document.activeElement).toBe(items[2]);
    });

    it('cannot be chosen by click or from the keyboard, and the menu stays open', async () => {
      const { fixture, menu, items } = await open(withDisabled);

      items[1].click();
      pressKey(menu, 'ArrowDown');
      pressKey(items[1], 'Enter');
      await fixture.whenStable();
      await tick();

      expect(fixture.componentInstance.chosen).toBeNull();
      expect(document.querySelector('[role="menu"]')).not.toBeNull();
    });
  });

  it('shows three dots by default and a download arrow for an export menu', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-icon="more"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-icon="download"]')).toBeNull();

    fixture.componentInstance.icon.set('download');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-icon="download"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-icon="more"]')).toBeNull();
  });

  it('says it is busy, pulses its icon and stays focusable while an action runs', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.busy.set(true);
    fixture.detectChanges();
    const trigger = fixture.nativeElement.querySelector('[data-testid="menu-trigger"]') as HTMLButtonElement;

    expect(trigger.getAttribute('aria-busy')).toBe('true');
    expect(trigger.getAttribute('aria-disabled')).toBeNull(); // it still opens
    expect(trigger.disabled).toBe(false);
    expect(trigger.querySelector('svg')?.classList.contains('animate-pulse')).toBe(true);
    trigger.focus();
    expect(document.activeElement).toBe(trigger);
  });

  it('still opens while busy, from the keyboard and the mouse, and stops saying it is busy once the action ends', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.busy.set(true);
    fixture.detectChanges();
    const trigger = fixture.nativeElement.querySelector('[data-testid="menu-trigger"]') as HTMLButtonElement;
    trigger.focus();

    pressKey(trigger, 'ArrowDown');
    await fixture.whenStable();
    await tick();
    expect(document.querySelector('[role="menu"]')).not.toBeNull();
    pressKey(document.querySelector('[role="menu"]') as HTMLElement, 'Escape');
    await fixture.whenStable();
    await tick();
    expect(document.querySelector('[role="menu"]')).toBeNull();

    trigger.click();
    await fixture.whenStable();
    await tick();
    expect(document.querySelector('[role="menu"]')).not.toBeNull();

    fixture.componentInstance.busy.set(false);
    fixture.detectChanges();
    expect(trigger.getAttribute('aria-busy')).toBeNull();
  });

  it('draws a flat list of items with no group around them, as before', async () => {
    const { menu } = await open();

    expect(menu.querySelector('[role="group"]')).toBeNull();
    expect(menu.querySelector('[role="separator"]')).toBeNull();
    expect(menu.querySelector('[data-testid="menu-group-heading"]')).toBeNull();
  });

  describe('groups', () => {
    const groups: MenuGroup[] = [
      { id: 'view', items: [{ id: 'table', label: 'Show as table', checked: false, testId: 'table' }] },
      { id: 'empty', label: 'Nothing here', items: [] },
      {
        id: 'export',
        label: 'Export',
        items: [
          { id: 'png', label: 'PNG image', testId: 'png' },
          { id: 'xlsx', label: 'Excel workbook', testId: 'xlsx', disabled: true, description: 'Preparing the export…' },
        ],
      },
      { id: 'edit', items: [{ id: 'remove', label: 'Remove widget', danger: true, testId: 'remove' }] },
    ];

    it('shows the groups in order, a line between those that are shown, and none for a group without items', async () => {
      const { menu } = await open(undefined, groups);

      expect(Array.from(menu.querySelectorAll('[role="group"]')).map((g) => g.getAttribute('aria-label'))).toEqual([null, 'Export', null]);
      expect(menu.querySelectorAll('[role="separator"]')).toHaveLength(2);
      expect(entriesOf(menu).map((e) => e.dataset['testid'])).toEqual(['table', 'png', 'xlsx', 'remove']);
      expect(menu.textContent).not.toContain('Nothing here');
    });

    it('names a group once: by its label, with a heading that assistive technology skips and the arrow keys never stop on', async () => {
      const { menu } = await open(undefined, groups);
      const heading = menu.querySelector<HTMLElement>('[data-testid="menu-group-heading"]')!;

      expect(heading.textContent?.trim()).toBe('Export');
      expect(heading.getAttribute('aria-hidden')).toBe('true');
      const order = entriesOf(menu);
      expect(document.activeElement).toBe(order[0]);
      for (const next of order.slice(1)) {
        pressKey(menu, 'ArrowDown');
        expect(document.activeElement).toBe(next);
      }
    });

    it('keeps the menu open when a heading or a line is pressed with the mouse', async () => {
      const { fixture, menu } = await open(undefined, groups);
      const press = (element: Element) => {
        const down = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
        element.dispatchEvent(down);
        return down.defaultPrevented;
      };

      expect(press(menu.querySelector('[data-testid="menu-group-heading"]')!)).toBe(true);
      expect(press(menu.querySelector('[role="separator"]')!)).toBe(true);
      await fixture.whenStable();
      await tick();
      expect(document.querySelector('[role="menu"]')).not.toBeNull();
    });

    it('still describes an unavailable item of a group', async () => {
      const { menu } = await open(undefined, groups);
      const unavailable = menu.querySelector<HTMLElement>('[data-testid="xlsx"]')!;

      expect(unavailable.getAttribute('aria-disabled')).toBe('true');
      expect(document.getElementById(unavailable.getAttribute('aria-describedby')!)?.textContent?.trim()).toBe('Preparing the export…');
    });
  });

  describe('a checkbox item', () => {
    const withCheck = (checked: boolean): MenuItem[] => [
      { id: 'table', label: 'Show as table', checked, testId: 'table' },
      { id: 'other', label: 'Other' },
    ];

    it.each([false, true])('is a menuitemcheckbox that reads as checked = %s', async (checked) => {
      const { menu } = await open(withCheck(checked));
      const item = menu.querySelector<HTMLElement>('[data-testid="table"]')!;

      expect(item.getAttribute('role')).toBe('menuitemcheckbox');
      expect(item.getAttribute('aria-checked')).toBe(String(checked));
      expect(item.querySelector('[data-icon="check"]')?.classList.contains('invisible')).toBe(!checked);
      expect(menu.querySelectorAll('[role="menuitem"]')).toHaveLength(1);
    });

    it.each(['click', 'Enter', 'Space'])('closes the menu on %s, returns focus to the trigger, and emits its id once it has', async (how) => {
      const { fixture, trigger, menu } = await open(withCheck(false));
      const item = menu.querySelector<HTMLElement>('[data-testid="table"]')!;

      if (how === 'click') {
        item.click();
      } else {
        pressKey(item, how === 'Space' ? ' ' : how);
      }
      expect(fixture.componentInstance.chosen).toBeNull(); // not while the menu is still closing
      await fixture.whenStable();
      await tick();

      expect(document.querySelector('[role="menu"]')).toBeNull();
      expect(fixture.componentInstance.chosen).toBe('table');
      expect(fixture.componentInstance.seen).toEqual({ focused: trigger, menuOpen: false });
    });
  });
});
