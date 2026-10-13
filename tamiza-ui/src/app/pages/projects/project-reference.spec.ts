import { TestBed } from '@angular/core/testing';
import { ProjectRole } from '../../core/projects/projects.service';
import { ReferenceTable, suggestedTableName } from '../../core/projects/reference.service';
import { ToastService } from '../../shared/ui';
import { tick } from '../../../testing/cdk';
import { byTestId, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';

const url = `/api/v1/projects/${projectId}/reference-tables`;

const population: ReferenceTable = {
  name: 'population',
  fileName: 'population.xlsx',
  rowCount: 1123,
  columns: [
    { name: 'municipality_code', header: 'Municipality code', type: 'text' },
    { name: 'population_2026', header: 'Population 2026', type: 'integer' },
  ],
  updatedBy: { id: 'u1', name: 'Ada' },
  createdAt: '2026-10-01T12:00:00Z',
  updatedAt: '2026-10-02T12:00:00Z',
};

async function open(role: ProjectRole, tables: ReferenceTable[] = [population]) {
  const opened = await openProjectSection('reference', role);
  opened.http.expectOne(url).flush(tables);
  await settle(opened.harness);
  return opened;
}

function overlay(): HTMLElement {
  return document.querySelector('.cdk-overlay-container') as HTMLElement;
}

function chooseFile(input: HTMLInputElement, name: string): void {
  Object.defineProperty(input, 'files', { value: [new File(['a,b\n1,2\n'], name)], configurable: true });
  input.dispatchEvent(new Event('change'));
}

describe('ProjectReference', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('lists the tables for viewers without editing actions', async () => {
    const { root } = await open('viewer');

    const row = byTestId(root, 'reference-row-population')!;
    expect(row.textContent).toContain('population.xlsx');
    expect(row.textContent).toContain('1123 rows, 2 columns');
    expect(row.textContent).toContain('Ada');
    expect(byTestId(root, 'reference-preview-population')).not.toBeNull();
    for (const id of ['reference-add', 'reference-replace-population', 'reference-delete-population']) {
      expect(byTestId(root, id)).toBeNull();
    }
    expect(byTestId(root, 'reference-cards')?.children.length).toBe(1);
  });

  it('explains the empty state to each role', async () => {
    const { root } = await open('analyst', []);

    expect(byTestId(root, 'reference-empty')?.textContent).toContain('Upload a CSV or XLSX file');
  });

  it('previews a table with its column types and rows', async () => {
    const { root, http, harness } = await open('viewer');

    byTestId<HTMLButtonElement>(root, 'reference-preview-population')!.click();
    await settle(harness);
    http.expectOne(`${url}/population`).flush({ table: population, rows: [['05001', 2612958], ['11001', null]] });
    await settle(harness);

    expect(byTestId(overlay(), 'reference-preview-note')?.textContent).toContain('First 2 of 1123 rows, from population.xlsx');
    expect([...overlay().querySelectorAll('[data-testid="column-type"]')].map((e) => e.textContent?.trim())).toEqual(['text', 'integer']);
    expect(overlay().querySelector('tbody')?.textContent).toContain('05001');
  });

  it('uploads a file with a suggested name and reports renamed columns', async () => {
    const { root, http, harness } = await open('analyst', []);

    byTestId<HTMLButtonElement>(root, 'reference-add')!.click();
    await settle(harness);
    chooseFile(byTestId<HTMLInputElement>(overlay(), 'reference-file')!, 'Población 2026.xlsx');
    await settle(harness);
    expect(byTestId<HTMLInputElement>(overlay(), 'reference-name')!.value).toBe('poblacion_2026');
    typeInto(byTestId<HTMLInputElement>(overlay(), 'reference-name')!, 'population');
    byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
    await settle(harness);

    const request = http.expectOne((r) => r.url === url && r.method === 'POST');
    const body = request.request.body as FormData;
    expect(body.get('name')).toBe('population');
    expect((body.get('file') as File).name).toBe('Población 2026.xlsx');
    request.flush({ table: population, renamed: [{ header: 'Municipality code', name: 'municipality_code' }] });
    await tick();
    await settle(harness);
    http.expectOne(url).flush([population]);
    await settle(harness);

    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('Reference table population uploaded');
    expect(byTestId(root, 'reference-renamed')?.textContent).toContain('Municipality code → municipality_code');
    expect(byTestId(root, 'reference-row-population')).not.toBeNull();
  });

  it('keeps the dialog open with the message when the name is taken or invalid', async () => {
    const { root, http, harness } = await open('analyst');

    byTestId<HTMLButtonElement>(root, 'reference-add')!.click();
    await settle(harness);
    byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
    await settle(harness);
    http.expectNone((r) => r.method === 'POST');
    expect(byTestId(overlay(), 'reference-file-error')?.textContent).toContain('Choose a CSV or XLSX file');

    chooseFile(byTestId<HTMLInputElement>(overlay(), 'reference-file')!, 'population.csv');
    await settle(harness);
    byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
    await settle(harness);
    http
      .expectOne((r) => r.url === url && r.method === 'POST')
      .flush({ title: 'Conflict', detail: 'x', code: 'reference.exists' }, { status: 409, statusText: 'Conflict' });
    await settle(harness);
    expect(byTestId(overlay(), 'reference-upload-error')?.textContent).toContain('already has a reference table with this name');

    typeInto(byTestId<HTMLInputElement>(overlay(), 'reference-name')!, 'Population 2026');
    byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
    await settle(harness);
    http
      .expectOne((r) => r.url === url && r.method === 'POST')
      .flush({ errors: { name: ['Use lowercase letters, digits and underscores.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(harness);
    expect(byTestId(overlay(), 'field-error')?.textContent).toContain('lowercase letters');
    expect(byTestId(overlay(), 'reference-upload-error')).toBeNull();
  });

  it('replaces a table only after confirmation', async () => {
    const { root, http, harness } = await open('analyst');
    const picker = byTestId<HTMLInputElement>(root, 'reference-replace-file')!;
    let opened = 0;
    picker.addEventListener('click', (event) => {
      opened++;
      event.preventDefault();
    });

    byTestId<HTMLButtonElement>(root, 'reference-replace-population')!.click();
    expect(opened).toBe(1);
    chooseFile(picker, 'population-2027.csv');
    await settle(harness);
    expect(overlay().textContent).toContain('Replace population?');
    expect(overlay().textContent).toContain('population-2027.csv');
    (overlay().querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await tick();
    await settle(harness);

    const request = http.expectOne((r) => r.url === `${url}/population` && r.method === 'PUT');
    expect((request.request.body as FormData).get('name')).toBeNull();
    request.flush({ table: { ...population, fileName: 'population-2027.csv' }, renamed: [] });
    await settle(harness);
    http.expectOne(url).flush([{ ...population, fileName: 'population-2027.csv' }]);
    await settle(harness);

    expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('Reference table population replaced');
    expect(byTestId(root, 'reference-renamed')).toBeNull();
    expect(byTestId(root, 'reference-row-population')?.textContent).toContain('population-2027.csv');
  });

  it('deletes a table only after confirmation', async () => {
    const { root, http, harness } = await open('admin');

    byTestId<HTMLButtonElement>(root, 'reference-delete-population')!.click();
    await settle(harness);
    expect(overlay().textContent).toContain('ctx.reference["population"]');
    (overlay().querySelector('button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    http.expectNone((r) => r.method === 'DELETE');

    byTestId<HTMLButtonElement>(root, 'reference-delete-population')!.click();
    await settle(harness);
    (overlay().querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    http.expectOne((r) => r.url === `${url}/population` && r.method === 'DELETE').flush(null, { status: 204, statusText: 'No Content' });
    await settle(harness);
    http.expectOne(url).flush([]);
    await settle(harness);

    expect(byTestId(root, 'reference-empty')).not.toBeNull();
  });
});

describe('suggestedTableName', () => {
  it.each([
    ['Población 2026.xlsx', 'poblacion_2026'],
    ['population.csv', 'population'],
    ['2026 targets.csv', 'table_2026_targets'],
    ['___.csv', 'table'],
  ])('suggests a valid name for %s', (file, name) => {
    expect(suggestedTableName(file)).toBe(name);
  });
});
