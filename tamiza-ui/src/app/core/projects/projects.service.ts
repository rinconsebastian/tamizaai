import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type ProjectRole = 'admin' | 'analyst' | 'viewer';

export const roleRank: Record<ProjectRole, number> = { viewer: 1, analyst: 2, admin: 3 };

export function hasRole(role: ProjectRole | undefined, minimum: ProjectRole): boolean {
  return !!role && roleRank[role] >= roleRank[minimum];
}

export interface ProjectSummary {
  id: string;
  name: string;
  formName: string;
  myRole: ProjectRole;
}

export interface MetricAvailability {
  metric: 'dailyEvolution' | 'completionTime' | 'perEnumerator' | 'coverage';
  available: boolean;
  missingFields: string[];
  hint: string | null;
}

export interface ProjectDetails {
  id: string;
  name: string;
  koboServerUrl: string;
  assetUid: string;
  formName: string;
  formCheckedAt: string;
  enumeratorField: string | null;
  fieldCheck: MetricAvailability[];
  myRole: ProjectRole;
  tokenUnreadable: boolean;
  createdAt: string;
}

export interface WebhookSettings {
  url: string;
  username: string;
  secret: string | null;
  secretUnreadable: boolean;
}

export interface CreatedProject {
  project: ProjectDetails;
  webhook: WebhookSettings;
}

export interface CreateProjectRequest {
  name: string;
  koboServerUrl: string;
  assetUid: string;
  apiToken: string;
}

export interface FormField {
  name: string;
  xpath: string;
  type: string;
  label: string | null;
}

export interface Member {
  userId: string;
  name: string;
  email: string | null;
  role: ProjectRole;
}

export interface Invitation {
  id: string;
  email: string;
  role: ProjectRole;
  createdAt: string;
}

export interface InviteResult {
  kind: 'member' | 'invitation';
  member: Member | null;
  invitation: Invitation | null;
}

export interface SamplingDimension {
  name: string;
  field: string;
}

export interface SamplingTarget {
  values: string[];
  target: number;
}

export interface SamplingFrame {
  dimensions: SamplingDimension[];
  targets: SamplingTarget[];
  updatedAt: string;
}

/** Body for saving a frame; targets may be invalid while editing, and the API reports why. */
export interface SamplingFrameInput {
  dimensions: SamplingDimension[];
  targets: { values: string[]; target: number | null }[];
}

export interface ImportPreview {
  dimensions: string[];
  rows: { row: number; values: string[]; target: number | null }[];
  errors: { row: number; message: string }[];
}

const base = '/api/v1/projects';

@Injectable({ providedIn: 'root' })
export class ProjectsService {
  private readonly http = inject(HttpClient);

  list(): Observable<ProjectSummary[]> {
    return this.http.get<ProjectSummary[]>(base);
  }

  create(request: CreateProjectRequest): Observable<CreatedProject> {
    return this.http.post<CreatedProject>(base, request);
  }

  get(id: string): Observable<ProjectDetails> {
    return this.http.get<ProjectDetails>(`${base}/${id}`);
  }

  update(id: string, changes: { name?: string; enumeratorField?: string }): Observable<ProjectDetails> {
    return this.http.patch<ProjectDetails>(`${base}/${id}`, changes);
  }

  replaceToken(id: string, apiToken: string): Observable<ProjectDetails> {
    return this.http.put<ProjectDetails>(`${base}/${id}/kobo-token`, { apiToken });
  }

  recheck(id: string): Observable<ProjectDetails> {
    return this.http.post<ProjectDetails>(`${base}/${id}/kobo-check`, null);
  }

  formFields(id: string): Observable<FormField[]> {
    return this.http.get<FormField[]>(`${base}/${id}/form-fields`);
  }

  webhook(id: string): Observable<WebhookSettings> {
    return this.http.get<WebhookSettings>(`${base}/${id}/webhook`);
  }

  regenerateSecret(id: string): Observable<WebhookSettings> {
    return this.http.post<WebhookSettings>(`${base}/${id}/webhook/secret`, null);
  }

  members(id: string): Observable<Member[]> {
    return this.http.get<Member[]>(`${base}/${id}/members`);
  }

  invitations(id: string): Observable<Invitation[]> {
    return this.http.get<Invitation[]>(`${base}/${id}/invitations`);
  }

  invite(id: string, email: string, role: ProjectRole): Observable<InviteResult> {
    return this.http.post<InviteResult>(`${base}/${id}/members`, { email, role });
  }

  changeMemberRole(id: string, userId: string, role: ProjectRole): Observable<Member> {
    return this.http.patch<Member>(`${base}/${id}/members/${userId}`, { role });
  }

  removeMember(id: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${base}/${id}/members/${userId}`);
  }

  changeInvitationRole(id: string, invitationId: string, role: ProjectRole): Observable<Invitation> {
    return this.http.patch<Invitation>(`${base}/${id}/invitations/${invitationId}`, { role });
  }

  revokeInvitation(id: string, invitationId: string): Observable<void> {
    return this.http.delete<void>(`${base}/${id}/invitations/${invitationId}`);
  }

  samplingFrame(id: string): Observable<SamplingFrame | null> {
    return this.http.get<SamplingFrame | null>(`${base}/${id}/sampling-frame`);
  }

  saveSamplingFrame(id: string, frame: SamplingFrameInput): Observable<SamplingFrame> {
    return this.http.put<SamplingFrame>(`${base}/${id}/sampling-frame`, frame);
  }

  importSamplingFrame(id: string, file: File): Observable<ImportPreview> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ImportPreview>(`${base}/${id}/sampling-frame/import`, form);
  }
}
