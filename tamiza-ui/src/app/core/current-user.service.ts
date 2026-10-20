import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';

/** Profile returned by `GET /api/v1/me`. */
export interface CurrentUser {
  id: string;
  name: string;
  email: string | null;
  isSuperAdmin: boolean;
  /** Whether the API would accept a new project from this user: a superadmin or an admin of some project. */
  canCreateProjects: boolean;
}

@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly http = inject(HttpClient);
  private inFlight: Promise<CurrentUser | null> | null = null;
  private readonly failed = signal(false);

  readonly user = signal<CurrentUser | null>(null);

  /** False while the profile loads, after a failed read and when the API does not report it. */
  readonly canCreateProjects = computed(() => !this.failed() && this.user()?.canCreateProjects === true);

  /** True once the profile has been read or its read has failed, so a page can wait before it chooses between texts. */
  readonly settled = computed(() => this.user() !== null || this.failed());

  /** Reads the profile once; does nothing when it is loaded or on its way. */
  load(): void {
    void this.profile();
  }

  /** Reads the profile again, e.g. after a role change. A request already in flight is shared, not repeated. */
  refresh(): void {
    void this.fetch();
  }

  /** Resolves with the loaded profile, reading it first when needed; null when it cannot be read. */
  profile(): Promise<CurrentUser | null> {
    return this.user() ? Promise.resolve(this.user()) : this.fetch();
  }

  private fetch(): Promise<CurrentUser | null> {
    this.inFlight ??= new Promise((resolve) => {
      this.http.get<CurrentUser>('/api/v1/me').subscribe({
        next: (user) => {
          this.failed.set(false);
          this.user.set(user);
        },
        error: () => {
          this.failed.set(true);
          this.inFlight = null;
          resolve(null);
        },
        complete: () => {
          this.inFlight = null;
          resolve(this.user());
        },
      });
    });
    return this.inFlight;
  }
}
