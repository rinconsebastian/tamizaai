using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Accounts;

/// <summary>The codes the UI translates when an account email action is refused.</summary>
public static class AccountEmailErrors
{
    public const string Disabled = "accountEmail.disabled";
    public const string ExistingAccount = "accountEmail.existingAccount";
    public const string NoAccount = "accountEmail.noAccount";
    public const string RateLimited = "accountEmail.rateLimited";
    public const string Failed = "accountEmail.failed";
}

/// <summary>The Problem Details the account email routes answer with. They never hold what the provider said.</summary>
public static class AccountEmailProblems
{
    public static ProblemHttpResult Disabled() =>
        Problems.Status(StatusCodes.Status503ServiceUnavailable, AccountEmailErrors.Disabled,
            "Account emails are not available.", "Account emails are not configured for this deployment.");

    public static ProblemHttpResult ExistingAccount() =>
        Problems.Conflict(AccountEmailErrors.ExistingAccount, "The person's account was not created by Tamiza or is already verified, so no invitation email can be sent.");

    public static ProblemHttpResult NoAccount() =>
        Problems.Conflict(AccountEmailErrors.NoAccount, "The member has no active account with an email address to send to.");

    public static ProblemHttpResult Failed() =>
        Problems.Status(StatusCodes.Status502BadGateway, AccountEmailErrors.Failed,
            "The email could not be requested.", "The identity provider could not be reached or could not send the email.");

    /// <summary>429 with <c>retryAfter</c> in seconds and the <c>Retry-After</c> header.</summary>
    public static IResult RateLimited(int retryAfterSeconds) => new ProblemWithRetryAfter(
        Problems.Status(StatusCodes.Status429TooManyRequests, AccountEmailErrors.RateLimited,
            "Too many account emails.", "Account emails are limited. Try again later.",
            new Dictionary<string, object?> { ["retryAfter"] = retryAfterSeconds }),
        retryAfterSeconds);

    /// <summary>The refusal for an outcome other than <see cref="EmailOutcome.Sent"/>.</summary>
    public static IResult Of(EmailResult result) => result.Outcome switch
    {
        EmailOutcome.RecentlySent or EmailOutcome.RateLimited => RateLimited(Math.Max(1, result.RetryAfterSeconds)),
        EmailOutcome.ExistingAccount => ExistingAccount(),
        EmailOutcome.NoAccount => NoAccount(),
        _ => Failed(),
    };

    private sealed class ProblemWithRetryAfter(ProblemHttpResult problem, int seconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return problem.ExecuteAsync(httpContext);
        }
    }
}

public sealed record ResendInvitationEmailRequest(string? Language = null);

public static class AccountEmailRequests
{
    /// <summary>The language of the new account: <c>en</c> or <c>es</c>, from the inviter's interface; anything else means no preference.</summary>
    public static string? Language(string? value) =>
        value?.Trim().ToLowerInvariant() is "en" or "es" ? value.Trim().ToLowerInvariant() : null;
}

public static class AccountEmailEndpoints
{
    public static RouteGroupBuilder MapAccountEmailEndpoints(this RouteGroupBuilder api)
    {
        var project = api.MapGroup("/projects/{projectId:guid}").WithTags("Members");
        project.MapPost("/invitations/{invitationId:guid}/email", ResendInvitationEmailAsync)
            .RequireProjectRole(ProjectRole.Admin)
            .WithName("ResendInvitationEmail")
            .Produces<InvitationView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        project.MapPost("/members/{userId:guid}/password-reset", SendPasswordResetAsync)
            .RequireProjectRole(ProjectRole.Admin)
            .WithName("SendMemberPasswordReset")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return api;
    }

    private static async Task<IResult> ResendInvitationEmailAsync(
        Guid projectId, Guid invitationId, ResendInvitationEmailRequest? request, TamizaDbContext db, ICurrentUser user,
        AccountEmails accountEmails, CancellationToken ct)
    {
        var invitation = await db.ProjectInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.ProjectId == projectId && i.Id == invitationId, ct);
        if (invitation is null)
        {
            return TypedResults.NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Invitation not found." });
        }

        var result = await accountEmails.SendInvitationAsync(
            invitation.NormalizedEmail, user.Id, AccountEmailRequests.Language(request?.Language), ct);
        if (result is not { } sent)
        {
            return AccountEmailProblems.Disabled();
        }

        return sent.Outcome == EmailOutcome.Sent
            ? TypedResults.Ok(new InvitationView(invitation.Id, invitation.Email, invitation.Role, invitation.CreatedAt,
                await MemberEndpoints.LastEmailAsync(db, invitation.NormalizedEmail, ct)))
            : AccountEmailProblems.Of(sent);
    }

    private static async Task<IResult> SendPasswordResetAsync(
        Guid projectId, Guid userId, TamizaDbContext db, ICurrentUser user, AccountEmails accountEmails, CancellationToken ct)
    {
        // The account is the one the member signs in with, found from the token's subject; only members of this project qualify.
        var subject = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .Select(m => m.User!.KeycloakSub)
            .SingleOrDefaultAsync(ct);
        if (subject is null)
        {
            return TypedResults.NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Member not found." });
        }

        var result = await accountEmails.SendPasswordResetAsync(subject, user.Id, ct);
        if (result is not { } sent)
        {
            return AccountEmailProblems.Disabled();
        }

        return sent.Outcome == EmailOutcome.Sent ? TypedResults.NoContent() : AccountEmailProblems.Of(sent);
    }
}
