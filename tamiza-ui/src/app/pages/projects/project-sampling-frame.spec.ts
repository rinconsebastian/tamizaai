import { byTestId, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';

describe('ProjectSamplingFrame', () => {
  const url = `/api/v1/projects/${projectId}/sampling-frame`;
  const frame = {
    dimensions: [{ name: 'Municipality', field: 'municipality' }],
    targets: [{ values: ['Cali'], target: 120 }],
    updatedAt: '2026-10-02T00:00:00Z',
  };

  it('shows viewers a read-only table', async () => {
    const { http, harness, root } = await openProjectSection('sampling-frame', 'viewer');
    http.expectOne(url).flush(frame);
    await settle(harness);

    const table = byTestId(root, 'frame-table')!;
    expect(table.textContent).toContain('Municipality');
    expect(table.textContent).toContain('120');
    expect(root.querySelector('input')).toBeNull();
  });

  it('lets analysts edit dimensions and targets and saves the expected body', async () => {
    const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
    http.expectOne(url).flush(null);
    await settle(harness);

    byTestId<HTMLButtonElement>(root, 'add-dimension')!.click();
    await settle(harness);
    const dimension = byTestId(root, 'dimension-0')!;
    typeInto(dimension.querySelector('input')!, 'Sex');
    typeInto(dimension.querySelector('select')!, 'sex');
    byTestId<HTMLButtonElement>(root, 'add-row')!.click();
    await settle(harness);
    const cells = byTestId(root, 'targets-grid')!.querySelectorAll<HTMLInputElement>('tbody input');
    typeInto(cells[0], 'F');
    typeInto(cells[1], '75');
    byTestId<HTMLButtonElement>(root, 'save-frame')!.click();
    await settle(harness);

    const request = http.expectOne(url);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ dimensions: [{ name: 'Sex', field: 'sex' }], targets: [{ values: ['F'], target: 75 }] });
    request.flush({ code: undefined, errors: { 'targets[0].target': ['The target must be a whole number from 0 to 1,000,000.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(harness);
    expect(byTestId(root, 'targets-grid')!.textContent).toContain('Row 1: The target must be a whole number');
  });

  it('imports a file, maps its dimensions to form fields and saves after review', async () => {
    const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
    http.expectOne(url).flush(null);
    await settle(harness);

    const input = byTestId<HTMLInputElement>(root, 'import-file')!;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'frame.csv')], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle(harness);
    http.expectOne(`${url}/import`).flush({
      dimensions: ['municipality', 'Gender'],
      rows: [{ row: 2, values: ['Cali', 'F'], target: 120 }],
      errors: [],
    });
    await settle(harness);

    const preview = byTestId(root, 'import-preview')!;
    expect(preview.textContent).toContain('1 rows and 2 dimensions found');
    expect(byTestId<HTMLSelectElement>(root, 'map-0')!.value).toBe('municipality');
    typeInto(byTestId<HTMLSelectElement>(root, 'map-1')!, 'sex');
    byTestId<HTMLButtonElement>(root, 'apply-import')!.click();
    await settle(harness);
    http.expectNone(url);
    byTestId<HTMLButtonElement>(root, 'save-frame')!.click();
    await settle(harness);

    expect(http.expectOne(url).request.body).toEqual({
      dimensions: [
        { name: 'municipality', field: 'municipality' },
        { name: 'Gender', field: 'sex' },
      ],
      targets: [{ values: ['Cali', 'F'], target: 120 }],
    });
  });

  it('shows row errors from an import without loading it', async () => {
    const { http, harness, root } = await openProjectSection('sampling-frame', 'admin');
    http.expectOne(url).flush(null);
    await settle(harness);

    const input = byTestId<HTMLInputElement>(root, 'import-file')!;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'frame.csv')], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle(harness);
    http.expectOne(`${url}/import`).flush({
      dimensions: ['municipality'],
      rows: [{ row: 2, values: ['Cali'], target: null }],
      errors: [{ row: 7, message: 'The target must be a whole number from 0 to 1,000,000.' }],
    });
    await settle(harness);

    expect(byTestId(root, 'import-errors')?.textContent).toContain('Row 7: The target must be a whole number');
    expect(byTestId(root, 'apply-import')).toBeNull();
  });
});
