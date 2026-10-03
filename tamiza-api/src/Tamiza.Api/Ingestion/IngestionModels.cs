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
