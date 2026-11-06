using System.Text.RegularExpressions;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.Indicators;

/// <summary>
/// One entry of a place that is ordered: a group axis or a series legend. <paramref name="Kind"/> is one of <see cref="SeriesKinds"/>:
/// a value (with its text), the missing value shown as "(none)", or the combined "Other".
/// </summary>
public readonly record struct OrderEntry(string? Value, string Kind)
{
    public static OrderEntry OfValue(string? value) => value is null ? new OrderEntry(null, SeriesKinds.None) : new OrderEntry(value, SeriesKinds.Value);
}

/// <summary>
/// The order that applies to one place (design D3): the values to list first, in order, the values kept at the end, and whether a sort
/// or an order by median decides the order of the rest, in which case <paramref name="Order"/> is not used and only the values kept at
/// the end move.
/// </summary>
public sealed record PlaceOrder(IReadOnlyList<string?> Order, IReadOnlyList<string?> Last, bool Sorted);

/// <summary>
/// What a widget declares for its values, resolved (design D3, D5): the order of its groups and of its series, and whether the text of
/// each place hides numbered prefixes. A null place is arranged as it is today.
/// </summary>
public sealed record CategoryOrders(PlaceOrder? Groups, PlaceOrder? Series, bool HideGroupPrefixes, bool HideSeriesPrefixes);

/// <summary>
/// The declared order of a column's values (add-declared-category-order): one rule, applied after the query by every consumer, with a
/// TypeScript twin (<c>category-order.ts</c>) checked against one shared fixture (<c>testing/category-orders.json</c>).
/// </summary>
public static class CategoryOrder
{
    /// <summary>The most values an order lists, for a widget and for an indicator's column.</summary>
    public const int MaxValues = 100;

    /// <summary>The most values an indicator's column keeps at the end, such as "Otro", "No sabe" or "Sin dato".</summary>
    public const int MaxLast = 20;

    /// <summary>
    /// A leading number of 1 to 3 digits, optional spaces, one of <c>. ) : - –</c> and at least one space, followed by a rest that is not
    /// empty and does not start with a digit or a space (design D5): "1. Sí", "2) No", "3 - Tal vez" lose their number; "10 - 14", "1.5
    /// horas", "1.Sí" and "$10.000" do not.
    /// </summary>
    private static readonly Regex Prefix = new(@"^[0-9]{1,3} *[.):\-–] +(?=[^0-9\s])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The rules of a list of values that a widget's order and a column's order and values kept last share (design D6): at most
    /// <paramref name="max"/> values, each a text of up to <see cref="DataQueryBuilder.MaxTextLength"/> characters with no NUL, none
    /// listed twice. A value is exact text, so it is never trimmed and the empty text is a value. <paramref name="key"/> is the list's
    /// key, and an item's error is on <c>key[i]</c>, the second one for a value listed twice.
    /// </summary>
    internal static void CheckValues(IReadOnlyList<string?> values, int max, string tooMany, string key, FieldErrors errors)
    {
        if (values.Count > max)
        {
            errors.Add(key, tooMany);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (value is null || value.Length > DataQueryBuilder.MaxTextLength)
            {
                errors.Add($"{key}[{i}]", $"Each value is a text of up to {DataQueryBuilder.MaxTextLength} characters.");
            }
            else if (value.Contains('\0'))
            {
                errors.Add($"{key}[{i}]", "The value cannot contain a NUL character.");
            }
            else if (!seen.Add(value))
            {
                errors.Add($"{key}[{i}]", $"'{value}' is listed twice.");
            }
        }
    }

    /// <summary>
    /// Arranges the <paramref name="entries"/> of one place, which come in the order the place has today (design D3): the entries the
    /// order lists, in its order; then the others, as given; then the values kept last, in their own order; then "(none)"; then "Other".
    /// Values match as exact text, a listed value that is not an entry is skipped, and a place with no declaration, or whose declaration
    /// has nothing that applies, is returned as it came.
    /// </summary>
    public static List<T> Arrange<T>(IReadOnlyList<T> entries, Func<T, OrderEntry> keyOf, PlaceOrder? declared)
    {
        if (declared is null)
        {
            return [.. entries];
        }

        var order = declared.Sorted ? [] : declared.Order;
        if (order.Count == 0 && declared.Last.Count == 0)
        {
            return [.. entries];
        }

        var listedAt = IndexOf(order);
        var keptAt = IndexOf(declared.Last);
        var listed = new List<(int At, T Entry)>();
        var kept = new List<(int At, T Entry)>();
        var rest = new List<T>();
        var none = new List<T>();
        var other = new List<T>();
        foreach (var entry in entries)
        {
            var key = keyOf(entry);
            if (key.Kind == SeriesKinds.Other)
            {
                other.Add(entry);
            }
            else if (key.Kind == SeriesKinds.None || key.Value is null)
            {
                none.Add(entry);
            }
            else if (listedAt.TryGetValue(key.Value, out var at))
            {
                listed.Add((at, entry));
            }
            else if (keptAt.TryGetValue(key.Value, out var keptIndex))
            {
                kept.Add((keptIndex, entry));
            }
            else
            {
                rest.Add(entry);
            }
        }

        var result = new List<T>(entries.Count);
        result.AddRange(listed.OrderBy(pair => pair.At).Select(pair => pair.Entry));
        result.AddRange(rest);
        result.AddRange(kept.OrderBy(pair => pair.At).Select(pair => pair.Entry));
        result.AddRange(none);
        result.AddRange(other);
        return result;
    }

    private static Dictionary<string, int> IndexOf(IReadOnlyList<string?> values)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is { } value)
            {
                index.TryAdd(value, i);
            }
        }

        return index;
    }

    /// <summary>The text of one value without its leading number (design D5), or the value itself when it has none to hide.</summary>
    public static string StripPrefix(string value) => Prefix.Match(value) is { Success: true } match ? value[match.Length..] : value;

    /// <summary>
    /// The text each value of one place is shown with (design D5): without its leading number when <paramref name="strip"/> is set,
    /// except that two different values that would read alike keep their full text. The map holds every distinct non-null value.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ShownValues(IEnumerable<string?> values, bool strip)
    {
        var distinct = values.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (!strip)
        {
            return distinct.ToDictionary(value => value, value => value, StringComparer.Ordinal);
        }

        var stripped = distinct.ToDictionary(value => value, StripPrefix, StringComparer.Ordinal);
        var alike = stripped.Values.GroupBy(text => text, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        return distinct.ToDictionary(value => value, value => alike.Contains(stripped[value]) ? value : stripped[value], StringComparer.Ordinal);
    }

    /// <summary>The text a value is shown with, from <see cref="ShownValues"/>; a missing value stays missing, and a value the map lacks is shown as it is.</summary>
    public static string? Shown(IReadOnlyDictionary<string, string>? shown, string? value) =>
        value is not null && shown is not null && shown.TryGetValue(value, out var text) ? text : value;

    /// <summary>
    /// What a widget of <paramref name="chartType"/> declares for its groups and series (design D3 and D4), from its own order and its
    /// indicator's column metadata: the widget's list replaces the column's order, never its values kept last, and a sort (or a box
    /// plot's order by median) keeps only the values kept last. Null when no place has a declaration and no column it shows hides
    /// prefixes, so a caller then runs today's code.
    /// </summary>
    public static CategoryOrders? For(string? chartType, WidgetConfig config, IReadOnlyDictionary<string, IndicatorColumnMetadata>? columns)
    {
        if (chartType is null)
        {
            return null;
        }

        IndicatorColumnMetadata? MetaOf(string? column) =>
            column is not null && columns is not null && columns.TryGetValue(column, out var meta) ? meta : null;

        PlaceOrder? groups = null;
        var hideGroups = false;
        if (config.GroupBy is { } groupBy && chartType is not (ChartTypes.Stat or ChartTypes.Histogram))
        {
            var meta = MetaOf(groupBy);
            hideGroups = meta?.StripPrefix is true;
            if (chartType != ChartTypes.Map)
            {
                var sorted = (chartType is ChartTypes.Bar or ChartTypes.Column && config.Sort is not null)
                    || (chartType == ChartTypes.Boxplot && config.Sort is { By: SortValues.ByValue });
                groups = PlaceOf(config.Display?.Order?.Group, meta, sorted);
            }
        }

        PlaceOrder? series = null;
        var hideSeries = false;
        if (config.SeriesBy is { } seriesBy && ChartTypes.HasSeries(chartType))
        {
            var meta = MetaOf(seriesBy);
            hideSeries = meta?.StripPrefix is true;
            if (chartType is not (ChartTypes.Pyramid or ChartTypes.Likert))
            {
                series = PlaceOf(config.Display?.Order?.Series, meta, sorted: false);
            }
        }

        return groups is null && series is null && !hideGroups && !hideSeries ? null : new CategoryOrders(groups, series, hideGroups, hideSeries);
    }

    private static PlaceOrder? PlaceOf(IReadOnlyList<string?>? widgetOrder, IndicatorColumnMetadata? meta, bool sorted)
    {
        var order = widgetOrder is { Count: > 0 } ? widgetOrder : meta?.Order ?? [];
        var last = meta?.Last ?? [];
        return (sorted ? last.Count == 0 : order.Count == 0 && last.Count == 0) ? null : new PlaceOrder(order, last, sorted);
    }

    /// <summary>
    /// The grid of a widget with a series arranged (design D4): the rows in the order of the groups and each column, with its cells,
    /// shares and total, in the order of the series. A grid whose places have no declaration is returned as it is.
    /// </summary>
    public static SeriesTable ArrangeTable(SeriesTable table, CategoryOrders? orders)
    {
        if (orders is null || (orders.Groups is null && orders.Series is null))
        {
            return table;
        }

        var columns = Arrange(
            Enumerable.Range(0, table.Columns.Count).ToList(), i => new OrderEntry(table.Columns[i].Series, table.Columns[i].Kind), orders.Series);
        var rows = Arrange(table.Rows, row => OrderEntry.OfValue(row.Group), orders.Groups)
            .Select(row => row with
            {
                Cells = columns.Select(i => row.Cells[i]).ToList(),
                Shares = row.Shares is { } shares ? columns.Select(i => shares[i]).ToList() : null,
                Figures = row.Figures is { } figures ? columns.Select(i => figures[i]).ToList() : null,
            })
            .ToList();
        return table with { Columns = columns.Select(i => table.Columns[i]).ToList(), Rows = rows };
    }
}
