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

/// <summary>The scripts of a project, script 1, script 2 and its named indicators, and their immutable versions.</summary>
public static class ScriptEndpoints
{
    public const int MaxCodeBytes = 200 * 1024;
    public const int MaxIndicators = 100;
    public const int MaxLabelLength = 120;
    public const int MaxDescriptionLength = 1000;
    public const int MaxUnitLength = 32;
    public const string NotSetCode = "script.not_set";
    public const string IndicatorLimitCode = "script.indicator_limit";

    /// <summary>What an indicator's <c>format</c> may be.</summary>
    public static readonly IReadOnlyList<string> Formats = ["count", "decimal", "percent", "currency"];

    public static RouteGroupBuilder MapScriptEndpoints(this RouteGroupBuilder api)
    {
        var scripts = api.MapGroup("/projects/{projectId:guid}/scripts").WithTags("Pipeline");
        scripts.MapGet("", ListAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListScripts");
        scripts.MapPut("/{kind}", SaveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveScript");
        scripts.MapDelete("/{kind}", RemoveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RemoveScript");
        scripts.MapGet("/{kind}/versions", VersionsAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListScriptVersions");
        scripts.MapGet("/{kind}/versions/{number:int}", VersionAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetScriptVersion");
        scripts.MapPost("/{kind}/versions/{number:int}/restore", RestoreAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RestoreScriptVersion");

        var indicators = scripts.MapGroup("/indicators");
        indicators.MapPut("/{name}", SaveIndicatorAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveIndicatorScript");
        indicators.MapDelete("/{name}", RemoveIndicatorAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RemoveIndicatorScript");
        indicators.MapGet("/{name}/versions", IndicatorVersionsAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListIndicatorScriptVersions");
        indicators.MapGet("/{name}/versions/{number:int}", IndicatorVersionAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetIndicatorScriptVersion");
        indicators.MapPost("/{name}/versions/{number:int}/restore", RestoreIndicatorAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RestoreIndicatorScriptVersion");
        return api;
    }

    private static readonly Expression<Func<ScriptVersion, ScriptVersionDetails>> ToDetails = v => new ScriptVersionDetails(
        v.Id, v.Kind, v.Name, v.Number, v.Code == null, v.Code, v.Label, v.Description, v.Unit, v.Format,
        new Person(v.Author!.Id, v.Author.Name), v.CreatedAt);

    /// <summary>The current version of every indicator the project has: the latest of each name, unless it marks the indicator removed.</summary>
    internal static async Task<List<ScriptVersionDetails>> CurrentIndicatorsAsync(TamizaDbContext db, Guid projectId, CancellationToken ct) =>
        (await db.ScriptVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.Kind == ScriptKind.Indicator && v.Code != null
                        && v.Number == db.ScriptVersions.Where(o => o.ProjectId == projectId && o.Kind == v.Kind && o.Name == v.Name).Max(o => o.Number))
            .Select(ToDetails)
            .ToListAsync(ct))
        .OrderBy(v => v.Name, StringComparer.Ordinal)
        .ToList();

    private static async Task<Ok<ScriptList>> ListAsync(Guid projectId, TamizaDbContext db, CancellationToken ct)
    {
        var latest = await db.ScriptVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId
                        && v.Number == db.ScriptVersions
                            .Where(o => o.ProjectId == projectId && o.Kind == v.Kind && o.Name == v.Name).Max(o => o.Number))
            .Select(ToDetails)
            .ToListAsync(ct);
        var indicators = latest
            .Where(v => v.Kind == ScriptKind.Indicator && !v.Removed)
            .OrderBy(v => v.Name, StringComparer.Ordinal)
            .Select(v => new IndicatorScript(v.Name!, v.Label!, v.Description, v.Unit, v.Format!, v))
            .ToList();
        return TypedResults.Ok(new ScriptList(
            new ProjectScript(ScriptKind.Clean, latest.SingleOrDefault(v => v.Kind == ScriptKind.Clean)),
            new ProjectScript(ScriptKind.Output, latest.SingleOrDefault(v => v.Kind == ScriptKind.Output)),
            indicators));
    }

    // Script 1 and script 2

    private static async Task<Results<Created<ScriptVersionDetails>, ValidationProblem, NotFound>> SaveAsync(
        Guid projectId, string kind, SaveScriptRequest request, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        var errors = new FieldErrors();
        AddCodeError(request.Code, errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var version = await AddVersionAsync(db, projectId, scriptKind, null, new Metadata(request.Code), user.Id, time.GetUtcNow(), ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/scripts/{Lower(scriptKind)}/versions/{version.Number}", version);
    }

    private static async Task<Results<Ok<ScriptVersionDetails>, ProblemHttpResult, NotFound>> RemoveAsync(
        Guid projectId, string kind, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        return await RemoveCoreAsync(db, projectId, scriptKind, null, user.Id, time.GetUtcNow(), ct);
    }

    private static async Task<Results<Ok<List<ScriptVersionSummary>>, NotFound>> VersionsAsync(
        Guid projectId, string kind, TamizaDbContext db, CancellationToken ct) =>
        TryParseKind(kind, out var scriptKind)
            ? TypedResults.Ok(await SummariesAsync(db, projectId, scriptKind, null, ct))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ScriptVersionDetails>, NotFound>> VersionAsync(
        Guid projectId, string kind, int number, TamizaDbContext db, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        var version = await Versions(db, projectId, scriptKind, null).Where(v => v.Number == number).Select(ToDetails).SingleOrDefaultAsync(ct);
        return version is null ? TypedResults.NotFound() : TypedResults.Ok(version);
    }

    private static async Task<Results<Created<ScriptVersionDetails>, ValidationProblem, NotFound>> RestoreAsync(
        Guid projectId, string kind, int number, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        return await RestoreCoreAsync(db, projectId, scriptKind, null, number, user.Id, time.GetUtcNow(), ct);
    }

    // Indicator scripts

    private static async Task<Results<Created<ScriptVersionDetails>, ValidationProblem>> SaveIndicatorAsync(
        Guid projectId, string name, SaveIndicatorRequest request, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        if (!ColumnNames.IsValidTableName(name))
        {
            errors.Add("name", InvalidNameMessage);
        }

        AddCodeError(request.Code, errors);
        var metadata = ValidateMetadata(request, errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        if (await IsNewIndicatorOverLimitAsync(db, projectId, name, ct))
        {
            return Problems.Validation("name", LimitMessage);
        }

        var version = await AddVersionAsync(db, projectId, ScriptKind.Indicator, name, metadata, user.Id, time.GetUtcNow(), ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/scripts/indicators/{name}/versions/{version.Number}", version);
    }

    private static async Task<Results<Ok<ScriptVersionDetails>, ProblemHttpResult, NotFound>> RemoveIndicatorAsync(
        Guid projectId, string name, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct) =>
        ColumnNames.IsValidTableName(name)
            ? await RemoveCoreAsync(db, projectId, ScriptKind.Indicator, name, user.Id, time.GetUtcNow(), ct)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<List<ScriptVersionSummary>>, NotFound>> IndicatorVersionsAsync(
        Guid projectId, string name, TamizaDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await SummariesAsync(db, projectId, ScriptKind.Indicator, name, ct));

    private static async Task<Results<Ok<ScriptVersionDetails>, NotFound>> IndicatorVersionAsync(
        Guid projectId, string name, int number, TamizaDbContext db, CancellationToken ct)
    {
        var version = await Versions(db, projectId, ScriptKind.Indicator, name).Where(v => v.Number == number).Select(ToDetails).SingleOrDefaultAsync(ct);
        return version is null ? TypedResults.NotFound() : TypedResults.Ok(version);
    }

    private static async Task<Results<Created<ScriptVersionDetails>, ValidationProblem, NotFound>> RestoreIndicatorAsync(
        Guid projectId, string name, int number, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        // Restoring a removed indicator creates it again, so the limit applies.
        if (await IsNewIndicatorOverLimitAsync(db, projectId, name, ct))
        {
            return Problems.Validation("name", LimitMessage);
        }

        return await RestoreCoreAsync(db, projectId, ScriptKind.Indicator, name, number, user.Id, time.GetUtcNow(), ct);
    }

    // Shared

    private static readonly string LimitMessage = $"A project can have at most {MaxIndicators} indicators. Remove one before adding another.";

    private const string InvalidNameMessage =
        "Use lowercase letters, digits and underscores only, starting with a letter, up to 63 characters (for example piped_water_rate).";

    private static void AddCodeError(string? code, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            errors.Add("code", "Enter the script's code. To stop using the script, remove it instead.");
        }
        else if (Encoding.UTF8.GetByteCount(code) > MaxCodeBytes)
        {
            errors.Add("code", $"The script is larger than {MaxCodeBytes / 1024} KB.");
        }
    }

    private static Metadata ValidateMetadata(SaveIndicatorRequest request, FieldErrors errors)
    {
        var label = request.Label?.Trim();
        if (string.IsNullOrEmpty(label) || label.Length > MaxLabelLength)
        {
            errors.Add("label", $"Enter a label of 1 to {MaxLabelLength} characters.");
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
        {
            errors.Add("description", $"The description can have up to {MaxDescriptionLength:N0} characters.");
        }

        var unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim();
        if (unit is { Length: > MaxUnitLength })
        {
            errors.Add("unit", $"The unit can have up to {MaxUnitLength} characters.");
        }

        var format = request.Format?.Trim().ToLowerInvariant();
        if (format is null || !Formats.Contains(format))
        {
            errors.Add("format", "Choose a format: count, decimal, percent or currency.");
        }

        return new Metadata(request.Code, label, description, unit, format);
    }

    /// <summary>True when the name has no current version and the project already has the maximum number of indicators.</summary>
    private static async Task<bool> IsNewIndicatorOverLimitAsync(
        TamizaDbContext db, Guid projectId, string name, CancellationToken ct)
    {
        var current = db.ScriptVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.Kind == ScriptKind.Indicator
                        && v.Number == db.ScriptVersions.Where(o => o.ProjectId == projectId && o.Kind == v.Kind && o.Name == v.Name).Max(o => o.Number)
                        && v.Code != null);
        var count = await current.CountAsync(ct);
        return count >= MaxIndicators && !await current.AnyAsync(v => v.Name == name, ct);
    }

    private static async Task<Results<Ok<ScriptVersionDetails>, ProblemHttpResult, NotFound>> RemoveCoreAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, Guid authorId, DateTimeOffset now, CancellationToken ct)
    {
        var current = await Versions(db, projectId, kind, name).OrderByDescending(v => v.Number).FirstOrDefaultAsync(ct);
        if (current is null || current.Code is null)
        {
            return Problems.Conflict(NotSetCode, "There is no script to remove.");
        }

        return TypedResults.Ok(await AddVersionAsync(db, projectId, kind, name, new Metadata(null), authorId, now, ct));
    }

    private static async Task<Results<Created<ScriptVersionDetails>, ValidationProblem, NotFound>> RestoreCoreAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, int number, Guid authorId, DateTimeOffset now, CancellationToken ct)
    {
        var source = await Versions(db, projectId, kind, name).Where(v => v.Number == number)
            .Select(v => new { v.Code, v.Label, v.Description, v.Unit, v.Format }).SingleOrDefaultAsync(ct);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        if (source.Code is null)
        {
            return Problems.Validation("number", "That version marks the script as removed; choose a version with code.");
        }

        var version = await AddVersionAsync(
            db, projectId, kind, name, new Metadata(source.Code, source.Label, source.Description, source.Unit, source.Format), authorId, now, ct);
        var path = kind == ScriptKind.Indicator ? $"indicators/{name}" : Lower(kind);
        return TypedResults.Created($"/api/v1/projects/{projectId}/scripts/{path}/versions/{version.Number}", version);
    }

    private static async Task<List<ScriptVersionSummary>> SummariesAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, CancellationToken ct) =>
        await Versions(db, projectId, kind, name)
            .OrderByDescending(v => v.Number)
            .Select(v => new ScriptVersionSummary(v.Id, v.Kind, v.Name, v.Label, v.Number, v.Code == null, new Person(v.Author!.Id, v.Author.Name), v.CreatedAt))
            .ToListAsync(ct);

    /// <summary>The code of a version and, for an indicator, the metadata saved with it; all null for a removal.</summary>
    private sealed record Metadata(string? Code, string? Label = null, string? Description = null, string? Unit = null, string? Format = null);

    /// <summary>Adds the next version number for the script; retries when two saves race for the same number.</summary>
    private static async Task<ScriptVersionDetails> AddVersionAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, Metadata metadata, Guid authorId, DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var number = (await Versions(db, projectId, kind, name).MaxAsync(v => (int?)v.Number, ct) ?? 0) + 1;
            var version = new ScriptVersion
            {
                Id = Guid.CreateVersion7(now),
                ProjectId = projectId,
                Kind = kind,
                Name = name,
                Number = number,
                Code = metadata.Code,
                Label = metadata.Label,
                Description = metadata.Description,
                Unit = metadata.Unit,
                Format = metadata.Format,
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

    private static IQueryable<ScriptVersion> Versions(TamizaDbContext db, Guid projectId, ScriptKind kind, string? name) =>
        db.ScriptVersions.AsNoTracking().Where(v => v.ProjectId == projectId && v.Kind == kind && v.Name == name);

    /// <summary>Script 1 or script 2; indicators have their own routes.</summary>
    private static bool TryParseKind(string value, out ScriptKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && kind is ScriptKind.Clean or ScriptKind.Output && !int.TryParse(value, out _);

    private static string Lower(ScriptKind kind) => kind.ToString().ToLowerInvariant();
}
