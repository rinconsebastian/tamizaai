import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../../../public/i18n/en.json';
import es from '../../../../public/i18n/es.json';
import { ColumnDraft, IndicatorMetadata } from '../../core/projects/pipeline.service';
import { columnsOf, draftsOf, sameColumns } from './column-names';
import { IndicatorMetadataFields } from './indicator-metadata-fields';

// The Column names section of the indicator's details (add-indicator-column-metadata, design D8), and the helpers behind it.

const base: IndicatorMetadata = { label: 'Teatro hoy', description: '', unit: '', format: 'percent', columns: {} };

/** A row of the form: a label, and the order, values kept last and prefix choice it says nothing of unless given. */
const row = (label: string, extra: Partial<ColumnDraft> = {}): ColumnDraft => ({ label, order: [], last: [], stripPrefix: false, ...extra });

@Component({
  imports: [IndicatorMetadataFields],
  template: `<app-indicator-metadata-fields [value]="value()" [columns]="columns()" [offered]="offered()" [errors]="errors()" [readonly]="readonly()" (valueChange)="value.set($event)" />`,
})
class Host {
  readonly value = signal<IndicatorMetadata>(base);
  readonly columns = signal<string[]>(['variable', 'comuna', 'numerator']);
  readonly offered = signal<Record<string, readonly string[]>>({});
  readonly errors = signal<Record<string, string>>({});
  readonly readonly = signal(false);
}

describe('IndicatorMetadataFields column names', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [Host, TranslocoTestingModule.forRoot({ langs: { en, es }, translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' }, preloadLangs: true })],
    }),
  );

  async function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const inputs = () => Array.from(root.querySelectorAll<HTMLInputElement>('[data-testid="meta-column-label"]'));
    const type = (column: string, text: string) => {
      const input = inputs().find((i) => i.dataset['column'] === column)!;
      input.value = text;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    return { fixture, host: fixture.componentInstance, root, inputs, type };
  }

  it('lists one field for each column, in the order it is given, labelled by the column\'s name', async () => {
    const { root, inputs } = await render();

    expect(root.querySelector('[data-testid="meta-columns"] h3')!.textContent).toBe('Column names');
    expect(root.querySelector('[data-testid="meta-columns"] p')!.textContent).toContain('Empty: the column\'s own name.');
    expect(inputs().map((input) => input.dataset['column'])).toEqual(['variable', 'comuna', 'numerator']);
    const label = root.querySelector(`label[for="${inputs()[1].id}"]`)!;
    expect(label.textContent!.trim()).toBe('comuna');
    expect(inputs().every((input) => input.maxLength === 80)).toBe(true);
  });

  it('shows the saved labels, and emits the value with the label typed for a column', async () => {
    const { fixture, host, type, inputs } = await render();
    host.value.set({ ...base, columns: { comuna: row('Comuna') } });
    fixture.detectChanges();
    expect(inputs().map((input) => input.value)).toEqual(['', 'Comuna', '']);

    type('numerator', 'Hogares que asistieron');

    expect(host.value().columns).toEqual({ comuna: row('Comuna'), numerator: row('Hogares que asistieron') });
    expect(host.value().label).toBe('Teatro hoy');
  });

  it('follows the columns with a saved label that the list lacks, marked as not in the table', async () => {
    const { fixture, host, root, inputs } = await render();

    host.value.set({ ...base, columns: { estrato_hogar: row('Estrato del hogar'), comuna: row('Comuna') } });
    fixture.detectChanges();

    expect(inputs().map((input) => input.dataset['column'])).toEqual(['variable', 'comuna', 'numerator', 'estrato_hogar']);
    const fields = Array.from(root.querySelectorAll('[data-testid="meta-columns"] tmz-form-field'));
    expect(fields.map((field) => field.textContent!.includes('Not in the table'))).toEqual([false, false, false, true]);
  });

  it('does not list a column that has only an empty field, and says the columns come after a test when it has none', async () => {
    const { fixture, host, root, inputs } = await render();

    host.value.set({ ...base, columns: { estrato_hogar: row('  ') } });
    host.columns.set([]);
    fixture.detectChanges();

    expect(inputs()).toHaveLength(0);
    expect(root.querySelector('[data-testid="columns-empty"]')!.textContent).toBe('The columns are listed after a test.');
  });

  it('shows an error on a label next to its field and an error on the labels under the heading', async () => {
    const { fixture, host, root } = await render();

    host.errors.set({ 'columns.comuna.label': 'A column label can have up to 80 characters.', columns: 'An indicator labels at most 100 columns.' });
    fixture.detectChanges();

    const fields = Array.from(root.querySelectorAll('[data-testid="meta-columns"] tmz-form-field'));
    expect(fields.map((field) => field.querySelector('[data-testid="field-error"]')?.textContent ?? null)).toEqual([null, 'A column label can have up to 80 characters.', null]);
    expect(root.querySelector('[data-testid="columns-error"]')!.textContent).toBe('An indicator labels at most 100 columns.');
  });

  it('makes its fields read-only for a version that cannot be edited', async () => {
    const { fixture, host, inputs } = await render();

    host.readonly.set(true);
    fixture.detectChanges();

    expect(inputs().every((input) => input.readOnly)).toBe(true);
  });
});

describe('the rows of the column names', () => {
  it('draws the rows of a saved map from its labels only', () => {
    expect(draftsOf({ comuna: { label: 'Comuna' }, estrato: {}, barrio: { label: ' ' } })).toEqual({ comuna: row('Comuna') });
    expect(draftsOf(null)).toEqual({});
    expect(draftsOf(undefined)).toEqual({});
  });

  it('builds what a save sends: the trimmed, non-empty labels', () => {
    expect(columnsOf({ comuna: row('  Comuna '), estrato: row(''), barrio: row('   ') })).toEqual({ comuna: { label: 'Comuna' } });
    expect(columnsOf({})).toEqual({});
  });

  it('treats a row with an empty field as no row when comparing, and a label trimmed', () => {
    expect(sameColumns({}, { comuna: row('') })).toBe(true);
    expect(sameColumns({ comuna: row('Comuna') }, { comuna: row(' Comuna ') })).toBe(true);
    expect(sameColumns({ comuna: row('Comuna') }, {})).toBe(false);
    expect(sameColumns({ comuna: row('Comuna') }, { comuna: row('Zona') })).toBe(false);
    expect(sameColumns({ comuna: row('Comuna') }, { estrato: row('Comuna') })).toBe(false);
  });

  it('keeps a column called __proto__ as a column', () => {
    const drafts = draftsOf(JSON.parse('{"__proto__":{"label":"Raro"}}'));

    expect(Object.keys(columnsOf(drafts))).toEqual(['__proto__']);
  });
});

describe('the order of the values in the column names (add-declared-category-order, design D9)', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [Host, TranslocoTestingModule.forRoot({ langs: { en, es }, translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' }, preloadLangs: true })],
    }),
  );

  async function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.columns.set(['variable', 'categoria', 'respuesta']);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
    const disclosure = (column: string) => root.querySelector<HTMLElement>(`[data-testid="meta-column-order"][data-column="${column}"]`)!;
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, host: fixture.componentInstance, root, text, disclosure, settle };
  }

  it('gives each column an Order disclosure, closed, that says what is set (Order disclosure)', async () => {
    const { host, fixture, disclosure, text } = await render();
    host.value.set({ ...base, columns: { categoria: row('', { order: ['1. Sí', '2. No'], last: ['4. No sabe'], stripPrefix: true }) } });
    fixture.detectChanges();

    expect(['variable', 'categoria', 'respuesta'].map((c) => disclosure(c).querySelector('details')!.open)).toEqual([false, false, false]);
    expect(text(disclosure('categoria').querySelector('summary'))).toContain('Order');
    expect(text(disclosure('categoria').querySelector('summary'))).toContain('Ordered: 2 · At the end: 1 · Numbers hidden');
    expect(text(disclosure('variable').querySelector('summary'))).toContain('Not set');
  });

  it('shows the order, the values kept at the end and the prefix choice of a column, with the values the test saw on offer', async () => {
    const { host, fixture, root, disclosure } = await render();
    host.value.set({ ...base, columns: { categoria: row('', { order: ['1. Sí'], last: ['4. No sabe'] }) } });
    host.offered.set({ categoria: ['1. Sí', '2. No', '4. No sabe'] });
    fixture.detectChanges();

    const inside = disclosure('categoria');
    const values = (id: string) => Array.from(inside.querySelectorAll(`[data-testid="${id}"] [data-testid="order-value"]`)).map((v) => v.textContent);
    expect(values('order-editor-categoria')).toEqual(['1. Sí']);
    expect(values('last-editor-categoria')).toEqual(['4. No sabe']);
    expect(Array.from(inside.querySelectorAll('datalist option')).map((o) => (o as HTMLOptionElement).value)).toEqual(['2. No', '4. No sabe', '1. Sí', '2. No']);
    expect(root.querySelector<HTMLInputElement>('[data-testid="meta-column-strip"]')).not.toBeNull();
  });

  it('emits the order with the other facts of the column when a value is added or moved, and the box when it is ticked', async () => {
    const { host, fixture, disclosure, settle } = await render();
    host.value.set({ ...base, columns: { categoria: row('Respuesta', { order: ['1. Sí', '2. No'] }) } });
    fixture.detectChanges();

    disclosure('categoria').querySelectorAll<HTMLButtonElement>('[data-testid="order-editor-categoria"] [data-testid="order-down"]')[0].click();
    await settle();
    expect(host.value().columns['categoria']).toEqual(row('Respuesta', { order: ['2. No', '1. Sí'] }));

    const strip = disclosure('categoria').querySelector<HTMLInputElement>('[data-testid="meta-column-strip"]')!;
    strip.checked = true;
    strip.dispatchEvent(new Event('change'));
    await settle();
    expect(host.value().columns['categoria']).toEqual(row('Respuesta', { order: ['2. No', '1. Sí'], stripPrefix: true }));
  });

  it('keeps the order of a column when its label is typed', async () => {
    const { host, fixture, root, settle } = await render();
    host.value.set({ ...base, columns: { categoria: row('', { order: ['1. Sí'] }) } });
    fixture.detectChanges();

    const input = root.querySelector<HTMLInputElement>('[data-testid="meta-column-label"][data-column="categoria"]')!;
    input.value = 'Respuesta';
    input.dispatchEvent(new Event('input'));
    await settle();

    expect(host.value().columns['categoria']).toEqual(row('Respuesta', { order: ['1. Sí'] }));
  });

  it('follows, as not in the table and open, a column that has only an order and that the list lacks (Order of a column the latest test lacks)', async () => {
    const { host, fixture, disclosure } = await render();

    host.value.set({ ...base, columns: { comuna: row('', { last: ['Sin dato'] }) } });
    fixture.detectChanges();

    expect(disclosure('comuna')).not.toBeNull();
    expect(disclosure('comuna').querySelector('details')!.open).toBe(true);
    expect(Array.from(disclosure('comuna').querySelectorAll('[data-testid="last-editor-comuna"] [data-testid="order-value"]')).map((v) => v.textContent)).toEqual(['Sin dato']);
  });

  it('shows a refusal of a value on the row of its column, and opens the disclosure', async () => {
    const { host, fixture, disclosure, text } = await render();
    host.value.set({ ...base, columns: { categoria: row('', { order: ['1. Sí', '1. Sí'] }) } });
    host.errors.set({ 'columns.categoria.order[1]': "'1. Sí' is listed twice.", 'columns.respuesta.last[0]': "'No' is already in the order." });
    fixture.detectChanges();

    expect(text(disclosure('categoria').querySelector('[data-testid="order-error"]'))).toBe("'1. Sí' is listed twice.");
    expect(disclosure('categoria').querySelector('details')!.open).toBe(true);
    expect(text(disclosure('respuesta').querySelector('[data-testid="last-error"]'))).toBe("'No' is already in the order.");
    expect(disclosure('variable').querySelector('[data-testid="order-error"]')).toBeNull();
  });

  it('says that the order applies to every widget of the indicator that has none of its own, and can recolor values', async () => {
    const { disclosure, text } = await render();

    expect(text(disclosure('variable'))).toContain('every widget of this indicator that has no order of its own');
    expect(text(disclosure('variable'))).toContain('other colors');
  });

  it('shows only what is set, without editors, for a version that cannot be edited', async () => {
    const { host, fixture, disclosure } = await render();
    host.value.set({ ...base, columns: { categoria: row('', { order: ['1. Sí'] }) } });
    host.readonly.set(true);
    fixture.detectChanges();

    expect(disclosure('categoria').querySelector('[data-testid="order-editor-categoria"]')).toBeNull();
    expect(disclosure('categoria').querySelector('[data-testid="meta-column-strip"]')).toBeNull();
  });
});

describe('the rows of the order of the column names (add-declared-category-order, design D9)', () => {
  it('draws a row for a column that has only an order, only values kept at the end or only the prefix choice', () => {
    expect(draftsOf({ a: { order: ['x'] }, b: { last: ['y'] }, c: { stripPrefix: true }, d: { order: [], last: [], stripPrefix: false } })).toEqual({
      a: row('', { order: ['x'] }),
      b: row('', { last: ['y'] }),
      c: row('', { stripPrefix: true }),
    });
  });

  it('builds what a save sends: the label, the order, the values kept last and the prefix choice, leaving out what is empty', () => {
    expect(columnsOf({ a: row(' Respuesta ', { order: ['x', 'y'], last: ['z'], stripPrefix: true }), b: row('', { order: ['x'] }), c: row('', { order: [], last: [], stripPrefix: false }) })).toEqual({
      a: { label: 'Respuesta', order: ['x', 'y'], last: ['z'], stripPrefix: true },
      b: { order: ['x'] },
    });
  });

  it('sends a column with only a name no order, and a column with only an order no name', () => {
    expect(columnsOf({ a: row('Respuesta'), b: row('', { order: ['x'] }) })).toEqual({ a: { label: 'Respuesta' }, b: { order: ['x'] } });
  });

  it('counts moving a value as a change, and a row with only empty lists and an unticked box as no row', () => {
    expect(sameColumns({ a: row('', { order: ['x', 'y'] }) }, { a: row('', { order: ['y', 'x'] }) })).toBe(false);
    expect(sameColumns({ a: row('', { order: ['x', 'y'] }) }, { a: row('', { order: ['x', 'y'] }) })).toBe(true);
    expect(sameColumns({ a: row('', { last: ['x'] }) }, { a: row('', { stripPrefix: true }) })).toBe(false);
    expect(sameColumns({}, { a: row('', { order: [], last: [], stripPrefix: false }) })).toBe(true);
  });

  it('reads back what was saved', () => {
    const saved = { a: { label: 'Respuesta', order: ['x', 'y'], last: ['z'], stripPrefix: true as const } };

    expect(columnsOf(draftsOf(saved))).toEqual(saved);
  });
});
