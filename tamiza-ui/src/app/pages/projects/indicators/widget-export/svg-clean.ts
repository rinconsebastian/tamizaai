// Turns the live <svg> of a chart into a standalone one (add-widget-export, design D5, D6): the marks the chart drew, each with the
// colors, strokes and fonts the page computed for it written as attributes, and nothing else. The result is built node by node from
// an allowlist, so a class, a data attribute, an event handler, a link or a script of the page cannot reach a file.

const SVG_NS = 'http://www.w3.org/2000/svg';

/** Reads the computed value of a CSS property of an element; in a browser, `getComputedStyle`. */
export type StyleReader = (element: Element) => (property: string) => string;

export interface CleanOptions {
  styleOf: StyleReader;
  /** Written before every id, so two images in one document never share a pattern. */
  prefix: string;
  /** Writes a computed color in a form every SVG program reads (`rgb()`); a browser's oklch and color-mix are not. Default: as given. */
  color?: (value: string) => string;
}

const containers = new Set(['g', 'defs', 'pattern', 'clippath']);
const shapes = new Set(['rect', 'path', 'line', 'polyline', 'polygon', 'circle']);
const texts = new Set(['text', 'tspan']);
const allowed = new Set([...containers, ...shapes, ...texts]);

/** The attributes of each element that say where it is and how big; everything else is dropped or computed. */
const geometry: Record<string, string[]> = {
  g: ['transform'],
  pattern: ['width', 'height', 'patternUnits', 'patternTransform', 'x', 'y'],
  clippath: ['clipPathUnits'],
  rect: ['x', 'y', 'width', 'height', 'rx', 'ry', 'transform'],
  path: ['d', 'transform'],
  line: ['x1', 'y1', 'x2', 'y2', 'transform'],
  polyline: ['points', 'transform'],
  polygon: ['points', 'transform'],
  circle: ['cx', 'cy', 'r', 'transform'],
  text: ['x', 'y', 'dx', 'dy', 'transform'],
  tspan: ['x', 'y', 'dx', 'dy'],
};

/** Marks that only exist to answer the pointer or to show what is pointed at: never in a picture. */
const transient = new Set(['chart-hit', 'outlier-hit', 'chart-guide', 'heat-active', 'likert-active']);

const isTransient = (element: Element) => transient.has(element.getAttribute('data-testid') ?? '') || element.classList.contains('tmz-active');

const defaults = { 'fill-opacity': '1', 'stroke-opacity': '1', opacity: '1', 'fill-rule': 'nonzero', 'stroke-dasharray': 'none', 'stroke-linecap': 'butt', 'stroke-linejoin': 'miter' };

const px = (value: string) => value.replace(/px$/, '');
const invisible = (value: string) => value === '' || value === 'none' || value === 'transparent' || /^rgba\(\s*0,\s*0,\s*0,\s*0\s*\)$/.test(value);

/**
 * A sanitized copy of `source` as a nested `<svg>` with the source's own width, height and view box. `source` should be a clone put
 * where the page's styles reach it and nothing hovers or focuses it (see `inProbe`).
 */
export function sanitizeSvg(source: Element, options: CleanOptions): SVGSVGElement {
  const document = source.ownerDocument;
  const color = options.color ?? ((value: string) => value);
  const ids = new Map<string, string>();
  source.querySelectorAll('[id]').forEach((element) => ids.set(element.id, options.prefix + element.id));

  /** `url("#hatch")` and `url(#hatch)` as a reference to the image's own, prefixed id; any other reference is dropped. */
  const reference = (value: string | null): string | null => {
    const match = value?.match(/^url\(\s*["']?#([^"')\s]+)["']?\s*\)$/);
    const target = match ? ids.get(match[1]) : undefined;
    return target ? `url(#${target})` : null;
  };

  const paint = (value: string): string | null => {
    if (value.startsWith('url(')) {
      return reference(value);
    }
    return invisible(value) ? 'none' : color(value);
  };

  const build = (element: Element): Element | null => {
    const tag = element.localName.toLowerCase();
    if (!allowed.has(tag) || isTransient(element)) {
      return null;
    }
    const read = options.styleOf(element);
    if (read('display') === 'none') {
      return null;
    }

    const node = document.createElementNS(SVG_NS, tag === 'clippath' ? 'clipPath' : tag);
    for (const name of geometry[tag] ?? []) {
      const value = element.getAttribute(name);
      if (value !== null) {
        node.setAttribute(name, value);
      }
    }
    const id = element.getAttribute('id');
    if (id && ids.has(id)) {
      node.setAttribute('id', ids.get(id)!);
    }
    const clip = reference(element.getAttribute('clip-path'));
    if (clip) {
      node.setAttribute('clip-path', clip);
    }

    // A text with a halo (a reference line's label, a data label: the surface color stroked round the letters, painted under them) keeps
    // its stroke and its paint order. Any other text has no stroke of its own to carry, whatever it inherits (add-chart-annotations, D9).
    const order = texts.has(tag) ? read('paint-order') : '';
    const halo = /^(normal|fill|stroke|markers)( (fill|stroke|markers))*$/.test(order) && order !== 'normal';

    if (shapes.has(tag) || texts.has(tag)) {
      const fill = paint(read('fill'));
      const stroke = shapes.has(tag) || halo ? paint(read('stroke')) : null;
      // A shape that paints nothing answers a pointer or marks nothing: it is not in the picture.
      if (shapes.has(tag) && (fill === null || fill === 'none') && (stroke === null || stroke === 'none')) {
        return null;
      }
      if (fill !== null) {
        node.setAttribute('fill', fill);
      }
      if (stroke !== null && stroke !== 'none') {
        node.setAttribute('stroke', stroke);
        node.setAttribute('stroke-width', px(read('stroke-width')));
      }
      for (const [property, normal] of Object.entries(defaults)) {
        const value = read(property);
        if (value && value !== normal && !(property.startsWith('stroke-') && (stroke === null || stroke === 'none'))) {
          node.setAttribute(property, property === 'stroke-dasharray' ? px(value).replace(/px/g, '') : value);
        }
      }
    }

    if (texts.has(tag)) {
      if (halo) {
        node.setAttribute('paint-order', order);
      }
      node.setAttribute('font-size', px(read('font-size')));
      const weight = read('font-weight');
      if (weight && weight !== '400' && weight !== 'normal') {
        node.setAttribute('font-weight', weight);
      }
      const anchor = read('text-anchor');
      if (anchor && anchor !== 'start') {
        node.setAttribute('text-anchor', anchor);
      }
      const baseline = read('dominant-baseline');
      if (baseline && baseline !== 'auto') {
        node.setAttribute('dominant-baseline', baseline);
      }
      const mode = read('writing-mode');
      if (mode && mode !== 'horizontal-tb') {
        node.setAttribute('writing-mode', mode);
      }
      // The text, as the screen shows it: the template's whitespace collapsed, a mark's <title> (its tooltip) left out.
      let content = '';
      for (const child of Array.from(element.childNodes)) {
        if (child.nodeType === 3) {
          content += child.textContent ?? '';
        } else if (child instanceof Element && child.localName.toLowerCase() === 'tspan') {
          const span = build(child);
          if (span) {
            node.appendChild(span);
          }
        }
      }
      content = content.replace(/\s+/g, ' ').trim();
      if (content) {
        node.insertBefore(document.createTextNode(content), node.firstChild);
      }
      return node;
    }

    for (const child of Array.from(element.children)) {
      const built = build(child);
      if (built) {
        node.appendChild(built);
      }
    }
    return node;
  };

  const root = document.createElementNS(SVG_NS, 'svg');
  const width = source.getAttribute('width');
  const height = source.getAttribute('height');
  root.setAttribute('width', width ?? '0');
  root.setAttribute('height', height ?? '0');
  root.setAttribute('viewBox', source.getAttribute('viewBox') ?? `0 0 ${width ?? 0} ${height ?? 0}`);
  for (const child of Array.from(source.children)) {
    const built = build(child);
    if (built) {
      root.appendChild(built);
    }
  }
  return root;
}

/**
 * Runs `work` on a clone of `source` placed where the page's styles reach it but nothing else does (design D5): appended to the body,
 * outside the application, hidden, inert and off screen, with the inline style of the widget around the source (the sequential scale's
 * variables) and optionally a class. The clone is never hovered or focused, so no state of the page's own leaks into the picture.
 */
export function inProbe<T>(source: Element, hostStyle: string, hostClass: string | null, work: (clone: Element) => T): T {
  const document = source.ownerDocument;
  const probe = document.createElement('div');
  probe.setAttribute('aria-hidden', 'true');
  probe.setAttribute('inert', '');
  probe.setAttribute('style', `${hostStyle};position:absolute;left:-100000px;top:0;visibility:hidden;pointer-events:none`);
  if (hostClass) {
    probe.setAttribute('class', hostClass);
  }
  const clone = source.cloneNode(true) as Element;
  probe.appendChild(clone);
  document.body.appendChild(probe);
  try {
    return work(clone);
  } finally {
    probe.remove();
  }
}

/** The inline style of the elements around `source`, outermost first, up to the application: the variables a chart's host sets. */
export function inheritedStyle(source: Element): string {
  const styles: string[] = [];
  for (let element = source.parentElement; element && element.localName !== 'app-root'; element = element.parentElement) {
    const style = element.getAttribute('style');
    if (style) {
      styles.unshift(style);
    }
  }
  return styles.join(';');
}

/** `getComputedStyle`, as a `StyleReader`. */
export const computedStyle: StyleReader = (element) => {
  const style = element.ownerDocument.defaultView!.getComputedStyle(element);
  return (property) => style.getPropertyValue(property).trim();
};

/**
 * Writes any CSS color as `rgb()` or `rgba()`, which every program that opens an SVG reads: the browser draws it on a pixel and reads
 * the pixel back. Where there is no canvas the color is returned as given.
 */
export function srgbColor(document: Document): (value: string) => string {
  const cache = new Map<string, string>();
  let context: CanvasRenderingContext2D | null | undefined;
  return (value) => {
    const known = cache.get(value);
    if (known !== undefined) {
      return known;
    }
    if (context === undefined) {
      try {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 1;
        context = canvas.getContext('2d', { willReadFrequently: true });
      } catch {
        context = null;
      }
    }
    let result = value;
    if (context && !/^(rgb|#)/.test(value)) {
      try {
        context.clearRect(0, 0, 1, 1);
        context.fillStyle = '#000';
        context.fillStyle = value;
        context.fillRect(0, 0, 1, 1);
        const [r, g, b, a] = context.getImageData(0, 0, 1, 1).data;
        result = a === 255 ? `rgb(${r}, ${g}, ${b})` : `rgba(${r}, ${g}, ${b}, ${Math.round((a / 255) * 100) / 100})`;
      } catch {
        result = value;
      }
    }
    cache.set(value, result);
    return result;
  };
}
