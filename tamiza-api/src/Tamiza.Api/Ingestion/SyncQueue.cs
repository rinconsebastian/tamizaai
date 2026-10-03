using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Tamiza.Api.Data;

namespace Tamiza.Api.Ingestion;

public sealed record ClaimedSync(Guid Id, Guid ProjectId, SyncKind Kind);

/// <summary>
/// The <c>syncs</c> table as a queue. The partial unique index <c>ix_syncs_project_id_active</c> allows one queued or
/// running row per project, which serializes the runs of a project across the scheduler and manual requests.
/// </summary>
public sealed class SyncQueue(TamizaDbContext db, IOptions<IngestionOptions> options)
{
    public const string InterruptedCode = "sync.interrupted";
    public const string InProgressCode = "sync.in_progress";

    private const string SucceededSyncExists = """
        EXISTS (SELECT 1 FROM tamiza.syncs done
                WHERE done.project_id = {0} AND done.status = 'succeeded' AND done.kind IN ('incremental', 'full'))
        """;

    /// <summary>Marks runs left <c>running</c> by a previous process as failed. Assumes a single API instance.</summary>
    public Task<int> FailInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Syncs.Where(s => s.Status == SyncStatus.Running).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, SyncStatus.Failed)
            .SetProperty(x => x.FinishedAt, now)
            .SetProperty(x => x.ErrorCode, InterruptedCode)
            .SetProperty(x => x.Error, "The synchronization was interrupted because the API stopped."), cancellationToken);

    /// <summary>
    /// Queues a run for every project whose synchronization or reconciliation is due. A project with an active run is
    /// skipped and tried again on the next tick, so a due run waits for the active one.
    /// </summary>
    public async Task EnqueueDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await db.Projects.AsNoTracking()
            .Where(p => p.NextSyncAt <= now || p.NextReconcileAt <= now)
            .Select(p => new { p.Id, SyncDue = p.NextSyncAt <= now })
            .ToListAsync(cancellationToken);

        foreach (var project in due)
        {
            // A due synchronization goes first; the reconciliation follows once it has started and moved its due time.
            var kind = project.SyncDue ? SyncKind.Incremental : SyncKind.Reconciliation;
            var trigger = project.SyncDue
                ? $"CASE WHEN {string.Format(SucceededSyncExists, "@project_id")} THEN 'schedule' ELSE 'initial' END"
                : "'schedule'";
            await ExecuteAsync($"""
                INSERT INTO tamiza.syncs (id, project_id, kind, trigger, status, queued_at)
                VALUES (@id, @project_id, @kind, {trigger}, 'queued', @now)
                ON CONFLICT DO NOTHING
                """, cancellationToken,
                ("id", Guid.CreateVersion7(now)), ("project_id", project.Id), ("kind", Lower(kind)), ("now", now));
        }
    }

    /// <summary>
    /// Queues a run requested by a user. Returns null when the project already has a queued or running one.
    /// </summary>
    public async Task<Sync?> EnqueueManualAsync(Guid projectId, SyncKind kind, Guid requestedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sync = new Sync
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            Kind = kind,
            Trigger = SyncTrigger.Manual,
            Status = SyncStatus.Queued,
            RequestedBy = requestedBy,
            QueuedAt = now,
        };
        db.Syncs.Add(sync);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return sync;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(sync).State = EntityState.Detached;
            return null;
        }
    }

    /// <summary>
    /// Starts the oldest queued run, if any. An incremental run of a project that never synchronized successfully
    /// becomes a full one. The project's next due times move to the start plus its intervals.
    /// </summary>
    public async Task<ClaimedSync?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sql = $"""
            WITH claimed AS (
                UPDATE tamiza.syncs s
                SET status = 'running', started_at = @now,
                    kind = CASE WHEN s.kind = 'incremental' AND NOT {string.Format(SucceededSyncExists, "s.project_id")} THEN 'full' ELSE s.kind END
                WHERE s.id = (SELECT id FROM tamiza.syncs WHERE status = 'queued' ORDER BY queued_at FOR UPDATE SKIP LOCKED LIMIT 1)
                RETURNING s.id, s.project_id, s.kind
            )
            UPDATE tamiza.projects p
            SET next_sync_at = CASE WHEN c.kind IN ('incremental', 'full') THEN @now + make_interval(hours => p.sync_interval_hours) ELSE p.next_sync_at END,
                next_reconcile_at = CASE WHEN c.kind IN ('reconciliation', 'full') THEN @now + make_interval(hours => p.reconcile_interval_hours) ELSE p.next_reconcile_at END
            FROM claimed c
            WHERE p.id = c.project_id
            RETURNING c.id, c.project_id, c.kind
            """;

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("now", now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new ClaimedSync(reader.GetGuid(0), reader.GetGuid(1), Enum.Parse<SyncKind>(reader.GetString(2), ignoreCase: true))
                : null;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Adds the counts of one stored page to the run.</summary>
    public Task AddCountsAsync(Guid syncId, int created, int updated, int deleted, CancellationToken cancellationToken) =>
        db.Syncs.Where(s => s.Id == syncId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.CreatedCount, x => x.CreatedCount + created)
            .SetProperty(x => x.UpdatedCount, x => x.UpdatedCount + updated)
            .SetProperty(x => x.DeletedCount, x => x.DeletedCount + deleted), cancellationToken);

    /// <summary>
    /// Ends a run. A failed scheduled or initial run is retried after the retry delay, or after the project's interval
    /// when that is shorter, unless the run was already due sooner.
    /// </summary>
    public async Task FinishAsync(Guid syncId, string? errorCode, string? error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var failed = errorCode is not null;
        await db.Syncs.Where(s => s.Id == syncId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, failed ? SyncStatus.Failed : SyncStatus.Succeeded)
            .SetProperty(x => x.FinishedAt, now)
            .SetProperty(x => x.ErrorCode, errorCode)
            .SetProperty(x => x.Error, error), cancellationToken);

        if (!failed)
        {
            return;
        }

        var retryHours = options.Value.RetryDelay.TotalHours;
        await ExecuteAsync("""
            UPDATE tamiza.projects p
            SET next_sync_at = CASE WHEN s.kind IN ('incremental', 'full')
                    THEN LEAST(p.next_sync_at, @now + make_interval(secs => 3600 * LEAST(@retry_hours, p.sync_interval_hours))) ELSE p.next_sync_at END,
                next_reconcile_at = CASE WHEN s.kind = 'reconciliation'
                    THEN LEAST(p.next_reconcile_at, @now + make_interval(secs => 3600 * LEAST(@retry_hours, p.reconcile_interval_hours))) ELSE p.next_reconcile_at END
            FROM tamiza.syncs s
            WHERE s.id = @id AND p.id = s.project_id AND s.trigger IN ('schedule', 'initial')
            """, cancellationToken, ("id", syncId), ("now", now), ("retry_hours", retryHours));
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static string Lower(SyncKind kind) => kind.ToString().ToLowerInvariant();
}
