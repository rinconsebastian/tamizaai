import { Dialog } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DataColumn } from '../../../core/projects/data.service';
import { DashboardColors, IndicatorColumns, Widget, maxValueColors, paletteIds } from '../../../core/projects/indicators.service';
import { InterfaceLanguage } from '../../../core/i18n/interface-language';
import { pressKey, provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { valueSearchDelayMs } from '../data-filter-dialog';
import { DashboardColorsData, DashboardColorsDialog, DashboardColorsResult, sameColors } from './dashboard-colors-dialog';
import { config, indicator, wait, widget } from './indicators-testing';

@Component({ template: '' })
class Host {
  readonly container = inject(ViewContainerRef);
}

const columnsUrl = '/columns';
const colorsUrl = '/api/v1/projects/p1/dashboards/d1/colors';
const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const col = (name: string, type: DataColumn['type']): DataColumn => ({ name, type, label: null });
const catalog = (columns: DataColumn[]): IndicatorColumns => ({ published: true, columns, numeric: [], defaults: { measure: null, groupBy: null } });

/** Two widgets over two indicators: `coverage` has the text columns `municipality` and `sex`, `water` has `water_source`. */
const widgets: Widget[] = [widget('w1', { config: config({ indicator: 'coverage' }) }), widget('w2', { config: config({ indicator: 'water' }) })];
const tables: Record<string, DataColumn[]> = {
  coverage: [col('municipality', 'text'), col('sex', 'text'), col('round', 'integer'), col('value', 'decimal')],
  water: [col('water_source', 'text'), col('value', 'decimal')],
};
const published = ['coverage', 'water'].map((name) => indicator(name));
const tamiza: DashboardColors = { palette: 'tamiza', valueColors: [] };
const kept = (column: string, value: string, color: number) => ({ column, value, color });

describe('DashboardColorsDialog', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(colors: DashboardColors = tamiza, options: Partial<DashboardColorsData> = {}, lang: InterfaceLanguage = 'en') {
    TestBed.configureTestingModule({
      imports: [translocoTesting(lang)],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Host);
    const ref = TestBed.inject(Dialog).open<DashboardColorsResult, DashboardColorsData>(DashboardColorsDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { projectId: 'p1', dashboardId: 'd1', colors, widgets, indicators: published, ...options },
    });
    const results: (DashboardColorsResult | undefined)[] = [];
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
    const root = () => document.querySelector('[data-testid="dashboard-colors-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const getAll = <T extends HTMLElement = HTMLElement>(id: string) => Array.from(root()?.querySelectorAll<T>(`[data-testid="${id}"]`) ?? []);
    const rows = () => getAll('dashboard-colors-row');
    const inRow = <T extends HTMLElement = HTMLElement>(index: number, id: string) => rows()[index].querySelector<T>(`[data-testid="${id}"]`)!;
    const colorRadios = (index: number) => Array.from(rows()[index].querySelectorAll<HTMLInputElement>('[data-testid="dashboard-colors-color"]'));
    const checkedColor = (index: number) => colorRadios(index).find((r) => r.checked)?.dataset['color'] ?? null;
    const palettes = () => getAll<HTMLInputElement>('dashboard-colors-palette');
    const answerColumns = async (answers: Record<string, DataColumn[]> = tables) => {
      for (const request of http.match((r) => r.url.endsWith(columnsUrl))) {
        const name = request.request.url.split('/').at(-2)!;
        request.flush(catalog(answers[name] ?? []));
      }
      await settle();
    };
    const valueRequests = () => http.match((r) => r.url.endsWith('/values'));
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
    const columnOptions = (index: number) => Array.from(inRow<HTMLSelectElement>(index, 'dashboard-colors-column').options).map((o) => text(o));
    /** Adds an entry as a member would: the column, the value typed and the color clicked. */
    const addEntry = async (column: string, value: string, color: number) => {
      await click(get('dashboard-colors-add')!);
      const index = rows().length - 1;
      await choose(inRow<HTMLSelectElement>(index, 'dashboard-colors-column'), column);
      await type(inRow<HTMLInputElement>(index, 'dashboard-colors-value'), value);
      await click(colorRadios(index).find((r) => r.dataset['color'] === String(color))!);
    };
    return { http, root, get, getAll, rows, inRow, colorRadios, addEntry, checkedColor, palettes, answerColumns, valueRequests, type, choose, click, columnOptions, settle, results, isClosed: () => closed };
  }

  describe('the palette', () => {
    it('offers Tamiza, Institutional, Earth and Vivid as radio inputs and no field for a color code (Palettes offered)', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      expect(dialog.getAll('dashboard-colors-palette-name').map(text)).toEqual(['Tamiza', 'Institutional', 'Earth', 'Vivid']);
      expect(dialog.palettes().map((p) => p.type)).toEqual(['radio', 'radio', 'radio', 'radio']);
      expect(new Set(dialog.palettes().map((p) => p.name)).size).toBe(1);
      // With no color kept by value, the only fields are the palette, the buttons: no text input, no color input.
      expect(dialog.root()!.querySelectorAll('input[type="color"], input[type="text"], input:not([type])')).toHaveLength(0);
      expect(dialog.root()!.querySelectorAll('fieldset > legend')).toHaveLength(1);
      expect(text(dialog.root()!.querySelector('fieldset > legend'))).toBe('Palette');
    });

    it('shows each palette\'s eight colors in order and its scale as a gradient, the current one selected (Palette previewed)', async () => {
      const dialog = await open({ palette: 'earth', valueColors: [] });
      await dialog.answerColumns();

      const cards = dialog.getAll('dashboard-colors-palette-card');
      expect(cards).toHaveLength(4);
      for (const card of cards) {
        const id = card.dataset['palette']!;
        const swatches = Array.from(card.querySelectorAll<HTMLElement>('[data-testid="dashboard-colors-palette-swatches"] > span'));
        expect(swatches).toHaveLength(8);
        swatches.forEach((swatch, i) => expect(swatch.style.backgroundColor).toBe(`var(--tmz-pal-${id}-${i + 1})`));
      }
      const scales = Object.fromEntries(cards.map((card) => [card.dataset['palette'], card.querySelector<HTMLElement>('[data-scale]')!.dataset['scale']]));
      expect(scales).toEqual({ tamiza: 'blue', institutional: 'teal', earth: 'orange', vivid: 'purple' });
      const earth = cards.find((c) => c.dataset['palette'] === 'earth')!.querySelector<HTMLElement>('[data-scale]')!;
      for (let step = 1; step <= 7; step++) {
        expect(earth.style.backgroundImage).toContain(`var(--tmz-ramp-orange-${step})`);
      }
      expect(earth.style.backgroundImage).toMatch(/^linear-gradient\(/);
      expect(cards.map((c) => text(c.querySelector('[data-testid="dashboard-colors-palette-scale"]')))).toEqual([
        'Scale for maps and heat maps: Blue',
        'Scale for maps and heat maps: Teal',
        'Scale for maps and heat maps: Orange',
        'Scale for maps and heat maps: Purple',
      ]);
      expect(dialog.palettes().map((p) => p.checked)).toEqual([false, false, true, false]);
    });

    it('names each palette to a screen reader and describes its colors by number and hue', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      const institutional = dialog.palettes()[1];
      const named = (attribute: string) => text(document.getElementById(institutional.getAttribute(attribute)!));
      expect(named('aria-labelledby')).toBe('Institutional');
      expect(named('aria-describedby')).toContain('Colors in order: 1 navy blue, 2 turquoise, 3 burgundy, 4 sky blue, 5 mustard, 6 slate, 7 salmon, 8 olive green.');
    });

    it('offers every palette the API knows, in the catalog\'s order', async () => {
      const dialog = await open();

      expect(dialog.palettes().map((p) => p.value)).toEqual([...paletteIds]);
    });

    it('saves the palette chosen with the colors kept as they are (Palette chosen, UI part)', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();

      await dialog.click(dialog.palettes()[2]);
      expect(dialog.palettes().map((p) => p.checked)).toEqual([false, false, true, false]);
      await dialog.click(dialog.get('dashboard-colors-save')!);

      const request = dialog.http.expectOne(colorsUrl);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ palette: 'earth', valueColors: [kept('sex', 'Female', 5)] });
      request.flush({ palette: 'earth', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.settle();
      expect(dialog.results).toEqual([{ palette: 'earth', valueColors: [kept('sex', 'Female', 5)] }]);
    });

    it('sends nothing and closes when nothing changed (Colors saved unchanged)', async () => {
      const dialog = await open({ palette: 'vivid', valueColors: [kept('sex', 'Female', 5), kept('sex', 'Male', 1)] });
      await dialog.answerColumns();

      await dialog.click(dialog.get('dashboard-colors-save')!);

      dialog.http.expectNone(colorsUrl);
      expect(dialog.results).toEqual([undefined]);
    });

    it('sends nothing for a palette picked and then put back', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      await dialog.click(dialog.palettes()[3]);
      await dialog.click(dialog.palettes()[0]);
      await dialog.click(dialog.get('dashboard-colors-save')!);

      dialog.http.expectNone(colorsUrl);
      expect(dialog.results).toEqual([undefined]);
    });

    it('shows the server\'s error on the palette', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      await dialog.click(dialog.palettes()[1]);
      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectOne(colorsUrl).flush(
        { title: 'One or more validation errors occurred.', status: 400, errors: { palette: ['Choose one of the palettes: tamiza, institutional, earth, vivid.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await dialog.settle();

      expect(text(dialog.get('dashboard-colors-palettes')!.querySelector('[data-testid="field-error"]'))).toBe('Choose one of the palettes: tamiza, institutional, earth, vivid.');
      expect(dialog.isClosed()).toBe(false);
      await dialog.click(dialog.palettes()[2]);
      expect(dialog.get('dashboard-colors-palettes')!.querySelector('[data-testid="field-error"]')).toBeNull();
    });
  });

  describe('the colors kept by value', () => {
    it('lists the entries as saved, in order, with the text columns of the dashboard\'s tables to choose from', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5), kept('municipality', 'Rionegro', 2)] });
      await dialog.answerColumns();

      expect(dialog.rows()).toHaveLength(2);
      expect(dialog.inRow<HTMLSelectElement>(0, 'dashboard-colors-column').value).toBe('sex');
      expect(dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value').value).toBe('Female');
      expect(dialog.checkedColor(0)).toBe('5');
      expect(dialog.inRow<HTMLInputElement>(1, 'dashboard-colors-value').value).toBe('Rionegro');
      expect(dialog.checkedColor(1)).toBe('2');
      // Text columns only, one each: no `round` (integer) and no `value` (decimal).
      expect(dialog.columnOptions(0)).toEqual(['municipality', 'sex', 'water_source']);
    });

    it('says there is nothing kept yet', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      expect(text(dialog.get('dashboard-colors-empty'))).toContain('No colors are kept by value.');
    });

    it('keeps a saved entry on a column no table has, marked as in no table', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('district', 'North', 3)] });
      await dialog.answerColumns();

      const select = dialog.inRow<HTMLSelectElement>(0, 'dashboard-colors-column');
      expect(text(select.selectedOptions[0])).toBe('district · in no table');
      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectNone(colorsUrl);
    });

    it('suggests the column\'s values as the member searches and accepts any typed text (Value picked from the data)', async () => {
      const dialog = await open();
      await dialog.answerColumns();
      await dialog.click(dialog.get('dashboard-colors-add')!);
      await dialog.choose(dialog.inRow<HTMLSelectElement>(0, 'dashboard-colors-column'), 'water_source');

      const input = dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value');
      // A native combobox: an input tied to a list of options, which screen readers and the keyboard already know.
      const list = document.getElementById(input.getAttribute('list')!) as HTMLDataListElement;
      expect(list.tagName).toBe('DATALIST');
      await dialog.type(input, 'pip');
      expect(dialog.valueRequests()).toHaveLength(0);
      await wait(valueSearchDelayMs + 50);

      const [request] = dialog.valueRequests();
      expect(request.request.params.get('column')).toBe('water_source');
      expect(request.request.params.get('search')).toBe('pip');
      request.flush({ values: [{ value: 'Piped', count: 12 }, { value: '', count: 1 }] });
      await dialog.settle();
      expect(Array.from(list.options).map((o) => o.value)).toEqual(['Piped']);

      // Picking one sets the value; so would typing text the data does not hold.
      await dialog.type(input, 'Piped');
      expect(input.value).toBe('Piped');
      await dialog.type(input, 'Lake');
      await dialog.click(dialog.get('dashboard-colors-save')!);
      expect(dialog.http.expectOne(colorsUrl).request.body.valueColors).toEqual([kept('water_source', 'Lake', 1)]);
    });

    it('reads the column\'s most frequent values once when its value is first focused, and only the last search counts', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();

      expect(dialog.valueRequests()).toHaveLength(0);
      const input = dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value');
      input.dispatchEvent(new Event('focus'));
      await dialog.settle();
      const [first] = dialog.valueRequests();
      expect(first.request.params.get('column')).toBe('sex');
      expect(first.request.params.has('search')).toBe(false);
      first.flush({ values: [{ value: 'Female', count: 3 }, { value: 'Male', count: 2 }] });
      await dialog.settle();
      const list = document.getElementById(input.getAttribute('list')!) as HTMLDataListElement;
      expect(Array.from(list.options).map((o) => o.value)).toEqual(['Female', 'Male']);

      // Focused again: the list is already there.
      input.dispatchEvent(new Event('focus'));
      await dialog.settle();
      expect(dialog.valueRequests()).toHaveLength(0);

      await dialog.type(input, 'Fem');
      await dialog.type(input, 'Fema');
      await wait(valueSearchDelayMs + 50);
      const searches = dialog.valueRequests();
      expect(searches.filter((r) => !r.cancelled).map((r) => r.request.params.get('search'))).toEqual(['Fema']);
    });

    it('drops the answer for a column the row no longer has and asks for the new column\'s values when the value is focused again', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();
      const input = dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value');
      const list = document.getElementById(input.getAttribute('list')!) as HTMLDataListElement;

      input.dispatchEvent(new Event('focus'));
      await dialog.settle();
      const [stale] = dialog.valueRequests();
      expect(stale.request.params.get('column')).toBe('sex');
      // The column changes while the answer for `sex` is on its way.
      await dialog.choose(dialog.inRow<HTMLSelectElement>(0, 'dashboard-colors-column'), 'municipality');
      stale.flush({ values: [{ value: 'Female', count: 3 }, { value: 'Male', count: 2 }] });
      await dialog.settle();
      expect(Array.from(list.options).map((o) => o.value)).toEqual([]);

      // The late answer did not mark the row as read: focusing the value asks for the municipalities.
      input.dispatchEvent(new Event('focus'));
      await dialog.settle();
      const [fresh] = dialog.valueRequests();
      expect(fresh.request.params.get('column')).toBe('municipality');
      fresh.flush({ values: [{ value: 'Rionegro', count: 4 }] });
      await dialog.settle();
      expect(Array.from(list.options).map((o) => o.value)).toEqual(['Rionegro']);
    });

    it('drops a failed lookup for a column the row no longer has as it drops an answer', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();
      const input = dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value');
      const list = document.getElementById(input.getAttribute('list')!) as HTMLDataListElement;

      input.dispatchEvent(new Event('focus'));
      await dialog.settle();
      const [stale] = dialog.valueRequests();
      await dialog.choose(dialog.inRow<HTMLSelectElement>(0, 'dashboard-colors-column'), 'municipality');
      stale.flush({ title: 'Down' }, { status: 503, statusText: 'Unavailable' });
      await dialog.settle();

      // Had the failure been kept as the new column's (empty) list, focusing again would not ask.
      input.dispatchEvent(new Event('focus'));
      await dialog.settle();
      const [fresh] = dialog.valueRequests();
      expect(fresh.request.params.get('column')).toBe('municipality');
      fresh.flush({ values: [{ value: 'Rionegro', count: 4 }] });
      await dialog.settle();
      expect(Array.from(list.options).map((o) => o.value)).toEqual(['Rionegro']);
    });

    it('does not ask for the values of the old column when the column changes while the member is still typing', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();
      const input = dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value');

      await dialog.type(input, 'Fem');
      await dialog.choose(dialog.inRow<HTMLSelectElement>(0, 'dashboard-colors-column'), 'municipality');
      await wait(valueSearchDelayMs + 50);

      expect(dialog.valueRequests().filter((r) => !r.cancelled)).toEqual([]);
    });

    it('still lets a value be typed when the values cannot be read', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();
      const input = dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value');

      input.dispatchEvent(new Event('focus'));
      await dialog.settle();
      dialog.valueRequests()[0].flush({ title: 'Down' }, { status: 503, statusText: 'Unavailable' });
      await dialog.settle();
      await dialog.type(input, 'Intersex');
      await dialog.click(dialog.get('dashboard-colors-save')!);

      expect(dialog.http.expectOne(colorsUrl).request.body.valueColors).toEqual([kept('sex', 'Intersex', 5)]);
    });

    it('adds an entry on the column of the one before, in the first color that column does not use, and moves focus to it', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 1), kept('sex', 'Male', 2)] });
      await dialog.answerColumns();

      await dialog.click(dialog.get('dashboard-colors-add')!);

      expect(dialog.rows()).toHaveLength(3);
      expect(dialog.inRow<HTMLSelectElement>(2, 'dashboard-colors-column').value).toBe('sex');
      expect(dialog.checkedColor(2)).toBe('3');
      expect(document.activeElement).toBe(dialog.inRow(2, 'dashboard-colors-column'));
    });

    it('adds an entry after one on a column no table offers on the first column offered, in the first color that column does not use', async () => {
      // The row before is on `district`, which is in no table, and `municipality` (the first column offered) already uses color 1: the
      // color of the new row comes from the column it lands on, not from the one it fell back from.
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('municipality', 'Rionegro', 1), kept('district', 'North', 2)] });
      await dialog.answerColumns();

      await dialog.click(dialog.get('dashboard-colors-add')!);

      expect(dialog.rows()).toHaveLength(3);
      expect(dialog.inRow<HTMLSelectElement>(2, 'dashboard-colors-column').value).toBe('municipality');
      expect(dialog.checkedColor(2)).toBe('2');
      await dialog.type(dialog.inRow<HTMLInputElement>(2, 'dashboard-colors-value'), 'Pasto');
      await dialog.click(dialog.get('dashboard-colors-save')!);
      expect(dialog.http.expectOne(colorsUrl).request.body.valueColors).toEqual([
        kept('municipality', 'Rionegro', 1),
        kept('district', 'North', 2),
        kept('municipality', 'Pasto', 2),
      ]);
    });

    it('starts the first entry on the first column offered, in color 1', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      await dialog.click(dialog.get('dashboard-colors-add')!);

      expect(dialog.inRow<HTMLSelectElement>(0, 'dashboard-colors-column').value).toBe('municipality');
      expect(dialog.checkedColor(0)).toBe('1');
    });

    it('refuses an entry with no value before sending, unless it was saved with the empty text', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', '', 5)] });
      await dialog.answerColumns();
      await dialog.click(dialog.get('dashboard-colors-add')!);

      await dialog.click(dialog.get('dashboard-colors-save')!);

      dialog.http.expectNone(colorsUrl);
      expect(text(dialog.rows()[1].querySelector('[data-testid="field-error"]'))).toBe('Pick or type a value.');
      expect(dialog.rows()[0].querySelector('[data-testid="field-error"]')).toBeNull();
      await dialog.type(dialog.inRow<HTMLInputElement>(1, 'dashboard-colors-value'), 'Male');
      expect(dialog.rows()[1].querySelector('[data-testid="field-error"]')).toBeNull();
      await dialog.click(dialog.get('dashboard-colors-save')!);
      // Color 5 is the saved entry's, so the new one takes the first color `sex` does not use.
      expect(dialog.http.expectOne(colorsUrl).request.body.valueColors).toEqual([kept('sex', '', 5), kept('sex', 'Male', 1)]);
    });

    it('changes a color, a column and a value, removes an entry and saves the whole list in order', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5), kept('municipality', 'Rionegro', 2), kept('sex', 'Male', 1)] });
      await dialog.answerColumns();

      await dialog.click(dialog.colorRadios(0).find((r) => r.dataset['color'] === '7')!);
      await dialog.choose(dialog.inRow<HTMLSelectElement>(1, 'dashboard-colors-column'), 'water_source');
      await dialog.type(dialog.inRow<HTMLInputElement>(1, 'dashboard-colors-value'), 'Piped');
      await dialog.click(dialog.inRow(2, 'dashboard-colors-remove'));
      expect(dialog.rows()).toHaveLength(2);
      await dialog.click(dialog.get('dashboard-colors-save')!);

      const request = dialog.http.expectOne(colorsUrl);
      expect(request.request.body).toEqual({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 7), kept('water_source', 'Piped', 2)] });
      request.flush(request.request.body);
      await dialog.settle();
      expect(dialog.results).toEqual([request.request.body]);
    });

    it('keeps the value as typed, spaces and case included', async () => {
      const dialog = await open();
      await dialog.answerColumns();
      await dialog.click(dialog.get('dashboard-colors-add')!);

      await dialog.type(dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value'), ' piped ');
      await dialog.click(dialog.get('dashboard-colors-save')!);

      expect(dialog.http.expectOne(colorsUrl).request.body.valueColors).toEqual([kept('municipality', ' piped ', 1)]);
    });

    it('keeps "Add" focusable at 64 entries, unavailable, saying why', async () => {
      const full = Array.from({ length: maxValueColors }, (_, i) => kept(`c${i}`, 'x', 1));
      const dialog = await open({ palette: 'tamiza', valueColors: full });
      await dialog.answerColumns();

      const add = dialog.get('dashboard-colors-add')!;
      expect(add.hasAttribute('disabled')).toBe(false);
      expect(add.getAttribute('aria-disabled')).toBe('true');
      expect(text(document.getElementById(add.getAttribute('aria-describedby')!))).toBe('A dashboard keeps at most 64 colors by value.');
      await dialog.click(add);
      expect(dialog.rows()).toHaveLength(maxValueColors);
    });

    it('says there are no columns to keep colors for and offers none while no indicator has published', async () => {
      const dialog = await open(tamiza, { indicators: published.map((i) => ({ ...i, published: false })) });

      dialog.http.expectNone((r) => r.url.endsWith(columnsUrl));
      expect(dialog.get('dashboard-colors-no-columns')).not.toBeNull();
      expect(dialog.get('dashboard-colors-add')!.getAttribute('aria-disabled')).toBe('true');
      await dialog.click(dialog.get('dashboard-colors-add')!);
      expect(dialog.rows()).toHaveLength(0);
    });

    it('offers only text columns: a dashboard whose tables have none offers none', async () => {
      const dialog = await open();
      await dialog.answerColumns({ coverage: [col('round', 'integer'), col('value', 'decimal')], water: [col('value', 'decimal')] });

      expect(dialog.get('dashboard-colors-no-columns')).not.toBeNull();
    });

    it('shows a failure to read the columns inside and still saves the list as it is', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      dialog.http.match((r) => r.url.endsWith(columnsUrl))[0].flush({ title: 'Down' }, { status: 503, statusText: 'Unavailable' });
      await dialog.settle();
      dialog.http.match((r) => r.url.endsWith(columnsUrl)).forEach((r) => r.flush(catalog([])));
      await dialog.settle();

      expect(dialog.get('dashboard-colors-columns-error')).not.toBeNull();
      await dialog.click(dialog.palettes()[1]);
      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectOne(colorsUrl).flush({ palette: 'institutional', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.settle();
      expect(dialog.isClosed()).toBe(true);
    });
  });

  describe('the color of an entry', () => {
    it('is one of the palette\'s eight swatches, each with its number visible and read as number and hue (Named choices)', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 3)] });
      await dialog.answerColumns();

      const radios = dialog.colorRadios(0);
      expect(radios.map((r) => r.getAttribute('aria-label'))).toEqual([
        'Color 1, blue',
        'Color 2, orange',
        'Color 3, aqua',
        'Color 4, yellow',
        'Color 5, magenta',
        'Color 6, green',
        'Color 7, violet',
        'Color 8, red',
      ]);
      expect(radios.map((r) => r.checked)).toEqual([false, false, true, false, false, false, false, false]);
      expect(new Set(radios.map((r) => r.name)).size).toBe(1);
      const numbers = Array.from(dialog.rows()[0].querySelectorAll('[data-testid="dashboard-colors-color-number"]')).map(text);
      expect(numbers).toEqual(['1', '2', '3', '4', '5', '6', '7', '8']);
      const dots = radios.map((r) => r.parentElement!.querySelector<HTMLElement>('span > span')!.style.backgroundColor);
      expect(dots).toEqual([1, 2, 3, 4, 5, 6, 7, 8].map((n) => `var(--tmz-pal-tamiza-${n})`));
    });

    it('follows the palette picked in the form, in swatches and in names', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 3)] });
      await dialog.answerColumns();

      await dialog.click(dialog.palettes()[2]);

      expect(dialog.colorRadios(0)[2].getAttribute('aria-label')).toBe('Color 3, ochre');
      expect(dialog.colorRadios(0)[2].parentElement!.querySelector<HTMLElement>('span > span')!.style.backgroundColor).toBe('var(--tmz-pal-earth-3)');
    });

    it('is named in Spanish by number and hue', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 3)] }, {}, 'es');
      await dialog.answerColumns();

      expect(dialog.colorRadios(0)[2].getAttribute('aria-label')).toBe('Color 3, aguamarina');
      expect(dialog.getAll('dashboard-colors-palette-name').map(text)).toEqual(['Tamiza', 'Institucional', 'Tierra', 'Vívido']);
      expect(text(dialog.get('dashboard-colors-save'))).toBe('Guardar colores');
    });

    it('is named in the legend of its group with the entry it colors', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 3)] });
      await dialog.answerColumns();

      expect(text(dialog.inRow(0, 'dashboard-colors-choice').querySelector('legend'))).toBe('Color for sex: Female');
    });
  });

  describe('what the API refuses', () => {
    const refused = (errors: Record<string, string[]>) => [
      { title: 'One or more validation errors occurred.', status: 400, errors },
      { status: 400, statusText: 'Bad Request' },
    ] as const;

    it('shows a second entry for a color a column already uses on its row, and announces that the form needs attention (Color used twice in a column, UI part)', async () => {
      const dialog = await open();
      await dialog.answerColumns();
      await dialog.addEntry('sex', 'Female', 5);
      await dialog.addEntry('sex', 'Male', 5);

      await dialog.click(dialog.get('dashboard-colors-save')!);
      const request = dialog.http.expectOne(colorsUrl);
      expect(request.request.body.valueColors).toEqual([kept('sex', 'Female', 5), kept('sex', 'Male', 5)]);
      request.flush(...refused({ 'valueColors[1]': ['sex already uses color 5.'] }));
      await dialog.settle();

      expect(dialog.isClosed()).toBe(false);
      expect(dialog.rows()[0].querySelector('[data-testid="dashboard-colors-row-error"]')).toBeNull();
      expect(text(dialog.inRow(1, 'dashboard-colors-row-error'))).toBe('sex already uses color 5.');
      expect(text(dialog.get('dashboard-colors-error'))).toBe('Some fields need attention.');
      expect(dialog.get('dashboard-colors-error')!.getAttribute('role')).toBe('alert');
      expect(dialog.get('dashboard-colors-save')!.hasAttribute('disabled')).toBe(false);

      // Changing the entry clears what was said of it.
      await dialog.click(dialog.colorRadios(1).find((r) => r.dataset['color'] === '1')!);
      expect(dialog.rows()[1].querySelector('[data-testid="dashboard-colors-row-error"]')).toBeNull();
    });

    it('shows a value kept twice in a column on its row', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();
      await dialog.addEntry('sex', 'Female', 6);

      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectOne(colorsUrl).flush(...refused({ 'valueColors[1]': ['sex already keeps a color for Female.'] }));
      await dialog.settle();

      expect(text(dialog.inRow(1, 'dashboard-colors-row-error'))).toBe('sex already keeps a color for Female.');
    });

    it('puts the errors on a column, a value and a color next to those fields of their row', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5), kept('municipality', 'Rionegro', 2)] });
      await dialog.answerColumns();

      // Something changed, so it is sent; the server's reasons are the ones its rules give.
      await dialog.click(dialog.colorRadios(0).find((r) => r.dataset['color'] === '6')!);
      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectOne(colorsUrl).flush(
        ...refused({
          'valueColors[0].column': ['The column must be a published column\'s name.'],
          'valueColors[1].value': ['The value is longer than 500 characters.'],
          'valueColors[1].color': ['The color must be a number from 1 to 8.'],
        }),
      );
      await dialog.settle();

      const errorsOf = (index: number) => Array.from(dialog.rows()[index].querySelectorAll('[data-testid="field-error"]')).map(text);
      expect(errorsOf(0)).toEqual(['The column must be a published column\'s name.']);
      expect(errorsOf(1)).toEqual(['The value is longer than 500 characters.', 'The color must be a number from 1 to 8.']);
      expect(dialog.inRow(0, 'dashboard-colors-column').getAttribute('aria-invalid')).toBe('true');
      expect(dialog.inRow(1, 'dashboard-colors-value').getAttribute('aria-invalid')).toBe('true');
      expect(dialog.inRow(1, 'dashboard-colors-choice').getAttribute('aria-describedby')).toBeTruthy();
      expect(dialog.inRow(0, 'dashboard-colors-value').getAttribute('aria-invalid')).toBeNull();
    });

    it('shows the limit of the whole list under the list, and anything it cannot place in the form\'s alert', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();

      await dialog.click(dialog.palettes()[1]);
      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectOne(colorsUrl).flush(...refused({ valueColors: ['A dashboard keeps at most 64 colors by value.'] }));
      await dialog.settle();
      expect(text(dialog.get('dashboard-colors-add')!.parentElement!.querySelector('[data-testid="field-error"]'))).toBe('A dashboard keeps at most 64 colors by value.');

      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectOne(colorsUrl).flush(...refused({ 'valueColors[9]': ['Nothing there.'], other: ['Something else.'] }));
      await dialog.settle();
      expect(text(dialog.get('dashboard-colors-error'))).toBe('Nothing there. Something else.');
      expect(dialog.get('dashboard-colors-add')!.parentElement!.querySelector('[data-testid="field-error"]')).toBeNull();
    });

    it('shows a failure with no field in the form\'s alert', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      await dialog.click(dialog.palettes()[1]);
      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectOne(colorsUrl).flush({ title: 'Boom', status: 500, detail: 'The colors could not be saved.' }, { status: 500, statusText: 'Server Error' });
      await dialog.settle();

      expect(text(dialog.get('dashboard-colors-error'))).toBe('The colors could not be saved.');
      expect(dialog.isClosed()).toBe(false);
    });

    it('closes with gone when the dashboard was removed meanwhile', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      await dialog.click(dialog.palettes()[1]);
      await dialog.click(dialog.get('dashboard-colors-save')!);
      dialog.http.expectOne(colorsUrl).flush({ title: 'Dashboard not found.', status: 404, code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' });
      await dialog.settle();

      expect(dialog.results).toEqual([{ gone: true }]);
    });
  });

  describe('from the keyboard', () => {
    it('reaches every field with Tab: native controls only, in the order they read, and each radio group is one stop with arrow keys', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();
      const controls = Array.from(dialog.root()!.querySelectorAll<HTMLElement>('input, select, button, textarea, [tabindex]'));

      // Nothing is taken out of the tab order and nothing is positioned ahead of the others.
      expect(controls.every((c) => c.tabIndex === 0 && !c.hasAttribute('disabled'))).toBe(true);
      expect(controls.every((c) => !c.hasAttribute('tabindex'))).toBe(true);
      // The palettes are one radio group (the browser moves between them with the arrow keys), the colors of each entry another.
      const groups = new Map<string, number>();
      for (const radio of controls.filter((c): c is HTMLInputElement => c instanceof HTMLInputElement && c.type === 'radio')) {
        groups.set(radio.name, (groups.get(radio.name) ?? 0) + 1);
      }
      expect([...groups.values()]).toEqual([4, 8]);
      // Each group is a fieldset with its legend, so a screen reader says what it is choosing.
      for (const fieldset of Array.from(dialog.root()!.querySelectorAll('fieldset'))) {
        expect(fieldset.querySelector(':scope > legend')).not.toBeNull();
      }
      // Each control has a name: a label tied to it, or an aria-label.
      for (const control of controls.filter((c) => c instanceof HTMLInputElement || c instanceof HTMLSelectElement)) {
        const named = (control as HTMLInputElement).labels?.length || control.getAttribute('aria-label') || control.getAttribute('aria-labelledby');
        expect(named, control.outerHTML).toBeTruthy();
      }
      // The labelled order: palette, then column, value, color, remove, then add, cancel and save.
      expect(controls.map((c) => c.dataset['testid'])).toEqual([
        ...Array(4).fill('dashboard-colors-palette'),
        'dashboard-colors-column',
        'dashboard-colors-value',
        ...Array(8).fill('dashboard-colors-color'),
        'dashboard-colors-remove',
        'dashboard-colors-add',
        'dashboard-colors-cancel',
        'dashboard-colors-save',
      ]);
    });

    it.each([...paletteIds])('opens with focus on the palette in use, %s, not on the first radio, so the arrow keys move from it', async (palette) => {
      const dialog = await open({ palette, valueColors: [] });
      await dialog.answerColumns();

      const checked = dialog.palettes().filter((p) => p.checked);
      expect(checked.map((p) => p.value)).toEqual([palette]);
      expect(document.activeElement).toBe(checked[0]);
    });

    it('moves focus to the next entry\'s Remove button when one is removed, and to "Add" when none is left', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5), kept('sex', 'Male', 1)] });
      await dialog.answerColumns();
      dialog.inRow(0, 'dashboard-colors-remove').focus();

      await dialog.click(dialog.inRow(0, 'dashboard-colors-remove'));
      expect(document.activeElement).toBe(dialog.inRow(0, 'dashboard-colors-remove'));
      expect(dialog.inRow<HTMLInputElement>(0, 'dashboard-colors-value').value).toBe('Male');

      await dialog.click(dialog.inRow(0, 'dashboard-colors-remove'));
      expect(dialog.rows()).toHaveLength(0);
      expect(document.activeElement).toBe(dialog.get('dashboard-colors-add'));
    });

    it('names the Remove button after the entry it removes', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5)] });
      await dialog.answerColumns();

      expect(dialog.inRow(0, 'dashboard-colors-remove').getAttribute('aria-label')).toBe('Remove the color kept for sex: Female');
    });

    it('names each entry\'s column and value after the visible label and the entry\'s place, so no two entries read the same', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5), kept('sex', 'Male', 1), kept('municipality', 'Cali', 2)] });
      await dialog.answerColumns();

      const controls = (id: string) => dialog.rows().map((_, i) => dialog.inRow<HTMLSelectElement | HTMLInputElement>(i, id));
      const names = (id: string) => controls(id).map((control) => control.getAttribute('aria-label'));
      expect(names('dashboard-colors-column')).toEqual(['Column, entry 1 of 3', 'Column, entry 2 of 3', 'Column, entry 3 of 3']);
      expect(names('dashboard-colors-value')).toEqual(['Value, entry 1 of 3', 'Value, entry 2 of 3', 'Value, entry 3 of 3']);
      expect(new Set([...names('dashboard-colors-column'), ...names('dashboard-colors-value')]).size).toBe(6);
      // The visible label is still the start of the name (label in name), and still tied to its control.
      for (const control of [...controls('dashboard-colors-column'), ...controls('dashboard-colors-value')]) {
        const visible = text(control.labels![0]);
        expect(['Column', 'Value']).toContain(visible);
        expect(control.getAttribute('aria-label')!.startsWith(visible)).toBe(true);
      }
    });

    it('keeps the entries\' names in step with the list as entries are added and removed', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5), kept('sex', 'Male', 1)] });
      await dialog.answerColumns();
      const valueNames = () => dialog.rows().map((_, i) => dialog.inRow(i, 'dashboard-colors-value').getAttribute('aria-label'));

      await dialog.click(dialog.get('dashboard-colors-add')!);
      expect(valueNames()).toEqual(['Value, entry 1 of 3', 'Value, entry 2 of 3', 'Value, entry 3 of 3']);

      await dialog.click(dialog.inRow(0, 'dashboard-colors-remove'));
      expect(valueNames()).toEqual(['Value, entry 1 of 2', 'Value, entry 2 of 2']);
      expect(dialog.rows().map((_, i) => dialog.inRow(i, 'dashboard-colors-column').getAttribute('aria-label'))).toEqual(['Column, entry 1 of 2', 'Column, entry 2 of 2']);
    });

    it('names them in Spanish too', async () => {
      const dialog = await open({ palette: 'tamiza', valueColors: [kept('sex', 'Female', 5), kept('sex', 'Male', 1)] }, {}, 'es');
      await dialog.answerColumns();

      expect(dialog.inRow(1, 'dashboard-colors-column').getAttribute('aria-label')).toBe('Columna, entrada 2 de 2');
      expect(dialog.inRow(1, 'dashboard-colors-value').getAttribute('aria-label')).toBe('Valor, entrada 2 de 2');
    });

    it('closes on Escape without saving', async () => {
      const dialog = await open();
      await dialog.answerColumns();

      await dialog.click(dialog.palettes()[1]);
      pressKey(dialog.root()!, 'Escape');
      await dialog.settle();

      expect(dialog.results).toEqual([undefined]);
      dialog.http.expectNone(colorsUrl);
    });
  });
});

describe('sameColors', () => {
  it('compares the palette and the entries in order', () => {
    const base: DashboardColors = { palette: 'earth', valueColors: [kept('sex', 'Female', 5), kept('sex', 'Male', 1)] };

    expect(sameColors(base, { palette: 'earth', valueColors: [...base.valueColors] })).toBe(true);
    expect(sameColors(base, { ...base, palette: 'vivid' })).toBe(false);
    expect(sameColors(base, { ...base, valueColors: [base.valueColors[1], base.valueColors[0]] })).toBe(false);
    expect(sameColors(base, { ...base, valueColors: [base.valueColors[0]] })).toBe(false);
    expect(sameColors(base, { ...base, valueColors: [kept('sex', 'female', 5), base.valueColors[1]] })).toBe(false);
  });
});
