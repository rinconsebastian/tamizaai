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
        ChartTypes.Heatmap or ChartTypes.Pyramid or ChartTypes.Crosstab => (6, 5),
        ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line or ChartTypes.Histogram or ChartTypes.Pie or ChartTypes.Likert => (6, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(chartType), chartType, "Not a chart type."),
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

        CheckSeries(config, chartType, prefix, errors);

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
        CheckTooltip(config, chartType, prefix, errors);
        CheckBasemap(config.Basemap, chartType, prefix, errors);
        CheckIgnoredDashboardFilters(config.IgnoredDashboardFilters, prefix, errors);
    }

    /// <summary>
    /// The dashboard filters a widget ignores, by column (design D6): at most 8, once each, each a published column's name. They are
    /// not checked against any dashboard's filters, so a move, a copy or a filter defined again keeps them.
    /// </summary>
    private static void CheckIgnoredDashboardFilters(IReadOnlyList<string> columns, string prefix, FieldErrors errors)
    {
        if (columns.Count > DashboardFilterRules.MaxFilters)
        {
            errors.Add(prefix + "ignoredDashboardFilters", $"A widget can ignore at most {DashboardFilterRules.MaxFilters} dashboard filters.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            var field = $"{prefix}ignoredDashboardFilters[{i}]";
            if (!ColumnNames.IsValidColumnName(columns[i]))
            {
                errors.Add(field, $"'{columns[i]}' is not a valid column name.");
            }
            else if (!seen.Add(columns[i]))
            {
                errors.Add(field, $"The filter on column '{columns[i]}' is already ignored.");
            }
        }
    }

    /// <summary>The checks of a map's tooltip columns that need no table: where they may be, how many, and the shape of each.</summary>
    private static void CheckTooltip(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        var items = config.Tooltip;
        if (items.Count == 0)
        {
            return;
        }

        if (chartType is not null && chartType != ChartTypes.Map)
        {
            errors.Add(prefix + "tooltip", "Only map widgets can have tooltip columns.");
            return;
        }

        if (items.Count > WidgetConfig.MaxTooltipItems)
        {
            errors.Add(prefix + "tooltip", $"A map has at most {WidgetConfig.MaxTooltipItems} tooltip columns.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var field = $"{prefix}tooltip[{i}]";
            if (items[i] is not { } item)
            {
                errors.Add(field, "A tooltip column needs a column.");
                continue;
            }

            if (string.IsNullOrEmpty(item.Column))
            {
                errors.Add(field + ".column", "Choose the tooltip column.");
            }
            else if (item.Column.Length > DataQueryBuilder.MaxTextLength)
            {
                errors.Add(field + ".column", $"The tooltip column is longer than {DataQueryBuilder.MaxTextLength} characters.");
            }
            else if (!seen.Add(item.Column))
            {
                errors.Add(field + ".column", $"Column '{item.Column}' is already a tooltip column.");
            }
            else if (item.Column == config.GeometryColumn)
            {
                errors.Add(field + ".column", "The geometry column cannot be a tooltip column.");
            }

            if (item.Label is { Length: > WidgetConfig.MaxTooltipLabel })
            {
                errors.Add(field + ".label", $"The label is longer than {WidgetConfig.MaxTooltipLabel} characters.");
            }

            if (item.Format is not null && !TooltipFormats.All.Contains(item.Format))
            {
                errors.Add(field + ".format", $"The format must be one of {string.Join(", ", TooltipFormats.All)}.");
            }
        }
    }

    /// <summary>
    /// The series column and what goes with it (design D2): which kinds may or must have one, its layout, a pyramid's sides and a Likert
    /// scale. Per-kind rules are skipped when <paramref name="chartType"/> is null, as the query endpoint may have none.
    /// </summary>
    private static void CheckSeries(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        CheckColumnName(config.SeriesBy, prefix + "seriesBy", "series column", errors);
        if (config.SeriesBy is not null && config.SeriesBy == config.GroupBy)
        {
            errors.Add(prefix + "seriesBy", "Choose a series column other than the group column.");
        }
        else if (config.SeriesBy is not null && config.GroupBy is null)
        {
            errors.Add(prefix + "seriesBy", "A series needs a column to group by.");
        }

        if (chartType is not null)
        {
            if (config.SeriesBy is not null && !ChartTypes.HasSeries(chartType))
            {
                errors.Add(prefix + "seriesBy", $"A {chartType} widget cannot have a series.");
            }
            else if (config.SeriesBy is null && ChartTypes.NeedsSeries(chartType))
            {
                errors.Add(prefix + "seriesBy", $"A {chartType} widget needs a series column.");
            }
        }

        if (config.Layout is { } layout)
        {
            if (!SeriesLayouts.All.Contains(layout))
            {
                errors.Add(prefix + "layout", $"The layout must be '{SeriesLayouts.Grouped}', '{SeriesLayouts.Stacked}' or '{SeriesLayouts.Percent}'.");
            }
            else if (chartType is not null && chartType is not (ChartTypes.Bar or ChartTypes.Column))
            {
                errors.Add(prefix + "layout", "Only bar and column widgets have a layout.");
            }
            else if (config.SeriesBy is null)
            {
                errors.Add(prefix + "layout", "A layout needs a series column.");
            }
            else if (SeriesLayouts.Adds(layout) && config.Ratio is not null)
            {
                errors.Add(prefix + "layout", "Stacked bars add their values, so they need a measure, not a ratio.");
            }
        }

        CheckPyramid(config.Pyramid, chartType, prefix, errors);
        CheckScale(config, chartType, prefix, errors);
    }

    private static void CheckPyramid(WidgetPyramid? pyramid, string? chartType, string prefix, FieldErrors errors)
    {
        if (chartType is not null && chartType != ChartTypes.Pyramid)
        {
            if (pyramid is not null)
            {
                errors.Add(prefix + "pyramid", "Only population pyramids have sides.");
            }

            return;
        }

        if (pyramid is null)
        {
            if (chartType == ChartTypes.Pyramid)
            {
                errors.Add(prefix + "pyramid", "A population pyramid needs a left and a right value.");
            }

            return;
        }

        CheckSeriesValue(pyramid.Left, prefix + "pyramid.left", "Choose the value drawn to the left.", errors);
        CheckSeriesValue(pyramid.Right, prefix + "pyramid.right", "Choose the value drawn to the right.", errors);
        if (!string.IsNullOrEmpty(pyramid.Left) && string.Equals(pyramid.Left, pyramid.Right, StringComparison.Ordinal))
        {
            errors.Add(prefix + "pyramid.right", "The left and right values must differ.");
        }
    }

    private static void CheckScale(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        var scale = config.Scale;
        if (chartType is not null && chartType != ChartTypes.Likert)
        {
            if (scale.Count > 0)
            {
                errors.Add(prefix + "scale", "Only Likert widgets have a scale.");
            }

            return;
        }

        if (chartType is null && scale.Count == 0)
        {
            return;
        }

        if (chartType == ChartTypes.Likert && config.Ratio is not null)
        {
            errors.Add(prefix + "measure", "A Likert widget shows shares of a measure, not a ratio.");
        }

        if (scale.Count < WidgetConfig.MinScaleValues || scale.Count > WidgetConfig.MaxScaleValues)
        {
            errors.Add(prefix + "scale", $"A Likert scale has between {WidgetConfig.MinScaleValues} and {WidgetConfig.MaxScaleValues} values.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var counts = new int[3];
        var lastRank = -1;
        for (var i = 0; i < scale.Count; i++)
        {
            var field = $"{prefix}scale[{i}]";
            if (scale[i] is not { } step)
            {
                errors.Add(field, "Each value of the scale needs a name and a side.");
                continue;
            }

            CheckSeriesValue(step.Value, field + ".value", "Name the value.", errors);
            if (!string.IsNullOrEmpty(step.Value) && !seen.Add(step.Value))
            {
                errors.Add(field + ".value", $"'{step.Value}' is already in the scale.");
            }

            var rank = LikertSides.Rank(step.Side);
            if (rank < 0)
            {
                errors.Add(field + ".side", $"The side must be '{LikertSides.Negative}', '{LikertSides.Neutral}' or '{LikertSides.Positive}'.");
                continue;
            }

            counts[rank]++;
            if (rank < lastRank)
            {
                errors.Add(field + ".side", "Negative values come first, then the neutral one, then the positive ones.");
            }

            lastRank = Math.Max(lastRank, rank);
        }

        if (counts[1] > 1)
        {
            errors.Add(prefix + "scale", "A scale has at most one neutral value.");
        }

        if (counts[0] > WidgetConfig.MaxScaleSide || counts[2] > WidgetConfig.MaxScaleSide)
        {
            errors.Add(prefix + "scale", $"A scale has at most {WidgetConfig.MaxScaleSide} values on each side.");
        }
    }

    /// <summary>A value of the series column named in a pyramid or a scale: text, not empty, as long as a filter's value may be.</summary>
    private static void CheckSeriesValue(string? value, string field, string emptyMessage, FieldErrors errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            errors.Add(field, emptyMessage);
        }
        else if (value.Length > DataQueryBuilder.MaxTextLength)
        {
            errors.Add(field, $"The value is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }
    }

    private static void CheckBasemap(string? basemap, string? chartType, string prefix, FieldErrors errors)
    {
        if (basemap is null)
        {
            return;
        }

        if (!BasemapKinds.All.Contains(basemap))
        {
            errors.Add(prefix + "basemap", $"The basemap must be '{BasemapKinds.Streets}' or '{BasemapKinds.Satellite}'.");
        }
        else if (chartType is not null && chartType != ChartTypes.Map)
        {
            errors.Add(prefix + "basemap", "Only map widgets can have a basemap.");
        }
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
    /// The checks of a query's dashboard filters, which need no table (design D3): at most 8, one per column, each a published column's
    /// name, a known kind and a value of that kind. Errors are keyed <c>dashboardFilters[i]</c> and name the column.
    /// </summary>
    public static void CheckDashboardFilters(IReadOnlyList<AppliedDashboardFilter>? filters, FieldErrors errors)
    {
        if (filters is null)
        {
            return;
        }

        if (filters.Count > DashboardFilterRules.MaxFilters)
        {
            errors.Add("dashboardFilters", $"At most {DashboardFilterRules.MaxFilters} dashboard filters are allowed.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < filters.Count; i++)
        {
            var field = $"dashboardFilters[{i}]";
            if (filters[i] is not { } filter || !ColumnNames.IsValidColumnName(filter.Column))
            {
                errors.Add(field, $"The dashboard filter's column '{filters[i]?.Column}' is not a valid column name.");
            }
            else if (!seen.Add(filter.Column!))
            {
                errors.Add(field, $"Column '{filter.Column}' has more than one dashboard filter.");
            }
            else if (!DashboardFilterKinds.IsKnown(filter.Kind))
            {
                errors.Add(field, $"Column '{filter.Column}': the kind must be one of {string.Join(", ", DashboardFilterKinds.All)}.");
            }
            else
            {
                DashboardFilterKinds.CheckValue(filter.Kind!, filter.Value, filter.Column!, field, errors);
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

        if (config.SeriesBy is not null && !byName.ContainsKey(config.SeriesBy))
        {
            errors.Add(prefix + "seriesBy", $"The table has no column '{config.SeriesBy}'.");
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

        for (var i = 0; i < config.Tooltip.Count; i++)
        {
            if (config.Tooltip[i] is not { Column: { Length: > 0 } tooltipColumn } item)
            {
                continue;
            }

            var field = $"{prefix}tooltip[{i}]";
            if (!byName.TryGetValue(tooltipColumn, out var column))
            {
                errors.Add(field + ".column", $"The table has no column '{tooltipColumn}'.");
            }
            else if (item.Format is not null && !IsNumeric(column))
            {
                errors.Add(field + ".format", "Only a number column can have a format.");
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
