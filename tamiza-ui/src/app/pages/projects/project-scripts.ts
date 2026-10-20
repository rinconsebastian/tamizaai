import { Dialog } from '@angular/cdk/dialog';
import { Component, ElementRef, LOCALE_ID, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { SystemConfigService } from '../../core/system-config';
import {
  FixedScriptKind,
  IndicatorMetadata,
  IndicatorScript,
  PipelineService,
  RunStatus,
  RunSummary,
  SampleDownload,
  ScriptInputs,
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
} from '../../core/projects/pipeline.service';
import { Alert, ApiLang, Badge, Button, Card, ConfirmDialog, DropdownMenu, FormField, Input, MenuItem, Skeleton, ToastService, Tone, DateTimePipe, numberText } from '../../shared/ui';
import { CodeEditor } from '../../shared/ui/code-editor';
import { AddIndicatorData, AddIndicatorDialog, NewIndicator } from './add-indicator-dialog';
import { IndicatorMetadataFields } from './indicator-metadata-fields';
import { PipelineRuns } from './pipeline-runs';
import { valueFormatter } from './indicators/format';
import { ProjectContext } from './project-context';
import { SampleDownloadPanel } from './sample-download-panel';
import { ScriptTestPanel } from './script-test-panel';
import { IndicatorMenu, IndicatorMenuItem } from './script-editor/indicator-menu';
import { ResolvedProblem, scriptAssist } from './script-editor/script-assist';
import { codeEditorPhrases } from '../../core/i18n/code-editor-phrases';
import { ASSISTANT_SESSION, ApplyRequest, FixTarget, cellReplacement, findCell } from './script-editor/assistant-state';
import { AssistantSession } from './script-editor/assistant-session';
import { askedWarning, viewWarnings } from './script-editor/indicator-view';
import { ScriptInputsInfo, signatureOf } from './script-editor/script-analysis';
import { ScriptSidePanel } from './script-editor/script-side-panel';
import { TemplateMenu, TemplateOption } from './script-editor/template-menu';
import { newIndicatorTemplate, scriptTemplates } from './script-templates/templates';

const maxCodeBytes = 200 * 1024;

const emptyMetadata: IndicatorMetadata = { label: '', description: '', unit: '', format: 'decimal' };

const runTones: Record<RunStatus, Tone> = { queued: 'neutral', running: 'brand', succeeded: 'success', failed: 'danger' };

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
    DateTimePipe,
    RouterLink,
    TranslocoDirective,
    Alert,
    ApiLang,
    Badge,
    Button,
    Card,
    DropdownMenu,
    FormField,
    Input,
    Skeleton,
    CodeEditor,
    IndicatorMenu,
    IndicatorMetadataFields,
    PipelineRuns,
    SampleDownloadPanel,
    ScriptSidePanel,
    ScriptTestPanel,
    TemplateMenu,
  ],
  // The Assistant's state outlives its tab: one session per Scripts page, which also ends its request when the page is left.
  providers: [AssistantSession, { provide: ASSISTANT_SESSION, useExisting: AssistantSession }],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t">
      <div class="grid gap-6 lg:grid-cols-4">
        <div class="flex min-w-0 flex-col gap-6 lg:col-span-3">
          <tmz-card class="min-w-0">
            <!-- One picker: script 1, script 2, and a menu of the indicators. -->
            <div class="mb-3 flex flex-wrap items-center gap-2" role="group" [attr.aria-label]="t('scripts.title')">
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
                  @if (kind === selected().kind) {
                    <tmz-badge [tone]="badgeTone(kind)" class="ms-1">{{ badgeText(kind) }}</tmz-badge>
                  }
                </button>
              }
              <app-indicator-menu [items]="indicatorItems()" [selected]="indicatorName()" (chosen)="select({ kind: 'indicator', name: $event })" />
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
              <p class="mb-3 text-sm text-fg-warning" data-testid="indicator-limit">{{ t('scripts.indicatorLimit', { max: maxIndicators }) }}</p>
            }

            <!-- The last run and the way to start another; the whole list is below. -->
            <div class="mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-default pb-4 text-sm text-body" data-testid="last-run">
              @if (latestRun(); as run) {
                <tmz-badge [tone]="runTone(run.status)" data-testid="last-run-status">{{ t('runs.status.' + run.status) }}</tmz-badge>
                <span data-testid="last-run-time">{{ (run.startedAt ?? run.queuedAt) | dateTime }}</span>
                @if (failedStep(run, t); as failed) {
                  <span class="text-fg-danger-strong" data-testid="last-run-failed">{{ failed }}</span>
                }
              } @else if (runsLoaded()) {
                <span data-testid="last-run-none">{{ t('runs.noneYet') }}</span>
              }
              <a [routerLink]="[]" fragment="runs" class="text-fg-brand hover:underline" data-testid="last-run-link">{{ t('runs.title') }}</a>
              @if (canEdit()) {
                <button tmzButton size="sm" type="button" class="ms-auto" [disabled]="runs()?.busy()" (click)="runNow()" data-testid="run-now">{{ t('runs.runNow') }}</button>
              }
            </div>

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
                  @if (canEdit() && !viewing()) {
                    <!-- Set when the indicator was added and rarely changed: a line, with the fields on request. -->
                    <div class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm" data-testid="indicator-meta-summary">
                      <span class="break-words font-medium text-heading" data-testid="indicator-meta-label">{{ shownMetadata().label || name }}</span>
                      <code class="text-xs text-body" data-testid="indicator-meta-name">{{ name }}</code>
                      <span class="text-body">· {{ t('scripts.formats.' + shownMetadata().format) }}{{ shownMetadata().unit ? ' · ' + shownMetadata().unit : '' }}</span>
                      <button
                        tmzButton
                        variant="ghost"
                        size="sm"
                        type="button"
                        [attr.aria-expanded]="metaShown()"
                        aria-controls="indicatorMetaFields"
                        (click)="metaOpen.set(!metaOpen())"
                        data-testid="edit-details"
                      >
                        {{ t('scripts.editDetails') }}
                      </button>
                    </div>
                    @if (metaShown()) {
                      <app-indicator-metadata-fields
                        id="indicatorMetaFields"
                        class="mt-3 block"
                        [value]="shownMetadata()"
                        [errors]="fieldErrors()"
                        (valueChange)="editMetadata($event)"
                      />
                    }
                  } @else {
                    <dl class="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-2" data-testid="indicator-meta-readonly">
                      <div><dt class="text-body">{{ t('scripts.fields.label') }}</dt><dd class="break-words font-medium text-heading">{{ shownMetadata().label || name }}</dd></div>
                      <div><dt class="text-body">{{ t('scripts.fields.name') }}</dt><dd><code class="text-heading" data-testid="indicator-meta-name">{{ name }}</code></dd></div>
                      <div><dt class="text-body">{{ t('scripts.fields.format') }}</dt><dd class="text-heading">{{ t('scripts.formats.' + shownMetadata().format) }}{{ shownMetadata().unit ? ' · ' + shownMetadata().unit : '' }}</dd></div>
                      @if (shownMetadata().description) {
                        <div class="sm:col-span-2"><dt class="text-body">{{ t('scripts.fields.description') }}</dt><dd class="break-words text-heading">{{ shownMetadata().description }}</dd></div>
                      }
                    </dl>
                  }
                </div>
              }
              <div class="mb-1 flex flex-wrap items-center gap-2 text-xs text-body">
                <span>{{ indicatorName() ? t('scripts.editorLabelIndicator', { name: indicatorName() }) : t('scripts.editorLabel', { kind: t('scripts.kinds.' + selected().kind) }) }}</span>
                <code class="rounded-sm bg-neutral-secondary-soft px-1.5 py-0.5 text-heading" data-testid="contract-signature">{{ signature() }}</code>
              </div>
              <tmz-code-editor
                [value]="editorValue()"
                [readonly]="!canEdit() || !!viewing()"
                [label]="indicatorName() ? t('scripts.editorLabelIndicator', { name: indicatorName() }) : t('scripts.editorLabel', { kind: t('scripts.kinds.' + selected().kind) })"
                [extensions]="assist()"
                [phrases]="phrases"
                (changed)="onEdit($event)"
              />
              <p class="mt-1 text-xs text-body" data-testid="editor-keys">
                {{ t('scripts.editor.keys') }}
                @if (canEdit() && !viewing()) {
                  {{ t('scripts.editor.keysRun') }}
                }
              </p>
              @if (problems().length) {
                <section class="mt-3" aria-labelledby="problems-title" data-testid="problems">
                  <h3 id="problems-title" class="text-sm font-medium text-heading">
                    {{ t('scripts.editor.problemsTitle') }}
                    <tmz-badge class="ms-1" data-testid="problem-count">{{ problems().length }}</tmz-badge>
                  </h3>
                  <ul class="mt-1 flex max-h-40 flex-col gap-1 overflow-y-auto">
                    @for (problem of problems(); track problem.from + ':' + problem.message) {
                      <li class="flex flex-wrap items-baseline gap-x-3">
                        <button type="button" class="flex items-baseline gap-2 text-left text-sm text-body hover:underline" (click)="goToProblem(problem)" data-testid="problem">
                          <tmz-badge [tone]="problem.severity === 'error' ? 'danger' : 'warning'">{{ t('scripts.editor.problemLine', { line: problem.line }) }}</tmz-badge>
                          <span class="min-w-0">{{ problem.message }}</span>
                        </button>
                        @if (referenceLink() && isReferenceProblem(problem)) {
                          <a [routerLink]="referenceLink()" class="text-xs text-fg-brand hover:underline" data-testid="problem-reference-link">{{ t('scripts.inputs.manage') }}</a>
                        }
                      </li>
                    }
                  </ul>
                  @if (canEdit()) {
                    <p class="mt-1 text-xs text-body" data-testid="problems-hint">{{ t('scripts.editor.problemsHint') }}</p>
                  }
                </section>
              }
              @if (saveError(); as message) {
                <p class="mt-2 text-sm text-fg-danger-strong" [tmzApiLang]="message" data-testid="save-error">{{ message }}</p>
              }

              @if (viewing(); as version) {
                <div class="mt-4 flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center">
                  <button tmzButton variant="secondary" type="button" (click)="resetDraft()" data-testid="back-to-current">
                    {{ t('scripts.backToCurrent') }}
                  </button>
                  @if (canEdit() && !version.removed) {
                    <button tmzButton type="button" [disabled]="busy()" (click)="restore(version)" data-testid="restore">
                      {{ t('scripts.restore', { number: version.number }) }}
                    </button>
                  }
                </div>
              } @else if (canEdit()) {
                <!-- Save, test and download share one row and one sample size; the rarer actions wait in the menus. -->
                <form class="mt-4 flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-start" novalidate (submit)="$event.preventDefault(); startTest()">
                  <button tmzButton type="button" class="sm:mt-7" [disabled]="busy() || !dirty()" (click)="save()" data-testid="save">{{ t('scripts.save') }}</button>
                  <tmz-form-field class="sm:w-36" [label]="t('scriptTests.sampleSize')" fieldId="sample-size" [error]="testError()">
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
                  <button tmzButton variant="secondary" type="submit" class="sm:mt-7" [disabled]="testing() || !draft().trim()" data-testid="test-on-sample">
                    {{ t('scriptTests.start') }}
                  </button>
                  <button tmzButton variant="secondary" type="button" class="sm:mt-7" [disabled]="downloading()" (click)="startDownload()" data-testid="download-sample">
                    {{ t('sampleDownloads.start') }}
                  </button>
                  <app-template-menu class="sm:mt-7" [label]="t('scripts.template')" [options]="templateOptions()" (chosen)="chooseTemplate($event)" />
                  <tmz-dropdown-menu class="sm:mt-7" [label]="t('scripts.moreActions')" [items]="moreItems()" (selected)="onMore($event)" />
                  <input #uploadInput type="file" accept=".py,text/x-python,text/plain" class="sr-only" tabindex="-1" aria-hidden="true" (change)="upload($event)" data-testid="upload" />
                  @if (dirty()) {
                    <span class="text-sm text-body sm:mt-8" data-testid="unsaved">{{ t('scripts.unsaved') }}</span>
                  }
                </form>
                <p class="mt-1 text-xs text-body" data-testid="test-hint">{{ t('scriptTests.hint') }}</p>
              }
            }
          </tmz-card>

          @if (test(); as test) {
            <app-script-test-panel
              #testPanel
              [started]="test"
              [projectId]="projectId()"
              [draft]="draft()"
              [metadata]="indicatorName() ? shownMetadata() : null"
              [canAsk]="canAsk()"
              (changed)="latestTest.set($event)"
              (ask)="askAssistant($event)"
            />
          }

          @if (sampleDownload(); as sample) {
            <app-sample-download-panel [started]="sample" [projectId]="projectId()" (again)="requestDownload($event)" />
          }
        </div>

        <div class="min-w-0">
          <app-script-side-panel
            [kind]="selected().kind"
            [inputs]="inputs()"
            [failed]="inputsFailed()"
            [canInsert]="canEdit() && !viewing()"
            [manageLink]="referenceLink()"
            [versions]="versions()"
            [viewingNumber]="viewing()?.number ?? null"
            [canEdit]="canEdit()"
            [projectId]="projectId()"
            [scriptName]="indicatorName()"
            [draft]="draft()"
            [cursorLine]="cursorLine()"
            [details]="indicatorName() ? meta() : null"
            (pick)="insertName($event)"
            (refresh)="reloadInputs()"
            (view)="view($event)"
            (apply)="applyProposal($event)"
          />
        </div>
      </div>

      <app-pipeline-runs (runEnded)="reloadInputs()" (latest)="onLatestRun($event)" />
    </div>
  `,
})
export class ProjectScripts {
  private readonly service = inject(PipelineService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly dialog = inject(Dialog);
  private readonly context = inject(ProjectContext);
  private readonly assistant = inject(AssistantSession);
  private readonly editor = viewChild(CodeEditor);
  private readonly uploadInput = viewChild<ElementRef<HTMLInputElement>>('uploadInput');
  private readonly testPanel = viewChild('testPanel', { read: ElementRef });
  private readonly sidePanel = viewChild(ScriptSidePanel);
  private readonly sidePanelElement = viewChild(ScriptSidePanel, { read: ElementRef });
  private readonly systemConfig = inject(SystemConfigService);
  protected readonly runs = viewChild(PipelineRuns);

  protected readonly kinds = fixedScriptKinds;
  /** The newest run, for the strip under the picker; the runs list below reports it. */
  protected readonly latestRun = signal<RunSummary | null>(null);
  protected readonly runsLoaded = signal(false);
  /** Whether the indicator's details (label, description, unit, format) are open for editing. */
  protected readonly metaOpen = signal(false);
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
  /** The latest test as the test panel last saw it, polled to its end: what a fix of the assistant refers to. */
  protected readonly latestTest = signal<ScriptTest | null>(null);
  /** The sentences "Ask the assistant" put in a request box, so asking again replaces them and never the analyst's own words. */
  private readonly askedSentences = new Set<string>();
  protected readonly downloading = signal(false);
  /** The latest sample download requested from this page; the panel follows it until it is ready. */
  protected readonly sampleDownload = signal<SampleDownload | null>(null);

  /** What the side panel shows, by source: the raw tables for script 1, the clean tables for the others. */
  protected readonly inputsBySource = signal<Partial<Record<'raw' | 'clean', ScriptInputs>>>({});
  protected readonly inputsFailed = signal(false);
  /** The problems the editor's checks found in the text, for the list under the editor. */
  protected readonly problems = signal<ResolvedProblem[]>([]);

  protected readonly canEdit = computed(() => this.context.can('analyst'));
  /** Whether the test result may offer "Ask the assistant": the deployment has it, the user can edit, no older version is on view and the test is the selected script's. */
  protected readonly canAsk = computed(() => {
    const test = this.test();
    return this.systemConfig.assistantEnabled() && this.canEdit() && !this.viewing() && !!test && this.isSelected(test);
  });
  /** The line the editor's cursor is on, which follows the cursor without any typing; the Assistant's "This cell" scope reads it. */
  protected readonly cursorLine = computed(() => this.editor()?.cursorLine() ?? 1);
  protected readonly inputs = computed(() => this.inputsBySource()[this.selected().kind === 'clean' ? 'raw' : 'clean'] ?? null);
  /** What the editor's checks know about the tables the selected script receives; null until they have loaded. */
  private readonly inputsInfo = computed<ScriptInputsInfo | null>(() => {
    const inputs = this.inputs();
    return inputs && { tables: inputs.tables, referenceTables: inputs.referenceTables, questions: inputs.questions };
  });
  /** The editor's extensions: completion, checks, hover and cell actions; rebuilt when the script kind or the inputs change. */
  protected readonly assist = computed(() => {
    return scriptAssist({
      kind: this.selected().kind,
      inputs: this.inputsInfo(),
      translate: (key, params) => this.transloco.translate(key, params),
      locale: this.locale,
      canRun: this.canEdit() && !this.viewing(),
      runCell: (stopLine) => this.startTest(stopLine ?? undefined),
      problems: (found) => this.problems.set(found),
    });
  });
  /** The texts CodeMirror writes itself (search panel, folding, diagnostics) in the interface language. */
  protected readonly phrases = codeEditorPhrases((key) => this.transloco.translate(key));
  protected readonly indicators = computed(() => this.scripts()?.indicators ?? []);
  /** The indicators of the menu: one being added (its script not saved yet) comes first, with its label from the dialog. */
  protected readonly indicatorItems = computed<IndicatorMenuItem[]>(() => {
    const added = this.newIndicator();
    const saved = this.indicators().map((i) => ({ name: i.name, label: i.label, version: i.current.number }));
    return added ? [{ name: added, label: this.meta().label || added, version: null }, ...saved] : saved;
  });
  /** The signature the selected script has to define, shown beside the editor. */
  protected readonly signature = computed(() => signatureOf(this.selected().kind));
  /** The details stay open while they hold edits or the API refused one of them, so nothing unsaved or wrong is hidden. */
  protected readonly metaShown = computed(() => this.metaOpen() || this.metadataDirty() || Object.keys(this.fieldErrors()).length > 0);
  /** Where the reference tables are managed, for whoever may; null for everyone else. */
  protected readonly referenceLink = computed(() => (this.canEdit() ? ['/projects', this.projectId(), 'reference'] : null));
  /** Upload and remove wait in the menu: they are rare next to saving and testing. */
  protected readonly moreItems = computed<MenuItem[]>(() => {
    const t = (key: string) => this.transloco.translate(key);
    const items: MenuItem[] = [{ id: 'upload', label: t('scripts.upload'), testId: 'upload-item' }];
    if (this.current() && !this.current()!.removed) {
      items.push({ id: 'remove', label: t('scripts.remove'), danger: true, testId: 'remove' });
    }
    return items;
  });
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
  protected readonly metadataDirty = computed(
    () => this.indicatorName() !== null && !this.newIndicator() && !sameMetadata(this.meta(), this.savedMetadata()),
  );
  /** A script that was never saved is always unsaved, even before its first edit. */
  protected readonly dirty = computed(
    () => !this.viewing() && (!!this.newIndicator() || this.draft() !== this.savedCode() || this.metadataDirty()),
  );
  /** What the editor starts from: a version being viewed, the template of a new indicator, or the saved code. Edits flow back through `onEdit`. */
  protected readonly editorValue = computed(() =>
    this.viewing() ? (this.viewing()!.code ?? '') : this.newIndicator() ? newIndicatorTemplate.source : this.savedCode(),
  );
  protected readonly shownMetadata = computed(() => (this.viewing() ? metadataOf(this.viewing()!) : this.meta()));

  constructor() {
    this.assistant.connect({
      projectId: () => this.projectId(),
      script: this.selected,
      draft: this.draft,
      inputs: this.inputsInfo,
      fixValid: (target) => this.fixValid(target),
    });
    this.load();
    this.loadInputs();
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
    this.loadInputs();
  }

  protected runTone(status: RunStatus): Tone {
    return runTones[status];
  }

  protected onLatestRun(run: RunSummary | null): void {
    this.latestRun.set(run);
    this.runsLoaded.set(true);
  }

  protected runNow(): void {
    this.runs()?.start();
  }

  /** The step that failed in a run, named, for the strip: "Clean failed" or the indicator's name. */
  protected failedStep(run: RunSummary, t: (key: string, params?: Record<string, unknown>) => string): string | null {
    const step = run.steps.find((s) => s.status === 'failed');
    return step ? t('runs.stepFailed', { step: step.step === 'indicator' ? step.name : t('runs.steps.' + step.step) }) : null;
  }

  /** A check that is about a reference table: its message offers to open the reference data. */
  protected isReferenceProblem(problem: ResolvedProblem): boolean {
    return problem.key === 'scripts.checks.noReference' || problem.key === 'scripts.checks.unknownReference';
  }

  protected onMore(action: string): void {
    if (action === 'upload') {
      this.uploadInput()?.nativeElement.click();
    } else if (action === 'remove') {
      void this.remove();
    }
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
    this.draft.set(newIndicatorTemplate.source);
    // Explicit, because the editor's value input does not change when the previous script had the template as its code.
    this.editor()?.replace(newIndicatorTemplate.source);
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
  protected startTest(stopAtLine?: number): void {
    if (this.testing()) {
      return;
    }
    const size = this.readSampleSize();
    if (size === null) {
      return;
    }
    const code = this.draft();
    if (new TextEncoder().encode(code).length > maxCodeBytes) {
      this.testError.set(this.transloco.translate('scripts.tooLarge'));
      return;
    }
    this.testing.set(true);
    this.service.startTest(this.projectId(), this.selected(), code, size, stopAtLine).subscribe({
      next: (test) => {
        this.testing.set(false);
        this.test.set(test);
        this.latestTest.set(test);
        if (stopAtLine === undefined) {
          setTimeout(() => this.testPanel()?.nativeElement.scrollIntoView?.({ block: 'nearest' }));
        }
      },
      error: (error) => {
        this.testing.set(false);
        const parsed = this.errors.parse(error);
        this.testError.set(parsed.fieldErrors['sampleSize'] ?? parsed.fieldErrors['code'] ?? parsed.fieldErrors['name'] ?? parsed.message);
      },
    });
  }

  /** The sample size in the field, or null after saying why it cannot be used. */
  private readSampleSize(): number | null {
    const size = Number(this.sampleSize());
    this.testError.set(null);
    if (!Number.isInteger(size) || size < minSampleSize || size > maxSampleSize) {
      this.testError.set(this.transloco.translate('scriptTests.sampleSizeRange', { min: minSampleSize, max: numberText(maxSampleSize, this.locale) }));
      return null;
    }
    return size;
  }

  /** Asks for a sample of the project's data to work on a computer, of the size in the field. */
  protected startDownload(): void {
    const size = this.readSampleSize();
    if (size !== null) {
      this.requestDownload(size);
    }
  }

  /** Queues a sample download; a new request replaces the user's earlier one, ready or not. */
  protected requestDownload(size: number): void {
    if (this.downloading()) {
      return;
    }
    this.downloading.set(true);
    this.service.startSampleDownload(this.projectId(), size).subscribe({
      next: (download) => {
        this.downloading.set(false);
        this.sampleDownload.set(download);
      },
      error: (error) => {
        this.downloading.set(false);
        const parsed = this.errors.parse(error);
        this.testError.set(parsed.fieldErrors['sampleSize'] ?? parsed.message);
      },
    });
  }

  /** Reads the tables the selected script receives, unless they are already known. */
  private loadInputs(force = false): void {
    const kind = this.selected().kind;
    const source = kind === 'clean' ? 'raw' : 'clean';
    if (!force && this.inputsBySource()[source]) {
      return;
    }
    this.service.scriptInputs(this.projectId(), kind).subscribe({
      next: (inputs) => {
        this.inputsFailed.set(false);
        this.inputsBySource.update((known) => ({ ...known, [inputs.source]: inputs }));
      },
      error: () => this.inputsFailed.set(true),
    });
  }

  /** Reads the published tables again, after a run ended or on the panel's refresh button. */
  protected reloadInputs(): void {
    this.inputsBySource.set({});
    this.loadInputs(true);
  }

  /** Puts a table, column or question name in the editor as a quoted string, or copies it when the editor is read-only. */
  protected insertName(name: string): void {
    if (this.canEdit() && !this.viewing()) {
      this.editor()?.insertAtCursor(JSON.stringify(name));
      return;
    }
    void navigator.clipboard?.writeText(name);
    this.toasts.success(this.transloco.translate('scripts.inputs.copied', { name }));
  }

  protected goToProblem(problem: ResolvedProblem): void {
    this.editor()?.goTo(problem.from);
  }

  /** The templates of the selected script's kind with their translated names and descriptions. */
  protected templateOptions(): TemplateOption[] {
    const kind = this.selected().kind;
    return scriptTemplates[kind].map(({ id }) => ({
      id,
      name: this.transloco.translate(`scripts.templates.${kind}.${id}.name`),
      description: this.transloco.translate(`scripts.templates.${kind}.${id}.description`),
    }));
  }

  /** Replaces the editor's text with a template as one edit that can be undone, after asking when there are edits to lose. */
  protected async chooseTemplate(id: string): Promise<void> {
    const template = scriptTemplates[this.selected().kind].find((candidate) => candidate.id === id);
    if (!template) {
      return;
    }
    const startingPoint = this.newIndicator() ? newIndicatorTemplate.source : this.savedCode();
    if (this.draft() !== startingPoint && this.draft() !== template.source) {
      const t = (key: string) => this.transloco.translate(key);
      const confirmed = await this.confirmDialog.confirm({
        title: t('scripts.templateReplaceTitle'),
        message: t('scripts.templateReplaceMessage'),
        confirmLabel: t('scripts.templateReplace'),
        cancelLabel: t('common.cancel'),
        tone: 'danger',
      });
      if (!confirmed) {
        return;
      }
    }
    this.editor()?.replace(template.source);
  }

  /** Whether a test is of the selected script: the same kind and, for an indicator, the same name. */
  private isSelected(test: ScriptTest): boolean {
    const selected = this.selected();
    return test.kind === selected.kind && (selected.kind !== 'indicator' || test.name === selected.name);
  }

  /**
   * Whether a fix target still matches the page (design D11, "Fix target"): the latest test is the one asked about and the selected
   * script's, ran the editor's text, no older version is on view and, for a warning, the dashboard view being edited still shows it.
   */
  private fixValid(target: FixTarget): boolean {
    const test = this.latestTest();
    if (!test || test.id !== target.testId || !this.isSelected(test) || this.viewing() || test.code !== this.draft()) {
      return false;
    }
    const asked = target.warning;
    if (!asked) {
      return true;
    }
    const facts = test.steps.find((step) => step.step === 'indicator')?.dashboard;
    // Only warning keys are compared: the formatted numbers are dropped, so the locale given to the formatter does not matter.
    const params = JSON.stringify(Object.entries(asked.params ?? {}).sort());
    return (
      !!facts &&
      viewWarnings(facts, valueFormatter({ format: this.meta().format, unit: null }, 'en'), this.meta().format)
        .map(askedWarning)
        .some((warning) => warning.key === asked.key && JSON.stringify(Object.entries(warning.params).sort()) === params)
    );
  }

  /**
   * "Ask the assistant" on the failed step or on a warning of the dashboard view: the Assistant tab opens with the Fix scope for that
   * test, the request box holds a sentence that names the step or the kind of warning, and the tab is scrolled into view and focused.
   */
  protected askAssistant(target: FixTarget): void {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const selected = this.selected();
    const sentence = target.warning
      ? t(`scripts.assistant.fixWarning.${target.warning.key}`)
      : t('scripts.assistant.fixStep', {
          step: t(`scripts.assistant.scriptLabels.${selected.kind}`, selected.kind === 'indicator' ? { name: selected.name } : {}),
        });
    const typed = this.assistant.text();
    // The sentence is the request. Words the analyst already typed stay before it; a sentence from an earlier ask is replaced.
    this.assistant.setFixTarget(target);
    this.assistant.setText(typed.trim() && !this.askedSentences.has(typed.trim()) ? `${typed.trimEnd()}\n\n${sentence}` : sentence);
    this.askedSentences.add(sentence);
    this.assistant.setScope('fix');
    if (this.sidePanel()?.open('assistant', true)) {
      setTimeout(() => this.sidePanelElement()?.nativeElement.scrollIntoView?.({ block: 'start' }));
    }
  }

  /**
   * Puts a block of the Assistant's proposal in the editor as one edit that undo reverts, after asking when the editor's text is not
   * the text the request was sent with. Nothing is saved or tested. Afterwards the usage record learns how it was applied.
   */
  protected async applyProposal(request: ApplyRequest): Promise<void> {
    const plan = this.assistant.applyPlan(request);
    if (!plan || !this.canEdit() || this.viewing()) {
      return;
    }
    if (this.draft() !== plan.sentDraft) {
      const t = (key: string) => this.transloco.translate(key);
      const confirmed = await this.confirmDialog.confirm({
        title: t('scripts.assistant.replaceEditedTitle'),
        message: t('scripts.assistant.replaceEditedMessage'),
        confirmLabel: t('scripts.assistant.replaceEditedConfirm'),
        cancelLabel: t('common.cancel'),
        tone: 'danger',
      });
      if (!confirmed) {
        return;
      }
    }
    const editor = this.editor();
    if (!editor || this.viewing()) {
      return;
    }
    let applied = false;
    if (plan.action === 'script') {
      editor.replace(plan.code);
      applied = true;
    } else if (plan.action === 'cell') {
      // The cell is found again by its text as sent, wherever it is now; the confirmation above may have let the text change.
      const range = plan.cellText === null ? null : findCell(editor.text(), plan.cellText);
      applied = range !== null && editor.replaceRange(range.from, range.to, cellReplacement(plan.code, range.text));
      if (!applied) {
        this.toasts.error(this.transloco.translate('scripts.assistant.notes.cellChanged'));
      }
    } else {
      applied = editor.insertAtCursor(plan.code);
    }
    if (applied) {
      this.assistant.recordOutcome(plan);
    }
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
    this.metaOpen.set(false);
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
