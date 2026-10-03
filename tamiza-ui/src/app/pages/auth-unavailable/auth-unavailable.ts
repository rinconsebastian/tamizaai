import { DOCUMENT, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-auth-unavailable',
  imports: [TranslocoDirective],
  template: `
    <section class="notice" role="alert" *transloco="let t">
      <h1>{{ t('authUnavailable.title') }}</h1>
      @if (reason() === 'insecure-context') {
        <p>{{ t('authUnavailable.insecureContext') }}</p>
      } @else {
        <p>{{ t('authUnavailable.unreachable') }}</p>
      }
      <button type="button" (click)="retry()">{{ t('authUnavailable.retry') }}</button>
    </section>
  `,
  styles: `
    .notice {
      max-width: 40rem;
    }
  `,
})
export class AuthUnavailablePage {
  private readonly document = inject(DOCUMENT);
  protected readonly reason = inject(AuthService).unavailableReason;

  /** A clean reload from the root, so a failed login callback is not replayed. */
  protected retry(): void {
    this.document.location.assign('/');
  }
}
