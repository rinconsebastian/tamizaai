import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IndicatorsService, WidgetConfig, chartTypes, emptyConfig, needsGroup } from './indicators.service';

const base = '/api/v1/projects/p1/indicators';

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

  it('creates a widget, leaving the layout out when there is none', () => {
    service.create('p1', { title: 'Coverage', chartType: 'column', config }).subscribe();

    const request = http.expectOne(`${base}/widgets`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ title: 'Coverage', chartType: 'column', config });
  });

  it('patches only what changed and deletes by id', () => {
    service.update('p1', 'w1', { layout: { x: 1, y: 2, w: 3, h: 4 } }).subscribe();
    service.remove('p1', 'w1').subscribe();

    const patch = http.expectOne((r) => r.method === 'PATCH');
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ layout: { x: 1, y: 2, w: 3, h: 4 } });
    expect(patch.request.url).toBe(`${base}/widgets/w1`);
    expect(http.expectOne((r) => r.method === 'DELETE').request.url).toBe(`${base}/widgets/w1`);
  });

  it('asks for the export of one indicator\'s whole table in the chosen format', () => {
    service.export('p1', 'coverage', 'xlsx').subscribe();

    const request = http.expectOne(`${base}/coverage/exports`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ format: 'xlsx' });
  });

  it('starts a query with an indicator and nothing else chosen: the default measure, no group', () => {
    expect(emptyConfig('coverage')).toEqual({ indicator: 'coverage', measure: null, ratio: null, groupBy: null, filters: [], geometryColumn: null, buckets: null });
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
