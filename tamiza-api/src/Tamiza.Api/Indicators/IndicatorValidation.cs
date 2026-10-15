using Tamiza.Api.CleanData;
using Tamiza.Api.Http;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Indicators;

/// <summary>
/// What a widget's fields must satisfy. The checks that need no table run on every request; the checks against
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
    /// The checks of a widget's query that need no table. <paramref name="chartType"/> is null when the caller has none, as the query
    /// endpoint may; then the per-type constraints are not checked. <paramref name="prefix"/> is <c>config.</c> inside a widget.
    /// </summary>
    public static void CheckConfig(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        if (string.IsNullOrEmpty(config.Indicator))
        {
            errors.Add(prefix + "indicator", "Choose an indicator.");
        }
        else if (!ColumnNames.IsValidTableName(config.Indicator))
        {
            errors.Add(prefix + "indicator", "The indicator is not a valid indicator name.");
        }

        if (config.Measure is not null && config.Ratio is not null)
        {
            errors.Add(prefix + "measure", "Plot a measure or a ratio, not both.");
        }

        CheckColumnName(config.Measure, prefix + "measure", "measure", errors);
        if (config.Ratio is { } ratio)
        {
            if (string.IsNullOrEmpty(ratio.Numerator))
            {
                errors.Add(prefix + "ratio.numerator", "Choose the numerator column.");
            }
            else
            {
                CheckColumnName(ratio.Numerator, prefix + "ratio.numerator", "numerator", errors);
            }

            if (string.IsNullOrEmpty(ratio.Denominator))
            {
                errors.Add(prefix + "ratio.denominator", "Choose the denominator column.");
            }
            else
            {
                CheckColumnName(ratio.Denominator, prefix + "ratio.denominator", "denominator", errors);
            }
        }

        CheckColumnName(config.GroupBy, prefix + "groupBy", "group column", errors);
        if (chartType is not null && ChartTypes.NeedsGroup(chartType) && config.GroupBy is null)
        {
            errors.Add(prefix + "groupBy", $"A {chartType} widget needs a column to group by.");
        }

        if (config.Buckets is { } buckets && (buckets < 1 || buckets > WidgetConfig.MaxBuckets))
        {
            errors.Add(prefix + "buckets", $"The number of buckets must be between 1 and {WidgetConfig.MaxBuckets}.");
        }

        if (chartType == ChartTypes.Map && config.GeometryColumn is null)
        {
            errors.Add(prefix + "geometryColumn", "A map widget needs a geometry column.");
        }
        else
        {
            CheckColumnName(config.GeometryColumn, prefix + "geometryColumn", "geometry column", errors);
        }

        CheckFilterShape(config.Filters, prefix, errors);
        CheckSort(config, chartType, prefix, errors);
    }

    /// <summary>The checks of a widget's order that need no table: where it may be, and the shape of its fields.</summary>
    private static void CheckSort(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        if (config.Sort is not { } sort)
        {
            return;
        }

        if (chartType is not null && chartType is not (ChartTypes.Bar or ChartTypes.Column))
        {
            errors.Add(prefix + "sort", "Only bar and column widgets can be sorted.");
            return;
        }

        if (config.GroupBy is null)
        {
            errors.Add(prefix + "sort", "Sorting needs a column to group by.");
        }

        if (sort.By is not (SortValues.ByValue or SortValues.ByColumn))
        {
            errors.Add(prefix + "sort.by", "Sort by 'value' or by 'column'.");
        }

        if (sort.Direction is not (SortValues.Asc or SortValues.Desc))
        {
            errors.Add(prefix + "sort.direction", "The direction must be 'asc' or 'desc'.");
        }

        if (sort.By == SortValues.ByColumn && sort.Column is null)
        {
            errors.Add(prefix + "sort.column", "Choose a column to sort by.");
        }
        else if (sort.By == SortValues.ByValue && sort.Column is not null)
        {
            errors.Add(prefix + "sort.column", "Sort by the plotted value or by a column, not both.");
        }

        CheckColumnName(sort.Column, prefix + "sort.column", "sort column", errors);

        if (sort.CompareAs is not null && sort.CompareAs is not (SortValues.Text or SortValues.Number))
        {
            errors.Add(prefix + "sort.compareAs", "Compare as 'text' or as 'number'.");
        }
        else if (sort.By == SortValues.ByValue && sort.CompareAs is not null)
        {
            errors.Add(prefix + "sort.compareAs", "The plotted value is always compared as a number.");
        }
    }

    /// <summary>
    /// What is wrong with comparing <paramref name="column"/> the way <paramref name="sort"/> says: a date, timestamp or true/false
    /// column is only compared in its own order. Null when the order fits the column.
    /// </summary>
    public static string? SortColumnError(WidgetSort sort, TableColumn column) =>
        sort.By == SortValues.ByColumn && sort.CompareAs is not null && column.Type is DataColumnType.Date or DataColumnType.Timestamp or DataColumnType.Boolean
            ? $"Column '{column.Name}' is {DataQueryBuilder.Describe(column.Type)}, so it is sorted in its own order and cannot be compared as text or as a number."
            : null;

    private static void CheckColumnName(string? name, string field, string what, FieldErrors errors)
    {
        if (name is { Length: > DataQueryBuilder.MaxTextLength })
        {
            errors.Add(field, $"The {what} is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }
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
    /// Checks a query against the published table: the measure or ratio columns must exist and be numbers, the group and geometry
    /// columns must exist, and the filters must make sense for their columns' types, exactly as the clean data dashboard checks them.
    /// </summary>
    public static void CheckAgainstColumns(WidgetConfig config, IReadOnlyList<TableColumn> columns, string timeZone, string prefix, FieldErrors errors)
    {
        var byName = columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        void Numeric(string? name, string field)
        {
            if (name is null)
            {
                return;
            }

            if (!byName.TryGetValue(name, out var column))
            {
                errors.Add(field, $"The table has no column '{name}'.");
            }
            else if (!IsNumeric(column))
            {
                errors.Add(field, $"Column '{name}' is not a number, so it cannot be plotted.");
            }
        }

        Numeric(config.Measure, prefix + "measure");
        Numeric(config.Ratio?.Numerator, prefix + "ratio.numerator");
        Numeric(config.Ratio?.Denominator, prefix + "ratio.denominator");
        if (config.GroupBy is not null && !byName.ContainsKey(config.GroupBy))
        {
            errors.Add(prefix + "groupBy", $"The table has no column '{config.GroupBy}'.");
        }

        if (config.GeometryColumn is not null && !byName.ContainsKey(config.GeometryColumn))
        {
            errors.Add(prefix + "geometryColumn", $"The table has no column '{config.GeometryColumn}'.");
        }

        if (config.Sort is { By: SortValues.ByColumn, Column: { } sortColumn } sort)
        {
            if (!byName.TryGetValue(sortColumn, out var column))
            {
                errors.Add(prefix + "sort.column", $"The table has no column '{sortColumn}'.");
            }
            else if (SortColumnError(sort, column) is { } sortError)
            {
                errors.Add(prefix + "sort.column", sortError);
            }
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

    public static bool IsNumeric(TableColumn column) => column.Type is DataColumnType.Integer or DataColumnType.Decimal;
}
