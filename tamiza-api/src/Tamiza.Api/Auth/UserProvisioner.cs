using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tamiza.Api.Data;

namespace Tamiza.Api.Auth;

public sealed record UserSnapshot(Guid Id, string Name, string? Email);

/// <summary>Creates the local user on first sign-in and keeps name and email in step with the token.</summary>
public sealed class UserProvisioner(TamizaDbContext db, IMemoryCache cache, TimeProvider time, ILogger<UserProvisioner> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<UserSnapshot> ResolveAsync(TokenIdentity identity, CancellationToken cancellationToken)
    {
        var cacheKey = $"tamiza:user:{identity.Subject}";
        if (cache.TryGetValue(cacheKey, out UserSnapshot? cached) && cached!.Name == identity.Name && cached.Email == identity.Email)
        {
            return cached;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.KeycloakSub == identity.Subject, cancellationToken)
            ?? await InsertAsync(identity, cancellationToken);

        if (user.Name != identity.Name || user.Email != identity.Email)
        {
            await UpdateAsync(user, identity, cancellationToken);
        }

        var snapshot = new UserSnapshot(user.Id, user.Name, user.Email);
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
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tamiza.users (id, keycloak_sub, name, email, created_at, updated_at)
            VALUES ({id}, {identity.Subject}, {identity.Name}, {identity.Email}, {now}, {now})
            ON CONFLICT (keycloak_sub) DO NOTHING
            """,
            cancellationToken);

        if (inserted == 1)
        {
            logger.LogInformation("Provisioned local user {UserId}.", id);
        }

        return await db.Users.SingleAsync(u => u.KeycloakSub == identity.Subject, cancellationToken);
    }

    /// <summary>Update path: name or email changed in Keycloak.</summary>
    private async Task UpdateAsync(User user, TokenIdentity identity, CancellationToken cancellationToken)
    {
        user.Name = identity.Name;
        user.Email = identity.Email;
        user.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Updated local user {UserId} from token claims.", user.Id);
    }
}
