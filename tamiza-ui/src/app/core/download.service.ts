import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/** Starts a download by navigating to a link. A separate service so tests can see the link without the browser following it. */
@Injectable({ providedIn: 'root' })
export class Downloader {
  private readonly document = inject(DOCUMENT);

  /** The link answers with an attachment, so the page stays where it is while the browser saves the file. */
  start(url: string): void {
    this.document.defaultView?.location.assign(url);
  }
}
