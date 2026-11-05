import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { maxOrderValues } from '../../../core/projects/indicators.service';
import { ValueOrderEditor } from './value-order-editor';

@Component({
  imports: [ValueOrderEditor],
  template: `<app-value-order-editor [values]="values()" [shown]="shown()" [offered]="offered()" (changed)="values.set($event); changes.push($event)" />`,
})
class Host {
  readonly values = signal<string[]>(['1. Sí', '2. Tal vez', '3. No']);
  readonly shown = signal<string[] | null>(null);
  readonly offered = signal<string[]>(['1. Sí', '4. No sabe', 'Otro']);
  readonly changes: string[][] = [];
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

describe('ValueOrderEditor (add-declared-category-order, design D9)', () => {
  async function render() {
    TestBed.configureTestingModule({ imports: [translocoTesting('en')] });
    const fixture = TestBed.createComponent(Host);
    document.body.append(fixture.nativeElement);
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const root = fixture.nativeElement as HTMLElement;
    const items = () => Array.from(root.querySelectorAll<HTMLElement>('[data-testid="order-item"]'));
    const values = () => items().map((item) => text(item.querySelector('[data-testid="order-value"]')));
    const button = (id: string, row: number) => items()[row].querySelector<HTMLButtonElement>(`[data-testid="${id}"]`)!;
    const field = () => root.querySelector<HTMLInputElement>('[data-testid="order-new"]')!;
    return { fixture, host: fixture.componentInstance, settle, root, items, values, button, field };
  }

  afterEach(() => document.body.replaceChildren());

  it('lists the values in order, in full, each with a button to move it up, one to move it down and one to remove it', async () => {
    const editor = await render();

    expect(editor.values()).toEqual(['1. Sí', '2. Tal vez', '3. No']);
    expect(editor.button('order-up', 1).getAttribute('aria-label')).toBe('Move 2. Tal vez up');
    expect(editor.button('order-down', 1).getAttribute('aria-label')).toBe('Move 2. Tal vez down');
    expect(editor.button('order-remove', 1).getAttribute('aria-label')).toBe('Remove 2. Tal vez');
  });

  it('turns off the button that has nowhere to go at either end', async () => {
    const editor = await render();

    expect(editor.button('order-up', 0).disabled).toBe(true);
    expect(editor.button('order-down', 0).disabled).toBe(false);
    expect(editor.button('order-down', 2).disabled).toBe(true);
  });

  it('keeps targets of at least 40 px for every button', async () => {
    const editor = await render();

    for (const id of ['order-up', 'order-down', 'order-remove']) {
      expect(editor.button(id, 1).className).toContain('min-h-10');
      expect(editor.button(id, 1).className).toContain('min-w-10');
    }
  });

  it('moves a value with the keyboard and keeps the focus on it', async () => {
    const editor = await render();
    const down = editor.button('order-down', 0);
    down.focus();

    down.click();
    await editor.settle();

    expect(editor.host.changes.at(-1)).toEqual(['2. Tal vez', '1. Sí', '3. No']);
    expect(editor.values()).toEqual(['2. Tal vez', '1. Sí', '3. No']);
    expect(document.activeElement).toBe(editor.button('order-down', 1));
  });

  it('puts the focus on the other button when the value reaches an end and its own button is off', async () => {
    const editor = await render();

    editor.button('order-down', 1).click();
    await editor.settle();

    expect(editor.values()).toEqual(['1. Sí', '3. No', '2. Tal vez']);
    expect(document.activeElement).toBe(editor.button('order-up', 2));
  });

  it('removes a value and moves the focus to the value that took its place', async () => {
    const editor = await render();

    editor.button('order-remove', 1).click();
    await editor.settle();

    expect(editor.values()).toEqual(['1. Sí', '3. No']);
    expect(document.activeElement).toBe(editor.button('order-remove', 1));
  });

  it('moves the focus to the field that adds a value when the last one is removed', async () => {
    const editor = await render();
    editor.host.values.set(['Solo']);
    await editor.settle();

    editor.button('order-remove', 0).click();
    await editor.settle();

    expect(editor.values()).toEqual([]);
    expect(document.activeElement).toBe(editor.field());
  });

  it('adds an offered value and a typed one at the end, and clears the field', async () => {
    const editor = await render();
    const add = editor.root.querySelector<HTMLButtonElement>('[data-testid="order-add"]')!;

    editor.field().value = '4. No sabe';
    editor.field().dispatchEvent(new Event('input'));
    add.click();
    await editor.settle();
    editor.field().value = 'Quizás';
    editor.field().dispatchEvent(new Event('input'));
    pressKey(editor.field(), 'Enter');
    await editor.settle();

    expect(editor.values()).toEqual(['1. Sí', '2. Tal vez', '3. No', '4. No sabe', 'Quizás']);
    expect(editor.field().value).toBe('');
  });

  it('offers only the values the data holds that the order does not list yet', async () => {
    const editor = await render();

    const options = Array.from(editor.root.querySelectorAll<HTMLOptionElement>('datalist option')).map((option) => option.value);

    expect(options).toEqual(['4. No sabe', 'Otro']);
    expect(editor.field().getAttribute('list')).toBe(editor.root.querySelector('datalist')!.id);
  });

  it('adds nothing for a value that is empty or already listed', async () => {
    const editor = await render();
    const add = editor.root.querySelector<HTMLButtonElement>('[data-testid="order-add"]')!;

    add.click();
    editor.field().value = '2. Tal vez';
    editor.field().dispatchEvent(new Event('input'));
    add.click();
    await editor.settle();

    expect(editor.host.changes).toEqual([]);
  });

  it('lists apart, with only Remove, a value that is not in the chart, and moves the others among themselves', async () => {
    const editor = await render();
    editor.host.shown.set(['1. Sí', '3. No']);
    await editor.settle();

    expect(editor.values()).toEqual(['1. Sí', '3. No']);
    const apart = editor.root.querySelectorAll('[data-testid="order-apart-item"]');
    expect(Array.from(apart).map((item) => text(item.querySelector('[data-testid="order-apart-value"]')))).toEqual(['2. Tal vez']);
    expect(apart[0].querySelector('[data-testid="order-up"]')).toBeNull();

    editor.button('order-down', 0).click();
    await editor.settle();
    expect(editor.host.changes.at(-1)).toEqual(['3. No', '2. Tal vez', '1. Sí']);

    (apart[0].querySelector('[data-testid="order-apart-remove"]') as HTMLButtonElement).click();
    await editor.settle();
    expect(editor.host.changes.at(-1)).toEqual(['3. No', '1. Sí']);
  });

  it('turns the add button off at the limit and says so', async () => {
    const editor = await render();
    editor.host.values.set(Array.from({ length: maxOrderValues }, (_, i) => `Valor ${i + 1}`));
    await editor.settle();

    expect((editor.root.querySelector('[data-testid="order-add"]') as HTMLButtonElement).disabled).toBe(true);
    expect(text(editor.root.querySelector('[data-testid="order-full"]'))).toBe(`An order lists at most ${maxOrderValues} values.`);
  });
});
