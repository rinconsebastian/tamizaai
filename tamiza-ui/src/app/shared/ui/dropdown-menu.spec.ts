import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../testing/cdk';
import { DropdownMenu, MenuItem } from './dropdown-menu';

@Component({
  imports: [DropdownMenu],
  template: `<tmz-dropdown-menu label="Actions for Grace" [items]="items" (selected)="choose($event)" />`,
})
class Host {
  items: MenuItem[] = [
    { id: 'analyst', label: 'Make analyst' },
    { id: 'viewer', label: 'Make viewer' },
    { id: 'remove', label: 'Remove', danger: true },
  ];
  chosen: string | null = null;
  /** What the page would see when it acts on the choice, such as opening a dialog that records where focus is. */
  seen: { focused: Element | null; menuOpen: boolean } | null = null;

  choose(id: string): void {
    this.chosen = id;
    this.seen = { focused: document.activeElement, menuOpen: document.querySelector('[role="menu"]') !== null };
  }
}

describe('DropdownMenu', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [Host], providers: [provideVisibleElements()] }));
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(items?: MenuItem[]) {
    const fixture = TestBed.createComponent(Host);
    if (items) {
      fixture.componentInstance.items = items;
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
      { id: 'duplicate', label: 'Duplicate', disabled: true, description: 'A project can have at most 7 dashboards.', testId: 'duplicate' },
      { id: 'remove', label: 'Remove', danger: true },
    ];

    it('is announced as unavailable, with its description as its accessible description', async () => {
      const { items } = await open(withDisabled);
      const duplicate = items[1];

      expect(duplicate.getAttribute('aria-disabled')).toBe('true');
      expect(duplicate.getAttribute('aria-label')).toBe('Duplicate'); // the name is the label alone
      const description = document.getElementById(duplicate.getAttribute('aria-describedby')!);
      expect(description?.textContent?.trim()).toBe('A project can have at most 7 dashboards.');
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
});
