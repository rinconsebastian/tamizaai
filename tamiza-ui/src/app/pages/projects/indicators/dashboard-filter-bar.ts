import { Dialog } from '@angular/cdk/dialog';
import { Component, computed, inject, input, output } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { map, of } from 'rxjs';
import { DashboardFilter, DashboardFilterValue, IndicatorsService } from '../../../core/projects/indicators.service';
import { Button } from '../../../shared/ui';
import { DataFilterDialog, DataFilterDialogData, DataFilterDialogResult } from '../data-filter-dialog';
import { FilterValues, columnFor, defaultsOf, describeValue, fromDataFilter, operationOf, sameValues, toDataFilter } from './dashboard-filters';

/** A filter's value changed on the member's screen; null is "All". */
export interface FilterChange {
  column: string;
  value: DashboardFilterValue | null;
}

/**
 * The dashboard's filters, between its header and its widgets: one pill each with its label and the value in force, which every
 * member can change on their own screen, plus clearing them all and going back to the defaults (dashboard filters, design D7). The
 * page keeps the values and writes them to the address; the bar only says what changed.
 */
@Component({
  selector: 'app-dashboard-filter-bar',
  imports: [TranslocoDirective, Button],
  template: `
    @if (filters().length) {
      <section class="flex flex-wrap items-center gap-2" aria-labelledby="dashboard-filters-title" data-testid="dashboard-filter-bar" *transloco="let t">
        <h3 id="dashboard-filters-title" class="sr-only">{{ t('dashboardFilters.bar.title') }}</h3>
        <ul class="flex min-w-0 max-w-full flex-wrap gap-2">
          @for (filter of filters(); track filter.column) {
            <li class="min-w-0 max-w-full">
              <button
                type="button"
                class="inline-flex min-h-9 max-w-full items-center rounded-full border px-3 py-1 text-sm focus:outline-none focus:ring-4 focus:ring-brand-medium"
                [class]="values()[filter.column] ? 'border-brand-subtle bg-brand-softer text-fg-brand-strong' : 'border-default bg-neutral-primary-soft text-body hover:bg-neutral-tertiary-medium'"
                [attr.title]="text(filter, t)"
                (click)="open(filter)"
                [attr.data-column]="filter.column"
                data-testid="dashboard-filter"
              >
                <span class="min-w-0 truncate"><span class="font-medium">{{ filter.label }}:</span> {{ value(filter, t) }}</span>
              </button>
            </li>
          }
        </ul>
        @if (anySet()) {
          <button tmzButton variant="ghost" size="sm" type="button" (click)="cleared.emit()" data-testid="dashboard-filters-clear">{{ t('dashboardFilters.clearAll') }}</button>
        }
        @if (offersDefaults()) {
          <button tmzButton variant="ghost" size="sm" type="button" (click)="reset.emit()" data-testid="dashboard-filters-reset">{{ t('dashboardFilters.reset') }}</button>
        }
      </section>
    }
  `,
  host: { class: 'block' },
})
export class DashboardFilterBar {
  private readonly dialog = inject(Dialog);
  private readonly service = inject(IndicatorsService);
  private readonly transloco = inject(TranslocoService);

  readonly projectId = input.required<string>();
  readonly dashboardId = input.required<string>();
  readonly filters = input.required<DashboardFilter[]>();
  /** The value in force of each filter, by column. */
  readonly values = input.required<FilterValues>();
  readonly timeZone = input('UTC');

  readonly changed = output<FilterChange>();
  /** Every filter to "All". */
  readonly cleared = output<void>();
  /** Every filter back to its default. */
  readonly reset = output<void>();

  protected readonly anySet = computed(() => this.filters().some((f) => this.values()[f.column]));
  /** Going back to the defaults is offered only when there are defaults and the values differ from them. */
  protected readonly offersDefaults = computed(
    () => this.filters().some((f) => f.default) && !sameValues(this.filters(), this.values(), defaultsOf(this.filters())),
  );

  protected value(filter: DashboardFilter, t: (key: string, params?: Record<string, unknown>) => string): string {
    return describeValue(filter, this.values()[filter.column], t);
  }

  /** The whole text of a pill, for its tooltip once the pill is cut short. */
  protected text(filter: DashboardFilter, t: (key: string, params?: Record<string, unknown>) => string): string {
    return `${filter.label}: ${this.value(filter, t)}`;
  }

  /** Opens the clean data dashboard's filter dialog for one filter: its one condition, titled with its label (design D7). */
  protected open(filter: DashboardFilter): void {
    const current = this.values()[filter.column];
    const data: DataFilterDialogData = {
      column: columnFor(filter),
      filter: current ? toDataFilter(filter.column, filter.kind, current) : null,
      timeZone: this.timeZone(),
      // A row of an indicator table is a group, not a submission: its count would mislead, so none is shown.
      loadValues: (search) =>
        this.service.dashboardValues(this.projectId(), this.dashboardId(), filter.column, search).pipe(map((r) => r.values.map((v) => ({ value: v.value, count: null })))),
      // The widgets' queries check the value; the dialog already took one of the filter's kind.
      check: () => of(null),
      allowTypedValues: true,
      title: filter.label,
      operations: [operationOf(filter.kind)],
      removeLabel: this.transloco.translate('dashboardFilters.clear'),
    };
    const ref = this.dialog.open<DataFilterDialogResult, DataFilterDialogData>(DataFilterDialog, {
      data,
      ariaLabelledBy: 'data-filter-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
      maxWidth: '100vw',
      maxHeight: '100dvh',
    });
    ref.closed.subscribe((result) => {
      if (!result) {
        return;
      }
      if ('remove' in result) {
        this.changed.emit({ column: filter.column, value: null });
        return;
      }
      const value = fromDataFilter(filter.kind, result.filter);
      if (value) {
        this.changed.emit({ column: filter.column, value });
      }
    });
  }
}
