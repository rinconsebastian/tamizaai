import { Component, computed, inject, input, effect } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ApiErrors } from '../../core/api-error';
import { ProjectsService } from '../../core/projects/projects.service';
import { Badge, Button, Skeleton, TabItem, TabsNav } from '../../shared/ui';
import { ProjectContext } from './project-context';

@Component({
  selector: 'app-project-page',
  imports: [RouterOutlet, RouterLink, TranslocoDirective, Badge, Button, Skeleton, TabsNav],
  providers: [ProjectContext],
  template: `
    <section *transloco="let t">
      @if (project(); as project) {
        <header class="mb-6 flex flex-col gap-2">
          <a routerLink="/projects" class="text-sm text-fg-brand hover:underline">← {{ t('projects.title') }}</a>
          <div class="flex flex-wrap items-center gap-3">
            <h1 class="text-2xl font-semibold text-heading break-words" data-testid="project-name">{{ project.name }}</h1>
            <tmz-badge [tone]="project.myRole === 'admin' ? 'brand' : 'neutral'">{{ t('roles.' + project.myRole) }}</tmz-badge>
          </div>
          <p class="text-sm text-body break-words">{{ project.formName }}</p>
        </header>
        <tmz-tabs-nav class="mb-6 block" [items]="tabs()" [label]="t('project.sections')" />
        <router-outlet />
      } @else if (notFound()) {
        <div class="mx-auto max-w-xl py-8" data-testid="project-not-found">
          <h1 class="mb-3 text-2xl font-semibold text-heading">{{ t('project.notFoundTitle') }}</h1>
          <p class="mb-6 text-body">{{ t('project.notFoundBody') }}</p>
          <a tmzButton variant="secondary" routerLink="/projects">{{ t('projects.title') }}</a>
        </div>
      } @else {
        <div aria-busy="true" class="flex flex-col gap-3">
          <tmz-skeleton class="h-6 w-1/3" /><tmz-skeleton class="w-1/4" />
        </div>
      }
    </section>
  `,
})
export class ProjectPage {
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly context = inject(ProjectContext);

  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  protected readonly project = this.context.project;
  protected readonly notFound = signal(false);
  protected readonly tabs = computed<TabItem[]>(() => {
    const id = this.id();
    const t = (key: string) => this.transloco.translate(key);
    const tabs: TabItem[] = [
      { label: t('project.tabs.overview'), link: `/projects/${id}/overview` },
      { label: t('project.tabs.members'), link: `/projects/${id}/members` },
      { label: t('project.tabs.samplingFrame'), link: `/projects/${id}/sampling-frame` },
    ];
    if (this.context.can('admin')) {
      tabs.push({ label: t('project.tabs.webhook'), link: `/projects/${id}/webhook` });
    }
    return tabs;
  });

  constructor() {
    effect(() => {
      const id = this.id();
      this.notFound.set(false);
      this.context.project.set(null);
      this.service.get(id).subscribe({
        next: (project) => this.context.update(project),
        error: (error) => {
          if (error instanceof HttpErrorResponse && error.status === 404) {
            this.notFound.set(true);
          } else {
            this.errors.report(error);
          }
        },
      });
    });
  }
}
