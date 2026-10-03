import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button } from '../../shared/ui';

/** Unknown routes keep their address (no redirect), so a requested route survives the sign-in round trip. */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink, TranslocoDirective, Button],
  template: `
    <section class="mx-auto max-w-xl py-8" *transloco="let t">
      <h1 class="mb-3 text-2xl font-semibold text-heading">{{ t('notFound.title') }}</h1>
      <p class="mb-6 text-body">{{ t('notFound.body') }}</p>
      <a tmzButton variant="secondary" routerLink="/">{{ t('notFound.home') }}</a>
    </section>
  `,
})
export class NotFoundPage {}
