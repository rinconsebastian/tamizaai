using Tamiza.Api.Data;
using Tamiza.Api.Indicators;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Projects;

public sealed record CreateProjectRequest(string? Name, string? KoboServerUrl, string? AssetUid, string? ApiToken);

/// <summary>
/// <c>null</c> leaves a value unchanged; an empty <see cref="EnumeratorField"/> clears it. <see cref="TimeZone"/> is a time
/// zone name PostgreSQL knows, such as <c>America/Bogota</c>.
/// </summary>
/// <param name="McpAccess"><c>off</c>, <c>aggregates</c> or <c>rows</c>: how much of the project's data AI clients may read. A string, so a wrong value is a field error and not a failed read of the body.</param>
public sealed record UpdateProjectRequest(string? Name, string? EnumeratorField, string? TimeZone = null, string? McpAccess = null);

public sealed record ReplaceTokenRequest(string? ApiToken);

public sealed record ProjectSummary(Guid Id, string Name, string FormName, ProjectRole MyRole);

/// <param name="KoboServerUrl">
/// This value, <paramref name="AssetUid"/>, <paramref name="FormCheckedAt"/>, <paramref name="EnumeratorField"/>, <paramref name="FieldCheck"/>,
/// <paramref name="TokenUnreadable"/> and <paramref name="UnavailableFormVersions"/> are the ones only Settings &gt; General shows. A <c>viewer</c>
/// gets them as <c>null</c>.
/// </param>
/// <param name="Dashboards">The project's own dashboards in their order, at least one: what the tab row shows after Performance and Clean data.</param>
/// <param name="McpAccess">How much of the project's data AI clients may read (<c>off</c>, <c>aggregates</c> or <c>rows</c>); <c>null</c> for a <c>viewer</c>.</param>
/// <param name="McpMaxAccess">The most the deployment allows any project (<c>aggregates</c> or <c>rows</c>); <c>null</c> for a <c>viewer</c>.</param>
public sealed record ProjectDetails(
    Guid Id,
    string Name,
    string? KoboServerUrl,
    string? AssetUid,
    string FormName,
    DateTimeOffset? FormCheckedAt,
    string? EnumeratorField,
    IReadOnlyList<MetricAvailability>? FieldCheck,
    ProjectRole MyRole,
    bool? TokenUnreadable,
    DateTimeOffset CreatedAt,
    int? UnavailableFormVersions,
    string TimeZone,
    IReadOnlyList<DashboardEntry> Dashboards,
    McpAccess? McpAccess = null,
    McpAccess? McpMaxAccess = null);

public sealed record WebhookSettings(string Url, string Username, string? Secret, bool SecretUnreadable);

public sealed record CreatedProject(ProjectDetails Project, WebhookSettings Webhook);
