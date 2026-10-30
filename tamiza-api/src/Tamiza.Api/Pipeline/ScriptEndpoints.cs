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
        scripts.MapGet("", ListAsync).RequireProjectRole(ProjectRole.Analyst).WithName("ListScripts");
        scripts.MapPut("/{kind}", SaveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveScript");
        scripts.MapDelete("/{kind}", RemoveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RemoveScript");
        scripts.MapGet("/{kind}/versions", VersionsAsync).RequireProjectRole(ProjectRole.Analyst).WithName("ListScriptVersions");
        scripts.MapGet("/{kind}/versions/{number:int}", VersionAsync).RequireProjectRole(ProjectRole.Analyst).WithName("GetScriptVersion");
        scripts.MapPost("/{kind}/versions/{number:int}/restore", RestoreAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RestoreScriptVersion");

        var indicators = scripts.MapGroup("/indicators");
        indicators.MapPut("/{name}", SaveIndicatorAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveIndicatorScript");
        indicators.MapDelete("/{name}", RemoveIndicatorAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RemoveIndicatorScript");
        indicators.MapGet("/{name}/versions", IndicatorVersionsAsync).RequireProjectRole(ProjectRole.Analyst).WithName("ListIndicatorScriptVersions");
        indicators.MapGet("/{name}/versions/{number:int}", IndicatorVersionAsync).RequireProjectRole(ProjectRole.Analyst).WithName("GetIndicatorScriptVersion");
        indicators.MapPost("/{name}/versions/{number:int}/restore", RestoreIndicatorAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RestoreIndicatorScriptVersion");
        return api;
    }

    internal static readonly Expression<Func<ScriptVersion, ScriptVersionDetails>> ToDetails = v => new ScriptVersionDetails(
        v.Id, v.Kind, v.Name, v.Number, v.Code == null, v.Code, v.Label, v.Description, v.Unit, v.Format,
        new Person(v.Author!.Id, v.Author.Name), v.CreatedAt, v.Origin);

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

        var outcome = await SaveFixedCoreAsync(db, projectId, scriptKind, request.Code, user.Id, time.GetUtcNow(), ScriptWrite.App, ct);
        if (outcome.Errors is { } errors)
        {
            return Problems.Validation(errors);
        }

        var version = outcome.Version!;
        return TypedResults.Created($"/api/v1/projects/{projectId}/scripts/{Lower(scriptKind)}/versions/{version.Number}", version);
    }

    /// <summary>Saves script 1 or script 2: the same checks and the same version numbering for the editor and for an AI client.</summary>
    internal static async Task<ScriptOutcome> SaveFixedCoreAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? code, Guid authorId, DateTimeOffset now, ScriptWrite write, CancellationToken ct)
    {
        var errors = new FieldErrors();
        AddCodeError(code, errors);
        return errors.Any
            ? ScriptOutcome.Invalid(errors.ToDictionary())
            : ScriptOutcome.Saved(await AddVersionAsync(db, projectId, kind, null, new Metadata(code), authorId, now, write, ct));
    }

    private static async Task<Results<Ok<ScriptVersionDetails>, ProblemHttpResult, NotFound>> RemoveAsync(
        Guid projectId, string kind, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var scriptKind))
        {
            return TypedResults.NotFound();
        }

        return RemoveResult(await RemoveCoreAsync(db, projectId, scriptKind, null, user.Id, time.GetUtcNow(), ScriptWrite.App, ct));
    }

    private static Results<Ok<ScriptVersionDetails>, ProblemHttpResult, NotFound> RemoveResult(ScriptOutcome outcome) =>
        outcome.Conflict is { } conflict ? Problems.Conflict(conflict.Code, conflict.Detail) : TypedResults.Ok(outcome.Version!);

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

        return RestoreResult(projectId, await RestoreCoreAsync(db, projectId, scriptKind, null, number, user.Id, time.GetUtcNow(), ScriptWrite.App, ct));
    }

    private static Results<Created<ScriptVersionDetails>, ValidationProblem, NotFound> RestoreResult(Guid projectId, ScriptOutcome outcome)
    {
        if (outcome.NotFound)
        {
            return TypedResults.NotFound();
        }

        if (outcome.Errors is { } errors)
        {
            return Problems.Validation(errors);
        }

        var version = outcome.Version!;
        var path = version.Kind == ScriptKind.Indicator ? $"indicators/{version.Name}" : Lower(version.Kind);
        return TypedResults.Created($"/api/v1/projects/{projectId}/scripts/{path}/versions/{version.Number}", version);
    }

    // Indicator scripts

    private static async Task<Results<Created<ScriptVersionDetails>, ValidationProblem>> SaveIndicatorAsync(
        Guid projectId, string name, SaveIndicatorRequest request, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var outcome = await SaveIndicatorCoreAsync(db, projectId, name, request, user.Id, time.GetUtcNow(), ScriptWrite.App, ct);
        if (outcome.Errors is { } errors)
        {
            return Problems.Validation(errors);
        }

        var version = outcome.Version!;
        return TypedResults.Created($"/api/v1/projects/{projectId}/scripts/indicators/{name}/versions/{version.Number}", version);
    }

    /// <summary>Saves an indicator script with its details: the same checks, limit and numbering for the editor and for an AI client.</summary>
    internal static async Task<ScriptOutcome> SaveIndicatorCoreAsync(
        TamizaDbContext db, Guid projectId, string name, SaveIndicatorRequest request, Guid authorId, DateTimeOffset now, ScriptWrite write, CancellationToken ct)
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
            return ScriptOutcome.Invalid(errors.ToDictionary());
        }

        if (await IsNewIndicatorOverLimitAsync(db, projectId, name, ct))
        {
            return ScriptOutcome.Invalid(new Dictionary<string, string[]> { ["name"] = [LimitMessage] });
        }

        return ScriptOutcome.Saved(await AddVersionAsync(db, projectId, ScriptKind.Indicator, name, metadata, authorId, now, write, ct));
    }

    private static async Task<Results<Ok<ScriptVersionDetails>, ProblemHttpResult, NotFound>> RemoveIndicatorAsync(
        Guid projectId, string name, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct) =>
        ColumnNames.IsValidTableName(name)
            ? RemoveResult(await RemoveCoreAsync(db, projectId, ScriptKind.Indicator, name, user.Id, time.GetUtcNow(), ScriptWrite.App, ct))
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
        return RestoreResult(projectId, await RestoreIndicatorCoreAsync(db, projectId, name, number, user.Id, time.GetUtcNow(), ScriptWrite.App, ct));
    }

    /// <summary>Restores a version of an indicator. Restoring a removed indicator creates it again, so the limit applies.</summary>
    internal static async Task<ScriptOutcome> RestoreIndicatorCoreAsync(
        TamizaDbContext db, Guid projectId, string name, int number, Guid authorId, DateTimeOffset now, ScriptWrite write, CancellationToken ct)
    {
        if (await IsNewIndicatorOverLimitAsync(db, projectId, name, ct))
        {
            return ScriptOutcome.Invalid(new Dictionary<string, string[]> { ["name"] = [LimitMessage] });
        }

        return await RestoreCoreAsync(db, projectId, ScriptKind.Indicator, name, number, authorId, now, write, ct);
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

    /// <summary>Marks a script as removed with a new version; its earlier versions stay and can be restored.</summary>
    internal static async Task<ScriptOutcome> RemoveCoreAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, Guid authorId, DateTimeOffset now, ScriptWrite write, CancellationToken ct)
    {
        var current = await Versions(db, projectId, kind, name).OrderByDescending(v => v.Number).FirstOrDefaultAsync(ct);
        if (current is null || current.Code is null)
        {
            return ScriptOutcome.NotSet(NotSetCode, "There is no script to remove.");
        }

        return ScriptOutcome.Saved(await AddVersionAsync(db, projectId, kind, name, new Metadata(null), authorId, now, write, ct));
    }

    /// <summary>Saves the code and details of an earlier version as a new version.</summary>
    internal static async Task<ScriptOutcome> RestoreCoreAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, int number, Guid authorId, DateTimeOffset now, ScriptWrite write, CancellationToken ct)
    {
        var source = await Versions(db, projectId, kind, name).Where(v => v.Number == number)
            .Select(v => new { v.Code, v.Label, v.Description, v.Unit, v.Format }).SingleOrDefaultAsync(ct);
        if (source is null)
        {
            return ScriptOutcome.Missing();
        }

        if (source.Code is null)
        {
            return ScriptOutcome.Invalid(new Dictionary<string, string[]> { ["number"] = ["That version marks the script as removed; choose a version with code."] });
        }

        return ScriptOutcome.Saved(await AddVersionAsync(
            db, projectId, kind, name, new Metadata(source.Code, source.Label, source.Description, source.Unit, source.Format), authorId, now, write, ct));
    }

    internal static async Task<List<ScriptVersionSummary>> SummariesAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, CancellationToken ct) =>
        await Versions(db, projectId, kind, name)
            .OrderByDescending(v => v.Number)
            .Select(v => new ScriptVersionSummary(v.Id, v.Kind, v.Name, v.Label, v.Number, v.Code == null, new Person(v.Author!.Id, v.Author.Name), v.CreatedAt, v.Origin))
            .ToListAsync(ct);

    /// <summary>The code of a version and, for an indicator, the metadata saved with it; all null for a removal.</summary>
    private sealed record Metadata(string? Code, string? Label = null, string? Description = null, string? Unit = null, string? Format = null);

    /// <summary>Adds the next version number for the script; retries when two saves race for the same number.</summary>
    private static async Task<ScriptVersionDetails> AddVersionAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, Metadata metadata, Guid authorId, DateTimeOffset now, ScriptWrite write,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var latest = await Versions(db, projectId, kind, name).MaxAsync(v => (int?)v.Number, ct) ?? 0;
            if (write.GuardsBase && latest != (write.BaseVersion ?? 0))
            {
                // Checked on every attempt, so a save that races another one is refused instead of numbered after it.
                throw await ScriptChangedException.ForAsync(db, projectId, kind, name, latest, ct);
            }

            var number = latest + 1;
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
                Origin = write.Origin,
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

    internal static IQueryable<ScriptVersion> Versions(TamizaDbContext db, Guid projectId, ScriptKind kind, string? name) =>
        db.ScriptVersions.AsNoTracking().Where(v => v.ProjectId == projectId && v.Kind == kind && v.Name == name);

    /// <summary>Script 1 or script 2; indicators have their own routes.</summary>
    private static bool TryParseKind(string value, out ScriptKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && kind is ScriptKind.Clean or ScriptKind.Output && !int.TryParse(value, out _);

    private static string Lower(ScriptKind kind) => kind.ToString().ToLowerInvariant();
}

/// <summary>Where a version is written from and, for an AI client, which version it read as the current one (design D9).</summary>
/// <param name="BaseVersion">The number of the version the writer read as current; null means it saw no version, so the script never existed.</param>
/// <param name="GuardsBase">Whether <paramref name="BaseVersion"/> must still be the current version for the write to go ahead.</param>
internal sealed record ScriptWrite(ScriptOrigin Origin, int? BaseVersion, bool GuardsBase)
{
    /// <summary>The editor and the REST API: no guard, as before.</summary>
    public static readonly ScriptWrite App = new(ScriptOrigin.App, null, false);

    public static ScriptWrite Mcp(int? baseVersion) => new(ScriptOrigin.Mcp, baseVersion, true);
}

/// <summary>The result of saving, removing or restoring a script, before it becomes an HTTP result or a tool result.</summary>
internal sealed record ScriptOutcome(
    ScriptVersionDetails? Version, IDictionary<string, string[]>? Errors = null, bool NotFound = false, (string Code, string Detail)? Conflict = null)
{
    public static ScriptOutcome Saved(ScriptVersionDetails version) => new(version);

    public static ScriptOutcome Invalid(IDictionary<string, string[]> errors) => new(null, errors);

    public static ScriptOutcome Missing() => new(null, NotFound: true);

    public static ScriptOutcome NotSet(string code, string detail) => new(null, Conflict: (code, detail));
}

/// <summary>A guarded write found that the script's current version is not the one the writer read.</summary>
internal sealed class ScriptChangedException(int current, string author, DateTimeOffset at, bool removed)
    : Exception("The script was changed since it was read.")
{
    public int Current { get; } = current;

    public string Author { get; } = author;

    public DateTimeOffset At { get; } = at;

    /// <summary>Whether the current version marks the script as removed.</summary>
    public bool Removed { get; } = removed;

    public static async Task<ScriptChangedException> ForAsync(
        TamizaDbContext db, Guid projectId, ScriptKind kind, string? name, int latest, CancellationToken ct)
    {
        var current = await db.ScriptVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.Kind == kind && v.Name == name && v.Number == latest)
            .Select(v => new { Author = v.Author!.Name, v.CreatedAt, Removed = v.Code == null })
            .SingleOrDefaultAsync(ct);
        return new ScriptChangedException(latest, current?.Author ?? "", current?.CreatedAt ?? DateTimeOffset.MinValue, current?.Removed ?? false);
    }
}
