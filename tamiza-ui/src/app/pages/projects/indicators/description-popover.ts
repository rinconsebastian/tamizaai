import { CdkConnectedOverlay, CdkOverlayOrigin, ConnectedPosition } from '@angular/cdk/overlay';
import { Component, DestroyRef, Injectable, Injector, afterNextRender, computed, inject, input, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button } from '../../../shared/ui';

/** Which description is open: one at a time, so opening another closes the first (add-widget-context, design D6). */
@Injectable({ providedIn: 'root' })
export class DescriptionPopovers {
  readonly open = signal<string | null>(null);
}

/** Below the button and aligned to its end first, then above, then the same from its start: whichever fits in the viewport. */
const positions: ConnectedPosition[] = [
  { originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetY: 4 },
  { originX: 'end', originY: 'top', overlayX: 'end', overlayY: 'bottom', offsetY: -4 },
  { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetY: 4 },
  { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetY: -4 },
];

/**
 * The button of a widget that offers its indicator's description (add-widget-context, design D6) and the description it opens next to
 * it. The button is named after the indicator and says whether the description is open; the description is plain text in a panel that
 * is not modal. Opening moves focus into the panel. Escape, the button and a click elsewhere close it, and focus is back on the button
 * unless the click went to another control. Tab and Shift+Tab inside the panel close it and leave from the button, since the panel is
 * at the end of the page. A long description scrolls inside the panel. Only one description is open at a time.
 */
@Component({
  selector: 'app-description-popover',
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, TranslocoDirective, Button],
  template: `
    <ng-container *transloco="let t">
      <button
        tmzButton
        variant="ghost"
        size="sm"
        type="button"
        class="min-w-10"
        cdkOverlayOrigin
        #origin="cdkOverlayOrigin"
        aria-haspopup="dialog"
        [attr.aria-label]="t('indicators.grid.about', { label: label() })"
        [attr.aria-expanded]="isOpen()"
        [attr.aria-controls]="panelId"
        (click)="toggle()"
        data-testid="widget-description"
      >
        <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="9" /><path d="M12 11v5" /><circle cx="12" cy="7.8" r="0.6" fill="currentColor" />
        </svg>
      </button>
      <ng-template
        cdkConnectedOverlay
        [cdkConnectedOverlayOrigin]="origin"
        [cdkConnectedOverlayOpen]="isOpen()"
        [cdkConnectedOverlayPositions]="positions"
        (overlayOutsideClick)="outside($event, origin.elementRef.nativeElement)"
        (overlayKeydown)="keydown($event)"
        (attach)="focusPanel()"
      >
        <div
          [id]="panelId"
          role="dialog"
          tabindex="-1"
          [attr.aria-labelledby]="panelId + '-title'"
          class="max-h-[min(24rem,70vh)] max-w-80 overflow-y-auto rounded-base border border-default-medium bg-neutral-primary-medium px-3 py-2 text-sm text-body shadow-lg focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
          data-testid="widget-description-panel"
        >
          <p [id]="panelId + '-title'" class="break-words font-medium text-heading">{{ label() }}</p>
          <p class="mt-1 whitespace-pre-line break-words" data-testid="widget-description-text">{{ description() }}</p>
        </div>
      </ng-template>
    </ng-container>
  `,
})
export class DescriptionPopover {
  private static next = 0;

  private readonly popovers = inject(DescriptionPopovers);
  private readonly injector = inject(Injector);

  /** The indicator's label, which names the button and the panel. */
  readonly label = input.required<string>();
  /** The indicator's description, shown as plain text. */
  readonly description = input.required<string>();

  private readonly id = `widget-description-${DescriptionPopover.next++}`;
  protected readonly panelId = `${this.id}-panel`;
  protected readonly positions = positions;
  protected readonly isOpen = computed(() => this.popovers.open() === this.id);

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.popovers.open() === this.id) {
        this.popovers.open.set(null);
      }
    });
  }

  protected toggle(): void {
    if (this.isOpen()) {
      this.close(true);
    } else {
      this.popovers.open.set(this.id);
    }
  }

  /** Moves focus into the panel once it is drawn, so a keyboard user reads it and Escape reaches it. */
  protected focusPanel(): void {
    afterNextRender(() => document.getElementById(this.panelId)?.focus(), { injector: this.injector });
  }

  protected keydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close(true);
    } else if (event.key === 'Tab' && event.target instanceof Node && document.getElementById(this.panelId)?.contains(event.target)) {
      // The panel lives at the end of <body>, so Tab (or Shift+Tab) from it would leave the widget's header. Closing it first puts
      // focus back on the button before the browser moves it, which keeps the keyboard order in the header.
      this.close(true);
    }
  }

  /**
   * A click away from the panel closes it. The button toggles it itself. Focus goes back to the button unless the click went to
   * another control, which keeps the focus it was given.
   */
  protected outside(event: MouseEvent, button: HTMLElement): void {
    const target = event.target as Node | null;
    if (target && button.contains(target)) {
      return;
    }
    const active = document.activeElement;
    this.close(!active || active === document.body || !!document.getElementById(this.panelId)?.contains(active));
  }

  private close(restoreFocus: boolean): void {
    if (this.popovers.open() === this.id) {
      this.popovers.open.set(null);
    }
    if (restoreFocus) {
      const button = document.querySelector<HTMLElement>(`[aria-controls="${this.panelId}"]`);
      button?.focus();
    }
  }
}
