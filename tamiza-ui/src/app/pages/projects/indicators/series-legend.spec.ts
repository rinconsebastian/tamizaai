import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { LegendEntry, SeriesLegend, SeriesNotes, seriesFill, seriesName } from './series-legend';

const entries: LegendEntry[] = [
  { name: 'Piped', color: 'var(--tmz-series-1)' },
  { name: 'Well', color: 'var(--tmz-series-2)' },
  { name: 'Mixed', color: 'var(--tmz-div-neutral)', outlined: true },
];

async function render<T>(component: new () => T, inputs: Record<string, unknown>, lang: 'en' | 'es' = 'en') {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ imports: [translocoTesting(lang)] });
  const fixture = TestBed.createComponent(component);
  for (const [name, value] of Object.entries(inputs)) {
    fixture.componentRef.setInput(name, value);
  }
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('series legend', () => {
  it('shows a swatch in each series\' color beside its name, in the text colors', async () => {
    const root = await render(SeriesLegend, { entries });

    const items = Array.from(root.querySelectorAll('[data-testid="legend-item"]'));
    expect(items.map((item) => item.querySelector('[data-testid="legend-name"]')!.textContent!.trim())).toEqual(['Piped', 'Well', 'Mixed']);
    const swatches = items.map((item) => item.querySelector<HTMLElement>('[data-testid="legend-swatch"]')!);
    expect(swatches.map((s) => s.getAttribute('style'))).toEqual([
      expect.stringContaining('var(--tmz-series-1)'),
      expect.stringContaining('var(--tmz-series-2)'),
      expect.stringContaining('var(--tmz-div-neutral)'),
    ]);
    // The swatch carries the color; the name is text, never in the series' color.
    expect(swatches.every((s) => s.getAttribute('aria-hidden') === 'true')).toBe(true);
    const name = items[0].querySelector<HTMLElement>('[data-testid="legend-name"]')!;
    expect(name.getAttribute('style')).toBeNull();
    expect(root.querySelector('ul')!.className).toContain('text-body');
    expect(root.querySelector('ul')!.getAttribute('aria-label')).toBe('Series');
  });

  it('wraps rather than scrolls, so it prints whole', async () => {
    const root = await render(SeriesLegend, { entries });

    expect(root.querySelector('ul')!.className).toContain('flex-wrap');
    expect(root.querySelector('ul')!.className).not.toContain('overflow');
  });

  it('draws the keys of a line chart as strokes, the swatches of the others as squares, and outlines a neutral swatch', async () => {
    const lines = await render(SeriesLegend, { entries, shape: 'line' });
    expect(lines.querySelector('[data-testid="legend-swatch"]')!.className).toContain('h-0.5');

    const squares = await render(SeriesLegend, { entries });
    const [piped, , mixed] = Array.from(squares.querySelectorAll('[data-testid="legend-swatch"]'));
    expect(piped.className).toContain('size-2.5');
    expect(piped.className).not.toContain('border');
    expect(mixed.className).toContain('border');
  });
});

describe('series names and colors', () => {
  const t = (key: string) => ({ 'indicators.series.none': '(none)', 'indicators.series.other': 'Other' })[key] ?? key;

  it('names a series by its value, and "(none)" and "Other" by their kind', () => {
    expect(seriesName({ series: 'Piped', kind: 'value' }, t)).toBe('Piped');
    expect(seriesName({ series: null, kind: 'none' }, t)).toBe('(none)');
    expect(seriesName({ series: null, kind: 'other' }, t)).toBe('Other');
  });

  it('colors a series by its place among the series, and "Other" in its own neutral', () => {
    expect(seriesFill('value', 0)).toBe('var(--tmz-series-1)');
    expect(seriesFill('none', 2)).toBe('var(--tmz-series-3)');
    expect(seriesFill('other', 3)).toBe('var(--tmz-series-other)');
    expect(seriesFill('value', 7)).toBe('var(--tmz-series-8)');
  });
});

describe('series notes', () => {
  const note = (root: HTMLElement, id: string) => root.querySelector(`[data-testid="${id}"]`)?.textContent?.trim();

  it('says how many negative values the shares left out, and how many rows a pyramid or a Likert widget left out', async () => {
    const pyramid = await render(SeriesNotes, { negativeLeftOut: 2, leftOut: 15, kind: 'pyramid' });
    expect(note(pyramid, 'series-negative-left-out')).toBe('2 negative values are left out of the shares.');
    expect(note(pyramid, 'series-left-out')).toBe('15 rows left out: they have no group or series value, or a series value other than the two sides.');

    const likert = await render(SeriesNotes, { leftOut: 7, kind: 'likert' });
    expect(note(likert, 'series-left-out')).toBe('7 rows left out: they have no group or answer, or an answer outside the scale.');
  });

  it('says one in the singular, and says nothing for none', async () => {
    const one = await render(SeriesNotes, { negativeLeftOut: 1, leftOut: 1, kind: 'likert' });
    expect(note(one, 'series-negative-left-out')).toBe('1 negative value is left out of the shares.');
    expect(note(one, 'series-left-out')).toContain('1 row left out');

    const none = await render(SeriesNotes, { negativeLeftOut: 0, leftOut: 0, kind: 'pyramid' });
    expect(none.querySelector('p')).toBeNull();
  });

  it('says it in Spanish', async () => {
    const root = await render(SeriesNotes, { leftOut: 15, kind: 'pyramid' }, 'es');

    expect(note(root, 'series-left-out')).toContain('15 filas omitidas');
  });
});
