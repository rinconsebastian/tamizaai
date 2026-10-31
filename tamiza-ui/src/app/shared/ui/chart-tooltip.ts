import { FlexibleConnectedPositionStrategy, Overlay, OverlayRef } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { Component, ComponentRef, Injectable, inject, input } from '@angular/core';
import type { ConnectedPosition } from '@angular/cdk/overlay';

/**
 * What a tooltip says: a heading, lines of a label and a value, and an optional footer. All of it is shown as text, never as markup.
 * A line may carry the color of the series it is about, shown as a short key before its label (it is decoration: the label names it).
 */
export interface ChartTooltipContent {
  heading: string;
  lines: { label: string; value: string; align: 'start' | 'end'; color?: string | null }[];
  footer: string | null;
}

/** Where the pointer is, in the viewport's pixels. */
export interface ChartTooltipOrigin {
  x: number;
  y: number;
}

/** How far the tooltip sits from the point it is anchored to. */
export const tooltipOffset = 12;

/**
 * Below and to the right of the point first, then to the left, then above on either side: whichever fits in the viewport. The
 * origin is a point, so only the overlay's own corner matters.
 */
export const tooltipPositions: ConnectedPosition[] = [
  { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'top', offsetX: tooltipOffset, offsetY: tooltipOffset },
  { originX: 'start', originY: 'top', overlayX: 'end', overlayY: 'top', offsetX: -tooltipOffset, offsetY: tooltipOffset },
  { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetX: tooltipOffset, offsetY: -tooltipOffset },
  { originX: 'start', originY: 'top', overlayX: 'end', overlayY: 'bottom', offsetX: -tooltipOffset, offsetY: -tooltipOffset },
];

/**
 * The tooltip's content. Every string goes through a text binding (design D2). The panel is hidden from assistive technology on
 * purpose: the shape the keyboard is on carries the same text as its accessible name, so reading both would say it twice.
 */
@Component({
  selector: 'tmz-chart-tooltip',
  template: `
    <p class="font-medium text-heading" data-testid="tooltip-heading">{{ content().heading }}</p>
    @if (content().lines.length) {
      <dl class="mt-1 grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-0.5">
        @for (line of content().lines; track $index) {
          <div class="contents" data-testid="tooltip-line">
            <dt>
              @if (line.color) {
                <span aria-hidden="true" class="me-1.5 inline-block h-0.5 w-3 align-middle" [style.background]="line.color" data-testid="tooltip-key"></span>
              }
              {{ line.label }}:
            </dt>
            {{ ' ' }}<dd [class]="line.align === 'end' ? 'text-end tabular-nums' : 'text-start'">{{ line.value }}</dd>
          </div>
        }
      </dl>
    }
    @if (content().footer) {
      <p class="mt-1 text-xs text-body" data-testid="tooltip-footer">{{ content().footer }}</p>
    }
  `,
  host: {
    'aria-hidden': 'true',
    class: 'block max-w-72 break-words rounded-base border border-default-medium bg-neutral-primary-medium px-3 py-2 text-sm text-body shadow-lg',
    'data-testid': 'chart-tooltip',
  },
})
export class ChartTooltipBody {
  readonly content = input.required<ChartTooltipContent>();
}

/**
 * One tooltip a chart opens and closes, in a CDK overlay: it escapes the tile's `overflow-hidden` and the map's panes, and works
 * inside the widget form's dialog. It is detached when closed, not hidden. It never subscribes to the overlay's `keydownEvents()`:
 * the CDK's keyboard dispatcher would then give it the Escape of a dialog opened beneath it, so the chart closes it itself.
 */
export class ChartTooltip {
  private ref: OverlayRef | null = null;
  private strategy: FlexibleConnectedPositionStrategy | null = null;
  private body: ComponentRef<ChartTooltipBody> | null = null;
  private origin: ChartTooltipOrigin = { x: 0, y: 0 };

  constructor(
    private readonly overlay: Overlay,
    private readonly released: () => void,
  ) {}

  get isOpen(): boolean {
    return !!this.ref?.hasAttached();
  }

  /** Shows the tooltip, or changes what it says and where it is when it is open. */
  open(origin: ChartTooltipOrigin, content: ChartTooltipContent): void {
    this.origin = origin;
    if (!this.ref) {
      this.strategy = this.overlay.position().flexibleConnectedTo(origin).withPositions(tooltipPositions).withFlexibleDimensions(false).withPush(true);
      this.ref = this.overlay.create({ positionStrategy: this.strategy, scrollStrategy: this.overlay.scrollStrategies.noop(), panelClass: 'tmz-chart-tooltip' });
    }
    if (!this.ref.hasAttached()) {
      this.body = this.ref.attach(new ComponentPortal(ChartTooltipBody));
      // It follows the pointer, so it must never be under it. (Attaching gives the pane its pointer events back, so this comes after.)
      this.ref.overlayElement.style.pointerEvents = 'none';
    }
    this.body!.setInput('content', content);
    this.body!.changeDetectorRef.detectChanges();
    this.move(origin);
  }

  /** Moves the tooltip to a new point. */
  move(origin: ChartTooltipOrigin): void {
    this.origin = origin;
    this.strategy?.setOrigin(origin);
    this.ref?.updatePosition();
  }

  get at(): ChartTooltipOrigin {
    return this.origin;
  }

  close(): void {
    if (this.ref?.hasAttached()) {
      this.ref.detach();
    }
    this.body = null;
  }

  destroy(): void {
    this.ref?.dispose();
    this.ref = null;
    this.strategy = null;
    this.body = null;
    this.released();
  }
}

@Injectable({ providedIn: 'root' })
export class ChartTooltips {
  private readonly overlay = inject(Overlay);
  private readonly open = new Set<ChartTooltip>();

  /** A tooltip for one chart. Dispose of it with its `destroy()`. */
  create(): ChartTooltip {
    const tooltip: ChartTooltip = new ChartTooltip(this.overlay, () => this.open.delete(tooltip));
    this.open.add(tooltip);
    return tooltip;
  }

  /** Closes every tooltip: a tile being dragged or resized moves the chart from under the pointer. */
  closeAll(): void {
    for (const tooltip of this.open) {
      tooltip.close();
    }
  }
}
