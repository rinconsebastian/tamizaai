import { DialogRef } from '@angular/cdk/dialog';
import { DOCUMENT, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { TranslocoDirective } from '@jsverse/transloco';
import { ApiErrors } from '../core/api-error';
import { McpConnection, McpService } from '../core/projects/mcp.service';
import { Alert, Button, Skeleton } from '../shared/ui';

/** What the dialog shows a copy button for. */
type Copyable = 'address' | 'clientId' | 'command';

/** The name the Claude Code command gives the server. */
const serverName = 'tamiza';

/** The command that adds Tamiza to Claude Code with the registered client and its fixed callback port. */
export function claudeCodeCommand(connection: McpConnection): string {
  return `claude mcp add --transport http --client-id ${connection.clientId} --callback-port ${connection.callbackPort} ${serverName} ${connection.url}`;
}

/**
 * How a person adds Tamiza to an AI client: the MCP address and the client ID with copy buttons, the steps for claude.ai and Claude
 * Desktop, the command for Claude Code, how to revoke a connection and what projects a client can reach. The details come from the
 * API when the dialog opens. Open it with `maxWidth: '100vw'` so the panel can use the whole width of a phone.
 */
@Component({
  selector: 'app-connect-ai-client-dialog',
  imports: [NgTemplateOutlet, TranslocoDirective, Alert, Button, Skeleton],
  template: `
    <div
      *transloco="let t"
      class="flex max-h-[calc(100dvh-2rem)] w-[min(36rem,calc(100vw-2rem))] flex-col rounded-base border border-default bg-neutral-primary-soft shadow-sm"
      data-testid="connect-dialog"
    >
      <div class="flex items-start justify-between gap-3 border-b border-default p-4 sm:px-6">
        <h2 id="connect-ai-client-title" class="text-lg font-medium text-heading">{{ t('connect.title') }}</h2>
        <button
          type="button"
          class="-me-2 -mt-1 inline-flex min-h-10 min-w-10 shrink-0 items-center justify-center rounded-base text-body hover:bg-neutral-secondary-medium focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
          [attr.aria-label]="t('connect.close')"
          (click)="ref.close()"
          data-testid="connect-x"
        >
          <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
        </button>
      </div>

      <div class="flex min-w-0 flex-col gap-6 overflow-y-auto p-4 sm:px-6">
        @if (connection(); as connection) {
          <p class="text-sm text-body">{{ t('connect.intro') }}</p>

          <dl class="flex flex-col gap-4">
            <div>
              <dt class="mb-1 text-sm font-medium text-heading">{{ t('connect.address') }}</dt>
              <dd>
                <ng-container
                  *ngTemplateOutlet="copyable; context: { key: 'address', value: connection.url, label: t('connect.copyAddress'), testId: 'address' }"
                />
              </dd>
            </div>
            <div>
              <dt class="mb-1 text-sm font-medium text-heading">{{ t('connect.clientId') }}</dt>
              <dd>
                <ng-container
                  *ngTemplateOutlet="copyable; context: { key: 'clientId', value: connection.clientId, label: t('connect.copyClientId'), testId: 'client-id' }"
                />
              </dd>
            </div>
          </dl>

          <section aria-labelledby="connect-claude-ai-title" class="flex flex-col gap-2">
            <h3 id="connect-claude-ai-title" class="text-sm font-semibold text-heading">{{ t('connect.claudeAi.title') }}</h3>
            <ol class="list-decimal space-y-1 ps-5 text-sm text-body" data-testid="connect-claude-ai-steps">
              @for (step of claudeAiSteps; track step) {
                <li>{{ t('connect.claudeAi.steps.' + step) }}</li>
              }
            </ol>
          </section>

          <section aria-labelledby="connect-claude-code-title" class="flex flex-col gap-2">
            <h3 id="connect-claude-code-title" class="text-sm font-semibold text-heading">{{ t('connect.claudeCode.title') }}</h3>
            <p class="text-sm text-body">{{ t('connect.claudeCode.intro') }}</p>
            <ng-container
              *ngTemplateOutlet="copyable; context: { key: 'command', value: command(), label: t('connect.copyCommand'), testId: 'command' }"
            />
            <p class="text-sm text-body">{{ t('connect.claudeCode.then') }}</p>
          </section>

          <section aria-labelledby="connect-revoke-title" class="flex flex-col gap-2">
            <h3 id="connect-revoke-title" class="text-sm font-semibold text-heading">{{ t('connect.revoke.title') }}</h3>
            <p class="text-sm text-body">{{ t('connect.revoke.body') }}</p>
            <p class="text-sm">
              <a
                [href]="connection.accountUrl"
                target="_blank"
                rel="noopener noreferrer"
                class="font-medium text-fg-brand underline break-all"
                data-testid="connect-revoke-link"
                >{{ t('connect.revoke.link') }}</a
              >
            </p>
          </section>

          <p class="border-t border-default pt-4 text-sm text-body" data-testid="connect-projects-note">{{ t('connect.projects') }}</p>
        } @else if (failure(); as failure) {
          <tmz-alert tone="danger" data-testid="connect-error">{{ t(failure === 'unavailable' ? 'connect.unavailable' : 'connect.loadFailed') }}</tmz-alert>
        } @else {
          <div aria-busy="true" class="flex flex-col gap-3" data-testid="connect-loading">
            <tmz-skeleton class="w-full" /><tmz-skeleton class="w-3/4" /><tmz-skeleton class="w-1/2" />
          </div>
        }

        <ng-template #copyable let-key="key" let-value="value" let-label="label" let-testId="testId">
          <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
            <code
              class="min-w-0 flex-1 break-all rounded-base border border-default bg-neutral-secondary-medium px-3 py-2 text-sm text-heading"
              [attr.data-testid]="'connect-' + testId"
              >{{ value }}</code
            >
            <button
              tmzButton
              variant="secondary"
              size="sm"
              type="button"
              class="shrink-0 self-start sm:self-auto"
              [attr.aria-label]="label"
              [attr.title]="label"
              (click)="copy(key, value)"
              [attr.data-testid]="'copy-' + testId"
            >
              <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V6a2 2 0 0 1 2-2h9" /></svg>
            </button>
          </div>
        </ng-template>
      </div>

      <div class="flex items-center justify-between gap-3 border-t border-default p-4 sm:px-6">
        <p
          role="status"
          class="min-w-0 flex-1 text-sm"
          [class.text-fg-success-strong]="!copyFailed()"
          [class.text-fg-danger-strong]="copyFailed()"
          data-testid="connect-copy-status"
        >
          @if (copyFailed()) {
            {{ t('connect.copyFailed') }}
          } @else if (copied()) {
            {{ t('connect.copied') }}
          }
        </p>
        <button tmzButton class="shrink-0" variant="secondary" type="button" (click)="ref.close()" data-testid="connect-close">{{ t('connect.close') }}</button>
      </div>
    </div>
  `,
})
export class ConnectAiClientDialog {
  protected readonly ref = inject<DialogRef<void>>(DialogRef);
  private readonly service = inject(McpService);
  private readonly errors = inject(ApiErrors);
  private readonly document = inject(DOCUMENT);

  protected readonly claudeAiSteps = ['open', 'address', 'client', 'signIn'];
  protected readonly connection = signal<McpConnection | null>(null);
  /** Why the details are not shown: the server is not enabled (404), or the read failed. */
  protected readonly failure = signal<'unavailable' | 'failed' | null>(null);
  protected readonly command = computed(() => (this.connection() ? claudeCodeCommand(this.connection()!) : ''));
  protected readonly copied = signal<Copyable | null>(null);
  protected readonly copyFailed = signal(false);
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    this.service.connection().subscribe({
      next: (connection) => this.connection.set(connection),
      error: (error) => this.failure.set(this.errors.parse(error).status === 404 ? 'unavailable' : 'failed'),
    });
  }

  /** Copies `value`, and says so (or that it could not) in the status line, which screen readers announce. */
  protected async copy(key: Copyable, value: string): Promise<void> {
    clearTimeout(this.timer);
    try {
      await this.document.defaultView!.navigator.clipboard.writeText(value);
      this.copied.set(key);
      this.copyFailed.set(false);
    } catch {
      this.copied.set(null);
      this.copyFailed.set(true);
    }
    this.timer = setTimeout(() => {
      this.copied.set(null);
      this.copyFailed.set(false);
    }, 4000);
  }
}
