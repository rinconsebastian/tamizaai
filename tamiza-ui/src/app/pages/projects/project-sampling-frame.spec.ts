import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { Downloader } from '../../core/download.service';
import { FormChoice, FormField, MetricAvailability, SamplingFrame } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
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
    byTestId<HTMLButtonElement>(context.root, saved ? 'edit-frame' : 'start-type')!.click();
    await settle(context.harness);
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

    it('Analyst without a frame', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'admin');
      http.expectOne(url).flush(null);
      await settle(harness);

      const start = byTestId(root, 'frame-start')!;
      expect(start.textContent).toContain('Plan your sample');
      expect(['start-questions', 'start-import', 'start-type'].map((id) => byTestId(start, id)?.textContent?.trim())).toEqual([
        'Choose questions',
        'Import a file',
        'Type it in',
      ]);
      const guide = byTestId(start, 'start-guide')!.querySelector('details')!;
      expect(guide.querySelector('summary')!.textContent).toContain('How to organize a file');
      expect(guide.open).toBe(false);
      expect(byTestId(root, 'add-dimension')).toBeNull();
      expect(byTestId(root, 'edit-frame')).toBeNull();
      expect(byTestId(root, 'open-import')).toBeNull();
      expect(byTestId(root, 'download-frame')).toBeNull();
    });

    it('Viewer without a frame', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'viewer');
      http.expectOne(url).flush(null);
      await settle(harness);

      expect(byTestId(root, 'frame-empty')!.textContent).toContain('No sampling frame has been defined');
      expect(byTestId(root, 'frame-start')).toBeNull();
      expect(root.querySelector('section button')).toBeNull();
    });

    it('Typing the frame in', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush(null);
      await settle(harness);

      byTestId<HTMLButtonElement>(root, 'start-type')!.click();
      await settle(harness);
      expect(byTestId(root, 'frame-add-dimension-first')).not.toBeNull();
      expect(byTestId(root, 'open-import')).not.toBeNull();
      byTestId<HTMLButtonElement>(root, 'cancel-edit')!.click();
      await settle(harness);

      expect(byTestId(root, 'add-dimension')).toBeNull();
      expect(byTestId(root, 'frame-start')).not.toBeNull();
    });

    it('Frame load fails', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush({ detail: 'The database is not available.' }, { status: 503, statusText: 'Service Unavailable' });
      await settle(harness);

      const failure = byTestId(root, 'frame-load-failed')!;
      expect(failure.textContent).toContain('The sampling frame could not be loaded.');
      expect(failure.textContent).toContain('The database is not available.');
      expect(failure.querySelector('[lang="en"]')!.textContent).toBe('The database is not available.');
      expect(failure.querySelector('p')!.hasAttribute('lang')).toBe(false); // the heading is the UI's
      for (const id of ['frame-start', 'add-dimension', 'open-import', 'download-frame', 'edit-frame', 'frame-empty']) {
        expect(byTestId(root, id)).toBeNull();
      }

      byTestId<HTMLButtonElement>(root, 'frame-retry')!.click();
      await settle(harness);
      http.expectOne(url).flush(frame);
      await settle(harness);

      expect(byTestId(root, 'frame-load-failed')).toBeNull();
      expect(byTestId(root, 'frame-table')!.textContent).toContain('Municipality');
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
      const rowMessage = byTestId(root, 'targets-grid')!.querySelector('td[colspan] [lang="en"]')!;
      expect(rowMessage.textContent).toBe('The target must be a whole number from 0 to 1,000,000.');
      expect(rowMessage.parentElement!.textContent).toBe('Row 1: The target must be a whole number from 0 to 1,000,000.');
      expect(byTestId(root, 'error-count')!.textContent).toContain('1 problem to fix before saving, in row 1.');
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
      byTestId<HTMLButtonElement>(root, 'start-import')!.click();
      await settle(harness);

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({
        dimensions: ['municipality', 'Gender'],
        rows: [{ row: 2, values: ['Cali', 'F'], target: 120 }],
        errors: [],
      });
      await settle(harness);

      expect(byTestId(overlay(), 'import-summary')!.textContent).toContain('1 row found, with 2 dimensions.');
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
      byTestId<HTMLButtonElement>(root, 'start-import')!.click();
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
      byTestId<HTMLButtonElement>(root, 'start-import')!.click();
      await settle(harness);

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({
        dimensions: ['municipality'],
        rows: [{ row: 2, values: ['Cali'], target: null }],
        errors: Array.from({ length: 25 }, (_, i) => ({ row: i + 7, message: 'The target must be a whole number from 0 to 1,000,000.' })),
      });
      await settle(harness);

      expect(byTestId(overlay(), 'import-errors')!.textContent).toContain('Row 7: The target must be a whole number');
      const firstError = byTestId(overlay(), 'import-errors')!.querySelector('li')!;
      expect(firstError.textContent).toBe('Row 7: The target must be a whole number from 0 to 1,000,000.');
      expect(firstError.querySelector('[lang="en"]')!.textContent).toBe('The target must be a whole number from 0 to 1,000,000.');
      expect(byTestId(overlay(), 'import-errors')!.querySelectorAll('li').length).toBe(20);
      expect(byTestId(overlay(), 'import-errors-more')!.textContent).toContain('and 5 more.');
      expect(byTestId(overlay(), 'apply-import')).toBeNull();
    });

    it('explains a refused file in the dialog and keeps the editor as it was', async () => {
      const { http, harness, root } = await openProjectSection('sampling-frame', 'analyst');
      http.expectOne(url).flush(null);
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'start-import')!.click();
      await settle(harness);

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({ errors: { file: ['The file needs a target column.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);
      expect(byTestId(overlay(), 'import-error')!.textContent).toContain('The file needs a target column.');
      expect(byTestId(overlay(), 'import-error')!.getAttribute('lang')).toBe('en');

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

  describe('downloading', () => {
    const exportUrl = `${url}/export`;

    async function openWithDownloader(role: 'viewer' | 'analyst') {
      const downloader = { start: vi.fn() };
      const context = await openProjectSection('sampling-frame', role, {}, [{ provide: Downloader, useValue: downloader }]);
      context.http.expectOne(url).flush(frame);
      await settle(context.harness);
      return { ...context, downloader };
    }

    it('lets a viewer download the frame, busy while it is prepared, ending in one success toast', async () => {
      const { http, harness, root, downloader } = await openWithDownloader('viewer');

      byTestId<HTMLButtonElement>(root, 'download-frame')!.click();
      await settle(harness);
      const button = byTestId<HTMLButtonElement>(root, 'download-frame')!;
      expect(button.disabled).toBe(true);
      expect(button.getAttribute('aria-busy')).toBe('true');
      const request = http.expectOne(exportUrl);
      expect(request.request.method).toBe('POST');
      request.flush({ downloadUrl: '/api/v1/data-exports/t1', fileName: 'frame.xlsx', rowCount: 1, expiresAt: '2026-10-15T12:00:00Z' });
      await settle(harness);

      expect(downloader.start).toHaveBeenCalledTimes(1);
      expect(downloader.start).toHaveBeenCalledWith('/api/v1/data-exports/t1');
      expect(TestBed.inject(ToastService).toasts().map((t) => [t.tone, t.message])).toEqual([['success', 'Sampling frame downloaded.']]);
      expect(byTestId<HTMLButtonElement>(root, 'download-frame')!.disabled).toBe(false);
    });

    it('shows one error toast with the API message when the download is refused', async () => {
      const { http, harness, root, downloader } = await openWithDownloader('viewer');

      byTestId<HTMLButtonElement>(root, 'download-frame')!.click();
      await settle(harness);
      http.expectOne(exportUrl).flush({ code: 'samplingFrame.none', detail: 'This project has no sampling frame yet.' }, { status: 409, statusText: 'Conflict' });
      await settle(harness);

      expect(downloader.start).not.toHaveBeenCalled();
      expect(TestBed.inject(ToastService).toasts().map((t) => [t.tone, t.message])).toEqual([['danger', 'This project has no sampling frame yet.']]);
    });
  });

  describe('the import dialog', () => {
    async function openImport(saved: SamplingFrame | null, role: 'analyst' | 'admin' = 'analyst') {
      const context = await openProjectSection('sampling-frame', role);
      context.http.expectOne(url).flush(saved);
      await settle(context.harness);
      byTestId<HTMLButtonElement>(context.root, saved ? 'open-import' : 'start-import')!.click();
      await settle(context.harness);
      return context;
    }

    const guide = () => byTestId(overlay(), 'import-guide')!.querySelector('details')!;

    it('First import', async () => {
      await openImport(null);

      expect(guide().open).toBe(true);
      expect(guide().textContent).toContain('Columns whose name starts with # are notes');
    });

    it('Import with a saved frame', async () => {
      await openImport(frame);

      expect(guide().open).toBe(false);
      guide().querySelector('summary')!.click();
      await tick();
      expect(guide().open).toBe(true);
    });

    it('Template from the guide', async () => {
      const { http, harness } = await openImport(null);

      byTestId<HTMLButtonElement>(overlay(), 'guide-template')!.click();
      await settle(harness);

      expect(byTestId(overlay(), 'import-dialog')).toBeNull();
      expect(byTestId(overlay(), 'questions-dialog')).not.toBeNull();
      http.expectOne(`/api/v1/projects/${projectId}/form-choices`).flush([]);
    });

    it('is busy while it reads a file, and a second file drops the first file\'s late answer', async () => {
      const { http, harness } = await openImport(null);

      await chooseFile(overlay(), harness);
      const button = byTestId(overlay(), 'import-file-button')!;
      expect(button.textContent).toContain('Reading the file…');
      expect(button.getAttribute('aria-busy')).toBe('true');
      const first = http.expectOne(`${url}/import`);
      await chooseFile(overlay(), harness);
      const second = http.expectOne(`${url}/import`);

      expect(first.cancelled).toBe(true);
      second.flush({ dimensions: ['sex'], rows: [{ row: 2, values: ['F'], target: 3 }], errors: [], rowsWithoutTarget: [], ignoredColumns: [] });
      await settle(harness);

      expect(byTestId(overlay(), 'import-summary')!.textContent).toContain('1 row found, with 1 dimension.');
      expect(byTestId(overlay(), 'import-file-button')!.textContent).toContain('Choose a file');
      expect(byTestId(overlay(), 'import-file-button')!.hasAttribute('aria-busy')).toBe(false);
    });

    it('lists left-out rows as ranges and ignored columns, announcing only the counts', async () => {
      const { http, harness } = await openImport(null);
      // 43 blocks of 50 rows and one of 20, separated by one row each: 2,170 rows in 44 ranges.
      const rowsWithoutTarget = Array.from({ length: 44 }, (_, block) => Array.from({ length: block < 43 ? 50 : 20 }, (_, i) => 2 + block * 51 + i)).flat();

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({
        dimensions: ['municipality'],
        rows: [{ row: 3000, values: ['Cali'], target: 1 }],
        errors: [],
        rowsWithoutTarget,
        ignoredColumns: [
          { column: 'B', name: '# municipio label', reason: 'note' },
          { column: 'F', name: null, reason: 'unnamed' },
        ],
      });
      await settle(harness);

      const live = byTestId(overlay(), 'import-live')!;
      expect(live.getAttribute('aria-live')).toBe('polite');
      expect(byTestId(live, 'left-out-count')!.textContent).toBe('2,170 rows without a target are left out.');
      expect(byTestId(live, 'ignored-count')!.textContent).toBe('2 columns are not read.');
      expect(live.textContent).not.toContain('Rows left out');
      expect(live.textContent).not.toContain('# municipio label');

      const rows = byTestId(overlay(), 'left-out-rows')!.textContent!;
      expect(rows).toMatch(/^Rows left out: 2–51, 53–102, /);
      expect(rows.match(/–/g)!.length).toBe(20);
      expect(rows).toContain('and 1,170 more rows');
      expect([...byTestId(overlay(), 'ignored-columns')!.querySelectorAll('li')].map((li) => li.textContent!.trim())).toEqual([
        'B · # municipio label',
        'F · values under a column without a name',
      ]);
    });

    it('names both candidates when the saved frame and the form disagree, preselecting neither', async () => {
      const saved: SamplingFrame = { ...frame, dimensions: [{ name: 'sex', field: 'enumerator' }], targets: [{ values: ['Ana'], target: 1 }] };
      const { http, harness } = await openImport(saved);

      await chooseFile(overlay(), harness);
      http.expectOne(`${url}/import`).flush({
        dimensions: ['sex', 'Municipality'],
        rows: [{ row: 2, values: ['F', 'Cali'], target: 1 }],
        errors: [],
        rowsWithoutTarget: [],
        ignoredColumns: [],
      });
      await settle(harness);

      expect(byTestId<HTMLSelectElement>(overlay(), 'map-0')!.value).toBe('');
      expect(byTestId(overlay(), 'map-conflict-0')!.textContent).toBe('Saved frame: enumerator · Form question: sex');
      expect(byTestId<HTMLSelectElement>(overlay(), 'map-1')!.value).toBe('municipality');
      expect(byTestId(overlay(), 'map-conflict-1')).toBeNull();
    });
  });

  describe('rows from questions', () => {
    const choicesUrl = `/api/v1/projects/${projectId}/form-choices`;
    const pickerFields: FormField[] = [
      { name: 'start', xpath: 'start', type: 'start', label: null },
      { name: 'municipio', xpath: 'municipio', type: 'select_one', label: 'Municipio', listName: 'municipios' },
      { name: 'sexo', xpath: 'sexo', type: 'select_one', label: 'Sexo', listName: 'sexos' },
      { name: 'estrato', xpath: 'estrato', type: 'integer', label: 'Estrato' },
    ];
    const pickerChoices: FormChoice[] = [
      { listName: 'municipios', name: '05001', label: 'Medellín' },
      { listName: 'municipios', name: '05088', label: 'Bello' },
      { listName: 'municipios', name: '05266', label: 'Envigado' },
      { listName: 'sexos', name: 'f', label: 'Femenino' },
      { listName: 'sexos', name: 'm', label: 'Masculino' },
    ];

    /** Opens the page with a form whose questions have choices, in the editor when there is a saved frame. */
    async function open(saved: SamplingFrame | null) {
      const context = await openProjectSection('sampling-frame', 'analyst');
      context.harness.routeDebugElement!.injector.get(ProjectContext).formFields.set(pickerFields);
      context.http.expectOne(url).flush(saved);
      await settle(context.harness);
      if (saved) {
        byTestId<HTMLButtonElement>(context.root, 'edit-frame')!.click();
        await settle(context.harness);
      }
      return context;
    }

    async function openPicker(context: Awaited<ReturnType<typeof open>>, from: 'start-questions' | 'create-rows-from-questions') {
      byTestId<HTMLButtonElement>(context.root, from)!.click();
      await settle(context.harness);
      context.http.match(choicesUrl).forEach((request) => request.flush(pickerChoices));
      await settle(context.harness);
    }

    async function click(context: Awaited<ReturnType<typeof open>>, id: string) {
      byTestId<HTMLElement>(overlay(), id)!.click();
      await settle(context.harness);
    }

    function grid(root: HTMLElement): string[][] {
      return [...byTestId(root, 'targets-grid')!.querySelectorAll('tbody tr')]
        .filter((tr) => tr.querySelector('input'))
        .map((tr) => [...tr.querySelectorAll<HTMLInputElement>('input')].map((input) => input.value));
    }

    const dimensionNames = (root: HTMLElement) => [...root.querySelectorAll<HTMLInputElement>('[data-testid^="dimension-"] input')].map((input) => input.value);

    function savedFrame(dimensions: { name: string; field: string }[], targets: [string[], number][]): SamplingFrame {
      return { dimensions, targets: targets.map(([values, target]) => ({ values, target })), updatedAt: '2026-10-02T15:30:00Z' };
    }

    it('Rows created in the editor', async () => {
      const context = await open(null);
      await openPicker(context, 'start-questions');
      await click(context, 'pick-municipio');
      await click(context, 'pick-sexo');
      await click(context, 'create-rows');

      expect(confirmButton()).toBeNull();
      expect(dimensionNames(context.root)).toEqual(['municipio', 'sexo']);
      expect([...context.root.querySelectorAll<HTMLSelectElement>('[data-testid^="dimension-"] select')].map((s) => s.value)).toEqual(['municipio', 'sexo']);
      expect(grid(context.root)).toEqual([
        ['05001', 'f', ''],
        ['05001', 'm', ''],
        ['05088', 'f', ''],
        ['05088', 'm', ''],
        ['05266', 'f', ''],
        ['05266', 'm', ''],
      ]);
      context.http.expectNone(url);
    });

    it('Existing targets kept', async () => {
      const context = await open(savedFrame([{ name: 'municipio', field: 'municipio' }, { name: 'sexo', field: 'sexo' }], [[['05001', 'f'], 120]]));
      await openPicker(context, 'create-rows-from-questions');
      expect(byTestId<HTMLInputElement>(overlay(), 'pick-municipio')!.checked).toBe(true);
      expect(byTestId(overlay(), 'row-count')!.textContent).toContain('1 row');
      await click(context, 'edit-choices-municipio');
      await click(context, 'choice-05088');
      await click(context, 'create-rows');

      expect(document.getElementById('tmz-confirm-message')!.textContent).not.toContain('targets will not be kept');
      expect(document.getElementById('tmz-confirm-message')!.textContent).toContain("The editor's 1 row will be replaced");
      confirmButton()!.click();
      await settle(context.harness);

      expect(grid(context.root)).toEqual([
        ['05001', 'f', '120'],
        ['05088', 'f', ''],
      ]);
    });

    it('Question added', async () => {
      const context = await open(savedFrame([{ name: 'municipio', field: 'municipio' }], [[['05001'], 120]]));
      await openPicker(context, 'create-rows-from-questions');
      await click(context, 'pick-sexo');
      await click(context, 'create-rows');

      expect(document.getElementById('tmz-confirm-message')!.textContent).toContain('The questions changed, so the current targets will not be kept.');
      confirmButton()!.click();
      await settle(context.harness);

      expect(grid(context.root)).toEqual([
        ['05001', 'f', ''],
        ['05001', 'm', ''],
      ]);
    });

    it('Dimension name kept', async () => {
      const context = await open(savedFrame([{ name: 'Municipality', field: 'municipio' }], [[['05001'], 120]]));
      await openPicker(context, 'create-rows-from-questions');
      await click(context, 'edit-choices-municipio');
      await click(context, 'choice-05266');
      await click(context, 'create-rows');
      confirmButton()!.click();
      await settle(context.harness);

      expect(dimensionNames(context.root)).toEqual(['Municipality']);
      expect(grid(context.root)).toEqual([
        ['05001', '120'],
        ['05266', ''],
      ]);
    });

    it('Typed values kept', async () => {
      const context = await open(
        savedFrame(
          [{ name: 'municipio', field: 'municipio' }, { name: 'estrato', field: 'estrato' }],
          [
            [['05001', '1'], 10],
            [['05001', '2'], 20],
            [['05001', '3'], 30],
          ],
        ),
      );
      await openPicker(context, 'create-rows-from-questions');
      expect(byTestId<HTMLTextAreaElement>(overlay(), 'values-estrato')!.value).toBe('1\n2\n3');
      await click(context, 'edit-choices-municipio');
      await click(context, 'choice-05088');
      await click(context, 'create-rows');
      confirmButton()!.click();
      await settle(context.harness);

      expect(grid(context.root)).toEqual([
        ['05001', '1', '10'],
        ['05001', '2', '20'],
        ['05001', '3', '30'],
        ['05088', '1', ''],
        ['05088', '2', ''],
        ['05088', '3', ''],
      ]);
    });

    it('Values that are not choices', async () => {
      const context = await open(
        savedFrame(
          [{ name: 'municipio', field: 'municipio' }],
          [
            [['5001'], 120],
            [['05088'], 40],
          ],
        ),
      );
      await openPicker(context, 'create-rows-from-questions');
      await click(context, 'create-rows');

      expect(document.getElementById('tmz-confirm-message')!.textContent).toContain(
        '1 row has values that are not choices of their question and will not be kept.',
      );
      expect(document.getElementById('tmz-confirm-message')!.textContent).toContain("The editor's 2 rows will be replaced");
      cancelConfirm();
      await settle(context.harness);
      expect(grid(context.root)).toEqual([
        ['5001', '120'],
        ['05088', '40'],
      ]);
    });

    it('names the dimensions that cannot be made from questions', async () => {
      const context = await open(savedFrame([{ name: 'municipio', field: 'municipio' }, { name: 'Old', field: 'gone' }], [[['05001', 'x'], 1]]));
      await openPicker(context, 'create-rows-from-questions');
      await click(context, 'create-rows');

      expect(document.getElementById('tmz-confirm-message')!.textContent).toContain('These dimensions cannot be made from questions and will not be kept: Old.');
    });

    it('Rows without a target removed', async () => {
      const context = await open(
        savedFrame(
          [{ name: 'municipio', field: 'municipio' }],
          Array.from({ length: 30 }, (_, i): [string[], number] => [[`Town ${i + 1}`], i + 1]),
        ),
      );
      const targets = () => [...byTestId(context.root, 'targets-grid')!.querySelectorAll<HTMLInputElement>('tbody input[inputmode="numeric"]')];
      for (const input of targets().filter((_, i) => i % 5 < 2)) {
        typeInto(input, '');
      }
      await settle(context.harness);

      const remove = byTestId<HTMLButtonElement>(context.root, 'remove-empty-targets')!;
      expect(remove.textContent).toContain('Remove rows without a target (12)');
      remove.click();
      await settle(context.harness);

      expect(grid(context.root).length).toBe(18);
      expect(byTestId(context.root, 'remove-empty-targets')).toBeNull();
    });

    it('asks before rows made from questions replace unsaved rows, and keeps them when the user cancels', async () => {
      const context = await open(frame);
      typeInto(byTestId(context.root, 'targets-grid')!.querySelector<HTMLInputElement>('tbody input')!, 'Palmira');
      await settle(context.harness);
      await openPicker(context, 'create-rows-from-questions');
      await click(context, 'pick-sexo');
      await click(context, 'create-rows');

      expect(confirmButton()).not.toBeNull();
      cancelConfirm();
      await settle(context.harness);

      expect(grid(context.root)).toEqual([['Palmira', '120']]);
    });
  });

  describe('the General section\'s form check', () => {
    const projectUrl = `/api/v1/projects/${projectId}`;
    const withoutFrame = { fieldCheck: coverage({ available: false, hint: 'defineSamplingFrame' }) };

    async function saveAFrame(role: 'analyst' | 'admin') {
      const context = await openProjectSection('sampling-frame', role, withoutFrame);
      context.http.expectOne(url).flush(null);
      await settle(context.harness);
      byTestId<HTMLButtonElement>(context.root, 'start-type')!.click();
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
