import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../../../../public/i18n/en.json';
import type { IndicatorMetadata, IndicatorTestView } from '../../../core/projects/pipeline.service';
import { valueFormatter } from '../indicators/format';
import { IndicatorView, ViewWarning, askedWarning, viewWarnings } from './indicator-view';

/** The facts of a rate by municipality over 12 municipalities: value, numerator and denominator, no problems. */
function rate(overrides: Partial<IndicatorTestView> = {}): IndicatorTestView {
  const groups = Array.from({ length: 12 }, (_, n) => ({ group: `Town ${n}`, value: 0.5, numerator: 5, denominator: 10 }));
  return {
    measures: ['value', 'numerator', 'denominator'],
    defaultMeasure: 'value',
    ratio: true,
    dimensions: ['municipality'],
    defaultDimension: 'municipality',
    geometry: [],
    rows: 12,
    defaultMeasureNulls: 0,
    defaultMeasureMax: 0.5,
    totals: { value: 6, numerator: 60, denominator: 120 },
    duplicateRows: 0,
    charts: { municipality: { values: 12, groups } },
    ...overrides,
  };
}

const percent: IndicatorMetadata = { label: 'Households with piped water', description: '', unit: '', format: 'percent' };
const warningKeys = (facts: IndicatorTestView, format: IndicatorMetadata['format'] = 'percent') =>
  viewWarnings(facts, valueFormatter({ format, unit: null }, 'en'), format).map((w) => w.key);

describe('viewWarnings', () => {
  it('finds nothing in a rate that follows the conventions', () => {
    expect(warningKeys(rate())).toEqual([]);
  });

  it('warns when there is no value column, naming the measure widgets will plot', () => {
    const facts = rate({ measures: ['households'], defaultMeasure: 'households', ratio: false });

    const [warning] = viewWarnings(facts, valueFormatter({ format: 'count', unit: null }, 'en'), 'count');

    expect(warning).toEqual({ key: 'noValue', params: { measure: 'households' } });
  });

  it('warns about a percent above one with the figure it would show', () => {
    const [warning] = viewWarnings(rate({ defaultMeasureMax: 45.2 }), valueFormatter({ format: 'percent', unit: null }, 'en'), 'percent');

    expect(warning).toEqual({ key: 'percentAboveOne', params: { max: 45.2, shown: '4,520%' } });
    expect(warningKeys(rate({ defaultMeasureMax: 45.2 }), 'decimal')).toEqual([]);
  });

  it('warns about a number named like a dimension, such as year', () => {
    const facts = rate({ measures: ['year', 'value'], ratio: false });

    expect(viewWarnings(facts, valueFormatter({ format: 'count', unit: null }, 'en'), 'count')).toEqual([
      { key: 'dimensionAsMeasure', params: { column: 'year' } },
    ]);
    for (const column of ['month', 'code', 'municipality_code', 'household_id', 'id']) {
      expect(warningKeys(rate({ measures: [column, 'value'], ratio: false }))).toEqual(['dimensionAsMeasure']);
    }
    expect(warningKeys(rate({ measures: ['value', 'identity', 'barcode'], ratio: false }))).toEqual([]);
  });

  it('warns about rows that widgets add up, an empty table, empty values and a lone numerator', () => {
    expect(warningKeys(rate({ duplicateRows: 2 }))).toEqual(['duplicateRows']);
    expect(warningKeys(rate({ rows: 0 }))).toEqual(['noRows']);
    expect(warningKeys(rate({ defaultMeasureNulls: 3 }))).toEqual(['emptyValues']);
    expect(warningKeys(rate({ measures: ['value', 'numerator'], ratio: false }))).toEqual(['ratioIncomplete']);
  });
});

@Component({
  imports: [IndicatorView],
  template: `<app-indicator-view [facts]="facts()" [metadata]="metadata()" [canAsk]="canAsk()" (ask)="asked.push($event)" />`,
})
class Host {
  readonly facts = signal<IndicatorTestView>(rate());
  readonly metadata = signal<IndicatorMetadata>(percent);
  readonly canAsk = signal(false);
  readonly asked: ViewWarning[] = [];
}

describe('askedWarning', () => {
  it('keeps only the params the API keeps for each kind of warning', () => {
    expect(askedWarning({ key: 'percentAboveOne', params: { max: 87.5, shown: '8,750%' } })).toEqual({ key: 'percentAboveOne', params: {} });
    expect(askedWarning({ key: 'emptyValues', params: { count: 4, measure: 'value', extra: 'x' } })).toEqual({ key: 'emptyValues', params: { count: 4, measure: 'value' } });
    expect(askedWarning({ key: 'dimensionAsMeasure', params: { column: 'year' } })).toEqual({ key: 'dimensionAsMeasure', params: { column: 'year' } });
    expect(askedWarning({ key: 'duplicateRows', params: { count: 2 } })).toEqual({ key: 'duplicateRows', params: { count: 2 } });
    expect(askedWarning({ key: 'noRows', params: {} })).toEqual({ key: 'noRows', params: {} });
  });
});

describe('IndicatorView', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [Host, TranslocoTestingModule.forRoot({ langs: { en }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' }, preloadLangs: true })],
    }),
  );

  async function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    const q = (testId: string) => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
    return { fixture, q, host: fixture.componentInstance };
  }

  it('lists the measures with the default, the ratio and the dimensions, and charts the municipalities', async () => {
    const { q } = await render();

    expect(q('view-measures')!.textContent).toContain('value');
    expect(q('view-measures')!.textContent).toContain('numerator');
    expect(q('view-measures')!.textContent).toContain('value is plotted by default');
    expect(q('view-ratio')!.textContent).toContain('can be plotted as a ratio');
    expect(q('view-dimensions')!.textContent).toContain('municipality');
    expect(q('view-chart')!.querySelectorAll('rect').length).toBeGreaterThanOrEqual(12);
  });

  it('shows the sum of the default measure and the ratio of the sums as percentages', async () => {
    const { q } = await render();

    expect(q('view-measure-tile')!.querySelector('[data-testid="stat-value"]')!.textContent).toBe('600%');
    expect(q('view-ratio-tile')!.querySelector('[data-testid="stat-value"]')!.textContent).toBe('50%');
  });

  it('shows the same figures in another format without a new test', async () => {
    const { fixture, q, host } = await render();

    host.metadata.set({ ...percent, format: 'decimal', unit: 'households' });
    fixture.detectChanges();

    expect(q('view-measure-tile')!.querySelector('[data-testid="stat-value"]')!.textContent).toBe('6.00 households');
    expect(q('view-ratio-tile')!.querySelector('[data-testid="stat-value"]')!.textContent).toBe('0.50 households');
  });

  it('plots the ratio of each group when asked, leaving out groups whose denominator is zero', async () => {
    const { fixture, q, host } = await render();
    host.facts.set(
      rate({
        charts: {
          municipality: {
            values: 3,
            groups: [
              { group: 'Cali', value: 0.9, numerator: 9, denominator: 10 },
              { group: 'Buga', value: 0.1, numerator: 1, denominator: 10 },
              { group: 'Empty', value: 0, numerator: 0, denominator: 0 },
            ],
          },
        },
      }),
    );
    fixture.detectChanges();
    const plot = q('view-plot') as unknown as HTMLSelectElement;
    plot.value = 'ratio';
    plot.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Each bar says what it shows in its tooltip: the group and the ratio of its sums.
    const bars = [...q('view-chart')!.querySelectorAll('rect title')].map((title) => title.textContent);
    expect(bars).toEqual(['Cali: 90%', 'Buga: 10%']);
  });

  it('lists a dimension with many values but does not offer it for the chart, and says why', async () => {
    const { fixture, q, host } = await render();

    host.facts.set(
      rate({
        dimensions: ['code', 'municipality'],
        defaultDimension: 'code',
        charts: { code: { values: 300, groups: null }, municipality: rate().charts['municipality'] },
      }),
    );
    fixture.detectChanges();

    expect(q('view-dimensions')!.textContent).toContain('code');
    expect([...q('view-dimension')!.querySelectorAll('option')].map((o) => o.textContent)).toEqual(['municipality']);
    expect(q('view-too-many')!.textContent).toContain('code has more than 50 values');
  });

  it('lists the geometry columns', async () => {
    const { fixture, q, host } = await render();

    host.facts.set(rate({ geometry: ['geometry'], dimensions: ['municipality', 'geometry'] }));
    fixture.detectChanges();

    expect(q('view-geometry')!.textContent).toContain('geometry');
  });

  it('shows the warnings of the table', async () => {
    const { fixture, host } = await render();
    host.facts.set(rate({ measures: ['year', 'value'], ratio: false, defaultMeasureMax: 45.2 }));
    fixture.detectChanges();

    const texts = [...fixture.nativeElement.querySelectorAll('[data-testid="view-warning"]')].map((w) => (w as HTMLElement).textContent);

    expect(texts.some((text) => text!.includes('which shows as 4,520%'))).toBe(true);
    expect(texts.some((text) => text!.includes('year is a number') && text!.includes('Return it as text'))).toBe(true);
  });

  it('offers "Ask the assistant" on each warning only when it may, and reports the warning chosen', async () => {
    const { fixture, q, host } = await render();
    host.facts.set(rate({ measures: ['year', 'value'], ratio: false, defaultMeasureMax: 45.2 }));
    fixture.detectChanges();
    const buttons = () => [...fixture.nativeElement.querySelectorAll('[data-testid="view-warning-ask"]')] as HTMLButtonElement[];
    expect(buttons()).toHaveLength(0);
    expect(q('view-warning')).not.toBeNull();

    host.canAsk.set(true);
    fixture.detectChanges();
    expect(buttons().map((b) => b.textContent!.trim())).toEqual(['Ask the assistant', 'Ask the assistant']);
    buttons()[1].click();

    expect(host.asked).toEqual([{ key: 'dimensionAsMeasure', params: { column: 'year' } }]);
  });
});
