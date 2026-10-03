import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { forkJoin, of } from 'rxjs';
import { ApiErrors } from '../../core/api-error';
import { Invitation, Member, ProjectRole, ProjectsService } from '../../core/projects/projects.service';
import {
  Alert,
  Badge,
  Button,
  Card,
  ConfirmDialog,
  DropdownMenu,
  FormField,
  Input,
  MenuItem,
  Table,
  TableContainer,
  ToastService,
  tableCell,
  tableHead,
  tableHeaderCell,
  tableRow,
} from '../../shared/ui';
import { ProjectContext } from './project-context';

const roles: ProjectRole[] = ['admin', 'analyst', 'viewer'];

@Component({
  selector: 'app-project-members',
  imports: [TranslocoDirective, Alert, Badge, Button, Card, DropdownMenu, FormField, Input, Table, TableContainer],
  template: `
    <div class="flex flex-col gap-6" *transloco="let t">
      @if (isAdmin()) {
        <tmz-card [heading]="t('members.inviteTitle')">
          <p class="mb-4 text-sm text-body">{{ t('members.inviteIntro') }}</p>
          <form class="flex flex-col gap-3 lg:flex-row lg:items-start" (submit)="$event.preventDefault(); invite(email.value, inviteRole.value)" data-testid="invite-form">
            <tmz-form-field class="flex-1" [label]="t('members.email')" fieldId="inviteEmail" [error]="inviteErrors()['email']">
              <input tmzInput #email id="inviteEmail" type="email" autocomplete="off" />
            </tmz-form-field>
            <tmz-form-field class="lg:w-48" [label]="t('members.role')" fieldId="inviteRole" [error]="inviteErrors()['role']">
              <select tmzInput #inviteRole id="inviteRole">
                @for (role of roles; track role) {
                  <option [value]="role" [selected]="role === 'analyst'">{{ t('roles.' + role) }}</option>
                }
              </select>
            </tmz-form-field>
            <button tmzButton type="submit" class="lg:mt-7" [disabled]="busy()">{{ t('members.invite') }}</button>
          </form>
          @if (inviteError(); as message) {
            <tmz-alert tone="danger" class="mt-4" data-testid="invite-error">{{ message }}</tmz-alert>
          }
        </tmz-card>
      }

      <section>
        <h2 class="mb-3 text-lg font-semibold text-heading">{{ t('members.title') }}</h2>
        <!-- Phones: stacked cards. md and up: a table. -->
        <ul class="flex flex-col gap-3 md:hidden" data-testid="member-cards">
          @for (member of members(); track member.userId) {
            <li class="flex items-start justify-between gap-3 rounded-base border border-default bg-neutral-primary-soft p-4">
              <div class="min-w-0">
                <p class="font-medium text-heading break-words">{{ member.name }}</p>
                <p class="text-sm text-body break-all">{{ member.email }}</p>
                <tmz-badge class="mt-2" [tone]="member.role === 'admin' ? 'brand' : 'neutral'">{{ t('roles.' + member.role) }}</tmz-badge>
              </div>
              @if (isAdmin()) {
                <tmz-dropdown-menu [label]="t('members.actionsFor', { name: member.name })" [items]="memberActions(member)" (selected)="onMemberAction(member, $event)" />
              }
            </li>
          }
        </ul>
        <tmz-table-container class="hidden md:block">
          <table tmzTable data-testid="member-table">
            <thead [class]="tableHead">
              <tr>
                <th scope="col" [class]="tableHeaderCell">{{ t('members.name') }}</th>
                <th scope="col" [class]="tableHeaderCell">{{ t('members.email') }}</th>
                <th scope="col" [class]="tableHeaderCell">{{ t('members.role') }}</th>
                @if (isAdmin()) {
                  <th scope="col" [class]="tableHeaderCell"><span class="sr-only">{{ t('members.actions') }}</span></th>
                }
              </tr>
            </thead>
            <tbody>
              @for (member of members(); track member.userId) {
                <tr [class]="tableRow">
                  <th scope="row" [class]="tableCell + ' font-medium text-heading'">{{ member.name }}</th>
                  <td [class]="tableCell">{{ member.email }}</td>
                  <td [class]="tableCell"><tmz-badge [tone]="member.role === 'admin' ? 'brand' : 'neutral'">{{ t('roles.' + member.role) }}</tmz-badge></td>
                  @if (isAdmin()) {
                    <td [class]="tableCell + ' text-end'">
                      <tmz-dropdown-menu [label]="t('members.actionsFor', { name: member.name })" [items]="memberActions(member)" (selected)="onMemberAction(member, $event)" />
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </tmz-table-container>
      </section>

      @if (isAdmin()) {
        <section data-testid="invitations">
          <h2 class="mb-3 text-lg font-semibold text-heading">{{ t('members.pendingTitle') }}</h2>
          @if (invitations().length) {
            <ul class="flex flex-col gap-3">
              @for (invitation of invitations(); track invitation.id) {
                <li class="flex flex-col gap-3 rounded-base border border-default bg-neutral-primary-soft p-4 sm:flex-row sm:items-center sm:justify-between">
                  <div class="min-w-0">
                    <p class="font-medium text-heading break-all">{{ invitation.email }}</p>
                    <p class="text-sm text-body">{{ t('members.pendingRole', { role: t('roles.' + invitation.role) }) }}</p>
                  </div>
                  <button tmzButton variant="secondary" size="sm" type="button" (click)="revoke(invitation)" data-testid="revoke">{{ t('members.revoke') }}</button>
                </li>
              }
            </ul>
          } @else {
            <p class="text-sm text-body">{{ t('members.noPending') }}</p>
          }
        </section>
      }
    </div>
  `,
})
export class ProjectMembers {
  private readonly service = inject(ProjectsService);
  private readonly errors = inject(ApiErrors);
  private readonly toasts = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly confirmDialog = inject(ConfirmDialog);
  private readonly context = inject(ProjectContext);

  protected readonly roles = roles;
  protected readonly tableHead = tableHead;
  protected readonly tableHeaderCell = tableHeaderCell;
  protected readonly tableRow = tableRow;
  protected readonly tableCell = tableCell;
  protected readonly members = signal<Member[]>([]);
  protected readonly invitations = signal<Invitation[]>([]);
  protected readonly busy = signal(false);
  protected readonly inviteErrors = signal<Record<string, string>>({});
  protected readonly inviteError = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected isAdmin(): boolean {
    return this.context.can('admin');
  }

  protected memberActions(member: Member): MenuItem[] {
    const t = (key: string) => this.transloco.translate(key);
    return [
      ...roles.filter((role) => role !== member.role).map((role) => ({ id: role, label: t('members.makeRole.' + role) })),
      { id: 'remove', label: t('members.remove'), danger: true },
    ];
  }

  protected invite(email: string, role: string): void {
    this.inviteErrors.set({});
    this.inviteError.set(null);
    this.busy.set(true);
    this.service.invite(this.projectId(), email, role as ProjectRole).subscribe({
      next: (result) => {
        const key = result.kind === 'member' ? 'members.added' : 'members.invited';
        this.toasts.success(this.transloco.translate(key, { email }));
        this.busy.set(false);
        this.load();
      },
      error: (error) => {
        const parsed = this.errors.parse(error);
        this.inviteErrors.set(parsed.fieldErrors);
        if (!Object.keys(parsed.fieldErrors).length) {
          this.inviteError.set(parsed.message);
        }
        this.busy.set(false);
      },
    });
  }

  protected async onMemberAction(member: Member, action: string): Promise<void> {
    if (action === 'remove') {
      const t = (key: string, params?: Record<string, string>) => this.transloco.translate(key, params);
      const confirmed = await this.confirmDialog.confirm({
        title: t('members.removeTitle', { name: member.name }),
        message: t('members.removeMessage'),
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
      },
      error: (e) => this.errors.report(e),
    });
  }
}
