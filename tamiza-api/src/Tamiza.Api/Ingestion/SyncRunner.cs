using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Data;
using Tamiza.Api.Kobo;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Ingestion;

/// <summary>
/// Executes one claimed run: refreshes the form snapshot, reads submissions page by page and stores each page in its
/// own transaction, then (for full runs and reconciliations) marks deletions once the whole listing was read.
/// </summary>
public sealed class SyncRunner(
    TamizaDbContext db,
    SyncQueue queue,
    RunQueue runs,
    SubmissionStore store,
    KoboClient kobo,
    SecretProtector secrets,
    TimeProvider time,
    IOptions<IngestionOptions> options,
    ILogger<SyncRunner> logger)
{
    public const string FailedCode = "sync.failed";

    public async Task RunAsync(ClaimedSync claimed, CancellationToken cancellationToken)
    {
        string? errorCode = null, error = null;
        try
        {
            await ExecuteAsync(claimed, cancellationToken);
        }
        catch (KoboReadException exception)
        {
            (errorCode, error) = (exception.Error.Code, exception.Error.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            (errorCode, error) = (SyncQueue.InterruptedCode, "The synchronization was interrupted because the API stopped.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Synchronization {SyncId} of project {ProjectId} failed unexpectedly.", claimed.Id, claimed.ProjectId);
            (errorCode, error) = (FailedCode, "The synchronization failed because of an unexpected error. See the API log for details.");
        }

        // Recorded even when stopping, so the log never shows a run as running forever.
        await queue.FinishAsync(claimed.Id, errorCode, error, time.GetUtcNow(), CancellationToken.None);
        if (errorCode is null)
        {
            logger.LogInformation("Synchronization {SyncId} ({Kind}) of project {ProjectId} succeeded.", claimed.Id, claimed.Kind, claimed.ProjectId);
            await QueueRunAsync(claimed);
        }
        else
        {
            logger.LogWarning("Synchronization {SyncId} ({Kind}) of project {ProjectId} failed: {ErrorCode} {Error}",
                claimed.Id, claimed.Kind, claimed.ProjectId, errorCode, error);
        }
    }

    /// <summary>A synchronization that changed submissions starts the pipeline, when the project wants that.</summary>
    private async Task QueueRunAsync(ClaimedSync claimed)
    {
        try
        {
            var changes = await db.Syncs.AsNoTracking()
                .Where(s => s.Id == claimed.Id)
                .Select(s => s.CreatedCount + s.UpdatedCount + s.DeletedCount)
                .SingleAsync(CancellationToken.None);
            await runs.EnqueueAfterSyncAsync(claimed.ProjectId, changes, time.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not queue a pipeline run after synchronization {SyncId}.", claimed.Id);
        }
    }

    private async Task ExecuteAsync(ClaimedSync claimed, CancellationToken cancellationToken)
    {
        var sync = await db.Syncs.AsNoTracking().SingleAsync(s => s.Id == claimed.Id, cancellationToken);
        var project = await db.Projects.SingleAsync(p => p.Id == claimed.ProjectId, cancellationToken);
        if (!secrets.TryUnprotect(project.EncryptedApiToken, out var token))
        {
            throw new KoboReadException(new KoboError(KoboErrorCodes.TokenUnreadable,
                "The stored API token can no longer be read. A project admin must replace it."));
        }

        var (asset, assetError) = await kobo.GetAssetAsync(project.KoboServerUrl, project.KoboAssetUid, token, cancellationToken);
        if (assetError is not null)
        {
            throw new KoboReadException(assetError);
        }

        FormSnapshot.Apply(project, asset!, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var settings = options.Value;
        var read = claimed.Kind switch
        {
            SyncKind.Incremental => new SubmissionRead(await CutoffAsync(claimed, cancellationToken), false, settings.PageSize, settings.MaxPageBytes, settings.PageTimeout),
            SyncKind.Full => new SubmissionRead(null, false, settings.PageSize, settings.MaxPageBytes, settings.PageTimeout),
            _ => new SubmissionRead(null, true, settings.IdPageSize, settings.MaxPageBytes, settings.PageTimeout),
        };

        // Full runs and reconciliations collect every _id (about 8 bytes each) to find the deleted ones afterwards.
        var koboIds = claimed.Kind == SyncKind.Incremental ? null : new List<long>();
        await foreach (var page in kobo.ReadSubmissionsAsync(project.KoboServerUrl, project.KoboAssetUid, token, read, cancellationToken))
        {
            koboIds?.AddRange(page.Ids);
            if (page.Rows.Count == 0)
            {
                continue;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var counts = await store.UpsertAsync(project.Id, page.Rows, SubmissionOrigin.Sync, time.GetUtcNow(), cancellationToken);
            await queue.AddCountsAsync(claimed.Id, counts.Created, counts.Updated, 0, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Reached only after every page was read: deletions are never inferred from a partial listing.
        if (koboIds is not null)
        {
            var deleted = await store.MarkDeletedAsync(project.Id, koboIds, sync.StartedAt!.Value, time.GetUtcNow(), cancellationToken);
            await queue.AddCountsAsync(claimed.Id, 0, 0, deleted, cancellationToken);
        }
    }

    /// <summary>The start of the project's last successful synchronization, minus the safety margin.</summary>
    private async Task<DateTimeOffset?> CutoffAsync(ClaimedSync claimed, CancellationToken cancellationToken)
    {
        var lastStart = await db.Syncs.AsNoTracking()
            .Where(s => s.ProjectId == claimed.ProjectId && s.Id != claimed.Id && s.Status == SyncStatus.Succeeded
                        && (s.Kind == SyncKind.Incremental || s.Kind == SyncKind.Full))
            .MaxAsync(s => s.StartedAt, cancellationToken);
        return lastStart - options.Value.SafetyMargin;
    }
}
