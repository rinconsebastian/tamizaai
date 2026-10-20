import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DashboardFilter } from '../../../core/projects/indicators.service';
import { provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { DashboardFilterBar, FilterChange } from './dashboard-filter-bar';
import { FilterValues } from './dashboard-filters';

const municipality: DashboardFilter = { column: 'municipality', label: 'Municipality', kind: 'text', default: null };
const round: DashboardFilter = { column: 'round', label: 'Round', kind: 'number', default: { min: '3', max: '3' } };
const consent: DashboardFilter = { column: 'consent', label: 'Consent', kind: 'boolean', default: null };

@Component({
  imports: [DashboardFilterBar],
  template: `<app-dashboard-filter-bar
    projectId="p1"
    dashboardId="d1"
    timeZone="America/Bogota"
    [filters]="filters()"
    [values]="values()"
    (changed)="changes.push($event)"
    (cleared)="events.push('cleared')"
    (reset)="events.push('reset')"
  />`,
})
class Host {
  readonly filters = signal<DashboardFilter[]>([]);
  readonly values = signal<FilterValues>({});
  readonly changes: FilterChange[] = [];
  readonly events: string[] = [];
}

describe('DashboardFilterBar', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function render(filters: DashboardFilter[], values: FilterValues = {}) {
    TestBed.configureTestingModule({
      imports: [translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements()],
    });
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.filters.set(filters);
    fixture.componentInstance.values.set(values);
    const http = TestBed.inject(HttpTestingController);
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const root = fixture.nativeElement as HTMLElement;
    const pills = () => Array.from(root.querySelectorAll<HTMLButtonElement>('[data-testid="dashboard-filter"]'));
    const pill = (column: string) => root.querySelector<HTMLButtonElement>(`[data-testid="dashboard-filter"][data-column="${column}"]`)!;
    const dialog = () => document.querySelector('[data-testid="filter-dialog"]') as HTMLElement | null;
    const inDialog = <T extends HTMLElement = HTMLElement>(id: string) => dialog()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    return { fixture, host: fixture.componentInstance, http, settle, root, pills, pill, dialog, inDialog };
  }

  const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  it('shows each filter with its label and "All" to a member when none has a value (Viewer sees the filter bar)', async () => {
    const bar = await render([municipality, { ...round, default: null }]);

    expect(bar.pills().map(text)).toEqual(['Municipality: All', 'Round: All']);
    expect(bar.root.querySelector('[data-testid="dashboard-filter-bar"]')!.getAttribute('aria-labelledby')).toBe('dashboard-filters-title');
  });

  it('renders nothing for a dashboard with no filters (No filters)', async () => {
    const bar = await render([]);

    expect(bar.root.querySelector('[data-testid="dashboard-filter-bar"]')).toBeNull();
  });

  it('describes the values chosen and the ranges set', async () => {
    const bar = await render([municipality, round, consent, { column: 'visit', label: 'Visit', kind: 'date', default: null }], {
      municipality: { values: ['Cali', 'Palmira'] },
      round: { min: '2', max: '5' },
      consent: { values: ['true'] },
      visit: { min: null, max: '2026-09-30' },
    });

    expect(bar.pills().map(text)).toEqual(['Municipality: Cali, Palmira', 'Round: 2 to 5', 'Consent: true', 'Visit: up to 2026-09-30']);
  });

  it('cuts a long label short and keeps its whole text in the tooltip and the accessible name', async () => {
    const label = 'Municipality of the household interviewed';
    const bar = await render([{ ...municipality, label: label.slice(0, 40) }], { municipality: { values: ['Cali'] } });

    const pill = bar.pill('municipality');
    expect(pill.querySelector('.truncate')).not.toBeNull();
    expect(pill.title).toBe(`${label.slice(0, 40)}: Cali`);
    expect(text(pill)).toBe(`${label.slice(0, 40)}: Cali`);
  });

  it('wraps 8 filters with 40-character labels onto as many rows as they need, each cut within the width (Filter bar at phone width)', async () => {
    // jsdom lays nothing out, so this checks what keeps every pill inside a 360-pixel viewport; the stack check measures it.
    const eight = Array.from({ length: 8 }, (_, i): DashboardFilter => ({ column: `c${i}`, label: `${i}`.padEnd(40, 'x'), kind: 'text', default: null }));
    const bar = await render(eight, Object.fromEntries(eight.map((f) => [f.column, { values: ['A value long enough to need cutting short'] }])));

    const section = bar.root.querySelector<HTMLElement>('[data-testid="dashboard-filter-bar"]')!;
    expect(section.classList).toContain('flex-wrap');
    expect(section.querySelector('ul')!.classList).toContain('flex-wrap');
    expect(bar.pills()).toHaveLength(8);
    for (const pill of bar.pills()) {
      expect(pill.closest('li')!.classList).toContain('max-w-full');
      expect(pill.closest('li')!.classList).toContain('min-w-0');
      expect(pill.classList).toContain('max-w-full');
      expect(pill.querySelector('span')!.classList).toContain('truncate');
    }
    expect(bar.root.querySelector('[data-testid="dashboard-filters-clear"]')).not.toBeNull();
  });

  it('shows a label with markup as text (Label shown as typed)', async () => {
    const bar = await render([{ ...municipality, label: '<b>Zona</b>' }]);

    expect(text(bar.pill('municipality'))).toBe('<b>Zona</b>: All');
    expect(bar.pill('municipality').querySelector('b')).toBeNull();
  });

  it('offers to clear the filters only while one has a value, and the defaults only while the values differ from them', async () => {
    const bar = await render([municipality, round], { municipality: null, round: { min: '3', max: '3' } });
    const clear = () => bar.root.querySelector<HTMLButtonElement>('[data-testid="dashboard-filters-clear"]');
    const reset = () => bar.root.querySelector<HTMLButtonElement>('[data-testid="dashboard-filters-reset"]');

    // At the defaults: Round has a value, so it can be cleared; there is nothing to go back to.
    expect(clear()).not.toBeNull();
    expect(reset()).toBeNull();

    bar.host.values.set({ municipality: null, round: null });
    await bar.settle();
    expect(clear()).toBeNull();
    expect(reset()).not.toBeNull();

    reset()!.click();
    bar.host.values.set({ municipality: { values: ['Cali'] }, round: { min: '3', max: '3' } });
    await bar.settle();
    clear()!.click();
    expect(bar.host.events).toEqual(['reset', 'cleared']);
  });

  it('offers no defaults on a dashboard whose filters have none', async () => {
    const bar = await render([municipality], { municipality: { values: ['Cali'] } });

    expect(bar.root.querySelector('[data-testid="dashboard-filters-reset"]')).toBeNull();
  });

  it('opens the filter dialog titled with the label, with its one condition and the values of the dashboard, without counts', async () => {
    const bar = await render([municipality]);

    bar.pill('municipality').click();
    await bar.settle();

    expect(text(bar.dialog()!.querySelector('h2'))).toBe('Municipality');
    expect(bar.inDialog('filter-op')).toBeNull();
    const request = bar.http.expectOne((r) => r.url === '/api/v1/projects/p1/dashboards/d1/values');
    expect(request.request.params.get('column')).toBe('municipality');
    request.flush({ values: [{ value: 'Cali', count: 12 }, { value: 'Palmira', count: 3 }] });
    await bar.settle();
    const options = Array.from(bar.dialog()!.querySelectorAll('[data-testid="filter-option"]'));
    expect(options.map((o) => text(o.closest('label')))).toEqual(['Cali', 'Palmira']);

    bar.dialog()!.querySelectorAll<HTMLInputElement>('[data-testid="filter-option"]')[1].click();
    bar.inDialog('filter-apply')!.click();
    await bar.settle();
    expect(bar.host.changes).toEqual([{ column: 'municipality', value: { values: ['Palmira'] } }]);
  });

  it('offers true and false for a true/false filter without asking the server', async () => {
    const bar = await render([consent]);

    bar.pill('consent').click();
    await bar.settle();

    bar.http.expectNone(() => true);
    const options = Array.from(bar.dialog()!.querySelectorAll('[data-testid="filter-option"]'));
    expect(options.map((o) => text(o.closest('label')))).toEqual(['true', 'false']);
  });

  it('takes a range for a number filter, and clears a filter with the dialog\'s Clear button', async () => {
    const bar = await render([round], { round: { min: '3', max: '3' } });

    bar.pill('round').click();
    await bar.settle();
    expect(bar.inDialog<HTMLInputElement>('filter-min')!.value).toBe('3');
    expect(text(bar.inDialog('filter-remove'))).toBe('Clear');
    bar.inDialog('filter-remove')!.click();
    await bar.settle();

    expect(bar.host.changes).toEqual([{ column: 'round', value: null }]);
  });
});
