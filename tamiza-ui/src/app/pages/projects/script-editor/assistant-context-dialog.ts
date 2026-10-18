import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, LOCALE_ID, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ApiErrors } from '../../../core/api-error';
import { AssistantContext, AssistantRequest, AssistantService } from '../../../core/projects/assistant.service';
import { Alert, ApiLang, Button, Skeleton, numberText } from '../../../shared/ui';

export interface AssistantContextData {
  projectId: string;
  /** The request the next send would make; the API assembles its context without calling the provider. */
  request: AssistantRequest;
}

/** The exact text the next request would send to the model, and what the API left out to fit its size limit. */
@Component({
  selector: 'app-assistant-context-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, Skeleton],
  template: `
    <div
      class="flex max-h-[calc(100vh-2rem)] w-[min(56rem,calc(100vw-2rem))] flex-col rounded-base border border-default bg-neutral-primary-soft shadow-sm"
      *transloco="let t"
    >
      <header class="flex items-start justify-between gap-3 border-b border-default p-4 md:px-6">
        <h2 id="assistant-context-title" class="text-lg font-medium text-heading">
          {{ t('scripts.assistant.contextTitle') }}
          @if (context(); as context) {
            <span class="block text-sm font-normal text-body" data-testid="context-summary">
              {{ t('scripts.assistant.model', { model: context.model, host: context.host }) }}
              · {{ t('scripts.assistant.contextSize', { size: kilobytes(context.bytes) }) }}
            </span>
          }
        </h2>
        <button tmzButton variant="ghost" size="sm" type="button" (click)="ref.close()" data-testid="close-context">{{ t('scripts.assistant.close') }}</button>
      </header>
      <div class="flex min-w-0 flex-col gap-3 overflow-y-auto p-4 md:px-6">
        @if (failure(); as message) {
          <tmz-alert tone="danger" data-testid="context-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
        } @else if (context(); as context) {
          <p class="text-sm text-body">{{ t('scripts.assistant.contextIntro') }}</p>
          @if (context.omitted.length) {
            <div class="text-sm text-body" data-testid="context-omitted">
              <p class="font-medium text-heading">{{ t('scripts.assistant.contextOmitted') }}</p>
              <ul class="list-disc ps-5">
                @for (entry of context.omitted; track entry) {
                  <li lang="en">{{ entry }}</li>
                }
              </ul>
            </div>
          }
          <pre
            lang="en"
            class="max-h-[60vh] overflow-auto rounded-base border border-default bg-neutral-secondary-soft p-3 text-xs break-words whitespace-pre-wrap text-body"
            tabindex="0"
            data-testid="context-text"
          >{{ context.text }}</pre>
        } @else {
          <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
        }
      </div>
    </div>
  `,
})
export class AssistantContextDialog {
  private readonly locale = inject(LOCALE_ID);
  private readonly data = inject<AssistantContextData>(DIALOG_DATA);
  private readonly service = inject(AssistantService);
  private readonly errors = inject(ApiErrors);
  protected readonly ref = inject<DialogRef<void>>(DialogRef);

  protected readonly context = signal<AssistantContext | null>(null);
  protected readonly failure = signal<string | null>(null);

  constructor() {
    this.service.context(this.data.projectId, this.data.request).subscribe({
      next: (context) => this.context.set(context),
      error: (error) => this.failure.set(this.errors.parse(error).message),
    });
  }

  protected kilobytes(bytes: number): string {
    return numberText(bytes / 1024, this.locale, '1.1-1') ?? '';
  }
}
