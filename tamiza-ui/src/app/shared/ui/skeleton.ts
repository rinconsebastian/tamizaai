import { Component } from '@angular/core';

@Component({
  selector: 'tmz-skeleton',
  template: '',
  host: { class: 'block animate-pulse bg-neutral-quaternary rounded-full h-2.5', 'aria-hidden': 'true' },
})
export class Skeleton {}
