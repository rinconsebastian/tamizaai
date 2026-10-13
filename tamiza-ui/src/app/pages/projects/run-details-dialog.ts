import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { PipelineService, RunDetails, RunStepStatus } from '../../core/projects/pipeline.service';
import { Badge, Button, Skeleton, Tone } from '../../shared/ui';
import { durationText } from './pipeline-runs.util';

export interface RunDetailsData {
  projectId: string;
  runId: string;
}

const stepTones: Record<RunStepStatus, Tone> = { succeeded: 'success', failed: 'danger', skipped: 'neutral' };

/** One section per step: status, duration, rows, tables, log and error. */
@Component({
  selector: 'app-run-details-dialog',
  imports: [DatePipe, TranslocoDirective, Badge, Button, Skeleton],
  template: `
    <div
      class="flex max-h-[calc(100vh-2rem)] w-[min(56rem,calc(100vw-2rem))] flex-col rounded-base border border-default bg-neutral-primary-soft shadow-sm"
      *transloco="let t"
    >
      <header class="flex items-start justify-between gap-3 border-b border-default p-4 md:px-6">
        <h2 id="run-details-title" class="text-lg font-medium text-heading">
          {{ t('runs.detailsTitle') }}
          @if (details(); as details) {
            <span class="block text-sm font-normal text-body">{{ (details.run.startedAt ?? details.run.queuedAt) | date: 'medium' }} · {{ t('runs.triggers.' + details.run.trigger) }}</span>
          }
        </h2>
        <button tmzButton variant="ghost" size="sm" type="button" (click)="ref.close()" data-testid="close-run">{{ t('runs.close') }}</button>
      </header>
      <div class="overflow-y-auto p-4 md:px-6">
        @if (details(); as details) {
          <ol class="flex flex-col gap-5">
            @for (step of details.steps; track step.step) {
              <li [attr.data-testid]="'step-' + step.step">
                <div class="mb-2 flex flex-wrap items-center gap-2">
                  <h3 class="font-medium text-heading">{{ t('runs.steps.' + step.step) }}</h3>
                  <tmz-badge [tone]="tone(step.status)">{{ t('runs.stepStatus.' + step.status) }}</tmz-badge>
                  @if (duration(step.startedAt, step.finishedAt); as duration) {
                    <span class="text-sm text-body">{{ duration }}</span>
                  }
                  @if (step.inputRows !== null || step.outputRows !== null) {
                    <span class="text-sm text-body">{{ t('runs.rows', { input: step.inputRows ?? '—', output: step.outputRows ?? '—' }) }}</span>
                  }
                </div>
                @if (step.tables.length) {
                  <p class="mb-2 text-sm text-body" data-testid="step-tables">
                    {{ t('runs.tables') }}
                    @for (table of step.tables; track table.name; let last = $last) {
                      <code class="text-heading">{{ table.name }}</code> ({{ table.rows }}){{ last ? '' : ', ' }}
                    }
                  </p>
                }
                @if (step.error) {
                  <pre class="mb-2 max-h-60 overflow-auto rounded-base border border-danger-subtle bg-danger-soft p-3 text-xs whitespace-pre-wrap text-fg-danger-strong" data-testid="step-error">{{ step.error }}</pre>
                }
                @if (step.log) {
                  <pre class="max-h-60 overflow-auto rounded-base border border-default bg-neutral-secondary-soft p-3 text-xs whitespace-pre-wrap text-body" data-testid="step-log">{{ step.log }}</pre>
                }
              </li>
            }
          </ol>
          <section class="mt-5 border-t border-default pt-4" data-testid="run-reference">
            <h3 class="mb-2 font-medium text-heading">{{ t('runs.referenceTitle') }}</h3>
            @if (details.referenceTables.length) {
              <ul class="flex flex-col gap-1 text-sm text-body">
                @for (table of details.referenceTables; track table.name) {
                  <li>
                    <code class="text-heading">{{ table.name }}</code>
                    {{ t('runs.referenceTable', { rows: table.rows, updated: (table.updatedAt | date: 'medium') }) }}
                  </li>
                }
              </ul>
            } @else {
              <p class="text-sm text-body">{{ t('runs.noReference') }}</p>
            }
          </section>
        } @else {
          <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
        }
      </div>
    </div>
  `,
})
export class RunDetailsDialog {
  protected readonly data = inject<RunDetailsData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<void>>(DialogRef);
  private readonly service = inject(PipelineService);
  private readonly errors = inject(ApiErrors);

  protected readonly details = signal<RunDetails | null>(null);
  protected readonly duration = durationText;

  constructor() {
    this.service.run(this.data.projectId, this.data.runId).subscribe({
      next: (details) => this.details.set(details),
      error: (error) => {
        this.errors.report(error);
        this.ref.close();
      },
    });
  }

  protected tone(status: RunStepStatus): Tone {
    return stepTones[status];
  }
}
