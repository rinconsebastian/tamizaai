import { HttpTestingController } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { configureApp } from '../../../../testing/project-harness';
import { tick } from '../../../../testing/cdk';
import { ChartType, IndicatorEntry, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { config, indicator, isQuery, result, row, seriesResult, cell } from './indicators-testing';
import { WidgetView } from './widget-view';

@Component({
  imports: [WidgetView],
  template: `<app-widget-view projectId="p1" [chartType]="type()" [config]="config()" [indicator]="entry()" [asTable]="asTable()" [showToggle]="false" />`,
})
class Host {
  readonly type = signal<ChartType>('column');
  readonly config = signal<WidgetConfig | null>(config());
  readonly entry = signal<IndicatorEntry | null>(indicator('coverage'));
  readonly asTable = signal(false);
}

// Which formats a widget can export now, and why not (add-widget-export, design D3).
describe('WidgetView exportState', () => {
  const stillLoading = 'The widget is still loading.';
  const noRows = 'No rows match this widget’s query.';

  async function render(options: { type?: ChartType; entry?: IndicatorEntry | null; config?: WidgetConfig } = {}) {
    const http: HttpTestingController = configureApp();
    const fixture = TestBed.createComponent(Host);
    if (options.type) fixture.componentInstance.type.set(options.type);
    if (options.entry !== undefined) fixture.componentInstance.entry.set(options.entry);
    if (options.config) fixture.componentInstance.config.set(options.config);
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const view = fixture.debugElement.children[0].componentInstance as WidgetView;
    const answer = async (body: QueryResult) => {
      http.match(isQuery).filter((r) => !r.cancelled).forEach((r) => r.flush(body));
      await settle();
    };
    return { fixture, host: fixture.componentInstance, http, settle, view, answer };
  }

  it('offers nothing while the rows are on their way', async () => {
    const { view } = await render();

    expect(view.exportState()).toEqual({ image: stillLoading, workbook: stillLoading, imageNote: null });
  });

  it('says the indicator has not published when the project lists it without a table, without asking', async () => {
    const { view, http } = await render({ entry: indicator('coverage', { published: false }) });

    const state = view.exportState();
    expect(state.image).toBe('Coverage has not published yet.');
    expect(state.workbook).toBe('Coverage has not published yet.');
    expect(http.match(isQuery)).toHaveLength(0);
  });

  it('gives the message of a ratio over several answers as the reason, for the image and for the workbook (Card over two reasons)', async () => {
    const { view, answer } = await render({ type: 'stat', config: config({ groupBy: null, ratio: { numerator: 'numerator', denominator: 'denominator' } }) });

    await answer(result([], { combinedCategories: 'categoria' }));

    const state = view.exportState();
    expect(state.image).toBe(
      'categoria holds the answers of a multiple-choice question. Adding up several of them counts a respondent once for each answer. Keep one answer, group or split the widget by categoria, or plot a row the indicator publishes for any of them.',
    );
    expect(state.workbook).toBe(state.image);
    expect(state.imageNote).toBeNull();
  });

  it('says the indicator has not published when the answer says so', async () => {
    const { view, answer } = await render();

    await answer(result([], { published: false }));

    expect(view.exportState().image).toBe('Coverage has not published yet.');
    expect(view.exportState().workbook).toBe('Coverage has not published yet.');
  });

  it('names the columns its table no longer has', async () => {
    const { view, answer } = await render();

    await answer(result([], { missing: { indicator: false, columns: ['municipality', 'round'] } }));

    expect(view.exportState().image).toBe('Its table no longer has municipality, round.');
    expect(view.exportState().workbook).toBe('Its table no longer has municipality, round.');
  });

  it('says no row matches when the answer has none', async () => {
    const { view, answer } = await render();

    await answer(result([]));

    expect(view.exportState()).toEqual({ image: noRows, workbook: noRows, imageNote: null });
  });

  it('says the widget could not be loaded when the query fails', async () => {
    const { view, http, settle } = await render();

    http.match(isQuery).forEach((r) => r.flush({ title: 'Boom' }, { status: 500, statusText: 'Server Error' }));
    await settle();

    expect(view.exportState().image).toBe('The widget could not be loaded.');
    expect(view.exportState().workbook).toBe('The widget could not be loaded.');
  });

  it.each<[ChartType, WidgetConfig, () => QueryResult]>([
    ['column', config(), () => result([row('Cali', 75)])],
    ['stat', config({ groupBy: null }), () => result([row(null, 75)])],
    ['pie', config(), () => result([row('Cali', 75), row('Pasto', 25)])],
    ['crosstab', config({ seriesBy: 'water_source' }), () => seriesResult([cell('Cali', 'Piped', 5)], ['Piped'])],
  ])('offers both formats for a %s with rows, also when the row limit was reached', async (type, widgetConfig, body) => {
    const { view, answer } = await render({ type, config: widgetConfig });

    await answer({ ...body(), rowLimitReached: true });

    expect(view.exportState()).toEqual({ image: null, workbook: null, imageNote: null });
  });

  describe('a map', () => {
    const shapes = result([row('Cali', 75, 'POINT(1 1)')]);

    async function map(asTable = false) {
      const page = await render({ type: 'map', config: config({ geometryColumn: 'geometry' }) });
      page.host.asTable.set(asTable);
      await page.answer(shapes);
      return page;
    }

    it('withholds its images, not its workbook, while it is being drawn', async () => {
      const { view } = await map();

      view.mapReadiness.set('drawing');

      expect(view.exportState()).toEqual({ image: 'The map is still being drawn.', workbook: null, imageNote: null });
    });

    it('withholds its images when it has no shape to draw, and keeps its workbook', async () => {
      const { view } = await map();

      view.mapReadiness.set('nothing');

      expect(view.exportState()).toEqual({ image: 'The map has no shape to draw.', workbook: null, imageNote: null });
    });

    it('offers everything once it is drawn, and says the basemap is left out when one is shown', async () => {
      const { view } = await map();

      view.mapReadiness.set('ready');
      view.mapBasemap.set(false);
      expect(view.exportState()).toEqual({ image: null, workbook: null, imageNote: null });

      view.mapBasemap.set(true);
      expect(view.exportState()).toEqual({ image: null, workbook: null, imageNote: 'Shapes and legend, without the basemap' });
    });

    it('is a table like any other when shown as a table, whatever its engine is doing', async () => {
      const { view } = await map(true);

      view.mapReadiness.set('drawing');

      expect(view.exportState()).toEqual({ image: null, workbook: null, imageNote: null });
    });
  });
});

