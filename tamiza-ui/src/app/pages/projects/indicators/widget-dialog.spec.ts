import { MapStub, stubMap } from '../../../../testing/leaflet';
import { details, projectId, settle, typeInto } from '../../../../testing/project-harness';
import { BasemapList } from '../../../core/basemaps.service';
import { ChartType, DashboardColors, DashboardFilter, IndicatorColumns, IndicatorEntry, Widget, WidgetConfig, chartTypes } from '../../../core/projects/indicators.service';
import { DashboardEntry } from '../../../core/projects/projects.service';
import { valueSearchDelayMs } from '../data-filter-dialog';
import { encodeFilters } from '../data-view';
import {
  Context,
  OpenAt,
  all,
  catalog,
  cell,
  chooseInWidgetMenu,
  clearOverlay,
  config,
  indicator,
  indicatorsUrl,
  isColumns,
  isPatch,
  isQuery,
  open,
  overlay,
  result,
  row,
  seriesResult,
  text,
  wait,
  widget,
  widgetUrl,
  widgetsUrl,
} from './indicators-testing';
import { loadLeaflet } from './map-engine';
import { previewDelayMs } from './widget-dialog';

const dialog = () => overlay().querySelector<HTMLElement>('[data-testid="widget-dialog"]')!;
const field = <T extends HTMLElement>(id: string) => dialog().querySelector<T>(`[data-testid="${id}"]`)!;
const fields = (id: string) => Array.from(dialog().querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));
const optionsOf = (id: string) => Array.from(field<HTMLSelectElement>(id).options).map((o) => o.value);
const optionTexts = (id: string) => Array.from(field<HTMLSelectElement>(id).options).map((o) => text(o));
const errorOf = (id: string) => text(dialog().querySelector(`#${id}-error`));

interface Setup {
  existing?: Widget | null;
  /** The table of the indicator the dialog opens on, or of any indicator by name. */
  columns?: IndicatorColumns | ((name: string) => IndicatorColumns);
  indicators?: IndicatorEntry[];
  /** The project's dashboards, when they are not just "Indicators" (`d1`, where the dialog is opened). */
  dashboards?: DashboardEntry[];
  /** The open dashboard's filters, and the query string of its address (`f=…`). */
  filters?: DashboardFilter[];
  query?: string;
  /** The open dashboard's palette and colors kept by value, when it has any. */
  colors?: DashboardColors;
  /** The interface language; English unless said. */
  lang?: OpenAt['lang'];
}

/** Opens the dialog to add a widget, or to edit `existing`, and answers the request for the columns of the indicator it starts on. */
async function openDialog({ existing = null, columns = catalog(), indicators, dashboards, filters, query, colors, lang }: Setup = {}) {
  const context = await open('analyst', existing ? [existing] : [], existing ? result([row('Cali', 75)]) : null, indicators ? { indicators } : {}, {
    ...(dashboards ? { dashboards } : {}),
    ...(filters ? { filters } : {}),
    ...(query ? { query } : {}),
    ...(colors ? { colors } : {}),
    ...(lang ? { lang } : {}),
  });
  if (existing) {
    // Edit is an item of the widget's menu.
    await chooseInWidgetMenu(context, 'widget-edit');
  } else {
    context.root.querySelector<HTMLElement>('[data-testid="widget-add"]')!.click();
  }
  await settle(context.harness);
  await answerColumns(context, columns);
  return context;
}

async function answerColumns(context: Context, columns: IndicatorColumns | ((name: string) => IndicatorColumns)) {
  const request = context.http.expectOne(isColumns);
  const name = request.request.url.split('/').at(-2)!;
  request.flush(typeof columns === 'function' ? columns(name) : columns);
  await settle(context.harness);
  return name;
}

async function change(context: Context, target: HTMLElement, value: string) {
  typeInto(target as HTMLInputElement, value);
  await settle(context.harness);
}

async function click(context: Context, target: HTMLElement) {
  target.click();
  await settle(context.harness);
}

/** Lets the debounce of the preview pass and answers the query it then sends. */
async function previewAnswer(context: Context, answer: ReturnType<typeof result> | { status: number; body: object }) {
  await wait(previewDelayMs + 100);
  await settle(context.harness);
  const requests = context.http.match(isQuery).filter((r) => !r.cancelled);
  expect(requests).toHaveLength(1);
  if ('status' in answer) {
    requests[0].flush(answer.body, { status: answer.status, statusText: 'Bad Request' });
  } else {
    requests[0].flush(answer);
  }
  await settle(context.harness);
  return requests[0].request.body as { config: WidgetConfig; chartType: string };
}

const unpublishedColumns = catalog({ published: false, columns: [], numeric: [], defaults: { measure: null, groupBy: null } });
const isCreate = (r: { url: string; method: string }) => r.method === 'POST' && r.url === widgetsUrl;

describe('the widget dialog', () => {
  afterEach(clearOverlay);

  describe('the indicator', () => {
    it('lists every indicator with its label and name, marks those without a table, and starts on the first that has published', async () => {
      const indicators = [indicator('attendance', { published: false, publishedAt: null, runId: null, rows: null }), indicator('coverage'), indicator('target')];
      const context = await openDialog({ indicators });

      expect(optionTexts('dialog-indicator')).toEqual(['Choose an indicator', 'Attendance (attendance) · not published yet', 'Coverage (coverage)', 'Target (target)']);
      expect(field<HTMLSelectElement>('dialog-indicator').value).toBe('coverage');
      expect(context.http.match(isColumns)).toHaveLength(0); // the columns were read once, for coverage
    });

    it('reads the columns of the indicator it starts on', async () => {
      const context = await open('analyst', [], null);
      context.root.querySelector<HTMLElement>('[data-testid="widget-add"]')!.click();
      await settle(context.harness);

      const request = context.http.expectOne(isColumns);
      expect(request.request.url).toBe(`${indicatorsUrl}/attendance/columns`);
      request.flush(catalog());
    });

    it('shows the indicator\'s format and unit next to the measure', async () => {
      const context = await openDialog();
      expect(text(field('dialog-format-note'))).toBe('Values are shown as: Count.');

      await change(context, field('dialog-indicator'), 'households_visited');
      await answerColumns(context, catalog());

      expect(text(field('dialog-format-note'))).toBe('Values are shown as: Count, with the unit households.');
    });

    it('reads the columns of another indicator when it is chosen, and drops what was chosen from the old table', async () => {
      const context = await openDialog();
      await change(context, field('dialog-measure'), 'numerator');
      await change(context, field('dialog-group-by'), 'round');
      expect(field<HTMLSelectElement>('dialog-measure').value).toBe('numerator');

      await change(context, field('dialog-indicator'), 'target');
      const request = context.http.expectOne(isColumns);
      expect(request.request.url).toBe(`${indicatorsUrl}/target/columns`);
      request.flush(catalog());
      await settle(context.harness);

      expect(field<HTMLSelectElement>('dialog-measure').value).toBe('');
      expect(field<HTMLSelectElement>('dialog-group-by').value).toBe('municipality'); // the new table's default, not 'round'
      await change(context, field('dialog-indicator'), 'target'); // nothing is read again when it is chosen again
      expect(context.http.match(isColumns)).toHaveLength(0);
    });

    it('starts the new indicator\'s widget on its own default group column and measure', async () => {
      const context = await openDialog();
      await change(context, field('dialog-measure'), 'numerator');

      await change(context, field('dialog-indicator'), 'target');
      await answerColumns(
        context,
        catalog({
          columns: [
            { name: 'sex', type: 'text', label: null },
            { name: 'count', type: 'integer', label: null },
          ],
          numeric: ['count'],
          defaults: { measure: 'count', groupBy: 'sex' },
        }),
      );

      expect(field<HTMLSelectElement>('dialog-group-by').value).toBe('sex');
      expect(field<HTMLSelectElement>('dialog-measure').value).toBe('');
      expect(optionTexts('dialog-measure')).toEqual(['Default (count)', 'count']);
      expect(optionsOf('dialog-group-by')).toEqual(['', 'sex', 'count']);
    });

    it('warns that an indicator without a table cannot list its columns, and keeps what the widget saved', async () => {
      const stale = widget('w1', { config: config({ indicator: 'removed_one', measure: 'old_measure', groupBy: 'old_group' }) });
      const context = await openDialog({ existing: stale, columns: unpublishedColumns });

      expect(optionTexts('dialog-indicator')[1]).toBe('removed_one (removed_one) · not published yet');
      expect(field<HTMLSelectElement>('dialog-indicator').value).toBe('removed_one');
      expect(text(field('dialog-unpublished'))).toBe('removed_one has not published yet, so its columns cannot be listed. Run the scripts first.');
      // What is saved stays visible and can be kept.
      expect(field<HTMLSelectElement>('dialog-measure').value).toBe('old_measure');
      expect(field<HTMLSelectElement>('dialog-group-by').value).toBe('old_group');
      expect(text(field('dialog-filters'))).toContain('Filters can be added once the indicator is published.');
      expect(field('filter-add')).toBeNull();
      await click(context, field('dialog-save'));
      const request = context.http.expectOne((r) => r.method === 'PATCH');
      expect(request.request.body.config).toMatchObject({ indicator: 'removed_one', measure: 'old_measure', groupBy: 'old_group' });
    });

    it('does not ask for rows while the indicator has no table, and says so', async () => {
      const unpublished = [indicator('coverage'), indicator('attendance', { published: false, publishedAt: null, runId: null, rows: null })];
      const context = await openDialog({ indicators: unpublished, columns: (name) => (name === 'attendance' ? unpublishedColumns : catalog()) });
      await previewAnswer(context, result([row('Cali', 1)])); // coverage draws

      await change(context, field('dialog-indicator'), 'attendance');
      await answerColumns(context, unpublishedColumns);
      await wait(previewDelayMs + 100);

      expect(context.http.match(isQuery)).toHaveLength(0);
      expect(text(field('dialog-preview'))).toContain('Attendance has not published yet.');
    });
  });

  describe('a measure or a ratio', () => {
    it('plots the table\'s default measure unless another numeric column is chosen', async () => {
      const context = await openDialog();

      expect(optionTexts('dialog-measure')).toEqual(['Default (value)', 'value', 'numerator', 'denominator', 'round']);
      expect(field<HTMLSelectElement>('dialog-measure').value).toBe('');
      await change(context, field('dialog-title'), 'Coverage');
      await click(context, field('dialog-save'));
      expect(context.http.expectOne(isCreate).request.body.config).toMatchObject({ measure: null, ratio: null });
    });

    it('sends the measure that was chosen', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Coverage');
      await change(context, field('dialog-measure'), 'numerator');
      await click(context, field('dialog-save'));

      expect(context.http.expectOne(isCreate).request.body.config).toMatchObject({ measure: 'numerator', ratio: null });
    });

    it('plots a ratio of two numeric columns, starting on the conventional ones', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Rate');
      await change(context, field('dialog-mode'), 'ratio');

      expect(field('dialog-measure')).toBeNull();
      expect(field<HTMLSelectElement>('dialog-numerator').value).toBe('numerator');
      expect(field<HTMLSelectElement>('dialog-denominator').value).toBe('denominator');
      expect(optionsOf('dialog-numerator')).toEqual(['', 'value', 'numerator', 'denominator', 'round']);
      await click(context, field('dialog-save'));

      expect(context.http.expectOne(isCreate).request.body.config).toMatchObject({ measure: null, ratio: { numerator: 'numerator', denominator: 'denominator' } });
    });

    it('requires both columns of a ratio', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Rate');
      await change(context, field('dialog-mode'), 'ratio');
      await change(context, field('dialog-denominator'), '');

      await click(context, field('dialog-save'));

      expect(errorOf('widgetNumerator')).toBe('Choose the numerator and the denominator.');
      context.http.expectNone(isCreate);
    });

    it('opens a ratio widget as a ratio, and drops the ratio when a measure is chosen instead', async () => {
      const rate = widget('w1', { config: config({ indicator: 'piped_water_rate', ratio: { numerator: 'cases', denominator: 'population' } }) });
      const context = await openDialog({ existing: rate, columns: catalog({ numeric: ['cases', 'population', 'value'] }) });

      expect(field<HTMLSelectElement>('dialog-mode').value).toBe('ratio');
      // The saved columns are kept in the choices even when the table lacks them.
      expect(field<HTMLSelectElement>('dialog-numerator').value).toBe('cases');
      expect(field<HTMLSelectElement>('dialog-denominator').value).toBe('population');

      await change(context, field('dialog-mode'), 'measure');
      await change(context, field('dialog-measure'), 'value');
      await click(context, field('dialog-save'));

      expect(context.http.expectOne((r) => r.method === 'PATCH').request.body.config).toMatchObject({ measure: 'value', ratio: null });
    });
  });

  describe('the fields of each chart type', () => {
    it('requires a column to group a column chart by (the dialog requiring a group column)', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Coverage by municipality');
      expect(field<HTMLSelectElement>('dialog-group-by').value).toBe('municipality'); // the table's default

      await change(context, field('dialog-group-by'), '');
      await click(context, field('dialog-save'));

      expect(errorOf('widgetGroupBy')).toBe('Choose the column to group by.');
      expect(optionTexts('dialog-group-by')[0]).toBe('Choose a column');
      context.http.expectNone(isCreate);

      await change(context, field('dialog-group-by'), 'round');
      expect(errorOf('widgetGroupBy')).toBe('');
      await click(context, field('dialog-save'));
      expect(context.http.expectOne(isCreate).request.body.config.groupBy).toBe('round');
    });

    it.each(['bar', 'line', 'pie', 'map'])('requires a column to group a %s by', async (type) => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), type);
      await change(context, field('dialog-title'), 'Chart');
      await change(context, field('dialog-group-by'), '');
      if (type === 'map') await change(context, field('dialog-geometry'), 'geometry');

      await click(context, field('dialog-save'));

      expect(errorOf('widgetGroupBy')).toBe('Choose the column to group by.');
      context.http.expectNone(isCreate);
    });

    it('has no group column for a stat tile, which is one figure', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'stat');
      await change(context, field('dialog-title'), 'Total');

      expect(field('dialog-group-by')).toBeNull();
      await click(context, field('dialog-save'));
      expect(context.http.expectOne(isCreate).request.body.config.groupBy).toBeNull();
    });

    it('lets a histogram count the rows as they are, or the groups of a column', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'histogram');
      await change(context, field('dialog-title'), 'Distribution');

      expect(optionTexts('dialog-group-by')[0]).toBe('(none)');
      await change(context, field('dialog-group-by'), '');
      await click(context, field('dialog-save'));
      expect(context.http.expectOne(isCreate).request.body.config.groupBy).toBeNull();
    });

    it('asks for the geometry column of a map only, among the text columns, and requires it', async () => {
      const context = await openDialog();
      expect(field('dialog-geometry')).toBeNull();
      expect(field('dialog-buckets')).toBeNull();

      await change(context, field('dialog-chart-type'), 'map');
      expect(optionsOf('dialog-geometry')).toEqual(['', 'municipality', 'geometry']);

      await change(context, field('dialog-title'), 'Coverage map');
      await click(context, field('dialog-save'));
      expect(errorOf('widgetGeometry')).toBe('Choose the geometry column of the map.');
      context.http.expectNone(isCreate);
    });

    it('asks for the number of buckets of a histogram only, between 1 and 100', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'histogram');
      await change(context, field('dialog-title'), 'Distribution');

      await change(context, field('dialog-buckets'), '101');
      await click(context, field('dialog-save'));
      expect(errorOf('widgetBuckets')).toBe('Enter a whole number from 1 to 100, or leave it empty.');

      await change(context, field('dialog-buckets'), '2.5');
      expect(errorOf('widgetBuckets')).toContain('whole number');
    });

    it('requires a title and an indicator', async () => {
      const context = await openDialog();

      await click(context, field('dialog-save'));
      expect(errorOf('widgetTitle')).toBe('Enter a title.');
      context.http.expectNone(isCreate);

      await change(context, field('dialog-title'), 'Coverage');
      await change(context, field('dialog-indicator'), '');
      await click(context, field('dialog-save'));
      expect(errorOf('widgetIndicator')).toBe('Choose an indicator.');
      context.http.expectNone(isCreate);
    });
  });

  describe('saving', () => {
    async function fill(context: Context) {
      await change(context, field('dialog-title'), '  Attendance by round  ');
      await change(context, field('dialog-chart-type'), 'line');
      await change(context, field('dialog-indicator'), 'coverage');
      await answerColumns(context, catalog());
      await change(context, field('dialog-group-by'), 'round');
      await change(context, field('dialog-measure'), 'numerator');
    }

    it('creates the widget from the form, without a layout so the server places it', async () => {
      const context = await openDialog();
      await fill(context);

      await click(context, field('dialog-save'));

      const request = context.http.expectOne(isCreate);
      expect(request.request.body).toEqual({
        title: 'Attendance by round',
        chartType: 'line',
        config: {
          indicator: 'coverage',
          measure: 'numerator',
          ratio: null,
          groupBy: 'round',
          filters: [],
          geometryColumn: null,
          buckets: null,
          sort: null,
          tooltip: [],
          basemap: null,
          ignoredDashboardFilters: [],
        },
      });
      request.flush(widget('w9', { title: 'Attendance by round', chartType: 'line' }), { status: 201, statusText: 'Created' });
      await settle(context.harness);
      expect(overlay().querySelector('[data-testid="widget-dialog"]')).toBeNull();
    });

    it('sends the geometry column of a map and the buckets of a histogram, and neither otherwise', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Map');
      await change(context, field('dialog-chart-type'), 'map');
      await change(context, field('dialog-geometry'), 'geometry');
      await change(context, field('dialog-chart-type'), 'histogram');
      await change(context, field('dialog-buckets'), '25');
      await change(context, field('dialog-chart-type'), 'map');
      await click(context, field('dialog-save'));

      const request = context.http.expectOne(isCreate);
      expect(request.request.body.config.geometryColumn).toBe('geometry');
      expect(request.request.body.config.buckets).toBeNull();
      request.flush(widget('w9'), { status: 201, statusText: 'Created' });
    });

    it('shows what the server refuses next to the field, and keeps the dialog open', async () => {
      const context = await openDialog();
      await fill(context);
      await click(context, field('dialog-save'));

      context.http
        .expectOne(isCreate)
        .flush({ title: 'x', errors: { 'config.groupBy': ["The table has no column 'round'."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(errorOf('widgetGroupBy')).toBe("The table has no column 'round'.");
      expect(field<HTMLButtonElement>('dialog-save').disabled).toBe(false);
    });

    it('shows the refusal of a measure or of the columns of a ratio where they are chosen', async () => {
      const context = await openDialog();
      await fill(context);
      await click(context, field('dialog-save'));
      context.http
        .expectOne(isCreate)
        .flush({ title: 'x', errors: { 'config.measure': ["Column 'numerator' is not a number, so it cannot be plotted."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);
      expect(errorOf('widgetMeasure')).toContain('is not a number');

      await change(context, field('dialog-mode'), 'ratio');
      await click(context, field('dialog-save'));
      context.http
        .expectOne(isCreate)
        .flush({ title: 'x', errors: { 'config.ratio.numerator': ["The table has no column 'numerator'."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);
      expect(errorOf('widgetNumerator')).toBe("The table has no column 'numerator'.");
    });

    it('shows a refusal that names no field in an alert, such as the widget limit', async () => {
      const context = await openDialog();
      await fill(context);
      await click(context, field('dialog-save'));

      context.http
        .expectOne(isCreate)
        .flush({ title: 'x', errors: { widgets: ['A project can have at most 50 widgets.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(field('dialog-error'))).toBe('A project can have at most 50 widgets.');
      expect(field('dialog-error').querySelector('[lang="en"]')).not.toBeNull(); // written by the API
    });

    it('closes without saving on Cancel', async () => {
      const context = await openDialog();

      await click(context, field('dialog-cancel'));

      expect(overlay().querySelector('[data-testid="widget-dialog"]')).toBeNull();
      context.http.expectNone(isCreate);
    });
  });

  describe('live preview', () => {
    it('says the form is incomplete until the required fields are chosen, and asks for nothing', async () => {
      const context = await openDialog();
      await change(context, field('dialog-group-by'), '');

      expect(text(field('view-idle'))).toBe('Complete the form to see a preview.');
      await wait(previewDelayMs + 100);
      expect(context.http.match(isQuery)).toHaveLength(0);
    });

    it('asks for the rows once the draft stops changing, with the chart type, and draws them', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'bar');
      await change(context, field('dialog-measure'), 'numerator');
      expect(context.http.match(isQuery)).toHaveLength(0);

      const body = await previewAnswer(context, result([row('Cali', 75), row('Pasto', 40)]));

      expect(body).toEqual({ config: config({ indicator: 'attendance', measure: 'numerator' }), chartType: 'bar' });
      expect(field('dialog-preview').querySelectorAll('[data-testid="chart-mark"]')).toHaveLength(2);
    });

    it('offers no Export in the preview of a draft (Export button)', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'bar');

      await previewAnswer(context, result([row('Cali', 75)]));

      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).not.toBeNull();
      expect(field('dialog-preview').querySelector('[data-testid="widget-menu"]')).toBeNull();
    });

    it('shows the server\'s refusal of an invalid draft in place of the earlier chart', async () => {
      const context = await openDialog();
      await previewAnswer(context, result([row('Cali', 75)]));
      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).not.toBeNull();

      await change(context, field('dialog-measure'), 'round');
      // The old chart goes at once; the draft is not drawn as if it were the saved one.
      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).toBeNull();
      await previewAnswer(context, { status: 400, body: { title: 'x', errors: { measure: ["Column 'round' is not a number."] } } });

      expect(text(field('dialog-preview').querySelector('[data-testid="view-error"]'))).toBe("Column 'round' is not a number.");
      expect(field('dialog-preview').querySelector('[data-testid="view-error"] [lang="en"]')).not.toBeNull();
      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).toBeNull();
    });

    it('names a column the table lacks instead of drawing', async () => {
      const context = await openDialog();
      await previewAnswer(context, result([], { missing: { indicator: false, columns: ['municipality'] } }));

      expect(text(field('dialog-preview').querySelector('[data-testid="missing-columns"]'))).toContain('municipality');
    });

    it('shows only the latest draft when the form changes while a request is out', async () => {
      const context = await openDialog();
      await wait(previewDelayMs + 100);
      await settle(context.harness);
      const first = context.http.match(isQuery);
      expect(first).toHaveLength(1);

      await change(context, field('dialog-measure'), 'numerator');
      expect(first[0].cancelled).toBe(true);
      const body = await previewAnswer(context, result([row('Cali', 5)]));
      expect(body.config.measure).toBe('numerator');
    });
  });

  describe('filters', () => {
    const filterDialog = () => overlay().querySelector<HTMLElement>('[data-testid="filter-dialog"]')!;
    const inFilter = <T extends HTMLElement>(id: string) => filterDialog().querySelector<T>(`[data-testid="${id}"]`)!;

    async function addRoundFilter(context: Context) {
      await change(context, field('filter-column'), 'round');
      await click(context, field('filter-add'));
      expect(filterDialog()).not.toBeNull();
      await change(context, inFilter('filter-min'), '2');
      await change(context, inFilter('filter-max'), '3');
      await click(context, inFilter('filter-apply'));
    }

    async function settlePreview(context: Context) {
      await wait(previewDelayMs + 100);
      context.http.match(isQuery).forEach((r) => r.flush(result([])));
      await settle(context.harness);
    }

    it('offers every column of the table to filter on', async () => {
      await openDialog();

      expect(optionsOf('filter-column')).toEqual(['municipality', 'value', 'numerator', 'denominator', 'round', 'geometry']);
    });

    it('adds a filter built in the clean data filter dialog, checked by the server with the rest of the draft', async () => {
      const context = await openDialog();
      await settlePreview(context);

      await addRoundFilter(context);
      const check = context.http.expectOne(isQuery);
      expect(check.request.body.config.filters).toEqual([{ column: 'round', op: 'range', min: '2', max: '3' }]);
      expect(check.request.body.config.indicator).toBe('attendance');
      expect(check.request.body.chartType).toBe('column');
      check.flush(result([row('Cali', 5)]));
      await settle(context.harness);

      expect(filterDialog()).toBeNull();
      expect(fields('dialog-filter-chip').map(text)).toEqual(['round from 2 to 3']);
    });

    it('shows the server\'s refusal of a filter inside the filter dialog', async () => {
      const context = await openDialog();
      await settlePreview(context);

      await addRoundFilter(context);
      context.http.expectOne(isQuery).flush({ title: 'x', errors: { 'filters[0]': ['The range is reversed.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(filterDialog().querySelector('[data-testid="field-error"]'))).toBe('The range is reversed.');
      expect(fields('dialog-filter-chip')).toHaveLength(0);
    });

    it('sends the filters with the widget, and lets them be edited and removed', async () => {
      const withFilter = widget('w1', { config: config({ filters: [{ column: 'round', op: 'range', min: '2' }] }) });
      const context = await openDialog({ existing: withFilter });
      context.http.match(isQuery).forEach((r) => r.flush(result([])));
      expect(fields('dialog-filter-chip').map(text)).toEqual(['round from 2']);

      await click(context, field('dialog-filter-edit'));
      expect(inFilter<HTMLInputElement>('filter-min').value).toBe('2');
      await click(context, inFilter('filter-remove'));
      expect(fields('dialog-filter-chip')).toHaveLength(0);

      await click(context, field('dialog-save'));
      const request = context.http.expectOne((r) => r.method === 'PATCH');
      expect(request.request.body.config.filters).toEqual([]);
      request.flush(widget('w1'));
    });

    it('drops the filters of the old table when another indicator is chosen', async () => {
      const withFilter = widget('w1', { config: config({ filters: [{ column: 'round', op: 'range', min: '2' }] }) });
      const context = await openDialog({ existing: withFilter });
      context.http.match(isQuery).forEach((r) => r.flush(result([])));
      expect(fields('dialog-filter-chip')).toHaveLength(1);

      await change(context, field('dialog-indicator'), 'target');
      await answerColumns(context, catalog());

      expect(fields('dialog-filter-chip')).toHaveLength(0);
    });

    it('lets the values of a text column be typed, since the table cannot list them', async () => {
      const context = await openDialog();
      await change(context, field('filter-column'), 'municipality');
      await click(context, field('filter-add'));
      await change(context, inFilter('filter-op'), 'in');
      await change(context, inFilter('filter-search'), 'Cali');
      await wait(valueSearchDelayMs + 50);
      await settle(context.harness);

      const offered = Array.from(filterDialog().querySelectorAll('[data-testid="filter-value-list"] li')).map(text);
      expect(offered).toEqual(['Cali']);
    });
  });

  describe('the restructured dialog', () => {
    it('keeps the filters in a closed disclosure for a new widget, with their count in the heading', async () => {
      await openDialog();
      const disclosure = field('dialog-filters');
      expect(text(disclosure.querySelector('summary h3'))).toBe('Filters (0)');
      expect((disclosure.querySelector('details') as HTMLDetailsElement).open).toBe(false);
    });

    it('opens the filters of a widget that already has some', async () => {
      const withFilter = widget('w1', { config: config({ filters: [{ column: 'round', op: 'range', min: '2' }] }) });
      await openDialog({ existing: withFilter });
      const disclosure = field('dialog-filters');
      expect(text(disclosure.querySelector('summary h3'))).toBe('Filters (1)');
      expect((disclosure.querySelector('details') as HTMLDetailsElement).open).toBe(true);
    });

    it('writes the indicator\'s description and its format in one line', async () => {
      await openDialog({ indicators: [indicator('attendance', { description: 'Pupils present', unit: 'pupils' })] });
      expect(text(field('dialog-format-note'))).toBe('Pupils present · Values are shown as: Count, with the unit pupils.');
      expect(fields('dialog-format-note')).toHaveLength(1);
    });

    it('does not offer a ratio when the table has fewer than two numeric columns', async () => {
      await openDialog({ columns: catalog({ numeric: ['value'] }) });
      expect(field('dialog-mode')).toBeNull();
      expect(field('dialog-measure')).not.toBeNull();
    });

    it('offers a ratio when the table has two numeric columns', async () => {
      await openDialog();
      expect(field('dialog-mode')).not.toBeNull();
    });

    it('makes the preview of a single figure shorter than the preview of a chart', async () => {
      const context = await openDialog();
      expect(field('dialog-preview').className).toContain('h-72');
      await change(context, field('dialog-chart-type'), 'stat');
      expect(field('dialog-preview').className).toContain('h-40');
    });

    it('is wider from lg, with the preview beside the fields', async () => {
      await openDialog();
      expect(dialog().className).toContain('lg:w-[min(64rem');
      expect(field('dialog-preview').closest('section')!.className).toContain('lg:sticky');
    });
  });

  describe('the order of bars and columns', () => {
    const typed = catalog({
      columns: [...catalog().columns, { name: 'visited_on', type: 'date', label: null }, { name: 'urban', type: 'boolean', label: null }],
    });
    const sorted = (overrides: Partial<NonNullable<WidgetConfig['sort']>> = {}) =>
      ({ by: 'column', column: 'round', compareAs: 'number', direction: 'asc', ...overrides }) as NonNullable<WidgetConfig['sort']>;

    async function saveNew(context: Context) {
      await change(context, field('dialog-title'), 'Ordered');
      await click(context, field('dialog-save'));
      return context.http.expectOne(isCreate).request.body.config as WidgetConfig;
    }

    it('is offered for a bar and a column chart only', async () => {
      const context = await openDialog();
      expect(field('dialog-sort-by')).not.toBeNull(); // a new widget is a column chart

      await change(context, field('dialog-chart-type'), 'bar');
      expect(field('dialog-sort-by')).not.toBeNull();

      for (const type of ['line', 'pie', 'stat', 'histogram', 'map']) {
        await change(context, field('dialog-chart-type'), type);
        expect(field('dialog-sort-by')).toBeNull();
      }
    });

    it('offers the default order, the plotted value and every column, and starts on the default order', async () => {
      await openDialog({ columns: typed });

      expect(optionsOf('dialog-sort-by')).toEqual([
        '', 'value', 'column:municipality', 'column:value', 'column:numerator', 'column:denominator', 'column:round', 'column:geometry', 'column:visited_on', 'column:urban',
      ]);
      expect(optionTexts('dialog-sort-by').slice(0, 2)).toEqual(['Default order', 'Plotted value']);
      expect(field<HTMLSelectElement>('dialog-sort-by').value).toBe('');
      expect(field('dialog-sort-direction')).toBeNull();
      expect(field('dialog-sort-compare-as')).toBeNull();
    });

    it('sends no order when none is chosen', async () => {
      const context = await openDialog();

      expect((await saveNew(context)).sort).toBeNull();
    });

    it('sorts descending when the plotted value is chosen, ascending when a column is, and keeps the direction between columns', async () => {
      const context = await openDialog({ columns: typed });

      await change(context, field('dialog-sort-by'), 'value');
      expect(field<HTMLSelectElement>('dialog-sort-direction').value).toBe('desc');
      expect(optionTexts('dialog-sort-direction')).toEqual(['Ascending', 'Descending']);

      await change(context, field('dialog-sort-by'), 'column:round');
      expect(field<HTMLSelectElement>('dialog-sort-direction').value).toBe('asc');

      await change(context, field('dialog-sort-direction'), 'desc');
      await change(context, field('dialog-sort-by'), 'column:municipality');
      expect(field<HTMLSelectElement>('dialog-sort-direction').value).toBe('desc');
    });

    it('offers to compare a text, integer or decimal column as text or as a number, preselected from its type', async () => {
      const context = await openDialog({ columns: typed });

      await change(context, field('dialog-sort-by'), 'column:municipality');
      expect(field<HTMLSelectElement>('dialog-sort-compare-as').value).toBe('text');
      expect(optionTexts('dialog-sort-compare-as')).toEqual(['Text (10 before 2)', 'Number (2 before 10)']);

      await change(context, field('dialog-sort-by'), 'column:round');
      expect(field<HTMLSelectElement>('dialog-sort-compare-as').value).toBe('number');
      expect(dialog().textContent).toContain('Values that are not numbers go last.');

      await change(context, field('dialog-sort-by'), 'column:value');
      expect(field<HTMLSelectElement>('dialog-sort-compare-as').value).toBe('number');
    });

    it('does not offer the comparison for the plotted value or for a date or true/false column', async () => {
      const context = await openDialog({ columns: typed });

      await change(context, field('dialog-sort-by'), 'value');
      expect(field('dialog-sort-compare-as')).toBeNull();
      await change(context, field('dialog-sort-by'), 'column:visited_on');
      expect(field('dialog-sort-compare-as')).toBeNull();
      await change(context, field('dialog-sort-by'), 'column:urban');
      expect(field('dialog-sort-compare-as')).toBeNull();
    });

    it('saves the order chosen', async () => {
      const context = await openDialog({ columns: typed });
      await change(context, field('dialog-sort-by'), 'column:municipality');
      await change(context, field('dialog-sort-compare-as'), 'number');
      await change(context, field('dialog-sort-direction'), 'desc');

      expect((await saveNew(context)).sort).toEqual({ by: 'column', column: 'municipality', compareAs: 'number', direction: 'desc' });
    });

    it('saves the plotted value without a column or a comparison', async () => {
      const context = await openDialog({ columns: typed });
      await change(context, field('dialog-sort-by'), 'value');

      expect((await saveNew(context)).sort).toEqual({ by: 'value', column: null, compareAs: null, direction: 'desc' });
    });

    it('sends no comparison after moving from a text column to a date column', async () => {
      const context = await openDialog({ columns: typed });
      await change(context, field('dialog-sort-by'), 'column:municipality');
      await change(context, field('dialog-sort-compare-as'), 'number');
      await change(context, field('dialog-sort-by'), 'column:visited_on');

      expect((await saveNew(context)).sort).toEqual({ by: 'column', column: 'visited_on', compareAs: null, direction: 'asc' });
    });

    it('drops the order from the query of a chart type that has none', async () => {
      const context = await openDialog({ columns: typed });
      await change(context, field('dialog-sort-by'), 'value');
      await change(context, field('dialog-chart-type'), 'line');

      expect((await saveNew(context)).sort).toBeNull();
    });

    it('previews the order', async () => {
      const context = await openDialog({ columns: typed });
      await change(context, field('dialog-sort-by'), 'column:round');

      const query = await previewAnswer(context, result([row('Cali', 75)]));

      expect(query.config.sort).toEqual(sorted());
    });

    it('shows a saved order in its three controls', async () => {
      await openDialog({ existing: widget('w1', { config: config({ sort: sorted({ column: 'municipality', compareAs: 'number', direction: 'desc' }) }) }), columns: typed });

      expect(field<HTMLSelectElement>('dialog-sort-by').value).toBe('column:municipality');
      expect(field<HTMLSelectElement>('dialog-sort-direction').value).toBe('desc');
      expect(field<HTMLSelectElement>('dialog-sort-compare-as').value).toBe('number');
    });

    it('shows a saved order by the plotted value', async () => {
      await openDialog({ existing: widget('w1', { config: config({ sort: { by: 'value', column: null, compareAs: null, direction: 'desc' } }) }), columns: typed });

      expect(field<HTMLSelectElement>('dialog-sort-by').value).toBe('value');
      expect(field<HTMLSelectElement>('dialog-sort-direction').value).toBe('desc');
      expect(field('dialog-sort-compare-as')).toBeNull();
    });

    it('shows the type\'s own comparison for a saved order that left it open, and sends it', async () => {
      const context = await openDialog({ existing: widget('w1', { config: config({ sort: sorted({ compareAs: null }) }) }), columns: typed });

      expect(field<HTMLSelectElement>('dialog-sort-compare-as').value).toBe('number');
      await click(context, field('dialog-save'));
      expect(context.http.expectOne(isPatch).request.body.config.sort.compareAs).toBe('number');
    });

    it('keeps a saved sort column the table no longer has, in the order list only', async () => {
      const context = await openDialog({ existing: widget('w1', { config: config({ sort: sorted({ column: 'gone', compareAs: 'text' }) }) }), columns: typed });

      expect(optionsOf('dialog-sort-by')[2]).toBe('column:gone');
      expect(field<HTMLSelectElement>('dialog-sort-by').value).toBe('column:gone');
      expect(optionsOf('dialog-group-by')).not.toContain('gone');
      expect(optionsOf('filter-column')).not.toContain('gone');
      // Its type is unknown, so what was saved is sent unchanged.
      await click(context, field('dialog-save'));
      expect(context.http.expectOne(isPatch).request.body.config.sort).toEqual(sorted({ column: 'gone', compareAs: 'text' }));
    });

    it('sends no comparison for a saved order whose column has become a date column', async () => {
      const context = await openDialog({ existing: widget('w1', { config: config({ sort: sorted({ column: 'visited_on', compareAs: 'number' }) }) }), columns: typed });

      expect(field('dialog-sort-compare-as')).toBeNull();
      await click(context, field('dialog-save'));
      expect(context.http.expectOne(isPatch).request.body.config.sort.compareAs).toBeNull();
    });

    it('starts again from the default order when another indicator is chosen', async () => {
      const context = await openDialog({ columns: typed });
      await change(context, field('dialog-sort-by'), 'column:round');
      await change(context, field('dialog-sort-direction'), 'desc');

      await change(context, field('dialog-indicator'), 'target');
      await answerColumns(context, typed);

      expect(field<HTMLSelectElement>('dialog-sort-by').value).toBe('');
      expect(field('dialog-sort-direction')).toBeNull();
    });

    it('shows a refusal of the order under "Order bars by"', async () => {
      const context = await openDialog({ columns: typed });
      await change(context, field('dialog-sort-by'), 'column:visited_on');
      await change(context, field('dialog-title'), 'Ordered');
      await click(context, field('dialog-save'));

      context.http
        .expectOne(isCreate)
        .flush({ title: 'x', errors: { 'config.sort.column': ["The table has no column 'visited_on'."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(errorOf('widgetSortBy')).toBe("The table has no column 'visited_on'.");
      expect(field('dialog-error')).toBeNull();
    });
  });

  describe('tooltip columns of a map', () => {
    const survey = catalog({
      columns: [
        { name: 'block_code', type: 'text', label: null },
        { name: 'address', type: 'text', label: null },
        { name: 'sample_target', type: 'integer', label: null },
        { name: 'achieved', type: 'integer', label: null },
        { name: 'progress', type: 'decimal', label: null },
        { name: 'last_visit', type: 'timestamp', label: null },
        { name: 'value', type: 'decimal', label: null },
        { name: 'geometry', type: 'text', label: 'Boundary' },
      ],
      numeric: ['value', 'sample_target', 'achieved', 'progress'],
      defaults: { measure: 'value', groupBy: 'block_code' },
    });
    const items = () => fields('tooltip-item');
    const columnSelects = () => fields('tooltip-column') as HTMLSelectElement[];
    const columnsChosen = () => columnSelects().map((select) => select.value);

    /** A new map over the survey table with its geometry chosen. */
    async function newMap(columns = survey) {
      const context = await openDialog({ columns });
      await change(context, field('dialog-chart-type'), 'map');
      await change(context, field('dialog-geometry'), 'geometry');
      return context;
    }

    async function addItem(context: Context, column: string, label = '') {
      await click(context, field('tooltip-add'));
      const at = items().length - 1;
      await change(context, columnSelects()[at], column);
      if (label) {
        await change(context, fields('tooltip-label')[at], label);
      }
    }

    const existingMap = (tooltip: WidgetConfig['tooltip']) => widget('w1', { chartType: 'map', config: config({ geometryColumn: 'geometry', groupBy: 'block_code', tooltip }) });

    it('is offered for a map and for no other chart type, with nothing in it to begin with', async () => {
      const context = await openDialog({ columns: survey });
      expect(fields('dialog-tooltip')).toHaveLength(0);

      await change(context, field('dialog-chart-type'), 'map');

      expect(fields('dialog-tooltip')).toHaveLength(1);
      expect(items()).toHaveLength(0);
      expect(text(field('dialog-tooltip').querySelector('legend'))).toBe('Tooltip columns');
      await change(context, field('dialog-chart-type'), 'bar');
      expect(fields('dialog-tooltip')).toHaveLength(0);
    });

    it('adds, reorders and removes columns', async () => {
      const context = await newMap();
      await addItem(context, 'address', 'Dirección');
      await addItem(context, 'sample_target');
      await addItem(context, 'achieved');
      expect(columnsChosen()).toEqual(['address', 'sample_target', 'achieved']);

      await click(context, fields('tooltip-down')[0]);
      expect(columnsChosen()).toEqual(['sample_target', 'address', 'achieved']);
      expect((fields('tooltip-label')[1] as HTMLInputElement).value).toBe('Dirección'); // the label moved with its column
      await click(context, fields('tooltip-up')[2]);
      expect(columnsChosen()).toEqual(['sample_target', 'achieved', 'address']);
      expect((fields('tooltip-up')[0] as HTMLButtonElement).disabled).toBe(true);
      expect((fields('tooltip-down')[2] as HTMLButtonElement).disabled).toBe(true);
      await click(context, fields('tooltip-remove')[1]);

      expect(columnsChosen()).toEqual(['sample_target', 'address']);
    });

    it('takes at most five, and says so', async () => {
      const context = await newMap();
      for (const column of ['address', 'sample_target', 'achieved', 'progress', 'last_visit']) {
        await addItem(context, column);
      }

      expect(items()).toHaveLength(5);
      expect(field<HTMLButtonElement>('tooltip-add').disabled).toBe(true);
      expect(text(field('tooltip-full'))).toBe('A map takes at most 5 tooltip columns.');
      await click(context, fields('tooltip-remove')[4]);
      expect(field<HTMLButtonElement>('tooltip-add').disabled).toBe(false);
    });

    it('offers every column of the table except the geometry column, and a column once', async () => {
      const context = await newMap();
      await click(context, field('tooltip-add'));
      await addItem(context, 'address');

      expect(Array.from(columnSelects()[0].options).map((o) => o.value)).toEqual(['', 'block_code', 'sample_target', 'achieved', 'progress', 'last_visit', 'value']);
      expect(Array.from(columnSelects()[1].options).map((o) => o.value)).toEqual(['', 'block_code', 'address', 'sample_target', 'achieved', 'progress', 'last_visit', 'value']);
    });

    it('offers a format for the numeric columns only', async () => {
      const context = await newMap();
      await addItem(context, 'address');
      expect(fields('tooltip-format')).toHaveLength(0);

      await addItem(context, 'last_visit');
      expect(fields('tooltip-format')).toHaveLength(0);
      await addItem(context, 'progress');

      expect(fields('tooltip-format')).toHaveLength(1);
      expect(Array.from((fields('tooltip-format')[0] as HTMLSelectElement).options).map((o) => text(o))).toEqual(['Default format', 'Count', 'Decimal', 'Percentage']);
    });

    it('keeps the label optional, and shows the column\'s name where the label would be', async () => {
      const context = await newMap();
      await addItem(context, 'sample_target');

      expect((fields('tooltip-label')[0] as HTMLInputElement).placeholder).toBe('sample_target');
      expect((fields('tooltip-label')[0] as HTMLInputElement).maxLength).toBe(80);
    });

    it('starts again when another indicator is chosen', async () => {
      const context = await newMap();
      await addItem(context, 'address');
      expect(items()).toHaveLength(1);

      await change(context, field('dialog-indicator'), 'target');
      await answerColumns(context, survey);

      expect(items()).toHaveLength(0);
    });

    it('sends the items as they are, with a blank label as none, and the basemap as none', async () => {
      const context = await newMap();
      await change(context, field('dialog-title'), 'Blocks');
      await addItem(context, 'address', 'Dirección');
      await addItem(context, 'sample_target');
      await change(context, fields('tooltip-format')[0], 'count');
      await click(context, field('dialog-save'));

      const request = context.http.expectOne(isCreate);
      expect(request.request.body.config.tooltip).toEqual([
        { column: 'address', label: 'Dirección', format: null },
        { column: 'sample_target', label: null, format: 'count' },
      ]);
      expect(request.request.body.config.basemap).toBeNull();
      request.flush(widget('w9'), { status: 201, statusText: 'Created' });
    });

    it('does not save an item with no column chosen', async () => {
      const context = await newMap();
      await change(context, field('dialog-title'), 'Blocks');
      await click(context, field('tooltip-add'));
      await click(context, field('dialog-save'));

      context.http.expectNone(isCreate);
      expect(text(field('tooltip-list-error'))).toBe('Choose a column for every tooltip column, or remove the empty ones.');
      expect(field('tooltip-list-error').hasAttribute('lang')).toBe(false); // the UI wrote this one
    });

    it('saves no tooltip columns when a map is turned into another chart type', async () => {
      const kept: WidgetConfig['tooltip'] = [{ column: 'address', label: 'Dirección', format: null }, { column: 'sample_target', label: null, format: 'count' }];
      const context = await openDialog({ existing: existingMap(kept), columns: survey });
      expect(columnsChosen()).toEqual(['address', 'sample_target']);

      await change(context, field('dialog-chart-type'), 'bar');
      await click(context, field('dialog-save'));

      const patch = context.http.match(isPatch)[0];
      expect(patch.request.body.chartType).toBe('bar');
      expect(patch.request.body.config.tooltip).toEqual([]);
      expect(patch.request.body.config.basemap).toBeNull();
      patch.flush(widget('w1'));
    });

    it('brings the items back when the type goes back to map before saving', async () => {
      const kept: WidgetConfig['tooltip'] = [{ column: 'address', label: 'Dirección', format: null }];
      const context = await openDialog({ existing: existingMap(kept), columns: survey });

      await change(context, field('dialog-chart-type'), 'bar');
      await change(context, field('dialog-chart-type'), 'map');

      expect(columnsChosen()).toEqual(['address']);
    });

    it('shows what the server refuses under the item it concerns', async () => {
      const context = await newMap();
      await change(context, field('dialog-title'), 'Blocks');
      await addItem(context, 'address');
      await addItem(context, 'sample_target');
      await click(context, field('dialog-save'));

      context.http.expectOne(isCreate).flush(
        { title: 'x', errors: { 'config.tooltip[1].format': ['Only a number column can have a format.'], 'config.tooltip[0].label': ['A label takes at most 80 characters.'], 'config.tooltip': ['A map has at most 5 tooltip columns.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle(context.harness);

      const shown = items().map((item) => Array.from(item.querySelectorAll('[data-testid="tooltip-error"]')).map(text));
      expect(shown).toEqual([['A label takes at most 80 characters.'], ['Only a number column can have a format.']]);
      expect(text(field('tooltip-list-error'))).toBe('A map has at most 5 tooltip columns.');
      expect(field('tooltip-list-error').getAttribute('lang')).toBe('en');
      expect(items()[0].querySelector('[data-testid="tooltip-error"]')!.getAttribute('lang')).toBe('en');
      expect(field<HTMLButtonElement>('dialog-save').disabled).toBe(false);
    });

    it('shows a refused format under its item', async () => {
      const context = await newMap();
      await change(context, field('dialog-title'), 'Blocks');
      await addItem(context, 'sample_target');
      await click(context, field('dialog-save'));

      context.http.expectOne(isCreate).flush({ title: 'x', errors: { 'config.tooltip[0].format': ['Only a number column can have a format.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(Array.from(items()[0].querySelectorAll('[data-testid="tooltip-error"]')).map(text)).toEqual(['Only a number column can have a format.']);
    });

    it('keeps a saved column the table no longer has, selected and marked', async () => {
      const gone: WidgetConfig['tooltip'] = [{ column: 'old_address', label: 'Dirección', format: null }, { column: 'sample_target', label: null, format: null }];
      await openDialog({ existing: existingMap(gone), columns: survey });

      expect(columnsChosen()).toEqual(['old_address', 'sample_target']);
      const marked = Array.from(columnSelects()[0].options).find((o) => o.value === 'old_address')!;
      expect(text(marked)).toBe('old_address (not in the table)');
      expect(marked.selected).toBe(true);
      expect(Array.from(columnSelects()[1].options).some((o) => o.value === 'old_address')).toBe(false);
    });

    it('saves, after changing only the title, a format left on a column that has since become text', async () => {
      const stale: WidgetConfig['tooltip'] = [{ column: 'address', label: null, format: 'percent' }];
      const context = await openDialog({ existing: existingMap(stale), columns: survey });

      await change(context, field('dialog-title'), 'Blocks, renamed');
      await click(context, field('dialog-save'));

      const patch = context.http.match(isPatch)[0];
      expect(patch.request.body.title).toBe('Blocks, renamed');
      expect(patch.request.body.config.tooltip).toEqual([{ column: 'address', label: null, format: null }]);
      patch.flush(widget('w1'));
    });

    it('does not query again, or blank the preview, when a label or a format changes', async () => {
      const kept: WidgetConfig['tooltip'] = [{ column: 'sample_target', label: null, format: null }];
      const context = await openDialog({ existing: existingMap(kept), columns: survey });
      await previewAnswer(context, result([row('0001', 1, 'POINT (-76.5 3.4)', { rows: 1, tooltip: [20] })], { tooltipTypes: ['integer'] }));

      await change(context, fields('tooltip-label')[0], 'Meta muestral');
      await change(context, fields('tooltip-format')[0], 'count');
      await wait(previewDelayMs + 150);
      await settle(context.harness);

      expect(context.http.match(isQuery).filter((r) => !r.cancelled)).toHaveLength(0);
      expect(fields('view-loading')).toHaveLength(0);
    });

    it('queries again, with the names alone, when a column is added', async () => {
      const kept: WidgetConfig['tooltip'] = [{ column: 'sample_target', label: 'Meta', format: 'count' }];
      const context = await openDialog({ existing: existingMap(kept), columns: survey });
      await previewAnswer(context, result([row('0001', 1, 'POINT (-76.5 3.4)', { rows: 1, tooltip: [20] })], { tooltipTypes: ['integer'] }));

      await addItem(context, 'address', 'Dirección');
      const body = await previewAnswer(context, result([row('0001', 1, 'POINT (-76.5 3.4)', { rows: 1, tooltip: [20, 'Cra 5'] })], { tooltipTypes: ['integer', 'text'] }));

      expect(body.config.tooltip).toEqual([{ column: 'sample_target', label: null, format: null }, { column: 'address', label: null, format: null }]);
      expect(body.config.basemap).toBeNull();
    });
  });

  describe('basemap of a map', () => {
    const basemapsUrl = '/api/v1/basemaps';
    const isBasemaps = (r: { url: string; method: string }) => r.method === 'GET' && r.url === basemapsUrl;
    const none: BasemapList = { provider: 'none', basemaps: [] };
    const google: BasemapList = {
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
    const square = 'POLYGON ((-76.6 3.4, -76.59 3.4, -76.59 3.41, -76.6 3.41, -76.6 3.4))';
    const existingMap = (basemap: WidgetConfig['basemap']) => widget('w1', { chartType: 'map', config: config({ geometryColumn: 'geometry', basemap }) });
    const select = () => field<HTMLSelectElement>('dialog-basemap');
    let stub: MapStub;

    beforeAll(async () => {
      const preload = stubMap();
      await loadLeaflet();
      preload.restore();
    });
    beforeEach(() => {
      stub = stubMap({ width: 600, height: 400 });
    });
    afterEach(() => stub.restore());

    async function answerList(context: Context, list: BasemapList) {
      context.http.match(isBasemaps).forEach((request) => request.flush(list));
      await settle(context.harness);
    }

    it('offers no basemap choice where the deployment offers none, and saves a new map with none', async () => {
      const context = await openDialog();
      await answerList(context, none);
      await change(context, field('dialog-chart-type'), 'map');
      await change(context, field('dialog-title'), 'Blocks');
      await change(context, field('dialog-geometry'), 'geometry');

      expect(fields('dialog-basemap')).toHaveLength(0);
      await click(context, field('dialog-save'));

      const request = context.http.expectOne(isCreate);
      expect(request.request.body.config.basemap).toBeNull();
      request.flush(widget('w9'), { status: 201, statusText: 'Created' });
    });

    it('offers none and the kinds the deployment offers, starting a new map on none', async () => {
      const context = await openDialog();
      await answerList(context, google);
      await change(context, field('dialog-chart-type'), 'map');

      expect(Array.from(select().options).map((o) => text(o))).toEqual(['None', 'Streets', 'Satellite']);
      expect(select().value).toBe('');
    });

    it('shows a saved basemap the deployment no longer offers beside None, and keeps it when only the title changes', async () => {
      const context = await openDialog({ existing: existingMap('streets') });
      await answerList(context, none);

      expect(Array.from(select().options).map((o) => [o.value, text(o)])).toEqual([['', 'None'], ['streets', 'Streets (no longer offered)']]);
      expect(select().value).toBe('streets');
      await change(context, field('dialog-title'), 'Blocks, renamed');
      await click(context, field('dialog-save'));

      const patch = context.http.match(isPatch)[0];
      expect(patch.request.body.config.basemap).toBe('streets');
      patch.flush(widget('w1'));
    });

    it('lets the analyst turn a basemap that is no longer offered off', async () => {
      const context = await openDialog({ existing: existingMap('satellite') });
      await answerList(context, { provider: 'osm', basemaps: [google.basemaps[0]] });
      expect(Array.from(select().options).map((o) => text(o))).toEqual(['None', 'Streets', 'Satellite (no longer offered)']);

      await change(context, select(), '');
      await click(context, field('dialog-save'));

      const patch = context.http.match(isPatch)[0];
      expect(patch.request.body.config.basemap).toBeNull();
      patch.flush(widget('w1'));
    });

    it('draws the preview over satellite imagery once the analyst picks it, without querying the indicator again', async () => {
      const context = await openDialog({ existing: existingMap(null) });
      await answerList(context, google);
      await previewAnswer(context, result([row('Cali', 75, square)]));
      for (let i = 0; i < 20 && !overlay().querySelector('.leaflet-container path'); i++) {
        await wait(5);
        await settle(context.harness);
      }
      const map = () => overlay().querySelector<HTMLElement>('[data-testid="map-container"]')!;
      expect(map().getAttribute('data-basemap')).toBeNull();
      expect(overlay().querySelectorAll('[data-testid="map-tile"]')).toHaveLength(0);

      await change(context, select(), 'satellite');
      expect(map().getAttribute('data-basemap')).toBe('satellite');
      let credits = context.http.match((r) => r.url.startsWith(`${basemapsUrl}/satellite/credits`));
      for (let i = 0; i < 20 && !credits.length; i++) {
        await wait(5);
        await settle(context.harness);
        credits = context.http.match((r) => r.url.startsWith(`${basemapsUrl}/satellite/credits`));
      }
      expect(credits).toHaveLength(1);
      expect(overlay().querySelectorAll('[data-testid="map-tile"]')).toHaveLength(0); // not before its credits
      credits[0].flush({ text: 'Imagery ©2026 Maxar Technologies', maxZoom: 19 });
      await settle(context.harness);
      await wait(20);
      await settle(context.harness);

      expect(overlay().querySelectorAll('[data-testid="map-tile"]').length).toBeGreaterThan(0);
      await wait(previewDelayMs + 150);
      await settle(context.harness);
      expect(context.http.match(isQuery).filter((r) => !r.cancelled)).toHaveLength(0);
      expect(fields('view-loading')).toHaveLength(0);

      await click(context, field('dialog-save'));
      const patch = context.http.match(isPatch)[0];
      expect(patch.request.body.config.basemap).toBe('satellite');
      patch.flush(widget('w1'));
    });

    it('shows a refusal of the basemap next to its select', async () => {
      const context = await openDialog({ existing: existingMap('streets') });
      await answerList(context, google);
      await click(context, field('dialog-save'));

      context.http.match(isPatch)[0].flush({ title: 'x', errors: { 'config.basemap': ["The basemap must be 'streets' or 'satellite'."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(errorOf('widgetBasemap')).toBe("The basemap must be 'streets' or 'satellite'.");
    });

    it('saves no basemap when a map is turned into another chart type', async () => {
      const context = await openDialog({ existing: existingMap('streets') });
      await answerList(context, google);

      await change(context, field('dialog-chart-type'), 'bar');
      expect(fields('dialog-basemap')).toHaveLength(0);
      await click(context, field('dialog-save'));

      const patch = context.http.match(isPatch)[0];
      expect(patch.request.body.config.basemap).toBeNull();
      patch.flush(widget('w1'));
    });
  });

  describe('the dashboard of an edited widget (design D9)', () => {
    const indicatorsAndField: DashboardEntry[] = [{ id: 'd1', name: 'Indicators' }, { id: 'd2', name: 'Field' }];
    const projectUrl = `/api/v1/projects/${projectId}`;
    const select = () => field<HTMLSelectElement>('dialog-dashboard');
    const refusal = (message: string) => [{ title: 'x', errors: { dashboardId: [message] } }, { status: 400, statusText: 'Bad Request' }] as const;
    const full = 'A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first.';

    it('is offered after the title, on the widget\'s own, listing the project\'s dashboards (Widget moved to another dashboard)', async () => {
      await openDialog({ existing: widget('w1'), dashboards: indicatorsAndField });

      expect(optionsOf('dialog-dashboard')).toEqual(['d1', 'd2']);
      expect(optionTexts('dialog-dashboard')).toEqual(['Indicators', 'Field']);
      expect(select().value).toBe('d1');
      expect(text(dialog().querySelector('label[for="widgetDashboard"]'))).toBe('Dashboard');
      expect(field('dialog-title').compareDocumentPosition(select()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('is not offered in a project that has one dashboard', async () => {
      await openDialog({ existing: widget('w1') });

      expect(fields('dialog-dashboard')).toHaveLength(0);
    });

    it('is not offered when adding a widget, which goes to the dashboard open (New widget)', async () => {
      const context = await openDialog({ dashboards: indicatorsAndField });
      expect(fields('dialog-dashboard')).toHaveLength(0);

      await change(context, field('dialog-title'), 'Coverage');
      await click(context, field('dialog-save'));

      const request = context.http.expectOne(isCreate);
      expect(request.request.url).toBe(widgetsUrl);
      expect(Object.keys(request.request.body)).toEqual(['title', 'chartType', 'config']);
    });

    it('sends the dashboard alone when nothing else changed (Widget moved to another dashboard)', async () => {
      const context = await openDialog({ existing: widget('w1'), dashboards: indicatorsAndField });

      await change(context, select(), 'd2');
      await click(context, field('dialog-save'));

      const request = context.http.expectOne(isPatch);
      expect(request.request.url).toBe(widgetUrl('w1'));
      expect(request.request.body).toEqual({ dashboardId: 'd2' });
    });

    it('sends the whole form with the dashboard once another field changed, before or after choosing it', async () => {
      const context = await openDialog({ existing: widget('w1', { title: 'Coverage by municipality' }), dashboards: indicatorsAndField });

      await change(context, select(), 'd2');
      await change(context, field('dialog-title'), 'Coverage');
      await click(context, field('dialog-save'));

      expect(context.http.expectOne(isPatch).request.body).toEqual({ title: 'Coverage', chartType: 'column', config: config(), dashboardId: 'd2' });
    });

    it('counts a filter removed with its button as a change', async () => {
      const filtered = widget('w1', { config: config({ filters: [{ column: 'round', op: 'range', min: '2' }] }) });
      const context = await openDialog({ existing: filtered, dashboards: indicatorsAndField });

      await click(context, field('dialog-filter-remove'));
      await change(context, select(), 'd2');
      await click(context, field('dialog-save'));

      expect(context.http.expectOne(isPatch).request.body).toEqual({ title: 'Widget w1', chartType: 'column', config: config(), dashboardId: 'd2' });
    });

    it('counts a tooltip column moved with its button as a change', async () => {
      const tooltip: WidgetConfig['tooltip'] = [{ column: 'value', label: null, format: null }, { column: 'round', label: null, format: null }];
      const map = widget('w1', { chartType: 'map', config: config({ geometryColumn: 'geometry', tooltip }) });
      const context = await openDialog({ existing: map, dashboards: indicatorsAndField });

      await click(context, fields('tooltip-down')[0]);
      await change(context, select(), 'd2');
      await click(context, field('dialog-save'));

      const body = context.http.expectOne(isPatch).request.body;
      expect(body.dashboardId).toBe('d2');
      expect(body.config.tooltip.map((item: { column: string }) => item.column)).toEqual(['round', 'value']);
    });

    it('does not count the column picked for a filter that was not added as a change', async () => {
      const context = await openDialog({ existing: widget('w1'), dashboards: indicatorsAndField });

      await change(context, field('filter-column'), 'round');
      await change(context, select(), 'd2');
      await click(context, field('dialog-save'));

      expect(context.http.expectOne(isPatch).request.body).toEqual({ dashboardId: 'd2' });
    });

    it('moves a widget whose group column the table no longer has, sending the dashboard alone (Stale widget moved)', async () => {
      const stale = widget('w1', { config: config({ groupBy: 'district' }) });
      const context = await openDialog({ existing: stale, dashboards: indicatorsAndField });
      expect(field<HTMLSelectElement>('dialog-group-by').value).toBe('district'); // what is saved, though the table lacks it

      await change(context, select(), 'd2');
      await click(context, field('dialog-save'));

      expect(context.http.expectOne(isPatch).request.body).toEqual({ dashboardId: 'd2' });
    });

    it('moves a map whose tooltip column has become text, sending the dashboard alone and not the format the form drops (Stale widget moved)', async () => {
      const stale = widget('w1', { chartType: 'map', config: config({ geometryColumn: 'geometry', tooltip: [{ column: 'municipality', label: null, format: 'percent' }] }) });
      const context = await openDialog({ existing: stale, dashboards: indicatorsAndField });

      await change(context, select(), 'd2');
      await click(context, field('dialog-save'));

      expect(context.http.expectOne(isPatch).request.body).toEqual({ dashboardId: 'd2' });
    });

    it('shows a full dashboard\'s refusal next to the select, keeps the form open and reads the project again (Full dashboard)', async () => {
      const context = await openDialog({ existing: widget('w1'), dashboards: indicatorsAndField });
      await change(context, select(), 'd2');
      await click(context, field('dialog-save'));

      context.http.expectOne(isPatch).flush(...refusal(full));
      await settle(context.harness);

      expect(errorOf('widgetDashboard')).toBe(full);
      expect(select().getAttribute('aria-describedby')).toBe('widgetDashboard-error');
      expect(field('dialog-error')).toBeNull();
      expect(field<HTMLButtonElement>('dialog-save').disabled).toBe(false);
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: indicatorsAndField }));
      await settle(context.harness);
      expect(select().value).toBe('d2'); // still chosen: the project still has it
      expect(errorOf('widgetDashboard')).toBe(full);
    });

    it('drops a dashboard removed meanwhile from the select once the project is read again (Target removed meanwhile)', async () => {
      const three: DashboardEntry[] = [{ id: 'd1', name: 'Indicators' }, { id: 'd2', name: 'Health' }, { id: 'd3', name: 'Field' }];
      const context = await openDialog({ existing: widget('w1'), dashboards: three });
      await change(context, select(), 'd3');
      await click(context, field('dialog-save'));

      context.http.expectOne(isPatch).flush(...refusal('The dashboard does not exist.'));
      await settle(context.harness);
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: three.slice(0, 2) }));
      await settle(context.harness);

      expect(optionTexts('dialog-dashboard')).toEqual(['Indicators', 'Health']);
      expect(select().value).toBe('d1'); // back on the widget's own
      expect(errorOf('widgetDashboard')).toBe('The dashboard does not exist.');

      await change(context, select(), 'd2');
      expect(errorOf('widgetDashboard')).toBe('');
      await click(context, field('dialog-save'));
      expect(context.http.expectOne(isPatch).request.body).toEqual({ dashboardId: 'd2' });
    });

    it('keeps the select and its refusal when the project is left with one dashboard', async () => {
      const context = await openDialog({ existing: widget('w1'), dashboards: indicatorsAndField });
      await change(context, select(), 'd2');
      await click(context, field('dialog-save'));

      context.http.expectOne(isPatch).flush(...refusal('The dashboard does not exist.'));
      await settle(context.harness);
      context.http.expectOne(projectUrl).flush(details('analyst', { dashboards: indicatorsAndField.slice(0, 1) }));
      await settle(context.harness);

      expect(optionTexts('dialog-dashboard')).toEqual(['Indicators']);
      expect(errorOf('widgetDashboard')).toBe('The dashboard does not exist.');
    });

    it('reads the project again for a refusal of the dashboard only', async () => {
      const context = await openDialog({ existing: widget('w1'), dashboards: indicatorsAndField });
      await change(context, select(), 'd2');
      await change(context, field('dialog-group-by'), 'round');
      await click(context, field('dialog-save'));

      context.http
        .expectOne(isPatch)
        .flush({ title: 'x', errors: { 'config.groupBy': ["The table has no column 'round'."] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(errorOf('widgetGroupBy')).toBe("The table has no column 'round'.");
      expect(errorOf('widgetDashboard')).toBe('');
      context.http.expectNone(projectUrl);
    });
  });
});

// Ignoring dashboard filters, and the preview with the dashboard's filters (dashboard filters, design D6, D9).
describe('the widget dialog on a dashboard with filters', () => {
  afterEach(clearOverlay);

  const municipality: DashboardFilter = { column: 'municipality', label: 'Municipality', kind: 'text', default: null };
  const round: DashboardFilter = { column: 'round', label: 'Round', kind: 'number', default: null };
  const cali = `f=${encodeFilters([{ column: 'municipality', op: 'in', values: ['Cali'] }])}`;
  const box = (column: string) => dialog().querySelector<HTMLInputElement>(`[data-testid="dialog-dashboard-filter"][data-column="${column}"]`)!;
  const indicatorsAndField: DashboardEntry[] = [{ id: 'd1', name: 'Indicators' }, { id: 'd2', name: 'Field' }];
  const stat = widget('w1', { title: 'Households, all municipalities', chartType: 'stat', config: config({ groupBy: null }) });

  it('saves the filters left unchecked as ignored (Total that does not change)', async () => {
    const context = await openDialog({ existing: stat, filters: [municipality, round] });

    expect(text(dialog().querySelector('[data-testid="dialog-dashboard-filters"] legend'))).toBe('Dashboard filters');
    expect(box('municipality').checked).toBe(true);
    await click(context, box('municipality'));
    await click(context, field('dialog-save'));

    expect(context.http.expectOne(isPatch).request.body.config.ignoredDashboardFilters).toEqual(['municipality']);
  });

  it('offers nothing to ignore on a dashboard with no filters (No filters to ignore)', async () => {
    await openDialog({ existing: stat });

    expect(dialog().querySelector('[data-testid="dialog-dashboard-filters"]')).toBeNull();
  });

  it('previews with the dashboard\'s values in force, and says so (Preview filtered)', async () => {
    const context = await openDialog({ existing: widget('w1'), filters: [municipality], query: cali });

    const body = (await previewAnswer(context, result([row('Cali', 75)]))) as { dashboardFilters?: unknown };

    expect(body.dashboardFilters).toEqual([{ column: 'municipality', kind: 'text', values: ['Cali'] }]);
    expect(text(field('dialog-preview-filtered'))).toBe('Preview filtered by: Municipality');
  });

  it('previews again without a filter once it is unchecked (Ignoring updates the preview)', async () => {
    const context = await openDialog({ existing: widget('w1'), filters: [municipality], query: cali });
    await previewAnswer(context, result([row('Cali', 75)]));

    await click(context, box('municipality'));
    const body = (await previewAnswer(context, result([row('Cali', 75), row('Palmira', 60)]))) as { dashboardFilters?: unknown };

    expect(body.dashboardFilters).toBeUndefined();
    expect(dialog().querySelector('[data-testid="dialog-preview-filtered"]')).toBeNull();
    expect(text(dialog().querySelector('[data-testid="widget-not-filtered"]'))).toBe('Not filtered by: Municipality');
  });

  it('keeps ignoring a filter the dashboard does not have now', async () => {
    const ignoring = widget('w1', { config: config({ ignoredDashboardFilters: ['district'] }) });
    const context = await openDialog({ existing: ignoring, filters: [municipality] });

    await click(context, box('municipality'));
    await click(context, field('dialog-save'));

    expect(context.http.expectOne(isPatch).request.body.config.ignoredDashboardFilters).toEqual(['district', 'municipality']);
  });

  it('counts a box changed as a change, so a move sends the whole form', async () => {
    const context = await openDialog({ existing: widget('w1'), dashboards: indicatorsAndField, filters: [municipality] });

    await click(context, box('municipality'));
    await change(context, field('dialog-dashboard'), 'd2');
    await click(context, field('dialog-save'));

    const body = context.http.expectOne(isPatch).request.body;
    expect(body.dashboardId).toBe('d2');
    expect(body.config.ignoredDashboardFilters).toEqual(['municipality']);
  });

  it('shows the server\'s refusal of an ignored filter at the boxes', async () => {
    const context = await openDialog({ existing: widget('w1'), filters: [municipality] });

    await click(context, box('municipality'));
    await click(context, field('dialog-save'));
    context.http
      .expectOne(isPatch)
      .flush({ title: 'x', errors: { 'config.ignoredDashboardFilters[0]': ["'municipality' is not a valid column name."] } }, { status: 400, statusText: 'Bad Request' });
    await settle(context.harness);

    expect(text(dialog().querySelector('[data-testid="dialog-dashboard-filters"] [data-testid="field-error"]'))).toBe("'municipality' is not a valid column name.");
  });
});

describe('the widget dialog with a series (design D7)', () => {
  afterEach(clearOverlay);

  /** The table of `households_by_source`: two text dimensions besides the default `municipality`. */
  const twoDimensions = (extra: Partial<IndicatorColumns> = {}) =>
    catalog({ columns: [...catalog().columns, { name: 'water_source', type: 'text', label: null }, { name: 'sex', type: 'text', label: null }], ...extra });
  const radio = (id: string) => field<HTMLInputElement>(`dialog-layout-${id}`);
  const queries = (context: Context) => context.http.match(isQuery).filter((r) => !r.cancelled);
  const typeOf = (request: { request: { body: { chartType: string } } }) => request.request.body.chartType;

  /** Lets the query that lists the values of the series column be sent, and answers it with these values. */
  async function answerValues(context: Context, values: string[]) {
    await wait(previewDelayMs + 100);
    await settle(context.harness);
    const requests = queries(context).filter((r) => typeOf(r) === 'heatmap');
    expect(requests).toHaveLength(1);
    requests[0].flush(seriesResult(values.map((value) => cell('Cali', value, 1)), values));
    await settle(context.harness);
    return requests[0].request.body as { config: WidgetConfig; chartType: string };
  }

  const create = async (context: Context) => {
    await click(context, field('dialog-save'));
    return context.http.expectOne(isCreate).request.body as { title: string; chartType: ChartType; config: WidgetConfig };
  };
  const config_ = (body: { config: WidgetConfig }) => body.config;

  describe('the fields of each chart type', () => {
    /** What each kind shows: its series select (and what the first option says), its own settings, and what it still shares. */
    const expected: Record<ChartType, { series: string | null; own: string[]; sort: boolean }> = {
      stat: { series: null, own: [], sort: false },
      bar: { series: 'No series', own: [], sort: true },
      column: { series: 'No series', own: [], sort: true },
      line: { series: 'No series', own: [], sort: false },
      histogram: { series: null, own: ['dialog-buckets'], sort: false },
      pie: { series: null, own: [], sort: false },
      map: { series: null, own: ['dialog-geometry', 'dialog-tooltip'], sort: false },
      heatmap: { series: 'Choose a column', own: [], sort: false },
      pyramid: { series: 'Choose a column', own: ['dialog-pyramid'], sort: false },
      likert: { series: 'Choose a column', own: ['dialog-scale'], sort: false },
      crosstab: { series: 'Choose a column', own: [], sort: false },
      boxplot: { series: null, own: ['dialog-orientation'], sort: true },
    };
    const settings = ['dialog-buckets', 'dialog-geometry', 'dialog-tooltip', 'dialog-pyramid', 'dialog-scale', 'dialog-layout', 'dialog-orientation'];

    it('lists the twelve kinds in the chart type select', async () => {
      await openDialog({ columns: twoDimensions() });

      expect(optionsOf('dialog-chart-type')).toEqual([...chartTypes]);
      expect(optionTexts('dialog-chart-type')).toEqual([
        'Stat tile',
        'Bar chart',
        'Column chart',
        'Line chart',
        'Histogram',
        'Pie chart',
        'Map',
        'Heat map',
        'Population pyramid',
        'Diverging Likert bars',
        'Cross table',
        'Box plot',
      ]);
    });

    it.each(chartTypes)('shows the fields of a %s widget and no others', async (type) => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-chart-type'), type);
      const want = expected[type];

      if (want.series === null) {
        expect(field('dialog-series-by')).toBeNull();
      } else {
        expect(field('dialog-series-by')).not.toBeNull();
        expect(optionTexts('dialog-series-by')[0]).toBe(want.series);
        expect(optionsOf('dialog-series-by')).toEqual(['', 'value', 'numerator', 'denominator', 'round', 'geometry', 'water_source', 'sex']);
      }
      for (const id of settings.filter((id) => id !== 'dialog-layout')) {
        expect(field(id) !== null, `${type} / ${id}`).toBe(want.own.includes(id));
      }
      expect(field('dialog-sort-by') !== null).toBe(want.sort);
      expect(field('dialog-layout')).toBeNull(); // only once a series is chosen
      expect(field('dialog-group-by') !== null).toBe(type !== 'stat');
    });
  });

  describe('the series column', () => {
    it('offers every column but the group column, and no series for a bar, column or line chart to begin with', async () => {
      const context = await openDialog({ columns: twoDimensions() });

      expect(field<HTMLSelectElement>('dialog-series-by').value).toBe('');
      expect(optionsOf('dialog-series-by')).toEqual(['', 'value', 'numerator', 'denominator', 'round', 'geometry', 'water_source', 'sex']);
      await change(context, field('dialog-group-by'), 'water_source');
      expect(optionsOf('dialog-series-by')).not.toContain('water_source');
      expect(optionsOf('dialog-series-by')).toContain('municipality');
    });

    it('sends no series, as before, when none is chosen', async () => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-title'), 'Coverage');

      const body = await create(context);

      expect(Object.keys(body.config).sort()).toEqual(Object.keys(config()).sort());
      expect('seriesBy' in body.config).toBe(false);
      expect('layout' in body.config).toBe(false);
    });

    it('saves the series column of a chart that takes one, and previews it', async () => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-title'), 'Water source by municipality');
      await change(context, field('dialog-series-by'), 'water_source');

      const preview = await previewAnswer(context, seriesResult([cell('Cali', 'Piped', 5), cell('Cali', 'Well', 3)], ['Piped', 'Well']));
      expect(preview.config).toMatchObject({ seriesBy: 'water_source', layout: 'grouped' });
      expect(preview.chartType).toBe('column');
      expect(all(dialog(), 'legend-name').map(text)).toEqual(['Piped', 'Well']);

      const body = await create(context);
      expect(body.config).toMatchObject({ groupBy: 'municipality', seriesBy: 'water_source', layout: 'grouped' });
    });

    it.each(['heatmap', 'pyramid', 'likert', 'crosstab'] as const)('requires the series column of a %s widget', async (type) => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-chart-type'), type);
      await change(context, field('dialog-title'), 'Cross');

      await click(context, field('dialog-save'));

      expect(errorOf('widgetSeriesBy')).toBe('Choose the series column.');
      context.http.expectNone(isCreate);
    });

    it('refuses the group column as the series column', async () => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-chart-type'), 'heatmap');
      await change(context, field('dialog-title'), 'Cross');
      await change(context, field('dialog-series-by'), 'sex');
      await change(context, field('dialog-group-by'), 'sex');

      await click(context, field('dialog-save'));

      expect(errorOf('widgetSeriesBy')).toBe('Choose a series column other than the group column.');
      context.http.expectNone(isCreate);
    });

    it('drops the series when the indicator changes, as every column chosen from the old table', async () => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-series-by'), 'water_source');
      expect(field<HTMLSelectElement>('dialog-series-by').value).toBe('water_source');
      expect(radio('grouped')).not.toBeNull();

      await change(context, field('dialog-indicator'), 'households_visited');
      await answerColumns(context, twoDimensions());

      expect(field<HTMLSelectElement>('dialog-series-by').value).toBe('');
      expect(field('dialog-layout')).toBeNull();
    });

    it('keeps a saved series column the table no longer has, so the form shows what is saved', async () => {
      const saved = widget('w1', { chartType: 'heatmap', config: config({ seriesBy: 'gone' }) });
      await openDialog({ existing: saved, columns: twoDimensions() });

      expect(field<HTMLSelectElement>('dialog-series-by').value).toBe('gone');
      expect(optionsOf('dialog-series-by')).toContain('gone');
    });

    it('sends the series of the kinds that take one and of no other', async () => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-title'), 'Chart');
      await change(context, field('dialog-series-by'), 'water_source');
      await change(context, field('dialog-chart-type'), 'pie');

      const body = await create(context);

      expect('seriesBy' in body.config).toBe(false);
    });

    it('shows the server\'s refusal of the series under the select', async () => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-title'), 'Chart');
      await change(context, field('dialog-series-by'), 'water_source');
      await click(context, field('dialog-save'));

      context.http.expectOne(isCreate).flush({ title: 'x', errors: { 'config.seriesBy': ['A column widget cannot have a series.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(errorOf('widgetSeriesBy')).toBe('A column widget cannot have a series.');
    });
  });

  describe('the layout of bars and columns', () => {
    async function withSeries(type: 'bar' | 'column' = 'column') {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-chart-type'), type);
      await change(context, field('dialog-title'), 'Water');
      await change(context, field('dialog-series-by'), 'water_source');
      return context;
    }

    it('offers Grouped, Stacked and Stacked to 100% once a bar or column chart has a series, grouped to begin with', async () => {
      const context = await withSeries();

      expect(Array.from(dialog().querySelectorAll('[data-testid="dialog-layout"] label')).map(text)).toEqual(['Grouped', 'Stacked', 'Stacked to 100%']);
      expect(radio('grouped').checked).toBe(true);
      expect(radio('stacked').checked).toBe(false);
      await change(context, field('dialog-chart-type'), 'bar');
      expect(field('dialog-layout')).not.toBeNull();
    });

    it('offers no layout for a line chart or for a chart without a series', async () => {
      const context = await withSeries();
      await change(context, field('dialog-chart-type'), 'line');
      expect(field('dialog-layout')).toBeNull();

      await change(context, field('dialog-chart-type'), 'column');
      await change(context, field('dialog-series-by'), '');
      expect(field('dialog-layout')).toBeNull();
    });

    it('saves the layout chosen', async () => {
      const context = await withSeries();
      await click(context, radio('percent'));

      expect(radio('percent').checked).toBe(true);
      expect(config_(await create(context))).toMatchObject({ seriesBy: 'water_source', layout: 'percent' });
    });

    it('previews the layout', async () => {
      const context = await withSeries('bar');
      await click(context, radio('stacked'));

      const preview = await previewAnswer(context, seriesResult([cell('Cali', 'Piped', 5), cell('Cali', 'Well', 3)], ['Piped', 'Well']));

      expect(preview.config.layout).toBe('stacked');
      expect(preview.chartType).toBe('bar');
    });

    it('disables Stacked and 100% while the widget plots a ratio, and says why', async () => {
      const context = await withSeries();
      await click(context, radio('stacked'));

      await change(context, field('dialog-mode'), 'ratio');

      expect(radio('stacked').disabled).toBe(true);
      expect(radio('percent').disabled).toBe(true);
      expect(radio('grouped').disabled).toBe(false);
      expect(radio('grouped').checked).toBe(true); // a stacked layout is dropped for a ratio
      expect(text(field('dialog-layout-reason'))).toBe('Stacked bars add their values, so they need a measure, not a ratio.');

      await change(context, field('dialog-mode'), 'measure');
      expect(radio('stacked').disabled).toBe(false);
      expect(field('dialog-layout-reason')).toBeNull();
    });

    it('shows a saved layout, and the server\'s refusal of a layout', async () => {
      const saved = widget('w1', { chartType: 'column', config: config({ seriesBy: 'water_source', layout: 'stacked' }) });
      const context = await openDialog({ existing: saved, columns: twoDimensions() });

      expect(radio('stacked').checked).toBe(true);
      await change(context, field('dialog-title'), 'Water');
      await click(context, field('dialog-save'));
      context.http.expectOne(isPatch).flush({ title: 'x', errors: { 'config.layout': ['Stacked bars add their values, so they need a measure, not a ratio.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(dialog().querySelector('[data-testid="dialog-layout"] [data-testid="field-error"]'))).toBe('Stacked bars add their values, so they need a measure, not a ratio.');
    });
  });

  describe('the sides of a population pyramid', () => {
    async function pyramid() {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-chart-type'), 'pyramid');
      await change(context, field('dialog-title'), 'Population');
      await change(context, field('dialog-group-by'), 'round');
      await change(context, field('dialog-series-by'), 'sex');
      return context;
    }

    it('offers the values of the series column that the data holds, and accepts a typed value', async () => {
      const context = await pyramid();

      const asked = await answerValues(context, ['Female', 'Male', 'Other']);

      // The values come from the rows of the widget as a heat map would list them; the sides themselves are not asked for.
      expect(asked.config).toMatchObject({ groupBy: 'round', seriesBy: 'sex' });
      expect(Array.from(dialog().querySelectorAll('#widgetSeriesValues option')).map((o) => o.getAttribute('value'))).toEqual(['Female', 'Male', 'Other']);
      await change(context, field('dialog-pyramid-left'), 'Female');
      await change(context, field('dialog-pyramid-right'), 'Unknown to the data');
      expect(field<HTMLInputElement>('dialog-pyramid-right').value).toBe('Unknown to the data');
    });

    it('requires both sides, and two different ones', async () => {
      const context = await pyramid();

      await click(context, field('dialog-save'));
      expect(errorOf('widgetPyramidLeft')).toBe('Name the value drawn to the left.');
      expect(errorOf('widgetPyramidRight')).toBe('Name the value drawn to the right.');

      await change(context, field('dialog-pyramid-left'), 'Female');
      await change(context, field('dialog-pyramid-right'), 'Female');
      expect(errorOf('widgetPyramidRight')).toBe('Choose different values for the two sides.');
      context.http.expectNone(isCreate);
    });

    it('saves the sides with the series column, and previews them', async () => {
      const context = await pyramid();
      await change(context, field('dialog-pyramid-left'), 'Female');
      await change(context, field('dialog-pyramid-right'), 'Male');

      await wait(previewDelayMs + 100);
      await settle(context.harness);
      const requests = queries(context);
      const preview = requests.find((r) => typeOf(r) === 'pyramid')!;
      expect(preview.request.body.config).toMatchObject({ seriesBy: 'sex', pyramid: { left: 'Female', right: 'Male' } });
      preview.flush(seriesResult([cell('1', 'Female', 5), cell('1', 'Male', 4)], ['Female', 'Male']));
      requests.filter((r) => r !== preview).forEach((r) => r.flush(seriesResult([cell('1', 'Female', 5)], ['Female', 'Male', 'Other'])));
      await settle(context.harness);
      expect(all(dialog(), 'chart-mark')).toHaveLength(2);

      const body = await create(context);
      expect(body.chartType).toBe('pyramid');
      expect(body.config).toMatchObject({ seriesBy: 'sex', pyramid: { left: 'Female', right: 'Male' } });
      expect('layout' in body.config).toBe(false);
    });

    it('starts again when the series column changes, for its values are another column\'s', async () => {
      const context = await pyramid();
      await change(context, field('dialog-pyramid-left'), 'Female');

      await change(context, field('dialog-series-by'), 'water_source');

      expect(field<HTMLInputElement>('dialog-pyramid-left').value).toBe('');
    });

    it('shows a saved pyramid, and the server\'s refusal of its right side at that field', async () => {
      const saved = widget('w1', { chartType: 'pyramid', config: config({ groupBy: 'round', seriesBy: 'sex', pyramid: { left: 'Female', right: 'Male' } }) });
      const context = await openDialog({ existing: saved, columns: twoDimensions() });
      expect(field<HTMLInputElement>('dialog-pyramid-left').value).toBe('Female');
      expect(field<HTMLInputElement>('dialog-pyramid-right').value).toBe('Male');

      await change(context, field('dialog-pyramid-right'), 'Female');
      await change(context, field('dialog-pyramid-right'), 'Male');
      await click(context, field('dialog-save'));
      context.http.expectOne(isPatch).flush({ title: 'x', errors: { 'config.pyramid.right': ['Choose a different value for the right side.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(errorOf('widgetPyramidRight')).toBe('Choose a different value for the right side.');
      expect(errorOf('widgetPyramidLeft')).toBe('');
    });
  });

  describe('the scale of a Likert widget', () => {
    const opinions = ['Strongly agree', 'Agree', 'Neither', 'Disagree', 'Strongly disagree'];
    const items = () => fields('scale-item');
    const valuesOf = () => fields('scale-value').map((input) => (input as HTMLInputElement).value);
    const sidesOf = () => fields('scale-side').map((select) => (select as HTMLSelectElement).value);
    const setSide = (context: Context, index: number, side: string) => change(context, fields('scale-side')[index], side);

    async function likert(values: string[] = opinions) {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-chart-type'), 'likert');
      await change(context, field('dialog-title'), 'Opinion');
      await change(context, field('dialog-series-by'), 'sex');
      await answerValues(context, values);
      return context;
    }

    it('lists the values of the data in natural order, each left out until marked', async () => {
      await likert();

      expect(valuesOf()).toEqual(['Agree', 'Disagree', 'Neither', 'Strongly agree', 'Strongly disagree']);
      expect(sidesOf()).toEqual(['out', 'out', 'out', 'out', 'out']);
      expect(Array.from(fields('scale-side')[0].querySelectorAll('option')).map(text)).toEqual(['Negative', 'Neutral', 'Positive', 'Left out']);
    });

    it('asks for the series column before it has values to list', async () => {
      const context = await openDialog({ columns: twoDimensions() });
      await change(context, field('dialog-chart-type'), 'likert');

      expect(text(field('scale-none'))).toBe('Choose the series column to see the values of the data.');
      expect(items()).toHaveLength(0);
    });

    it('marks sides and orders the values with the up and down buttons, and saves the values that are in the scale, in order', async () => {
      const context = await likert();
      // The data lists Agree, Disagree, Neither, Strongly agree, Strongly disagree; the scale goes from the most negative to the most positive.
      const wanted = ['Strongly disagree', 'Disagree', 'Neither', 'Agree', 'Strongly agree'];
      for (let target = 0; target < wanted.length; target++) {
        let at = valuesOf().indexOf(wanted[target]);
        while (at > target) {
          await click(context, fields('scale-up')[at]);
          at--;
        }
      }
      expect(valuesOf()).toEqual(wanted);
      await click(context, fields('scale-down')[0]);
      expect(valuesOf().slice(0, 2)).toEqual(['Disagree', 'Strongly disagree']);
      await click(context, fields('scale-up')[1]);
      expect(valuesOf()).toEqual(wanted);
      for (const [i, side] of ['negative', 'negative', 'neutral', 'positive', 'positive'].entries()) {
        await setSide(context, i, side);
      }

      const body = await create(context);

      expect(body.chartType).toBe('likert');
      expect(body.config.seriesBy).toBe('sex');
      expect(body.config.scale).toEqual([
        { value: 'Strongly disagree', side: 'negative' },
        { value: 'Disagree', side: 'negative' },
        { value: 'Neither', side: 'neutral' },
        { value: 'Agree', side: 'positive' },
        { value: 'Strongly agree', side: 'positive' },
      ]);
    });

    it('leaves out of the scale the values marked so, and removes one at will', async () => {
      const context = await likert(['Agree', 'Disagree', "Don't know"]);
      await setSide(context, 0, 'positive');
      await setSide(context, 1, 'negative');
      expect(valuesOf()).toEqual(['Agree', 'Disagree', "Don't know"]);

      await click(context, fields('scale-up')[1]);
      await click(context, fields('scale-remove')[2]);

      expect(valuesOf()).toEqual(['Disagree', 'Agree']);
      expect(config_(await create(context)).scale).toEqual([
        { value: 'Disagree', side: 'negative' },
        { value: 'Agree', side: 'positive' },
      ]);
    });

    it('keeps the keyboard on the value it moves with the up and down buttons, and on a neighbor when it removes one', async () => {
      const context = await likert(['Agree', 'Disagree', "Don't know"]);
      fields('scale-down')[0].focus();

      await click(context, fields('scale-down')[0]);
      expect(valuesOf()).toEqual(['Disagree', 'Agree', "Don't know"]);
      expect(document.activeElement).toBe(fields('scale-down')[1]); // still on the Down button of Agree, now the second
      await click(context, fields('scale-down')[1]);
      expect(valuesOf()).toEqual(['Disagree', "Don't know", 'Agree']);
      expect(document.activeElement).toBe(fields('scale-up')[2]); // Agree is last: its Down button is off, so the Up button
      await click(context, fields('scale-up')[2]);
      expect(document.activeElement).toBe(fields('scale-up')[1]);

      await click(context, fields('scale-remove')[2]);
      expect(valuesOf()).toEqual(['Disagree', 'Agree']);
      expect(document.activeElement).toBe(fields('scale-remove')[1]); // the last one went: the one before it
      await click(context, fields('scale-remove')[0]);
      expect(document.activeElement).toBe(fields('scale-remove')[0]); // the next one took its place
      await click(context, fields('scale-remove')[0]);
      expect(items()).toHaveLength(0);
      expect(document.activeElement).toBe(field('scale-new')); // nothing left to remove: the field that adds a value
    });

    it('accepts a value typed by hand, left out until it is marked', async () => {
      const context = await likert(['Agree', 'Disagree']);

      await change(context, field('scale-new'), 'Not sure');
      await click(context, field('scale-add'));

      expect(valuesOf()).toEqual(['Agree', 'Disagree', 'Not sure']);
      expect(sidesOf()).toEqual(['out', 'out', 'out']);
      expect(field<HTMLInputElement>('scale-new').value).toBe('');
      await change(context, field('scale-new'), 'Not sure'); // already there
      await click(context, field('scale-add'));
      expect(valuesOf()).toHaveLength(3);
    });

    it('names each control for a screen reader by its value, and reaches them all by keyboard', async () => {
      await likert(['Agree', 'Disagree']);

      const labels = [...fields('scale-up'), ...fields('scale-down'), ...fields('scale-remove'), ...fields('scale-side'), ...fields('scale-value')].map((c) => c.getAttribute('aria-label'));
      expect(labels).toEqual([
        'Move Agree up',
        'Move Disagree up',
        'Move Agree down',
        'Move Disagree down',
        'Remove Agree',
        'Remove Disagree',
        'Side of Agree',
        'Side of Disagree',
        'Value 1',
        'Value 2',
      ]);
      for (const control of [...fields('scale-down'), ...fields('scale-remove'), ...fields('scale-side'), ...fields('scale-value'), field('scale-add')]) {
        expect(control.tabIndex).toBeGreaterThanOrEqual(0); // native buttons, selects and inputs: in the tab order
      }
    });

    it('shows the rules of a scale before saving', async () => {
      const context = await likert(['A', 'B', 'C', 'D', 'E', 'F', 'G']);
      const scaleError = () => text(field('scale-error'));

      await click(context, field('dialog-save'));
      expect(scaleError()).toBe('A scale has 2 to 11 values.');

      for (let i = 0; i < 6; i++) await setSide(context, i, 'negative');
      await click(context, field('dialog-save'));
      expect(scaleError()).toBe('A scale has at most 5 values on one side.');

      await setSide(context, 5, 'positive');
      await setSide(context, 6, 'neutral');
      await click(context, field('dialog-save'));
      expect(scaleError()).toBe('Order the scale from negative to positive: the negative values, then the neutral one, then the positive ones.');

      await click(context, fields('scale-down')[5]); // A..E negative, G neutral, F positive -> in order
      await click(context, fields('scale-up')[6]);
      await setSide(context, 5, 'neutral');
      await setSide(context, 6, 'neutral');
      await click(context, field('dialog-save'));
      expect(scaleError()).toBe('A scale has at most one neutral value.');
      context.http.expectNone(isCreate);
    });

    it('refuses an empty value and a repeated value', async () => {
      const context = await likert(['A', 'B']);
      await setSide(context, 0, 'negative');
      await setSide(context, 1, 'positive');

      await change(context, fields('scale-value')[1], 'A');
      await click(context, field('dialog-save'));
      expect(text(field('scale-error'))).toBe('A value appears twice in the scale.');

      await change(context, fields('scale-value')[1], '  ');
      await click(context, field('dialog-save'));
      expect(text(field('scale-error'))).toBe('A value of the scale is empty.');
    });

    it('refuses a ratio, which a Likert widget cannot show', async () => {
      const context = await likert(['A', 'B']);
      await change(context, field('dialog-mode'), 'ratio');

      await click(context, field('dialog-save'));

      expect(errorOf('widgetNumerator')).toBe('A Likert widget shows shares of a measure, not a ratio.');
    });

    it('shows the server\'s refusal of a value of the scale at that value, and of the scale as a whole under it', async () => {
      const context = await likert(['A', 'B', 'C']);
      await setSide(context, 0, 'negative');
      await setSide(context, 2, 'positive');
      await click(context, field('dialog-save'));

      // The scale the server read is A and C: its second value is the third of the editor.
      context.http.expectOne(isCreate).flush({ title: 'x', errors: { 'config.scale[1]': ['Choose a side for this value.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(fields('scale-item').map((item) => text(item.querySelector('[data-testid="scale-item-error"]')))).toEqual(['', '', 'Choose a side for this value.']);
      expect(field('dialog-error')).toBeNull();

      await setSide(context, 1, 'neutral');
      await click(context, field('dialog-save'));
      context.http.expectOne(isCreate).flush({ title: 'x', errors: { 'config.scale': ['A scale has at most one neutral value.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);
      expect(text(field('scale-error'))).toBe('A scale has at most one neutral value.');
    });

    it('shows a saved scale in its order, and previews the widget with it', async () => {
      const scale = [
        { value: 'Disagree', side: 'negative' },
        { value: 'Agree', side: 'positive' },
      ] as const;
      const saved = widget('w1', { chartType: 'likert', config: config({ seriesBy: 'sex', scale: [...scale] }) });
      const context = await openDialog({ existing: saved, columns: twoDimensions() });

      expect(valuesOf()).toEqual(['Disagree', 'Agree']);
      expect(sidesOf()).toEqual(['negative', 'positive']);
      await wait(previewDelayMs + 100);
      await settle(context.harness);
      const requests = queries(context);
      const preview = requests.find((r) => typeOf(r) === 'likert')!;
      expect(preview.request.body.config.scale).toEqual(scale);
      const values = requests.find((r) => typeOf(r) === 'heatmap')!;
      preview.flush(seriesResult([cell('Cali', 'Disagree', 1), cell('Cali', 'Agree', 3)], ['Disagree', 'Agree']));
      values.flush(seriesResult([cell('Cali', 'Disagree', 1), cell('Cali', 'Agree', 3), cell('Cali', "Don't know", 1)], ['Agree', 'Disagree', "Don't know"]));
      await settle(context.harness);

      expect(all(dialog(), 'chart-mark')).toHaveLength(2);
      // The value of the data that the scale does not hold is offered, left out, after the scale's.
      expect(valuesOf()).toEqual(['Disagree', 'Agree', "Don't know"]);
      expect(sidesOf()).toEqual(['negative', 'positive', 'out']);
    });
  });
});

// A widget's color and scale are part of what it is saved with, not of its query (add-dashboard-palettes, design D3). The form keeps
// what the widget was saved with, for the kinds that take it, and saves nothing new for a widget that has neither.
describe('the widget dialog and a widget\'s display', () => {
  afterEach(clearOverlay);

  const twoDimensions = catalog({ columns: [...catalog().columns, { name: 'water_source', type: 'text', label: null }] });
  const saveEdited = async (context: Context) => {
    await click(context, field('dialog-save'));
    return context.http.expectOne(isPatch).request.body as { title: string; chartType: ChartType; config: WidgetConfig };
  };

  it('saves a widget saved before palettes with exactly the config it was saved with (Widget form saved unchanged)', async () => {
    const context = await openDialog({ existing: widget('w1') });

    const body = await saveEdited(context);

    expect(body.config).toEqual(config());
    expect('display' in body.config).toBe(false);
  });

  it('saves a widget with every text of the widget unchanged: subtitle, footnote, caption, unit and description button (Saved unchanged)', async () => {
    const display = {
      color: 3,
      subtitle: '¿En qué zona ha visto teatro en el último año?',
      footnote: 'No suman 100 %: respuesta múltiple.',
      caption: { text: '% de quienes asistieron' },
      unit: 'de quienes asistieron',
      showDescription: true as const,
    };
    const context = await openDialog({ existing: widget('w1', { config: config({ display }) }) });

    const body = await saveEdited(context);

    expect(body.config.display).toEqual(display);
    expect(body.config).toEqual(config({ display }));
  });

  it('keeps a hidden caption when the form is saved unchanged', async () => {
    const display = { caption: { hidden: true as const } };
    const context = await openDialog({ existing: widget('w1', { config: config({ display }) }) });

    const body = await saveEdited(context);

    expect(body.config.display).toEqual(display);
  });

  it('keeps the texts of a widget when it changes its chart type (the texts take every kind)', async () => {
    const display = { subtitle: 'Hogares por zona', footnote: 'Fuente propia.', unit: 'hogares' };
    const context = await openDialog({ existing: widget('w1', { config: config({ display }) }) });
    await change(context, field('dialog-chart-type'), 'pie');

    const body = await saveEdited(context);

    expect(body.chartType).toBe('pie');
    expect(body.config.display).toEqual(display);
  });

  it('keeps the preview from asking again for a widget with texts of its own', async () => {
    const context = await openDialog({ existing: widget('w1', { config: config({ display: { subtitle: 'Hogares por zona', unit: 'hogares' } }) }) });
    await wait(previewDelayMs + 100);
    await settle(context.harness);
    const previews = context.http.match(isQuery).filter((r) => !r.cancelled);

    expect(previews).toHaveLength(1);
    expect(previews[0].request.body.config).not.toHaveProperty('display');
    previews[0].flush(result([row('Cali', 75)]));
  });

  it('keeps the color of a chart with no series when it saves, and the preview is not asked again for it', async () => {
    const context = await openDialog({ existing: widget('w1', { config: config({ display: { color: 3 } }) }) });
    await wait(previewDelayMs + 100);
    await settle(context.harness);
    const previews = context.http.match(isQuery).filter((r) => !r.cancelled);
    expect(previews).toHaveLength(1);
    expect(previews[0].request.body.config).not.toHaveProperty('display');
    previews[0].flush(result([row('Cali', 75)]));
    await settle(context.harness);

    const body = await saveEdited(context);

    expect(body.config.display).toEqual({ color: 3 });
  });

  it('saves only the fields of the widget\'s kind: a column chart keeps no scale', async () => {
    const context = await openDialog({ existing: widget('w1', { config: config({ display: { color: 3, ramp: 'green' } }) }) });

    const body = await saveEdited(context);

    expect(body.config.display).toEqual({ color: 3 });
  });

  it('saves no color for a chart that gains a series', async () => {
    const context = await openDialog({ existing: widget('w1', { config: config({ display: { color: 3 } }) }), columns: twoDimensions });
    await change(context, field('dialog-series-by'), 'water_source');

    const body = await saveEdited(context);

    expect(body.config.seriesBy).toBe('water_source');
    expect('display' in body.config).toBe(false);
  });

  it('saves no color for a chart that changes into a pie', async () => {
    const context = await openDialog({ existing: widget('w1', { config: config({ display: { color: 3 } }) }) });
    await change(context, field('dialog-chart-type'), 'pie');

    const body = await saveEdited(context);

    expect(body.chartType).toBe('pie');
    expect('display' in body.config).toBe(false);
  });

  it('is given the dashboard\'s colors for its preview', async () => {
    const colors: DashboardColors = { palette: 'earth', valueColors: [{ column: 'municipality', value: 'Cali', color: 4 }] };
    await openDialog({ existing: widget('w1'), colors });

    const preview = (globalThis as unknown as { ng: { getComponent(element: Element): { resolvedColors(): { accent: string; kept(column: string, value: string): string | null } } } }).ng.getComponent(
      field('dialog-preview').querySelector('app-widget-view')!,
    );

    expect(preview.resolvedColors().accent).toBe('var(--tmz-pal-earth-1)');
    expect(preview.resolvedColors().kept('municipality', 'Cali')).toBe('var(--tmz-pal-earth-4)');
  });
});

// The Color and Scale choices of the widget form (add-dashboard-palettes, design D3, D9; dashboard-colors "Color choice in the widget form").
describe('the widget dialog\'s color and scale', () => {
  afterEach(clearOverlay);

  const optionsOfGroup = (group: string, option: string) => Array.from(field(group).querySelectorAll<HTMLInputElement>(`[data-testid="${option}"]`));
  const colorRadios = () => optionsOfGroup('dialog-color', 'dialog-color-option');
  const rampRadios = () => optionsOfGroup('dialog-ramp', 'dialog-ramp-option');
  const colorChosen = () => colorRadios().filter((radio) => radio.checked).map((radio) => Number(radio.dataset['color']));
  const rampChosen = () => rampRadios().filter((radio) => radio.checked).map((radio) => radio.dataset['ramp']);
  const colorNamed = (n: number) => colorRadios().find((radio) => radio.dataset['color'] === String(n))!;
  const rampNamed = (id: string) => rampRadios().find((radio) => radio.dataset['ramp'] === id)!;
  /** The name a radio in a label has for a screen reader: the label's own text, since the radio has no aria-label. */
  const labelText = (radio: HTMLInputElement) => text(radio.closest('label'));
  const marks = () => all(field('dialog-preview'), 'chart-mark');
  const colorOf = (element: Element | null) => /var\(--[\w-]+\)/.exec(element?.getAttribute('style') ?? '')?.[0] ?? null;
  const previewColors = () =>
    (globalThis as unknown as { ng: { getComponent(element: Element): { resolvedColors(): { accent: string; ramp: string; rampStyle: Record<string, string> } } } }).ng
      .getComponent(field('dialog-preview').querySelector('app-widget-view')!)
      .resolvedColors();
  const twoDimensions = catalog({ columns: [...catalog().columns, { name: 'water_source', type: 'text', label: null }] });
  const withColors = (palette: DashboardColors['palette'], valueColors: DashboardColors['valueColors'] = []): DashboardColors => ({ palette, valueColors });
  const groups = result([row('Cali', 75), row('Bogotá', 60), row('Pasto', 35)]);
  const isCreate = (r: { url: string; method: string }) => r.method === 'POST' && r.url === widgetsUrl;
  const saved = async (context: Context) => {
    await click(context, field('dialog-save'));
    return context.http.expectOne((r) => isCreate(r) || isPatch(r)).request.body as { title: string; chartType: ChartType; config: WidgetConfig };
  };

  describe('which chart types take which', () => {
    it.each(chartTypes)('offers %s the choices of its kind and no other', async (type) => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), type);

      const takesColor = ['bar', 'column', 'line', 'histogram', 'boxplot'].includes(type);
      const takesScale = ['map', 'heatmap'].includes(type);
      expect(fields('dialog-color')).toHaveLength(takesColor ? 1 : 0);
      expect(fields('dialog-color-option')).toHaveLength(takesColor ? 8 : 0);
      expect(fields('dialog-ramp')).toHaveLength(takesScale ? 1 : 0);
      expect(fields('dialog-ramp-option')).toHaveLength(takesScale ? 8 : 0);
      expect(context.http.match(isCreate)).toHaveLength(0);
    });

    it.each(['bar', 'column', 'line'])('takes the color of a %s back when it has a series, and gives it again without one', async (type) => {
      const context = await openDialog({ columns: twoDimensions });
      await change(context, field('dialog-chart-type'), type);
      expect(fields('dialog-color')).toHaveLength(1);

      await change(context, field('dialog-series-by'), 'water_source');
      expect(fields('dialog-color')).toHaveLength(0);
      expect(fields('dialog-ramp')).toHaveLength(0);

      await change(context, field('dialog-series-by'), '');
      expect(fields('dialog-color')).toHaveLength(1);
    });

    it('offers a heat map its scale with the series it needs', async () => {
      const context = await openDialog({ columns: twoDimensions });
      await change(context, field('dialog-chart-type'), 'heatmap');
      await change(context, field('dialog-series-by'), 'water_source');

      expect(fields('dialog-ramp')).toHaveLength(1);
      expect(fields('dialog-color')).toHaveLength(0);
    });
  });

  describe('the color', () => {
    it('offers the eight colors of the dashboard\'s palette by number, with the first chosen for a widget with no color (Widget saved before colors)', async () => {
      await openDialog({ colors: withColors('earth') });

      expect(text(field('dialog-color').querySelector('legend'))).toBe('Color');
      expect(colorRadios().map((radio) => radio.dataset['color'])).toEqual(['1', '2', '3', '4', '5', '6', '7', '8']);
      expect(colorChosen()).toEqual([1]);
      expect(colorRadios().map((radio) => colorOf(radio.closest('label')!.querySelector('[data-testid="dialog-color-swatch"]')))).toEqual(
        [1, 2, 3, 4, 5, 6, 7, 8].map((n) => `var(--tmz-pal-earth-${n})`),
      );
    });

    it('shows the preview in the color picked before the widget is saved, and asks nothing again for it (Color picked)', async () => {
      const context = await openDialog({ colors: withColors('tamiza') });
      await previewAnswer(context, groups);
      expect(marks().map(colorOf)).toEqual(Array(3).fill('var(--tmz-pal-tamiza-1)'));

      await click(context, colorNamed(3));
      expect(colorChosen()).toEqual([3]);
      expect(marks().map(colorOf)).toEqual(Array(3).fill('var(--tmz-pal-tamiza-3)'));

      await wait(previewDelayMs + 100);
      await settle(context.harness);
      expect(context.http.match(isQuery)).toHaveLength(0);
      expect(marks()).toHaveLength(3); // the answer stayed on screen: no loading state in between
      await change(context, field('dialog-title'), 'Coverage');
      expect((await saved(context)).config.display).toEqual({ color: 3 });
    });

    it('draws the preview in the dashboard\'s palette and its colors kept by value, the widget\'s color for the other bars', async () => {
      const context = await openDialog({ colors: withColors('earth', [{ column: 'municipality', value: 'Bogotá', color: 7 }]) });
      await previewAnswer(context, groups);
      expect(marks().map(colorOf)).toEqual(['var(--tmz-pal-earth-1)', 'var(--tmz-pal-earth-7)', 'var(--tmz-pal-earth-1)']);

      await click(context, colorNamed(4));

      expect(marks().map(colorOf)).toEqual(['var(--tmz-pal-earth-4)', 'var(--tmz-pal-earth-7)', 'var(--tmz-pal-earth-4)']);
    });

    it('names each color by its number and its hue for a screen reader, in the interface\'s language (Named choices)', async () => {
      await openDialog({ colors: withColors('earth') });
      expect(colorRadios().map((radio) => radio.getAttribute('aria-label'))).toEqual([
        'Color 1, terracotta',
        'Color 2, indigo',
        'Color 3, ochre',
        'Color 4, wine',
        'Color 5, moss',
        'Color 6, brown',
        'Color 7, plum',
        'Color 8, teal',
      ]);
    });

    it('names them in Spanish too', async () => {
      await openDialog({ colors: withColors('vivid'), lang: 'es' });

      expect(text(field('dialog-color').querySelector('legend'))).toBe('Color');
      expect(colorRadios().map((radio) => radio.getAttribute('aria-label'))).toEqual([
        'Color 1, púrpura',
        'Color 2, cian',
        'Color 3, lima',
        'Color 4, fucsia',
        'Color 5, verde',
        'Color 6, azul',
        'Color 7, carmesí',
        'Color 8, naranja',
      ]);
    });

    it('also says the name of the color chosen in text', async () => {
      const context = await openDialog({ colors: withColors('tamiza') });
      expect(text(field('dialog-color-chosen'))).toBe('Color 1, blue');

      await click(context, colorNamed(3));

      expect(text(field('dialog-color-chosen'))).toBe('Color 3, jade');
    });

    it('is a group of native radio inputs under one legend, reachable from the keyboard', async () => {
      await openDialog();

      expect(field('dialog-color').tagName).toBe('FIELDSET');
      const radios = colorRadios();
      expect(radios.every((radio) => radio.tagName === 'INPUT' && radio.type === 'radio')).toBe(true);
      expect(new Set(radios.map((radio) => radio.name)).size).toBe(1); // one group: the arrow keys move through it with no code of ours
      expect(radios.every((radio) => !radio.disabled && radio.tabIndex >= 0)).toBe(true);
      radios[2].focus();
      expect(document.activeElement).toBe(radios[2]);
    });

    it('opens a widget saved in a color on that color, and saves it unchanged (Widget form saved unchanged)', async () => {
      const context = await openDialog({ existing: widget('w1', { config: config({ display: { color: 5 } }) }) });
      expect(colorChosen()).toEqual([5]);

      expect((await saved(context)).config.display).toEqual({ color: 5 });
    });

    it('saves what was saved when a color is tried and the widget goes back to color 1', async () => {
      const context = await openDialog({ existing: widget('w1') });
      await click(context, colorNamed(3));
      await click(context, colorNamed(1));

      const body = await saved(context);

      expect(colorChosen()).toEqual([1]);
      expect(body.config).toEqual(config());
      expect('display' in body.config).toBe(false);
    });

    it('keeps color 1 when the widget was saved with it', async () => {
      const context = await openDialog({ existing: widget('w1', { config: config({ display: { color: 1 } }) }) });
      await click(context, colorNamed(3));
      await click(context, colorNamed(1));

      expect((await saved(context)).config.display).toEqual({ color: 1 });
    });

    it('sends the color of a new widget with it', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Coverage');
      await click(context, colorNamed(6));

      const body = await saved(context);

      expect(body.config.display).toEqual({ color: 6 });
    });

    it('keeps the color while the kind changes, and saves it only for a kind that takes one', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Coverage');
      await click(context, colorNamed(3));

      await change(context, field('dialog-chart-type'), 'pie');
      expect(fields('dialog-color')).toHaveLength(0);
      await change(context, field('dialog-chart-type'), 'histogram');
      expect(colorChosen()).toEqual([3]); // still there: the member can change back

      expect((await saved(context)).config.display).toEqual({ color: 3 });
    });

    it('saves no color for a kind that takes none, nor for a chart with a series', async () => {
      const context = await openDialog({ columns: twoDimensions });
      await change(context, field('dialog-title'), 'Coverage');
      await click(context, colorNamed(3));
      await change(context, field('dialog-series-by'), 'water_source');

      const body = await saved(context);

      expect(body.config.seriesBy).toBe('water_source');
      expect('display' in body.config).toBe(false);
    });

    it('shows next to the group what the server refused about the color, and clears it when another is picked', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Coverage');
      await click(context, colorNamed(3));
      await click(context, field('dialog-save'));

      context.http
        .expectOne(isCreate)
        .flush({ title: 'x', errors: { 'config.display.color': ['A column widget has no color of its own.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(field('dialog-color').querySelector('[data-testid="field-error"]'))).toBe('A column widget has no color of its own.');
      expect(field('dialog-color').getAttribute('aria-describedby')).toBe('widgetColor-error');
      expect(fields('dialog-error')).toHaveLength(0); // it is on the group, not repeated in the alert
      await click(context, colorNamed(4));
      expect(field('dialog-color').querySelector('[data-testid="field-error"]')).toBeNull();
    });
  });

  describe('the scale', () => {
    const map = (display?: WidgetConfig['display']) =>
      widget('w1', { chartType: 'map', config: config({ geometryColumn: 'geometry', ...(display ? { display } : {}) }) });

    it('offers the palette\'s scale first, then the seven scales as their steps, each named in text (Named choices)', async () => {
      await openDialog({ existing: map(), colors: withColors('earth') });

      expect(text(field('dialog-ramp').querySelector('legend'))).toBe('Scale');
      expect(rampRadios().map((radio) => radio.dataset['ramp'])).toEqual(['palette', 'blue', 'teal', 'green', 'orange', 'red', 'purple', 'gray']);
      expect(rampRadios().map(labelText)).toEqual(["Palette's scale Orange", 'Blue', 'Teal', 'Green', 'Orange', 'Red', 'Purple', 'Gray']);
      const gradients = all(field('dialog-ramp'), 'dialog-ramp-gradient').map((g) => g.style.backgroundImage);
      expect(gradients[0]).toBe(gradients[4]); // the palette's own scale is orange on Earth
      expect(gradients[3]).toBe(`linear-gradient(to right, ${[1, 2, 3, 4, 5, 6, 7].map((n) => `var(--tmz-ramp-green-${n})`).join(', ')})`);
      expect(gradients.every((g) => g.startsWith('linear-gradient'))).toBe(true);
    });

    it('keeps every gradient in the first row of its card, packed from the top, so they line up although the palette\'s scale has a caption more', async () => {
      await openDialog({ existing: map(), colors: withColors('earth') });

      // jsdom has no layout: this proves the structure that lines the gradients up; the alignment itself is checked in the browser.
      const cards = rampRadios().map((radio) => radio.closest('label')!.querySelector<HTMLElement>(':scope > span')!);
      expect(cards).toHaveLength(8);
      for (const card of cards) {
        // Rows packed from the top: a card as tall as the palette's one would otherwise spread (or center) its rows.
        expect(card.className).toContain('grid');
        expect(card.className).toContain('content-start');
        expect(card.firstElementChild!.getAttribute('data-testid')).toBe('dialog-ramp-gradient');
      }
      // The palette's caption comes after the scale's name, below the gradient, and no other card has one.
      expect(cards.map((card) => Array.from(card.children).map((child) => child.getAttribute('data-testid')))).toEqual([
        ['dialog-ramp-gradient', null, 'dialog-ramp-palette-scale'],
        ...Array(7).fill(['dialog-ramp-gradient', null]),
      ]);
    });

    it('names them in Spanish too', async () => {
      await openDialog({ existing: map(), colors: withColors('institutional'), lang: 'es' });

      expect(text(field('dialog-ramp').querySelector('legend'))).toBe('Escala');
      expect(rampRadios().map(labelText)).toEqual(['Escala de la paleta Verde azulado', 'Azul', 'Verde azulado', 'Verde', 'Naranja', 'Rojo', 'Morado', 'Gris']);
    });

    it('starts on the palette\'s scale for a widget with none (Palette\'s scale)', async () => {
      await openDialog({ existing: map(), colors: withColors('vivid') });

      expect(rampChosen()).toEqual(['palette']);
      expect(previewColors().ramp).toBe('purple');
    });

    it('redraws the preview on the scale picked without asking again, and saves it (Map on the green scale)', async () => {
      const context = await openDialog({ existing: map(), colors: withColors('tamiza') });
      await wait(previewDelayMs + 100);
      await settle(context.harness);
      context.http.match(isQuery).forEach((request) => request.flush(result([row('Cali', 75)])));
      await settle(context.harness);
      expect(previewColors().rampStyle['--tmz-seq-3']).toBe('var(--tmz-ramp-blue-3)');

      await click(context, rampNamed('green'));

      expect(rampChosen()).toEqual(['green']);
      expect(previewColors().ramp).toBe('green');
      expect(previewColors().rampStyle['--tmz-seq-3']).toBe('var(--tmz-ramp-green-3)');
      expect(previewColors().rampStyle['--tmz-seq-ink-5']).toBe('var(--tmz-ramp-green-ink-5)');
      await wait(previewDelayMs + 100);
      await settle(context.harness);
      expect(context.http.match(isQuery)).toHaveLength(0);
      expect((await saved(context)).config.display).toEqual({ ramp: 'green' });
    });

    it('goes back to the palette\'s scale, and then saves no scale at all', async () => {
      const context = await openDialog({ existing: map({ ramp: 'green' }), colors: withColors('tamiza') });
      expect(rampChosen()).toEqual(['green']);

      await click(context, rampNamed('palette'));
      const body = await saved(context);

      expect(rampChosen()).toEqual(['palette']);
      expect('display' in body.config).toBe(false);
    });

    it('keeps a scale chosen when the palette\'s own is chosen by name (the widget stays on that scale if the palette changes)', async () => {
      const context = await openDialog({ existing: map(), colors: withColors('tamiza') });

      await click(context, rampNamed('blue'));

      expect((await saved(context)).config.display).toEqual({ ramp: 'blue' });
    });

    it('opens a widget saved on a scale on that scale, and saves it unchanged (Widget form saved unchanged)', async () => {
      const context = await openDialog({ existing: map({ ramp: 'red' }) });
      expect(rampChosen()).toEqual(['red']);

      expect((await saved(context)).config.display).toEqual({ ramp: 'red' });
    });

    it('saves a map\'s scale and drops it for a kind that takes none', async () => {
      const context = await openDialog({ existing: map({ ramp: 'red' }) });
      await change(context, field('dialog-chart-type'), 'pie');

      expect('display' in (await saved(context)).config).toBe(false);
    });

    it('is a group of native radio inputs under one legend, reachable from the keyboard', async () => {
      await openDialog({ existing: map() });

      expect(field('dialog-ramp').tagName).toBe('FIELDSET');
      const radios = rampRadios();
      expect(radios).toHaveLength(8);
      expect(radios.every((radio) => radio.type === 'radio' && !radio.disabled && radio.tabIndex >= 0)).toBe(true);
      expect(new Set(radios.map((radio) => radio.name)).size).toBe(1);
    });

    it('shows next to the group what the server refused about the scale', async () => {
      const context = await openDialog({ existing: map({ ramp: 'green' }) });
      await click(context, field('dialog-save'));

      context.http
        .expectOne(isPatch)
        .flush({ title: 'x', errors: { 'config.display.ramp': ['Choose one of the scales.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(field('dialog-ramp').querySelector('[data-testid="field-error"]'))).toBe('Choose one of the scales.');
      expect(fields('dialog-error')).toHaveLength(0);
    });
  });
});

describe('the widget dialog for a box plot (add-boxplot-widget, design D7)', () => {
  afterEach(clearOverlay);

  const sized = indicator('household_size', { label: 'Household size', format: 'count', unit: 'people' });
  const box = (overrides: Partial<WidgetConfig> = {}) => config({ indicator: 'household_size', groupBy: 'municipality', ...overrides });
  const boxplot = (overrides: Partial<WidgetConfig> = {}) => widget('w1', { chartType: 'boxplot', config: box(overrides) });
  const orientationRadios = () => Array.from(field('dialog-orientation').querySelectorAll<HTMLInputElement>('input[type="radio"]'));
  const orientationChosen = () => orientationRadios().filter((radio) => radio.checked).map((radio) => radio.value);
  const saved = async (context: Context) => {
    await click(context, field('dialog-save'));
    return context.http.expectOne((r) => isCreate(r) || isPatch(r)).request.body as { title: string; chartType: ChartType; config: WidgetConfig };
  };
  const open_ = (existing?: Widget) => openDialog({ existing, indicators: [sized], columns: catalog({ columns: [...catalog().columns, { name: 'round', type: 'integer', label: null }] }) });
  const median = { by: 'value', column: null, compareAs: null, direction: 'desc' } as const;

  it('offers the group as optional, and says what it does without one', async () => {
    const context = await open_();
    await change(context, field('dialog-chart-type'), 'boxplot');

    expect(text(field('dialog-group-by').closest('tmz-form-field'))).toContain('Optional. Without it, a box plot draws one box over every row.');
    expect(field('dialog-series-by')).toBeNull();
  });

  it('says that a ratio is taken row by row', async () => {
    const context = await open_();
    await change(context, field('dialog-chart-type'), 'boxplot');
    await change(context, field('dialog-mode'), 'ratio');

    expect(text(field('dialog-denominator').closest('tmz-form-field'))).toContain('Each row’s numerator is divided by its own denominator');
  });

  it('orders the boxes by median only: the default or the median, a direction, and no column and no comparison', async () => {
    const context = await open_();
    await change(context, field('dialog-chart-type'), 'boxplot');

    expect(text(field('dialog-sort-by').closest('tmz-form-field'))).toContain('Order boxes by');
    expect(optionsOf('dialog-sort-by')).toEqual(['', 'value']);
    expect(optionTexts('dialog-sort-by')).toEqual(['Default order', 'Median']);
    expect(field('dialog-sort-direction')).toBeNull();
    await change(context, field('dialog-sort-by'), 'value');
    expect(field('dialog-sort-direction')).not.toBeNull();
    expect(field('dialog-sort-compare-as')).toBeNull();
    expect((field('dialog-sort-direction') as HTMLSelectElement).value).toBe('desc');
  });

  it('saves an order by median descending as the plotted value, with no column', async () => {
    const context = await open_();
    await change(context, field('dialog-title'), 'Spread');
    await change(context, field('dialog-chart-type'), 'boxplot');
    await change(context, field('dialog-sort-by'), 'value');

    const body = await saved(context);

    expect(body.chartType).toBe('boxplot');
    expect(body.config.sort).toEqual(median);
  });

  it('reads an order by a column left from a bar as the default order, and saves no order', async () => {
    const context = await open_(widget('w1', { chartType: 'bar', config: box({ sort: { by: 'column', column: 'round', compareAs: 'number', direction: 'asc' } }) }));
    await change(context, field('dialog-chart-type'), 'boxplot');

    expect((field('dialog-sort-by') as HTMLSelectElement).value).toBe('');
    expect(field('dialog-sort-direction')).toBeNull();
    expect(field('dialog-sort-compare-as')).toBeNull();
    expect((await saved(context)).config.sort).toBeNull();
  });

  it('clears the group after an order by median: the field says why it is off, and the preview and the save carry no order', async () => {
    const context = await open_(boxplot({ sort: median }));
    expect((field('dialog-sort-by') as HTMLSelectElement).value).toBe('value');

    await change(context, field('dialog-group-by'), '');

    expect((field('dialog-sort-by') as HTMLSelectElement).disabled).toBe(true);
    expect(text(field('dialog-sort-by').closest('tmz-form-field'))).toContain('Choose a column to group by to order the boxes.');
    const query = await previewAnswer(context, result([row(null, 4.5)]));
    expect(query.chartType).toBe('boxplot');
    expect(query.config.sort).toBeNull();
    expect((await saved(context)).config.sort).toBeNull();
  });

  it('asks for the preview with the box plot chart type', async () => {
    const context = await open_();
    await change(context, field('dialog-chart-type'), 'boxplot');

    const query = await previewAnswer(context, result([row('Cali', 4.5)]));

    expect(query.chartType).toBe('boxplot');
  });

  it('opens a box plot with no orientation on Vertical, and leaves it without one when Vertical is picked again', async () => {
    const context = await open_(boxplot());
    expect(orientationChosen()).toEqual(['vertical']);

    await click(context, field('dialog-orientation-horizontal'));
    expect(orientationChosen()).toEqual(['horizontal']);
    await click(context, field('dialog-orientation-vertical'));

    const body = await saved(context);
    expect(orientationChosen()).toEqual(['vertical']);
    expect('display' in body.config).toBe(false);
  });

  it('saves the orientation of a new box plot', async () => {
    const context = await open_();
    await change(context, field('dialog-title'), 'Spread');
    await change(context, field('dialog-chart-type'), 'boxplot');
    await click(context, field('dialog-orientation-horizontal'));

    expect((await saved(context)).config.display).toEqual({ orientation: 'horizontal' });
  });

  it('opens a horizontal box plot on Horizontal and saves it unchanged', async () => {
    const config_ = box({ display: { color: 4, orientation: 'horizontal' } });
    const context = await open_(widget('w1', { chartType: 'boxplot', config: config_ }));
    expect(orientationChosen()).toEqual(['horizontal']);

    expect((await saved(context)).config).toEqual(config_);
  });

  it('keeps a saved Vertical when it is picked again, so a form saved unchanged stays unchanged', async () => {
    const config_ = box({ display: { orientation: 'vertical' } });
    const context = await open_(widget('w1', { chartType: 'boxplot', config: config_ }));
    await click(context, field('dialog-orientation-horizontal'));
    await click(context, field('dialog-orientation-vertical'));

    expect((await saved(context)).config.display).toEqual({ orientation: 'vertical' });
  });

  it('redraws the preview when the orientation changes and asks for no rows again', async () => {
    const context = await open_(boxplot());
    const boxes = [{ group: 'Cali', values: 5, min: 3, q1: 4, median: 4.5, q3: 5, max: 6, mean: 4.5, lowerWhisker: 3, upperWhisker: 6, outlierCount: 0, outliers: [] }];
    await previewAnswer(context, result([row('Cali', 4.5)], { boxes }));

    const median = () => field('dialog-preview').querySelector('[data-testid="box-median"]')!;
    expect(median().getAttribute('y1')).toBe(median().getAttribute('y2')); // vertical: the median runs across

    await click(context, field('dialog-orientation-horizontal'));
    await wait(previewDelayMs + 100);
    await settle(context.harness);

    expect(context.http.match(isQuery).filter((r) => !r.cancelled)).toHaveLength(0);
    expect(median().getAttribute('x1')).toBe(median().getAttribute('x2')); // horizontal: redrawn from the same rows
  });

  it('drops the orientation when the kind changes away from a box plot', async () => {
    const context = await open_(widget('w1', { chartType: 'boxplot', config: box({ display: { orientation: 'horizontal' } }) }));
    await change(context, field('dialog-chart-type'), 'column');

    expect(field('dialog-orientation')).toBeNull();
    const body = await saved(context);
    expect(body.chartType).toBe('column');
    expect('display' in body.config).toBe(false);
  });

  it('offers the eight colors of the palette to a box plot', async () => {
    const context = await open_();
    await change(context, field('dialog-chart-type'), 'boxplot');

    expect(fields('dialog-color-option')).toHaveLength(8);
  });

  it('shows next to the orientation what the server refused about it, and clears it when another is picked', async () => {
    const context = await open_(boxplot({ display: { orientation: 'horizontal' } }));
    await click(context, field('dialog-save'));

    context.http
      .expectOne(isPatch)
      .flush({ title: 'x', errors: { 'config.display.orientation': ['The orientation must be vertical or horizontal.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(context.harness);

    expect(text(field('dialog-orientation').querySelector('[data-testid="field-error"]'))).toBe('The orientation must be vertical or horizontal.');
    expect(field('dialog-orientation').getAttribute('aria-describedby')).toBe('widgetOrientation-error');
    expect(fields('dialog-error')).toHaveLength(0);
    await click(context, field('dialog-orientation-vertical'));
    expect(field('dialog-orientation').querySelector('[data-testid="field-error"]')).toBeNull();
  });
});

describe('the widget dialog column names (add-indicator-column-metadata, design D9)', () => {
  afterEach(clearOverlay);

  const ratio = { numerator: 'numerator', denominator: 'denominator' };
  /** `coverage`, labelled, with the names a member typed for the ratio's two columns and its dimension. */
  const teatro = indicator('coverage', {
    label: 'Coverage',
    columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' }, municipality: { label: 'Comuna' } },
  });
  const withRound = catalog({ columns: [...catalog().columns, { name: 'estrato', type: 'text', label: null }] });
  const saved = async (context: Context) => {
    await click(context, field('dialog-save'));
    return context.http.expectOne((r) => isCreate(r) || isPatch(r)).request.body as { title: string; chartType: ChartType; config: WidgetConfig };
  };
  const labelFields = () => fields('dialog-label') as HTMLInputElement[];
  const labelOf = (column: string) => labelFields().find((input) => input.dataset['column'] === column)!;
  const edit = (overrides: Partial<WidgetConfig>, chartType: ChartType = 'column') => widget('w1', { chartType, config: config(overrides) });
  const edited = (existing: Widget, extra: Setup = {}) => openDialog({ existing, indicators: [teatro, indicator('target')], columns: withRound, ...extra });

  it('offers a field for each column the widget shows, with the names of the indicator as placeholders (Labels offered)', async () => {
    const context = await edited(edit({ ratio }));

    expect(labelFields().map((input) => input.dataset['column'])).toEqual(['numerator', 'denominator', 'municipality']);
    expect(labelFields().map((input) => input.placeholder)).toEqual(['Hogares que asistieron', 'Hogares', 'Comuna']);
    expect(labelFields().map((input) => input.value)).toEqual(['', '', '']);
    expect(labelFields().every((input) => input.maxLength === 80)).toBe(true);
    expect(text(field('dialog-labels').querySelector('h3'))).toBe('Column names');
    expect(context.http.match(isQuery).filter((r) => !r.cancelled).length).toBeLessThanOrEqual(1);
  });

  it('offers the measure when the widget names one, and the group and series columns of a chart that has them', async () => {
    const context = await edited(edit({ measure: 'round', seriesBy: 'estrato' }));

    expect(labelFields().map((input) => input.dataset['column'])).toEqual(['round', 'municipality', 'estrato']);
    await change(context, field('dialog-chart-type'), 'stat');
    expect(labelFields().map((input) => input.dataset['column'])).toEqual(['round']);
    await change(context, field('dialog-chart-type'), 'pie');
    expect(labelFields().map((input) => input.dataset['column'])).toEqual(['round', 'municipality']);
  });

  it('offers a column nothing but the default measure when the widget names none, and the placeholder is the column\'s own name when its indicator has no name for it', async () => {
    await edited(edit({}), { indicators: [indicator('coverage'), indicator('target')] });

    expect(labelFields().map((input) => [input.dataset['column'], input.placeholder])).toEqual([['municipality', 'municipality']]);
  });

  it('shows what is typed in the preview without asking for the rows again, and saves it (Preview without a query)', async () => {
    const context = await edited(edit({ ratio }));
    await previewAnswer(context, result([row('Cali', 0.5)]));
    const caption = () => text(overlay().querySelector('[data-testid="dialog-preview"] [data-testid="chart-caption"]'));
    expect(caption()).toBe('Coverage: Hogares que asistieron / Hogares by Comuna');

    await change(context, labelOf('denominator'), 'Hogares de la base E1');
    await wait(previewDelayMs + 100);
    await settle(context.harness);

    expect(context.http.match(isQuery).filter((r) => !r.cancelled)).toHaveLength(0);
    expect(caption()).toBe('Coverage: Hogares que asistieron / Hogares de la base E1 by Comuna');
    expect((await saved(context)).config.display).toEqual({ labels: { denominator: 'Hogares de la base E1' } });
  });

  it('saves the labels of a widget it did not touch as they were saved, the ones of columns it does not show too (Saved unchanged)', async () => {
    const context = await edited(edit({ display: { labels: { numerator: 'Hogares que asistieron', estrato: 'Estrato del hogar' } } }));

    const body = await saved(context);

    expect(JSON.stringify(body.config.display)).toBe('{"labels":{"numerator":"Hogares que asistieron","estrato":"Estrato del hogar"}}');
  });

  it('keeps the labels of every column it saved when only the chart type changes (Chart type changed)', async () => {
    const context = await edited(edit({ display: { labels: { municipality: 'Comuna', numerator: 'Hogares que asistieron' } } }));
    await change(context, field('dialog-chart-type'), 'bar');
    expect((await saved(context)).config.display).toEqual({ labels: { municipality: 'Comuna', numerator: 'Hogares que asistieron' } });
  });

  it('keeps the labels of a column chart turned into a stat tile, the group column\'s too (Chart type changed)', async () => {
    const context = await edited(edit({ display: { labels: { municipality: 'Comuna' } } }));
    await change(context, field('dialog-chart-type'), 'stat');

    const body = await saved(context);

    expect(body.chartType).toBe('stat');
    expect(body.config.display).toEqual({ labels: { municipality: 'Comuna' } });
  });

  it('drops the label of a column the member replaced (Column replaced)', async () => {
    const context = await edited(edit({ display: { labels: { municipality: 'Comuna' } } }));

    await change(context, field('dialog-group-by'), 'estrato');
    const body = await saved(context);

    expect(body.config.groupBy).toBe('estrato');
    expect('display' in body.config).toBe(false);
  });

  it('holds only the labels of the columns it shows once a label is typed, and brings back the others with their column', async () => {
    const context = await edited(edit({ ratio, display: { labels: { estrato: 'Estrato del hogar', numerator: 'Hogares que asistieron' } } }));

    await change(context, labelOf('denominator'), 'Hogares');
    const body = await saved(context);

    expect(body.config.display).toEqual({ labels: { numerator: 'Hogares que asistieron', denominator: 'Hogares' } });
  });

  it('saves no display for a label emptied again', async () => {
    const context = await edited(edit({ ratio, display: { labels: { numerator: 'Hogares que asistieron' } } }));

    await change(context, labelOf('numerator'), '   ');
    const body = await saved(context);

    expect('display' in body.config).toBe(false);
  });

  it('adds a widget with its labels, besides its color', async () => {
    const context = await openDialog({ indicators: [teatro], columns: withRound });
    await change(context, field('dialog-title'), 'Asistencia');
    await change(context, field('dialog-mode'), 'ratio');
    await change(context, labelOf('numerator'), 'Asistentes');

    const body = await saved(context);

    expect(body.config.display).toEqual({ labels: { numerator: 'Asistentes' } });
    expect(body.config.ratio).toEqual(ratio);
  });

  it('proposes the name the indicator gives a tooltip column that has no label of its own (Tooltip column placeholder)', async () => {
    const registros = indicator('coverage', { label: 'Coverage', columns: { value: { label: 'Registros recibidos' } } });
    const context = await openDialog({ indicators: [registros], columns: catalog() });
    await change(context, field('dialog-chart-type'), 'map');
    await change(context, field('dialog-geometry'), 'geometry');
    await click(context, field('tooltip-add'));
    expect((fields('tooltip-label')[0] as HTMLInputElement).placeholder).toBe('Label (optional)');

    await change(context, fields('tooltip-column')[0], 'value');
    expect((fields('tooltip-label')[0] as HTMLInputElement).placeholder).toBe('Registros recibidos');
    await change(context, fields('tooltip-column')[0], 'round');
    expect((fields('tooltip-label')[0] as HTMLInputElement).placeholder).toBe('round');
  });

  it('names the columns of the form as they are, whatever the widget calls them (Filters keep the column\'s name)', async () => {
    const filter: WidgetConfig['filters'][number] = { column: 'municipality', op: 'in', values: ['Cali'] };
    const context = await edited(edit({ filters: [filter], display: { labels: { municipality: 'Comuna' } } }));

    expect(optionsOf('dialog-group-by')).toContain('municipality');
    expect(optionTexts('dialog-group-by')).toContain('municipality');
    expect(optionTexts('dialog-group-by')).not.toContain('Comuna');
    expect(Array.from(field<HTMLSelectElement>('filter-column').options).map((o) => o.value)).toContain('municipality');
    expect(fields('dialog-filter-edit')[0].getAttribute('aria-label')).toContain('municipality');
    expect(text(fields('dialog-filter-edit')[0])).toContain('municipality');
    expect((await saved(context)).config.filters).toEqual([filter]);
  });

  it('shows what the server refused about the labels, next to the group and next to the field (Too many labels, Label too long)', async () => {
    const context = await edited(edit({ ratio, display: { labels: { numerator: 'x'.repeat(81) } } }));
    await click(context, field('dialog-save'));

    context.http.expectOne(isPatch).flush(
      {
        title: 'x',
        errors: { 'config.display.labels': ['A widget labels at most 10 columns.'], 'config.display.labels.numerator': ['A column label can have up to 80 characters.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(context.harness);

    expect(text(field('labels-error'))).toBe('A widget labels at most 10 columns.');
    expect(text(labelOf('numerator').closest('tmz-form-field'))).toContain('up to 80 characters');
    expect(text(labelOf('denominator').closest('tmz-form-field'))).not.toContain('80 characters');
    expect(fields('dialog-error')).toHaveLength(0);
    expect(field('dialog-labels').querySelector('details')!.open).toBe(true);
    await change(context, labelOf('numerator'), 'Hogares');
    expect(text(labelOf('numerator').closest('tmz-form-field'))).not.toContain('80 characters');
  });

  it('opens the group only for a widget that has names of its own', async () => {
    await edited(edit({ ratio }));
    expect(field('dialog-labels').querySelector('details')!.open).toBe(false);
  });

  it('opens the group for a widget that has names of its own', async () => {
    await edited(edit({ ratio, display: { labels: { numerator: 'Hogares' } } }));
    expect(field('dialog-labels').querySelector('details')!.open).toBe(true);
  });
});

// A widget's words (add-widget-context, design D11): subtitle, caption, unit, footnote and the description button.
describe('the widget dialog text on the widget (add-widget-context, design D11)', () => {
  afterEach(clearOverlay);

  const saved = async (context: Context) => {
    await click(context, field('dialog-save'));
    return context.http.expectOne((r) => isCreate(r) || isPatch(r)).request.body as { title: string; chartType: ChartType; config: WidgetConfig };
  };
  const edit = (display: NonNullable<WidgetConfig['display']> | undefined, overrides: Partial<Widget> = {}) =>
    widget('w1', { config: config(display ? { display } : {}), ...overrides });
  const radio = (mode: string) => field<HTMLInputElement>(`dialog-caption-${mode}`);
  const typeAndSettle = (context: Context, id: string, value: string) => change(context, field(id), value);
  const refuse = async (context: Context, errors: Record<string, string[]>) => {
    await click(context, field('dialog-save'));
    context.http.expectOne(isPatch).flush({ title: 'x', errors }, { status: 400, statusText: 'Bad Request' });
    await settle(context.harness);
  };
  const warning = () => dialog().querySelector('[data-testid="dialog-context-warning"]');
  /** A pasted block of text: the stat tile's size is 3x2, a column chart's 6x4. */
  const wordy = (length: number) => 'palabra '.repeat(Math.ceil(length / 8)).slice(0, length);

  it('offers the subtitle under the title, and a group of fields after the colors (every field is reached with the keyboard)', async () => {
    await openDialog({ indicators: [indicator('coverage', { description: 'About coverage' })] });

    const ids = ['dialog-title', 'dialog-subtitle', 'dialog-caption-default', 'dialog-caption-text', 'dialog-caption-hidden', 'dialog-unit', 'dialog-footnote', 'dialog-show-description'];
    const present = ids.filter((id) => fields(id).length).map((id) => field(id));
    expect(present.map((e) => e.dataset['testid'])).toEqual(ids);
    // The caption's own text field only exists under "Own text".
    expect(fields('dialog-caption-own')).toHaveLength(0);
    for (const control of present) {
      expect((control as HTMLInputElement).disabled).toBe(false);
      expect(control.tabIndex).toBe(0);
    }
    // In the order the form reads, the subtitle follows the title, and the group of texts is its own fieldset.
    for (let i = 1; i < present.length; i++) {
      expect(present[i - 1].compareDocumentPosition(present[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
    expect(field('dialog-subtitle').getAttribute('maxlength')).toBe('200');
    expect(field('dialog-footnote').getAttribute('maxlength')).toBe('500');
    expect(field('dialog-unit').getAttribute('maxlength')).toBe('32');
    expect(field('dialog-footnote').tagName).toBe('TEXTAREA');
    expect(field('dialog-context').querySelector('legend')!.textContent!.trim()).toBe('Text on the widget');
    expect(field('dialog-subtitle').closest('tmz-form-field')!.querySelector('label')!.textContent!.trim()).toBe('Subtitle');
  });

  it('reaches the text of an own caption with the keyboard too, once Own text is chosen', async () => {
    const context = await openDialog();
    expect(fields('dialog-caption-own')).toHaveLength(0);

    await click(context, radio('text'));

    expect(field('dialog-caption-own').getAttribute('maxlength')).toBe('200');
    expect(field<HTMLInputElement>('dialog-caption-own').tabIndex).toBe(0);
    expect(text(field('dialog-caption-own').closest('tmz-form-field')!.querySelector('label'))).toBe('Caption text');
  });

  it('starts every field as the widget was saved: the default caption, and no unit, footnote or button for a widget saved before', async () => {
    await openDialog({ existing: edit(undefined) });

    expect(radio('default').checked).toBe(true);
    expect(radio('text').checked).toBe(false);
    expect(radio('hidden').checked).toBe(false);
    expect(field<HTMLInputElement>('dialog-subtitle').value).toBe('');
    expect(field<HTMLInputElement>('dialog-unit').value).toBe('');
    expect(field<HTMLTextAreaElement>('dialog-footnote').value).toBe('');
    expect(field<HTMLInputElement>('dialog-show-description').checked).toBe(false);
  });

  it('shows what the widget was saved with in each field, and an own caption in its text field', async () => {
    await openDialog({
      existing: edit({ subtitle: 'Hogares por zona', footnote: 'Sin ponderar.', caption: { text: '% de quienes asistieron' }, unit: 'de quienes asistieron' }),
    });

    expect(field<HTMLInputElement>('dialog-subtitle').value).toBe('Hogares por zona');
    expect(field<HTMLTextAreaElement>('dialog-footnote').value).toBe('Sin ponderar.');
    expect(field<HTMLInputElement>('dialog-unit').value).toBe('de quienes asistieron');
    expect(radio('text').checked).toBe(true);
    expect(field<HTMLInputElement>('dialog-caption-own').value).toBe('% de quienes asistieron');
  });

  it('shows a hidden caption as chosen', async () => {
    await openDialog({ existing: edit({ caption: { hidden: true } }) });

    expect(radio('hidden').checked).toBe(true);
    expect(fields('dialog-caption-own')).toHaveLength(0);
  });

  it('says what the indicator\'s unit is next to the unit field (Unit hint)', async () => {
    await openDialog({ indicators: [indicator('coverage', { unit: 'de los hogares' })] });

    expect(text(field('dialog-unit').closest('tmz-form-field'))).toContain('The indicator\'s unit is “de los hogares”.');
  });

  it('says so when the indicator has no unit', async () => {
    await openDialog({ indicators: [indicator('coverage', { unit: null })] });

    expect(text(field('dialog-unit').closest('tmz-form-field'))).toContain('The indicator has no unit.');
  });

  it('names the unit in Spanish in the Spanish interface', async () => {
    await openDialog({ indicators: [indicator('coverage', { unit: 'de los hogares' })], lang: 'es' });

    expect(text(field('dialog-unit').closest('tmz-form-field'))).toContain('La unidad del indicador es «de los hogares».');
    expect(text(field('dialog-context').querySelector('legend'))).toBe('Textos del widget');
  });

  it('makes the description option unavailable, and says the indicator has none (Description option unavailable)', async () => {
    await openDialog({ indicators: [indicator('coverage', { description: null })] });

    expect(field<HTMLInputElement>('dialog-show-description').disabled).toBe(true);
    expect(text(field('dialog-show-description-help'))).toBe('The indicator has no description.');
  });

  it('offers the description option when the indicator has a description', async () => {
    const context = await openDialog({ indicators: [indicator('coverage', { description: 'Share of households.' })] });
    await change(context, field('dialog-title'), 'Asistencia');

    expect(field<HTMLInputElement>('dialog-show-description').disabled).toBe(false);
    await click(context, field('dialog-show-description'));
    expect((await saved(context)).config.display).toEqual({ showDescription: true });
  });

  it('lets a widget saved with the option on turn it off though its indicator has lost its description', async () => {
    const context = await openDialog({ existing: edit({ showDescription: true }) });

    const box = field<HTMLInputElement>('dialog-show-description');
    expect(box.disabled).toBe(false);
    expect(box.checked).toBe(true);
    await click(context, box);
    expect('display' in (await saved(context)).config).toBe(false);
  });

  it('reads the line an own caption text makes as "{text} by {group}" (the caption\'s hint)', async () => {
    const context = await openDialog();
    await click(context, radio('text'));

    await typeAndSettle(context, 'dialog-caption-own', '% de quienes asistieron');

    expect(text(field('dialog-caption-own').closest('tmz-form-field'))).toContain('Reads: % de quienes asistieron by municipality');
  });

  it('reads the caption of a stat tile as its text alone', async () => {
    const context = await openDialog();
    await change(context, field('dialog-chart-type'), 'stat');
    await click(context, radio('text'));

    await typeAndSettle(context, 'dialog-caption-own', 'de quienes asistieron');

    expect(text(field('dialog-caption-own').closest('tmz-form-field'))).toContain('Reads: de quienes asistieron');
    expect(text(field('dialog-caption-own').closest('tmz-form-field'))).not.toContain(' by ');
  });

  it('shows the subtitle and the footnote in the preview and hides the caption, without asking for the rows again (Preview without a query)', async () => {
    const context = await openDialog({ existing: edit(undefined) });
    await previewAnswer(context, result([row('Cali', 75)]));
    const preview = () => field('dialog-preview');
    expect(preview().querySelector('[data-testid="widget-footnote"]')).toBeNull();
    expect(preview().querySelector('[data-testid="chart-caption"]')!.classList.contains('sr-only')).toBe(false);

    await typeAndSettle(context, 'dialog-subtitle', '¿Quién fue al evento?');
    await typeAndSettle(context, 'dialog-footnote', 'No suman 100 %: respuesta múltiple.');
    await click(context, radio('hidden'));
    await wait(previewDelayMs + 100);
    await settle(context.harness);

    expect(context.http.match(isQuery).filter((r) => !r.cancelled)).toHaveLength(0);
    expect(text(preview().querySelector('[data-testid="dialog-preview-subtitle"]'))).toBe('¿Quién fue al evento?');
    expect(text(preview().querySelector('[data-testid="widget-footnote"]'))).toBe('No suman 100 %: respuesta múltiple.');
    expect(preview().querySelector('[data-testid="chart-caption"]')!.classList.contains('sr-only')).toBe(true);
  });

  it('shows an own caption text, a unit and the subtitle of a new widget in the preview as typed', async () => {
    const context = await openDialog();
    await typeAndSettle(context, 'dialog-subtitle', 'Hogares por zona');
    await click(context, radio('text'));
    await typeAndSettle(context, 'dialog-caption-own', 'Hogares con niños');
    await previewAnswer(context, result([row('Cali', 12450)]));

    expect(text(field('dialog-preview').querySelector('[data-testid="dialog-preview-subtitle"]'))).toBe('Hogares por zona');
    expect(text(field('dialog-preview').querySelector('[data-testid="chart-caption"]'))).toBe('Hogares con niños by municipality');

    await typeAndSettle(context, 'dialog-unit', 'hogares');
    expect(context.http.match(isQuery).filter((r) => !r.cancelled)).toHaveLength(0);
    expect(all(field('dialog-preview'), 'chart-mark').length).toBeGreaterThan(0);
  });

  it('saves the texts of a new widget trimmed, besides its other settings', async () => {
    const context = await openDialog();
    await change(context, field('dialog-title'), 'Asistencia');
    await typeAndSettle(context, 'dialog-subtitle', '  ¿Quién fue al evento?  ');
    await typeAndSettle(context, 'dialog-footnote', '  Sin ponderar.  ');
    await typeAndSettle(context, 'dialog-unit', ' de quienes asistieron ');
    await click(context, radio('text'));
    await typeAndSettle(context, 'dialog-caption-own', ' % de quienes asistieron ');

    const body = await saved(context);

    expect(body.config.display).toEqual({
      subtitle: '¿Quién fue al evento?',
      footnote: 'Sin ponderar.',
      caption: { text: '% de quienes asistieron' },
      unit: 'de quienes asistieron',
    });
  });

  it('saves a hidden caption as hidden, and the default caption as nothing', async () => {
    const context = await openDialog({ existing: edit(undefined) });
    await click(context, radio('hidden'));
    expect((await saved(context)).config.display).toEqual({ caption: { hidden: true } });
  });

  it('saves nothing for a caption that goes back to Default, with the text typed kept out', async () => {
    const context = await openDialog({ existing: edit(undefined) });
    await click(context, radio('text'));
    await typeAndSettle(context, 'dialog-caption-own', 'Hogares con niños');
    await click(context, radio('default'));

    expect('display' in (await saved(context)).config).toBe(false);
  });

  it('saves no caption for an own text left empty or blank', async () => {
    const context = await openDialog({ existing: edit(undefined) });
    await click(context, radio('text'));
    await typeAndSettle(context, 'dialog-caption-own', '   ');

    expect('display' in (await saved(context)).config).toBe(false);
  });

  it('saves a pasted line break as a space', async () => {
    const context = await openDialog({ existing: edit(undefined) });
    await typeAndSettle(context, 'dialog-footnote', 'No suman 100 %:\nrespuesta múltiple.\r\n\tVer nota.');

    expect((await saved(context)).config.display).toEqual({ footnote: 'No suman 100 %: respuesta múltiple. Ver nota.' });
  });

  it('saves a widget with a subtitle and nothing else changed as it was saved (Saved unchanged)', async () => {
    const display = { color: 2, subtitle: 'Hogares por zona', caption: { hidden: true as const } };
    const context = await openDialog({ existing: edit(display) });

    const body = await saved(context);

    expect(body.config).toEqual(config({ display }));
    expect(JSON.stringify(body.config.display)).toBe(JSON.stringify(display));
  });

  it('saves a column chart in color 2 saved before these texts with no new key (Form saved unchanged before)', async () => {
    const context = await openDialog({ existing: edit({ color: 2 }) });

    const body = await saved(context);

    expect(body.config.display).toEqual({ color: 2 });
    expect(Object.keys(body.config.display!)).toEqual(['color']);
  });

  it('changes only the unit, and sends it (Unit changed)', async () => {
    const context = await openDialog({ existing: edit({ unit: 'hogares' }) });
    await typeAndSettle(context, 'dialog-unit', 'de los hogares');

    expect((await saved(context)).config.display).toEqual({ unit: 'de los hogares' });
  });

  describe('a refusal', () => {
    it('shows a refused subtitle on the subtitle field and not on the title (Error on its field)', async () => {
      const context = await openDialog({ existing: edit({ subtitle: 'x'.repeat(5) }) });

      await refuse(context, { 'config.display.subtitle': ['The subtitle is longer than 200 characters.'] });

      expect(text(field('dialog-subtitle').closest('tmz-form-field'))).toContain('The subtitle is longer than 200 characters.');
      expect(text(field('dialog-title').closest('tmz-form-field'))).not.toContain('subtitle');
      expect(field('dialog-title').getAttribute('aria-invalid')).toBeNull();
      expect(field('dialog-subtitle').getAttribute('aria-invalid')).toBe('true');
      expect(fields('dialog-error')).toHaveLength(0);
    });

    it('shows a refused footnote and a refused unit on their fields', async () => {
      const context = await openDialog({ existing: edit({ footnote: 'Nota.', unit: 'hogares' }) });

      await refuse(context, {
        'config.display.footnote': ['The footnote is longer than 500 characters.'],
        'config.display.unit': ['The unit is longer than 32 characters.'],
      });

      expect(text(field('dialog-footnote').closest('tmz-form-field'))).toContain('The footnote is longer than 500 characters.');
      expect(text(field('dialog-unit').closest('tmz-form-field'))).toContain('The unit is longer than 32 characters.');
      expect(fields('dialog-error')).toHaveLength(0);
    });

    it('shows a refused caption on the caption\'s radios', async () => {
      const context = await openDialog({ existing: edit({ caption: { text: 'Hogares' } }) });

      await refuse(context, { 'config.display.caption': ['Give the caption its own text or hide it, not both.'] });

      expect(text(field('dialog-caption').querySelector('[data-testid="field-error"]'))).toBe('Give the caption its own text or hide it, not both.');
      expect(field('dialog-caption').getAttribute('aria-describedby')).toBe('widgetCaption-error');
      expect(fields('dialog-error')).toHaveLength(0);
    });

    it('shows a refused caption text on the text field', async () => {
      const context = await openDialog({ existing: edit({ caption: { text: 'Hogares' } }) });

      await refuse(context, { 'config.display.caption.text': ['The caption is longer than 200 characters.'] });

      expect(text(field('dialog-caption-own').closest('tmz-form-field'))).toContain('The caption is longer than 200 characters.');
      expect(field('dialog-caption').querySelector('#widgetCaption-error')).toBeNull();
    });

    it('lets the error go once the field is changed', async () => {
      const context = await openDialog({ existing: edit({ subtitle: 'Hogares' }) });
      await refuse(context, { 'config.display.subtitle': ['The subtitle is longer than 200 characters.'] });

      await typeAndSettle(context, 'dialog-subtitle', 'Hogares por zona');

      expect(text(field('dialog-subtitle').closest('tmz-form-field'))).not.toContain('longer than');
    });
  });

  describe('the warning that the texts crowd the tile (Widget context warning in the widget form)', () => {
    const crowded = 'The texts take room from the chart. A taller tile shows more of them.';
    const crowdedFigure = 'The texts take room from the figure. A taller tile shows more of them.';

    it('warns that a footnote takes room from the figure of a new stat tile at its default 3×2 (Warning in the form)', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'stat');
      expect(warning()).toBeNull();

      await typeAndSettle(context, 'dialog-footnote', 'Sin ponderar.');

      expect(text(warning())).toBe(crowdedFigure);
    });

    it('warns for an existing tile two rows high', async () => {
      const context = await openDialog({ existing: edit({ subtitle: 'Hogares por zona' }, { layout: { x: 0, y: 0, w: 6, h: 2 } }) });

      expect(text(warning())).toBe(crowded);
      await typeAndSettle(context, 'dialog-subtitle', '');
      expect(warning()).toBeNull();
    });

    it('warns for a footnote of 500 characters on a column chart 4 columns wide and 3 rows high (Warning for long texts)', async () => {
      await openDialog({ existing: edit({ footnote: wordy(500) }, { layout: { x: 0, y: 0, w: 4, h: 3 } }) });

      expect(text(warning())).toBe(crowded);
    });

    it('does not warn for a subtitle of 80 characters on a column chart 6 columns wide and 4 rows high (No warning on a tall tile)', async () => {
      await openDialog({ existing: edit({ subtitle: wordy(80) }, { layout: { x: 0, y: 0, w: 6, h: 4 } }) });

      expect(warning()).toBeNull();
    });

    it('does not warn for a widget with no texts, nor for blank ones', async () => {
      const context = await openDialog({ existing: edit(undefined, { layout: { x: 0, y: 0, w: 3, h: 2 } }) });
      expect(warning()).toBeNull();

      await typeAndSettle(context, 'dialog-subtitle', '   ');
      await typeAndSettle(context, 'dialog-footnote', ' ');
      expect(warning()).toBeNull();
    });

    it('does not refuse the save: the warning is a hint', async () => {
      const context = await openDialog({ existing: edit({ footnote: 'Sin ponderar.' }, { layout: { x: 0, y: 0, w: 3, h: 2 } }) });
      expect(warning()).not.toBeNull();

      expect((await saved(context)).config.display).toEqual({ footnote: 'Sin ponderar.' });
    });
  });
});

describe('the widget dialog and the chart annotations (add-chart-annotations, design D8)', () => {
  afterEach(clearOverlay);

  const twoDimensions = catalog({ columns: [...catalog().columns, { name: 'water_source', type: 'text', label: null }] });
  const queries = (context: Context) => context.http.match(isQuery).filter((r) => !r.cancelled);
  const saved = async (context: Context) => {
    await click(context, field('dialog-save'));
    return context.http.expectOne((r) => isCreate(r) || isPatch(r)).request.body as { title: string; chartType: ChartType; config: WidgetConfig };
  };
  const edit = (display: NonNullable<WidgetConfig['display']> | undefined, overrides: Partial<WidgetConfig> = {}, widgetOverrides: Partial<Widget> = {}) =>
    widget('w1', { config: config({ ...(display ? { display } : {}), ...overrides }), ...widgetOverrides });
  const lineRows = () => fields('reference-line');
  const kindSelect = (index: number) => lineRows()[index].querySelector<HTMLSelectElement>('[data-testid="reference-kind"]')!;
  const kindsOf = (index: number) => Array.from(kindSelect(index).options).map((o) => o.value);
  const valueInput = (index: number) => lineRows()[index].querySelector<HTMLInputElement>('[data-testid="reference-value"]')!;
  const labelInput = (index: number) => lineRows()[index].querySelector<HTMLInputElement>('[data-testid="reference-label"]')!;
  const add = (context: Context) => click(context, field('reference-add'));
  const withSeries = (extra: Partial<WidgetConfig> = {}) => ({ seriesBy: 'water_source', ...extra });
  const ratio = { ratio: { numerator: 'numerator', denominator: 'denominator' } };

  describe('which kinds the form offers', () => {
    it('offers a mean, a median and a fixed value for a measure with no series, and an overall line too for a ratio', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await add(context);
      expect(kindsOf(0)).toEqual(['mean', 'median', 'value']);
      expect(kindSelect(0).value).toBe('mean');

      await change(context, field('dialog-mode'), 'ratio');
      expect(kindsOf(0)).toEqual(['mean', 'median', 'overall', 'value']);
    });

    it('offers only the overall and fixed value kinds to a grouped chart of a ratio with a series (Kinds offered with a series)', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries(ratio)), columns: twoDimensions });
      await add(context);

      expect(kindsOf(0)).toEqual(['overall', 'value']);
      expect(kindSelect(0).value).toBe('overall');
    });

    it('offers only a fixed value to a measure with a series, and to each stacked layout', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries()), columns: twoDimensions });
      await add(context);
      expect(kindsOf(0)).toEqual(['value']);
      await click(context, field('dialog-layout-stacked'));
      expect(kindsOf(0)).toEqual(['value']);
      await click(context, field('dialog-layout-percent'));
      expect(kindsOf(0)).toEqual(['value']);
      await click(context, field('dialog-layout-grouped'));
      expect(kindsOf(0)).toEqual(['value']);
    });

    it('offers the same kinds to a line chart, and none to a chart that draws no lines', async () => {
      const context = await openDialog({ existing: edit(undefined, ratio, { chartType: 'line' }) });
      await add(context);
      expect(kindsOf(0)).toEqual(['mean', 'median', 'overall', 'value']);

      for (const type of ['pie', 'stat', 'histogram', 'map']) {
        await change(context, field('dialog-chart-type'), type);
        expect(fields('dialog-reference-lines'), type).toHaveLength(0);
      }
    });

    it('offers a kind once: a line takes the next kind that no line has, and a fixed value can be added again', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await add(context);
      await add(context);
      await add(context);

      expect(lineRows().map((_, i) => kindSelect(i).value)).toEqual(['mean', 'median', 'value']);
      expect(kindsOf(0)).toEqual(['mean', 'value']);
      expect(kindsOf(2)).toEqual(['value']);
    });

    it('does not let a fifth line be added, and says why', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      for (let i = 0; i < 4; i++) {
        await add(context);
      }

      expect(lineRows()).toHaveLength(4);
      expect(field<HTMLButtonElement>('reference-add').disabled).toBe(true);
      expect(text(field('reference-full'))).toBe('A widget has at most 4 reference lines.');
      await click(context, field('reference-add'));
      expect(lineRows()).toHaveLength(4);

      await click(context, lineRows()[1].querySelector<HTMLElement>('[data-testid="reference-remove"]')!);
      expect(lineRows()).toHaveLength(3);
      expect(field<HTMLButtonElement>('reference-add').disabled).toBe(false);
    });

    it('says what each kind is, and the caution of the overall line (Overall help)', async () => {
      const context = await openDialog({ existing: edit(undefined, { ...ratio, groupBy: 'municipality' }, { chartType: 'bar' }) });
      await add(context);
      expect(lineRows()[0].querySelector('[data-testid="reference-overall-help"]')).toBeNull();
      expect(text(lineRows()[0].querySelector('[data-testid="reference-help"]'))).toContain('mean of the values the chart draws');

      await change(context, kindSelect(0), 'overall');

      expect(text(lineRows()[0].querySelector('[data-testid="reference-overall-help"]'))).toContain(
        'When the bars are the answers of one question, such as places or reasons, they share one base, and this line is only their mean, not a total.',
      );
      expect(text(lineRows()[0].querySelector('[data-testid="reference-help"]'))).toContain('stat tile with the same filters');
    });
  });

  describe('the value of a fixed line', () => {
    const percentIndicator = [indicator('piped_water_rate')];
    const percentWidget = (display?: NonNullable<WidgetConfig['display']>, overrides: Partial<WidgetConfig> = {}) =>
      edit(display, { indicator: 'piped_water_rate', ...overrides });

    it('takes a percent typed as a percent and saves the fraction: 50 is 0.5 (Percent typed in the form)', async () => {
      const context = await openDialog({ existing: percentWidget(), indicators: percentIndicator });
      await add(context);
      await add(context);
      await add(context);
      await change(context, valueInput(2), '50');

      expect(text(lineRows()[2].querySelector('[data-testid="reference-unit"]'))).toBe('%');
      const body = await saved(context);

      expect(body.config.display?.referenceLines).toEqual([{ kind: 'mean' }, { kind: 'median' }, { kind: 'value', value: 0.5 }]);
    });

    it('moves the decimal point of what is typed instead of dividing: 33.3 is 0.333, and a comma is a point', async () => {
      const context = await openDialog({ existing: percentWidget({ referenceLines: [{ kind: 'value', value: 0.1 }] }), indicators: percentIndicator });

      await change(context, valueInput(0), '33.3');
      expect((await saved(context)).config.display?.referenceLines).toEqual([{ kind: 'value', value: 0.333 }]);
    });

    it('reads a comma as a point and ignores a percent sign: 33,3 % is 0.333', async () => {
      const context = await openDialog({ existing: percentWidget({ referenceLines: [{ kind: 'value', value: 0.1 }] }), indicators: percentIndicator });

      await change(context, valueInput(0), '33,3 %');
      const first = (await saved(context)).config.display?.referenceLines;
      expect(first).toEqual([{ kind: 'value', value: 0.333 }]);
    });

    it('shows a saved fraction as a percent, and keeps the saved number as it is until the input is edited', async () => {
      const context = await openDialog({ existing: percentWidget({ referenceLines: [{ kind: 'value', value: 0.3333333333, label: 'Meta' }] }), indicators: percentIndicator });
      expect(valueInput(0).value).toBe('33.33333333');

      const body = await saved(context);
      expect(body.config.display?.referenceLines).toEqual([{ kind: 'value', value: 0.3333333333, label: 'Meta' }]);
    });

    it('writes a small saved number as a percent too: 1e-7 shows as 0.00001', async () => {
      await openDialog({ existing: percentWidget({ referenceLines: [{ kind: 'value', value: 1e-7 }] }), indicators: percentIndicator });
      expect(valueInput(0).value).toBe('0.00001');
    });

    it('takes a count in the indicator\'s units, with its unit beside the input', async () => {
      const context = await openDialog({ existing: edit(undefined, { indicator: 'households_visited' }), indicators: [indicator('households_visited')] });
      await add(context);
      await add(context);
      await add(context);
      await change(context, valueInput(2), '100');

      expect(text(lineRows()[2].querySelector('[data-testid="reference-unit"]'))).toBe('households');
      expect((await saved(context)).config.display?.referenceLines).toEqual([{ kind: 'mean' }, { kind: 'median' }, { kind: 'value', value: 100 }]);
    });

    it('takes the share of a 100% layout as a percent', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries()), columns: twoDimensions });
      await click(context, field('dialog-layout-percent'));
      await add(context);
      await change(context, valueInput(0), '50');

      expect(text(lineRows()[0].querySelector('[data-testid="reference-unit"]'))).toBe('%');
      expect((await saved(context)).config.display?.referenceLines).toEqual([{ kind: 'value', value: 0.5 }]);
    });

    it('asks for the number of a fixed value before saving, and does not blank the preview meanwhile', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await previewAnswer(context, result([row('Cali', 75)]));
      await add(context);
      await add(context);
      await add(context);
      await click(context, field('dialog-save'));

      expect(context.http.match(isPatch)).toHaveLength(0);
      expect(text(field('reference-list-error'))).toBe('Type a number for every fixed value.');
      expect(fields('view-idle')).toHaveLength(0);

      await change(context, valueInput(2), '12');
      expect(fields('reference-list-error')).toHaveLength(0);
    });

    it('shows the number again, in the units now in force, when the units change under a text typed (Units changed under a typed value)', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries()), columns: twoDimensions, indicators: [indicator('households_visited')] });
      await add(context);
      await change(context, valueInput(0), '0.5');
      expect(valueInput(0).value).toBe('0.5');

      // The 100% layout takes a share as a percent: the 0.5 that is saved is 50%, and the input must say so.
      await click(context, field('dialog-layout-percent'));
      expect(text(lineRows()[0].querySelector('[data-testid="reference-unit"]'))).toBe('%');
      expect(valueInput(0).value).toBe('50');
      expect((await saved(context)).config.display?.referenceLines).toEqual([{ kind: 'value', value: 0.5 }]);
    });

    it('keeps the text as typed while the units stay, and goes back to it when they do', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries()), columns: twoDimensions, indicators: [indicator('households_visited')] });
      await add(context);
      await change(context, valueInput(0), '0.50');
      await click(context, field('dialog-layout-percent'));
      expect(valueInput(0).value).toBe('50');

      await click(context, field('dialog-layout-grouped'));
      expect(valueInput(0).value).toBe('0.50');
    });

    it.each([
      ['1.500', 'a point'],
      ['1,500', 'a comma'],
      ['-12.345', 'a negative number'],
    ])('does not guess whether %s (%s) is thousands or a decimal in a count: it blocks saving and says so (Thousands separator)', async (typed) => {
      const context = await openDialog({ existing: edit(undefined, { indicator: 'households_visited' }), indicators: [indicator('households_visited')] });
      await add(context);
      await add(context);
      await add(context);
      await change(context, valueInput(2), typed);
      await click(context, field('dialog-save'));

      expect(context.http.match(isPatch)).toHaveLength(0);
      expect(text(field('reference-list-error'))).toContain(`“${typed}” can be read as a decimal or as thousands`);
      expect(valueInput(2).getAttribute('aria-invalid')).toBe('true');
      expect(valueInput(2).value).toBe(typed);
    });

    it.each([
      ['33.3', 33.3],
      ['.5', 0.5],
      ['5,', 5],
      ['0.125', 0.125],
      ['1.5000', 1.5],
      ['1500', 1500],
      ['12.34', 12.34],
    ])('reads %s as %s in a count', async (typed, expected) => {
      const context = await openDialog({ existing: edit(undefined, { indicator: 'households_visited' }), indicators: [indicator('households_visited')] });
      await add(context);
      await add(context);
      await add(context);
      await change(context, valueInput(2), typed);

      expect((await saved(context)).config.display?.referenceLines).toEqual([{ kind: 'mean' }, { kind: 'median' }, { kind: 'value', value: expected }]);
    });

    it('reads 33.333 in a percent as a share, since a percent has no thousands', async () => {
      const context = await openDialog({ existing: percentWidget({ referenceLines: [{ kind: 'value', value: 0.1 }] }), indicators: percentIndicator });
      await change(context, valueInput(0), '33.333');

      expect((await saved(context)).config.display?.referenceLines).toEqual([{ kind: 'value', value: 0.33333 }]);
    });

    it('tells a count not to use a thousands separator', async () => {
      const context = await openDialog({ existing: edit(undefined, { indicator: 'households_visited' }), indicators: [indicator('households_visited')] });
      await add(context);
      await add(context);
      await add(context);

      expect(text(lineRows()[2].querySelector('[data-testid="reference-help"]'))).toContain('with no thousands separator');
    });
  });

  describe('the lines for the keyboard and assistive technology', () => {
    const idsOf = (element: Element) => (element.getAttribute('aria-describedby') ?? '').split(' ').filter(Boolean);
    const textOf = (id: string) => text(document.getElementById(id));

    it('describes each control by the texts that belong to it', async () => {
      const context = await openDialog({ existing: edit(undefined, { ...ratio, indicator: 'piped_water_rate', groupBy: 'municipality' }), indicators: [indicator('piped_water_rate')] });
      await add(context);
      await change(context, kindSelect(0), 'overall');
      await add(context);
      await change(context, kindSelect(1), 'value');

      expect(idsOf(kindSelect(0))).toEqual(['referenceHelp0', 'referenceCaution0']);
      expect(textOf('referenceHelp0')).toContain('stat tile');
      expect(textOf('referenceCaution0')).toContain('answers of one question');
      expect(idsOf(kindSelect(1))).toEqual(['referenceHelp1']);
      expect(idsOf(valueInput(1))).toEqual(['referenceValue1-unit']);
      expect(textOf('referenceValue1-unit')).not.toBe('');
      expect(valueInput(1).getAttribute('aria-invalid')).toBeNull();
    });

    it('marks a fixed value that has no number once Save was tried, and points it at the message', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await add(context);
      await add(context);
      await add(context);
      expect(valueInput(2).getAttribute('aria-invalid')).toBeNull();

      await click(context, field('dialog-save'));

      expect(valueInput(2).getAttribute('aria-invalid')).toBe('true');
      expect(idsOf(valueInput(2))).toContain('widgetLines-error');
      expect(textOf('widgetLines-error')).toBe('Type a number for every fixed value.');
      // The other rows are fine.
      expect(kindSelect(0).getAttribute('aria-invalid')).toBeNull();
      expect(labelInput(2).getAttribute('aria-invalid')).toBeNull();

      await change(context, valueInput(2), '12');
      expect(valueInput(2).getAttribute('aria-invalid')).toBeNull();
      expect(idsOf(valueInput(2))).not.toContain('widgetLines-error');
    });

    it('marks what the server refused in a row and describes the control by the message', async () => {
      const context = await openDialog({ existing: edit({ referenceLines: [{ kind: 'mean' }, { kind: 'value', value: 0.5, label: 'Meta' }] }) });
      await click(context, field('dialog-save'));
      context.http.expectOne(isPatch).flush(
        {
          title: 'x',
          errors: {
            'config.display.referenceLines[1].label': ["A reference line's label has at most 40 characters."],
            'config.display.referenceLines[1].value': ['A value must be a number.'],
            'config.display.referenceLines[0].kind': ['The kind is not valid.'],
          },
        },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle(context.harness);

      expect(labelInput(1).getAttribute('aria-invalid')).toBe('true');
      expect(idsOf(labelInput(1))).toEqual(['referenceLabel1-error']);
      expect(textOf('referenceLabel1-error')).toContain('at most 40');
      expect(valueInput(1).getAttribute('aria-invalid')).toBe('true');
      expect(idsOf(valueInput(1))).toEqual(['referenceValue1-error']);
      expect(textOf('referenceValue1-error')).toBe('A value must be a number.');
      expect(kindSelect(0).getAttribute('aria-invalid')).toBe('true');
      expect(idsOf(kindSelect(0))).toEqual(['referenceHelp0', 'referenceKind0-error']);
      expect(labelInput(0).getAttribute('aria-invalid')).toBeNull();
    });

    it('puts the keyboard on the kind of a new line', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await add(context);
      expect(document.activeElement).toBe(kindSelect(0));
      await add(context);
      expect(document.activeElement).toBe(kindSelect(1));
    });

    it('puts the keyboard on the line that takes the place of a removed one, the one before it, or the button that adds one', async () => {
      const context = await openDialog({ existing: edit({ referenceLines: [{ kind: 'mean' }, { kind: 'median' }, { kind: 'value', value: 1 }] }) });
      const remove = (index: number) => click(context, lineRows()[index].querySelector<HTMLElement>('[data-testid="reference-remove"]')!);

      await remove(1);
      expect(document.activeElement).toBe(kindSelect(1));
      expect(kindSelect(1).value).toBe('value');
      await remove(1);
      expect(document.activeElement).toBe(kindSelect(0));
      await remove(0);
      expect(document.activeElement).toBe(field('reference-add'));
    });
  });

  describe('what a line saves', () => {
    it('saves the label as one line of text, trimmed, and none for a blank one', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await add(context);
      await add(context);
      await change(context, labelInput(0), '  Promedio de zonas  ');
      await change(context, labelInput(1), '   ');

      expect(labelInput(0).getAttribute('maxlength')).toBe('40');
      expect((await saved(context)).config.display?.referenceLines).toEqual([{ kind: 'mean', label: 'Promedio de zonas' }, { kind: 'median' }]);
    });

    it('proposes the default label of the kind as the placeholder', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await add(context);
      expect(labelInput(0).placeholder).toBe('Mean');
      await change(context, kindSelect(0), 'median');
      expect(labelInput(0).placeholder).toBe('Median');
    });

    it('saves a line removed as no line, and no display at all when nothing else is set', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await add(context);
      await click(context, lineRows()[0].querySelector<HTMLElement>('[data-testid="reference-remove"]')!);

      expect('display' in (await saved(context)).config).toBe(false);
    });
  });

  describe('previewing the lines', () => {
    it('redraws without a new query when a line\'s label or kind changes (Label edited)', async () => {
      const context = await openDialog({ existing: edit({ referenceLines: [{ kind: 'mean' }] }) });
      await previewAnswer(context, result([row('Cali', 75), row('Buga', 55)]));

      await change(context, labelInput(0), 'Promedio');
      await change(context, kindSelect(0), 'median');
      await wait(previewDelayMs + 100);
      await settle(context.harness);

      expect(queries(context)).toHaveLength(0);
    });

    it('asks once, for the overall value, when an overall line is added to a chart with no series (Overall line added)', async () => {
      const context = await openDialog({ existing: edit(undefined, ratio) });
      const first = await previewAnswer(context, result([row('Cali', 0.75)]));
      expect('overall' in first).toBe(false);

      await add(context);
      await change(context, kindSelect(0), 'overall');
      const second = await previewAnswer(context, result([row('Cali', 0.75)], { overall: 0.6 }));

      expect((second as unknown as { overall: boolean }).overall).toBe(true);
      expect('display' in second.config).toBe(false); // the lines themselves are never part of the query
    });

    it('does not ask again for an overall line over a series, which reads its totals', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries(ratio)), columns: twoDimensions });
      await previewAnswer(context, seriesResult([cell('Cali', 'Piped', 0.5)], ['Piped']));

      await add(context);
      await wait(previewDelayMs + 100);
      await settle(context.harness);

      expect(queries(context)).toHaveLength(0);
    });
  });

  describe('steps and data labels', () => {
    const steps = () => ({ linear: field<HTMLInputElement>('dialog-interpolation-linear'), step: field<HTMLInputElement>('dialog-interpolation-step') });

    it('offers Straight and Steps to a line chart only, straight to begin with', async () => {
      const context = await openDialog({ existing: edit(undefined, {}, { chartType: 'line' }) });

      expect(Array.from(dialog().querySelectorAll('[data-testid="dialog-interpolation"] label')).map(text)).toEqual(['Straight', 'Steps']);
      expect(steps().linear.checked).toBe(true);
      expect(steps().step.checked).toBe(false);
      await change(context, field('dialog-chart-type'), 'column');
      expect(fields('dialog-interpolation')).toHaveLength(0);
    });

    it('saves steps, and previews them without a new query', async () => {
      const context = await openDialog({ existing: edit(undefined, {}, { chartType: 'line' }) });
      await previewAnswer(context, result([row('A', 1), row('B', 2)]));

      await click(context, steps().step);
      await wait(previewDelayMs + 100);
      await settle(context.harness);

      expect(queries(context)).toHaveLength(0);
      expect((await saved(context)).config.display).toEqual({ interpolation: 'step' });
    });

    it('stores straight only when the widget had a choice saved: picking it on a widget saved without one leaves it without one', async () => {
      const context = await openDialog({ existing: edit(undefined, {}, { chartType: 'line' }) });
      await click(context, steps().step);
      await click(context, steps().linear);

      expect('display' in (await saved(context)).config).toBe(false);
    });

    it('keeps a saved "linear" as it is, and moves from steps back to it', async () => {
      const context = await openDialog({ existing: edit({ interpolation: 'step' }, {}, { chartType: 'line' }) });
      expect(steps().step.checked).toBe(true);
      await click(context, steps().linear);

      expect((await saved(context)).config.display).toEqual({ interpolation: 'linear' });
    });

    it('drops the steps of a widget that is no longer a line, and brings them back with the line', async () => {
      const context = await openDialog({ existing: edit({ interpolation: 'step' }, {}, { chartType: 'line' }) });
      await change(context, field('dialog-chart-type'), 'column');
      await change(context, field('dialog-chart-type'), 'line');

      expect((await saved(context)).config.display).toEqual({ interpolation: 'step' });
    });

    it('offers the data labels to a bar, column, line and pie, and not to stacked bars', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries()), columns: twoDimensions });
      expect(fields('dialog-data-labels')).toHaveLength(1);
      await click(context, field('dialog-layout-stacked'));
      expect(fields('dialog-data-labels')).toHaveLength(0);
      await click(context, field('dialog-layout-percent'));
      expect(fields('dialog-data-labels')).toHaveLength(0);
      await click(context, field('dialog-layout-grouped'));
      expect(fields('dialog-data-labels')).toHaveLength(1);

      for (const type of ['bar', 'line', 'pie']) {
        await change(context, field('dialog-chart-type'), type);
        expect(fields('dialog-data-labels'), type).toHaveLength(1);
      }
      for (const type of ['stat', 'histogram', 'map', 'boxplot']) {
        await change(context, field('dialog-chart-type'), type);
        expect(fields('dialog-data-labels'), type).toHaveLength(0);
      }
    });

    it('saves data labels, and none by default; unchecking a setting the widget never had leaves it out', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      expect(field<HTMLInputElement>('dialog-data-labels').checked).toBe(false);
      await click(context, field('dialog-data-labels'));
      await click(context, field('dialog-data-labels'));
      expect('display' in (await saved(context)).config).toBe(false);
    });

    it('saves checked data labels', async () => {
      const context = await openDialog({ existing: edit(undefined) });
      await click(context, field('dialog-data-labels'));

      expect((await saved(context)).config.display).toEqual({ dataLabels: true });
    });

    it('keeps a saved "false" as it is when the box is unchecked, and checked when it was', async () => {
      const context = await openDialog({ existing: edit({ dataLabels: true }) });
      expect(field<HTMLInputElement>('dialog-data-labels').checked).toBe(true);
      await click(context, field('dialog-data-labels'));

      expect((await saved(context)).config.display).toEqual({ dataLabels: false });
    });
  });

  describe('a widget saved unchanged (Form saved unchanged)', () => {
    it('sends no display for a widget saved before the three settings', async () => {
      const context = await openDialog({ existing: edit(undefined, { ...ratio }) });
      const body = await saved(context);

      expect(body.config).toEqual(config(ratio));
      expect('display' in body.config).toBe(false);
    });

    it('sends the settings of a column chart saved with "false", two lines and its color', async () => {
      const display = { color: 2, referenceLines: [{ kind: 'mean' as const }, { kind: 'value' as const, value: 0.5, label: 'Meta' }], dataLabels: false };
      const context = await openDialog({ existing: edit(display) });

      expect((await saved(context)).config.display).toEqual(display);
    });

    it('sends the "linear" of a line chart, and its two lines', async () => {
      const display = { referenceLines: [{ kind: 'median' as const, label: 'Mediana' }, { kind: 'value' as const, value: 120 }], interpolation: 'linear' as const, dataLabels: false };
      const context = await openDialog({ existing: edit(display, {}, { chartType: 'line' }) });

      expect((await saved(context)).config.display).toEqual(display);
    });

    it('sends a chart with a series saved with no layout without one: grouped is shown but nothing is written', async () => {
      const context = await openDialog({ existing: edit({ referenceLines: [{ kind: 'overall' }] }, withSeries(ratio)), columns: twoDimensions });
      expect(field<HTMLInputElement>('dialog-layout-grouped').checked).toBe(true);

      const body = await saved(context);

      expect('layout' in body.config).toBe(false);
      expect(body.config.display).toEqual({ referenceLines: [{ kind: 'overall' }] });
    });

    it('writes the layout once the member picks one, and goes back to none when grouped is picked again on a widget saved with none', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries()), columns: twoDimensions });
      await click(context, field('dialog-layout-stacked'));
      await click(context, field('dialog-layout-grouped'));

      expect('layout' in (await saved(context)).config).toBe(false);
    });

    it('saves the layout it was saved with, grouped included', async () => {
      const context = await openDialog({ existing: edit(undefined, withSeries({ layout: 'grouped' })), columns: twoDimensions });

      expect((await saved(context)).config.layout).toBe('grouped');
    });

    it('still saves grouped for a new widget with a series', async () => {
      const context = await openDialog({ columns: twoDimensions });
      await change(context, field('dialog-title'), 'Water');
      await change(context, field('dialog-series-by'), 'water_source');

      expect((await saved(context)).config.layout).toBe('grouped');
    });
  });

  describe('what the widget no longer takes', () => {
    const lineDisplay = { referenceLines: [{ kind: 'mean' as const }, { kind: 'value' as const, value: 0.5 }], dataLabels: true };

    it('keeps the data labels and drops the lines of a column chart turned into a pie chart (Column chart turned into a pie chart)', async () => {
      const context = await openDialog({ existing: edit(lineDisplay) });
      await change(context, field('dialog-chart-type'), 'pie');

      expect(fields('dialog-reference-lines')).toHaveLength(0);
      expect(text(field('dialog-annotations-note'))).toBe('This chart does not draw reference lines, so they are not saved. They come back if you change the chart type back.');
      const body = await saved(context);

      expect(body.chartType).toBe('pie');
      expect(body.config.display).toEqual({ dataLabels: true });
    });

    it('saves no mean line for a column chart that gains a series, shows the note, and brings the line back when the series goes', async () => {
      const context = await openDialog({ existing: edit(lineDisplay), columns: twoDimensions });
      expect(fields('dialog-annotations-note')).toHaveLength(0);

      await change(context, field('dialog-series-by'), 'water_source');
      expect(text(field('dialog-annotations-note'))).toBe('A mean or median line needs a chart with no series, so it is not saved. It comes back if you remove the series.');
      expect(lineRows()).toHaveLength(2); // still in the form

      await change(context, field('dialog-series-by'), '');
      expect(fields('dialog-annotations-note')).toHaveLength(0);
      expect((await saved(context)).config.display).toEqual(lineDisplay);
    });

    it('saves only the fixed value of a column chart that gains a series, with the note', async () => {
      const context = await openDialog({ existing: edit(lineDisplay), columns: twoDimensions });
      await change(context, field('dialog-series-by'), 'water_source');

      expect((await saved(context)).config.display).toEqual({ referenceLines: [{ kind: 'value', value: 0.5 }], dataLabels: true });
    });

    it('brings the line back when the chart type changes back before saving', async () => {
      const context = await openDialog({ existing: edit(lineDisplay) });
      await change(context, field('dialog-chart-type'), 'stat');
      await change(context, field('dialog-chart-type'), 'column');

      expect(fields('dialog-annotations-note')).toHaveLength(0);
      expect((await saved(context)).config.display).toEqual(lineDisplay);
    });

    it('drops an overall line when the plot becomes a measure, with its note', async () => {
      const context = await openDialog({ existing: edit({ referenceLines: [{ kind: 'overall' }] }, ratio) });
      await change(context, field('dialog-mode'), 'measure');

      expect(text(field('dialog-annotations-note'))).toBe('An overall line needs a ratio, so it is not saved. It comes back if you plot a ratio.');
      expect('display' in (await saved(context)).config).toBe(false);
    });

    it('drops the lines but the fixed value of a stacked layout, and the data labels, with their notes', async () => {
      const context = await openDialog({
        existing: edit({ referenceLines: [{ kind: 'value', value: 100 }, { kind: 'mean' }], dataLabels: true }, withSeries()),
        columns: twoDimensions,
      });
      expect(fields('dialog-annotations-note')).toHaveLength(1); // the mean needs no series

      await click(context, field('dialog-layout-stacked'));
      expect(text(field('dialog-annotations-note'))).toBe('Stacked bars take only fixed values, so the other lines are not saved. They come back with the grouped layout.');
      expect(text(field('dialog-data-labels-note'))).toBe('Stacked bars have no data labels, so they are not saved. They come back with the grouped layout.');
      const body = await saved(context);

      expect(body.config.display).toEqual({ referenceLines: [{ kind: 'value', value: 100 }] });
    });
  });

  describe('a refusal', () => {
    const refuse = async (context: Context, errors: Record<string, string[]>) => {
      await click(context, field('dialog-save'));
      context.http.expectOne(isPatch).flush({ title: 'x', errors }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);
    };

    it('shows a refused label on its line and not on the others', async () => {
      const context = await openDialog({ existing: edit({ referenceLines: [{ kind: 'mean' }, { kind: 'value', value: 0.5, label: 'Meta' }] }) });

      await refuse(context, { 'config.display.referenceLines[1].label': ['A reference line\'s label has at most 40 characters.'] });

      expect(text(lineRows()[1])).toContain('A reference line\'s label has at most 40 characters.');
      expect(text(lineRows()[0])).not.toContain('at most 40');
      expect(fields('dialog-error')).toHaveLength(0);
    });

    it('shows a refused kind and a refused value on their lines', async () => {
      const context = await openDialog({ existing: edit({ referenceLines: [{ kind: 'value', value: 0.2 }, { kind: 'value', value: 1.5 }] }, withSeries()), columns: twoDimensions });
      await click(context, field('dialog-layout-percent'));

      await refuse(context, {
        'config.display.referenceLines[0].kind': ["The kind must be 'mean', 'median', 'overall' or 'value'."],
        'config.display.referenceLines[1].value': ['In a 100% layout a value is a share from 0 to 1.'],
      });

      expect(text(lineRows()[0])).toContain('The kind must be');
      expect(text(lineRows()[1])).toContain('In a 100% layout a value is a share from 0 to 1.');
      expect(text(lineRows()[0])).not.toContain('share from 0 to 1');
    });

    it('shows the refusal of the lines as a whole under the list, and lets it go with the next change', async () => {
      const context = await openDialog({ existing: edit({ referenceLines: [{ kind: 'mean' }] }) });
      await refuse(context, { 'config.display.referenceLines': ['A widget has at most 4 reference lines.'] });

      expect(text(field('reference-list-error'))).toBe('A widget has at most 4 reference lines.');
      expect(field('dialog-reference-lines').getAttribute('aria-describedby')).toBe('widgetLines-error');
      expect(fields('dialog-error')).toHaveLength(0);
      await add(context);
      expect(fields('reference-list-error')).toHaveLength(0);
    });

    it('maps a refusal to the row the member sees when the form holds a line the widget does not save', async () => {
      // The mean is not saved with a series, so the server's second line is the form's third.
      const context = await openDialog({
        existing: edit({ referenceLines: [{ kind: 'value', value: 1 }, { kind: 'mean' }, { kind: 'value', value: 2, label: 'x' }] }, withSeries()),
        columns: twoDimensions,
      });

      await refuse(context, { 'config.display.referenceLines[1].label': ['A reference line\'s label has at most 40 characters.'] });

      expect(text(lineRows()[2])).toContain('at most 40');
      expect(text(lineRows()[1])).not.toContain('at most 40');
    });

    it('shows a refused choice of segments on its field', async () => {
      const context = await openDialog({ existing: edit({ interpolation: 'step' }, {}, { chartType: 'line' }) });
      await refuse(context, { 'config.display.interpolation': ['Only a line chart has an interpolation.'] });

      expect(text(field('dialog-interpolation').querySelector('[data-testid="field-error"]'))).toBe('Only a line chart has an interpolation.');
      expect(field('dialog-interpolation').getAttribute('aria-describedby')).toBe('widgetInterpolation-error');
    });

    it('shows refused data labels on their field', async () => {
      const context = await openDialog({ existing: edit({ dataLabels: true }) });
      await refuse(context, { 'config.display.dataLabels': ['Stacked bars have no data labels.'] });

      expect(text(dialog().querySelector('[data-testid="dialog-data-labels-group"] [data-testid="field-error"]'))).toBe('Stacked bars have no data labels.');
    });
  });
});

describe('the widget dialog and the order of values (add-declared-category-order, design D9)', () => {
  afterEach(clearOverlay);

  const twoDimensions = catalog({ columns: [...catalog().columns, { name: 'water_source', type: 'text', label: null }] });
  const queries = (context: Context) => context.http.match(isQuery).filter((r) => !r.cancelled);
  const saved = async (context: Context) => {
    await click(context, field('dialog-save'));
    return context.http.expectOne((r) => isCreate(r) || isPatch(r)).request.body as { title: string; chartType: ChartType; config: WidgetConfig };
  };
  const edit = (display: NonNullable<WidgetConfig['display']> | undefined, overrides: Partial<WidgetConfig> = {}, widgetOverrides: Partial<Widget> = {}) =>
    widget('w1', { config: config({ seriesBy: 'water_source', ...(display ? { display } : {}), ...overrides }), ...widgetOverrides });
  const answer = () => seriesResult([cell('Cali', 'Piped', 5), cell('Cali', 'Well', 3), cell('Cali', 'River', 1), cell('Pasto', 'Piped', 2)], ['Piped', 'River', 'Well']);
  const seriesValues = () => Array.from(dialog().querySelectorAll('[data-testid="dialog-order-series"] [data-testid="order-value"]')).map(text);
  const downButtons = () => dialog().querySelectorAll<HTMLButtonElement>('[data-testid="dialog-order-series"] [data-testid="order-down"]');

  it('offers the default order and an own order of the groups and of the series of a widget with a series', async () => {
    await openDialog({ existing: edit(undefined), columns: twoDimensions });

    expect(fields('dialog-order')).toHaveLength(1);
    expect(field<HTMLInputElement>('dialog-order-group-default').checked).toBe(true);
    expect(field<HTMLInputElement>('dialog-order-series-default').checked).toBe(true);
    expect(text(dialog().querySelector('[data-testid="dialog-order-series"] label'))).toBe('Default order (the chart’s order)');
  });

  it('words the default as the order of the indicator when the indicator declares one for the column', async () => {
    const declared = indicator('coverage', { columns: { water_source: { order: ['Well', 'Piped'] } } });

    await openDialog({ existing: edit(undefined), columns: twoDimensions, indicators: [declared] });

    expect(text(dialog().querySelector('[data-testid="dialog-order-series"] label'))).toBe('Default order (the indicator’s order)');
    expect(text(dialog().querySelector('[data-testid="dialog-order-group"] label'))).toBe('Default order (the chart’s order)');
  });

  it('lists the values the preview shows, in its current order, with a button to move each (Own order from the preview)', async () => {
    const context = await openDialog({ existing: edit(undefined), columns: twoDimensions });
    await previewAnswer(context, answer());

    await click(context, field('dialog-order-series-own'));

    expect(seriesValues()).toEqual(['Piped', 'River', 'Well']);
    expect(dialog().querySelectorAll('[data-testid="dialog-order-series"] [data-testid="order-up"]')).toHaveLength(3);
    expect(downButtons()).toHaveLength(3);
  });

  it('redraws the preview in the new order without asking the indicator again (No new query)', async () => {
    const context = await openDialog({ existing: edit(undefined), columns: twoDimensions });
    await previewAnswer(context, answer());
    await click(context, field('dialog-order-series-own'));

    await click(context, downButtons()[0]);
    await wait(previewDelayMs + 100);
    await settle(context.harness);

    expect(queries(context)).toHaveLength(0);
    expect(seriesValues()).toEqual(['River', 'Piped', 'Well']);
    expect(Array.from(dialog().querySelectorAll('[data-testid="dialog-preview"] [data-testid="legend-name"]')).map(text)).toEqual(['River', 'Piped', 'Well']);
  });

  it('saves nothing of the order once the default is chosen again (Back to the default)', async () => {
    const context = await openDialog({ existing: edit(undefined), columns: twoDimensions });
    await previewAnswer(context, answer());
    await click(context, field('dialog-order-series-own'));
    await click(context, downButtons()[0]);
    await click(context, field('dialog-order-series-default'));

    const body = await saved(context);

    expect(body.config.display).toBeUndefined();
  });

  it('saves the order moved to, as the last member of the display', async () => {
    const context = await openDialog({ existing: edit({ labels: { value: 'Hogares' } }), columns: twoDimensions });
    await previewAnswer(context, answer());
    await click(context, field('dialog-order-series-own'));
    await click(context, downButtons()[0]);

    const body = await saved(context);

    expect(body.config.display).toEqual({ labels: { value: 'Hogares' }, order: { series: ['River', 'Piped', 'Well'] } });
    expect(Object.keys(body.config.display!)).toEqual(['labels', 'order']);
  });

  it('lists apart, as not shown, a value the order holds that the preview does not, with a button to remove it (Listed value no longer shown)', async () => {
    const context = await openDialog({ existing: edit({ order: { series: ['No sabe', 'Well', 'Piped'] } }), columns: twoDimensions });
    await previewAnswer(context, answer());

    expect(seriesValues()).toEqual(['Well', 'Piped']);
    const apart = dialog().querySelectorAll('[data-testid="dialog-order-series"] [data-testid="order-apart-item"]');
    expect(Array.from(apart).map((item) => text(item.querySelector('[data-testid="order-apart-value"]')))).toEqual(['No sabe']);
    await click(context, apart[0].querySelector<HTMLButtonElement>('[data-testid="order-apart-remove"]')!);
    expect((await saved(context)).config.display).toEqual({ order: { series: ['Well', 'Piped'] } });
  });

  it('says that values with no color kept take the colors of the palette in this order (Colors follow the order)', async () => {
    const context = await openDialog({ existing: edit(undefined), columns: twoDimensions });
    await previewAnswer(context, answer());
    expect(fields('order-colors')).toHaveLength(0);

    await click(context, field('dialog-order-series-own'));

    expect(text(field('order-colors'))).toContain('in this order');
  });

  it('sends the saved settings back unchanged for a widget with no order (Saved unchanged)', async () => {
    const context = await openDialog({ existing: edit({ labels: { value: 'Hogares' } }), columns: twoDimensions });

    const body = await saved(context);

    expect(body.config.display).toEqual({ labels: { value: 'Hogares' } });
  });

  it('shows a refusal of the order of groups on its field', async () => {
    const context = await openDialog({ existing: edit({ order: { group: ['Cali'] } }, { seriesBy: null }), columns: twoDimensions });
    await click(context, field('dialog-save'));

    context.http
      .expectOne((r) => isCreate(r) || isPatch(r))
      .flush({ title: 'x', errors: { 'config.display.order.group[0]': ['It is listed twice.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(context.harness);

    expect(text(dialog().querySelector('[data-testid="dialog-order-group"] [data-testid="field-error"]'))).toBe('It is listed twice.');
  });

  it('turns the order of groups off with its reason while the widget has a sort, and still offers the order of series (Order of groups off while sorted)', async () => {
    const context = await openDialog({ existing: edit(undefined), columns: twoDimensions });
    await previewAnswer(context, answer());

    await change(context, field('dialog-sort-by'), 'value');

    expect(fields('dialog-order-group-own')).toHaveLength(0);
    expect(text(field('order-sorted'))).toBe('The sort decides the order of the bars.');
    expect(fields('dialog-order-series-own')).toHaveLength(1);
  });

  it('drops the order of series when the widget becomes a pie (Order dropped by a kind change)', async () => {
    const context = await openDialog({ existing: edit({ order: { series: ['Well', 'Piped'] } }), columns: twoDimensions });
    await change(context, field('dialog-chart-type'), 'pie');

    const body = await saved(context);

    expect(body.chartType).toBe('pie');
    expect(body.config.display?.order).toBeUndefined();
  });

  it('offers no order of groups to a stat tile, a histogram and a map, and no order of series to a pyramid', async () => {
    const context = await openDialog({ existing: edit(undefined), columns: twoDimensions });

    for (const type of ['stat', 'histogram', 'map']) {
      await change(context, field('dialog-chart-type'), type);
      expect(fields('dialog-order-group'), type).toHaveLength(0);
    }
    await change(context, field('dialog-chart-type'), 'pyramid');
    expect(fields('dialog-order-series')).toHaveLength(0);
  });
});
