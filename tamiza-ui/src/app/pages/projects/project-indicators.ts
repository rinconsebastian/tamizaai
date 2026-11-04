import { Dialog } from '@angular/cdk/dialog';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, ElementRef, Injector, LOCALE_ID, afterNextRender, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, Subscription, catchError, firstValueFrom, forkJoin, switchMap } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import { ExportFormat } from '../../core/projects/data.service';
import {
  DashboardColors,
  DashboardFilter,
  DashboardRestore,
  DashboardVersionDetails,
  IndicatorEntry,
  IndicatorsOverview,
  IndicatorsService,
  Layout,
  Widget,
  maxDashboards,
} from '../../core/projects/indicators.service';
import { DashboardEntry } from '../../core/projects/projects.service';
import { SystemConfigService } from '../../core/system-config';
import { Alert, ApiLang, Badge, Button, Card, ChartTooltips, ConfirmDialog, Disclosure, DropdownMenu, MenuItem, Skeleton, ToastService, DateTimePipe, NumberPipe, dateText, numberText } from '../../shared/ui';
import { colorsOf, defaultColors } from './indicators/chart-colors';
import { DashboardColorsData, DashboardColorsDialog, DashboardColorsResult } from './indicators/dashboard-colors-dialog';
import { DashboardFilterBar, FilterChange } from './indicators/dashboard-filter-bar';
import { FilterValues, activeFilters, defaultsOf, describeValue, readAddress, writeAddress } from './indicators/dashboard-filters';
import { DashboardFiltersData, DashboardFiltersDialog, DashboardFiltersResult } from './indicators/dashboard-filters-dialog';
import { DashboardNameAction, DashboardNameData, DashboardNameDialog, DashboardNameResult } from './indicators/dashboard-name-dialog';
import { DashboardPrint, isoDay, printTitle } from './indicators/dashboard-print';
import { DashboardReportData, DashboardReportDialog, DashboardReportResult } from './indicators/dashboard-report-dialog';
import { DashboardSourceData, DashboardSourceDialog, DashboardSourceResult } from './indicators/dashboard-source-dialog';
import { DashboardVersionsData, DashboardVersionsDialog, DashboardVersionsResult, VersionToRestore } from './indicators/dashboard-versions-dialog';
import { captionShown } from './indicators/format';
import { Placed } from './indicators/grid-layout';
import { WidgetDialog, WidgetDialogData, WidgetDialogResult } from './indicators/widget-dialog';
import { WidgetFileExtension, widgetFileName } from './indicators/widget-export/file-name';
import { loadExporter } from './indicators/widget-export/load';
import { WidgetExportChoice, WidgetGrid } from './indicators/widget-grid';
import { savedConfig } from './indicators/widget-view';
import { ProjectContext } from './project-context';
import { countKey, isSingular } from '../../core/i18n/plural';

/** Wait this long after a drag or resize ends before saving the layout, so a run of keystrokes or drags is one request. */
export const layoutSaveDelayMs = 400;

/**
 * One of the project's own dashboards: its name, a canvas of widgets, each a chart or map over one indicator's table, and, after
 * it, the project's indicators with whether each has published its table. Every member sees it and exports a table; analysts and
 * admins add, edit, arrange and remove widgets (design D5, D6 of add-indicators-dashboard). While no indicator has published, it
 * says so, shows the indicators as part of that message and still lists the widgets already configured. The router reuses the
 * page from one dashboard to the next, so it shows one dashboard at a time; from its header, analysts and admins add, rename,
 * duplicate, move and remove the project's dashboards (design D10, D12 of add-multiple-dashboards), and open its version history,
 * from which they view a version read-only in place of the dashboard and restore it (design D11 of add-dashboard-versions). Every
 * member prints it from its header, laid out as on a wide screen (add-dashboard-printing).
 */
@Component({
  selector: 'app-project-indicators',
  providers: [DashboardPrint],
  imports: [
    DateTimePipe,
    NumberPipe,
    NgTemplateOutlet,
    RouterLink,
    TranslocoDirective,
    CdkMenu,
    CdkMenuItem,
    CdkMenuTrigger,
    Alert,
    ApiLang,
    Badge,
    Button,
    Card,
    Disclosure,
    DropdownMenu,
    Skeleton,
    DashboardFilterBar,
    WidgetGrid,
  ],
  template: `
    <!-- The paper canvas (dashboard printing, design D2): as wide as a landscape Letter sheet while the print is prepared and on paper.
         Nothing caps it on paper: on a narrower sheet the browser scales the whole page down to the sheet's width. -->
    <div class="flex flex-col gap-6 paper:w-[980px]" *transloco="let t" data-testid="dashboard-canvas">
      <!-- Outside the loading and overview branches, so the heading and the live region exist from the first render. -->
      <div class="flex flex-wrap items-center gap-2" data-testid="dashboard-header">
        <h2 #heading tabindex="-1" class="min-w-0 flex-1 break-words text-lg font-semibold text-heading [overflow-wrap:anywhere] focus:outline-none" data-testid="dashboard-heading">{{ shownName() }}</h2>
        <!-- None of them while a version is shown, Print included: the page offers no action that changes or prints it (dashboard-versions "Viewing a version"). -->
        @if (canPrint() || editing()) {
          <!-- Under the name on a phone. At the limit "New dashboard" stays focusable, so the hint saying why it is unavailable can be read. -->
          <div class="ms-auto flex w-full items-center justify-end gap-2 sm:w-auto print:hidden" data-testid="dashboard-actions">
            @if (canPrint()) {
              <!-- Every member's: it stays focusable while the print is prepared, so its busy state can be read (design D11). -->
              <button
                #printButton
                tmzButton
                variant="secondary"
                size="sm"
                type="button"
                class="aria-disabled:cursor-not-allowed"
                [attr.aria-disabled]="preparing() ? 'true' : null"
                [attr.aria-busy]="preparing() ? 'true' : null"
                (click)="print()"
                data-testid="dashboard-print"
              >
                <svg aria-hidden="true" class="size-4" [class.animate-pulse]="preparing()" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M7 8V3h10v5M7 17H5a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M7 14h10v7H7z" />
                </svg>
                {{ t('dashboards.print.action') }}
              </button>
            }
            @if (editing()) {
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
            }
          </div>
          @if (editing() && atLimit()) {
            <p id="dashboard-limit-hint" class="w-full text-sm text-body print:hidden" data-testid="dashboard-limit-hint">{{ t('dashboards.limitHint', { max: dashboardLimit() }) }}</p>
          }
        }
        <!-- The print header (design D7): when the sheet was printed, and when its data was published. -->
        @if (printedAt(); as printedAt) {
          <div class="hidden w-full text-sm text-body paper:block" data-testid="print-header">
            @if (viewing(); as version) {
              <!-- The browser's own menu still prints a version shown, whose notice paper leaves out: the sheet says which version it is, in the project's time zone as the lines below. -->
              <p data-testid="print-version">{{ t('dashboards.print.version', { number: version.number, author: version.author.name, time: when(version.savedAt) }) }}</p>
            }
            <p data-testid="print-time">{{ t('dashboards.print.printedAt', { time: when(printedAt), zone: timeZone() }) }}</p>
            @if (publishedAt(); as publishedAt) {
              <p data-testid="print-published">{{ t('dashboards.print.publishedAt', { time: when(publishedAt) }) }}</p>
            } @else {
              <p data-testid="print-published">{{ t('dashboards.print.notPublished') }}</p>
            }
            @if (source(); as source) {
              <p data-testid="print-source">{{ t('dashboards.source.line', { source }) }}</p>
            }
          </div>
        }
        <!-- Rendered empty: a live region added with its text already in it would not be read. -->
        <p class="sr-only" role="status" data-testid="dashboard-status">{{ announcement() }}</p>
      </div>

      @if (viewing(); as version) {
        <!-- Focus moves here when the view starts, so screen readers read it then; it is not a live region besides (design D11). -->
        <tmz-alert #versionNotice tabindex="-1" tone="brand" class="focus:outline-none paper:hidden" data-testid="dashboard-version-notice">
          <div class="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between">
            <p class="min-w-0 break-words" data-testid="dashboard-version-notice-text">
              {{ t('dashboards.versions.viewing', { number: version.number, author: version.author.name, time: (version.savedAt | dateTime) }) }}
            </p>
            <div class="flex flex-wrap gap-2">
              <button tmzButton variant="secondary" size="sm" type="button" (click)="backToCurrent()" data-testid="dashboard-version-back">
                {{ t('dashboards.versions.backToCurrent') }}
              </button>
              @if (!version.current) {
                <!-- It stays focusable while the restore is on its way, so its busy state can be read. -->
                <button
                  tmzButton
                  size="sm"
                  type="button"
                  class="aria-disabled:cursor-not-allowed"
                  [attr.aria-disabled]="restoring() ? 'true' : null"
                  [attr.aria-busy]="restoring() ? 'true' : null"
                  (click)="restoreShown(version)"
                  data-testid="dashboard-version-restore-shown"
                >
                  {{ t('dashboards.versions.restoreLabel', { number: version.number }) }}
                </button>
              }
            </div>
          </div>
        </tmz-alert>
      }

      @if (notice(); as notice) {
        <!-- Not itself a live region: the header's announces the same text once. -->
        <tmz-alert tone="warning" class="paper:hidden" data-testid="dashboard-notice">
          <div class="flex items-start justify-between gap-3">
            <p class="min-w-0 break-words" [tmzApiLang]="notice.text" data-testid="dashboard-notice-text">{{ notice.text }}</p>
            <button tmzButton variant="ghost" size="sm" type="button" class="shrink-0" (click)="dismissNotice()" data-testid="dashboard-notice-dismiss">{{ t('dashboards.notice.dismiss') }}</button>
          </div>
        </tmz-alert>
      }

      @if (dropped().length) {
        <!-- Not itself a live region: the header's announces the same text once. -->
        <tmz-alert tone="warning" class="paper:hidden" [heading]="t('dashboardFilters.dropped.title')" data-testid="dashboard-filters-dropped">
          <ul class="list-inside list-disc">
            @for (name of dropped(); track $index) {
              <li class="break-words">{{ t('dashboardFilters.dropped.item', { name }) }}</li>
            }
          </ul>
        </tmz-alert>
      }

      @if (!loading() && !error()) {
        <app-dashboard-filter-bar
          [projectId]="projectId"
          [dashboardId]="dashboardId()"
          [filters]="shownFilters()"
          [values]="filterValues()"
          [indicators]="valueIndicators()"
          [timeZone]="context.project()?.timeZone ?? 'UTC'"
          (changed)="filterChanged($event)"
          (cleared)="clearFilters()"
          (reset)="resetFilters()"
        />
      }

      @if (loading()) {
        <tmz-card><div class="flex flex-col gap-3" aria-busy="true" data-testid="indicators-loading"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div></tmz-card>
      } @else if (error(); as message) {
        <tmz-alert tone="danger" data-testid="indicators-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
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
                    {{ t('indicators.published', { time: (indicator.publishedAt | dateTime) }) }}
                  </span>
                } @else {
                  <tmz-badge tone="neutral" data-testid="indicator-unpublished">{{ t('indicators.list.notPublished') }}</tmz-badge>
                }
                <span class="min-w-16 whitespace-nowrap text-end tabular-nums text-body">
                  {{ indicator.rows === null ? '—' : t(key('indicators.list.rowCount', indicator.rows), { count: (indicator.rows | num) }) }}
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
          <tmz-card class="paper:hidden" [heading]="t('indicators.empty.title')" data-testid="indicators-empty">
            <p class="text-sm text-body">{{ t('indicators.empty.body') }}</p>
            @if (context.can('analyst')) {
              <p class="mt-2 text-sm text-body">{{ t('indicators.empty.editor') }}</p>
              <div class="mt-4 flex flex-wrap gap-2">
                <a tmzButton variant="secondary" [routerLink]="['/projects', projectId, 'scripts']" data-testid="indicators-empty-link">{{ t('indicators.empty.editorLink') }}</a>
                @if (!viewing()) {
                  <button tmzButton type="button" [disabled]="!overview.indicators.length" (click)="addWidget()" data-testid="widget-add">{{ t('indicators.add') }}</button>
                }
              </div>
            } @else {
              <p class="mt-2 text-sm text-body" data-testid="indicators-empty-viewer">{{ t('indicators.empty.viewer') }}</p>
            }
            @if (shownWidgets().length) {
              <p class="mt-4 text-sm text-body" data-testid="indicators-empty-widgets">{{ t(key('indicators.empty.widgets', shownWidgets().length), { count: shownWidgets().length }) }}</p>
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

        @if (shownWidgets().length) {
          <!-- Editors see how many widgets there are (and the limit), except in a version shown; everyone gets the switch to tables. -->
          <div class="flex flex-wrap items-center justify-between gap-3 paper:hidden" data-testid="indicators-toolbar">
            @if (editing()) {
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
              @if (editing()) {
                <button tmzButton size="sm" type="button" [disabled]="limitReached() || !overview.indicators.length" (click)="addWidget()" data-testid="widget-add">
                  {{ t('indicators.add') }}
                </button>
              }
            </div>
          </div>

          <app-widget-grid
            [widgets]="shownWidgets()"
            [projectId]="projectId"
            [editable]="editing()"
            [indicators]="overview.indicators"
            [allTables]="allTables()"
            [dashboardFilters]="active()"
            [colors]="colors()"
            [exporting]="exportingWidgets()"
            (layoutsChange)="layoutsChanged($event)"
            (edit)="editWidget($event)"
            (remove)="removeWidget($event)"
            (exportWidget)="exportWidget($event)"
          />
        } @else if (anyPublished()) {
          @if (viewing()) {
            <tmz-card [heading]="t('dashboards.versions.noWidgets')" data-testid="no-widgets" />
          } @else {
            <tmz-card [heading]="t('indicators.noWidgets.title')" data-testid="no-widgets">
              <p class="text-sm text-body">{{ context.can('analyst') ? t('indicators.noWidgets.editor') : t('indicators.noWidgets.viewer') }}</p>
              @if (context.can('analyst')) {
                <button tmzButton type="button" class="mt-4" (click)="addWidget()" data-testid="widget-add">{{ t('indicators.add') }}</button>
              }
            </tmz-card>
          }
        }

        <!-- The dashboard's source (add-widget-context, design D7): under the widgets, before the list of indicators. On paper the print header holds it once. -->
        @if (source(); as source) {
          <p class="break-words text-sm text-body paper:hidden" data-testid="dashboard-source">{{ t('dashboards.source.line', { source }) }}</p>
        }

        @if (anyPublished() && overview.indicators.length) {
          <tmz-disclosure
            class="paper:hidden"
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
  private readonly locale = inject(LOCALE_ID);
  private readonly dialog = inject(Dialog);
  private readonly confirm = inject(ConfirmDialog);
  private readonly downloader = inject(Downloader);
  private readonly tooltips = inject(ChartTooltips);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly context = inject(ProjectContext);
  /** The dashboard's paper mode (dashboard printing, design D1). */
  private readonly printer = inject(DashboardPrint);

  /** Bound from the `:dashboardId` route parameter; it changes when another dashboard opens. */
  readonly dashboardId = input.required<string>();
  /**
   * Bound from the `f` query parameter: the dashboard filter values in force, written as clean data writes its filters. Absent,
   * every filter is at its default; present, exactly what it holds (dashboard filters, design D7). A tab's link has none.
   */
  readonly f = input<string | undefined>();

  /**
   * The project the page belongs to. The page renders only once the project is read, and leaves with it, so the id is read once:
   * the layout saves sent as the page goes still have it.
   */
  protected readonly projectId = this.context.project()!.id;

  /** The open dashboard, from the project's list of dashboards, so a rename shows at once; null once it has left the list. */
  private readonly current = computed(() => this.context.dashboards().find((d) => d.id === this.dashboardId()) ?? null);
  protected readonly name = computed(() => this.current()?.name ?? '');
  /** The heading: the name of the version shown, or the dashboard's own. */
  protected readonly shownName = computed(() => this.viewing()?.name ?? this.name());
  /** Analysts and admins add, edit, arrange and remove widgets and change the dashboard, except while a version is shown (design D11). */
  protected readonly editing = computed(() => this.context.can('analyst') && !this.viewing());
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
      { id: 'filters', label: t('dashboards.menu.filters'), testId: 'dashboard-filters' },
      { id: 'colors', label: t('dashboards.menu.colors'), testId: 'dashboard-colors' },
      { id: 'source', label: t('dashboards.menu.source'), testId: 'dashboard-source' },
      ...(this.emailEnabled() ? [{ id: 'report', label: t('dashboards.menu.report'), testId: 'dashboard-report' }] : []),
      { id: 'versions', label: t('dashboards.menu.versions'), testId: 'dashboard-versions' },
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
   * Whether Tamiza sends email itself: only then does a dashboard offer a scheduled email (dashboard reports, design D14). Only the
   * actions ask, and only analysts and admins are shown them, so a viewer's page reads nothing of the deployment's configuration. The
   * service is created outside the actions' tracking: its first read sends a request.
   */
  private emailEnabled(): boolean {
    return untracked(() => this.injector.get(SystemConfigService)).emailEnabled();
  }

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

  /** The open dashboard's filters, in order; none until it is read. */
  protected readonly filters = signal<DashboardFilter[]>([]);
  /** The open dashboard has been read, so an entry of the address for a filter it lacks is known to be stale. */
  private readonly filtersRead = signal(false);
  /** The values of the open dashboard's own filters, from the address, which a version shown leaves as it is. */
  private readonly address = computed(() => readAddress(this.f(), this.filters()));
  /** The filters the bar shows: the version's while one is shown, the dashboard's otherwise. */
  protected readonly shownFilters = computed(() => this.viewing()?.filters ?? this.filters());
  /** The value in force of each filter on the member's screen, never saved. */
  protected readonly filterValues = computed(() => (this.viewing() ? this.viewValues() : this.address().values));
  /**
   * The indicators a text filter offers values from: those of the version's widgets while one is shown, which the dashboard may no
   * longer have; none otherwise, so the API reads those of the dashboard's own widgets.
   */
  protected readonly valueIndicators = computed(() => {
    const version = this.viewing();
    return version ? [...new Set(version.widgets.map((w) => w.config.indicator))] : undefined;
  });
  /** The filters with a value, in the bar's order, for the widgets. */
  protected readonly active = computed(() => activeFilters(this.shownFilters(), this.filterValues()));

  /** The open dashboard's palette and colors kept by value as it is; Tamiza and none until it is read (add-dashboard-palettes, design D7). */
  protected readonly dashboardColors = signal<DashboardColors>(defaultColors);
  /** What the widgets are drawn in: the colors of the version shown while one is, so it looks as it did; the dashboard's otherwise. */
  protected readonly colors = computed<DashboardColors>(() => {
    const version = this.viewing();
    return version ? colorsOf(version) : this.dashboardColors();
  });
  /** The open dashboard's source as it is; null while it has none or until it is read (add-widget-context, design D7). */
  protected readonly dashboardSource = signal<string | null>(null);
  /** The source shown under the widgets and in the print header: the version's while one is shown, so it reads as it did; the dashboard's otherwise. */
  protected readonly source = computed<string | null>(() => {
    const version = this.viewing();
    return (version ? version.source : this.dashboardSource()) || null;
  });
  /** The filters of an address that no longer apply, named, until the member changes a value or another dashboard opens. */
  protected readonly dropped = signal<string[]>([]);

  /** True once any indicator has a published table; until then the page explains how an indicator publishes. */
  protected readonly anyPublished = computed(() => !!this.overview()?.indicators.some((i: IndicatorEntry) => i.published));

  protected readonly publishedCount = computed(() => this.overview()?.indicators.filter((i) => i.published).length ?? 0);
  protected readonly limitReached = computed(() => this.widgets().length >= (this.overview()?.limits.maxWidgets ?? Infinity));

  /** Some widget has a chart or map whose table can be shown. */
  protected readonly hasTables = computed(() => this.anyPublished() && this.shownWidgets().some((w) => w.chartType !== 'stat'));

  // Viewing a version (add-dashboard-versions, design D11): its name, filters, palette, colors kept by value and widgets in place of the
  // dashboard's, read-only and with today's data, until the member goes back to the current version, restores one or opens another
  // dashboard.

  /** The version shown in place of the dashboard as it is; null while the dashboard itself is shown. */
  protected readonly viewing = signal<DashboardVersionDetails | null>(null);
  /** The value in force of each filter of the version shown, from their defaults; only on screen, never in the address. */
  private readonly viewValues = signal<FilterValues>({});
  /** The read of the version to show, dropped when another dashboard opens or the page goes. */
  private versionRead: Subscription | null = null;
  /** The widgets the grid draws: the version's while one is shown, each drawn from its config as a saved widget is. */
  protected readonly shownWidgets = computed<Widget[]>(() => {
    const version = this.viewing();
    return version ? version.widgets.map((w) => ({ ...w, dashboardId: this.dashboardId(), updatedAt: version.savedAt })) : this.widgets();
  });
  /** A restore is on its way, from its confirmation to its answer: another one waits for it. */
  protected readonly restoring = signal(false);
  /** The notice of the version shown, a component's element: focus moves to it when the view starts. */
  private readonly versionNotice = viewChild<unknown, ElementRef<HTMLElement>>('versionNotice', { read: ElementRef });

  // Printing (dashboard printing, design D7, D11).

  /** Every member may print a dashboard once its widgets are read, when it has any; the action prints no version shown (design D11). */
  protected readonly canPrint = computed(() => !this.viewing() && !this.loading() && !this.error() && this.widgets().length > 0);
  /** The Print action is getting the dashboard ready: it is busy until the browser's dialog opens. */
  protected readonly preparing = signal(false);
  protected readonly printedAt = this.printer.printedAt;
  protected readonly timeZone = computed(() => this.context.project()?.timeZone ?? 'UTC');
  /** The latest time one of the shown widgets' indicators was published, or null when none has; a version's widgets while one is shown. */
  protected readonly publishedAt = computed(() => {
    const named = new Set(this.shownWidgets().map((w) => w.config.indicator));
    const times = (this.overview()?.indicators ?? [])
      .filter((i) => i.published && i.publishedAt && named.has(i.name))
      .map((i) => new Date(i.publishedAt!).getTime())
      .filter((time) => !Number.isNaN(time));
    return times.length ? new Date(Math.max(...times)) : null;
  });
  private readonly printButton = viewChild<ElementRef<HTMLElement>>('printButton');

  /**
   * Layouts waiting for the pause after the last drag before they are saved, by the dashboard they were made on, then by widget: one
   * timer per dashboard, so a drag and the widgets it pushed, or a run of drags, are one request (add-dashboard-versions, design D8).
   */
  private readonly pending = new Map<string, { layouts: Map<string, Layout>; timer: ReturnType<typeof setTimeout> }>();
  /** Layout saves sent and not answered yet, each with its dashboard and the widgets it holds. */
  private readonly inFlight = new Set<{ dashboardId: string; widgetIds: ReadonlySet<string>; answered: Promise<void> }>();
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
      this.versionRead?.unsubscribe();
      // Leaving the dashboards opens another page: the notice has done its job.
      this.context.notice.set(null);
    });

    // While printing, the page's title names the project, the dashboard and the day (design D10); the version too while one is
    // shown, which the browser's own menu still prints, so the PDF's file name does not pass it off as the dashboard as it is.
    this.printer.titleSource = () => {
      const version = this.viewing();
      const shown = version ? this.transloco.translate('dashboards.print.versionTitle', { name: this.shownName(), number: version.number }) : this.shownName();
      return printTitle(this.context.project()?.name ?? '', shown, this.printer.printedAt() ?? new Date(), this.timeZone());
    };
    // Back from the browser's dialog, focus returns to the Print action; a wait the page cut short (it opened another dashboard,
    // or left) moves focus nowhere.
    this.printer.restored.pipe(takeUntilDestroyed()).subscribe(({ fromAction }) => {
      const cutShort = this.preparing();
      this.preparing.set(false);
      if (fromAction && !cutShort) {
        this.printButton()?.nativeElement.focus();
      }
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
        this.filters.set(dashboard.filters ?? []);
        this.filtersRead.set(true);
        this.dashboardColors.set(colorsOf(dashboard));
        this.dashboardSource.set(dashboard.source || null);
        this.widgets.set(dashboard.widgets);
        this.error.set(null);
        this.loading.set(false);
      });

    // Only the id is tracked: a new list of dashboards (setDashboards, reload, refresh) neither resets nor reads the open one again.
    effect(() => {
      const id = this.dashboardId();
      untracked(() => this.open(id));
    });

    // An address that names filters the dashboard no longer has, or values their kinds cannot take: the page drops them from the
    // address, which it replaces, and says so once (dashboard filters, design D7).
    effect(() => {
      const { values, dropped } = this.address();
      if (!this.filtersRead() || !dropped.length) {
        return;
      }
      untracked(() => {
        this.dropped.set(dropped);
        void this.router.navigate([], { relativeTo: this.route, queryParams: { f: writeAddress(this.filters(), values) }, replaceUrl: true });
        const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
        const text = [t('dashboardFilters.dropped.title'), ...dropped.map((name) => t('dashboardFilters.dropped.item', { name }))].join(' ');
        afterNextRender(() => this.announce(text), { injector: this.injector });
      });
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
    // A print being prepared was for the dashboard left: nothing is printed.
    this.printer.restore();
    // The previous dashboard's layouts go out now, each to its own dashboard; the new one does not wait for them.
    void this.flushSaves();
    // A version shown, or on its way, was one of the dashboard left (design D11).
    this.leaveView();
    this.widgets.set([]);
    this.filters.set([]);
    this.filtersRead.set(false);
    this.dashboardColors.set(defaultColors);
    this.dashboardSource.set(null);
    this.dropped.set([]);
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

  /**
   * The Print action (design D11): the dashboard is laid out as it prints, the widgets and tiles still on their way are waited
   * for, then the browser's dialog opens. The dialog is the outcome: no message follows it (web-ui, "Feedback while an action runs").
   */
  protected async print(): Promise<void> {
    if (this.preparing() || this.printer.printing()) {
      return;
    }
    this.preparing.set(true);
    this.announce(this.transloco.translate('dashboards.print.preparing'));
    await this.printer.print(() => this.preparing.set(false));
  }

  /** The widgets whose export is being prepared, by id, with the format: only their Export menu is busy (add-widget-export). */
  protected readonly exportingWidgets = signal<Record<string, WidgetFileExtension>>({});

  /**
   * Makes the file of a widget a member chose from its Export menu: an image from what the widget shows now, made in the browser, or its
   * workbook, prepared by the API from the settings and the filters on this screen. Ends with one message, a success or a failure.
   */
  protected async exportWidget(choice: WidgetExportChoice): Promise<void> {
    const { widget, format } = choice;
    if (widget.id in this.exportingWidgets()) {
      return;
    }
    this.exportingWidgets.update((busy) => ({ ...busy, [widget.id]: format }));
    try {
      await (format === 'xlsx' ? this.exportWorkbook(choice) : this.exportImage({ ...choice, format }));
    } catch (error) {
      this.reportExportFailure(error);
    } finally {
      this.exportingWidgets.update(({ [widget.id]: _done, ...rest }) => rest);
    }
  }

  private reportExportFailure(error: unknown): void {
    const t = (key: string) => this.transloco.translate(key);
    if (error instanceof Error && error.name === 'PngError') {
      this.toasts.error(t((error as Error & { reason: string }).reason === 'tooLarge' ? 'indicators.widgetExport.pngTooLarge' : 'indicators.widgetExport.pngFailed'));
      return;
    }
    this.errors.report(error);
  }

  /** The file name of a widget's export: the project, the dashboard (or the version's name), the widget, the version and the day. */
  private fileNameOf(widget: { title: string }, extension: WidgetFileExtension, now: Date): string {
    return widgetFileName({
      project: this.context.project()?.name ?? '',
      dashboard: this.shownName(),
      title: widget.title,
      version: this.viewing()?.number ?? null,
      day: isoDay(now, this.timeZone()),
      extension,
    });
  }

  private async exportImage({ widget, format, view }: WidgetExportChoice & { format: 'png' | 'svg' }): Promise<void> {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const registration = view.exportRegistration();
    if (!registration) {
      return;
    }
    const lang = this.transloco.getActiveLang();
    const now = new Date();
    const version = this.viewing();
    const indicator = this.overview()?.indicators.find((entry) => entry.name === widget.config.indicator);
    const { browserEnvironment, makeImage } = await loadExporter();
    const asChart = !view.asTable();
    const result = await makeImage(
      {
        format,
        title: widget.title,
        subtitle: widget.config.display?.subtitle ?? null,
        footnote: widget.config.display?.footnote ?? null,
        captionHidden: !captionShown(widget.config),
        bodyWidth: view.bodyWidth(),
        registration,
        notesAbove: view.exportNotesAbove(),
        mapPicture: view.chartType() === 'map' && asChart ? view.mapPicture() : null,
        basemapNote: view.chartType() === 'map' && asChart && view.mapBasemap() ? t('indicators.widgetExport.basemapLeftOut') : null,
        facts: {
          project: this.context.project()?.name ?? '',
          dashboard: this.shownName(),
          version: version ? t('dashboards.print.version', { number: version.number, author: version.author.name, time: this.when(version.savedAt) }) : null,
          source: this.source() ? t('dashboards.source.line', { source: this.source() }) : null,
          filters: this.shownFilters().map((filter) => `${filter.label}: ${describeValue(filter, this.filterValues()[filter.column], t)}`),
          published: indicator?.publishedAt ? t('dashboards.print.publishedAt', { time: this.when(indicator.publishedAt) }) : t('dashboards.print.notPublished'),
          exported: t('indicators.widgetExport.footerExported', { time: this.when(now), zone: this.timeZone() }),
        },
        tableLabels: {
          rowsNotShown: (count) => t(countKey('indicators.widgetExport.rowsNotShown', count, lang), { count }),
          columnsNotShown: (count) => t(countKey('indicators.widgetExport.columnsNotShown', count, lang), { count }),
          inWorkbook: t('indicators.widgetExport.inWorkbook'),
        },
        closeTooltips: () => this.tooltips.closeAll(),
      },
      browserEnvironment(view.host.ownerDocument, view.host),
    );
    this.downloader.save(result.blob, this.fileNameOf(widget, format, now));
    this.toasts.success(t(format === 'png' ? 'indicators.widgetExport.imageReadyPng' : 'indicators.widgetExport.imageReadySvg', { title: widget.title }));
  }

  private async exportWorkbook({ widget, view }: WidgetExportChoice): Promise<void> {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const ignored = widget.config.ignoredDashboardFilters ?? [];
    // The workbook is built from what the screen shows: the widget as it is saved, its display included (labels, tooltip labels and formats, texts, unit).
    const result = await firstValueFrom(
      this.service.exportWidget(this.projectId, this.dashboardId(), {
        format: 'xlsx',
        title: widget.title,
        chartType: widget.chartType,
        config: savedConfig(widget.chartType, widget.config),
        filters: this.active().map(({ filter, value }) => ({
          column: filter.column,
          kind: filter.kind,
          ...(value.values ? { values: value.values } : {}),
          ...(value.min ? { min: value.min } : {}),
          ...(value.max ? { max: value.max } : {}),
          ignored: ignored.includes(filter.column),
        })),
        locale: this.locale,
        ...(this.viewing() ? { version: this.viewing()!.number } : {}),
      }),
    );
    this.downloader.start(result.downloadUrl);
    const indicator = this.overview()?.indicators.find((entry) => entry.name === widget.config.indicator);
    const newer = !!result.publishedAt && !!indicator?.publishedAt && new Date(result.publishedAt) > new Date(indicator.publishedAt);
    const ready = t(countKey('indicators.widgetExport.workbookReady', result.rowCount, this.transloco.getActiveLang()), { title: widget.title, count: numberText(result.rowCount, this.locale) });
    this.toasts.success(newer ? `${ready} ${t('indicators.widgetExport.newerData', { time: this.when(result.publishedAt!) })}` : ready);
    void view;
  }

  /** A time on the print header: the shared format, in the project's time zone. */
  protected when(value: Date | string): string {
    return dateText(value, this.locale, 'dateTime', this.timeZone()) ?? '';
  }

  /** The key of a count message: its singular form when `count` is 1. */
  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
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

  // Dashboard filters: the values live in the address, so a link shows the same view; changing one never saves anything (design D7).

  protected filterChanged(change: FilterChange): void {
    this.setFilterValues({ ...this.filterValues(), [change.column]: change.value });
  }

  protected clearFilters(): void {
    this.setFilterValues(Object.fromEntries(this.shownFilters().map((f) => [f.column, null])));
  }

  protected resetFilters(): void {
    this.setFilterValues(defaultsOf(this.shownFilters()));
  }

  /**
   * Writes the values to the address, which every widget follows, and says what the dashboard is filtered by; focus stays put. The
   * values of a version shown stay on screen: the address keeps those of the dashboard as it is (add-dashboard-versions, design D11).
   */
  private setFilterValues(values: FilterValues): void {
    const filters = this.shownFilters();
    if (this.viewing()) {
      this.viewValues.set(values);
    } else {
      this.dropped.set([]);
      void this.router.navigate([], { relativeTo: this.route, queryParams: { f: writeAddress(filters, values) } });
    }
    const names = activeFilters(filters, values).map((a) => a.filter.label);
    this.announce(
      names.length
        ? this.transloco.translate('dashboardFilters.announce.filtered', { names: names.join(', ') })
        : this.transloco.translate('dashboardFilters.announce.cleared'),
    );
  }

  // Layout: applied at once, saved once the dragging on the dashboard has been still for a moment (add-dashboard-versions, design D8).

  protected layoutsChanged(changes: Placed[]): void {
    const dashboardId = this.dashboardId();
    this.widgets.update((list) => list.map((w) => ({ ...w, layout: changes.find((c) => c.id === w.id)?.layout ?? w.layout })));
    // Every change restarts the dashboard's one timer; a widget moved again keeps only its latest place.
    const waiting = this.pending.get(dashboardId);
    if (waiting) {
      clearTimeout(waiting.timer);
    }
    const layouts = waiting?.layouts ?? new Map<string, Layout>();
    changes.forEach((change) => layouts.set(change.id, change.layout));
    this.pending.set(dashboardId, { layouts, timer: setTimeout(() => void this.saveLayouts(dashboardId), layoutSaveDelayMs) });
  }

  /**
   * Sends the dashboard's waiting layouts now, all in one request, to the dashboard they were made on. The promise settles once
   * every layout save of that dashboard sent so far is answered, whatever the answer, so a step that must follow them can wait:
   * editing a widget and duplicating (design D12 of add-multiple-dashboards), and opening the version history, viewing a version and
   * restoring one (design D8).
   */
  private saveLayouts(dashboardId: string): Promise<void> {
    this.send(dashboardId);
    return this.answered((save) => save.dashboardId === dashboardId);
  }

  /** Sends every dashboard's waiting layouts now; settles once every layout save sent so far is answered. */
  private flushSaves(): Promise<void> {
    [...this.pending.keys()].forEach((dashboardId) => this.send(dashboardId));
    return this.answered(() => true);
  }

  /** Sends the dashboard's waiting layouts, if it has any, as one save. A failed save shows what the server has (`saveFailed`). */
  private send(dashboardId: string): void {
    const waiting = this.pending.get(dashboardId);
    if (!waiting) {
      return;
    }
    clearTimeout(waiting.timer);
    this.pending.delete(dashboardId);
    const widgets = [...waiting.layouts].map(([id, layout]) => ({ id, layout }));
    const answered = new Promise<void>((resolve) =>
      this.service.saveLayout(this.projectId, dashboardId, widgets).subscribe({
        next: () => resolve(),
        error: (failure) => {
          this.saveFailed(dashboardId, failure);
          resolve();
        },
      }),
    );
    const save = { dashboardId, widgetIds: new Set(waiting.layouts.keys()), answered };
    this.inFlight.add(save);
    void answered.then(() => this.inFlight.delete(save));
  }

  /** Settles once every layout save on its way that `which` picks is answered: one sent before the latest drags included. */
  private answered(which: (save: { dashboardId: string; widgetIds: ReadonlySet<string> }) => boolean): Promise<void> {
    return Promise.all([...this.inFlight].filter(which).map((save) => save.answered)).then(() => undefined);
  }

  /** The widget's layout on the dashboard waits for the pause, or is on its way. */
  private layoutUnsaved(dashboardId: string, widgetId: string): boolean {
    const sending = [...this.inFlight].some((save) => save.dashboardId === dashboardId && save.widgetIds.has(widgetId));
    return sending || !!this.pending.get(dashboardId)?.layouts.has(widgetId);
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
   * The form can move the widget to another dashboard, so its layout still waiting or on its way is saved first, with the others of
   * the dashboard waiting with it: sent later, it would land on the dashboard the widget left (design D9).
   */
  protected async editWidget(widget: Widget): Promise<void> {
    const dashboardId = this.dashboardId();
    if (!this.layoutUnsaved(dashboardId, widget.id)) {
      this.openDialog(widget);
      return;
    }
    await this.saveLayouts(dashboardId);
    const latest = this.widgets().find((w) => w.id === widget.id);
    // Another dashboard or page opened meanwhile, or the save found the widget's dashboard removed: there is nothing left to edit
    // here. A form opened while waiting (Edit chosen again, or another action) stays the only one, and a version shown is read-only.
    if (latest && this.showing(dashboardId) && this.current() && !this.dialog.openDialogs.length && !this.viewing()) {
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
        filters: this.filters(),
        values: this.filterValues(),
        colors: this.colors(),
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
    // Its own waiting layout goes with it; the other widgets' still wait for the pause.
    this.dropLayout(dashboard.id, widget.id);
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
      case 'filters':
        this.editFilters();
        break;
      case 'colors':
        this.editColors();
        break;
      case 'source':
        this.editSource();
        break;
      case 'report':
        this.editReport();
        break;
      case 'versions':
        void this.openVersions();
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

  /**
   * Defines the dashboard's filters (dashboard filters, design D8). Once saved, the dashboard shows them at their defaults: the values
   * in the address go. Focus returns to the actions button, where the form was opened from.
   */
  private editFilters(): void {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    this.dialog
      .open<DashboardFiltersResult, DashboardFiltersData>(DashboardFiltersDialog, {
        data: {
          projectId: this.projectId,
          dashboardId: dashboard.id,
          filters: this.filters(),
          widgets: this.widgets(),
          indicators: this.overview()?.indicators ?? [],
          timeZone: this.context.project()?.timeZone ?? 'UTC',
        },
        ariaLabelledBy: 'dashboard-filters-dialog-title',
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        backdropClass: ['cdk-overlay-dark-backdrop'],
        maxWidth: '100vw',
        maxHeight: '100dvh',
      })
      .closed.subscribe((saved) => {
        if (!saved) {
          return;
        }
        if ('gone' in saved) {
          this.gone(dashboard);
          return;
        }
        if (!this.showing(dashboard.id)) {
          return; // another dashboard opened meanwhile: it reads its own filters
        }
        this.filters.set(saved);
        this.dropped.set([]);
        void this.router.navigate([], { relativeTo: this.route, queryParams: { f: null } });
        this.toasts.success(this.transloco.translate('dashboardFilters.saved'));
      });
  }

  /**
   * Chooses the dashboard's palette and the colors kept by value (add-dashboard-palettes, design D9). Once saved, the widgets are drawn
   * in them at once: only their colors change, so nothing is queried again. Saving what the dashboard already has closes the form and
   * changes nothing. Focus returns to the actions button, where the form was opened from.
   */
  private editColors(): void {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    this.dialog
      .open<DashboardColorsResult, DashboardColorsData>(DashboardColorsDialog, {
        data: {
          projectId: this.projectId,
          dashboardId: dashboard.id,
          colors: this.dashboardColors(),
          widgets: this.widgets(),
          indicators: this.overview()?.indicators ?? [],
        },
        ariaLabelledBy: 'dashboard-colors-dialog-title',
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        backdropClass: ['cdk-overlay-dark-backdrop'],
        maxWidth: '100vw',
        maxHeight: '100dvh',
      })
      .closed.subscribe((saved) => {
        if (!saved) {
          return;
        }
        if ('gone' in saved) {
          this.gone(dashboard);
          return;
        }
        if (!this.showing(dashboard.id)) {
          return; // another dashboard opened meanwhile: it reads its own colors
        }
        this.dashboardColors.set(saved);
        this.toasts.success(this.transloco.translate('dashboardColors.saved'));
      });
  }

  /**
   * Gives the dashboard a source, changes it or removes it (add-widget-context, design D7). Once saved, the line under the widgets
   * follows at once and nothing is queried again. Saving what the dashboard already has closes the form and changes nothing. A
   * dashboard another member removed meanwhile is handled as for any change to it. Focus returns to the actions button.
   */
  private editSource(): void {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    this.dialog
      .open<DashboardSourceResult, DashboardSourceData>(DashboardSourceDialog, {
        data: { projectId: this.projectId, dashboardId: dashboard.id, name: dashboard.name, source: this.dashboardSource() },
        ariaLabelledBy: 'dashboard-source-dialog-title',
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        backdropClass: ['cdk-overlay-dark-backdrop'],
        maxWidth: '100vw',
        maxHeight: '100dvh',
      })
      .closed.subscribe((saved) => {
        if (!saved) {
          return;
        }
        if ('gone' in saved) {
          this.gone(dashboard);
          return;
        }
        if (!this.showing(dashboard.id)) {
          return; // another dashboard opened meanwhile: it reads its own source
        }
        this.dashboardSource.set(saved.source);
        this.toasts.success(this.transloco.translate(saved.source ? 'dashboardSource.saved' : 'dashboardSource.removed'));
      });
  }

  /**
   * The dashboard's scheduled email (dashboard reports, design D14). Saving or removing it changes nothing the page shows, so a toast
   * says what was done once the form has closed and focus is back on the actions button.
   */
  private editReport(): void {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    this.dialog
      .open<DashboardReportResult, DashboardReportData>(DashboardReportDialog, {
        data: { projectId: this.projectId, dashboard },
        ariaLabelledBy: 'dashboard-report-dialog-title',
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        backdropClass: ['cdk-overlay-dark-backdrop'],
        maxWidth: '100vw',
        maxHeight: '100dvh',
      })
      .closed.subscribe((result) => {
        if (!result) {
          return;
        }
        if ('gone' in result) {
          this.gone(dashboard);
          return;
        }
        this.toasts.success(this.transloco.translate('removed' in result ? 'dashboards.report.removed' : 'dashboards.report.saved', { name: dashboard.name }));
      });
  }

  /**
   * The dashboard's version history (add-dashboard-versions, design D11). The layouts still waiting for their pause, or on their
   * way, are saved first, so the current version holds the widgets where the member left them (design D8). The member then views
   * or restores a version, or the dialog finds the dashboard removed meanwhile; closed, it gives focus back to the actions button.
   */
  private async openVersions(): Promise<void> {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    await this.saveLayouts(dashboard.id);
    // Removed or left meanwhile, or the history (or another form) was opened while the layouts were saved: it stays the only one.
    if (!this.showing(dashboard.id) || !this.current() || this.dialog.openDialogs.length) {
      return;
    }
    this.dialog
      .open<DashboardVersionsResult, DashboardVersionsData>(DashboardVersionsDialog, {
        data: { projectId: this.projectId, dashboard },
        ariaLabelledBy: 'dashboard-versions-title',
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        backdropClass: ['cdk-overlay-dark-backdrop'],
        maxWidth: '100vw',
        maxHeight: '100dvh',
      })
      .closed.subscribe((result) => {
        if (!result) {
          return;
        }
        if ('gone' in result) {
          this.gone(dashboard);
        } else if ('view' in result) {
          void this.viewVersion(dashboard, result.view);
        } else {
          void this.restoreVersion(dashboard, result.restore);
        }
      });
  }

  /**
   * Shows version `number` of the dashboard in its place, read-only, with today's data (design D11, "Viewing"): its name, its widgets
   * drawn from their configs and its filters from their defaults. The layouts still waiting are saved first (design D8). Focus moves
   * to the notice naming the version, which screen readers then read, and which offers to go back or to restore it.
   */
  private async viewVersion(dashboard: DashboardEntry, number: number): Promise<void> {
    await this.saveLayouts(dashboard.id);
    if (!this.showing(dashboard.id) || !this.current()) {
      return; // removed or left meanwhile
    }
    this.versionRead?.unsubscribe();
    this.versionRead = this.service.getVersion(this.projectId, dashboard.id, number).subscribe({
      next: (version) => {
        this.versionRead = null;
        this.viewValues.set(defaultsOf(version.filters));
        this.viewing.set(version);
        afterNextRender(
          () => {
            if (!this.dialog.openDialogs.length) {
              this.versionNotice()?.nativeElement.focus();
            }
          },
          { injector: this.injector },
        );
      },
      error: (failure) => {
        this.versionRead = null;
        this.versionRefused(failure, dashboard);
      },
    });
  }

  /** Shows the dashboard as it is again, with focus on its heading (design D11). */
  protected backToCurrent(): void {
    this.leaveView();
    this.focusHeading();
  }

  /** Restores the version shown, from its notice. */
  protected restoreShown(version: DashboardVersionDetails): void {
    const dashboard = this.current();
    if (dashboard) {
      void this.restoreVersion(dashboard, version);
    }
  }

  private leaveView(): void {
    this.versionRead?.unsubscribe();
    this.versionRead = null;
    this.viewing.set(null);
  }

  /**
   * Restores a version of the dashboard, from the history or from its view, once the member confirms (design D11, "Restoring"). The
   * layouts still waiting are saved first (design D8), so the state the restore replaces is a version of its own. The member who
   * opened another dashboard meanwhile restores nothing; one already restoring waits for that restore.
   */
  private async restoreVersion(dashboard: DashboardEntry, version: VersionToRestore): Promise<void> {
    if (this.restoring()) {
      return;
    }
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const confirmed = await this.confirm.confirm({
      title: t('dashboards.versions.confirm.title', { number: version.number }),
      message: t('dashboards.versions.confirm.message', { name: dashboard.name, time: dateText(version.savedAt, this.locale, 'dateTime') }),
      confirmLabel: t('dashboards.versions.confirm.action'),
      cancelLabel: t('common.cancel'),
    });
    if (!confirmed || !this.showing(dashboard.id) || this.restoring()) {
      return;
    }
    this.restoring.set(true);
    await this.saveLayouts(dashboard.id);
    if (!this.showing(dashboard.id) || !this.current()) {
      this.restoring.set(false);
      return; // the save found the dashboard removed, or the member left
    }
    this.service.restoreVersion(this.projectId, dashboard.id, version.number).subscribe({
      next: (restore) => {
        this.restoring.set(false);
        this.restored(dashboard, version, restore);
      },
      error: (failure) => {
        this.restoring.set(false);
        this.versionRefused(failure, dashboard);
      },
    });
  }

  /**
   * After a restore (design D11): the page leaves the view, takes the restored colors and reads the dashboard again, which drops from
   * the address the values of filters the version does not define, the tab follows a name the restore changed, one message says what
   * was done (the name kept and why, each widget that came back as a copy and the dashboard that still holds it), and focus moves to
   * the heading. A restore that found the dashboard already as in the version changed nothing, and nothing on the page changes either.
   */
  private restored(dashboard: DashboardEntry, version: VersionToRestore, restore: DashboardRestore): void {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const name = restore.dashboard.name;
    const nameKept = restore.nameKept ? [t('dashboards.versions.nameKept', { current: name, name: version.name })] : [];
    if (!restore.restored) {
      this.toasts.success([t('dashboards.versions.unchanged', { name, number: version.number }), ...nameKept].join(' '));
      return;
    }
    const list = this.context.dashboards();
    if (list.some((d) => d.id === dashboard.id && d.name !== name)) {
      this.context.setDashboards(list.map((d) => (d.id === dashboard.id ? { id: d.id, name } : d)));
    }
    if (this.showing(dashboard.id)) {
      this.leaveView();
      // The palette and colors kept by value the restore brought back, at once: until the dashboard is read again, the widgets would be
      // drawn in the colors the restore replaced, or in the version's, which are no longer shown.
      this.dashboardColors.set(colorsOf(restore.dashboard));
      this.dashboardSource.set(restore.dashboard.source || null);
      this.loads.next(dashboard.id);
      this.focusHeading();
    }
    const copies = restore.copies.map((copy) => t('dashboards.versions.copy', { title: copy.title, dashboard: copy.dashboardName }));
    this.toasts.success([t('dashboards.versions.restored', { number: version.number, version: restore.version.number }), ...nameKept, ...copies].join(' '));
  }

  /**
   * A version that could not be read or restored (design D11). One no longer kept is said in a toast, and the history lists the
   * versions kept again, in place of a view of it; a dashboard removed meanwhile is handled as any change to it (`gone`); a refusal
   * with a field message, such as a version over the widget limit, says that message; any other failure is reported.
   */
  private versionRefused(failure: unknown, dashboard: DashboardEntry): void {
    const parsed = this.errors.parse(failure);
    if (parsed.code === 'dashboards.notFound') {
      this.gone(dashboard);
      return;
    }
    this.toasts.error(Object.values(parsed.fieldErrors)[0] ?? parsed.message);
    if (parsed.code === 'dashboards.versionNotFound' && this.showing(dashboard.id)) {
      if (this.viewing()) {
        // Now, before the history opens: it gives focus back to the heading when it closes.
        this.leaveView();
        this.heading().nativeElement.focus();
      }
      void this.openVersions();
    }
  }

  private async duplicate(): Promise<void> {
    const dashboard = this.current();
    if (!dashboard) {
      return;
    }
    // The copy is made from what the server has: the layouts still waiting for their pause, or on their way, are saved first, as
    // the original's own version (add-dashboard-versions, design D12).
    await this.saveLayouts(dashboard.id);
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
    const amount = count === 0 ? 'None' : isSingular(count, this.transloco.getActiveLang()) ? 'One' : '';
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
    const waiting = this.pending.get(dashboardId);
    if (waiting) {
      clearTimeout(waiting.timer);
      this.pending.delete(dashboardId);
    }
  }

  /** Forgets the waiting layout of a widget being removed; the dashboard's timer goes when it waits for no other. */
  private dropLayout(dashboardId: string, widgetId: string): void {
    const waiting = this.pending.get(dashboardId);
    if (waiting?.layouts.delete(widgetId) && !waiting.layouts.size) {
      this.dropSaves(dashboardId);
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
        const ready = this.transloco.translate(this.key('indicators.export.ready', result.rowCount), { rows: numberText(result.rowCount, this.locale) });
        this.toasts.success(result.cutCells > 0 ? `${ready} ${this.transloco.translate(this.key('cleanData.export.readyCut', result.cutCells), { cut: result.cutCells })}` : ready);
      },
      error: (failure) => {
        this.exporting.set(null);
        this.errors.report(failure);
      },
    });
  }
}
