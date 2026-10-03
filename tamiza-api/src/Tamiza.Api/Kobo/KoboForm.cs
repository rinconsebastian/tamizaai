using System.Text.Json;
using Tamiza.Api.Data;

namespace Tamiza.Api.Kobo;

public sealed record KoboAsset(string Name, IReadOnlyList<FormField> Fields, IReadOnlyList<FormChoice> Choices);

/// <summary>Reads the form name, its fields and its choice lists from a Kobo API v2 asset document.</summary>
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
        var choices = new List<FormChoice>();
        if (asset.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
        {
            foreach (var row in Rows(content, "survey"))
            {
                var rawType = Text(row, "type");
                var type = rawType?.Replace(' ', '_');
                var fieldName = Text(row, "name") ?? Text(row, "$autoname");
                if (type is null || fieldName is null || StructuralTypes.Contains(type))
                {
                    continue;
                }

                fields.Add(new FormField(fieldName, Text(row, "$xpath") ?? fieldName, type, Label(row), ListName(row, rawType!)));
            }

            foreach (var row in Rows(content, "choices"))
            {
                var listName = Text(row, "list_name");
                var choiceName = Text(row, "name") ?? Text(row, "$autovalue");
                if (listName is not null && choiceName is not null)
                {
                    choices.Add(new FormChoice(listName, choiceName, Label(row)));
                }
            }
        }

        return new KoboAsset(name, fields, choices);
    }

    private static IEnumerable<JsonElement> Rows(JsonElement content, string property) =>
        content.TryGetProperty(property, out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object)
            : [];

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
