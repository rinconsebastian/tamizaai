import { Dialog } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, ViewContainerRef, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { Downloader } from '../../core/download.service';
import { FormChoice, FormField } from '../../core/projects/projects.service';
import { ToastService } from '../../shared/ui';
import { provideVisibleElements, tick } from '../../../testing/cdk';
import { translocoTesting } from '../../../testing/transloco-testing';
import { FrameQuestionsData, FrameQuestionsDialog, FrameQuestionsResult } from './frame-questions-dialog';

@Component({ template: '' })
class Host {
  readonly container = inject(ViewContainerRef);
}

const field = (xpath: string, type: string, extra: Partial<FormField> = {}): FormField => ({
  name: xpath.split('/').at(-1)!,
  xpath,
  type,
  label: null,
  ...extra,
});
const codes = (listName: string, count: number, label = (i: number) => `Choice ${i}`): FormChoice[] =>
  Array.from({ length: count }, (_, i) => ({ listName, name: `${listName}${String(i + 1).padStart(4, '0')}`, label: label(i + 1) }));

const snapshot: FormField[] = [
  field('start', 'start'),
  field('municipio', 'select_one', { label: 'Municipality', listName: 'municipios' }),
  field('sexo', 'select_one', { label: 'Sex', listName: 'sexos', orOther: true }),
  field('rango_edad', 'select_one', { label: 'Age range', listName: 'rangos' }),
  field('estrato', 'integer', { label: 'Stratum' }),
  field('idiomas', 'select_multiple', { label: 'Languages', listName: 'idiomas' }),
  field('miembros/edad', 'integer', { label: 'Member age', inRepeat: true }),
  field('Target', 'select_one', { label: 'Goal', listName: 'sexos' }),
];

const choices: FormChoice[] = [
  ...codes('municipios', 5),
  { listName: 'sexos', name: 'f', label: 'Female' },
  { listName: 'sexos', name: 'm', label: 'Male' },
  ...codes('rangos', 3),
  { listName: 'idiomas', name: 'es', label: 'Spanish' },
];

describe('FrameQuestionsDialog', () => {
  let downloader: { start: ReturnType<typeof vi.fn> };

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(options: { snapshot?: FormField[]; choices?: FormChoice[] | null; initial?: FrameQuestionsData['initial'] } = {}) {
    downloader = { start: vi.fn() };
    TestBed.configureTestingModule({
      imports: [translocoTesting()],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements(), { provide: Downloader, useValue: downloader }],
    });
    const fixture = TestBed.createComponent(Host);
    const loaded = signal<FormChoice[] | null>(options.choices === undefined ? choices : options.choices);
    const failed = signal(false);
    const loadChoices = vi.fn();
    const ref = TestBed.inject(Dialog).open<FrameQuestionsResult, FrameQuestionsData>(FrameQuestionsDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { projectId: 'p1', snapshot: options.snapshot ?? snapshot, choices: loaded, choicesFailed: failed, loadChoices, initial: options.initial },
    });
    const results: (FrameQuestionsResult | undefined)[] = [];
    ref.closed.subscribe((result) => results.push(result));
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const root = () => document.querySelector('[data-testid="questions-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const click = async (id: string) => {
      get(id)!.click();
      await settle();
    };
    const type = async (id: string, value: string) => {
      const input = get<HTMLInputElement | HTMLTextAreaElement>(id)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await settle();
    };
    return { http: TestBed.inject(HttpTestingController), settle, root, get, click, type, results, loaded, failed, loadChoices };
  }

  it('Row count', async () => {
    const { get, click } = await open();

    await click('pick-municipio');
    await click('pick-sexo');
    await click('pick-rango_edad');

    expect(get('row-count')!.textContent!.trim()).toBe('5 × 2 × 3 = 30 rows');
    expect(get('row-count')!.getAttribute('aria-live')).toBe('polite');
    expect(get<HTMLButtonElement>('download-template')!.disabled).toBe(false);
  });

  it('Too many rows', async () => {
    const big = [...codes('municipios', 1_100), { listName: 'sexos', name: 'f', label: 'F' }, { listName: 'sexos', name: 'm', label: 'M' }, ...codes('rangos', 5)];
    const { get, click } = await open({ choices: big });

    await click('pick-municipio');
    await click('pick-sexo');
    await click('pick-rango_edad');

    expect(get('row-count')!.textContent).toContain('1,100 × 2 × 5 = 11,000 rows');
    expect(get('picker-problem')!.textContent).toContain('a sampling frame holds at most 10,000');
    expect(get<HTMLButtonElement>('download-template')!.disabled).toBe(true);
    expect(get<HTMLButtonElement>('create-rows')!.disabled).toBe(true);
  });

  it('Select all with a search', async () => {
    const big = codes('municipios', 1_100, (i) => (i === 88 ? 'Bello' : `Town ${i}`));
    const { get, click, type } = await open({ choices: big });
    await click('pick-municipio');
    await click('edit-choices-municipio');
    await click('clear-all');
    expect(get('chosen-count')!.textContent).toContain('0 of 1,100 chosen');

    await type('choice-search', 'Bello');
    expect(get('select-all')!.textContent).toContain('Select the 1 shown');
    await click('select-all');
    await type('choice-search', '');

    expect(get('chosen-count')!.textContent).toContain('1 of 1,100 chosen');
    expect(get<HTMLInputElement>('choice-municipios0088')!.checked).toBe(true);
    expect(get<HTMLInputElement>('choice-municipios0001')!.checked).toBe(false);
  });

  it('starts every choice ticked and the added other unticked', async () => {
    const { get, click } = await open();
    await click('pick-sexo');
    await click('edit-choices-sexo');

    expect(get<HTMLInputElement>('choice-f')!.checked).toBe(true);
    expect(get<HTMLInputElement>('choice-m')!.checked).toBe(true);
    expect(get<HTMLInputElement>('choice-other')!.checked).toBe(false);
    expect(get('choice-list')!.textContent).toContain('Female — f');
    expect(get('choice-list')!.textContent).toContain('Other — other');
    expect(get('chosen-count')!.textContent).toContain('2 of 3 chosen');
  });

  it('caps the questions at 10', async () => {
    const many = Array.from({ length: 11 }, (_, i) => field(`q${i + 1}`, 'text'));
    const { get, click } = await open({ snapshot: many, choices: [] });

    for (let i = 1; i <= 10; i++) {
      await click(`pick-q${i}`);
    }

    expect(get('question-count')!.textContent).toContain('10 of 10');
    expect(get<HTMLInputElement>('pick-q11')!.disabled).toBe(true);
    expect(get<HTMLInputElement>('pick-q1')!.disabled).toBe(false);
  });

  it('lists multiple-choice, repeat and target questions disabled with their reason', async () => {
    const { get } = await open();

    const disabled = get('group-disabled')!;
    expect(disabled.textContent).toContain('Coverage cannot count these');
    expect(get<HTMLInputElement>('pick-idiomas')!.disabled).toBe(true);
    expect(get('reason-idiomas')!.textContent).toContain('an answer can hold several choices');
    expect(get<HTMLInputElement>('pick-miembros/edad')!.disabled).toBe(true);
    expect(get('reason-miembros/edad')!.textContent).toContain('answered once per repeat');
    expect(get<HTMLInputElement>('pick-Target')!.disabled).toBe(true);
    expect(get('reason-Target')!.textContent).toContain('reserved for the target column');
    expect(get('group-choices')!.textContent).toContain('Municipality · municipio · 5 choices');
    expect(get('group-typed')!.textContent).toContain('Stratum');
    expect(get('question-start')).toBeNull();
  });

  it('cleans typed values and refuses one over 500 characters', async () => {
    const { get, click, type } = await open();
    await click('pick-estrato');
    await click('pick-sexo');

    await type('values-estrato', '1\n 1\n\n2\n');
    expect(get('row-count')!.textContent).toContain('2 × 2 = 4 rows');

    await type('values-estrato', `1\n${'x'.repeat(501)}`);
    expect(get('too-long-estrato')!.textContent).toContain('longer than 500 characters');
    expect(get<HTMLButtonElement>('download-template')!.disabled).toBe(true);
    expect(get<HTMLButtonElement>('create-rows')!.disabled).toBe(true);
  });

  it('downloads the template once, busy while it is prepared, and ends with one toast', async () => {
    const { http, get, click, settle, results } = await open();
    await click('pick-municipio');
    await click('pick-sexo');
    await click('edit-choices-sexo');
    await click('choice-m');
    await click('choices-back');

    await click('download-template');
    const request = http.expectOne('/api/v1/projects/p1/sampling-frame/template');
    const button = get<HTMLButtonElement>('download-template')!;
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.textContent).toContain('Preparing the template…');
    expect(request.request.body).toEqual({ dimensions: [{ field: 'municipio', values: null }, { field: 'sexo', values: ['f'] }] });

    request.flush({ downloadUrl: '/api/v1/data-exports/abc', fileName: 't.xlsx', rowCount: 5, expiresAt: '2026-10-15T12:00:00Z' });
    await settle();

    expect(downloader.start).toHaveBeenCalledTimes(1);
    expect(downloader.start).toHaveBeenCalledWith('/api/v1/data-exports/abc');
    const toasts = TestBed.inject(ToastService).toasts();
    expect(toasts.length).toBe(1);
    expect(toasts[0].message).toBe('Template downloaded with 5 rows. Fill in the target column, then import it here.');
    expect(results).toEqual([{ kind: 'downloaded' }]);
  });

  it('shows a refused request in the dialog', async () => {
    const { http, get, click, settle } = await open();
    await click('pick-municipio');
    await click('download-template');

    http
      .expectOne('/api/v1/projects/p1/sampling-frame/template')
      .flush({ errors: { dimensions: ['This template would be larger than the 5 MB import limit. Choose fewer choices or questions.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(get('template-error')!.textContent).toContain('larger than the 5 MB import limit');
    expect(downloader.start).not.toHaveBeenCalled();
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
    expect(get<HTMLButtonElement>('download-template')!.disabled).toBe(false);
  });

  it('creates the rows here with each question\'s column name', async () => {
    const { click, type, results } = await open();
    await click('pick-sexo');
    await click('pick-estrato');
    await type('values-estrato', '1\n2');

    await click('create-rows');

    expect(results).toEqual([
      {
        kind: 'rows',
        questions: [
          { field: 'sexo', header: 'sexo' },
          { field: 'estrato', header: 'estrato' },
        ],
        rows: [
          ['f', '1'],
          ['f', '2'],
          ['m', '1'],
          ['m', '2'],
        ],
      },
    ]);
  });

  it('starts with the editor\'s questions, their codes and typed values', async () => {
    const { get, click } = await open({
      initial: [
        { field: 'sexo', values: ['F', 'f', 'nope'] },
        { field: 'municipio', values: ['5001'] },
        { field: 'estrato', values: [' 3', '1', '3'] },
        { field: 'idiomas', values: ['es'] },
      ],
    });

    expect(get<HTMLInputElement>('pick-sexo')!.checked).toBe(true);
    expect(get<HTMLInputElement>('pick-idiomas')!.checked).toBe(false);
    expect(get<HTMLTextAreaElement>('values-estrato')!.value).toBe('3\n1');
    expect(get('row-count')!.textContent).toContain('5 × 1 × 2 = 10 rows');
    await click('edit-choices-sexo');
    expect(get('chosen-count')!.textContent).toContain('1 of 3 chosen');
  });

  it('shows a skeleton while the choices load, and a retry after a failure', async () => {
    const { get, click, settle, loaded, failed, loadChoices } = await open({ choices: null });
    expect(loadChoices).toHaveBeenCalledTimes(1);
    expect(get('picker-loading')!.getAttribute('aria-busy')).toBe('true');

    failed.set(true);
    await settle();
    expect(get('picker-load-failed')!.textContent).toContain("The form's questions could not be loaded.");
    await click('picker-retry');
    expect(loadChoices).toHaveBeenCalledTimes(2);

    failed.set(false);
    loaded.set(choices);
    await settle();
    expect(get('pick-municipio')).not.toBeNull();
  });
});
