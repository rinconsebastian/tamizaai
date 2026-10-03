import { InteractivityChecker } from '@angular/cdk/a11y';
import { Provider } from '@angular/core';

/**
 * jsdom has no layout, so the CDK sees every element as invisible and its focus trap and menus find nothing to
 * focus. Real browsers are covered by the stack verification with headless Chrome.
 */
export function provideVisibleElements(): Provider {
  return {
    provide: InteractivityChecker,
    useValue: {
      isDisabled: (element: HTMLElement) => element.hasAttribute('disabled'),
      isVisible: () => true,
      isFocusable: (element: HTMLElement) => !element.hasAttribute('disabled') && element.tabIndex >= 0,
      isTabbable: (element: HTMLElement) => !element.hasAttribute('disabled') && element.tabIndex >= 0,
    },
  };
}

const keyCodes: Record<string, number> = { Enter: 13, Escape: 27, ArrowUp: 38, ArrowDown: 40, Tab: 9, ' ': 32 };

/** The CDK still reads the legacy `keyCode`, which jsdom leaves at 0 unless it is set explicitly. */
export function pressKey(target: EventTarget, key: string): void {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  Object.defineProperty(event, 'keyCode', { value: keyCodes[key] ?? 0 });
  target.dispatchEvent(event);
}

export const tick = () => new Promise((resolve) => setTimeout(resolve, 0));
