using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Ingestion;

public static class IngestionEndpoints
{
    public const int MinIntervalHours = 1;
    public const int MaxIntervalHours = 168;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapIngestionEndpoints(this RouteGroupBuilder api)
    {
        var project = api.MapGroup("/projects/{projectId:guid}").WithTags("Ingestion");
        project.MapGet("/ingestion", GetStatusAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetIngestionStatus");
        project.MapPatch("/ingestion", UpdateAsync).RequireProjectRole(ProjectRole.Admin).WithName("UpdateIngestionSettings");
        project.MapPost("/syncs", StartAsync).RequireProjectRole(ProjectRole.Analyst).WithName("StartSync");
        project.MapGet("/syncs", ListAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListSyncs");
        return api;
    }

    private static readonly Expression<Func<Sync, SyncItem>> ToItem = s => new SyncItem(
        s.Id, s.Kind, s.Trigger, s.Status,
        s.RequestedByUser == null ? null : new SyncRequester(s.RequestedByUser.Id, s.RequestedByUser.Name),
        s.QueuedAt, s.StartedAt, s.FinishedAt, s.CreatedCount, s.UpdatedCount, s.DeletedCount, s.ErrorCode, s.Error);

    private static async Task<Ok<IngestionStatus>> GetStatusAsync(Guid projectId, TamizaDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await StatusAsync(db, projectId, ct));

    private static async Task<Results<Ok<IngestionStatus>, ValidationProblem>> UpdateAsync(
        Guid projectId, UpdateIngestionRequest request, TamizaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        ValidateInterval(request.SyncIntervalHours, "syncIntervalHours", errors);
        ValidateInterval(request.ReconcileIntervalHours, "reconcileIntervalHours", errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        var now = time.GetUtcNow();

        // The next run is due one new interval after the last run of that kind started (or now, if none has).
        if (request.SyncIntervalHours is { } syncHours)
        {
            project.SyncIntervalHours = syncHours;
            project.NextSyncAt = (await LastStartAsync(db, projectId, s => s.Kind == SyncKind.Incremental || s.Kind == SyncKind.Full, ct))?.AddHours(syncHours) ?? now;
        }

        if (request.ReconcileIntervalHours is { } reconcileHours)
        {
            project.ReconcileIntervalHours = reconcileHours;
            project.NextReconcileAt = (await LastStartAsync(db, projectId, s => s.Kind == SyncKind.Reconciliation || s.Kind == SyncKind.Full, ct))?.AddHours(reconcileHours) ?? now;
        }

        project.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await StatusAsync(db, projectId, ct));
    }

    private static async Task<Results<Accepted<SyncItem>, ValidationProblem, ProblemHttpResult>> StartAsync(
        Guid projectId, StartSyncRequest? request, TamizaDbContext db, SyncQueue queue, IngestionSignal signal,
        ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var kind = request?.Kind ?? SyncKind.Incremental;
        if (kind == SyncKind.Reconciliation)
        {
            return Problems.Validation("kind", "Choose incremental or full; reconciliations run on their schedule.");
        }

        var sync = await queue.EnqueueManualAsync(projectId, kind, user.Id, time.GetUtcNow(), ct);
        if (sync is null)
        {
            return Problems.Conflict(SyncQueue.InProgressCode, "A synchronization of this project is already in progress.");
        }

        signal.Wake();
        var item = await db.Syncs.AsNoTracking().Where(s => s.Id == sync.Id).Select(ToItem).SingleAsync(ct);
        return TypedResults.Accepted($"/api/v1/projects/{projectId}/syncs", item);
    }

    private static async Task<Ok<SyncPage>> ListAsync(Guid projectId, TamizaDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var syncs = db.Syncs.AsNoTracking().Where(s => s.ProjectId == projectId);
        var items = await syncs
            .OrderByDescending(s => s.QueuedAt).ThenByDescending(s => s.Id)
            .Skip((number - 1) * size).Take(size)
            .Select(ToItem)
            .ToListAsync(ct);
        return TypedResults.Ok(new SyncPage(items, await syncs.CountAsync(ct)));
    }

    private static async Task<IngestionStatus> StatusAsync(TamizaDbContext db, Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
        var submissions = db.RawSubmissions.Where(s => s.ProjectId == projectId);
        var syncs = db.Syncs.AsNoTracking().Where(s => s.ProjectId == projectId).OrderByDescending(s => s.QueuedAt);
        return new IngestionStatus(
            await submissions.LongCountAsync(s => !s.Deleted, ct),
            await submissions.LongCountAsync(s => s.Deleted, ct),
            project.LastWebhookAt,
            await syncs.Where(s => s.Status == SyncStatus.Succeeded || s.Status == SyncStatus.Failed).Select(ToItem).FirstOrDefaultAsync(ct),
            await syncs.Where(s => s.Status == SyncStatus.Queued || s.Status == SyncStatus.Running).Select(ToItem).FirstOrDefaultAsync(ct),
            project.NextSyncAt,
            project.NextReconcileAt,
            project.SyncIntervalHours,
            project.ReconcileIntervalHours);
    }

    private static Task<DateTimeOffset?> LastStartAsync(TamizaDbContext db, Guid projectId, Expression<Func<Sync, bool>> ofKind, CancellationToken ct) =>
        db.Syncs.Where(s => s.ProjectId == projectId).Where(ofKind).MaxAsync(s => s.StartedAt, ct);

    private static void ValidateInterval(int? hours, string field, FieldErrors errors)
    {
        if (hours is < MinIntervalHours or > MaxIntervalHours)
        {
            errors.Add(field, $"Enter a whole number of hours from {MinIntervalHours} to {MaxIntervalHours}.");
        }
    }
}
