import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ApiErrors } from '../../core/api-error';
import { SystemConfigService } from '../../core/system-config';
import { InviteResult, ProjectRole, ProjectsService } from '../../core/projects/projects.service';
import { Alert, ApiLang, Button, FormField, Input } from '../../shared/ui';

export interface AddMemberData {
  projectId: string;
}

/** What the dialog closes with: the API's answer (a member at once, or a pending invitation) and the email it was given. */
export interface AddedMember {
  result: InviteResult;
  email: string;
}

const roles: ProjectRole[] = ['admin', 'analyst', 'viewer'];

/**
 * Asks for the email and role of a person to add to the project. A refusal stays in the dialog (a field message next to its
 * field, anything else in an alert) with what was typed kept.
 */
@Component({
  selector: 'app-add-member-dialog',
  imports: [TranslocoDirective, Alert, ApiLang, Button, FormField, Input],
  template: `
    <form
      class="w-[min(30rem,calc(100vw-2rem))] rounded-base border border-default bg-neutral-primary-soft p-4 shadow-sm md:p-6"
      novalidate
      (submit)="$event.preventDefault(); submit(email.value, role.value)"
      *transloco="let t"
      data-testid="invite-form"
    >
      <h2 id="add-member-title" class="text-lg font-medium text-heading">{{ t('members.inviteTitle') }}</h2>

      <tmz-form-field class="mt-4" [label]="t('members.email')" fieldId="inviteEmail" [hint]="emailsEnabled() ? t('members.emailHintInvite') : t('members.emailHint')" [error]="fieldErrors()['email']">
        <input tmzInput #email id="inviteEmail" type="email" autocomplete="off" />
      </tmz-form-field>
      <tmz-form-field class="mt-4" [label]="t('members.role')" fieldId="inviteRole" [error]="fieldErrors()['role']">
        <select tmzInput #role id="inviteRole">
          @for (option of roles; track option) {
            <option [value]="option" [selected]="option === 'analyst'">{{ t('roles.' + option) }}</option>
          }
        </select>
      </tmz-form-field>

      @if (error(); as message) {
        <tmz-alert tone="danger" class="mt-4" [tmzApiLang]="message" data-testid="invite-error">{{ message }}</tmz-alert>
      }

      <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <button tmzButton variant="secondary" type="button" (click)="ref.close()" data-testid="invite-cancel">{{ t('common.cancel') }}</button>
        <button tmzButton type="submit" [disabled]="busy()" data-testid="invite-submit">{{ t('members.invite') }}</button>
      </div>
    </form>
  `,
})
export class AddMemberDialog {
  private readonly data = inject<AddMemberData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<AddedMember>>(DialogRef);
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly transloco = inject(TranslocoService);

  protected readonly roles = roles;
  /** Whether the identity provider emails the people invited; the hint under the email field says so. */
  protected readonly emailsEnabled = inject(SystemConfigService).accountEmailsEnabled;
  protected readonly busy = signal(false);
  protected readonly fieldErrors = signal<Record<string, string>>({});
  protected readonly error = signal<string | null>(null);

  protected submit(email: string, role: string): void {
    this.fieldErrors.set({});
    this.error.set(null);
    this.busy.set(true);
    this.service.invite(this.data.projectId, email, role as ProjectRole, this.transloco.getActiveLang()).subscribe({
      next: (result) => this.ref.close({ result, email }),
      error: (failure) => {
        this.busy.set(false);
        const parsed = this.errors.parse(failure);
        this.fieldErrors.set(parsed.fieldErrors);
        if (!Object.keys(parsed.fieldErrors).length) {
          this.error.set(parsed.message);
        }
      },
    });
  }
}
