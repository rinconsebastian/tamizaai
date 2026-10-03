import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { PipelineService, PipelineSettings } from '../../core/projects/pipeline.service';
import { Button, Card, FormField, Input, ToastService } from '../../shared/ui';
import { ProjectContext } from './project-context';

/** When the script chain runs on its own: after synchronizations that changed data, and on a schedule. */
@Component({
  selector: 'app-pipeline-settings',
  imports: [DatePipe, TranslocoDirective, Button, Card, FormField, Input],
  template: `
    <tmz-card [heading]="t('pipeline.settingsTitle')" *transloco="let t">
      @if (settings(); as settings) {
        @if (context.can('admin')) {
          <form
            class="flex flex-col gap-4 lg:flex-row lg:items-start"
            (submit)="$event.preventDefault(); save(afterSync.checked, interval.value)"
            data-testid="pipeline-settings-form"
          >
            <label class="flex items-center gap-2 text-sm text-heading lg:mt-8 lg:flex-1">
              <input #afterSync type="checkbox" class="h-4 w-4 rounded-sm" [checked]="settings.runAfterSync" data-testid="run-after-sync" />
              {{ t('pipeline.runAfterSync') }}
            </label>
            <tmz-form-field class="lg:flex-1" [label]="t('pipeline.interval')" fieldId="runIntervalHours" [error]="errors()['runIntervalHours']" [hint]="t('pipeline.intervalHint')">
              <input tmzInput #interval id="runIntervalHours" type="number" min="1" max="168" step="1" [value]="settings.runIntervalHours ?? ''" />
            </tmz-form-field>
            <button tmzButton type="submit" class="lg:mt-7" [disabled]="busy()">{{ t('common.save') }}</button>
          </form>
        } @else {
          <dl class="grid gap-3 text-sm sm:grid-cols-2" data-testid="pipeline-settings-read-only">
            <div>
              <dt class="text-body">{{ t('pipeline.runAfterSync') }}</dt>
              <dd class="text-heading">{{ settings.runAfterSync ? t('pipeline.yes') : t('pipeline.no') }}</dd>
            </div>
            <div>
              <dt class="text-body">{{ t('pipeline.interval') }}</dt>
              <dd class="text-heading">{{ settings.runIntervalHours ? t('data.everyHours', { hours: settings.runIntervalHours }) : t('pipeline.noSchedule') }}</dd>
            </div>
          </dl>
        }
        @if (settings.nextRunAt) {
          <p class="mt-3 text-sm text-body" data-testid="next-run">{{ t('pipeline.nextRun', { time: (settings.nextRunAt | date: 'medium') }) }}</p>
        }
      }
    </tmz-card>
  `,
})
export class PipelineSettingsCard {
  private readonly service = inject(PipelineService);
  private readonly apiErrors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  protected readonly context = inject(ProjectContext);

  protected readonly settings = signal<PipelineSettings | null>(null);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly busy = signal(false);

  constructor() {
    this.service.settings(this.context.project()!.id).subscribe({
      next: (settings) => this.settings.set(settings),
      error: (error) => this.apiErrors.report(error),
    });
  }

  protected save(runAfterSync: boolean, interval: string): void {
    this.errors.set({});
    this.busy.set(true);
    const runIntervalHours = interval.trim() === '' ? null : Number(interval);
    this.service.saveSettings(this.context.project()!.id, { runAfterSync, runIntervalHours }).subscribe({
      next: (settings) => {
        this.busy.set(false);
        this.settings.set(settings);
        this.toasts.success(this.transloco.translate('pipeline.settingsSaved'));
      },
      error: (error) => {
        this.busy.set(false);
        this.errors.set(this.apiErrors.report(error).fieldErrors);
      },
    });
  }
}
