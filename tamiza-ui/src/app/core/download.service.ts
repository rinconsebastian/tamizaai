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

  /**
   * Saves a file the browser made, such as an image of a widget, under <paramref name="fileName"/>: a temporary link to the file is
   * clicked and removed, and the file is released after 30 seconds, long enough for the browser to have taken it. A download link is a
   * navigation, not an image load, so the Content-Security-Policy's `img-src` stays as it is.
   */
  save(blob: Blob, fileName: string): void {
    const view = this.document.defaultView;
    if (!view) {
      return;
    }

    const url = view.URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    link.style.display = 'none';
    this.document.body.appendChild(link);
    link.click();
    link.remove();
    view.setTimeout(() => view.URL.revokeObjectURL(url), 30_000);
  }
}
