using Microsoft.AspNetCore.Http.HttpResults;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Indicators;

/// <summary>A dashboard's kept versions, newest first (design D9 of dashboard versions).</summary>
/// <param name="Current">The number of the current version, the newest; 0 while the dashboard has none (design D5).</param>
public sealed record DashboardVersionList(int Current, IReadOnlyList<DashboardVersionSummary> Versions);

/// <summary>What the version history shows of a version (design D9): what it holds in short, what changed, who made it and when.</summary>
/// <param name="Current">Whether it is the newest version, which holds the dashboard as it is.</param>
/// <param name="Name">The dashboard's name in the version.</param>
/// <param name="Changes">What the version holds, at most 20 entries, in the order they were made (design D2).</param>
/// <param name="MoreChanges">How many more changes it holds than are listed.</param>
/// <param name="RestoredFrom">The number of the version a restore brought back; null for any other change.</param>
/// <param name="Origin"><c>app</c> for the dashboard page and the REST API, <c>mcp</c> for an AI client.</param>
/// <param name="Note">The request the AI client gave as <c>change</c>; null for <c>app</c>.</param>
/// <param name="CreatedAt">The first write the version holds.</param>
/// <param name="SavedAt">The last write it holds: the time the history shows.</param>
public sealed record DashboardVersionSummary(
    int Number,
    bool Current,
    string Name,
    int WidgetCount,
    int FilterCount,
    IReadOnlyList<DashboardChange> Changes,
    int MoreChanges,
    int? RestoredFrom,
    DashboardVersionOrigin Origin,
    string? Note,
    Person Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset SavedAt);

/// <summary>A kept version's name, filters, colors and widgets, with what the history shows of it (design D9).</summary>
/// <param name="Filters">As <c>GET .../dashboards/{id}</c> returns a dashboard's filters.</param>
/// <param name="Palette">The dashboard's palette in the version; <c>tamiza</c> for a version recorded before palettes existed.</param>
/// <param name="ValueColors">The colors the dashboard kept by value in the version, in the order they were saved; empty for a version recorded before palettes existed.</param>
/// <param name="Widgets">In reading order: top to bottom, then left to right, then the older first.</param>
public sealed record DashboardVersionDetails(
    int Number,
    bool Current,
    string Name,
    IReadOnlyList<DashboardFilter> Filters,
    string Palette,
    IReadOnlyList<ValueColor> ValueColors,
    IReadOnlyList<VersionWidget> Widgets,
    IReadOnlyList<DashboardChange> Changes,
    int MoreChanges,
    int? RestoredFrom,
    DashboardVersionOrigin Origin,
    string? Note,
    Person Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset SavedAt);

/// <summary>
/// A widget as a version holds it. Its config is read as the dashboard's widgets are, so the widget view and an AI client use it as they
/// use a widget of the dashboard.
/// </summary>
public sealed record VersionWidget(Guid Id, string Title, string ChartType, WidgetConfig Config, WidgetLayout Layout);

/// <summary>What a restore did (design D7, D9).</summary>
/// <param name="Restored">False when the dashboard already was as the version would leave it, so nothing was written.</param>
/// <param name="Version">The version the restore recorded, or the current one when it restored nothing.</param>
/// <param name="NameKept">Whether the dashboard kept its current name because another dashboard of the project now has the version's.</param>
/// <param name="Copies">The widgets of the version that another dashboard now holds, restored as copies.</param>
/// <param name="Dashboard">The dashboard after the restore, as <c>GET .../dashboards/{id}</c> returns it.</param>
public sealed record DashboardRestore(
    bool Restored, DashboardVersionSummary Version, bool NameKept, IReadOnlyList<RestoredCopy> Copies, DashboardView Dashboard);

/// <summary>A widget restored as a copy with a new id, because another dashboard of the project still holds the original.</summary>
/// <param name="OriginalId">The id the version holds, still the widget's on the other dashboard.</param>
/// <param name="CopyId">The copy's id on the dashboard restored.</param>
/// <param name="DashboardId">The dashboard that holds the original.</param>
/// <param name="DashboardName">That dashboard's name.</param>
public sealed record RestoredCopy(Guid OriginalId, Guid CopyId, string Title, Guid DashboardId, string DashboardName);

/// <summary>
/// The versions of a project's own dashboards (dashboard-versions): the history, one version's name, filters, colors and widgets, and the restore
/// of a kept one (design D9). The routes are mapped with the dashboards' (<see cref="DashboardEndpoints.MapDashboardEndpoints"/>), so one
/// whose dashboard the project does not have answers 404 <see cref="DashboardEndpoints.NotFoundCode"/>, and one whose version the
/// dashboard does not keep 404 <see cref="DashboardEndpoints.VersionNotFoundCode"/>.
/// </summary>
public static class DashboardVersionEndpoints
{
    /// <summary>The dashboard's kept versions, newest first, the current one marked.</summary>
    internal static async Task<Results<Ok<DashboardVersionList>, ProblemHttpResult>> ListAsync(
        Guid projectId, Guid dashboardId, TamizaDbContext db, CancellationToken ct) =>
        await DashboardEndpoints.DashboardExistsAsync(db, projectId, dashboardId, ct)
            ? TypedResults.Ok(await DashboardVersions.ListAsync(db, projectId, dashboardId, ct))
            : DashboardEndpoints.DashboardNotFound();

    /// <summary>A kept version's name, filters, colors and widgets, which nothing changes.</summary>
    internal static async Task<Results<Ok<DashboardVersionDetails>, ProblemHttpResult>> GetAsync(
        Guid projectId, Guid dashboardId, int number, TamizaDbContext db, CancellationToken ct)
    {
        if (!await DashboardEndpoints.DashboardExistsAsync(db, projectId, dashboardId, ct))
        {
            return DashboardEndpoints.DashboardNotFound();
        }

        return await DashboardVersions.DetailsAsync(db, projectId, dashboardId, number, ct) is { } details
            ? TypedResults.Ok(details)
            : DashboardEndpoints.VersionNotFound();
    }

    /// <summary>
    /// Restores a kept version as a new version (design D7): 201 with it, or 200 with the current one when there was nothing to do. A
    /// restore from the page is not guarded: the last change wins, as everywhere on the page, and the state it replaces is a version.
    /// It records its version itself, so the route group's flush has nothing to record after it.
    /// </summary>
    internal static Task<Results<Created<DashboardRestore>, Ok<DashboardRestore>, ValidationProblem, ProblemHttpResult>> RestoreAsync(
        Guid projectId, Guid dashboardId, int number, DashboardVersions versions, ICurrentUser user, CancellationToken ct) =>
        versions.RestoreAsync(projectId, dashboardId, number, DashboardRestoreWrite.App, user.Id, ct);
}
