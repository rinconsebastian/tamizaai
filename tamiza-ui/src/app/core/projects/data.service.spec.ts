import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DataQuery, DataService } from './data.service';

const base = '/api/v1/projects/p1/data';

describe('DataService', () => {
  let service: DataService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(DataService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const query: DataQuery = {
    filters: [{ column: 'age', op: 'range', min: '18', max: '29' }],
    sort: { column: 'name', direction: 'desc' },
    page: 2,
    pageSize: 25,
  };

  it('reads the overview', () => {
    service.overview('p1').subscribe();

    expect(http.expectOne(base).request.method).toBe('GET');
  });

  it('posts the filters, sort, page and page size of a page', () => {
    service.query('p1', 'output', 'persons', query).subscribe();

    const request = http.expectOne(`${base}/output/persons/query`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ filters: query.filters, sort: query.sort, page: 2, pageSize: 25 });
  });

  it('encodes table and column names that are not plain', () => {
    service.query('p1', 'clean', 'a b/c', query).subscribe();
    service.values('p1', 'clean', 'main', 'Mixed Case/1', 'x').subscribe();

    http.expectOne(`${base}/clean/a%20b%2Fc/query`);
    http.expectOne((r) => r.url === `${base}/clean/main/columns/Mixed%20Case%2F1/values`);
  });

  it('sends the search of the values only when there is one', () => {
    service.values('p1', 'output', 'persons', 'name').subscribe();
    service.values('p1', 'output', 'persons', 'name', 'pal').subscribe();
    service.values('p1', 'output', 'persons', 'name', '').subscribe();

    const requests = http.match((r) => r.url === `${base}/output/persons/columns/name/values`);
    expect(requests.map((r) => r.request.params.get('search'))).toEqual([null, 'pal', null]);
  });

  it('posts an export with the format, filters and sort but no page', () => {
    service.export('p1', 'output', 'persons', 'xlsx', { filters: query.filters, sort: query.sort }).subscribe();

    const request = http.expectOne(`${base}/output/persons/exports`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ format: 'xlsx', filters: query.filters, sort: query.sort });
  });
});
