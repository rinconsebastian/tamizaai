using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.Mcp;

/// <summary>
/// The versions of a project's own dashboards (dashboard-versions): their history, one version's name, filters, palette, colors kept
/// by value and widgets, and the restore of a kept one as a new version (design D10 of dashboard versions). They run the same code as
/// the dashboard page's version history. Versions hold no rows, so the summaries level is enough; reading and restoring them is an
/// analyst's action, as in the page.
/// </summary>
[McpServerToolType]
public sealed class McpDashboardVersionTools(
    McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options, TamizaDbContext db, ICurrentUser user, DashboardVersions versions)
    : McpToolBase(gate, call, options)
{
    private const string ProjectHelp = "The project's id, from list_projects.";

    private const string DashboardHelp = "The dashboard's id, from list_dashboards.";

    [McpServerTool(Name = "list_dashboard_versions", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "List the versions of a dashboard"),
     Description("A dashboard's kept versions, newest first: the 10 newest, the current one included. Each has its number, whether it is current, the dashboard's name, how many widgets and filters it holds, " +
                 "what changed (changes, the first 20, and moreChanges), restoredFrom for a restore, who made it, when its first and last changes were saved, and whether it was made in Tamiza (app) or through MCP (mcp) with the request given as change (note). " +
                 "current is the number restore_dashboard_version needs as baseVersion. Use get_dashboard_version for what a version holds.")]
    public Task<CallToolResult> ListDashboardVersions(
        [Description(ProjectHelp)] Guid projectId, [Description(DashboardHelp)] Guid dashboardId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var list = McpResults.Unwrap<DashboardVersionList>(await DashboardVersionEndpoints.ListAsync(projectId, dashboardId, db, ct));
            return new { dashboardId, current = list.Current, versions = list.Versions };
        }, hint: "Older versions are left out.");

    [McpServerTool(Name = "get_dashboard_version", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get a version of a dashboard"),
     Description("One kept version of a dashboard: its name, its filters, its palette and colors kept by value (palette and valueColors; tamiza and none for a version recorded before palettes) and its widgets in reading order, each with its id, title, chart type, query and display (config) and layout as get_dashboard gives them, " +
                 "with what changed, who made it and when. Nothing changes. Compare it with get_dashboard before restoring it; to bring back one widget only, add it again with create_widget.")]
    public Task<CallToolResult> GetDashboardVersion(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The version's number, from list_dashboard_versions.")] int version,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<DashboardVersionDetails>(await DashboardVersionEndpoints.GetAsync(projectId, dashboardId, version, db, ct));
        }, hint: "The widgets last in reading order are left out; get_dashboard gives the current ones.");

    [McpServerTool(Name = "restore_dashboard_version", Title = "Restore a version of a dashboard", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Restores a kept version of a dashboard as a new version: the dashboard's name, filters, palette, colors kept by value and widgets become the version's, and the state it replaces stays among the versions, so a restore can be undone too. " +
                 "The dashboard keeps its id, its place and its scheduled email. Pass baseVersion = the current version you last read with list_dashboard_versions or get_dashboard: " +
                 "if a version was recorded meanwhile the call is refused with mcp.dashboardChanged and nothing is restored. " +
                 "When another dashboard now has the version's name, the current name is kept (nameKept); a widget of the version that another dashboard now holds comes back as a copy with a new id (copies). " +
                 "restored is false when the dashboard already is as in that version, and nothing changes. A version with more widgets than a dashboard may now hold is refused.")]
    public Task<CallToolResult> RestoreDashboardVersion(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The number of the version to restore, from list_dashboard_versions.")] int version,
        [Description("The number of the dashboard's current version as you last read it, from list_dashboard_versions or get_dashboard.")] int baseVersion,
        [Description(McpDashboardTools.ChangeHelp)] string change,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var request = McpDashboardTools.RequireChange(change);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            // The restore records its version itself and never grows one (design D7), so nothing is noted for a flush.
            var restore = await GuardedAsync(async () => McpResults.Unwrap<DashboardRestore>(
                await versions.RestoreAsync(projectId, dashboardId, version, DashboardRestoreWrite.Mcp(request, baseVersion), user.Id, ct)));
            return new
            {
                restore.Restored,
                dashboardId,
                version = restore.Version.Number,
                restoredFrom = restore.Restored ? version : (int?)null,
                name = restore.Dashboard.Name,
                restore.NameKept,
                restore.Copies,
                dashboardVersions = new[] { new DashboardVersionNumber(dashboardId, restore.Version.Number) },
            };
        });

    /// <summary>
    /// Runs a guarded restore and turns the guard's exception into the refusal that tells the client which version is current, as
    /// <c>mcp.scriptChanged</c> does for scripts (design D10). A version the restore recorded first, for a state no version held, is kept.
    /// </summary>
    private static async Task<DashboardRestore> GuardedAsync(Func<Task<DashboardRestore>> restore)
    {
        try
        {
            return await restore();
        }
        catch (DashboardChangedException changed)
        {
            var where = changed.Origin == DashboardVersionOrigin.Mcp ? "through MCP" : "in Tamiza";
            throw new McpRefusal(
                McpRefusal.DashboardChangedCode,
                $"The dashboard was changed since you read it: its current version is {changed.Current}, saved by {changed.Author} at {changed.SavedAt:u} {where}. "
                + "Read it again with list_dashboard_versions and get_dashboard_version and decide what to restore; nothing was restored.",
                new Dictionary<string, object?>
                {
                    ["current"] = new { version = changed.Current, author = changed.Author, savedAt = changed.SavedAt, origin = changed.Origin },
                });
        }
    }
}
