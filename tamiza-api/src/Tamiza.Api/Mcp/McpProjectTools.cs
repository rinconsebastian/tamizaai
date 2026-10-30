using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Indicators;
using Tamiza.Api.Ingestion;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>Orientation: the projects a person can work on and the state of one of them.</summary>
[McpServerToolType]
public sealed class McpProjectTools(
    McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options, TamizaDbContext db, ICurrentUser user, DataDashboard data,
    IndicatorDashboard indicators, IOptions<IndicatorDashboardOptions> indicatorOptions) : McpToolBase(gate, call, options)
{
    [McpServerTool(Name = "list_projects", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "List my projects"),
     Description("Lists the Tamiza projects the signed-in person belongs to, with their role in each and the data access AI clients have there. " +
                 "Call this first. A project with available=false is off for AI clients (an admin of the project can allow it in Settings > General) and no other tool will answer for it.")]
    public Task<CallToolResult> ListProjects(CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var projects = McpResults.Unwrap<List<ProjectSummary>>(await ProjectEndpoints.ListAsync(db, user, ct));
            var ids = projects.Select(p => p.Id).ToList();
            var stored = await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.McpAccess }).ToDictionaryAsync(p => p.Id, p => p.McpAccess, ct);
            return new
            {
                deploymentMaximum = Settings.MaxAccess,
                projects = projects.Select(p =>
                {
                    var level = McpAccessGate.Effective(stored.GetValueOrDefault(p.Id), Settings.MaxAccess);
                    return new { id = p.Id, name = p.Name, formName = p.FormName, role = p.MyRole, access = level, available = level != McpAccess.Off };
                }).ToList(),
            };
        });

    [McpServerTool(Name = "get_project", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get a project"),
     Description("The state of one project: its form, how many submissions are stored and when the last arrived, the tables the last runs published with their row counts, " +
                 "the indicators and whether each has published, the dashboards, and, for analysts and admins, the last synchronization, the scripts with their current version numbers and the last run. " +
                 "Use it to decide what to read or build next.")]
    public Task<CallToolResult> GetProject([Description("The project's id, from list_projects.")] Guid projectId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var project = await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            var details = await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
                .Select(p => new { p.Name, p.FormName, p.TimeZone, p.EnumeratorField, p.McpAccess, p.FormVersion }).SingleAsync(ct);
            var submissions = db.RawSubmissions.AsNoTracking().Where(s => s.ProjectId == projectId);
            var stored = await submissions.LongCountAsync(s => !s.Deleted, ct);
            var lastReceived = await submissions.Where(s => !s.Deleted).MaxAsync(s => (DateTimeOffset?)s.ReceivedAt, ct);
            var tables = await data.GetOverviewAsync(projectId, ct);
            var overview = McpResults.Unwrap<IndicatorOverview>(await IndicatorEndpoints.GetOverviewAsync(projectId, indicators, db, indicatorOptions, ct));
            var dashboards = await db.Dashboards.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Position)
                .Select(d => new { id = d.Id, name = d.Name, widgets = db.DashboardWidgets.Count(w => w.DashboardId == d.Id) }).ToListAsync(ct);
            var analyst = project.Role >= ProjectRole.Analyst;

            return new
            {
                id = projectId,
                name = details.Name,
                formName = details.FormName,
                timeZone = details.TimeZone,
                myRole = project.Role,
                access = new { project = details.McpAccess, deploymentMaximum = Settings.MaxAccess, effective = project.Level },
                submissions = new { stored, lastReceivedAt = lastReceived },
                publishedTables = tables.Sources.Select(s => new { source = s.Source, publishedAt = s.PublishedAt, tables = s.Tables }).ToList(),
                indicators = overview.Indicators.Select(i => new { i.Name, i.Label, i.Unit, i.Format, i.Published, i.Rows, i.PublishedAt }).ToList(),
                dashboards,
                enumeratorField = analyst ? details.EnumeratorField : null,
                formVersion = analyst ? details.FormVersion : null,
                ingestion = analyst ? await IngestionAsync(projectId, ct) : null,
                scripts = analyst ? await ScriptsAsync(projectId, ct) : null,
                lastRun = analyst ? McpResults.Unwrap<RunPage>(await RunEndpoints.ListAsync(projectId, db, 1, 1, ct)).Items.FirstOrDefault() : null,
            };
        }, hint: "Use list_scripts or get_tables for the detail.");

    private async Task<object> IngestionAsync(Guid projectId, CancellationToken ct)
    {
        var status = await IngestionEndpoints.StatusAsync(db, projectId, ct);
        return new { status.DeletedCount, status.LastWebhookAt, status.LastSync, status.ActiveSync, status.SyncIntervalHours };
    }

    /// <summary>The current version of each script, without its code.</summary>
    private async Task<object> ScriptsAsync(Guid projectId, CancellationToken ct)
    {
        var current = await McpScripts.CurrentAsync(db, projectId, ct);
        return new
        {
            clean = current.FirstOrDefault(s => s.Kind == ScriptKind.Clean && !s.Removed),
            output = current.FirstOrDefault(s => s.Kind == ScriptKind.Output && !s.Removed),
            indicators = current.Where(s => s.Kind == ScriptKind.Indicator && !s.Removed).OrderBy(s => s.Name, StringComparer.Ordinal).ToList(),
        };
    }
}
