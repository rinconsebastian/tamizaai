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
  template: `<app-widget-view projectId="p1" [chartType]="type()" [config]="config()" [colors]="colors()" [indicator]="coverage" />`,
})
class Host {
  readonly type = signal<ChartType>('column');
  readonly config = signal<WidgetConfig | null>(config());
  readonly colors = signal<DashboardColors>(defaultColors);
  readonly coverage = indicator('coverage');
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
});
