import { CdkDrag, CdkDragEnd, CdkDragHandle, CdkDragMove } from '@angular/cdk/drag-drop';
import { Component, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Layout, Widget } from '../../../core/projects/indicators.service';
import { Placed, changed, movedBy, readingOrder, reflow, resizedBy, unitsFor } from './grid-layout';
import { WidgetView } from './widget-view';

type Resize = 'move' | 'resize';

const arrows: Record<string, [number, number]> = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
/** Used when the grid has no measured width (before layout). */
const fallbackWidth = 1200;

/**
 * The widget canvas (design D7): a 12-column grid with a fixed row height, from `md` up; one column below it. Whoever may edit
 * (analyst, admin) moves a widget by its handle with the pointer or the arrow keys, and resizes it by the corner handle or with
 * shift and the arrow keys. A drop moves the widgets it lands on downward and reports every layout that changed; saving them is
 * the page's job. The widgets are in reading order (top to bottom, left to right) in the page, whatever their place.
 */
@Component({
  selector: 'app-widget-grid',
  imports: [CdkDrag, CdkDragHandle, TranslocoDirective, WidgetView],
  template: `
    <ul #grid class="tmz-grid" *transloco="let t" data-testid="widget-grid">
      @for (widget of ordered(); track widget.id) {
        <li
          class="tmz-item"
          [style.grid-column]="widget.layout.x + 1 + ' / span ' + widget.layout.w"
          [style.grid-row]="widget.layout.y + 1 + ' / span ' + widget.layout.h"
          [style.--h]="widget.layout.h"
          [attr.data-widget-id]="widget.id"
          data-testid="widget"
        >
          <article
            cdkDrag
            [cdkDragDisabled]="!editable()"
            (cdkDragMoved)="dragMoved(widget, $event)"
            (cdkDragEnded)="dragEnded(widget, $event)"
            class="flex h-full min-h-0 flex-col rounded-base border border-default bg-neutral-primary-soft shadow-xs"
            [class.relative]="dragging() === widget.id"
            [class.z-30]="dragging() === widget.id"
            [class.shadow-lg]="dragging() === widget.id"
            [attr.aria-labelledby]="'widget-title-' + widget.id"
          >
            <header class="flex shrink-0 items-center gap-1 border-b border-default px-3 py-1.5">
              @if (editable()) {
                <button
                  cdkDragHandle
                  type="button"
                  class="hidden size-9 shrink-0 cursor-grab items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary md:inline-flex"
                  [attr.aria-label]="t('indicators.grid.move', { title: widget.title })"
                  [attr.aria-describedby]="'grid-help'"
                  (keydown)="key(widget, $event, 'move')"
                  data-testid="widget-move"
                >
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="currentColor"><circle cx="9" cy="6" r="1.6" /><circle cx="15" cy="6" r="1.6" /><circle cx="9" cy="12" r="1.6" /><circle cx="15" cy="12" r="1.6" /><circle cx="9" cy="18" r="1.6" /><circle cx="15" cy="18" r="1.6" /></svg>
                </button>
              }
              <h3 [id]="'widget-title-' + widget.id" class="min-w-0 flex-1 truncate text-sm font-medium text-heading" [attr.title]="widget.title" data-testid="widget-title">{{ widget.title }}</h3>
              @if (editable()) {
                <button
                  type="button"
                  class="inline-flex size-9 shrink-0 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
                  [attr.aria-label]="t('indicators.grid.edit', { title: widget.title })"
                  (click)="edit.emit(widget)"
                  data-testid="widget-edit"
                >
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                </button>
                <button
                  type="button"
                  class="inline-flex size-9 shrink-0 items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-fg-danger-strong focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
                  [attr.aria-label]="t('indicators.grid.remove', { title: widget.title })"
                  (click)="remove.emit(widget)"
                  data-testid="widget-remove"
                >
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M5 7h14M10 7V4h4v3M7 7l1 13h8l1-13" /></svg>
                </button>
              }
            </header>
            <div class="min-h-0 flex-1 overflow-hidden p-3">
              <app-widget-view
                [projectId]="projectId()"
                [chartType]="widget.chartType"
                [config]="widget.config"
                [enabled]="published()"
                [allTables]="allTables()"
              />
            </div>
          </article>
          @if (editable()) {
            <button
              cdkDrag
              type="button"
              class="absolute bottom-0 end-0 z-20 hidden size-8 cursor-nwse-resize items-end justify-end rounded-ee-base p-1 text-body hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary md:inline-flex"
              [attr.aria-label]="t('indicators.grid.resize', { title: widget.title })"
              [attr.aria-describedby]="'grid-help'"
              (cdkDragMoved)="resizeMoved(widget, $event)"
              (cdkDragEnded)="resizeEnded(widget, $event)"
              (keydown)="key(widget, $event, 'resize')"
              data-testid="widget-resize"
            >
              <svg aria-hidden="true" class="size-3" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M11 3L3 11M11 7l-4 4" /></svg>
            </button>
          }
        </li>
      }
      @if (ghost(); as ghost) {
        <li
          class="tmz-ghost"
          aria-hidden="true"
          [style.grid-column]="ghost.x + 1 + ' / span ' + ghost.w"
          [style.grid-row]="ghost.y + 1 + ' / span ' + ghost.h"
          data-testid="widget-ghost"
        ></li>
      }
    </ul>
    <p id="grid-help" class="sr-only">{{ help() }}</p>
    <p class="sr-only" aria-live="polite" data-testid="grid-status">{{ status() }}</p>
  `,
  styles: [
    `
      .tmz-grid {
        display: grid;
        grid-template-columns: repeat(12, minmax(0, 1fr));
        grid-auto-rows: 72px;
        gap: 16px;
        margin: 0;
        padding: 0;
        list-style: none;
      }
      .tmz-item {
        position: relative;
        min-width: 0;
      }
      .tmz-ghost {
        border: 2px dashed currentColor;
        border-radius: 8px;
        pointer-events: none;
        opacity: 0.5;
      }
      /* Phones: one column in reading order; each widget keeps its height in rows. */
      @media (max-width: 47.99rem) {
        .tmz-grid {
          grid-template-columns: minmax(0, 1fr);
          grid-auto-rows: auto;
        }
        .tmz-item {
          height: calc(var(--h) * 72px + (var(--h) - 1) * 16px);
          grid-column: auto !important;
          grid-row: auto !important;
        }
        .tmz-ghost {
          display: none;
        }
      }
    `,
  ],
  host: { class: 'block' },
})
export class WidgetGrid {
  private readonly transloco = inject(TranslocoService);
  private readonly grid = viewChild.required<ElementRef<HTMLElement>>('grid');

  readonly widgets = input.required<Widget[]>();
  readonly projectId = input.required<string>();
  /** Analysts and admins: widgets get their move, resize, edit and remove controls. */
  readonly editable = input(false);
  /** False while nothing is published: widgets do not ask for rows. */
  readonly published = input(true);
  readonly allTables = input(false);

  /** Every layout a move or resize changed, the dropped widget's and those it pushed down. */
  readonly layoutsChange = output<Placed[]>();
  readonly edit = output<Widget>();
  readonly remove = output<Widget>();

  protected readonly ordered = computed(() => readingOrder(this.widgets()));
  protected readonly dragging = signal<string | null>(null);
  protected readonly ghost = signal<Layout | null>(null);
  protected readonly status = signal('');

  protected help(): string {
    return this.transloco.translate('indicators.grid.help');
  }

  private placed(): Placed[] {
    return this.widgets().map((w) => ({ id: w.id, layout: w.layout }));
  }

  private commit(widget: Widget, next: Layout): void {
    const before = this.placed();
    const diff = changed(before, reflow(before, widget.id, next));
    if (diff.length) {
      this.layoutsChange.emit(diff);
    }
    const now = diff.find((p) => p.id === widget.id)?.layout ?? widget.layout;
    this.status.set(
      this.transloco.translate('indicators.grid.status', { title: widget.title, column: now.x + 1, row: now.y + 1, width: now.w, height: now.h }),
    );
  }

  private units(distance: { x: number; y: number }) {
    return unitsFor(distance, this.grid().nativeElement.getBoundingClientRect().width || fallbackWidth);
  }

  protected dragMoved(widget: Widget, event: CdkDragMove): void {
    const { columns, rows } = this.units(event.distance);
    this.dragging.set(widget.id);
    this.ghost.set(movedBy(widget.layout, columns, rows));
  }

  protected dragEnded(widget: Widget, event: CdkDragEnd): void {
    const { columns, rows } = this.units(event.distance);
    event.source.reset();
    this.dragging.set(null);
    this.ghost.set(null);
    this.commit(widget, movedBy(widget.layout, columns, rows));
  }

  protected resizeMoved(widget: Widget, event: CdkDragMove): void {
    const { columns, rows } = this.units(event.distance);
    this.ghost.set(resizedBy(widget.layout, columns, rows));
  }

  protected resizeEnded(widget: Widget, event: CdkDragEnd): void {
    const { columns, rows } = this.units(event.distance);
    event.source.reset();
    this.ghost.set(null);
    this.commit(widget, resizedBy(widget.layout, columns, rows));
  }

  /** Arrow keys move the widget by a cell; with shift (or on the corner handle) they resize it by a cell. */
  protected key(widget: Widget, event: KeyboardEvent, handle: Resize): void {
    const step = arrows[event.key];
    if (!step || event.ctrlKey || event.altKey || event.metaKey) {
      return;
    }
    event.preventDefault();
    const [columns, rows] = step;
    this.commit(widget, handle === 'resize' || event.shiftKey ? resizedBy(widget.layout, columns, rows) : movedBy(widget.layout, columns, rows));
  }
}
