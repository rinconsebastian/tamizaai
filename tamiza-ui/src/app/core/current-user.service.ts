import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

/** Profile returned by `GET /api/v1/me`. */
export interface CurrentUser {
  id: string;
  name: string;
  email: string | null;
  isSuperAdmin: boolean;
}

@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly http = inject(HttpClient);
  private loading = false;

  readonly user = signal<CurrentUser | null>(null);

  load(): void {
    if (this.user() || this.loading) {
      return;
    }
    this.loading = true;
    this.http.get<CurrentUser>('/api/v1/me').subscribe({
      next: (user) => this.user.set(user),
      complete: () => (this.loading = false),
      error: () => (this.loading = false),
    });
  }
}
