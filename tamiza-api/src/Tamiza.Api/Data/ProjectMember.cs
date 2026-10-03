namespace Tamiza.Api.Data;

public sealed class ProjectMember
{
    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public ProjectRole Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}

/// <summary>A pending membership for someone who has not signed in with that verified email yet.</summary>
public sealed class ProjectInvitation
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>The email as the admin typed it.</summary>
    public required string Email { get; set; }

    /// <summary>Trimmed and lower-cased; what matching uses.</summary>
    public required string NormalizedEmail { get; set; }

    public ProjectRole Role { get; set; }

    public Guid InvitedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
