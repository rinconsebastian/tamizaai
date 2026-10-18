using Tamiza.Api.Data;
using Tamiza.Api.Indicators;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Projects;

public sealed record CreateProjectRequest(string? Name, string? KoboServerUrl, string? AssetUid, string? ApiToken);

/// <summary>
/// <c>null</c> leaves a value unchanged; an empty <see cref="EnumeratorField"/> clears it. <see cref="TimeZone"/> is a time
/// zone name PostgreSQL knows, such as <c>America/Bogota</c>.
/// </summary>
public sealed record UpdateProjectRequest(string? Name, string? EnumeratorField, string? TimeZone = null);

public sealed record ReplaceTokenRequest(string? ApiToken);

public sealed record ProjectSummary(Guid Id, string Name, string FormName, ProjectRole MyRole);

/// <param name="Dashboards">The project's own dashboards in their order, at least one: what the tab row shows after Performance and Clean data.</param>
public sealed record ProjectDetails(
    Guid Id,
    string Name,
    string KoboServerUrl,
    string AssetUid,
    string FormName,
    DateTimeOffset FormCheckedAt,
    string? EnumeratorField,
    IReadOnlyList<MetricAvailability> FieldCheck,
    ProjectRole MyRole,
    bool TokenUnreadable,
    DateTimeOffset CreatedAt,
    int UnavailableFormVersions,
    string TimeZone,
    IReadOnlyList<DashboardEntry> Dashboards);

public sealed record WebhookSettings(string Url, string Username, string? Secret, bool SecretUnreadable);

public sealed record CreatedProject(ProjectDetails Project, WebhookSettings Webhook);
