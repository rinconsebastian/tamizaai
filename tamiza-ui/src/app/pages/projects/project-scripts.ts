import { Dialog } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import {
  FixedScriptKind,
  IndicatorMetadata,
  IndicatorScript,
  PipelineService,
  ScriptList,
  ScriptRef,
  ScriptTest,
  ScriptVersionDetails,
  ScriptVersionSummary,
  defaultSampleSize,
  fixedScriptKinds,
  maxIndicators,
  maxSampleSize,
  minSampleSize,
  scriptTemplates,
} from '../../core/projects/pipeline.service';
import { Alert, Badge, Button, Card, ConfirmDialog, FormField, Input, Skeleton, ToastService } from '../../shared/ui';
import { CodeEditor } from '../../shared/ui/code-editor';
import { AddIndicatorData, AddIndicatorDialog, NewIndicator } from './add-indicator-dialog';
import { IndicatorMetadataFields } from './indicator-metadata-fields';
import { PipelineRuns } from './pipeline-runs';
import { PipelineSettingsCard } from './pipeline-settings';
import { ProjectContext } from './project-context';
import { ScriptTestPanel } from './script-test-panel';

const maxCodeBytes = 200 * 1024;

const emptyMetadata: IndicatorMetadata = { label: '', description: '', unit: '', format: 'decimal' };

function sameScript(a: ScriptRef, b: ScriptRef): boolean {
  return a.kind === b.kind && (a.kind !== 'indicator' || (b.kind === 'indicator' && a.name === b.name));
}

/** The metadata a version was saved with; blank for script 1 and script 2 and for a removal. */
function metadataOf(version: ScriptVersionDetails): IndicatorMetadata {
  return { label: version.label ?? '', description: version.description ?? '', unit: version.unit ?? '', format: version.format ?? 'decimal' };
}

function sameMetadata(a: IndicatorMetadata, b: IndicatorMetadata): boolean {
  return a.label === b.label && a.description === b.description && a.unit === b.unit && a.format === b.format;
}

@Component({
  selector: 'app-project-scripts',
  imports: [
    DatePipe,
    TranslocoDirective,
    Alert,
    Badge,
    Button,
    Card,
    FormField,
    Input,
    Skeleton,
    CodeEditor,
    IndicatorMetadataFields,
    PipelineRuns,
    PipelineSettingsCard,
    ScriptTestPanel,
  ],
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
                [variant]="kind === selected().kind ? 'primary' : 'secondary'"
                [attr.aria-pressed]="kind === selected().kind"
                (click)="select({ kind })"
                [attr.data-testid]="'script-' + kind"
              >
                {{ t('scripts.kinds.' + kind) }}
                <tmz-badge [tone]="badgeTone(kind)" class="ms-1">{{ badgeText(kind) }}</tmz-badge>
              </button>
            }
          </nav>

          <section class="mb-4" aria-labelledby="indicators-heading">
            <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 id="indicators-heading" class="text-sm font-medium text-heading">
                {{ t('scripts.indicators') }}
                <tmz-badge class="ms-1" data-testid="indicator-count">{{ indicators().length }}</tmz-badge>
              </h2>
              @if (canEdit()) {
                <button
                  tmzButton
                  variant="secondary"
                  size="sm"
                  type="button"
                  [disabled]="busy() || indicators().length >= maxIndicators"
                  (click)="addIndicator()"
                  data-testid="add-indicator"
                >
                  {{ t('scripts.addIndicator') }}
                </button>
              }
            </div>
            @if (canEdit() && indicators().length >= maxIndicators) {
              <p class="mb-2 text-sm text-fg-warning" data-testid="indicator-limit">{{ t('scripts.indicatorLimit', { max: maxIndicators }) }}</p>
            }
            @if (indicators().length === 0 && !newIndicator()) {
              <p class="text-sm text-body" data-testid="no-indicators">{{ canEdit() ? t('scripts.noIndicatorsEditor') : t('scripts.noIndicators') }}</p>
            } @else {
              <ul class="flex max-h-56 flex-col gap-1 overflow-y-auto" data-testid="indicator-list">
                @if (newIndicator(); as added) {
                  <li>
                    <button type="button" class="flex w-full items-center gap-2 rounded-base border border-brand px-3 py-2 text-left text-sm" aria-pressed="true" [attr.data-testid]="'indicator-' + added">
                      <span class="min-w-0 flex-1">
                        <span class="block truncate font-medium text-heading">{{ meta().label }}</span>
                        <code class="text-xs text-body">{{ added }}</code>
                      </span>
                      <tmz-badge tone="brand">{{ t('scripts.newBadge') }}</tmz-badge>
                    </button>
                  </li>
                }
                @for (indicator of indicators(); track indicator.name) {
                  <li>
                    <button
                      type="button"
                      class="flex w-full items-center gap-2 rounded-base border border-default px-3 py-2 text-left text-sm hover:bg-neutral-secondary-medium"
                      [class.border-brand]="indicatorName() === indicator.name"
                      [attr.aria-pressed]="indicatorName() === indicator.name"
                      (click)="select({ kind: 'indicator', name: indicator.name })"
                      [attr.data-testid]="'indicator-' + indicator.name"
                    >
                      <span class="min-w-0 flex-1">
                        <span class="block truncate font-medium text-heading">{{ indicator.label }}</span>
                        <code class="text-xs text-body">{{ indicator.name }}</code>
                      </span>
                      <tmz-badge tone="success">{{ t('scripts.versionShort', { number: indicator.current.number }) }}</tmz-badge>
                    </button>
                  </li>
                }
              </ul>
            }
          </section>

          <p class="mb-3 text-sm text-body">{{ t('scripts.contracts.' + selected().kind) }}</p>

          @if (scripts() === null) {
            <div aria-busy="true" class="flex flex-col gap-3"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
          } @else {
            @if (viewing(); as version) {
              <tmz-alert tone="brand" class="mb-3" data-testid="viewing-version">
                {{ t('scripts.viewingVersion', { number: version.number, author: version.author.name }) }}
              </tmz-alert>
            }
            @if (indicatorName(); as name) {
              <div class="mb-3" data-testid="indicator-meta">
                <p class="mb-3 text-sm text-body">
                  {{ t('scripts.fields.name') }}: <code class="text-heading" data-testid="indicator-meta-name">{{ name }}</code>
                </p>
                <app-indicator-metadata-fields
                  [value]="shownMetadata()"
                  [readonly]="!canEdit() || !!viewing()"
                  [errors]="fieldErrors()"
                  (valueChange)="editMetadata($event)"
                />
              </div>
            }
            <tmz-code-editor
              [value]="editorValue()"
              [readonly]="!canEdit() || !!viewing()"
              [label]="indicatorName() ? t('scripts.editorLabelIndicator', { name: indicatorName() }) : t('scripts.editorLabel', { kind: t('scripts.kinds.' + selected().kind) })"
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

            @if (canEdit() && !viewing()) {
              <form class="mt-4 flex flex-col gap-3 border-t border-default pt-4 sm:flex-row sm:items-end" novalidate (submit)="$event.preventDefault(); startTest()">
                <tmz-form-field class="sm:w-48" [label]="t('scriptTests.sampleSize')" fieldId="sample-size" [error]="testError()">
                  <input
                    tmzInput
                    id="sample-size"
                    type="number"
                    inputmode="numeric"
                    [min]="minSampleSize"
                    [max]="maxSampleSize"
                    [value]="sampleSize()"
                    (input)="sampleSize.set($any($event.target).value)"
                    data-testid="sample-size"
                  />
                </tmz-form-field>
                <button tmzButton variant="secondary" type="submit" [disabled]="testing() || !draft().trim()" data-testid="test-on-sample">
                  {{ t('scriptTests.start') }}
                </button>
              </form>
              <p class="mt-2 text-sm text-body">{{ t('scriptTests.hint') }}</p>
            }
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

      @if (test(); as test) {
        <app-script-test-panel [started]="test" [projectId]="projectId()" [draft]="draft()" />
      }

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
  private readonly dialog = inject(Dialog);
  private readonly context = inject(ProjectContext);
  private readonly editor = viewChild(CodeEditor);

  protected readonly kinds = fixedScriptKinds;
  protected readonly maxIndicators = maxIndicators;
  protected readonly scripts = signal<ScriptList | null>(null);
  protected readonly selected = signal<ScriptRef>({ kind: 'clean' });
  /** The name of an indicator added in the dialog whose script is not saved yet; it is the selected script while set. */
  protected readonly newIndicator = signal<string | null>(null);
  protected readonly versions = signal<ScriptVersionSummary[]>([]);
  protected readonly viewing = signal<ScriptVersionDetails | null>(null);
  protected readonly draft = signal('');
  /** The metadata of the selected indicator as edited above the editor. */
  protected readonly meta = signal<IndicatorMetadata>(emptyMetadata);
  protected readonly busy = signal(false);
  protected readonly saveError = signal<string | null>(null);
  /** The API's messages for the label, description, unit and format fields. */
  protected readonly fieldErrors = signal<Record<string, string>>({});
  protected readonly minSampleSize = minSampleSize;
  protected readonly maxSampleSize = maxSampleSize;
  protected readonly sampleSize = signal<number | string>(defaultSampleSize);
  protected readonly testing = signal(false);
  protected readonly testError = signal<string | null>(null);
  /** The latest test started from this page; the panel follows it until it ends. */
  protected readonly test = signal<ScriptTest | null>(null);

  protected readonly canEdit = computed(() => this.context.can('analyst'));
  protected readonly indicators = computed(() => this.scripts()?.indicators ?? []);
  /** The selected indicator's name, or null while script 1 or script 2 is selected. */
  protected readonly indicatorName = computed(() => {
    const selected = this.selected();
    return selected.kind === 'indicator' ? selected.name : null;
  });
  protected readonly current = computed(() => {
    const selected = this.selected();
    return selected.kind === 'indicator'
      ? (this.indicators().find((i) => i.name === selected.name)?.current ?? null)
      : (this.scripts()?.[selected.kind].current ?? null);
  });
  protected readonly savedCode = computed(() => this.current()?.code ?? '');
  private readonly savedMetadata = computed(() => {
    const current = this.current();
    return current && !this.newIndicator() ? metadataOf(current) : emptyMetadata;
  });
  private readonly metadataDirty = computed(
    () => this.indicatorName() !== null && !this.newIndicator() && !sameMetadata(this.meta(), this.savedMetadata()),
  );
  /** A script that was never saved is always unsaved, even before its first edit. */
  protected readonly dirty = computed(
    () => !this.viewing() && (!!this.newIndicator() || this.draft() !== this.savedCode() || this.metadataDirty()),
  );
  /** What the editor starts from: a version being viewed, the template of a new indicator, or the saved code. Edits flow back through `onEdit`. */
  protected readonly editorValue = computed(() =>
    this.viewing() ? (this.viewing()!.code ?? '') : this.newIndicator() ? scriptTemplates.indicator : this.savedCode(),
  );
  protected readonly shownMetadata = computed(() => (this.viewing() ? metadataOf(this.viewing()!) : this.meta()));

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

  protected editMetadata(metadata: IndicatorMetadata): void {
    if (!this.viewing()) {
      this.meta.set(metadata);
    }
  }

  protected async select(script: ScriptRef): Promise<void> {
    if ((sameScript(script, this.selected()) && !this.newIndicator()) || !(await this.confirmDiscard())) {
      return;
    }
    this.newIndicator.set(null);
    this.selected.set(script);
    this.resetDraft();
    this.loadVersions();
  }

  protected badgeTone(kind: FixedScriptKind) {
    const current = this.scripts()?.[kind].current;
    return !current ? 'neutral' : current.removed ? 'warning' : 'success';
  }

  protected badgeText(kind: FixedScriptKind): string {
    const current = this.scripts()?.[kind].current;
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    return !current ? t('scripts.notSet') : current.removed ? t('scripts.removedBadge') : t('scripts.versionShort', { number: current.number });
  }

  /** Asks for the name and metadata of a new indicator; its script is created by the first save. */
  protected async addIndicator(): Promise<void> {
    if (!(await this.confirmDiscard())) {
      return;
    }
    const ref = this.dialog.open<NewIndicator, AddIndicatorData>(AddIndicatorDialog, {
      data: { existing: this.indicators().map((i) => i.name) },
      ariaLabelledBy: 'add-indicator-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
    ref.closed.subscribe((added) => added && this.startIndicator(added));
  }

  private startIndicator({ name, ...metadata }: NewIndicator): void {
    this.newIndicator.set(name);
    this.selected.set({ kind: 'indicator', name });
    this.versions.set([]);
    this.viewing.set(null);
    this.saveError.set(null);
    this.fieldErrors.set({});
    this.meta.set(metadata);
    this.draft.set(scriptTemplates.indicator);
    // Explicit, because the editor's value input does not change when the previous script had the template as its code.
    this.editor()?.replace(scriptTemplates.indicator);
  }

  protected save(): void {
    const script = this.selected();
    const code = this.draft();
    this.saveError.set(null);
    this.fieldErrors.set({});
    if (new TextEncoder().encode(code).length > maxCodeBytes) {
      this.saveError.set(this.transloco.translate('scripts.tooLarge'));
      return;
    }
    const meta = this.meta();
    const body =
      script.kind === 'indicator'
        ? { code, ...meta, label: meta.label.trim(), description: meta.description.trim(), unit: meta.unit.trim() }
        : { code };
    this.busy.set(true);
    this.service.save(this.projectId(), script, body).subscribe({
      next: (version) => this.afterChange(version, 'scripts.saved'),
      error: (error) => {
        this.busy.set(false);
        const parsed = this.errors.parse(error);
        // The 100-indicator limit comes under the name.
        const { code, name, ...fields } = parsed.fieldErrors;
        this.fieldErrors.set(fields);
        this.saveError.set(code ?? name ?? (Object.keys(fields).length ? null : parsed.message));
      },
    });
  }

  protected async remove(): Promise<void> {
    const t = (key: string) => this.transloco.translate(key);
    const confirmed = await this.confirmDialog.confirm({
      title: t('scripts.removeTitle'),
      message: t('scripts.removeMessage.' + this.selected().kind),
      confirmLabel: t('scripts.remove'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (!confirmed) {
      return;
    }
    this.busy.set(true);
    this.service.remove(this.projectId(), this.selected()).subscribe({
      next: (version) => this.afterChange(version, this.selected().kind === 'indicator' ? 'scripts.removedIndicator' : 'scripts.removed'),
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

  /** Tests the editor's text, saved or not, on a sample of the project's submissions; an indicator is tested under its name. */
  protected startTest(): void {
    const size = Number(this.sampleSize());
    this.testError.set(null);
    if (!Number.isInteger(size) || size < minSampleSize || size > maxSampleSize) {
      this.testError.set(this.transloco.translate('scriptTests.sampleSizeRange', { min: minSampleSize, max: maxSampleSize.toLocaleString('en') }));
      return;
    }
    const code = this.draft();
    if (new TextEncoder().encode(code).length > maxCodeBytes) {
      this.testError.set(this.transloco.translate('scripts.tooLarge'));
      return;
    }
    this.testing.set(true);
    this.service.startTest(this.projectId(), this.selected(), code, size).subscribe({
      next: (test) => {
        this.testing.set(false);
        this.test.set(test);
      },
      error: (error) => {
        this.testing.set(false);
        const parsed = this.errors.parse(error);
        this.testError.set(parsed.fieldErrors['sampleSize'] ?? parsed.fieldErrors['code'] ?? parsed.fieldErrors['name'] ?? parsed.message);
      },
    });
  }

  protected insertTemplate(): void {
    this.editor()?.replace(scriptTemplates[this.selected().kind]);
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
    this.newIndicator.set(null);
    this.scripts.update((scripts) => this.withVersion(scripts!, version));
    if (version.kind === 'indicator' && version.removed) {
      this.selected.set({ kind: 'clean' });
    }
    this.resetDraft();
    this.loadVersions();
    this.toasts.success(this.transloco.translate(messageKey, { number: version.number }));
  }

  /** The list with the script's new current version: script 1 or 2 replaced, or an indicator added, replaced or, when removed, dropped. */
  private withVersion(scripts: ScriptList, version: ScriptVersionDetails): ScriptList {
    if (version.kind !== 'indicator') {
      return { ...scripts, [version.kind]: { kind: version.kind, current: version } };
    }
    const others = scripts.indicators.filter((i) => i.name !== version.name);
    if (version.removed) {
      return { ...scripts, indicators: others };
    }
    const entry: IndicatorScript = {
      name: version.name!,
      label: version.label!,
      description: version.description,
      unit: version.unit,
      format: version.format!,
      current: version,
    };
    return { ...scripts, indicators: [...others, entry].sort((a, b) => (a.name < b.name ? -1 : 1)) };
  }

  private fail(error: unknown): void {
    this.busy.set(false);
    this.errors.report(error);
  }

  /** Shows the selected script's saved code and metadata, dropping edits (callers ask first when there are any). */
  protected resetDraft(): void {
    this.viewing.set(null);
    this.saveError.set(null);
    this.fieldErrors.set({});
    this.draft.set(this.savedCode());
    this.meta.set(this.savedMetadata());
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
    const script = this.selected();
    this.service.versions(this.projectId(), script).subscribe({
      next: (versions) => {
        if (sameScript(script, this.selected())) {
          this.versions.set(versions);
        }
      },
      error: (error) => this.errors.report(error),
    });
  }

  protected projectId(): string {
    return this.context.project()!.id;
  }
}
