import { Component, computed, input } from '@angular/core';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { ValueFormatter, plotted } from './format';

/**
 * A stat tile: one figure for its indicator, formatted as the indicator says. The server sums the measure over every row the
 * query matches, or divides the sum of the numerator by the sum of the denominator for a ratio, and answers with that one row.
 */
@Component({
  selector: 'app-stat-widget',
  template: `
    <div class="flex h-full min-h-0 flex-col justify-center gap-1" data-testid="stat">
      <p class="break-all text-4xl font-semibold text-heading" data-testid="stat-value">{{ value() }}</p>
      <p class="break-words text-sm text-body" data-testid="stat-label">{{ description() }}</p>
    </div>
  `,
  host: { class: 'block h-full min-h-0' },
})
export class StatWidget {
  readonly rows = input.required<QueryRow[]>();
  readonly config = input.required<WidgetConfig>();
  readonly label = input.required<string>();
  readonly format = input.required<ValueFormatter>();

  protected readonly value = computed(() => {
    const row = this.rows()[0];
    return row ? this.format()(row.value) : '—';
  });
  protected readonly description = computed(() => plotted(this.config(), this.label()));
}
