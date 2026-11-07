import { CdkConnectedOverlay, CdkOverlayOrigin, ConnectedPosition } from '@angular/cdk/overlay';
import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { TilePopovers } from './description-popover';

/** The chips of a widget's foot, in the order they come (improve-widget-text-hierarchy, design D4). */
export type FootKind = 'caption' | 'n' | 'small' | 'noData' | 'lines' | 'detail' | 'note';

/**
 * One chip of the foot: its short text, the whole text it opens, and whether it warns. `kind` is unique in a foot, so it is the chip's key.
 * `warning` chips are amber with a warning icon.
 */
export interface FootItem {
  kind: FootKind;
  /** The chip's name: short, cut by the chip when it is longer than it. */
  label: string;
  /** The whole text: what the panel opens, what a screen reader gets as the chip's description, and the line printed on paper. */
  text: string;
  tone: 'neutral' | 'warning';
  /**
   * A shorter text for the line printed on paper, when the whole one would not fit a short tile (the amber n of a stat tile). The page
   * keeps the whole text for screen readers and the panel, and an image and the email keep it too.
   */
  printText?: string;
}

/** Above the chip first, then below, each from the chip's start and then from its end: whichever fits in the viewport. */
const positions: ConnectedPosition[] = [
  { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetY: -4 },
  { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetY: 4 },
  { originX: 'end', originY: 'top', overlayX: 'end', overlayY: 'bottom', offsetY: -4 },
  { originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetY: 4 },
];

/** How long a panel stays open after the pointer leaves its chip, so crossing the foot does not make it flicker. */
const closeDelayMs = 100;

/**
 * The foot of a widget (improve-widget-text-hierarchy, design D4, D9): the small things a widget says about its figures, as chips under
 * its chart, its table or its message that no row matches. Each chip is a button that opens a panel with its whole text: by a click or a
 * tap (pinned until the next click, a click elsewhere or Escape), by keyboard focus and while the pointer is on it (transient). Only one
 * panel of a tile is open at a time, a widget's description included (`TilePopovers`). Each chip says whether it is open, and is described
 * by a line of its whole text that is always in the page, so screen readers hear it open or not. On paper the chips are not printed: each
 * is a line of whole text instead (`paper:`), the subtitle of a compact stat tile included: it is the Detail chip's line, under the figure.
 */
@Component({
  selector: 'app-widget-foot',
  imports: [CdkConnectedOverlay, CdkOverlayOrigin],
  template: `
    @if (items().length) {
      <div class="flex flex-wrap gap-1.5 border-t border-default pt-1.5 paper:hidden" data-testid="widget-foot">
        @for (item of items(); track item.kind) {
          <button
            type="button"
            cdkOverlayOrigin
            #origin="cdkOverlayOrigin"
            [class]="chipClass(item)"
            [attr.aria-expanded]="isOpen(item.kind)"
            [attr.aria-describedby]="lineId(item.kind)"
            (pointerenter)="enter($event, item.kind)"
            (pointerleave)="leave($event, item.kind)"
            (focus)="focused($event, item.kind)"
            (blur)="blurred(item.kind)"
            (click)="click(item.kind)"
            (keydown.escape)="escape($event, item.kind)"
            [attr.data-testid]="'foot-chip-' + item.kind"
            [attr.data-tone]="item.tone"
          >
            <svg aria-hidden="true" class="size-3.5 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" data-testid="foot-chip-icon">
              @if (item.tone === 'warning') {
                <path d="M12 3 2.5 20h19L12 3Z" /><path d="M12 10v4" /><circle cx="12" cy="17" r="0.6" fill="currentColor" />
              } @else {
                @switch (item.kind) {
                  @case ('caption') {
                    <path d="M3 8h18v8H3z" /><path d="M7 8v3M11 8v4M15 8v3M19 8v4" />
                  }
                  @case ('n') {
                    <circle cx="9" cy="8" r="3" /><path d="M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6" /><path d="M16 5.5a3 3 0 0 1 0 5M18 14.3c1.8.8 3 2.6 3 4.7" />
                  }
                  @case ('noData') {
                    <circle cx="12" cy="12" r="9" /><path d="M5.6 5.6l12.8 12.8" />
                  }
                  @case ('lines') {
                    <path d="M3 12h4M10 12h4M17 12h4" /><path d="M3 6v12M21 6v12" />
                  }
                  @default {
                    <circle cx="12" cy="12" r="9" /><path d="M12 11v5" /><circle cx="12" cy="7.8" r="0.6" fill="currentColor" />
                  }
                }
              }
            </svg>
            <span class="min-w-0 max-w-40 truncate">{{ item.label }}</span>
          </button>
          <ng-template
            cdkConnectedOverlay
            [cdkConnectedOverlayOrigin]="origin"
            [cdkConnectedOverlayOpen]="isOpen(item.kind)"
            [cdkConnectedOverlayPositions]="positions"
            (overlayOutsideClick)="outside($event, origin.elementRef.nativeElement, item.kind)"
            (overlayKeydown)="panelKey($event, item.kind)"
          >
            <!-- The panel is not a stop of the keyboard and is not read: its text is already the chip's description (design D9). -->
            <div
              aria-hidden="true"
              class="max-w-72 break-words rounded-base border border-default-medium bg-neutral-primary-medium px-3 py-2 text-sm text-body shadow-lg"
              [attr.data-testid]="'foot-panel'"
              [attr.data-kind]="item.kind"
            >{{ item.text }}</div>
          </ng-template>
        }
      </div>
      <!-- On paper each chip is a line of its whole text, in the foot's order. A chip's description points at its line, which stays in the page. -->
      <ul class="hidden paper:block" data-testid="widget-foot-lines">
        @for (item of items(); track item.kind) {
          <li [id]="lineId(item.kind)" class="text-xs text-body" [class]="compact() ? 'paper:text-[10px] paper:leading-[12px]' : ''" [attr.data-testid]="'foot-line-' + item.kind">
            @if (item.printText) {
              <span class="paper:hidden" data-testid="foot-line-whole">{{ item.text }}</span><span class="hidden paper:inline" aria-hidden="true" data-testid="foot-line-print">{{ item.printText }}</span>
            } @else {
              {{ item.text }}
            }
          </li>
        }
      </ul>
    }
  `,
  host: { class: 'block shrink-0' },
})
export class WidgetFoot {
  private static next = 0;

  readonly items = input.required<FootItem[]>();
  /** A stat tile's foot: its lines print at 10 px, so a tile two rows high holds more of them after its figure and label. */
  readonly compact = input(false);

  private readonly popovers = inject(TilePopovers);
  private readonly uid = `widget-foot-${WidgetFoot.next++}`;
  protected readonly positions = positions;
  /** The kind of the chip that a click pinned open: it stays until a second click, a click elsewhere or Escape. */
  private readonly pinned = signal<FootKind | null>(null);
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.cancelClose();
      if (this.popovers.open()?.startsWith(this.uid)) {
        this.popovers.open.set(null);
      }
    });
  }

  private key(kind: FootKind): string {
    return `${this.uid}-${kind}`;
  }

  /** The id of the line of whole text that describes a chip. */
  protected lineId(kind: FootKind): string {
    return `${this.uid}-line-${kind}`;
  }

  protected isOpen(kind: FootKind): boolean {
    return this.popovers.open() === this.key(kind);
  }

  protected chipClass(item: FootItem): string {
    const base =
      'inline-flex min-h-[22px] max-w-full items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium leading-4 focus:outline-none focus-visible:ring-4 focus-visible:ring-neutral-tertiary';
    return item.tone === 'warning'
      ? `${base} border-warning-subtle bg-warning-soft text-fg-warning`
      : `${base} border-transparent bg-neutral-secondary-medium text-body`;
  }

  private open(kind: FootKind, pinned: boolean): void {
    this.cancelClose();
    this.pinned.set(pinned ? kind : null);
    this.popovers.open.set(this.key(kind));
  }

  private close(kind: FootKind): void {
    this.cancelClose();
    if (this.isOpen(kind)) {
      this.popovers.open.set(null);
    }
    if (this.pinned() === kind) {
      this.pinned.set(null);
    }
  }

  private cancelClose(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  /** The pointer on a chip opens it, unpinned; a touch does not (its tap is a click). */
  protected enter(event: PointerEvent, kind: FootKind): void {
    if (event.pointerType === 'touch') {
      return;
    }
    this.cancelClose();
    if (!this.isOpen(kind)) {
      this.open(kind, false);
    }
  }

  /** The pointer leaving closes an unpinned chip after a short wait. */
  protected leave(event: PointerEvent, kind: FootKind): void {
    if (event.pointerType === 'touch' || !this.isOpen(kind) || this.pinned() === kind) {
      return;
    }
    this.cancelClose();
    this.timer = setTimeout(() => {
      this.timer = null;
      if (this.isOpen(kind) && this.pinned() !== kind) {
        this.close(kind);
      }
    }, closeDelayMs);
  }

  /** Keyboard focus opens a chip, unpinned; the focus a click gives does not, since the click decides then. */
  protected focused(event: FocusEvent, kind: FootKind): void {
    let keyboard = true;
    try {
      keyboard = (event.currentTarget as HTMLElement).matches(':focus-visible');
    } catch {
      // A browser that does not know the selector reads every focus as the keyboard's.
    }
    if (keyboard && !this.isOpen(kind)) {
      this.open(kind, false);
    }
  }

  protected blurred(kind: FootKind): void {
    if (this.isOpen(kind) && this.pinned() !== kind) {
      this.close(kind);
    }
  }

  /** A click or a tap pins a chip that is not pinned and closes one that is, whatever opened it first. */
  protected click(kind: FootKind): void {
    if (this.isOpen(kind) && this.pinned() === kind) {
      this.close(kind);
    } else {
      this.open(kind, true);
    }
  }

  /** Escape closes the open chip and leaves the focus where it is. */
  protected escape(event: Event, kind: FootKind): void {
    if (this.isOpen(kind)) {
      event.preventDefault();
      this.close(kind);
    }
  }

  protected panelKey(event: KeyboardEvent, kind: FootKind): void {
    if (event.key === 'Escape') {
      this.escape(event, kind);
    }
  }

  /** A click away from the chip and its panel closes it; a click on the chip is its own click's business. */
  protected outside(event: MouseEvent, chip: HTMLElement, kind: FootKind): void {
    const target = event.target as Node | null;
    if (target && chip.contains(target)) {
      return;
    }
    this.close(kind);
  }
}
