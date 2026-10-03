import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

/** Unknown routes keep their address (no redirect), so a requested route survives the sign-in round trip. */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink, TranslocoDirective],
  template: `
    <section *transloco="let t">
      <h1>{{ t('notFound.title') }}</h1>
      <p>{{ t('notFound.body') }}</p>
      <a routerLink="/">{{ t('notFound.home') }}</a>
    </section>
  `,
})
export class NotFoundPage {}
