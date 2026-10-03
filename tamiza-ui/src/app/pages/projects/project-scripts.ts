import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import {
  PipelineService,
  ProjectScript,
  ScriptKind,
  ScriptVersionDetails,
  ScriptVersionSummary,
  scriptKinds,
  scriptTemplates,
} from '../../core/projects/pipeline.service';
import { Alert, Badge, Button, Card, ConfirmDialog, Skeleton, ToastService } from '../../shared/ui';
import { CodeEditor } from '../../shared/ui/code-editor';
import { PipelineRuns } from './pipeline-runs';
import { PipelineSettingsCard } from './pipeline-settings';
import { ProjectContext } from './project-context';

const maxCodeBytes = 200 * 1024;

@Component({
  selector: 'app-project-scripts',
  imports: [DatePipe, TranslocoDirective, Alert, Badge, Button, Card, Skeleton, CodeEditor, PipelineRuns, PipelineSettingsCard],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t">
      <div class="grid gap-6 lg:grid-cols-4">
        <tmz-card class="min-w-0 lg:col-span-3">
          <nav class="mb-4 flex flex-wrap gap-2" [attr.aria-label]="t('scripts.title')">
            @for (kind of kinds; track kind) {
              <button
                tmzButton
                type="button"
                size="sm"
                [variant]="kind === selected() ? 'primary' : 'secondary'"
                [attr.aria-pressed]="kind === selected()"
                (click)="select(kind)"
                [attr.data-testid]="'script-' + kind"
              >
                {{ t('scripts.kinds.' + kind) }}
                <tmz-badge [tone]="badgeTone(kind)" class="ms-1">{{ badgeText(kind) }}</tmz-badge>
              </button>
            }
          </nav>
          <p class="mb-3 text-sm text-body">{{ t('scripts.contracts.' + selected()) }}</p>

          @if (scripts() === null) {
            <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
          } @else {
            @if (viewing(); as version) {
              <tmz-alert tone="brand" class="mb-3" data-testid="viewing-version">
                {{ t('scripts.viewingVersion', { number: version.number, author: version.author.name }) }}
              </tmz-alert>
            }
            <tmz-code-editor
              [value]="editorValue()"
              [readonly]="!canEdit() || !!viewing()"
              [label]="t('scripts.editorLabel', { kind: t('scripts.kinds.' + selected()) })"
              (changed)="onEdit($event)"
            />
            @if (saveError(); as message) {
              <p class="mt-2 text-sm text-fg-danger-strong" data-testid="save-error">{{ message }}</p>
            }

            <div class="mt-4 flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center">
              @if (viewing(); as version) {
                <button tmzButton variant="secondary" type="button" (click)="resetDraft()" data-testid="back-to-current">
                  {{ t('scripts.backToCurrent') }}
                </button>
                @if (canEdit() && !version.removed) {
                  <button tmzButton type="button" [disabled]="busy()" (click)="restore(version)" data-testid="restore">
                    {{ t('scripts.restore', { number: version.number }) }}
                  </button>
                }
              } @else if (canEdit()) {
                <button tmzButton type="button" [disabled]="busy() || !dirty()" (click)="save()" data-testid="save">{{ t('scripts.save') }}</button>
                <button tmzButton variant="secondary" type="button" (click)="insertTemplate()" data-testid="template">
                  {{ t('scripts.template') }}
                </button>
                <label tmzButton variant="secondary" class="cursor-pointer">
                  {{ t('scripts.upload') }}
                  <input type="file" accept=".py,text/x-python,text/plain" class="sr-only" (change)="upload($event)" data-testid="upload" />
                </label>
                @if (current() && !current()!.removed) {
                  <button tmzButton variant="danger" type="button" [disabled]="busy()" (click)="remove()" data-testid="remove">
                    {{ t('scripts.remove') }}
                  </button>
                }
                @if (dirty()) {
                  <span class="text-sm text-body" data-testid="unsaved">{{ t('scripts.unsaved') }}</span>
                }
              }
            </div>
          }
        </tmz-card>

        <tmz-card [heading]="t('scripts.versionsTitle')" class="min-w-0">
          @if (versions().length === 0) {
            <p class="text-sm text-body">{{ t('scripts.noVersions') }}</p>
          } @else {
            <ol class="flex max-h-[28rem] flex-col gap-2 overflow-y-auto" data-testid="versions">
              @for (version of versions(); track version.versionId) {
                <li>
                  <button
                    type="button"
                    class="w-full rounded-base border border-default px-3 py-2 text-left text-sm hover:bg-neutral-secondary-medium"
                    [class.border-brand]="viewing()?.number === version.number"
                    (click)="view(version)"
                    [attr.data-testid]="'version-' + version.number"
                  >
                    <span class="font-medium text-heading">{{ t('scripts.versionNumber', { number: version.number }) }}</span>
                    @if (version.removed) {
                      <tmz-badge tone="warning" class="ms-1">{{ t('scripts.removedBadge') }}</tmz-badge>
                    }
                    <span class="block text-body">{{ version.author.name }} · {{ version.createdAt | date: 'medium' }}</span>
                  </button>
                </li>
              }
            </ol>
          }
        </tmz-card>
      </div>

      <app-pipeline-settings />
      <app-pipeline-runs />
    </div>
  `,
})
export class ProjectScripts {
  private readonly service = inject(PipelineService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly context = inject(ProjectContext);
  private readonly editor = viewChild(CodeEditor);

  protected readonly kinds = scriptKinds;
  protected readonly scripts = signal<ProjectScript[] | null>(null);
  protected readonly selected = signal<ScriptKind>('clean');
  protected readonly versions = signal<ScriptVersionSummary[]>([]);
  protected readonly viewing = signal<ScriptVersionDetails | null>(null);
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly saveError = signal<string | null>(null);

  protected readonly canEdit = computed(() => this.context.can('analyst'));
  protected readonly current = computed(() => this.scripts()?.find((s) => s.kind === this.selected())?.current ?? null);
  protected readonly savedCode = computed(() => this.current()?.code ?? '');
  protected readonly dirty = computed(() => !this.viewing() && this.draft() !== this.savedCode());
  /** What the editor starts from: a version being viewed, or the saved code. Edits flow back through `onEdit`. */
  protected readonly editorValue = computed(() => (this.viewing() ? (this.viewing()!.code ?? '') : this.savedCode()));

  constructor() {
    this.load();
  }

  /** Asks before discarding unsaved edits; used when switching scripts and by the route guard. */
  async confirmDiscard(): Promise<boolean> {
    if (!this.dirty()) {
      return true;
    }
    const t = (key: string) => this.transloco.translate(key);
    return this.confirmDialog.confirm({
      title: t('scripts.discardTitle'),
      message: t('scripts.discardMessage'),
      confirmLabel: t('scripts.discard'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
  }

  protected onEdit(text: string): void {
    if (!this.viewing()) {
      this.draft.set(text);
    }
  }

  protected async select(kind: ScriptKind): Promise<void> {
    if (kind === this.selected() || !(await this.confirmDiscard())) {
      return;
    }
    this.selected.set(kind);
    this.resetDraft();
    this.loadVersions();
  }

  protected badgeTone(kind: ScriptKind) {
    const current = this.scripts()?.find((s) => s.kind === kind)?.current;
    return !current ? 'neutral' : current.removed ? 'warning' : 'success';
  }

  protected badgeText(kind: ScriptKind): string {
    const current = this.scripts()?.find((s) => s.kind === kind)?.current;
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    return !current ? t('scripts.notSet') : current.removed ? t('scripts.removedBadge') : t('scripts.versionShort', { number: current.number });
  }

  protected save(): void {
    const code = this.draft();
    this.saveError.set(null);
    if (new TextEncoder().encode(code).length > maxCodeBytes) {
      this.saveError.set(this.transloco.translate('scripts.tooLarge'));
      return;
    }
    this.busy.set(true);
    this.service.save(this.projectId(), this.selected(), code).subscribe({
      next: (version) => this.afterChange(version, 'scripts.saved'),
      error: (error) => {
        this.busy.set(false);
        const parsed = this.errors.parse(error);
        this.saveError.set(parsed.fieldErrors['code'] ?? parsed.message);
      },
    });
  }

  protected async remove(): Promise<void> {
    const t = (key: string) => this.transloco.translate(key);
    const confirmed = await this.confirmDialog.confirm({
      title: t('scripts.removeTitle'),
      message: t('scripts.removeMessage.' + this.selected()),
      confirmLabel: t('scripts.remove'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (!confirmed) {
      return;
    }
    this.busy.set(true);
    this.service.remove(this.projectId(), this.selected()).subscribe({
      next: (version) => this.afterChange(version, 'scripts.removed'),
      error: (error) => this.fail(error),
    });
  }

  protected restore(version: ScriptVersionDetails): void {
    this.busy.set(true);
    this.service.restore(this.projectId(), this.selected(), version.number).subscribe({
      next: (restored) => this.afterChange(restored, 'scripts.restored'),
      error: (error) => this.fail(error),
    });
  }

  protected async view(summary: ScriptVersionSummary): Promise<void> {
    if (summary.number === this.current()?.number && !summary.removed) {
      if (this.viewing()) {
        this.resetDraft();
      }
      return;
    }
    if (!(await this.confirmDiscard())) {
      return;
    }
    this.service.version(this.projectId(), this.selected(), summary.number).subscribe({
      next: (version) => this.viewing.set(version),
      error: (error) => this.errors.report(error),
    });
  }

  protected insertTemplate(): void {
    this.editor()?.replace(scriptTemplates[this.selected()]);
  }

  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    if (file.size > maxCodeBytes) {
      this.saveError.set(this.transloco.translate('scripts.tooLarge'));
      return;
    }
    file.text().then((text) => this.editor()?.replace(text));
  }

  private afterChange(version: ScriptVersionDetails, messageKey: string): void {
    this.busy.set(false);
    this.viewing.set(null);
    this.scripts.update((scripts) => (scripts ?? []).map((s) => (s.kind === version.kind ? { ...s, current: version } : s)));
    this.resetDraft();
    this.loadVersions();
    this.toasts.success(this.transloco.translate(messageKey, { number: version.number }));
  }

  private fail(error: unknown): void {
    this.busy.set(false);
    this.errors.report(error);
  }

  /** Shows the selected script's saved code, dropping edits (callers ask first when there are any). */
  protected resetDraft(): void {
    this.viewing.set(null);
    this.saveError.set(null);
    this.draft.set(this.savedCode());
    // Explicit, because the editor's value input does not change when two scripts have the same saved code.
    this.editor()?.replace(this.savedCode());
  }

  private load(): void {
    this.service.scripts(this.projectId()).subscribe({
      next: (scripts) => {
        this.scripts.set(scripts);
        this.resetDraft();
        this.loadVersions();
      },
      error: (error) => this.errors.report(error),
    });
  }

  private loadVersions(): void {
    const kind = this.selected();
    this.service.versions(this.projectId(), kind).subscribe({
      next: (versions) => {
        if (kind === this.selected()) {
          this.versions.set(versions);
        }
      },
      error: (error) => this.errors.report(error),
    });
  }

  private projectId(): string {
    return this.context.project()!.id;
  }
}
