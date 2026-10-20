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
