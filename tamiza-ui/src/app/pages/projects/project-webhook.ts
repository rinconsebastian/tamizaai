import { DatePipe } from '@angular/common';
import { DOCUMENT, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { IngestionService } from '../../core/projects/ingestion.service';
import { ProjectsService, WebhookSettings } from '../../core/projects/projects.service';
import { Alert, Button, Card, ConfirmDialog, Disclosure, Skeleton, ToastService, dateTimeFormat } from '../../shared/ui';
import { ProjectContext } from './project-context';

/**
 * The values an admin enters in Kobo's REST Services, on one card: whether deliveries are arriving comes first, because it is
 * what the admin came to check, then the credentials, then the setup steps, which stay open until the first delivery proves they
 * were followed.
 */
@Component({
  selector: 'app-project-webhook',
  imports: [DatePipe, RouterLink, TranslocoDirective, Alert, Button, Card, Disclosure, Skeleton],
  template: `
    <ng-container *transloco="let t">
      @if (!isAdmin) {
        <tmz-alert tone="warning" data-testid="webhook-admins-only">{{ t('webhook.adminsOnly') }}</tmz-alert>
      } @else {
        <tmz-card [heading]="t('webhook.title')" class="max-w-3xl">
          @if (delivery(); as delivery) {
            @if (delivery.at) {
              <p class="mb-3 text-sm text-body" data-testid="last-delivery">{{ t('webhook.lastDelivery', { time: (delivery.at | date: dateTimeFormat) }) }}</p>
            } @else {
              <tmz-alert tone="warning" class="mb-3" data-testid="last-delivery">
                {{ t('webhook.noDelivery') }}
                <a [routerLink]="['..', 'data']" class="font-medium underline" data-testid="deliveries-link">{{ t('webhook.seeSubmissions') }}</a>
              </tmz-alert>
            }
          } @else if (!deliveryFailed()) {
            <div class="mb-3" aria-busy="true" data-testid="delivery-loading"><tmz-skeleton class="w-1/2" /></div>
          }

          <p class="mb-4 text-sm text-body">{{ t('webhook.intro') }}</p>

          @if (settings(); as settings) {
            @if (settings.secretUnreadable) {
              <tmz-alert tone="danger" class="mb-4" data-testid="secret-unreadable">
                <p>{{ t('webhook.secretUnreadable') }}</p>
                <button tmzButton variant="secondary" size="sm" class="mt-3" type="button" (click)="regenerate()" data-testid="regenerate">{{ t('webhook.regenerate') }}</button>
              </tmz-alert>
            }
            <dl class="flex flex-col gap-4">
              @for (row of rows(settings); track row.key) {
                <div>
                  <dt class="mb-1 text-sm font-medium text-heading">{{ t('webhook.' + row.key) }}</dt>
                  <dd class="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
                    <code class="min-w-0 flex-1 break-all rounded-base border border-default bg-neutral-secondary-medium px-3 py-2 text-sm text-heading sm:min-w-64" [attr.data-testid]="'webhook-' + row.key">
                      @if (row.key === 'secret' && !revealed()) {
                        <span aria-hidden="true">••••••••••••••••</span><span class="sr-only">{{ t('webhook.hidden') }}</span>
                      } @else {
                        {{ row.value }}
                      }
                    </code>
                    <div class="flex items-center gap-2">
                      @if (row.key === 'secret') {
                        <button tmzButton variant="ghost" size="sm" type="button" (click)="revealed.set(!revealed())" data-testid="toggle-secret">
                          {{ revealed() ? t('webhook.hide') : t('webhook.show') }}
                        </button>
                      }
                      @if (row.key !== 'username') {
                        <button tmzButton variant="secondary" size="sm" type="button" (click)="copy(row.value)" [attr.aria-label]="t('webhook.copy') + ': ' + t('webhook.' + row.key)" [attr.title]="t('webhook.copy')" [attr.data-testid]="'copy-' + row.key">
                          <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V6a2 2 0 0 1 2-2h9" /></svg>
                        </button>
                      }
                      @if (row.key === 'secret') {
                        <button tmzButton variant="secondary" size="sm" type="button" (click)="regenerate()" data-testid="regenerate">{{ t('webhook.regenerate') }}</button>
                      }
                    </div>
                  </dd>
                </div>
              }
            </dl>
          } @else if (settingsFailed()) {
            <tmz-alert tone="danger" data-testid="webhook-load-error">{{ t('webhook.loadFailed') }}</tmz-alert>
          } @else {
            <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-3/4" /><tmz-skeleton class="w-1/2" /></div>
          }

          @if (delivery() || deliveryFailed()) {
            <tmz-disclosure class="mt-6 border-t border-default pt-4" [heading]="t('webhook.stepsTitle')" [open]="!delivery()?.at" data-testid="webhook-steps">
              <ol class="list-decimal space-y-2 ps-5 text-sm text-body">
                @for (step of steps; track step) {
                  <li>{{ t('webhook.steps.' + step) }}</li>
                }
              </ol>
            </tmz-disclosure>
          }
        </tmz-card>
      }
    </ng-container>
  `,
})
export class ProjectWebhook {
  private readonly service = inject(ProjectsService);
  private readonly ingestion = inject(IngestionService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly document = inject(DOCUMENT);
  private readonly context = inject(ProjectContext);

  protected readonly dateTimeFormat = dateTimeFormat;
  protected readonly settings = signal<WebhookSettings | null>(null);
  protected readonly settingsFailed = signal(false);
  protected readonly revealed = signal(false);
  protected readonly delivery = signal<{ at: string | null } | null>(null);
  protected readonly deliveryFailed = signal(false);
  protected readonly steps = ['open', 'restServices', 'register', 'url', 'type', 'auth', 'save'];

  /** Only admins may read the webhook settings; for anyone else the page says so instead of asking the API. */
  protected readonly isAdmin = this.context.can('admin');

  constructor() {
    if (!this.isAdmin) {
      return;
    }
    const projectId = this.context.project()!.id;
    this.service.webhook(projectId).subscribe({
      next: (settings) => this.settings.set(settings),
      error: (error) => {
        this.errors.report(error);
        this.settingsFailed.set(true);
      },
    });
    this.ingestion.status(projectId).subscribe({
      next: (status) => this.delivery.set({ at: status.lastWebhookAt }),
      error: (error) => {
        this.errors.report(error);
        this.deliveryFailed.set(true);
      },
    });
  }

  /** The values to enter in Kobo. A secret that can no longer be read has no value to show, mask or copy. */
  protected rows(settings: WebhookSettings): { key: 'url' | 'username' | 'secret'; value: string }[] {
    const rows: { key: 'url' | 'username' | 'secret'; value: string }[] = [
      { key: 'url', value: settings.url },
      { key: 'username', value: settings.username },
    ];
    return settings.secretUnreadable ? rows : [...rows, { key: 'secret', value: settings.secret ?? '' }];
  }

  protected async copy(value: string): Promise<void> {
    try {
      await this.document.defaultView!.navigator.clipboard.writeText(value);
      this.toasts.success(this.transloco.translate('webhook.copied'));
    } catch {
      this.toasts.error(this.transloco.translate('webhook.copyFailed'));
    }
  }

  protected async regenerate(): Promise<void> {
    const t = (key: string) => this.transloco.translate(key);
    const confirmed = await this.confirmDialog.confirm({
      title: t('webhook.regenerateTitle'),
      message: t('webhook.regenerateMessage'),
      confirmLabel: t('webhook.regenerate'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (!confirmed) {
      return;
    }
    this.service.regenerateSecret(this.context.project()!.id).subscribe({
      next: (settings) => {
        this.settings.set(settings);
        this.revealed.set(true);
        this.toasts.success(t('webhook.regenerated'));
      },
      error: (error) => this.errors.report(error),
    });
  }
}
