import { Injectable, computed, inject, signal } from '@angular/core';
import { FormField, ProjectDetails, ProjectRole, ProjectsService, hasRole } from '../../core/projects/projects.service';

/** The project open on the project page, shared by its sections. */
@Injectable()
export class ProjectContext {
  private readonly service = inject(ProjectsService);

  readonly project = signal<ProjectDetails | null>(null);
  readonly formFields = signal<FormField[] | null>(null);
  readonly role = computed(() => this.project()?.myRole);

  can(minimum: ProjectRole): boolean {
    return hasRole(this.role(), minimum);
  }

  loadFormFields(): void {
    const project = this.project();
    if (project && this.formFields() === null) {
      this.service.formFields(project.id).subscribe((fields) => this.formFields.set(fields));
    }
  }

  /** Replaces the details after an update; a new form check invalidates the field list. */
  update(project: ProjectDetails): void {
    if (project.formCheckedAt !== this.project()?.formCheckedAt) {
      this.formFields.set(null);
    }
    this.project.set(project);
  }
}
