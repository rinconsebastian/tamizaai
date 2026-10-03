import { DIALOG_DATA, Dialog, DialogRef } from '@angular/cdk/dialog';
import { Component, Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Button, ButtonVariant } from './button';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  tone?: Extract<ButtonVariant, 'primary' | 'danger'>;
}

/** Flowbite modal markup on the CDK dialog: focus trapped inside, Escape closes, focus returns to the trigger. */
@Component({
  selector: 'tmz-confirm-dialog',
  imports: [Button],
  template: `
    <div class="bg-neutral-primary-soft border border-default rounded-base shadow-sm p-4 md:p-6 w-[min(28rem,calc(100vw-2rem))]">
      <h2 id="tmz-confirm-title" class="text-lg font-medium text-heading">{{ data.title }}</h2>
      <p id="tmz-confirm-message" class="mt-3 text-body">{{ data.message }}</p>
      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close(false)">{{ data.cancelLabel }}</button>
        <button tmzButton [variant]="data.tone ?? 'primary'" type="button" (click)="ref.close(true)" data-testid="confirm">
          {{ data.confirmLabel }}
        </button>
      </div>
    </div>
  `,
})
export class ConfirmDialogComponent {
  protected readonly data = inject<ConfirmOptions>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialog {
  private readonly dialog = inject(Dialog);

  /** Resolves true when confirmed; false on cancel, Escape or a backdrop click. */
  async confirm(options: ConfirmOptions): Promise<boolean> {
    const ref = this.dialog.open<boolean, ConfirmOptions>(ConfirmDialogComponent, {
      data: options,
      ariaLabelledBy: 'tmz-confirm-title',
      ariaDescribedBy: 'tmz-confirm-message',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
    return (await firstValueFrom(ref.closed)) === true;
  }
}
