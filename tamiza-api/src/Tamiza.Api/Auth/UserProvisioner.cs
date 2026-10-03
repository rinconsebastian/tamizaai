using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tamiza.Api.Data;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Auth;

public sealed record UserSnapshot(Guid Id, string Name, string? Email, bool EmailVerified = false)
{
    public bool Matches(TokenIdentity identity) =>
        Name == identity.Name && Email == identity.Email && EmailVerified == identity.EmailVerified;
}

/// <summary>Creates the local user on first sign-in and keeps name, email and its verification in step with the token.</summary>
public sealed class UserProvisioner(
    TamizaDbContext db, IMemoryCache cache, TimeProvider time, InvitationAcceptor invitations, ILogger<UserProvisioner> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<UserSnapshot> ResolveAsync(TokenIdentity identity, CancellationToken cancellationToken)
    {
        var cacheKey = $"tamiza:user:{identity.Subject}";
        if (cache.TryGetValue(cacheKey, out UserSnapshot? cached) && cached!.Matches(identity))
        {
            return cached;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.KeycloakSub == identity.Subject, cancellationToken)
            ?? await InsertAsync(identity, cancellationToken);

        if (user.Name != identity.Name || user.Email != identity.Email || user.EmailVerified != identity.EmailVerified)
        {
            await UpdateAsync(user, identity, cancellationToken);
        }

        var snapshot = new UserSnapshot(user.Id, user.Name, user.Email, user.EmailVerified);
        cache.Set(cacheKey, snapshot, CacheDuration);
        return snapshot;
    }

    /// <summary>
    /// Insert path. <c>ON CONFLICT DO NOTHING</c> makes concurrent first requests safe; the row returned is
    /// ours or the one another request inserted first.
    /// </summary>
    private async Task<User> InsertAsync(TokenIdentity identity, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tamiza.users (id, keycloak_sub, name, email, email_verified, created_at, updated_at)
            VALUES ({id}, {identity.Subject}, {identity.Name}, {identity.Email}, {identity.EmailVerified}, {now}, {now})
            ON CONFLICT (keycloak_sub) DO NOTHING
            """,
            cancellationToken);

        if (inserted == 1)
        {
            logger.LogInformation("Provisioned local user {UserId}.", id);
            if (identity is { EmailVerified: true, Email: not null })
            {
                await invitations.AcceptAsync(id, identity.Email, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return await db.Users.SingleAsync(u => u.KeycloakSub == identity.Subject, cancellationToken);
    }

    /// <summary>Update path: name, email or its verification changed in Keycloak.</summary>
    private async Task UpdateAsync(User user, TokenIdentity identity, CancellationToken cancellationToken)
    {
        var verificationGained = identity is { EmailVerified: true, Email: not null }
            && (user.Email != identity.Email || !user.EmailVerified);
        user.Name = identity.Name;
        user.Email = identity.Email;
        user.EmailVerified = identity.EmailVerified;
        user.UpdatedAt = time.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (verificationGained)
        {
            await invitations.AcceptAsync(user.Id, identity.Email!, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Updated local user {UserId} from token claims.", user.Id);
    }
}
