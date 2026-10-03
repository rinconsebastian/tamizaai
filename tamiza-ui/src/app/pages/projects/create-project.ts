import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { ProjectsService } from '../../core/projects/projects.service';
import { Alert, Button, Card, FormField, Input, ToastService } from '../../shared/ui';

export const koboServers = [
  { id: 'kf', url: 'https://kf.kobotoolbox.org' },
  { id: 'eu', url: 'https://eu.kobotoolbox.org' },
  { id: 'custom', url: '' },
] as const;

type ServerChoice = (typeof koboServers)[number]['id'];

@Component({
  selector: 'app-create-project',
  imports: [ReactiveFormsModule, TranslocoDirective, Alert, Button, Card, FormField, Input],
  template: `
    <section class="mx-auto max-w-2xl" *transloco="let t">
      <h1 class="mb-2 text-2xl font-semibold text-heading">{{ t('createProject.title') }}</h1>
      <p class="mb-6 text-body">{{ t('createProject.intro') }}</p>

      <tmz-card>
        <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-5" novalidate>
          @if (formError(); as message) {
            <tmz-alert tone="danger" data-testid="form-error">{{ message }}</tmz-alert>
          }

          <tmz-form-field [label]="t('createProject.name')" fieldId="name" [error]="fieldErrors()['name']">
            <input tmzInput id="name" formControlName="name" autocomplete="off" />
          </tmz-form-field>

          <fieldset>
            <legend class="mb-2 text-sm font-medium text-heading">{{ t('createProject.server') }}</legend>
            <div class="grid gap-2 sm:grid-cols-3">
              @for (server of servers; track server.id) {
                <label
                  class="flex min-h-10 cursor-pointer items-center gap-2 rounded-base border border-default-medium bg-neutral-secondary-medium px-3 py-2 text-sm text-heading has-[:checked]:border-brand has-[:checked]:bg-brand-softer"
                >
                  <input type="radio" name="server" [value]="server.id" [checked]="choice() === server.id" (change)="choose(server.id)" class="text-brand focus:ring-brand" />
                  {{ t('createProject.servers.' + server.id) }}
                </label>
              }
            </div>
          </fieldset>

          @if (choice() === 'custom') {
            <tmz-form-field [label]="t('createProject.serverUrl')" fieldId="koboServerUrl" [hint]="t('createProject.serverUrlHint')" [error]="fieldErrors()['koboServerUrl']">
              <input tmzInput id="koboServerUrl" formControlName="koboServerUrl" type="url" inputmode="url" placeholder="https://kobo.example.org" />
            </tmz-form-field>
          } @else if (fieldErrors()['koboServerUrl']) {
            <p class="-mt-3 text-sm text-fg-danger-strong">{{ fieldErrors()['koboServerUrl'] }}</p>
          }

          <div class="grid gap-5 lg:grid-cols-2">
            <tmz-form-field [label]="t('createProject.assetUid')" fieldId="assetUid" [hint]="t('createProject.assetUidHint')" [error]="fieldErrors()['assetUid']">
              <input tmzInput id="assetUid" formControlName="assetUid" autocomplete="off" spellcheck="false" />
            </tmz-form-field>
            <tmz-form-field [label]="t('createProject.apiToken')" fieldId="apiToken" [hint]="t('createProject.apiTokenHint')" [error]="fieldErrors()['apiToken']">
              <input tmzInput id="apiToken" formControlName="apiToken" type="password" autocomplete="off" spellcheck="false" />
            </tmz-form-field>
          </div>

          <div class="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            <button tmzButton variant="secondary" type="button" (click)="cancel()">{{ t('common.cancel') }}</button>
            <button tmzButton type="submit" [disabled]="submitting()" data-testid="submit">
              {{ submitting() ? t('createProject.checking') : t('createProject.submit') }}
            </button>
          </div>
        </form>
      </tmz-card>
    </section>
  `,
})
export class CreateProjectPage {
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly router = inject(Router);

  protected readonly servers = koboServers;
  protected readonly choice = signal<ServerChoice>('kf');
  protected readonly submitting = signal(false);
  protected readonly fieldErrors = signal<Record<string, string>>({});
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', Validators.required],
    koboServerUrl: [koboServers[0].url as string],
    assetUid: ['', Validators.required],
    apiToken: ['', Validators.required],
  });

  protected choose(choice: ServerChoice): void {
    this.choice.set(choice);
    this.form.controls.koboServerUrl.setValue(koboServers.find((s) => s.id === choice)!.url);
  }

  protected submit(): void {
    this.submitting.set(true);
    this.fieldErrors.set({});
    this.formError.set(null);
    this.service.create(this.form.getRawValue()).subscribe({
      next: (created) => {
        this.toasts.success(this.transloco.translate('createProject.created', { name: created.project.name }));
        void this.router.navigate(['/projects', created.project.id, 'webhook']);
      },
      error: (error) => {
        const parsed = this.errors.parse(error);
        this.fieldErrors.set(parsed.fieldErrors);
        if (!Object.keys(parsed.fieldErrors).length) {
          this.formError.set(parsed.message);
        }
        this.submitting.set(false);
      },
    });
  }

  protected cancel(): void {
    void this.router.navigate(['/projects']);
  }
}
