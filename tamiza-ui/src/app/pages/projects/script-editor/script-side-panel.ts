import { Component, input, output, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ScriptInputs, ScriptKind } from '../../../core/projects/pipeline.service';
import { Card } from '../../../shared/ui';
import { CtxReference } from './ctx-reference';
import { ScriptInputsPanel } from './script-inputs-panel';

type Tab = 'inputs' | 'reference';

/** Beside the editor: the tables the script receives, and the reference of its contract and `ctx`. */
@Component({
  selector: 'app-script-side-panel',
  imports: [TranslocoDirective, Card, ScriptInputsPanel, CtxReference],
  template: `
    <tmz-card class="min-w-0" *transloco="let t" data-testid="side-panel">
      <div role="tablist" [attr.aria-label]="t('scripts.sidePanel')" class="mb-3 flex gap-1 border-b border-default">
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
        @if (tab() === 'inputs') {
          <app-script-inputs-panel [inputs]="inputs()" [failed]="failed()" [canInsert]="canInsert()" (pick)="pick.emit($event)" (refresh)="refresh.emit()" />
        } @else {
          <app-ctx-reference [kind]="kind()" />
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
  readonly pick = output<string>();
  readonly refresh = output<void>();

  protected readonly tabs: Tab[] = ['inputs', 'reference'];
  protected readonly tab = signal<Tab>('inputs');
}
