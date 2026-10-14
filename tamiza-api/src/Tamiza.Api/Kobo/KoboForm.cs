using System.Globalization;
using System.Text.Json;
using Tamiza.Api.Data;

namespace Tamiza.Api.Kobo;

/// <summary>
/// A Kobo asset document. <see cref="VersionId"/> is the latest version, which can be an undeployed draft, and
/// <see cref="DeployedVersionId"/> the version of the current deployment (null without one). The fields and choices
/// describe the asset's latest content.
/// </summary>
public sealed record KoboAsset(
    string Name, IReadOnlyList<FormField> Fields, IReadOnlyList<FormChoice> Choices, string? VersionId = null, string? DeployedVersionId = null);

/// <summary>The definition of one version of an asset. <see cref="DeployedAt"/> is null for a draft or an unknown date.</summary>
public sealed record KoboFormVersion(string? Uid, IReadOnlyList<FormField> Fields, IReadOnlyList<FormChoice> Choices, DateTimeOffset? DeployedAt);

/// <summary>
/// Reads the form name, its fields and its choice lists from a Kobo API v2 asset or asset version document.
/// Questions and choices are described as Kobo deploys them, since those are the names that appear in submissions.
/// </summary>
public static class KoboForm
{
    // begin_<x> / end_<x> rows that open and close a level of nesting in submissions. Rank, score and matrix questions
    // are expanded by Kobo into generated rows that live under their own group.
    private static readonly HashSet<string> NestingTypes = ["group", "repeat", "score", "rank", "kobomatrix"];

    public static KoboAsset Parse(JsonElement asset)
    {
        var (fields, choices) = ParseContent(asset);
        return new KoboAsset(Text(asset, "name") ?? "", fields, choices, Text(asset, "version_id"), Text(asset, "deployed_version_id"));
    }

    /// <summary>Reads the document returned for <c>/assets/{uid}/versions/{version}/</c>.</summary>
    public static KoboFormVersion ParseVersion(JsonElement version)
    {
        var (fields, choices) = ParseContent(version);
        return new KoboFormVersion(Text(version, "uid"), fields, choices, DeployedAt(version));
    }

    private static (List<FormField> Fields, List<FormChoice> Choices) ParseContent(JsonElement document)
    {
        var fields = new List<FormField>();
        var choices = new List<FormChoice>();
        if (!document.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
        {
            return (fields, choices);
        }

        // Paths come from the nesting, not from $xpath: it prefers `name` over the deployed name, ignores rank and
        // matrix groups and is missing in old versions.
        var enclosing = new List<string>();
        foreach (var row in Rows(content, "survey"))
        {
            var rawType = TypeText(row);
            if (rawType is null)
            {
                continue;
            }

            var type = rawType.Replace(' ', '_');
            var name = Text(row, "$autoname") ?? Text(row, "name");
            if (type.StartsWith("begin_", StringComparison.Ordinal) && NestingTypes.Contains(type[6..]))
            {
                enclosing.Add(name ?? "");
                continue;
            }

            if (type.StartsWith("end_", StringComparison.Ordinal) && NestingTypes.Contains(type[4..]))
            {
                if (enclosing.Count > 0)
                {
                    enclosing.RemoveAt(enclosing.Count - 1);
                }

                continue;
            }

            if (name is null)
            {
                continue;
            }

            var path = string.Join('/', enclosing.Append(name).Where(segment => segment.Length > 0));
            fields.Add(new FormField(name, path, type, Label(row), ListName(row, rawType), OrOther(row)));
        }

        // A list may repeat a choice name (allow_choice_duplicates); as in Kobo's exports, the last one wins.
        var position = new Dictionary<(string List, string Name), int>();
        foreach (var row in Rows(content, "choices"))
        {
            var listName = Text(row, "list_name");
            var choiceName = Text(row, "$autovalue") ?? Text(row, "name");
            if (listName is null || choiceName is null)
            {
                continue;
            }

            var choice = new FormChoice(listName, choiceName, Label(row));
            if (position.TryGetValue((listName, choiceName), out var index))
            {
                choices[index] = choice;
            }
            else
            {
                position[(listName, choiceName)] = choices.Count;
                choices.Add(choice);
            }
        }

        return (fields, choices);
    }

    private static IEnumerable<JsonElement> Rows(JsonElement content, string property) =>
        content.TryGetProperty(property, out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object)
            : [];

    // Kobo writes the type as "select_one x", or as {"select_one": "x"} in older documents.
    private static string? TypeText(JsonElement row)
    {
        if (!row.TryGetProperty("type", out var type))
        {
            return null;
        }

        switch (type.ValueKind)
        {
            case JsonValueKind.String:
                return type.GetString() is { Length: > 0 } text ? text : null;
            case JsonValueKind.Object:
                foreach (var property in type.EnumerateObject())
                {
                    return property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } listName
                        ? $"{property.Name} {listName}"
                        : property.Name;
                }

                return null;
            default:
                return null;
        }
    }

    // Kobo writes either {"type": "select_one", "select_from_list_name": "x"} or the XLSForm form "select_one x".
    private static string? ListName(JsonElement row, string rawType)
    {
        if (Text(row, "select_from_list_name") is { } listName)
        {
            return listName;
        }

        var parts = rawType.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] is ("select_one" or "select_multiple") ? parts[1] : null;
    }

    private static bool OrOther(JsonElement row) =>
        row.TryGetProperty("_or_other", out var orOther) && orOther.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    // Kobo stores labels as one entry per translation, and the first is the default language. A missing or empty
    // default-language label is no label: another language's text would be shown as if it were the default one.
    private static string? Label(JsonElement row)
    {
        if (!row.TryGetProperty("label", out var label))
        {
            return null;
        }

        var first = label.ValueKind == JsonValueKind.Array ? label.EnumerateArray().FirstOrDefault() : label;
        return first.ValueKind == JsonValueKind.String && first.GetString() is { Length: > 0 } text ? text : null;
    }

    // `date_deployed` is a datetime, or false for a draft.
    private static DateTimeOffset? DeployedAt(JsonElement version) =>
        Text(version, "date_deployed") is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var deployedAt)
            ? deployedAt
            : null;
}
