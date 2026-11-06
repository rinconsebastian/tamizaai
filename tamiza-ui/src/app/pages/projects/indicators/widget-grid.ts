import { CdkDrag, CdkDragEnd, CdkDragHandle, CdkDragMove } from '@angular/cdk/drag-drop';
import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { DashboardBases, DashboardColors, IndicatorEntry, Layout, Widget } from '../../../core/projects/indicators.service';
import { ChartTooltips, DropdownMenu, MenuGroup, MenuItem } from '../../../shared/ui';
import { noBaseSettings } from './base-figures';
import { defaultColors } from './chart-colors';
import { ActiveDashboardFilter } from './dashboard-filters';
import { DashboardPrint } from './dashboard-print';
import { DescriptionPopover } from './description-popover';
import { Placed, changed, movedBy, readingOrder, reflow, resizedBy, unitsFor } from './grid-layout';
import { WidgetFileExtension } from './widget-export/file-name';
import { ExportState, WidgetView } from './widget-view';

type Resize = 'move' | 'resize';

/** What a member chose from a widget's menu: the widget, the format, and the view that knows what it shows (add-widget-export). */
export interface WidgetExportChoice {
  widget: Widget;
  format: WidgetFileExtension;
  view: WidgetView;
}

const arrows: Record<string, [number, number]> = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
/** Used when the grid has no measured width (before layout). */
const fallbackWidth = 1200;

/**
 * The widget canvas (design D7): a 12-column grid with a fixed row height, from `md` up and on paper; one column below it on
 * screens. Whoever may edit (analyst, admin) moves a widget by its handle with the pointer or the arrow keys, and resizes it by
 * the corner handle or with shift and the arrow keys. A drop moves the widgets it lands on downward and reports every layout that
 * changed; saving them is the page's job. The widgets are in reading order (top to bottom, left to right) in the page, whatever
 * their place.
 */
@Component({
  selector: 'app-widget-grid',
  imports: [CdkDrag, CdkDragHandle, NgTemplateOutlet, TranslocoDirective, DropdownMenu, DescriptionPopover, WidgetView],
  template: `
    <ul #grid class="tmz-grid" [class.tmz-paper]="paper()" *transloco="let t" data-testid="widget-grid">
      @for (widget of ordered(); track widget.id) {
        <li
          class="tmz-item"
          [style.grid-column]="widget.layout.x + 1 + ' / span ' + widget.layout.w"
          [style.grid-row]="widget.layout.y + 1 + ' / span ' + spanOf(widget)"
          [style.--h]="spanOf(widget)"
          [attr.data-widget-id]="widget.id"
          data-testid="widget"
        >
          <article
            cdkDrag
            [cdkDragDisabled]="!editable()"
            (cdkDragStarted)="tileMoving()"
            (cdkDragMoved)="dragMoved(widget, $event)"
            (cdkDragEnded)="dragEnded(widget, $event)"
            class="flex h-full min-h-0 flex-col rounded-base border border-default bg-neutral-primary-soft shadow-xs paper:bg-white paper:shadow-none"
            [class.relative]="dragging() === widget.id"
            [class.z-30]="dragging() === widget.id"
            [class.shadow-lg]="dragging() === widget.id"
            [attr.aria-labelledby]="'widget-title-' + widget.id"
            [attr.aria-describedby]="subtitleOf(widget) ? 'widget-subtitle-' + widget.id : null"
          >
            <!-- A widget with a subtitle has a header of two rows, the usual row and the subtitle; one without keeps its one row. -->
            @if (subtitleOf(widget); as subtitle) {
              <header class="flex shrink-0 flex-col gap-0.5 border-b border-default px-3 py-1.5">
                <div class="flex min-w-0 items-center gap-1">
                  <ng-container *ngTemplateOutlet="headerRow" />
                </div>
                <p
                  [id]="'widget-subtitle-' + widget.id"
                  class="text-xs text-body paper:line-clamp-none"
                  [class.line-clamp-1]="spanOf(widget) <= 2"
                  [class.line-clamp-2]="spanOf(widget) > 2"
                  [attr.title]="subtitle"
                  data-testid="widget-subtitle"
                >{{ subtitle }}</p>
              </header>
            } @else {
              <header class="flex shrink-0 items-center gap-1 border-b border-default px-3 py-1.5">
                <ng-container *ngTemplateOutlet="headerRow" />
              </header>
            }
            <ng-template #headerRow>
              @if (editable()) {
                <button
                  cdkDragHandle
                  type="button"
                  class="hidden size-9 shrink-0 cursor-grab items-center justify-center rounded-base text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary md:inline-flex paper:hidden"
                  [attr.aria-label]="t('indicators.grid.move', { title: widget.title })"
                  [attr.aria-describedby]="'grid-help'"
                  (keydown)="key(widget, $event, 'move')"
                  data-testid="widget-move"
                >
                  <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="currentColor"><circle cx="9" cy="6" r="1.6" /><circle cx="15" cy="6" r="1.6" /><circle cx="9" cy="12" r="1.6" /><circle cx="15" cy="12" r="1.6" /><circle cx="9" cy="18" r="1.6" /><circle cx="15" cy="18" r="1.6" /></svg>
                </button>
              }
              <h3 [id]="'widget-title-' + widget.id" class="min-w-0 flex-1 truncate text-sm font-medium text-heading" [attr.title]="widget.title" data-testid="widget-title">{{ widget.title }}</h3>
              <!-- The indicator's description on demand, for a widget that offers it (add-widget-context, design D6): never on paper. -->
              @if (descriptionOf(widget); as description) {
                <app-description-popover class="paper:hidden" [label]="indicatorOf(widget)!.label" [description]="description" />
              }
              <!-- One menu for every member: Show as table, the Export formats and, for editors, Edit and Remove. -->
              <tmz-dropdown-menu
                class="paper:hidden"
                triggerTestId="widget-menu"
                [label]="t(isExporting(widget) ? 'indicators.grid.menuForPreparing' : 'indicators.grid.menuFor', { title: widget.title })"
                [busy]="isExporting(widget)"
                [groups]="menuGroups(widget, view)"
                (selected)="menuChosen(widget, view, $event)"
              />
            </ng-template>
            <div class="min-h-0 flex-1 overflow-hidden p-3">
              <app-widget-view
                #view
                [projectId]="projectId()"
                [chartType]="widget.chartType"
                [config]="widget.config"
                [indicator]="indicatorOf(widget)"
                [scriptsLink]="editable()"
                [showToggle]="false"
                [asTable]="shownAsTable(widget)"
                [dashboardFilters]="filtersOf().get(widget.id) ?? none"
                [colors]="colors()"
                [bases]="bases()"
                [tileRows]="spanOf(widget)"
                (tableAvailable)="setDrawable(widget.id, $event)"
              />
            </div>
          </article>
          @if (editable()) {
            <button
              cdkDrag
              type="button"
              class="absolute bottom-0 end-0 z-20 hidden size-8 cursor-nwse-resize items-end justify-end rounded-ee-base p-1 text-body hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary md:inline-flex paper:hidden"
              [attr.aria-label]="t('indicators.grid.resize', { title: widget.title })"
              [attr.aria-describedby]="'grid-help'"
              (cdkDragStarted)="tileMoving()"
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
      /*
       * Phone screens: one column in reading order; each widget keeps its height in rows. Never on paper, nor while the page prepares
       * the print (tmz-paper): a sheet is narrower than this breakpoint, and it keeps the 12 columns (dashboard printing, design D4).
       */
      @media screen and (max-width: 47.99rem) {
        .tmz-grid:not(.tmz-paper) {
          grid-template-columns: minmax(0, 1fr);
          grid-auto-rows: auto;
        }
        .tmz-grid:not(.tmz-paper) > .tmz-item {
          height: calc(var(--h) * 72px + (var(--h) - 1) * 16px);
          grid-column: auto !important;
          grid-row: auto !important;
        }
        .tmz-ghost {
          display: none;
        }
      }
      /*
       * On paper each tile starts 2px below its grid area. A browser that scales the page down to the sheet (a portrait sheet, or a
       * scale picked in the dialog) can paint the first pixel of the next sheet at the foot of this one, where the top border of a
       * tile moved to the next sheet would show as a stray line (dashboard printing, design D2).
       */
      .tmz-grid.tmz-paper > .tmz-item {
        padding-top: 2px;
      }
      /* A widget that fits on a sheet starts on the next one rather than being split; colors print without "Background graphics". */
      @media print {
        .tmz-grid {
          -webkit-print-color-adjust: exact;
          print-color-adjust: exact;
        }
        .tmz-item {
          break-inside: avoid;
        }
      }
    `,
  ],
  host: { class: 'block' },
})
export class WidgetGrid {
  private readonly transloco = inject(TranslocoService);
  private readonly tooltips = inject(ChartTooltips);
  /** The dashboard page's paper mode; none where the grid is used without it. */
  private readonly print = inject(DashboardPrint, { optional: true });
  private readonly grid = viewChild.required<ElementRef<HTMLElement>>('grid');

  readonly widgets = input.required<Widget[]>();
  readonly projectId = input.required<string>();
  /** Analysts and admins: widgets get their move, resize, edit and remove controls. */
  readonly editable = input(false);
  /** The project's indicators: a widget draws with the format of its own, and does not ask for rows while that one has no table. */
  readonly indicators = input<IndicatorEntry[]>([]);
  readonly allTables = input(false);
  /** The dashboard filters with a value in force on the member's screen, in the bar's order (dashboard filters, design D7). */
  readonly dashboardFilters = input<ActiveDashboardFilter[]>([]);
  /** The palette and the colors kept by value of the dashboard shown, or of the version shown: each widget is drawn in them (add-dashboard-palettes, design D7). */
  readonly colors = input<DashboardColors>(defaultColors);
  /** The base settings of the dashboard shown, or of the version shown: each widget applies its own over them (add-small-sample-warnings, design D7, D8). */
  readonly bases = input<DashboardBases>(noBaseSettings);
  /** The widgets whose export is being prepared, by id, with the format: only their menu is busy, and only their formats are unavailable. */
  readonly exporting = input<Record<string, WidgetFileExtension>>({});

  /** Every layout a move or resize changed, the dropped widget's and those it pushed down. */
  readonly layoutsChange = output<Placed[]>();
  readonly edit = output<Widget>();
  readonly remove = output<Widget>();
  /** A format chosen from a widget's menu. The page makes the file: it knows the project, the dashboard, the version and the filters. */
  readonly exportWidget = output<WidgetExportChoice>();

  protected readonly ordered = computed(() => readingOrder(this.widgets()));
  /** Laid out as it prints: the 12 columns at every width, the phone layout off. */
  protected readonly paper = computed(() => this.print?.printing() ?? false);
  protected readonly none: ActiveDashboardFilter[] = [];
  /** Each widget's dashboard filters, marked where it ignores them; one list per widget, so a widget only re-queries when its own changes. */
  protected readonly filtersOf = computed(() => {
    const active = this.dashboardFilters();
    return new Map(
      this.widgets().map((widget) => {
        const ignored = widget.config.ignoredDashboardFilters ?? [];
        return [widget.id, active.map((a) => ({ ...a, ignored: ignored.includes(a.filter.column) }))];
      }),
    );
  });
  /** The widgets whose table the member chose to see (or to hide again), by id; a widget without an entry follows `allTables`. */
  private readonly tableChoice = signal<Record<string, boolean>>({});
  /** The widgets that currently have rows to draw, by id: only those get the chart/table toggle. */
  protected readonly drawable = signal<Record<string, boolean>>({});
  /** The editing actions of a widget's menu, with their translated names. */
  protected readonly actions = computed<MenuItem[]>(() => [
    { id: 'edit', label: this.transloco.translate('indicators.grid.editItem'), testId: 'widget-edit' },
    { id: 'remove', label: this.transloco.translate('indicators.grid.removeItem'), danger: true, testId: 'widget-remove' },
  ]);
  protected readonly dragging = signal<string | null>(null);
  protected readonly ghost = signal<Layout | null>(null);
  protected readonly status = signal('');

  constructor() {
    // "Show all as tables" is a bulk switch: it resets the choices made on single widgets.
    effect(() => {
      this.allTables();
      untracked(() => this.tableChoice.set({}));
    });
  }

  protected shownAsTable(widget: Widget): boolean {
    return this.tableChoice()[widget.id] ?? this.allTables();
  }

  protected toggleTable(widget: Widget): void {
    const next = !this.shownAsTable(widget);
    this.tableChoice.update((choice) => ({ ...choice, [widget.id]: next }));
  }

  protected isExporting(widget: Widget): boolean {
    return widget.id in this.exporting();
  }

  /**
   * The parts of a widget's menu (design D3 of unify-widget-menu-and-ten-dashboards): Show as table, while the widget has rows to draw;
   * the Export formats, under their heading; and, for editors, Edit and Remove. A part with nothing in it is left out.
   */
  protected menuGroups(widget: Widget, view: WidgetView): MenuGroup[] {
    const groups: MenuGroup[] = [];
    if (this.drawable()[widget.id]) {
      groups.push({
        id: 'view',
        items: [{ id: 'table', label: this.transloco.translate('indicators.widget.showTable'), checked: this.shownAsTable(widget), testId: 'widget-show-table' }],
      });
    }
    groups.push({ id: 'export', label: this.transloco.translate('indicators.grid.exportGroup'), items: this.exportItems(view.exportState(), this.isExporting(widget)) });
    if (this.editable()) {
      groups.push({ id: 'edit', items: this.actions() });
    }
    return groups;
  }

  /**
   * The three formats of a widget's Export group. One that cannot be made stays in the menu, announced as unavailable, with the reason
   * under it (design D3 of add-widget-export); an image that can be made may carry a line about what it leaves out. While the widget's
   * own export is prepared, none can be started again.
   */
  protected exportItems(state: ExportState, exporting = false): MenuItem[] {
    const t = (key: string) => this.transloco.translate(`indicators.widgetExport.${key}`);
    const busy = exporting ? t('reason.preparing') : null;
    return [
      { id: 'png', label: t('png'), testId: 'widget-export-png', disabled: exporting || state.image !== null, description: busy ?? state.image ?? state.imageNote ?? undefined },
      { id: 'svg', label: t('svg'), testId: 'widget-export-svg', disabled: exporting || state.image !== null, description: busy ?? state.image ?? state.imageNote ?? undefined },
      { id: 'xlsx', label: t('xlsx'), testId: 'widget-export-xlsx', disabled: exporting || state.workbook !== null, description: busy ?? state.workbook ?? undefined },
    ];
  }

  /** What was chosen in a widget's menu: the table view, a format to export, or an editing action. */
  protected menuChosen(widget: Widget, view: WidgetView, id: string): void {
    if (id === 'table') {
      this.toggleTable(widget);
      this.status.set(this.transloco.translate(this.shownAsTable(widget) ? 'indicators.grid.shownAsTable' : 'indicators.grid.shownAsChart', { title: widget.title }));
    } else if (id === 'edit' || id === 'remove') {
      this.act(widget, id);
    } else {
      this.exportChosen(widget, view, id);
    }
  }

  protected exportChosen(widget: Widget, view: WidgetView, format: string): void {
    if (format === 'png' || format === 'svg' || format === 'xlsx') {
      this.exportWidget.emit({ widget, format, view });
    }
  }

  protected setDrawable(id: string, value: boolean): void {
    this.drawable.update((state) => (state[id] === value ? state : { ...state, [id]: value }));
  }

  protected act(widget: Widget, action: string): void {
    if (action === 'edit') {
      this.edit.emit(widget);
    } else if (action === 'remove') {
      this.remove.emit(widget);
    }
  }

  /** A widget whose indicator has not published yet has nothing to draw, so its tile is short until it does. */
  protected spanOf(widget: Widget): number {
    const indicator = this.indicatorOf(widget);
    return indicator && !indicator.published ? Math.min(widget.layout.h, 2) : widget.layout.h;
  }

  protected indicatorOf(widget: Widget): IndicatorEntry | null {
    return this.indicators().find((indicator) => indicator.name === widget.config.indicator) ?? null;
  }

  /** The widget's subtitle, trimmed, or null while it has none (add-widget-context, design D5). */
  protected subtitleOf(widget: Widget): string | null {
    return widget.config.display?.subtitle?.trim() || null;
  }

  /** The description its button opens: the widget offers it and the indicator the project lists has one (design D6); null otherwise. */
  protected descriptionOf(widget: Widget): string | null {
    return widget.config.display?.showDescription === true ? (this.indicatorOf(widget)?.description?.trim() || null) : null;
  }

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

  /** A tile moves from under the pointer: a tooltip left open over it would point at nothing. */
  protected tileMoving(): void {
    this.tooltips.closeAll();
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
    this.tileMoving();
    const [columns, rows] = step;
    this.commit(widget, handle === 'resize' || event.shiftKey ? resizedBy(widget.layout, columns, rows) : movedBy(widget.layout, columns, rows));
  }
}
