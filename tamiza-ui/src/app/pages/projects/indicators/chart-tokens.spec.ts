import styles from '../../../../styles.css';

// The color tokens of the charts (src/styles.css): the eight categorical colors of each palette, the seven steps of each sequential
// scale and the labels on them, and the two arms and the neutral midpoint of the diverging Likert scale (add-two-dimension-widgets,
// design D5, task 13.1; add-dashboard-palettes, design D2, task 7.3). The palettes, the scales and the Likert arms were validated with
// the palette validator (commands and results in the Verification Results of each design); these checks keep the tokens at the values
// that were validated and keep the properties the validator checks: for a palette, the lightness band, the chroma floor and
// neighbors that full-color readers tell apart; for a scale, one hue, lightness stepping the same way, steps apart, labels that read.
// The color-vision-deficiency check stays with the validator and its recorded results: simulating the deficiencies here would
// duplicate the script.

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
/** OKLab: lightness L, chroma and hue angle in degrees, and the a and b axes the distance between two colors is measured on. */
function oklab(hex: string) {
  const [r, g, b] = linear(hex);
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  const a = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s;
  const bb = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s;
  return { lightness: 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s, chroma: Math.hypot(a, bb), hue: ((Math.atan2(bb, a) * 180) / Math.PI + 360) % 360, a, b: bb };
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

const themes = ['light', 'dark'] as const;
type Theme = (typeof themes)[number];

/**
 * What the palette validator was run on: the eight colors of each palette in their fixed order, per theme (add-dashboard-palettes,
 * design D2). `tamiza` is the set the charts had before palettes existed, validated in the dataviz method's reference palette, and
 * is pinned literally with it.
 */
const validatedPalettes = {
  tamiza: {
    light: ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#6250d6', '#e34948'],
    dark: ['#3987e5', '#d95926', '#199e70', '#c98500', '#d55181', '#008300', '#9085e9', '#e66767'],
  },
  institutional: {
    light: ['#1f5398', '#18aec2', '#893747', '#52ade3', '#be9922', '#4a5e9f', '#e5957b', '#076392'],
    dark: ['#3f6cbb', '#00a5b6', '#a94d58', '#26a0ca', '#a98900', '#6473bd', '#cf785c', '#357cb1'],
  },
  earth: {
    light: ['#af4a1b', '#345895', '#b88a01', '#82344e', '#669929', '#7e4701', '#934791', '#676e02'],
    dark: ['#b34027', '#506cad', '#988904', '#984e6b', '#65a84b', '#8e5a01', '#a860b0', '#677401'],
  },
  vivid: {
    light: ['#9d32e5', '#00c5ec', '#95c500', '#f22dbd', '#009d1f', '#5485ff', '#c4134d', '#ff8210'],
    dark: ['#b054ff', '#00a5c1', '#7ca400', '#c500a7', '#009f20', '#485edf', '#cc004f', '#df7100'],
  },
} as const;

/**
 * What the validator was run on, as ordinal ramps: the seven steps of each sequential scale, per theme, light to dark on the light
 * theme and dark to light on the dark one. `blue` is the ramp the maps and heat maps had before palettes existed, pinned literally.
 */
const validatedScales = {
  blue: {
    light: ['#cde2fb', '#9ec5f4', '#6da7ec', '#3987e5', '#256abf', '#184f95', '#0d366b'],
    dark: ['#184f95', '#256abf', '#3987e5', '#5598e7', '#6da7ec', '#9ec5f4', '#cde2fb'],
  },
  teal: {
    light: ['#adeee9', '#75d5cf', '#3fb9b2', '#149a95', '#0a7b76', '#065d59', '#05403e'],
    dark: ['#095d59', '#117772', '#1b938d', '#2bada7', '#4bc5bf', '#78dbd5', '#abefea'],
  },
  green: {
    light: ['#bfeebf', '#92d593', '#61bb67', '#359f41', '#1e802c', '#12611e', '#074411'],
    dark: ['#12611e', '#197c28', '#279936', '#38b447', '#55cd5f', '#82e186', '#bdefbd'],
  },
  orange: {
    light: ['#fcd7c0', '#f2b185', '#e68942', '#ca6907', '#a15306', '#7b3e06', '#572a02'],
    dark: ['#793f0d', '#9a5111', '#be6517', '#e0791f', '#fb903c', '#fab688', '#fbd8c1'],
  },
  red: {
    light: ['#fdd4d6', '#f7aaaa', '#ef7d79', '#e24a48', '#b93432', '#8f2420', '#651714'],
    dark: ['#982622', '#bc2e2e', '#e34746', '#f16c6a', '#f49190', '#f7b4b4', '#fdd4d6'],
  },
  purple: {
    light: ['#e9d7fb', '#d0b2f4', '#b68bf0', '#9d61ea', '#8041cc', '#602fa0', '#421f72'],
    dark: ['#6436a6', '#7d45c8', '#9a62e9', '#ae80f1', '#c09ef0', '#d4bbf3', '#e8d8f9'],
  },
  gray: {
    light: ['#dde0e3', '#bdc2c8', '#9ea4ad', '#818791', '#646c74', '#4b515a', '#32383f'],
    dark: ['#51565f', '#656b74', '#80878f', '#959da5', '#abb2b9', '#c5c8cd', '#dde0e3'],
  },
} as const;

const paletteIds = Object.keys(validatedPalettes) as (keyof typeof validatedPalettes)[];
const scaleIds = Object.keys(validatedScales) as (keyof typeof validatedScales)[];
const tokensOf = (theme: Theme) => (theme === 'light' ? light : dark);

/** The labels on the seven steps of a scale: near-black on the pale steps and white on the deep ones, reversed with the steps. */
const validatedInks = {
  light: ['#0b0b0b', '#0b0b0b', '#0b0b0b', '#0b0b0b', '#ffffff', '#ffffff', '#ffffff'],
  dark: ['#ffffff', '#ffffff', '#0b0b0b', '#0b0b0b', '#0b0b0b', '#0b0b0b', '#0b0b0b'],
};

/** The lightness band (OKLab L) of a palette's colors, per theme, and the chroma floor below which a hue reads as gray: the validator's. */
const band = { light: [0.43, 0.77], dark: [0.48, 0.67] } as const;
const chromaFloor = 0.1;
/** How far apart (OKLab x 100) neighbors in a palette's order must be for full-color readers: the validator's normal-vision floor. */
const neighborFloor = 15;
/**
 * How far (OKLab x 100) a palette's colors must stay from the neutral of "Other". The palettes were built to be at least 11.8 from it
 * (institutional's salmon, dark), so 10 is a tripwire for a color drifting toward gray rather than the validated margin.
 */
const otherFloor = 10;

/** The distance between two colors in OKLab, times 100. */
function deltaE(x: string, y: string): number {
  const p = oklab(x);
  const q = oklab(y);
  return 100 * Math.hypot(p.lightness - q.lightness, p.a - q.a, p.b - q.b);
}

/** What is wrong with the eight colors of a palette in `theme` (design D2): count, distinct, band, chroma floor, neighbors, "Other". */
function paletteProblems(colors: readonly string[], theme: Theme, other: string): string[] {
  const found: string[] = [];
  if (colors.length !== 8) {
    found.push(`${colors.length} colors, not 8`);
  }
  if (new Set(colors).size !== colors.length) {
    found.push('a color is used twice');
  }
  colors.forEach((hex, i) => {
    const { lightness, chroma } = oklab(hex);
    const name = `color ${i + 1} (${hex})`;
    if (lightness < band[theme][0] || lightness > band[theme][1]) {
      found.push(`${name}: lightness ${lightness.toFixed(3)} is outside ${band[theme].join('-')}`);
    }
    if (chroma < chromaFloor) {
      found.push(`${name}: chroma ${chroma.toFixed(3)} is below ${chromaFloor}`);
    }
    if (i > 0 && deltaE(colors[i - 1], hex) < neighborFloor) {
      found.push(`${name}: only ${deltaE(colors[i - 1], hex).toFixed(1)} from color ${i}`);
    }
    if (deltaE(hex, other) < otherFloor) {
      found.push(`${name}: only ${deltaE(hex, other).toFixed(1)} from the neutral of "Other"`);
    }
  });
  return found;
}

/**
 * The smallest lightness step (OKLab L) between neighbors of a scale. The six scales built for palettes keep the validator's 0.06;
 * `blue`, which predates the check, keeps 0.04: its dark ladder reuses steps of the light one and has two gaps of 0.046 and 0.049,
 * which the validator's ordinal check flags (the Verification Results record it).
 */
const minStep = (scale: string) => (scale === 'blue' ? 0.04 : 0.06);
/** The chroma below which a color is a gray, and the ceiling of the `gray` scale: it was measured at 0.017 (light) and 0.016 (dark). */
const grayCeiling = 0.02;
/**
 * How far the first step of a scale must stand from the chart surface: it is the step that recedes, so it is a tint rather than the
 * surface itself. Measured 1.30:1 to 1.35:1 on white (blue 1.32:1) and 2.14:1 to 2.41:1 on the dark surface (blue 2.19:1).
 */
const lowEndContrast = { light: 1.25, dark: 2 };
/** How far (OKLab x 100) two scales must be apart at the same step. The pale ends, where every scale converges on the surface, are the closest: 3.3. */
const scaleFloor = 3;

/** The spread of a set of hue angles around the circle, in degrees. */
function hueSpread(hues: readonly number[]): number {
  const sorted = [...hues].sort((x, y) => x - y);
  const gaps = sorted.map((hue, i) => (i === 0 ? sorted[0] + 360 - sorted[sorted.length - 1] : hue - sorted[i - 1]));
  return 360 - Math.max(...gaps);
}

/** What is wrong with the seven steps of a scale and the labels on them in `theme` (design D2): see each check. */
function scaleProblems(id: string, steps: readonly string[], inks: readonly string[], theme: Theme): string[] {
  const found: string[] = [];
  if (steps.length !== 7) {
    found.push(`${steps.length} steps, not 7`);
  }
  if (new Set(steps).size !== steps.length) {
    found.push('a step is used twice');
  }

  // Lightness in one direction with visible steps: light to dark on the light theme, dark to light on the dark one.
  const direction = theme === 'light' ? -1 : 1;
  const lightness = steps.map((hex) => oklab(hex).lightness);
  lightness.slice(1).forEach((value, i) => {
    if ((value - lightness[i]) * direction < minStep(id)) {
      found.push(`step ${i + 2}: lightness moves ${((value - lightness[i]) * direction).toFixed(3)}, under ${minStep(id)}`);
    }
  });

  // One hue. Gray has none: its angle is rounding noise at a chroma this low, so it must stay below a ceiling instead.
  if (id === 'gray') {
    steps.forEach((hex, i) => {
      if (oklab(hex).chroma >= grayCeiling) {
        found.push(`step ${i + 1}: chroma ${oklab(hex).chroma.toFixed(3)} is not a gray (under ${grayCeiling})`);
      }
    });
  } else if (hueSpread(steps.map((hex) => oklab(hex).hue)) > 40) {
    found.push(`hue spread ${hueSpread(steps.map((hex) => oklab(hex).hue)).toFixed(0)} degrees, over 40`);
  }

  // The low end recedes, but stays a tint.
  if (contrast(steps[0], surface[theme]) < lowEndContrast[theme]) {
    found.push(`step 1 is ${contrast(steps[0], surface[theme]).toFixed(2)}:1 on the surface, under ${lowEndContrast[theme]}:1`);
  }

  // The label of each step: white or near-black, the one that reads better, and at 4.5:1 for the small text of a cell. (White on the
  // middle step of blue, #3987e5, is 3.6:1 and near-black 5.4:1: the label that reads better is the one used.)
  steps.forEach((fill, i) => {
    const ink = inks[i];
    const other = ink === '#ffffff' ? '#0b0b0b' : '#ffffff';
    if (!['#ffffff', '#0b0b0b'].includes(ink)) {
      found.push(`step ${i + 1}: label ${ink} is neither white nor near-black`);
    } else if (contrast(ink, fill) < 4.5) {
      found.push(`step ${i + 1}: label ${ink} is ${contrast(ink, fill).toFixed(2)}:1, under 4.5:1`);
    } else if (contrast(ink, fill) < contrast(other, fill)) {
      found.push(`step ${i + 1}: label ${other} reads better than ${ink}`);
    }
  });

  return found;
}

describe('Checks on every palette', () => {
  const other = light['--tmz-series-other'];
  /** The names a catalog must define in a theme: `count` numbered tokens for each id, and their labels for a scale. */
  const expected = (prefix: string, ids: readonly string[], count: number, withInk: boolean) =>
    ids.flatMap((id) => Array.from({ length: count }, (_, i) => [`--tmz-${prefix}-${id}-${i + 1}`, ...(withInk ? [`--tmz-${prefix}-${id}-ink-${i + 1}`] : [])]).flat());

  describe('the tokens', () => {
    it('are the palettes and scales of the catalog and no others, in both themes', () => {
      const palette = expected('pal', paletteIds, 8, false);
      const steps = expected('ramp', scaleIds, 7, true);

      for (const theme of themes) {
        const found = Object.keys(tokensOf(theme));
        expect(found.filter((name) => name.startsWith('--tmz-pal-')).sort(), theme).toEqual([...palette].sort());
        expect(found.filter((name) => name.startsWith('--tmz-ramp-')).sort(), theme).toEqual([...steps].sort());
        expect(found.filter((name) => /^--tmz-series-\d/.test(name)), `${theme}: the old series tokens are gone`).toEqual([]);
      }
    });

    it('keep the neutral of "Other" as it was, for every palette', () => {
      expect(other).toBe('#898781');
    });

    it('give a map and a heat map the blue scale until they set their own steps, in both themes', () => {
      for (const theme of themes) {
        const set = tokensOf(theme);
        for (let n = 1; n <= 7; n++) {
          expect(set[`--tmz-seq-${n}`], `${theme} step ${n}`).toBe(`var(--tmz-ramp-blue-${n})`);
          expect(set[`--tmz-seq-ink-${n}`], `${theme} label ${n}`).toBe(`var(--tmz-ramp-blue-ink-${n})`);
        }
      }
    });
  });

  describe.each(paletteIds)('the palette %s', (id) => {
    it.each(themes)('holds the eight colors that were validated in the %s theme, in their order', (theme) => {
      expect(ramp(tokensOf(theme), `pal-${id}`, 8)).toEqual(validatedPalettes[id][theme]);
    });

    it.each(themes)('has eight distinct colors, each inside the lightness band and above the chroma floor, neighbors apart and clear of "Other", in the %s theme', (theme) => {
      const colors = ramp(tokensOf(theme), `pal-${id}`, 8);

      expect(paletteProblems(colors, theme, tokensOf(theme)['--tmz-series-other'] ?? other)).toEqual([]);
    });
  });

  describe.each(scaleIds)('the scale %s', (id) => {
    const steps = (theme: Theme) => ramp(tokensOf(theme), `ramp-${id}`, 7);
    const inks = (theme: Theme) => ramp(tokensOf(theme), `ramp-${id}-ink`, 7);

    it.each(themes)('holds the seven steps that were validated in the %s theme', (theme) => {
      expect(steps(theme)).toEqual(validatedScales[id][theme]);
    });

    it.each(themes)('flips the label colors between the themes as the steps reverse: %s', (theme) => {
      expect(inks(theme)).toEqual(validatedInks[theme]);
    });

    it.each(themes)('runs one hue, light to dark on the light theme and dark to light on the dark one, with visible steps and labels that read at 4.5:1, in the %s theme', (theme) => {
      expect(scaleProblems(id, steps(theme), inks(theme), theme)).toEqual([]);
    });
  });

  describe('the scales together', () => {
    it.each(themes)('are told apart at every step in the %s theme', (theme) => {
      const close: string[] = [];
      scaleIds.forEach((first, i) =>
        scaleIds.slice(i + 1).forEach((second) =>
          ramp(tokensOf(theme), `ramp-${first}`, 7).forEach((hex, step) => {
            const distance = deltaE(hex, ramp(tokensOf(theme), `ramp-${second}`, 7)[step]);
            if (distance < scaleFloor) {
              close.push(`${first} and ${second} at step ${step + 1}: ${distance.toFixed(1)}`);
            }
          }),
        ),
      );

      expect(close).toEqual([]);
    });
  });

  describe('the checks themselves', () => {
    const good = validatedPalettes.earth.light as readonly string[];
    const withColor = (colors: readonly string[], index: number, hex: string) => colors.map((color, i) => (i === index ? hex : color));
    const goodSteps = validatedScales.teal.light as readonly string[];
    const goodInks = validatedInks.light;

    it('pass the palettes and scales that were validated, so a failure below is the check working', () => {
      expect(paletteProblems(good, 'light', other)).toEqual([]);
      expect(scaleProblems('teal', goodSteps, goodInks, 'light')).toEqual([]);
    });

    it('report a palette with a missing or a repeated color', () => {
      expect(paletteProblems(good.slice(0, 7), 'light', other)).toContain('7 colors, not 8');
      expect(paletteProblems(withColor(good, 4, good[0]), 'light', other)).toContain('a color is used twice');
    });

    it('report a color outside the lightness band, below the chroma floor, close to its neighbor or close to "Other"', () => {
      expect(paletteProblems(withColor(good, 0, '#101010'), 'light', other).join()).toContain('color 1 (#101010): lightness');
      expect(paletteProblems(withColor(good, 2, '#a89c8a'), 'light', other).join()).toContain('color 3 (#a89c8a): chroma');
      expect(paletteProblems(withColor(good, 1, '#b74e20'), 'light', other).join()).toContain('color 2 (#b74e20): only');
      expect(paletteProblems(withColor(good, 6, '#8a8a82'), 'light', other).join()).toContain('from the neutral of "Other"');
    });

    it('report the band of the theme: a color that is in the light band but too light for the dark one', () => {
      expect(paletteProblems(withColor(validatedPalettes.earth.dark, 1, '#9aa7d4'), 'dark', other).join()).toContain('color 2 (#9aa7d4): lightness');
    });

    it('report steps that do not run one way, or are too close', () => {
      const swapped = goodSteps.map((hex, i) => goodSteps[i === 2 ? 3 : i === 3 ? 2 : i]); // steps 3 and 4 trade places
      expect(scaleProblems('teal', swapped, goodInks, 'light').join()).toContain('step 4: lightness moves -');
      expect(scaleProblems('teal', goodSteps.map((hex, i) => (i === 3 ? '#38bcb6' : hex)), goodInks, 'light').join()).toContain('lightness moves');
    });

    it('report more than one hue, but not for gray, which must stay a gray instead', () => {
      const mixed = goodSteps.map((hex, i) => (i === 4 ? '#7b4fa0' : hex));
      expect(scaleProblems('teal', mixed, goodInks, 'light').join()).toContain('hue spread');
      expect(scaleProblems('gray', validatedScales.gray.light, goodInks, 'light')).toEqual([]);
      expect(scaleProblems('gray', validatedScales.gray.light.map((hex, i) => (i === 2 ? '#9aa0c0' : hex)), goodInks, 'light').join()).toContain('is not a gray');
    });

    it('report a first step that is lost in the surface, and a label that is the wrong color or too faint', () => {
      expect(scaleProblems('teal', goodSteps.map((hex, i) => (i === 0 ? '#f3fbfa' : hex)), goodInks, 'light').join()).toContain('step 1 is');
      expect(scaleProblems('teal', goodSteps, goodInks.map((ink, i) => (i === 0 ? '#ffffff' : ink)), 'light').join()).toContain('step 1: label');
      expect(scaleProblems('teal', goodSteps, goodInks.map((ink, i) => (i === 0 ? '#777777' : ink)), 'light').join()).toContain('neither white nor near-black');
    });
  });
});
