import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Every screen requires a session; without one the user goes to Keycloak and returns to the same route. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  switch (auth.status()) {
    case 'authenticated':
      return true;
    case 'unavailable':
      return inject(Router).createUrlTree(['/auth-unavailable']);
    default:
      auth.login(state.url);
      return false;
  }
};
