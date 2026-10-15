import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { IndicatorMetadata, PipelineService, RunStatus, SCRIPT_TEST_POLL_MS, ScriptTest, ShownPreview, TestStep, TestTable } from '../../core/projects/pipeline.service';
import { Alert, Badge, Card, Input, Tone } from '../../shared/ui';
import { durationText } from './pipeline-runs.util';
import { IndicatorView } from './script-editor/indicator-view';
import { cellAt, cells } from './script-editor/script-analysis';
import { TablePreview } from './table-preview';

const statusTones: Record<RunStatus | 'stopped', Tone> = {
  queued: 'neutral',
  running: 'brand',
  succeeded: 'success',
  failed: 'danger',
  stopped: 'neutral',
};

/** The result of a script test: its status while it waits or runs, then each step with its log and table previews. */
@Component({
  selector: 'app-script-test-panel',
  imports: [DatePipe, TranslocoDirective, Alert, Badge, Card, IndicatorView, Input, TablePreview],
  template: `
    <tmz-card *transloco="let t" data-testid="test-panel">
      @if (test(); as test) {
        <div class="mb-3 flex flex-wrap items-center gap-2">
          <h2 class="text-lg font-semibold text-heading">
            {{ t('scriptTests.title', { kind: kindLabel(test), size: test.sampleSize }) }}
          </h2>
          <tmz-badge [tone]="tone(test.status)" data-testid="test-status">{{ t('runs.status.' + test.status) }}</tmz-badge>
          <span class="text-sm text-body">{{ test.queuedAt | date: 'mediumTime' }}</span>
        </div>

        @if (stale()) {
          <tmz-alert tone="warning" class="mb-3" data-testid="test-stale">{{ t('scriptTests.stale') }}</tmz-alert>
        }
        @if (test.status === 'queued' || test.status === 'running') {
          <p class="mb-3 text-sm text-body" role="status" data-testid="test-progress">{{ progress(test) }}</p>
        }
        @if (test.status === 'failed' && test.error) {
          <p class="mb-3 break-words text-sm text-fg-danger-strong" data-testid="test-error">{{ errorText(test) }}</p>
        }
        @if (test.status === 'succeeded') {
          <p class="mb-3 text-sm text-body" data-testid="test-success">{{ test.stopAtLine ? t('scriptTests.succeededThroughCell') : t('scriptTests.succeeded') }}</p>
        }

        <div class="flex flex-col gap-3">
          @for (step of test.steps; track step.step; let last = $last) {
            <details
              class="rounded-base border border-default p-3"
              [open]="last || step.status === 'failed'"
              [attr.data-testid]="'test-step-' + step.step"
            >
              <summary class="flex cursor-pointer flex-wrap items-center gap-2">
                <span class="font-medium text-heading">{{ stepName(test, step) }}</span>
                <tmz-badge [tone]="tone(step.status)">{{ t('runs.stepStatus.' + step.status) }}</tmz-badge>
                @if (step.reused) {
                  <tmz-badge tone="brand" [attr.title]="t('scriptTests.reusedHint')" data-testid="step-reused">{{ t('scriptTests.reused') }}</tmz-badge>
                }
                @if (duration(step.startedAt, step.finishedAt); as duration) {
                  <span class="text-sm text-body">{{ duration }}</span>
                }
                @if (step.inputRows !== null || step.outputRows !== null) {
                  <span class="text-sm text-body">{{ t('runs.rows', { input: step.inputRows ?? '—', output: step.outputRows ?? '—' }) }}</span>
                }
              </summary>
              <div class="mt-3 flex min-w-0 flex-col gap-3">
                @if (step.error) {
                  <pre class="max-h-60 overflow-auto rounded-base border border-danger-subtle bg-danger-soft p-3 text-xs whitespace-pre-wrap text-fg-danger-strong" data-testid="test-step-error">{{ step.error }}</pre>
                }
                @if (step.status === 'stopped') {
                  <p class="text-sm text-body" data-testid="test-stopped">{{ stoppedText(test, step) }}</p>
                }
                @if (step.shown?.length) {
                  @let shown = selectedShown(step);
                  <div class="flex flex-col gap-2" data-testid="test-shown">
                    <h3 class="text-sm font-medium text-heading">{{ t('scriptTests.shownTitle') }}</h3>
                    <ul class="flex flex-wrap gap-1">
                      @for (item of step.shown!; track $index; let position = $index) {
                        <li>
                          <button
                            type="button"
                            class="rounded-base border px-2 py-1 text-sm hover:bg-neutral-secondary-medium"
                            [class]="position === shown.index ? 'border-brand text-heading' : 'border-default text-body'"
                            [attr.aria-pressed]="position === shown.index"
                            (click)="chooseShown(step, position)"
                            [attr.data-testid]="'test-shown-' + position"
                          >
                            <span class="font-medium">{{ item.name }}</span>
                            <span class="ms-1 text-xs">{{ t('scriptTests.shownLine', { line: item.line }) }}</span>
                          </button>
                        </li>
                      }
                    </ul>
                    @if (shown.item.table; as shownTable) {
                      <p class="text-sm text-body" data-testid="test-shown-note">
                        {{ t('scriptTests.shownTable', { name: shown.item.name, line: shown.item.line, shown: shownTable.previewRows, rows: shown.item.rows }) }}
                      </p>
                      @if (shownTable.columns && shownTable.preview) {
                        <app-table-preview [columns]="shownTable.columns" [rows]="shownTable.preview" [cutCells]="shownTable.cutCells" />
                      }
                    } @else {
                      <p class="text-sm text-body" data-testid="test-shown-note">{{ t('scriptTests.shownText', { name: shown.item.name, line: shown.item.line }) }}</p>
                      <pre class="max-h-60 overflow-auto rounded-base border border-default bg-neutral-secondary-soft p-3 text-xs whitespace-pre-wrap text-body" data-testid="test-shown-text">{{ shown.item.text }}</pre>
                    }
                  </div>
                }
                @if (step.tables.length) {
                  @let table = selectedTable(step);
                  <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
                    <label class="text-sm font-medium text-heading" [attr.for]="'table-' + step.step">{{ t('scriptTests.table') }}</label>
                    <select
                      tmzInput
                      class="sm:max-w-xs"
                      [id]="'table-' + step.step"
                      [value]="table.name"
                      (change)="chooseTable(step, $any($event.target).value)"
                      [attr.data-testid]="'test-table-select-' + step.step"
                    >
                      @for (option of step.tables; track option.name) {
                        <option [value]="option.name">{{ t('scriptTests.tableOption', { name: option.name, rows: option.rows }) }}</option>
                      }
                    </select>
                  </div>
                  @if (table.columns && table.preview) {
                    <p class="text-sm text-body" data-testid="test-preview-note">{{ previewNote(table) }}</p>
                    <app-table-preview [columns]="table.columns" [rows]="table.preview" [cutCells]="table.cutCells" />
                  } @else {
                    <p class="text-sm text-body" data-testid="test-no-preview">{{ t('scriptTests.noPreview', { rows: table.rows }) }}</p>
                  }
                }
                @if (step.dashboard; as facts) {
                  @if (metadata(); as indicator) {
                    <app-indicator-view [facts]="facts" [metadata]="indicator" />
                  }
                }
                @if (step.log) {
                  <pre class="max-h-60 overflow-auto rounded-base border border-default bg-neutral-secondary-soft p-3 text-xs whitespace-pre-wrap text-body" data-testid="test-step-log">{{ step.log }}</pre>
                }
              </div>
            </details>
          }
        </div>
      }
    </tmz-card>
  `,
})
export class ScriptTestPanel {
  /** The test as the API returned it when it was queued; the panel refreshes it while it is active. */
  readonly started = input.required<ScriptTest>();
  readonly projectId = input.required<string>();
  /** The editor's current text, to tell when the result no longer matches it. */
  readonly draft = input.required<string>();
  /** The indicator's metadata as it is being edited, which the dashboard view of its table is written with. */
  readonly metadata = input<IndicatorMetadata | null>(null);

  private readonly service = inject(PipelineService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly pollMs = inject(SCRIPT_TEST_POLL_MS);

  protected readonly test = signal<ScriptTest | null>(null);
  protected readonly duration = durationText;
  protected readonly stale = computed(() => {
    const test = this.test();
    return !!test && test.code !== this.draft();
  });
  private readonly chosen = signal<Record<string, string>>({});
  private readonly chosenShown = signal<Record<string, number>>({});

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  constructor() {
    effect(() => {
      this.test.set(this.started());
      this.chosen.set({});
      this.chosenShown.set({});
    });
    // While the test is queued or running, refresh it until it ends.
    effect(() => {
      const test = this.test();
      this.clearPoll();
      if (test && (test.status === 'queued' || test.status === 'running') && !this.destroyed) {
        this.pollTimer = setTimeout(() => this.refresh(test.id), this.pollMs);
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.clearPoll();
    });
  }

  protected tone(status: RunStatus | TestStep['status']): Tone {
    return statusTones[status];
  }

  protected progress(test: ScriptTest): string {
    const t = (key: string) => this.transloco.translate(key);
    if (test.status === 'running') {
      return t('scriptTests.running');
    }
    return test.waitingFor === 'run' ? t('scriptTests.waitingForRun') : test.waitingFor === 'test' ? t('scriptTests.waitingForTest') : t('scriptTests.starting');
  }

  protected stepName(test: ScriptTest, step: TestStep): string {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    if (step.step === 'clean' && test.kind !== 'clean') {
      return t('scriptTests.savedClean', { number: test.cleanVersion ?? '—' });
    }
    return step.step === test.kind ? t('scriptTests.draft', { kind: this.kindLabel(test) }) : t('runs.steps.' + step.step);
  }

  /** "1. Clean", "2. Output" or "indicator coverage". */
  protected kindLabel(test: ScriptTest): string {
    return test.kind === 'indicator'
      ? this.transloco.translate('scriptTests.indicator', { name: test.name })
      : this.transloco.translate('scripts.kinds.' + test.kind);
  }

  protected selectedTable(step: TestStep): TestTable {
    const name = this.chosen()[step.step];
    return step.tables.find((table) => table.name === name) ?? step.tables[0];
  }

  protected chooseTable(step: TestStep, name: string): void {
    this.chosen.update((chosen) => ({ ...chosen, [step.step]: name }));
  }

  /** The preview shown for a step: the one chosen, or the last one the script showed. */
  protected selectedShown(step: TestStep): { item: ShownPreview; index: number } {
    const all = step.shown ?? [];
    const index = Math.min(this.chosenShown()[step.step] ?? all.length - 1, all.length - 1);
    return { item: all[index], index };
  }

  protected chooseShown(step: TestStep, index: number): void {
    this.chosenShown.update((chosen) => ({ ...chosen, [step.step]: index }));
  }

  /** "Ran through the cell Rate and stopped at line 6, where the next cell begins." */
  protected stoppedText(test: ScriptTest, step: TestStep): string {
    const line = step.stoppedAtLine ?? 0;
    const hit = cellAt(cells(test.code), line - 1);
    const name = hit?.cell.name || this.transloco.translate('scriptTests.cellNumber', { number: (hit?.index ?? 0) + 1 });
    return this.transloco.translate('scriptTests.stoppedAt', { name, line });
  }

  protected previewNote(table: TestTable): string {
    const key = table.previewRows < Math.min(table.rows, 100) ? 'scriptTests.previewTrimmed' : 'scriptTests.previewRows';
    return this.transloco.translate(key, { shown: table.previewRows, rows: table.rows });
  }

  /** The translated message for the test's error code, followed by the error itself for a failed step. */
  protected errorText(test: ScriptTest): string {
    if (test.errorCode) {
      const key = `errors.codes.${test.errorCode}`;
      const translated = this.transloco.translate(key);
      if (translated && translated !== key) {
        return test.error && test.errorCode.startsWith('step.') ? `${translated} ${test.error}` : translated;
      }
    }
    return test.error ?? this.transloco.translate('errors.generic');
  }

  private refresh(testId: string): void {
    this.service.test(this.projectId(), testId).subscribe({
      next: (test) => {
        if (!this.destroyed && this.test()?.id === test.id) {
          this.test.set(test);
        }
      },
      error: (error) => this.errors.report(error),
    });
  }

  private clearPoll(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
