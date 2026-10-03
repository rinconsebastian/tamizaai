using System.Net.Mail;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;

namespace Tamiza.Api.Projects;

public sealed record MemberView(Guid UserId, string Name, string? Email, ProjectRole Role);

public sealed record InvitationView(Guid Id, string Email, ProjectRole Role, DateTimeOffset CreatedAt);

public sealed record InviteRequest(string? Email, string? Role);

public sealed record ChangeRoleRequest(string? Role);

/// <summary><see cref="Kind"/> is <c>member</c> when the person already uses Tamiza, <c>invitation</c> otherwise.</summary>
public sealed record InviteResult(string Kind, MemberView? Member, InvitationView? Invitation);

public static class MemberEndpoints
{
    public const string MemberExists = "member.exists";
    public const string InvitationExists = "invitation.exists";
    public const string LastAdmin = "project.last_admin";

    public static RouteGroupBuilder MapMemberEndpoints(this RouteGroupBuilder api)
    {
        var project = api.MapGroup("/projects/{projectId:guid}").WithTags("Members");
        project.MapGet("/members", ListMembersAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListMembers");
        project.MapPost("/members", InviteAsync).RequireProjectRole(ProjectRole.Admin).WithName("InviteMember");
        project.MapPatch("/members/{userId:guid}", ChangeMemberRoleAsync).RequireProjectRole(ProjectRole.Admin).WithName("ChangeMemberRole");
        project.MapDelete("/members/{userId:guid}", RemoveMemberAsync).RequireProjectRole(ProjectRole.Admin).WithName("RemoveMember");
        project.MapGet("/invitations", ListInvitationsAsync).RequireProjectRole(ProjectRole.Admin).WithName("ListInvitations");
        project.MapPatch("/invitations/{invitationId:guid}", ChangeInvitationRoleAsync).RequireProjectRole(ProjectRole.Admin).WithName("ChangeInvitationRole");
        project.MapDelete("/invitations/{invitationId:guid}", RevokeInvitationAsync).RequireProjectRole(ProjectRole.Admin).WithName("RevokeInvitation");
        return api;
    }

    private static async Task<Ok<List<MemberView>>> ListMembersAsync(Guid projectId, TamizaDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.ProjectMembers
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.User!.Name)
            .Select(m => new MemberView(m.UserId, m.User!.Name, m.User.Email, m.Role))
            .ToListAsync(ct));

    private static async Task<Ok<List<InvitationView>>> ListInvitationsAsync(Guid projectId, TamizaDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.ProjectInvitations
            .Where(i => i.ProjectId == projectId)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new InvitationView(i.Id, i.Email, i.Role, i.CreatedAt))
            .ToListAsync(ct));

    private static async Task<Results<Created<InviteResult>, ValidationProblem, ProblemHttpResult>> InviteAsync(
        Guid projectId, InviteRequest request, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var email = request.Email?.Trim() ?? "";
        if (email.Length is 0 or > 320 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
        {
            errors.Add("email", "Enter a valid email address.");
        }

        var role = ParseRole(request.Role, errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var normalized = Emails.Normalize(email);
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var alreadyMember = await db.ProjectMembers
            .AnyAsync(m => m.ProjectId == projectId && m.User!.Email != null && m.User.Email.ToLower() == normalized, ct);
        if (alreadyMember)
        {
            return Problems.Conflict(MemberExists, $"{email} is already a member of this project.");
        }

        // Only a verified email identifies a person; several matches (unusual) fall back to an invitation.
        var verified = await db.Users
            .Where(u => u.EmailVerified && u.Email != null && u.Email.ToLower() == normalized)
            .Select(u => new { u.Id, u.Name, u.Email })
            .Take(2)
            .ToListAsync(ct);
        if (verified.Count == 1)
        {
            var person = verified[0];
            db.ProjectMembers.Add(new ProjectMember { ProjectId = projectId, UserId = person.Id, Role = role, CreatedAt = now });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return TypedResults.Created($"/api/v1/projects/{projectId}/members/{person.Id}",
                new InviteResult("member", new MemberView(person.Id, person.Name, person.Email, role), null));
        }

        var invitation = new ProjectInvitation
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            Email = email,
            NormalizedEmail = normalized,
            Role = role,
            InvitedBy = user.Id,
            CreatedAt = now,
        };
        db.ProjectInvitations.Add(invitation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problems.Conflict(InvitationExists, $"{email} already has a pending invitation to this project.");
        }

        await transaction.CommitAsync(ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/invitations/{invitation.Id}",
            new InviteResult("invitation", null, new InvitationView(invitation.Id, invitation.Email, role, now)));
    }

    private static async Task<Results<Ok<MemberView>, ValidationProblem, ProblemHttpResult, NotFound<ProblemDetails>>> ChangeMemberRoleAsync(
        Guid projectId, Guid userId, ChangeRoleRequest request, TamizaDbContext db, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var role = ParseRole(request.Role, errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAdminsAsync(db, projectId, ct);
        var member = await db.ProjectMembers.Include(m => m.User).SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
        if (member is null)
        {
            return MemberNotFound();
        }

        if (member.Role == ProjectRole.Admin && role != ProjectRole.Admin && await AdminCountAsync(db, projectId, ct) <= 1)
        {
            return Problems.Conflict(LastAdmin, "The project needs at least one admin. Make someone else admin first.");
        }

        member.Role = role;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(new MemberView(member.UserId, member.User!.Name, member.User.Email, member.Role));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, NotFound<ProblemDetails>>> RemoveMemberAsync(
        Guid projectId, Guid userId, TamizaDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAdminsAsync(db, projectId, ct);
        var member = await db.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
        if (member is null)
        {
            return MemberNotFound();
        }

        if (member.Role == ProjectRole.Admin && await AdminCountAsync(db, projectId, ct) <= 1)
        {
            return Problems.Conflict(LastAdmin, "The project needs at least one admin. Make someone else admin first.");
        }

        db.ProjectMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<InvitationView>, ValidationProblem, NotFound<ProblemDetails>>> ChangeInvitationRoleAsync(
        Guid projectId, Guid invitationId, ChangeRoleRequest request, TamizaDbContext db, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var role = ParseRole(request.Role, errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var invitation = await db.ProjectInvitations.SingleOrDefaultAsync(i => i.ProjectId == projectId && i.Id == invitationId, ct);
        if (invitation is null)
        {
            return InvitationNotFound();
        }

        invitation.Role = role;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new InvitationView(invitation.Id, invitation.Email, invitation.Role, invitation.CreatedAt));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> RevokeInvitationAsync(
        Guid projectId, Guid invitationId, TamizaDbContext db, CancellationToken ct)
    {
        var deleted = await db.ProjectInvitations.Where(i => i.ProjectId == projectId && i.Id == invitationId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? InvitationNotFound() : TypedResults.NoContent();
    }

    /// <summary>
    /// Locks the project's admin memberships until the transaction ends, so concurrent demotions are serialized and
    /// cannot leave the project without an admin.
    /// </summary>
    private static Task LockAdminsAsync(TamizaDbContext db, Guid projectId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM tamiza.project_members WHERE project_id = {projectId} AND role = 'admin' FOR UPDATE", ct);

    private static Task<int> AdminCountAsync(TamizaDbContext db, Guid projectId, CancellationToken ct) =>
        db.ProjectMembers.CountAsync(m => m.ProjectId == projectId && m.Role == ProjectRole.Admin, ct);

    private static ProjectRole ParseRole(string? value, FieldErrors errors)
    {
        if (Enum.TryParse<ProjectRole>(value, ignoreCase: true, out var role) && Enum.IsDefined(role) && !int.TryParse(value, out _))
        {
            return role;
        }

        errors.Add("role", "Choose admin, analyst or viewer.");
        return ProjectRole.Viewer;
    }

    private static NotFound<ProblemDetails> MemberNotFound() =>
        TypedResults.NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Member not found." });

    private static NotFound<ProblemDetails> InvitationNotFound() =>
        TypedResults.NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Invitation not found." });
}
