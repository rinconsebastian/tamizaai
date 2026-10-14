namespace Tamiza.Api.Data;

/// <summary>
/// A version of the project's Kobo form that some of its submissions were made with (<c>__version__</c>): the questions
/// and choice lists it had, kept so that answers are translated with the labels of their own version.
/// </summary>
public sealed class FormVersion
{
    public Guid ProjectId { get; set; }

    /// <summary>Kobo's uid of the asset version.</summary>
    public required string Uid { get; set; }

    public FormVersionStatus Status { get; set; }

    /// <summary>The version's fields, in the shape of <see cref="Project.FormFields"/>. Empty until fetched.</summary>
    public List<FormField> Fields { get; set; } = [];

    /// <summary>The version's choices, in the shape of <see cref="Project.FormChoices"/>. Empty until fetched.</summary>
    public List<FormChoice> Choices { get; set; } = [];

    /// <summary>When Kobo deployed the version, when known.</summary>
    public DateTimeOffset? DeployedAt { get; set; }

    /// <summary>When a submission with this version was first stored.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public DateTimeOffset? FetchedAt { get; set; }

    /// <summary>Failed fetches in a row; it sets the delay before the next attempt.</summary>
    public int Attempts { get; set; }

    /// <summary>When the background pass tries again; null once available or not found.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Machine-readable error code (<c>kobo.*</c>) of the last failed attempt.</summary>
    public string? ErrorCode { get; set; }

    public string? Error { get; set; }
}

public enum FormVersionStatus
{
    /// <summary>Recorded and not fetched yet.</summary>
    Pending,

    /// <summary>The definition is kept; it never changes.</summary>
    Available,

    /// <summary>The last attempt failed; the pass retries later.</summary>
    Failed,

    /// <summary>Kobo does not know the version while the form itself is readable.</summary>
    NotFound,
}

/// <summary>The text stored in <c>form_versions.status</c> and shown by the API for each <see cref="FormVersionStatus"/>.</summary>
public static class FormVersionStatusText
{
    public const string Pending = "pending";
    public const string Available = "available";
    public const string Failed = "failed";
    public const string NotFound = "not_found";

    public static string ToText(FormVersionStatus status) => status switch
    {
        FormVersionStatus.Pending => Pending,
        FormVersionStatus.Available => Available,
        FormVersionStatus.Failed => Failed,
        FormVersionStatus.NotFound => NotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static FormVersionStatus Parse(string text) => text switch
    {
        Pending => FormVersionStatus.Pending,
        Available => FormVersionStatus.Available,
        Failed => FormVersionStatus.Failed,
        NotFound => FormVersionStatus.NotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Unknown form version status."),
    };
}
