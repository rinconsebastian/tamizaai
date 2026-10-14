import { Dialog } from '@angular/cdk/dialog';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import { ExportFormat } from '../../core/projects/data.service';
import { IndicatorEntry, IndicatorsOverview, IndicatorsService, Layout, Widget } from '../../core/projects/indicators.service';
import {
  Alert,
  Badge,
  Button,
  Card,
  ConfirmDialog,
  Skeleton,
  Table,
  TableContainer,
  ToastService,
  tableCell,
  tableHead,
  tableHeaderCell,
  tableRow,
} from '../../shared/ui';
import { Placed } from './indicators/grid-layout';
import { WidgetDialog, WidgetDialogData } from './indicators/widget-dialog';
import { WidgetGrid } from './indicators/widget-grid';
import { ProjectContext } from './project-context';

/** Wait this long after a drag or resize ends before saving the layout, so a run of keystrokes or drags is one request. */
export const layoutSaveDelayMs = 400;

/**
 * The indicators dashboard: the project's indicators with whether each has published its table, and a canvas of widgets, each a
 * chart or map over one indicator's table. Every member sees it and exports a table; analysts and admins add, edit, arrange and
 * remove widgets (design D5, D6). While no indicator has published, it says so and still lists the widgets already configured.
 */
@Component({
  selector: 'app-project-indicators',
  imports: [DatePipe, RouterLink, TranslocoDirective, CdkMenu, CdkMenuItem, CdkMenuTrigger, Alert, Badge, Button, Card, Skeleton, Table, TableContainer, WidgetGrid],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t" [attr.aria-busy]="!overview() && !error()">
      @if (error(); as message) {
        <tmz-alert tone="danger" data-testid="indicators-error">{{ message }}</tmz-alert>
      }

      @if (overview(); as overview) {
        @if (!anyPublished()) {
          <tmz-card [heading]="t('indicators.empty.title')" data-testid="indicators-empty">
            <p class="text-sm text-body">{{ t('indicators.empty.body') }}</p>
            @if (context.can('analyst')) {
              <p class="mt-2 text-sm text-body">{{ t('indicators.empty.editor') }}</p>
              <a tmzButton variant="secondary" class="mt-4" [routerLink]="['..', 'scripts']" data-testid="indicators-empty-link">{{ t('indicators.empty.editorLink') }}</a>
            } @else {
              <p class="mt-2 text-sm text-body" data-testid="indicators-empty-viewer">{{ t('indicators.empty.viewer') }}</p>
            }
            @if (widgets().length) {
              <p class="mt-4 text-sm text-body" data-testid="indicators-empty-widgets">{{ t('indicators.empty.widgets', { count: widgets().length }) }}</p>
            }
          </tmz-card>
        }

        @if (overview.indicators.length) {
          <tmz-card [heading]="t('indicators.list.title', { count: overview.indicators.length })" data-testid="indicator-table-card">
            <tmz-table-container class="max-h-80 overflow-y-auto">
              <table tmzTable data-testid="indicator-table">
                <thead [class]="tableHead + ' sticky top-0'">
                  <tr>
                    <th scope="col" [class]="tableHeaderCell">{{ t('indicators.list.indicator') }}</th>
                    <th scope="col" [class]="tableHeaderCell">{{ t('indicators.list.format') }}</th>
                    <th scope="col" [class]="tableHeaderCell">{{ t('indicators.list.status') }}</th>
                    <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('indicators.list.rows') }}</th>
                    <th scope="col" [class]="tableHeaderCell"><span class="sr-only">{{ t('indicators.export.button') }}</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (indicator of overview.indicators; track indicator.name) {
                    <tr [class]="tableRow" [attr.data-testid]="'indicator-row-' + indicator.name">
                      <td [class]="tableCell + ' max-w-xs'">
                        <span class="block break-words font-medium text-heading" data-testid="indicator-label">{{ indicator.label }}</span>
                        <code class="text-xs" data-testid="indicator-name">{{ indicator.name }}</code>
                        @if (indicator.description) {
                          <span class="block break-words text-xs">{{ indicator.description }}</span>
                        }
                      </td>
                      <td [class]="tableCell + ' whitespace-nowrap'" data-testid="indicator-format">
                        {{ t('scripts.formats.' + indicator.format) }}{{ indicator.unit ? ' · ' + indicator.unit : '' }}
                      </td>
                      <td [class]="tableCell">
                        @if (indicator.published) {
                          <tmz-badge tone="success">{{ t('indicators.list.published') }}</tmz-badge>
                          <span class="block text-xs" data-testid="indicator-published">
                            {{ t('indicators.published', { time: (indicator.publishedAt | date: 'medium') }) }}
                            @if (indicator.runId; as run) {
                              {{ t('indicators.publishedRun', { run: run.slice(0, 8) }) }}
                            }
                          </span>
                        } @else {
                          <tmz-badge tone="neutral" data-testid="indicator-unpublished">{{ t('indicators.list.notPublished') }}</tmz-badge>
                        }
                      </td>
                      <td [class]="tableCell + ' text-end tabular-nums'">{{ indicator.rows === null ? '—' : indicator.rows.toLocaleString('en') }}</td>
                      <td [class]="tableCell + ' text-end'">
                        <button
                          tmzButton
                          variant="secondary"
                          size="sm"
                          type="button"
                          [cdkMenuTriggerFor]="exportMenu"
                          [cdkMenuTriggerData]="{ name: indicator.name }"
                          [disabled]="exporting() || !indicator.published"
                          [attr.title]="t('indicators.export.help')"
                          [attr.data-testid]="'indicator-export-' + indicator.name"
                        >
                          {{ t('indicators.export.button') }}
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </tmz-table-container>
          </tmz-card>
        }

        <tmz-card>
          <div class="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
            <p class="text-sm text-body" data-testid="indicators-count">{{ t('indicators.count', { count: widgets().length, max: overview.limits.maxWidgets }) }}</p>
            <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
              @if (hasTables()) {
                <button tmzButton variant="secondary" type="button" [attr.aria-pressed]="allTables()" (click)="allTables.set(!allTables())" data-testid="tables-toggle">
                  {{ allTables() ? t('indicators.showCharts') : t('indicators.showTables') }}
                </button>
              }
              @if (context.can('analyst')) {
                <button
                  tmzButton
                  type="button"
                  [disabled]="widgets().length >= overview.limits.maxWidgets || !overview.indicators.length"
                  (click)="addWidget()"
                  data-testid="widget-add"
                >
                  {{ t('indicators.add') }}
                </button>
              }
            </div>
          </div>
          @if (context.can('analyst') && widgets().length >= overview.limits.maxWidgets) {
            <p class="mt-3 text-sm text-fg-warning" data-testid="widget-limit">{{ t('indicators.limit', { count: overview.limits.maxWidgets }) }}</p>
          }
        </tmz-card>

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
          </tmz-card>
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
  protected readonly exporting = signal(false);
  protected readonly allTables = signal(false);

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  /** True once any indicator has a published table; until then the page explains how an indicator publishes. */
  protected readonly anyPublished = computed(() => !!this.overview()?.indicators.some((i: IndicatorEntry) => i.published));

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
    if (this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.toasts.info(this.transloco.translate('indicators.export.preparing'));
    this.service.export(this.projectId, indicator, format).subscribe({
      next: (result) => {
        this.exporting.set(false);
        this.downloader.start(result.downloadUrl);
        const ready = this.transloco.translate('indicators.export.ready', { rows: result.rowCount });
        this.toasts.success(result.cutCells > 0 ? `${ready} ${this.transloco.translate('cleanData.export.readyCut', { cut: result.cutCells })}` : ready);
      },
      error: (failure) => {
        this.exporting.set(false);
        this.errors.report(failure);
      },
    });
  }
}
