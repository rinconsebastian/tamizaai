using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tamiza.Api.Data;

namespace Tamiza.Api.Pipeline;

/// <summary>
/// Queues pipeline runs for the analytics worker. The partial unique index <c>ix_runs_project_id_queued</c> allows one
/// queued run per project: a manual request beyond it is refused, automatic triggers are simply absorbed.
/// </summary>
public sealed class RunQueue(TamizaDbContext db)
{
    public const string AlreadyQueuedCode = "run.already_queued";

    /// <summary>Queues a run started by a user, or returns null when the project already has one queued.</summary>
    public async Task<Run?> EnqueueManualAsync(Guid projectId, Guid requestedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = new Run
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            Trigger = RunTrigger.Manual,
            Status = RunStatus.Queued,
            RequestedBy = requestedBy,
            QueuedAt = now,
        };
        db.Runs.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return run;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(run).State = EntityState.Detached;
            return null;
        }
    }

    /// <summary>Queues an automatic run unless one is already queued. Returns whether a run was added.</summary>
    public async Task<bool> EnqueueAutomaticAsync(Guid projectId, RunTrigger trigger, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at)
            VALUES ({Guid.CreateVersion7(now)}, {projectId}, {trigger.ToString().ToLowerInvariant()}, 'queued', {now})
            ON CONFLICT DO NOTHING
            """, cancellationToken) > 0;

    /// <summary>
    /// Queues a run for every project whose schedule is due. The worker moves <c>next_run_at</c> when it starts the
    /// run, so a due project keeps being offered only until its run is queued.
    /// </summary>
    public async Task EnqueueScheduledAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await db.Projects.AsNoTracking()
            .Where(p => p.RunIntervalHours != null && p.NextRunAt != null && p.NextRunAt <= now)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        foreach (var projectId in due)
        {
            await EnqueueAutomaticAsync(projectId, RunTrigger.Schedule, now, cancellationToken);
        }
    }

    /// <summary>
    /// After a successful synchronization, queues a run if the project wants one and its submissions changed: in the
    /// synchronization, or through a webhook delivery since its last run started. A webhook never queues a run itself,
    /// and the synchronization finds what the webhook stored unchanged, so it queues the run for those submissions.
    /// </summary>
    public async Task EnqueueAfterSyncAsync(Guid projectId, int changes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.RunAfterSync,
                p.LastWebhookAt,
                LastRunStart = db.Runs.Where(r => r.ProjectId == p.Id).Max(r => r.StartedAt),
            })
            .SingleAsync(cancellationToken);
        var webhookSinceRun = project.LastWebhookAt is { } webhook && (project.LastRunStart is not { } start || webhook > start);
        if (project.RunAfterSync && (changes > 0 || webhookSinceRun))
        {
            await EnqueueAutomaticAsync(projectId, RunTrigger.Sync, now, cancellationToken);
        }
    }
}
