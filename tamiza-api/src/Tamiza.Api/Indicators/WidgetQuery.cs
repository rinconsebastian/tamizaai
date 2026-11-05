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
    /// change the rows, so a saved config is never sent as it is. <c>missingFooter</c> is sent only when it is set and the kind takes it
    /// (design D6 of add-small-sample-warnings), so the query of every widget without it is the one it was.
    /// </summary>
    public static WidgetConfigInput For(string chartType, WidgetConfig config) => new(
        config.Indicator,
        config.Ratio is null ? config.Measure : null,
        config.Ratio,
        chartType == ChartTypes.Stat ? null : config.GroupBy,
        config.Filters,
        chartType == ChartTypes.Map ? config.GeometryColumn : null,
        chartType == ChartTypes.Histogram ? config.Buckets : null,
        SortOf(chartType, config),
        // A label and a format change nothing in the rows; the query needs only the columns.
        chartType == ChartTypes.Map ? config.Tooltip.Select(item => new WidgetTooltipItem(item.Column)).ToList() : [],
        null,
        [],
        ChartTypes.HasSeries(chartType) ? config.SeriesBy : null,
        chartType is ChartTypes.Bar or ChartTypes.Column ? config.Layout : null,
        chartType == ChartTypes.Pyramid ? config.Pyramid : null,
        chartType == ChartTypes.Likert ? config.Scale : [],
        MissingFooter: ChartTypes.TakesMissingFooter(chartType) ? config.MissingFooter : null);

    /// <summary>
    /// Whether a saved widget asks its query for the overall value too (design D4 of add-chart-annotations): a bar, column or line with
    /// no series and a ratio, whose display has an <c>overall</c> reference line. It is a flag of the query request, derived here and never
    /// stored in a config. A widget with a series reads its overall line from its totals, and a measure has no overall line. Twin of
    /// <c>overallFor</c> in <c>widget-view.ts</c>: change both together.
    /// </summary>
    public static bool Overall(string chartType, WidgetConfig config) =>
        chartType is ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line
        && config.SeriesBy is null
        && config.Ratio is not null
        && config.Display?.ReferenceLines is { } lines
        && lines.Any(line => line?.Kind == ReferenceLineKinds.Overall);

    /// <summary>
    /// A bar or column keeps its order. A box plot keeps only an order by median, and only with a group column: an order the form keeps
    /// while the group is cleared is not sent, so the query is not refused for it. Twin of the sort rule of <c>queryFor</c>.
    /// </summary>
    private static WidgetSort? SortOf(string chartType, WidgetConfig config) => chartType switch
    {
        ChartTypes.Bar or ChartTypes.Column => config.Sort is { } sort ? SortFor(sort) : null,
        ChartTypes.Boxplot => config.GroupBy is not null && config.Sort is { By: SortValues.ByValue } sort ? SortFor(sort) : null,
        _ => null,
    };

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

    /// <summary>
    /// The labels, in the filter bar's order, of the filters in force on a member's screen that do not narrow the widget: those it
    /// ignores, and those whose column the answer lists in <paramref name="notFiltered"/> (null when nothing was sent). Twin of
    /// <c>notFiltered</c> in <c>widget-view.ts</c>, which reads the filters in force, ignored ones included (design D12 of
    /// add-widget-export).
    /// </summary>
    public static IReadOnlyList<string> NotNarrowedBy(IReadOnlyList<FilterInForce> inForce, IReadOnlyList<string>? notFiltered) =>
        inForce
            .Where(f => f.Ignored || (notFiltered?.Contains(f.Column) ?? false))
            .Select(f => f.Label)
            .ToList();
}

/// <summary>A dashboard filter with a value in force on a member's screen, named as the dashboard stores it; <see cref="Ignored"/> when the widget ignores it.</summary>
public sealed record FilterInForce(string Column, string Label, bool Ignored);
