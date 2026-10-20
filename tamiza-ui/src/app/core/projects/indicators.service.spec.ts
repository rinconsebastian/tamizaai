import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IndicatorsService, WidgetConfig, chartTypes, emptyConfig, needsGroup } from './indicators.service';

const base = '/api/v1/projects/p1/indicators';
const dashboards = '/api/v1/projects/p1/dashboards';

describe('IndicatorsService', () => {
  let service: IndicatorsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(IndicatorsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const config: WidgetConfig = {
    ...emptyConfig('coverage'),
    groupBy: 'municipality',
    ratio: { numerator: 'numerator', denominator: 'denominator' },
    filters: [{ column: 'year', op: 'range', min: '2024' }],
  };

  it('reads the overview, and the columns of one indicator\'s table', () => {
    service.overview('p1').subscribe();
    service.columns('p1', 'piped_water_rate').subscribe();

    expect(http.expectOne(base).request.method).toBe('GET');
    expect(http.expectOne(`${base}/piped_water_rate/columns`).request.method).toBe('GET');
  });

  it('posts the widget configuration as the query body, with the chart type when it is known', () => {
    service.query('p1', config).subscribe();
    service.query('p1', config, 'histogram').subscribe();

    const [plain, typed] = http.match(`${base}/query`);
    expect(plain.request.method).toBe('POST');
    expect(plain.request.body).toEqual({ config });
    expect(typed.request.body).toEqual({ config, chartType: 'histogram' });
  });

  it('sends the dashboard filters in force with a query only when there are some', () => {
    const filters = [
      { column: 'municipality', kind: 'text' as const, values: ['Cali'] },
      { column: 'round', kind: 'number' as const, min: '2', max: null },
    ];
    service.query('p1', config, 'column', filters).subscribe();
    service.query('p1', config, undefined, []).subscribe();

    const [filtered, none] = http.match(`${base}/query`);
    expect(filtered.request.body).toEqual({ config, chartType: 'column', dashboardFilters: filters });
    expect(none.request.body).toEqual({ config });
  });

  it('replaces a dashboard\'s filters with the whole list and answers them as saved', () => {
    const filters = [{ column: 'municipality', label: 'Municipality', kind: 'text' as const, default: { values: ['Cali'] } }];
    let saved: unknown;
    service.saveDashboardFilters('p1', 'd2', filters).subscribe((answer) => (saved = answer));

    const request = http.expectOne(`${dashboards}/d2/filters`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ filters });
    request.flush({ filters: [{ ...filters[0], label: 'Municipality' }] });
    expect(saved).toEqual(filters);
  });

  it('reads the values a dashboard\'s text filter offers, with the search only when there is one', () => {
    service.dashboardValues('p1', 'd2', 'municipality').subscribe();
    service.dashboardValues('p1', 'd2', 'municipality', 'pal').subscribe();

    const [all, searched] = http.match((r) => r.url === `${dashboards}/d2/values`);
    expect(all.request.method).toBe('GET');
    expect(all.request.params.keys()).toEqual(['column']);
    expect(all.request.params.get('column')).toBe('municipality');
    expect(searched.request.params.get('search')).toBe('pal');
  });

  it('creates a widget on its dashboard, leaving the layout out when there is none', () => {
    service.create('p1', 'd1', { title: 'Coverage', chartType: 'column', config }).subscribe();

    const request = http.expectOne(`${dashboards}/d1/widgets`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ title: 'Coverage', chartType: 'column', config });
  });

  it('patches only what changed and deletes by id, under the widget\'s dashboard', () => {
    service.update('p1', 'd1', 'w1', { layout: { x: 1, y: 2, w: 3, h: 4 } }).subscribe();
    service.remove('p1', 'd1', 'w1').subscribe();

    const patch = http.expectOne((r) => r.method === 'PATCH');
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ layout: { x: 1, y: 2, w: 3, h: 4 } });
    expect(patch.request.url).toBe(`${dashboards}/d1/widgets/w1`);
    expect(http.expectOne((r) => r.method === 'DELETE').request.url).toBe(`${dashboards}/d1/widgets/w1`);
  });

  it('moves a widget to another dashboard with a patch of the target alone, sent to the dashboard it is on', () => {
    service.update('p1', 'd1', 'w1', { dashboardId: 'd2' }).subscribe();

    const patch = http.expectOne(`${dashboards}/d1/widgets/w1`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ dashboardId: 'd2' });
  });

  it('reads one dashboard with its widgets', () => {
    service.dashboard('p1', 'd2').subscribe();

    const request = http.expectOne(`${dashboards}/d2`);
    expect(request.request.method).toBe('GET');
    expect(request.request.body).toBeNull();
  });

  it('adds, renames and removes a dashboard', () => {
    service.addDashboard('p1', 'Field progress').subscribe();
    service.renameDashboard('p1', 'd2', 'Coverage').subscribe();
    service.removeDashboard('p1', 'd3').subscribe();

    const add = http.expectOne((r) => r.method === 'POST');
    expect(add.request.url).toBe(dashboards);
    expect(add.request.body).toEqual({ name: 'Field progress' });
    const rename = http.expectOne((r) => r.method === 'PATCH');
    expect(rename.request.url).toBe(`${dashboards}/d2`);
    expect(rename.request.body).toEqual({ name: 'Coverage' });
    const remove = http.expectOne((r) => r.method === 'DELETE');
    expect(remove.request.url).toBe(`${dashboards}/d3`);
    expect(remove.request.body).toBeNull();
  });

  it('reorders the dashboards with the order the page shows and the one it asks for', () => {
    service.reorderDashboards('p1', ['d1', 'd2', 'd3'], ['d2', 'd1', 'd3']).subscribe();

    const request = http.expectOne(`${dashboards}/order`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ expectedIds: ['d1', 'd2', 'd3'], dashboardIds: ['d2', 'd1', 'd3'] });
  });

  it('duplicates a dashboard under the name given', () => {
    service.duplicateDashboard('p1', 'd2', 'Housing (copy)').subscribe();

    const request = http.expectOne(`${dashboards}/d2/copies`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Housing (copy)' });
  });

  it('asks for the export of one indicator\'s whole table in the chosen format', () => {
    service.export('p1', 'coverage', 'xlsx').subscribe();

    const request = http.expectOne(`${base}/coverage/exports`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ format: 'xlsx' });
  });

  it('starts a query with an indicator and nothing else chosen: the default measure, no group', () => {
    expect(emptyConfig('coverage')).toEqual({
      indicator: 'coverage',
      measure: null,
      ratio: null,
      groupBy: null,
      filters: [],
      geometryColumn: null,
      buckets: null,
      sort: null,
      tooltip: [],
      basemap: null,
      ignoredDashboardFilters: [],
    });
  });

  it('knows which chart types need a column to group by', () => {
    expect(chartTypes.map((t) => [t, needsGroup(t)])).toEqual([
      ['stat', false],
      ['bar', true],
      ['column', true],
      ['line', true],
      ['histogram', false],
      ['pie', true],
      ['map', true],
    ]);
  });
});
