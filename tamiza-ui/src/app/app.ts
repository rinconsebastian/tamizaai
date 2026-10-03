import { Dialog, DialogRef } from '@angular/cdk/dialog';
import { Overlay } from '@angular/cdk/overlay';
import { Component, DestroyRef, TemplateRef, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { filter } from 'rxjs';
import { AuthService } from './core/auth/auth.service';
import { CurrentUserService } from './core/current-user.service';
import { Button, ToastRegion } from './shared/ui';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoDirective, Button, ToastRegion],
  templateUrl: './app.html',
})
export class App {
  private readonly auth = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly dialog = inject(Dialog);
  private readonly overlay = inject(Overlay);
  private readonly drawerTemplate = viewChild.required<TemplateRef<unknown>>('drawer');
  private drawerRef: DialogRef | null = null;

  protected readonly user = this.currentUser.user;
  protected readonly menuOpen = signal(false);

  constructor() {
    effect(() => {
      if (this.auth.status() === 'authenticated') {
        this.currentUser.load();
      }
    });
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.closeMenu());
  }

  /** Below `md` the navigation lives in a side drawer: focus stays inside, Escape closes it, focus returns to the button. */
  protected openMenu(): void {
    this.drawerRef = this.dialog.open(this.drawerTemplate(), {
      ariaLabelledBy: 'tmz-drawer-title',
      positionStrategy: this.overlay.position().global().top('0').right('0'),
      height: '100%',
      width: 'min(20rem, 85vw)',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
    this.menuOpen.set(true);
    this.drawerRef.closed.subscribe(() => {
      this.menuOpen.set(false);
      this.drawerRef = null;
    });
  }

  protected closeMenu(): void {
    this.drawerRef?.close();
  }

  protected signOut(): void {
    this.closeMenu();
    this.auth.logout();
  }
}
