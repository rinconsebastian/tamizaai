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

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A field of the Kobo form, as read from the asset's <c>content.survey</c>.</summary>
public sealed record FormField(string Name, string Xpath, string Type, string? Label);
