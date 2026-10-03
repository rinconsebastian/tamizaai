import { DOCUMENT, Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { ProjectsService, WebhookSettings } from '../../core/projects/projects.service';
import { Alert, Button, Card, ConfirmDialog, Skeleton, ToastService } from '../../shared/ui';
import { ProjectContext } from './project-context';

@Component({
  selector: 'app-project-webhook',
  imports: [TranslocoDirective, Alert, Button, Card, Skeleton],
  template: `
    <div class="grid gap-6 lg:grid-cols-5" *transloco="let t">
      <tmz-card [heading]="t('webhook.title')" class="lg:col-span-3">
        <p class="mb-4 text-sm text-body">{{ t('webhook.intro') }}</p>
        @if (settings(); as settings) {
          @if (settings.secretUnreadable) {
            <tmz-alert tone="danger" class="mb-4">{{ t('webhook.secretUnreadable') }}</tmz-alert>
          }
          <dl class="flex flex-col gap-4">
            @for (row of rows(settings); track row.key) {
              <div>
                <dt class="mb-1 text-sm font-medium text-heading">{{ t('webhook.' + row.key) }}</dt>
                <dd class="flex flex-col gap-2 sm:flex-row sm:items-center">
                  <code class="flex-1 rounded-base border border-default bg-neutral-secondary-medium px-3 py-2 text-sm text-heading break-all" [attr.data-testid]="'webhook-' + row.key">
                    {{ row.key === 'secret' && !revealed() ? '••••••••••••••••' : row.value }}
                  </code>
                  <div class="flex gap-2">
                    @if (row.key === 'secret') {
                      <button tmzButton variant="ghost" size="sm" type="button" (click)="revealed.set(!revealed())">
                        {{ revealed() ? t('webhook.hide') : t('webhook.show') }}
                      </button>
                    }
                    <button tmzButton variant="secondary" size="sm" type="button" (click)="copy(row.value)" [attr.data-testid]="'copy-' + row.key">
                      {{ t('webhook.copy') }}
                    </button>
                  </div>
                </dd>
              </div>
            }
          </dl>
          <button tmzButton variant="danger" class="mt-6" type="button" (click)="regenerate()" data-testid="regenerate">
            {{ t('webhook.regenerate') }}
          </button>
        } @else {
          <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-3/4" /><tmz-skeleton class="w-1/2" /></div>
        }
      </tmz-card>

      <tmz-card [heading]="t('webhook.stepsTitle')" class="lg:col-span-2">
        <ol class="list-decimal space-y-2 ps-5 text-sm text-body">
          @for (step of steps; track step) {
            <li>{{ t('webhook.steps.' + step) }}</li>
          }
        </ol>
        <p class="mt-4 text-sm text-body">{{ t('webhook.ingestionNote') }}</p>
      </tmz-card>
    </div>
  `,
})
export class ProjectWebhook {
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly document = inject(DOCUMENT);
  private readonly context = inject(ProjectContext);

  protected readonly settings = signal<WebhookSettings | null>(null);
  protected readonly revealed = signal(false);
  protected readonly steps = ['open', 'restServices', 'register', 'url', 'auth', 'save'];

  constructor() {
    this.service.webhook(this.context.project()!.id).subscribe({
      next: (settings) => this.settings.set(settings),
      error: (error) => this.errors.report(error),
    });
  }

  protected rows(settings: WebhookSettings): { key: 'url' | 'username' | 'secret'; value: string }[] {
    return [
      { key: 'url', value: settings.url },
      { key: 'username', value: settings.username },
      { key: 'secret', value: settings.secret ?? '' },
    ];
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
