import { DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';

/** A stat tile: the plotted field of its one indicator, summed over every row the query matches. */
@Component({
  selector: 'app-stat-widget',
  imports: [DecimalPipe, TranslocoDirective],
  template: `
    <div class="flex h-full min-h-0 flex-col justify-center gap-1" *transloco="let t" data-testid="stat">
      <p class="break-all text-4xl font-semibold text-heading" data-testid="stat-value">{{ total() | number: '1.0-2' }}</p>
      <p class="break-words text-sm text-body" data-testid="stat-label">{{ config().indicators[0] }} · {{ t('indicators.valueFields.' + config().valueField) }}</p>
      <p class="text-xs text-body" data-testid="stat-rows">{{ t('indicators.stat.rows', { count: rows().length }) }}</p>
    </div>
  `,
  host: { class: 'block h-full min-h-0' },
})
export class StatWidget {
  readonly rows = input.required<QueryRow[]>();
  readonly config = input.required<WidgetConfig>();

  protected readonly total = computed(() => this.rows().reduce((sum, row) => sum + row.value, 0));
}
