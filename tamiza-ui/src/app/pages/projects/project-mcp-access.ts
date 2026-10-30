import { Component, DestroyRef, LOCALE_ID, computed, effect, inject, linkedSignal, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { McpCall, McpCallStatus, McpDataAccess, McpProjectAccess, McpService, mcpProjectAccessLevels } from '../../core/projects/mcp.service';
import { ProjectsService } from '../../core/projects/projects.service';
import {
  Alert,
  ApiLang,
  Badge,
  Button,
  Card,
  Skeleton,
  Table,
  TableContainer,
  ToastService,
  Tone,
  dateText,
  tableCell,
  tableHead,
  tableHeaderCell,
  tableRow,
} from '../../shared/ui';
import { ProjectContext } from './project-context';

const statusTones: Record<McpCallStatus, Tone> = { succeeded: 'success', failed: 'danger', refused: 'warning' };

/**
 * The project's access for AI clients (Settings > General, shown only when the deployment turned the MCP server on): an admin
 * chooses the level among three options and saves, an analyst reads it, and both see the project's latest calls below it. The
 * level `rows` is offered only while the deployment's maximum allows it; a level stored before the maximum was lowered is shown as
 * it is, with a note that clients are held to summaries. The API judges every change.
 */
@Component({
  selector: 'app-project-mcp-access',
  imports: [TranslocoDirective, Alert, ApiLang, Badge, Button, Card, Skeleton, Table, TableContainer],
  template: `
    <tmz-card *transloco="let t" [heading]="t('settings.mcp.title')" data-testid="mcp-card">
      <p class="mb-4 text-sm text-body">{{ t('settings.mcp.intro') }}</p>

      @if (limited()) {
        <tmz-alert tone="warning" class="mb-4" data-testid="mcp-limited">{{ t('settings.mcp.limited') }}</tmz-alert>
      }

      @if (canChange()) {
        <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); save()" data-testid="mcp-form">
          <fieldset class="flex flex-col gap-2" [attr.aria-describedby]="error() ? 'mcpAccess-error' : null">
            <legend class="mb-2 text-sm font-medium text-heading">{{ t('settings.mcp.access') }}</legend>
            @for (level of levels; track level) {
              <label
                class="flex cursor-pointer items-start gap-3 rounded-base border border-default-medium bg-neutral-secondary-medium p-3 text-sm has-[:checked]:border-brand has-[:checked]:bg-brand-softer has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-70"
                [attr.data-testid]="'mcp-level-' + level"
              >
                <input
                  type="radio"
                  name="mcpAccess"
                  class="mt-0.5 size-4 shrink-0 text-brand focus:ring-brand"
                  [value]="level"
                  [checked]="choice() === level"
                  [disabled]="busy() || blocked(level)"
                  [attr.aria-labelledby]="'mcp-level-' + level + '-title'"
                  [attr.aria-describedby]="'mcp-level-' + level + '-description'"
                  (change)="choice.set(level)"
                />
                <span class="min-w-0">
                  <span class="block font-medium text-heading" [id]="'mcp-level-' + level + '-title'">{{ t('settings.mcp.levels.' + level + '.title') }}</span>
                  <span class="block text-body" [id]="'mcp-level-' + level + '-description'">{{ t('settings.mcp.levels.' + level + '.description') }}</span>
                  @if (blocked(level)) {
                    <span class="mt-1 block font-medium text-fg-warning" data-testid="mcp-rows-blocked">{{ t('settings.mcp.rowsBlocked') }}</span>
                  }
                </span>
              </label>
            }
          </fieldset>
          @if (error(); as message) {
            <p id="mcpAccess-error" class="text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="mcp-error">{{ message }}</p>
          }
          <button tmzButton type="submit" class="sm:self-end" [disabled]="busy() || unchanged()" data-testid="mcp-save">{{ t('common.save') }}</button>
        </form>
      } @else {
        <dl class="grid gap-1 text-sm" data-testid="mcp-readonly">
          <dt class="text-body">{{ t('settings.mcp.access') }}</dt>
          <dd class="font-medium text-heading" data-testid="mcp-level">{{ t('settings.mcp.levels.' + stored() + '.title') }}</dd>
          <dd class="text-body">{{ t('settings.mcp.levels.' + stored() + '.description') }}</dd>
        </dl>
        <p class="mt-2 text-sm text-body">{{ t('settings.mcp.setByAdmin') }}</p>
      }

      <section class="mt-6 border-t border-default pt-5" aria-labelledby="mcp-activity-title" data-testid="mcp-activity">
        <h3 id="mcp-activity-title" class="text-base font-semibold text-heading">{{ t('settings.mcp.activityTitle') }}</h3>
        <p class="mb-3 mt-1 text-sm text-body">{{ t('settings.mcp.activityIntro', { zone: timeZone() }) }}</p>
        @if (calls(); as calls) {
          @if (calls.length === 0) {
            <p class="text-sm text-body" data-testid="mcp-activity-empty">{{ t('settings.mcp.activityEmpty') }}</p>
          } @else {
            <ul class="flex flex-col gap-3 md:hidden" [attr.aria-label]="t('settings.mcp.activityLabel')" data-testid="mcp-call-cards">
              @for (call of calls; track call.id) {
                <li class="rounded-base border border-default bg-neutral-primary-soft p-3 text-sm" data-testid="mcp-call-card">
                  <div class="flex flex-wrap items-center gap-2">
                    <tmz-badge [tone]="tone(call.status)">{{ t('settings.mcp.status.' + call.status) }}</tmz-badge>
                    <code class="min-w-0 break-all font-medium text-heading">{{ call.tool }}</code>
                  </div>
                  <p class="mt-1 break-words text-body">{{ when(call.startedAt) }} · {{ call.userName }}</p>
                  @if (call.access; as access) {
                    <p class="text-xs text-body">{{ t('settings.mcp.levels.' + access + '.title') }}</p>
                  }
                  @if (call.errorCode; as code) {
                    <code class="mt-1 block break-all text-xs text-body">{{ code }}</code>
                  }
                </li>
              }
            </ul>
            <tmz-table-container class="hidden md:block">
              <table tmzTable [attr.aria-label]="t('settings.mcp.activityLabel')" data-testid="mcp-calls">
                <thead [class]="tableHead">
                  <tr>
                    <th scope="col" [class]="tableHeaderCell">{{ t('settings.mcp.columns.when') }}</th>
                    <th scope="col" [class]="tableHeaderCell">{{ t('settings.mcp.columns.person') }}</th>
                    <th scope="col" [class]="tableHeaderCell">{{ t('settings.mcp.columns.tool') }}</th>
                    <th scope="col" [class]="tableHeaderCell">{{ t('settings.mcp.columns.outcome') }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (call of calls; track call.id) {
                    <tr [class]="tableRow" data-testid="mcp-call">
                      <td [class]="tableCell + ' whitespace-nowrap'" data-testid="mcp-call-when">{{ when(call.startedAt) }}</td>
                      <td [class]="tableCell + ' break-words'" data-testid="mcp-call-person">{{ call.userName }}</td>
                      <td [class]="tableCell">
                        <code class="break-all text-heading" data-testid="mcp-call-tool">{{ call.tool }}</code>
                        @if (call.access; as access) {
                          <span class="block text-xs" data-testid="mcp-call-access">{{ t('settings.mcp.levels.' + access + '.title') }}</span>
                        }
                      </td>
                      <td [class]="tableCell">
                        <tmz-badge [tone]="tone(call.status)" data-testid="mcp-call-status">{{ t('settings.mcp.status.' + call.status) }}</tmz-badge>
                        @if (call.errorCode; as code) {
                          <code class="mt-1 block break-all text-xs" data-testid="mcp-call-code">{{ code }}</code>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </tmz-table-container>
          }
        } @else if (failed()) {
          <tmz-alert tone="danger" data-testid="mcp-activity-error">
            <p>{{ t('settings.mcp.activityFailed') }}</p>
            <button tmzButton variant="secondary" size="sm" type="button" class="mt-3" (click)="load()" data-testid="mcp-activity-retry">{{ t('settings.mcp.retry') }}</button>
          </tmz-alert>
        } @else {
          <div aria-busy="true" class="flex flex-col gap-3" data-testid="mcp-activity-loading"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
        }
      </section>
    </tmz-card>
  `,
  host: { class: 'block' },
})
export class ProjectMcpAccess {
  private readonly projects = inject(ProjectsService);
  private readonly mcp = inject(McpService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);
  private readonly context = inject(ProjectContext);

  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;
  protected readonly levels = mcpProjectAccessLevels;

  private readonly project = this.context.project;
  /** The level the project stores. A viewer's details have none, but a viewer never reaches this page. */
  protected readonly stored = computed<McpProjectAccess>(() => this.project()?.mcpAccess ?? 'off');
  /** The most the deployment allows; summaries when the details do not say, which is the safe reading. */
  protected readonly maximum = computed<McpDataAccess>(() => this.project()?.mcpMaxAccess ?? 'aggregates');
  protected readonly canChange = computed(() => this.context.can('admin'));
  /** The stored level asks for more than the deployment allows now, so clients are held to summaries. */
  protected readonly limited = computed(() => this.stored() === 'rows' && this.maximum() === 'aggregates');
  protected readonly timeZone = computed(() => this.project()?.timeZone ?? 'UTC');

  /** The option picked in the form: the stored level until the admin picks another, and again after a save. */
  protected readonly choice = linkedSignal<McpProjectAccess>(() => this.stored());
  protected readonly unchanged = computed(() => this.choice() === this.stored());
  protected readonly busy = signal(false);
  /** The API's message for the level, shown under the options. */
  protected readonly error = signal<string | null>(null);

  protected readonly calls = signal<McpCall[] | null>(null);
  protected readonly failed = signal(false);
  private readonly projectId = computed(() => this.project()?.id ?? null);

  constructor() {
    // The activity is read when the card is shown, and again if the page moves to another project.
    effect(() => {
      if (this.projectId()) {
        untracked(() => this.load());
      }
    });
  }

  /** Whether an option cannot be chosen: `rows` while the deployment allows summaries only. */
  protected blocked(level: McpProjectAccess): boolean {
    return level === 'rows' && this.maximum() === 'aggregates';
  }

  protected tone(status: McpCallStatus): Tone {
    return statusTones[status];
  }

  /** A time of the list in the shared format, in the project's time zone. */
  protected when(value: string): string {
    try {
      return dateText(value, this.locale, 'dateTime', this.timeZone()) ?? value;
    } catch {
      return dateText(value, this.locale) ?? value; // a zone this browser does not know: the viewer's own
    }
  }

  /** Reads the latest calls. Only analysts and admins may; the API answers a viewer 403, so a viewer's page never asks. */
  protected load(): void {
    const id = this.projectId();
    if (!id || !this.context.can('analyst')) {
      return;
    }
    this.failed.set(false);
    this.mcp
      .calls(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (calls) => {
          if (this.projectId() === id) {
            this.calls.set(calls);
          }
        },
        error: () => {
          if (this.projectId() === id) {
            this.calls.set(null);
            this.failed.set(true);
          }
        },
      });
  }

  protected save(): void {
    const id = this.projectId();
    if (!id || this.busy() || this.unchanged()) {
      return;
    }
    this.error.set(null);
    this.busy.set(true);
    this.projects.update(id, { mcpAccess: this.choice() }).subscribe({
      next: (project) => {
        this.context.update(project);
        this.toasts.success(this.transloco.translate('settings.mcp.saved'));
        this.busy.set(false);
      },
      error: (failure) => {
        const parsed = this.errors.report(failure);
        this.error.set(parsed.fieldErrors['mcpAccess'] ?? Object.values(parsed.fieldErrors)[0] ?? null);
        this.busy.set(false);
      },
    });
  }
}
