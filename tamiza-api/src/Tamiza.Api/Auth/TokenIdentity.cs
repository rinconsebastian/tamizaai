using System.Security.Claims;

namespace Tamiza.Api.Auth;

/// <summary>The identity claims Tamiza reads from a validated Keycloak access token.</summary>
public sealed record TokenIdentity(string Subject, string Name, string? Email, bool EmailVerified)
{
    public const string SubjectClaim = "sub";
    public const string NameClaim = "name";
    public const string PreferredUsernameClaim = "preferred_username";
    public const string EmailClaim = "email";
    public const string EmailVerifiedClaim = "email_verified";

    /// <summary>Returns null when the token has no <c>sub</c>, which Tamiza cannot accept.</summary>
    public static TokenIdentity? FromPrincipal(ClaimsPrincipal principal)
    {
        var subject = Value(principal, SubjectClaim);
        if (subject is null)
        {
            return null;
        }

        var name = Value(principal, NameClaim) ?? Value(principal, PreferredUsernameClaim) ?? subject;
        var emailVerified = bool.TryParse(Value(principal, EmailVerifiedClaim), out var verified) && verified;
        return new TokenIdentity(subject, name, Value(principal, EmailClaim), emailVerified);
    }

    private static string? Value(ClaimsPrincipal principal, string type) =>
        principal.FindFirst(type)?.Value is { Length: > 0 } value ? value : null;
}
