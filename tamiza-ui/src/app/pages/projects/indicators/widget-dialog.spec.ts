import { MapStub, stubMap } from '../../../../testing/leaflet';
import { details, projectId, settle, typeInto } from '../../../../testing/project-harness';
import { BasemapList } from '../../../core/basemaps.service';
import { IndicatorColumns, IndicatorEntry, Widget, WidgetConfig } from '../../../core/projects/indicators.service';
import { DashboardEntry } from '../../../core/projects/projects.service';
import { valueSearchDelayMs } from '../data-filter-dialog';
import {
  Context,
  all,
  catalog,
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
}

/** Opens the dialog to add a widget, or to edit `existing`, and answers the request for the columns of the indicator it starts on. */
async function openDialog({ existing = null, columns = catalog(), indicators, dashboards }: Setup = {}) {
  const context = await open('analyst', existing ? [existing] : [], existing ? result([row('Cali', 75)]) : null, indicators ? { indicators } : {}, dashboards ? { dashboards } : {});
  if (existing) {
    // Edit is an item of the widget's actions menu.
    context.root.querySelector<HTMLElement>('[data-testid="menu-trigger"]')!.click();
    await settle(context.harness);
    document.querySelector<HTMLElement>('[data-testid="widget-edit"]')!.click();
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
        config: { indicator: 'coverage', measure: 'numerator', ratio: null, groupBy: 'round', filters: [], geometryColumn: null, buckets: null, sort: null, tooltip: [], basemap: null },
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

    it('shows the server\'s refusal of an invalid draft in place of the earlier chart', async () => {
      const context = await openDialog();
      await previewAnswer(context, result([row('Cali', 75)]));
      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).not.toBeNull();

      await change(context, field('dialog-measure'), 'round');
      // The old chart goes at once; the draft is not drawn as if it were the saved one.
      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).toBeNull();
      await previewAnswer(context, { status: 400, body: { title: 'x', errors: { measure: ["Column 'round' is not a number."] } } });

      expect(text(field('dialog-preview').querySelector('[data-testid="view-error"]'))).toBe("Column 'round' is not a number.");
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
