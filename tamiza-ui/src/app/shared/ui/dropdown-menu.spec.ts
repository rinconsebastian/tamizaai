import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../testing/cdk';
import { DropdownMenu, MenuItem } from './dropdown-menu';

@Component({
  imports: [DropdownMenu],
  template: `<tmz-dropdown-menu label="Actions for Grace" [items]="items" (selected)="chosen = $event" />`,
})
class Host {
  items: MenuItem[] = [
    { id: 'analyst', label: 'Make analyst' },
    { id: 'viewer', label: 'Make viewer' },
    { id: 'remove', label: 'Remove', danger: true },
  ];
  chosen: string | null = null;
}

describe('DropdownMenu', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [Host], providers: [provideVisibleElements()] }));
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const trigger = fixture.nativeElement.querySelector('[data-testid="menu-trigger"]') as HTMLButtonElement;
    expect(trigger.getAttribute('aria-label')).toBe('Actions for Grace');
    trigger.focus();
    pressKey(trigger, 'ArrowDown');
    await fixture.whenStable();
    await tick();
    const menu = document.querySelector('[role="menu"]') as HTMLElement;
    return { fixture, trigger, menu };
  }

  it('opens from the keyboard and moves between items with the arrow keys', async () => {
    const { menu } = await open();
    const items = Array.from(menu.querySelectorAll('[role="menuitem"]')) as HTMLElement[];

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
  });

  it('emits the chosen item', async () => {
    const { fixture, menu } = await open();

    (menu.querySelectorAll('[role="menuitem"]')[2] as HTMLElement).click();

    expect(fixture.componentInstance.chosen).toBe('remove');
  });
});
