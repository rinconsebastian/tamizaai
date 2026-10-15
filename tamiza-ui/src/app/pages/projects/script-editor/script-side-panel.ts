import { DatePipe } from '@angular/common';
import { Component, input, output, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ScriptInputs, ScriptKind, ScriptVersionSummary } from '../../../core/projects/pipeline.service';
import { Badge, Card, dateTimeFormat } from '../../../shared/ui';
import { CtxReference } from './ctx-reference';
import { ScriptInputsPanel } from './script-inputs-panel';

type Tab = 'inputs' | 'reference' | 'versions';

/** Beside the editor: the tables the script receives, the reference of its contract and `ctx`, and the script's versions. */
@Component({
  selector: 'app-script-side-panel',
  imports: [DatePipe, TranslocoDirective, Badge, Card, ScriptInputsPanel, CtxReference],
  template: `
    <tmz-card class="min-w-0" *transloco="let t" data-testid="side-panel">
      <div role="tablist" [attr.aria-label]="t('scripts.sidePanel')" class="mb-3 flex flex-wrap gap-x-1 border-b border-default">
        @for (item of tabs; track item) {
          <button
            type="button"
            role="tab"
            class="-mb-px border-b-2 px-3 py-2 text-sm font-medium"
            [class]="tab() === item ? 'border-brand text-fg-brand' : 'border-transparent text-body hover:text-heading'"
            [attr.aria-selected]="tab() === item"
            [attr.aria-controls]="'side-panel-' + item"
            [id]="'side-tab-' + item"
            (click)="tab.set(item)"
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
          @default {
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
                      <span class="block text-body">{{ version.author.name }} · {{ version.createdAt | date: dateTimeFormat }}</span>
                    </button>
                  </li>
                }
              </ol>
            }
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
  readonly pick = output<string>();
  readonly refresh = output<void>();
  readonly view = output<ScriptVersionSummary>();

  protected readonly dateTimeFormat = dateTimeFormat;
  protected readonly tabs: Tab[] = ['inputs', 'reference', 'versions'];
  protected readonly tab = signal<Tab>('inputs');
}
