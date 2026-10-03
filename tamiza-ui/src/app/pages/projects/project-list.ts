import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { ProjectSummary, ProjectsService } from '../../core/projects/projects.service';
import { Badge, Button, Card, Skeleton } from '../../shared/ui';

@Component({
  selector: 'app-project-list',
  imports: [RouterLink, TranslocoDirective, Badge, Button, Card, Skeleton],
  template: `
    <section *transloco="let t">
      <div class="mb-6 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <h1 class="text-2xl font-semibold text-heading">{{ t('projects.title') }}</h1>
        @if (projects()?.length) {
          <a tmzButton routerLink="/projects/new" data-testid="new-project">{{ t('projects.new') }}</a>
        }
      </div>

      @if (projects(); as projects) {
        @if (projects.length) {
          <ul class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            @for (project of projects; track project.id) {
              <li>
                <a
                  [routerLink]="['/projects', project.id]"
                  class="block h-full rounded-base focus:outline-none focus:ring-4 focus:ring-brand-medium"
                  data-testid="project-card"
                >
                  <tmz-card class="h-full transition hover:border-brand-subtle hover:shadow-md">
                    <div class="flex items-start justify-between gap-3">
                      <h2 class="text-lg font-semibold text-heading break-words">{{ project.name }}</h2>
                      <tmz-badge [tone]="project.myRole === 'admin' ? 'brand' : 'neutral'">{{ t('roles.' + project.myRole) }}</tmz-badge>
                    </div>
                    <p class="mt-2 text-sm text-body break-words">{{ project.formName }}</p>
                  </tmz-card>
                </a>
              </li>
            }
          </ul>
        } @else {
          <tmz-card class="mx-auto max-w-xl text-center" data-testid="empty-state">
            <h2 class="text-lg font-semibold text-heading">{{ t('projects.emptyTitle') }}</h2>
            <p class="mt-2 text-body">{{ t('projects.emptyBody') }}</p>
            <a tmzButton class="mt-6" routerLink="/projects/new">{{ t('projects.createFirst') }}</a>
          </tmz-card>
        }
      } @else {
        <div class="grid gap-4 md:grid-cols-2 lg:grid-cols-3" aria-busy="true">
          @for (i of [1, 2, 3]; track i) {
            <tmz-card><tmz-skeleton class="mb-4 w-2/3" /><tmz-skeleton class="w-1/2" /></tmz-card>
          }
        </div>
      }
    </section>
  `,
})
export class ProjectListPage implements OnInit {
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  protected readonly projects = signal<ProjectSummary[] | null>(null);

  ngOnInit(): void {
    this.service.list().subscribe({
      next: (projects) => this.projects.set(projects),
      error: (error) => {
        this.errors.report(error);
        this.projects.set([]);
      },
    });
  }
}
