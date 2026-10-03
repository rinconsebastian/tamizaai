import { Component, Injectable, inject, signal } from '@angular/core';

export type ToastTone = 'success' | 'danger' | 'info';

export interface Toast {
  id: number;
  tone: ToastTone;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  readonly toasts = signal<Toast[]>([]);

  success(message: string): void {
    this.show('success', message);
  }

  error(message: string): void {
    this.show('danger', message, 10000);
  }

  info(message: string): void {
    this.show('info', message);
  }

  dismiss(id: number): void {
    this.toasts.update((toasts) => toasts.filter((t) => t.id !== id));
  }

  private show(tone: ToastTone, message: string, durationMs = 6000): void {
    const id = this.nextId++;
    this.toasts.update((toasts) => [...toasts, { id, tone, message }]);
    setTimeout(() => this.dismiss(id), durationMs);
  }
}

/** Flowbite toast markup in a polite live region, so screen readers announce messages without moving focus. */
@Component({
  selector: 'tmz-toast-region',
  template: `
    <!-- Phones: below the navbar, so toasts never cover the actions at the bottom of a form. -->
    <div aria-live="polite" class="fixed top-16 inset-x-4 sm:top-auto sm:bottom-4 sm:inset-x-auto sm:end-4 z-50 flex flex-col gap-3 sm:w-96">
      @for (toast of toasts.toasts(); track toast.id) {
        <div
          class="flex items-start gap-3 w-full p-4 text-body bg-neutral-primary-soft rounded-base shadow-lg border"
          [class.border-danger-subtle]="toast.tone === 'danger'"
          [class.border-success-subtle]="toast.tone === 'success'"
          [class.border-default]="toast.tone === 'info'"
          [attr.role]="toast.tone === 'danger' ? 'alert' : 'status'"
          data-testid="toast"
        >
          <span
            aria-hidden="true"
            class="mt-1 size-2.5 shrink-0 rounded-full"
            [class.bg-danger]="toast.tone === 'danger'"
            [class.bg-success]="toast.tone === 'success'"
            [class.bg-brand]="toast.tone === 'info'"
          ></span>
          <p class="flex-1 text-sm">{{ toast.message }}</p>
          <button
            type="button"
            class="-m-1.5 p-1.5 rounded-base text-body hover:text-heading hover:bg-neutral-secondary-medium min-h-8 min-w-8"
            [attr.aria-label]="dismissLabel"
            (click)="toasts.dismiss(toast.id)"
          >
            <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </div>
      }
    </div>
  `,
})
export class ToastRegion {
  protected readonly toasts = inject(ToastService);
  protected readonly dismissLabel = 'Dismiss';
}
