using System.Text.Json;
using Tamiza.Api.Data;

namespace Tamiza.Api.Kobo;

public sealed record KoboAsset(string Name, IReadOnlyList<FormField> Fields);

/// <summary>Reads the form name and its fields from a Kobo API v2 asset document.</summary>
public static class KoboForm
{
    private static readonly HashSet<string> StructuralTypes =
        ["begin_group", "end_group", "begin_repeat", "end_repeat"];

    public static KoboAsset Parse(JsonElement asset)
    {
        var name = asset.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()!
            : "";

        var fields = new List<FormField>();
        if (asset.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("survey", out var survey)
            && survey.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in survey.EnumerateArray())
            {
                var type = Text(row, "type")?.Replace(' ', '_');
                var fieldName = Text(row, "name") ?? Text(row, "$autoname");
                if (type is null || fieldName is null || StructuralTypes.Contains(type))
                {
                    continue;
                }

                fields.Add(new FormField(fieldName, Text(row, "$xpath") ?? fieldName, type, Label(row)));
            }
        }

        return new KoboAsset(name, fields);
    }

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    // Kobo stores labels as one entry per translation; the first is the default language.
    private static string? Label(JsonElement row)
    {
        if (!row.TryGetProperty("label", out var label))
        {
            return null;
        }

        return label.ValueKind switch
        {
            JsonValueKind.String => label.GetString(),
            JsonValueKind.Array => label.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()).FirstOrDefault(),
            _ => null,
        };
    }
}
