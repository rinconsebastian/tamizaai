import { HttpClient, HttpParams } from '@angular/common/http';
import { InjectionToken, Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type SyncKind = 'incremental' | 'full' | 'reconciliation';
export type SyncTrigger = 'schedule' | 'manual' | 'initial';
export type SyncStatus = 'queued' | 'running' | 'succeeded' | 'failed';

export interface SyncItem {
  id: string;
  kind: SyncKind;
  trigger: SyncTrigger;
  status: SyncStatus;
  requestedBy: { id: string; name: string } | null;
  queuedAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  createdCount: number;
  updatedCount: number;
  deletedCount: number;
  errorCode: string | null;
  error: string | null;
}

export interface SyncPage {
  items: SyncItem[];
  total: number;
}

export interface IngestionStatus {
  submissionCount: number;
  deletedCount: number;
  lastWebhookAt: string | null;
  lastSync: SyncItem | null;
  activeSync: SyncItem | null;
  nextSyncAt: string;
  nextReconcileAt: string;
  syncIntervalHours: number;
  reconcileIntervalHours: number;
}

export interface IngestionSettings {
  syncIntervalHours?: number | null;
  reconcileIntervalHours?: number | null;
}

/** How often the Data section refreshes while a synchronization is queued or running. */
export const INGESTION_POLL_MS = new InjectionToken<number>('INGESTION_POLL_MS', { factory: () => 5000 });

const base = '/api/v1/projects';

@Injectable({ providedIn: 'root' })
export class IngestionService {
  private readonly http = inject(HttpClient);

  status(projectId: string): Observable<IngestionStatus> {
    return this.http.get<IngestionStatus>(`${base}/${projectId}/ingestion`);
  }

  updateSettings(projectId: string, settings: IngestionSettings): Observable<IngestionStatus> {
    return this.http.patch<IngestionStatus>(`${base}/${projectId}/ingestion`, settings);
  }

  /** "Sync now" (`incremental`) or a full resynchronization. */
  startSync(projectId: string, kind: 'incremental' | 'full'): Observable<SyncItem> {
    return this.http.post<SyncItem>(`${base}/${projectId}/syncs`, { kind });
  }

  syncs(projectId: string, page: number, pageSize: number): Observable<SyncPage> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<SyncPage>(`${base}/${projectId}/syncs`, { params });
  }
}
