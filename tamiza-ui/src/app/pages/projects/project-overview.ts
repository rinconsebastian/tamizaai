import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { ProjectDetails, ProjectsService } from '../../core/projects/projects.service';
import { Alert, Badge, Button, Card, FormField, Input, ToastService } from '../../shared/ui';
import { ProjectContext } from './project-context';

const metadataTypes = new Set(['start', 'end', 'today', 'deviceid', 'username', 'audit', 'calculate', 'note']);

@Component({
  selector: 'app-project-overview',
  imports: [DatePipe, TranslocoDirective, Alert, Badge, Button, Card, FormField, Input],
  template: `
    @if (project(); as project) {
      <div class="grid gap-6 lg:grid-cols-2" *transloco="let t">
        @if (project.tokenUnreadable && context.can('admin')) {
          <tmz-alert tone="danger" class="lg:col-span-2" data-testid="token-unreadable">{{ t('overview.tokenUnreadable') }}</tmz-alert>
        }

        <tmz-card [heading]="t('overview.formTitle')">
          <dl class="grid gap-3 text-sm">
            <div><dt class="text-body">{{ t('overview.formName') }}</dt><dd class="font-medium text-heading break-words">{{ project.formName }}</dd></div>
            <div><dt class="text-body">{{ t('overview.server') }}</dt><dd class="font-medium text-heading break-all">{{ project.koboServerUrl }}</dd></div>
            <div><dt class="text-body">{{ t('overview.assetUid') }}</dt><dd class="font-mono text-heading break-all">{{ project.assetUid }}</dd></div>
            <div><dt class="text-body">{{ t('overview.checkedAt') }}</dt><dd class="text-heading">{{ project.formCheckedAt | date: 'medium' }}</dd></div>
          </dl>
          <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:flex-wrap">
            @if (context.can('analyst')) {
              <button tmzButton variant="secondary" type="button" [disabled]="busy()" (click)="recheck()" data-testid="recheck">
                {{ t('overview.recheck') }}
              </button>
            }
            @if (context.can('admin')) {
              <button tmzButton variant="secondary" type="button" (click)="replacing.set(!replacing())" data-testid="replace-token-toggle">
                {{ t('overview.replaceToken') }}
              </button>
            }
          </div>
          @if (replacing()) {
            <form class="mt-5 flex flex-col gap-3 border-t border-default pt-5" (submit)="$event.preventDefault(); replaceToken(tokenInput.value)" data-testid="replace-token-form">
              <tmz-form-field [label]="t('createProject.apiToken')" fieldId="newToken" [error]="tokenError()">
                <input tmzInput #tokenInput id="newToken" type="password" autocomplete="off" />
              </tmz-form-field>
              <button tmzButton type="submit" class="sm:self-end" [disabled]="busy()">{{ t('overview.saveToken') }}</button>
            </form>
          }
        </tmz-card>

        <tmz-card [heading]="t('overview.checkTitle')">
          <p class="mb-4 text-sm text-body">{{ t('overview.checkIntro') }}</p>
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
                  <p class="text-sm text-body">{{ t('overview.missing', { fields: metric.missingFields.join(', ') }) }}</p>
                }
                @if (metric.metric === 'perEnumerator' && context.can('admin') && (!metric.available || project.enumeratorField)) {
                  <form class="mt-2 flex flex-col gap-2 sm:flex-row sm:items-end" (submit)="$event.preventDefault(); saveEnumerator(enumerator.value)" data-testid="enumerator-form">
                    <tmz-form-field class="flex-1" [label]="t('overview.enumeratorField')" fieldId="enumeratorField" [hint]="t('overview.enumeratorHint')">
                      <select tmzInput #enumerator id="enumeratorField">
                        <option value="">{{ t('overview.noEnumerator') }}</option>
                        @for (field of questionFields(); track field.xpath) {
                          <option [value]="field.xpath" [selected]="field.xpath === project.enumeratorField">{{ field.label ?? field.name }} ({{ field.xpath }})</option>
                        }
                      </select>
                    </tmz-form-field>
                    <button tmzButton variant="secondary" type="submit" [disabled]="busy()">{{ t('common.save') }}</button>
                  </form>
                }
              </li>
            }
          </ul>
        </tmz-card>

        @if (context.can('admin')) {
          <tmz-card [heading]="t('overview.settingsTitle')" class="lg:col-span-2">
            <form class="flex flex-col gap-3 sm:flex-row sm:items-end" (submit)="$event.preventDefault(); rename(nameInput.value)" data-testid="rename-form">
              <tmz-form-field class="flex-1" [label]="t('createProject.name')" fieldId="projectName" [error]="nameError()">
                <input tmzInput #nameInput id="projectName" [value]="project.name" autocomplete="off" />
              </tmz-form-field>
              <button tmzButton type="submit" [disabled]="busy()">{{ t('common.save') }}</button>
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
  protected readonly context = inject(ProjectContext);

  protected readonly project = this.context.project;
  protected readonly busy = signal(false);
  protected readonly replacing = signal(false);
  protected readonly tokenError = signal<string | null>(null);
  protected readonly nameError = signal<string | null>(null);
  protected readonly questionFields = computed(() => (this.context.formFields() ?? []).filter((f) => !metadataTypes.has(f.type)));

  constructor() {
    this.context.loadFormFields();
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
    this.run(this.service.update(this.id(), { enumeratorField: field }), 'overview.enumeratorSaved');
  }

  protected rename(name: string): void {
    this.nameError.set(null);
    this.run(this.service.update(this.id(), { name }), 'overview.renamed', (message, fields) => this.nameError.set(fields['name'] ?? message));
  }

  private id(): string {
    return this.project()!.id;
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
        this.context.loadFormFields();
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
}
