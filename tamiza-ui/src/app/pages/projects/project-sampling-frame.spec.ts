import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { FormField, MetricAvailability, SamplingFrame } from '../../core/projects/projects.service';
import { tick } from '../../../testing/cdk';
import { byTestId, details, formFields, openProjectSection, projectId, settle, typeInto } from '../../../testing/project-harness';
import { ProjectContext } from './project-context';

const coverage = (check: Partial<MetricAvailability>): MetricAvailability[] => [
  { metric: 'dailyEvolution', available: true, missingFields: [], hint: null },
  { metric: 'completionTime', available: true, missingFields: [], hint: null },
  { metric: 'perEnumerator', available: true, missingFields: [], hint: null },
  { metric: 'coverage', available: true, missingFields: [], hint: null, ...check },
];

function overlay(): HTMLElement {
  return document.querySelector('.cdk-overlay-container') as HTMLElement;
}

function confirmButton(): HTMLButtonElement | null {
  return document.querySelector('[data-testid="confirm"]');
}

/** Lets a navigation reach its guard. Not settle(): the pending navigation keeps the app unstable until the dialog is answered. */
async function pending(harness: Parameters<typeof settle>[0]): Promise<void> {
  for (let i = 0; i < 3; i++) {
    harness.fixture.detectChanges();
    await tick();
  }
}

function cancelConfirm(): void {
  (document.querySelector('.cdk-overlay-container button[tmzButton]:not([data-testid="confirm"])') as HTMLButtonElement).click();
}

describe('ProjectSamplingFrame', () => {
  const url = `/api/v1/projects/${projectId}/sampling-frame`;
  const frame: SamplingFrame = {
    dimensions: [{ name: 'Municipality', field: 'municipality' }],
    targets: [{ values: ['Cali'], target: 120 }],
    updatedAt: '2026-10-02T15:30:00Z',
  };
  const bigFrame: SamplingFrame = {
    ...frame,
    targets: Array.from({ length: 120 }, (_, i) => ({ values: [`Town ${i + 1}`], target: i + 1 })),
  };

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function openEditor(role: 'analyst' | 'admin', saved: SamplingFrame | null = frame) {
    const context = await openProjectSection('sampling-frame', role);
    context.http.expectOne(url).flush(saved);
    await settle(context.harness);
    if (saved) {
      byTestId<HTMLButtonElement>(context.root, 'edit-frame')!.click();
      await settle(context.harness);
    }
    return context;
  }

  async function chooseFile(root: ParentNode, harness: Parameters<typeof settle>[0]) {
    const input = byTestId<HTMLInputElement>(root, 'import-file')!;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'frame.csv')], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle(harness);
  }

  describe('reading', () => {
    it('shows a skeleton while the frame loads', async () => {
      const { http, root } = await openProjectSection('sampling-frame', 'viewer');

      expect(byTestId(root, 'frame-loading')?.getAttribute('aria-busy')).toBe('true');
      http.expectOne(url);
    });

    it('shows viewers the summary line and a read-only table without any action', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'viewer');
      http.expectOne(url).flush(frame);
      await settle(harness);

      expect(root.querySelector('h2')!.textContent).toContain('Sampling frame');
      expect(byTestId(root, 'frame-summary')!.textContent).toMatch(/1 dimension · 1 combination · saved Oct 2, 2026/);
      const table = byTestId(root, 'frame-table')!;
      expect(table.textContent).toContain('Municipality');
      expect(table.textContent).toContain('120');
      expect(root.querySelector('input')).toBeNull();
      expect(byTestId(root, 'edit-frame')).toBeNull();
      expect(byTestId(root, 'open-import')).toBeNull();
    });

    it('says plainly that a viewer has no frame to read', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'viewer');
      http.expectOne(url).flush(null);
      await settle(harness);

      expect(byTestId(root, 'frame-empty')!.textContent).toContain('No sampling frame has been defined');
      expect(byTestId(root, 'save-frame')).toBeNull();
    });

    it('pages a long frame for viewers', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'viewer');
      http.expectOne(url).flush(bigFrame);
      await settle(harness);

      expect(byTestId(root, 'frame-table')!.querySelectorAll('tbody tr').length).toBe(50);
      expect(byTestId(root, 'frame-page-info')!.textContent).toContain('Rows 1–50 of 120');
      byTestId<HTMLButtonElement>(root, 'frame-next')!.click();
      await settle(harness);
      expect(byTestId(root, 'frame-table')!.textContent).toContain('Town 51');
      expect(byTestId(root, 'frame-page-info')!.textContent).toContain('Rows 51–100 of 120');
    });

    it('offers analysts Edit and Import over the table, with no inputs until they edit', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush(frame);
      await settle(harness);

      expect(byTestId(root, 'frame-table')).not.toBeNull();
      expect(byTestId(root, 'edit-frame')).not.toBeNull();
      expect(byTestId(root, 'open-import')).not.toBeNull();
      expect(root.querySelector('input')).toBeNull();
      expect(byTestId(root, 'save-frame')).toBeNull();
    });

    it('opens the editor straight away when there is no frame', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'admin');
      http.expectOne(url).flush(null);
      await settle(harness);

      expect(byTestId(root, 'add-dimension')).not.toBeNull();
      expect(byTestId(root, 'edit-frame')).toBeNull();
      expect(byTestId(root, 'open-import')).not.toBeNull();
      expect(byTestId(root, 'frame-add-dimension-first')).not.toBeNull();
      expect(byTestId(root, 'targets-grid')).toBeNull();
    });
  });

  describe('editing', () => {
    it('lets analysts edit dimensions and targets, saves the expected body and returns to the read view', async () => {
      const { http, harness, root } = await openEditor('analyst', null);

      byTestId<HTMLButtonElement>(root, 'add-dimension')!.click();
      await settle(harness);
      const dimension = byTestId(root, 'dimension-0')!;
      expect(dimension.textContent).toContain('Name');
      expect(dimension.textContent).toContain('Form field');
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
      request.flush({ ...frame, dimensions: [{ name: 'Sex', field: 'sex' }], targets: [{ values: ['F'], target: 75 }] });
      await settle(harness);
      http.expectOne(`/api/v1/projects/${projectId}`);

      expect(byTestId(root, 'save-frame')).toBeNull();
      expect(byTestId(root, 'frame-table')!.textContent).toContain('75');
    });

    it('names a refused row under the grid and keeps the editor open', async () => {
      const { http, harness, root } = await openEditor('analyst', null);
      byTestId<HTMLButtonElement>(root, 'add-dimension')!.click();
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'add-row')!.click();
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'save-frame')!.click();
      await settle(harness);

      http.expectOne(url).flush({ code: undefined, errors: { 'targets[0].target': ['The target must be a whole number from 0 to 1,000,000.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);

      expect(byTestId(root, 'targets-grid')!.textContent).toContain('Row 1: The target must be a whole number');
      expect(byTestId(root, 'error-count')!.textContent).toContain('1 problems to fix before saving, the first in row 1.');
      expect(byTestId(root, 'save-frame')).not.toBeNull();
    });

    it('jumps to the page of the first refused row and names that row', async () => {
      const { http, harness, root } = await openEditor('analyst', bigFrame);
      byTestId<HTMLButtonElement>(root, 'save-frame')!.click();
      await settle(harness);

      http.expectOne(url).flush(
        { errors: { 'targets[99].target': ['Invalid.'], 'targets[75]': ['Repeats row 12.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle(harness);

      expect(byTestId(root, 'frame-page-info')!.textContent).toContain('Rows 51–100 of 120');
      expect(byTestId(root, 'targets-grid')!.textContent).toContain('Row 76: Repeats row 12.');
      expect(byTestId(root, 'error-count')!.textContent).toContain('the first in row 76');
    });

    it('keeps the page it was on after a successful save', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush(bigFrame);
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'frame-next')!.click();
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'edit-frame')!.click();
      await settle(harness);
      expect(byTestId(root, 'frame-page-info')!.textContent).toContain('Rows 51–100 of 120');

      byTestId<HTMLButtonElement>(root, 'save-frame')!.click();
      await settle(harness);
      http.expectOne(url).flush(bigFrame);
      await settle(harness);

      expect(byTestId(root, 'frame-page-info')!.textContent).toContain('Rows 51–100 of 120');
    });

    it('restores the saved frame on Cancel', async () => {
      const { harness, root } = await openEditor('analyst');
      typeInto(byTestId(root, 'dimension-0')!.querySelector('input')!, 'Other');

      byTestId<HTMLButtonElement>(root, 'cancel-edit')!.click();
      await settle(harness);
      expect(byTestId(root, 'frame-table')!.textContent).toContain('Municipality');
      expect(byTestId(root, 'frame-table')!.textContent).not.toContain('Other');

      byTestId<HTMLButtonElement>(root, 'edit-frame')!.click();
      await settle(harness);
      expect(byTestId(root, 'dimension-0')!.querySelector('input')!.value).toBe('Municipality');
    });

    it('suffixes repeat-group and multiple-choice questions, keeps them selectable and warns while one is chosen', async () => {
      const { harness, root } = await openEditor('analyst');
      const extra: FormField[] = [
        { name: 'members', xpath: 'household/members', type: 'text', label: 'Members', inRepeat: true },
        { name: 'services', xpath: 'services', type: 'select_multiple', label: 'Services' },
        { name: 'hobbies', xpath: 'hobbies', type: 'select_multiple_hobbies', label: 'Hobbies' },
      ];
      harness.routeDebugElement!.injector.get(ProjectContext).formFields.set([...formFields, ...extra]);
      await settle(harness);

      const select = byTestId(root, 'dimension-0')!.querySelector('select')!;
      const options = [...select.options];
      expect(options.find((o) => o.value === 'household/members')!.textContent).toContain('— in a repeat group');
      expect(options.find((o) => o.value === 'services')!.textContent).toContain('— multiple choice');
      expect(options.find((o) => o.value === 'hobbies')!.textContent).toContain('— multiple choice');
      expect(options.find((o) => o.value === 'sex')!.textContent).not.toContain('—');
      expect(options.every((o) => !o.disabled)).toBe(true);
      expect(byTestId(root, 'dimension-warning-0')).toBeNull();

      typeInto(select, 'services');
      await settle(harness);
      expect(byTestId(root, 'dimension-warning-0')!.textContent).toContain('an answer can hold several choices');
      typeInto(select, 'household/members');
      await settle(harness);
      expect(byTestId(root, 'dimension-warning-0')!.textContent).toContain('answered once per repeat');
    });
  });

  describe('leaving the page', () => {
    it('asks before leaving with unsaved edits and stays when the user cancels', async () => {
      const { harness, root } = await openEditor('analyst');
      typeInto(byTestId(root, 'dimension-0')!.querySelector('input')!, 'Other');

      const leaving = harness.navigateByUrl(`/projects/${projectId}/members`);
      await pending(harness);
      expect(confirmButton()).not.toBeNull();
      cancelConfirm();
      await tick();
      await leaving;

      expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/sampling-frame`);
    });

    it('leaves once the user discards the edits', async () => {
      const { http, harness, root } = await openEditor('analyst');
      typeInto(byTestId(root, 'dimension-0')!.querySelector('input')!, 'Other');

      const leaving = harness.navigateByUrl(`/projects/${projectId}/members`);
      await pending(harness);
      confirmButton()!.click();
      await tick();
      await leaving;

      expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/members`);
      http.match(`/api/v1/projects/${projectId}/members`);
    });

    it('leaves without asking when nothing changed', async () => {
      const { harness } = await openEditor('analyst');

      await harness.navigateByUrl(`/projects/${projectId}/members`);
      await settle(harness);

      expect(confirmButton()).toBeNull();
      expect(TestBed.inject(Router).url).toBe(`/projects/${projectId}/members`);
    });
  });

  describe('importing', () => {
    it('imports a file in a dialog, maps its dimensions and loads them into the editor without saving', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush(null);
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'open-import')!.click();
      await settle(harness);

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({
        dimensions: ['municipality', 'Gender'],
        rows: [{ row: 2, values: ['Cali', 'F'], target: 120 }],
        errors: [],
      });
      await settle(harness);

      const preview = byTestId(overlay(), 'import-preview')!;
      expect(preview.textContent).toContain('1 rows and 2 dimensions found');
      expect(byTestId<HTMLSelectElement>(overlay(), 'map-0')!.value).toBe('municipality');
      typeInto(byTestId<HTMLSelectElement>(overlay(), 'map-1')!, 'sex');
      byTestId<HTMLButtonElement>(overlay(), 'apply-import')!.click();
      await settle(harness);
      http.expectNone(url);
      expect(byTestId(overlay() , 'import-preview')).toBeNull();

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

    it('preselects the form field of each dimension by the dimension\'s own position', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush(null);
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'open-import')!.click();
      await settle(harness);
      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({ dimensions: ['sex', 'enumerator', 'Municipality'], rows: [{ row: 2, values: ['F', 'Ana', 'Cali'], target: 5 }], errors: [] });
      await settle(harness);

      expect(['map-0', 'map-1', 'map-2'].map((id) => byTestId<HTMLSelectElement>(overlay(), id)!.value)).toEqual(['sex', 'enumerator', 'municipality']);
    });

    it('opens the editor with the imported rows when the saved frame was being read', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush(frame);
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'open-import')!.click();
      await settle(harness);
      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({ dimensions: ['sex'], rows: [{ row: 2, values: ['F'], target: 5 }], errors: [] });
      await settle(harness);
      byTestId<HTMLButtonElement>(overlay(), 'apply-import')!.click();
      await settle(harness);

      expect(byTestId(root, 'targets-grid')!.textContent).toContain('sex');
      expect(byTestId(root, 'dimension-0')!.querySelector('input')!.value).toBe('sex');
      http.expectNone(url);
    });

    it('shows row errors without offering to load the file, counting those it does not list', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'admin');
      http.expectOne(url).flush(null);
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'open-import')!.click();
      await settle(harness);

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({
        dimensions: ['municipality'],
        rows: [{ row: 2, values: ['Cali'], target: null }],
        errors: Array.from({ length: 25 }, (_, i) => ({ row: i + 7, message: 'The target must be a whole number from 0 to 1,000,000.' })),
      });
      await settle(harness);

      expect(byTestId(overlay(), 'import-errors')!.textContent).toContain('Row 7: The target must be a whole number');
      expect(byTestId(overlay(), 'import-errors')!.querySelectorAll('li').length).toBe(20);
      expect(byTestId(overlay(), 'import-errors-more')!.textContent).toContain('and 5 more.');
      expect(byTestId(overlay(), 'apply-import')).toBeNull();
    });

    it('explains a refused file in the dialog and keeps the editor as it was', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush(null);
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'open-import')!.click();
      await settle(harness);

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({ errors: { file: ['The file needs a target column.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);
      expect(byTestId(overlay(), 'import-error')!.textContent).toContain('The file needs a target column.');

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({ errors: { file: ['The file is larger than 5 MB or has more than 10,000 rows.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);
      expect(byTestId(overlay(), 'import-error')!.textContent).toContain('larger than 5 MB');
      expect(byTestId(overlay(), 'apply-import')).toBeNull();
      http.expectNone(url);
    });

    it('asks before an import replaces edits that were not saved', async () => {
      const { harness, root } = await openEditor('analyst');
      typeInto(byTestId(root, 'dimension-0')!.querySelector('input')!, 'Other');

      byTestId<HTMLButtonElement>(root, 'open-import')!.click();
      await settle(harness);
      expect(confirmButton()).not.toBeNull();
      expect(byTestId(overlay(), 'import-file')).toBeNull();
      cancelConfirm();
      await settle(harness);

      expect(byTestId(overlay(), 'import-file')).toBeNull();
      expect(byTestId(root, 'dimension-0')!.querySelector('input')!.value).toBe('Other');
    });
  });

  describe('the General section\'s form check', () => {
    const projectUrl = `/api/v1/projects/${projectId}`;
    const withoutFrame = { fieldCheck: coverage({ available: false, hint: 'defineSamplingFrame' }) };

    async function saveAFrame(role: 'analyst' | 'admin') {
      const context = await openProjectSection('sampling-frame', role, withoutFrame);
      context.http.expectOne(url).flush(null);
      await settle(context.harness);
      byTestId<HTMLButtonElement>(context.root, 'add-dimension')!.click();
      await settle(context.harness);
      const dimension = byTestId(context.root, 'dimension-0')!;
      typeInto(dimension.querySelector('input')!, 'Sex');
      typeInto(dimension.querySelector('select')!, 'sex');
      byTestId<HTMLButtonElement>(context.root, 'add-row')!.click();
      await settle(context.harness);
      const cells = byTestId(context.root, 'targets-grid')!.querySelectorAll<HTMLInputElement>('tbody input');
      typeInto(cells[0], 'f');
      typeInto(cells[1], '75');
      byTestId<HTMLButtonElement>(context.root, 'save-frame')!.click();
      await settle(context.harness);
      return context;
    }

    it('turns coverage available without a reload after a frame is saved', async () => {
      const { http, harness, root } = await saveAFrame('analyst');
      http.expectOne(url).flush({ ...frame, dimensions: [{ name: 'Sex', field: 'sex' }], targets: [{ values: ['f'], target: 75 }] });
      await settle(harness);

      // The details are read again, and only once.
      const refetch = http.expectOne(projectUrl);
      expect(refetch.request.method).toBe('GET');
      refetch.flush(details('analyst', { fieldCheck: coverage({}) }));
      await settle(harness);

      await harness.navigateByUrl(`/projects/${projectId}/overview`);
      await settle(harness);
      const metric = byTestId(root, 'metric-coverage')!;
      expect(metric.textContent).toContain('Available');
      expect(byTestId(root, 'hint-coverage')).toBeNull();
      http.verify();
    });

    it('shows the old check until the new details arrive', async () => {
      const { http, harness, root } = await saveAFrame('admin');
      http.expectOne(url).flush(frame);
      await settle(harness);
      http.expectOne(projectUrl);

      await harness.navigateByUrl(`/projects/${projectId}/overview`);
      await settle(harness);
      expect(byTestId(root, 'metric-coverage')!.textContent).toContain('Unavailable');
    });

    it('does not read the details again when saving fails', async () => {
      const { http, harness } = await saveAFrame('analyst');
      http.expectOne(url).flush({ errors: { 'targets[0].target': ['Invalid.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);

      http.expectNone(projectUrl);
    });

    it('keeps the frame saved when reading the details fails', async () => {
      const { http, harness, root } = await saveAFrame('analyst');
      http.expectOne(url).flush(frame);
      await settle(harness);
      http.expectOne(projectUrl).flush(null, { status: 500, statusText: 'Server Error' });
      await settle(harness);

      expect(byTestId(root, 'frame-table')).not.toBeNull();
    });
  });
});
