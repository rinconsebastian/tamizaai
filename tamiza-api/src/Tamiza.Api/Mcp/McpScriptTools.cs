using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
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
     Description("The code of a script and, for an indicator, its label, description, unit and format. The current version unless you name one.")]
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
            var outcome = await GuardedAsync(() => scriptKind == ScriptKind.Indicator
                ? ScriptEndpoints.SaveIndicatorCoreAsync(db, projectId, scriptName!, new SaveIndicatorRequest(code, label, description, unit, format), user.Id, now, write, ct)
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
        return new { saved = true, v.Kind, v.Name, version = v.Number, v.Removed, v.Label, v.Description, v.Unit, v.Format, v.Author, savedAt = v.CreatedAt, v.Origin };
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
