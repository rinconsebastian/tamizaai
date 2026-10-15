import { Dialog } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { forkJoin, of } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { CurrentUserService } from '../../core/current-user.service';
import { Invitation, Member, ProjectRole, ProjectsService } from '../../core/projects/projects.service';
import { Badge, Button, ConfirmDialog, DropdownMenu, MenuItem, Skeleton, ToastService } from '../../shared/ui';
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
              </div>
              <tmz-badge tone="warning">{{ t('members.pendingRole', { role: t('roles.' + invitation.role) }) }}</tmz-badge>
              <tmz-dropdown-menu [label]="t('members.actionsFor', { name: invitation.email })" [items]="invitationActions()" (selected)="revoke(invitation)" />
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
    if (member.userId === this.onlyAdmin()) {
      return [];
    }
    const t = (key: string) => this.transloco.translate(key);
    return [
      ...roles.filter((role) => role !== member.role).map((role) => ({ id: role, label: t('members.makeRole.' + role) })),
      { id: 'remove', label: t('members.remove'), danger: true },
    ];
  }

  protected invitationActions(): MenuItem[] {
    return [{ id: 'revoke', label: this.transloco.translate('members.revoke'), danger: true, testId: 'revoke' }];
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
        const key = added.result.kind === 'member' ? 'members.added' : 'members.invited';
        this.toasts.success(this.transloco.translate(key, { email: added.email }));
        this.load();
      }
    });
  }

  protected async onMemberAction(member: Member, action: string): Promise<void> {
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
