using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Pipeline;

/// <summary>Pipeline runs (queued here, executed by the analytics worker) and the project's pipeline settings.</summary>
public static class RunEndpoints
{
    public const int MinIntervalHours = 1;
    public const int MaxIntervalHours = 168;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapRunEndpoints(this RouteGroupBuilder api)
    {
        var project = api.MapGroup("/projects/{projectId:guid}").WithTags("Pipeline");
        project.MapPost("/runs", StartAsync).RequireProjectRole(ProjectRole.Analyst).WithName("StartRun");
        project.MapGet("/runs", ListAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListRuns");
        project.MapGet("/runs/{runId:guid}", GetAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetRun");
        project.MapGet("/pipeline", GetSettingsAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetPipelineSettings");
        project.MapPut("/pipeline", UpdateSettingsAsync).RequireProjectRole(ProjectRole.Admin).WithName("UpdatePipelineSettings");
        return api;
    }

    private static async Task<Results<Accepted<RunSummary>, ProblemHttpResult>> StartAsync(
        Guid projectId, TamizaDbContext db, RunQueue queue, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var run = await queue.EnqueueManualAsync(projectId, user.Id, time.GetUtcNow(), ct);
        if (run is null)
        {
            return Problems.Conflict(RunQueue.AlreadyQueuedCode, "A run of this project is already waiting to start.");
        }

        var summary = (await SummariesAsync(db, db.Runs.Where(r => r.Id == run.Id), ct)).Single();
        return TypedResults.Accepted($"/api/v1/projects/{projectId}/runs/{run.Id}", summary);
    }

    private static async Task<Ok<RunPage>> ListAsync(Guid projectId, TamizaDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var runs = db.Runs.Where(r => r.ProjectId == projectId);
        var pageQuery = runs.OrderByDescending(r => r.QueuedAt).ThenByDescending(r => r.Id).Skip((number - 1) * size).Take(size);
        return TypedResults.Ok(new RunPage(await SummariesAsync(db, pageQuery, ct), await runs.CountAsync(ct)));
    }

    private static async Task<Results<Ok<RunDetails>, NotFound>> GetAsync(Guid projectId, Guid runId, TamizaDbContext db, CancellationToken ct)
    {
        var summary = (await SummariesAsync(db, db.Runs.Where(r => r.ProjectId == projectId && r.Id == runId), ct)).SingleOrDefault();
        if (summary is null)
        {
            return TypedResults.NotFound();
        }

        var steps = await db.RunSteps.AsNoTracking().Where(s => s.RunId == runId).ToListAsync(ct);
        var referenceTables = await db.Runs.Where(r => r.Id == runId).Select(r => r.ReferenceTables).SingleAsync(ct);
        return TypedResults.Ok(new RunDetails(summary, steps
            .OrderBy(s => s.Step)
            .Select(s => new StepDetails(s.Step, s.Status, s.StartedAt, s.FinishedAt, s.InputRows, s.OutputRows, s.Tables, s.Log, s.Error))
            .ToList(), referenceTables));
    }

    private static async Task<Ok<PipelineSettings>> GetSettingsAsync(Guid projectId, TamizaDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Projects.Where(p => p.Id == projectId)
            .Select(p => new PipelineSettings(p.RunAfterSync, p.RunIntervalHours, p.NextRunAt))
            .SingleAsync(ct));

    private static async Task<Results<Ok<PipelineSettings>, ValidationProblem>> UpdateSettingsAsync(
        Guid projectId, PipelineSettingsRequest request, TamizaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        if (request.RunAfterSync is null)
        {
            errors.Add("runAfterSync", "Choose whether to run after each synchronization.");
        }

        if (request.RunIntervalHours is < MinIntervalHours or > MaxIntervalHours)
        {
            errors.Add("runIntervalHours", $"Enter a whole number of hours from {MinIntervalHours} to {MaxIntervalHours}, or no schedule.");
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        var now = time.GetUtcNow();
        project.RunAfterSync = request.RunAfterSync!.Value;
        if (project.RunIntervalHours != request.RunIntervalHours)
        {
            project.RunIntervalHours = request.RunIntervalHours;
            // Due one interval after the last run started, or now if the project never ran.
            var lastStart = await db.Runs.Where(r => r.ProjectId == projectId).MaxAsync(r => r.StartedAt, ct);
            project.NextRunAt = request.RunIntervalHours is { } hours ? (lastStart ?? now).AddHours(hours) : null;
        }

        project.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new PipelineSettings(project.RunAfterSync, project.RunIntervalHours, project.NextRunAt));
    }

    private static async Task<List<RunSummary>> SummariesAsync(TamizaDbContext db, IQueryable<Run> runs, CancellationToken ct)
    {
        var rows = await runs.AsNoTracking()
            .Select(r => new
            {
                Run = r,
                RequestedBy = r.RequestedByUser == null ? null : new Person(r.RequestedByUser.Id, r.RequestedByUser.Name),
                Clean = db.ScriptVersions.Where(v => v.Id == r.CleanVersionId).Select(v => (int?)v.Number).FirstOrDefault(),
                Output = db.ScriptVersions.Where(v => v.Id == r.OutputVersionId).Select(v => (int?)v.Number).FirstOrDefault(),
                Indicators = db.ScriptVersions.Where(v => v.Id == r.IndicatorsVersionId).Select(v => (int?)v.Number).FirstOrDefault(),
                Steps = r.Steps.Select(s => new { s.Step, s.Status, s.InputRows, s.OutputRows }).ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(row => new RunSummary(
                row.Run.Id,
                row.Run.Trigger,
                row.Run.Status,
                row.RequestedBy,
                row.Run.QueuedAt,
                row.Run.StartedAt,
                row.Run.FinishedAt,
                new RunVersions(row.Clean, row.Output, row.Indicators),
                row.Steps.OrderBy(s => s.Step).Select(s => new StepSummary(s.Step, s.Status, s.InputRows, s.OutputRows)).ToList(),
                row.Run.ErrorCode,
                row.Run.Error))
            .ToList();
    }
}
