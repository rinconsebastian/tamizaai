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
  /** What to do about an unavailable metric; null when available or when there is nothing to suggest. */
  hint: 'selectEnumeratorField' | 'defineSamplingFrame' | 'updateSamplingFrame' | null;
}

/** One of the project's own dashboards: its name is shown as typed, never translated, and its id makes its address. */
export interface DashboardEntry {
  id: string;
  name: string;
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
  /** Form versions used by the project's submissions whose definition (and so labels) is not available. */
  unavailableFormVersions: number;
  /** IANA name of the zone that defines the dashboard's days; `UTC` until an admin sets another. */
  timeZone: string;
  /** The project's own dashboards, in the order of their tabs: at least one. */
  dashboards: DashboardEntry[];
}

export type FormVersionStatus = 'available' | 'pending' | 'failed' | 'not_found';

/** A form version the project's submissions use, and the state of its definition. */
export interface FormVersionItem {
  uid: string;
  current: boolean;
  deployedAt: string | null;
  submissions: number;
  status: FormVersionStatus;
  errorCode: string | null;
  error: string | null;
  nextAttemptAt: string | null;
}

export interface FormVersionList {
  versions: FormVersionItem[];
  withoutVersion: number;
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
  /** The choice list of a select question, when the form names one. */
  listName?: string | null;
  /** Whether the question accepts "other" (pyxform adds an `other` choice when the form is deployed). */
  orOther?: boolean;
  inRepeat?: boolean;
}

/** A choice of the form's lists: its code (`name`, what the answers store) and its label. */
export interface FormChoice {
  listName: string;
  name: string;
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

/** A column the import did not read: a note (its name starts with `#`) or values under a column without a name. */
export interface IgnoredColumn {
  /** The spreadsheet letter of the column. */
  column: string;
  name: string | null;
  reason: 'note' | 'unnamed';
}

export interface ImportPreview {
  dimensions: string[];
  rows: { row: number; values: string[]; target: number | null }[];
  errors: { row: number; message: string }[];
  /** Row numbers of the rows left out because their target is empty. */
  rowsWithoutTarget: number[];
  ignoredColumns: IgnoredColumn[];
}

/** The questions of an import template; `values` null means every choice of a question with choices. */
export interface FrameTemplateRequest {
  dimensions: { field: string; values: string[] | null }[];
}

/** A prepared sampling frame workbook, downloaded once through its link. */
export interface SamplingFrameFile {
  downloadUrl: string;
  fileName: string;
  rowCount: number;
  expiresAt: string;
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

  update(id: string, changes: { name?: string; enumeratorField?: string; timeZone?: string }): Observable<ProjectDetails> {
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

  /** The choice lists of the form snapshot, in form order. */
  formChoices(id: string): Observable<FormChoice[]> {
    return this.http.get<FormChoice[]>(`${base}/${id}/form-choices`);
  }

  /** The form versions the project's submissions use, with the state of each definition. */
  formVersions(id: string): Observable<FormVersionList> {
    return this.http.get<FormVersionList>(`${base}/${id}/form-versions`);
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

  /** Prepares an import template for the chosen questions. */
  samplingFrameTemplate(id: string, request: FrameTemplateRequest): Observable<SamplingFrameFile> {
    return this.http.post<SamplingFrameFile>(`${base}/${id}/sampling-frame/template`, request);
  }

  /** Prepares the saved frame as a workbook in the template's layout. */
  exportSamplingFrame(id: string): Observable<SamplingFrameFile> {
    return this.http.post<SamplingFrameFile>(`${base}/${id}/sampling-frame/export`, null);
  }
}
