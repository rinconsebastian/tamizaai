import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, ElementRef, LOCALE_ID, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../../core/api-error';
import { DashboardBases, IndicatorsService, basesOf, maxMinN } from '../../../core/projects/indicators.service';
import { Alert, ApiLang, Button, FormField, Input, markApiText, numberText } from '../../../shared/ui';

export interface DashboardBasesData {
  projectId: string;
  dashboardId: string;
  /** The dashboard's name, for the title. */
  name: string;
  /** The settings the page has for the dashboard: no threshold and no n line when it has none. */
  bases: DashboardBases;
}

/** The settings as saved, or `gone` when the dashboard no longer exists. Closing with nothing means nothing was saved. */
export type DashboardBasesResult = DashboardBases | { gone: true };

/**
 * Asks for a dashboard's base settings and saves them (add-small-sample-warnings, design D8), built like the source dialog: a checkbox for
 * the base n line and one number field for the small-base threshold, empty for off. A threshold that is not a whole number from 1 to 10,000
 * (0 included) is refused before anything is sent, the server's refusal shows on the field, and settings equal to the dashboard's send
 * nothing. A dashboard removed meanwhile closes the dialog with `gone`, for the page to catch up.
 */
@Component({
  selector: 'app-dashboard-bases-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, FormField, Input],
  template: `
    <form
      class="w-[min(32rem,calc(100vw-2rem))] rounded-base border border-default bg-neutral-primary-soft p-4 shadow-sm md:p-6"
      novalidate
      (submit)="$event.preventDefault(); submit()"
      *transloco="let t"
      data-testid="dashboard-bases-dialog"
    >
      <h2 id="dashboard-bases-dialog-title" class="break-words text-lg font-medium text-heading [overflow-wrap:anywhere]">{{ t('dashboardBases.title', { name: data.name }) }}</h2>
      <p class="mt-1 text-sm text-body">{{ t('dashboardBases.intro') }}</p>
      <p class="mt-1 text-sm text-body" data-testid="dashboard-bases-where">{{ t('dashboardBases.where') }}</p>

      <label class="mt-4 flex min-h-10 cursor-pointer items-start gap-3 py-1 text-sm">
        <input
          type="checkbox"
          class="mt-0.5 size-4 shrink-0"
          [checked]="showN()"
          (change)="showN.set($any($event.target).checked); serverShowError.set(null)"
          data-testid="dashboard-bases-show"
        />
        <span class="min-w-0 flex-1 break-words text-heading">{{ t('dashboardBases.showN') }}</span>
      </label>
      @if (serverShowError(); as message) {
        <p class="text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="field-error">{{ message }}</p>
      }

      <tmz-form-field class="mt-4" [label]="t('dashboardBases.minN')" fieldId="dashboard-bases-min" [error]="minError()" [hint]="t('dashboardBases.minHint')">
        <input
          #input
          tmzInput
          id="dashboard-bases-min"
          inputmode="numeric"
          autocomplete="off"
          [value]="minN()"
          (input)="minN.set($any($event.target).value); serverMinError.set(null)"
          data-testid="dashboard-bases-min"
        />
      </tmz-form-field>

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="dashboard-bases-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()" data-testid="dashboard-bases-cancel">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" [disabled]="busy()" [attr.aria-busy]="busy()" data-testid="dashboard-bases-save">
          {{ busy() ? t('dashboardBases.saving') : t('dashboardBases.save') }}
        </button>
      </div>
    </form>
  `,
})
export class DashboardBasesDialog {
  protected readonly data = inject<DashboardBasesData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DashboardBasesResult>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  protected readonly showN = signal(this.data.bases.showN);
  /** The threshold as typed: empty is off. */
  protected readonly minN = signal(this.data.bases.minN === null ? '' : String(this.data.bases.minN));
  protected readonly serverMinError = signal<string | null>(null);
  protected readonly serverShowError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  private readonly submitted = signal(false);

  /** The threshold the field means: null for an empty field (off), NaN for a text that is not a whole number from 1 to the largest. */
  private readonly threshold = computed(() => {
    const typed = this.minN().trim();
    if (!typed) {
      return null;
    }
    const value = /^\d{1,5}$/.test(typed) ? Number(typed) : NaN;
    return value >= 1 && value <= maxMinN ? value : NaN;
  });

  /** A threshold out of range is an error once the form is submitted; the server's verdict holds until the text changes. */
  protected readonly minError = computed(() => {
    if (this.submitted() && Number.isNaN(this.threshold())) {
      return this.transloco.translate('dashboardBases.errors.range', { max: numberText(maxMinN, this.locale) });
    }
    return this.serverMinError();
  });

  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

  constructor() {
    afterNextRender(() => this.input().nativeElement.focus());
  }

  protected submit(): void {
    this.submitted.set(true);
    this.serverMinError.set(null);
    this.serverShowError.set(null);
    this.error.set(null);
    if (this.minError() || this.busy()) {
      return;
    }
    const bases: DashboardBases = { minN: this.threshold(), showN: this.showN() };
    if (bases.minN === this.data.bases.minN && bases.showN === this.data.bases.showN) {
      this.ref.close(); // nothing changed: nothing is sent, and the page keeps what it has
      return;
    }
    this.busy.set(true);
    this.service.saveBases(this.data.projectId, this.data.dashboardId, bases).subscribe({
      next: (saved) => this.ref.close(basesOf(saved)),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        const fields = Object.entries(parsed.fieldErrors);
        const named = (name: string) => fields.find(([field]) => field.toLowerCase() === name)?.[1] ?? null;
        const others = fields.filter(([field]) => !['minn', 'shown'].includes(field.toLowerCase())).map(([, message]) => message);
        this.serverMinError.set(named('minn'));
        this.serverShowError.set(named('shown'));
        this.error.set(others.length ? markApiText(others.join(' ')) : this.serverMinError() || this.serverShowError() ? null : parsed.message);
      },
    });
  }
}
