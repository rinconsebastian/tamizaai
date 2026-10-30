import { Component, Injector, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { SystemConfigService } from '../../../core/system-config';
import type { IndicatorMetadata, ScriptInputs, ScriptKind, ScriptVersionSummary } from '../../../core/projects/pipeline.service';
import { Badge, Card, DateTimePipe } from '../../../shared/ui';
import { ApplyRequest } from './assistant-state';
import { AssistantPanel } from './assistant-panel';
import { CtxReference } from './ctx-reference';
import { ScriptInputsPanel } from './script-inputs-panel';

export type SideTab = 'inputs' | 'reference' | 'versions' | 'assistant';

/**
 * Beside the editor: the tables the script receives, the reference of its contract and `ctx`, the script's versions and, when the
 * deployment has the AI assistant and the user can edit, the Assistant. Only the shown tab exists: a hidden tab's component is
 * destroyed, so what must outlive a tab lives in the page-scoped assistant session.
 */
@Component({
  selector: 'app-script-side-panel',
  imports: [DateTimePipe, TranslocoDirective, Badge, Card, AssistantPanel, ScriptInputsPanel, CtxReference],
  template: `
    <tmz-card class="min-w-0" *transloco="let t" data-testid="side-panel">
      <div role="tablist" [attr.aria-label]="t('scripts.sidePanel')" class="mb-3 flex flex-wrap gap-x-1 border-b border-default">
        @for (item of tabs(); track item) {
          <button
            type="button"
            role="tab"
            class="-mb-px border-b-2 px-3 py-2 text-sm font-medium"
            [class]="tab() === item ? 'border-brand text-fg-brand' : 'border-transparent text-body hover:text-heading'"
            [attr.aria-selected]="tab() === item"
            [attr.aria-controls]="'side-panel-' + item"
            [id]="'side-tab-' + item"
            (click)="chosen.set(item)"
            [attr.data-testid]="'side-tab-' + item"
          >
            {{ t('scripts.sideTabs.' + item) }}
          </button>
        }
      </div>
      <div role="tabpanel" [id]="'side-panel-' + tab()" [attr.aria-labelledby]="'side-tab-' + tab()">
        @switch (tab()) {
          @case ('inputs') {
            <app-script-inputs-panel
              [inputs]="inputs()"
              [failed]="failed()"
              [canInsert]="canInsert()"
              [manageLink]="manageLink()"
              (pick)="pick.emit($event)"
              (refresh)="refresh.emit()"
            />
          }
          @case ('reference') {
            <app-ctx-reference [kind]="kind()" />
          }
          @case ('versions') {
            @if (versions().length === 0) {
              <p class="text-sm text-body">{{ t('scripts.noVersions') }}</p>
            } @else {
              <ol class="flex max-h-[28rem] flex-col gap-2 overflow-y-auto" data-testid="versions">
                @for (version of versions(); track version.versionId) {
                  <li>
                    <button
                      type="button"
                      class="w-full rounded-base border border-default px-3 py-2 text-left text-sm hover:bg-neutral-secondary-medium"
                      [class.border-brand]="viewingNumber() === version.number"
                      (click)="view.emit(version)"
                      [attr.data-testid]="'version-' + version.number"
                    >
                      <span class="font-medium text-heading">{{ t('scripts.versionNumber', { number: version.number }) }}</span>
                      @if (version.removed) {
                        <tmz-badge tone="warning" class="ms-1">{{ t('scripts.removedBadge') }}</tmz-badge>
                      }
                      <span class="block text-body">{{ version.author.name }} · {{ version.createdAt | dateTime }}@if (version.origin === 'mcp') { · <span data-testid="version-via-mcp">{{ t('scripts.versions.viaMcp') }}</span>}</span>
                    </button>
                  </li>
                }
              </ol>
            }
          }
          @case ('assistant') {
            <app-assistant-panel
              [projectId]="projectId()"
              [kind]="kind()"
              [scriptName]="scriptName()"
              [draft]="draft()"
              [cursorLine]="cursorLine()"
              [details]="details()"
              [viewing]="viewingNumber() !== null"
              (apply)="apply.emit($event)"
            />
          }
        }
      </div>
    </tmz-card>
  `,
})
export class ScriptSidePanel {
  readonly kind = input.required<ScriptKind>();
  readonly inputs = input<ScriptInputs | null>(null);
  readonly failed = input(false);
  readonly canInsert = input(false);
  /** The commands of the link to the reference tables, for whoever may manage them; null for everyone else. */
  readonly manageLink = input<string[] | null>(null);
  readonly versions = input<ScriptVersionSummary[]>([]);
  /** The number of the older version on view, if any. */
  readonly viewingNumber = input<number | null>(null);
  /** Whether the user may edit scripts; the Assistant tab is for those who can. */
  readonly canEdit = input(false);
  readonly projectId = input('');
  /** The selected indicator's name; null for script 1 and script 2. */
  readonly scriptName = input<string | null>(null);
  /** The editor's text, saved or not, for the Assistant. */
  readonly draft = input('');
  /** The 1-based line the editor's cursor is on, for the Assistant's "This cell" scope. */
  readonly cursorLine = input(1);
  /** The indicator's details as edited, for the Assistant; null for script 1 and script 2. */
  readonly details = input<IndicatorMetadata | null>(null);
  readonly pick = output<string>();
  readonly refresh = output<void>();
  readonly view = output<ScriptVersionSummary>();
  /** The analyst chose an action of the Assistant's proposal; the page puts the code in the editor. */
  readonly apply = output<ApplyRequest>();

  private readonly injector = inject(Injector);
  private readonly config = inject(SystemConfigService);
  private readonly assistant = viewChild(AssistantPanel);
  /** Tables, Reference and Versions, and after them Assistant when the deployment enables it and the user can edit. */
  protected readonly tabs = computed<SideTab[]>(() =>
    this.config.assistantEnabled() && this.canEdit() ? ['inputs', 'reference', 'versions', 'assistant'] : ['inputs', 'reference', 'versions'],
  );
  protected readonly chosen = signal<SideTab>('inputs');
  /** The tab on view: the one chosen, or Tables while the chosen one is not available (the assistant was switched off, or the role lost). */
  protected readonly tab = computed<SideTab>(() => (this.tabs().includes(this.chosen()) ? this.chosen() : 'inputs'));

  /**
   * Shows a tab on the page's request, even when it was requested before and the analyst has since chosen another one. With
   * `focus`, the Assistant's request box gets the focus once the tab is shown. False when the tab is not available.
   */
  open(tab: SideTab, focus = false): boolean {
    if (!this.tabs().includes(tab)) {
      return false;
    }
    this.chosen.set(tab);
    if (focus && tab === 'assistant') {
      // Already on view: nothing re-renders, so a callback waiting for the next render could wait forever.
      const shown = this.tab() === 'assistant' ? this.assistant() : undefined;
      if (shown) {
        shown.focusRequest();
      } else {
        afterNextRender(() => this.assistant()?.focusRequest(), { injector: this.injector });
      }
    }
    return true;
  }
}
