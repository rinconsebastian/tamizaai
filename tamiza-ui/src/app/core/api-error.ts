import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { ToastService } from '../shared/ui';

/** An API failure ready to show: a message, the API's error code and field messages keyed by field path. */
export interface ApiError {
  status: number;
  code: string | null;
  message: string;
  fieldErrors: Record<string, string>;
}

interface ProblemBody {
  title?: string;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

@Injectable({ providedIn: 'root' })
export class ApiErrors {
  private readonly transloco = inject(TranslocoService);
  private readonly toasts = inject(ToastService);

  /** Reads Problem Details: field errors go next to fields; a known `code` gets the translated message. */
  parse(error: unknown): ApiError {
    if (!(error instanceof HttpErrorResponse)) {
      return { status: 0, code: null, message: this.t('errors.generic'), fieldErrors: {} };
    }

    if (error.status === 0) {
      return { status: 0, code: null, message: this.t('errors.network'), fieldErrors: {} };
    }

    const body: ProblemBody = typeof error.error === 'object' && error.error !== null ? error.error : {};
    const fieldErrors = Object.fromEntries(
      Object.entries(body.errors ?? {})
        .filter(([, messages]) => messages?.length)
        .map(([field, messages]) => [field, messages[0]]),
    );
    const code = body.code ?? null;
    const translated = code ? this.translated(`errors.codes.${code}`) : null;
    const message =
      translated ??
      (Object.keys(fieldErrors).length ? this.t('errors.validation') : (body.detail ?? body.title ?? this.t('errors.generic')));
    return { status: error.status, code, message, fieldErrors };
  }

  /** Parses the error and, when no field can show it, raises a toast. */
  report(error: unknown): ApiError {
    const parsed = this.parse(error);
    if (Object.keys(parsed.fieldErrors).length === 0) {
      this.toasts.error(parsed.message);
    }
    return parsed;
  }

  private t(key: string): string {
    return this.transloco.translate(key);
  }

  private translated(key: string): string | null {
    const text = this.transloco.translate(key);
    return text && text !== key ? text : null;
  }
}
