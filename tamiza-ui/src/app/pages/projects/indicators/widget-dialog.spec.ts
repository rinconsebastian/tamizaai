import { settle, typeInto } from '../../../../testing/project-harness';
import { IndicatorColumns, IndicatorEntry, Widget, WidgetConfig } from '../../../core/projects/indicators.service';
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
  isQuery,
  open,
  overlay,
  result,
  row,
  text,
  wait,
  widget,
} from './indicators-testing';
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
}

/** Opens the dialog to add a widget, or to edit `existing`, and answers the request for the columns of the indicator it starts on. */
async function openDialog({ existing = null, columns = catalog(), indicators }: Setup = {}) {
  const context = await open('analyst', existing ? [existing] : [], existing ? result([row('Cali', 75)]) : null, indicators ? { indicators } : {});
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
const widgetsUrl = `${indicatorsUrl}/widgets`;
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
        config: { indicator: 'coverage', measure: 'numerator', ratio: null, groupBy: 'round', filters: [], geometryColumn: null, buckets: null },
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
});
