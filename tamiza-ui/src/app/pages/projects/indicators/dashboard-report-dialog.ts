import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, ElementRef, Injector, LOCALE_ID, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../../core/api-error';
import {
  DashboardReport,
  DashboardReportFailure,
  DashboardReportSettings,
  IndicatorsService,
  maxReportRecipients,
} from '../../../core/projects/indicators.service';
import { DashboardEntry } from '../../../core/projects/projects.service';
import { Alert, ApiLang, Button, ConfirmDialog, FormField, Input, dateText, markApiText, weekdayText } from '../../../shared/ui';

export interface DashboardReportData {
  projectId: string;
  dashboard: DashboardEntry;
}

/** The email as saved, `removed` once it is removed, or `gone` when the dashboard no longer exists. */
export type DashboardReportResult = { saved: DashboardReport } | { removed: true } | { gone: true };

/** The fields an error can sit next to; any other field error (`locale`) is the dialog's error. */
type Field = 'recipients' | 'days' | 'time';

/** The ISO weekdays, Monday first. */
const isoWeek = [1, 2, 3, 4, 5, 6, 7];

/**
 * What a new email starts with: every day at 07:00, active, so a member who only types the recipients gets a daily morning email.
 * Any day can be turned off before saving.
 */
const newEmail = { days: isoWeek, time: '07:00', active: true };

/** The addresses typed: one per line, or separated by commas or semicolons, each trimmed, the empty ones dropped. The API checks them. */
export function parseRecipients(text: string): string[] {
  return text
    .split(/[\r\n,;]+/)
    .map((address) => address.trim())
    .filter((address) => address.length > 0);
}

/**
 * A dashboard's scheduled email (dashboard reports, design D14): its recipients, weekdays, time and whether it is active, saved
 * at once, with the results of its last sends. It can also be removed (after a confirmation) or sent now. Errors the API puts on
 * a field show next to it. Full screen on phones, a panel from `md` up. A dashboard removed meanwhile closes it with `gone`.
 */
@Component({
  selector: 'app-dashboard-report-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, FormField, Input],
  template: `
    <!-- Long addresses, in the API's errors for instance, wrap rather than widen the dialog on a phone. -->
    <form
      class="flex h-dvh w-screen flex-col overflow-y-auto bg-neutral-primary-soft p-4 [overflow-wrap:anywhere] md:h-auto md:max-h-[calc(100dvh-2rem)] md:w-[min(40rem,calc(100vw-2rem))] md:rounded-base md:border md:border-default md:p-6 md:shadow-sm"
      novalidate
      (submit)="$event.preventDefault(); save()"
      *transloco="let t"
      data-testid="dashboard-report-dialog"
    >
      <h2 id="dashboard-report-dialog-title" class="text-lg font-medium text-heading">
        {{ t('dashboards.report.title', { name: data.dashboard.name }) }}
      </h2>
      <p class="mt-1 text-sm text-body">{{ t('dashboards.report.intro') }}</p>

      @if (loadError(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="dashboard-report-load-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      } @else if (report(); as report) {
        <tmz-alert tone="warning" class="mt-4" data-testid="dashboard-report-warning">
          <p>{{ t('dashboards.report.warning') }}</p>
          <p class="mt-2" data-testid="dashboard-report-filters-note">{{ t('dashboards.report.filtersNote') }}</p>
        </tmz-alert>

        @if (report.settings) {
          <dl class="mt-4 grid gap-x-3 gap-y-1 text-sm sm:grid-cols-[auto_1fr]" data-testid="dashboard-report-status">
            <dt class="text-body">{{ t('dashboards.report.lastSent') }}</dt>
            <dd class="text-heading" data-testid="dashboard-report-last-sent">{{ report.lastSentAt ? when(report.lastSentAt) : t('dashboards.report.neverSent') }}</dd>
            @if (report.updatedAt) {
              <dt class="text-body">{{ t('dashboards.report.lastSaved') }}</dt>
              <dd class="text-heading" data-testid="dashboard-report-last-saved">
                {{ report.updatedBy ? t('dashboards.report.savedBy', { time: when(report.updatedAt), name: report.updatedBy.name }) : when(report.updatedAt) }}
              </dd>
            }
          </dl>
          @if (report.lastFailure; as failure) {
            <!-- What the last send left, there when the dialog opens: shown, not announced. -->
            <tmz-alert tone="warning" class="mt-3" data-testid="dashboard-report-failure">
              {{ t('dashboards.report.lastFailure', { time: when(failure.at), reason: reason(failure) }) }}
            </tmz-alert>
          }
        }

        <tmz-form-field
          class="mt-4"
          [label]="t('dashboards.report.recipients')"
          fieldId="report-recipients"
          [error]="fieldErrors().recipients"
          [hint]="t('dashboards.report.recipientsHint', { max: maxRecipients })"
        >
          <textarea
            #recipientsInput
            tmzInput
            id="report-recipients"
            rows="4"
            autocomplete="off"
            spellcheck="false"
            [value]="recipients()"
            (input)="setRecipients($any($event.target).value)"
            data-testid="dashboard-report-recipients"
          ></textarea>
        </tmz-form-field>

        <fieldset class="mt-4 min-w-0" [attr.aria-describedby]="fieldErrors().days ? 'report-days-error' : null" data-testid="dashboard-report-days">
          <legend class="mb-2 text-sm font-medium text-heading">{{ t('dashboards.report.days') }}</legend>
          <div class="flex flex-wrap gap-2">
            @for (day of weekdays; track day.day) {
              <!-- Each says its full name, and whether it is chosen. -->
              <button
                tmzButton
                size="sm"
                type="button"
                class="min-w-14"
                [variant]="days().includes(day.day) ? 'primary' : 'secondary'"
                [attr.aria-pressed]="days().includes(day.day)"
                (click)="toggleDay(day.day)"
                [attr.data-day]="day.day"
                data-testid="dashboard-report-day"
              >
                <span aria-hidden="true">{{ day.short }}</span><span class="sr-only">{{ day.long }}</span>
              </button>
            }
          </div>
          @if (fieldErrors().days; as message) {
            <p id="report-days-error" class="mt-2 text-sm text-fg-danger-strong" data-testid="field-error" [tmzApiLang]="message">{{ message }}</p>
          }
        </fieldset>

        <div class="mt-4 grid gap-4 sm:grid-cols-2">
          <tmz-form-field [label]="t('dashboards.report.time')" fieldId="report-time" [error]="fieldErrors().time" [hint]="t('dashboards.report.timeHint', { zone: report.timeZone })">
            <input
              tmzInput
              id="report-time"
              type="time"
              [value]="time()"
              (input)="setTime($any($event.target).value)"
              (change)="setTime($any($event.target).value)"
              data-testid="dashboard-report-time"
            />
          </tmz-form-field>
          <div class="sm:pt-7">
            <label class="inline-flex min-h-10 cursor-pointer items-center gap-3">
              <input
                type="checkbox"
                role="switch"
                class="peer sr-only"
                [checked]="active()"
                (change)="active.set($any($event.target).checked)"
                aria-describedby="report-active-hint"
                data-testid="dashboard-report-active"
              />
              <span
                aria-hidden="true"
                class="relative h-6 w-11 shrink-0 rounded-full bg-neutral-quaternary transition-colors after:absolute after:start-0.5 after:top-0.5 after:size-5 after:rounded-full after:bg-white after:shadow-xs after:transition-transform peer-checked:bg-brand peer-checked:after:translate-x-5 peer-focus-visible:ring-4 peer-focus-visible:ring-brand-medium rtl:peer-checked:after:-translate-x-5"
              ></span>
              <span class="text-sm font-medium text-heading">{{ t('dashboards.report.active') }}</span>
            </label>
            <p id="report-active-hint" class="mt-1 text-sm text-body">{{ t('dashboards.report.activeHint') }}</p>
          </div>
        </div>

        <p class="mt-4 text-sm text-body">{{ t('dashboards.report.savingNote') }}</p>
        @if (report.settings) {
          <p id="report-send-hint" class="mt-2 text-sm text-body">{{ t('dashboards.report.sendNowHint') }}</p>
        }
      } @else {
        <p class="mt-4 text-sm text-body" aria-busy="true" data-testid="dashboard-report-loading">{{ t('dashboards.report.loading') }}</p>
      }

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="dashboard-report-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      }
      @if (queued()) {
        <!-- In the dialog, not a toast: while the dialog is open the rest of the page is hidden from screen readers, and covered on phones. -->
        <tmz-alert tone="success" [announce]="true" class="mt-4" data-testid="dashboard-report-queued">{{ t('dashboards.report.queued') }}</tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:flex-wrap sm:justify-end">
        @if (report()?.settings) {
          <button tmzButton variant="ghost" type="button" class="sm:me-auto" [disabled]="removing() || saving()" [attr.aria-busy]="removing()" (click)="remove()" data-testid="dashboard-report-remove">
            {{ t('dashboards.report.remove') }}
          </button>
        }
        <button tmzButton variant="secondary" type="button" (click)="ref.close()" data-testid="dashboard-report-cancel">{{ t('common.cancel') }}</button>
        @if (report()?.settings) {
          <button
            tmzButton
            variant="secondary"
            type="button"
            [disabled]="sending()"
            [attr.aria-busy]="sending()"
            aria-describedby="report-send-hint"
            (click)="sendNow()"
            data-testid="dashboard-report-send"
          >
            {{ sending() ? t('dashboards.report.sending') : t('dashboards.report.sendNow') }}
          </button>
        }
        @if (report()) {
          <button tmzButton type="submit" [disabled]="saving() || removing()" [attr.aria-busy]="saving()" data-testid="dashboard-report-save">
            {{ saving() ? t('dashboards.report.saving') : t('dashboards.report.save') }}
          </button>
        }
      </div>
    </form>
  `,
})
export class DashboardReportDialog {
  protected readonly data = inject<DashboardReportData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DashboardReportResult>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly confirm = inject(ConfirmDialog);
  private readonly injector = inject(Injector);
  /** The interface's regional format: it names the weekdays, and is saved as the email's (design D8). */
  private readonly locale = inject(LOCALE_ID);

  protected readonly maxRecipients = maxReportRecipients;
  protected readonly weekdays = isoWeek.map((day) => ({
    day,
    short: this.capitalised(weekdayText(day, this.locale, 'short')),
    long: this.capitalised(weekdayText(day, this.locale)),
  }));

  /** The email as the API has it; null while it is read. */
  protected readonly report = signal<DashboardReport | null>(null);
  /** The email could not be read: the dialog says why, with nothing to save. */
  protected readonly loadError = signal<string | null>(null);

  protected readonly recipients = signal('');
  /** The weekdays chosen, ascending. */
  protected readonly days = signal<number[]>([]);
  protected readonly time = signal('');
  protected readonly active = signal(true);

  protected readonly saving = signal(false);
  protected readonly removing = signal(false);
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);
  /** Send now was accepted. */
  protected readonly queued = signal(false);
  private readonly submitted = signal(false);
  /** The API's errors, by field; each goes once its field changes. */
  private readonly serverErrors = signal<Partial<Record<Field, string>>>({});

  /** A field left empty, once the form was sent, or what the API said about it. */
  protected readonly fieldErrors = computed<Partial<Record<Field, string>>>(() => {
    const own: Partial<Record<Field, string>> = {};
    if (this.submitted()) {
      if (!parseRecipients(this.recipients()).length) {
        own.recipients = this.transloco.translate('dashboards.report.errors.recipientsRequired');
      }
      if (!this.days().length) {
        own.days = this.transloco.translate('dashboards.report.errors.daysRequired');
      }
      if (!/^\d{2}:\d{2}/.test(this.time())) {
        own.time = this.transloco.translate('dashboards.report.errors.timeRequired');
      }
    }
    return { ...this.serverErrors(), ...own };
  });

  private readonly recipientsInput = viewChild<ElementRef<HTMLTextAreaElement>>('recipientsInput');

  constructor() {
    this.service.getReport(this.data.projectId, this.data.dashboard.id).subscribe({
      next: (report) => {
        this.show(report);
        // Focus was on the only control there while the email was read.
        afterNextRender(() => this.recipientsInput()?.nativeElement.focus(), { injector: this.injector });
      },
      error: (failure) => {
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        this.loadError.set(parsed.message);
      },
    });
  }

  /** A time of the email's history in the shared format, in the project's time zone, which its schedule is in. */
  protected when(value: string): string {
    try {
      return dateText(value, this.locale, 'dateTime', this.report()?.timeZone) ?? value;
    } catch {
      return dateText(value, this.locale) ?? value; // a zone this browser does not know: the viewer's own
    }
  }

  /** Why the last send failed, in the interface's words; a refused recipient is named. */
  protected reason(failure: DashboardReportFailure): string {
    if (failure.code === 'email.rejected' && failure.address) {
      return this.transloco.translate('errors.codes.email.rejectedAddress', { address: failure.address });
    }
    const key = `errors.codes.${failure.code}`;
    const text = this.transloco.translate(key);
    return text && text !== key ? text : this.transloco.translate('errors.generic');
  }

  protected setRecipients(text: string): void {
    this.recipients.set(text);
    this.clearServerError('recipients');
  }

  protected toggleDay(day: number): void {
    this.days.update((days) => (days.includes(day) ? days.filter((d) => d !== day) : [...days, day].sort((a, b) => a - b)));
    this.clearServerError('days');
  }

  protected setTime(time: string): void {
    if (time !== this.time()) {
      this.time.set(time);
      this.clearServerError('time');
    }
  }

  protected save(): void {
    this.submitted.set(true);
    // Sent again, the form is judged again: the API's errors on the last attempt go.
    this.serverErrors.set({});
    this.error.set(null);
    this.queued.set(false);
    if (!this.report() || this.saving() || this.removing() || Object.keys(this.fieldErrors()).length) {
      return;
    }
    const settings: DashboardReportSettings = {
      recipients: parseRecipients(this.recipients()),
      days: this.days(),
      time: this.time().slice(0, 5),
      active: this.active(),
      locale: this.locale,
    };
    this.saving.set(true);
    this.service.saveReport(this.data.projectId, this.data.dashboard.id, settings).subscribe({
      next: (saved) => this.ref.close({ saved }),
      error: (failure) => {
        this.saving.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        // recipients, recipients[3], days, time: next to the field; anything else (locale) as the dialog's error.
        const byField: Partial<Record<Field, string>> = {};
        const others: string[] = [];
        for (const [path, message] of Object.entries(parsed.fieldErrors)) {
          const field = /^(recipients|days|time)\b/i.exec(path)?.[1].toLowerCase() as Field | undefined;
          if (field) {
            byField[field] = byField[field] ? markApiText(`${byField[field]} ${message}`) : message;
          } else {
            others.push(message);
          }
        }
        this.serverErrors.set(byField);
        this.error.set(others.length ? markApiText(others.join(' ')) : Object.keys(byField).length ? null : parsed.message);
      },
    });
  }

  /** Sends the saved email now; the button stays busy until the API answers, which says it is queued or why it is refused. */
  protected sendNow(): void {
    if (this.sending()) {
      return;
    }
    this.sending.set(true);
    this.error.set(null);
    this.queued.set(false);
    this.service.sendReportNow(this.data.projectId, this.data.dashboard.id).subscribe({
      next: () => {
        this.sending.set(false);
        this.queued.set(true);
      },
      error: (failure) => {
        this.sending.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        // reports.tooSoon, reports.none, reports.unavailable: translated from their codes.
        this.error.set(parsed.message);
      },
    });
  }

  protected async remove(): Promise<void> {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const confirmed = await this.confirm.confirm({
      title: t('dashboards.report.removeTitle'),
      message: t('dashboards.report.removeMessage', { name: this.data.dashboard.name }),
      confirmLabel: t('dashboards.report.removeConfirm'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (!confirmed || this.removing() || this.saving()) {
      return;
    }
    this.removing.set(true);
    this.error.set(null);
    this.queued.set(false);
    this.service.removeReport(this.data.projectId, this.data.dashboard.id).subscribe({
      next: () => this.ref.close({ removed: true }),
      error: (failure) => {
        this.removing.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        this.error.set(parsed.message);
      },
    });
  }

  /** Fills the form with the saved settings, or with those of a new email. */
  private show(report: DashboardReport): void {
    const settings = report.settings;
    this.report.set(report);
    this.recipients.set(settings ? settings.recipients.join('\n') : '');
    this.days.set([...(settings?.days ?? newEmail.days)].sort((a, b) => a - b));
    this.time.set(settings?.time ?? newEmail.time);
    this.active.set(settings?.active ?? newEmail.active);
  }

  private clearServerError(field: Field): void {
    this.serverErrors.update((errors) => {
      const rest = { ...errors };
      delete rest[field];
      return rest;
    });
  }

  /** A weekday's name as a label: `lunes` reads "Lunes". */
  private capitalised(name: string): string {
    return name.charAt(0).toLocaleUpperCase(this.locale) + name.slice(1);
  }
}
