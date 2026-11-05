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
    [InlineData(ChartTypes.Boxplot)]
    public void Every_other_chart_type_has_no_order(string type) => Assert.Null(WidgetQuery.For(type, Config(ByColumn)).Sort);

    [Fact]
    public void A_box_plot_keeps_only_an_order_by_its_median_and_only_with_a_group_column()
    {
        var byMedian = new WidgetSort(SortValues.ByValue, "round", SortValues.Text, SortValues.Desc);

        Assert.Equal(new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc), WidgetQuery.For(ChartTypes.Boxplot, Config(byMedian)).Sort);
        Assert.Null(WidgetQuery.For(ChartTypes.Boxplot, Config(ByColumn)).Sort);
        Assert.Null(WidgetQuery.For(ChartTypes.Boxplot, Config(byMedian) with { GroupBy = null }).Sort);
        Assert.Null(WidgetQuery.For(ChartTypes.Boxplot, Config(byMedian)).SeriesBy);
        Assert.Equal("municipality", WidgetQuery.For(ChartTypes.Boxplot, Config(byMedian)).GroupBy);
    }

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
    [InlineData(ChartTypes.Boxplot)]
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

    private static readonly FilterInForce[] InForce =
    [
        new("municipality", "Municipality", Ignored: false),
        new("round", "Round", Ignored: true),
        new("visited_on", "Visit date", Ignored: false),
    ];

    [Fact]
    public void An_ignored_filter_in_force_is_not_narrowing_the_widget() =>
        Assert.Equal(["Round"], WidgetQuery.NotNarrowedBy(InForce, notFiltered: null));

    [Fact]
    public void A_filter_in_force_the_table_cannot_apply_is_named_too_in_the_bar_order() =>
        Assert.Equal(["Round", "Visit date"], WidgetQuery.NotNarrowedBy(InForce, notFiltered: ["visited_on"]));

    [Fact]
    public void A_filter_that_is_ignored_and_not_applied_is_named_once() =>
        Assert.Equal(["Round"], WidgetQuery.NotNarrowedBy(InForce, notFiltered: ["round"]));

    [Fact]
    public void With_no_filter_in_force_nothing_is_named() =>
        Assert.Empty(WidgetQuery.NotNarrowedBy([], notFiltered: ["round"]));

    private static WidgetConfig WithSeries() => new WidgetConfigInput(
        "population", "value", GroupBy: "age_band", SeriesBy: "sex", Layout: SeriesLayouts.Stacked, Pyramid: new WidgetPyramid("Female", "Male"),
        Scale: [new LikertStep("No", LikertSides.Negative), new LikertStep("Yes", LikertSides.Positive)]).Normalize();

    [Fact]
    public void Only_the_charts_that_have_a_series_send_the_series_column()
    {
        foreach (var chartType in ChartTypes.All)
        {
            var query = WidgetQuery.For(chartType, WithSeries());

            Assert.Equal(ChartTypes.HasSeries(chartType) ? "sex" : null, query.SeriesBy);
        }
    }

    [Fact]
    public void The_layout_goes_with_bars_and_columns_the_sides_with_the_pyramid_and_the_scale_with_the_likert_widget()
    {
        foreach (var chartType in ChartTypes.All)
        {
            var query = WidgetQuery.For(chartType, WithSeries());

            Assert.Equal(chartType is ChartTypes.Bar or ChartTypes.Column ? "stacked" : null, query.Layout);
            Assert.Equal(chartType == ChartTypes.Pyramid ? new WidgetPyramid("Female", "Male") : null, query.Pyramid);
            Assert.Equal(chartType == ChartTypes.Likert ? 2 : 0, query.Scale?.Count ?? 0);
        }
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Column_labels_never_change_a_query(string chartType)
    {
        var plain = Config();
        var labelled = plain with
        {
            Display = new WidgetDisplay(Labels: new Dictionary<string, string> { ["numerator"] = "Hogares que asistieron", ["municipality"] = "Municipio" }),
        };

        var query = WidgetQuery.For(chartType, labelled);

        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, plain), json), System.Text.Json.JsonSerializer.Serialize(query, json));
        Assert.Null(query.Display);
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void The_texts_of_a_widget_never_change_a_query(string chartType)
    {
        var plain = Config();
        var texts = plain with
        {
            Display = new WidgetDisplay(
                Color: 2, Labels: new Dictionary<string, string> { ["numerator"] = "Hogares que asistieron" }, Subtitle: "¿En qué lugares ha visto teatro?",
                Footnote: "No suman 100 %: respuesta múltiple.", Caption: new WidgetCaption("% de quienes asistieron"), Unit: "de quienes asistieron",
                ShowDescription: true),
        };

        var query = WidgetQuery.For(chartType, texts);

        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, plain), json), System.Text.Json.JsonSerializer.Serialize(query, json));
        Assert.Null(query.Display);
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void An_own_order_of_values_never_changes_a_query(string chartType)
    {
        var plain = Config();
        var ordered = plain with
        {
            Display = new WidgetDisplay(Color: 2, Order: new WidgetCategoryOrder(["Piped", "Well"], ["Sí", "No", "No sabe"])),
        };

        var query = WidgetQuery.For(chartType, ordered);

        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, plain), json), System.Text.Json.JsonSerializer.Serialize(query, json));
        Assert.Null(query.Display);
        Assert.DoesNotContain("order", System.Text.Json.JsonSerializer.Serialize(query, json));
    }

    public static TheoryData<string> EveryKind() => new(ChartTypes.All);

    [Fact]
    public void The_new_charts_keep_their_group_column_and_a_stat_tile_still_drops_it()
    {
        Assert.Equal("age_band", WidgetQuery.For(ChartTypes.Heatmap, WithSeries()).GroupBy);
        Assert.Equal("age_band", WidgetQuery.For(ChartTypes.Crosstab, WithSeries()).GroupBy);
        Assert.Null(WidgetQuery.For(ChartTypes.Stat, WithSeries()).GroupBy);
    }

    // The overall value is a flag of the query request, derived from the saved widget (design D4 of add-chart-annotations).

    private static WidgetConfig Lined(
        WidgetReferenceLine?[]? lines, WidgetRatio? ratio = null, string? seriesBy = null, string? measure = null, WidgetDisplay? display = null) => new(
        "coverage", measure, ratio, "municipality", [], null, null, SeriesBy: seriesBy,
        Display: display ?? (lines is null ? null : new WidgetDisplay(ReferenceLines: lines)));

    private static readonly WidgetRatio Rate = new("numerator", "denominator");
    private static readonly WidgetReferenceLine[] OverallLine = [new("overall")];

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    public void A_bar_a_column_and_a_line_with_no_series_a_ratio_and_an_overall_line_ask_for_the_overall_value(string chartType)
    {
        Assert.True(WidgetQuery.Overall(chartType, Lined(OverallLine, Rate)));
        Assert.True(WidgetQuery.Overall(chartType, Lined([new("mean"), new("value", 0.5, "Meta"), new("overall", Label: "General")], Rate)));
    }

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    public void A_measure_a_series_a_mean_line_only_or_no_display_do_not_ask_for_it(string chartType)
    {
        Assert.False(WidgetQuery.Overall(chartType, Lined(OverallLine, measure: "value")));
        Assert.False(WidgetQuery.Overall(chartType, Lined(OverallLine)));
        Assert.False(WidgetQuery.Overall(chartType, Lined(OverallLine, Rate, seriesBy: "water_source")));
        Assert.False(WidgetQuery.Overall(chartType, Lined([new("mean"), new("median"), new("value", 0.5)], Rate)));
        Assert.False(WidgetQuery.Overall(chartType, Lined(null, Rate)));
        Assert.False(WidgetQuery.Overall(chartType, Lined(null, Rate, display: new WidgetDisplay(Color: 2, DataLabels: true))));
        Assert.False(WidgetQuery.Overall(chartType, Lined([null, new(null, Label: "Meta")], Rate)));
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Pie)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Map)]
    [InlineData(ChartTypes.Heatmap)]
    [InlineData(ChartTypes.Pyramid)]
    [InlineData(ChartTypes.Likert)]
    [InlineData(ChartTypes.Crosstab)]
    [InlineData(ChartTypes.Boxplot)]
    public void Every_other_chart_type_never_asks_for_it(string chartType)
    {
        Assert.False(WidgetQuery.Overall(chartType, Lined(OverallLine, Rate)));
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void The_reference_lines_the_steps_and_the_data_labels_never_change_the_query(string chartType)
    {
        var plain = Lined(null, Rate, display: new WidgetDisplay(Color: 2));
        var annotated = Lined(null, Rate, display: new WidgetDisplay(Color: 2, ReferenceLines: [new("mean"), new("overall")], Interpolation: "step", DataLabels: true));

        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, plain), json),
            System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, annotated), json));
        Assert.DoesNotContain("overall", System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, annotated), json));
    }

    // Leaving out the values that mean no data is a query setting (design D6 of add-small-sample-warnings); twin of query-for.spec.ts.

    private static readonly string[] KindsThatTakeMissingFooter =
        [ChartTypes.Bar, ChartTypes.Column, ChartTypes.Line, ChartTypes.Pie, ChartTypes.Heatmap, ChartTypes.Pyramid, ChartTypes.Likert, ChartTypes.Crosstab];

    [Fact]
    public void The_kinds_that_take_missing_footer_are_the_eight_of_the_design()
    {
        Assert.Equal(
            ChartTypes.All.Where(KindsThatTakeMissingFooter.Contains),
            ChartTypes.All.Where(ChartTypes.TakesMissingFooter));
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Missing_footer_is_sent_only_when_it_is_set_and_the_kind_takes_it(string chartType)
    {
        var plain = Lined(null, Rate);
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var takes = KindsThatTakeMissingFooter.Contains(chartType);

        Assert.Null(WidgetQuery.For(chartType, plain).MissingFooter);
        Assert.DoesNotContain("missingFooter", System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, plain), json));
        Assert.Equal(takes ? true : null, WidgetQuery.For(chartType, plain with { MissingFooter = true }).MissingFooter);
        Assert.Equal(takes ? false : null, WidgetQuery.For(chartType, plain with { MissingFooter = false }).MissingFooter);
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void The_base_settings_of_the_display_never_change_the_query(string chartType)
    {
        var plain = Lined(null, Rate, display: new WidgetDisplay(Color: 2));
        var based = Lined(null, Rate, display: new WidgetDisplay(Color: 2, MinN: 30, ShowN: true));

        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, plain), json),
            System.Text.Json.JsonSerializer.Serialize(WidgetQuery.For(chartType, based), json));
    }
}
