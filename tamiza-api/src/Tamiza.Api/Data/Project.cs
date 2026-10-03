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

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A field of the Kobo form, as read from the asset's <c>content.survey</c>.</summary>
public sealed record FormField(string Name, string Xpath, string Type, string? Label);
