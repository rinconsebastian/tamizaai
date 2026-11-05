import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../../../public/i18n/en.json';
import es from '../../../../public/i18n/es.json';
import { ColumnDraft, IndicatorMetadata } from '../../core/projects/pipeline.service';
import { columnsOf, draftsOf, sameColumns } from './column-names';
import { IndicatorMetadataFields } from './indicator-metadata-fields';

// The Column names section of the indicator's details (add-indicator-column-metadata, design D8), and the helpers behind it.

const base: IndicatorMetadata = { label: 'Teatro hoy', description: '', unit: '', format: 'percent', columns: {} };

/** A row of the form: a label, and the order, values kept last and prefix choice it says nothing of unless given. */
const row = (label: string, extra: Partial<ColumnDraft> = {}): ColumnDraft => ({ label, order: [], last: [], stripPrefix: false, base: false, missing: [], categories: '', ...extra });

@Component({
  imports: [IndicatorMetadataFields],
  template: `<app-indicator-metadata-fields [value]="value()" [columns]="columns()" [numeric]="numeric()" [offered]="offered()" [errors]="errors()" [readonly]="readonly()" (valueChange)="value.set($event)" />`,
})
class Host {
  readonly value = signal<IndicatorMetadata>(base);
  readonly columns = signal<string[]>(['variable', 'comuna', 'numerator']);
  readonly numeric = signal<string[]>([]);
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

describe('the rows of the base facts of the column names (add-small-sample-warnings, design D1)', () => {
  it('keeps a row that has only a base, only values that mean no data or only a kind of answers', () => {
    expect(draftsOf({ hogares: { base: true }, comuna: { missing: ['Sin dato'] }, categoria: { categories: 'multiple' }, otra: { base: false, missing: [] } })).toEqual({
      hogares: row('', { base: true }),
      comuna: row('', { missing: ['Sin dato'] }),
      categoria: row('', { categories: 'multiple' }),
    });
  });

  it('sends the base facts of a row with the label, leaving out the ones that are not set', () => {
    expect(
      columnsOf({
        hogares: row('Hogares', { base: true }),
        comuna: row('', { missing: ['Sin dato', 'No sabe'] }),
        categoria: row('Respuesta', { categories: 'single' }),
        vacia: row('', { base: false, missing: [], categories: '' }),
      }),
    ).toEqual({
      hogares: { label: 'Hogares', base: true },
      comuna: { missing: ['Sin dato', 'No sabe'] },
      categoria: { label: 'Respuesta', categories: 'single' },
    });
  });

  it('counts each base fact as a change, and a row with none as no row', () => {
    expect(sameColumns({ a: row('', { base: true }) }, { a: row('') })).toBe(false);
    expect(sameColumns({ a: row('', { missing: ['x'] }) }, { a: row('', { missing: ['x', 'y'] }) })).toBe(false);
    expect(sameColumns({ a: row('', { categories: 'single' }) }, { a: row('', { categories: 'multiple' }) })).toBe(false);
    expect(sameColumns({ a: row('', { missing: ['x'] }) }, { a: row('', { missing: ['x'] }) })).toBe(true);
    expect(sameColumns({}, { a: row('', { base: false, missing: [], categories: '' }) })).toBe(true);
  });

  it('reads back what was saved, and ignores a kind of answers it does not know', () => {
    const saved = { hogares: { label: 'Hogares', base: true as const }, comuna: { missing: ['Sin dato'] }, categoria: { categories: 'multiple' as const } };

    expect(columnsOf(draftsOf(saved))).toEqual(saved);
    expect(draftsOf({ a: { label: 'x', categories: 'ranking' as never } })['a'].categories).toBe('');
  });
});

describe('the base facts in the column names (add-small-sample-warnings, design D1)', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [Host, TranslocoTestingModule.forRoot({ langs: { en, es }, translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' }, preloadLangs: true })],
    }),
  );

  async function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.columns.set(['variable', 'categoria', 'comuna', 'numerator', 'denominator', 'value']);
    fixture.componentInstance.numeric.set(['numerator', 'denominator', 'value']);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
    const facts = (column: string) => root.querySelector<HTMLElement>(`[data-testid="meta-column-facts"][data-column="${column}"]`);
    const baseOptions = () => Array.from(root.querySelectorAll<HTMLInputElement>('[data-testid="meta-base-option"]'));
    const none = () => root.querySelector<HTMLInputElement>('[data-testid="meta-base-none"]')!;
    const chips = (column: string) => Array.from(facts(column)!.querySelectorAll('[data-testid="chip-value"]')).map((chip) => text(chip));
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const choose = async (input: HTMLInputElement) => {
      input.checked = true;
      input.dispatchEvent(new Event('change'));
      await settle();
    };
    const addChip = async (column: string, value: string, how: 'button' | 'enter' = 'button') => {
      const field = facts(column)!.querySelector<HTMLInputElement>('[data-testid="chip-new"]')!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
      await settle();
      if (how === 'enter') {
        field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      } else {
        facts(column)!.querySelector<HTMLButtonElement>('[data-testid="chip-add"]')!.click();
      }
      await settle();
    };
    return { fixture, host: fixture.componentInstance, root, text, facts, baseOptions, none, chips, settle, choose, addChip };
  }

  it('offers the numeric columns as the base, None first, and the no-data values and answers for the others (Save form)', async () => {
    const { root, baseOptions, none, text, facts } = await render();

    expect(text(root.querySelector('[data-testid="meta-base"] legend'))).toBe('Base column');
    expect(baseOptions().map((option) => option.dataset['column'])).toEqual(['numerator', 'denominator', 'value']);
    expect(none().checked).toBe(true);
    expect(root.querySelector('[data-testid="meta-base"]')!.compareDocumentPosition(root.querySelector('[data-testid="meta-column-label"]')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    for (const column of ['variable', 'categoria', 'comuna']) {
      expect(facts(column)).not.toBeNull();
      expect(text(facts(column)!.querySelector('summary'))).toContain('Base and answers');
      expect(facts(column)!.querySelector('[data-testid="chip-new"]')).not.toBeNull();
      expect(facts(column)!.querySelector<HTMLSelectElement>('[data-testid="meta-column-answers"]')!.value).toBe('');
    }
    for (const column of ['numerator', 'denominator', 'value']) {
      expect(facts(column)).toBeNull();
    }
    expect(text(facts('comuna')!.querySelector('summary'))).toContain('Not set');
  });

  it('offers no base group while the table has no numeric column, and every column the other two facts', async () => {
    const { fixture, host, root, facts } = await render();

    host.numeric.set([]);
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="meta-base"]')).toBeNull();
    expect(facts('numerator')).not.toBeNull();
  });

  it('says which columns to declare as answers, and never a column that splits the households', async () => {
    const { text, facts } = await render();

    const help = text(facts('categoria')!);
    expect(help).toContain('variable, orden and categoria');
    expect(help).toContain('Never for a column that splits the households, such as comuna or estrato');
    expect(help).toContain('multiple choice');
  });

  it('declares one column as the base, takes it from the other, and leaves None to clear it', async () => {
    const { host, fixture, baseOptions, none, choose, settle } = await render();
    host.value.set({ ...base, columns: { numerator: row('', { base: true }), comuna: row('Comuna') } });
    fixture.detectChanges();
    expect(baseOptions().map((option) => option.checked)).toEqual([true, false, false]);

    await choose(baseOptions()[2]);
    expect(Object.entries(host.value().columns).filter(([, draft]) => draft.base).map(([column]) => column)).toEqual(['value']);
    expect(host.value().columns['comuna']).toEqual(row('Comuna'));

    await choose(none());
    await settle();
    expect(Object.values(host.value().columns).some((draft) => draft.base)).toBe(false);
  });

  it('gives up the answers and the no-data values of a column made the base, which the API refuses together', async () => {
    const { host, fixture, baseOptions, choose } = await render();
    host.value.set({ ...base, columns: { value: row('Hogares', { missing: ['x'], categories: 'single' }) } });
    fixture.detectChanges();

    await choose(baseOptions()[2]);

    expect(host.value().columns['value']).toEqual(row('Hogares', { base: true }));
  });

  it('adds a value that means no data with the button or Enter, and emits the column with its other facts', async () => {
    const { host, fixture, addChip, chips } = await render();
    host.value.set({ ...base, columns: { comuna: row('Comuna') } });
    fixture.detectChanges();

    await addChip('comuna', 'Sin dato');
    await addChip('comuna', 'No sabe', 'enter');

    expect(chips('comuna')).toEqual(['Sin dato', 'No sabe']);
    expect(host.value().columns['comuna']).toEqual(row('Comuna', { missing: ['Sin dato', 'No sabe'] }));
  });

  it('keeps a value as typed, spaces included, and ignores an empty value and one already listed', async () => {
    const { host, addChip, chips } = await render();

    await addChip('comuna', ' Sin dato ');
    await addChip('comuna', ' Sin dato ');
    await addChip('comuna', '');

    expect(chips('comuna')).toEqual(['Sin dato']);
    expect(host.value().columns['comuna'].missing).toEqual([' Sin dato ']);
  });

  it('limits a column to 20 values and says so', async () => {
    const { host, fixture, facts, text, addChip } = await render();
    host.value.set({ ...base, columns: { comuna: row('', { missing: Array.from({ length: 19 }, (_, i) => `Valor ${i + 1}`) }) } });
    fixture.detectChanges();
    expect(facts('comuna')!.querySelector('[data-testid="chips-full"]')).toBeNull();

    await addChip('comuna', 'Valor 20');
    expect(host.value().columns['comuna'].missing).toHaveLength(20);
    expect(text(facts('comuna')!.querySelector('[data-testid="chips-full"]'))).toBe('At most 20 values.');
    expect(facts('comuna')!.querySelector<HTMLButtonElement>('[data-testid="chip-add"]')!.disabled).toBe(true);

    await addChip('comuna', 'Valor 21', 'enter');
    expect(host.value().columns['comuna'].missing).toHaveLength(20);
  });

  it('removes a value with its button, named by the value, and the column keeps no row when nothing is left', async () => {
    const { host, fixture, facts, settle } = await render();
    host.value.set({ ...base, columns: { comuna: row('', { missing: ['Sin dato', 'No sabe'] }) } });
    fixture.detectChanges();
    const remove = facts('comuna')!.querySelectorAll<HTMLButtonElement>('[data-testid="chip-remove"]');
    expect(Array.from(remove).map((button) => button.getAttribute('aria-label'))).toEqual(['Remove Sin dato', 'Remove No sabe']);

    remove[0].click();
    await settle();

    expect(host.value().columns['comuna'].missing).toEqual(['No sabe']);
  });

  it('chooses single or multiple choice for a column, and none again', async () => {
    const { host, facts, settle } = await render();
    const select = () => facts('categoria')!.querySelector<HTMLSelectElement>('[data-testid="meta-column-answers"]')!;
    expect(Array.from(select().options).map((option) => option.textContent!.trim())).toEqual(['No', 'Yes, single choice', 'Yes, multiple choice']);

    select().value = 'multiple';
    select().dispatchEvent(new Event('change'));
    await settle();
    expect(host.value().columns['categoria']).toEqual(row('', { categories: 'multiple' }));

    select().value = '';
    select().dispatchEvent(new Event('change'));
    await settle();
    expect(host.value().columns['categoria'].categories).toBe('');
  });

  it('says in the closed disclosure what is set', async () => {
    const { host, fixture, facts, text } = await render();
    host.value.set({ ...base, columns: { comuna: row('', { missing: ['Sin dato', 'No sabe'] }), categoria: row('', { categories: 'multiple' }) } });
    fixture.detectChanges();

    expect(text(facts('comuna')!.querySelector('summary'))).toContain('No data: 2');
    expect(text(facts('categoria')!.querySelector('summary'))).toContain('Answers: multiple choice');
    expect(facts('comuna')!.querySelector('details')!.open).toBe(false);
  });

  it('shows the interface in Spanish', async () => {
    TestBed.inject(TranslocoService).setActiveLang('es');
    const { text, facts, root } = await render();

    expect(text(root.querySelector('[data-testid="meta-base"] legend'))).toBe('Columna de base');
    expect(text(facts('comuna')!.querySelector('summary'))).toContain('Base y respuestas');
  });

  it('follows a declared base the table lacks, marked as not in the table, and clears it with None (a column no longer in the table)', async () => {
    const { host, fixture, baseOptions, none, text, root, choose } = await render();
    host.value.set({ ...base, columns: { hogares: row('', { base: true }) } });
    fixture.detectChanges();

    expect(baseOptions().map((option) => option.dataset['column'])).toEqual(['numerator', 'denominator', 'value', 'hogares']);
    expect(text(baseOptions()[3].closest('label'))).toBe('hogares (Not in the table)');
    expect(baseOptions()[3].checked).toBe(true);
    expect(root.querySelectorAll('[data-testid="meta-column-label"]')).toHaveLength(7);

    await choose(none());

    expect(Object.values(host.value().columns).some((draft) => draft.base)).toBe(false);
    expect(baseOptions().map((option) => option.dataset['column'])).toEqual(['numerator', 'denominator', 'value']);
  });

  it('follows a column with no-data values that the table lacks, open, with a way to clear them', async () => {
    const { host, fixture, facts, chips, settle } = await render();
    host.value.set({ ...base, columns: { estrato_hogar: row('', { missing: ['Sin dato'] }) } });
    fixture.detectChanges();

    expect(facts('estrato_hogar')!.querySelector('details')!.open).toBe(true);
    expect(chips('estrato_hogar')).toEqual(['Sin dato']);
    facts('estrato_hogar')!.querySelector<HTMLButtonElement>('[data-testid="chip-remove"]')!.click();
    await settle();
    expect(host.value().columns['estrato_hogar'].missing).toEqual([]);
  });

  describe('a refusal of the API', () => {
    it('shows an error of the values that mean no data on the row of its column, and opens it', async () => {
      const { host, fixture, facts, text } = await render();
      host.value.set({ ...base, columns: { comuna: row('', { missing: ['Sin dato', 'Sin dato'] }) } });
      host.errors.set({ 'columns.comuna.missing[1]': "'Sin dato' is listed twice.", 'columns.categoria.missing': 'A column lists at most 20 values that mean no data.' });
      fixture.detectChanges();

      expect(text(facts('comuna')!.querySelector('[data-testid="missing-error"]'))).toBe("'Sin dato' is listed twice.");
      expect(facts('comuna')!.querySelector('details')!.open).toBe(true);
      expect(text(facts('categoria')!.querySelector('[data-testid="missing-error"]'))).toBe('A column lists at most 20 values that mean no data.');
      expect(facts('variable')!.querySelector('[data-testid="missing-error"]')).toBeNull();
    });

    it('shows an error of the kind of answers under its select', async () => {
      const { host, fixture, facts, text } = await render();
      host.errors.set({ 'columns.categoria.categories': "Answers are 'single' or 'multiple'." });
      fixture.detectChanges();

      expect(text(facts('categoria')!.querySelector('[data-testid="answers-error"]'))).toBe("Answers are 'single' or 'multiple'.");
      expect(facts('categoria')!.querySelector('details')!.open).toBe(true);
    });

    it('shows the error of a second base under the base group', async () => {
      const { host, fixture, root, text } = await render();
      host.errors.set({ 'columns.denominator.base': 'Only one column can be the base: numerator already is.' });
      fixture.detectChanges();

      expect(text(root.querySelector('[data-testid="meta-base"] [data-testid="base-error"]'))).toBe('Only one column can be the base: numerator already is.');
      expect(root.querySelector('[data-testid="meta-base"]')!.getAttribute('aria-describedby')).toBe('meta-base-error');
    });

    it('shows an error of a column as a whole next to its row', async () => {
      const { host, fixture, root, text } = await render();
      host.errors.set({ 'columns.value': 'A base column counts units: it holds no answers and no values that mean no data.' });
      fixture.detectChanges();

      expect(text(root.querySelector('[data-testid="column-error"]'))).toBe('A base column counts units: it holds no answers and no values that mean no data.');
    });
  });

  it('shows what is set without editors, and disables the base choice, for a version that cannot be edited', async () => {
    const { host, fixture, facts, baseOptions, none, root } = await render();
    host.value.set({ ...base, columns: { comuna: row('', { missing: ['Sin dato'] }), numerator: row('', { base: true }) } });
    host.readonly.set(true);
    fixture.detectChanges();

    expect(facts('comuna')!.querySelector('[data-testid="chip-new"]')).toBeNull();
    expect(facts('comuna')!.querySelector('[data-testid="chip-remove"]')).toBeNull();
    expect(facts('comuna')!.querySelector('[data-testid="meta-column-answers"]')).toBeNull();
    expect(root.querySelector('[data-testid="chip-add"]')).toBeNull();
    expect(baseOptions().every((option) => option.disabled)).toBe(true);
    expect(none().disabled).toBe(true);
  });

  it('is reached with the keyboard: radios, chips and the select are native controls in the reading order', async () => {
    const { facts, baseOptions, none } = await render();

    for (const control of [none(), ...baseOptions(), facts('comuna')!.querySelector<HTMLElement>('[data-testid="chip-new"]')!, facts('comuna')!.querySelector<HTMLElement>('[data-testid="meta-column-answers"]')!]) {
      expect(control.tabIndex).toBe(0);
    }
    expect(new Set([none(), ...baseOptions()].map((radio) => radio.name)).size).toBe(1);
  });
});
