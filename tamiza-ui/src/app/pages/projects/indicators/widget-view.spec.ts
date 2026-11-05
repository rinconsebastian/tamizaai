import { HttpTestingController } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { configureApp } from '../../../../testing/project-harness';
import { tick } from '../../../../testing/cdk';
import { ChartType, DashboardColors, WidgetConfig } from '../../../core/projects/indicators.service';
import { defaultColors } from './chart-colors';
import { config, indicator, isQuery, result, row } from './indicators-testing';
import { WidgetView } from './widget-view';

@Component({
  imports: [WidgetView],
  template: `<app-widget-view projectId="p1" [chartType]="type()" [config]="config()" [colors]="colors()" [indicator]="coverage()" [tileRows]="tileRows()" />`,
})
class Host {
  readonly tileRows = signal<number | null>(null);
  readonly type = signal<ChartType>('column');
  readonly config = signal<WidgetConfig | null>(config());
  readonly colors = signal<DashboardColors>(defaultColors);
  readonly coverage = signal(indicator('coverage'));
}

describe('WidgetView colors (add-dashboard-palettes, design D3, D7)', () => {
  async function render() {
    const http: HttpTestingController = configureApp();
    const fixture = TestBed.createComponent(Host);
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const view = fixture.debugElement.children[0].componentInstance as WidgetView;
    const queries = () => http.match(isQuery).filter((r) => !r.cancelled);
    // The first query is the draw; answer it so a later one would be the only request pending.
    const [first] = queries();
    first.flush(result([row('Cali', 75)]));
    await settle();
    return { fixture, host: fixture.componentInstance, http, settle, view, first, queries };
  }

  it('asks once for the rows, and never again when the widget\'s color changes', async () => {
    const { host, settle, view, first, queries } = await render();
    expect(first.request.body.config).not.toHaveProperty('display');
    expect(view.resolvedColors().accent).toBe('var(--tmz-pal-tamiza-1)');

    host.config.set(config({ display: { color: 3 } }));
    await settle();

    expect(queries()).toHaveLength(0);
    expect(view.resolvedColors().accent).toBe('var(--tmz-pal-tamiza-3)');
  });

  it('asks again when what the query holds changes, as it did before', async () => {
    const { host, settle, queries } = await render();

    host.config.set(config({ groupBy: 'round', display: { color: 3 } }));
    await settle();

    const requests = queries();
    expect(requests).toHaveLength(1);
    expect(requests[0].request.body.config).toMatchObject({ groupBy: 'round' });
    expect(requests[0].request.body.config).not.toHaveProperty('display');
  });

  it('asks for no rows when the scale of a map or a heat map changes', async () => {
    const { host, settle, view, queries } = await render();
    host.type.set('heatmap');
    host.config.set(config({ seriesBy: 'water_source' }));
    await settle();
    queries().forEach((request) => request.flush(result([row('Cali', 75)])));
    await settle();

    host.config.set(config({ seriesBy: 'water_source', display: { ramp: 'green' } }));
    await settle();

    expect(queries()).toHaveLength(0);
    expect(view.resolvedColors().ramp).toBe('green');
    expect(view.resolvedColors().rampStyle['--tmz-seq-4']).toBe('var(--tmz-ramp-green-4)');
  });

  it('draws in the colors of the dashboard it is given, and in Tamiza when it is given none', async () => {
    const { host, settle, view, queries } = await render();
    expect(view.resolvedColors().ramp).toBe('blue');

    host.colors.set({ palette: 'earth', valueColors: [{ column: 'municipality', value: 'Cali', color: 2 }] });
    await settle();

    expect(queries()).toHaveLength(0); // a palette is not part of the query
    expect(view.resolvedColors().accent).toBe('var(--tmz-pal-earth-1)');
    expect(view.resolvedColors().ramp).toBe('orange');
    expect(view.resolvedColors().kept('municipality', 'Cali')).toBe('var(--tmz-pal-earth-2)');
  });

  it('resolves colors for a draft that is not complete yet', async () => {
    const { host, settle, view } = await render();

    host.config.set(null);
    await settle();

    expect(view.resolvedColors().accent).toBe('var(--tmz-pal-tamiza-1)');
  });

  it('asks for the boxes of a box plot with its chart type, and never again when its orientation or color changes', async () => {
    const { host, http, settle, view, queries } = await render();
    host.type.set('boxplot');
    host.config.set(config({ groupBy: 'municipality' }));
    await settle();
    const [draw] = queries();
    expect(draw.request.body.chartType).toBe('boxplot');
    draw.flush(result([row('Cali', 4.5)], { boxes: [{ group: 'Cali', values: 5, min: 3, q1: 4, median: 4.5, q3: 5, max: 6, mean: 4.5, lowerWhisker: 3, upperWhisker: 6, outlierCount: 0, outliers: [] }] }));
    await settle();

    host.config.set(config({ groupBy: 'municipality', display: { orientation: 'horizontal' } }));
    await settle();
    host.config.set(config({ groupBy: 'municipality', display: { orientation: 'vertical', color: 4 } }));
    await settle();

    expect(queries()).toHaveLength(0);
    expect(view.resolvedColors().accent).toBe('var(--tmz-pal-tamiza-4)');
    http.verify();
  });

  it('asks for no rows when a column label is typed, and names the column at once (Preview without a query)', async () => {
    const { host, settle, view, queries, fixture } = await render();
    const caption = () => fixture.nativeElement.querySelector('[data-testid="chart-caption"]').textContent.trim();
    expect(caption()).toBe('Coverage by municipality');
    const ratio = { numerator: 'numerator', denominator: 'denominator' };
    host.config.set(config({ ratio }));
    await settle();
    queries().forEach((request) => request.flush(result([row('Cali', 0.5)])));
    await settle();
    expect(caption()).toBe('Coverage: numerator / denominator by municipality');

    host.config.set(config({ ratio, display: { labels: { denominator: 'Hogares que asistieron', municipality: 'Comuna' } } }));
    await settle();

    expect(queries()).toHaveLength(0);
    expect(caption()).toBe('Coverage: numerator / Hogares que asistieron by Comuna');
    expect((view as unknown as { names(): (column: string) => string }).names()('denominator')).toBe('Hogares que asistieron');
  });

  it('names a column with the label of its indicator, and with the own label of the widget over it (Widget label over the indicator label)', async () => {
    const { host, settle, queries, fixture } = await render();
    const caption = () => fixture.nativeElement.querySelector('[data-testid="chart-caption"]').textContent.trim();
    host.coverage.set(indicator('coverage', { columns: { municipality: { label: 'Municipio' } } }));
    await settle();
    expect(caption()).toBe('Coverage by Municipio');

    host.config.set(config({ display: { labels: { municipality: 'Comuna' } } }));
    await settle();
    expect(caption()).toBe('Coverage by Comuna');

    host.config.set(config({ display: { labels: { municipality: '' } } }));
    await settle();
    expect(caption()).toBe('Coverage by Municipio');
    expect(queries()).toHaveLength(0);
  });

  it('asks for no rows when any text of the widget changes (Widget context left unset: no query)', async () => {
    const { host, settle, queries } = await render();

    for (const display of [
      { subtitle: '¿Quién fue al evento?' },
      { footnote: 'No suman 100 %: respuesta múltiple.' },
      { caption: { text: 'Cobertura propia' } },
      { caption: { hidden: true as const } },
      { unit: 'de quienes asistieron' },
      { showDescription: true as const },
      { subtitle: 'A', footnote: 'B', caption: { text: 'C' }, unit: 'D', showDescription: true as const },
    ]) {
      host.config.set(config({ display }));
      await settle();
      expect(queries()).toHaveLength(0);
    }
  });

  it('asks for no rows when the threshold or the n line changes, and asks again when leaving out the no-data values does (add-small-sample-warnings, design D12)', async () => {
    const { host, settle, queries } = await render();

    for (const display of [{ minN: 30 }, { minN: 0 }, { showN: true }, { showN: false }, { minN: 30, showN: true }]) {
      host.config.set(config({ display }));
      await settle();
      expect(queries()).toHaveLength(0);
    }

    host.config.set(config({ display: { minN: 30 }, missingFooter: true }));
    await settle();

    const asked = queries();
    expect(asked).toHaveLength(1);
    expect(asked[0].request.body.config).toMatchObject({ missingFooter: true });
    expect(asked[0].request.body.config).not.toHaveProperty('display');
    asked[0].flush(result([row('Cali', 75)]));
    await settle();

    host.config.set(config({ display: { minN: 10 }, missingFooter: true }));
    await settle();
    expect(queries()).toHaveLength(0);
  });

  describe('reference lines, steps and data labels (add-chart-annotations, design D4)', () => {
    const ratio = { numerator: 'numerator', denominator: 'denominator' };

    it('asks for no rows when a line is switched from mean to median or its label is edited (Label edited)', async () => {
      const { host, settle, queries } = await render();
      host.config.set(config({ ratio, display: { referenceLines: [{ kind: 'mean' }] } }));
      await settle();
      queries().forEach((request) => request.flush(result([row('Cali', 0.5)])));
      await settle();

      for (const referenceLines of [[{ kind: 'median' as const }], [{ kind: 'median' as const, label: 'Mediana de comunas' }], [{ kind: 'value' as const, value: 0.5, label: 'Meta' }]]) {
        host.config.set(config({ ratio, display: { referenceLines } }));
        await settle();
        expect(queries()).toHaveLength(0);
      }
      host.config.set(config({ ratio, display: { referenceLines: [{ kind: 'median' }], interpolation: 'step', dataLabels: true } }));
      await settle();
      expect(queries()).toHaveLength(0);
    });

    it('asks once, with overall: true, when an overall line is added, and once more when it is removed (Overall line added)', async () => {
      const { host, settle, queries } = await render();
      host.config.set(config({ ratio }));
      await settle();
      const [plain] = queries();
      expect(plain.request.body).not.toHaveProperty('overall');
      plain.flush(result([row('Cali', 0.5)]));
      await settle();

      host.config.set(config({ ratio, display: { referenceLines: [{ kind: 'mean' }, { kind: 'overall' }] } }));
      await settle();

      const added = queries();
      expect(added).toHaveLength(1);
      expect(added[0].request.body.overall).toBe(true);
      expect(added[0].request.body.config).not.toHaveProperty('overall');
      expect(added[0].request.body.config).not.toHaveProperty('display');
      added[0].flush(result([row('Cali', 0.5)], { overall: 0.4599 }));
      await settle();

      host.config.set(config({ ratio, display: { referenceLines: [{ kind: 'overall', label: 'General' }] } }));
      await settle();
      expect(queries()).toHaveLength(0);

      host.config.set(config({ ratio, display: { referenceLines: [{ kind: 'mean' }] } }));
      await settle();
      const removed = queries();
      expect(removed).toHaveLength(1);
      expect(removed[0].request.body).not.toHaveProperty('overall');
    });

    it('posts a body with no overall key for a widget with no overall line, and for one that cannot have it', async () => {
      const { host, settle, queries } = await render();

      for (const next of [
        config({ display: { referenceLines: [{ kind: 'overall' }] } }), // a measure
        config({ ratio, seriesBy: 'water_source', display: { referenceLines: [{ kind: 'overall' }] } }), // a series: read from the totals
        config({ ratio: { numerator: 'a', denominator: 'b' }, display: { dataLabels: true } }),
      ]) {
        host.config.set(next);
        await settle();
        // The measure's query is the one the widget already asked for, so it may send nothing at all; no request ever carries the flag.
        const sent = queries();
        expect(sent.length).toBeLessThanOrEqual(1);
        sent.forEach((request) => {
          expect(request.request.body).not.toHaveProperty('overall');
          request.flush(result([row('Cali', 0.5)]));
        });
        await settle();
      }
    });
  });

  it('asks again when what a box plot is ordered by changes', async () => {
    const { host, settle, queries } = await render();
    host.type.set('boxplot');
    host.config.set(config({ groupBy: 'municipality' }));
    await settle();
    queries().forEach((request) => request.flush(result([row('Cali', 4.5)])));
    await settle();

    host.config.set(config({ groupBy: 'municipality', sort: { by: 'value', column: null, compareAs: null, direction: 'desc' } }));
    await settle();

    const requests = queries();
    expect(requests).toHaveLength(1);
    expect(requests[0].request.body.config.sort).toEqual({ by: 'value', column: null, compareAs: null, direction: 'desc' });
  });

  describe('the footnote (add-widget-context, design D5)', () => {
    const note = 'No suman 100 %: respuesta múltiple.';
    const footnoteOf = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement.querySelector<HTMLElement>('[data-testid="widget-footnote"]');
    /** The box the chart is drawn in: the parent of the axis chart. */
    const boxOf = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement.querySelector('app-axis-chart')!.parentElement as HTMLElement;

    it('shows it under the chart, which is cut at its own box (Footnote under the chart)', async () => {
      const { host, settle, fixture } = await render();

      host.config.set(config({ display: { footnote: note } }));
      await settle();

      const line = footnoteOf(fixture)!;
      expect(line.textContent!.trim()).toBe(note);
      expect(line.getAttribute('title')).toBe(note);
      expect(boxOf(fixture).classList.contains('overflow-hidden')).toBe(true);
      expect(boxOf(fixture).compareDocumentPosition(line) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('shows it under the table (Footnote under the table)', async () => {
      const { host, settle, fixture, view } = await render();
      host.config.set(config({ display: { footnote: note } }));
      await settle();

      view.asTable.set(true);
      await settle();

      expect(fixture.nativeElement.querySelector('[data-testid="widget-table"]')).not.toBeNull();
      expect(footnoteOf(fixture)!.textContent!.trim()).toBe(note);
      expect(fixture.nativeElement.querySelector('[data-testid="widget-table"]').compareDocumentPosition(footnoteOf(fixture)) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('shows it under the message that no row matches', async () => {
      const { host, settle, fixture, queries } = await render();
      host.config.set(config({ groupBy: 'round', display: { footnote: note } }));
      await settle();
      queries().forEach((request) => request.flush(result([])));
      await settle();

      expect(fixture.nativeElement.querySelector('[data-testid="widget-empty"]')).not.toBeNull();
      expect(footnoteOf(fixture)!.textContent!.trim()).toBe(note);
    });

    it('shows one line on a tile two rows high or less, and two on a taller one or none', async () => {
      const { host, settle, fixture } = await render();
      host.config.set(config({ display: { footnote: note } }));
      await settle();
      const classes = () => footnoteOf(fixture)!.classList;

      expect(classes().contains('line-clamp-2')).toBe(true);
      expect(classes().contains('line-clamp-1')).toBe(false);
      host.tileRows.set(4);
      await settle();
      expect(classes().contains('line-clamp-2')).toBe(true);
      host.tileRows.set(2);
      await settle();
      expect(classes().contains('line-clamp-1')).toBe(true);
      expect(classes().contains('line-clamp-2')).toBe(false);
      expect(classes().contains('paper:line-clamp-none')).toBe(true);
    });

    it('is not shown while the rows load, nor when the query fails', async () => {
      const { host, settle, fixture, queries } = await render();
      host.config.set(config({ groupBy: 'round', display: { footnote: note } }));
      await settle();

      expect(fixture.nativeElement.querySelector('[data-testid="view-loading"]')).not.toBeNull();
      expect(footnoteOf(fixture)).toBeNull();

      queries().forEach((request) => request.flush({ title: 'x' }, { status: 500, statusText: 'Server Error' }));
      await settle();

      expect(fixture.nativeElement.querySelector('[data-testid="view-error"]')).not.toBeNull();
      expect(footnoteOf(fixture)).toBeNull();
    });

    it('is not shown for a column the table no longer has, nor for an indicator that has not published', async () => {
      const { host, settle, fixture, queries } = await render();
      host.config.set(config({ groupBy: 'round', display: { footnote: note } }));
      await settle();
      queries().forEach((request) => request.flush(result([], { missing: { indicator: false, columns: ['round'] } })));
      await settle();

      expect(fixture.nativeElement.querySelector('[data-testid="widget-missing"]')).not.toBeNull();
      expect(footnoteOf(fixture)).toBeNull();

      host.config.set(config({ groupBy: 'estrato', display: { footnote: note } }));
      await settle();
      queries().forEach((request) => request.flush(result([], { published: false })));
      await settle();

      expect(fixture.nativeElement.querySelector('[data-testid="view-not-published"]')).not.toBeNull();
      expect(footnoteOf(fixture)).toBeNull();
    });

    it('is none for a blank footnote, and a widget without one keeps its chart box as it was (Widgets saved before)', async () => {
      const { host, settle, fixture } = await render();

      expect(footnoteOf(fixture)).toBeNull();
      expect(boxOf(fixture).className).toBe('min-h-0 flex-1');

      host.config.set(config({ display: { footnote: '   ', color: 2 } }));
      await settle();

      expect(footnoteOf(fixture)).toBeNull();
      expect(boxOf(fixture).className).toBe('min-h-0 flex-1');
    });
  });
});
