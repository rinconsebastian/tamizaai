import styles from '../styles.css';

// The global print rules (dashboard printing, design D3, D12). Nothing in jsdom prints or paints, so these checks read src/styles.css
// as text (the .css loader of the build in angular.json), as map-styles.spec.ts does.

const withoutComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');

/** The bodies of every top-level block whose prelude is exactly `prelude` (`@media print`), braces matched. */
function blocksOf(css: string, prelude: string): string[] {
  const flat = withoutComments(css);
  const bodies: string[] = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < flat.length; i++) {
    if (flat[i] === '{') {
      if (depth === 0 && flat.slice(start, i).trim() === prelude) {
        let inner = 1;
        let j = i + 1;
        for (; j < flat.length && inner > 0; j++) {
          inner += flat[j] === '{' ? 1 : flat[j] === '}' ? -1 : 0;
        }
        bodies.push(flat.slice(i + 1, j - 1));
        i = j - 1;
        start = j;
        continue;
      }
      depth++;
    } else if (flat[i] === '}') {
      depth--;
      if (depth === 0) {
        start = i + 1;
      }
    } else if (flat[i] === ';' && depth === 0) {
      start = i + 1;
    }
  }
  return bodies;
}

/** The declarations of the rule for exactly `selector` in a block's body, or null. */
function ruleIn(body: string, selector: string): string | null {
  const rule = Array.from(body.matchAll(/([^{}]+)\{([^{}]*)\}/g)).find((r) => r[1].split(',').map((s) => s.trim()).includes(selector));
  return rule ? rule[2].trim() : null;
}

describe('print styles', () => {
  const print = blocksOf(styles, '@media print').join('\n');

  it('prints on white sheets, over the page background of the base layer', () => {
    expect(ruleIn(print, 'html')).toBe('background: #fff;');
  });

  it('leaves the CDK overlay out of every printout: menus, dialogs and tooltips', () => {
    expect(ruleIn(print, '.cdk-overlay-container')).toBe('display: none;');
  });

  it('scales a chart into its box on paper when the box came out a different size (design D2)', () => {
    expect(ruleIn(print, '.tmz-chart-svg')).toBe('width: 100%; height: 100%;');
  });

  it('clips the paper canvas on a screen narrower than it while the print is prepared, rather than scroll the page', () => {
    expect(ruleIn(blocksOf(styles, '@media screen').join('\n'), '.tmz-printing main')).toBe('overflow-x: clip;');
  });

  it('leaves the paper canvas unclipped on paper, so the browser scales a page wider than the sheet down to fit it (design D2)', () => {
    const rules = Array.from(withoutComments(styles).matchAll(/([^{}]+)\{([^{}]*)\}/g)).filter((r) => r[1].trim() === '.tmz-printing main');
    expect(rules).toHaveLength(1); // the screen-only rule above
  });

  it('declares the paper variant for the sheet and for the page while it prepares the print (design D3)', () => {
    const [variant] = blocksOf(styles, '@custom-variant paper');
    expect(variant).toBeDefined();
    const flat = variant.replace(/\s+/g, ' ');
    expect(flat).toContain('@media print { @slot; }');
    expect(flat).toContain('&:where(.tmz-printing, .tmz-printing *) { @slot; }');
  });

  it('is unlayered, so no layered utility outranks it: only its @media print block is open around the rules', () => {
    const flat = withoutComments(styles);
    const before = flat.slice(0, flat.indexOf('.cdk-overlay-container'));
    expect((before.match(/\{/g) ?? []).length - (before.match(/\}/g) ?? []).length).toBe(1);
    expect(before.slice(before.lastIndexOf('@')).startsWith('@media print')).toBe(true);
  });
});
