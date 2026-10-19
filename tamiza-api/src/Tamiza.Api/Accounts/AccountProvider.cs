namespace Tamiza.Api.Accounts;

/// <summary>An account in the identity provider, as far as account emails need to know it.</summary>
public sealed record ProviderAccount(string Id, bool EmailVerified, bool Enabled, bool HasEmail);

public enum ProviderFailure
{
    None,

    /// <summary>The provider could not be reached, or did not answer in time.</summary>
    Unreachable,

    /// <summary>The provider refused Tamiza's own credentials or permissions.</summary>
    Unauthorized,

    /// <summary>An account with that email already exists.</summary>
    Conflict,

    /// <summary>The account is disabled, deleted or has no email, so it cannot be sent anything.</summary>
    AccountUnavailable,

    Other,
}

/// <summary>What a provider call returned: a failure kind (with the HTTP status for logs) or success.</summary>
public readonly record struct ProviderResult(ProviderFailure Failure = ProviderFailure.None, int? Status = null)
{
    public bool Succeeded => Failure == ProviderFailure.None;

    public static readonly ProviderResult Ok = new();
}

public readonly record struct ProviderResult<T>(T? Value, ProviderFailure Failure = ProviderFailure.None, int? Status = null)
{
    public bool Succeeded => Failure == ProviderFailure.None;
}

/// <summary>
/// What account emails need from an identity provider. Policy (who may be emailed, limits, ownership) lives in
/// <see cref="AccountEmails"/>; an implementation only maps these calls to the provider's admin API.
/// </summary>
public interface IAccountProvider
{
    Task<ProviderResult<ProviderAccount?>> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>The account a user signs in with, from the token's <c>sub</c>.</summary>
    Task<ProviderResult<ProviderAccount?>> FindBySubjectAsync(string subject, CancellationToken cancellationToken);

    /// <summary>Creates an enabled, unverified account without a password; <see cref="ProviderFailure.Conflict"/> when one exists.</summary>
    Task<ProviderResult<string>> CreateAsync(string email, string? language, CancellationToken cancellationToken);

    /// <summary>Emails the link that sets a password and verifies the email.</summary>
    Task<ProviderResult> SendSetupEmailAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>Emails the link that sets a new password.</summary>
    Task<ProviderResult> SendPasswordResetEmailAsync(string accountId, CancellationToken cancellationToken);
}

/// <summary>Gives the configured provider, or null when the deployment has none. No adapter means account emails are off.</summary>
public interface IAccountProviderSource
{
    IAccountProvider? Provider { get; }
}
