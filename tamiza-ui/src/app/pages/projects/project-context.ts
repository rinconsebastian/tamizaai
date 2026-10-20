import { Injectable, computed, inject, signal } from '@angular/core';
import { EMPTY, Observable, tap } from 'rxjs';
import { DashboardEntry, FormChoice, FormField, ProjectDetails, ProjectRole, ProjectsService, hasRole } from '../../core/projects/projects.service';

/**
 * What the dashboard page says after opening another dashboard than the one asked for, which no longer exists (design D10), or
 * after a change to the dashboards the API refused (design D12): `dashboardId` is the dashboard shown, and the notice is shown only
 * while it is open.
 */
export interface DashboardNotice {
  dashboardId: string;
  text: string;
}

/** The project open on the project page, shared by its sections. */
@Injectable()
export class ProjectContext {
  private readonly service = inject(ProjectsService);

  readonly project = signal<ProjectDetails | null>(null);
  /** The project's own dashboards, in the order of their tabs; part of the details, so there is one source. */
  readonly dashboards = computed(() => this.project()?.dashboards ?? []);
  /** Kept here rather than on the dashboard page, so it survives the redirect to the dashboard it names. */
  readonly notice = signal<DashboardNotice | null>(null);
  readonly formFields = signal<FormField[] | null>(null);
  /** The form's choice lists, read once per form check by the pages that need them. */
  readonly formChoices = signal<FormChoice[] | null>(null);
  /** The last read of the choices failed; `loadFormChoices()` tries again. */
  readonly formChoicesFailed = signal(false);
  readonly role = computed(() => this.project()?.myRole);

  /** Bumped when the form check changes, so a read started for the previous form is dropped. */
  private formGeneration = 0;
  private loadingChoices = false;

  can(minimum: ProjectRole): boolean {
    return hasRole(this.role(), minimum);
  }

  loadFormFields(): void {
    const project = this.project();
    if (project && this.formFields() === null) {
      this.service.formFields(project.id).subscribe((fields) => this.formFields.set(fields));
    }
  }

  /** Reads the choices unless they are loaded or being read; after a failure, reads them again. */
  loadFormChoices(): void {
    const project = this.project();
    if (!project || this.formChoices() !== null || this.loadingChoices) {
      return;
    }
    const generation = this.formGeneration;
    this.loadingChoices = true;
    this.formChoicesFailed.set(false);
    this.service.formChoices(project.id).subscribe({
      next: (choices) => {
        if (generation === this.formGeneration) {
          this.loadingChoices = false;
          this.formChoices.set(choices);
        }
      },
      error: () => {
        if (generation === this.formGeneration) {
          this.loadingChoices = false;
          this.formChoicesFailed.set(true);
        }
      },
    });
  }

  /**
   * Reads the details again, for changes made outside the Overview that its checks depend on (the sampling frame).
   * A failure keeps the details already shown: the change that prompted this has been reported by its own page.
   */
  refresh(): void {
    const project = this.project();
    if (project) {
      this.service.get(project.id).subscribe({ next: (fresh) => this.update(fresh), error: () => undefined });
    }
  }

  /**
   * Reads the details again when the subscriber needs the fresh ones (a dashboard another member removed or reordered): they are
   * applied, then emitted. A failure keeps the details already shown and reaches the subscriber.
   */
  reload(): Observable<ProjectDetails> {
    const project = this.project();
    return project ? this.service.get(project.id).pipe(tap((fresh) => this.update(fresh))) : EMPTY;
  }

  /** Replaces the list of dashboards after a change the API answered with it, or one this page knows of (a dashboard found removed). */
  setDashboards(dashboards: DashboardEntry[]): void {
    const project = this.project();
    if (project) {
      this.project.set({ ...project, dashboards });
    }
  }

  /**
   * Replaces the details after an update; a new form check invalidates the field list and the choices. A viewer's details carry
   * no check time (`null`), so for them it never changes.
   */
  update(project: ProjectDetails): void {
    if (project.formCheckedAt !== this.project()?.formCheckedAt) {
      this.formFields.set(null);
      this.formChoices.set(null);
      this.formChoicesFailed.set(false);
      this.formGeneration++;
      this.loadingChoices = false;
    }
    this.project.set(project);
  }
}
