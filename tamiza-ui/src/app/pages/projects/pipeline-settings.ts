import { Component, inject, input, output, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { PipelineService, PipelineSettings } from '../../core/projects/pipeline.service';
import { Button, Card, FormField, Input, ToastService } from '../../shared/ui';
import { ProjectContext } from './project-context';

/**
 * The form for when the script chain runs on its own: after synchronizations that changed data, and on a schedule. Admins only;
 * the page that hosts it reads the settings, shows them as a line for everyone and offers this form on request (design D6).
 */
@Component({
  selector: 'app-pipeline-settings',
  imports: [TranslocoDirective, Button, Card, FormField, Input],
  template: `
    <tmz-card [heading]="t('pipeline.settingsTitle')" *transloco="let t">
      <form
        class="flex flex-col gap-4 lg:flex-row lg:items-start"
        (submit)="$event.preventDefault(); save(afterSync.checked, interval.value)"
        data-testid="pipeline-settings-form"
      >
        <label class="flex items-center gap-2 text-sm text-heading lg:mt-8 lg:flex-1">
          <input #afterSync type="checkbox" class="h-4 w-4 rounded-sm" [checked]="settings().runAfterSync" data-testid="run-after-sync" />
          {{ t('pipeline.runAfterSync') }}
        </label>
        <tmz-form-field class="lg:flex-1" [label]="t('pipeline.interval')" fieldId="runIntervalHours" [error]="errors()['runIntervalHours']" [hint]="t('pipeline.intervalHint')">
          <input tmzInput #interval id="runIntervalHours" type="number" min="1" max="168" step="1" [value]="settings().runIntervalHours ?? ''" />
        </tmz-form-field>
        <button tmzButton type="submit" class="lg:mt-7" [disabled]="busy()">{{ t('common.save') }}</button>
      </form>
    </tmz-card>
  `,
})
export class PipelineSettingsCard {
  private readonly service = inject(PipelineService);
  private readonly apiErrors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly context = inject(ProjectContext);

  readonly settings = input.required<PipelineSettings>();
  /** The settings as the API stored them, after a successful save. */
  readonly saved = output<PipelineSettings>();

  protected readonly errors = signal<Record<string, string>>({});
  protected readonly busy = signal(false);

  protected save(runAfterSync: boolean, interval: string): void {
    this.errors.set({});
    this.busy.set(true);
    const runIntervalHours = interval.trim() === '' ? null : Number(interval);
    this.service.saveSettings(this.context.project()!.id, { runAfterSync, runIntervalHours }).subscribe({
      next: (settings) => {
        this.busy.set(false);
        this.toasts.success(this.transloco.translate('pipeline.settingsSaved'));
        this.saved.emit(settings);
      },
      error: (error) => {
        this.busy.set(false);
        this.errors.set(this.apiErrors.report(error).fieldErrors);
      },
    });
  }
}
