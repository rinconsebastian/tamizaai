using Tamiza.Api.Data;

namespace Tamiza.Api.Ingestion;

public sealed record IngestionStatus(
    long SubmissionCount,
    long DeletedCount,
    DateTimeOffset? LastWebhookAt,
    SyncItem? LastSync,
    SyncItem? ActiveSync,
    DateTimeOffset NextSyncAt,
    DateTimeOffset NextReconcileAt,
    int SyncIntervalHours,
    int ReconcileIntervalHours);

/// <summary><c>null</c> leaves a value unchanged.</summary>
public sealed record UpdateIngestionRequest(int? SyncIntervalHours, int? ReconcileIntervalHours);

/// <summary><c>incremental</c> ("Sync now", the default) or <c>full</c> (re-read every submission).</summary>
public sealed record StartSyncRequest(SyncKind? Kind);

public sealed record SyncRequester(Guid Id, string Name);

public sealed record SyncItem(
    Guid Id,
    SyncKind Kind,
    SyncTrigger Trigger,
    SyncStatus Status,
    SyncRequester? RequestedBy,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int CreatedCount,
    int UpdatedCount,
    int DeletedCount,
    string? ErrorCode,
    string? Error);

public sealed record SyncPage(IReadOnlyList<SyncItem> Items, int Total);

/// <summary>
/// One form version used by the project's submissions. <see cref="Status"/> is <c>available</c>, <c>pending</c>, <c>failed</c>
/// or <c>not_found</c>; a version with no record yet reads as <c>pending</c>. <see cref="Current"/> marks the version the
/// project's form snapshot describes.
/// </summary>
public sealed record FormVersionItem(
    string Uid,
    bool Current,
    DateTimeOffset? DeployedAt,
    long Submissions,
    string Status,
    string? ErrorCode,
    string? Error,
    DateTimeOffset? NextAttemptAt);

/// <summary>The versions in use, the current one first, and how many submissions have no version.</summary>
public sealed record FormVersionList(IReadOnlyList<FormVersionItem> Versions, long WithoutVersion);
