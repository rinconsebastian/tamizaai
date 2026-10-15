import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ScriptKind } from '../../../core/projects/pipeline.service';
import { ctxMembersByPath, memberGroup, signatureOf } from './script-analysis';

/**
 * The reference tab: the contract of the selected script, what it returns, cells and `ctx.show`, and every member of
 * `ctx`, `ctx.form`, `ctx.log` and `ctx.reference` with its description and an example. The members are the list the
 * completion, hover and checks use, so the three always agree.
 */
@Component({
  selector: 'app-ctx-reference',
  imports: [TranslocoDirective],
  template: `
    <div class="flex flex-col gap-4 text-sm" *transloco="let t" data-testid="ctx-reference">
      <section aria-labelledby="reference-contract">
        <h3 id="reference-contract" class="mb-1 font-medium text-heading">{{ t('scripts.reference.contractTitle') }}</h3>
        <code class="block rounded-base bg-neutral-secondary-soft px-2 py-1 text-xs text-heading" data-testid="reference-signature">{{ signature() }}</code>
        <p class="mt-1 text-body" data-testid="reference-parameters">{{ t('scripts.reference.parameters.' + kind()) }}</p>
        <p class="mt-1 text-body" data-testid="reference-returns">{{ t('scripts.reference.returns.' + kind()) }}</p>
      </section>

      <section aria-labelledby="reference-cells">
        <h3 id="reference-cells" class="mb-1 font-medium text-heading">{{ t('scripts.reference.cellsTitle') }}</h3>
        <p class="text-body">{{ t('scripts.reference.cellsText') }}</p>
        <pre class="mt-1 overflow-x-auto rounded-base bg-neutral-secondary-soft p-2 text-xs text-heading">{{ t('scripts.reference.cellsExample') }}</pre>
      </section>

      <section aria-labelledby="reference-ctx">
        <h3 id="reference-ctx" class="mb-1 font-medium text-heading">{{ t('scripts.reference.ctxTitle') }}</h3>
        @for (group of groups; track group.path) {
          <h4 class="mt-2 font-mono text-xs font-medium text-heading">{{ group.path }}</h4>
          <dl class="mt-1 flex flex-col gap-2">
            @for (member of group.members; track member.name) {
              <div [attr.data-testid]="'reference-' + group.path + '.' + member.name">
                <dt><code class="text-xs text-heading">{{ member.signature }}</code></dt>
                <dd class="text-body">
                  {{ t('scripts.ctx.members.' + group.key + '.' + member.name + '.description') }}
                  <span class="block text-xs"
                    >{{ t('scripts.reference.example') }}: <code class="text-heading">{{ t('scripts.ctx.members.' + group.key + '.' + member.name + '.example') }}</code></span
                  >
                </dd>
              </div>
            }
          </dl>
        }
      </section>
    </div>
  `,
})
export class CtxReference {
  readonly kind = input.required<ScriptKind>();

  protected readonly signature = computed(() => signatureOf(this.kind()));
  protected readonly groups = ['ctx', 'ctx.form', 'ctx.log', 'ctx.reference'].map((path) => ({
    path,
    key: memberGroup(path),
    members: ctxMembersByPath[path],
  }));
}
