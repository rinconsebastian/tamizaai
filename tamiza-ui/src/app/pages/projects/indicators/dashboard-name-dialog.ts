import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, ElementRef, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { ApiErrors } from '../../../core/api-error';
import { IndicatorsService, maxDashboardNameLength } from '../../../core/projects/indicators.service';
import { DashboardEntry } from '../../../core/projects/projects.service';
import { Alert, ApiLang, Button, FormField, Input, markApiText } from '../../../shared/ui';

/** What the name is for: a new dashboard, a new name for one, or a copy of one. */
export type DashboardNameAction = 'add' | 'rename' | 'duplicate';

export interface DashboardNameData {
  projectId: string;
  action: DashboardNameAction;
  /** The dashboard renamed or duplicated; null when adding one. */
  dashboard: DashboardEntry | null;
  /** The project's dashboards as the page shows them: a copy is proposed under a name none of them has. */
  dashboards: DashboardEntry[];
  /** Told when the API refuses with a rule on the project's dashboards (`dashboards.limit`): the page's list is out of date. */
  refused?: () => void;
}

/** The dashboard added, renamed or copied, or `gone` when the dashboard renamed or duplicated no longer exists. */
export type DashboardNameResult = DashboardEntry | { gone: true };

/**
 * The name proposed for a copy (design D12): the original's name followed by " (copy)", or " (copy 2)", " (copy 3)" and so on when
 * the project already has that name in any case, the original's name cut so the whole fits the 40 characters.
 */
export function copyName(name: string, dashboards: DashboardEntry[], suffix: (n: number) => string): string {
  const taken = new Set(dashboards.map((d) => d.name.trim().toLowerCase()));
  for (let n = 1; ; n++) {
    const end = suffix(n);
    // Trailing spaces go, and so does half of a character the cut splits.
    const base = name
      .slice(0, maxDashboardNameLength - end.length)
      .replace(/[\uD800-\uDBFF]$/, '')
      .trimEnd();
    if (!taken.has(`${base}${end}`.toLowerCase())) {
      return `${base}${end}`;
    }
  }
}

/**
 * Asks for a dashboard's name and saves it: adds a dashboard, renames one or duplicates one (design D12). A refused name shows next
 * to the input; any other refusal (the most dashboards a project can hold, more widgets than a dashboard can hold) shows as the
 * dialog's error, with what was typed kept. A dashboard removed meanwhile closes the dialog with `gone`, for the page to catch up.
 */
@Component({
  selector: 'app-dashboard-name-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, FormField, Input],
  template: `
    <form
      class="w-[min(28rem,calc(100vw-2rem))] rounded-base border border-default bg-neutral-primary-soft p-4 shadow-sm md:p-6"
      novalidate
      (submit)="$event.preventDefault(); submit()"
      *transloco="let t"
      data-testid="dashboard-name-dialog"
    >
      <h2 id="dashboard-name-title" class="break-words text-lg font-medium text-heading [overflow-wrap:anywhere]">{{ title }}</h2>

      <tmz-form-field class="mt-4" [label]="t('dashboards.nameDialog.name')" fieldId="dashboard-name" [error]="nameError()" [hint]="t('dashboards.nameDialog.hint', { max: maxLength })">
        <input
          #input
          tmzInput
          id="dashboard-name"
          autocomplete="off"
          [value]="name()"
          (input)="name.set($any($event.target).value); serverNameError.set(null)"
          data-testid="dashboard-name"
        />
      </tmz-form-field>

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" data-testid="dashboard-name-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()" data-testid="dashboard-name-cancel">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" [disabled]="busy()" [attr.aria-busy]="busy()" data-testid="dashboard-name-save">
          {{ busy() ? t('dashboards.nameDialog.saving') : t('dashboards.nameDialog.confirm.' + data.action) }}
        </button>
      </div>
    </form>
  `,
})
export class DashboardNameDialog {
  protected readonly data = inject<DashboardNameData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DashboardNameResult>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);

  protected readonly maxLength = maxDashboardNameLength;
  protected readonly title = this.transloco.translate(`dashboards.nameDialog.title.${this.data.action}`, { name: this.data.dashboard?.name ?? '' });

  protected readonly name = signal(this.proposed());
  protected readonly serverNameError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  private readonly submitted = signal(false);

  /** An empty or long name is an error once the form is submitted; the server's verdict holds until the name changes. */
  protected readonly nameError = computed(() => {
    const name = this.name().trim();
    if (this.submitted() && !name) {
      return this.transloco.translate('dashboards.nameDialog.errors.required');
    }
    if (this.submitted() && name.length > maxDashboardNameLength) {
      return this.transloco.translate('dashboards.nameDialog.errors.long', { max: maxDashboardNameLength });
    }
    return this.serverNameError();
  });

  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

  constructor() {
    // A proposed name is replaced as a whole by typing, or kept by pressing Enter.
    afterNextRender(() => this.input().nativeElement.select());
  }

  private proposed(): string {
    const { action, dashboard, dashboards } = this.data;
    if (action === 'rename') {
      return dashboard?.name ?? '';
    }
    if (action === 'duplicate' && dashboard) {
      return copyName(dashboard.name, dashboards, (n) =>
        n === 1 ? this.transloco.translate('dashboards.nameDialog.copy') : this.transloco.translate('dashboards.nameDialog.copyN', { n }),
      );
    }
    return '';
  }

  protected submit(): void {
    this.submitted.set(true);
    this.serverNameError.set(null);
    this.error.set(null);
    if (this.nameError() || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.request(this.name().trim()).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        const fields = Object.entries(parsed.fieldErrors);
        const name = fields.find(([field]) => field.toLowerCase() === 'name');
        const others = fields.filter(([field]) => field.toLowerCase() !== 'name').map(([, message]) => message);
        this.serverNameError.set(name?.[1] ?? null);
        this.error.set(others.length ? markApiText(others.join(' ')) : name ? null : parsed.message);
        if (parsed.code?.startsWith('dashboards.')) {
          this.data.refused?.();
        }
      },
    });
  }

  private request(name: string): Observable<DashboardEntry> {
    const { projectId, action, dashboard } = this.data;
    if (action === 'rename') {
      return this.service.renameDashboard(projectId, dashboard!.id, name);
    }
    if (action === 'duplicate') {
      return this.service.duplicateDashboard(projectId, dashboard!.id, name);
    }
    return this.service.addDashboard(projectId, name);
  }
}
