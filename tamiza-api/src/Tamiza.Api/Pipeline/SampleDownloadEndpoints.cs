using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Pipeline;

/// <summary>
/// Samples of a project's data to work on a computer (design D8). The analytics worker prepares each as one zip and keeps it in
/// the database for an hour; the requester, and nobody else, reads its status and asks for single-use links to it.
/// </summary>
public static class SampleDownloadEndpoints
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public const string NotReadyCode = "sampleDownload.notReady";
    public const string FailedCode = "sampleDownload.failed";
    public const string IncompleteCode = "sampleDownload.incomplete";
    public const string BusyCode = "sampleDownload.busy";

    public static RouteGroupBuilder MapSampleDownloadEndpoints(this RouteGroupBuilder api)
    {
        var downloads = api.MapGroup("/projects/{projectId:guid}/sample-downloads").WithTags("Pipeline");
        downloads.MapPost("", StartAsync).RequireProjectRole(ProjectRole.Analyst).WithName("StartSampleDownload");
        downloads.MapGet("/{downloadId:guid}", GetAsync).RequireProjectRole(ProjectRole.Analyst).WithName("GetSampleDownload");
        downloads.MapPost("/{downloadId:guid}/link", LinkAsync).RequireProjectRole(ProjectRole.Analyst).WithName("LinkSampleDownload");
        return api;
    }

    private static async Task<Results<Accepted<SampleDownloadDetails>, ValidationProblem>> StartAsync(
        Guid projectId, SampleDownloadRequest? request, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var sampleSize = request?.SampleSize ?? ScriptTestEndpoints.DefaultSampleSize;
        if (sampleSize is < ScriptTestEndpoints.MinSampleSize or > ScriptTestEndpoints.MaxSampleSize)
        {
            return Problems.Validation(
                "sampleSize", $"Enter a sample of {ScriptTestEndpoints.MinSampleSize} to {ScriptTestEndpoints.MaxSampleSize:N0} submissions.");
        }

        var download = await QueueAsync(db, projectId, user.Id, sampleSize, time.GetUtcNow(), ct);
        return TypedResults.Accepted($"/api/v1/projects/{projectId}/sample-downloads/{download.Id}", await DetailsAsync(db, download, ct));
    }

    private static async Task<Results<Ok<SampleDownloadDetails>, NotFound>> GetAsync(
        Guid projectId, Guid downloadId, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var download = await FindAsync(db, projectId, downloadId, user.Id, time, ct);
        return download is null ? TypedResults.NotFound() : TypedResults.Ok(await DetailsAsync(db, download, ct));
    }

    /// <summary>
    /// Copies the stored zip into the export directory and registers a single-use link to it. The copy takes an export slot, and
    /// a link can be asked for again after one was used, for as long as the download is available.
    /// </summary>
    private static async Task<Results<Ok<SampleDownloadLink>, NotFound, ProblemHttpResult>> LinkAsync(
        Guid projectId, Guid downloadId, TamizaDbContext db, ICurrentUser user, TimeProvider time, DataDashboard dashboard,
        ExportRegistry exports, CancellationToken ct)
    {
        var download = await FindAsync(db, projectId, downloadId, user.Id, time, ct);
        if (download is null)
        {
            return TypedResults.NotFound();
        }

        if (download.Status == RunStatus.Failed)
        {
            return Problems.Conflict(FailedCode, "The sample could not be prepared. Request a new one.");
        }

        if (download.Status != RunStatus.Succeeded || download.SizeBytes is not { } size)
        {
            return Problems.Conflict(NotReadyCode, "The sample is not ready yet.");
        }

        var projectName = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Name).SingleAsync(ct);
        string? path = null;
        try
        {
            path = await dashboard.WithExportSlotAsync(() => CopyToFileAsync(db, download.Id, exports.NewFilePath(), ct), ct);
            if (new FileInfo(path).Length != size)
            {
                ExportRegistry.DeleteQuietly(path);
                path = null;
                return Problems.Conflict(IncompleteCode, "The stored sample is incomplete. Request a new one.");
            }

            var fileName = ExportFileName.Create(projectName, "sample", DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), "zip");
            var link = exports.Add(projectId, user.Id, path, fileName, "application/zip");
            path = null; // the registry owns the file now
            return TypedResults.Ok(new SampleDownloadLink($"/api/v1/data-exports/{link.Token}", fileName, size, link.ExpiresAt));
        }
        catch (DataExportBusyException)
        {
            return Problems.Unavailable(BusyCode, "Other files are being prepared. Try again in a moment.");
        }
        finally
        {
            if (path is not null)
            {
                ExportRegistry.DeleteQuietly(path);
            }
        }
    }

    /// <summary>Writes the parts in order, one at a time, so neither side holds the whole zip in memory.</summary>
    private static async Task<string> CopyToFileAsync(TamizaDbContext db, Guid downloadId, string path, CancellationToken ct)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            await foreach (var data in db.SampleDownloadParts.AsNoTracking()
                               .Where(p => p.DownloadId == downloadId).OrderBy(p => p.Part).Select(p => p.Data)
                               .AsAsyncEnumerable().WithCancellation(ct))
            {
                await file.WriteAsync(data, ct);
            }
        }
        catch
        {
            ExportRegistry.DeleteQuietly(path);
            throw;
        }

        return path;
    }

    /// <summary>The requester's download in the project, unless it ended more than an hour ago.</summary>
    private static Task<SampleDownload?> FindAsync(TamizaDbContext db, Guid projectId, Guid downloadId, Guid userId, TimeProvider time, CancellationToken ct)
    {
        var expired = time.GetUtcNow() - Lifetime;
        return db.SampleDownloads.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == downloadId && d.ProjectId == projectId && d.RequestedBy == userId
                                       && (d.FinishedAt == null || d.FinishedAt > expired), ct);
    }

    /// <summary>
    /// Replaces the user's downloads in the project that are not being prepared (queued, ready or failed) with a new queued one; a
    /// download that is running is never touched.
    /// </summary>
    private static async Task<SampleDownload> QueueAsync(
        TamizaDbContext db, Guid projectId, Guid userId, int sampleSize, DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var download = new SampleDownload
            {
                Id = Guid.CreateVersion7(now),
                ProjectId = projectId,
                RequestedBy = userId,
                SampleSize = sampleSize,
                Status = RunStatus.Queued,
                QueuedAt = now,
            };
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SampleDownloads
                .Where(d => d.ProjectId == projectId && d.RequestedBy == userId && d.Status != RunStatus.Running)
                .ExecuteDeleteAsync(ct);
            db.SampleDownloads.Add(download);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return download;
            }
            catch (DbUpdateException exception) when (
                attempt < 3 && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another request of the same user queued a download at the same time; replace that one.
                db.Entry(download).State = EntityState.Detached;
            }
        }
    }

    private static async Task<SampleDownloadDetails> DetailsAsync(TamizaDbContext db, SampleDownload download, CancellationToken ct) =>
        new(download.Id, download.Status, download.SampleSize, await WaitingForAsync(db, download, ct), download.QueuedAt,
            download.StartedAt, download.FinishedAt,
            download.Status == RunStatus.Succeeded && download.FinishedAt is { } finished ? finished + Lifetime : null,
            download.SizeBytes, download.Contents, download.ErrorCode, download.Error);

    /// <summary>What a queued download waits for: the worker runs one job at a time, tests first, then downloads, then runs.</summary>
    private static async Task<ScriptTestWait?> WaitingForAsync(TamizaDbContext db, SampleDownload download, CancellationToken ct)
    {
        if (download.Status != RunStatus.Queued)
        {
            return null;
        }

        if (await db.Runs.AnyAsync(r => r.Status == RunStatus.Running, ct))
        {
            return ScriptTestWait.Run;
        }

        if (await db.ScriptTests.AnyAsync(t => t.Status == RunStatus.Running || t.Status == RunStatus.Queued, ct))
        {
            return ScriptTestWait.Test;
        }

        var downloadAhead = await db.SampleDownloads.AnyAsync(d => d.Id != download.Id
            && (d.Status == RunStatus.Running || (d.Status == RunStatus.Queued && d.QueuedAt < download.QueuedAt)), ct);
        return downloadAhead ? ScriptTestWait.Download : null;
    }
}
