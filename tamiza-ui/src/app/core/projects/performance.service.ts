import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { MetricAvailability } from './projects.service';

// Mirrors Tamiza.Api/Performance/PerformanceModels.cs. Days are `yyyy-MM-dd` strings (never converted to dates, so no
// time zone can move them), instants are ISO strings, and durations are seconds.

export interface DayCount {
  day: string;
  count: number;
}

export interface SubmissionsSection {
  total: number;
  withoutReceptionTime: number;
  /** From the first to the last day with submissions, days without any included as zero. */
  days: DayCount[];
}

/** Figures are null when unavailable, or when there is no usable duration. */
export interface CompletionTimeSection {
  available: boolean;
  missingFields: string[];
  usableDurations: number | null;
  unusableDurations: number | null;
  outliers: number | null;
  medianSeconds: number | null;
  meanWithoutOutliersSeconds: number | null;
  lowerFenceSeconds: number | null;
  upperFenceSeconds: number | null;
}

export interface EnumeratorRow {
  /** One of the spellings seen; null for submissions with no enumerator. */
  name: string | null;
  submissions: number;
  meanSeconds: number | null;
  outliers: number;
  lastSubmissionAt: string | null;
}

export interface EnumeratorsSection {
  available: boolean;
  hint: string | null;
  rows: EnumeratorRow[];
}

export type DimensionWarning = 'missingField' | 'repeat' | 'multipleChoice';

export interface CoverageDimension {
  name: string;
  field: string;
  warning: DimensionWarning | null;
}

export interface CoverageValue {
  code: string;
  label: string | null;
}

export interface CoverageRow {
  /** One per dimension, in dimension order. */
  values: CoverageValue[];
  target: number;
  achieved: number;
  /** In percent (75 is 75%), not capped; null when the target is 0. */
  percent: number | null;
}

export interface OutsideCombination {
  /** One per dimension; null where the answer was empty. */
  values: (CoverageValue | null)[];
  submissions: number;
}

export interface CoverageSection {
  state: 'notConfigured' | 'configured';
  dimensions: CoverageDimension[];
  rows: CoverageRow[];
  /** In percent; null when no row has a target above 0. */
  overallPercent: number | null;
  outsideFrame: number;
  outsideTop: OutsideCombination[];
}

export interface PerformanceDashboard {
  computedAt: string;
  timeZone: string;
  from: string | null;
  to: string | null;
  availability: MetricAvailability[];
  submissions: SubmissionsSection;
  completionTime: CompletionTimeSection;
  enumerators: EnumeratorsSection;
  coverage: CoverageSection;
}

const base = '/api/v1/projects';

@Injectable({ providedIn: 'root' })
export class PerformanceService {
  private readonly http = inject(HttpClient);

  /** The dashboard of a project; `from` and `to` are optional `yyyy-MM-dd` days, both included. */
  get(projectId: string, from?: string | null, to?: string | null): Observable<PerformanceDashboard> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    return this.http.get<PerformanceDashboard>(`${base}/${projectId}/performance`, { params });
  }
}
