import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, take, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Attaches the access token to same-origin `/api/` calls only, and signs in again on a 401. */
export const apiAuthInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isSameOriginApi(request.url)) {
    return next(request);
  }

  const auth = inject(AuthService);
  return auth.accessToken().pipe(
    take(1),
    switchMap((token) =>
      next(token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request),
    ),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && auth.status() === 'authenticated') {
        auth.login(window.location.pathname + window.location.search);
      }
      return throwError(() => error);
    }),
  );
};

export function isSameOriginApi(url: string): boolean {
  const target = new URL(url, window.location.origin);
  return target.origin === window.location.origin && target.pathname.startsWith('/api/');
}
