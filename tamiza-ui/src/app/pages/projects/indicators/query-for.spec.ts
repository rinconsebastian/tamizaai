import { LikertStep, WidgetConfig, WidgetDisplay, WidgetReferenceLine, WidgetSort, WidgetTooltipItem, chartTypes, emptyConfig } from '../../../core/projects/indicators.service';
import { offersDataLabels, overallFor, queryFor, referenceKindsFor, savedConfig } from './widget-view';

const byColumn: WidgetSort = { by: 'column', column: 'round', compareAs: 'number', direction: 'asc' };
const byValue: WidgetSort = { by: 'value', column: null, compareAs: null, direction: 'desc' };

function config(sort: WidgetSort | null): WidgetConfig {
  return { ...emptyConfig('coverage'), groupBy: 'municipality', geometryColumn: 'geometry', buckets: 20, sort };
}

describe('queryFor sort', () => {
  it('keeps the order of a bar and of a column chart', () => {
    expect(queryFor('bar', config(byColumn)).sort).toEqual(byColumn);
    expect(queryFor('column', config(byColumn)).sort).toEqual(byColumn);
  });

  it('sets the order to null, not leaves it out, for every other chart type', () => {
    for (const type of ['stat', 'line', 'histogram', 'pie', 'map'] as const) {
      const query = queryFor(type, config(byColumn));
      expect(query.sort).toBeNull();
      expect('sort' in query).toBe(true);
    }
  });

  it('does not send a column or a comparison when sorting by the plotted value', () => {
    const sort: WidgetSort = { by: 'value', column: 'round', compareAs: 'text', direction: 'desc' };

    expect(queryFor('column', config(sort)).sort).toEqual(byValue);
  });

  it('keeps no order as no order', () => {
    expect(queryFor('column', config(null)).sort).toBeNull();
  });
});

const items: WidgetTooltipItem[] = [
  { column: 'address', label: 'Dirección', format: null },
  { column: 'sample_target', label: null, format: 'count' },
  { column: 'progress', label: 'Avance', format: 'percent' },
];

function mapConfig(): WidgetConfig {
  return { ...config(null), tooltip: items, basemap: 'satellite' };
}

describe('queryFor tooltip and basemap', () => {
  it('sends the names of the tooltip columns of a map, and never their labels or formats', () => {
    expect(queryFor('map', mapConfig()).tooltip).toEqual([
      { column: 'address', label: null, format: null },
      { column: 'sample_target', label: null, format: null },
      { column: 'progress', label: null, format: null },
    ]);
  });

  it('gives the same query whatever the labels and formats are, so editing them never queries again', () => {
    const relabelled = { ...mapConfig(), tooltip: items.map((item) => ({ ...item, label: 'Otro', format: null })) };

    expect(JSON.stringify(queryFor('map', relabelled))).toBe(JSON.stringify(queryFor('map', mapConfig())));
  });

  it('sends the basemap as null, and the same query whatever the basemap, so choosing one never queries again', () => {
    for (const basemap of [null, 'streets', 'satellite'] as const) {
      const query = queryFor('map', { ...mapConfig(), basemap });
      expect(query.basemap).toBeNull();
      expect('basemap' in query).toBe(true);
    }
    expect(JSON.stringify(queryFor('map', { ...mapConfig(), basemap: null }))).toBe(JSON.stringify(queryFor('map', mapConfig())));
  });

  it('sets the tooltip columns to an empty list, not leaves them out, for every other chart type', () => {
    for (const type of ['stat', 'bar', 'column', 'line', 'histogram', 'pie'] as const) {
      const query = queryFor(type, mapConfig());
      expect(query.tooltip).toEqual([]);
      expect('tooltip' in query).toBe(true);
      expect(query.basemap).toBeNull();
    }
  });

  it('keeps the order of the columns', () => {
    const reordered = { ...mapConfig(), tooltip: [...items].reverse() };

    expect(queryFor('map', reordered).tooltip.map((item) => item.column)).toEqual(['progress', 'sample_target', 'address']);
  });
});

describe('savedConfig', () => {
  it('keeps the whole tooltip items and the basemap of a map, besides the query', () => {
    const saved = savedConfig('map', mapConfig());

    expect(saved.tooltip).toEqual(items);
    expect(saved.basemap).toBe('satellite');
    expect({ ...saved, tooltip: [], basemap: null }).toEqual({ ...queryFor('map', mapConfig()), tooltip: [], basemap: null });
  });

  it('saves a map with no basemap as null', () => {
    expect(savedConfig('map', { ...mapConfig(), basemap: null }).basemap).toBeNull();
  });

  it('saves no tooltip columns and no basemap for any other chart type, as a map turned into a bar is', () => {
    for (const type of ['stat', 'bar', 'column', 'line', 'histogram', 'pie'] as const) {
      const saved = savedConfig(type, mapConfig());
      expect(saved.tooltip).toEqual([]);
      expect(saved.basemap).toBeNull();
    }
  });

  it('is the same for the query whatever the tooltip items and the basemap are', () => {
    const saved = savedConfig('map', mapConfig());

    expect(JSON.stringify(queryFor('map', saved))).toBe(JSON.stringify(queryFor('map', mapConfig())));
  });
});

describe('the dashboard filters a widget ignores', () => {
  const ignoring = (): WidgetConfig => ({ ...emptyConfig('coverage'), groupBy: 'municipality', ignoredDashboardFilters: ['municipality', 'round'] });

  it('are saved with the widget, whatever its chart type', () => {
    for (const type of ['stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map'] as const) {
      expect(savedConfig(type, ignoring()).ignoredDashboardFilters).toEqual(['municipality', 'round']);
    }
  });

  it('are not part of the query, so changing them never queries the widget again', () => {
    expect(queryFor('column', ignoring()).ignoredDashboardFilters).toEqual([]);
    expect(JSON.stringify(queryFor('column', ignoring()))).toBe(JSON.stringify(queryFor('column', { ...ignoring(), ignoredDashboardFilters: [] })));
  });

  it('read as none from a config that predates them', () => {
    const old = { ...emptyConfig('coverage') } as Partial<WidgetConfig>;
    delete old.ignoredDashboardFilters;

    expect(savedConfig('column', old as WidgetConfig).ignoredDashboardFilters).toEqual([]);
  });
});

describe('queryFor and savedConfig with a series (design D1, D7)', () => {
  const scale: LikertStep[] = [
    { value: 'Disagree', side: 'negative' },
    { value: 'Neither', side: 'neutral' },
    { value: 'Agree', side: 'positive' },
  ];
  /** A config with every setting of the second dimension filled in, as a draft that was switched between kinds would hold. */
  const full = (): WidgetConfig => ({ ...config(null), seriesBy: 'water_source', layout: 'stacked', pyramid: { left: 'Female', right: 'Male' }, scale });
  const keys = ['seriesBy', 'layout', 'pyramid', 'scale'] as const;
  /** Which of the four settings each kind keeps. */
  const kept: Record<(typeof chartTypes)[number], readonly (typeof keys)[number][]> = {
    stat: [],
    bar: ['seriesBy', 'layout'],
    column: ['seriesBy', 'layout'],
    line: ['seriesBy'],
    histogram: [],
    pie: [],
    map: [],
    heatmap: ['seriesBy'],
    pyramid: ['seriesBy', 'pyramid'],
    likert: ['seriesBy', 'scale'],
    crosstab: ['seriesBy'],
    boxplot: [],
  };

  it('keeps, for each of the twelve kinds, exactly the settings of the second dimension that kind uses', () => {
    expect(Object.keys(kept)).toEqual([...chartTypes]);
    for (const type of chartTypes) {
      const query = queryFor(type, full());
      expect(keys.filter((key) => key in query)).toEqual(kept[type]);
      for (const key of kept[type]) {
        expect(query[key]).toEqual(full()[key]);
      }
    }
  });

  it('leaves the four settings out, not null, of a widget without a series, so it queries exactly as before', () => {
    for (const type of chartTypes) {
      const query = queryFor(type, config(null));
      expect(keys.filter((key) => key in query)).toEqual([]);
      expect(JSON.stringify(query)).toBe(JSON.stringify({ ...query }));
    }
    expect(Object.keys(queryFor('column', config(null))).sort()).toEqual(Object.keys(emptyConfig()).sort());
  });

  it('leaves out an empty series column, an unset layout and an empty scale', () => {
    const query = queryFor('column', { ...config(null), seriesBy: '', layout: null, pyramid: null, scale: [] });

    expect(keys.filter((key) => key in query)).toEqual([]);
    expect(keys.filter((key) => key in queryFor('likert', { ...config(null), scale: [] }))).toEqual([]);
  });

  it('is the same query whatever the layout of a bar chart, but not the same as a chart without the series', () => {
    const stacked = JSON.stringify(queryFor('bar', { ...full(), layout: 'stacked' }));

    expect(stacked).not.toBe(JSON.stringify(queryFor('bar', { ...full(), layout: 'percent' })));
    expect(stacked).not.toBe(JSON.stringify(queryFor('bar', config(null))));
  });

  it('saves the same settings as it queries, and the tooltip and basemap only for a map', () => {
    for (const type of chartTypes) {
      const saved = savedConfig(type, full());
      expect(keys.filter((key) => key in saved)).toEqual(kept[type]);
      expect(saved.tooltip).toEqual([]);
    }
  });
});

describe('queryFor and savedConfig with a display (add-dashboard-palettes, design D3)', () => {
  /** A config with both fields of the widget's display filled in, as a draft that was switched between kinds would hold. */
  const displayed = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({ ...config(null), display: { color: 3, ramp: 'green' }, ...overrides });
  /** What each kind keeps of the display: a color for bar, column and line with no series, a histogram and a box plot, a scale for a map and a heat map, an orientation for a box plot. */
  const kept: Record<(typeof chartTypes)[number], WidgetDisplay | undefined> = {
    stat: undefined,
    bar: { color: 3 },
    column: { color: 3 },
    line: { color: 3 },
    histogram: { color: 3 },
    pie: undefined,
    map: { ramp: 'green' },
    heatmap: { ramp: 'green' },
    pyramid: undefined,
    likert: undefined,
    crosstab: undefined,
    boxplot: { color: 3 },
  };

  it('leaves the display out of the query of every kind, so choosing a color never queries again', () => {
    expect(Object.keys(kept)).toEqual([...chartTypes]);
    for (const type of chartTypes) {
      const query = queryFor(type, displayed({ seriesBy: type === 'heatmap' ? 'water_source' : undefined }));
      expect('display' in query).toBe(false);
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, { ...displayed({ seriesBy: type === 'heatmap' ? 'water_source' : undefined }), display: { color: 7, ramp: 'gray' } })));
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, { ...displayed({ seriesBy: type === 'heatmap' ? 'water_source' : undefined }), display: undefined })));
    }
  });

  it('keeps, for each of the twelve kinds, exactly the fields of the display that kind takes', () => {
    for (const type of chartTypes) {
      const saved = savedConfig(type, displayed());
      expect(saved.display).toEqual(kept[type]);
      expect('display' in saved).toBe(kept[type] !== undefined);
    }
  });

  it('saves no color for a bar, column or line chart that has a series', () => {
    for (const type of ['bar', 'column', 'line'] as const) {
      const saved = savedConfig(type, displayed({ seriesBy: 'water_source' }));
      expect(saved.seriesBy).toBe('water_source');
      expect('display' in saved).toBe(false);
    }
  });

  it('keeps the color again when the series is cleared', () => {
    expect(savedConfig('column', displayed({ seriesBy: '' })).display).toEqual({ color: 3 });
    expect(savedConfig('column', displayed({ seriesBy: null })).display).toEqual({ color: 3 });
  });

  it('keeps the scale of a heat map, which always has a series', () => {
    expect(savedConfig('heatmap', displayed({ seriesBy: 'water_source' })).display).toEqual({ ramp: 'green' });
  });

  it('keeps the color 1 a member chose, which is not the same as no color', () => {
    expect(savedConfig('column', displayed({ display: { color: 1 } })).display).toEqual({ color: 1 });
  });

  it('saves no display at all, not an empty one, when there is nothing to keep (a widget saved before palettes)', () => {
    for (const type of chartTypes) {
      for (const display of [undefined, null, {}, { color: undefined }]) {
        const saved = savedConfig(type, { ...config(null), display });
        expect('display' in saved).toBe(false);
      }
    }
    expect(Object.keys(savedConfig('column', config(null))).sort()).toEqual(Object.keys(emptyConfig()).sort());
  });

  it('saves the same config as before palettes for a widget saved before them', () => {
    const before = config(null);

    for (const type of chartTypes) {
      const withoutDisplay = JSON.stringify(savedConfig(type, before));
      expect(withoutDisplay).toBe(JSON.stringify(savedConfig(type, { ...before, display: undefined })));
      expect(withoutDisplay).not.toContain('display');
    }
  });
});

describe('queryFor and savedConfig with the texts of a widget (add-widget-context, design D1)', () => {
  /** Every text of the display at once, as an author who set them all. */
  const texts: WidgetDisplay = {
    subtitle: '¿Quién fue al evento?',
    footnote: 'No suman 100 %: respuesta múltiple.',
    caption: { text: '% de quienes asistieron' },
    unit: 'de quienes asistieron',
    showDescription: true,
  };
  const withTexts = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({ ...config(null), display: { ...texts }, ...overrides });

  it('gives, for every kind, the query of the same config without the texts, so editing them never queries again', () => {
    for (const type of chartTypes) {
      const query = queryFor(type, withTexts({ seriesBy: type === 'heatmap' ? 'water_source' : undefined }));
      expect('display' in query).toBe(false);
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, withTexts({ seriesBy: type === 'heatmap' ? 'water_source' : undefined, display: undefined }))));
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, withTexts({ seriesBy: type === 'heatmap' ? 'water_source' : undefined, display: { caption: { hidden: true }, subtitle: 'Otro' } }))));
    }
  });

  it('keeps the texts on every one of the twelve kinds, with nothing else the kind does not take', () => {
    for (const type of chartTypes) {
      expect(savedConfig(type, withTexts()).display).toEqual(texts);
    }
  });

  it('keeps the texts when a stat tile becomes a bar chart, and when it becomes anything else', () => {
    const stat = savedConfig('stat', withTexts());
    expect(stat.display).toEqual(texts);
    for (const type of chartTypes) {
      expect(savedConfig(type, stat).display).toEqual(texts);
    }
  });

  it('keeps a color or a scale beside the texts, in the order the API writes them', () => {
    const saved = savedConfig('map', withTexts({ display: { ...texts, ramp: 'green', color: 4, labels: { municipality: 'Comuna' } } }));

    expect(Object.keys(saved.display!)).toEqual(['ramp', 'labels', 'subtitle', 'footnote', 'caption', 'unit', 'showDescription']);
  });

  it('keeps a hidden caption, and leaves out a caption, a text and a flag that say nothing', () => {
    expect(savedConfig('bar', withTexts({ display: { caption: { hidden: true } } })).display).toEqual({ caption: { hidden: true } });
    for (const display of [
      { caption: {} },
      { caption: { text: '  ' } },
      { subtitle: '  ', footnote: '', unit: ' ' },
      { showDescription: false as unknown as true },
    ] satisfies WidgetDisplay[]) {
      expect('display' in savedConfig('bar', withTexts({ display }))).toBe(false);
    }
  });

  it('saves what a widget without texts saved before', () => {
    for (const type of chartTypes) {
      expect(JSON.stringify(savedConfig(type, { ...config(null), display: { color: 3, ramp: 'green' } }))).toBe(
        JSON.stringify({ ...savedConfig(type, { ...config(null), display: { color: 3, ramp: 'green', caption: {} } }) }),
      );
    }
  });
});

describe('queryFor and savedConfig for a box plot (add-boxplot-widget, design D7)', () => {
  const boxed = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({
    ...emptyConfig('household_income'),
    groupBy: 'municipality',
    seriesBy: 'sex',
    sort: byValue,
    display: { color: 3, ramp: 'green', orientation: 'horizontal' },
    ...overrides,
  });

  it('sends an order by median, with a group column, and no series', () => {
    const query = queryFor('boxplot', boxed());

    expect(query.sort).toEqual(byValue);
    expect(query.groupBy).toBe('municipality');
    expect('seriesBy' in query).toBe(false);
    expect('display' in query).toBe(false);
  });

  it('sends no order that is by a column, and none without a group column, so the query is not refused for the form\'s choice', () => {
    expect(queryFor('boxplot', boxed({ sort: byColumn })).sort).toBeNull();
    expect(queryFor('boxplot', boxed({ groupBy: null })).sort).toBeNull();
    expect(queryFor('boxplot', boxed({ sort: null })).sort).toBeNull();
    expect(savedConfig('boxplot', boxed({ groupBy: null })).sort).toBeNull();
  });

  it('does not send a column or a comparison with an order by median', () => {
    const sort: WidgetSort = { by: 'value', column: 'round', compareAs: 'text', direction: 'asc' };

    expect(queryFor('boxplot', boxed({ sort })).sort).toEqual({ by: 'value', column: null, compareAs: null, direction: 'asc' });
  });

  it('saves its color and its orientation, and no scale', () => {
    expect(savedConfig('boxplot', boxed()).display).toEqual({ color: 3, orientation: 'horizontal' });
    expect(savedConfig('boxplot', boxed({ display: { orientation: 'vertical' } })).display).toEqual({ orientation: 'vertical' });
    expect('display' in savedConfig('boxplot', boxed({ display: undefined }))).toBe(false);
  });

  it('is the only kind that saves an orientation, so changing the kind away drops it', () => {
    for (const type of chartTypes.filter((t) => t !== 'boxplot')) {
      const saved = savedConfig(type, boxed({ seriesBy: type === 'heatmap' || type === 'pyramid' || type === 'likert' || type === 'crosstab' ? 'sex' : null }));
      expect(saved.display?.orientation, type).toBeUndefined();
    }
  });

  it('sends the same query whatever its orientation, so switching it never queries again', () => {
    const horizontal = JSON.stringify(queryFor('boxplot', boxed()));

    expect(JSON.stringify(queryFor('boxplot', boxed({ display: { orientation: 'vertical' } })))).toBe(horizontal);
    expect(JSON.stringify(queryFor('boxplot', boxed({ display: undefined })))).toBe(horizontal);
  });
});

describe('queryFor and savedConfig with column labels (add-indicator-column-metadata, design D5)', () => {
  const labels = { numerator: 'Hogares que asistieron', zona: 'Zona' };
  const labelled = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({ ...config(null), display: { labels }, ...overrides });

  it('leaves the labels out of the query of every kind, so typing one never queries again', () => {
    for (const type of chartTypes) {
      const seriesBy = type === 'heatmap' || type === 'pyramid' || type === 'likert' || type === 'crosstab' ? 'sex' : undefined;
      const query = queryFor(type, labelled({ seriesBy }));

      expect('display' in query).toBe(false);
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, { ...labelled({ seriesBy }), display: undefined })));
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, { ...labelled({ seriesBy }), display: { labels: { zona: 'Otra' } } })));
    }
  });

  it('saves the labels of every kind, and only them for a kind that takes no color or scale', () => {
    for (const type of chartTypes) {
      const saved = savedConfig(type, { ...labelled({ seriesBy: 'sex' }), display: { color: 3, ramp: 'green', orientation: 'horizontal', labels } });

      expect(saved.display?.labels, type).toEqual(labels);
    }
    expect(savedConfig('stat', labelled()).display).toEqual({ labels });
    expect(savedConfig('pie', labelled()).display).toEqual({ labels });
  });

  it('keeps the labels across a change of kind', () => {
    const draft = labelled({ groupBy: 'zona' });

    expect(savedConfig('column', draft).display?.labels).toEqual(labels);
    expect(savedConfig('bar', draft).display?.labels).toEqual(labels);
    expect(savedConfig('stat', draft).display?.labels).toEqual(labels);
  });

  it('keeps the color and the labels together, and the labels alone where no color is taken', () => {
    const draft = { ...labelled(), display: { color: 3, labels } };

    expect(savedConfig('column', draft).display).toEqual({ color: 3, labels });
    expect(savedConfig('stat', draft).display).toEqual({ labels });
  });

  it('drops a blank label and saves no display when none is left', () => {
    expect(savedConfig('column', { ...config(null), display: { labels: { zona: '  ', numerator: 'Hogares' } } }).display).toEqual({ labels: { numerator: 'Hogares' } });
    expect('display' in savedConfig('column', { ...config(null), display: { labels: { zona: '' } } })).toBe(false);
    expect('display' in savedConfig('column', { ...config(null), display: { labels: {} } })).toBe(false);
  });

  it('saves the same config as before for a widget with no labels', () => {
    const before = JSON.stringify(savedConfig('column', config(null)));

    expect(JSON.stringify(savedConfig('column', { ...config(null), display: { labels: undefined } }))).toBe(before);
    expect(before).not.toContain('labels');
  });
});

describe('overallFor and the chart annotations (add-chart-annotations, design D2, D4)', () => {
  const rate = { numerator: 'numerator', denominator: 'denominator' };
  const lined = (referenceLines: WidgetReferenceLine[] | undefined, overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({
    ...config(null),
    ratio: rate,
    ...overrides,
    ...(referenceLines ? { display: { ...overrides.display, referenceLines } } : {}),
  });
  const overall: WidgetReferenceLine[] = [{ kind: 'overall' }];

  // The cases of `WidgetQueryTests` for `WidgetQuery.Overall` (tamiza-api): both twins decide it the same way.
  it.each(['bar', 'column', 'line'] as const)('asks for the overall value of a %s with no series, a ratio and an overall line', (type) => {
    expect(overallFor(type, lined(overall))).toBe(true);
    expect(overallFor(type, lined([{ kind: 'mean' }, { kind: 'value', value: 0.5, label: 'Meta' }, { kind: 'overall', label: 'General' }]))).toBe(true);
  });

  it.each(['bar', 'column', 'line'] as const)('does not ask for it for a measure, a series, a mean line only or no display (%s)', (type) => {
    expect(overallFor(type, lined(overall, { ratio: null, measure: 'value' }))).toBe(false);
    expect(overallFor(type, lined(overall, { ratio: null }))).toBe(false);
    expect(overallFor(type, lined(overall, { seriesBy: 'water_source' }))).toBe(false);
    expect(overallFor(type, lined([{ kind: 'mean' }, { kind: 'median' }, { kind: 'value', value: 0.5 }]))).toBe(false);
    expect(overallFor(type, lined(undefined))).toBe(false);
    expect(overallFor(type, lined(undefined, { display: { color: 2, dataLabels: true } }))).toBe(false);
    expect(overallFor(type, lined(undefined, { display: { referenceLines: [] } }))).toBe(false);
  });

  it('never asks for it for any other chart type', () => {
    for (const type of chartTypes.filter((t) => t !== 'bar' && t !== 'column' && t !== 'line')) {
      expect(overallFor(type, lined(overall))).toBe(false);
    }
  });

  it('leaves the lines, the steps and the data labels out of the query of every kind, so editing them never queries again', () => {
    const annotated: WidgetDisplay = { color: 2, referenceLines: [{ kind: 'mean' }, { kind: 'overall' }], interpolation: 'step', dataLabels: true };
    for (const type of chartTypes) {
      const series = type === 'heatmap' ? 'water_source' : undefined;
      const query = queryFor(type, { ...config(null), ratio: rate, seriesBy: series, display: annotated });
      expect('display' in query).toBe(false);
      expect(JSON.stringify(query)).toBe(JSON.stringify(queryFor(type, { ...config(null), ratio: rate, seriesBy: series })));
    }
  });

  it('saves the lines, the steps and the data labels last, after the texts, in the order the API writes them', () => {
    const display: WidgetDisplay = {
      referenceLines: [{ kind: 'mean' }, { kind: 'value', value: 0.5, label: 'Meta' }],
      dataLabels: true,
      interpolation: 'step',
      showDescription: true,
      color: 2,
      subtitle: 'Subtítulo',
    };

    const saved = savedConfig('line', lined(undefined, { display })).display!;

    expect(Object.keys(saved)).toEqual(['color', 'subtitle', 'showDescription', 'referenceLines', 'interpolation', 'dataLabels']);
    expect(saved.referenceLines).toEqual([{ kind: 'mean' }, { kind: 'value', value: 0.5, label: 'Meta' }]);
  });

  it('keeps a saved "linear" and a saved false as they are, and saves no display for a widget with none of the three', () => {
    expect(savedConfig('line', lined(undefined, { display: { interpolation: 'linear', dataLabels: false } })).display).toEqual({ interpolation: 'linear', dataLabels: false });
    expect(savedConfig('pie', lined(undefined, { display: { dataLabels: false } })).display).toEqual({ dataLabels: false });
    expect('display' in savedConfig('line', lined(undefined, { display: { referenceLines: [] } }))).toBe(false);
    expect(JSON.stringify(savedConfig('column', lined(undefined, { display: { color: 3 } })))).toBe(
      JSON.stringify(savedConfig('column', lined(undefined, { display: { color: 3, referenceLines: undefined, interpolation: undefined, dataLabels: undefined } }))),
    );
  });

  it('keeps the reference lines on a bar, a column and a line only', () => {
    for (const type of chartTypes) {
      const kept = savedConfig(type, lined([{ kind: 'value', value: 0.5 }], { seriesBy: type === 'heatmap' ? 'water_source' : undefined })).display?.referenceLines;
      expect(kept !== undefined, type).toBe(type === 'bar' || type === 'column' || type === 'line');
    }
  });

  it('keeps the steps on a line only, and the data labels on a bar, a column, a line and a pie only', () => {
    for (const type of chartTypes) {
      const saved = savedConfig(type, lined(undefined, { seriesBy: type === 'heatmap' ? 'water_source' : undefined, display: { interpolation: 'step', dataLabels: true } })).display;
      expect(saved?.interpolation !== undefined, type).toBe(type === 'line');
      expect(saved?.dataLabels !== undefined, type).toBe(type === 'bar' || type === 'column' || type === 'line' || type === 'pie');
    }
  });

  it('drops a mean and a median line when the chart has a series, and an overall line when it has no ratio', () => {
    const all: WidgetReferenceLine[] = [{ kind: 'mean' }, { kind: 'median' }, { kind: 'overall' }, { kind: 'value', value: 0.5 }];

    expect(savedConfig('column', lined(all, { seriesBy: 'water_source' })).display?.referenceLines?.map((l) => l.kind)).toEqual(['overall', 'value']);
    expect(savedConfig('column', lined(all, { ratio: null, measure: 'value' })).display?.referenceLines?.map((l) => l.kind)).toEqual(['mean', 'median', 'value']);
    expect(savedConfig('line', lined(all, { ratio: null, measure: 'value', seriesBy: 'water_source' })).display?.referenceLines?.map((l) => l.kind)).toEqual(['value']);
    expect('display' in savedConfig('column', lined([{ kind: 'mean' }], { seriesBy: 'water_source' }))).toBe(false);
  });

  it('saves only the fixed values of a stacked or a 100% layout, and no data labels there', () => {
    const all: WidgetReferenceLine[] = [{ kind: 'mean' }, { kind: 'overall' }, { kind: 'value', value: 0.5 }];
    for (const layout of ['stacked', 'percent'] as const) {
      const saved = savedConfig('bar', lined(all, { seriesBy: 'water_source', layout, ratio: null, measure: 'value', display: { dataLabels: true } })).display!;
      expect(saved.referenceLines).toEqual([{ kind: 'value', value: 0.5 }]);
      expect('dataLabels' in saved).toBe(false);
    }
    // A saved false never blocks a layout change, and a grouped layout takes data labels.
    expect(savedConfig('bar', lined(undefined, { seriesBy: 'water_source', layout: 'stacked', ratio: null, measure: 'value', display: { dataLabels: false } })).display).toEqual({ dataLabels: false });
    expect(savedConfig('bar', lined(undefined, { seriesBy: 'water_source', layout: 'grouped', display: { dataLabels: true } })).display).toEqual({ dataLabels: true });
    // A bar chart that lost its series is plain again: its old layout takes nothing away.
    expect(savedConfig('bar', lined(all, { layout: 'stacked' })).display?.referenceLines?.map((l) => l.kind)).toEqual(['mean', 'overall', 'value']);
  });

  it('saves a line as its kind, its value only on a value line, and its label unless it is blank', () => {
    const saved = savedConfig('column', lined([{ kind: 'mean', value: 3, label: '  ' }, { kind: 'value', label: ' Meta ', value: 0.5 }, { kind: 'overall', label: '' }])).display!;

    expect(saved.referenceLines).toEqual([{ kind: 'mean' }, { kind: 'value', value: 0.5, label: ' Meta ' }, { kind: 'overall' }]);
    expect(Object.keys(saved.referenceLines![1])).toEqual(['kind', 'value', 'label']);
  });

  it('saves a value line without its number as it is, for the API to refuse', () => {
    expect(savedConfig('column', lined([{ kind: 'value' }])).display?.referenceLines).toEqual([{ kind: 'value' }]);
  });
});

// What the widget form offers is what savedConfig keeps (add-chart-annotations, design D2, D8): one rule, read by both.
describe('the kinds of reference line and the data labels a widget takes', () => {
  const ratio = { numerator: 'numerator', denominator: 'denominator' };

  it('offers a mean, a median and a fixed value for a measure with no series, and an overall line too for a ratio', () => {
    expect(referenceKindsFor('column', { ratio: null })).toEqual(['mean', 'median', 'value']);
    expect(referenceKindsFor('bar', { ratio })).toEqual(['mean', 'median', 'overall', 'value']);
    expect(referenceKindsFor('line', { ratio })).toEqual(['mean', 'median', 'overall', 'value']);
  });

  it('offers the overall and fixed kinds with a series on a ratio, and the fixed value alone on a measure', () => {
    expect(referenceKindsFor('column', { ratio, seriesBy: 'water_source' })).toEqual(['overall', 'value']);
    expect(referenceKindsFor('line', { ratio: null, seriesBy: 'water_source' })).toEqual(['value']);
    expect(referenceKindsFor('bar', { ratio: null, seriesBy: 'water_source', layout: 'stacked' })).toEqual(['value']);
    expect(referenceKindsFor('bar', { ratio: null, seriesBy: 'water_source', layout: 'percent' })).toEqual(['value']);
    expect(referenceKindsFor('bar', { ratio: null, seriesBy: 'water_source', layout: 'grouped' })).toEqual(['value']);
  });

  it('offers none outside a bar, column or line', () => {
    for (const type of chartTypes.filter((t) => t !== 'bar' && t !== 'column' && t !== 'line')) {
      expect(referenceKindsFor(type, { ratio }), type).toEqual([]);
    }
  });

  it('offers data labels to a bar, column, line and pie, but not to a stacked or 100% layout', () => {
    for (const type of ['bar', 'column', 'line', 'pie'] as const) {
      expect(offersDataLabels(type, {}), type).toBe(true);
    }
    expect(offersDataLabels('column', { seriesBy: 'water_source', layout: 'grouped' })).toBe(true);
    expect(offersDataLabels('bar', { seriesBy: 'water_source', layout: 'stacked' })).toBe(false);
    expect(offersDataLabels('bar', { seriesBy: 'water_source', layout: 'percent' })).toBe(false);
    expect(offersDataLabels('line', { seriesBy: 'water_source', layout: 'stacked' })).toBe(true); // a line has no layout
    for (const type of chartTypes.filter((t) => !['bar', 'column', 'line', 'pie'].includes(t))) {
      expect(offersDataLabels(type, {}), type).toBe(false);
    }
  });
});

describe('queryFor with an own order of values (add-declared-category-order, design D7)', () => {
  /** A config with a widget's own order of groups and of series, as the form would hold it. */
  const ordered = (seriesBy: string | undefined): WidgetConfig => ({
    ...config(null),
    seriesBy,
    display: { color: 2, order: { group: ['Palmira', 'Cali'], series: ['Well', 'Piped'] } },
  });

  it('leaves the order out of the query of every kind, so moving a value never queries again', () => {
    for (const type of chartTypes) {
      const seriesBy = type === 'heatmap' || type === 'crosstab' || type === 'pyramid' || type === 'likert' ? 'water_source' : undefined;
      const query = queryFor(type, ordered(seriesBy));
      expect('display' in query, type).toBe(false);
      expect(JSON.stringify(query), type).not.toContain('order');
      expect(JSON.stringify(query), type).toBe(JSON.stringify(queryFor(type, { ...ordered(seriesBy), display: undefined })));
      expect(JSON.stringify(query), type).toBe(
        JSON.stringify(queryFor(type, { ...ordered(seriesBy), display: { order: { group: ['Buga'], series: ['River'] } } })),
      );
    }
  });
});

describe('savedConfig with an own order of values (add-declared-category-order, design D7)', () => {
  const own = { group: ['Palmira', 'Cali'], series: ['Well', 'Piped'] };
  const withOrder = (overrides: Partial<WidgetConfig> = {}): WidgetConfig => ({ ...config(null), seriesBy: 'water_source', display: { order: own }, ...overrides });

  it('keeps the order of groups on every kind that has groups, and the order of series on those that take one', () => {
    const groups = ['bar', 'column', 'line', 'pie', 'heatmap', 'pyramid', 'likert', 'crosstab', 'boxplot'] as const;
    const series = ['bar', 'column', 'line', 'heatmap', 'crosstab'] as const;
    for (const type of groups) {
      const saved = savedConfig(type, withOrder({ seriesBy: type === 'pie' || type === 'boxplot' ? undefined : 'water_source' }));
      expect(saved.display?.order?.group, type).toEqual(own.group);
      expect(saved.display?.order?.series, type).toEqual((series as readonly string[]).includes(type) ? own.series : undefined);
    }
  });

  it('drops the order of groups of a stat tile, a histogram and a map', () => {
    for (const type of ['stat', 'histogram', 'map'] as const) {
      expect('display' in savedConfig(type, withOrder({ seriesBy: undefined })), type).toBe(false);
    }
  });

  it('drops the order of series where there is no series column, and on a pie, a pyramid and a Likert widget', () => {
    expect(savedConfig('column', withOrder({ seriesBy: null })).display?.order).toEqual({ group: own.group });
    expect(savedConfig('column', withOrder({ seriesBy: '' })).display?.order).toEqual({ group: own.group });
    for (const type of ['pie', 'pyramid', 'likert'] as const) {
      expect(savedConfig(type, withOrder()).display?.order?.series, type).toBeUndefined();
    }
  });

  it('drops the order of groups under a sort, and keeps the order of series', () => {
    for (const type of ['bar', 'column'] as const) {
      expect(savedConfig(type, withOrder({ sort: byValue })).display?.order, type).toEqual({ series: own.series });
      expect(savedConfig(type, withOrder({ sort: byColumn })).display?.order, type).toEqual({ series: own.series });
    }
  });

  it('drops the order of boxes under an order by median, and keeps it with no sort', () => {
    expect(savedConfig('boxplot', withOrder({ seriesBy: undefined, sort: byValue })).display).toBeUndefined();
    expect(savedConfig('boxplot', withOrder({ seriesBy: undefined })).display?.order).toEqual({ group: own.group });
  });

  it('drops the order of groups without a group column', () => {
    expect(savedConfig('column', withOrder({ groupBy: null, seriesBy: undefined })).display).toBeUndefined();
    expect(savedConfig('boxplot', withOrder({ groupBy: null, seriesBy: undefined })).display).toBeUndefined();
  });

  it('leaves out an empty order, so a widget saved unchanged keeps its stored text', () => {
    expect(savedConfig('column', withOrder({ display: { order: { group: [], series: [] } } })).display).toBeUndefined();
    expect(savedConfig('column', withOrder({ display: { color: 2, order: { group: [] } }, seriesBy: undefined })).display).toEqual({ color: 2 });
  });
});
