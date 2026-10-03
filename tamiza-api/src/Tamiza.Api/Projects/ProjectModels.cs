using Tamiza.Api.Data;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Projects;

public sealed record CreateProjectRequest(string? Name, string? KoboServerUrl, string? AssetUid, string? ApiToken);

/// <summary><c>null</c> leaves a value unchanged; an empty <see cref="EnumeratorField"/> clears it.</summary>
public sealed record UpdateProjectRequest(string? Name, string? EnumeratorField);

public sealed record ReplaceTokenRequest(string? ApiToken);

public sealed record ProjectSummary(Guid Id, string Name, string FormName, ProjectRole MyRole);

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
    DateTimeOffset CreatedAt);

public sealed record WebhookSettings(string Url, string Username, string? Secret, bool SecretUnreadable);

public sealed record CreatedProject(ProjectDetails Project, WebhookSettings Webhook);
