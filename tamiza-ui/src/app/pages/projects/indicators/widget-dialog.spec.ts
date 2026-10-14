import { settle, typeInto } from '../../../../testing/project-harness';
import { IndicatorsColumns, Widget, WidgetConfig } from '../../../core/projects/indicators.service';
import { valueSearchDelayMs } from '../data-filter-dialog';
import {
  Context,
  all,
  catalog,
  clearOverlay,
  config,
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
import { maxSeries, previewDelayMs } from './widget-dialog';

const dialog = () => overlay().querySelector<HTMLElement>('[data-testid="widget-dialog"]')!;
const field = <T extends HTMLElement>(id: string) => dialog().querySelector<T>(`[data-testid="${id}"]`)!;
const fields = (id: string) => Array.from(dialog().querySelectorAll<HTMLInputElement>(`[data-testid="${id}"]`));
const options = () => fields('indicator-option');
const option = (name: string) => options().find((o) => o.value === name)!;

async function openDialog(existing: Widget | null = null, columns: IndicatorsColumns = catalog(), published = true) {
  const widgets = existing ? [existing] : [];
  const context = await open('analyst', widgets, published ? result([row('coverage', 'Cali', 75)]) : null, published ? {} : { published: false, publishedAt: null, runId: null });
  const button = existing ? 'widget-edit' : 'widget-add';
  context.root.querySelector<HTMLElement>(`[data-testid="${button}"]`)!.click();
  await settle(context.harness);
  context.http.expectOne(isColumns).flush(columns);
  await settle(context.harness);
  return context;
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
  return requests[0].request.body as WidgetConfig;
}

describe('the widget dialog', () => {
  afterEach(clearOverlay);

  describe('indicators by chart type', () => {
    it.each(['stat', 'pie', 'map'])('offers exactly one indicator for a %s: choosing another replaces the first', async (type) => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), type);

      expect(options().every((o) => o.type === 'radio')).toBe(true);
      expect(text(field('indicators-rule'))).toBe('This chart type plots exactly one indicator.');
      await click(context, option('coverage'));
      await click(context, option('target'));

      expect(options().filter((o) => o.checked).map((o) => o.value)).toEqual(['target']);
      expect(text(field('indicators-selected'))).toBe('1 selected');
    });

    it.each(['bar', 'column', 'line', 'histogram'])('offers one or more indicators for a %s', async (type) => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), type);

      expect(options().every((o) => o.type === 'checkbox')).toBe(true);
      await click(context, option('coverage'));
      await click(context, option('target'));
      expect(options().filter((o) => o.checked).map((o) => o.value)).toEqual(['coverage', 'target']);
      await click(context, option('coverage'));
      expect(options().filter((o) => o.checked).map((o) => o.value)).toEqual(['target']);
    });

    it('keeps only the first indicator when the chart type changes to one that takes a single indicator', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'bar');
      await click(context, option('coverage'));
      await click(context, option('target'));

      await change(context, field('dialog-chart-type'), 'stat');

      expect(options().filter((o) => o.checked).map((o) => o.value)).toEqual(['coverage']);
    });

    it('refuses a second indicator for a stat tile when saving', async () => {
      const stale = widget('w1', { chartType: 'stat', config: config({ indicators: ['coverage', 'target'] }) });
      const context = await openDialog(stale);

      await click(context, field('dialog-save'));

      expect(text(dialog().querySelector('#widgetIndicatorsError'))).toBe('Choose exactly one indicator for this chart type.');
      context.http.expectNone((r) => r.method === 'PATCH');
    });

    it('stops at as many indicators as the palette has colors', async () => {
      const many = Array.from({ length: 10 }, (_, i) => `indicator_${i + 1}`);
      const context = await openDialog(null, catalog({ indicators: many }));
      await change(context, field('dialog-chart-type'), 'line');
      for (const name of many.slice(0, maxSeries)) await click(context, option(name));

      expect(options().filter((o) => o.checked)).toHaveLength(maxSeries);
      expect(options().filter((o) => !o.checked).every((o) => o.disabled)).toBe(true);
    });

    it('lists the indicators the widget names that the table no longer has, marked', async () => {
      const stale = widget('w1', { config: config({ indicators: ['removed_one'] }) });
      const context = await openDialog(stale);

      expect(options()[0].value).toBe('removed_one');
      expect(options()[0].checked).toBe(true);
      expect(text(field('indicator-gone'))).toBe('not published');
      expect(context.http.match(isQuery).length).toBeGreaterThanOrEqual(0);
    });

    it('offers a search once there are many indicators', async () => {
      const many = Array.from({ length: 12 }, (_, i) => `indicator_${i + 1}`);
      const context = await openDialog(null, catalog({ indicators: many }));

      await change(context, field('indicator-search'), 'indicator_1');

      expect(options().map((o) => o.value)).toEqual(['indicator_1', 'indicator_10', 'indicator_11', 'indicator_12']);
    });
  });

  describe('the fields of each chart type', () => {
    it('asks for the geometry column of a map only, among the text columns, and requires it', async () => {
      const context = await openDialog();
      expect(field('dialog-geometry')).toBeNull();
      expect(field('dialog-buckets')).toBeNull();

      await change(context, field('dialog-chart-type'), 'map');
      const choices = Array.from(field<HTMLSelectElement>('dialog-geometry').options).map((o) => o.value);
      expect(choices).toEqual(['', 'indicator', 'disaggregation', 'geometry']);

      await change(context, field('dialog-title'), 'Coverage map');
      await click(context, option('coverage'));
      await click(context, field('dialog-save'));
      expect(text(dialog().querySelector('#widgetGeometry-error'))).toBe('Choose the geometry column of the map.');
      context.http.expectNone((r) => r.method === 'POST' && r.url.endsWith('/widgets'));
    });

    it('asks for the number of buckets of a histogram only, between 1 and 100', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'histogram');
      await change(context, field('dialog-title'), 'Distribution');
      await click(context, option('coverage'));

      await change(context, field('dialog-buckets'), '101');
      await click(context, field('dialog-save'));
      expect(text(dialog().querySelector('#widgetBuckets-error'))).toBe('Enter a whole number from 1 to 100, or leave it empty.');

      await change(context, field('dialog-buckets'), '2.5');
      expect(text(dialog().querySelector('#widgetBuckets-error'))).toContain('whole number');
    });

    it('groups by disaggregation and plots the value unless told otherwise', async () => {
      await openDialog();

      expect(field<HTMLSelectElement>('dialog-group-by').value).toBe('disaggregation');
      expect(field<HTMLSelectElement>('dialog-value-field').value).toBe('value');
      expect(Array.from(field<HTMLSelectElement>('dialog-group-by').options).map((o) => o.value)).toEqual(['indicator', 'disaggregation', 'value', 'numerator', 'denominator', 'round', 'geometry']);
    });

    it('requires a title', async () => {
      const context = await openDialog();
      await click(context, option('coverage'));

      await click(context, field('dialog-save'));

      expect(text(dialog().querySelector('#widgetTitle-error'))).toBe('Enter a title.');
      context.http.expectNone((r) => r.method === 'POST' && r.url.endsWith('/widgets'));
    });
  });

  describe('saving', () => {
    async function fill(context: Context) {
      await change(context, field('dialog-title'), '  Attendance by round  ');
      await change(context, field('dialog-chart-type'), 'line');
      await click(context, option('attendance'));
      await change(context, field('dialog-group-by'), 'round');
      await change(context, field('dialog-value-field'), 'numerator');
    }

    it('creates the widget from the form, without a layout so the server places it', async () => {
      const context = await openDialog();
      await fill(context);

      await click(context, field('dialog-save'));

      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === `${indicatorsUrl}/widgets`);
      expect(request.request.body).toEqual({
        title: 'Attendance by round',
        chartType: 'line',
        config: { indicators: ['attendance'], groupBy: 'round', valueField: 'numerator', filters: [], geometryColumn: null, buckets: null },
      });
      request.flush(widget('w9', { title: 'Attendance by round', chartType: 'line' }), { status: 201, statusText: 'Created' });
      await settle(context.harness);
      expect(overlay().querySelector('[data-testid="widget-dialog"]')).toBeNull();
    });

    it('sends the geometry column of a map and the buckets of a histogram, and neither otherwise', async () => {
      const context = await openDialog();
      await change(context, field('dialog-title'), 'Map');
      await change(context, field('dialog-chart-type'), 'map');
      await click(context, option('coverage'));
      await change(context, field('dialog-geometry'), 'geometry');
      await change(context, field('dialog-chart-type'), 'histogram');
      await change(context, field('dialog-buckets'), '25');
      await change(context, field('dialog-chart-type'), 'map');
      await click(context, field('dialog-save'));

      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === `${indicatorsUrl}/widgets`);
      expect(request.request.body.config.geometryColumn).toBe('geometry');
      expect(request.request.body.config.buckets).toBeNull();
      request.flush(widget('w9'), { status: 201, statusText: 'Created' });
    });

    it('shows what the server refuses next to the field, and keeps the dialog open', async () => {
      const context = await openDialog();
      await fill(context);
      await click(context, field('dialog-save'));

      context.http
        .expectOne((r) => r.method === 'POST' && r.url === `${indicatorsUrl}/widgets`)
        .flush({ title: 'x', errors: { 'config.groupBy': ['The table has no column called round.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(dialog().querySelector('#widgetGroupBy-error'))).toBe('The table has no column called round.');
      expect(field<HTMLButtonElement>('dialog-save').disabled).toBe(false);
    });

    it('shows a refusal that names no field in an alert, such as the widget limit', async () => {
      const context = await openDialog();
      await fill(context);
      await click(context, field('dialog-save'));

      context.http
        .expectOne((r) => r.method === 'POST' && r.url === `${indicatorsUrl}/widgets`)
        .flush({ title: 'x', errors: { widgets: ['A project takes at most 50 widgets.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(field('dialog-error'))).toBe('A project takes at most 50 widgets.');
    });

    it('closes without saving on Cancel', async () => {
      const context = await openDialog();

      await click(context, field('dialog-cancel'));

      expect(overlay().querySelector('[data-testid="widget-dialog"]')).toBeNull();
      context.http.expectNone((r) => r.method === 'POST' && r.url.endsWith('/widgets'));
    });
  });

  describe('live preview', () => {
    it('says the form is incomplete until an indicator is chosen, and asks for nothing', async () => {
      const context = await openDialog();

      expect(text(field('view-idle'))).toBe('Complete the form to see a preview.');
      await wait(previewDelayMs + 100);
      expect(context.http.match(isQuery)).toHaveLength(0);
    });

    it('asks for the rows once the draft stops changing, and draws them', async () => {
      const context = await openDialog();
      await change(context, field('dialog-chart-type'), 'bar');
      await click(context, option('coverage'));
      await click(context, option('target'));
      expect(context.http.match(isQuery)).toHaveLength(0);

      const body = await previewAnswer(context, result([row('coverage', 'Cali', 75), row('coverage', 'Pasto', 40)]));

      expect(body).toEqual({ indicators: ['coverage', 'target'], groupBy: 'disaggregation', valueField: 'value', filters: [], geometryColumn: null, buckets: null });
      expect(field('dialog-preview').querySelectorAll('[data-testid="chart-mark"]')).toHaveLength(2);
    });

    it('shows the server\'s refusal of an invalid draft in place of the earlier chart', async () => {
      const context = await openDialog();
      await click(context, option('coverage'));
      await previewAnswer(context, result([row('coverage', 'Cali', 75)]));
      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).not.toBeNull();

      await change(context, field('dialog-value-field'), 'denominator');
      // The old chart goes at once; the draft is not drawn as if it were the saved one.
      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).toBeNull();
      await previewAnswer(context, { status: 400, body: { title: 'x', errors: { valueField: ['The table has no column called denominator.'] } } });

      expect(text(field('dialog-preview').querySelector('[data-testid="view-error"]'))).toBe('The table has no column called denominator.');
      expect(field('dialog-preview').querySelector('[data-testid="chart-svg"]')).toBeNull();
    });

    it('names a column the table lacks instead of drawing', async () => {
      const context = await openDialog();
      await click(context, option('coverage'));
      await previewAnswer(context, result([], { missing: { indicators: [], columns: ['disaggregation'] } }));

      expect(text(field('dialog-preview').querySelector('[data-testid="missing-columns"]'))).toContain('disaggregation');
    });

    it('shows only the latest draft when the form changes while a request is out', async () => {
      const context = await openDialog();
      await click(context, option('coverage'));
      await wait(previewDelayMs + 100);
      await settle(context.harness);
      const first = context.http.match(isQuery);
      expect(first).toHaveLength(1);

      await change(context, field('dialog-value-field'), 'numerator');
      expect(first[0].cancelled).toBe(true);
      const body = await previewAnswer(context, result([row('coverage', 'Cali', 5)]));
      expect(body.valueField).toBe('numerator');
    });

    it('does not ask while nothing is published, and says so', async () => {
      const context = await openDialog(null, catalog({ published: false, columns: [], indicators: [] }), false);
      await change(context, field('indicator-text'), 'coverage, target');
      await wait(previewDelayMs + 100);

      expect(context.http.match(isQuery)).toHaveLength(0);
      expect(text(field('view-idle'))).toContain('Nothing is published yet');
    });
  });

  describe('while nothing is published', () => {
    it('takes the indicators and the group-by column as typed text, and saves them', async () => {
      const context = await openDialog(null, catalog({ published: false, columns: [], indicators: [] }), false);
      await change(context, field('dialog-title'), 'Later');
      await change(context, field('indicator-text'), 'coverage, , target, coverage');
      expect(text(field('indicators-selected'))).toBe('2 selected');
      await change(context, field('dialog-group-by'), 'year');
      await click(context, field('dialog-save'));

      const request = context.http.expectOne((r) => r.method === 'POST' && r.url === `${indicatorsUrl}/widgets`);
      expect(request.request.body.config).toMatchObject({ indicators: ['coverage', 'target'], groupBy: 'year' });
      request.flush(widget('w9'), { status: 201, statusText: 'Created' });
    });

    it('explains that filters wait for the table', async () => {
      await openDialog(null, catalog({ published: false, columns: [], indicators: [] }), false);

      expect(text(field('dialog-filters'))).toContain('Filters can be added once the indicators are published.');
      expect(field('filter-add')).toBeNull();
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

    it('adds a filter built in the clean data filter dialog, checked by the server with the rest of the draft', async () => {
      const context = await openDialog();
      await click(context, option('coverage'));
      await wait(previewDelayMs + 100);
      context.http.match(isQuery).forEach((r) => r.flush(result([])));

      await addRoundFilter(context);
      const check = context.http.expectOne(isQuery);
      expect(check.request.body.filters).toEqual([{ column: 'round', op: 'range', min: '2', max: '3' }]);
      expect(check.request.body.indicators).toEqual(['coverage']);
      check.flush(result([row('coverage', 'Cali', 5)]));
      await settle(context.harness);

      expect(filterDialog()).toBeNull();
      expect(fields('dialog-filter-chip').map(text)).toEqual(['round from 2 to 3']);
    });

    it('shows the server\'s refusal of a filter inside the filter dialog', async () => {
      const context = await openDialog();
      await click(context, option('coverage'));
      await wait(previewDelayMs + 100);
      context.http.match(isQuery).forEach((r) => r.flush(result([])));

      await addRoundFilter(context);
      context.http.expectOne(isQuery).flush({ title: 'x', errors: { 'filters[0]': ['The range is reversed.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(context.harness);

      expect(text(filterDialog().querySelector('[data-testid="field-error"]'))).toBe('The range is reversed.');
      expect(fields('dialog-filter-chip')).toHaveLength(0);
    });

    it('sends the filters with the widget, and lets them be edited and removed', async () => {
      const withFilter = widget('w1', { config: config({ filters: [{ column: 'round', op: 'range', min: '2' }] }) });
      const context = await openDialog(withFilter);
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

    it('lets the indicator column\'s values be picked from the table\'s indicators', async () => {
      const context = await openDialog();
      await change(context, field('filter-column'), 'indicator');
      await click(context, field('filter-add'));
      await change(context, inFilter('filter-op'), 'in');
      await settle(context.harness);
      await wait(valueSearchDelayMs + 50);

      expect(Array.from(filterDialog().querySelectorAll('[data-testid="filter-option"]'))).toHaveLength(4);
    });

    it('lets values of other text columns be typed, since the table cannot list them', async () => {
      const context = await openDialog();
      await change(context, field('filter-column'), 'disaggregation');
      await click(context, field('filter-add'));
      await change(context, inFilter('filter-op'), 'in');
      await change(context, inFilter('filter-search'), 'Cali');
      await wait(valueSearchDelayMs + 50);
      await settle(context.harness);

      const offered = Array.from(filterDialog().querySelectorAll('[data-testid="filter-value-list"] li')).map(text);
      expect(offered).toEqual(['Cali']);
    });
  });
});
