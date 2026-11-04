import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../../../public/i18n/en.json';
import es from '../../../../public/i18n/es.json';
import { IndicatorMetadata } from '../../core/projects/pipeline.service';
import { columnsOf, draftsOf, sameColumns } from './column-names';
import { IndicatorMetadataFields } from './indicator-metadata-fields';

// The Column names section of the indicator's details (add-indicator-column-metadata, design D8), and the helpers behind it.

const base: IndicatorMetadata = { label: 'Teatro hoy', description: '', unit: '', format: 'percent', columns: {} };

@Component({
  imports: [IndicatorMetadataFields],
  template: `<app-indicator-metadata-fields [value]="value()" [columns]="columns()" [errors]="errors()" [readonly]="readonly()" (valueChange)="value.set($event)" />`,
})
class Host {
  readonly value = signal<IndicatorMetadata>(base);
  readonly columns = signal<string[]>(['variable', 'comuna', 'numerator']);
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
    host.value.set({ ...base, columns: { comuna: { label: 'Comuna' } } });
    fixture.detectChanges();
    expect(inputs().map((input) => input.value)).toEqual(['', 'Comuna', '']);

    type('numerator', 'Hogares que asistieron');

    expect(host.value().columns).toEqual({ comuna: { label: 'Comuna' }, numerator: { label: 'Hogares que asistieron' } });
    expect(host.value().label).toBe('Teatro hoy');
  });

  it('follows the columns with a saved label that the list lacks, marked as not in the table', async () => {
    const { fixture, host, root, inputs } = await render();

    host.value.set({ ...base, columns: { estrato_hogar: { label: 'Estrato del hogar' }, comuna: { label: 'Comuna' } } });
    fixture.detectChanges();

    expect(inputs().map((input) => input.dataset['column'])).toEqual(['variable', 'comuna', 'numerator', 'estrato_hogar']);
    const fields = Array.from(root.querySelectorAll('[data-testid="meta-columns"] tmz-form-field'));
    expect(fields.map((field) => field.textContent!.includes('Not in the table'))).toEqual([false, false, false, true]);
  });

  it('does not list a column that has only an empty field, and says the columns come after a test when it has none', async () => {
    const { fixture, host, root, inputs } = await render();

    host.value.set({ ...base, columns: { estrato_hogar: { label: '  ' } } });
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
    expect(draftsOf({ comuna: { label: 'Comuna' }, estrato: {}, barrio: { label: ' ' } })).toEqual({ comuna: { label: 'Comuna' } });
    expect(draftsOf(null)).toEqual({});
    expect(draftsOf(undefined)).toEqual({});
  });

  it('builds what a save sends: the trimmed, non-empty labels', () => {
    expect(columnsOf({ comuna: { label: '  Comuna ' }, estrato: { label: '' }, barrio: { label: '   ' } })).toEqual({ comuna: { label: 'Comuna' } });
    expect(columnsOf({})).toEqual({});
  });

  it('treats a row with an empty field as no row when comparing, and a label trimmed', () => {
    expect(sameColumns({}, { comuna: { label: '' } })).toBe(true);
    expect(sameColumns({ comuna: { label: 'Comuna' } }, { comuna: { label: ' Comuna ' } })).toBe(true);
    expect(sameColumns({ comuna: { label: 'Comuna' } }, {})).toBe(false);
    expect(sameColumns({ comuna: { label: 'Comuna' } }, { comuna: { label: 'Zona' } })).toBe(false);
    expect(sameColumns({ comuna: { label: 'Comuna' } }, { estrato: { label: 'Comuna' } })).toBe(false);
  });

  it('keeps a column called __proto__ as a column', () => {
    const drafts = draftsOf(JSON.parse('{"__proto__":{"label":"Raro"}}'));

    expect(Object.keys(columnsOf(drafts))).toEqual(['__proto__']);
  });
});
