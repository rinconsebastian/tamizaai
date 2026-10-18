import leaflet from 'leaflet/dist/leaflet.css';
import styles from '../../../../styles.css';

// The map's global styles (design D3, D5, D18): Leaflet's DOM never carries a component's _ngcontent attribute, so the shape colours
// and the touch override live unlayered in src/styles.css. Nothing in jsdom paints them, so these checks read the files as text
// (the .css loader of the build in angular.json; the builder's browser bundle has no node:fs).

/** The declarations of the rules (joined, when there are several) whose selector list holds exactly `selector`, or null. */
function ruleOf(css: string, selector: string): string | null {
  const flat = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const rules = Array.from(flat.matchAll(/([^{}]+)\{([^{}]*)\}/g));
  const found = rules.filter((rule) => rule[1].split(',').map((s) => s.trim()).includes(selector));
  return found.length ? found.map((rule) => rule[2].trim()).join(' ') : null;
}

/** Id, class and type counts of a simple compound selector: (a, b, c). */
function specificity(selector: string): [number, number, number] {
  const classes = (selector.match(/\.[\w-]+|\[[^\]]+\]|:(?!:)[\w-]+/g) ?? []).length;
  const types = (selector.replace(/\.[\w-]+|\[[^\]]+\]|:[\w-]+/g, ' ').match(/(?:^|\s)[a-z][\w-]*/gi) ?? []).length;
  return [0, classes, types];
}

const outranks = (a: string, b: string) => {
  const [x, y] = [specificity(a), specificity(b)];
  return x[1] > y[1] || (x[1] === y[1] && x[2] > y[2]);
};

describe('map styles', () => {
  it('fills the shapes with the seven sequential steps, by class', () => {
    for (let step = 1; step <= 7; step++) {
      expect(ruleOf(styles, `.tmz-map path.tmz-seq-${step}`)).toBe(`fill: var(--tmz-seq-${step});`);
    }
  });

  it('strokes the shapes and highlights the active one', () => {
    expect(ruleOf(styles, '.tmz-map path')).toContain('stroke: var(--color-neutral-primary-soft');
    expect(ruleOf(styles, '.tmz-map path.tmz-active')).toContain('stroke-width: 3px');
  });

  it('keeps the container transparent and in the page font, over Leaflet\'s grey', () => {
    expect(ruleOf(styles, '.tmz-map.leaflet-container')).toContain('background: transparent; font: inherit;');
    expect(ruleOf(leaflet, '.leaflet-container')).toContain('background: #ddd');
    expect(outranks('.tmz-map.leaflet-container', '.leaflet-container')).toBe(true);
  });

  it('lets one finger scroll the page whatever classes Leaflet adds, with a selector that outranks Leaflet\'s', () => {
    const ours = '.tmz-map.leaflet-container.leaflet-touch-drag.leaflet-touch-zoom';
    const theirs = '.leaflet-container.leaflet-touch-drag.leaflet-touch-zoom';

    expect(ruleOf(styles, ours)).toBe('touch-action: pan-x pan-y;');
    expect(ruleOf(styles, '.tmz-map.leaflet-container')).not.toBeNull();
    expect(ruleOf(styles, '.tmz-map.leaflet-container.leaflet-touch-drag')).not.toBeNull();
    expect(ruleOf(leaflet, theirs)).toContain('touch-action: none');
    expect(outranks(ours, theirs)).toBe(true);
    // The two shorter selectors beat the classes Leaflet puts on one handler alone.
    expect(outranks('.tmz-map.leaflet-container.leaflet-touch-drag', '.leaflet-touch-drag')).toBe(true);
  });

  it('is unlayered: the block sits outside every @layer', () => {
    const at = styles.indexOf('.tmz-map path.tmz-seq-1');
    const before = styles.slice(0, at);
    const opened = (before.match(/@layer[^{;]*\{/g) ?? []).length;
    expect(opened).toBe(1); // only the base layer above it, which is closed
    expect((before.match(/\{/g) ?? []).length - (before.match(/\}/g) ?? []).length).toBe(0);
  });
});
