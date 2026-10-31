import { Component, ElementRef, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../../testing/cdk';
import { MarkGrid, MarkPoint, markTooltips } from './mark-tooltips';

// The tooltip and the keyboard of the marks of a chart with a series (design D5, task 9.2): the map's pattern, in one helper.

const mark = (group: string, series: string, value: string, x = 10, y = 20): MarkPoint => ({
  content: { heading: group, lines: [{ label: series, value, align: 'end' }], footer: null },
  x,
  y,
});

/** Two groups of two series; Palmira has no second series. Groups run across, as in a column chart. */
const grid = (): MarkGrid => [
  [mark('Cali', 'Piped', '35'), mark('Cali', 'Well', '15')],
  [mark('Palmira', 'Piped', '20'), null],
];

@Component({
  selector: 'app-probe-marks',
  template: `<svg #host tabindex="0" data-testid="host" (keydown)="nav.keydown($event)" (focusout)="nav.focusout($event)"></svg><p data-testid="said">{{ nav.announcement() }}</p>`,
})
class ProbeMarks {
  readonly host = viewChild.required<ElementRef<SVGElement>>('host');
  readonly marks = signal<MarkGrid>(grid());
  readonly across = signal(true);
  readonly nav = markTooltips({ host: () => this.host().nativeElement, marks: () => this.marks(), groupsAcross: () => this.across() });
}

const panel = () => document.querySelector<HTMLElement>('[data-testid="chart-tooltip"]');
const heading = () => panel()?.querySelector('[data-testid="tooltip-heading"]')?.textContent?.trim();
const lines = () => Array.from(panel()?.querySelectorAll('[data-testid="tooltip-line"]') ?? []).map((l) => l.textContent?.replace(/\s+/g, ' ').trim());

async function create() {
  TestBed.configureTestingModule({ providers: [provideVisibleElements()] });
  const fixture = TestBed.createComponent(ProbeMarks);
  await fixture.whenStable();
  const host = fixture.componentInstance.host().nativeElement;
  host.focus();
  return { fixture, probe: fixture.componentInstance, host, said: () => (fixture.detectChanges(), (fixture.nativeElement.querySelector('[data-testid="said"]') as HTMLElement).textContent!.replaceAll('\u200b', '')) };
}

describe('markTooltips', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => c.replaceChildren()));

  describe('the pointer', () => {
    it('opens the tooltip of the mark under it, changes it for the next mark, and closes it on leaving', async () => {
      const { probe } = await create();

      probe.nav.hover({ g: 0, s: 1 }, new MouseEvent('pointerenter', { clientX: 5, clientY: 6 }));
      expect(heading()).toBe('Cali');
      expect(lines()).toEqual(['Well: 15']);
      expect(probe.nav.isLifted(0, 1)).toBe(true);

      probe.nav.leave({ g: 0, s: 1 });
      probe.nav.hover({ g: 1, s: 0 }, new MouseEvent('pointerenter'));
      expect(heading()).toBe('Palmira');
      expect(probe.nav.isLifted(0, 1)).toBe(false);

      probe.nav.leave({ g: 1, s: 0 });
      expect(panel()).toBeNull();
    });

    it('keeps the tooltip of a tap until something else is touched, and ignores a mouse press', async () => {
      const { probe } = await create();

      probe.nav.tap({ g: 0, s: 0 }, new MouseEvent('pointerdown') as PointerEvent);
      expect(heading()).toBe('Cali');
      probe.nav.leave({ g: 0, s: 0 }); // the finger lifts: pointerleave follows the tap
      expect(heading()).toBe('Cali');
      probe.nav.hover({ g: 1, s: 0 }, new MouseEvent('pointerenter')); // a pinned tooltip is not replaced by hovering
      expect(heading()).toBe('Cali');

      document.body.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
      expect(panel()).toBeNull();

      probe.nav.tap({ g: 0, s: 0 }, Object.assign(new MouseEvent('pointerdown'), { pointerType: 'mouse' }) as PointerEvent);
      expect(panel()).toBeNull();
    });
  });

  describe('the keyboard', () => {
    it('enters the marks at the first one with Enter or an arrow key, and says it', async () => {
      const { probe, host, said } = await create();

      pressKey(host, 'Enter');
      expect(probe.nav.active()).toEqual({ g: 0, s: 0 });
      expect(heading()).toBe('Cali');
      expect(said()).toBe('Cali. Piped: 35');
    });

    it('enters at the last mark with End', async () => {
      const { probe, host } = await create();

      pressKey(host, 'End');
      expect(probe.nav.active()).toEqual({ g: 1, s: 0 });
    });

    it('moves between groups with left and right and between series with up and down when the groups run across', async () => {
      const { probe, host, said } = await create();
      pressKey(host, 'Enter');

      pressKey(host, 'ArrowDown');
      expect(probe.nav.active()).toEqual({ g: 0, s: 1 });
      expect(said()).toBe('Cali. Well: 15');
      pressKey(host, 'ArrowRight'); // Palmira has no Well: its nearest series
      expect(probe.nav.active()).toEqual({ g: 1, s: 0 });
      expect(lines()).toEqual(['Piped: 20']);
      pressKey(host, 'ArrowLeft');
      expect(probe.nav.active()).toEqual({ g: 0, s: 0 });
      pressKey(host, 'ArrowUp'); // the first series: nowhere to go
      expect(probe.nav.active()).toEqual({ g: 0, s: 0 });
    });

    it('moves between groups with up and down and between series with left and right when the groups run down', async () => {
      const { probe, host } = await create();
      probe.across.set(false);
      pressKey(host, 'Enter');

      pressKey(host, 'ArrowRight');
      expect(probe.nav.active()).toEqual({ g: 0, s: 1 });
      pressKey(host, 'ArrowDown');
      expect(probe.nav.active()).toEqual({ g: 1, s: 0 });
      pressKey(host, 'ArrowRight'); // no mark to the right of Palmira's only one
      expect(probe.nav.active()).toEqual({ g: 1, s: 0 });
      pressKey(host, 'ArrowUp');
      expect(probe.nav.active()).toEqual({ g: 0, s: 0 });
    });

    it('goes to the first and the last mark with Home and End, and stops at the ends', async () => {
      const { probe, host } = await create();
      pressKey(host, 'Enter');

      pressKey(host, 'End');
      expect(probe.nav.active()).toEqual({ g: 1, s: 0 });
      pressKey(host, 'ArrowRight');
      expect(probe.nav.active()).toEqual({ g: 1, s: 0 });
      pressKey(host, 'Home');
      expect(probe.nav.active()).toEqual({ g: 0, s: 0 });
    });

    it('reads the same words twice in a row as a change', async () => {
      const { probe, host, fixture } = await create();
      pressKey(host, 'Enter');
      fixture.detectChanges();
      const first = (fixture.nativeElement.querySelector('[data-testid="said"]') as HTMLElement).textContent;
      probe.nav.activate({ g: 0, s: 0 });
      fixture.detectChanges();
      const second = (fixture.nativeElement.querySelector('[data-testid="said"]') as HTMLElement).textContent;

      expect(first).not.toBe(second);
      expect(first!.replaceAll('\u200b', '')).toBe(second!.replaceAll('\u200b', ''));
    });

    it('leaves the marks with Escape: the tooltip closes, no mark is the keyboard\'s, and the chart keeps the focus', async () => {
      const { probe, host } = await create();
      pressKey(host, 'Enter');

      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      host.dispatchEvent(escape);

      expect(escape.defaultPrevented).toBe(true);
      expect(panel()).toBeNull();
      expect(probe.nav.active()).toBeNull();
      expect(document.activeElement).toBe(host);
    });

    it('leaves an Escape alone when there is nothing to leave, so a dialog around the chart sees it', async () => {
      const { host } = await create();

      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      host.dispatchEvent(escape);

      expect(escape.defaultPrevented).toBe(false);
    });

    it('leaves other keys and keys with a modifier alone', async () => {
      const { probe, host } = await create();

      for (const init of [{ key: 'a' }, { key: 'Tab' }, { key: 'ArrowRight', ctrlKey: true }, { key: 'Enter', altKey: true }]) {
        const event = new KeyboardEvent('keydown', { ...init, bubbles: true, cancelable: true });
        host.dispatchEvent(event);
        expect(event.defaultPrevented).toBe(false);
      }
      expect(probe.nav.active()).toBeNull();
    });

    it('closes the tooltip and forgets the mark when the focus leaves the chart', async () => {
      const { probe, host } = await create();
      pressKey(host, 'Enter');

      host.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: document.body }));

      expect(probe.nav.active()).toBeNull();
      expect(panel()).toBeNull();
    });

    it('does nothing with arrow keys when the chart has no marks', async () => {
      const { probe, host } = await create();
      probe.marks.set([]);
      await tick();

      const event = new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true });
      host.dispatchEvent(event);

      expect(probe.nav.active()).toBeNull();
    });
  });

  it('closes the tooltip and forgets the marks when the marks change', async () => {
    const { probe, host, fixture } = await create();
    pressKey(host, 'Enter');
    expect(panel()).not.toBeNull();

    probe.marks.set([[mark('Buga', 'Piped', '5')]]);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(panel()).toBeNull();
    expect(probe.nav.active()).toBeNull();
  });

  it('closes its tooltip when the chart is destroyed', async () => {
    const { fixture, host } = await create();
    pressKey(host, 'Enter');

    fixture.destroy();

    expect(panel()).toBeNull();
  });
});
