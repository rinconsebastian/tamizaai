import { Dialog } from '@angular/cdk/dialog';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, DestroyRef, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Subscription, map, tap } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import {
  DataColumn,
  DataFilter,
  DataOverview,
  DataPage,
  DataQuery,
  DataService,
  DataSource,
  DataValue,
  ExportFormat,
  pageSizes,
} from '../../core/projects/data.service';
import { Alert, ApiLang, Button, Card, Input, Skeleton, Table, TableContainer, ToastService, tableCell, tableHead, tableHeaderCell, tableRow, DateTimePipe, NumberPipe, numberText } from '../../shared/ui';
import { DataFilterDialog, DataFilterDialogData, DataFilterDialogResult } from './data-filter-dialog';
import { describeFilter, emptyQuery, normalizeFilter, parseView, sanitize, viewParams } from './data-view';
import { countKey } from '../../core/i18n/plural';
import { ProjectContext } from './project-context';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/**
 * The clean data dashboard: the tables the last runs published, one page at a time, with sorting, typed filters and exports done
 * on the server. The page address holds the view (source, table, sort, page, filters), so a reload or a shared link shows the
 * same rows; every change goes through the address and the address drives the requests.
 */
@Component({
  selector: 'app-project-clean-data',
  imports: [
    DateTimePipe,
    NumberPipe,
    RouterLink,
    TranslocoDirective,
    CdkMenu,
    CdkMenuItem,
    CdkMenuTrigger,
    Alert,
    ApiLang,
    Button,
    Card,
    Input,
    Skeleton,
    Table,
    TableContainer,
  ],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t" [attr.aria-busy]="!overview() || pageLoading()">
      @if (overview(); as overview) {
        @if (error(); as message) {
          <tmz-alert tone="danger" data-testid="data-error" [tmzApiLang]="message">{{ message }}</tmz-alert>
        }

        @if (overview.sources.length === 0) {
          <tmz-card [heading]="t('cleanData.empty.title')" data-testid="data-empty">
            @if (context.can('analyst')) {
              <p class="text-sm text-body">{{ t('cleanData.empty.editor') }}</p>
              <a tmzButton variant="secondary" class="mt-4" [routerLink]="['..', 'scripts']" data-testid="data-empty-link">{{ t('cleanData.empty.editorLink') }}</a>
            } @else {
              <p class="text-sm text-body" data-testid="data-empty-viewer">{{ t('cleanData.empty.viewer') }}</p>
            }
          </tmz-card>
        } @else {
          <div class="flex flex-col gap-3">
            <div class="flex min-w-0 flex-col gap-4 sm:flex-row sm:flex-wrap sm:items-end">
              @if (overview.sources.length > 1) {
                <div role="group" [attr.aria-label]="t('cleanData.sourceLabel')" class="inline-flex w-full rounded-base shadow-xs sm:w-auto" data-testid="source-switch">
                  @for (item of overview.sources; track item.source) {
                    <button
                      type="button"
                      class="min-h-10 flex-1 border px-4 py-2.5 text-sm font-medium first:rounded-s-base last:rounded-e-base focus:z-10 focus:outline-none focus:ring-4 focus:ring-neutral-tertiary sm:flex-none"
                      [class]="item.source === source() ? 'border-transparent bg-brand text-white' : 'border-default-medium bg-neutral-secondary-medium text-body hover:bg-neutral-tertiary-medium hover:text-heading'"
                      [attr.aria-pressed]="item.source === source()"
                      (click)="selectSource(item.source)"
                      [attr.data-testid]="'source-' + item.source"
                    >
                      {{ t('cleanData.source.' + item.source) }}
                    </button>
                  }
                </div>
              }
              <div class="min-w-0 sm:w-80">
                <label for="dataTable" class="mb-2 block text-sm font-medium text-heading">{{ t('cleanData.tableLabel') }}</label>
                <select tmzInput id="dataTable" (change)="selectTable($any($event.target).value)" data-testid="table-select">
                  @for (item of tables(); track item.name) {
                    <option [value]="item.name" [selected]="item.name === table()">
                      {{ item.rowCount === null ? item.name : t(key('cleanData.tableOption', item.rowCount), { name: item.name, count: (item.rowCount | num) }) }}
                    </option>
                  }
                </select>
              </div>
            </div>

            <p class="text-sm text-body">
              @if (publishedAt(); as published) {
                <span data-testid="published">
                  {{ t('cleanData.published', { time: (published | dateTime) }) }}
                  @if (runId(); as run) {
                    {{ t('cleanData.publishedRun', { run: shortId(run) }) }}
                  }
                  @if (context.can('analyst')) {
                    <a [routerLink]="['..', 'scripts']" fragment="runs" class="ms-1 text-fg-brand hover:underline">{{ t('cleanData.runsLink') }}</a>
                  }
                </span>
                ·
              }
              <span data-testid="zone">{{ t('cleanData.zone', { zone: timeZone() }) }}</span>
            </p>
          </div>

          <ng-template #exportMenu>
            <div cdkMenu class="z-10 min-w-48 rounded-base border border-default-medium bg-neutral-primary-medium py-2 shadow-lg" data-testid="export-menu">
              <button cdkMenuItem type="button" class="block min-h-10 w-full px-4 py-2 text-left text-sm text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:bg-neutral-tertiary-medium focus:outline-none" (cdkMenuItemTriggered)="exportTable('csv')" data-testid="export-csv">
                {{ t('cleanData.export.csv') }}
              </button>
              <button cdkMenuItem type="button" class="block min-h-10 w-full px-4 py-2 text-left text-sm text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:bg-neutral-tertiary-medium focus:outline-none" (cdkMenuItemTriggered)="exportTable('xlsx')" data-testid="export-xlsx">
                {{ t('cleanData.export.xlsx') }}
              </button>
            </div>
          </ng-template>

          @if (republishedAt(); as time) {
            <tmz-alert tone="brand" [announce]="true" data-testid="republished">{{ t('cleanData.republished', { time: (time | dateTime) }) }}</tmz-alert>
          }
          @if (goneTable(); as gone) {
            <tmz-alert tone="warning" [announce]="true" data-testid="table-gone">
              {{ table() ? t('cleanData.tableGone', { name: gone, next: table() }) : t('cleanData.tableGoneNone', { name: gone }) }}
            </tmz-alert>
          }
          @if (dropped().length) {
            <tmz-alert tone="warning" [announce]="true" [heading]="t('cleanData.dropped.title')" data-testid="dropped">
              <ul class="mt-1 list-disc ps-5">
                @for (item of dropped(); track item) {
                  <li>{{ droppedText(item, t) }}</li>
                }
              </ul>
            </tmz-alert>
          }

          @if (table()) {
            <div class="flex items-center justify-between gap-3">
              <p class="text-sm text-body" data-testid="row-count">
                @if (page(); as page) {
                  {{ query().filters.length ? t(key('cleanData.matching', page.total), { count: (page.total | num) }) : t(key('cleanData.total', page.total), { count: (page.total | num) }) }}
                }
              </p>
              <button
                tmzButton
                variant="secondary"
                size="sm"
                type="button"
                [cdkMenuTriggerFor]="exportMenu"
                [disabled]="exporting()"
                [attr.aria-busy]="exporting()"
                [attr.title]="t('cleanData.export.help')"
                data-testid="export-button"
              >
                {{ exporting() ? t('cleanData.export.preparing') : t('cleanData.export.button') }}
              </button>
            </div>
          }

          @if (page(); as page) {
            <div>
              @if (query().filters.length) {
                <ul class="mb-3 flex flex-wrap items-center gap-2" data-testid="filter-chips">
                  @for (filter of query().filters; track filter.column) {
                    <li class="inline-flex max-w-full items-center rounded-full border border-brand-subtle bg-brand-softer text-sm text-fg-brand-strong" data-testid="filter-chip">
                      <button type="button" class="min-h-9 min-w-0 truncate rounded-s-full px-3 py-1 text-left focus:outline-none focus:ring-4 focus:ring-brand-medium" [attr.aria-label]="t('cleanData.chip.edit', { name: filter.column })" (click)="openFilter(columnOf(filter.column)!)" data-testid="chip-edit">
                        {{ chipText(filter, t) }}
                      </button>
                      <button type="button" class="min-h-9 min-w-9 rounded-e-full px-2 hover:bg-brand-soft focus:outline-none focus:ring-4 focus:ring-brand-medium" [attr.aria-label]="t('cleanData.chip.remove', { name: filter.column })" (click)="removeFilter(filter.column)" data-testid="chip-remove">
                        <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18" /></svg>
                      </button>
                    </li>
                  }
                  <li><button tmzButton variant="ghost" size="sm" type="button" (click)="clearFilters()" data-testid="clear-filters">{{ t('cleanData.clearFilters') }}</button></li>
                </ul>
              }

              @if (page.rows.length === 0) {
                <p class="mb-2 text-sm text-body" data-testid="no-rows">{{ query().filters.length ? t('cleanData.noMatch') : t('cleanData.emptyTable') }}</p>
              }

              <tmz-table-container class="max-h-[70vh] overflow-y-auto" [class.opacity-60]="pageLoading()">
                <table tmzTable data-testid="data-table">
                  <thead [class]="tableHead + ' sticky top-0 z-10'">
                    <tr>
                      @for (column of page.columns; track column.name) {
                        <th scope="col" [class]="tableHeaderCell + ' align-top'" [attr.aria-sort]="ariaSort(column.name)" data-testid="column-header">
                          <div class="flex items-start gap-1">
                            <button
                              type="button"
                              class="min-h-8 rounded-base text-left font-medium text-heading hover:underline focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
                              [attr.aria-label]="t('cleanData.sort.by', { name: column.name }) + ', ' + t('cleanData.sort.' + sortState(column.name))"
                              (click)="toggleSort(column)"
                              data-testid="sort-button"
                            >
                              {{ column.name }}
                              <span aria-hidden="true" class="ms-1 text-xs">{{ sortArrow(column.name) }}</span>
                            </button>
                            <button
                              type="button"
                              class="min-h-8 min-w-8 rounded-base p-1.5 hover:bg-neutral-tertiary-medium focus:outline-none focus:ring-4 focus:ring-neutral-tertiary"
                              [class]="hasFilter(column.name) ? 'text-fg-brand' : 'text-body'"
                              [attr.aria-label]="hasFilter(column.name) ? t('cleanData.filter.active', { name: column.name }) : t('cleanData.filter.open', { name: column.name })"
                              (click)="openFilter(column)"
                              data-testid="filter-button"
                            >
                              <svg aria-hidden="true" class="size-4" viewBox="0 0 24 24" fill="currentColor"><path d="M3 4h18l-7 8.5V20l-4-2v-5.5z" /></svg>
                            </button>
                          </div>
                          <span class="flex max-w-xs items-baseline gap-1 text-xs font-normal">
                            @if (column.label) {
                              <span class="min-w-0 truncate" [attr.title]="column.label" data-testid="column-label">{{ column.label }}</span>
                              <span aria-hidden="true">·</span>
                            }
                            <span class="shrink-0" data-testid="column-type">{{ t('preview.types.' + column.type) }}</span>
                          </span>
                        </th>
                      }
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of page.rows; track $index; let r = $index) {
                      <tr [class]="tableRow" data-testid="data-row">
                        @for (value of row; track $index; let c = $index) {
                          <td
                            [class]="tableCell + ' max-w-xs truncate whitespace-nowrap' + (isNumber(page.columns[c]) ? ' text-end tabular-nums' : '')"
                            [attr.title]="value === null ? null : cellTitle(value)"
                          >
                            @if (value === null) {
                              <span aria-hidden="true">—</span><span class="sr-only">{{ t('cleanData.emptyCell') }}</span>
                            } @else {
                              {{ cellText(value, page.columns[c], t) }}
                              @if (isCut(page, r, c)) {
                                <span aria-hidden="true">…</span><span class="sr-only">{{ t('preview.cut') }}</span>
                              }
                            }
                          </td>
                        }
                      </tr>
                    }
                  </tbody>
                </table>
              </tmz-table-container>

              @if (page.rows.length > 0 && page.total > smallestPageSize) {
              <nav class="mt-3 flex flex-col gap-3 text-sm text-body sm:flex-row sm:items-center sm:justify-between" [attr.aria-label]="t('cleanData.paging.label')">
                <div class="flex items-center gap-2">
                  <label for="dataPageSize">{{ t('cleanData.paging.size') }}</label>
                  <select tmzInput id="dataPageSize" class="!w-auto" (change)="setPageSize($any($event.target).value)" data-testid="page-size">
                    @for (size of pageSizes; track size) {
                      <option [value]="size" [selected]="size === page.pageSize">{{ size }}</option>
                    }
                  </select>
                </div>
                <div class="flex items-center justify-between gap-3 sm:justify-end">
                  @if (page.total > 0) {
                    <span data-testid="page-info" class="tabular-nums">{{ t('cleanData.paging.info', { from: (rangeStart(page) | num), to: (rangeEnd(page) | num), total: (page.total | num) }) }}</span>
                  }
                  <div class="flex gap-2">
                    <button tmzButton variant="secondary" size="sm" type="button" [disabled]="page.page <= 1" (click)="goToPage(page.page - 1)" data-testid="previous-page">{{ t('cleanData.paging.previous') }}</button>
                    <button tmzButton variant="secondary" size="sm" type="button" [disabled]="rangeEnd(page) >= page.total" (click)="goToPage(page.page + 1)" data-testid="next-page">{{ t('cleanData.paging.next') }}</button>
                  </div>
                </div>
              </nav>
              }
            </div>
          } @else if (pageLoading()) {
            <tmz-card><div class="flex flex-col gap-3" aria-busy="true" data-testid="data-loading"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div></tmz-card>
          }
        }
      } @else if (!error()) {
        <tmz-card><div class="flex flex-col gap-3" aria-busy="true" data-testid="data-loading"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div></tmz-card>
      } @else {
        <tmz-alert tone="danger" data-testid="data-error" [tmzApiLang]="error()">{{ error() }}</tmz-alert>
      }
    </div>
  `,
})
export class ProjectCleanData {
  private readonly service = inject(DataService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  private readonly dialog = inject(Dialog);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly downloader = inject(Downloader);
  protected readonly context = inject(ProjectContext);

  protected readonly pageSizes = pageSizes;
  protected readonly smallestPageSize = pageSizes[0];
  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  protected readonly overview = signal<DataOverview | null>(null);
  protected readonly source = signal<DataSource | null>(null);
  protected readonly table = signal<string | null>(null);
  protected readonly query = signal<DataQuery>(emptyQuery());
  protected readonly page = signal<DataPage | null>(null);
  protected readonly pageLoading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly exporting = signal(false);

  /** Notices about what changed under the member's feet. */
  protected readonly republishedAt = signal<string | null>(null);
  protected readonly dropped = signal<string[]>([]);
  protected readonly goneTable = signal<string | null>(null);

  protected readonly tables = computed(() => this.overview()?.sources.find((s) => s.source === this.source())?.tables ?? []);
  protected readonly publishedAt = computed(() => this.page()?.publishedAt ?? this.overview()?.sources.find((s) => s.source === this.source())?.publishedAt ?? null);
  protected readonly runId = computed(() => this.page()?.runId ?? this.overview()?.sources.find((s) => s.source === this.source())?.runId ?? null);
  protected readonly timeZone = computed(() => this.page()?.timeZone ?? this.overview()?.timeZone ?? 'UTC');

  private view = parseView({});
  private lastPublishedAt: string | null = null;
  private shownKey = '';
  private request: Subscription | null = null;
  /** A page the filter dialog already fetched to check a filter, used instead of asking again for the same view. */
  private prefetched: { key: string; page: DataPage } | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    this.route.queryParams.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.view = parseView(params);
      this.apply();
    });
    this.loadOverview();
  }

  // The address and the overview

  private get projectId(): string {
    return this.context.project()!.id;
  }

  private loadOverview(): void {
    this.service.overview(this.projectId).subscribe({
      next: (overview) => {
        this.overview.set(overview);
        this.error.set(null);
        this.apply();
      },
      error: (failure) => this.error.set(this.errors.parse(failure).message),
    });
  }

  /** Settles what to show from the address and the overview, and asks for the page when it changed. */
  private apply(): void {
    const overview = this.overview();
    if (!overview) {
      return;
    }
    const source = this.view.source && overview.sources.some((s) => s.source === this.view.source) ? this.view.source : overview.defaultSource;
    const tables = overview.sources.find((s) => s.source === source)?.tables ?? [];
    const requested = this.view.table;
    const table = requested && tables.some((t) => t.name === requested) ? requested : (tables[0]?.name ?? null);
    this.goneTable.set(requested && table !== requested ? requested : null);

    const changedTable = source !== this.source() || table !== this.table();
    this.source.set(source);
    this.table.set(table);
    this.query.set(this.view.query);
    if (changedTable) {
      this.page.set(null);
      this.lastPublishedAt = null;
      this.republishedAt.set(null);
      this.dropped.set([]);
    }
    if (!source || !table) {
      this.request?.unsubscribe();
      this.page.set(null);
      this.pageLoading.set(false);
      return;
    }
    this.fetch(source, table, this.view.query);
  }

  private fetch(source: DataSource, table: string, query: DataQuery): void {
    const key = keyOf(source, table, query);
    if (this.prefetched?.key === key) {
      const { page } = this.prefetched;
      this.prefetched = null;
      this.request?.unsubscribe();
      this.received(page, key);
      return;
    }
    if (key === this.shownKey && this.page()) {
      return; // the address changed to what is already on screen
    }
    this.request?.unsubscribe();
    this.pageLoading.set(true);
    this.error.set(null);
    this.request = this.service.query(this.projectId, source, table, query).subscribe({
      next: (page) => this.received(page, key),
      error: (failure) => this.failed(failure, source, table, query),
    });
  }

  private received(page: DataPage, key: string): void {
    if (page.publishedAt && this.lastPublishedAt && page.publishedAt !== this.lastPublishedAt) {
      this.republishedAt.set(page.publishedAt);
    }
    this.lastPublishedAt = page.publishedAt ?? this.lastPublishedAt;
    this.page.set(page);
    this.shownKey = key;
    this.pageLoading.set(false);
  }

  private failed(failure: unknown, source: DataSource, table: string, query: DataQuery): void {
    this.pageLoading.set(false);
    const parsed = this.errors.parse(failure);
    if (parsed.status === 404 && parsed.code === 'data.tableNotFound') {
      // A run replaced the tables: read the list again; the address still names the table, so the page says it is gone.
      this.page.set(null);
      this.shownKey = '';
      this.loadOverview();
      return;
    }
    if (parsed.status === 400 && Object.keys(parsed.fieldErrors).some((field) => field.startsWith('filters') || field === 'sort')) {
      this.recover(source, table, query);
      return;
    }
    this.error.set(parsed.message);
  }

  /**
   * The server refused the filters or sort of the address (a column that is gone, or a value that does not fit). Reads the table
   * without them to learn its columns, drops what does not apply, says what was dropped and goes on with the rest.
   */
  private recover(source: DataSource, table: string, query: DataQuery): void {
    this.pageLoading.set(true);
    this.request?.unsubscribe();
    this.request = this.service.query(this.projectId, source, table, { ...emptyQuery(), pageSize: query.pageSize }).subscribe({
      next: (bare) => {
        const result = sanitize(query, bare.columns);
        // Nothing names a missing column, so the values themselves are what the server refused: drop every filter and the sort.
        const dropped = result.dropped.length
          ? result.dropped
          : [...query.filters.map((f) => `filter:${f.column}`), ...(query.sort ? [`sort:${query.sort.column}`] : [])];
        const cleaned = result.dropped.length ? result.query : { ...query, filters: [], sort: null };
        this.dropped.set(dropped);
        this.go({ query: { ...cleaned, page: 1 } }, true);
      },
      error: (failure) => {
        this.pageLoading.set(false);
        this.error.set(this.errors.parse(failure).message);
      },
    });
  }

  /** Changes the address; the address then drives `apply`. */
  private go(changes: { source?: DataSource | null; table?: string | null; query?: DataQuery }, replace = false): void {
    if (!replace) {
      // A notice about the view as the member found it has done its job once they act on the view.
      this.dropped.set([]);
      this.republishedAt.set(null);
    }
    const source = changes.source !== undefined ? changes.source : this.source();
    const table = changes.table !== undefined ? changes.table : this.table();
    const query = changes.query ?? this.query();
    void this.router.navigate([], { relativeTo: this.route, queryParams: viewParams(source, table, query), replaceUrl: replace });
  }

  // What the member can do

  protected selectSource(source: DataSource): void {
    if (source === this.source()) {
      return;
    }
    const first = this.overview()?.sources.find((s) => s.source === source)?.tables[0]?.name ?? null;
    this.go({ source, table: first, query: { ...emptyQuery(), pageSize: this.query().pageSize } });
  }

  protected selectTable(table: string): void {
    if (table !== this.table()) {
      this.go({ table, query: { ...emptyQuery(), pageSize: this.query().pageSize } });
    }
  }

  protected toggleSort(column: DataColumn): void {
    const current = this.query().sort?.column === column.name ? this.query().sort!.direction : null;
    const sort = current === null ? { column: column.name, direction: 'asc' as const } : current === 'asc' ? { column: column.name, direction: 'desc' as const } : null;
    this.go({ query: { ...this.query(), sort, page: 1 } });
  }

  protected goToPage(page: number): void {
    this.go({ query: { ...this.query(), page: Math.max(1, page) } });
  }

  protected setPageSize(value: string): void {
    const size = Number(value);
    this.go({ query: { ...this.query(), pageSize: size, page: 1 } });
  }

  protected removeFilter(column: string): void {
    this.go({ query: { ...this.query(), filters: this.query().filters.filter((f) => f.column !== column), page: 1 } });
  }

  protected clearFilters(): void {
    this.go({ query: { ...this.query(), filters: [], page: 1 } });
  }

  protected openFilter(column: DataColumn): void {
    const source = this.source();
    const table = this.table();
    if (!source || !table) {
      return;
    }
    const existing = this.query().filters.find((f) => f.column === column.name) ?? null;
    const data: DataFilterDialogData = {
      column,
      filter: existing,
      timeZone: this.timeZone(),
      loadValues: (search) => this.service.values(this.projectId, source, table, column.name, search).pipe(map((result) => result.values)),
      // The page for the new filter is fetched here, so a refused value is shown in the dialog, and used when it is accepted.
      check: (filter) => {
        const next = this.queryWith(filter);
        return this.service.query(this.projectId, source, table, next).pipe(tap((page) => (this.prefetched = { key: keyOf(source, table, next), page })));
      },
    };
    const ref = this.dialog.open<DataFilterDialogResult, DataFilterDialogData>(DataFilterDialog, {
      data,
      ariaLabelledBy: 'data-filter-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
      maxWidth: '100vw',
      maxHeight: '100dvh',
    });
    ref.closed.subscribe((result) => {
      if (!result) {
        this.prefetched = null;
      } else if ('remove' in result) {
        this.prefetched = null;
        this.removeFilter(column.name);
      } else {
        this.go({ query: this.queryWith(result.filter) });
      }
    });
  }

  /** The current view with this filter set for its column, back on the first page. */
  private queryWith(filter: DataFilter): DataQuery {
    const normalized = normalizeFilter(filter);
    const filters = this.query().filters.some((f) => f.column === filter.column)
      ? this.query().filters.map((f) => (f.column === filter.column ? normalized : f))
      : [...this.query().filters, normalized];
    return { ...this.query(), filters, page: 1 };
  }

  protected exportTable(format: ExportFormat): void {
    const source = this.source();
    const table = this.table();
    if (!source || !table || this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.service.export(this.projectId, source, table, format, { filters: this.query().filters, sort: this.query().sort }).subscribe({
      next: (result) => {
        this.exporting.set(false);
        this.downloader.start(result.downloadUrl);
        const ready = this.transloco.translate(this.key('cleanData.export.ready', result.rowCount), { rows: numberText(result.rowCount, this.locale) });
        this.toasts.success(result.cutCells > 0 ? `${ready} ${this.transloco.translate(this.key('cleanData.export.readyCut', result.cutCells), { cut: result.cutCells })}` : ready);
      },
      error: (failure) => {
        this.exporting.set(false);
        this.errors.report(failure);
      },
    });
  }

  // Presentation

  /** The key of a count message: its singular form when `count` is 1. */
  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
  }

  protected columnOf(name: string): DataColumn | undefined {
    return this.page()?.columns.find((c) => c.name === name);
  }

  protected hasFilter(column: string): boolean {
    return this.query().filters.some((f) => f.column === column);
  }

  protected sortState(column: string): 'none' | 'ascending' | 'descending' {
    const sort = this.query().sort;
    return sort?.column !== column ? 'none' : sort.direction === 'asc' ? 'ascending' : 'descending';
  }

  protected ariaSort(column: string): 'ascending' | 'descending' | 'none' {
    return this.sortState(column);
  }

  protected sortArrow(column: string): string {
    const state = this.sortState(column);
    return state === 'ascending' ? '▲' : state === 'descending' ? '▼' : '';
  }

  protected isNumber(column: DataColumn): boolean {
    return column.type === 'integer' || column.type === 'decimal';
  }

  protected isCut(page: DataPage, row: number, column: number): boolean {
    return page.cutCells.some(([r, c]) => r === row && c === column);
  }

  protected rangeStart(page: DataPage): number {
    return (page.page - 1) * page.pageSize + 1;
  }

  protected rangeEnd(page: DataPage): number {
    return Math.min(page.page * page.pageSize, page.total);
  }

  protected shortId(id: string): string {
    return id.slice(0, 8);
  }

  /** True/false in words, timestamps as the local date and time without the offset (the zone is named above the table). */
  protected cellText(value: DataValue, column: DataColumn, t: Translate): string {
    if (typeof value === 'boolean') {
      return t(`cleanData.${value}`);
    }
    if (column.type === 'timestamp' && typeof value === 'string') {
      return value.replace('T', ' ').replace(/(Z|[+-]\d{2}:\d{2})$/, '');
    }
    return String(value);
  }

  /** The full value on hover, which for a timestamp includes the offset. */
  protected cellTitle(value: DataValue): string {
    return String(value);
  }

  protected chipText(filter: DataFilter, t: Translate): string {
    return describeFilter(filter, t);
  }

  protected droppedText(item: string, t: Translate): string {
    const at = item.indexOf(':');
    return t(`cleanData.dropped.${item.slice(0, at)}`, { name: item.slice(at + 1) });
  }
}

function keyOf(source: DataSource, table: string, query: DataQuery): string {
  return JSON.stringify([source, table, query]);
}

