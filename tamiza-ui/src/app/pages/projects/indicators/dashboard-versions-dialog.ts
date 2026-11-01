import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ApiErrors } from '../../../core/api-error';
import { DashboardChange, DashboardVersionList, DashboardVersionSummary, IndicatorsService } from '../../../core/projects/indicators.service';
import { DashboardEntry } from '../../../core/projects/projects.service';
import { Alert, ApiLang, Badge, Button, DateTimePipe, Skeleton } from '../../../shared/ui';

export interface DashboardVersionsData {
  projectId: string;
  dashboard: DashboardEntry;
}

/**
 * The version to restore: its number, the name it holds, which the message after the restore names when another dashboard has it
 * (design D11), and the time it was saved, which the confirmation names.
 */
export type VersionToRestore = Pick<DashboardVersionSummary, 'number' | 'name' | 'savedAt'>;

/** What the member chose: to view a version, by its number, or to restore one, or `gone` when the dashboard no longer exists. */
export type DashboardVersionsResult = { view: number } | { restore: VersionToRestore } | { gone: true };

/** The versions a dashboard keeps, the current one included: a rule of the product (`DashboardVersions.Kept`, design D6). */
export const keptVersions = 10;

/** The changes a version lists before "and N more" (design D11). */
const shownChanges = 3;

/**
 * A dashboard's version history (add-dashboard-versions, design D11): the versions it keeps, newest first, each with what changed,
 * who made it and when, "via MCP" and the request for one an AI client made, and the current one marked. Every version but the
 * current one can be viewed or restored, which the page does once the dialog has closed with the choice. Names and titles are
 * member text, shown as text. A dashboard removed meanwhile closes it with `gone`.
 */
@Component({
  selector: 'app-dashboard-versions-dialog',
  imports: [DateTimePipe, TranslocoDirective, Alert, ApiLang, Badge, Button, Skeleton],
  template: `
    <!-- Long names, titles and requests wrap rather than widen the dialog on a phone. -->
    <div
      class="flex max-h-[calc(100dvh-2rem)] w-[min(40rem,calc(100vw-2rem))] flex-col rounded-base border border-default bg-neutral-primary-soft shadow-sm [overflow-wrap:anywhere]"
      *transloco="let t"
      data-testid="dashboard-versions-dialog"
    >
      <header class="flex items-start justify-between gap-3 border-b border-default p-4 md:px-6">
        <div class="min-w-0">
          <h2 id="dashboard-versions-title" class="text-lg font-medium text-heading">{{ t('dashboards.versions.title', { name: data.dashboard.name }) }}</h2>
          <p class="mt-1 text-sm text-body">{{ t('dashboards.versions.intro', { count: kept }) }}</p>
        </div>
        <button tmzButton variant="ghost" size="sm" type="button" class="shrink-0" (click)="ref.close()" data-testid="dashboard-versions-close">
          {{ t('dashboards.versions.close') }}
        </button>
      </header>
      <div class="overflow-y-auto p-4 md:px-6" data-testid="dashboard-versions-body">
        @if (failure(); as message) {
          <tmz-alert tone="danger" data-testid="dashboard-versions-error"><span [tmzApiLang]="message">{{ message }}</span></tmz-alert>
        } @else if (list(); as list) {
          @if (list.versions.length) {
            <ol class="flex flex-col divide-y divide-default" data-testid="dashboard-versions">
              @for (version of list.versions; track version.number) {
                <li class="flex min-w-0 flex-col gap-2 py-3 first:pt-0 last:pb-0" [attr.data-number]="version.number" data-testid="dashboard-version">
                  <div class="flex flex-wrap items-center gap-2">
                    <h3 class="font-medium text-heading" data-testid="dashboard-version-number">{{ t('dashboards.versions.number', { number: version.number }) }}</h3>
                    @if (version.current) {
                      <tmz-badge tone="success" data-testid="dashboard-version-current">{{ t('dashboards.versions.current') }}</tmz-badge>
                    }
                  </div>
                  @if (version.origin === 'mcp' && version.note) {
                    <p class="text-sm text-heading" data-testid="dashboard-version-note">{{ t('dashboards.versions.note', { note: version.note }) }}</p>
                  }
                  <ul class="flex flex-col gap-0.5 text-sm text-heading" data-testid="dashboard-version-changes">
                    @for (change of shown(version); track $index) {
                      <li data-testid="dashboard-version-change">{{ t('dashboards.versions.changes.' + change.kind, params(change)) }}</li>
                    }
                    @if (more(version); as count) {
                      <li class="text-body" data-testid="dashboard-version-more">{{ t('dashboards.versions.more', { count }) }}</li>
                    }
                  </ul>
                  <p class="text-sm text-body" data-testid="dashboard-version-byline">
                    {{ version.author.name }} · {{ version.savedAt | dateTime }}@if (version.origin === 'mcp') {
                      · <span data-testid="dashboard-version-via-mcp">{{ t('dashboards.versions.viaMcp') }}</span>
                    }
                  </p>
                  @if (!version.current) {
                    <!-- Each names its version for screen readers, which read every version's actions in a row. -->
                    <div class="flex flex-wrap gap-2" data-testid="dashboard-version-actions">
                      <button
                        tmzButton
                        variant="secondary"
                        size="sm"
                        type="button"
                        [attr.aria-label]="t('dashboards.versions.viewLabel', { number: version.number })"
                        (click)="ref.close({ view: version.number })"
                        data-testid="dashboard-version-view"
                      >
                        {{ t('dashboards.versions.view') }}
                      </button>
                      <button
                        tmzButton
                        variant="secondary"
                        size="sm"
                        type="button"
                        [attr.aria-label]="t('dashboards.versions.restoreLabel', { number: version.number })"
                        (click)="ref.close({ restore: { number: version.number, name: version.name, savedAt: version.savedAt } })"
                        data-testid="dashboard-version-restore"
                      >
                        {{ t('dashboards.versions.restore') }}
                      </button>
                    </div>
                  }
                </li>
              }
            </ol>
          } @else {
            <p class="text-sm text-body" data-testid="dashboard-versions-empty">{{ t('dashboards.versions.empty') }}</p>
          }
        } @else {
          <div aria-busy="true" class="flex flex-col gap-3" data-testid="dashboard-versions-loading">
            <span class="sr-only">{{ t('dashboards.versions.loading') }}</span>
            <tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /><tmz-skeleton class="w-full" />
          </div>
        }
      </div>
    </div>
  `,
})
export class DashboardVersionsDialog {
  protected readonly data = inject<DashboardVersionsData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DashboardVersionsResult>>(DialogRef);
  private readonly service = inject(IndicatorsService);
  private readonly errors = inject(ApiErrors);

  protected readonly kept = keptVersions;
  /** The versions as the API lists them, newest first; null while they are read. */
  protected readonly list = signal<DashboardVersionList | null>(null);
  /** The versions could not be read: the dialog says why, with nothing to choose. */
  protected readonly failure = signal<string | null>(null);

  constructor() {
    this.service.listVersions(this.data.projectId, this.data.dashboard.id).subscribe({
      next: (list) => this.list.set(list),
      error: (failure) => {
        const parsed = this.errors.parse(failure);
        if (parsed.code === 'dashboards.notFound') {
          this.ref.close({ gone: true });
          return;
        }
        this.failure.set(parsed.message);
      },
    });
  }

  /** The first changes of a version, in the order they were made. */
  protected shown(version: DashboardVersionSummary): DashboardChange[] {
    return version.changes.slice(0, shownChanges);
  }

  /** How many changes the version holds besides those shown: the ones listed after them and the ones the API left out. */
  protected more(version: DashboardVersionSummary): number {
    return Math.max(version.changes.length - shownChanges, 0) + version.moreChanges;
  }

  /** What a change's text names: the widget's title and the other dashboard's name as they were, and the version a restore brought back. */
  protected params(change: DashboardChange): Record<string, unknown> {
    return { widget: change.widget ?? '', dashboard: change.dashboard ?? '', number: change.version ?? '' };
  }
}
