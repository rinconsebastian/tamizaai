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

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

/** Opens a row's actions menu and chooses an item. */
async function chooseAction(opened: Awaited<ReturnType<typeof open>>, row: string, action: 'replace' | 'delete') {
  byTestId(opened.root, `reference-row-${row}`)!.querySelector<HTMLButtonElement>('[data-testid="menu-trigger"]')!.click();
  await settle(opened.harness);
  document.querySelector<HTMLElement>(`[data-testid="reference-${action}-${row}"]`)!.click();
  await settle(opened.harness);
}

describe('ProjectReference', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('lists the tables with their file, size and who updated them', async () => {
    const { root } = await open('analyst');

    const row = byTestId(root, 'reference-row-population')!;
    expect(row.textContent).toContain('population.xlsx');
    expect(row.textContent).toContain('1,123 rows, 2 columns');
    expect(row.textContent).toContain('Ada');
    expect(byTestId(root, 'reference-preview-population')).not.toBeNull();
    expect(byTestId(root, 'reference-cards')?.children.length).toBe(1);
  });

  it('words the size of a table with one row and one column in the singular', async () => {
    const lookup: ReferenceTable = { ...population, name: 'lookup', fileName: 'lookup.csv', rowCount: 1, columns: [population.columns[0]] };
    const { root } = await open('analyst', [lookup]);

    expect(text(byTestId(root, 'reference-row-lookup'))).toContain('1 row, 1 column');
    expect(text(byTestId(root, 'reference-cards')!.children[0])).toContain('lookup.csv · 1 row, 1 column ·');
  });

  it('words the preview note for one row in the singular', async () => {
    const lookup: ReferenceTable = { ...population, name: 'lookup', fileName: 'lookup.csv', rowCount: 1 };
    const { root, http, harness } = await open('analyst', [lookup]);

    byTestId<HTMLButtonElement>(root, 'reference-preview-lookup')!.click();
    await settle(harness);
    http.expectOne(`${url}/lookup`).flush({ table: lookup, rows: [['05001', 2612958]] });
    await settle(harness);

    expect(text(byTestId(overlay(), 'reference-preview-note'))).toContain('First 1 of 1 row, from lookup.csv');
  });

  it('opens with the title and no introduction', async () => {
    const { root } = await open('analyst');
    expect(root.textContent).not.toContain('Tables your scripts read by name');
    expect(text(root.querySelector('h2'))).toBe('Reference data');
  });

  it('puts one line about each table in the phone cards, with formatted counts and the same update text as the table', async () => {
    const { root } = await open('analyst');
    const card = byTestId(root, 'reference-cards')!.children[0];
    expect(text(card)).toContain('population.xlsx · 1,123 rows, 2 columns · Updated by Ada, Oct 2, 2026');
    expect(text(byTestId(root, 'reference-row-population'))).toContain('Updated by Ada, Oct 2, 2026');
    expect(text(card)).not.toMatch(/\d{1,2}:\d{2}:\d{2}/);
  });

  it('keeps Preview as the one visible action of a row and Replace and Delete in its menu', async () => {
    const opened = await open('analyst');
    const row = byTestId(opened.root, 'reference-row-population')!;
    expect(byTestId(row, 'reference-replace-population')).toBeNull();
    expect(byTestId(row, 'reference-delete-population')).toBeNull();
    const trigger = row.querySelector<HTMLButtonElement>('[data-testid="menu-trigger"]')!;
    expect(trigger.getAttribute('aria-label')).toBe('Actions for population');
    trigger.click();
    await settle(opened.harness);
    expect([...document.querySelectorAll('[role="menuitem"]')].map((item) => text(item))).toEqual(['Replace', 'Delete']);
  });

  describe('when there are no tables', () => {
    it('invites an editor to upload the first one, with the button in the message', async () => {
      const { root } = await open('analyst', []);
      const empty = byTestId(root, 'reference-empty')!;
      expect(empty.textContent).toContain('Upload a CSV or XLSX file');
      expect(empty.contains(byTestId(root, 'reference-add-empty'))).toBe(true);
      expect(byTestId(root, 'reference-add')).toBeNull(); // the header button waits until there is a list
    });
  });

  it('previews a table with its column types and rows, and shows the original header of a renamed column', async () => {
    const { root, http, harness } = await open('analyst');

    byTestId<HTMLButtonElement>(root, 'reference-preview-population')!.click();
    await settle(harness);
    http.expectOne(`${url}/population`).flush({ table: population, rows: [['05001', 2612958], ['11001', null]] });
    await settle(harness);

    expect(byTestId(overlay(), 'reference-preview-note')?.textContent).toContain('First 2 of 1,123 rows, from population.xlsx');
    expect([...overlay().querySelectorAll('[data-testid="column-type"]')].map((e) => e.textContent?.trim())).toEqual(['text', 'integer']);
    expect([...overlay().querySelectorAll('[data-testid="column-label"]')].map((e) => e.textContent?.trim())).toEqual(['Municipality code', 'Population 2026']);
    expect(overlay().querySelector('tbody')?.textContent).toContain('05001');
    expect(overlay().querySelector('tmz-table-container')!.className).not.toContain('max-h-96'); // the dialog body is the one scroll region
  });

  it('uploads a file with a suggested name and reports renamed columns', async () => {
    const { root, http, harness } = await open('analyst', []);

    byTestId<HTMLButtonElement>(root, 'reference-add-empty')!.click();
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
    const notice = byTestId(root, 'reference-renamed')!;
    expect(text(notice)).toContain('Columns of population were renamed for scripts (1)');
    expect(text(notice)).toContain('Municipality code → municipality_code');
    expect(byTestId(root, 'reference-row-population')).not.toBeNull();
  });

  it('lets the member dismiss the note about renamed columns', async () => {
    const { root, http, harness } = await open('analyst', []);
    byTestId<HTMLButtonElement>(root, 'reference-add-empty')!.click();
    await settle(harness);
    chooseFile(byTestId<HTMLInputElement>(overlay(), 'reference-file')!, 'population.xlsx');
    await settle(harness);
    byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
    await settle(harness);
    http.expectOne((r) => r.url === url && r.method === 'POST').flush({ table: population, renamed: [{ header: 'Municipality code', name: 'municipality_code' }] });
    await tick();
    await settle(harness);
    http.expectOne(url).flush([population]);
    await settle(harness);
    expect(byTestId(root, 'reference-renamed')).not.toBeNull();

    byTestId<HTMLButtonElement>(root, 'reference-renamed-dismiss')!.click();
    await settle(harness);
    expect(byTestId(root, 'reference-renamed')).toBeNull();
  });

  it('closes the list of renames when there are many', async () => {
    const renamed = Array.from({ length: 8 }, (_, i) => ({ header: `Header ${i}`, name: `header_${i}` }));
    const { root, http, harness } = await open('analyst', []);
    byTestId<HTMLButtonElement>(root, 'reference-add-empty')!.click();
    await settle(harness);
    chooseFile(byTestId<HTMLInputElement>(overlay(), 'reference-file')!, 'wide.csv');
    await settle(harness);
    byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
    await settle(harness);
    http.expectOne((r) => r.url === url && r.method === 'POST').flush({ table: population, renamed });
    await tick();
    await settle(harness);
    http.expectOne(url).flush([population]);
    await settle(harness);

    expect((byTestId(root, 'reference-renamed-list')!.querySelector('details') as HTMLDetailsElement).open).toBe(false);
    expect(text(byTestId(root, 'reference-renamed'))).toContain('(8)');
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

  describe('replacing a table', () => {
    it('uses the dialog with the name fixed, and replaces the rows with the chosen file', async () => {
      const opened = await open('analyst');
      const { root, http, harness } = opened;
      await chooseAction(opened, 'population', 'replace');

      expect(text(overlay().querySelector('h2'))).toBe('Replace population');
      expect(byTestId<HTMLInputElement>(overlay(), 'reference-name')!.value).toBe('population');
      expect(byTestId<HTMLInputElement>(overlay(), 'reference-name')!.readOnly).toBe(true);
      expect(text(overlay())).toContain('Runs that already started keep the current rows');

      chooseFile(byTestId<HTMLInputElement>(overlay(), 'reference-file')!, 'population-2027.csv');
      await settle(harness);
      expect(byTestId<HTMLInputElement>(overlay(), 'reference-name')!.value).toBe('population'); // the file name does not rename the table
      byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
      await settle(harness);

      const request = http.expectOne((r) => r.url === `${url}/population` && r.method === 'PUT');
      expect((request.request.body as FormData).get('name')).toBeNull();
      request.flush({ table: { ...population, fileName: 'population-2027.csv' }, renamed: [] });
      await tick();
      await settle(harness);
      http.expectOne(url).flush([{ ...population, fileName: 'population-2027.csv' }]);
      await settle(harness);

      expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toContain('Reference table population replaced');
      expect(byTestId(root, 'reference-renamed')).toBeNull();
      expect(byTestId(root, 'reference-row-population')?.textContent).toContain('population-2027.csv');
    });

    it('explains a refused file in the dialog, with the choice kept, instead of in a message that fades', async () => {
      const opened = await open('analyst');
      const { http, harness } = opened;
      await chooseAction(opened, 'population', 'replace');
      chooseFile(byTestId<HTMLInputElement>(overlay(), 'reference-file')!, 'empty.csv');
      await settle(harness);
      byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
      await settle(harness);
      http
        .expectOne((r) => r.url === `${url}/population` && r.method === 'PUT')
        .flush({ errors: { file: ['The file has a header and no rows.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);

      expect(text(byTestId(overlay(), 'reference-file-error'))).toBe('The file has a header and no rows.');
      expect(text(byTestId(overlay(), 'reference-file-name'))).toBe('empty.csv');
      expect(TestBed.inject(ToastService).toasts()).toHaveLength(0);
    });

    it('says nothing about renames that the table already had', async () => {
      const renamedBefore = { ...population, columns: [{ name: 'municipality_code', header: 'Municipality code', type: 'text' as const }] };
      const opened = await open('analyst', [renamedBefore]);
      const { http, harness, root } = opened;
      await chooseAction(opened, 'population', 'replace');
      chooseFile(byTestId<HTMLInputElement>(overlay(), 'reference-file')!, 'population-2027.csv');
      await settle(harness);
      byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
      await settle(harness);
      http
        .expectOne((r) => r.url === `${url}/population` && r.method === 'PUT')
        .flush({ table: renamedBefore, renamed: [{ header: 'Municipality code', name: 'municipality_code' }] });
      await tick();
      await settle(harness);
      http.expectOne(url).flush([renamedBefore]);
      await settle(harness);

      expect(byTestId(root, 'reference-renamed')).toBeNull();
    });

    it('does tell about a rename that is new', async () => {
      const opened = await open('analyst');
      const { http, harness, root } = opened;
      await chooseAction(opened, 'population', 'replace');
      chooseFile(byTestId<HTMLInputElement>(overlay(), 'reference-file')!, 'population-2027.csv');
      await settle(harness);
      byTestId<HTMLButtonElement>(overlay(), 'reference-upload')!.click();
      await settle(harness);
      http
        .expectOne((r) => r.url === `${url}/population` && r.method === 'PUT')
        .flush({ table: population, renamed: [{ header: 'New header!', name: 'new_header' }] });
      await tick();
      await settle(harness);
      http.expectOne(url).flush([population]);
      await settle(harness);

      expect(text(byTestId(root, 'reference-renamed'))).toContain('New header! → new_header');
    });
  });

  it('deletes a table only after confirmation', async () => {
    const opened = await open('admin');
    const { root, http, harness } = opened;

    await chooseAction(opened, 'population', 'delete');
    expect(overlay().textContent).toContain('ctx.reference["population"]');
    (overlay().querySelector('button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
    await tick();
    await settle(harness);
    http.expectNone((r) => r.method === 'DELETE');

    await chooseAction(opened, 'population', 'delete');
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
