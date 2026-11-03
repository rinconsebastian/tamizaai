import { CleanOptions, StyleReader, inProbe, inheritedStyle, sanitizeSvg, srgbColor } from './svg-clean';

// A page's styles, by class: what `getComputedStyle` would answer for an element of the page (jsdom has no Tailwind).
const sheet: Record<string, Record<string, string>> = {
  'fill-body': { fill: 'rgb(71, 85, 105)' },
  'stroke-default': { stroke: 'rgb(226, 232, 240)' },
  'stroke-heading': { stroke: 'rgb(15, 23, 42)' },
  hidden: { display: 'none' },
};

const styleOf: StyleReader = (element) => {
  const own: Record<string, string> = { fill: 'rgb(0, 0, 0)', stroke: 'none', 'stroke-width': '1px', 'font-size': '16px', 'font-weight': '400', 'text-anchor': 'start' };
  for (const name of Array.from(element.classList)) {
    Object.assign(own, sheet[name]);
  }
  const inline = (element as SVGElement).style;
  for (const property of ['fill', 'stroke', 'opacity']) {
    const value = inline?.getPropertyValue(property);
    if (value) own[property] = value;
  }
  for (const attribute of ['fill', 'stroke', 'stroke-width', 'font-size', 'text-anchor', 'writing-mode', 'stroke-dasharray', 'fill-opacity']) {
    const value = element.getAttribute(attribute);
    if (value !== null) own[attribute] = attribute === 'stroke-width' || attribute === 'font-size' ? `${value}px` : value;
  }
  return (property) => own[property] ?? '';
};

const options: CleanOptions = { styleOf, prefix: 'wx1-' };

function parse(markup: string): Element {
  const host = document.createElement('div');
  host.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="300" height="200" viewBox="0 0 300 200">${markup}</svg>`;
  return host.firstElementChild!;
}

const serialize = (node: Node) => new XMLSerializer().serializeToString(node);

describe('sanitizeSvg', () => {
  it('keeps the size and the view box of the chart', () => {
    const clean = sanitizeSvg(parse(''), options);

    expect(clean.getAttribute('width')).toBe('300');
    expect(clean.getAttribute('height')).toBe('200');
    expect(clean.getAttribute('viewBox')).toBe('0 0 300 200');
  });

  it('writes the colors the page computed as attributes, and no class or style', () => {
    const clean = sanitizeSvg(
      parse(`<line class="stroke-default" x1="0" y1="10" x2="300" y2="10" stroke-width="1" />
             <rect class="hover:opacity-80" style="fill: rgb(1, 2, 3)" x="5" y="6" width="40" height="30" rx="2" data-testid="chart-mark" />
             <text class="fill-body" x="4" y="190" font-size="11" text-anchor="end">  Cali
             </text>`),
      options,
    );

    const markup = serialize(clean);
    expect(markup).toContain('<line x1="0" y1="10" x2="300" y2="10" fill="rgb(0, 0, 0)" stroke="rgb(226, 232, 240)" stroke-width="1"/>');
    expect(markup).toContain('<rect x="5" y="6" width="40" height="30" rx="2" fill="rgb(1, 2, 3)"/>');
    expect(markup).toContain('<text x="4" y="190" fill="rgb(71, 85, 105)" font-size="11" text-anchor="end">Cali</text>');
    expect(markup).not.toMatch(/class=|style=|data-/);
  });

  it('drops the marks that only answer the pointer or show what is pointed at', () => {
    const clean = sanitizeSvg(
      parse(`<rect fill="transparent" x="0" y="0" width="9" height="9" data-testid="chart-hit" />
             <rect fill="transparent" x="0" y="0" width="9" height="9" data-testid="outlier-hit" />
             <line class="stroke-heading" x1="0" y1="0" x2="1" y2="1" data-testid="chart-guide" />
             <rect class="stroke-heading" fill="none" x="1" y="1" width="9" height="9" data-testid="heat-active" />
             <rect class="stroke-heading" fill="none" x="1" y="1" width="9" height="9" data-testid="likert-active" />
             <path class="tmz-active" d="M0 0" style="fill: red" />
             <rect fill="transparent" x="0" y="0" width="9" height="9" />
             <rect x="2" y="2" width="9" height="9" style="fill: rgb(9, 9, 9)" data-testid="chart-mark" />`),
      options,
    );

    expect(clean.querySelectorAll('*')).toHaveLength(1);
    expect(clean.querySelector('rect')!.getAttribute('fill')).toBe('rgb(9, 9, 9)');
  });

  it('drops an element the page computed as not displayed, with everything inside it', () => {
    const clean = sanitizeSvg(parse(`<g class="hidden"><rect x="0" y="0" width="5" height="5" style="fill: red" /></g><rect x="1" y="1" width="5" height="5" style="fill: blue" />`), options);

    expect(clean.querySelectorAll('rect')).toHaveLength(1);
    expect(clean.querySelector('rect')!.getAttribute('fill')).toBe('blue');
  });

  it('does not carry the lifted opacity of a mark that was pointed at', () => {
    const clean = sanitizeSvg(parse(`<rect x="0" y="0" width="5" height="5" style="fill: blue" opacity="0.8" />`), options);

    expect(clean.querySelector('rect')!.hasAttribute('opacity')).toBe(false);
  });

  it('removes every element and attribute that is not a mark: scripts, links, handlers, titles, embedded HTML', () => {
    const clean = sanitizeSvg(
      parse(`<script>alert(1)</script>
             <foreignObject width="10" height="10"><div xmlns="http://www.w3.org/1999/xhtml">hi</div></foreignObject>
             <image href="https://example.test/x.png" width="5" height="5" />
             <a href="https://example.test"><rect x="0" y="0" width="5" height="5" style="fill: red" /></a>
             <style>rect { fill: red }</style>
             <rect x="3" y="3" width="5" height="5" style="fill: red" onclick="steal()" onload="x()" tabindex="0" role="img" aria-label="Cali" href="https://example.test" data-testid="chart-mark" class="a b" _ngcontent-ng-c1="">
               <title>Cali: 75</title>
             </rect>`),
      options,
    );

    expect(serialize(clean)).toBe('<svg xmlns="http://www.w3.org/2000/svg" width="300" height="200" viewBox="0 0 300 200"><rect x="3" y="3" width="5" height="5" fill="red"/></svg>');
  });

  it('writes a label that looks like markup as text, never as markup', () => {
    const source = parse('<text x="1" y="1"></text>');
    source.querySelector('text')!.textContent = '<script>alert(1)</script> & "quotes"';

    const clean = sanitizeSvg(source, options);

    expect(clean.querySelector('script')).toBeNull();
    expect(clean.querySelector('text')!.textContent).toBe('<script>alert(1)</script> & "quotes"');
    expect(serialize(clean)).toContain('&lt;script&gt;alert(1)&lt;/script&gt; &amp; "quotes"');
  });

  it('prefixes the ids and points every reference at the new ones', () => {
    const clean = sanitizeSvg(
      parse(`<defs><pattern id="heat-hatch-1" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="6" height="6" style="fill: rgb(1, 1, 1)" /></pattern>
             <clipPath id="clip"><rect width="10" height="10" style="fill: rgb(1, 1, 1)" /></clipPath></defs>
             <rect x="0" y="0" width="9" height="9" fill="url(#heat-hatch-1)" />
             <g clip-path="url(#clip)"><rect x="0" y="0" width="9" height="9" style="fill: url('#heat-hatch-1')" /></g>
             <rect x="0" y="0" width="9" height="9" fill="url(#elsewhere)" />`),
      options,
    );

    const markup = serialize(clean);
    expect(clean.querySelector('pattern')!.getAttribute('id')).toBe('wx1-heat-hatch-1');
    expect(clean.querySelector('pattern')!.getAttribute('patternTransform')).toBe('rotate(45)');
    expect(clean.querySelector('clipPath')!.getAttribute('id')).toBe('wx1-clip');
    expect(clean.querySelector('g')!.getAttribute('clip-path')).toBe('url(#wx1-clip)');
    expect(markup).toContain('fill="url(#wx1-heat-hatch-1)"');
    expect(markup).not.toContain('url(#heat-');
    expect(markup).not.toContain('elsewhere');
  });

  it('keeps a rotated label and the weight and baseline of a text', () => {
    const clean = sanitizeSvg(parse(`<text x="4" y="9" font-size="12" writing-mode="vertical-rl" text-anchor="middle">Piped</text>`), options);

    expect(serialize(clean)).toContain('writing-mode="vertical-rl"');
    expect(serialize(clean)).toContain('text-anchor="middle"');
  });

  it('writes colors through the converter, so a program that does not know oklch still draws them', () => {
    const source = parse(`<rect x="0" y="0" width="5" height="5" style="fill: oklch(0.6 0.2 250)" />`);

    const clean = sanitizeSvg(source, { ...options, color: (value) => (value.startsWith('oklch') ? 'rgb(10, 20, 30)' : value) });

    expect(clean.querySelector('rect')!.getAttribute('fill')).toBe('rgb(10, 20, 30)');
  });
});

describe('inProbe', () => {
  it('puts a clone where the page\'s styles reach it and nothing else does, and removes it afterwards', () => {
    const app = document.createElement('app-root');
    document.body.appendChild(app);
    const chart = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    app.appendChild(chart);
    let during: { parent: Element | null; insideApp: boolean; attributes: Record<string, string | null>; className: string } | null = null;

    const result = inProbe(chart, '--tmz-seq-1: red', 'tmz-map', (clone) => {
      const probe = clone.parentElement!;
      during = {
        parent: probe.parentElement,
        insideApp: app.contains(probe),
        attributes: { hidden: probe.getAttribute('aria-hidden'), inert: probe.getAttribute('inert'), style: probe.getAttribute('style') },
        className: probe.className,
      };
      return clone === chart ? 'same' : 'clone';
    });

    expect(result).toBe('clone');
    expect(during!.parent).toBe(document.body);
    expect(during!.insideApp).toBe(false);
    expect(during!.attributes['hidden']).toBe('true');
    expect(during!.attributes['inert']).toBe('');
    expect(during!.attributes['style']).toContain('visibility:hidden');
    expect(during!.attributes['style']).toContain('left:-100000px');
    expect(during!.attributes['style']).toContain('--tmz-seq-1: red');
    expect(during!.className).toBe('tmz-map');
    expect(document.body.querySelectorAll('[inert]')).toHaveLength(0);
    app.remove();
  });

  it('removes the probe when the work fails', () => {
    const chart = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    document.body.appendChild(chart);

    expect(() => inProbe(chart, '', null, () => { throw new Error('boom'); })).toThrow('boom');

    expect(document.body.querySelectorAll('[inert]')).toHaveLength(0);
    chart.remove();
  });
});

describe('inheritedStyle', () => {
  it('collects the inline style of the elements around a chart up to the application, outermost first', () => {
    const root = document.createElement('app-root');
    root.setAttribute('style', 'color: red');
    const outer = document.createElement('app-widget-view');
    const host = document.createElement('app-heatmap-chart');
    host.setAttribute('style', '--tmz-seq-1: #fff; --tmz-seq-2: #eee');
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    root.append(outer);
    outer.append(host);
    host.append(svg);

    expect(inheritedStyle(svg)).toBe('--tmz-seq-1: #fff; --tmz-seq-2: #eee');
  });
});

describe('srgbColor', () => {
  it('gives a color back as it is where there is no canvas to ask', () => {
    const convert = srgbColor(document);

    expect(convert('oklch(0.6 0.2 250)')).toBe('oklch(0.6 0.2 250)');
    expect(convert('rgb(1, 2, 3)')).toBe('rgb(1, 2, 3)');
  });
});
