import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { CoverageRow, EnumeratorRow, PerformanceDashboard, PerformanceService } from '../../core/projects/performance.service';
import {
  Alert,
  Button,
  Card,
  FormField,
  Input,
  Skeleton,
  Table,
  TableContainer,
  tableCell,
  tableHead,
  tableHeaderCell,
  tableRow,
} from '../../shared/ui';
import { ProjectContext } from './project-context';

const pageSize = 50;
/** Day slots the chart always reserves, so a few days do not stretch into a few huge bars. */
const minSlots = 14;
const slotWidth = 10;
const chartHeight = 100;

type EnumeratorSort = 'submissions' | 'name';
type CoverageSort = 'frame' | 'percentAsc' | 'percentDesc';

@Component({
  selector: 'app-project-performance',
  imports: [DatePipe, DecimalPipe, RouterLink, TranslocoDirective, Alert, Button, Card, FormField, Input, Skeleton, Table, TableContainer],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t" [attr.aria-busy]="loading()">
      <tmz-card>
        <form class="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-start" (submit)="$event.preventDefault(); load()" data-testid="range-form">
          <tmz-form-field class="sm:w-48" [label]="t('performance.from')" fieldId="performanceFrom" [error]="fieldErrors()['from']">
            <input tmzInput id="performanceFrom" type="date" [value]="from()" [attr.max]="to() || null" (change)="setFrom($any($event.target).value)" data-testid="range-from" />
          </tmz-form-field>
          <tmz-form-field class="sm:w-48" [label]="t('performance.to')" fieldId="performanceTo" [error]="fieldErrors()['to']">
            <input tmzInput id="performanceTo" type="date" [value]="to()" [attr.min]="from() || null" (change)="setTo($any($event.target).value)" data-testid="range-to" />
          </tmz-form-field>
          <div class="flex flex-col gap-3 sm:mt-7 sm:flex-row">
            <button tmzButton variant="secondary" type="button" [disabled]="!from() && !to()" (click)="clear()" data-testid="range-clear">{{ t('performance.clear') }}</button>
            <button tmzButton type="submit" [disabled]="loading()" data-testid="refresh">{{ t('performance.refresh') }}</button>
          </div>
        </form>
        @if (data(); as data) {
          <div class="mt-4 flex flex-col gap-1 text-sm text-body">
            <p data-testid="computed-at">{{ t('performance.computedAt', { time: (data.computedAt | date: 'medium') }) }}</p>
            @if (data.timeZone === 'UTC') {
              <p data-testid="days-zone">
                {{ t('performance.daysUtc') }}
                @if (context.can('admin')) {
                  <a [routerLink]="['..', 'overview']" class="text-fg-brand hover:underline" data-testid="zone-link">{{ t('performance.daysUtcAction') }}</a>
                }
              </p>
            } @else {
              <p data-testid="days-zone">{{ t('performance.daysZone', { zone: data.timeZone }) }}</p>
            }
          </div>
        }
      </tmz-card>

      @if (error(); as message) {
        <tmz-alert tone="danger" data-testid="performance-error">{{ message }}</tmz-alert>
      }

      @if (data(); as data) {
        <tmz-card [heading]="t('performance.submissions.title')" data-testid="submissions-card">
          <dl class="grid grid-cols-2 gap-4">
            <div class="rounded-base border border-default bg-neutral-secondary-soft p-4">
              <dt class="text-sm text-body">{{ t('performance.submissions.total') }}</dt>
              <dd class="text-2xl font-semibold text-heading tabular-nums" data-testid="total">{{ data.submissions.total | number }}</dd>
            </div>
            <div class="rounded-base border border-default bg-neutral-secondary-soft p-4">
              <dt class="text-sm text-body">{{ t('performance.submissions.withoutTime') }}</dt>
              <dd class="text-2xl font-semibold text-heading tabular-nums" data-testid="without-time">{{ data.submissions.withoutReceptionTime | number }}</dd>
            </div>
          </dl>
          @if (data.submissions.days.length) {
            <div class="mt-5 flex flex-wrap items-center justify-between gap-3">
              <h3 class="text-base font-medium text-heading">{{ t('performance.submissions.perDay') }}</h3>
              <button tmzButton variant="secondary" size="sm" type="button" [attr.aria-pressed]="asTable()" (click)="asTable.set(!asTable())" data-testid="chart-toggle">
                {{ asTable() ? t('performance.submissions.showChart') : t('performance.submissions.showTable') }}
              </button>
            </div>
            @if (asTable()) {
              <tmz-table-container class="mt-3 max-h-96 overflow-y-auto">
                <table tmzTable data-testid="days-table">
                  <thead [class]="tableHead">
                    <tr>
                      <th scope="col" [class]="tableHeaderCell">{{ t('performance.submissions.day') }}</th>
                      <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('performance.submissions.count') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (day of data.submissions.days; track day.day) {
                      <tr [class]="tableRow">
                        <td [class]="tableCell + ' tabular-nums'">{{ day.day }}</td>
                        <td [class]="tableCell + ' text-end tabular-nums'">{{ day.count | number }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </tmz-table-container>
            } @else {
              <div class="mt-3" data-testid="days-chart">
                <p class="mb-1 text-xs text-body">{{ t('performance.submissions.peak', { count: (chart().peak | number) }) }}</p>
                <div class="border-b border-default">
                  <svg
                    role="img"
                    class="block h-40 w-full"
                    preserveAspectRatio="none"
                    [attr.viewBox]="'0 0 ' + chart().width + ' ' + chartHeight"
                    [attr.aria-label]="chartLabel(t)"
                  >
                    @for (bar of chart().bars; track bar.day) {
                      <rect class="fill-brand" [attr.x]="bar.x" [attr.y]="bar.y" [attr.width]="bar.width" [attr.height]="bar.height">
                        <title>{{ bar.day }}: {{ bar.count }}</title>
                      </rect>
                    }
                  </svg>
                </div>
                <div class="mt-1 flex justify-between gap-3 text-xs text-body tabular-nums">
                  <span>{{ data.submissions.days[0].day }}</span>
                  <span>{{ data.submissions.days[data.submissions.days.length - 1].day }}</span>
                </div>
              </div>
            }
          } @else {
            <p class="mt-4 text-sm text-body" data-testid="days-empty">{{ t('performance.submissions.noDays') }}</p>
          }
        </tmz-card>

        <tmz-card [heading]="t('performance.completion.title')" data-testid="completion-card">
          @if (data.completionTime; as time) {
            @if (time.available) {
              <dl class="grid grid-cols-2 gap-4 lg:grid-cols-4">
                <div class="rounded-base border border-default bg-neutral-secondary-soft p-4">
                  <dt class="text-sm text-body">{{ t('performance.completion.median') }}</dt>
                  <dd class="text-xl font-semibold text-heading sm:text-2xl" data-testid="median">{{ duration(time.medianSeconds) }}</dd>
                </div>
                <div class="rounded-base border border-default bg-neutral-secondary-soft p-4">
                  <dt class="text-sm text-body">{{ t('performance.completion.mean') }}</dt>
                  <dd class="text-xl font-semibold text-heading sm:text-2xl" data-testid="mean">{{ duration(time.meanWithoutOutliersSeconds) }}</dd>
                </div>
                <div class="rounded-base border border-default bg-neutral-secondary-soft p-4">
                  <dt class="text-sm text-body">{{ t('performance.completion.outliers') }}</dt>
                  <dd class="text-2xl font-semibold text-heading tabular-nums" data-testid="outliers">{{ time.outliers ?? 0 | number }}</dd>
                </div>
                <div class="rounded-base border border-default bg-neutral-secondary-soft p-4">
                  <dt class="text-sm text-body">{{ t('performance.completion.unusable') }}</dt>
                  <dd class="text-2xl font-semibold text-heading tabular-nums" data-testid="unusable">{{ time.unusableDurations ?? 0 | number }}</dd>
                </div>
              </dl>
              @if (time.lowerFenceSeconds !== null && time.upperFenceSeconds !== null) {
                <p class="mt-4 text-sm text-body" data-testid="fences">
                  {{
                    t('performance.completion.fences', {
                      lower: (fenceMinutes(time.lowerFenceSeconds) | number: '1.0-1'),
                      upper: (fenceMinutes(time.upperFenceSeconds) | number: '1.0-1')
                    })
                  }}
                </p>
              } @else {
                <p class="mt-4 text-sm text-body" data-testid="no-usable">{{ t('performance.completion.noneUsable') }}</p>
              }
            } @else {
              <tmz-alert tone="warning" data-testid="completion-unavailable">
                <p>{{ t('performance.completion.unavailable', { fields: time.missingFields.join(', ') }) }}</p>
                <p class="mt-2">
                  {{ t('performance.completion.howTo') }}
                  <a [routerLink]="['..', 'overview']" class="font-medium underline" data-testid="completion-link">{{ t('performance.seeOverview') }}</a>
                </p>
              </tmz-alert>
            }
          }
        </tmz-card>

        <tmz-card [heading]="t('performance.enumerators.title')" data-testid="enumerators-card">
          @if (data.enumerators.available) {
            @if (data.enumerators.rows.length === 0) {
              <p class="text-sm text-body" data-testid="enumerators-empty">{{ t('performance.enumerators.empty') }}</p>
            } @else {
              <div class="mb-4 sm:w-64">
                <label for="enumeratorSort" class="mb-2 block text-sm font-medium text-heading">{{ t('performance.sortBy') }}</label>
                <select tmzInput id="enumeratorSort" (change)="setEnumeratorSort($any($event.target).value)" data-testid="enumerator-sort">
                  <option value="submissions" [selected]="enumeratorSort() === 'submissions'">{{ t('performance.enumerators.sortSubmissions') }}</option>
                  <option value="name" [selected]="enumeratorSort() === 'name'">{{ t('performance.enumerators.sortName') }}</option>
                </select>
              </div>
              <!-- Phones: stacked cards. md and up: a table. -->
              <ul class="flex flex-col gap-3 md:hidden" data-testid="enumerator-cards">
                @for (row of enumeratorPage(); track $index) {
                  <li class="rounded-base border border-default bg-neutral-primary-soft p-4 text-sm" data-testid="enumerator-card">
                    <p class="mb-2 break-words font-medium text-heading">
                      @if (row.name !== null) {
                        {{ row.name }}
                      } @else {
                        <em>{{ t('performance.enumerators.none') }}</em>
                      }
                    </p>
                    <p class="text-body">{{ t('performance.enumerators.submissions') }}: {{ row.submissions | number }}</p>
                    <p class="text-body">{{ t('performance.enumerators.mean') }}: {{ duration(row.meanSeconds) }}</p>
                    <p class="text-body">{{ t('performance.enumerators.outliers') }}: {{ row.outliers | number }}</p>
                    <p class="text-body">{{ t('performance.enumerators.last') }}: {{ row.lastSubmissionAt ? (row.lastSubmissionAt | date: 'medium') : '—' }}</p>
                  </li>
                }
              </ul>
              <tmz-table-container class="hidden md:block">
                <table tmzTable data-testid="enumerator-table">
                  <thead [class]="tableHead">
                    <tr>
                      <th scope="col" [class]="tableHeaderCell">{{ t('performance.enumerators.name') }}</th>
                      <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('performance.enumerators.submissions') }}</th>
                      <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('performance.enumerators.mean') }}</th>
                      <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('performance.enumerators.outliers') }}</th>
                      <th scope="col" [class]="tableHeaderCell">{{ t('performance.enumerators.last') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of enumeratorPage(); track $index) {
                      <tr [class]="tableRow" data-testid="enumerator-row">
                        <td [class]="tableCell + ' max-w-xs break-words text-heading'">
                          @if (row.name !== null) {
                            {{ row.name }}
                          } @else {
                            <em>{{ t('performance.enumerators.none') }}</em>
                          }
                        </td>
                        <td [class]="tableCell + ' text-end tabular-nums'">{{ row.submissions | number }}</td>
                        <td [class]="tableCell + ' whitespace-nowrap text-end'">{{ duration(row.meanSeconds) }}</td>
                        <td [class]="tableCell + ' text-end tabular-nums'">{{ row.outliers | number }}</td>
                        <td [class]="tableCell + ' whitespace-nowrap'">{{ row.lastSubmissionAt ? (row.lastSubmissionAt | date: 'medium') : '—' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </tmz-table-container>
              @if (enumeratorRows().length > pageSize) {
                <nav class="mt-3 flex flex-wrap items-center justify-between gap-3 text-sm text-body" [attr.aria-label]="t('performance.enumerators.title')">
                  <span data-testid="enumerator-page-info">{{ t('performance.pageInfo', { from: enumeratorPageNumber() * pageSize + 1, to: enumeratorPageEnd(), total: enumeratorRows().length }) }}</span>
                  <div class="flex gap-2">
                    <button tmzButton variant="secondary" size="sm" type="button" [disabled]="enumeratorPageNumber() === 0" (click)="enumeratorPageNumber.set(enumeratorPageNumber() - 1)" data-testid="enumerator-previous">{{ t('performance.previous') }}</button>
                    <button tmzButton variant="secondary" size="sm" type="button" [disabled]="enumeratorPageEnd() >= enumeratorRows().length" (click)="enumeratorPageNumber.set(enumeratorPageNumber() + 1)" data-testid="enumerator-next">{{ t('performance.next') }}</button>
                  </div>
                </nav>
              }
            }
          } @else {
            <tmz-alert tone="warning" data-testid="enumerators-unavailable">
              <p>{{ t('performance.enumerators.unavailable') }}</p>
              <p class="mt-2">
                @if (context.can('admin')) {
                  {{ t('performance.enumerators.adminHow') }}
                  <a [routerLink]="['..', 'overview']" fragment="enumeratorField" class="font-medium underline" data-testid="enumerators-link">{{ t('performance.enumerators.adminLink') }}</a>
                } @else {
                  {{ t('performance.enumerators.askAdmin') }}
                }
              </p>
            </tmz-alert>
          }
        </tmz-card>

        <tmz-card [heading]="t('performance.coverage.title')" data-testid="coverage-card">
          @if (data.coverage; as coverage) {
            @if (coverage.state === 'notConfigured') {
              <div data-testid="coverage-none">
                <p class="text-sm text-heading">{{ t('performance.coverage.none') }}</p>
                @if (context.can('analyst')) {
                  <p class="mt-2 text-sm text-body">{{ t('performance.coverage.noneEditor') }}</p>
                  <a tmzButton variant="secondary" class="mt-4" [routerLink]="['..', 'sampling-frame']" data-testid="coverage-link">{{ t('performance.coverage.define') }}</a>
                } @else {
                  <p class="mt-2 text-sm text-body">{{ t('performance.coverage.noneViewer') }}</p>
                }
              </div>
            } @else {
              @if (warnings(); as list) {
                @if (list.length) {
                  <tmz-alert tone="warning" class="mb-5" [heading]="t('performance.coverage.warningsTitle')" data-testid="coverage-warnings">
                    <ul class="mt-1 list-disc ps-5">
                      @for (dimension of list; track dimension.name) {
                        <li [attr.data-testid]="'warning-' + dimension.warning">{{ t('performance.coverage.warning.' + dimension.warning, { name: dimension.name, field: dimension.field }) }}</li>
                      }
                    </ul>
                  </tmz-alert>
                }
              }

              <div class="grid gap-4 lg:grid-cols-2">
                <div class="rounded-base border border-default bg-neutral-secondary-soft p-4" data-testid="overall">
                  <p class="text-sm text-body">{{ t('performance.coverage.overall') }}</p>
                  @if (coverage.overallPercent !== null) {
                    <p class="text-2xl font-semibold text-heading tabular-nums" data-testid="overall-percent">{{ coverage.overallPercent | number: '1.0-1' }}%</p>
                    <div class="mt-2 h-2 w-full rounded-full bg-neutral-quaternary" role="progressbar" aria-valuemin="0" aria-valuemax="100" [attr.aria-valuenow]="cap(coverage.overallPercent)" [attr.aria-label]="t('performance.coverage.overall')">
                      <div class="h-2 rounded-full bg-brand" [style.width.%]="cap(coverage.overallPercent)" data-testid="overall-bar"></div>
                    </div>
                    <p class="mt-2 text-xs text-body">{{ t('performance.coverage.overallHelp') }}</p>
                  } @else {
                    <p class="mt-1 text-sm text-heading" data-testid="overall-none">{{ t('performance.coverage.overallNone') }}</p>
                  }
                </div>
                <div class="rounded-base border border-default bg-neutral-secondary-soft p-4" data-testid="outside">
                  <p class="text-sm text-body">{{ t('performance.coverage.outside') }}</p>
                  <p class="text-2xl font-semibold text-heading tabular-nums" data-testid="outside-count">{{ coverage.outsideFrame | number }}</p>
                  <p class="mt-1 text-xs text-body">{{ t('performance.coverage.outsideHelp') }}</p>
                </div>
              </div>

              @if (coverage.outsideTop.length) {
                <h3 class="mb-2 mt-5 text-base font-medium text-heading">{{ t('performance.coverage.outsideTop') }}</h3>
                <ul class="flex flex-col divide-y divide-default rounded-base border border-default" data-testid="outside-list">
                  @for (combination of coverage.outsideTop; track $index) {
                    <li class="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 px-4 py-3 text-sm" data-testid="outside-item">
                      <span class="min-w-0 break-words text-heading">
                        @for (value of combination.values; track $index; let last = $last) {
                          <span>
                            <span class="text-body">{{ coverage.dimensions[$index]?.name }}: </span>
                            @if (value) {
                              <span class="font-mono">{{ value.code }}</span>@if (value.label) {
                                <span class="text-body"> ({{ value.label }})</span>
                              }
                            } @else {
                              <em class="text-body">{{ t('performance.coverage.empty') }}</em>
                            }
                          </span>
                          @if (!last) {
                            <span class="text-body"> · </span>
                          }
                        }
                      </span>
                      <span class="whitespace-nowrap tabular-nums text-body">{{ t('performance.coverage.submissionsCount', { count: (combination.submissions | number) }) }}</span>
                    </li>
                  }
                </ul>
              }

              <h3 class="mb-3 mt-6 text-base font-medium text-heading">{{ t('performance.coverage.rowsTitle') }}</h3>
              @if (coverage.rows.length === 0) {
                <p class="text-sm text-body" data-testid="coverage-empty">{{ t('performance.coverage.noRows') }}</p>
              } @else {
                <div class="mb-4 sm:w-64">
                  <label for="coverageSort" class="mb-2 block text-sm font-medium text-heading">{{ t('performance.sortBy') }}</label>
                  <select tmzInput id="coverageSort" (change)="setCoverageSort($any($event.target).value)" data-testid="coverage-sort">
                    <option value="frame" [selected]="coverageSort() === 'frame'">{{ t('performance.coverage.sortFrame') }}</option>
                    <option value="percentAsc" [selected]="coverageSort() === 'percentAsc'">{{ t('performance.coverage.sortPercentAsc') }}</option>
                    <option value="percentDesc" [selected]="coverageSort() === 'percentDesc'">{{ t('performance.coverage.sortPercentDesc') }}</option>
                  </select>
                </div>
                <ul class="flex flex-col gap-3 md:hidden" data-testid="coverage-cards">
                  @for (row of coveragePage(); track $index) {
                    <li class="rounded-base border border-default bg-neutral-primary-soft p-4 text-sm" data-testid="coverage-card-item">
                      <ul class="mb-3 flex flex-col gap-1">
                        @for (value of row.values; track $index) {
                          <li class="break-words">
                            <span class="text-body">{{ coverage.dimensions[$index]?.name }}: </span>
                            <span class="font-mono text-heading">{{ value.code }}</span>@if (value.label) {
                              <span class="text-body"> ({{ value.label }})</span>
                            }
                          </li>
                        }
                      </ul>
                      <p class="text-body">{{ t('performance.coverage.target') }}: {{ row.target | number }} · {{ t('performance.coverage.achieved') }}: {{ row.achieved | number }}</p>
                      @if (row.percent !== null) {
                        <p class="mt-1 font-medium text-heading tabular-nums">{{ row.percent | number: '1.0-1' }}%</p>
                        <div class="mt-1 h-2 w-full rounded-full bg-neutral-quaternary" role="progressbar" aria-valuemin="0" aria-valuemax="100" [attr.aria-valuenow]="cap(row.percent)" [attr.aria-label]="t('performance.coverage.percent')">
                          <div class="h-2 rounded-full bg-brand" [style.width.%]="cap(row.percent)" data-testid="row-bar"></div>
                        </div>
                      }
                    </li>
                  }
                </ul>
                <tmz-table-container class="hidden md:block">
                  <table tmzTable data-testid="coverage-table">
                    <thead [class]="tableHead">
                      <tr>
                        @for (dimension of coverage.dimensions; track $index) {
                          <th scope="col" [class]="tableHeaderCell">{{ dimension.name }}</th>
                        }
                        <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('performance.coverage.target') }}</th>
                        <th scope="col" [class]="tableHeaderCell + ' text-end'">{{ t('performance.coverage.achieved') }}</th>
                        <th scope="col" [class]="tableHeaderCell + ' min-w-40'">{{ t('performance.coverage.percent') }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (row of coveragePage(); track $index) {
                        <tr [class]="tableRow" data-testid="coverage-row">
                          @for (value of row.values; track $index) {
                            <td [class]="tableCell + ' max-w-xs break-words'">
                              <span class="font-mono text-heading">{{ value.code }}</span>
                              @if (value.label) {
                                <span class="block text-xs"> {{ value.label }}</span>
                              }
                            </td>
                          }
                          <td [class]="tableCell + ' text-end tabular-nums'">{{ row.target | number }}</td>
                          <td [class]="tableCell + ' text-end tabular-nums'">{{ row.achieved | number }}</td>
                          <td [class]="tableCell">
                            @if (row.percent !== null) {
                              <span class="tabular-nums text-heading" data-testid="row-percent">{{ row.percent | number: '1.0-1' }}%</span>
                              <div class="mt-1 h-2 w-full rounded-full bg-neutral-quaternary" role="progressbar" aria-valuemin="0" aria-valuemax="100" [attr.aria-valuenow]="cap(row.percent)" [attr.aria-label]="t('performance.coverage.percent')">
                                <div class="h-2 rounded-full bg-brand" [style.width.%]="cap(row.percent)" data-testid="row-bar"></div>
                              </div>
                            } @else {
                              <span data-testid="row-percent">—</span>
                            }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </tmz-table-container>
                @if (coverageRows().length > pageSize) {
                  <nav class="mt-3 flex flex-wrap items-center justify-between gap-3 text-sm text-body" [attr.aria-label]="t('performance.coverage.rowsTitle')">
                    <span data-testid="coverage-page-info">{{ t('performance.pageInfo', { from: coveragePageNumber() * pageSize + 1, to: coveragePageEnd(), total: coverageRows().length }) }}</span>
                    <div class="flex gap-2">
                      <button tmzButton variant="secondary" size="sm" type="button" [disabled]="coveragePageNumber() === 0" (click)="coveragePageNumber.set(coveragePageNumber() - 1)" data-testid="coverage-previous">{{ t('performance.previous') }}</button>
                      <button tmzButton variant="secondary" size="sm" type="button" [disabled]="coveragePageEnd() >= coverageRows().length" (click)="coveragePageNumber.set(coveragePageNumber() + 1)" data-testid="coverage-next">{{ t('performance.next') }}</button>
                    </div>
                  </nav>
                }
              }
            }
          }
        </tmz-card>
      } @else if (loading()) {
        <div class="flex flex-col gap-6" aria-busy="true" data-testid="performance-loading">
          @for (card of [1, 2, 3]; track card) {
            <tmz-card><div class="flex flex-col gap-3"><tmz-skeleton class="w-1/3" /><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div></tmz-card>
          }
        </div>
      }
    </div>
  `,
})
export class ProjectPerformance {
  private readonly service = inject(PerformanceService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);
  protected readonly context = inject(ProjectContext);

  protected readonly pageSize = pageSize;
  protected readonly chartHeight = chartHeight;
  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly data = signal<PerformanceDashboard | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string>>({});
  protected readonly asTable = signal(false);

  protected readonly enumeratorSort = signal<EnumeratorSort>('submissions');
  protected readonly enumeratorPageNumber = signal(0);
  protected readonly coverageSort = signal<CoverageSort>('frame');
  protected readonly coveragePageNumber = signal(0);

  private request: Subscription | null = null;

  protected readonly chart = computed(() => {
    const days = this.data()?.submissions.days ?? [];
    const peak = Math.max(1, ...days.map((d) => d.count));
    const bars = days.map((d, i) => {
      const height = (d.count / peak) * chartHeight;
      return { day: d.day, count: d.count, x: i * slotWidth + 1, width: slotWidth - 2, height, y: chartHeight - height };
    });
    return { bars, peak: Math.max(0, ...days.map((d) => d.count)), width: Math.max(days.length, minSlots) * slotWidth };
  });

  protected readonly warnings = computed(() => (this.data()?.coverage.dimensions ?? []).filter((d) => d.warning !== null));

  protected readonly enumeratorRows = computed<EnumeratorRow[]>(() => {
    const rows = this.data()?.enumerators.rows ?? [];
    const sort = this.enumeratorSort();
    const byName = (a: EnumeratorRow, b: EnumeratorRow) => (a.name ?? '').localeCompare(b.name ?? '', undefined, { sensitivity: 'base' });
    const named = rows.filter((r) => r.name !== null).sort(sort === 'name' ? byName : (a, b) => b.submissions - a.submissions || byName(a, b));
    return [...named, ...rows.filter((r) => r.name === null)];
  });
  protected readonly enumeratorPage = computed(() => this.enumeratorRows().slice(this.enumeratorPageNumber() * pageSize, (this.enumeratorPageNumber() + 1) * pageSize));
  protected readonly enumeratorPageEnd = computed(() => Math.min((this.enumeratorPageNumber() + 1) * pageSize, this.enumeratorRows().length));

  protected readonly coverageRows = computed<CoverageRow[]>(() => {
    const rows = this.data()?.coverage.rows ?? [];
    const sort = this.coverageSort();
    if (sort === 'frame') {
      return rows;
    }
    const direction = sort === 'percentAsc' ? 1 : -1;
    // Rows without a percentage (target 0) go last either way.
    return [...rows].sort((a, b) => {
      if (a.percent === null || b.percent === null) {
        return (a.percent === null ? 1 : 0) - (b.percent === null ? 1 : 0);
      }
      return (a.percent - b.percent) * direction;
    });
  });
  protected readonly coveragePage = computed(() => this.coverageRows().slice(this.coveragePageNumber() * pageSize, (this.coveragePageNumber() + 1) * pageSize));
  protected readonly coveragePageEnd = computed(() => Math.min((this.coveragePageNumber() + 1) * pageSize, this.coverageRows().length));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    this.load();
  }

  protected setFrom(value: string): void {
    this.from.set(value);
    this.load();
  }

  protected setTo(value: string): void {
    this.to.set(value);
    this.load();
  }

  protected clear(): void {
    this.from.set('');
    this.to.set('');
    this.load();
  }

  protected setEnumeratorSort(value: string): void {
    this.enumeratorSort.set(value === 'name' ? 'name' : 'submissions');
    this.enumeratorPageNumber.set(0);
  }

  protected setCoverageSort(value: string): void {
    this.coverageSort.set(value === 'percentAsc' || value === 'percentDesc' ? value : 'frame');
    this.coveragePageNumber.set(0);
  }

  /** Asks for the figures again with the range in the inputs; a request still in flight is abandoned. */
  protected load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    this.request = this.service.get(this.context.project()!.id, this.from() || null, this.to() || null).subscribe({
      next: (dashboard) => {
        this.data.set(dashboard);
        this.enumeratorPageNumber.set(0);
        this.coveragePageNumber.set(0);
        this.loading.set(false);
      },
      error: (failure) => {
        const parsed = this.errors.parse(failure);
        const rangeErrors = { ...(parsed.fieldErrors['from'] ? { from: parsed.fieldErrors['from'] } : {}), ...(parsed.fieldErrors['to'] ? { to: parsed.fieldErrors['to'] } : {}) };
        this.fieldErrors.set(rangeErrors);
        this.error.set(Object.keys(rangeErrors).length ? null : parsed.message);
        this.data.set(null);
        this.loading.set(false);
      },
    });
  }

  protected cap(percent: number): number {
    return Math.min(100, Math.max(0, percent));
  }

  /** The fences can fall below zero, which no duration does; they are shown from zero. */
  protected fenceMinutes(seconds: number): number {
    return Math.max(0, seconds) / 60;
  }

  /** "45 s", "24 min" or "1 h 05 min". */
  protected duration(seconds: number | null): string {
    if (seconds === null) {
      return '—';
    }
    const whole = Math.round(seconds);
    if (whole < 60) {
      return this.transloco.translate('performance.duration.seconds', { n: String(whole) });
    }
    const minutes = Math.round(whole / 60);
    if (minutes < 60) {
      return this.transloco.translate('performance.duration.minutes', { n: String(minutes) });
    }
    return this.transloco.translate('performance.duration.hours', {
      h: String(Math.floor(minutes / 60)),
      m: String(minutes % 60).padStart(2, '0'),
    });
  }

  protected chartLabel(t: (key: string, params?: Record<string, unknown>) => string): string {
    const days = this.data()?.submissions.days ?? [];
    if (!days.length) {
      return '';
    }
    return t('performance.submissions.chartLabel', {
      first: days[0].day,
      last: days[days.length - 1].day,
      days: days.length,
      total: this.data()!.submissions.total,
      peak: this.chart().peak,
    });
  }
}
