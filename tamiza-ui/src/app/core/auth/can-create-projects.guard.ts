import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CurrentUserService } from '../current-user.service';

/** Project creation is for superadmins and project admins; anyone else, or a profile that cannot be read, goes to the list. */
export const canCreateProjectsGuard: CanActivateFn = async () => {
  const currentUser = inject(CurrentUserService);
  const router = inject(Router);
  await currentUser.profile();
  return currentUser.canCreateProjects() || router.createUrlTree(['/projects']);
};
