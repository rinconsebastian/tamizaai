import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../../testing/transloco-testing';
import { ToastService } from '../shared/ui';
import { ApiErrors } from './api-error';

describe('ApiErrors', () => {
  let errors: ApiErrors;
  let toasts: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [translocoTesting()] });
    errors = TestBed.inject(ApiErrors);
    toasts = TestBed.inject(ToastService);
  });

  const response = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

  it('maps ValidationProblemDetails to the first message per field', () => {
    const parsed = errors.parse(
      response(400, { title: 'One or more validation errors occurred.', errors: { name: ['Enter a name.'], 'dimensions[1].field': ['Pick a field.', 'x'] } }),
    );

    expect(parsed.fieldErrors).toEqual({ name: 'Enter a name.', 'dimensions[1].field': 'Pick a field.' });
    expect(parsed.message).toBe('Some fields need attention.');
  });

  it('translates a 409 by its code', () => {
    const parsed = errors.parse(response(409, { detail: 'grace@example.org is already a member of this project.', code: 'member.exists' }));

    expect(parsed.code).toBe('member.exists');
    expect(parsed.message).toBe('This person is already a member of the project.');
  });

  it('translates a 422 Kobo error by its code', () => {
    const parsed = errors.parse(response(422, { detail: 'raw', code: 'kobo.unauthorized' }));
    expect(parsed.message).toContain('Kobo rejected the API token');
  });

  it('falls back to the detail for an unknown code', () => {
    expect(errors.parse(response(409, { detail: 'Something specific.', code: 'new.code' })).message).toBe('Something specific.');
  });

  it('reports network errors with a dedicated message', () => {
    expect(errors.parse(response(0, null)).message).toContain('could not reach the server');
  });

  it('raises a toast only when no field can show the error', () => {
    errors.report(response(400, { errors: { name: ['Enter a name.'] } }));
    expect(toasts.toasts()).toHaveLength(0);

    errors.report(response(409, { code: 'project.last_admin' }));
    expect(toasts.toasts()).toEqual([expect.objectContaining({ tone: 'danger', message: expect.stringContaining('at least one admin') })]);
  });
});
