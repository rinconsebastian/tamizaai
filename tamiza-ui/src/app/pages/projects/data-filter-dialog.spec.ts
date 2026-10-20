import { Dialog } from '@angular/cdk/dialog';
import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { DataColumn, DataFilter } from '../../core/projects/data.service';
import { provideVisibleElements, tick } from '../../../testing/cdk';
import { translocoTesting } from '../../../testing/transloco-testing';
import { DataFilterDialog, DataFilterDialogData, DataFilterDialogResult } from './data-filter-dialog';

@Component({ template: '' })
class Host {
  readonly container = inject(ViewContainerRef);
}

// The options a dashboard filter opens the dialog with (dashboard filters, design D7); clean data and the widget form open it without them.
describe('DataFilterDialog', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(column: DataColumn, options: Partial<DataFilterDialogData> = {}, filter: DataFilter | null = null) {
    TestBed.configureTestingModule({ imports: [translocoTesting('en')], providers: [provideVisibleElements()] });
    const fixture = TestBed.createComponent(Host);
    const loadValues = vi.fn(() => of([{ value: 'Cali', count: null }]));
    const ref = TestBed.inject(Dialog).open<DataFilterDialogResult, DataFilterDialogData>(DataFilterDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { column, filter, timeZone: 'America/Bogota', loadValues, check: () => of(null), ...options },
    });
    const results: (DataFilterDialogResult | undefined)[] = [];
    ref.closed.subscribe((result) => results.push(result));
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const root = () => document.querySelector('[data-testid="filter-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const type = async (id: string, value: string) => {
      const input = get<HTMLInputElement>(id)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await settle();
    };
    const apply = async () => {
      get('filter-apply')!.click();
      await settle();
    };
    return { root, get, type, apply, settle, results, loadValues };
  }

  const municipality: DataColumn = { name: 'municipality', type: 'text', label: null };

  it('shows a given title as text in place of the column name', async () => {
    const dialog = await open(municipality, { title: '<b>Zona</b>' });

    const heading = dialog.root()!.querySelector('h2')!;
    expect(heading.textContent!.trim()).toBe('<b>Zona</b>');
    expect(heading.querySelector('b')).toBeNull();
  });

  it('keeps the column name as the title and every condition of its type without options', async () => {
    const dialog = await open(municipality);

    expect(dialog.root()!.querySelector('h2')!.textContent).toContain('municipality');
    expect(Array.from(dialog.get<HTMLSelectElement>('filter-op')!.options).map((o) => o.value)).toEqual(['contains', 'equals', 'in', 'empty', 'notEmpty']);
  });

  it('offers only "is one of", with no choice of condition, when that is the one condition given', async () => {
    const dialog = await open(municipality, { operations: ['in'] });

    expect(dialog.get('filter-op')).toBeNull();
    expect(dialog.get('filter-values')).not.toBeNull();
    expect(dialog.loadValues).toHaveBeenCalledWith('');
    dialog.get<HTMLInputElement>('filter-option')!.click();
    await dialog.apply();

    expect(dialog.results).toEqual([{ filter: { column: 'municipality', op: 'in', values: ['Cali'] } }]);
  });

  it('takes a decimal range on a decimal column when only a range is offered', async () => {
    const dialog = await open({ name: 'round', type: 'decimal', label: null }, { operations: ['range'] });

    expect(dialog.get('filter-op')).toBeNull();
    expect(dialog.get<HTMLInputElement>('filter-min')!.step).toBe('any');
    await dialog.type('filter-min', '2.5');
    await dialog.apply();

    expect(dialog.results).toEqual([{ filter: { column: 'round', op: 'range', min: '2.5' } }]);
  });

  it('takes days on a date column', async () => {
    const dialog = await open({ name: 'visited_at', type: 'date', label: null }, { operations: ['range'] });

    expect(dialog.get<HTMLInputElement>('filter-min')!.type).toBe('date');
    await dialog.type('filter-min', '2026-09-01');
    await dialog.type('filter-max', '2026-09-01');
    await dialog.apply();

    expect(dialog.results).toEqual([{ filter: { column: 'visited_at', op: 'range', min: '2026-09-01', max: '2026-09-01' } }]);
  });

  it('labels the button that removes the filter with the text given', async () => {
    const dialog = await open(municipality, { operations: ['in'], removeLabel: 'Clear' }, { column: 'municipality', op: 'in', values: ['Cali'] });

    expect(dialog.get('filter-remove')!.textContent!.trim()).toBe('Clear');
    dialog.get('filter-remove')!.click();
    await dialog.settle();
    expect(dialog.results).toEqual([{ remove: true }]);
  });
});
