import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { DescriptionPopover } from './description-popover';
import { FootItem, WidgetFoot } from './widget-foot';

// The chips of a widget's foot (improve-widget-text-hierarchy, design D9): opened by a click, by keyboard focus and by pointing, closed by
// Escape, a click elsewhere and another chip, one at a time with a widget's description, and described by a line of whole text.

const note: FootItem = { kind: 'note', label: 'Note', text: 'No suman 100 %: respuesta múltiple.', tone: 'neutral' };
const small: FootItem = {
  kind: 'small',
  label: '2 with a small base',
  text: 'Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed.',
  tone: 'warning',
};
const n: FootItem = { kind: 'n', label: 'n = 475 Hogares', text: 'n = 475 Hogares', tone: 'neutral' };
const detail: FootItem = { kind: 'detail', label: 'Detail', text: 'E1, % de los hogares con encuesta completa', tone: 'neutral' };

@Component({
  imports: [WidgetFoot, DescriptionPopover],
  template: `
    <button type="button" data-testid="elsewhere">Elsewhere</button>
    <app-description-popover label="Coverage" description="Share of households that watched a play." />
    <app-widget-foot [items]="items()" [compact]="compact()" />
  `,
})
class Host {
  readonly items = signal<FootItem[]>([n, small, note]);
  readonly compact = signal(false);
}

describe('WidgetFoot (improve-widget-text-hierarchy, design D9)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
  });

  /** jsdom does not tell the keyboard's focus from a click's: say that it is the keyboard's, as `:focus-visible` is after a Tab. */
  const asKeyboardFocus = () => {
    const matches = HTMLElement.prototype.matches;
    vi.spyOn(HTMLElement.prototype, 'matches').mockImplementation(function (this: HTMLElement, selector: string) {
      return selector === ':focus-visible' ? true : matches.call(this, selector);
    });
  };

  async function render(items: FootItem[] = [n, small, note], compact = false) {
    TestBed.configureTestingModule({ imports: [Host, translocoTesting('en')], providers: [provideVisibleElements()] });
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.items.set(items);
    fixture.componentInstance.compact.set(compact);
    document.body.appendChild(fixture.nativeElement);
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const root = fixture.nativeElement as HTMLElement;
    const chip = (kind: string) => root.querySelector<HTMLButtonElement>(`[data-testid="foot-chip-${kind}"]`)!;
    const line = (kind: string) => root.querySelector<HTMLElement>(`[data-testid="foot-line-${kind}"]`)!;
    const panels = () => Array.from(document.querySelectorAll<HTMLElement>('[data-testid="foot-panel"]'));
    const click = async (element: HTMLElement) => {
      element.click();
      await settle();
    };
    const pointer = async (element: HTMLElement, type: 'pointerenter' | 'pointerleave', pointerType = 'mouse') => {
      const event = new Event(type, { bubbles: false });
      Object.defineProperty(event, 'pointerType', { value: pointerType });
      element.dispatchEvent(event);
      await settle();
    };
    return { fixture, root, settle, chip, line, panels, click, pointer, elsewhere: root.querySelector('[data-testid="elsewhere"]') as HTMLButtonElement };
  }

  it('draws one button per chip, in order, closed to begin with, named by its short text', async () => {
    const { root, chip, panels } = await render();

    expect(Array.from(root.querySelectorAll('[data-testid^="foot-chip-"]:not([data-testid="foot-chip-icon"])')).map((b) => b.getAttribute('data-testid'))).toEqual([
      'foot-chip-n',
      'foot-chip-small',
      'foot-chip-note',
    ]);
    expect(chip('small').textContent!.trim()).toBe('2 with a small base');
    expect(chip('small').getAttribute('aria-expanded')).toBe('false');
    expect(chip('small').getAttribute('type')).toBe('button');
    expect(panels()).toHaveLength(0);
  });

  it('draws no foot for no chip (Nothing to say)', async () => {
    const { root } = await render([]);

    expect(root.querySelector('[data-testid="widget-foot"]')).toBeNull();
    expect(root.querySelector('[data-testid="widget-foot-lines"]')).toBeNull();
  });

  it('opens a panel with the whole text while the pointer is on the chip, and closes it when the pointer leaves (Opened by pointing)', async () => {
    const { chip, panels, pointer } = await render();

    await pointer(chip('small'), 'pointerenter');

    expect(panels()).toHaveLength(1);
    expect(panels()[0].textContent!.trim()).toBe(small.text);
    expect(chip('small').getAttribute('aria-expanded')).toBe('true');

    await pointer(chip('small'), 'pointerleave');
    await new Promise((resolve) => setTimeout(resolve, 150));

    expect(panels()).toHaveLength(0);
    expect(chip('small').getAttribute('aria-expanded')).toBe('false');
  });

  it('keeps the panel closed for a touch that only passes over the chip, and opens it on the tap', async () => {
    const { chip, panels, pointer, click } = await render();

    await pointer(chip('note'), 'pointerenter', 'touch');
    expect(panels()).toHaveLength(0);

    await click(chip('note'));
    expect(panels()).toHaveLength(1);
    expect(panels()[0].textContent!.trim()).toBe(note.text);
  });

  it('opens on keyboard focus and closes with Escape, leaving focus on the chip (Opened with the keyboard)', async () => {
    const { chip, panels, settle } = await render();
    asKeyboardFocus();

    chip('note').focus();
    await settle();
    expect(panels()).toHaveLength(1);
    expect(panels()[0].textContent!.trim()).toBe(note.text);

    pressKey(chip('note'), 'Escape');
    await settle();

    expect(panels()).toHaveLength(0);
    expect(chip('note').getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(chip('note'));
  });

  it('closes an unpinned panel when the focus leaves the chip', async () => {
    const { chip, panels, settle, elsewhere } = await render();
    asKeyboardFocus();
    chip('note').focus();
    await settle();
    expect(panels()).toHaveLength(1);

    elsewhere.focus();
    await settle();

    expect(panels()).toHaveLength(0);
  });

  it('pins a panel at a click, which stays after the pointer moves away, until a second click closes it (Opened by a click)', async () => {
    const { chip, panels, pointer, click } = await render();

    await pointer(chip('n'), 'pointerenter');
    await click(chip('n'));
    await pointer(chip('n'), 'pointerleave');
    await new Promise((resolve) => setTimeout(resolve, 150));

    expect(panels()).toHaveLength(1);
    expect(chip('n').getAttribute('aria-expanded')).toBe('true');

    await click(chip('n'));

    expect(panels()).toHaveLength(0);
  });

  it('does not toggle twice for the focus a click gives (a click on a closed chip opens it once)', async () => {
    const { chip, panels, click } = await render();

    chip('n').focus();
    await click(chip('n'));

    expect(panels()).toHaveLength(1);
    await click(chip('n'));
    expect(panels()).toHaveLength(0);
  });

  it('closes a pinned panel with a click elsewhere, and with Escape', async () => {
    const { chip, panels, click, elsewhere, settle } = await render();
    await click(chip('small'));
    expect(panels()).toHaveLength(1);

    await click(elsewhere);
    expect(panels()).toHaveLength(0);

    await click(chip('small'));
    pressKey(document.body, 'Escape');
    await settle();
    expect(panels()).toHaveLength(0);
  });

  it('keeps one panel open at a time: opening a chip closes the one open', async () => {
    const { chip, panels, click } = await render();

    await click(chip('n'));
    await click(chip('note'));

    expect(panels()).toHaveLength(1);
    expect(panels()[0].getAttribute('data-kind')).toBe('note');
    expect(chip('n').getAttribute('aria-expanded')).toBe('false');
  });

  it('closes a widget\'s description when a chip opens, and the chip when the description opens (One at a time)', async () => {
    const { chip, panels, click, fixture } = await render();
    const description = () => fixture.nativeElement.querySelector('[data-testid="widget-description"]') as HTMLButtonElement;

    await click(description());
    expect(document.querySelector('[data-testid="widget-description-panel"]')).not.toBeNull();

    await click(chip('note'));
    expect(document.querySelector('[data-testid="widget-description-panel"]')).toBeNull();
    expect(panels()).toHaveLength(1);

    await click(description());
    expect(panels()).toHaveLength(0);
    expect(document.querySelector('[data-testid="widget-description-panel"]')).not.toBeNull();
  });

  it('tells a screen reader its name, that it is collapsed, and the whole text as its description, open or not (Screen reader)', async () => {
    const { chip, line, click } = await render();

    const described = document.getElementById(chip('note').getAttribute('aria-describedby')!)!;
    expect(described).toBe(line('note'));
    expect(described.textContent!.trim()).toBe(note.text);
    expect(chip('note').getAttribute('aria-expanded')).toBe('false');

    await click(chip('note'));

    expect(chip('note').getAttribute('aria-expanded')).toBe('true');
    expect(document.getElementById(chip('note').getAttribute('aria-describedby')!)!.textContent!.trim()).toBe(note.text);
    // The panel repeats the description, so it is not read as well, and it is no stop of the keyboard.
    const panel = document.querySelector('[data-testid="foot-panel"]')!;
    expect(panel.getAttribute('aria-hidden')).toBe('true');
    expect(panel.hasAttribute('tabindex')).toBe(false);
  });

  it('tells a warning chip by its icon and its words, not by color alone (Not by color alone)', async () => {
    const { chip } = await render();

    const icons = (kind: string) => chip(kind).querySelector('[data-testid="foot-chip-icon"] path')!.getAttribute('d');
    expect(chip('small').getAttribute('data-tone')).toBe('warning');
    expect(chip('small').className).toContain('bg-warning-soft');
    expect(chip('small').textContent!.trim()).toContain('small base');
    expect(icons('small')).not.toBe(icons('n'));
    expect(icons('small')).toBe('M12 3 2.5 20h19L12 3Z');
  });

  it('cuts a long label at the chip and keeps the whole text in its line and its panel', async () => {
    const long: FootItem = { kind: 'caption', label: '% de los hogares que asistieron a espectáculos en el último año', text: 'Texto completo', tone: 'neutral' };
    const { chip } = await render([long]);

    const label = chip('caption').querySelector('span')!;
    expect(label.classList).toContain('truncate');
    expect(label.className).toContain('max-w-40');
    expect(label.textContent).toBe(long.label);
  });

  it('prints each chip as a line of its whole text in the order of the foot, the detail chip\'s included (Foot printed as lines)', async () => {
    const { root } = await render([n, small, detail, note]);

    const lines = Array.from(root.querySelectorAll<HTMLElement>('[data-testid="widget-foot-lines"] li'));
    expect(lines.map((li) => li.getAttribute('data-testid'))).toEqual(['foot-line-n', 'foot-line-small', 'foot-line-detail', 'foot-line-note']);
    expect(lines.map((li) => li.classList.contains('paper:hidden'))).toEqual([false, false, false, false]);
    const list = root.querySelector('[data-testid="widget-foot-lines"]')!;
    expect(list.classList).toContain('hidden');
    expect(list.classList).toContain('paper:block');
    expect(root.querySelector('[data-testid="widget-foot"]')!.classList).toContain('paper:hidden');
  });

  it('prints the lines of a stat tile at 10 px and the others at the usual size, so a tile two rows high holds more of them (Foot printed as lines)', async () => {
    const stat = await render([n, small, note], true);
    const sizes = Array.from(stat.root.querySelectorAll<HTMLElement>('[data-testid="widget-foot-lines"] li')).map((li) => li.className);
    expect(sizes).toHaveLength(3);
    sizes.forEach((c) => {
      expect(c).toContain('paper:text-[10px]');
      expect(c).toContain('paper:leading-[12px]');
    });
    TestBed.resetTestingModule();

    const chart = await render([n, small, note]);
    Array.from(chart.root.querySelectorAll<HTMLElement>('[data-testid="widget-foot-lines"] li')).forEach((li) => {
      expect(li.className).not.toContain('paper:text-[10px]');
      expect(li.className).toContain('text-xs');
    });
  });

  it('describes a detail chip by its own line, which prints as a line of the foot', async () => {
    const { chip, line } = await render([detail]);

    expect(chip('detail').getAttribute('aria-describedby')).toBe(line('detail').id);
    expect(line('detail').textContent!.trim()).toBe(detail.text);
  });

  it('closes its panel when the foot is taken away', async () => {
    const { fixture, chip, panels, click, settle } = await render();
    await click(chip('n'));
    expect(panels()).toHaveLength(1);

    fixture.componentInstance.items.set([]);
    await settle();

    expect(panels()).toHaveLength(0);
  });
});
