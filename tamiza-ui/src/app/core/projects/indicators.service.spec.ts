import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DashboardBases, DashboardColors, IndicatorsService, WidgetConfig, chartTypes, dashboardChangeKinds, basesOf, emptyConfig, hasSeries, needsGroup, needsSeries, paletteIds, scaleIds, takesSeries } from './indicators.service';

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

  it('posts the settings and filters the screen shows to a dashboard\'s widget-exports, and answers the link', () => {
    const request = {
      format: 'xlsx' as const,
      title: 'Coverage by municipality',
      chartType: 'bar' as const,
      config,
      filters: [{ column: 'municipality', kind: 'text' as const, values: ['Cali'], ignored: false }],
      locale: 'es-CO',
    };
    const result = { downloadUrl: '/api/v1/data-exports/t', fileName: 'a.xlsx', rowCount: 3, publishedAt: '2026-11-02T10:41:00Z', expiresAt: '2026-11-02T10:46:00Z' };
    let answer: unknown;

    service.exportWidget('p1', 'd1', request).subscribe((r) => (answer = r));
    const call = http.expectOne(`${dashboards}/d1/widget-exports`);
    call.flush(result);

    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual(request);
    expect(answer).toEqual(result);
  });

  it('sends the version being viewed with a widget export, and nothing about it on the current dashboard', () => {
    const request = { format: 'xlsx' as const, title: 'W', chartType: 'bar' as const, config, filters: [], locale: 'en-US' };

    service.exportWidget('p1', 'd1', { ...request, version: 9 }).subscribe();
    service.exportWidget('p1', 'd1', request).subscribe();

    const [viewed, current] = http.match(`${dashboards}/d1/widget-exports`);
    expect(viewed.request.body.version).toBe(9);
    expect('version' in current.request.body).toBe(false);
    viewed.flush({});
    current.flush({});
  });

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

  it('asks for the overall value only when told to, and then adds only `overall: true` to the body', () => {
    const filters = [{ column: 'municipality', kind: 'text' as const, values: ['Cali'] }];
    service.query('p1', config, 'column').subscribe();
    service.query('p1', config, 'column', [], false).subscribe();
    service.query('p1', config, 'column', filters, true).subscribe();
    service.query('p1', config, 'column', [], true).subscribe();

    const [plain, off, withFilters, alone] = http.match(`${base}/query`);
    expect(plain.request.body).toEqual({ config, chartType: 'column' });
    expect('overall' in off.request.body).toBe(false);
    expect(withFilters.request.body).toEqual({ config, chartType: 'column', dashboardFilters: filters, overall: true });
    expect(alone.request.body).toEqual({ config, chartType: 'column', overall: true });
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

  it('replaces a dashboard\'s palette and colors kept by value with a put to its colors, and answers them as saved', () => {
    const colors: DashboardColors = { palette: 'earth', valueColors: [{ column: 'sex', value: 'Female', color: 5 }] };
    let saved: unknown;
    service.saveColors('p1', 'd2', colors).subscribe((answer) => (saved = answer));

    const request = http.expectOne(`${dashboards}/d2/colors`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(colors);
    request.flush(colors);
    expect(saved).toEqual(colors);
  });

  it('sets the source of a dashboard with a put to its source, and answers it as saved', () => {
    let saved: unknown;
    service.saveSource('p1', 'd2', '  Encuesta de hogares, ronda 2  ').subscribe((answer) => (saved = answer));

    const request = http.expectOne(`${dashboards}/d2/source`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ source: '  Encuesta de hogares, ronda 2  ' });
    request.flush({ source: 'Encuesta de hogares, ronda 2' });
    expect(saved).toEqual({ source: 'Encuesta de hogares, ronda 2' });
  });

  it('sets the base settings of a dashboard with a put to its bases, and answers them as saved', () => {
    const bases: DashboardBases = { minN: 30, showN: true };
    let saved: unknown;
    service.saveBases('p1', 'd2', bases).subscribe((answer) => (saved = answer));

    const request = http.expectOne(`${dashboards}/d2/bases`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ minN: 30, showN: true });
    request.flush(bases);
    expect(saved).toEqual(bases);
  });

  it('turns the threshold off by sending null, and sends nothing else the object it was given holds', () => {
    service.saveBases('p1', 'd2', { minN: null, showN: false, name: 'Teatro' } as DashboardBases).subscribe();

    expect(http.expectOne(`${dashboards}/d2/bases`).request.body).toEqual({ minN: null, showN: false });
  });

  it('reads the base settings of a dashboard or a version that has none as off', () => {
    expect(basesOf({})).toEqual({ minN: null, showN: false });
    expect(basesOf(null)).toEqual({ minN: null, showN: false });
    expect(basesOf({ minN: 30, showN: true })).toEqual({ minN: 30, showN: true });
  });

  it('removes the source of a dashboard by sending null', () => {
    let saved: unknown;
    service.saveSource('p1', 'd2', null).subscribe((answer) => (saved = answer));

    const request = http.expectOne(`${dashboards}/d2/source`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ source: null });
    request.flush({ source: null });
    expect(saved).toEqual({ source: null });
  });

  it('sends the palette and the colors kept by value of a colors save, and nothing else the object it was given holds', () => {
    const dashboard = { id: 'd2', name: 'Housing', palette: 'vivid' as const, valueColors: [], filters: [], widgets: [], version: 3 };
    service.saveColors('p1', 'd2', dashboard).subscribe();

    expect(http.expectOne(`${dashboards}/d2/colors`).request.body).toEqual({ palette: 'vivid', valueColors: [] });
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

  it('names the indicators to read the values from, one parameter each, only when there are some', () => {
    service.dashboardValues('p1', 'd2', 'municipality', undefined, ['coverage', 'piped_water_rate']).subscribe();
    service.dashboardValues('p1', 'd2', 'municipality', 'pal', ['coverage']).subscribe();
    service.dashboardValues('p1', 'd2', 'municipality', undefined, []).subscribe();

    const [two, searched, none] = http.match((r) => r.url === `${dashboards}/d2/values`);
    expect(two.request.params.getAll('indicator')).toEqual(['coverage', 'piped_water_rate']);
    expect(two.request.urlWithParams).toBe(`${dashboards}/d2/values?column=municipality&indicator=coverage&indicator=piped_water_rate`);
    expect(searched.request.params.get('search')).toBe('pal');
    expect(searched.request.params.getAll('indicator')).toEqual(['coverage']);
    expect(none.request.params.keys()).toEqual(['column']);
  });

  it('reads, saves and removes a dashboard\'s scheduled email', () => {
    const settings = { recipients: ['ana@partner.org', 'luis@city.gov.co'], days: [1, 4], time: '07:00', active: true, locale: 'es-CO' };
    const report = { settings, timeZone: 'America/Bogota', updatedBy: { name: 'Ada' }, updatedAt: '2026-10-27T10:00:00-05:00', lastSentAt: null, lastFailure: null };
    let saved: unknown;
    service.getReport('p1', 'd2').subscribe();
    service.saveReport('p1', 'd2', settings).subscribe((answer) => (saved = answer));
    service.removeReport('p1', 'd2').subscribe();

    const read = http.expectOne((r) => r.method === 'GET');
    expect(read.request.url).toBe(`${dashboards}/d2/report`);
    expect(read.request.body).toBeNull();
    const save = http.expectOne((r) => r.method === 'PUT');
    expect(save.request.url).toBe(`${dashboards}/d2/report`);
    expect(save.request.body).toEqual(settings);
    save.flush(report);
    expect(saved).toEqual(report);
    const remove = http.expectOne((r) => r.method === 'DELETE');
    expect(remove.request.url).toBe(`${dashboards}/d2/report`);
    expect(remove.request.body).toBeNull();
  });

  it('asks for a dashboard\'s scheduled email to be sent now, with no body', () => {
    service.sendReportNow('p1', 'd2').subscribe();

    const request = http.expectOne(`${dashboards}/d2/report/send`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
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

  it('saves the places of several widgets of a dashboard in one put, and answers the widgets as saved', () => {
    const layouts = [
      { id: 'w1', layout: { x: 6, y: 0, w: 6, h: 4 } },
      { id: 'w2', layout: { x: 6, y: 4, w: 6, h: 4 } },
    ];
    const saved = layouts.map(({ id, layout }) => ({ id, dashboardId: 'd1', title: id, chartType: 'column', config, layout, updatedAt: '2026-10-31T10:00:00Z' }));
    let answer: unknown;
    service.saveLayout('p1', 'd1', layouts).subscribe((widgets) => (answer = widgets));

    const request = http.expectOne(`${dashboards}/d1/layout`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ widgets: layouts });
    request.flush(saved);
    expect(answer).toEqual(saved);
  });

  describe('versions', () => {
    const summary = {
      number: 12,
      current: true,
      name: 'Housing',
      widgetCount: 3,
      filterCount: 1,
      changes: [{ kind: 'widgetAdded', widget: 'Water source' }],
      moreChanges: 0,
      restoredFrom: null,
      origin: 'mcp',
      note: 'Add five water indicators',
      author: { id: 'u1', name: 'Ana Pérez' },
      createdAt: '2026-10-31T10:30:00Z',
      savedAt: '2026-10-31T10:32:00Z',
    };

    it('lists a dashboard\'s kept versions', () => {
      let list: unknown;
      service.listVersions('p1', 'd2').subscribe((answer) => (list = answer));

      const request = http.expectOne(`${dashboards}/d2/versions`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      request.flush({ current: 12, versions: [summary] });
      expect(list).toEqual({ current: 12, versions: [summary] });
    });

    it('reads one version by its number', () => {
      service.getVersion('p1', 'd2', 9).subscribe();

      const request = http.expectOne(`${dashboards}/d2/versions/9`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
    });

    it('restores a version with a post and no body, and answers what the restore did', () => {
      const restore = {
        restored: true,
        version: { ...summary, number: 13, changes: [{ kind: 'restored', version: 9 }], restoredFrom: 9, origin: 'app', note: null },
        nameKept: false,
        copies: [{ originalId: 'w4', copyId: 'w7', title: 'Coverage', dashboardId: 'd3', dashboardName: 'Field' }],
        dashboard: { id: 'd2', name: 'Housing', filters: [], widgets: [], version: 13 },
      };
      let answer: unknown;
      service.restoreVersion('p1', 'd2', 9).subscribe((restored) => (answer = restored));

      const request = http.expectOne(`${dashboards}/d2/versions/9/restore`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toBeNull();
      request.flush(restore, { status: 201, statusText: 'Created' });
      expect(answer).toEqual(restore);
    });
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
      ['heatmap', true],
      ['pyramid', true],
      ['likert', true],
      ['crosstab', true],
      ['boxplot', false],
    ]);
  });

  it('knows which chart types take a series, which need one, and so have one at all', () => {
    expect(chartTypes.map((t) => [t, takesSeries(t), needsSeries(t), hasSeries(t)])).toEqual([
      ['stat', false, false, false],
      ['bar', true, false, true],
      ['column', true, false, true],
      ['line', true, false, true],
      ['histogram', false, false, false],
      ['pie', false, false, false],
      ['map', false, false, false],
      ['heatmap', false, true, true],
      ['pyramid', false, true, true],
      ['likert', false, true, true],
      ['crosstab', false, true, true],
      ['boxplot', false, false, false],
    ]);
  });

  it('lists the twelve chart types the API knows, in its order', () => {
    expect(chartTypes).toEqual(['stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map', 'heatmap', 'pyramid', 'likert', 'crosstab', 'boxplot']);
  });

  it('lists the four palettes and the seven sequential scales the API knows, in its order', () => {
    expect(paletteIds).toEqual(['tamiza', 'institutional', 'earth', 'vivid']);
    expect(scaleIds).toEqual(['blue', 'teal', 'green', 'orange', 'red', 'purple', 'gray']);
  });

  it('names the change that saves a dashboard\'s colors among the kinds of change a version records', () => {
    expect(dashboardChangeKinds).toContain('colorsChanged');
    expect(dashboardChangeKinds).toContain('sourceChanged');
    expect(dashboardChangeKinds.indexOf('sourceChanged')).toBe(dashboardChangeKinds.indexOf('colorsChanged') + 1);
    expect(new Set(dashboardChangeKinds).size).toBe(dashboardChangeKinds.length);
  });

  it('names the change that saves the base settings of a dashboard among the kinds of change a version records', () => {
    expect(dashboardChangeKinds).toContain('basesChanged');
  });
});
