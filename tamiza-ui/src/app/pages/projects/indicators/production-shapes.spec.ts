import { MapStub, stubMap } from '../../../../testing/leaflet';
import { settle } from '../../../../testing/project-harness';
import { BasemapList } from '../../../core/basemaps.service';
import { ChartType, IndicatorColumns, WidgetConfig } from '../../../core/projects/indicators.service';
import { DataColumnType } from '../../../core/projects/data.service';
import { ColumnMetadataMap } from '../../../core/projects/pipeline.service';
import { chooseInWidgetMenu, clearOverlay, indicator, isColumns, isPatch, open, overlay, widget } from './indicators-testing';
import { ordersFor } from './category-order';
import { loadLeaflet } from './map-engine';
import fixture from './testing/production-shapes.json';
import { overallFor, queryFor, savedConfig } from './widget-view';

// The shapes of widget that a real project's dashboards use (add-indicator-column-metadata, design D13), against goldens written from
// the code before the changes of the widget and dashboard program. The API's ProductionShapesTests reads the same file. Every later
// change of the program runs this spec and regenerates no golden: what a shape saves, what it queries and what the widget form sends
// when it is saved without a change must stay what they were. The names and values are invented; only the structure is a real project's.

interface Shape {
  name: string;
  chartType: ChartType;
  columns: { name: string; type: string }[];
  stored: string;
  saved?: unknown;
  uiQuery?: unknown;
  formSaved?: unknown;
  indicatorColumns?: ColumnMetadataMap;
}

const shapes = fixture.shapes as unknown as Shape[];

/**
 * The shapes the widget context adds (add-widget-context, design D13) to the fixture, whose stored text, query and plan the API's
 * ProductionShapesTests pins. Each has its `saved`, `uiQuery` and `formSaved` goldens, so the widget form is checked to send their
 * texts back when it is saved without a change.
 */
const contextShapes = [
  'ratio bar with a subtitle, a footnote, a hidden caption and a unit',
  'column chart in color 2 with an own caption text',
  'stat tile with an own caption text, a footnote and a description button',
  'map with four tooltip columns, a basemap and a unit',
  'Likert widget with a subtitle',
];

/**
 * The shapes the chart annotations add (add-chart-annotations, task 1.4) to the fixture: reference lines, steps and data labels. The
 * API's ProductionShapesTests pins their stored text, query and plan; each has its `saved`, `uiQuery` and `formSaved` goldens, so the
 * widget form is checked to send the three settings back when it is saved without a change.
 */
const annotationShapes = [
  'ratio column chart with a mean line and an overall line',
  'bar by a category column with data labels',
  'line by a price column with steps',
  '100% bar chart of a count with a value line of 0.5',
  'grouped column chart with an overall line',
];

/**
 * The shapes the declared category order adds (add-declared-category-order, task 1.1) to the fixture, after the others: a column chart
 * sorted by `orden` over a numbered question column, a 100% bar over a numbered series column and a heat map of two numbered columns. The
 * API's ProductionShapesTests pins their stored text, query and plan; each has its `saved`, `uiQuery` and `formSaved` goldens.
 */
const orderShapes = [
  'column chart sorted by orden as text over a numbered question column',
  '100% bar over counts with a numbered series column',
  'heat map of two numbered columns',
];

/**
 * The shapes whose indicator publishes its group and series columns with only a label (`indicatorColumns`), so that no order applies to
 * them: the three above and the line over text prices, the box plot with kept colors, both Likert widgets, the cross table and the pie.
 */
const labelledShapes = [
  'line over text prices with a series and no sort',
  'box plot grouped by a column with kept colors',
  'Likert widget with its scale',
  'Likert widget with a subtitle',
  'cross table',
  'pie',
  ...orderShapes,
];

const hasQueryGoldens = (shape: Shape): boolean => !!shape.uiQuery && !!shape.saved;
const hasFormGolden = (shape: Shape): boolean => !!shape.formSaved;

/** The shapes that take part in the context checks: those whose stored display holds one of the texts of add-widget-context. */
const textKeys = ['subtitle', 'footnote', 'caption', 'unit', 'showDescription'] as const;
const withTexts = (shape: Shape): boolean => textKeys.some((key) => key in (JSON.parse(shape.stored).display ?? {}));

/** What reaches the wire: a value as JSON, so that a key left undefined is a key left out, as it is in a request. */
const wire = (value: unknown): unknown => JSON.parse(JSON.stringify(value));

const configOf = (shape: Shape) => JSON.parse(shape.stored) as WidgetConfig;

function catalogOf(shape: Shape): IndicatorColumns {
  const columns = shape.columns.map((c) => ({ name: c.name, type: c.type as DataColumnType, label: null }));
  const numeric = columns.filter((c) => c.type === 'integer' || c.type === 'decimal').map((c) => c.name);
  return {
    published: true,
    columns,
    numeric,
    defaults: { measure: numeric.includes('value') ? 'value' : (numeric[0] ?? null), groupBy: columns.find((c) => c.type === 'text')?.name ?? null },
  };
}

const basemaps: BasemapList = {
  provider: 'google',
  basemaps: (['streets', 'satellite'] as const).map((kind) => ({
    kind,
    minZoom: 0,
    maxZoom: 22,
    tiles: `/api/v1/basemap-tiles/link-${kind}/{z}/{x}/{y}`,
    linksExpireAt: '2026-10-17T15:15:00Z',
    linksRefreshInSeconds: 1980,
    attribution: { source: 'google' as const, text: null, url: null },
  })),
};

/** Opens the widget form on the shape and saves it without touching a field; the config the form sent. */
async function savedByTheForm(shape: Shape): Promise<{ chartType: string; config: unknown }> {
  const context = await open('analyst', [widget('w1', { chartType: shape.chartType, config: configOf(shape) })], null, {
    indicators: [indicator('encuesta_hoy')],
  });
  await chooseInWidgetMenu(context, 'widget-edit');
  const columns = context.http.expectOne(isColumns);
  columns.flush(catalogOf(shape));
  await settle(context.harness);
  context.http.match((r) => r.method === 'GET' && r.url === '/api/v1/basemaps').forEach((request) => request.flush(basemaps));
  await settle(context.harness);

  overlay().querySelector<HTMLElement>('[data-testid="dialog-save"]')!.click();
  await settle(context.harness);

  const patch = context.http.match(isPatch)[0];
  expect(patch, 'the form did not send the widget').toBeDefined();
  return patch.request.body as { chartType: string; config: unknown };
}

describe('the production shapes', () => {
  afterEach(clearOverlay);

  it('lists the shapes the design names, once each', () => {
    expect(shapes).toHaveLength(28);
    expect(new Set(shapes.map((s) => s.name)).size).toBe(28);
    expect(shapes.every(hasQueryGoldens)).toBe(true);
  });

  it('has a widget form golden for every shape, none is skipped', () => {
    expect(shapes.filter((s) => !hasFormGolden(s)).map((s) => s.name)).toEqual([]);
  });

  it('has the shapes of the widget context, and only they hold its texts', () => {
    expect(shapes.filter(withTexts).map((s) => s.name)).toEqual(contextShapes);
  });

  it('has the shapes of the chart annotations, and only they hold its settings', () => {
    const annotationKeys = ['referenceLines', 'interpolation', 'dataLabels'];
    const holding = shapes.filter((s) => annotationKeys.some((key) => key in (JSON.parse(s.stored).display ?? {})));
    expect(holding.map((s) => s.name)).toEqual(annotationShapes);
  });

  it('has the shapes of the declared category order, last, and the columns of the indicators that label their columns only', () => {
    expect(shapes.slice(25).map((s) => s.name)).toEqual(orderShapes);
    const labelled = shapes.filter((s) => s.indicatorColumns);
    expect(labelled.map((s) => s.name).sort()).toEqual([...labelledShapes].sort());
    for (const shape of labelled) {
      // The indicator only labels its group and series columns, so no order or hidden prefix applies (add-declared-category-order, task 3.4).
      expect(ordersFor(shape.chartType, configOf(shape), shape.indicatorColumns), shape.name).toBeNull();
      const config = configOf(shape);
      for (const column of [config.groupBy, config.seriesBy].filter((c): c is string => !!c)) {
        expect(Object.keys(shape.indicatorColumns![column] ?? {}), `${shape.name}: ${column}`).toEqual(['label']);
      }
    }
  });

  describe.each(shapes.filter(withTexts).map((s) => [s.name, s] as const))('%s (widget context)', (_name, shape) => {
    it('saves its texts as they are stored, whatever the chart type takes of the rest', () => {
      const stored = JSON.parse(shape.stored).display;
      const saved = wire(savedConfig(shape.chartType, configOf(shape))) as WidgetConfig;
      for (const key of textKeys) {
        expect(saved.display?.[key], key).toEqual(stored[key]);
      }
    });

    it('queries what the same shape without the texts queries', () => {
      const without = { ...configOf(shape), display: undefined };
      expect(wire(queryFor(shape.chartType, configOf(shape)))).toEqual(wire(queryFor(shape.chartType, without)));
      expect(wire(queryFor(shape.chartType, configOf(shape)))).toEqual(shape.uiQuery);
    });

    it('is saved by savedConfig as it is saved again (a second save changes nothing)', () => {
      const once = savedConfig(shape.chartType, configOf(shape));
      expect(wire(savedConfig(shape.chartType, once))).toEqual(wire(once));
    });
  });

  describe.each(shapes.map((s) => [s.name, s] as const))('%s', (_name, shape) => {
    it('saves what it saved', () => {
      expect(wire(savedConfig(shape.chartType, configOf(shape)))).toEqual(shape.saved);
    });

    it('queries what it queried', () => {
      expect(wire(queryFor(shape.chartType, configOf(shape)))).toEqual(shape.uiQuery);
    });

    // The overall value is asked for by one shape only: the ratio column chart with no series and an overall line (add-chart-annotations, task 2.1).
    it('asks for the overall value only when it has an overall line over a ratio and no series', () => {
      expect(overallFor(shape.chartType, configOf(shape))).toBe(shape.name === 'ratio column chart with a mean line and an overall line');
    });

    it('is sent by the widget form, saved without a change, as it was', async () => {
      let stub: MapStub | null = null;
      if (shape.chartType === 'map') {
        const preload = stubMap();
        await loadLeaflet();
        preload.restore();
        stub = stubMap({ width: 600, height: 400 });
      }
      try {
        const body = await savedByTheForm(shape);

        expect(body.chartType).toBe(shape.chartType);
        expect(wire(body.config)).toEqual(shape.formSaved);
      } finally {
        stub?.restore();
      }
    });
  });
});
