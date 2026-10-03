import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IngestionService } from './ingestion.service';

describe('IngestionService', () => {
  let service: IngestionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(IngestionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads and updates the ingestion status and settings', () => {
    service.status('p1').subscribe();
    service.updateSettings('p1', { syncIntervalHours: 6 }).subscribe();

    expect(http.expectOne((r) => r.method === 'GET').request.url).toBe('/api/v1/projects/p1/ingestion');
    const patch = http.expectOne((r) => r.method === 'PATCH');
    expect(patch.request.url).toBe('/api/v1/projects/p1/ingestion');
    expect(patch.request.body).toEqual({ syncIntervalHours: 6 });
  });

  it('starts runs and pages the log', () => {
    service.startSync('p1', 'full').subscribe();
    service.syncs('p1', 3, 10).subscribe();

    const post = http.expectOne((r) => r.method === 'POST');
    expect(post.request.url).toBe('/api/v1/projects/p1/syncs');
    expect(post.request.body).toEqual({ kind: 'full' });
    const get = http.expectOne((r) => r.method === 'GET');
    expect(get.request.urlWithParams).toBe('/api/v1/projects/p1/syncs?page=3&pageSize=10');
  });
});
