using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Data;

namespace Tamiza.Api.Mcp;

/// <summary>A script's current version without its code: who saved it, when and where.</summary>
public sealed record McpScriptSummary(
    ScriptKind Kind, string? Name, int Version, bool Removed, string? Label, string? Unit, string? Format, string Author, DateTimeOffset SavedAt, ScriptOrigin Origin);

public static class McpScripts
{
    /// <summary>The latest version of every script the project has ever had (the two fixed ones and each indicator), including those marked removed.</summary>
    public static async Task<List<McpScriptSummary>> CurrentAsync(TamizaDbContext db, Guid projectId, CancellationToken ct) =>
        await db.ScriptVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId
                        && v.Number == db.ScriptVersions.Where(o => o.ProjectId == projectId && o.Kind == v.Kind && o.Name == v.Name).Max(o => o.Number))
            .Select(v => new McpScriptSummary(v.Kind, v.Name, v.Number, v.Code == null, v.Label, v.Unit, v.Format, v.Author!.Name, v.CreatedAt, v.Origin))
            .ToListAsync(ct);
}
