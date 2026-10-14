import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IndicatorsService, WidgetConfig, emptyConfig, takesOneIndicator } from './indicators.service';

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

  const config: WidgetConfig = { ...emptyConfig(), indicators: ['coverage'], filters: [{ column: 'year', op: 'range', min: '2024' }] };

  it('reads the overview and the column catalog', () => {
    service.overview('p1').subscribe();
    service.columns('p1').subscribe();

    expect(http.expectOne(base).request.method).toBe('GET');
    expect(http.expectOne(`${base}/columns`).request.method).toBe('GET');
  });

  it('posts the widget configuration as the query body', () => {
    service.query('p1', config).subscribe();

    const request = http.expectOne(`${base}/query`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(config);
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

  it('asks for the export of the whole table in the chosen format', () => {
    service.export('p1', 'xlsx').subscribe();

    const request = http.expectOne(`${base}/exports`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ format: 'xlsx' });
  });

  it('defaults to grouping by disaggregation and plotting the value', () => {
    expect(emptyConfig()).toEqual({ indicators: [], groupBy: 'disaggregation', valueField: 'value', filters: [], geometryColumn: null, buckets: null });
  });

  it('knows which chart types take exactly one indicator', () => {
    expect(['stat', 'pie', 'map', 'bar', 'column', 'line', 'histogram'].map((t) => takesOneIndicator(t as never))).toEqual([true, true, true, false, false, false, false]);
  });
});
