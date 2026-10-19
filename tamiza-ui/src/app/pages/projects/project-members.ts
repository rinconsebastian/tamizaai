import { Dialog } from '@angular/cdk/dialog';
import { Component, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { forkJoin, of } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { CurrentUserService } from '../../core/current-user.service';
import { countKey } from '../../core/i18n/plural';
import { Invitation, Member, ProjectRole, ProjectsService } from '../../core/projects/projects.service';
import { SystemConfigService } from '../../core/system-config';
import { Badge, Button, ConfirmDialog, DropdownMenu, MenuItem, Skeleton, ToastService, dateTimeText } from '../../shared/ui';
import { AddMemberData, AddMemberDialog, AddedMember } from './add-member-dialog';
import { ProjectContext } from './project-context';

const roles: ProjectRole[] = ['admin', 'analyst', 'viewer'];

/**
 * The project's people: one list at every width with the members first and, for admins, the people invited who have not signed
 * in yet. Everyone reads the members; an admin adds a person in a dialog and changes roles, removes and revokes from each row's menu.
 */
@Component({
  selector: 'app-project-members',
  imports: [TranslocoDirective, Badge, Button, DropdownMenu, Skeleton],
  template: `
    <section *transloco="let t">
      <div class="mb-3 flex items-center justify-between gap-3">
        <h2 class="text-lg font-semibold text-heading">{{ t('members.title') }}</h2>
        @if (isAdmin()) {
          <button tmzButton type="button" class="shrink-0" (click)="addMember()" data-testid="add-member">{{ t('members.add') }}</button>
        }
      </div>

      @if (loading()) {
        <div aria-busy="true" class="flex flex-col gap-3" data-testid="members-loading"><tmz-skeleton class="w-full" /><tmz-skeleton class="w-2/3" /></div>
      } @else {
        <ul class="flex flex-col divide-y divide-default rounded-base border border-default bg-neutral-primary-soft" data-testid="member-list">
          @for (member of members(); track member.userId) {
            <li class="flex items-center gap-3 px-4 py-3" [attr.data-testid]="'member-row-' + member.userId">
              <div class="min-w-0 flex-1">
                <p class="break-words font-medium text-heading">
                  {{ member.name }}
                  @if (isYou(member)) {
                    <tmz-badge class="ms-1" data-testid="you-badge">{{ t('members.you') }}</tmz-badge>
                  }
                </p>
                @if (member.email) {
                  <p class="break-all text-sm text-body">{{ member.email }}</p>
                }
              </div>
              <tmz-badge [tone]="member.role === 'admin' ? 'brand' : 'neutral'">{{ t('roles.' + member.role) }}</tmz-badge>
              @if (isAdmin() && memberActions(member).length) {
                <tmz-dropdown-menu [label]="t('members.actionsFor', { name: member.name })" [items]="memberActions(member)" (selected)="onMemberAction(member, $event)" />
              }
            </li>
          }
          @for (invitation of invitations(); track invitation.id) {
            <li class="flex items-center gap-3 px-4 py-3" [attr.data-testid]="'invitation-row-' + invitation.id">
              <div class="min-w-0 flex-1">
                <p class="break-all font-medium text-heading">{{ invitation.email }}</p>
                @if (invitation.lastEmailAt; as sentAt) {
                  <p class="text-sm text-body" [attr.data-testid]="'email-sent-' + invitation.id">{{ t('members.emailSentAt', { when: sentText(sentAt) }) }}</p>
                }
              </div>
              <tmz-badge tone="warning">{{ t('members.pendingRole', { role: t('roles.' + invitation.role) }) }}</tmz-badge>
              <tmz-dropdown-menu [label]="t('members.actionsFor', { name: invitation.email })" [items]="invitationActions()" (selected)="onInvitationAction(invitation, $event)" />
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class ProjectMembers {
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly dialog = inject(Dialog);
  private readonly context = inject(ProjectContext);
  private readonly currentUser = inject(CurrentUserService).user;
  private readonly locale = inject(LOCALE_ID);
  /** Whether the identity provider can email people: invitations, resends and password resets. */
  protected readonly emailsEnabled = inject(SystemConfigService).accountEmailsEnabled;

  protected readonly members = signal<Member[]>([]);
  protected readonly invitations = signal<Invitation[]>([]);
  protected readonly loading = signal(true);
  /** The project holds exactly one admin: the rule that it keeps at least one is visible in that row's menu. */
  private readonly onlyAdmin = computed(() => {
    const admins = this.members().filter((m) => m.role === 'admin');
    return admins.length === 1 ? admins[0].userId : null;
  });

  constructor() {
    this.load();
  }

  protected isAdmin(): boolean {
    return this.context.can('admin');
  }

  /** The row of the signed-in person; nothing is marked until the profile is known. */
  protected isYou(member: Member): boolean {
    return this.currentUser()?.id === member.userId;
  }

  /** What an admin can do to a member. The only admin keeps their role and their place, as the API insists. */
  protected memberActions(member: Member): MenuItem[] {
    const t = (key: string) => this.transloco.translate(key);
    const protectedAdmin = member.userId === this.onlyAdmin();
    const items: MenuItem[] = protectedAdmin
      ? []
      : roles.filter((role) => role !== member.role).map((role) => ({ id: role, label: t('members.makeRole.' + role) }));
    if (this.emailsEnabled()) {
      items.push({ id: 'passwordReset', label: t('members.passwordReset'), testId: 'password-reset' });
    }
    if (!protectedAdmin) {
      items.push({ id: 'remove', label: t('members.remove'), danger: true });
    }
    return items;
  }

  protected invitationActions(): MenuItem[] {
    const t = (key: string) => this.transloco.translate(key);
    return [
      ...(this.emailsEnabled() ? [{ id: 'resend', label: t('members.resendEmail'), testId: 'resend-email' }] : []),
      { id: 'revoke', label: t('members.revoke'), danger: true, testId: 'revoke' },
    ];
  }

  /** When the last invitation email went out, in the interface's date and time format. */
  protected sentText(sentAt: string): string {
    return dateTimeText(sentAt, this.locale) ?? sentAt;
  }

  protected addMember(): void {
    const ref = this.dialog.open<AddedMember, AddMemberData>(AddMemberDialog, {
      data: { projectId: this.projectId() },
      ariaLabelledBy: 'add-member-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: ['cdk-overlay-dark-backdrop'],
    });
    ref.closed.subscribe((added) => {
      if (added) {
        this.announce(added);
        this.load();
      }
    });
  }

  /** Says what the invitation did: a member at once, a saved invitation, and, when account emails are on, what happened to the email. */
  private announce({ result, email }: AddedMember): void {
    const t = (key: string) => this.transloco.translate(key, { email });
    const outcome = result.kind === 'invitation' ? result.emailOutcome : null;
    if (result.kind === 'member') {
      this.toasts.success(t('members.added'));
    } else if (!outcome) {
      this.toasts.success(t('members.invited'));
    } else if (outcome === 'existingAccount') {
      this.toasts.info(t('members.inviteEmail.existingAccount'));
    } else if (outcome === 'rateLimited' || outcome === 'failed') {
      this.toasts.error(t('members.inviteEmail.' + outcome));
    } else {
      this.toasts.success(t('members.inviteEmail.' + outcome));
    }
  }

  protected onInvitationAction(invitation: Invitation, action: string): void {
    if (action === 'resend') {
      this.service.resendInvitationEmail(this.projectId(), invitation.id, this.transloco.getActiveLang()).subscribe({
        next: () => {
          this.toasts.success(this.transloco.translate('members.resent', { email: invitation.email }));
          this.load();
        },
        error: (e) => this.reportEmailFailure(e),
      });
      return;
    }
    void this.revoke(invitation);
  }

  /** A 429 from an account email action says how long to wait, in minutes; anything else is reported as any API failure. */
  private reportEmailFailure(error: unknown): void {
    const failure = this.errors.parse(error);
    const retryAfter = failure.extensions['retryAfter'];
    if (failure.code === 'accountEmail.rateLimited' && typeof retryAfter === 'number' && retryAfter > 0) {
      const minutes = Math.max(1, Math.ceil(retryAfter / 60));
      const key = countKey('errors.codes.accountEmail.rateLimitedIn', minutes, this.transloco.getActiveLang());
      this.toasts.error(this.transloco.translate(key, { minutes }));
      return;
    }
    this.errors.report(error);
  }

  protected async onMemberAction(member: Member, action: string): Promise<void> {
    if (action === 'passwordReset') {
      this.service.sendPasswordReset(this.projectId(), member.userId).subscribe({
        next: () => this.toasts.success(this.transloco.translate('members.passwordResetSent', { email: member.email ?? member.name })),
        error: (e) => this.reportEmailFailure(e),
      });
      return;
    }
    if (action === 'remove') {
      const t = (key: string, params?: Record<string, string>) => this.transloco.translate(key, params);
      const self = this.isYou(member);
      const confirmed = await this.confirmDialog.confirm({
        title: self ? t('members.removeSelfTitle') : t('members.removeTitle', { name: member.name }),
        message: self ? t('members.removeSelfMessage') : t('members.removeMessage'),
        confirmLabel: t('members.remove'),
        cancelLabel: t('common.cancel'),
        tone: 'danger',
      });
      if (confirmed) {
        this.service.removeMember(this.projectId(), member.userId).subscribe({ next: () => this.load(), error: (e) => this.errors.report(e) });
      }
      return;
    }
    this.service.changeMemberRole(this.projectId(), member.userId, action as ProjectRole).subscribe({
      next: () => this.load(),
      error: (e) => this.errors.report(e),
    });
  }

  protected async revoke(invitation: Invitation): Promise<void> {
    const t = (key: string, params?: Record<string, string>) => this.transloco.translate(key, params);
    const confirmed = await this.confirmDialog.confirm({
      title: t('members.revokeTitle', { email: invitation.email }),
      message: t('members.revokeMessage'),
      confirmLabel: t('members.revoke'),
      cancelLabel: t('common.cancel'),
      tone: 'danger',
    });
    if (confirmed) {
      this.service.revokeInvitation(this.projectId(), invitation.id).subscribe({ next: () => this.load(), error: (e) => this.errors.report(e) });
    }
  }

  private projectId(): string {
    return this.context.project()!.id;
  }

  private load(): void {
    const id = this.projectId();
    forkJoin([this.service.members(id), this.isAdmin() ? this.service.invitations(id) : of([])]).subscribe({
      next: ([members, invitations]) => {
        this.members.set(members);
        this.invitations.set(invitations);
        this.loading.set(false);
      },
      error: (e) => {
        this.errors.report(e);
        this.loading.set(false);
      },
    });
  }
}
