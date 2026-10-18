import { Dialog } from '@angular/cdk/dialog';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, forkJoin, switchMap } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import { ExportFormat } from '../../core/projects/data.service';
import { IndicatorEntry, IndicatorsOverview, IndicatorsService, Layout, Widget, maxDashboards } from '../../core/projects/indicators.service';
import { DashboardEntry } from '../../core/projects/projects.service';
import { Alert, Badge, Button, Card, ConfirmDialog, Disclosure, DropdownMenu, MenuItem, Skeleton, ToastService, dateTimeFormat } from '../../shared/ui';
import { DashboardNameAction, DashboardNameData, DashboardNameDialog, DashboardNameResult } from './indicators/dashboard-name-dialog';
import { Placed } from './indicators/grid-layout';
import { WidgetDialog, WidgetDialogData, WidgetDialogResult } from './indicators/widget-dialog';
import { WidgetGrid } from './indicators/widget-grid';
import { ProjectContext } from './project-context';

/** Wait this long after a drag or resize ends before saving the layout, so a run of keystrokes or drags is one request. */
export const layoutSaveDelayMs = 400;

/**
 * One of the project's own dashboards: its name, a canvas of widgets, each a chart or map over one indicator's table, and, after
 * it, the project's indicators with whether each has published its table. Every member sees it and exports a table; analysts and
 * admins add, edit, arrange and remove widgets (design D5, D6 of add-indicators-dashboard). While no indicator has published, it
 * says so, shows the indicators as part of that message and still lists the widgets already configured. The router reuses the
 * page from one dashboard to the next, so it shows one dashboard at a time; from its header, analysts and admins add, rename,
 * duplicate, move and remove the project's dashboards (design D10, D12 of add-multiple-dashboards).
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
    DropdownMenu,
    Skeleton,
    WidgetGrid,
  ],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t">
      <!-- Outside the loading and overview branches, so the heading and the live region exist from the first render. -->
      <div class="flex flex-wrap items-center gap-2" data-testid="dashboard-header">
        <h2 #heading tabindex="-1" class="min-w-0 flex-1 break-words text-lg font-semibold text-heading [overflow-wrap:anywhere] focus:outline-none" data-testid="dashboard-heading">{{ name() }}</h2>
        @if (context.can('analyst')) {
          <!-- Under the name on a phone. At the limit "New dashboard" stays focusable, so the hint saying why it is unavailable can be read. -->
          <div class="ms-auto flex w-full items-center justify-end gap-2 sm:w-auto" data-testid="dashboard-actions">
            <button
              tmzButton
              variant="secondary"
              size="sm"
              type="button"
              class="aria-disabled:cursor-not-allowed aria-disabled:opacity-50"
              [attr.aria-disabled]="atLimit() ? 'true' : null"
              [attr.aria-describedby]="atLimit() ? 'dashboard-limit-hint' : null"
              (click)="addDashboard()"
              data-testid="dashboard-add"
            >
              {{ t('dashboards.add') }}
            </button>
            <tmz-dropdown-menu [label]="t('dashboards.actions')" [items]="actions()" triggerTestId="dashboard-menu" (selected)="act($event)" />
          </div>
          @if (atLimit()) {
            <p id="dashboard-limit-hint" class="w-full text-sm text-body" data-testid="dashboard-limit-hint">{{ t('dashboards.limitHint', { max: dashboardLimit() }) }}</p>
          }
        }
        <!-- Rendered empty: a live region added with its text already in it would not be read. -->
        <p class="sr-only" role="status" data-testid="dashboard-status">{{ announcement() }}</p>
      </div>

      @if (notice(); as notice) {
        <!-- Not itself a live region: the header's announces the same text once. -->
        <tmz-alert tone="warning" data-testid="dashboard-notice">
          <div class="flex items-start justify-between gap-3">
            <p class="min-w-0 break-words" data-testid="dashboard-notice-text">{{ notice.text }}</p>
            <button tmzButton variant="ghost" size="sm" type="button" class="shrink-0" (click)="dismissNotice()" data-testid="dashboard-notice-dismiss">{{ t('dashboards.notice.dismiss') }}</button>
          </div>
        </tmz-alert>
      }

      @if (loading()) {
        <tmz-card><div class="flex flex-col gap-3" aria-busy="true" data-testid="indicators-loading"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div></tmz-card>
      } @else if (error(); as message) {
        <tmz-alert tone="danger" data-testid="indicators-error">{{ message }}</tmz-alert>
      } @else if (overview(); as overview) {
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
                <a tmzButton variant="secondary" [routerLink]="['/projects', projectId, 'scripts']" data-testid="indicators-empty-link">{{ t('indicators.empty.editorLink') }}</a>
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
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly context = inject(ProjectContext);

  /** Bound from the `:dashboardId` route parameter; it changes when another dashboard opens. */
  readonly dashboardId = input.required<string>();

  /**
   * The project the page belongs to. The page renders only once the project is read, and leaves with it, so the id is read once:
   * the layout saves sent as the page goes still have it.
   */
  protected readonly projectId = this.context.project()!.id;

  /** The open dashboard, from the project's list of dashboards, so a rename shows at once; null once it has left the list. */
  private readonly current = computed(() => this.context.dashboards().find((d) => d.id === this.dashboardId()) ?? null);
  protected readonly name = computed(() => this.current()?.name ?? '');
  /** The most dashboards the project can hold; the overview says, and it is the same for every project. */
  protected readonly dashboardLimit = computed(() => this.overview()?.limits.maxDashboards ?? maxDashboards);
  protected readonly atLimit = computed(() => this.context.dashboards().length >= this.dashboardLimit());
  /** A new order is being saved: moving again waits for it (design D12). */
  protected readonly moving = signal(false);

  /** The dashboard's actions, each unavailable one saying why, as its description (design D12). */
  protected readonly actions = computed<MenuItem[]>(() => {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const list = this.context.dashboards();
    const index = list.findIndex((d) => d.id === this.dashboardId());
    const moving = this.moving();
    const first = index <= 0;
    const last = index < 0 || index === list.length - 1;
    const only = list.length <= 1;
    const moveHint = (end: boolean, which: string) => (moving ? t('dashboards.unavailable.moving') : end ? t(`dashboards.unavailable.${which}`) : undefined);
    return [
      { id: 'rename', label: t('dashboards.menu.rename'), testId: 'dashboard-rename' },
      {
        id: 'duplicate',
        label: t('dashboards.menu.duplicate'),
        disabled: this.atLimit(),
        description: this.atLimit() ? t('dashboards.unavailable.limit', { max: this.dashboardLimit() }) : undefined,
        testId: 'dashboard-duplicate',
      },
      { id: 'moveLeft', label: t('dashboards.menu.moveLeft'), disabled: moving || first, description: moveHint(first, 'first'), testId: 'dashboard-move-left' },
      { id: 'moveRight', label: t('dashboards.menu.moveRight'), disabled: moving || last, description: moveHint(last, 'last'), testId: 'dashboard-move-right' },
      {
        id: 'remove',
        label: t('dashboards.menu.remove'),
        danger: true,
        disabled: only,
        description: only ? t('dashboards.unavailable.lastOne') : undefined,
        testId: 'dashboard-remove',
      },
    ];
  });
  /**
   * The notice about a dashboard that does not exist, or about a refused change to the dashboards, shown only while the dashboard
   * it names is open (design D10, D12).
   */
  protected readonly notice = computed(() => {
    const notice = this.context.notice();
    return notice?.dashboardId === this.dashboardId() ? notice : null;
  });
  /** What the header's live region says. */
  protected readonly announcement = signal('');
  private announced = false;

  protected readonly overview = signal<IndicatorsOverview | null>(null);
  protected readonly widgets = signal<Widget[]>([]);
  /** The open dashboard is being read: neither its widgets nor whether it has any are known. */
  protected readonly loading = signal(true);
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

  /** Layouts waiting for the pause after the last drag before they are saved, by widget, with the dashboard they were made on. */
  private readonly pending = new Map<string, { dashboardId: string; layout: Layout; timer: ReturnType<typeof setTimeout> }>();
  /** Layout saves sent and not answered yet, by widget. */
  private readonly inFlight = new Map<string, Promise<void>>();
  /**
   * The removed dashboard the page is leaving for the first one, until another dashboard opens: a second refusal or reload that
   * finds it gone brings no news, so the page leaves it once and says so once.
   */
  private leaving: string | null = null;
  /** The dashboards to read, one after another: reading the next drops the answer for the one before, and `null` drops it alone. */
  private readonly loads = new Subject<string | null>();

  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  constructor() {
    this.destroyRef.onDestroy(() => {
      // Answered once the page is gone: the answers are handled as for a dashboard left (`saveFailed`).
      void this.flushSaves();
      // Leaving the dashboards opens another page: the notice has done its job.
      this.context.notice.set(null);
    });

    // The project's indicators and the dashboard's widgets are read together, so the page draws once with both.
    this.loads
      .pipe(
        switchMap((id) =>
          id === null
            ? EMPTY
            : forkJoin({ overview: this.service.overview(this.projectId), dashboard: this.service.dashboard(this.projectId, id) }).pipe(
                catchError((failure) => {
                  const parsed = this.errors.parse(failure);
                  const dashboard = this.current();
                  if (parsed.code === 'dashboards.notFound' && dashboard) {
                    // Removed by another member since the project was read: the page catches up as for any change refused so,
                    // still loading until the first dashboard opens, so the notice alone says what happened.
                    this.gone(dashboard);
                  } else {
                    this.error.set(parsed.message);
                    this.loading.set(false);
                  }
                  return EMPTY;
                }),
              ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ overview, dashboard }) => {
        this.overview.set(overview);
        this.widgets.set(dashboard.widgets);
        this.error.set(null);
        this.loading.set(false);
      });

    // Only the id is tracked: a new list of dashboards (setDashboards, reload, refresh) neither resets nor reads the open one again.
    effect(() => {
      const id = this.dashboardId();
      untracked(() => this.open(id));
    });

    // Written after the render that shows the notice, into a region that was already there, so it is read once, focus staying put.
    effect(() => {
      const notice = this.notice();
      if (notice) {
        afterNextRender(() => this.announce(notice.text), { injector: this.injector });
      }
    });
  }

  /** Shows another dashboard, or the first one in place of an id the project does not have, which is never requested (design D10, D12). */
  private open(id: string): void {
    // The previous dashboard's layouts go out now, each to its own dashboard; the new one does not wait for them.
    void this.flushSaves();
    this.widgets.set([]);
    this.allTables.set(false);
    this.exporting.set(null);
    this.error.set(null);
    this.loading.set(true);
    this.announcement.set('');
    this.leaving = null;
    if (this.context.notice()?.dashboardId !== id) {
      this.context.notice.set(null);
    }

    const dashboards = this.context.dashboards();
    if (dashboards.some((d) => d.id === id)) {
      this.loads.next(id);
      return;
    }
    // The dashboard left may still be on its way: it must not draw while the address is replaced.
    this.loads.next(null);
    const first = dashboards[0];
    if (!first) {
      this.loading.set(false);
      return;
    }
    this.context.notice.set({ dashboardId: first.id, text: this.transloco.translate('dashboards.notice.missing', { name: first.name }) });
    void this.router.navigate(['/projects', this.projectId, 'dashboards', first.id], { replaceUrl: true });
  }

  protected dismissNotice(): void {
    this.context.notice.set(null);
    this.heading().nativeElement.focus();
  }

  private announce(text: string): void {
    // The same words twice in a row would not be read again; a zero-width mark makes them a change.
    this.announced = !this.announced;
    this.announcement.set(this.announced ? text : `${text}​`);
  }

  // Layout: applied at once, saved once the drag has been still for a moment.

  protected layoutsChanged(changes: Placed[]): void {
    const dashboardId = this.dashboardId();
    this.widgets.update((list) => list.map((w) => ({ ...w, layout: changes.find((c) => c.id === w.id)?.layout ?? w.layout })));
    for (const change of changes) {
      const waiting = this.pending.get(change.id);
      if (waiting) {
        clearTimeout(waiting.timer);
      }
      this.pending.set(change.id, { dashboardId, layout: change.layout, timer: setTimeout(() => void this.save(change.id), layoutSaveDelayMs) });
    }
  }

  /**
   * Sends the widget's waiting layout to the dashboard it was made on. The promise settles once every save of the widget sent so
   * far is answered, whatever the answer, so a step that must follow them can wait (design D12).
   */
  private save(id: string): Promise<void> {
    const waiting = this.pending.get(id);
    if (!waiting) {
      return this.inFlight.get(id) ?? Promise.resolve();
    }
    clearTimeout(waiting.timer);
    this.pending.delete(id);
    const { dashboardId, layout } = waiting;
    const answered = new Promise<void>((resolve) =>
      this.service.update(this.projectId, dashboardId, id, { layout }).subscribe({
        next: () => resolve(),
        error: (failure) => {
          this.saveFailed(dashboardId, failure);
          resolve();
        },
      }),
    );
    // An earlier save of the same widget may still be on its way: waiting for this one waits for both.
    const done: Promise<void> = Promise.all([this.inFlight.get(id), answered]).then(() => {
      if (this.inFlight.get(id) === done) {
        this.inFlight.delete(id);
      }
    });
    this.inFlight.set(id, done);
    return done;
  }

  /** Sends every waiting layout now; settles once every layout save sent so far is answered. */
  private flushSaves(): Promise<void> {
    [...this.pending.keys()].forEach((id) => void this.save(id));
    return Promise.all([...this.inFlight.values()]).then(() => undefined);
  }

  /**
   * Shows what the server has when the failed save was made on the open dashboard, or the first dashboard when another member
   * removed it. One made on a dashboard left since is reported and nothing else, so the dashboard now open keeps its widgets; if
   * that dashboard was removed meanwhile, the tab row catches up, and so does the page if the one open went with it. A page that
   * is gone has no dashboard open: the saves it sent on its way out never take the member anywhere (design D12).
   */
  private saveFailed(dashboardId: string, failure: unknown): void {
    const removed = this.errors.parse(failure).code === 'dashboards.notFound';
    const open = this.destroyRef.destroyed ? null : this.dashboardId();
    if (dashboardId === open && removed) {
      // The first of several saves refused so takes the member to the first dashboard; the others bring no news.
      const dashboard = this.current();
      if (dashboard) {
        this.gone(dashboard);
      }
    } else if (dashboardId === open) {
      this.errors.report(failure);
      this.loads.next(dashboardId);
    } else if (removed && open !== null) {
      this.reloadProject(false);
    } else if (removed) {
      this.context.refresh();
    } else {
      this.errors.report(failure);
    }
  }

  // Widgets

  protected addWidget(): void {
    this.openDialog(null);
  }

  /**
   * The form can move the widget to another dashboard, so its layout still waiting or on its way is saved first: sent later, it
   * would land on the dashboard the widget left (design D9).
   */
  protected async editWidget(widget: Widget): Promise<void> {
    if (!this.pending.has(widget.id) && !this.inFlight.has(widget.id)) {
      this.openDialog(widget);
      return;
    }
    const dashboardId = this.dashboardId();
    await this.save(widget.id);
    const latest = this.widgets().find((w) => w.id === widget.id);
    // Another dashboard or page opened meanwhile, or the save found the widget's dashboard removed: there is nothing left to edit
    // here. A form opened while waiting (Edit chosen again, or another action) stays the only one.
    if (latest && this.showing(dashboardId) && this.current() && !this.dialog.openDialogs.length) {
      this.openDialog(latest);
    }
  }

  private openDialog(widget: Widget | null): void {
    const dashboard = this.current();
    const dashboardId = this.dashboardId();
    const ref = this.dialog.open<WidgetDialogResult, WidgetDialogData>(WidgetDialog, {
      data: {
        projectId: this.projectId,
        dashboardId,
        dashboards: this.context.dashboards,
        widget,
        indicators: this.overview()?.indicators ?? [],
        timeZone: this.context.project()?.timeZone ?? 'UTC',
        refused: () => this.reloadProject(),
      },
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
      if ('gone' in saved) {
        if (dashboard) {
          this.gone(dashboard);
        }
        return;
      }
      if (widget && saved.dashboardId !== dashboardId) {
        this.movedOut(saved);
        return;
      }
      this.widgets.update((list) => (widget ? list.map((w) => (w.id === saved.id ? saved : w)) : [...list, saved]));
      this.toasts.success(this.transloco.translate(widget ? 'indicators.saved' : 'indicators.added', { title: saved.title }));
    });
  }

  /**
   * A widget the form moved to another dashboard leaves this one. Its menu button goes with it, so focus moves to the heading
   * (design D9).
   */
  private movedOut(saved: Widget): void {
    this.widgets.update((list) => list.filter((w) => w.id !== saved.id));
    const target = this.context.dashboards().find((d) => d.id === saved.dashboardId);
    this.toasts.success(
      target
        ? this.transloco.translate('indicators.moved', { title: saved.title, name: target.name })
        : this.transloco.translate('indicators.saved', { title: saved.title }),
    );
    this.focusHeading();
  }

  protected async removeWidget(widget: Widget): Promise<void> {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const dashboard = this.current();
    const confirmed = await this.confirm.confirm({
      title: t('indicators.remove.title', { title: widget.title }),
      message: t('indicators.remove.message'),
      confirmLabel: t('indicators.remove.confirm'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (!confirmed || !dashboard) {
      return;
    }
    const waiting = this.pending.get(widget.id);
    if (waiting) {
      clearTimeout(waiting.timer);
      this.pending.delete(widget.id);
    }
    this.service.remove(this.projectId, dashboard.id, widget.id).subscribe({
      next: () => {
        this.widgets.update((list) => list.filter((w) => w.id !== widget.id));
        this.toasts.success(t('indicators.remove.done', { title: widget.title }));
      },
      error: (failure) => this.refused(failure, dashboard),
    });
  }

  // The project's dashboards: add, rename, duplicate, move and remove (design D12).

  protected act(action: string): void {
    switch (action) {
      case 'rename':
        this.rename();
        break;
      case 'duplicate':
        void this.duplicate();
        break;
      case 'moveLeft':
        this.move(-1);
        break;
      case 'moveRight':
        this.move(1);
        break;
      case 'remove':
        this.removeDashboard();
        break;
    }
  }

  protected addDashboard(): void {
    // At the limit the button only says why it is unavailable.
    if (this.atLimit()) {
      return;
    }
    this.nameDialog('add', null).subscribe((added) => {
      if (added && !('gone' in added)) {
        this.context.setDashboards([...this.context.dashboards(), added]);
        this.openDashboard(added.id);
      }
    });
  }

  private rename(): void {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    this.nameDialog('rename', dashboard).subscribe((renamed) => {
      if (!renamed) {
        return;
      }
      if ('gone' in renamed) {
        this.gone(dashboard);
        return;
      }
      // The tab, the heading and the select follow the list; focus is back on the actions button, where the rename started.
      this.context.setDashboards(this.context.dashboards().map((d) => (d.id === renamed.id ? renamed : d)));
      this.toasts.success(this.transloco.translate('dashboards.renamed', { name: renamed.name }));
    });
  }

  private async duplicate(): Promise<void> {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    // The copy is made from what the server has: a layout still waiting for its pause, or on its way, is saved first.
    await this.flushSaves();
    if (!this.showing(dashboard.id) || !this.current()) {
      return; // removed or left meanwhile
    }
    this.nameDialog('duplicate', dashboard).subscribe((copy) => {
      if (!copy) {
        return;
      }
      if ('gone' in copy) {
        this.gone(dashboard);
        return;
      }
      // Right after the original, as the API placed it.
      const list = this.context.dashboards();
      const index = list.findIndex((d) => d.id === dashboard.id);
      this.context.setDashboards([...list.slice(0, index + 1), copy, ...list.slice(index + 1)]);
      this.openDashboard(copy.id);
    });
  }

  /** Swaps the open dashboard with its neighbour, from the order the page shows, which the API checks is still the project's. */
  private move(step: -1 | 1): void {
    const dashboard = this.current();
    const shown = this.context.dashboards().map((d) => d.id);
    const from = dashboard ? shown.indexOf(dashboard.id) : -1;
    const to = from + step;
    if (!dashboard || this.moving() || from < 0 || to < 0 || to >= shown.length) {
      return;
    }
    const order = [...shown];
    [order[from], order[to]] = [order[to], order[from]];
    this.moving.set(true);
    // Focus stays on the actions button, where the menu put it back, so the move can be repeated.
    this.service.reorderDashboards(this.projectId, shown, order).subscribe({
      next: (dashboards) => {
        this.moving.set(false);
        this.context.setDashboards(dashboards);
        const index = dashboards.findIndex((d) => d.id === dashboard.id);
        const name = dashboards[index]?.name ?? dashboard.name;
        this.announce(this.transloco.translate('dashboards.moved', { name, position: index + 1, count: dashboards.length }));
      },
      error: (failure) => {
        this.moving.set(false);
        this.refused(failure, dashboard);
      },
    });
  }

  /**
   * Asks to confirm with the number of widgets the server has, not the number the page drew when it opened. Once the member has
   * opened another dashboard, or left the dashboards, nothing is asked: the removal was asked for the dashboard left. If that one
   * turns out removed meanwhile, it only leaves the tab row (`gone`).
   */
  private removeDashboard(): void {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    this.service.dashboard(this.projectId, dashboard.id).subscribe({
      next: (fresh) => {
        if (this.showing(dashboard.id)) {
          void this.confirmRemoval(dashboard, fresh.widgets.length);
        }
      },
      error: (failure) => this.refused(failure, dashboard),
    });
  }

  private async confirmRemoval(dashboard: DashboardEntry, count: number): Promise<void> {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const params = { name: dashboard.name, count };
    const amount = count === 0 ? 'None' : count === 1 ? 'One' : '';
    const confirmed = await this.confirm.confirm({
      title: t('dashboards.remove.title'),
      message: t(`dashboards.remove.message${amount}`, params),
      confirmLabel: t('dashboards.remove.confirm'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (!confirmed || !this.showing(dashboard.id)) {
      return;
    }
    this.service.removeDashboard(this.projectId, dashboard.id).subscribe({
      next: () => void this.removed(dashboard, t(`dashboards.remove.done${amount}`, params)),
      error: (failure) => this.refused(failure, dashboard),
    });
  }

  /**
   * Opens the dashboard now in the removed one's place, or the new last one, then says what was removed. A member who opened
   * another dashboard, or left the dashboards, while the removal was on its way stays where they went.
   */
  private async removed(dashboard: DashboardEntry, message: string): Promise<void> {
    this.dropSaves(dashboard.id);
    const list = this.context.dashboards();
    const rest = list.filter((d) => d.id !== dashboard.id);
    const next = rest[Math.min(Math.max(list.findIndex((d) => d.id === dashboard.id), 0), rest.length - 1)];
    const leave = this.showing(dashboard.id);
    // Opened while the list still has the removed dashboard, then the list drops it: the page loads the next one as any other.
    if (leave && next) {
      await this.router.navigate(['/projects', this.projectId, 'dashboards', next.id]);
    }
    this.context.setDashboards(rest);
    this.toasts.success(message);
    if (leave) {
      this.focusHeading();
    }
  }

  private nameDialog(action: DashboardNameAction, dashboard: DashboardEntry | null): Observable<DashboardNameResult | undefined> {
    return this.dialog.open<DashboardNameResult, DashboardNameData>(DashboardNameDialog, {
      data: { projectId: this.projectId, action, dashboard, dashboards: this.context.dashboards(), refused: () => this.reloadProject() },
      ariaLabelledBy: 'dashboard-name-title',
      autoFocus: 'first-tabbable',
      // Opened from the actions menu, this is its button: the menu gives the choice once focus is back on it.
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    }).closed;
  }

  /** Opens a dashboard the member just added or copied, and puts focus on its heading once it shows its name. */
  private openDashboard(dashboardId: string, replaceUrl = false): void {
    void this.router.navigate(['/projects', this.projectId, 'dashboards', dashboardId], { replaceUrl }).then(() => this.focusHeading());
  }

  /**
   * Puts focus on the heading after the next render. Never from a page that is gone, and never under a form still open (one whose
   * save was refused as the page caught up): the form keeps the focus, which returns to where it was opened from when it closes.
   */
  private focusHeading(): void {
    if (this.destroyRef.destroyed) {
      return;
    }
    afterNextRender(
      () => {
        if (!this.dialog.openDialogs.length) {
          this.heading().nativeElement.focus();
        }
      },
      { injector: this.injector },
    );
  }

  /** The page is still there and shows this dashboard. An input keeps its last value once the page is gone, so it alone does not say. */
  private showing(dashboardId: string): boolean {
    return !this.destroyRef.destroyed && this.dashboardId() === dashboardId;
  }

  /**
   * A refused change to a dashboard or one of its widgets (design D12). Each `dashboards.*` refusal shows once, never as a toast, and
   * the page catches up with the project's dashboards; any other failure is reported.
   */
  private refused(failure: unknown, dashboard: DashboardEntry): void {
    const parsed = this.errors.parse(failure);
    if (parsed.code === 'dashboards.notFound') {
      this.gone(dashboard);
    } else if (parsed.code?.startsWith('dashboards.')) {
      // `orderChanged`, `lastOne`: said in the notice, which the header's region reads once; the reload sets the actions right.
      if (!this.destroyRef.destroyed) {
        this.context.notice.set({ dashboardId: this.dashboardId(), text: parsed.message });
      }
      this.reloadProject();
    } else {
      this.errors.report(failure);
    }
  }

  /**
   * Another member removed the dashboard (`dashboards.notFound`): it leaves the tab row at once, the project's dashboards are read
   * again, and the first one opens with a notice naming both and focus on its heading. Without the fresh list, the page's own list
   * stands in for it. A dashboard the page has left meanwhile only leaves the tab row: the member stays where they went, unless
   * the fresh list shows the dashboard open was removed too.
   */
  private gone(dashboard: DashboardEntry): void {
    if (!this.context.dashboards().some((d) => d.id === dashboard.id)) {
      return; // already caught up
    }
    const corrected = this.context.dashboards().filter((d) => d.id !== dashboard.id);
    this.dropSaves(dashboard.id);
    this.context.setDashboards(corrected);
    if (!this.showing(dashboard.id)) {
      this.reloadProject(false);
      return;
    }
    this.context.reload().subscribe({
      next: (fresh) => this.openFirst(fresh.dashboards, dashboard),
      error: (failure) => {
        this.openFirst(corrected, dashboard);
        this.errors.report(failure);
      },
    });
  }

  /**
   * Opens the first dashboard in place of the removed one, with a notice naming both; once only, however many refusals or reloads
   * find it removed before another dashboard opens.
   */
  private openFirst(dashboards: DashboardEntry[], removed: DashboardEntry): void {
    if (this.leaving === removed.id) {
      return;
    }
    this.leaving = removed.id;
    const first = dashboards[0];
    if (!first) {
      // The page knew no other dashboard and could not read the project again: it says the dashboard is gone, in its place.
      this.loads.next(null);
      this.loading.set(false);
      this.error.set(this.transloco.translate('errors.codes.dashboards.notFound'));
      return;
    }
    this.context.notice.set({ dashboardId: first.id, text: this.transloco.translate('dashboards.notice.removed', { removed: removed.name, name: first.name }) });
    this.openDashboard(first.id, true);
  }

  /**
   * Reads the project's dashboards again after a refusal that says they changed, from the page or from one of its forms. The
   * dashboard open may be among the changes, removed by another member: the page then catches up as for `dashboards.notFound`, so
   * it never stays on a dashboard that no longer exists. A failed read is reported unless `report` is false (the refusal brought
   * no news the member must hear).
   */
  private reloadProject(report = true): void {
    const open = this.current();
    this.context.reload().subscribe({
      next: (fresh) => {
        if (open && this.showing(open.id) && !fresh.dashboards.some((d) => d.id === open.id)) {
          this.dropSaves(open.id);
          this.openFirst(fresh.dashboards, open);
        }
      },
      error: (failure) => {
        if (report) {
          this.errors.report(failure);
        }
      },
    });
  }

  /** Forgets the layouts waiting to be saved on a dashboard that is gone: there is nothing left to save them to. */
  private dropSaves(dashboardId: string): void {
    for (const [id, waiting] of this.pending) {
      if (waiting.dashboardId === dashboardId) {
        clearTimeout(waiting.timer);
        this.pending.delete(id);
      }
    }
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
