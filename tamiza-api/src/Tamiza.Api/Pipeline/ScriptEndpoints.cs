using System.Linq.Expressions;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Pipeline;

/// <summary>The three scripts of a project and their immutable versions.</summary>
public static class ScriptEndpoints
{
    public const int MaxCodeBytes = 200 * 1024;
    public const string NotSetCode = "script.not_set";

    public static RouteGroupBuilder MapScriptEndpoints(this RouteGroupBuilder api)
    {
        var scripts = api.MapGroup("/projects/{projectId:guid}/scripts").WithTags("Pipeline");
        scripts.MapGet("", ListAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListScripts");
        scripts.MapPut("/{kind}", SaveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveScript");
        scripts.MapDelete("/{kind}", RemoveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RemoveScript");
        scripts.MapGet("/{kind}/versions", VersionsAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListScriptVersions");
        scripts.MapGet("/{kind}/versions/{number:int}", VersionAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetScriptVersion");
        scripts.MapPost("/{kind}/versions/{number:int}/restore", RestoreAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RestoreScriptVersion");
        return api;
    }

    private static readonly Expression<Func<ScriptVersion, ScriptVersionDetails>> ToDetails = v => new ScriptVersionDetails(
        v.Id, v.Kind, v.Number, v.Code == null, v.Code, new Person(v.Author!.Id, v.Author.Name), v.CreatedAt);

    private static async Task<Ok<List<ProjectScript>>> ListAsync(Guid projectId, TamizaDbContext db, CancellationToken ct)
    {
        var current = await db.ScriptVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId
                        && v.Number == db.ScriptVersions.Where(o => o.ProjectId == projectId && o.Kind == v.Kind).Max(o => o.Number))
            .Select(ToDetails)
            .ToListAsync(ct);
        return TypedResults.Ok(Enum.GetValues<ScriptKind>()
            .Select(kind => new ProjectScript(kind, current.SingleOrDefault(v => v.Kind == kind)))
            .ToList());
    }

    private static async Task<Results<Created<ScriptVersionDetails>, ValidationProblem, NotFound>> SaveAsync(
        Guid projectId, string kind, SaveScriptRequest request, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Problems.Validation("code", "Enter the script's code. To stop using the script, remove it instead.");
        }

        if (Encoding.UTF8.GetByteCount(request.Code) > MaxCodeBytes)
        {
            return Problems.Validation("code", $"The script is larger than {MaxCodeBytes / 1024} KB.");
        }

        var version = await AddVersionAsync(db, projectId, scriptKind, request.Code, user.Id, time.GetUtcNow(), ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/scripts/{Lower(scriptKind)}/versions/{version.Number}", version);
    }

    private static async Task<Results<Ok<ScriptVersionDetails>, ProblemHttpResult, NotFound>> RemoveAsync(
        Guid projectId, string kind, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        var current = await Versions(db, projectId, scriptKind).OrderByDescending(v => v.Number).FirstOrDefaultAsync(ct);
        if (current is null || current.Code is null)
        {
            return Problems.Conflict(NotSetCode, "There is no script to remove.");
        }

        return TypedResults.Ok(await AddVersionAsync(db, projectId, scriptKind, null, user.Id, time.GetUtcNow(), ct));
    }

    private static async Task<Results<Ok<List<ScriptVersionSummary>>, NotFound>> VersionsAsync(
        Guid projectId, string kind, TamizaDbContext db, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await Versions(db, projectId, scriptKind)
            .OrderByDescending(v => v.Number)
            .Select(v => new ScriptVersionSummary(v.Id, v.Kind, v.Number, v.Code == null, new Person(v.Author!.Id, v.Author.Name), v.CreatedAt))
            .ToListAsync(ct));
    }

    private static async Task<Results<Ok<ScriptVersionDetails>, NotFound>> VersionAsync(
        Guid projectId, string kind, int number, TamizaDbContext db, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        var version = await Versions(db, projectId, scriptKind).Where(v => v.Number == number).Select(ToDetails).SingleOrDefaultAsync(ct);
        return version is null ? TypedResults.NotFound() : TypedResults.Ok(version);
    }

    private static async Task<Results<Created<ScriptVersionDetails>, ValidationProblem, NotFound>> RestoreAsync(
        Guid projectId, string kind, int number, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        var source = await Versions(db, projectId, scriptKind).Where(v => v.Number == number).Select(v => new { v.Code }).SingleOrDefaultAsync(ct);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        if (source.Code is null)
        {
            return Problems.Validation("number", "That version marks the script as removed; choose a version with code.");
        }

        var version = await AddVersionAsync(db, projectId, scriptKind, source.Code, user.Id, time.GetUtcNow(), ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/scripts/{Lower(scriptKind)}/versions/{version.Number}", version);
    }

    /// <summary>Adds the next version number for the kind; retries when two saves race for the same number.</summary>
    private static async Task<ScriptVersionDetails> AddVersionAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? code, Guid authorId, DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var number = (await Versions(db, projectId, kind).MaxAsync(v => (int?)v.Number, ct) ?? 0) + 1;
            var version = new ScriptVersion
            {
                Id = Guid.CreateVersion7(now),
                ProjectId = projectId,
                Kind = kind,
                Number = number,
                Code = code,
                AuthorId = authorId,
                CreatedAt = now,
            };
            db.ScriptVersions.Add(version);
            try
            {
                await db.SaveChangesAsync(ct);
                return await db.ScriptVersions.AsNoTracking().Where(v => v.Id == version.Id).Select(ToDetails).SingleAsync(ct);
            }
            catch (DbUpdateException exception) when (
                attempt < 3 && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(version).State = EntityState.Detached;
            }
        }
    }

    private static IQueryable<ScriptVersion> Versions(TamizaDbContext db, Guid projectId, ScriptKind kind) =>
        db.ScriptVersions.AsNoTracking().Where(v => v.ProjectId == projectId && v.Kind == kind);

    private static bool TryParseKind(string value, out ScriptKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind) && !int.TryParse(value, out _);

    private static string Lower(ScriptKind kind) => kind.ToString().ToLowerInvariant();
}
