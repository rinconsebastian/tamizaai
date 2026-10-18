import { Component, DestroyRef, LOCALE_ID, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { Downloader } from '../../core/download.service';
import { countKey } from '../../core/i18n/plural';
import { PipelineService, SCRIPT_TEST_POLL_MS, SampleDownload } from '../../core/projects/pipeline.service';
import { aroundDetail, detailMark } from './import-frame-dialog';
import { Alert, ApiLang, Button, Card, DateTimePipe, NumberPipe, numberText } from '../../shared/ui';

const megabyte = 1024 * 1024;

/** "812 KB" or "12.3 MB", with the number written for the locale. */
export function sizeText(bytes: number, locale: string): string {
  return bytes < megabyte
    ? `${numberText(Math.max(1, Math.round(bytes / 1024)), locale)} KB`
    : `${numberText(bytes / megabyte, locale, '1.1-1')} MB`;
}

/**
 * A sample download: its status while the worker prepares it, then what the zip holds and the button that downloads it.
 * The link behind the button works once; the button asks for a new one every time.
 */
@Component({
  selector: 'app-sample-download-panel',
  imports: [DateTimePipe, NumberPipe, TranslocoDirective, Alert, ApiLang, Button, Card],
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
            <tmz-alert tone="danger" [attr.lang]="download.error ? 'en' : null" data-testid="download-error">{{ download.error ?? t('errors.generic') }}</tmz-alert>
            <button tmzButton variant="secondary" type="button" class="mt-3" (click)="again.emit(download.sampleSize)" data-testid="download-again">
              {{ t('sampleDownloads.again') }}
            </button>
          }
          @case ('succeeded') {
            @if (download.contents; as contents) {
              <div class="flex flex-col gap-3" data-testid="download-ready">
                <div class="flex flex-wrap items-center justify-between gap-3">
                  <p class="text-sm text-body" data-testid="download-summary">
                    {{ t(key('sampleDownloads.ready', contents.sample.submissions), { submissions: (contents.sample.submissions | num), requested: (contents.sample.requested | num) }) }}
                    <span data-testid="download-size">
                      {{ t('sampleDownloads.size', { size: sizeLabel(download) }) }}
                      @if (download.expiresAt) {
                        {{ t('sampleDownloads.until', { time: (download.expiresAt | dateTime: 'time') }) }}
                      }
                    </span>
                  </p>
                  <button tmzButton type="button" [disabled]="linking()" (click)="save()" data-testid="download-button">
                    {{ t('sampleDownloads.download') }}
                  </button>
                </div>
                <p class="text-sm text-body" data-testid="download-tables">{{ tablesLine(contents, t) }}</p>
                @if (contents.clean.status === 'failed') {
                  <tmz-alert tone="warning" data-testid="download-script1-warning">
                    @let failed = script1Failed(contents);
                    {{ failed.before }}<span lang="en">{{ contents.clean.error ?? '' }}</span>{{ failed.after }}
                  </tmz-alert>
                }
                @if (linkError(); as message) {
                  <tmz-alert tone="danger" [tmzApiLang]="message" data-testid="download-link-error">{{ message }}</tmz-alert>
                }
                <p class="text-xs text-body" data-testid="download-notice">{{ t('sampleDownloads.notice') }}</p>
              </div>
            }
          }
          @default {
            <p class="text-sm text-body" role="status" data-testid="download-progress">{{ progress(download) }}</p>
            <p class="mt-1 text-xs text-body" data-testid="download-explanation">{{ t('sampleDownloads.hint') }}</p>
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
  private readonly locale = inject(LOCALE_ID);
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
    return sizeText(download.sizeBytes ?? 0, this.locale);
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

  /** The tables of the zip on one line: "Flattened: main (320 rows) · Clean v3: main (300 rows) · Reference: population (1,122 rows)". */
  protected tablesLine(contents: NonNullable<SampleDownload['contents']>, t: (key: string, params?: Record<string, unknown>) => string): string {
    const parts = [t('sampleDownloads.rawLine', { tables: this.tables(contents.raw.tables) })];
    if (contents.clean.status === 'succeeded') {
      parts.push(t('sampleDownloads.cleanLine', { version: contents.clean.version, tables: this.tables(contents.clean.tables) }));
    } else if (contents.clean.status === 'none') {
      parts.push(t('sampleDownloads.noScript1'));
    }
    parts.push(contents.reference.length ? t('sampleDownloads.referenceLine', { tables: this.tables(contents.reference) }) : t('sampleDownloads.noReferenceLine'));
    return parts.join(' · ');
  }

  /** The warning about a failed script 1 cut around the worker's error, which is English and sits between the two parts. */
  protected script1Failed(contents: NonNullable<SampleDownload['contents']>): { before: string; after: string } {
    return aroundDetail(this.transloco.translate('sampleDownloads.script1Failed', { version: contents.clean.version ?? '—', error: detailMark }));
  }

  /** The key of a message about a count: the singular form when the count is 1. */
  protected key(base: string, count: number): string {
    return countKey(base, count, this.transloco.getActiveLang());
  }

  /** "main (320 rows), members (871 rows)". */
  protected tables(tables: { name: string; rows: number }[]): string {
    return tables.map((table) => this.transloco.translate(this.key('sampleDownloads.tableRows', table.rows), { name: table.name, rows: numberText(table.rows, this.locale) })).join(', ');
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
