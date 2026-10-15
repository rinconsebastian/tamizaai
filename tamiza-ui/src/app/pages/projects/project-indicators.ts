import { Dialog } from '@angular/cdk/dialog';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import { ExportFormat } from '../../core/projects/data.service';
import { IndicatorEntry, IndicatorsOverview, IndicatorsService, Layout, Widget } from '../../core/projects/indicators.service';
import { Alert, Badge, Button, Card, ConfirmDialog, Disclosure, Skeleton, ToastService, dateTimeFormat } from '../../shared/ui';
import { Placed } from './indicators/grid-layout';
import { WidgetDialog, WidgetDialogData } from './indicators/widget-dialog';
import { WidgetGrid } from './indicators/widget-grid';
import { ProjectContext } from './project-context';

/** Wait this long after a drag or resize ends before saving the layout, so a run of keystrokes or drags is one request. */
export const layoutSaveDelayMs = 400;

/**
 * The indicators dashboard: a canvas of widgets, each a chart or map over one indicator's table, and, after it, the project's
 * indicators with whether each has published its table. Every member sees it and exports a table; analysts and admins add, edit,
 * arrange and remove widgets (design D5, D6). While no indicator has published, it says so, shows the indicators as part of that
 * message and still lists the widgets already configured.
 */
@Component({
  selector: 'app-project-indicators',
  imports: [
    DatePipe,
    DecimalPipe,
    NgTemplateOutlet,
    RouterLink,
    TranslocoDirective,
    CdkMenu,
    CdkMenuItem,
    CdkMenuTrigger,
    Alert,
    Badge,
    Button,
    Card,
    Disclosure,
    Skeleton,
    WidgetGrid,
  ],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t" [attr.aria-busy]="!overview() && !error()">
      @if (error(); as message) {
        <tmz-alert tone="danger" data-testid="indicators-error">{{ message }}</tmz-alert>
      }

      @if (overview(); as overview) {
        <!-- The indicators, one line each: shown inside the empty state while nothing has published, behind a disclosure after the widgets otherwise. -->
        <ng-template #inventory>
          <ul class="flex flex-col divide-y divide-default" data-testid="indicator-table">
            @for (indicator of overview.indicators; track indicator.name) {
              <li class="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm" [attr.data-testid]="'indicator-row-' + indicator.name">
                <span class="min-w-0 flex-1">
                  <span class="break-words font-medium text-heading" data-testid="indicator-label">{{ indicator.label }}</span>
                  <code class="ms-2 text-xs text-body" data-testid="indicator-name">{{ indicator.name }}</code>
                </span>
                <span class="whitespace-nowrap text-body" data-testid="indicator-format">
                  {{ t('scripts.formats.' + indicator.format) }}{{ indicator.unit ? ' · ' + indicator.unit : '' }}
                </span>
                @if (indicator.published) {
                  <tmz-badge tone="success" [attr.title]="indicator.runId ? t('indicators.publishedRun', { run: indicator.runId.slice(0, 8) }) : null">{{ t('indicators.list.published') }}</tmz-badge>
                  <span class="whitespace-nowrap text-xs text-body" data-testid="indicator-published">
                    {{ t('indicators.published', { time: (indicator.publishedAt | date: dateTimeFormat) }) }}
                  </span>
                } @else {
                  <tmz-badge tone="neutral" data-testid="indicator-unpublished">{{ t('indicators.list.notPublished') }}</tmz-badge>
                }
                <span class="min-w-16 whitespace-nowrap text-end tabular-nums text-body">
                  {{ indicator.rows === null ? '—' : t('indicators.list.rowCount', { count: (indicator.rows | number) }) }}
                </span>
                @if (indicator.published) {
                  <button
                    tmzButton
                    variant="ghost"
                    size="sm"
                    type="button"
                    [cdkMenuTriggerFor]="exportMenu"
                    [cdkMenuTriggerData]="{ name: indicator.name }"
                    [disabled]="exporting() === indicator.name"
                    [attr.aria-busy]="exporting() === indicator.name"
                    [attr.aria-label]="exporting() === indicator.name ? t('indicators.export.preparing') : t('indicators.export.label', { label: indicator.label })"
                    [attr.title]="t('indicators.export.help')"
                    [attr.data-testid]="'indicator-export-' + indicator.name"
                  >
                    <svg aria-hidden="true" class="size-4" [class.animate-pulse]="exporting() === indicator.name" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <path d="M12 4v11m0 0-4-4m4 4 4-4M5 20h14" />
                    </svg>
                  </button>
                } @else {
                  <span class="size-9" aria-hidden="true"></span>
                }
              </li>
            }
          </ul>
        </ng-template>

        @if (!anyPublished()) {
          <tmz-card [heading]="t('indicators.empty.title')" data-testid="indicators-empty">
            <p class="text-sm text-body">{{ t('indicators.empty.body') }}</p>
            @if (context.can('analyst')) {
              <p class="mt-2 text-sm text-body">{{ t('indicators.empty.editor') }}</p>
              <div class="mt-4 flex flex-wrap gap-2">
                <a tmzButton variant="secondary" [routerLink]="['..', 'scripts']" data-testid="indicators-empty-link">{{ t('indicators.empty.editorLink') }}</a>
                <button tmzButton type="button" [disabled]="!overview.indicators.length" (click)="addWidget()" data-testid="widget-add">{{ t('indicators.add') }}</button>
              </div>
            } @else {
              <p class="mt-2 text-sm text-body" data-testid="indicators-empty-viewer">{{ t('indicators.empty.viewer') }}</p>
            }
            @if (widgets().length) {
              <p class="mt-4 text-sm text-body" data-testid="indicators-empty-widgets">{{ t('indicators.empty.widgets', { count: widgets().length }) }}</p>
            }
            @if (overview.indicators.length) {
              <div class="mt-4 border-t border-default pt-2" data-testid="indicator-table-card">
                <h3 class="text-sm font-medium text-heading">{{ t('indicators.list.title', { count: overview.indicators.length }) }}</h3>
                <ng-container *ngTemplateOutlet="inventory" />
              </div>
            }
          </tmz-card>
        }

        <ng-template #exportMenu let-name="name">
          <div cdkMenu class="z-10 min-w-48 rounded-base border border-default-medium bg-neutral-primary-medium py-2 shadow-lg" data-testid="indicators-export-menu">
            <button cdkMenuItem type="button" class="block min-h-10 w-full px-4 py-2 text-left text-sm text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:bg-neutral-tertiary-medium focus:outline-none" (cdkMenuItemTriggered)="exportTable(name, 'csv')" data-testid="indicators-export-csv">
              {{ t('indicators.export.csv') }}
            </button>
            <button cdkMenuItem type="button" class="block min-h-10 w-full px-4 py-2 text-left text-sm text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:bg-neutral-tertiary-medium focus:outline-none" (cdkMenuItemTriggered)="exportTable(name, 'xlsx')" data-testid="indicators-export-xlsx">
              {{ t('indicators.export.xlsx') }}
            </button>
          </div>
        </ng-template>

        @if (widgets().length) {
          <!-- Editors see how many widgets there are (and the limit); everyone gets the switch to tables. -->
          <div class="flex flex-wrap items-center justify-between gap-3" data-testid="indicators-toolbar">
            @if (context.can('analyst')) {
              @if (limitReached()) {
                <p class="text-sm text-fg-warning" data-testid="widget-limit">{{ t('indicators.limit', { count: overview.limits.maxWidgets }) }}</p>
              } @else {
                <p class="text-sm text-body" data-testid="indicators-count">{{ t('indicators.count', { count: widgets().length, max: overview.limits.maxWidgets }) }}</p>
              }
            }
            <div class="ms-auto flex flex-wrap items-center gap-2">
              @if (hasTables()) {
                <button tmzButton variant="secondary" size="sm" type="button" [attr.aria-pressed]="allTables()" (click)="allTables.set(!allTables())" data-testid="tables-toggle">
                  {{ allTables() ? t('indicators.showCharts') : t('indicators.showTables') }}
                </button>
              }
              @if (context.can('analyst')) {
                <button tmzButton size="sm" type="button" [disabled]="limitReached() || !overview.indicators.length" (click)="addWidget()" data-testid="widget-add">
                  {{ t('indicators.add') }}
                </button>
              }
            </div>
          </div>

          <app-widget-grid
            [widgets]="widgets()"
            [projectId]="projectId"
            [editable]="context.can('analyst')"
            [indicators]="overview.indicators"
            [allTables]="allTables()"
            (layoutsChange)="layoutsChanged($event)"
            (edit)="editWidget($event)"
            (remove)="removeWidget($event)"
          />
        } @else if (anyPublished()) {
          <tmz-card [heading]="t('indicators.noWidgets.title')" data-testid="no-widgets">
            <p class="text-sm text-body">{{ context.can('analyst') ? t('indicators.noWidgets.editor') : t('indicators.noWidgets.viewer') }}</p>
            @if (context.can('analyst')) {
              <button tmzButton type="button" class="mt-4" (click)="addWidget()" data-testid="widget-add">{{ t('indicators.add') }}</button>
            }
          </tmz-card>
        }

        @if (anyPublished() && overview.indicators.length) {
          <tmz-disclosure
            [heading]="t('indicators.list.title', { count: overview.indicators.length })"
            [status]="t('indicators.list.summary', { count: publishedCount() })"
            data-testid="indicator-table-card"
          >
            <ng-container *ngTemplateOutlet="inventory" />
          </tmz-disclosure>
        }
      } @else if (!error()) {
        <tmz-card><div class="flex flex-col gap-3" aria-busy="true" data-testid="indicators-loading"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div></tmz-card>
      }
    </div>
  `,
})
export class ProjectIndicators {
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(Dialog);
  private readonly confirm = inject(ConfirmDialog);
  private readonly downloader = inject(Downloader);
  protected readonly context = inject(ProjectContext);

  protected readonly overview = signal<IndicatorsOverview | null>(null);
  protected readonly widgets = signal<Widget[]>([]);
  protected readonly error = signal<string | null>(null);
  /** The indicator whose table is being prepared; the others stay usable. */
  protected readonly exporting = signal<string | null>(null);
  protected readonly allTables = signal(false);

  protected readonly dateTimeFormat = dateTimeFormat;

  /** True once any indicator has a published table; until then the page explains how an indicator publishes. */
  protected readonly anyPublished = computed(() => !!this.overview()?.indicators.some((i: IndicatorEntry) => i.published));

  protected readonly publishedCount = computed(() => this.overview()?.indicators.filter((i) => i.published).length ?? 0);
  protected readonly limitReached = computed(() => this.widgets().length >= (this.overview()?.limits.maxWidgets ?? Infinity));

  /** Some widget has a chart or map whose table can be shown. */
  protected readonly hasTables = computed(() => this.anyPublished() && this.widgets().some((w) => w.chartType !== 'stat'));

  /** Layouts waiting for the pause after the last drag before they are saved, by widget. */
  private readonly pending = new Map<string, { layout: Layout; timer: ReturnType<typeof setTimeout> }>();

  constructor() {
    inject(DestroyRef).onDestroy(() => [...this.pending.keys()].forEach((id) => this.save(id)));
    this.load();
  }

  protected get projectId(): string {
    return this.context.project()!.id;
  }

  private load(): void {
    this.service.overview(this.projectId).subscribe({
      next: (overview) => {
        this.overview.set(overview);
        this.widgets.set(overview.widgets);
        this.error.set(null);
      },
      error: (failure) => this.error.set(this.errors.parse(failure).message),
    });
  }

  // Layout: applied at once, saved once the drag has been still for a moment.

  protected layoutsChanged(changes: Placed[]): void {
    this.widgets.update((list) => list.map((w) => ({ ...w, layout: changes.find((c) => c.id === w.id)?.layout ?? w.layout })));
    for (const change of changes) {
      const waiting = this.pending.get(change.id);
      if (waiting) {
        clearTimeout(waiting.timer);
      }
      this.pending.set(change.id, { layout: change.layout, timer: setTimeout(() => this.save(change.id), layoutSaveDelayMs) });
    }
  }

  private save(id: string): void {
    const waiting = this.pending.get(id);
    if (!waiting) {
      return;
    }
    clearTimeout(waiting.timer);
    this.pending.delete(id);
    this.service.update(this.projectId, id, { layout: waiting.layout }).subscribe({
      error: (failure) => {
        this.errors.report(failure);
        this.load(); // show what the server has
      },
    });
  }

  // Widgets

  protected addWidget(): void {
    this.openDialog(null);
  }

  protected editWidget(widget: Widget): void {
    this.openDialog(widget);
  }

  private openDialog(widget: Widget | null): void {
    const ref = this.dialog.open<Widget, WidgetDialogData>(WidgetDialog, {
      data: { projectId: this.projectId, widget, indicators: this.overview()?.indicators ?? [], timeZone: this.context.project()?.timeZone ?? 'UTC' },
      ariaLabelledBy: 'widget-dialog-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
      maxWidth: '100vw',
      maxHeight: '100dvh',
    });
    ref.closed.subscribe((saved) => {
      if (!saved) {
        return;
      }
      this.widgets.update((list) => (widget ? list.map((w) => (w.id === saved.id ? saved : w)) : [...list, saved]));
      this.toasts.success(this.transloco.translate(widget ? 'indicators.saved' : 'indicators.added', { title: saved.title }));
    });
  }

  protected async removeWidget(widget: Widget): Promise<void> {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const confirmed = await this.confirm.confirm({
      title: t('indicators.remove.title', { title: widget.title }),
      message: t('indicators.remove.message'),
      confirmLabel: t('indicators.remove.confirm'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (!confirmed) {
      return;
    }
    const waiting = this.pending.get(widget.id);
    if (waiting) {
      clearTimeout(waiting.timer);
      this.pending.delete(widget.id);
    }
    this.service.remove(this.projectId, widget.id).subscribe({
      next: () => {
        this.widgets.update((list) => list.filter((w) => w.id !== widget.id));
        this.toasts.success(t('indicators.remove.done', { title: widget.title }));
      },
      error: (failure) => this.errors.report(failure),
    });
  }

  protected exportTable(indicator: string, format: ExportFormat): void {
    if (this.exporting() === indicator) {
      return;
    }
    this.exporting.set(indicator);
    this.service.export(this.projectId, indicator, format).subscribe({
      next: (result) => {
        this.exporting.set(null);
        this.downloader.start(result.downloadUrl);
        const ready = this.transloco.translate('indicators.export.ready', { rows: result.rowCount });
        this.toasts.success(result.cutCells > 0 ? `${ready} ${this.transloco.translate('cleanData.export.readyCut', { cut: result.cutCells })}` : ready);
      },
      error: (failure) => {
        this.exporting.set(null);
        this.errors.report(failure);
      },
    });
  }
}
