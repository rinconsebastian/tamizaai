using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Data;

namespace Tamiza.Api.Projects;

public static class Emails
{
    /// <summary>The form invitations are matched on: trimmed and lower-cased.</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>Turns pending invitations into memberships when their email signs in verified.</summary>
public sealed class InvitationAcceptor(TamizaDbContext db, TimeProvider time, ILogger<InvitationAcceptor> logger)
{
    /// <summary>Runs inside the caller's transaction; returns the number of memberships created.</summary>
    public async Task<int> AcceptAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        var normalized = Emails.Normalize(email);
        var now = time.GetUtcNow();
        var created = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tamiza.project_members (project_id, user_id, role, created_at)
            SELECT project_id, {userId}, role, {now} FROM tamiza.project_invitations WHERE normalized_email = {normalized}
            ON CONFLICT (project_id, user_id) DO NOTHING
            """,
            cancellationToken);
        await db.Database.ExecuteSqlAsync($"DELETE FROM tamiza.project_invitations WHERE normalized_email = {normalized}", cancellationToken);

        if (created > 0)
        {
            logger.LogInformation("Accepted {Count} pending invitation(s) for user {UserId}.", created, userId);
        }

        return created;
    }
}
