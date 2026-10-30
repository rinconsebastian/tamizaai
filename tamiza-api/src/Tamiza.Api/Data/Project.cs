namespace Tamiza.Api.Data;

/// <summary>A project: one Kobo form, the people working on it and its analysis settings.</summary>
public sealed class Project
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Normalized base URL of the Kobo server, without a trailing slash.</summary>
    public required string KoboServerUrl { get; set; }

    public required string KoboAssetUid { get; set; }

    /// <summary>Kobo API token, encrypted with Data Protection. Never returned by the API.</summary>
    public required string EncryptedApiToken { get; set; }

    /// <summary>Webhook secret, encrypted with Data Protection.</summary>
    public required string EncryptedWebhookSecret { get; set; }

    public required string FormName { get; set; }

    /// <summary>Snapshot of the form's fields taken at the last connection check.</summary>
    public List<FormField> FormFields { get; set; } = [];

    public DateTimeOffset FormCheckedAt { get; set; }

    /// <summary>XPath of the question that identifies the enumerator, when the form has no <c>username</c>.</summary>
    public string? EnumeratorField { get; set; }

    /// <summary>
    /// The IANA time zone that defines the project's days in the performance dashboard (a name PostgreSQL knows).
    /// <c>UTC</c> until an admin sets another.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Hours between scheduled synchronizations (1–168).</summary>
    public int SyncIntervalHours { get; set; } = 12;

    /// <summary>Hours between deletion reconciliations (1–168).</summary>
    public int ReconcileIntervalHours { get; set; } = 24;

    /// <summary>When the scheduler next queues a synchronization.</summary>
    public DateTimeOffset NextSyncAt { get; set; }

    /// <summary>When the scheduler next queues a reconciliation.</summary>
    public DateTimeOffset NextReconcileAt { get; set; }

    /// <summary>When the last webhook delivery was accepted.</summary>
    public DateTimeOffset? LastWebhookAt { get; set; }

    /// <summary>Queue a pipeline run after each synchronization that changed submissions.</summary>
    public bool RunAfterSync { get; set; } = true;

    /// <summary>Hours between scheduled pipeline runs (1–168), or null for no schedule.</summary>
    public int? RunIntervalHours { get; set; }

    /// <summary>When the next scheduled run is due; null without a schedule.</summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>The form's choice lists, from the same snapshot as <see cref="FormFields"/>.</summary>
    public List<FormChoice> FormChoices { get; set; } = [];

    /// <summary>The uid of the deployed form version the snapshot describes; null when the form has no deployment.</summary>
    public string? FormVersion { get; set; }

    /// <summary>How much of the project's data AI clients may read through the MCP server. <c>off</c> until an admin allows it.</summary>
    public McpAccess McpAccess { get; set; } = McpAccess.Off;

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A field of the Kobo form, as read from the asset's <c>content.survey</c>. <see cref="Xpath"/> is the full path of the
/// answer in a submission, built from the group nesting. <see cref="ListName"/> names the choice list of select
/// questions, <see cref="OrOther"/> marks the ones with Kobo's "or other" option, and <see cref="InRepeat"/> the ones inside a
/// repeat group, which a submission answers once per repeat.
/// </summary>
public sealed record FormField(string Name, string Xpath, string Type, string? Label, string? ListName = null, bool OrOther = false, bool InRepeat = false);

/// <summary>A choice of one of the form's choice lists, as read from the asset's <c>content.choices</c>.</summary>
public sealed record FormChoice(string ListName, string Name, string? Label);
