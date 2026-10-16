import { Dialog } from '@angular/cdk/dialog';
import { formatDate } from '@angular/common';
import { Component, ElementRef, OnInit, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiError, ApiErrors } from '../../../core/api-error';
import { AssistantService, AssistantSettings } from '../../../core/projects/assistant.service';
import type { IndicatorMetadata, ScriptKind } from '../../../core/projects/pipeline.service';
import { Alert, Badge, Button, Input, ToastService, dateTimeFormat } from '../../../shared/ui';
import {
  ASSISTANT_SESSION,
  ApplyRequest,
  AssistantScope,
  RequestFrame,
  cellRange,
  maxRequestBytes,
  requestBytes,
} from './assistant-state';
import { AssistantContextData, AssistantContextDialog } from './assistant-context-dialog';
import { cellAt, cells, signatureOf } from './script-analysis';

/** The counter appears once a request reaches this share of the limit. */
const counterFromBytes = (maxRequestBytes * 3) / 4;

const kilobytes = (bytes: number) => (bytes / 1024).toFixed(1);

/**
 * The Assistant tab of the Scripts side panel: it renders the state of the selected script that the page-scoped session holds (the
 * thread, the request box, the scope, the proposal, the failure and the request being written) and sends requests through it.
 * It keeps nothing that must outlive the tab, which the side panel destroys while another tab is shown.
 */
@Component({
  selector: 'app-assistant-panel',
  imports: [TranslocoDirective, Alert, Badge, Button, Input],
  template: `
    <div class="flex min-w-0 flex-col gap-3 text-sm" *transloco="let t" data-testid="assistant-panel">
      <div class="flex flex-col gap-1">
        <p class="text-body" data-testid="assistant-disclosure">
          @if (settings(); as settings) {
            <span class="font-medium text-heading" data-testid="assistant-model">{{ t('scripts.assistant.model', { model: settings.model, host: settings.host }) }}</span>
          }
          {{ t('scripts.assistant.disclosure') }}
        </p>
        @if (!viewing()) {
          <div class="flex flex-wrap gap-x-1">
            <button tmzButton variant="ghost" size="sm" type="button" (click)="showContext()" data-testid="assistant-show-context">{{ t('scripts.assistant.showContext') }}</button>
            <button tmzButton variant="ghost" size="sm" type="button" [disabled]="copying()" (click)="copyContext()" data-testid="assistant-copy-context">{{ t('scripts.assistant.copyContext') }}</button>
          </div>
        }
      </div>

      @if (thread().length || pendingHere()) {
        <ol class="flex max-h-96 flex-col gap-2 overflow-y-auto" data-testid="assistant-thread">
          @for (turn of thread(); track $index) {
            <li class="min-w-0 rounded-base border px-3 py-2" [class]="turnClasses(turn.role)" [attr.data-testid]="'assistant-turn-' + turn.role">
              <span class="mb-1 block text-xs font-medium text-body">{{ t('scripts.assistant.roles.' + turn.role) }}</span>
              <span class="block break-words whitespace-pre-wrap text-heading">{{ turn.content }}</span>
              @if (turn.testExpired) {
                <span class="mt-1 block text-xs text-fg-warning" data-testid="assistant-test-expired">{{ t('scripts.assistant.testExpired') }}</span>
              }
            </li>
          }
          @if (pending(); as request) {
            @if (request.here) {
              <li class="min-w-0 rounded-base border px-3 py-2" [class]="turnClasses('user')" data-testid="assistant-turn-pending">
                <span class="mb-1 block text-xs font-medium text-body">{{ t('scripts.assistant.roles.user') }}</span>
                <span class="block break-words whitespace-pre-wrap text-heading">{{ request.text }}</span>
              </li>
            }
          }
        </ol>
      }

      @if (pending(); as request) {
        @if (request.here) {
          <div class="flex flex-wrap items-center gap-x-3 gap-y-2" data-testid="assistant-writing">
            <span class="text-body">
              <span role="status">{{ t('scripts.assistant.writing') }}</span>
              <span aria-hidden="true" class="ms-1" data-testid="assistant-elapsed">{{ t('scripts.assistant.seconds', { count: elapsed() }) }}</span>
            </span>
            <button tmzButton variant="secondary" size="sm" type="button" (click)="session.cancel()" data-testid="assistant-cancel">{{ t('common.cancel') }}</button>
          </div>
        } @else {
          <tmz-alert tone="brand" data-testid="assistant-pending-elsewhere">{{ t('scripts.assistant.pendingElsewhere', { script: request.scriptLabel }) }}</tmz-alert>
        }
      }

      @if (viewing()) {
        <tmz-alert tone="brand" data-testid="assistant-viewing-older">{{ t('scripts.assistant.viewingOlder') }}</tmz-alert>
      } @else {
        @if (failure(); as failure) {
          <tmz-alert tone="danger" data-testid="assistant-failure">
            <span data-testid="assistant-failure-message">{{ failureText(failure) }}</span>
            <button tmzButton variant="ghost" size="sm" type="button" class="ms-2" (click)="session.dismissFailure()" data-testid="assistant-failure-dismiss">{{ t('scripts.assistant.dismiss') }}</button>
          </tmz-alert>
        }

        @if (proposal(); as proposal) {
          <section class="flex min-w-0 flex-col gap-3 rounded-base border border-default p-3" aria-labelledby="assistant-proposal-title" data-testid="assistant-proposal">
            <h3 id="assistant-proposal-title" class="font-medium text-heading">{{ t('scripts.assistant.proposal') }}</h3>
            @for (note of proposal.notes; track note) {
              <p class="text-xs text-body" [attr.data-testid]="'assistant-note-' + note">{{ t('scripts.assistant.notes.' + note) }}</p>
            }
            @for (block of proposal.blocks; track $index; let position = $index) {
              <div class="flex min-w-0 flex-col gap-2" [attr.data-testid]="'assistant-block-' + position">
                <p class="text-body" data-testid="assistant-block-summary">
                  @if (proposal.blocks.length > 1) {
                    <span class="font-medium text-heading">{{ t('scripts.assistant.blockNumber', { number: position + 1 }) }}</span> ·
                  }
                  {{ block.lines === 1 ? t('scripts.assistant.oneLine') : t('scripts.assistant.lines', { lines: block.lines }) }}
                  ·
                  {{ block.definesContract ? t('scripts.assistant.definesContract', { signature: signature() }) : t('scripts.assistant.noContract', { signature: signature() }) }}
                </p>
                @if (block.marks.length) {
                  <div data-testid="assistant-marks">
                    <p class="text-xs font-medium text-heading">{{ t('scripts.assistant.marksTitle') }}</p>
                    <ul class="mt-1 flex flex-col gap-1">
                      @for (mark of block.marks; track $index) {
                        <li class="flex items-baseline gap-2">
                          <tmz-badge [tone]="mark.severity === 'error' ? 'danger' : 'warning'">{{ t('scripts.editor.problemLine', { line: mark.line }) }}</tmz-badge>
                          <span class="min-w-0 break-words text-body">{{ mark.message }}</span>
                        </li>
                      }
                    </ul>
                  </div>
                }
                <div class="flex flex-wrap gap-2">
                  @for (offer of block.offers; track offer) {
                    <button
                      tmzButton
                      size="sm"
                      type="button"
                      [variant]="offer === 'insert' ? 'secondary' : 'primary'"
                      (click)="apply.emit({ block: position, action: offer })"
                      [attr.data-testid]="'assistant-apply-' + offer + '-' + position"
                    >
                      {{ t('scripts.assistant.offers.' + offer) }}
                    </button>
                  }
                </div>
              </div>
            }
          </section>
        }

        <div class="flex flex-col gap-2" role="group" [attr.aria-label]="t('scripts.assistant.scopeLabel')" data-testid="assistant-scopes">
          <div class="flex flex-wrap gap-2">
            <button
              tmzButton
              size="sm"
              type="button"
              [variant]="scope() === 'script' ? 'primary' : 'secondary'"
              [attr.aria-pressed]="scope() === 'script'"
              (click)="session.setScope('script')"
              data-testid="assistant-scope-script"
            >
              {{ t('scripts.assistant.scopes.script') }}
            </button>
            @if (cellName(); as name) {
              <button
                tmzButton
                size="sm"
                type="button"
                [variant]="scope() === 'cell' ? 'primary' : 'secondary'"
                [attr.aria-pressed]="scope() === 'cell'"
                (click)="session.setScope('cell')"
                data-testid="assistant-scope-cell"
              >
                {{ t('scripts.assistant.scopes.cell', { name }) }}
              </button>
            }
            @if (fixTarget()) {
              <button
                tmzButton
                size="sm"
                type="button"
                [variant]="scope() === 'fix' ? 'primary' : 'secondary'"
                [attr.aria-pressed]="scope() === 'fix'"
                (click)="session.setScope('fix')"
                data-testid="assistant-scope-fix"
              >
                {{ t('scripts.assistant.scopes.fix') }}
              </button>
            }
          </div>
          @if (!cellName()) {
            <p class="text-xs text-body" data-testid="assistant-needs-cell">{{ t('scripts.assistant.needsCell') }}</p>
          }
          @if (fixLost()) {
            <p class="text-xs text-fg-warning" data-testid="assistant-test-again">{{ t('scripts.assistant.testAgain') }}</p>
          }
          @if (scope() === 'fix') {
            <p class="text-xs text-body" data-testid="assistant-fix-note">{{ t('scripts.assistant.fixNote') }}</p>
          }
        </div>

        <div class="flex flex-col gap-1">
          <label class="font-medium text-heading" for="assistant-request">{{ t('scripts.assistant.requestLabel') }}</label>
          <textarea
            #requestBox
            tmzInput
            id="assistant-request"
            rows="5"
            class="resize-y"
            [value]="text()"
            [placeholder]="t('scripts.assistant.placeholders.' + scope())"
            (input)="session.setText($any($event.target).value)"
            (keydown.control.enter)="send($event)"
            (keydown.meta.enter)="send($event)"
            data-testid="assistant-request"
          ></textarea>
          @if (tooLong()) {
            <p class="text-xs text-fg-danger-strong" role="alert" data-testid="assistant-too-long">{{ t('scripts.assistant.tooLong', { max: limit, size: size() }) }}</p>
          } @else if (showCounter()) {
            <p class="text-xs text-body" data-testid="assistant-counter">{{ t('scripts.assistant.counter', { max: limit, size: size() }) }}</p>
          }
        </div>

        <div class="flex flex-wrap items-center gap-2">
          <button tmzButton type="button" [disabled]="!canSend()" (click)="send()" data-testid="assistant-send">{{ t('scripts.assistant.send') }}</button>
          @if (details()) {
            <button tmzButton variant="secondary" type="button" (click)="draftFromDetails()" data-testid="assistant-draft-details">{{ t('scripts.assistant.draftFromDetails') }}</button>
          }
          @if (thread().length || proposal()) {
            <button tmzButton variant="ghost" type="button" [disabled]="pendingHere()" (click)="session.newConversation()" data-testid="assistant-new-conversation">
              {{ t('scripts.assistant.newConversation') }}
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class AssistantPanel implements OnInit {
  /** The project, for the disclosure line and the context routes. */
  readonly projectId = input.required<string>();
  readonly kind = input.required<ScriptKind>();
  /** The selected indicator's name; null for script 1 and script 2. */
  readonly scriptName = input<string | null>(null);
  /** The editor's text, saved or not. */
  readonly draft = input.required<string>();
  /** The 1-based line the cursor is on; it moves the "This cell" scope. */
  readonly cursorLine = input(1);
  /** The indicator's details as edited above the editor; null for script 1 and script 2. */
  readonly details = input<IndicatorMetadata | null>(null);
  /** An older version is on view: the assistant works on the current text and offers no new request. */
  readonly viewing = input(false);
  /** The analyst chose an action of a proposal; the page puts the code in the editor. */
  readonly apply = output<ApplyRequest>();

  protected readonly session = inject(ASSISTANT_SESSION);
  private readonly service = inject(AssistantService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(Dialog);
  private readonly requestBox = viewChild<ElementRef<HTMLTextAreaElement>>('requestBox');

  protected readonly thread = this.session.thread;
  protected readonly text = this.session.text;
  protected readonly proposal = this.session.proposal;
  protected readonly failure = this.session.failure;
  protected readonly pending = this.session.pending;
  protected readonly fixTarget = this.session.fixTarget;
  protected readonly limit = maxRequestBytes / 1024;
  protected readonly settings = signal<AssistantSettings | null>(null);
  protected readonly copying = signal(false);
  private readonly now = signal(Date.now());

  protected readonly signature = computed(() => signatureOf(this.kind()));
  private readonly cellHit = computed(() => cellAt(cells(this.draft()), this.cursorLine()));
  /** The name of the cell at the cursor, or null when the cursor is outside any cell (the scope is then not offered). */
  protected readonly cellName = computed(() => {
    const hit = this.cellHit();
    return hit ? hit.cell.name || this.transloco.translate('scripts.assistant.cellUnnamed', { number: hit.index + 1 }) : null;
  });
  /** The scope the next send uses: the chosen one, or the script while the cell or the fix is not available. */
  protected readonly scope = computed<AssistantScope>(() => {
    const chosen = this.session.scope();
    if ((chosen === 'cell' && !this.cellHit()) || (chosen === 'fix' && !this.fixTarget())) {
      return 'script';
    }
    return chosen;
  });
  protected readonly fixLost = computed(() => this.session.scope() === 'fix' && !this.fixTarget());
  protected readonly pendingHere = computed(() => this.pending()?.here === true);
  protected readonly elapsed = computed(() => {
    const request = this.pending();
    return request ? Math.max(0, Math.floor((this.now() - request.startedAt) / 1000)) : 0;
  });
  private readonly bytes = computed(() => requestBytes(this.text()));
  protected readonly size = computed(() => kilobytes(this.bytes()));
  protected readonly tooLong = computed(() => this.bytes() > maxRequestBytes);
  protected readonly showCounter = computed(() => this.bytes() >= counterFromBytes);
  protected readonly canSend = computed(() => !this.viewing() && !this.pending() && !!this.text().trim() && !this.tooLong());

  constructor() {
    // The seconds of the request being written tick while it belongs to the selected script.
    effect((onCleanup) => {
      if (this.pendingHere()) {
        this.now.set(Date.now());
        const timer = setInterval(() => this.now.set(Date.now()), 1000);
        onCleanup(() => clearInterval(timer));
      }
    });
  }

  ngOnInit(): void {
    this.service.settings(this.projectId()).subscribe({
      next: (settings) => this.settings.set(settings),
      // The disclosure sentence stands without the model; a failure shows itself when the first request is made.
      error: () => this.settings.set(null),
    });
  }

  /** Puts the focus on the request box (the page does it after "Ask the assistant"). */
  focusRequest(): void {
    this.requestBox()?.nativeElement.focus();
  }

  protected turnClasses(role: 'user' | 'assistant'): string {
    return role === 'user' ? 'border-default bg-neutral-secondary-soft' : 'border-brand-subtle bg-brand-softer';
  }

  /** What the editor and the cursor say about the request: the same for sending and for looking at the context. */
  private frame(): RequestFrame {
    const action = this.scope();
    const hit = this.cellHit();
    const text = this.draft();
    const cell = action === 'cell' && hit ? { line: hit.cell.line, text: cellRange(text, cells(text), hit.index).text } : null;
    return { action, draft: text, cell, details: this.details() };
  }

  protected send(event?: Event): void {
    event?.preventDefault();
    if (this.canSend()) {
      this.session.send(this.frame());
    }
  }

  /** Fills the box from the indicator's details as they are in the form now, saved or not; the analyst can edit it. */
  protected draftFromDetails(): void {
    const details = this.details();
    if (!details) {
      return;
    }
    const t = (key: string, params: Record<string, unknown>) => this.transloco.translate(`scripts.assistant.detailsRequest.${key}`, params);
    const lines = [t('intro', { label: details.label.trim() || this.scriptName() || '' })];
    if (details.description.trim()) {
      lines.push(t('description', { description: details.description.trim() }));
    }
    if (details.unit.trim()) {
      lines.push(t('unit', { unit: details.unit.trim() }));
    }
    lines.push(t('format', { format: details.format }));
    const current = this.text().trimEnd();
    this.session.setText(current ? `${current}\n\n${lines.join('\n')}` : lines.join('\n'));
    this.session.setScope('script');
    this.focusRequest();
  }

  protected showContext(): void {
    this.dialog.open<void, AssistantContextData>(AssistantContextDialog, {
      data: { projectId: this.projectId(), request: this.session.requestBody(this.frame()) },
      ariaLabelledBy: 'assistant-context-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
  }

  /** Copies the text "Show what will be sent" displays, built by the same assembly as a request. */
  protected copyContext(): void {
    this.copying.set(true);
    this.service.context(this.projectId(), this.session.requestBody(this.frame())).subscribe({
      next: async (context) => {
        try {
          await navigator.clipboard.writeText(context.text);
          this.toasts.success(this.transloco.translate('scripts.assistant.contextCopied'));
        } catch {
          this.toasts.error(this.transloco.translate('errors.generic'));
        }
        this.copying.set(false);
      },
      error: (error) => {
        this.copying.set(false);
        this.errors.report(error);
      },
    });
  }

  /** The message of a failure: the translated message of its code, naming the time to retry or when the budget renews when the API said. */
  protected failureText(failure: ApiError): string {
    const retryAfter = failure.extensions['retryAfter'];
    if (failure.code === 'assistant.rateLimited' && typeof retryAfter === 'number' && retryAfter > 0) {
      return this.transloco.translate('errors.codes.assistant.rateLimitedIn', { seconds: Math.ceil(retryAfter) });
    }
    const renewsAt = failure.extensions['renewsAt'];
    if (failure.code === 'assistant.budget' && typeof renewsAt === 'string' && !Number.isNaN(Date.parse(renewsAt))) {
      return this.transloco.translate('errors.codes.assistant.budgetRenews', { when: formatDate(renewsAt, dateTimeFormat, 'en') });
    }
    return failure.message;
  }
}
