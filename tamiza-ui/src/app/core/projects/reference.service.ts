import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Person } from './pipeline.service';

/** Reference table column types, detected from the uploaded values. */
export type ReferenceColumnType = 'integer' | 'decimal' | 'date' | 'text';

export interface ReferenceColumn {
  name: string;
  header: string;
  type: ReferenceColumnType;
}

export interface ReferenceTable {
  name: string;
  fileName: string;
  rowCount: number;
  columns: ReferenceColumn[];
  updatedBy: Person;
  createdAt: string;
  updatedAt: string;
}

/** An uploaded or replaced table, with the headers whose column names differ from them. */
export interface ReferenceTableUpload {
  table: ReferenceTable;
  renamed: { header: string; name: string }[];
}

export interface ReferenceTablePreview {
  table: ReferenceTable;
  rows: unknown[][];
}

export const maxReferenceFileBytes = 10 * 1024 * 1024;

const base = '/api/v1/projects';

@Injectable({ providedIn: 'root' })
export class ReferenceService {
  private readonly http = inject(HttpClient);

  list(projectId: string): Observable<ReferenceTable[]> {
    return this.http.get<ReferenceTable[]>(`${base}/${projectId}/reference-tables`);
  }

  preview(projectId: string, name: string): Observable<ReferenceTablePreview> {
    return this.http.get<ReferenceTablePreview>(`${base}/${projectId}/reference-tables/${name}`);
  }

  upload(projectId: string, name: string, file: File): Observable<ReferenceTableUpload> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('name', name);
    return this.http.post<ReferenceTableUpload>(`${base}/${projectId}/reference-tables`, form);
  }

  replace(projectId: string, name: string, file: File): Observable<ReferenceTableUpload> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.put<ReferenceTableUpload>(`${base}/${projectId}/reference-tables/${name}`, form);
  }

  remove(projectId: string, name: string): Observable<void> {
    return this.http.delete<void>(`${base}/${projectId}/reference-tables/${name}`);
  }
}

/** A table name suggested from a file name: `Población 2026.xlsx` → `poblacion_2026`. The API has the final say. */
export function suggestedTableName(fileName: string): string {
  const reduced = fileName
    .replace(/\.[^.]*$/, '')
    .normalize('NFKD')
    .replace(/[^\x00-\x7f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9_]+/g, '_')
    .replace(/_+/g, '_')
    .replace(/^_+|_+$/g, '');
  const named = /^[a-z]/.test(reduced) ? reduced : `table_${reduced}`.replace(/_+$/, '');
  return named.slice(0, 63);
}
