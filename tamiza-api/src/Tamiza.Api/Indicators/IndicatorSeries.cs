using System.Text.Json.Serialization;

namespace Tamiza.Api.Indicators;

/// <summary>The kinds of entry in a widget's series list (<see cref="SeriesInfo.Kind"/>), sent as strings.</summary>
public static class SeriesKinds
{
    /// <summary>A value of the series column.</summary>
    public const string Value = "value";

    /// <summary>The rows that have no value in the series column; shown as "(none)".</summary>
    public const string None = "none";

    /// <summary>The values folded together because the widget has more series than it shows; shown as "Other".</summary>
    public const string Other = "other";
}

/// <summary>One series of a widget, in display order. <paramref name="Value"/> is null for <see cref="SeriesKinds.None"/> and <see cref="SeriesKinds.Other"/>.</summary>
public sealed record SeriesInfo(string? Value, string Kind);

/// <summary>A group's total over every series, computed from its rows (a ratio's total is the ratio of the sums). <paramref name="Value"/> is null when it has none.</summary>
public sealed record GroupTotal(string? Group, double? Value, long Rows);

/// <summary>A series' total over every group of the widget, in the order of the response's <c>series</c>; null value and 0 rows for a series with no rows.</summary>
public sealed record SeriesTotal(string? Series, string Kind, double? Value, long Rows);

/// <summary>The total over every row the widget matches.</summary>
public sealed record GrandTotal(double? Value, long Rows);

/// <summary>The totals of a widget with a series (see design D12).</summary>
public sealed record SeriesTotals(IReadOnlyList<GroupTotal> Groups, IReadOnlyList<SeriesTotal> Series, GrandTotal All);

/// <summary>Which series a widget shows, and in what order (design D3, D4).</summary>
public static class SeriesDomain
{
    /// <summary>
    /// Keeps the series a widget has room for. <paramref name="valuesBestFirst"/> are the non-missing values, those with the largest total first, and may
    /// be cut after one more than <paramref name="limit"/>. When the values and the missing value do not fit in <paramref name="limit"/> entries, the
    /// largest are kept and the rest become one "Other" entry; the missing value is never folded, but it takes a place.
    /// </summary>
    public static (IReadOnlyList<string> Kept, bool HasOther) Fold(IReadOnlyList<string> valuesBestFirst, bool hasNone, int limit)
    {
        if (valuesBestFirst.Count + (hasNone ? 1 : 0) <= limit)
        {
            return (valuesBestFirst, false);
        }

        var room = Math.Max(0, limit - 1 - (hasNone ? 1 : 0));
        return (valuesBestFirst.Take(room).ToList(), true);
    }

    /// <summary>The display order: the kept values in natural order, then the missing value, then "Other".</summary>
    public static List<SeriesInfo> Order(IEnumerable<string> kept, bool hasNone, bool hasOther)
    {
        var series = kept.Order(NaturalOrder.ByText.Instance).Select(value => new SeriesInfo(value, SeriesKinds.Value)).ToList();
        if (hasNone)
        {
            series.Add(new SeriesInfo(null, SeriesKinds.None));
        }

        if (hasOther)
        {
            series.Add(new SeriesInfo(null, SeriesKinds.Other));
        }

        return series;
    }
}
