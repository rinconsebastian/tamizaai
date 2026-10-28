using Tamiza.Api.CleanData;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>The same cases as tamiza-ui's <c>query-for.spec.ts</c> and <c>dashboard-filters</c>, for the server's twin of each step.</summary>
public sealed class WidgetQueryTests
{
    private static readonly WidgetSort ByColumn = new(SortValues.ByColumn, "round", SortValues.Number, SortValues.Asc);

    private static readonly WidgetTooltipItem[] Items =
    [
        new("address", "Dirección"),
        new("sample_target", null, TooltipFormats.Count),
        new("progress", "Avance", TooltipFormats.Percent),
    ];

    private static WidgetConfig Config(WidgetSort? sort = null, WidgetRatio? ratio = null, IReadOnlyList<string>? ignored = null) => new(
        "coverage", "value", ratio, "municipality", [new DataFilter("round", "in", Values: ["2"])], "geometry", 20, sort, Items,
        BasemapKinds.Satellite, ignored ?? ["round"]);

    [Fact]
    public void A_stat_tile_never_groups()
    {
        var query = WidgetQuery.For(ChartTypes.Stat, Config());

        Assert.Null(query.GroupBy);
        Assert.Equal("value", query.Measure);
        Assert.Null(query.GeometryColumn);
    }

    [Fact]
    public void A_bar_and_a_column_chart_keep_their_order()
    {
        Assert.Equal(ByColumn, WidgetQuery.For(ChartTypes.Bar, Config(ByColumn)).Sort);
        Assert.Equal(ByColumn, WidgetQuery.For(ChartTypes.Column, Config(ByColumn)).Sort);
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Pie)]
    [InlineData(ChartTypes.Map)]
    public void Every_other_chart_type_has_no_order(string type) => Assert.Null(WidgetQuery.For(type, Config(ByColumn)).Sort);

    [Fact]
    public void Sorting_by_the_value_sends_no_column_or_comparison()
    {
        var sort = new WidgetSort(SortValues.ByValue, "round", SortValues.Text, SortValues.Desc);

        Assert.Equal(new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc), WidgetQuery.For(ChartTypes.Column, Config(sort)).Sort);
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Pie)]
    public void Only_a_map_keeps_its_geometry_and_tooltip_columns(string type)
    {
        var query = WidgetQuery.For(type, Config());

        Assert.Null(query.GeometryColumn);
        Assert.Empty(query.Tooltip!);
        Assert.Null(query.Basemap);
    }

    [Fact]
    public void A_map_sends_the_names_of_its_tooltip_columns_in_order_and_never_their_labels_formats_or_basemap()
    {
        var query = WidgetQuery.For(ChartTypes.Map, Config());

        Assert.Equal("geometry", query.GeometryColumn);
        Assert.Equal([new("address"), new("sample_target"), new("progress")], query.Tooltip!);
        Assert.Null(query.Basemap);
    }

    [Fact]
    public void A_ratio_drops_the_measure()
    {
        var query = WidgetQuery.For(ChartTypes.Bar, Config(ratio: new WidgetRatio("numerator", "denominator")));

        Assert.Null(query.Measure);
        Assert.Equal(new WidgetRatio("numerator", "denominator"), query.Ratio);
    }

    [Fact]
    public void Buckets_are_kept_only_for_a_histogram()
    {
        Assert.Equal(20, WidgetQuery.For(ChartTypes.Histogram, Config()).Buckets);
        Assert.Null(WidgetQuery.For(ChartTypes.Bar, Config()).Buckets);
    }

    [Fact]
    public void The_widget_filters_go_with_the_query_and_the_ignored_dashboard_filters_do_not()
    {
        var config = Config();
        var query = WidgetQuery.For(ChartTypes.Bar, config);

        Assert.Same(config.Filters, query.Filters);
        Assert.Empty(query.IgnoredDashboardFilters!);
    }

    private static readonly DashboardFilter[] Filters =
    [
        new("municipality", "Municipality", DashboardFilterKinds.Text, new DashboardFilterValue(["Cali", "Pasto"])),
        new("round", "Round", DashboardFilterKinds.Number, new DashboardFilterValue(Min: "3", Max: "3")),
        new("visited_on", "Visit date", DashboardFilterKinds.Date, new DashboardFilterValue(Min: "2026-10-01", Max: "2026-10-31")),
        new("urban", "Urban", DashboardFilterKinds.Boolean, null),
    ];

    [Fact]
    public void Default_applied()
    {
        var applied = WidgetQuery.AppliedDefaults(Filters, Config(ignored: []));

        Assert.Equal(3, applied.Count);
        Assert.Equal(["Cali", "Pasto"], applied[0].Values!);
        Assert.Equal(("round", DashboardFilterKinds.Number, "3", "3"), (applied[1].Column, applied[1].Kind, applied[1].Min, applied[1].Max));
        Assert.Null(applied[1].Values);
    }

    [Fact]
    public void Fixed_date_range()
    {
        var date = WidgetQuery.AppliedDefaults(Filters, Config(ignored: [])).Single(f => f.Column == "visited_on");

        Assert.Equal(("2026-10-01", "2026-10-31"), (date.Min, date.Max));
    }

    [Fact]
    public void A_filter_without_a_default_narrows_nothing() =>
        Assert.DoesNotContain(WidgetQuery.AppliedDefaults(Filters, Config(ignored: [])), f => f.Column == "urban");

    [Fact]
    public void Ignored_filter_is_not_sent_and_is_named_as_not_narrowing()
    {
        var config = Config(ignored: ["round"]);

        Assert.DoesNotContain(WidgetQuery.AppliedDefaults(Filters, config), f => f.Column == "round");
        Assert.Equal(["Round"], WidgetQuery.NotNarrowedBy(Filters, config, notFiltered: null));
    }

    [Fact]
    public void A_filter_the_table_cannot_apply_is_named_too_in_the_bar_order()
    {
        var notNarrowed = WidgetQuery.NotNarrowedBy(Filters, Config(ignored: ["visited_on"]), notFiltered: ["municipality"]);

        Assert.Equal(["Municipality", "Visit date"], notNarrowed);
    }

    [Fact]
    public void Filters_without_a_default_are_never_named() =>
        Assert.Empty(WidgetQuery.NotNarrowedBy(Filters, Config(ignored: ["urban"]), notFiltered: ["urban"]));
}
