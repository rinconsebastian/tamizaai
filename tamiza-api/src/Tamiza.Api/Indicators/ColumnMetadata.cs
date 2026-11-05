using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tamiza.Api.Http;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Indicators;

/// <summary>
/// What an indicator says about one column of its table (design D2 of the indicator column metadata). Every property is optional and left
/// out of the JSON while it is not set, so later facts about a column are added here as nullable properties, each with its own checks in
/// <see cref="ColumnMetadata.Check"/>. The description is what the MCP tools' input schema shows of the property; nothing is validated
/// with it, since <see cref="ColumnMetadata"/> and <see cref="ColumnLabels"/> are the rule.
/// </summary>
/// <param name="Label">The name shown for the column instead of its own, 1 to 80 characters; left out, the column's own name.</param>
/// <param name="Order">
/// The values of the column in the order widgets show them (design D1 of add-declared-category-order), up to
/// <see cref="CategoryOrder.MaxValues"/> texts of up to 500 characters, none twice. Values it does not list follow in the chart's own order.
/// </param>
/// <param name="Last">
/// Values kept at the end whatever the order or the sort, up to <see cref="CategoryOrder.MaxLast"/>, such as "Otro", "No sabe" or "Sin dato".
/// A value is not in both this list and <paramref name="Order"/>.
/// </param>
/// <param name="StripPrefix">
/// Hides a leading number ("1. ", "2) ", "3 - ") in the text widgets show for the column's values; filters and colors kept by value still
/// use the full value. Only <c>true</c> is stored.
/// </param>
public sealed record IndicatorColumnMetadata(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The name people read for the column in captions, tables, emails and workbooks, 1 to 80 characters with no line break; left out, the column's own name.")] string? Label = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The column's values, as full text, in the order widgets draw them (up to 100, none twice, each up to 500 characters); values not listed follow in the chart's own order. Left out, the chart's own order.")] IReadOnlyList<string?>? Order = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("Values, as full text, that widgets keep at the end whatever the order or the sort, such as 'Otro', 'No sabe' or 'Sin dato' (up to 20, none twice, none also in order).")] IReadOnlyList<string?>? Last = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("true hides a leading number such as '1. ', '2) ' or '3 - ' in the text widgets show for the column's values; filters and colors kept by value still use the full value. Only true is stored.")] bool? StripPrefix = null)
{
    /// <summary>Whether the entry says anything: a label, an order, values kept last or hidden prefixes.</summary>
    internal bool HasAny =>
        (Label is { } label && !string.IsNullOrWhiteSpace(label)) || Order is { Count: > 0 } || Last is { Count: > 0 } || StripPrefix is true;
}

/// <summary>
/// The map from a column's name to what an indicator says about it, stored as JSON with the indicator's version
/// (<c>script_versions.columns</c>). It is the column validator that <c>ScriptEndpoints.ValidateMetadata</c> calls, and the place later
/// changes add the checks of their properties. The order of the map carries no meaning: <c>jsonb</c> gives the keys back in its own order.
/// </summary>
public static class ColumnMetadata
{
    /// <summary>The most columns of an indicator that can carry a property.</summary>
    public const int MaxColumns = 100;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The map as saved: a label is trimmed, an empty list of values is none, <c>stripPrefix: false</c> is none (so "off" is never
    /// stored), and a null entry or one with no property left is dropped. An empty map is null. The keys are kept as given;
    /// <see cref="Check"/> is what refuses them. The values of a list are exact text: they are never trimmed.
    /// </summary>
    public static Dictionary<string, IndicatorColumnMetadata>? Normalize(IReadOnlyDictionary<string, IndicatorColumnMetadata?>? columns)
    {
        if (columns is null)
        {
            return null;
        }

        var normalized = new Dictionary<string, IndicatorColumnMetadata>(StringComparer.Ordinal);
        foreach (var (column, entry) in columns)
        {
            if (entry is null || !entry.HasAny)
            {
                continue;
            }

            normalized[column] = entry with
            {
                Label = entry.Label is { } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null,
                Order = entry.Order is { Count: > 0 } ? entry.Order : null,
                Last = entry.Last is { Count: > 0 } ? entry.Last : null,
                StripPrefix = entry.StripPrefix is true ? true : null,
            };
        }

        return normalized.Count == 0 ? null : normalized;
    }

    /// <summary>
    /// Adds to <paramref name="errors"/> what refuses the map as sent (design D3 of the indicator column metadata and D6 of
    /// add-declared-category-order): more than <see cref="MaxColumns"/> columns with a property, a key that is not a column's name (on
    /// <c>columns</c>, never in a field path), a label too long or with a control character (on <c>columns.{column}.label</c>), and the
    /// rules of an order and of the values kept last (on <c>columns.{column}.order</c> and <c>.last</c>, an item on its <c>[i]</c>). A
    /// column with no property left is not counted or checked, as <see cref="Normalize"/> drops it. No type check: a column may be an
    /// integer, a date or true/false, and its values are compared as a widget's rows write them.
    /// </summary>
    public static void Check(IReadOnlyDictionary<string, IndicatorColumnMetadata?>? columns, FieldErrors errors)
    {
        if (columns is null)
        {
            return;
        }

        var kept = columns.Where(pair => pair.Value is { HasAny: true }).ToList();
        if (kept.Count > MaxColumns)
        {
            errors.Add("columns", $"An indicator labels at most {MaxColumns} columns.");
        }

        foreach (var (column, entry) in kept)
        {
            if (!ColumnLabels.CheckKey(column, "columns", errors))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry!.Label))
            {
                ColumnLabels.CheckLabel(entry.Label, $"columns.{column}.label", errors);
            }

            CheckValues(column, entry, errors);
        }
    }

    private static void CheckValues(string column, IndicatorColumnMetadata entry, FieldErrors errors)
    {
        var order = entry.Order ?? [];
        var last = entry.Last ?? [];
        CategoryOrder.CheckValues(order, CategoryOrder.MaxValues, $"An order lists at most {CategoryOrder.MaxValues} values.", $"columns.{column}.order", errors);
        CategoryOrder.CheckValues(last, CategoryOrder.MaxLast, $"At most {CategoryOrder.MaxLast} values are kept last.", $"columns.{column}.last", errors);

        var ordered = order.Where(value => value is not null).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < last.Count; i++)
        {
            if (last[i] is { } value && ordered.Contains(value))
            {
                errors.Add($"columns.{column}.last[{i}]", $"'{value}' is already in the order.");
            }
        }
    }

    /// <summary>The map in its stored text, or null when it holds nothing.</summary>
    public static string? Write(IReadOnlyDictionary<string, IndicatorColumnMetadata>? columns) =>
        columns is null || columns.Count == 0 ? null : JsonSerializer.Serialize(columns, Web);

    /// <summary>The map from its stored text, normalized; null when there is none. Unknown properties are ignored.</summary>
    public static Dictionary<string, IndicatorColumnMetadata>? Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var map = JsonSerializer.Deserialize<Dictionary<string, IndicatorColumnMetadata?>>(stored, Web);
        return Normalize(map);
    }
}

/// <summary>
/// The name shown for a column (design D6): the widget's own label, else its indicator's, else the column's own name. This is the C# twin
/// of <c>columnLabels</c> in tamiza-ui <c>indicators/format.ts</c>; <c>testing/column-labels.json</c> holds the cases both pass.
/// </summary>
public static class ColumnLabels
{
    /// <summary>The longest label of a column, counted in UTF-16 code units as a title is.</summary>
    public const int MaxLength = 80;

    /// <summary>The most columns one widget labels.</summary>
    public const int MaxWidgetLabels = 10;

    /// <summary>The longest part of an invalid key that an error message repeats.</summary>
    private const int MaxKeyInMessage = 63;

    /// <summary>
    /// Adds the error for a key that is not a column's name, on <paramref name="path"/> and not in a field path, so a key of any length or
    /// character never makes an odd key. Returns whether the key is valid.
    /// </summary>
    public static bool CheckKey(string key, string path, FieldErrors errors)
    {
        if (ColumnNames.IsValidColumnName(key))
        {
            return true;
        }

        var shown = key.Length > MaxKeyInMessage ? key[..MaxKeyInMessage] : key;
        errors.Add(path, $"'{shown}' is not a column's name: use the name the indicator's table gives the column, in lowercase letters, digits and underscores.");
        return false;
    }

    /// <summary>
    /// Adds the errors of a label to <paramref name="path"/>: more than <see cref="MaxLength"/> characters once trimmed, or a control
    /// character in what is left of it once trimmed (a label's own ends are trimmed away, as for a widget's label).
    /// </summary>
    public static void CheckLabel(string? label, string path, FieldErrors errors)
    {
        var text = label?.Trim();
        if (text is null)
        {
            return;
        }

        if (text.Length > MaxLength)
        {
            errors.Add(path, $"A column label can have up to {MaxLength} characters.");
        }

        if (text.Any(char.IsControl))
        {
            errors.Add(path, "A column label cannot hold a line break or another control character.");
        }
    }

    /// <summary>
    /// The name shown for each column: <paramref name="widgetLabels"/> (the widget's own) win over <paramref name="indicatorColumns"/>,
    /// which win over the column's own name. A blank label is none. With neither, every column is named as it is.
    /// </summary>
    public static Func<string, string> For(
        IReadOnlyDictionary<string, IndicatorColumnMetadata>? indicatorColumns, IReadOnlyDictionary<string, string>? widgetLabels) =>
        column =>
        {
            if (widgetLabels is not null && widgetLabels.TryGetValue(column, out var own) && !string.IsNullOrWhiteSpace(own))
            {
                return own.Trim();
            }

            if (indicatorColumns is not null && indicatorColumns.TryGetValue(column, out var entry)
                && entry?.Label is { } label && !string.IsNullOrWhiteSpace(label))
            {
                return label.Trim();
            }

            return column;
        };
}
