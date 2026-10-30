import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { McpCall, McpConnection, McpService, mcpProjectAccessLevels } from './mcp.service';

const connection: McpConnection = {
  url: 'https://tamiza.example.org/mcp',
  clientId: 'tamiza-mcp',
  callbackPort: 33418,
  maxDataAccess: 'aggregates',
  accountUrl: 'https://kc.example.org/realms/tamiza/account/#/applications',
};

describe('McpService', () => {
  let service: McpService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(McpService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the connection details of the deployment', () => {
    let received: McpConnection | undefined;
    service.connection().subscribe((value) => (received = value));

    const call = http.expectOne('/api/v1/mcp');
    expect(call.request.method).toBe('GET');
    call.flush(connection);
    expect(received).toEqual(connection);
  });

  it('fails with the API status when the server is not enabled (Disabled)', () => {
    let status: number | undefined;
    service.connection().subscribe({ error: (error) => (status = error.status) });

    http.expectOne('/api/v1/mcp').flush(null, { status: 404, statusText: 'Not Found' });

    expect(status).toBe(404);
  });

  it("reads a project's latest calls and gives the list, newest first as the API sent it", () => {
    const items: McpCall[] = [
      { id: 'c2', startedAt: '2026-10-28T15:04:00Z', userName: 'Ada Lovelace', tool: 'create_widget', status: 'succeeded', errorCode: null, access: 'aggregates' },
      { id: 'c1', startedAt: '2026-10-28T15:03:00Z', userName: 'Ada Lovelace', tool: 'query_rows', status: 'refused', errorCode: 'mcp.rowsNotAllowed', access: null },
    ];
    let received: McpCall[] | undefined;
    service.calls('p1').subscribe((value) => (received = value));

    const call = http.expectOne('/api/v1/projects/p1/mcp-calls');
    expect(call.request.method).toBe('GET');
    call.flush({ items });
    expect(received).toEqual(items);
  });

  it('fails with the API status when a viewer asks for the calls', () => {
    let status: number | undefined;
    service.calls('p1').subscribe({ error: (error) => (status = error.status) });

    http.expectOne('/api/v1/projects/p1/mcp-calls').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(status).toBe(403);
  });

  it('lists the levels of a project from the least to the most a client sees', () => {
    expect(mcpProjectAccessLevels).toEqual(['off', 'aggregates', 'rows']);
  });
});
