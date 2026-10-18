import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { ToastService, markApiText } from '../shared/ui';

/**
 * An API failure ready to show: a message, the API's error code, field messages keyed by field path and the other members
 * of the Problem Details (for example `retryAfter` and `renewsAt`), for screens that format them.
 */
export interface ApiError {
  status: number;
  code: string | null;
  message: string;
  fieldErrors: Record<string, string>;
  extensions: Record<string, unknown>;
}

interface ProblemBody {
  title?: string;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
  [extension: string]: unknown;
}

/** The members of Problem Details that `ApiError` carries in their own properties or that say nothing a screen could use. */
const standardMembers = new Set(['type', 'title', 'status', 'detail', 'instance', 'code', 'errors', 'traceId']);

@Injectable({ providedIn: 'root' })
export class ApiErrors {
  private readonly transloco = inject(TranslocoService);
  private readonly toasts = inject(ToastService);

  /** Reads Problem Details: field errors go next to fields; a known `code` gets the translated message. */
  parse(error: unknown): ApiError {
    if (!(error instanceof HttpErrorResponse)) {
      return { status: 0, code: null, message: this.t('errors.generic'), fieldErrors: {}, extensions: {} };
    }

    if (error.status === 0) {
      return { status: 0, code: null, message: this.t('errors.network'), fieldErrors: {}, extensions: {} };
    }

    const body: ProblemBody = typeof error.error === 'object' && error.error !== null ? error.error : {};
    const fieldErrors = Object.fromEntries(
      Object.entries(body.errors ?? {})
        .filter(([, messages]) => messages?.length)
        .map(([field, messages]) => [field, markApiText(messages[0])]),
    );
    const code = body.code ?? null;
    const translated = code ? this.translated(`errors.codes.${code}`) : null;
    const message =
      translated ??
      (Object.keys(fieldErrors).length ? this.t('errors.validation') : this.apiMessage(body));
    const extensions = Object.fromEntries(Object.entries(body).filter(([name]) => !standardMembers.has(name)));
    return { status: error.status, code, message, fieldErrors, extensions };
  }

  /** Parses the error and, when no field can show it, raises a toast. */
  report(error: unknown): ApiError {
    const parsed = this.parse(error);
    if (Object.keys(parsed.fieldErrors).length === 0) {
      this.toasts.error(parsed.message);
    }
    return parsed;
  }

  /** The text the API wrote for an error: its detail, else its title; the UI's own generic text when it wrote neither. */
  private apiMessage(body: ProblemBody): string {
    const text = body.detail ?? body.title;
    return text ? markApiText(text) : this.t('errors.generic');
  }

  private t(key: string): string {
    return this.transloco.translate(key);
  }

  private translated(key: string): string | null {
    const text = this.transloco.translate(key);
    return text && text !== key ? text : null;
  }
}
