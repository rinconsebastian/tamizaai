import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { AuthUnavailablePage } from './pages/auth-unavailable/auth-unavailable';
import { HomePage } from './pages/home/home';
import { NotFoundPage } from './pages/not-found/not-found';

export const routes: Routes = [
  { path: 'auth-unavailable', component: AuthUnavailablePage },
  {
    path: '',
    canActivateChild: [authGuard],
    children: [
      { path: '', component: HomePage },
      { path: '**', component: NotFoundPage },
    ],
  },
];
