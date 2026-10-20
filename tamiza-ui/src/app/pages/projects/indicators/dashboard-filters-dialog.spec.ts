import { Dialog } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DataColumn } from '../../../core/projects/data.service';
import { DashboardFilter, IndicatorColumns, Widget } from '../../../core/projects/indicators.service';
import { pressKey, provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { DashboardFiltersData, DashboardFiltersDialog, DashboardFiltersResult, columnChoices } from './dashboard-filters-dialog';
import { config, indicator, widget } from './indicators-testing';

@Component({ template: '' })
class Host {
  readonly container = inject(ViewContainerRef);
}

const columnsUrl = (name: string) => `/api/v1/projects/p1/indicators/${name}/columns`;
const filtersUrl = '/api/v1/projects/p1/dashboards/d1/filters';
const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const col = (name: string, type: DataColumn['type']): DataColumn => ({ name, type, label: null });
const catalog = (columns: DataColumn[]): IndicatorColumns => ({ published: true, columns, numeric: [], defaults: { measure: null, groupBy: null } });

/** 5 widgets: 4 over indicators whose tables have a text `municipality`, 1 over one without it. */
const fiveWidgets: Widget[] = [
  widget('w1', { config: config({ indicator: 'coverage' }) }),
  widget('w2', { config: config({ indicator: 'coverage' }) }),
  widget('w3', { config: config({ indicator: 'attendance' }) }),
  widget('w4', { config: config({ indicator: 'attendance' }) }),
  widget('w5', { config: config({ indicator: 'target', groupBy: null }) }),
];
const tables: Record<string, DataColumn[]> = {
  coverage: [col('municipality', 'text'), col('round', 'integer'), col('value', 'decimal')],
  attendance: [col('municipality', 'text'), col('round', 'decimal'), col('value', 'decimal')],
  target: [col('zone', 'text'), col('value', 'decimal')],
};
const published = ['coverage', 'attendance', 'target'].map((name) => indicator(name));

describe('DashboardFiltersDialog', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(filters: DashboardFilter[] = [], options: Partial<DashboardFiltersData> = {}) {
    TestBed.configureTestingModule({
      imports: [translocoTesting('en')],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Host);
    const ref = TestBed.inject(Dialog).open<DashboardFiltersResult, DashboardFiltersData>(DashboardFiltersDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { projectId: 'p1', dashboardId: 'd1', filters, widgets: fiveWidgets, indicators: published, timeZone: 'America/Bogota', ...options },
    });
    const results: (DashboardFiltersResult | undefined)[] = [];
    let closed = false;
    ref.closed.subscribe((result) => {
      closed = true;
      results.push(result);
    });
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const root = () => document.querySelector('[data-testid="dashboard-filters-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const rows = () => Array.from(root()?.querySelectorAll<HTMLElement>('[data-testid="dashboard-filters-row"]') ?? []);
    const inRow = <T extends HTMLElement = HTMLElement>(index: number, id: string) => rows()[index].querySelector<T>(`[data-testid="${id}"]`)!;
    const answerColumns = async (answers: Record<string, DataColumn[]> = tables) => {
      for (const request of http.match((r) => r.url.endsWith('/columns'))) {
        const name = request.request.url.split('/').at(-2)!;
        request.flush(catalog(answers[name] ?? []));
      }
      await settle();
    };
    const type = async (input: HTMLInputElement, value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await settle();
    };
    const choose = async (select: HTMLSelectElement, value: string) => {
      select.value = value;
      select.dispatchEvent(new Event('change'));
      await settle();
    };
    const click = async (element: HTMLElement) => {
      element.click();
      await settle();
    };
    const columnOptions = (index: number) => Array.from(inRow<HTMLSelectElement>(index, 'dashboard-filters-column').options).map((o) => text(o));
    return { http, root, get, rows, inRow, answerColumns, type, choose, click, columnOptions, settle, results, isClosed: () => closed };
  }

  it('offers the columns of the dashboard\'s tables with how many widgets each narrows (Columns of several indicators)', async () => {
    const dialog = await open();
    await dialog.answerColumns();

    await dialog.click(dialog.get('dashboard-filters-add')!);

    expect(dialog.columnOptions(0)).toContain('municipality · text · narrows 4 of 5 widgets');
    expect(dialog.columnOptions(0)).toContain('zone · text · narrows 1 of 5 widgets');
    expect(dialog.inRow<HTMLInputElement>(0, 'dashboard-filters-label').value).toBe('Municipality');
    expect(document.activeElement).toBe(dialog.inRow(0, 'dashboard-filters-label'));
  });

  it('offers a column published as an integer and as a decimal once, as number (Integer and decimal together)', async () => {
    const dialog = await open();
    await dialog.answerColumns();
    await dialog.click(dialog.get('dashboard-filters-add')!);

    expect(dialog.columnOptions(0).filter((o) => o.startsWith('round'))).toEqual(['round · number · narrows 4 of 5 widgets']);
  });

  it('counts a widget that ignores the column among those it does not narrow', () => {
    const ignoring = fiveWidgets.map((w) => (w.id === 'w1' ? { ...w, config: { ...w.config, ignoredDashboardFilters: ['municipality'] } } : w));

    expect(columnChoices(tables, ignoring).find((c) => c.column === 'municipality')!.narrows).toBe(3);
  });

  it('says there are no columns yet and offers none while no indicator has published (No published indicator)', async () => {
    const dialog = await open([], { indicators: published.map((i) => ({ ...i, published: false })) });

    dialog.http.expectNone((r) => r.url.endsWith('/columns'));
    expect(dialog.get('dashboard-filters-no-columns')).not.toBeNull();
    expect(dialog.get('dashboard-filters-add')!.getAttribute('aria-disabled')).toBe('true');
    await dialog.click(dialog.get('dashboard-filters-add')!);
    expect(dialog.rows()).toHaveLength(0);
  });

  it('edits labels, columns and defaults, moves and removes filters, then saves the whole list', async () => {
    const dialog = await open([{ column: 'zone', label: 'Zone', kind: 'text', default: null }]);
    await dialog.answerColumns();

    await dialog.click(dialog.get('dashboard-filters-add')!);
    await dialog.choose(dialog.inRow<HTMLSelectElement>(1, 'dashboard-filters-column'), 'round|number');
    await dialog.type(dialog.inRow<HTMLInputElement>(1, 'dashboard-filters-label'), '  Round  ');
    // The default, through the clean data filter dialog titled with the label.
    await dialog.click(dialog.inRow(1, 'dashboard-filters-set-default'));
    const filterDialog = document.querySelector('[data-testid="filter-dialog"]') as HTMLElement;
    expect(text(filterDialog.querySelector('h2'))).toBe('Round');
    const min = filterDialog.querySelector<HTMLInputElement>('[data-testid="filter-min"]')!;
    await dialog.type(min, '3');
    const max = filterDialog.querySelector<HTMLInputElement>('[data-testid="filter-max"]')!;
    await dialog.type(max, '3');
    await dialog.click(filterDialog.querySelector<HTMLElement>('[data-testid="filter-apply"]')!);
    expect(text(dialog.inRow(1, 'dashboard-filters-default'))).toBe('Default: 3');

    await dialog.click(dialog.get('dashboard-filters-add')!);
    expect(dialog.rows()).toHaveLength(3);
    await dialog.click(dialog.inRow(2, 'dashboard-filters-remove'));
    await dialog.click(dialog.inRow(1, 'dashboard-filters-up'));
    expect(dialog.inRow(0, 'dashboard-filters-up').hasAttribute('disabled')).toBe(true);
    expect(dialog.inRow(1, 'dashboard-filters-down').hasAttribute('disabled')).toBe(true);
    await dialog.click(dialog.get('dashboard-filters-save')!);

    const request = dialog.http.expectOne(filtersUrl);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      filters: [
        { column: 'round', label: 'Round', kind: 'number', default: { min: '3', max: '3' } },
        { column: 'zone', label: 'Zone', kind: 'text', default: null },
      ],
    });
    request.flush({ filters: request.request.body.filters });
    await dialog.settle();
    expect(dialog.results).toEqual([request.request.body.filters]);
  });

  it('drops the default when the column changes to another kind', async () => {
    const dialog = await open([{ column: 'round', label: 'Round', kind: 'number', default: { min: '3', max: '3' } }]);
    await dialog.answerColumns();

    await dialog.choose(dialog.inRow<HTMLSelectElement>(0, 'dashboard-filters-column'), 'municipality|text');

    expect(text(dialog.inRow(0, 'dashboard-filters-default'))).toBe('Default: none');
  });

  it('keeps "Add filter" focusable at 8 filters, unavailable, saying why', async () => {
    const eight = Array.from({ length: 8 }, (_, i): DashboardFilter => ({ column: `c${i}`, label: `F${i}`, kind: 'text', default: null }));
    const dialog = await open(eight);
    await dialog.answerColumns();

    const add = dialog.get('dashboard-filters-add')!;
    expect(add.hasAttribute('disabled')).toBe(false);
    expect(add.getAttribute('aria-disabled')).toBe('true');
    expect(text(document.getElementById(add.getAttribute('aria-describedby')!))).toBe('A dashboard can have at most 8 filters.');
    await dialog.click(add);
    expect(dialog.rows()).toHaveLength(8);
  });

  it('keeps a saved filter on a column no table has, marked as in no table', async () => {
    const dialog = await open([{ column: 'district', label: 'District', kind: 'text', default: null }]);
    await dialog.answerColumns();

    const select = dialog.inRow<HTMLSelectElement>(0, 'dashboard-filters-column');
    expect(text(select.selectedOptions[0])).toBe('district · text · in no table');
  });

  it('shows a failure to read the columns inside and still saves the list as it is', async () => {
    const dialog = await open([{ column: 'municipality', label: 'Municipality', kind: 'text', default: null }]);
    dialog.http.match((r) => r.url.endsWith('/columns'))[0].flush({ title: 'Down' }, { status: 503, statusText: 'Unavailable' });
    await dialog.settle();
    dialog.http.match((r) => r.url.endsWith('/columns')).forEach((r) => r.flush(catalog([])));
    await dialog.settle();

    expect(dialog.get('dashboard-filters-columns-error')).not.toBeNull();
    await dialog.click(dialog.get('dashboard-filters-save')!);
    dialog.http.expectOne(filtersUrl).flush({ filters: [{ column: 'municipality', label: 'Municipality', kind: 'text', default: null }] });
    await dialog.settle();
    expect(dialog.isClosed()).toBe(true);
  });

  it('refuses an empty or taken label before sending, and shows the server\'s errors on their row', async () => {
    const dialog = await open([
      { column: 'municipality', label: 'Municipality', kind: 'text', default: null },
      { column: 'round', label: 'Round', kind: 'number', default: null },
    ]);
    await dialog.answerColumns();

    await dialog.type(dialog.inRow<HTMLInputElement>(1, 'dashboard-filters-label'), 'MUNICIPALITY');
    await dialog.click(dialog.get('dashboard-filters-save')!);
    dialog.http.expectNone(filtersUrl);
    expect(text(dialog.rows()[1].querySelector('[data-testid="field-error"]'))).toContain('Another filter of this dashboard is labelled "MUNICIPALITY".');

    await dialog.type(dialog.inRow<HTMLInputElement>(1, 'dashboard-filters-label'), 'Round');
    await dialog.click(dialog.get('dashboard-filters-save')!);
    dialog.http.expectOne(filtersUrl).flush(
      { title: 'One or more validation errors occurred.', status: 400, errors: { 'filters[1].default': ["Column 'round': 'three' is not a valid minimum for a number filter."], 'filters[0].column': ['Bad column.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await dialog.settle();

    expect(text(dialog.rows()[0])).toContain('Bad column.');
    expect(text(dialog.rows()[1])).toContain("'three' is not a valid minimum");
    expect(dialog.get('dashboard-filters-error')).toBeNull();
    expect(dialog.isClosed()).toBe(false);
  });

  it('closes with gone when the dashboard was removed meanwhile', async () => {
    const dialog = await open();
    await dialog.answerColumns();

    await dialog.click(dialog.get('dashboard-filters-save')!);
    dialog.http.expectOne(filtersUrl).flush({ title: 'Dashboard not found.', status: 404, code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' });
    await dialog.settle();

    expect(dialog.results).toEqual([{ gone: true }]);
  });

  it('closes on Escape without saving', async () => {
    const dialog = await open();
    await dialog.answerColumns();

    pressKey(dialog.root()!, 'Escape');
    await dialog.settle();

    expect(dialog.results).toEqual([undefined]);
    dialog.http.expectNone(filtersUrl);
  });
});
