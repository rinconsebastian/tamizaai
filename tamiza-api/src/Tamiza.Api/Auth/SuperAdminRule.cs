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

    public bool IsSuperAdmin(TokenIdentity identity) =>
        identity is { EmailVerified: true, Email: not null } && _emails.Contains(identity.Email.Trim());

    internal static HashSet<string> Parse(string emails) =>
        emails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
