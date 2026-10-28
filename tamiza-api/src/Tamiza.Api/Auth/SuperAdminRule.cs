using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Auth;

/// <summary>
/// Grants the global superadmin role to the configured emails, compared case-insensitively, and only when
/// the token marks the email as verified. The list is read once at startup.
/// </summary>
public sealed class SuperAdminRule(IOptions<TamizaOptions> options)
{
    private readonly HashSet<string> _emails = Parse(options.Value.SuperAdminEmails);

    public bool IsSuperAdmin(TokenIdentity identity) => IsSuperAdmin(identity.Email, identity.EmailVerified);

    /// <summary>The same rule for a user's stored email, when no token is at hand (a scheduled email checks its author).</summary>
    public bool IsSuperAdmin(string? email, bool emailVerified) => emailVerified && email is not null && _emails.Contains(email.Trim());

    internal static HashSet<string> Parse(string emails) =>
        emails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
