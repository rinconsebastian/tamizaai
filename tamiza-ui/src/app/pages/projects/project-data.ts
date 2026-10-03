import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import {
  INGESTION_POLL_MS,
  IngestionService,
  IngestionStatus,
  SyncItem,
  SyncPage,
  SyncStatus,
} from '../../core/projects/ingestion.service';
import {
  Badge,
  Button,
  Card,
  ConfirmDialog,
  FormField,
  Input,
  Skeleton,
  Table,
  TableContainer,
  ToastService,
  Tone,
  tableCell,
  tableHead,
  tableHeaderCell,
  tableRow,
} from '../../shared/ui';
import { ProjectContext } from './project-context';

const pageSize = 10;

const statusTones: Record<SyncStatus, Tone> = { queued: 'neutral', running: 'brand', succeeded: 'success', failed: 'danger' };

@Component({
  selector: 'app-project-data',
  imports: [DatePipe, TranslocoDirective, Badge, Button, Card, FormField, Input, Skeleton, Table, TableContainer],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t">
      <div class="grid gap-6 lg:grid-cols-5">
        <tmz-card [heading]="t('data.statusTitle')" class="lg:col-span-3">
          @if (status(); as status) {
            <dl class="grid gap-4 text-sm sm:grid-cols-2" data-testid="ingestion-status">
              <div>
                <dt class="text-body">{{ t('data.submissions') }}</dt>
                <dd class="text-2xl font-semibold text-heading" data-testid="submission-count">{{ status.submissionCount }}</dd>
              </div>
              <div>
                <dt class="text-body">{{ t('data.deleted') }}</dt>
                <dd class="text-2xl font-semibold text-heading" data-testid="deleted-count">{{ status.deletedCount }}</dd>
              </div>
              <div>
                <dt class="text-body">{{ t('data.lastWebhook') }}</dt>
                <dd class="text-heading" data-testid="last-webhook">
                  {{ status.lastWebhookAt ? (status.lastWebhookAt | date: 'medium') : t('data.noWebhook') }}
                </dd>
              </div>
              <div>
                <dt class="text-body">{{ t('data.lastSync') }}</dt>
                <dd class="flex flex-wrap items-center gap-2 text-heading" data-testid="last-sync">
                  @if (status.lastSync; as last) {
                    <tmz-badge [tone]="tone(last.status)">{{ t('data.status.' + last.status) }}</tmz-badge>
                    <span>{{ last.finishedAt | date: 'medium' }}</span>
                  } @else {
                    {{ t('data.never') }}
                  }
                </dd>
              </div>
              <div>
                <dt class="text-body">{{ t('data.nextSync') }}</dt>
                <dd class="text-heading">{{ status.nextSyncAt | date: 'medium' }}</dd>
              </div>
              <div>
                <dt class="text-body">{{ t('data.nextReconcile') }}</dt>
                <dd class="text-heading">{{ status.nextReconcileAt | date: 'medium' }}</dd>
              </div>
            </dl>

            @if (status.activeSync; as active) {
              <p class="mt-4 flex flex-wrap items-center gap-2 text-sm text-body" role="status" data-testid="active-sync">
                <tmz-badge [tone]="tone(active.status)">{{ t('data.status.' + active.status) }}</tmz-badge>
                {{ t('data.activeSync', { kind: t('data.kind.' + active.kind) }) }}
              </p>
            }

            @if (context.can('analyst')) {
              <div class="mt-5 flex flex-col gap-3 border-t border-default pt-5 sm:flex-row sm:flex-wrap">
                <button tmzButton type="button" [disabled]="!!status.activeSync || busy()" (click)="start('incremental')" data-testid="sync-now">
                  {{ t('data.syncNow') }}
                </button>
                <button tmzButton variant="secondary" type="button" [disabled]="!!status.activeSync || busy()" (click)="startFull()" data-testid="full-sync">
                  {{ t('data.fullSync') }}
                </button>
              </div>
            }
          } @else {
            <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-1/2" /><tmz-skeleton class="w-1/3" /></div>
          }
        </tmz-card>

        <tmz-card [heading]="t('data.scheduleTitle')" class="lg:col-span-2">
          @if (status(); as status) {
            @if (context.can('admin')) {
              <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); saveSchedule(syncHours.value, reconcileHours.value)" data-testid="schedule-form">
                <tmz-form-field [label]="t('data.syncInterval')" fieldId="syncIntervalHours" [error]="scheduleErrors()['syncIntervalHours']" [hint]="t('data.intervalHint')">
                  <input tmzInput #syncHours id="syncIntervalHours" type="number" min="1" max="168" step="1" [value]="status.syncIntervalHours" />
                </tmz-form-field>
                <tmz-form-field [label]="t('data.reconcileInterval')" fieldId="reconcileIntervalHours" [error]="scheduleErrors()['reconcileIntervalHours']" [hint]="t('data.reconcileHint')">
                  <input tmzInput #reconcileHours id="reconcileIntervalHours" type="number" min="1" max="168" step="1" [value]="status.reconcileIntervalHours" />
                </tmz-form-field>
                <button tmzButton type="submit" class="sm:self-end" [disabled]="busy()">{{ t('common.save') }}</button>
              </form>
            } @else {
              <dl class="grid gap-3 text-sm" data-testid="schedule-read-only">
                <div><dt class="text-body">{{ t('data.syncInterval') }}</dt><dd class="text-heading">{{ t('data.everyHours', { hours: status.syncIntervalHours }) }}</dd></div>
                <div><dt class="text-body">{{ t('data.reconcileInterval') }}</dt><dd class="text-heading">{{ t('data.everyHours', { hours: status.reconcileIntervalHours }) }}</dd></div>
              </dl>
            }
          }
        </tmz-card>
      </div>

      <section>
        <h2 class="mb-3 text-lg font-semibold text-heading">{{ t('data.logTitle') }}</h2>
        @if (log(); as log) {
          @if (log.items.length === 0) {
            <p class="text-sm text-body" data-testid="log-empty">{{ t('data.logEmpty') }}</p>
          } @else {
            <!-- Phones: stacked cards. md and up: a table. -->
            <ul class="flex flex-col gap-3 md:hidden" data-testid="log-cards">
              @for (sync of log.items; track sync.id) {
                <li class="rounded-base border border-default bg-neutral-primary-soft p-4 text-sm">
                  <div class="mb-2 flex flex-wrap items-center gap-2">
                    <tmz-badge [tone]="tone(sync.status)">{{ t('data.status.' + sync.status) }}</tmz-badge>
                    <span class="font-medium text-heading">{{ t('data.kind.' + sync.kind) }}</span>
                    <span class="text-body">· {{ t('data.trigger.' + sync.trigger) }}</span>
                  </div>
                  <p class="text-body">{{ (sync.startedAt ?? sync.queuedAt) | date: 'medium' }}</p>
                  <p class="text-body">{{ t('data.countsSummary', { created: sync.createdCount, updated: sync.updatedCount, deleted: sync.deletedCount }) }}</p>
                  @if (sync.requestedBy) {
                    <p class="text-body">{{ t('data.requestedByName', { name: sync.requestedBy.name }) }}</p>
                  }
                  @if (sync.status === 'failed') {
                    <p class="mt-2 text-fg-danger-strong break-words">{{ errorText(sync) }}</p>
                  }
                </li>
              }
            </ul>
            <tmz-table-container class="hidden md:block">
              <table tmzTable data-testid="log-table">
                <thead [class]="tableHead">
                  <tr>
                    <th scope="col" [class]="tableHeaderCell">{{ t('data.columns.started') }}</th>
                    <th scope="col" [class]="tableHeaderCell">{{ t('data.columns.kind') }}</th>
                    <th scope="col" [class]="tableHeaderCell">{{ t('data.columns.status') }}</th>
                    <th scope="col" [class]="tableHeaderCell + ' text-right'">{{ t('data.columns.created') }}</th>
                    <th scope="col" [class]="tableHeaderCell + ' text-right'">{{ t('data.columns.updated') }}</th>
                    <th scope="col" [class]="tableHeaderCell + ' text-right'">{{ t('data.columns.deleted') }}</th>
                    <th scope="col" [class]="tableHeaderCell">{{ t('data.columns.requestedBy') }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (sync of log.items; track sync.id) {
                    <tr [class]="tableRow" [attr.data-testid]="'log-row-' + sync.status">
                      <td [class]="tableCell + ' whitespace-nowrap'">{{ (sync.startedAt ?? sync.queuedAt) | date: 'medium' }}</td>
                      <td [class]="tableCell">
                        <span class="text-heading">{{ t('data.kind.' + sync.kind) }}</span>
                        <span class="block text-xs">{{ t('data.trigger.' + sync.trigger) }}</span>
                      </td>
                      <td [class]="tableCell">
                        <tmz-badge [tone]="tone(sync.status)">{{ t('data.status.' + sync.status) }}</tmz-badge>
                        @if (sync.status === 'failed') {
                          <span class="mt-1 block max-w-md text-xs text-fg-danger-strong break-words" data-testid="log-error">{{ errorText(sync) }}</span>
                        }
                      </td>
                      <td [class]="tableCell + ' text-right'">{{ sync.createdCount }}</td>
                      <td [class]="tableCell + ' text-right'">{{ sync.updatedCount }}</td>
                      <td [class]="tableCell + ' text-right'">{{ sync.deletedCount }}</td>
                      <td [class]="tableCell">{{ sync.requestedBy?.name ?? '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </tmz-table-container>
            @if (log.total > pageSize) {
              <nav class="mt-3 flex flex-wrap items-center justify-between gap-3 text-sm text-body" [attr.aria-label]="t('data.logTitle')">
                <span data-testid="log-page-info">{{ t('frame.pageInfo', { from: pageFrom(), to: pageTo(), total: log.total }) }}</span>
                <div class="flex gap-2">
                  <button tmzButton variant="secondary" size="sm" type="button" [disabled]="page() === 1" (click)="loadLog(page() - 1)" data-testid="log-previous">
                    {{ t('frame.previous') }}
                  </button>
                  <button tmzButton variant="secondary" size="sm" type="button" [disabled]="pageTo() >= log.total" (click)="loadLog(page() + 1)" data-testid="log-next">
                    {{ t('frame.next') }}
                  </button>
                </div>
              </nav>
            }
          }
        } @else {
          <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-full" /></div>
        }
      </section>
    </div>
  `,
})
export class ProjectData {
  private readonly service = inject(IngestionService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly pollMs = inject(INGESTION_POLL_MS);
  protected readonly context = inject(ProjectContext);

  protected readonly pageSize = pageSize;
  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  protected readonly status = signal<IngestionStatus | null>(null);
  protected readonly log = signal<SyncPage | null>(null);
  protected readonly page = signal(1);
  protected readonly busy = signal(false);
  protected readonly scheduleErrors = signal<Record<string, string>>({});
  protected readonly pageFrom = computed(() => (this.page() - 1) * pageSize + 1);
  protected readonly pageTo = computed(() => Math.min(this.page() * pageSize, this.log()?.total ?? 0));

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  constructor() {
    this.refreshStatus();
    this.loadLog(1);

    // While a run is queued or running, refresh until it ends, then reload the log.
    effect(() => {
      const active = !!this.status()?.activeSync;
      this.clearPoll();
      if (active && !this.destroyed) {
        this.pollTimer = setTimeout(() => this.refreshStatus(), this.pollMs);
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.clearPoll();
    });
  }

  protected tone(status: SyncStatus): Tone {
    return statusTones[status];
  }

  /** The translated message for the run's error code, or the API's message when there is no translation. */
  protected errorText(sync: SyncItem): string {
    if (sync.errorCode) {
      const key = `errors.codes.${sync.errorCode}`;
      const translated = this.transloco.translate(key);
      if (translated && translated !== key) {
        return translated;
      }
    }
    return sync.error ?? this.transloco.translate('errors.generic');
  }

  protected start(kind: 'incremental' | 'full'): void {
    this.busy.set(true);
    this.service.startSync(this.projectId(), kind).subscribe({
      next: (sync) => {
        this.busy.set(false);
        this.toasts.success(this.transloco.translate(kind === 'full' ? 'data.fullSyncQueued' : 'data.syncQueued'));
        this.status.update((status) => (status ? { ...status, activeSync: sync } : status));
        this.loadLog(1);
      },
      error: (error) => {
        this.busy.set(false);
        this.errors.report(error);
        this.refreshStatus();
      },
    });
  }

  protected async startFull(): Promise<void> {
    const t = (key: string) => this.transloco.translate(key);
    const confirmed = await this.confirmDialog.confirm({
      title: t('data.fullSyncTitle'),
      message: t('data.fullSyncMessage'),
      confirmLabel: t('data.fullSync'),
      cancelLabel: t('common.cancel'),
    });
    if (confirmed) {
      this.start('full');
    }
  }

  protected saveSchedule(syncHours: string, reconcileHours: string): void {
    this.scheduleErrors.set({});
    this.busy.set(true);
    this.service
      .updateSettings(this.projectId(), { syncIntervalHours: Number(syncHours), reconcileIntervalHours: Number(reconcileHours) })
      .subscribe({
        next: (status) => {
          this.busy.set(false);
          this.status.set(status);
          this.toasts.success(this.transloco.translate('data.scheduleSaved'));
        },
        error: (error) => {
          this.busy.set(false);
          this.scheduleErrors.set(this.errors.report(error).fieldErrors);
        },
      });
  }

  protected loadLog(page: number): void {
    this.service.syncs(this.projectId(), page, pageSize).subscribe({
      next: (log) => {
        this.page.set(page);
        this.log.set(log);
      },
      error: (error) => this.errors.report(error),
    });
  }

  private refreshStatus(): void {
    const wasActive = !!this.status()?.activeSync;
    this.service.status(this.projectId()).subscribe({
      next: (status) => {
        if (this.destroyed) {
          return;
        }
        this.status.set(status);
        if (wasActive && !status.activeSync) {
          this.loadLog(1);
        }
      },
      error: (error) => this.errors.report(error),
    });
  }

  private clearPoll(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private projectId(): string {
    return this.context.project()!.id;
  }
}
