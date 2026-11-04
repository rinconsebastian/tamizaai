import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, ElementRef, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../../core/api-error';
import { IndicatorsService, maxSourceLength } from '../../../core/projects/indicators.service';
import { Alert, ApiLang, Button, FormField, Input, markApiText } from '../../../shared/ui';

export interface DashboardSourceData {
  projectId: string;
  dashboardId: string;
  /** The dashboard's name, for the title. */
  name: string;
  /** The source the page has for the dashboard; null when it has none. */
  source: string | null;
}

/** The source as saved (null when the dashboard has none now), or `gone` when the dashboard no longer exists. Closing with nothing means nothing was saved. */
export type DashboardSourceResult = { source: string | null } | { gone: true };

/**
 * Asks for a dashboard's source and saves it (add-widget-context, design D7), built like the name dialog: one text field of at most
 * 300 characters, an empty field removes the source, the server's refusal shows on the field, and a source equal to the one the
 * dashboard has sends nothing. A dashboard removed meanwhile closes the dialog with `gone`, for the page to catch up.
 */
@Component({
  selector: 'app-dashboard-source-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, FormField, Input],
  template: `
    <form
      class="w-[min(32rem,calc(100vw-2rem))] rounded-base border border-default bg-neutral-primary-soft p-4 shadow-sm md:p-6"
      novalidate
      (submit)="$event.preventDefault(); submit()"
      *transloco="let t"
      data-testid="dashboard-source-dialog"
    >
      <h2 id="dashboard-source-dialog-title" class="break-words text-lg font-medium text-heading [overflow-wrap:anywhere]">{{ t('dashboardSource.title', { name: data.name }) }}</h2>
      <p class="mt-1 text-sm text-body">{{ t('dashboardSource.intro') }}</p>

      <tmz-form-field class="mt-4" [label]="t('dashboardSource.label')" fieldId="dashboard-source" [error]="sourceError()" [hint]="t('dashboardSource.hint', { max: maxLength })">
        <input
          #input
          tmzInput
          id="dashboard-source"
          autocomplete="off"
          [value]="source()"
          (input)="source.set($any($event.target).value); serverSourceError.set(null)"
          data-testid="dashboard-source-input"
        />
      </tmz-form-field>

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="dashboard-source-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()" data-testid="dashboard-source-cancel">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" [disabled]="busy()" [attr.aria-busy]="busy()" data-testid="dashboard-source-save">
          {{ busy() ? t('dashboardSource.saving') : t('dashboardSource.save') }}
        </button>
      </div>
    </form>
  `,
})
export class DashboardSourceDialog {
  protected readonly data = inject<DashboardSourceData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DashboardSourceResult>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);

  protected readonly maxLength = maxSourceLength;

  protected readonly source = signal(this.data.source ?? '');
  protected readonly serverSourceError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  private readonly submitted = signal(false);

  /** A long source is an error once the form is submitted; the server's verdict holds until the text changes. */
  protected readonly sourceError = computed(() => {
    if (this.submitted() && this.source().trim().length > maxSourceLength) {
      return this.transloco.translate('dashboardSource.errors.long', { max: maxSourceLength });
    }
    return this.serverSourceError();
  });

  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

  constructor() {
    afterNextRender(() => this.input().nativeElement.focus());
  }

  protected submit(): void {
    this.submitted.set(true);
    this.serverSourceError.set(null);
    this.error.set(null);
    if (this.sourceError() || this.busy()) {
      return;
    }
    // An empty text removes the source; the API stores it trimmed, and so is compared.
    const source = this.source().trim() || null;
    if (source === this.data.source) {
      this.ref.close(); // nothing changed: nothing is sent, and the page keeps what it has
      return;
    }
    this.busy.set(true);
    this.service.saveSource(this.data.projectId, this.data.dashboardId, source).subscribe({
      next: (saved) => this.ref.close({ source: saved.source ?? null }),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        const fields = Object.entries(parsed.fieldErrors);
        const own = fields.find(([field]) => field.toLowerCase() === 'source');
        const others = fields.filter(([field]) => field.toLowerCase() !== 'source').map(([, message]) => message);
        this.serverSourceError.set(own?.[1] ?? null);
        this.error.set(others.length ? markApiText(others.join(' ')) : own ? null : parsed.message);
      },
    });
  }
}
