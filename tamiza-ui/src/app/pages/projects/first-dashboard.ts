import { Component, afterNextRender, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ProjectContext } from './project-context';

/**
 * The former Indicators address, `/projects/<id>/indicators`: opens the project's first dashboard and puts its address in place of
 * this one (design D10). It renders inside the project page's outlet, which exists only once the project's details are read, so
 * the dashboards are known without asking for the project again.
 */
@Component({
  selector: 'app-first-dashboard',
  template: '',
})
export class FirstDashboard {
  constructor() {
    const context = inject(ProjectContext);
    const router = inject(Router);
    afterNextRender(() => {
      const project = context.project();
      const first = context.dashboards()[0];
      if (project && first) {
        void router.navigate(['/projects', project.id, 'dashboards', first.id], { replaceUrl: true });
      }
    });
  }
}
