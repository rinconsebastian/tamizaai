import { DatePipe } from '@angular/common';
import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import { PipelineService, SCRIPT_TEST_POLL_MS, SampleDownload } from '../../core/projects/pipeline.service';
import { Alert, Button, Card } from '../../shared/ui';

const megabyte = 1024 * 1024;

/** "812 KB" or "12.3 MB". */
export function sizeText(bytes: number): string {
  return bytes < megabyte ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / megabyte).toFixed(1)} MB`;
}

/**
 * A sample download: its status while the worker prepares it, then what the zip holds and the button that downloads it.
 * The link behind the button works once; the button asks for a new one every time.
 */
@Component({
  selector: 'app-sample-download-panel',
  imports: [DatePipe, TranslocoDirective, Alert, Button, Card],
  template: `
    <tmz-card *transloco="let t" [heading]="t('sampleDownloads.title')" data-testid="sample-download">
      @if (expired()) {
        <tmz-alert tone="warning" data-testid="download-expired">{{ t('sampleDownloads.expired') }}</tmz-alert>
        <button tmzButton variant="secondary" type="button" class="mt-3" (click)="again.emit(size())" data-testid="download-again">
          {{ t('sampleDownloads.again') }}
        </button>
      } @else if (download(); as download) {
        @switch (download.status) {
          @case ('failed') {
            <tmz-alert tone="danger" data-testid="download-error">{{ download.error ?? t('errors.generic') }}</tmz-alert>
            <button tmzButton variant="secondary" type="button" class="mt-3" (click)="again.emit(download.sampleSize)" data-testid="download-again">
              {{ t('sampleDownloads.again') }}
            </button>
          }
          @case ('succeeded') {
            @if (download.contents; as contents) {
              <div class="flex flex-col gap-3" data-testid="download-ready">
                <p class="text-sm text-body">
                  {{ t('sampleDownloads.ready', { submissions: contents.sample.submissions.toLocaleString('en'), requested: contents.sample.requested.toLocaleString('en') }) }}
                </p>
                <ul class="flex flex-col gap-1 text-sm text-body" data-testid="download-tables">
                  <li>
                    <span class="font-medium text-heading">{{ t('sampleDownloads.rawTables') }}</span>
                    {{ tables(contents.raw.tables) }}
                  </li>
                  @switch (contents.clean.status) {
                    @case ('succeeded') {
                      <li>
                        <span class="font-medium text-heading">{{ t('sampleDownloads.cleanTables', { version: contents.clean.version }) }}</span>
                        {{ tables(contents.clean.tables) }}
                      </li>
                    }
                    @case ('none') {
                      <li>{{ t('sampleDownloads.noScript1') }}</li>
                    }
                  }
                  <li>
                    <span class="font-medium text-heading">{{ t('sampleDownloads.referenceTables') }}</span>
                    {{ contents.reference.length ? tables(contents.reference) : t('sampleDownloads.noReference') }}
                  </li>
                </ul>
                @if (contents.clean.status === 'failed') {
                  <tmz-alert tone="warning" data-testid="download-script1-warning">
                    {{ t('sampleDownloads.script1Failed', { version: contents.clean.version ?? '—', error: contents.clean.error ?? '' }) }}
                  </tmz-alert>
                }
                <p class="text-sm text-body" data-testid="download-size">
                  {{ t('sampleDownloads.size', { size: sizeLabel(download) }) }}
                  @if (download.expiresAt) {
                    {{ t('sampleDownloads.until', { time: (download.expiresAt | date: 'shortTime') }) }}
                  }
                </p>
                <div>
                  <button tmzButton type="button" [disabled]="linking()" (click)="save()" data-testid="download-button">
                    {{ t('sampleDownloads.download') }}
                  </button>
                </div>
                @if (linkError(); as message) {
                  <tmz-alert tone="danger" data-testid="download-link-error">{{ message }}</tmz-alert>
                }
                <p class="text-sm text-body" data-testid="download-notice">{{ t('sampleDownloads.notice') }}</p>
              </div>
            }
          }
          @default {
            <p class="text-sm text-body" role="status" data-testid="download-progress">{{ progress(download) }}</p>
          }
        }
      }
    </tmz-card>
  `,
})
export class SampleDownloadPanel {
  /** The download as the API returned it when it was queued; the panel refreshes it while it is active. */
  readonly started = input.required<SampleDownload>();
  readonly projectId = input.required<string>();
  /** Asks to prepare another download of this many submissions, after one failed or expired. */
  readonly again = output<number>();

  private readonly service = inject(PipelineService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  private readonly downloader = inject(Downloader);
  private readonly pollMs = inject(SCRIPT_TEST_POLL_MS);

  protected readonly download = signal<SampleDownload | null>(null);
  /** The download is gone: an hour passed, or a newer request replaced it. */
  protected readonly expired = signal(false);
  protected readonly linking = signal(false);
  protected readonly linkError = signal<string | null>(null);

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  constructor() {
    effect(() => {
      this.download.set(this.started());
      this.expired.set(false);
      this.linkError.set(null);
    });
    // While the download is queued or running, refresh it until it ends.
    effect(() => {
      const download = this.download();
      this.clearPoll();
      if (download && (download.status === 'queued' || download.status === 'running') && !this.destroyed) {
        this.pollTimer = setTimeout(() => this.refresh(download.id), this.pollMs);
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.clearPoll();
    });
  }

  protected size(): number {
    return this.download()?.sampleSize ?? this.started().sampleSize;
  }

  protected sizeLabel(download: SampleDownload): string {
    return sizeText(download.sizeBytes ?? 0);
  }

  protected progress(download: SampleDownload): string {
    const t = (key: string) => this.transloco.translate(key);
    if (download.status === 'running') {
      return t('sampleDownloads.running');
    }
    switch (download.waitingFor) {
      case 'run':
        return t('sampleDownloads.waitingForRun');
      case 'test':
        return t('sampleDownloads.waitingForTest');
      case 'download':
        return t('sampleDownloads.waitingForDownload');
      default:
        return t('sampleDownloads.starting');
    }
  }

  /** "main (320 rows), members (871 rows)". */
  protected tables(tables: { name: string; rows: number }[]): string {
    return tables.map((table) => this.transloco.translate('sampleDownloads.tableRows', { name: table.name, rows: table.rows.toLocaleString('en') })).join(', ');
  }

  /** Asks for a new single-use link and follows it; a download that is gone says so instead of failing. */
  protected save(): void {
    const download = this.download();
    if (!download || this.linking()) {
      return;
    }
    this.linking.set(true);
    this.linkError.set(null);
    this.service.sampleDownloadLink(this.projectId(), download.id).subscribe({
      next: (link) => {
        this.linking.set(false);
        this.downloader.start(link.downloadUrl);
      },
      error: (error) => {
        this.linking.set(false);
        const parsed = this.errors.parse(error);
        if (parsed.status === 404) {
          this.expired.set(true);
        } else {
          this.linkError.set(parsed.message);
        }
      },
    });
  }

  private refresh(downloadId: string): void {
    this.service.sampleDownload(this.projectId(), downloadId).subscribe({
      next: (download) => {
        if (!this.destroyed && this.download()?.id === download.id) {
          this.download.set(download);
        }
      },
      error: (error) => {
        if (this.destroyed) {
          return;
        }
        if (this.errors.parse(error).status === 404) {
          this.expired.set(true);
        } else {
          this.errors.report(error);
        }
      },
    });
  }

  private clearPoll(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
