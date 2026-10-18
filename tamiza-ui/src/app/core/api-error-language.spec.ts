import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import es from '../../../public/i18n/es.json';
import { localeTesting, translocoTesting } from '../../testing/transloco-testing';
import { FormField, Input, ToastRegion } from '../shared/ui';
import { ApiErrors } from './api-error';

const response = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

@Component({
  imports: [FormField, Input],
  template: `
    <tmz-form-field label="Name" fieldId="name" [error]="error">
      <input tmzInput id="name" />
    </tmz-form-field>
  `,
})
class Field {
  error = '';
}

describe('API errors in the Spanish UI', () => {
  let errors: ApiErrors;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [translocoTesting('es')], providers: [localeTesting('es-CO')] });
    errors = TestBed.inject(ApiErrors);
  });

  function toast() {
    const fixture = TestBed.createComponent(ToastRegion);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('[data-testid="toast"] p');
  }

  function fieldError(message: string) {
    const fixture = TestBed.createComponent(Field);
    fixture.componentInstance.error = message;
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('[data-testid="field-error"]');
  }

  it('says in the interface language that the server cannot be reached, without marking it English', () => {
    const parsed = errors.report(response(0, null));

    expect(parsed.message).toBe(es.errors.network);
    expect(toast()?.textContent).toBe(es.errors.network);
    expect(toast()?.hasAttribute('lang')).toBe(false);
  });

  it('shows the detail of an error without a code as the API wrote it, marked English', () => {
    const parsed = errors.report(response(404, { title: 'Not Found', detail: 'Widget not found.' }));

    expect(parsed.message).toBe('Widget not found.');
    expect(toast()?.textContent).toBe('Widget not found.');
    expect(toast()?.getAttribute('lang')).toBe('en');
  });

  it('translates an error whose code the UI knows, without marking it English', () => {
    const parsed = errors.report(response(422, { detail: 'Kobo rejected the token.', code: 'kobo.unauthorized' }));

    expect(parsed.message).toBe(es.errors.codes.kobo.unauthorized);
    expect(toast()?.textContent).toBe(es.errors.codes.kobo.unauthorized);
    expect(toast()?.hasAttribute('lang')).toBe(false);
  });

  it('keeps a field message in English next to its field, marked English, under a summary in the interface language', () => {
    const parsed = errors.parse(response(400, { errors: { name: ['Enter a name of 1 to 200 characters.'] } }));

    expect(parsed.message).toBe(es.errors.validation);
    expect(fieldError(parsed.fieldErrors['name'])?.textContent).toBe('Enter a name of 1 to 200 characters.');
    expect(fieldError(parsed.fieldErrors['name'])?.getAttribute('lang')).toBe('en');
  });

  it('does not mark a field message the UI wrote itself', () => {
    expect(fieldError('Escribe un nombre.')?.hasAttribute('lang')).toBe(false);
  });
});
