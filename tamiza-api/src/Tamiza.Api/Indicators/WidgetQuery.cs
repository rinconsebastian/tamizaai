namespace Tamiza.Api.Indicators;

/// <summary>
/// The page's steps before a widget's query, for a server that runs saved widgets itself (a scheduled dashboard email). Each mirrors
/// its twin in tamiza-ui (<c>indicators/widget-view.ts</c> and <c>indicators/dashboard-filters.ts</c>): change both together.
/// </summary>
public static class WidgetQuery
{
    /// <summary>
    /// The query a chart type actually uses, as <c>queryFor</c> builds it: settings that belong to another chart type are not sent,
    /// and a stat tile never groups. The API accepts a <c>groupBy</c> on a stat tile and a geometry column on any widget, and both
    /// change the rows, so a saved config is never sent as it is.
    /// </summary>
    public static WidgetConfigInput For(string chartType, WidgetConfig config) => new(
        config.Indicator,
        config.Ratio is null ? config.Measure : null,
        config.Ratio,
        chartType == ChartTypes.Stat ? null : config.GroupBy,
        config.Filters,
        chartType == ChartTypes.Map ? config.GeometryColumn : null,
        chartType == ChartTypes.Histogram ? config.Buckets : null,
        chartType is ChartTypes.Bar or ChartTypes.Column && config.Sort is { } sort ? SortFor(sort) : null,
        // A label and a format change nothing in the rows; the query needs only the columns.
        chartType == ChartTypes.Map ? config.Tooltip.Select(item => new WidgetTooltipItem(item.Column)).ToList() : [],
        null,
        []);

    /// <summary>Plotting the value is always by number, and which column it is makes no sense then.</summary>
    private static WidgetSort SortFor(WidgetSort sort) => sort.By == SortValues.ByValue ? sort with { Column = null, CompareAs = null } : sort;

    /// <summary>
    /// What the query is sent of the dashboard's filters when every filter is at its default, as <c>appliedOf</c> does for the
    /// filters in force: those with a default that the widget does not ignore.
    /// </summary>
    public static IReadOnlyList<AppliedDashboardFilter> AppliedDefaults(IReadOnlyList<DashboardFilter> filters, WidgetConfig config) =>
        filters
            .Where(f => f.Default is not null && !config.IgnoredDashboardFilters.Contains(f.Column))
            .Select(f => DashboardFilterKinds.TakesValues(f.Kind)
                ? new AppliedDashboardFilter(f.Column, f.Kind, Values: [.. f.Default!.Values ?? []])
                : new AppliedDashboardFilter(f.Column, f.Kind, Min: f.Default!.Min, Max: f.Default!.Max))
            .ToList();

    /// <summary>
    /// The labels, in the filter bar's order, of the filters in force that do not narrow the widget: those it ignores, and those whose
    /// column the answer lists in <paramref name="notFiltered"/> (null when nothing was sent).
    /// </summary>
    public static IReadOnlyList<string> NotNarrowedBy(IReadOnlyList<DashboardFilter> filters, WidgetConfig config, IReadOnlyList<string>? notFiltered) =>
        filters
            .Where(f => f.Default is not null
                        && (config.IgnoredDashboardFilters.Contains(f.Column) || (notFiltered?.Contains(f.Column) ?? false)))
            .Select(f => f.Label)
            .ToList();
}
