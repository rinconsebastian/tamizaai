import { DOCUMENT, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { Alert, Button } from '../../shared/ui';

@Component({
  selector: 'app-auth-unavailable',
  imports: [TranslocoDirective, Alert, Button],
  template: `
    <section class="mx-auto max-w-xl py-8" *transloco="let t">
      <h1 class="mb-4 text-2xl font-semibold text-heading">{{ t('authUnavailable.title') }}</h1>
      <tmz-alert tone="danger">
        @if (reason() === 'insecure-context') {
          {{ t('authUnavailable.insecureContext') }}
        } @else {
          {{ t('authUnavailable.unreachable') }}
        }
      </tmz-alert>
      <button tmzButton class="mt-6" type="button" (click)="retry()">{{ t('authUnavailable.retry') }}</button>
    </section>
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
