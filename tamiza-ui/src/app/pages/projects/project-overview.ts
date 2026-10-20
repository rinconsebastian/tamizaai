import { DOCUMENT } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { ProjectDetails, ProjectsService } from '../../core/projects/projects.service';
import { Alert, Badge, Button, Card, FormField, Input, ToastService, DateTimePipe } from '../../shared/ui';
import { ProjectContext } from './project-context';
import { isSingular } from '../../core/i18n/plural';

/** The names the browser knows, plus UTC (the default, which it does not list). The API judges the final choice. */
function zoneNames(): string[] {
  const supported = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
  return ['UTC', ...supported.filter((name) => name !== 'UTC')];
}

/** The zones by region (the part before the first slash), so a list of hundreds can be scanned; UTC stays on its own, first. */
function groupZones(names: string[]): { region: string; zones: string[] }[] {
  const regions = new Map<string, string[]>();
  for (const name of names) {
    if (name === 'UTC') {
      continue;
    }
    const region = name.includes('/') ? name.slice(0, name.indexOf('/')) : 'Other';
    regions.set(region, [...(regions.get(region) ?? []), name]);
  }
  return [...regions].map(([region, zones]) => ({ region, zones }));
}

const metadataTypes = new Set(['start', 'end', 'today', 'deviceid', 'username', 'audit', 'calculate', 'note']);

@Component({
  selector: 'app-project-overview',
  imports: [DateTimePipe, RouterLink, TranslocoDirective, Alert, Badge, Button, Card, FormField, Input],
  template: `
    @if (project(); as project) {
      <div class="grid gap-6 lg:grid-cols-2" *transloco="let t">
        <tmz-card [heading]="t('overview.formTitle')">
          <dl class="grid gap-2 text-sm">
            <div class="sm:flex sm:gap-3"><dt class="text-body sm:w-28 sm:shrink-0">{{ t('overview.server') }}</dt><dd class="font-medium text-heading break-all">{{ project.koboServerUrl }}</dd></div>
            <div class="sm:flex sm:gap-3"><dt class="text-body sm:w-28 sm:shrink-0">{{ t('overview.assetUid') }}</dt><dd class="font-mono text-heading break-all">{{ project.assetUid }}</dd></div>
            @if (!context.can('admin')) {
              <div class="sm:flex sm:gap-3" data-testid="time-zone-row"><dt class="text-body sm:w-28 sm:shrink-0">{{ t('overview.timeZone') }}</dt><dd class="text-heading">{{ project.timeZone }}</dd></div>
            }
          </dl>
          @if (project.unavailableFormVersions; as unavailable) {
            <tmz-alert tone="warning" class="mt-5" data-testid="unavailable-versions">
              {{
                singular(unavailable)
                  ? t('overview.unavailableVersionsOne')
                  : t('overview.unavailableVersions', { count: unavailable })
              }}
              <a [routerLink]="['..', 'data']" fragment="form-versions" class="font-medium underline">{{ t('overview.unavailableVersionsAction') }}</a>
            </tmz-alert>
          }
          @if (project.tokenUnreadable && context.can('admin')) {
            <tmz-alert tone="danger" class="mt-5" data-testid="token-unreadable">
              {{ t('overview.tokenUnreadable') }}
              <a [routerLink]="['..', 'webhook']" class="font-medium underline" data-testid="token-unreadable-webhook">{{ t('overview.tokenUnreadableWebhook') }}</a>
            </tmz-alert>
          }
          <div class="mt-5 flex flex-wrap items-center gap-3">
            <span class="text-sm text-body" data-testid="checked-at">{{ t('overview.checkedAt') }}: {{ project.formCheckedAt | dateTime }}</span>
            @if (context.can('analyst')) {
              <button tmzButton variant="secondary" size="sm" type="button" [disabled]="busy()" (click)="recheck()" data-testid="recheck">
                {{ t('overview.recheck') }}
              </button>
            }
            @if (context.can('admin')) {
              <button
                tmzButton
                variant="ghost"
                size="sm"
                type="button"
                [attr.aria-expanded]="replacing()"
                aria-controls="replaceTokenForm"
                (click)="toggleReplace()"
                data-testid="replace-token-toggle"
              >
                {{ t('overview.replaceToken') }}
              </button>
            }
          </div>
          @if (replacing()) {
            <form id="replaceTokenForm" class="mt-5 flex flex-col gap-3 border-t border-default pt-5" (submit)="$event.preventDefault(); replaceToken(tokenInput.value)" data-testid="replace-token-form">
              <tmz-form-field [label]="t('createProject.apiToken')" fieldId="newToken" [error]="tokenError()">
                <input tmzInput #tokenInput id="newToken" type="password" autocomplete="off" />
              </tmz-form-field>
              <div class="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
                <button tmzButton variant="secondary" type="button" (click)="replacing.set(false)" data-testid="replace-token-cancel">{{ t('common.cancel') }}</button>
                <button tmzButton type="submit" [disabled]="busy()">{{ t('overview.saveToken') }}</button>
              </div>
            </form>
          }
        </tmz-card>

        <tmz-card [heading]="t('overview.checkTitle')">
          <ul class="flex flex-col gap-4">
            @for (metric of project.fieldCheck; track metric.metric) {
              <li class="flex flex-col gap-1" [attr.data-testid]="'metric-' + metric.metric">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="text-sm font-medium text-heading">{{ t('metrics.' + metric.metric) }}</span>
                  <tmz-badge [tone]="metric.available ? 'success' : 'warning'">
                    {{ metric.available ? t('overview.available') : t('overview.unavailable') }}
                  </tmz-badge>
                </div>
                @if (!metric.available && metric.missingFields.length) {
                  <p class="text-sm text-body wrap-anywhere">{{ t('overview.missing', { fields: metric.missingFields.join(', ') }) }}</p>
                }
                @if (!metric.available && metric.hint; as hint) {
                  <p class="text-sm text-body" [attr.data-testid]="'hint-' + metric.metric">
                    {{ t(hintKey(hint)) }}
                    @if (hint !== 'selectEnumeratorField') {
                      <a [routerLink]="['..', 'sampling-frame']" class="font-medium text-fg-brand hover:underline" data-testid="hint-link">{{ t('overview.hints.openFrame') }}</a>
                    }
                  </p>
                }
                @if (metric.metric === 'perEnumerator' && context.can('admin')) {
                  <!-- The question is set once: its value first, the select only on request (or while the check asks for it). -->
                  <div id="enumeratorField" class="mt-1" data-testid="enumerator-summary">
                    <p class="flex flex-wrap items-center gap-x-2 text-sm text-body">
                      <span>{{ t('overview.enumeratorField') }}:</span>
                      <span class="font-medium text-heading wrap-anywhere" data-testid="enumerator-value">{{ enumeratorText(t) }}</span>
                      @if (questionFields().length > 0 && !enumeratorAutoOpen()) {
                        <button
                          tmzButton
                          variant="ghost"
                          size="sm"
                          type="button"
                          [attr.aria-expanded]="enumeratorOpen()"
                          aria-controls="enumeratorEditor"
                          (click)="openEnumerator()"
                          data-testid="enumerator-change"
                        >
                          {{ t('overview.change') }}
                        </button>
                      }
                    </p>
                    @if (enumeratorOpen()) {
                      <form id="enumeratorEditor" class="mt-2 flex flex-col gap-2 sm:flex-row sm:items-end" (submit)="$event.preventDefault(); saveEnumerator(enumerator.value)" data-testid="enumerator-form">
                        <tmz-form-field class="flex-1" [label]="t('overview.enumeratorField')" fieldId="enumeratorSelect" [hint]="t('overview.enumeratorHint')">
                          <select tmzInput #enumerator id="enumeratorSelect">
                            <option value="">{{ t('overview.noEnumerator') }}</option>
                            @for (field of questionFields(); track field.xpath) {
                              <option [value]="field.xpath" [selected]="field.xpath === project.enumeratorField">{{ field.label ?? field.name }} ({{ field.xpath }})</option>
                            }
                          </select>
                        </tmz-form-field>
                        <button tmzButton variant="secondary" type="submit" [disabled]="busy()">{{ t('common.save') }}</button>
                        @if (!enumeratorAutoOpen()) {
                          <button tmzButton variant="ghost" type="button" (click)="editingEnumerator.set(false)" data-testid="enumerator-cancel">{{ t('common.cancel') }}</button>
                        }
                      </form>
                    }
                  </div>
                }
              </li>
            }
          </ul>
        </tmz-card>

        @if (context.can('admin')) {
          <tmz-card [heading]="t('overview.settingsTitle')" class="lg:col-span-2">
            <form class="flex flex-col gap-3" (submit)="$event.preventDefault(); saveSettings(nameInput.value, zone.value)" data-testid="settings-form">
              <div class="grid gap-3 lg:grid-cols-2">
                <tmz-form-field [label]="t('createProject.name')" fieldId="projectName" [error]="nameError()">
                  <input tmzInput #nameInput id="projectName" [value]="project.name" autocomplete="off" />
                </tmz-form-field>
                <tmz-form-field [label]="t('overview.timeZone')" fieldId="timeZone" [error]="timeZoneError()" [hint]="t('overview.timeZoneHint')">
                  <select tmzInput #zone id="timeZone">
                    <option value="UTC" [selected]="project.timeZone === 'UTC'">UTC</option>
                    @if (unlistedZone(); as unlisted) {
                      <option [value]="unlisted" selected>{{ unlisted }}</option>
                    }
                    @for (group of zoneGroups; track group.region) {
                      <optgroup [label]="group.region">
                        @for (name of group.zones; track name) {
                          <option [value]="name" [selected]="name === project.timeZone">{{ name }}</option>
                        }
                      </optgroup>
                    }
                  </select>
                  @if (browserZone && browserZone !== project.timeZone) {
                    <button tmzButton variant="ghost" size="sm" type="button" class="mt-1" (click)="useBrowserZone(zone)" data-testid="use-browser-zone">
                      {{ t('overview.useBrowserZone', { zone: browserZone }) }}
                    </button>
                  }
                </tmz-form-field>
              </div>
              <button tmzButton type="submit" class="sm:self-end" [disabled]="busy()">{{ t('common.save') }}</button>
            </form>
          </tmz-card>
        }
      </div>
    }
  `,
})
export class ProjectOverview {
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);
  private readonly route = inject(ActivatedRoute);
  protected readonly context = inject(ProjectContext);

  protected readonly project = this.context.project;
  protected readonly busy = signal(false);
  protected readonly replacing = signal(false);
  protected readonly editingEnumerator = signal(false);
  protected readonly tokenError = signal<string | null>(null);
  protected readonly nameError = signal<string | null>(null);
  protected readonly timeZoneError = signal<string | null>(null);
  private readonly knownZones = zoneNames();
  protected readonly zoneGroups = groupZones(this.knownZones);
  protected readonly browserZone = typeof Intl !== 'undefined' ? Intl.DateTimeFormat().resolvedOptions().timeZone : '';
  /** A zone the project has that the browser does not list, kept at the top so the saved value is never silently dropped. */
  protected readonly unlistedZone = computed(() => {
    const current = this.project()?.timeZone;
    return current && !this.knownZones.includes(current) ? current : null;
  });
  protected readonly questionFields = computed(() => (this.context.formFields() ?? []).filter((f) => !metadataTypes.has(f.type)));

  /** The check asks for the question (nothing identifies the enumerator yet), so the select is open for the admin. */
  protected readonly enumeratorAutoOpen = computed(() => {
    const metric = this.project()?.fieldCheck?.find((m) => m.metric === 'perEnumerator');
    return !!metric && !metric.available && metric.hint === 'selectEnumeratorField' && this.questionFields().length > 0;
  });
  protected readonly enumeratorOpen = computed(() => this.editingEnumerator() || this.enumeratorAutoOpen());

  private readonly fragment = toSignal(this.route.fragment, { initialValue: null });

  constructor() {
    this.loadFormFields();
    // A link from the dashboard names the enumerator question: open it and put the cursor there once the questions are known.
    effect(() => {
      if (this.fragment() === 'enumeratorField' && this.context.can('admin') && this.questionFields().length > 0) {
        this.editingEnumerator.set(true);
        this.focus('enumeratorSelect');
      }
    });
  }

  /** What identifies the enumerator now, as one line: the question, the metadata field used instead, or nothing. */
  protected enumeratorText(t: (key: string, params?: Record<string, unknown>) => string): string {
    const project = this.project()!;
    const saved = project.enumeratorField;
    if (saved) {
      if (this.context.formFields() === null) {
        return saved;
      }
      const field = this.questionFields().find((f) => f.xpath === saved);
      return field ? `${field.label ?? field.name} (${field.xpath})` : t('overview.enumeratorGone', { xpath: saved });
    }
    const metric = project.fieldCheck?.find((m) => m.metric === 'perEnumerator');
    return metric?.available ? t('overview.enumeratorUsername') : t('overview.noEnumerator');
  }

  protected openEnumerator(): void {
    this.editingEnumerator.set(true);
    this.focus('enumeratorSelect');
  }

  protected toggleReplace(): void {
    this.replacing.update((open) => !open);
    if (this.replacing()) {
      this.focus('newToken');
    }
  }

  protected useBrowserZone(select: HTMLSelectElement): void {
    if (Array.from(select.options).some((option) => option.value === this.browserZone)) {
      select.value = this.browserZone;
    }
  }

  protected recheck(): void {
    this.run(this.service.recheck(this.id()), 'overview.rechecked');
  }

  protected replaceToken(token: string): void {
    this.tokenError.set(null);
    this.run(this.service.replaceToken(this.id(), token), 'overview.tokenReplaced', (message) => this.tokenError.set(message), () =>
      this.replacing.set(false),
    );
  }

  protected saveEnumerator(field: string): void {
    this.run(this.service.update(this.id(), { enumeratorField: field }), 'overview.enumeratorSaved', undefined, () => this.editingEnumerator.set(false));
  }

  /** Hints name who can act: admins are told to choose the enumerator question, analysts whom to ask. */
  protected hintKey(hint: string): string {
    const askAdmin = hint === 'selectEnumeratorField' && !this.context.can('admin');
    return `overview.hints.${hint}${askAdmin ? 'Viewer' : ''}`;
  }

  /** The name and the time zone are saved together; the API's message for either appears next to its field. */
  protected saveSettings(name: string, timeZone: string): void {
    this.nameError.set(null);
    this.timeZoneError.set(null);
    this.run(this.service.update(this.id(), { name, timeZone }), 'overview.settingsSaved', (message, fields) => {
      this.nameError.set(fields['name'] ?? null);
      this.timeZoneError.set(fields['timeZone'] ?? (fields['name'] ? null : message));
    });
  }

  private id(): string {
    return this.project()!.id;
  }

  private loadFormFields(): void {
    // The questions are only needed for the admin's enumerator setting.
    if (this.context.can('admin')) {
      this.context.loadFormFields();
    }
  }

  /** Puts the cursor in a control that appears after the next render. */
  private focus(id: string): void {
    setTimeout(() => {
      const element = this.document.getElementById(id);
      element?.focus();
      element?.scrollIntoView?.({ block: 'center' });
    });
  }

  private run(
    request: ReturnType<ProjectsService['get']>,
    successKey: string,
    onError?: (message: string, fields: Record<string, string>) => void,
    onSuccess?: () => void,
  ): void {
    this.busy.set(true);
    request.subscribe({
      next: (project: ProjectDetails) => {
        this.context.update(project);
        this.loadFormFields();
        this.toasts.success(this.transloco.translate(successKey));
        this.busy.set(false);
        onSuccess?.();
      },
      error: (error) => {
        const parsed = onError ? this.errors.parse(error) : this.errors.report(error);
        onError?.(parsed.fieldErrors['apiToken'] ?? parsed.message, parsed.fieldErrors);
        this.busy.set(false);
      },
    });
  }

  /** Whether a message about `count` things takes its singular form in the interface language. */
  protected singular(count: number): boolean {
    return isSingular(count, this.transloco.getActiveLang());
  }
}
