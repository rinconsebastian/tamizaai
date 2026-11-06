using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Indicators;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>The project's Python scripts: reading them, and writing them as new versions that can always be restored.</summary>
[McpServerToolType]
public sealed class McpScriptTools(
    McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options, TamizaDbContext db, ICurrentUser user, TimeProvider time)
    : McpToolBase(gate, call, options)
{
    private const string KindHelp = "clean (script 1), output (script 2) or indicator (one per indicator, with a name).";

    /// <summary>The <c>columns</c> argument of <c>save_script</c> (design D11 of the indicator column metadata).</summary>
    private const string ColumnsHelp =
        "Optional, for an indicator: { column: { label } } names columns of its table for people, 1 to 80 characters with no line break, at most 100 columns, keyed by the column's name in lower case " +
        "(for example numerator and denominator). Left out, the current version's labels are kept; {} clears them; given, it replaces them all. " +
        "A column that widgets group or split by can also take order (its values as full text, in the order to draw them, up to 100), last (up to 20 values kept at the end whatever the order or the sort, such as Otro, No sabe or Sin dato, none also in order) " +
        "and stripPrefix: true (hides a leading number such as \"1. \" in what widgets show; filters and colors kept by value still use the full text). A column may have only these. " +
        "A column can also declare what the widgets' counts of people (n) need: base: true (a numeric column that counts the units each row stands for, such as hogares; at most one column, never also missing or categories), " +
        "missing (up to 20 values, as exact text, that mean no data, such as Sin dato or No sabe; a widget with missingFooter leaves them out and says what they held) " +
        "and categories, single or multiple (the columns of a long table that name a question and its answers, such as variable, orden and categoria; multiple when any question of the column is multiple-choice; never a column that splits the households, such as comuna or estrato). " +
        "Declared answers change a ratio over several of them: single counts the base once across the answers (each value divides its numerators by the largest per-answer denominator sum, per group), " +
        "and multiple makes a widget that would add up several answers show a message instead of a figure, since it would count a person once per answer; declare multiple when in doubt. " +
        "The map is replaced whole, so send each column's order, last and stripPrefix again with its name, and its base, missing and categories too (get_script shows what the current version has). A property that is not one of these is ignored.";

    [McpServerTool(Name = "get_authoring_guide", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get the script authoring guide"),
     Description("The rules for writing a script in Tamiza: the function contract of each kind, the conventions of the data (labels not codes, nullable integers, select_multiple joined by '; '), " +
                 "the members of ctx, the libraries available and not available, how cells and ctx.show work, and starter templates of the kind. Read it before writing any script. " +
                 "Needs the analyst role in a project AI clients may use.")]
    public Task<CallToolResult> GetAuthoringGuide(
        [Description("clean, output or indicator.")] string kind,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var scriptKind = ParseKind(kind);
            await RequireAnyProjectAsync(ct);
            return McpGuide.AuthoringGuide(scriptKind.ToString().ToLowerInvariant());
        });

    [McpServerTool(Name = "list_scripts", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "List the scripts"),
     Description("The project's scripts with the current version number of each (script 1, script 2 and every indicator), who saved it, when, and whether it was saved through MCP. " +
                 "The version number is what save_script, remove_script and restore_script_version need as baseVersion. Use get_script for the code.")]
    public Task<CallToolResult> ListScripts([Description("The project's id, from list_projects.")] Guid projectId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var current = await McpScripts.CurrentAsync(db, projectId, ct);
            object? Fixed(ScriptKind kind) => current.Where(s => s.Kind == kind).Select(s => new { s.Version, s.Removed, s.Author, s.SavedAt, s.Origin }).FirstOrDefault();
            return new
            {
                clean = Fixed(ScriptKind.Clean),
                output = Fixed(ScriptKind.Output),
                indicators = current.Where(s => s.Kind == ScriptKind.Indicator).OrderBy(s => s.Name, StringComparer.Ordinal)
                    .Select(s => new { s.Name, s.Label, s.Unit, s.Format, s.Version, s.Removed, s.Author, s.SavedAt, s.Origin }).ToList(),
            };
        });

    [McpServerTool(Name = "get_script", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get a script's code"),
     Description("The code of a script and, for an indicator, its label, description, unit, format and column labels (columns), with the base, no-data and answer columns it declares. The current version unless you name one.")]
    public Task<CallToolResult> GetScript(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description(KindHelp)] string kind,
        [Description("The indicator's name. Only for kind indicator.")] string? name = null,
        [Description("Optional. A version number from list_script_versions; the current one when omitted.")] int? version = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var (scriptKind, scriptName) = ParseScript(kind, name);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var versions = ScriptEndpoints.Versions(db, projectId, scriptKind, scriptName);
            var query = version is { } number ? versions.Where(v => v.Number == number) : versions.OrderByDescending(v => v.Number).Take(1);
            var found = await query.Select(ScriptEndpoints.ToDetails).SingleOrDefaultAsync(ct)
                ?? throw McpRefusal.NotFound(version is null ? "The script" : $"Version {version}", "Call list_scripts or list_script_versions to see what exists.");
            return found;
        }, hint: "The script is larger than the result size; read the part you need.");

    [McpServerTool(Name = "list_script_versions", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "List the versions of a script"),
     Description("Every version of a script, newest first, with its number, author, time, whether it marks the script as removed and whether it was saved through MCP. No code.")]
    public Task<CallToolResult> ListScriptVersions(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description(KindHelp)] string kind,
        [Description("The indicator's name. Only for kind indicator.")] string? name = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var (scriptKind, scriptName) = ParseScript(kind, name);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            return new { versions = await ScriptEndpoints.SummariesAsync(db, projectId, scriptKind, scriptName, ct) };
        }, hint: "Older versions are left out.");

    [McpServerTool(Name = "save_script", Title = "Save a script", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Saves a script as a new version (earlier versions stay and can be restored). Test the draft with test_script first. " +
                 "Pass baseVersion = the version number you read with list_scripts or get_script (omit it for a script that does not exist yet): if someone saved in the meantime the call is refused with mcp.scriptChanged and nothing changes. " +
                 "An indicator needs a name (lowercase letters, digits, underscores; it becomes its table's name and cannot change), a label, a format (count, decimal, percent or currency) and optionally a description and a unit. " +
                 "It can also name columns of its table for people with columns = { column: { label } }, such as numerator and denominator, which widgets then show: unlike the label, description and unit, which each save replaces, columns left out keeps the current version's labels, {} clears them, and given it replaces them all. " +
                 "The same columns can declare the base column (base), the values that mean no data (missing) and the answer columns of a long table (categories), so widgets can say how many people each value rests on. " +
                 "Code that imports os, subprocess, socket and similar, calls eval, exec or open, or holds a web address is refused.")]
    public Task<CallToolResult> SaveScript(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description(KindHelp)] string kind,
        [Description("The whole script.")] string code,
        [Description("The indicator's name. Only for kind indicator.")] string? name = null,
        [Description("The indicator's label. Only for kind indicator.")] string? label = null,
        [Description("The indicator's description. Optional.")] string? description = null,
        [Description("The indicator's unit, such as '%'. Optional.")] string? unit = null,
        [Description("The indicator's format: count, decimal, percent (a fraction shown as a percentage) or currency.")] string? format = null,
        [Description("The version number you last read as current; omit for a script that does not exist yet.")] int? baseVersion = null,
        [Description(ColumnsHelp)] IReadOnlyDictionary<string, IndicatorColumnMetadata?>? columns = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var (scriptKind, scriptName) = ParseScript(kind, name);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            if (UnsafeCode.RefusalFor(code) is { } unsafeCode)
            {
                throw unsafeCode;
            }

            var write = ScriptWrite.Mcp(baseVersion);
            var now = time.GetUtcNow();
            // columns is for an indicator only: script 1 and script 2 hold none, as they hold no label.
            var outcome = await GuardedAsync(() => scriptKind == ScriptKind.Indicator
                ? ScriptEndpoints.SaveIndicatorCoreAsync(db, projectId, scriptName!, new SaveIndicatorRequest(code, label, description, unit, format, columns), user.Id, now, write, ct)
                : ScriptEndpoints.SaveFixedCoreAsync(db, projectId, scriptKind, code, user.Id, now, write, ct));
            return Saved(outcome);
        });

    [McpServerTool(Name = "remove_script", Title = "Remove a script", Idempotent = false, OpenWorld = false, Destructive = true),
     Description("Marks a script as removed with a new version; its earlier versions stay and restore_script_version brings one back. Removing an indicator stops it being published by the next run; its table and any widgets on it stay until a run replaces them. " +
                 "baseVersion is the version number you last read as current.")]
    public Task<CallToolResult> RemoveScript(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description(KindHelp)] string kind,
        [Description("The version number you last read as current.")] int baseVersion,
        [Description("The indicator's name. Only for kind indicator.")] string? name = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var (scriptKind, scriptName) = ParseScript(kind, name);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var outcome = await GuardedAsync(() =>
                ScriptEndpoints.RemoveCoreAsync(db, projectId, scriptKind, scriptName, user.Id, time.GetUtcNow(), ScriptWrite.Mcp(baseVersion), ct));
            return Saved(outcome);
        });

    [McpServerTool(Name = "restore_script_version", Title = "Restore a version of a script", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Saves the code (and for an indicator the details) of an earlier version as a new version. baseVersion is the version number you last read as current. " +
                 "A version whose code holds the things save_script refuses is refused too.")]
    public Task<CallToolResult> RestoreScriptVersion(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description(KindHelp)] string kind,
        [Description("The number of the version to restore, from list_script_versions.")] int version,
        [Description("The version number you last read as current.")] int baseVersion,
        [Description("The indicator's name. Only for kind indicator.")] string? name = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var (scriptKind, scriptName) = ParseScript(kind, name);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var code = await ScriptEndpoints.Versions(db, projectId, scriptKind, scriptName).Where(v => v.Number == version).Select(v => v.Code).SingleOrDefaultAsync(ct);
            if (UnsafeCode.RefusalFor(code) is { } unsafeCode)
            {
                throw unsafeCode;
            }

            var write = ScriptWrite.Mcp(baseVersion);
            var now = time.GetUtcNow();
            var outcome = await GuardedAsync(() => scriptKind == ScriptKind.Indicator
                ? ScriptEndpoints.RestoreIndicatorCoreAsync(db, projectId, scriptName!, version, user.Id, now, write, ct)
                : ScriptEndpoints.RestoreCoreAsync(db, projectId, scriptKind, null, version, user.Id, now, write, ct));
            return Saved(outcome);
        });

    /// <summary>Runs a write and turns the guard's exception into the refusal that tells the client what changed.</summary>
    private static async Task<ScriptOutcome> GuardedAsync(Func<Task<ScriptOutcome>> write)
    {
        try
        {
            return await write();
        }
        catch (ScriptChangedException changed)
        {
            throw new McpRefusal(
                McpRefusal.ScriptChangedCode,
                $"The script was changed since you read it: its current version is {changed.Current}, saved by {changed.Author} at {changed.At:u}{(changed.Removed ? " (it marks the script as removed)" : "")}. "
                + "Read it again with get_script and decide how to merge; nothing was changed.",
                new Dictionary<string, object?>
                {
                    ["current"] = new { version = changed.Current, author = changed.Author, savedAt = changed.At, removed = changed.Removed },
                });
        }
    }

    /// <summary>The version that was written, without its code, or the refusal the REST endpoint would have given.</summary>
    private static object Saved(ScriptOutcome outcome)
    {
        if (outcome.Errors is { } errors)
        {
            throw McpRefusal.Validation(errors);
        }

        if (outcome.NotFound)
        {
            throw McpRefusal.NotFound("The version", "Call list_script_versions to see which exist.");
        }

        if (outcome.Conflict is { } conflict)
        {
            throw McpRefusal.Failed(conflict.Detail, StatusCodes.Status409Conflict, conflict.Code);
        }

        var v = outcome.Version!;
        return new { saved = true, v.Kind, v.Name, version = v.Number, v.Removed, v.Label, v.Description, v.Unit, v.Format, v.Columns, v.Author, savedAt = v.CreatedAt, v.Origin };
    }

    /// <summary>Whether the person is an analyst or an admin of at least one project that AI clients may use.</summary>
    private async Task RequireAnyProjectAsync(CancellationToken ct)
    {
        var stored = user.IsSuperAdmin
            ? await db.Projects.AsNoTracking().Select(p => p.McpAccess).ToListAsync(ct)
            : await db.ProjectMembers.AsNoTracking()
                .Where(m => m.UserId == user.Id && (m.Role == ProjectRole.Analyst || m.Role == ProjectRole.Admin))
                .Join(db.Projects, m => m.ProjectId, p => p.Id, (m, p) => p.McpAccess)
                .ToListAsync(ct);
        if (!stored.Any(level => McpAccessGate.Effective(level, Settings.MaxAccess) != McpAccess.Off))
        {
            throw new McpRefusal(McpRefusal.ForbiddenCode, "This needs the analyst role in a project that AI clients may use. An admin of a project can allow it in Settings > General.");
        }
    }

    private static ScriptKind ParseKind(string? kind) =>
        kind?.ToLowerInvariant() switch
        {
            "clean" => ScriptKind.Clean,
            "output" => ScriptKind.Output,
            "indicator" => ScriptKind.Indicator,
            _ => throw McpRefusal.Validation(new Dictionary<string, string[]> { ["kind"] = ["Choose clean, output or indicator."] }),
        };

    private static (ScriptKind Kind, string? Name) ParseScript(string? kind, string? name)
    {
        var scriptKind = ParseKind(kind);
        if (scriptKind == ScriptKind.Indicator)
        {
            if (!ColumnNames.IsValidTableName(name))
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name the indicator with lowercase letters, digits and underscores, starting with a letter, up to 63 characters (for example piped_water_rate)."],
                });
            }

            return (scriptKind, name);
        }

        if (name is not null)
        {
            throw McpRefusal.Validation(new Dictionary<string, string[]> { ["name"] = ["Only an indicator has a name."] });
        }

        return (scriptKind, null);
    }
}
