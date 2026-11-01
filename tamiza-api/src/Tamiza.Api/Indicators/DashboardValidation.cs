using Tamiza.Api.CleanData;
using Tamiza.Api.Http;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Indicators;

/// <summary>
/// What a dashboard's name must satisfy on its own (design D5). That no other dashboard of the project has it is checked against the
/// project's dashboards by <see cref="DashboardEndpoints"/>, and by the unique index for a race.
/// </summary>
public static class DashboardValidation
{
    public const int MaxNameLength = 40;

    /// <summary>The permanent tabs no dashboard may be named after, by normalized name, with the name each is shown under.</summary>
    private static readonly Dictionary<string, string> Reserved = new(StringComparer.Ordinal)
    {
        ["performance"] = "Performance",
        ["clean data"] = "Clean data",
        ["settings"] = "Settings",
    };

    /// <summary>The trimmed name, or the error on <c>name</c> that refuses it.</summary>
    public static (string? Name, string? Error) Name(string? input)
    {
        var name = input?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return (null, "The name is required.");
        }

        // Checked on the name as sent: trimming would quietly drop a trailing line break.
        if (input!.Any(char.IsControl))
        {
            return (null, "The name cannot contain control characters.");
        }

        if (name.Length > MaxNameLength)
        {
            return (null, $"The name can have at most {MaxNameLength} characters.");
        }

        if (Reserved.TryGetValue(Normalize(name), out var tab))
        {
            return (null, $"\"{tab}\" is the name of a permanent tab. Choose another name.");
        }

        return (name, null);
    }

    /// <summary>The name as <c>dashboards.normalized_name</c> stores it: trimmed and in lower case.</summary>
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    public static string TakenMessage(string name) => $"Another dashboard of this project is named \"{name}\".";

    /// <summary>
    /// A dashboard's filters as they are saved, or null with <paramref name="errors"/> keyed <c>filters</c> and <c>filters[i].label</c>,
    /// <c>.column</c>, <c>.kind</c> or <c>.default</c> (design D4). Nothing is checked against the tables: a filter may be defined before
    /// its indicators publish, as a widget may. Labels are trimmed; a default keeps only what its kind takes.
    /// </summary>
    public static List<DashboardFilter>? Filters(IReadOnlyList<DashboardFilterInput?>? input, FieldErrors errors)
    {
        if (input is null)
        {
            errors.Add("filters", "The filters are required; send an empty list for none.");
            return null;
        }

        if (input.Count > DashboardFilterRules.MaxFilters)
        {
            errors.Add("filters", $"A dashboard can have at most {DashboardFilterRules.MaxFilters} filters.");
            return null;
        }

        var filters = new List<DashboardFilter>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < input.Count; i++)
        {
            var field = $"filters[{i}]";
            if (input[i] is not { } filter)
            {
                errors.Add(field, "A filter needs a label, a column and a kind.");
                continue;
            }

            var label = Label(filter.Label, field + ".label", errors);
            if (label is not null && !labels.Add(label.ToLowerInvariant()))
            {
                errors.Add(field + ".label", $"Another filter of this dashboard is labelled \"{label}\".");
            }

            var column = filter.Column;
            if (!ColumnNames.IsValidColumnName(column))
            {
                errors.Add(field + ".column", "The column must be a published column's name: lowercase letters, digits and underscores, "
                    + "not starting with a digit, at most 63 characters.");
                column = null;
            }
            else if (!columns.Add(column!))
            {
                errors.Add(field + ".column", $"Column '{column}' already has a filter on this dashboard.");
            }

            var kind = filter.Kind;
            if (!DashboardFilterKinds.IsKnown(kind))
            {
                errors.Add(field + ".kind", $"The kind must be one of {string.Join(", ", DashboardFilterKinds.All)}.");
                continue;
            }

            if (filter.Default is { } value)
            {
                DashboardFilterKinds.CheckValue(kind!, value, column ?? filter.Column ?? "", field + ".default", errors);
            }

            if (label is not null && column is not null)
            {
                filters.Add(new DashboardFilter(column, label, kind!, Normalize(kind!, filter.Default)));
            }
        }

        return errors.Any ? null : filters;
    }

    /// <summary>
    /// A dashboard's palette and colors kept by value as they are saved, or null with <paramref name="errors"/> keyed <c>palette</c>,
    /// <c>valueColors</c>, <c>valueColors[i]</c> or <c>valueColors[i].column</c>, <c>.value</c> or <c>.color</c> (design D4 of dashboard
    /// palettes). The same function serves the REST endpoint and the MCP tools, which give it the palette the dashboard has when the
    /// request names none. Nothing is checked against the tables, and nothing is trimmed: a value is matched exactly, so an entry is
    /// kept as it was sent.
    /// </summary>
    public static DashboardColors? Colors(string? palette, IReadOnlyList<ValueColorInput?>? input, FieldErrors errors)
    {
        if (!Palettes.IsKnown(palette))
        {
            errors.Add("palette", $"Choose one of the palettes: {string.Join(", ", Palettes.All)}.");
        }

        if (input is null)
        {
            errors.Add("valueColors", "The colors kept by value are required; send an empty list for none.");
            return null;
        }

        if (input.Count > Palettes.MaxValueColors)
        {
            errors.Add("valueColors", $"A dashboard keeps at most {Palettes.MaxValueColors} colors by value.");
            return null;
        }

        var entries = new List<ValueColor>();
        // What each column keeps so far: the values and the colors of its earlier entries.
        var kept = new Dictionary<string, (HashSet<string> Values, HashSet<int> Colors)>(StringComparer.Ordinal);
        for (var i = 0; i < input.Count; i++)
        {
            var field = $"valueColors[{i}]";
            if (input[i] is not { } entry)
            {
                errors.Add(field, "An entry needs a column, a value and a color.");
                continue;
            }

            var column = entry.Column;
            if (!ColumnNames.IsValidColumnName(column))
            {
                errors.Add(field + ".column", "The column must be a published column's name: lowercase letters, digits and underscores, "
                    + "not starting with a digit, at most 63 characters.");
                column = null;
            }

            var value = entry.Value;
            if (value is null)
            {
                errors.Add(field + ".value", "The value is required; an empty text is a value too.");
            }
            else if (value.Length > DataQueryBuilder.MaxTextLength)
            {
                errors.Add(field + ".value", $"The value is longer than {DataQueryBuilder.MaxTextLength} characters.");
                value = null;
            }
            else if (value.Contains('\0'))
            {
                // Any other character is a value as it is, but jsonb cannot hold NUL, so saving it would end as a 500.
                errors.Add(field + ".value", "The value cannot contain a NUL character.");
                value = null;
            }

            var color = entry.Color;
            if (!Palettes.IsColor(color))
            {
                errors.Add(field + ".color", $"The color must be a number from 1 to {Palettes.ColorCount}.");
                color = null;
            }

            if (column is null || value is null || color is null)
            {
                continue;
            }

            // A value and a color are each kept once per column, so no two values of a widget share a color. Values compare as text,
            // exactly. An entry refused here is not kept, so it takes nothing from the entries after it.
            if (!kept.TryGetValue(column, out var ofColumn))
            {
                ofColumn = ([], []);
                kept[column] = ofColumn;
            }

            if (ofColumn.Values.Contains(value))
            {
                errors.Add(field, $"{column} already keeps a color for {(value.Length == 0 ? "an empty text" : value)}.");
            }
            else if (ofColumn.Colors.Contains(color.Value))
            {
                errors.Add(field, $"{column} already uses color {color}.");
            }
            else
            {
                ofColumn.Values.Add(value);
                ofColumn.Colors.Add(color.Value);
                entries.Add(new ValueColor(column, value, color.Value));
            }
        }

        return errors.Any ? null : new DashboardColors(palette!, entries);
    }

    private static string? Label(string? input, string field, FieldErrors errors)
    {
        var label = input?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            errors.Add(field, "The label is required.");
            return null;
        }

        if (input!.Any(char.IsControl))
        {
            errors.Add(field, "The label cannot contain control characters.");
            return null;
        }

        if (label.Length > DashboardFilterRules.MaxLabelLength)
        {
            errors.Add(field, $"The label can have at most {DashboardFilterRules.MaxLabelLength} characters.");
            return null;
        }

        return label;
    }

    /// <summary>Values for a text or true/false filter, a range for the others; an empty end of a range is no end.</summary>
    private static DashboardFilterValue? Normalize(string kind, DashboardFilterValue? value) =>
        value is null ? null
        : DashboardFilterKinds.TakesValues(kind) ? new DashboardFilterValue(value.Values)
        : new DashboardFilterValue(null, string.IsNullOrEmpty(value.Min) ? null : value.Min, string.IsNullOrEmpty(value.Max) ? null : value.Max);
}
