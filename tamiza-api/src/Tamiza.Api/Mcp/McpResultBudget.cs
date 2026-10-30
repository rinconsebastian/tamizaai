using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tamiza.Api.Mcp;

/// <summary>
/// Keeps a tool result within the characters a client reads (design D11). A longer result loses items from the end of its longest list,
/// repeatedly, until it fits, and says which list was cut, how many items were left out and which argument asks for less. A result
/// whose lists are all empty and whose text is still too long loses the end of its longest text.
/// </summary>
public static class McpResultBudget
{
    /// <summary>
    /// The camelCase JSON the REST API speaks, without the members that are null: results are read by models, and every token counts.
    /// Characters such as <c>&gt;</c> and accented letters are written as they are, not as <c>\u003E</c>: the text goes to a model, never into a page.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Room kept for the <c>truncated</c> note.</summary>
    private const int NoteReserve = 600;

    /// <summary>The most other cut lists the note names.</summary>
    private const int MaxNamedOthers = 5;

    /// <summary>The longest text kept when only text is left to cut.</summary>
    private const int MinKeptText = 200;

    /// <summary>The result as JSON text of at most <paramref name="maxCharacters"/> characters, with the note when something was left out.</summary>
    public static string Serialize(object? value, int maxCharacters, string? hint = null)
    {
        var node = JsonSerializer.SerializeToNode(value, Json);
        var text = node?.ToJsonString(Json) ?? "null";
        if (text.Length <= maxCharacters || node is not JsonObject root)
        {
            return text;
        }

        var cut = new List<(string Path, int Omitted, string Unit)>();
        var budget = maxCharacters - NoteReserve;
        while (root.ToJsonString(Json).Length > budget)
        {
            var excess = root.ToJsonString(Json).Length - budget;
            if (!CutLongestList(root, excess, cut) && !CutLongestText(root, excess, cut))
            {
                break;
            }
        }

        var note = new JsonObject
        {
            ["list"] = cut.Count > 0 ? cut[0].Path : null,
            ["omitted"] = cut.Count > 0 ? cut[0].Omitted : 0,
            ["hint"] = hint ?? "Ask for less: name the columns, a page or one item.",
        };
        if (cut.Count > 0 && cut[0].Unit != "items")
        {
            note["unit"] = cut[0].Unit;
        }

        if (cut.Count > 1)
        {
            // The note must stay within its reserve however many lists were cut: the five that lost the most are named, the rest counted.
            var others = cut.Skip(1).OrderByDescending(c => c.Omitted).ToList();
            note["others"] = new JsonArray(others.Take(MaxNamedOthers).Select(c => (JsonNode)new JsonObject { ["list"] = c.Path, ["omitted"] = c.Omitted }).ToArray());
            if (others.Count > MaxNamedOthers)
            {
                note["moreLists"] = others.Count - MaxNamedOthers;
            }
        }

        root["truncated"] = note;
        return root.ToJsonString(Json);
    }

    /// <summary>Removes items from the end of the list with the most characters, enough to cover <paramref name="excess"/>; false when every list is empty.</summary>
    private static bool CutLongestList(JsonObject root, int excess, List<(string Path, int Omitted, string Unit)> cut)
    {
        // A list whose items hold lists of their own (the tables of a result, each with its columns) is a container: cutting it would drop
        // whole items, so the lists inside are cut first and a container only when nothing else is left. A list of lists of values (rows)
        // is not a container, because its items are cut as a whole.
        var lists = Arrays(root, "").Where(a => a.Array.Count > 0).Select(a => (a.Array, a.Path, Length: a.Array.ToJsonString(Json).Length, Container: HoldsList(a.Array))).ToList();
        var candidates = lists.Any(l => !l.Container) ? lists.Where(l => !l.Container) : lists;
        (JsonArray Array, string Path, int Length)? longest = null;
        foreach (var (array, path, length, _) in candidates)
        {
            if (longest is null || length > longest.Value.Length)
            {
                longest = (array, path, length);
            }
        }

        if (longest is null)
        {
            return false;
        }

        var (list, listPath, listLength) = longest.Value;
        var average = Math.Max(1, listLength / list.Count);
        var remove = Math.Clamp((int)Math.Ceiling(excess / (double)average), 1, list.Count);
        for (var i = 0; i < remove; i++)
        {
            list.RemoveAt(list.Count - 1);
        }

        Record(cut, listPath, remove, "items");
        return true;
    }

    /// <summary>Whether an item of <paramref name="array"/> is an object that holds a list with items.</summary>
    private static bool HoldsList(JsonArray array) =>
        array.OfType<JsonObject>().Any(item => Arrays(item, "").Any(found => found.Array.Count > 0));

    /// <summary>Cuts the end of the longest text, enough to cover <paramref name="excess"/>; false when no text is long enough to cut.</summary>
    private static bool CutLongestText(JsonObject root, int excess, List<(string Path, int Omitted, string Unit)> cut)
    {
        (JsonObject Owner, string Name, string Path, string Text)? longest = null;
        foreach (var (owner, name, path, text) in Texts(root, ""))
        {
            if (text.Length > MinKeptText && (longest is null || text.Length > longest.Value.Text.Length))
            {
                longest = (owner, name, path, text);
            }
        }

        if (longest is null)
        {
            return false;
        }

        var (holder, key, textPath, whole) = longest.Value;
        var keep = Math.Max(MinKeptText, whole.Length - excess - 1);
        var omitted = whole.Length - keep;
        holder[key] = whole[..keep];
        Record(cut, textPath, omitted, "characters");
        return true;
    }

    private static void Record(List<(string Path, int Omitted, string Unit)> cut, string path, int omitted, string unit)
    {
        var index = cut.FindIndex(c => c.Path == path);
        if (index >= 0)
        {
            cut[index] = (path, cut[index].Omitted + omitted, unit);
        }
        else
        {
            cut.Add((path, omitted, unit));
        }
    }

    private static IEnumerable<(JsonArray Array, string Path)> Arrays(JsonNode node, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                {
                    if (child is not null)
                    {
                        foreach (var found in Arrays(child, path.Length == 0 ? name : $"{path}.{name}"))
                        {
                            yield return found;
                        }
                    }
                }

                break;
            case JsonArray array:
                yield return (array, path);
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item)
                    {
                        foreach (var found in Arrays(item, $"{path}[{i}]"))
                        {
                            yield return found;
                        }
                    }
                }

                break;
        }
    }

    private static IEnumerable<(JsonObject Owner, string Name, string Path, string Text)> Texts(JsonNode node, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                {
                    var childPath = path.Length == 0 ? name : $"{path}.{name}";
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        yield return (obj, name, childPath, text);
                    }
                    else if (child is not null)
                    {
                        foreach (var found in Texts(child, childPath))
                        {
                            yield return found;
                        }
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonObject item)
                    {
                        foreach (var found in Texts(item, $"{path}[{i}]"))
                        {
                            yield return found;
                        }
                    }
                }

                break;
        }
    }
}
