namespace Tamiza.Api.Data;

/// <summary>One synchronization or reconciliation of a project with Kobo. Queued rows are the scheduler's queue.</summary>
public sealed class Sync
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public SyncKind Kind { get; set; }

    public SyncTrigger Trigger { get; set; }

    public SyncStatus Status { get; set; }

    /// <summary>The user who started a manual run.</summary>
    public Guid? RequestedBy { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int DeletedCount { get; set; }

    /// <summary>Machine-readable error code (<c>kobo.*</c>, <c>sync.*</c>) when the run failed.</summary>
    public string? ErrorCode { get; set; }

    public string? Error { get; set; }

    public User? RequestedByUser { get; set; }
}

public enum SyncKind
{
    /// <summary>Submissions made since the last successful synchronization, minus a safety margin.</summary>
    Incremental,

    /// <summary>Every submission, followed by deletion marking.</summary>
    Full,

    /// <summary>Only submission IDs, to mark deletions.</summary>
    Reconciliation,
}

public enum SyncTrigger
{
    Schedule,
    Manual,

    /// <summary>The project's first synchronization, which back-fills every submission.</summary>
    Initial,
}

public enum SyncStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}
