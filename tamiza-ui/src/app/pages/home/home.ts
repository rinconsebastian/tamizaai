import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CurrentUserService } from '../../core/current-user.service';

@Component({
  selector: 'app-home',
  imports: [TranslocoDirective],
  template: `
    <section *transloco="let t">
      @if (user(); as user) {
        <h1>{{ t('home.welcome', { name: user.name }) }}</h1>
      } @else {
        <p>{{ t('home.loading') }}</p>
      }
      <p>{{ t('home.intro') }}</p>
    </section>
  `,
})
export class HomePage {
  protected readonly user = inject(CurrentUserService).user;
}
