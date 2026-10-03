using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tamiza.Api.Ingestion;

/// <summary>A submission ready to be stored: the identifying fields Kobo adds, plus the JSON as received.</summary>
public sealed record SubmissionRow(long KoboId, string KoboUuid, string? FormVersion, DateTimeOffset? SubmittedAt, string Data);

/// <summary>Reads the fields Tamiza needs from a submission as the Kobo API v2 and REST Services send it.</summary>
public static class KoboSubmission
{
    public const int MaxUuidLength = 128;
    public const int MaxVersionLength = 128;

    /// <summary>Returns the row, or the reason the element is not a usable submission.</summary>
    public static (SubmissionRow? Row, string? Error) Parse(JsonElement submission)
    {
        if (submission.ValueKind != JsonValueKind.Object)
        {
            return (null, "The submission must be a JSON object.");
        }

        if (!submission.TryGetProperty("_id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var koboId))
        {
            return (null, "The submission has no numeric _id.");
        }

        if (!submission.TryGetProperty("_uuid", out var uuid) || uuid.ValueKind != JsonValueKind.String
            || uuid.GetString() is not { Length: > 0 and <= MaxUuidLength } koboUuid)
        {
            return (null, "The submission has no _uuid.");
        }

        var version = submission.TryGetProperty("__version__", out var versionElement) && versionElement.ValueKind == JsonValueKind.String
            ? versionElement.GetString() is { Length: > 0 and <= MaxVersionLength } text ? text : null
            : null;

        return (new SubmissionRow(koboId, koboUuid, version, SubmissionTime(submission), StorableJson(submission)), null);
    }

    // Kobo writes _submission_time in UTC without an offset, for example "2026-05-01T10:15:00".
    private static DateTimeOffset? SubmissionTime(JsonElement submission) =>
        submission.TryGetProperty("_submission_time", out var time) && time.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    /// <summary>
    /// The raw JSON, except that NUL characters are dropped from strings: PostgreSQL's <c>jsonb</c> rejects them, and one
    /// such record would otherwise fail every synchronization of its project.
    /// </summary>
    private static string StorableJson(JsonElement submission)
    {
        var raw = submission.GetRawText();
        if (!raw.Contains("\\u0000", StringComparison.Ordinal))
        {
            return raw;
        }

        using var buffer = new MemoryStream();
        // Relaxed escaping keeps accented text readable; jsonb stores the same value either way.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteWithoutNul(submission, writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteWithoutNul(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name.Replace("\0", "", StringComparison.Ordinal));
                    WriteWithoutNul(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteWithoutNul(item, writer);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString()!.Replace("\0", "", StringComparison.Ordinal));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
