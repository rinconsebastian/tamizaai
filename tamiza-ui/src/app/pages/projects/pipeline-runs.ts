import { Dialog } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, output, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { PIPELINE_POLL_MS, PipelineService, RunPage, RunStatus, RunStepStatus, RunSummary, StepSummary } from '../../core/projects/pipeline.service';
import {
  Badge,
  Button,
  Card,
  Skeleton,
  Table,
  TableContainer,
  ToastService,
  Tone,
  tableCell,
  tableHead,
  tableHeaderCell,
  tableRow,
} from '../../shared/ui';
import { durationText } from './pipeline-runs.util';
import { ProjectContext } from './project-context';
import { RunDetailsData, RunDetailsDialog } from './run-details-dialog';

const pageSize = 10;
const runTones: Record<RunStatus, Tone> = { queued: 'neutral', running: 'brand', succeeded: 'success', failed: 'danger' };
const stepTones: Record<RunStepStatus, Tone> = { succeeded: 'success', failed: 'danger', skipped: 'neutral' };

/** "Run now" and the run log, refreshed while a run is queued or running. */
@Component({
  selector: 'app-pipeline-runs',
  imports: [DatePipe, TranslocoDirective, Badge, Button, Card, Skeleton, Table, TableContainer],
  template: `
    <section *transloco="let t">
      <div class="mb-3 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h2 class="text-lg font-semibold text-heading">{{ t('runs.title') }}</h2>
        @if (context.can('analyst')) {
          <button tmzButton type="button" [disabled]="busy()" (click)="start()" data-testid="run-now">{{ t('runs.runNow') }}</button>
        }
      </div>
      @if (log(); as log) {
        @if (log.items.length === 0) {
          <tmz-card><p class="text-sm text-body" data-testid="runs-empty">{{ t('runs.empty') }}</p></tmz-card>
        } @else {
          <ul class="flex flex-col gap-3 md:hidden" data-testid="run-cards">
            @for (run of log.items; track run.id) {
              <li class="rounded-base border border-default bg-neutral-primary-soft p-4 text-sm">
                <div class="mb-2 flex flex-wrap items-center gap-2">
                  <tmz-badge [tone]="tone(run.status)">{{ t('runs.status.' + run.status) }}</tmz-badge>
                  <span class="text-body">{{ t('runs.triggers.' + run.trigger) }}</span>
                </div>
                <p class="text-body">{{ (run.startedAt ?? run.queuedAt) | date: 'medium' }}</p>
                <p class="mt-1 flex flex-wrap gap-1">
                  @for (step of fixedSteps(run); track step.step) {
                    <tmz-badge [tone]="stepTone(step.status)">{{ t('runs.steps.' + step.step) }}</tmz-badge>
                  }
                  @if (indicatorSteps(run).length) {
                    <tmz-badge [tone]="stepTone(indicatorStatus(run))">{{ indicatorsSummary(run) }}</tmz-badge>
                  }
                </p>
                @if (run.error) {
                  <p class="mt-2 break-words text-fg-danger-strong">{{ errorText(run) }}</p>
                }
                <button tmzButton variant="secondary" size="sm" type="button" class="mt-3" (click)="open(run)">{{ t('runs.details') }}</button>
              </li>
            }
          </ul>
          <tmz-table-container class="hidden md:block">
            <table tmzTable data-testid="run-table">
              <thead [class]="tableHead">
                <tr>
                  <th scope="col" [class]="tableHeaderCell">{{ t('runs.columns.started') }}</th>
                  <th scope="col" [class]="tableHeaderCell">{{ t('runs.columns.status') }}</th>
                  <th scope="col" [class]="tableHeaderCell">{{ t('runs.columns.steps') }}</th>
                  <th scope="col" [class]="tableHeaderCell">{{ t('runs.columns.versions') }}</th>
                  <th scope="col" [class]="tableHeaderCell">{{ t('runs.columns.duration') }}</th>
                  <th scope="col" [class]="tableHeaderCell"><span class="sr-only">{{ t('runs.details') }}</span></th>
                </tr>
              </thead>
              <tbody>
                @for (run of log.items; track run.id) {
                  <tr [class]="tableRow" [attr.data-testid]="'run-row-' + run.status">
                    <td [class]="tableCell + ' whitespace-nowrap'">
                      {{ (run.startedAt ?? run.queuedAt) | date: 'medium' }}
                      <span class="block text-xs">{{ t('runs.triggers.' + run.trigger) }}{{ run.requestedBy ? ' · ' + run.requestedBy.name : '' }}</span>
                    </td>
                    <td [class]="tableCell">
                      <tmz-badge [tone]="tone(run.status)">{{ t('runs.status.' + run.status) }}</tmz-badge>
                      @if (run.error) {
                        <span class="mt-1 block max-w-xs break-words text-xs text-fg-danger-strong" data-testid="run-error">{{ errorText(run) }}</span>
                      }
                    </td>
                    <td [class]="tableCell">
                      <span class="flex flex-wrap gap-1">
                        @for (step of fixedSteps(run); track step.step) {
                          <tmz-badge [tone]="stepTone(step.status)">{{ t('runs.steps.' + step.step) }}</tmz-badge>
                        }
                        @if (indicatorSteps(run).length) {
                          <tmz-badge [tone]="stepTone(indicatorStatus(run))" data-testid="run-indicators">{{ indicatorsSummary(run) }}</tmz-badge>
                        }
                      </span>
                    </td>
                    <td [class]="tableCell + ' whitespace-nowrap'">{{ versions(run) }}</td>
                    <td [class]="tableCell + ' whitespace-nowrap'">{{ duration(run.startedAt, run.finishedAt) ?? '—' }}</td>
                    <td [class]="tableCell">
                      <button tmzButton variant="secondary" size="sm" type="button" (click)="open(run)" [attr.data-testid]="'run-details-' + run.id">
                        {{ t('runs.details') }}
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </tmz-table-container>
          @if (log.total > pageSize) {
            <nav class="mt-3 flex flex-wrap items-center justify-between gap-3 text-sm text-body" [attr.aria-label]="t('runs.title')">
              <span data-testid="runs-page-info">{{ t('frame.pageInfo', { from: pageFrom(), to: pageTo(), total: log.total }) }}</span>
              <div class="flex gap-2">
                <button tmzButton variant="secondary" size="sm" type="button" [disabled]="page() === 1" (click)="load(page() - 1)">{{ t('frame.previous') }}</button>
                <button tmzButton variant="secondary" size="sm" type="button" [disabled]="pageTo() >= log.total" (click)="load(page() + 1)" data-testid="runs-next">
                  {{ t('frame.next') }}
                </button>
              </div>
            </nav>
          }
        }
      } @else {
        <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-full" /></div>
      }
    </section>
  `,
})
export class PipelineRuns {
  private readonly service = inject(PipelineService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(Dialog);
  private readonly pollMs = inject(PIPELINE_POLL_MS);
  protected readonly context = inject(ProjectContext);

  protected readonly pageSize = pageSize;
  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;
  protected readonly duration = durationText;

  /** Emits when a run that was queued or running has ended, so the tables it published can be read again. */
  readonly runEnded = output<void>();

  protected readonly log = signal<RunPage | null>(null);
  protected readonly page = signal(1);
  protected readonly busy = signal(false);
  protected readonly pageFrom = computed(() => (this.page() - 1) * pageSize + 1);
  protected readonly pageTo = computed(() => Math.min(this.page() * pageSize, this.log()?.total ?? 0));
  private readonly active = computed(() => !!this.log()?.items.some((run) => run.status === 'queued' || run.status === 'running'));

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  constructor() {
    this.load(1);
    // While a run is queued or running, refresh the list until it ends.
    effect(() => {
      this.log();
      this.clearPoll();
      if (this.active() && !this.destroyed) {
        this.pollTimer = setTimeout(() => this.load(this.page()), this.pollMs);
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.clearPoll();
    });
  }

  protected start(): void {
    this.busy.set(true);
    this.service.startRun(this.projectId()).subscribe({
      next: () => {
        this.busy.set(false);
        this.toasts.success(this.transloco.translate('runs.started'));
        this.load(1);
      },
      error: (error) => {
        this.busy.set(false);
        this.errors.report(error);
      },
    });
  }

  protected load(page: number): void {
    this.service.runs(this.projectId(), page, pageSize).subscribe({
      next: (log) => {
        if (!this.destroyed) {
          const wasActive = this.active();
          this.page.set(page);
          this.log.set(log);
          if (wasActive && !this.active()) {
            this.runEnded.emit();
          }
        }
      },
      error: (error) => this.errors.report(error),
    });
  }

  protected open(run: RunSummary): void {
    this.dialog.open<void, RunDetailsData>(RunDetailsDialog, {
      data: { projectId: this.projectId(), runId: run.id },
      ariaLabelledBy: 'run-details-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
  }

  protected tone(status: RunStatus): Tone {
    return runTones[status];
  }

  protected stepTone(status: RunStepStatus): Tone {
    return stepTones[status];
  }

  protected fixedSteps(run: RunSummary): StepSummary[] {
    return run.steps.filter((step) => step.step !== 'indicator');
  }

  protected indicatorSteps(run: RunSummary): StepSummary[] {
    return run.steps.filter((step) => step.step === 'indicator');
  }

  /** The indicators as one badge: failed when any failed, succeeded when any did, skipped when none ran. */
  protected indicatorStatus(run: RunSummary): RunStepStatus {
    const steps = this.indicatorSteps(run);
    return steps.some((s) => s.status === 'failed') ? 'failed' : steps.some((s) => s.status === 'succeeded') ? 'succeeded' : 'skipped';
  }

  protected indicatorsSummary(run: RunSummary): string {
    const steps = this.indicatorSteps(run);
    return this.transloco.translate('runs.indicatorsSummary', { done: steps.filter((s) => s.status === 'succeeded').length, total: steps.length });
  }

  /** The versions of script 1 and script 2 the run used, then how many indicators it ran. */
  protected versions(run: RunSummary): string {
    const v = run.versions;
    const fixed = [v.clean, v.output].map((n) => (n === null ? '—' : `v${n}`));
    const count = v.indicators.length;
    return [...fixed, this.transloco.translate(count === 1 ? 'runs.indicatorCountOne' : 'runs.indicatorCount', { count })].join(' / ');
  }

  /** The translated message for the run's error code, or the API's text. */
  protected errorText(run: RunSummary): string {
    if (run.errorCode) {
      const key = `errors.codes.${run.errorCode}`;
      const translated = this.transloco.translate(key);
      if (translated && translated !== key) {
        return run.error && run.errorCode.startsWith('step.') ? `${translated} ${run.error}` : translated;
      }
    }
    return run.error ?? this.transloco.translate('errors.generic');
  }

  private clearPoll(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private projectId(): string {
    return this.context.project()!.id;
  }
}
