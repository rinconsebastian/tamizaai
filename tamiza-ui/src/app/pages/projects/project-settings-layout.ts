import { Component, DestroyRef, ElementRef, computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { filter } from 'rxjs';
import { ProjectContext } from './project-context';
import { projectSections } from './project-sections';

/**
 * Wraps the configuration sections (General, Submissions, Scripts, …). It sits on an empty-path route, so it consumes no part of
 * the address and every section keeps the address it always had. From `md` it shows the sections as a row of pills; below that
 * the project page's select already lists them, so the row is hidden.
 */
@Component({
  selector: 'app-project-settings-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <nav class="mb-6 hidden md:block" [attr.aria-label]="t('project.tabs.settings')" data-testid="settings-nav">
        <ul class="flex flex-wrap gap-2">
          @for (section of sections(); track section.link) {
            <li>
              <a
                [routerLink]="section.link"
                routerLinkActive="bg-brand-softer text-fg-brand"
                ariaCurrentWhenActive="page"
                class="inline-flex min-h-10 items-center rounded-base px-4 py-2 text-sm font-medium text-body hover:bg-neutral-secondary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
                >{{ t(section.labelKey) }}</a
              >
            </li>
          }
        </ul>
      </nav>
      <router-outlet />
    </ng-container>
  `,
})
export class ProjectSettingsLayout {
  private readonly context = inject(ProjectContext);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly sections = computed(() => projectSections(this.context.project()!.id, this.context.can('admin')));

  constructor() {
    // Focus follows a navigation made inside the page, never the first load of the address (nothing has navigated yet then).
    let moveFocus = this.router.navigated;
    let timer: ReturnType<typeof setTimeout> | undefined;
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        if (moveFocus) {
          // The pill's current-page state is set by the router's own listener for this same event, so wait for it.
          clearTimeout(timer);
          timer = setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('nav a[aria-current="page"]')?.focus());
        }
        moveFocus = true;
      });
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));
  }
}
