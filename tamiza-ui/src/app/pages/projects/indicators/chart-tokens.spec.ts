import styles from '../../../../styles.css';

// The color tokens of the charts (src/styles.css): the sequential ramp and the labels on its steps, and the two arms and the neutral
// midpoint of the diverging Likert scale (design D5, task 13.1). The arms were validated as ordinal ramps with the palette validator
// (commands and results in the design's Verification Results); these checks keep the tokens at the values that were validated and
// keep the properties the validator checks: one hue, lightness stepping the same way, steps apart, the pale end clear of the surface.

/** The declarations of the first rule whose selector is exactly `selector`, as a map of custom properties. */
function tokens(selector: string): Record<string, string> {
  const flat = styles.replace(/\/\*[\s\S]*?\*\//g, '');
  const rule = Array.from(flat.matchAll(/([^{}]+)\{([^{}]*)\}/g)).find((match) => match[1].trim() === selector);
  expect(rule, selector).toBeDefined();
  return Object.fromEntries(Array.from(rule![2].matchAll(/(--[\w-]+):\s*([^;]+);/g)).map((m) => [m[1], m[2].trim()]));
}

const light = tokens(':root');
const dark = tokens('.dark');

const ramp = (set: Record<string, string>, name: string, count: number) => Array.from({ length: count }, (_, i) => set[`--tmz-${name}-${i + 1}`]);

const linear = (hex: string) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
const luminance = (hex: string) => {
  const [r, g, b] = linear(hex);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const contrast = (a: string, b: string) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};
/** OKLab: lightness L, chroma and hue angle in degrees. */
function oklab(hex: string) {
  const [r, g, b] = linear(hex);
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  const a = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s;
  const bb = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s;
  return { lightness: 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s, chroma: Math.hypot(a, bb), hue: ((Math.atan2(bb, a) * 180) / Math.PI + 360) % 360 };
}

/** The surfaces a chart sits on: white, and the dark theme's `neutral-primary-soft` (Tailwind's gray-900). */
const surface = { light: '#ffffff', dark: '#101828' };

describe('the diverging Likert tokens', () => {
  /** What the palette validator was run on: five steps of each arm, nearest the neutral first, per theme. */
  const validated = {
    light: { neg: ['#ea9a93', '#dd716a', '#c74845', '#9e3432', '#762221'], pos: ['#86b6ef', '#5598e7', '#2a78d6', '#1c5cab', '#104281'], neutral: '#f0efec' },
    dark: { neg: ['#892b2a', '#b13f3c', '#d75853', '#e4857e', '#f1aea8'], pos: ['#184f95', '#256abf', '#3987e5', '#6da7ec', '#9ec5f4'], neutral: '#383835' },
  };

  it.each(['light', 'dark'] as const)('has the two arms of five steps and the neutral midpoint that were validated in the %s theme', (theme) => {
    const set = theme === 'light' ? light : dark;

    expect(ramp(set, 'div-neg', 5)).toEqual(validated[theme].neg);
    expect(ramp(set, 'div-pos', 5)).toEqual(validated[theme].pos);
    expect(set['--tmz-div-neutral']).toBe(validated[theme].neutral);
  });

  it.each([
    ['light', 'neg'],
    ['light', 'pos'],
    ['dark', 'neg'],
    ['dark', 'pos'],
  ] as const)('keeps the %s %s arm one hue, stepped one way with visible gaps, its pale end clear of the surface (ordinal checks)', (theme, arm) => {
    const steps = ramp(theme === 'light' ? light : dark, `div-${arm}`, 5);
    const lightness = steps.map((hex) => oklab(hex).lightness);
    const direction = theme === 'light' ? -1 : 1; // light to dark on the light theme, dark to light on the dark one

    lightness.slice(1).forEach((value, i) => expect((value - lightness[i]) * direction, `${arm} step ${i + 2}`).toBeGreaterThanOrEqual(0.06));
    const hues = steps.map((hex) => oklab(hex).hue);
    expect(Math.max(...hues) - Math.min(...hues)).toBeLessThanOrEqual(40);
    expect(contrast(steps[0], surface[theme])).toBeGreaterThanOrEqual(2);
  });

  it('puts the two arms on opposite sides of the hue circle, with a neutral midpoint that is a gray', () => {
    for (const set of [light, dark]) {
      const negative = oklab(set['--tmz-div-neg-3']).hue;
      const positive = oklab(set['--tmz-div-pos-3']).hue;
      expect(Math.abs(negative - positive)).toBeGreaterThan(150);
      expect(oklab(set['--tmz-div-neutral']).chroma).toBeLessThan(0.02); // far below the 0.10 floor of a hue: it reads as gray
    }
  });
});

describe('the sequential tokens and the labels on their steps', () => {
  it('runs the seven steps light to dark on the light theme and dark to light on the dark one, so the low end recedes in both', () => {
    const lightSteps = ramp(light, 'seq', 7).map((hex) => oklab(hex).lightness);
    const darkSteps = ramp(dark, 'seq', 7).map((hex) => oklab(hex).lightness);

    expect(lightSteps).toEqual([...lightSteps].sort((a, b) => b - a));
    expect(darkSteps).toEqual([...darkSteps].sort((a, b) => a - b));
  });

  it.each(['light', 'dark'] as const)('gives every step a label color that reads on it in the %s theme', (theme) => {
    const set = theme === 'light' ? light : dark;

    ramp(set, 'seq', 7).forEach((fill, i) => {
      const ink = set[`--tmz-seq-ink-${i + 1}`];
      expect(['#ffffff', '#0b0b0b'], `step ${i + 1}`).toContain(ink);
      expect(contrast(ink, fill), `step ${i + 1}`).toBeGreaterThanOrEqual(3.5);
    });
  });

  it('flips the label colors between the themes, as the steps reverse', () => {
    expect(ramp(light, 'seq-ink', 7)).toEqual(['#0b0b0b', '#0b0b0b', '#0b0b0b', '#ffffff', '#ffffff', '#ffffff', '#ffffff']);
    expect(ramp(dark, 'seq-ink', 7)).toEqual(['#ffffff', '#ffffff', '#ffffff', '#0b0b0b', '#0b0b0b', '#0b0b0b', '#0b0b0b']);
  });
});
