import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { InterfaceLanguage } from '../../../core/i18n/interface-language';
import { DescriptionPopover } from './description-popover';

@Component({
  imports: [DescriptionPopover],
  template: `
    <button type="button" data-testid="elsewhere">Elsewhere</button>
    <app-description-popover label="Coverage" [description]="first" />
    <app-description-popover label="Target" description="The goal for the round." />
  `,
})
class Host {
  first = 'Share of households that\nwatched a play.\n<b>Not markup</b>';
}

describe('DescriptionPopover (add-widget-context, design D6)', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function render(lang: InterfaceLanguage = 'en') {
    TestBed.configureTestingModule({ imports: [Host, translocoTesting(lang)], providers: [provideVisibleElements()] });
    const fixture = TestBed.createComponent(Host);
    document.body.appendChild(fixture.nativeElement);
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const buttons = () => Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('[data-testid="widget-description"]'));
    const panels = () => Array.from(document.querySelectorAll<HTMLElement>('[data-testid="widget-description-panel"]'));
    const click = async (element: HTMLElement) => {
      element.click();
      await settle();
    };
    return { fixture, settle, buttons, panels, click, elsewhere: fixture.nativeElement.querySelector('[data-testid="elsewhere"]') as HTMLButtonElement };
  }

  it('is a button named after the indicator, closed to begin with', async () => {
    const { buttons, panels } = await render();

    expect(buttons().map((b) => b.getAttribute('aria-label'))).toEqual(['About Coverage', 'About Target']);
    expect(buttons().map((b) => b.getAttribute('aria-expanded'))).toEqual(['false', 'false']);
    expect(buttons()[0].getAttribute('aria-haspopup')).toBe('dialog');
    expect(panels()).toHaveLength(0);
  });

  it('is named in Spanish in the Spanish interface', async () => {
    const { buttons } = await render('es');

    expect(buttons().map((b) => b.getAttribute('aria-label'))).toEqual(['Acerca de Coverage', 'Acerca de Target']);
  });

  it('opens a dialog named after the indicator with the description as plain text, and moves focus into it (Description opened)', async () => {
    const { buttons, panels, click } = await render();

    await click(buttons()[0]);

    const [panel] = panels();
    expect(panel.getAttribute('role')).toBe('dialog');
    expect(panel.getAttribute('aria-modal')).toBeNull();
    expect(document.getElementById(panel.getAttribute('aria-labelledby')!)!.textContent).toBe('Coverage');
    const description = panel.querySelector('[data-testid="widget-description-text"]') as HTMLElement;
    expect(description.textContent).toBe('Share of households that\nwatched a play.\n<b>Not markup</b>');
    expect(description.querySelector('b')).toBeNull();
    expect(description.className).toContain('whitespace-pre-line');
    expect(buttons()[0].getAttribute('aria-expanded')).toBe('true');
    expect(buttons()[0].getAttribute('aria-controls')).toBe(panel.id);
    expect(document.activeElement).toBe(panel);
  });

  it('closes with Escape and puts focus back on its button (Closed with Escape)', async () => {
    const { buttons, panels, click, settle } = await render();
    await click(buttons()[0]);
    expect(panels()).toHaveLength(1);

    pressKey(document.activeElement!, 'Escape');
    await settle();

    expect(panels()).toHaveLength(0);
    expect(buttons()[0].getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(buttons()[0]);
  });

  it('closes with Tab and with Shift+Tab from the panel and puts focus back on its button, so the keyboard order stays in the header', async () => {
    const { buttons, panels, click, settle } = await render();

    for (const shiftKey of [false, true]) {
      await click(buttons()[0]);
      expect(document.activeElement).toBe(panels()[0]);

      const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true });
      Object.defineProperty(event, 'keyCode', { value: 9 });
      document.activeElement!.dispatchEvent(event);
      await settle();

      expect(event.defaultPrevented).toBe(false);
      expect(panels()).toHaveLength(0);
      expect(document.activeElement).toBe(buttons()[0]);
    }
  });

  it('scrolls a long description inside the panel instead of running off a short screen', async () => {
    const { buttons, panels, click } = await render();

    await click(buttons()[0]);

    expect(panels()[0].className).toContain('overflow-y-auto');
    expect(panels()[0].className).toContain('max-h-[min(24rem,70vh)]');
  });

  it('closes with its button, which keeps the focus', async () => {
    const { buttons, panels, click } = await render();
    await click(buttons()[0]);

    await click(buttons()[0]);

    expect(panels()).toHaveLength(0);
    expect(buttons()[0].getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(buttons()[0]);
  });

  it('closes with a click elsewhere on the page, and puts focus back on the button when it was left on nothing', async () => {
    const { buttons, panels, click } = await render();
    await click(buttons()[0]);
    expect(document.activeElement).toBe(panels()[0]);

    // A click on empty space: the panel loses focus to the body, as a real pointer would take it.
    (document.activeElement as HTMLElement).blur();
    await click(document.body);

    expect(panels()).toHaveLength(0);
    expect(document.activeElement).toBe(buttons()[0]);
  });

  it('does not take focus from the control a click went to', async () => {
    const { buttons, panels, click, elsewhere } = await render();
    await click(buttons()[0]);

    elsewhere.focus();
    await click(elsewhere);

    expect(panels()).toHaveLength(0);
    expect(document.activeElement).toBe(elsewhere);
  });

  it('keeps one description open at a time: opening another closes the first', async () => {
    const { buttons, panels, click } = await render();
    await click(buttons()[0]);

    await click(buttons()[1]);

    expect(panels()).toHaveLength(1);
    expect(panels()[0].querySelector('[data-testid="widget-description-text"]')!.textContent).toBe('The goal for the round.');
    expect(buttons().map((b) => b.getAttribute('aria-expanded'))).toEqual(['false', 'true']);
    expect(document.activeElement).toBe(panels()[0]);
  });

  it('closes when its widget goes away', async () => {
    const { fixture, buttons, panels, click, settle } = await render();
    await click(buttons()[0]);
    expect(panels()).toHaveLength(1);

    fixture.destroy();
    await settle().catch(() => undefined);

    expect(panels()).toHaveLength(0);
  });
});
