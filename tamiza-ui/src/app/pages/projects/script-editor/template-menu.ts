import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, input, output } from '@angular/core';
import { Button } from '../../../shared/ui';

export interface TemplateOption {
  id: string;
  name: string;
  description: string;
}

/** A button that opens the templates of a script kind, each with its name and a one-line description (CDK menu: arrow keys, Escape). */
@Component({
  selector: 'app-template-menu',
  imports: [CdkMenuTrigger, CdkMenu, CdkMenuItem, Button],
  template: `
    <button tmzButton variant="secondary" type="button" [cdkMenuTriggerFor]="menu" [attr.aria-label]="label()" data-testid="template">
      {{ label() }}
      <svg aria-hidden="true" class="size-4" viewBox="0 0 20 20" fill="currentColor"><path d="M5.5 7.5 10 12l4.5-4.5z" /></svg>
    </button>
    <ng-template #menu>
      <div
        cdkMenu
        [attr.aria-label]="label()"
        class="z-10 w-80 max-w-[calc(100vw-2rem)] py-2 bg-neutral-primary-medium border border-default-medium rounded-base shadow-lg"
        data-testid="template-menu"
      >
        @for (option of options(); track option.id) {
          <button
            cdkMenuItem
            type="button"
            (cdkMenuItemTriggered)="chosen.emit(option.id)"
            class="block w-full text-left px-4 py-2 min-h-10 hover:bg-neutral-tertiary-medium focus:bg-neutral-tertiary-medium focus:outline-none"
            [attr.data-testid]="'template-' + option.id"
          >
            <span class="block text-sm font-medium text-heading">{{ option.name }}</span>
            <span class="block text-sm text-body">{{ option.description }}</span>
          </button>
        }
      </div>
    </ng-template>
  `,
})
export class TemplateMenu {
  readonly label = input.required<string>();
  readonly options = input.required<TemplateOption[]>();
  readonly chosen = output<string>();
}
