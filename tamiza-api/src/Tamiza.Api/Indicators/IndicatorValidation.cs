using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.Indicators;

/// <summary>
/// What a widget's fields must satisfy (design D3 and D9). The checks that need no table run on every request; the checks against
/// the published table's columns run when the table is published (<see cref="CheckAgainstColumns"/>).
/// </summary>
public static class IndicatorValidation
{
    public const int MaxTitleLength = 200;
    public const int GridColumns = 12;
    public const int MaxHeight = 40;

    private static readonly string[] FilterOps = ["contains", "equals", "in", "range", "empty", "notEmpty"];

    /// <summary>The size a new widget of this type gets.</summary>
    public static (int W, int H) DefaultSize(string chartType) => chartType switch
    {
        ChartTypes.Stat => (3, 2),
        ChartTypes.Map => (6, 6),
        _ => (6, 4),
    };

    public static void CheckTitle(string? title, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("title", "The title is required.");
        }
        else if (title.Trim().Length > MaxTitleLength)
        {
            errors.Add("title", $"The title is longer than {MaxTitleLength} characters.");
        }
    }

    public static void CheckChartType(string? chartType, FieldErrors errors)
    {
        if (!ChartTypes.IsKnown(chartType))
        {
            errors.Add("chartType", $"The chart type must be one of {string.Join(", ", ChartTypes.All)}.");
        }
    }

    public static void CheckLayout(WidgetLayout? layout, FieldErrors errors)
    {
        if (layout is null)
        {
            errors.Add("layout", "The layout is required.");
            return;
        }

        if (layout.X < 0)
        {
            errors.Add("layout.x", "x must be 0 or more.");
        }

        if (layout.Y < 0)
        {
            errors.Add("layout.y", "y must be 0 or more.");
        }

        if (layout.W < 1)
        {
            errors.Add("layout.w", "w must be 1 or more.");
        }

        if (layout.H is < 1 or > MaxHeight)
        {
            errors.Add("layout.h", $"h must be between 1 and {MaxHeight}.");
        }

        if (layout.X >= 0 && layout.W >= 1 && (long)layout.X + layout.W > GridColumns)
        {
            errors.Add("layout.w", $"x + w must not pass the {GridColumns} columns of the grid.");
        }
    }

    /// <summary>
    /// The checks of a widget's query that need no table. <paramref name="chartType"/> is null for the query endpoint, which has
    /// no chart type; then the per-type constraints are not checked. <paramref name="prefix"/> is <c>config.</c> inside a widget.
    /// </summary>
    public static void CheckConfig(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        var indicators = config.Indicators;
        if (indicators.Count == 0)
        {
            errors.Add(prefix + "indicators", "Choose at least one indicator.");
        }
        else if (indicators.Count > WidgetConfig.MaxIndicators)
        {
            errors.Add(prefix + "indicators", $"At most {WidgetConfig.MaxIndicators} indicators are allowed.");
        }
        else if (indicators.Any(i => string.IsNullOrEmpty(i) || i.Length > DataQueryBuilder.MaxTextLength))
        {
            errors.Add(prefix + "indicators", $"An indicator is empty or longer than {DataQueryBuilder.MaxTextLength} characters.");
        }
        else if (chartType is not null && ChartTypes.NeedsExactlyOneIndicator(chartType) && indicators.Count != 1)
        {
            errors.Add(prefix + "indicators", $"A {chartType} widget uses exactly one indicator.");
        }

        if (config.GroupBy.Length > DataQueryBuilder.MaxTextLength)
        {
            errors.Add(prefix + "groupBy", $"The group column is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }

        if (!ValueFields.All.Contains(config.ValueField))
        {
            errors.Add(prefix + "valueField", $"The field to plot must be one of {string.Join(", ", ValueFields.All)}.");
        }

        if (config.Buckets is { } buckets && (buckets < 1 || buckets > WidgetConfig.MaxBuckets))
        {
            errors.Add(prefix + "buckets", $"The number of buckets must be between 1 and {WidgetConfig.MaxBuckets}.");
        }

        if (chartType == ChartTypes.Map && config.GeometryColumn is null)
        {
            errors.Add(prefix + "geometryColumn", "A map widget needs a geometry column.");
        }
        else if (config.GeometryColumn is { Length: > DataQueryBuilder.MaxTextLength })
        {
            errors.Add(prefix + "geometryColumn", $"The geometry column is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }

        CheckFilterShape(config.Filters, prefix, errors);
    }

    /// <summary>What a filter must have whatever the table is. The meaning of the operator for the column's type is checked against the table.</summary>
    private static void CheckFilterShape(IReadOnlyList<DataFilter> filters, string prefix, FieldErrors errors)
    {
        if (filters.Count > DataQueryBuilder.MaxFilters)
        {
            errors.Add(prefix + "filters", $"At most {DataQueryBuilder.MaxFilters} filters are allowed.");
            return;
        }

        for (var i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            var field = $"{prefix}filters[{i}]";
            if (filter is null || string.IsNullOrEmpty(filter.Column))
            {
                errors.Add(field, "A filter needs a column.");
            }
            else if (!FilterOps.Contains(filter.Op))
            {
                errors.Add(field, $"The filter operator must be one of {string.Join(", ", FilterOps)}.");
            }
            else if (filter.Values is { Count: > DataQueryBuilder.MaxInValues })
            {
                errors.Add(field, $"Column '{filter.Column}': at most {DataQueryBuilder.MaxInValues} values are allowed.");
            }
        }
    }

    /// <summary>
    /// Checks a query against the published table: the group column, the geometry column and the filters' columns must exist, and
    /// the filters must make sense for their columns' types, exactly as the clean data dashboard checks them.
    /// </summary>
    public static void CheckAgainstColumns(WidgetConfig config, IReadOnlyList<TableColumn> columns, string timeZone, string prefix, FieldErrors errors)
    {
        var names = columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(config.GroupBy))
        {
            errors.Add(prefix + "groupBy", $"The table has no column '{config.GroupBy}'.");
        }

        if (config.GeometryColumn is not null && !names.Contains(config.GeometryColumn))
        {
            errors.Add(prefix + "geometryColumn", $"The table has no column '{config.GeometryColumn}'.");
        }

        var filterErrors = new FieldErrors();
        DataQueryBuilder.Build(new DataQueryRequest(config.Filters), columns, timeZone, filterErrors);
        foreach (var (field, messages) in filterErrors.ToDictionary())
        {
            foreach (var message in messages)
            {
                errors.Add(prefix + field, message);
            }
        }
    }
}
