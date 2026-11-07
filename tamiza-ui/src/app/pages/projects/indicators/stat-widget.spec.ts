import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartBases, noChartBases } from './base-figures';
import { WidgetExportHost } from './export-parts';
import { valueFormatter } from './format';
import { config, row } from './indicators-testing';
import { StatWidget } from './stat-widget';

// The stat tile itself (improve-widget-text-hierarchy, design D6, task 5.1): its figure keeps full contrast whatever its base, it draws the
// line without the dashboard filters under its label and nothing else, and it registers that one line for an image. Its n, small or not, is
// the chip of the widget's foot, which the view builds; the view's specs cover it.

const ratio = { numerator: 'numerator', denominator: 'denominator' };
const format = valueFormatter({ format: 'percent', unit: 'de los hogares' }, 'en-US');

@Component({
  imports: [StatWidget],
  providers: [WidgetExportHost],
  template: `<app-stat-widget [rows]="rows()" [config]="config()" [label]="'Teatro hoy'" [format]="format" [bases]="bases()" [n]="n()" [unfiltered]="unfiltered()" />`,
})
class Host {
  readonly format = format;
  readonly rows = signal<QueryRow[]>([row(null, 0.4685)]);
  readonly config = signal<WidgetConfig>(config({ indicator: 'teatro_hoy', ratio, groupBy: null }));
  readonly bases = signal<ChartBases>(noChartBases);
  readonly n = signal<number | null>(null);
  readonly unfiltered = signal<number | null>(null);
}

describe('StatWidget (improve-widget-text-hierarchy, design D6)', () => {
  async function render(setup: (host: Host) => void = () => undefined) {
    TestBed.configureTestingModule({ imports: [Host, translocoTesting('en')] });
    const fixture = TestBed.createComponent(Host);
    setup(fixture.componentInstance);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const part = (id: string) => root.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const parts = () => (fixture.debugElement.injector.get(WidgetExportHost).current()!.parts() as { stat: { value: string; description: string | null; size: number; secondary: string[]; dimmed?: boolean } }).stat;
    return { fixture, root, part, parts };
  }

  it('draws the figure at full contrast, in the heading color, whatever its base, and marks a small one with data-small (Stat tile not dimmed)', async () => {
    const small = await render((host) => {
      host.n.set(26);
      host.bases.set({ ...noChartBases, minN: 30 });
    });

    const figure = small.part('stat-value')!;
    expect(figure.style.opacity).toBe('');
    expect(figure.getAttribute('style')).not.toContain('opacity');
    expect(figure.classList).toContain('text-heading');
    expect(figure.classList).not.toContain('opacity-35');
    expect(figure.getAttribute('data-small')).toBe('true');
  });

  it('does not mark a figure whose base is enough, nor one with no threshold in force', async () => {
    const enough = await render((host) => {
      host.n.set(476);
      host.bases.set({ ...noChartBases, minN: 30 });
    });
    expect(enough.part('stat-value')!.hasAttribute('data-small')).toBe(false);
    TestBed.resetTestingModule();

    const off = await render((host) => host.n.set(5));
    expect(off.part('stat-value')!.hasAttribute('data-small')).toBe(false);
  });

  it('draws the value without the dashboard filters under its label (Value without the dashboard filters)', async () => {
    const { part, root } = await render((host) => {
      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: null, display: { secondary: ['unfiltered'] } }));
      host.rows.set([row(null, 0.31)]);
      host.unfiltered.set(0.4685);
    });

    expect(part('stat-value')!.textContent!.trim()).toBe('31% de los hogares');
    expect(Array.from(root.querySelectorAll('[data-testid="stat-line"]')).map((line) => line.textContent!.trim())).toEqual(['Without filters: 46.85% de los hogares']);
    expect(part('stat-label')!.classList).toContain('line-clamp-2');
  });

  it('draws the value without the dashboard filters of a median under its label (Median without the dashboard filters)', async () => {
    const { root } = await render((host) => {
      host.config.set(config({ indicator: 'teatro_precio_hogar', measure: 'value', groupBy: null, statistic: 'median', display: { secondary: ['unfiltered'] } }));
      host.rows.set([row(null, 35000, null, { n: 6 })]);
      host.unfiltered.set(40000);
    });

    expect(Array.from(root.querySelectorAll('[data-testid="stat-line"]')).map((line) => line.textContent!.trim())).toEqual([expect.stringMatching(/^Without filters: /)]);
  });

  it('draws no small-base note and no n line inside the tile, whatever the tile chose and its base is', async () => {
    const { root, part } = await render((host) => {
      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: null, display: { secondary: ['n'] } }));
      host.n.set(26);
      host.bases.set({ ...noChartBases, minN: 30, showN: true });
    });

    expect(root.querySelectorAll('[data-testid="stat-line"]')).toHaveLength(0);
    expect(part('widget-small-note')).toBeNull();
    expect(root.textContent).not.toContain('n = ');
    expect(root.textContent).not.toContain('dimmed');
    expect(part('stat-label')!.classList).not.toContain('line-clamp-2');
  });

  it('registers for an image the value, what it plots and the one line without filters, and no dimming (Small base in the image)', async () => {
    const { parts } = await render((host) => {
      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: null, display: { secondary: ['n', 'unfiltered'] } }));
      host.rows.set([row(null, 0.31)]);
      host.n.set(26);
      host.unfiltered.set(0.4685);
      host.bases.set({ ...noChartBases, minN: 30 });
    });

    const stat = parts();
    expect(stat.value).toBe('31% de los hogares');
    expect(stat.description).toBe('Teatro hoy: numerator / denominator');
    expect(stat.secondary).toEqual(['Without filters: 46.85% de los hogares']);
    expect(stat).not.toHaveProperty('dimmed');
  });

  it('keeps the figure and the label at their size and lets only the line without filters shrink, so a short tile cuts that line first (Tile with texts and the n line)', async () => {
    const { part, root } = await render((host) => {
      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: null, display: { secondary: ['unfiltered'] } }));
      host.unfiltered.set(0.4685);
    });

    expect(part('stat-value')!.classList).toContain('shrink-0');
    expect(part('stat-label')!.classList).toContain('shrink-0');
    const line = root.querySelector('[data-testid="stat-line"]')!;
    expect(line.classList).not.toContain('shrink-0');
    expect(line.classList).toContain('min-h-0');
    expect(line.classList).toContain('truncate');
  });

  it('keeps a hidden caption for screen readers only', async () => {
    const { part } = await render((host) => host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: null, display: { caption: { hidden: true } } })));

    expect(part('stat-label')!.classList).toContain('sr-only');
  });
});
