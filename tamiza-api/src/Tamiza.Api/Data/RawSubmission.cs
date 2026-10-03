namespace Tamiza.Api.Data;

/// <summary>A Kobo submission exactly as Kobo sent it. Written with raw SQL upserts (see <c>SubmissionStore</c>).</summary>
public sealed class RawSubmission
{
    public long Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>Kobo's <c>_id</c>: stable when the submission is edited, so it identifies the record.</summary>
    public long KoboId { get; set; }

    /// <summary>Kobo's current <c>_uuid</c>; it changes when the submission is edited.</summary>
    public required string KoboUuid { get; set; }

    /// <summary>The form version the submission was made with (<c>__version__</c>).</summary>
    public string? FormVersion { get; set; }

    /// <summary>Kobo's <c>_submission_time</c>.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>The submission JSON, stored as <c>jsonb</c>.</summary>
    public required string Data { get; set; }

    /// <summary>How the submission first arrived; later updates keep it.</summary>
    public SubmissionOrigin Origin { get; set; }

    /// <summary>When Tamiza first stored the submission.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>When Tamiza last changed the stored record.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft delete: the submission no longer exists in Kobo.</summary>
    public bool Deleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}

public enum SubmissionOrigin
{
    Webhook,
    Sync,
}
