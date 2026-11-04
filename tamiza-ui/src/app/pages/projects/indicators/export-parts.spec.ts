import { HttpTestingController } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { configureApp } from '../../../../testing/project-harness';
import { tick } from '../../../../testing/cdk';
import { ChartType, IndicatorEntry, QueryBox, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { ExportParts } from './export-parts';
import { ActiveDashboardFilter } from './dashboard-filters';
import { cell, config, indicator, isQuery, result, row, seriesResult } from './indicators-testing';
import { WidgetView } from './widget-view';

@Component({
  imports: [WidgetView],
  template: `<app-widget-view projectId="p1" [chartType]="type()" [config]="config()" [indicator]="entry()" [asTable]="asTable()" [dashboardFilters]="filters()" [showToggle]="false" />`,
})
class Host {
  readonly type = signal<ChartType>('column');
  readonly config = signal<WidgetConfig | null>(config());
  readonly entry = signal<IndicatorEntry | null>(indicator('coverage'));
  readonly asTable = signal(false);
  readonly filters = signal<ActiveDashboardFilter[]>([]);
}

const box = (group: string, median: number): QueryBox => ({
  group, values: 10, min: median - 2, max: median + 2, mean: median, q1: median - 1, median, q3: median + 1,
  lowerWhisker: median - 2, upperWhisker: median + 2, outlierCount: 0, outliers: [],
});

const sources = seriesResult([cell('Cali', 'Piped', 5), cell('Cali', 'Well', 3), cell('Pasto', 'Piped', 2)], ['Piped', 'Well']);

// What each chart and tile registers for an image of the widget (add-widget-export, design D5): its caption, legend, key or scale, notes
// and plot, read from the signals its template reads.
describe('what a widget registers for an image', () => {
  async function render(type: ChartType, widgetConfig: WidgetConfig, answer: QueryResult, asTable = false, filters: ActiveDashboardFilter[] = [], entry?: IndicatorEntry) {
    const http: HttpTestingController = configureApp();
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.type.set(type);
    fixture.componentInstance.config.set(widgetConfig);
    fixture.componentInstance.asTable.set(asTable);
    fixture.componentInstance.filters.set(filters);
    if (entry) {
      fixture.componentInstance.entry.set(entry);
    }
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    http.match(isQuery).filter((r) => !r.cancelled).forEach((r) => r.flush(answer));
    await settle();
    const view = fixture.debugElement.children[0].componentInstance as WidgetView;
    const registration = view.exportRegistration();
    expect(registration, `${type} registers`).not.toBeNull();
    return { view, registration: registration!, parts: registration!.parts() as ExportParts };
  }

  it('gives a stat tile its figure and what it measures, and no plot', async () => {
    const { parts, registration } = await render('stat', config({ groupBy: null }), result([row(null, 75)]));

    expect(parts.stat).toEqual({ value: '75', description: 'Coverage' });
    expect(registration.plot()).toBeNull();
    expect([parts.caption, parts.legend, parts.table]).toEqual([null, null, null]);
  });

  it('gives a chart without a series its caption and its plot', async () => {
    const { parts, registration } = await render('bar', config(), result([row('Cali', 75), row('Pasto', 40)]));

    expect(parts.caption).toContain('Coverage');
    expect(parts.legend).toBeNull();
    expect(parts.table).toBeNull();
    expect(registration.plot()?.getAttribute('data-testid')).toBe('chart-svg');
  });

  it('gives a histogram its caption and its plot', async () => {
    const { parts, registration } = await render('histogram', config({ groupBy: null, buckets: 2 }), result([row(null, 10), row(null, 30)]));

    expect(parts.caption).not.toBeNull();
    expect(registration.plot()).not.toBeNull();
  });

  it('puts the series legend in the place of the caption for a chart with a series', async () => {
    const { parts } = await render('column', config({ seriesBy: 'water_source', layout: 'stacked' }), sources);

    expect(parts.caption).toBeNull();
    expect(parts.legend).toMatchObject({ kind: 'entries', entries: [{ label: 'Piped', swatch: 'square' }, { label: 'Well', swatch: 'square' }] });
    expect((parts.legend as { entries: { color: string }[] }).entries.every((e) => e.color.startsWith('var(--tmz-pal-'))).toBe(true);
  });

  it('draws the keys of a line chart with a series as short strokes', async () => {
    const { parts } = await render('line', config({ seriesBy: 'water_source' }), sources);

    expect(parts.legend).toMatchObject({ kind: 'entries', entries: [{ swatch: 'line' }, { swatch: 'line' }] });
  });

  it('gives a pie its whole legend with each slice\'s value and share, and counts the groups it did not draw', async () => {
    const { parts } = await render('pie', config(), result([row('Cali', 75), row('Pasto', 25), row('Tumaco', 0)]));

    expect(parts.legend).toMatchObject({ kind: 'entries', entries: [{ label: 'Cali', detail: '75 · 75%' }, { label: 'Pasto', detail: '25 · 25%' }] });
    expect(parts.notes).toEqual(['1 group with a zero or negative value is not drawn; the table lists it.']);
  });

  it('gives a heat map its scale from the smallest value to the largest', async () => {
    const { parts, registration } = await render('heatmap', config({ seriesBy: 'water_source' }), sources);

    expect(parts.caption).toContain('Coverage');
    expect(parts.legend).toMatchObject({ kind: 'scale', low: '2', high: '5', empty: 'No value' });
    expect((parts.legend as { steps: string[] }).steps).toHaveLength(7);
    expect(registration.plot()).not.toBeNull();
  });

  it('gives a population pyramid its two sides', async () => {
    const answer = seriesResult([cell('0–4', 'Female', 5), cell('0–4', 'Male', 6)], ['Female', 'Male']);

    const { parts } = await render('pyramid', config({ seriesBy: 'sex', pyramid: { left: 'Female', right: 'Male' } }), answer);

    expect(parts.legend).toMatchObject({ kind: 'entries', entries: [{ label: 'Female' }, { label: 'Male' }] });
  });

  it('gives Likert bars their scale, the neutral step outlined', async () => {
    const answer = seriesResult([cell('Cali', 'No', 2), cell('Cali', 'Maybe', 1), cell('Cali', 'Yes', 3)], ['No', 'Maybe', 'Yes']);
    const scale = [
      { value: 'No', side: 'negative' as const },
      { value: 'Maybe', side: 'neutral' as const },
      { value: 'Yes', side: 'positive' as const },
    ];

    const { parts } = await render('likert', config({ seriesBy: 'answer', scale }), answer);

    expect(parts.legend).toMatchObject({
      kind: 'entries',
      entries: [{ label: 'No', swatch: 'square' }, { label: 'Maybe', swatch: 'outlined' }, { label: 'Yes', swatch: 'square' }],
    });
  });

  it('gives a cross table its table, whatever the widget is shown as', async () => {
    const { parts, registration } = await render('crosstab', config({ seriesBy: 'water_source' }), sources);

    expect(parts.table?.headers).toEqual(['municipality', 'Piped', 'Well', 'Total']);
    expect(parts.table?.rows.at(-1)?.[0]).toBe('Total');
    expect(parts.caption).toContain('Coverage');
    expect(registration.plot()).toBeNull();
  });

  it('gives a box plot its key of five marks and counts the outliers it did not draw', async () => {
    const answer = { ...result([row('Cali', 4)]), boxes: [{ ...box('Cali', 4), outlierCount: 130, outliers: [9] }] };

    const { parts, registration } = await render('boxplot', config({ groupBy: 'municipality' }), answer);

    expect(parts.legend).toEqual({
      kind: 'key',
      accent: expect.stringMatching(/^var\(--tmz-pal-/),
      items: [
        { glyph: 'box', label: expect.any(String) },
        { glyph: 'median', label: expect.any(String) },
        { glyph: 'mean', label: expect.any(String) },
        { glyph: 'whiskers', label: expect.any(String) },
        { glyph: 'outliers', label: expect.any(String) },
      ],
    });
    expect(parts.notes).toHaveLength(1);
    expect(registration.plot()).not.toBeNull();
  });

  it('gives a map its caption and its scale at full opacity, and the picture is the engine\'s, not a plot', async () => {
    const answer = result([row('Cali', 75, 'POINT(1 1)'), row('Pasto', 25, 'POINT(2 2)'), row('Tumaco', 5, null)]);

    const { parts, registration } = await render('map', config({ geometryColumn: 'geometry' }), answer);

    expect(registration.plot()).toBeNull();
    expect(parts.legend).toMatchObject({ kind: 'scale', low: '25', high: '75', stepOpacity: 1 });
    expect(parts.notes.some((note) => note.includes('1'))).toBe(true);
  });

  it.each<[ChartType, () => WidgetConfig, () => QueryResult]>([
    ['bar', () => config(), () => result([row('Cali', 75)])],
    ['pie', () => config(), () => result([row('Cali', 75)])],
    ['map', () => config({ geometryColumn: 'geometry' }), () => result([row('Cali', 75, 'POINT(1 1)')])],
    ['column', () => config({ seriesBy: 'water_source' }), () => sources],
    ['heatmap', () => config({ seriesBy: 'water_source' }), () => sources],
    ['boxplot', () => config({ groupBy: 'municipality' }), () => ({ ...result([row('Cali', 4)]), boxes: [box('Cali', 4)] })],
  ])('gives a %s shown as a table the table of its numbers, and no plot', async (type, widgetConfig, answer) => {
    const { parts, registration } = await render(type, widgetConfig(), answer(), true);

    expect(parts.table?.rows.length).toBeGreaterThan(0);
    expect(parts.legend ?? null).toBeNull();
    expect(registration.plot()).toBeNull();
  });

  // The reference lines in an image (add-chart-annotations, design D9, Chart annotations in widget images).
  describe('the reference lines', () => {
    const rate = indicator('piped_water_rate', { unit: 'de los hogares' });
    const ratio = { numerator: 'numerator', denominator: 'denominator' };
    const groups = result([row('Comuna 1', 0.4), row('Comuna 2', 0.5), row('Comuna 3', 0.6), row('Comuna 4', 0.2)], { overall: 0.4599 });
    const lined = (lines: NonNullable<WidgetConfig['display']>['referenceLines'], extra: Partial<WidgetConfig> = {}) =>
      config({ indicator: 'piped_water_rate', ratio, display: { referenceLines: lines }, ...extra });

    it('lists each line in the legend of a chart with no series, after the caption, with a dashed swatch in its own pattern (Mean line in an image)', async () => {
      const { parts } = await render('column', lined([{ kind: 'mean' }, { kind: 'overall', label: 'General' }]), groups, false, [], rate);

      expect(parts.caption).not.toBeNull();
      expect(parts.legend).toEqual({
        kind: 'entries',
        entries: [
          { label: 'Mean: 42.5% de los hogares', color: 'var(--color-heading, #111827)', swatch: 'dashed', dash: 0 },
          { label: 'General: 45.99% de los hogares', color: 'var(--color-heading, #111827)', swatch: 'dashed', dash: 1 },
        ],
      });
      expect(parts.notes).toEqual([]);
    });

    it('keeps the pattern of a line when the line before it has no value to draw', async () => {
      const { parts } = await render('column', lined([{ kind: 'overall' }, { kind: 'value', value: 0.5, label: 'Meta' }]), result(groups.rows), false, [], rate);

      expect(parts.legend).toMatchObject({ entries: [{ label: 'Meta: 50% de los hogares', swatch: 'dashed', dash: 1 }] });
    });

    it('lists the lines after the series of a chart with a series', async () => {
      const grouped = seriesResult([cell('Cali', 'Piped', 5), cell('Cali', 'Well', 3)], ['Piped', 'Well']);
      const { parts } = await render('column', config({ seriesBy: 'water_source', display: { referenceLines: [{ kind: 'value', value: 4, label: 'Meta' }] } }), grouped);

      expect(parts.caption).toBeNull();
      expect(parts.legend).toMatchObject({
        kind: 'entries',
        entries: [{ label: 'Piped', swatch: 'square' }, { label: 'Well', swatch: 'square' }, { label: 'Meta: 4', swatch: 'dashed', dash: 0 }],
      });
    });

    it('puts the lines in the notes while the chart is shown as a table, after the notes of the table itself (Shown as a table)', async () => {
      const { parts } = await render('column', lined([{ kind: 'overall' }]), groups, true, [], indicator('piped_water_rate'));

      expect(parts.table).not.toBeNull();
      expect(parts.legend).toBeNull();
      expect(parts.notes).toEqual(['Overall: 45.99%']);
    });

    it('draws a chart whose display holds no line as it always did: no legend, no note', async () => {
      const { parts } = await render('column', config({ display: { color: 2 } }), result([row('Cali', 75)]));

      expect(parts.legend).toBeNull();
      expect(parts.notes).toEqual([]);
      expect(parts.caption).not.toBeNull();
    });
  });

  describe('the names of the columns (add-indicator-column-metadata, Image caption)', () => {
    const ratio = { numerator: 'numerator', denominator: 'denominator' };
    const teatro = indicator('coverage', {
      label: 'Teatro hoy',
      columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' }, municipality: { label: 'Comuna' }, water_source: { label: 'Fuente' } },
    });
    const caption = 'Teatro hoy: Hogares que asistieron / Hogares by Comuna';

    it('names the columns in the caption of a column chart, as the screen does', async () => {
      const { parts } = await render('column', config({ ratio }), result([row('Cali', 0.5)]), false, [], teatro);

      expect(parts.caption).toBe(caption);
    });

    it('names the columns in the headers of the table of a chart shown as a table', async () => {
      const { parts } = await render('bar', config({ ratio }), result([row('Cali', 0.5)]), true, [], teatro);

      expect(parts.table?.headers).toEqual(['Comuna', 'Teatro hoy: Hogares que asistieron / Hogares']);
    });

    it('names the columns in the caption of a pie', async () => {
      const { parts } = await render('pie', config({ ratio }), result([row('Cali', 0.5)]), false, [], teatro);

      expect(parts.caption).toBe(caption);
    });

    it('names the columns in the caption of a map', async () => {
      const { parts } = await render('map', config({ ratio, geometryColumn: 'geometry' }), result([row('Cali', 0.5, 'POINT(1 1)')]), false, [], teatro);

      expect(parts.caption).toBe(caption);
    });

    it('names the columns in the caption of a heat map', async () => {
      const { parts } = await render('heatmap', config({ ratio, seriesBy: 'water_source' }), sources, false, [], teatro);

      expect(parts.caption).toBe('Teatro hoy: Hogares que asistieron / Hogares by Comuna and Fuente');
    });

    it('names the columns in the caption and the first header of a cross table', async () => {
      const { parts } = await render('crosstab', config({ ratio, seriesBy: 'water_source' }), sources, false, [], teatro);

      expect(parts.caption).toBe('Teatro hoy: Hogares que asistieron / Hogares by Comuna and Fuente');
      expect(parts.table?.headers).toEqual(['Comuna', 'Piped', 'Well', 'Total']);
    });

    it('names the columns in the description of a stat tile', async () => {
      const { parts } = await render('stat', config({ ratio, groupBy: null }), result([row(null, 0.5)]), false, [], teatro);

      expect(parts.stat).toEqual({ value: '1', description: 'Teatro hoy: Hogares que asistieron / Hogares' });
    });

    it('keeps the own names of the columns for a widget and an indicator that have no label', async () => {
      const { parts } = await render('column', config({ ratio }), result([row('Cali', 0.5)]));

      expect(parts.caption).toBe('Coverage: numerator / denominator by municipality');
    });
  });

  describe('the notes the tile itself says above its chart', () => {
    it('says the row limit, with the screen\'s words', async () => {
      const { view } = await render('bar', config(), { ...result([row('Cali', 75), row('Pasto', 40)]), rowLimitReached: true });

      expect(view.exportNotesAbove()).toEqual(['Only the first 2 rows are shown: the query matches more. Add a filter to narrow it down.']);
    });

    it('names the filters in force that did not narrow it, and the row limit of a box plot counts boxes', async () => {
      const filters: ActiveDashboardFilter[] = [
        { filter: { column: 'round', label: 'Round', kind: 'number', default: null }, value: { min: '3' }, ignored: true },
      ];
      const answer = { ...result([row('Cali', 4)]), boxes: [box('Cali', 4)], rowLimitReached: true };

      const { view } = await render('boxplot', config({ groupBy: 'municipality' }), answer, false, filters);

      expect(view.exportNotesAbove()).toEqual(['Not filtered by: Round', 'Only the first 1 boxes are shown: the query matches more. Add a filter to narrow it down.']);
    });

    it('says nothing for a widget that has nothing to say', async () => {
      const { view } = await render('bar', config(), result([row('Cali', 75)]));

      expect(view.exportNotesAbove()).toEqual([]);
    });
  });
});
