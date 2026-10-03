import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { AuthUnavailablePage } from './pages/auth-unavailable/auth-unavailable';
import { NotFoundPage } from './pages/not-found/not-found';

// Project screens load on demand to keep the initial bundle small.
export const routes: Routes = [
  { path: 'auth-unavailable', component: AuthUnavailablePage },
  {
    path: '',
    canActivateChild: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'projects' },
      { path: 'projects', loadComponent: () => import('./pages/projects/project-list').then((m) => m.ProjectListPage) },
      { path: 'projects/new', loadComponent: () => import('./pages/projects/create-project').then((m) => m.CreateProjectPage) },
      {
        path: 'projects/:id',
        loadComponent: () => import('./pages/projects/project-page').then((m) => m.ProjectPage),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'overview' },
          { path: 'overview', loadComponent: () => import('./pages/projects/project-overview').then((m) => m.ProjectOverview) },
          { path: 'data', loadComponent: () => import('./pages/projects/project-data').then((m) => m.ProjectData) },
          {
            path: 'scripts',
            loadComponent: () => import('./pages/projects/project-scripts').then((m) => m.ProjectScripts),
            // Inline rather than imported from the page, so the editor's chunk stays lazy.
            canDeactivate: [(component: { confirmDiscard(): Promise<boolean> }) => component.confirmDiscard()],
          },
          { path: 'members', loadComponent: () => import('./pages/projects/project-members').then((m) => m.ProjectMembers) },
          {
            path: 'sampling-frame',
            loadComponent: () => import('./pages/projects/project-sampling-frame').then((m) => m.ProjectSamplingFrame),
          },
          { path: 'webhook', loadComponent: () => import('./pages/projects/project-webhook').then((m) => m.ProjectWebhook) },
        ],
      },
      { path: '**', component: NotFoundPage },
    ],
  },
];
