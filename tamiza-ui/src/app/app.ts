import { Component, effect, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthService } from './core/auth/auth.service';
import { CurrentUserService } from './core/current-user.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, TranslocoDirective],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly auth = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);

  protected readonly user = this.currentUser.user;

  constructor() {
    effect(() => {
      if (this.auth.status() === 'authenticated') {
        this.currentUser.load();
      }
    });
  }

  protected signOut(): void {
    this.auth.logout();
  }
}
