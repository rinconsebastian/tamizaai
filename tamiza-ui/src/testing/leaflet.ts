/**
 * What the map specs need from a browser and jsdom lacks: a layout (Leaflet reads `clientWidth` and `clientHeight` of its container,
 * and the widget those of its box), `ResizeObserver` and `IntersectionObserver` that a test fires by hand, `matchMedia` for the
 * reduced-motion setting, and a stylesheet `<link>` that answers `load`. Call `stubMap()` in a `beforeEach` and `restore()` after.
 */

export interface MapStub {
  /** Gives the boxes a new size (the map's container and the widget's box), and tells every `ResizeObserver`. */
  resize(width: number, height: number): void;
  /** Makes `prefers-reduced-motion: reduce` match, or not. */
  reduceMotion(on: boolean): void;
  /** Tells the `IntersectionObserver`s that this element is now on screen (or off it). */
  intersect(element: Element, on?: boolean): void;
  /** Observers made, to see that the widget waits for one. */
  readonly intersectionObservers: number;
  restore(): void;
}

interface FakeObserver {
  callback: (entries: unknown[], observer: unknown) => void;
  elements: Set<Element>;
}

const boxes = '[data-testid="chart-box"], .leaflet-container';

export function stubMap(options: { width?: number; height?: number; intersection?: boolean } = {}): MapStub {
  let width = options.width ?? 600;
  let height = options.height ?? 400;
  let reduced = false;
  const resizers = new Set<FakeObserver>();
  const watchers = new Set<FakeObserver>();
  const original = {
    clientWidth: Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth'),
    clientHeight: Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientHeight'),
    matchMedia: window.matchMedia,
    resizeObserver: window.ResizeObserver,
    intersectionObserver: window.IntersectionObserver,
    appendChild: document.head.appendChild,
  };

  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get(this: HTMLElement) { return this.matches(boxes) ? width : 0; } });
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get(this: HTMLElement) { return this.matches(boxes) ? height : 0; } });

  window.matchMedia = ((query: string) => ({
    matches: reduced && query.includes('prefers-reduced-motion'),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
    onchange: null,
  })) as unknown as typeof window.matchMedia;

  class FakeResizeObserver {
    private readonly observer: FakeObserver;
    constructor(callback: (entries: unknown[], observer: unknown) => void) {
      this.observer = { callback, elements: new Set() };
      resizers.add(this.observer);
    }
    observe(element: Element) {
      this.observer.elements.add(element);
    }
    unobserve(element: Element) {
      this.observer.elements.delete(element);
    }
    disconnect() {
      resizers.delete(this.observer);
    }
  }
  window.ResizeObserver = FakeResizeObserver as unknown as typeof window.ResizeObserver;

  let made = 0;
  if (options.intersection) {
    class FakeIntersectionObserver {
      private readonly observer: FakeObserver;
      constructor(callback: (entries: unknown[], observer: unknown) => void) {
        made++;
        this.observer = { callback, elements: new Set() };
        watchers.add(this.observer);
      }
      observe(element: Element) {
        this.observer.elements.add(element);
      }
      unobserve(element: Element) {
        this.observer.elements.delete(element);
      }
      disconnect() {
        watchers.delete(this.observer);
      }
    }
    window.IntersectionObserver = FakeIntersectionObserver as unknown as typeof window.IntersectionObserver;
  } else {
    // jsdom has none, which is the case the widget handles by drawing at once; make sure of it.
    Reflect.deleteProperty(window, 'IntersectionObserver');
  }

  // A stylesheet link never loads under jsdom; the engine waits for its `load`, so this answers it.
  document.head.appendChild = function <T extends Node>(this: HTMLHeadElement, node: T): T {
    const added = original.appendChild.call(this, node) as T;
    if (node instanceof HTMLLinkElement && node.rel === 'stylesheet') {
      queueMicrotask(() => node.dispatchEvent(new Event('load')));
    }
    return added;
  } as typeof document.head.appendChild;

  // A stylesheet link from an earlier, unstubbed map in the same file is still waiting for its answer: it gets it now.
  document.head.querySelectorAll('link[rel="stylesheet"]').forEach((link) => queueMicrotask(() => link.dispatchEvent(new Event('load'))));

  return {
    resize(w, h) {
      width = w;
      height = h;
      for (const { callback, elements } of resizers) {
        const entries = Array.from(elements).map((target) => ({ target, contentRect: { width: w, height: h } }));
        if (entries.length) {
          callback(entries, null);
        }
      }
    },
    reduceMotion(on) {
      reduced = on;
    },
    intersect(element, on = true) {
      for (const { callback, elements } of watchers) {
        if (elements.has(element)) {
          callback([{ target: element, isIntersecting: on, intersectionRatio: on ? 1 : 0 }], null);
        }
      }
    },
    get intersectionObservers() {
      return made;
    },
    restore() {
      for (const name of ['clientWidth', 'clientHeight'] as const) {
        if (original[name]) {
          Object.defineProperty(HTMLElement.prototype, name, original[name]);
        } else {
          Reflect.deleteProperty(HTMLElement.prototype, name);
        }
      }
      window.matchMedia = original.matchMedia;
      window.ResizeObserver = original.resizeObserver;
      if (original.intersectionObserver) {
        window.IntersectionObserver = original.intersectionObserver;
      } else {
        Reflect.deleteProperty(window, 'IntersectionObserver');
      }
      document.head.appendChild = original.appendChild;
    },
  };
}

/** A touch event with this many fingers down: jsdom has no `Touch`, so the lists are plain objects. */
export function touch(target: EventTarget, type: 'touchstart' | 'touchmove' | 'touchend' | 'touchcancel', fingers: number, at = { x: 100, y: 100 }): Event {
  const event = new Event(type, { bubbles: true, cancelable: true });
  const list = Array.from({ length: fingers }, (_, i) => ({ identifier: i, clientX: at.x + i * 20, clientY: at.y, target }));
  Object.defineProperty(event, 'touches', { value: list });
  Object.defineProperty(event, 'targetTouches', { value: list });
  Object.defineProperty(event, 'changedTouches', { value: list });
  target.dispatchEvent(event);
  return event;
}

/** A wheel turn; Ctrl or ⌘ held when `zoom` says so. */
export function wheel(target: EventTarget, options: { ctrl?: boolean; meta?: boolean; deltaY?: number } = {}): WheelEvent {
  const event = new WheelEvent('wheel', { bubbles: true, cancelable: true, ctrlKey: !!options.ctrl, metaKey: !!options.meta, deltaY: options.deltaY ?? -240, clientX: 100, clientY: 100 });
  target.dispatchEvent(event);
  return event;
}

const codes: Record<string, number> = {
  Enter: 13, Escape: 27, Home: 36, End: 35, ArrowLeft: 37, ArrowUp: 38, ArrowRight: 39, ArrowDown: 40, Tab: 9, '+': 187, '-': 189, '0': 48,
};

/** A key press with the legacy `keyCode` that Leaflet (and the CDK) still read; returns the event to see whether it was stopped. */
export function key(target: EventTarget, name: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...init });
  Object.defineProperty(event, 'keyCode', { value: codes[name] ?? 0 });
  target.dispatchEvent(event);
  return event;
}
