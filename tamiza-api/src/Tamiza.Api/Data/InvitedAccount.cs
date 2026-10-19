namespace Tamiza.Api.Data;

/// <summary>
/// An identity-provider account that Tamiza created for an invited email. Only such accounts may be sent the invitation email
/// again, and <see cref="LastEmailAt"/> is what the invitation rows show.
/// </summary>
public sealed class InvitedAccount
{
    /// <summary>The provider's own id for the account.</summary>
    public required string AccountId { get; set; }

    public required string NormalizedEmail { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When an invitation email was last requested for the account; null until one was.</summary>
    public DateTimeOffset? LastEmailAt { get; set; }
}
