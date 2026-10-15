import { Component, computed, inject, input, effect } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ApiErrors } from '../../core/api-error';
import { ProjectsService } from '../../core/projects/projects.service';
import { Alert, Badge, Button, Skeleton, TabItem, TabsNav } from '../../shared/ui';
import { ProjectContext } from './project-context';
import { projectSections } from './project-sections';

@Component({
  selector: 'app-project-page',
  imports: [RouterOutlet, RouterLink, TranslocoDirective, Alert, Badge, Button, Skeleton, TabsNav],
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
        @if (project.tokenUnreadable && context.can('admin') && !onGeneral()) {
          <tmz-alert tone="danger" class="mb-6" data-testid="token-unreadable-banner">
            {{ t('project.tokenUnreadable') }}
            <a routerLink="overview" class="font-medium underline">{{ t('project.tokenUnreadableToken') }}</a>
            ·
            <a routerLink="webhook" class="font-medium underline">{{ t('project.tokenUnreadableWebhook') }}</a>
          </tmz-alert>
        }
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
  protected readonly context = inject(ProjectContext);
  private readonly router = inject(Router);

  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  protected readonly project = this.context.project;
  /** General shows the same warning beside the token it concerns, so the banner steps aside there. */
  protected readonly onGeneral = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => this.router.url.split(/[?#]/)[0].endsWith('/overview')),
    ),
    { initialValue: false },
  );
  protected readonly notFound = signal(false);
  protected readonly tabs = computed<TabItem[]>(() => {
    const id = this.id();
    const t = (key: string) => this.transloco.translate(key);
    const sections = projectSections(id, this.context.can('admin')).map((s) => ({ label: t(s.labelKey), link: s.link }));
    return [
      { label: t('project.tabs.performance'), link: `/projects/${id}/performance` },
      { label: t('project.tabs.cleanData'), link: `/projects/${id}/clean-data` },
      { label: t('project.tabs.indicators'), link: `/projects/${id}/indicators` },
      // Settings opens the first configuration section and stands for all of them.
      { label: t('project.tabs.settings'), link: sections[0].link, children: sections },
    ];
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
