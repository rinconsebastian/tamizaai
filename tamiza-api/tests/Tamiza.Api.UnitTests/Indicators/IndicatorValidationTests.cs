using Tamiza.Api.CleanData;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class IndicatorValidationTests
{
    private static readonly TableColumn[] Columns =
    [
        new("indicator", DataColumnType.Text, null),
        new("municipality", DataColumnType.Text, null),
        new("value", DataColumnType.Decimal, null),
        new("numerator", DataColumnType.Decimal, null),
        new("denominator", DataColumnType.Decimal, null),
        new("year", DataColumnType.Integer, null),
        new("geometry", DataColumnType.Text, null),
    ];

    private static WidgetConfig Config(string indicator = "coverage", string? groupBy = "municipality") =>
        new WidgetConfigInput(indicator, GroupBy: groupBy).Normalize();

    private static IDictionary<string, string[]> Check(WidgetConfig config, string? chartType)
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckConfig(config, chartType, "config.", errors);
        return errors.ToDictionary();
    }

    // Defaults

    [Fact]
    public void A_config_without_anything_takes_the_defaults()
    {
        var config = new WidgetConfigInput().Normalize();

        Assert.Equal("", config.Indicator);
        Assert.Null(config.Measure);
        Assert.Null(config.Ratio);
        Assert.Null(config.GroupBy);
        Assert.Empty(config.Filters);
        Assert.Null(config.GeometryColumn);
        Assert.Null(config.Buckets);
        Assert.Null(config.Sort);
        Assert.Empty(config.Tooltip);
        Assert.Null(config.Basemap);
    }

    [Fact]
    public void Empty_text_counts_as_left_out()
    {
        var config = new WidgetConfigInput("a", Measure: "", GroupBy: "", GeometryColumn: "").Normalize();

        Assert.Null(config.Measure);
        Assert.Null(config.GroupBy);
        Assert.Null(config.GeometryColumn);
    }

    [Theory]
    [InlineData("stat", 3, 2)]
    [InlineData("bar", 6, 4)]
    [InlineData("column", 6, 4)]
    [InlineData("line", 6, 4)]
    [InlineData("histogram", 6, 4)]
    [InlineData("pie", 6, 4)]
    [InlineData("map", 6, 6)]
    [InlineData("heatmap", 6, 5)]
    [InlineData("pyramid", 6, 5)]
    [InlineData("crosstab", 6, 5)]
    [InlineData("likert", 6, 4)]
    [InlineData("boxplot", 6, 4)]
    public void Each_chart_type_has_a_default_size(string chartType, int w, int h) =>
        Assert.Equal((w, h), IndicatorValidation.DefaultSize(chartType));

    // The indicator

    [Fact]
    public void No_indicator_is_always_an_error()
    {
        foreach (var chartType in new string?[] { "stat", "bar", null })
        {
            Assert.Equal(["Choose an indicator."], Check(Config(indicator: ""), chartType)["config.indicator"]);
        }
    }

    [Theory]
    [InlineData("Piped water")]
    [InlineData("1rate")]
    [InlineData("a-b")]
    public void An_indicator_that_is_not_a_valid_name_is_an_error(string name) =>
        Assert.Contains("config.indicator", Check(Config(indicator: name), "bar").Keys);

    [Fact]
    public void An_overlong_indicator_name_is_an_error()
    {
        Assert.Empty(Check(Config(indicator: new string('x', 63)), "bar"));
        Assert.Contains("config.indicator", Check(Config(indicator: new string('x', 64)), "bar").Keys);
    }

    // The group column per chart type (design D6)

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("pie")]
    public void Charts_that_group_need_a_group_column(string chartType)
    {
        Assert.Equal([$"A {chartType} widget needs a column to group by."], Check(Config(groupBy: null), chartType)["config.groupBy"]);
        Assert.Empty(Check(Config(), chartType));
    }

    [Fact]
    public void A_map_needs_a_group_column_and_a_geometry_column()
    {
        var errors = Check(Config(groupBy: null), "map");

        Assert.Contains("config.groupBy", errors.Keys);
        Assert.Contains("config.geometryColumn", errors.Keys);
        Assert.Empty(Check(WithGeometry(Config()), "map"));
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("histogram")]
    public void A_stat_tile_and_a_histogram_may_have_no_group_column(string chartType) =>
        Assert.Empty(Check(Config(groupBy: null), chartType));

    [Fact]
    public void Without_a_chart_type_as_in_the_query_endpoint_the_group_is_not_required() =>
        Assert.Empty(Check(Config(groupBy: null), chartType: null));

    [Fact]
    public void Only_a_map_needs_a_geometry_column() =>
        Assert.Empty(Check(Config(), "bar"));

    // Measure and ratio

    [Fact]
    public void A_measure_or_a_ratio_is_valid_but_not_both()
    {
        var measure = new WidgetConfigInput("a", Measure: "numerator", GroupBy: "g").Normalize();
        var ratio = new WidgetConfigInput("a", Ratio: new WidgetRatio("numerator", "denominator"), GroupBy: "g").Normalize();
        var both = new WidgetConfigInput("a", Measure: "value", Ratio: new WidgetRatio("numerator", "denominator"), GroupBy: "g").Normalize();

        Assert.Empty(Check(measure, "bar"));
        Assert.Empty(Check(ratio, "bar"));
        Assert.Equal(["Plot a measure or a ratio, not both."], Check(both, "bar")["config.measure"]);
    }

    [Fact]
    public void A_ratio_needs_both_columns()
    {
        var config = new WidgetConfigInput("a", Ratio: new WidgetRatio("", ""), GroupBy: "g").Normalize();

        var errors = Check(config, "bar");

        Assert.Contains("config.ratio.numerator", errors.Keys);
        Assert.Contains("config.ratio.denominator", errors.Keys);
    }

    [Fact]
    public void Column_names_are_at_most_the_query_text_length()
    {
        var tooLong = new string('c', DataQueryBuilder.MaxTextLength + 1);

        Assert.Contains("config.measure", Check(new WidgetConfigInput("a", Measure: tooLong, GroupBy: "g").Normalize(), "bar").Keys);
        Assert.Contains("config.groupBy", Check(new WidgetConfigInput("a", GroupBy: tooLong).Normalize(), "bar").Keys);
        Assert.Contains("config.ratio.numerator",
            Check(new WidgetConfigInput("a", Ratio: new WidgetRatio(tooLong, "d"), GroupBy: "g").Normalize(), "bar").Keys);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-3, false)]
    [InlineData(101, false)]
    public void Buckets_are_between_1_and_100(int buckets, bool valid)
    {
        var config = new WidgetConfigInput("a", Buckets: buckets).Normalize();

        Assert.Equal(valid, Check(config, "histogram").Count == 0);
    }

    // Filters

    [Fact]
    public void A_filter_needs_a_column_and_a_known_operator()
    {
        var config = new WidgetConfigInput("a", Filters: [new DataFilter("", "equals", "x"), new DataFilter("year", "like", "x"), null!]).Normalize();

        var errors = Check(config, "stat");

        Assert.Equal(["config.filters[0]", "config.filters[1]", "config.filters[2]"], errors.Keys.Order());
    }

    [Fact]
    public void At_most_50_filters_and_100_values_per_filter()
    {
        var many = Enumerable.Range(0, 51).Select(i => new DataFilter("c" + i, "empty")).ToArray();
        var values = new DataFilter("c", "in", Values: Enumerable.Range(0, 101).Select(i => "v" + i).ToArray());

        Assert.Contains("config.filters", Check(new WidgetConfigInput("a", Filters: many).Normalize(), "stat").Keys);
        Assert.Contains("config.filters[0]", Check(new WidgetConfigInput("a", Filters: [values]).Normalize(), "stat").Keys);
    }

    // Against the table

    private static IDictionary<string, string[]> CheckTable(WidgetConfig config)
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckAgainstColumns(config, Columns, "UTC", "config.", errors);
        return errors.ToDictionary();
    }

    [Fact]
    public void Group_geometry_and_filter_columns_must_be_in_the_table_and_are_named()
    {
        var config = new WidgetConfigInput("a", GroupBy: "round", GeometryColumn: "shape", Filters: [new DataFilter("region", "empty")]).Normalize();

        var errors = CheckTable(config);

        Assert.Contains("'round'", errors["config.groupBy"][0]);
        Assert.Contains("'shape'", errors["config.geometryColumn"][0]);
        Assert.Contains("'region'", errors["config.filters[0]"][0]);
    }

    [Fact]
    public void Measure_and_ratio_columns_must_be_numeric_columns_of_the_table()
    {
        var config = new WidgetConfigInput("a", Ratio: new WidgetRatio("municipality", "missing")).Normalize();
        var measure = new WidgetConfigInput("a", Measure: "geometry").Normalize();

        var errors = CheckTable(config);

        Assert.Contains("is not a number", errors["config.ratio.numerator"][0]);
        Assert.Contains("'missing'", errors["config.ratio.denominator"][0]);
        Assert.Contains("is not a number", CheckTable(measure)["config.measure"][0]);
        Assert.Empty(CheckTable(new WidgetConfigInput("a", Ratio: new WidgetRatio("numerator", "denominator")).Normalize()));
    }

    [Fact]
    public void A_config_that_matches_the_table_has_no_errors()
    {
        var config = new WidgetConfigInput("a", GroupBy: "year", GeometryColumn: "geometry",
            Filters: [new DataFilter("year", "range", Min: "2020", Max: "2026"), new DataFilter("municipality", "in", Values: ["Cali"])]).Normalize();

        Assert.Empty(CheckTable(config));
    }

    [Fact]
    public void A_filter_that_does_not_fit_its_columns_type_is_an_error()
    {
        var config = new WidgetConfigInput("a", Filters: [new DataFilter("municipality", "range", Min: "1")]).Normalize();

        Assert.Contains("does not support the filter 'range'", CheckTable(config)["config.filters[0]"][0]);
    }

    // Layout, title, chart type

    private static IDictionary<string, string[]> CheckLayout(WidgetLayout? layout)
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckLayout(layout, errors);
        return errors.ToDictionary();
    }

    [Theory]
    [InlineData(0, 0, 12, 1)]
    [InlineData(11, 0, 1, 40)]
    [InlineData(6, 99, 6, 4)]
    public void A_layout_inside_the_grid_is_valid(int x, int y, int w, int h) => Assert.Empty(CheckLayout(new WidgetLayout(x, y, w, h)));

    [Theory]
    [InlineData(-1, 0, 1, 1, "layout.x")]
    [InlineData(0, -1, 1, 1, "layout.y")]
    [InlineData(0, 0, 0, 1, "layout.w")]
    [InlineData(0, 0, 13, 1, "layout.w")]
    [InlineData(7, 0, 6, 1, "layout.w")]
    [InlineData(0, 0, 1, 0, "layout.h")]
    [InlineData(0, 0, 1, 41, "layout.h")]
    public void A_layout_outside_the_grid_names_the_field(int x, int y, int w, int h, string field) =>
        Assert.Contains(field, CheckLayout(new WidgetLayout(x, y, w, h)).Keys);

    [Fact]
    public void A_layout_with_huge_numbers_does_not_overflow() =>
        Assert.Contains("layout.w", CheckLayout(new WidgetLayout(int.MaxValue, 0, int.MaxValue, 1)).Keys);

    [Fact]
    public void A_layout_of_several_widgets_names_the_entry_of_each_error()
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckLayout(new WidgetLayout(-1, 0, 1, 41), errors, "widgets[2].");
        IndicatorValidation.CheckLayout(null, errors, "widgets[3].");

        Assert.Equal(["widgets[2].layout.x", "widgets[2].layout.h", "widgets[3].layout"], errors.ToDictionary().Keys);
        Assert.Equal(CheckLayout(new WidgetLayout(-1, 0, 1, 41))["layout.h"], errors.ToDictionary()["widgets[2].layout.h"]);
    }

    [Fact]
    public void The_title_is_required_and_at_most_200_characters()
    {
        string[] Title(string? title)
        {
            var errors = new FieldErrors();
            IndicatorValidation.CheckTitle(title, errors);
            return errors.Any ? errors.ToDictionary()["title"] : [];
        }

        Assert.NotEmpty(Title(null));
        Assert.NotEmpty(Title("   "));
        Assert.Empty(Title(new string('t', 200)));
        Assert.NotEmpty(Title(new string('t', 201)));
        Assert.Empty(Title("  " + new string('t', 200) + "  ")); // trimmed first, as the endpoint stores it
    }

    [Theory]
    [InlineData("stat", true)]
    [InlineData("map", true)]
    [InlineData("radar", false)]
    [InlineData("Bar", false)]
    [InlineData(null, false)]
    public void The_chart_type_is_one_of_seven(string? chartType, bool valid)
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckChartType(chartType, errors);

        Assert.Equal(valid, !errors.Any);
    }

    private static WidgetConfig WithGeometry(WidgetConfig config) => config with { GeometryColumn = "geometry" };

    // Order

    private static WidgetConfig Sorted(WidgetSort? sort, string? groupBy = "municipality") =>
        new WidgetConfigInput("a", GroupBy: groupBy, Sort: sort).Normalize();

    private static readonly WidgetSort ByColumn = new("column", "round", "number", "asc");

    [Fact]
    public void A_sort_with_empty_text_fields_takes_them_as_left_out()
    {
        var config = Sorted(new WidgetSort("value", "", "", "desc"));

        Assert.Null(config.Sort!.Column);
        Assert.Null(config.Sort.CompareAs);
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    public void Bar_and_column_widgets_can_be_sorted(string chartType)
    {
        Assert.Empty(Check(Sorted(ByColumn), chartType));
        Assert.Empty(Check(Sorted(new WidgetSort("value", null, null, "desc")), chartType));
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("line")]
    [InlineData("histogram")]
    [InlineData("pie")]
    [InlineData("map")]
    public void Other_widgets_cannot_be_sorted(string chartType)
    {
        var errors = Check(new WidgetConfigInput("a", GroupBy: "g", GeometryColumn: "geometry", Sort: ByColumn).Normalize(), chartType);

        Assert.Equal("Only bar, column and box plot widgets can be sorted.", errors["config.sort"][0]);
    }

    [Fact]
    public void A_sort_needs_a_group_column()
    {
        Assert.Equal("Sorting needs a column to group by.", Check(Sorted(ByColumn, groupBy: null), null)["config.sort"][0]);
    }

    [Fact]
    public void A_bad_sort_field_is_named_with_its_message()
    {
        Assert.Equal("Sort by 'value' or by 'column'.", Check(Sorted(new WidgetSort("size", null, null, "asc")), "bar")["config.sort.by"][0]);
        Assert.Equal("The direction must be 'asc' or 'desc'.", Check(Sorted(new WidgetSort("value", null, null, null)), "bar")["config.sort.direction"][0]);
        Assert.Equal("The direction must be 'asc' or 'desc'.", Check(Sorted(new WidgetSort("value", null, null, "up")), "bar")["config.sort.direction"][0]);
        Assert.Equal("Compare as 'text' or as 'number'.", Check(Sorted(new WidgetSort("column", "round", "date", "asc")), "bar")["config.sort.compareAs"][0]);
    }

    [Fact]
    public void A_sort_by_column_needs_a_column_and_a_sort_by_value_cannot_have_one()
    {
        Assert.Equal("Choose a column to sort by.", Check(Sorted(new WidgetSort("column", null, null, "asc")), "bar")["config.sort.column"][0]);
        Assert.Equal("Sort by the plotted value or by a column, not both.",
            Check(Sorted(new WidgetSort("value", "round", null, "asc")), "bar")["config.sort.column"][0]);
        Assert.Equal("The plotted value is always compared as a number.",
            Check(Sorted(new WidgetSort("value", null, "text", "asc")), "bar")["config.sort.compareAs"][0]);
    }

    [Fact]
    public void A_sort_column_name_has_the_column_name_limit()
    {
        var tooLong = new string('x', DataQueryBuilder.MaxTextLength + 1);

        Assert.Contains("config.sort.column", Check(Sorted(new WidgetSort("column", tooLong, null, "asc")), "bar").Keys);
    }

    [Fact]
    public void The_sort_column_must_be_in_the_table_and_is_named()
    {
        var errors = CheckTable(Sorted(new WidgetSort("column", "missing", null, "asc")));

        Assert.Equal("The table has no column 'missing'.", errors["config.sort.column"][0]);
        Assert.Empty(CheckTable(Sorted(ByColumn with { Column = "year" })));
        Assert.Empty(CheckTable(Sorted(new WidgetSort("value", null, null, "desc"))));
    }

    [Theory]
    [InlineData(DataColumnType.Date, "a date column")]
    [InlineData(DataColumnType.Timestamp, "a timestamp column")]
    [InlineData(DataColumnType.Boolean, "a true/false column")]
    public void A_date_timestamp_or_true_false_column_cannot_be_compared_as_text_or_number(DataColumnType type, string words)
    {
        TableColumn[] columns = [new("municipality", DataColumnType.Text, null), new("seen", type, null)];
        var config = Sorted(new WidgetSort("column", "seen", "number", "asc"));
        var errors = new FieldErrors();

        IndicatorValidation.CheckAgainstColumns(config, columns, "UTC", "config.", errors);

        Assert.Equal(
            $"Column 'seen' is {words}, so it is sorted in its own order and cannot be compared as text or as a number.",
            errors.ToDictionary()["config.sort.column"][0]);
        var own = new FieldErrors();
        IndicatorValidation.CheckAgainstColumns(Sorted(new WidgetSort("column", "seen", null, "asc")), columns, "UTC", "config.", own);
        Assert.Empty(own.ToDictionary());
    }

    // Tooltip columns and basemap (map only)

    private static WidgetConfig Mapped(IReadOnlyList<WidgetTooltipItem>? tooltip = null, string? basemap = null) =>
        new WidgetConfigInput("a", GroupBy: "municipality", GeometryColumn: "geometry", Tooltip: tooltip, Basemap: basemap).Normalize();

    private static WidgetTooltipItem Item(string column = "year", string? label = null, string? format = null) => new(column, label, format);

    [Fact]
    public void Tooltip_labels_and_formats_are_trimmed_and_blank_ones_are_left_out()
    {
        var config = Mapped([Item("year", "  Year  ", " "), Item("value", "", "")]);

        Assert.Equal("Year", config.Tooltip[0].Label);
        Assert.Null(config.Tooltip[0].Format);
        Assert.Null(config.Tooltip[1].Label);
        Assert.Null(config.Tooltip[1].Format);
        Assert.Null(new WidgetConfigInput("a", Basemap: "").Normalize().Basemap);
    }

    [Fact]
    public void A_null_tooltip_item_is_kept_so_that_it_can_be_named()
    {
        var config = Mapped([Item(), null!]);

        Assert.Equal(2, config.Tooltip.Count);
        Assert.Null(config.Tooltip[1]);
        Assert.Equal(["A tooltip column needs a column."], Check(config, "map")["config.tooltip[1]"]);
    }

    [Fact]
    public void A_map_may_have_up_to_five_tooltip_columns_and_a_basemap()
    {
        var five = Enumerable.Range(0, 5).Select(i => Item($"c{i}")).ToList();

        Assert.Empty(Check(Mapped(five, "satellite"), "map"));
        Assert.Empty(Check(Mapped([], "streets"), "map"));
        Assert.Empty(Check(Mapped(five), null));
    }

    [Fact]
    public void More_than_five_tooltip_columns_is_an_error_on_the_tooltip()
    {
        var six = Enumerable.Range(0, 6).Select(i => Item($"c{i}")).ToList();

        Assert.Equal(["A map has at most 5 tooltip columns."], Check(Mapped(six), "map")["config.tooltip"]);
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("pie")]
    [InlineData("stat")]
    [InlineData("histogram")]
    public void Only_maps_can_have_tooltip_columns_or_a_basemap(string chartType)
    {
        var errors = Check(Mapped([Item()], "streets") with { GeometryColumn = null }, chartType);

        Assert.Equal(["Only map widgets can have tooltip columns."], errors["config.tooltip"]);
        Assert.Equal(["Only map widgets can have a basemap."], errors["config.basemap"]);
    }

    [Fact]
    public void A_tooltip_column_needs_a_name_that_is_not_too_long_a_repeat_or_the_geometry_column()
    {
        var tooLong = new string('x', DataQueryBuilder.MaxTextLength + 1);

        Assert.Equal(["Choose the tooltip column."], Check(Mapped([Item("")]), "map")["config.tooltip[0].column"]);
        Assert.Equal([$"The tooltip column is longer than {DataQueryBuilder.MaxTextLength} characters."], Check(Mapped([Item(tooLong)]), "map")["config.tooltip[0].column"]);
        Assert.Equal(["Column 'year' is already a tooltip column."], Check(Mapped([Item("year"), Item("year", "Again")]), "map")["config.tooltip[1].column"]);
        Assert.Equal(["The geometry column cannot be a tooltip column."], Check(Mapped([Item("geometry")]), "map")["config.tooltip[0].column"]);
    }

    [Fact]
    public void A_tooltip_label_has_at_most_eighty_characters()
    {
        Assert.Empty(Check(Mapped([Item(label: new string('x', 80))]), "map"));
        Assert.Equal(["The label is longer than 80 characters."], Check(Mapped([Item(label: new string('x', 81))]), "map")["config.tooltip[0].label"]);
    }

    [Theory]
    [InlineData("count", true)]
    [InlineData("decimal", true)]
    [InlineData("percent", true)]
    [InlineData("currency", false)]
    [InlineData("Percent", false)]
    public void A_tooltip_format_is_count_decimal_or_percent(string format, bool valid)
    {
        var errors = Check(Mapped([Item(format: format)]), "map");

        Assert.Equal(valid, !errors.ContainsKey("config.tooltip[0].format"));
        if (!valid)
        {
            Assert.Equal(["The format must be one of count, decimal, percent."], errors["config.tooltip[0].format"]);
        }
    }

    [Theory]
    [InlineData("satellite", true)]
    [InlineData("streets", true)]
    [InlineData("hybrid", false)]
    [InlineData("Streets", false)]
    public void A_basemap_is_streets_or_satellite(string basemap, bool valid)
    {
        var errors = Check(Mapped(basemap: basemap), "map");

        Assert.Equal(valid, !errors.ContainsKey("config.basemap"));
        if (!valid)
        {
            Assert.Equal(["The basemap must be 'streets' or 'satellite'."], errors["config.basemap"]);
        }
    }

    [Fact]
    public void A_basemap_is_not_checked_against_what_the_deployment_offers()
    {
        // No provider is configured in this test: the saved kind is only a kind.
        Assert.Empty(Check(Mapped(basemap: "satellite"), "map"));
    }

    [Fact]
    public void Tooltip_columns_must_be_in_the_table_and_are_named()
    {
        var errors = CheckTable(Mapped([Item("year"), Item("address"), Item("round", "Round")]));

        Assert.Equal(["The table has no column 'address'."], errors["config.tooltip[1].column"]);
        Assert.Equal(["The table has no column 'round'."], errors["config.tooltip[2].column"]);
        Assert.DoesNotContain("config.tooltip[0].column", errors.Keys);
    }

    [Fact]
    public void Only_a_number_column_can_have_a_format()
    {
        var errors = CheckTable(Mapped([Item("municipality", format: "percent"), Item("year", format: "count"), Item("value", format: "percent")]));

        Assert.Equal(["Only a number column can have a format."], errors["config.tooltip[0].format"]);
        Assert.DoesNotContain("config.tooltip[1].format", errors.Keys);
        Assert.DoesNotContain("config.tooltip[2].format", errors.Keys);
    }

    // The dashboard filters a widget ignores (design D6)

    private static IDictionary<string, string[]> CheckIgnored(params string[] columns) =>
        Check(new WidgetConfigInput("coverage", GroupBy: "municipality", IgnoredDashboardFilters: columns).Normalize(), "bar");

    [Fact]
    public void A_widget_ignores_no_dashboard_filters_by_default() =>
        Assert.Empty(new WidgetConfigInput("coverage").Normalize().IgnoredDashboardFilters);

    [Fact]
    public void A_widget_ignores_up_to_eight_dashboard_filters()
    {
        Assert.Empty(CheckIgnored([.. Enumerable.Range(0, 8).Select(i => $"c{i}")]));
        Assert.Equal(
            ["A widget can ignore at most 8 dashboard filters."],
            CheckIgnored([.. Enumerable.Range(0, 9).Select(i => $"c{i}")])["config.ignoredDashboardFilters"]);
    }

    [Fact]
    public void An_ignored_filter_is_named_by_a_published_column_once()
    {
        Assert.Equal(["The filter on column 'round' is already ignored."], CheckIgnored("round", "round")["config.ignoredDashboardFilters[1]"]);
        Assert.Equal(["'Municipality' is not a valid column name."], CheckIgnored("Municipality")["config.ignoredDashboardFilters[0]"]);
        Assert.Empty(CheckIgnored("_version", "municipality"));
    }

    // A query's dashboard filters

    private static IDictionary<string, string[]> CheckDashboard(params AppliedDashboardFilter?[] filters)
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckDashboardFilters(filters!, errors);
        return errors.ToDictionary();
    }

    [Fact]
    public void No_dashboard_filters_or_eight_valid_ones_pass()
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckDashboardFilters(null, errors);
        Assert.False(errors.Any);

        Assert.Empty(CheckDashboard([.. Enumerable.Range(0, 8).Select(i => new AppliedDashboardFilter($"c{i}", DashboardFilterKinds.Text, Values: ["x"]))]));
    }

    [Fact]
    public void Nine_dashboard_filters_are_refused() =>
        Assert.Contains("dashboardFilters",
            CheckDashboard([.. Enumerable.Range(0, 9).Select(i => new AppliedDashboardFilter($"c{i}", DashboardFilterKinds.Text, Values: ["x"]))]).Keys);

    [Theory]
    [InlineData(null)]
    [InlineData("Municipality")]
    [InlineData("1round")]
    [InlineData("round\n")]
    public void A_dashboard_filter_column_must_be_a_published_column_name(string? column) =>
        Assert.Contains("dashboardFilters[0]", CheckDashboard(new AppliedDashboardFilter(column, DashboardFilterKinds.Text, Values: ["x"])).Keys);

    [Fact]
    public void A_dashboard_filter_on_kobos_own_column_is_accepted() =>
        Assert.Empty(CheckDashboard(new AppliedDashboardFilter("_version", DashboardFilterKinds.Text, Values: ["v1"])));

    [Fact]
    public void Two_dashboard_filters_on_one_column_an_unknown_kind_and_a_bad_value_are_refused_at_their_index()
    {
        var errors = CheckDashboard(
            new AppliedDashboardFilter("round", DashboardFilterKinds.Number, Min: "1"),
            new AppliedDashboardFilter("round", DashboardFilterKinds.Number, Min: "2"),
            new AppliedDashboardFilter("zone", "color", Values: ["red"]),
            new AppliedDashboardFilter("visit", DashboardFilterKinds.Date, Min: "second"),
            null);

        Assert.Equal(["dashboardFilters[1]", "dashboardFilters[2]", "dashboardFilters[3]", "dashboardFilters[4]"], errors.Keys.Order());
        Assert.Contains("'visit'", errors["dashboardFilters[3]"].Single());
    }

    // Every chart type

    [Fact]
    public void There_are_twelve_chart_types_and_each_has_a_default_size()
    {
        Assert.Equal("boxplot", ChartTypes.All[^1]);
        Assert.All(ChartTypes.All, chartType => Assert.True(IndicatorValidation.DefaultSize(chartType).W > 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndicatorValidation.DefaultSize("radar"));
    }

    [Fact]
    public void Every_chart_type_is_exactly_one_of_a_widget_without_series_one_that_may_have_a_series_or_one_made_of_a_series()
    {
        foreach (var chartType in ChartTypes.All)
        {
            Assert.False(ChartTypes.TakesSeries(chartType) && ChartTypes.NeedsSeries(chartType), chartType);
            Assert.Equal(ChartTypes.TakesSeries(chartType) || ChartTypes.NeedsSeries(chartType), ChartTypes.HasSeries(chartType));
        }

        Assert.Equal(["bar", "column", "line"], ChartTypes.All.Where(ChartTypes.TakesSeries));
        Assert.Equal(["heatmap", "pyramid", "likert", "crosstab"], ChartTypes.All.Where(ChartTypes.NeedsSeries));
    }

    [Theory]
    [InlineData("heatmap")]
    [InlineData("pyramid")]
    [InlineData("likert")]
    [InlineData("crosstab")]
    public void The_new_charts_group_their_rows(string chartType) => Assert.True(ChartTypes.NeedsGroup(chartType));

// Box plot (design D2 of add-boxplot-widget)

    private static WidgetConfig Boxed(WidgetDisplay? display = null, WidgetSort? sort = null, string? groupBy = "municipality") =>
        new WidgetConfigInput("household_income", GroupBy: groupBy, Sort: sort, Display: display).Normalize();

    [Fact]
    public void A_box_plot_needs_no_group_and_is_not_a_chart_that_groups_its_rows()
    {
        Assert.False(ChartTypes.NeedsGroup("boxplot"));
        Assert.False(ChartTypes.HasSeries("boxplot"));
        Assert.Empty(Check(Boxed(groupBy: null), "boxplot"));
        Assert.Empty(Check(Boxed(), "boxplot"));
    }

    [Theory]
    [InlineData("vertical")]
    [InlineData("horizontal")]
    public void An_orientation_is_accepted_on_a_box_plot(string orientation) =>
        Assert.Empty(Check(Boxed(new WidgetDisplay(Orientation: orientation)), "boxplot"));

    [Theory]
    [InlineData("Vertical")]
    [InlineData("sideways")]
    [InlineData("h")]
    public void An_unknown_orientation_is_refused_on_the_orientation(string orientation)
    {
        var errors = Check(Boxed(new WidgetDisplay(Orientation: orientation)), "boxplot");

        Assert.Equal("The orientation must be 'vertical' or 'horizontal'.", Assert.Single(errors["config.display.orientation"]));
        Assert.Contains("config.display.orientation", Check(Boxed(new WidgetDisplay(Orientation: orientation)), null).Keys);
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("histogram")]
    [InlineData("pie")]
    [InlineData("map")]
    [InlineData("heatmap")]
    [InlineData("pyramid")]
    [InlineData("likert")]
    [InlineData("crosstab")]
    public void An_orientation_on_any_other_kind_is_refused_on_the_orientation(string chartType)
    {
        var errors = Check(Drawn(new WidgetDisplay(Orientation: "horizontal")), chartType);

        Assert.Equal("Only a box plot has an orientation.", Assert.Single(errors["config.display.orientation"]));
    }

    [Fact]
    public void An_orientation_is_not_checked_against_a_kind_when_there_is_none()
    {
        Assert.Empty(Check(Boxed(new WidgetDisplay(Orientation: "horizontal")), null));
    }

    [Fact]
    public void A_box_plot_is_refused_a_series_a_ramp_and_a_layout()
    {
        var series = new WidgetConfigInput("household_income", GroupBy: "municipality", SeriesBy: "sex").Normalize();

        Assert.Equal("A boxplot widget cannot have a series.", Assert.Single(Check(series, "boxplot")["config.seriesBy"]));
        Assert.Equal("Only map and heatmap widgets have a scale.", Assert.Single(Check(Boxed(new WidgetDisplay(Ramp: "green")), "boxplot")["config.display.ramp"]));
        Assert.Contains("config.layout", Check(new WidgetConfigInput("household_income", GroupBy: "municipality", Layout: "stacked").Normalize(), "boxplot").Keys);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public void A_box_plot_can_be_ordered_by_its_median(string direction) =>
        Assert.Empty(Check(Boxed(sort: new WidgetSort(SortValues.ByValue, null, null, direction)), "boxplot"));

    [Fact]
    public void A_box_plot_cannot_be_ordered_by_a_column()
    {
        var errors = Check(Boxed(sort: new WidgetSort(SortValues.ByColumn, "round", SortValues.Number, SortValues.Asc)), "boxplot");

        Assert.Equal("A box plot can only be ordered by its median ('value').", Assert.Single(errors["config.sort.by"]));
    }

    [Fact]
    public void A_box_plot_with_no_group_cannot_be_ordered()
    {
        var errors = Check(Boxed(sort: new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc), groupBy: null), "boxplot");

        Assert.Equal("Sorting needs a column to group by.", Assert.Single(errors["config.sort"]));
    }

    // Series (design D2)

    private static WidgetConfig WithSeries(
        string? seriesBy = "water_source", string? layout = null, WidgetRatio? ratio = null, WidgetPyramid? pyramid = null,
        IReadOnlyList<LikertStep>? scale = null, string? groupBy = "municipality", WidgetSort? sort = null) =>
        new WidgetConfigInput("households_by_source", GroupBy: groupBy, SeriesBy: seriesBy, Layout: layout, Ratio: ratio, Pyramid: pyramid, Scale: scale, Sort: sort).Normalize();

    private static readonly LikertStep[] FiveStep =
    [
        new("Strongly disagree", LikertSides.Negative), new("Disagree", LikertSides.Negative), new("Neither", LikertSides.Neutral),
        new("Agree", LikertSides.Positive), new("Strongly agree", LikertSides.Positive),
    ];

    private static readonly WidgetPyramid Sides = new("Female", "Male");

    [Fact]
    public void A_config_saved_before_series_existed_has_none()
    {
        var config = new WidgetConfigInput("coverage", GroupBy: "municipality").Normalize();

        Assert.Null(config.SeriesBy);
        Assert.Null(config.Layout);
        Assert.Null(config.Pyramid);
        Assert.Empty(config.Scale);
    }

    [Fact]
    public void Empty_text_in_a_series_or_a_layout_counts_as_left_out()
    {
        var config = new WidgetConfigInput("a", SeriesBy: "", Layout: "").Normalize();

        Assert.Null(config.SeriesBy);
        Assert.Null(config.Layout);
    }

    [Fact]
    public void The_series_fields_survive_the_json_round_trip_the_widget_endpoints_use()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var input = new WidgetConfigInput("population", GroupBy: "age_band", SeriesBy: "sex", Layout: SeriesLayouts.Stacked, Pyramid: Sides, Scale: FiveStep);

        var json = System.Text.Json.JsonSerializer.Serialize(input, options);
        var back = System.Text.Json.JsonSerializer.Deserialize<WidgetConfigInput>(json, options)!.Normalize();

        Assert.Equal("sex", back.SeriesBy);
        Assert.Equal("stacked", back.Layout);
        Assert.Equal(Sides, back.Pyramid);
        Assert.Equal(FiveStep, back.Scale);
        Assert.Contains("\"seriesBy\":\"sex\"", json);
        Assert.Contains("\"side\":\"negative\"", json);
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    public void Bars_columns_and_lines_may_have_a_series_or_not(string chartType)
    {
        Assert.Empty(Check(WithSeries(), chartType));
        Assert.Empty(Check(WithSeries(seriesBy: null), chartType));
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("histogram")]
    [InlineData("pie")]
    [InlineData("boxplot")]
    public void Some_charts_cannot_have_a_series(string chartType)
    {
        var errors = Check(WithSeries(), chartType);

        Assert.Equal($"A {chartType} widget cannot have a series.", Assert.Single(errors["config.seriesBy"]));
    }

    [Fact]
    public void A_map_cannot_have_a_series()
    {
        var config = new WidgetConfigInput("blocks", GroupBy: "block", GeometryColumn: "geometry", SeriesBy: "sex").Normalize();

        Assert.Equal("A map widget cannot have a series.", Assert.Single(Check(config, "map")["config.seriesBy"]));
    }

    [Theory]
    [InlineData("heatmap")]
    [InlineData("crosstab")]
    public void A_heat_map_and_a_cross_table_need_a_series(string chartType)
    {
        Assert.Equal($"A {chartType} widget needs a series column.", Assert.Single(Check(WithSeries(seriesBy: null), chartType)["config.seriesBy"]));
        Assert.Empty(Check(WithSeries(), chartType));
    }

    [Fact]
    public void A_pyramid_and_a_likert_widget_need_a_series()
    {
        Assert.Contains("config.seriesBy", Check(WithSeries(seriesBy: null, pyramid: Sides), "pyramid").Keys);
        Assert.Contains("config.seriesBy", Check(WithSeries(seriesBy: null, scale: FiveStep), "likert").Keys);
    }

    [Fact]
    public void The_series_column_cannot_be_the_group_column_and_must_be_a_valid_name()
    {
        Assert.Equal(
            "Choose a series column other than the group column.",
            Assert.Single(Check(WithSeries(seriesBy: "municipality"), "column")["config.seriesBy"]));
        Assert.Contains("config.seriesBy", Check(WithSeries(seriesBy: new string('x', 501)), "column").Keys);
        // The query endpoint may have no chart type: the group rule still holds.
        Assert.Contains("config.seriesBy", Check(WithSeries(seriesBy: "municipality"), null).Keys);
        Assert.Equal("A series needs a column to group by.", Assert.Single(Check(WithSeries(groupBy: null), null)["config.seriesBy"]));
        Assert.Empty(Check(WithSeries(), null));
    }

    [Theory]
    [InlineData("grouped")]
    [InlineData("stacked")]
    [InlineData("percent")]
    public void Bars_and_columns_with_a_series_take_each_layout(string layout)
    {
        Assert.Empty(Check(WithSeries(layout: layout), "bar"));
        Assert.Empty(Check(WithSeries(layout: layout), "column"));
    }

    [Fact]
    public void A_layout_must_be_known_and_belong_to_a_bar_or_column_with_a_series()
    {
        Assert.Contains("config.layout", Check(WithSeries(layout: "tiled"), "column").Keys);
        Assert.Equal("Only bar and column widgets have a layout.", Assert.Single(Check(WithSeries(layout: "stacked"), "line")["config.layout"]));
        Assert.Equal("A layout needs a series column.", Assert.Single(Check(WithSeries(seriesBy: null, layout: "stacked"), "column")["config.layout"]));
    }

    [Theory]
    [InlineData("stacked")]
    [InlineData("percent")]
    public void Stacked_layouts_need_a_measure_and_a_grouped_one_may_plot_a_ratio(string layout)
    {
        var ratio = new WidgetRatio("numerator", "denominator");

        Assert.Equal(
            "Stacked bars add their values, so they need a measure, not a ratio.",
            Assert.Single(Check(WithSeries(layout: layout, ratio: ratio), "column")["config.layout"]));
        Assert.Empty(Check(WithSeries(layout: "grouped", ratio: ratio), "column"));
    }

    [Fact]
    public void A_pyramid_needs_two_different_sides()
    {
        Assert.Empty(Check(WithSeries(pyramid: Sides), "pyramid"));
        Assert.Equal("A population pyramid needs a left and a right value.", Assert.Single(Check(WithSeries(), "pyramid")["config.pyramid"]));
        Assert.Equal("Choose the value drawn to the left.", Assert.Single(Check(WithSeries(pyramid: new(null, "Male")), "pyramid")["config.pyramid.left"]));
        Assert.Equal("Choose the value drawn to the right.", Assert.Single(Check(WithSeries(pyramid: new("Female", "")), "pyramid")["config.pyramid.right"]));
        Assert.Equal("The left and right values must differ.", Assert.Single(Check(WithSeries(pyramid: new("Female", "Female")), "pyramid")["config.pyramid.right"]));
        Assert.Contains("config.pyramid.left", Check(WithSeries(pyramid: new(new string('x', 501), "Male")), "pyramid").Keys);
    }

    [Fact]
    public void Only_a_pyramid_has_sides_and_only_a_likert_widget_has_a_scale()
    {
        Assert.Equal("Only population pyramids have sides.", Assert.Single(Check(WithSeries(pyramid: Sides), "heatmap")["config.pyramid"]));
        Assert.Equal("Only Likert widgets have a scale.", Assert.Single(Check(WithSeries(scale: FiveStep), "heatmap")["config.scale"]));
    }

    [Fact]
    public void A_likert_scale_of_five_values_is_valid()
    {
        Assert.Empty(Check(WithSeries(scale: FiveStep), "likert"));
        Assert.Empty(Check(WithSeries(scale: [new("No", LikertSides.Negative), new("Yes", LikertSides.Positive)]), "likert"));
    }

    [Fact]
    public void A_likert_scale_has_between_two_and_eleven_values()
    {
        Assert.Equal("A Likert scale has between 2 and 11 values.", Assert.Single(Check(WithSeries(scale: []), "likert")["config.scale"]));
        Assert.Contains("config.scale", Check(WithSeries(scale: [new("Only", LikertSides.Neutral)]), "likert").Keys);
        LikertStep[] twelve = [.. Enumerable.Range(0, 5).Select(i => new LikertStep($"n{i}", LikertSides.Negative)), new("mid", LikertSides.Neutral), .. Enumerable.Range(0, 5).Select(i => new LikertStep($"p{i}", LikertSides.Positive)), new("extra", LikertSides.Positive)];
        Assert.Contains("A Likert scale has between 2 and 11 values.", Check(WithSeries(scale: twelve), "likert")["config.scale"]);
    }

    [Fact]
    public void A_likert_scale_keeps_its_sides_in_order_and_within_their_limits()
    {
        var outOfOrder = Check(WithSeries(scale: [new("Agree", LikertSides.Positive), new("Disagree", LikertSides.Negative)]), "likert");
        var twoNeutral = Check(WithSeries(scale: [new("No", LikertSides.Negative), new("A", LikertSides.Neutral), new("B", LikertSides.Neutral)]), "likert");
        var sixNegative = Check(WithSeries(scale: [.. Enumerable.Range(0, 6).Select(i => new LikertStep($"n{i}", LikertSides.Negative)), new("Yes", LikertSides.Positive)]), "likert");

        Assert.Equal("Negative values come first, then the neutral one, then the positive ones.", Assert.Single(outOfOrder["config.scale[1].side"]));
        Assert.Equal("A scale has at most one neutral value.", Assert.Single(twoNeutral["config.scale"]));
        Assert.Equal("A scale has at most 5 values on each side.", Assert.Single(sixNegative["config.scale"]));
    }

    [Fact]
    public void Each_value_of_a_likert_scale_is_named_once_and_has_a_known_side()
    {
        var errors = Check(WithSeries(scale: [new("A", LikertSides.Negative), new("A", LikertSides.Positive), new("", LikertSides.Positive), new("B", "middle"), null!]), "likert");

        Assert.Equal("'A' is already in the scale.", Assert.Single(errors["config.scale[1].value"]));
        Assert.Equal("Name the value.", Assert.Single(errors["config.scale[2].value"]));
        Assert.Contains("config.scale[3].side", errors.Keys);
        Assert.Contains("config.scale[4]", errors.Keys);
    }

    [Fact]
    public void A_likert_widget_plots_a_measure()
    {
        var errors = Check(WithSeries(scale: FiveStep, ratio: new WidgetRatio("numerator", "denominator")), "likert");

        Assert.Equal("A Likert widget shows shares of a measure, not a ratio.", Assert.Single(errors["config.measure"]));
    }

    [Theory]
    [InlineData("heatmap")]
    [InlineData("pyramid")]
    [InlineData("likert")]
    [InlineData("crosstab")]
    public void The_new_charts_cannot_be_sorted(string chartType)
    {
        var sort = new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc);

        Assert.Contains("config.sort", Check(WithSeries(sort: sort), chartType).Keys);
    }

    [Fact]
    public void A_bar_with_a_series_may_be_sorted()
    {
        var sort = new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc);

        Assert.Empty(Check(WithSeries(sort: sort), "bar"));
    }

    [Fact]
    public void A_missing_series_column_is_an_error_against_the_table_and_an_existing_one_is_not()
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckAgainstColumns(WithSeries(seriesBy: "water_source"), Columns, "UTC", "config.", errors);
        var found = new FieldErrors();
        IndicatorValidation.CheckAgainstColumns(WithSeries(seriesBy: "municipality", groupBy: "indicator"), Columns, "UTC", "config.", found);

        Assert.Equal("The table has no column 'water_source'.", Assert.Single(errors.ToDictionary()["config.seriesBy"]));
        Assert.DoesNotContain("config.seriesBy", found.ToDictionary().Keys);
    }

    // How a widget is drawn (dashboard-colors, design D4)

    private static WidgetConfig Drawn(WidgetDisplay? display, string? seriesBy = null, string? groupBy = "municipality") =>
        new WidgetConfigInput("households_by_source", GroupBy: groupBy, SeriesBy: seriesBy, Display: display).Normalize();

    [Fact]
    public void A_config_without_a_display_has_none_and_a_display_that_sets_nothing_is_none()
    {
        Assert.Null(new WidgetConfigInput("coverage").Normalize().Display);
        Assert.Null(new WidgetConfigInput("coverage", Display: new WidgetDisplay()).Normalize().Display);
        Assert.Null(new WidgetConfigInput("coverage", Display: new WidgetDisplay(Ramp: "")).Normalize().Display);
        Assert.Equal(new WidgetDisplay(3), new WidgetConfigInput("coverage", Display: new WidgetDisplay(3, "")).Normalize().Display);
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("histogram")]
    [InlineData("boxplot")]
    public void A_color_is_accepted_on_the_charts_that_draw_one_color(string chartType)
    {
        foreach (var color in new[] { 1, 3, 8 })
        {
            Assert.Empty(Check(Drawn(new WidgetDisplay(color), groupBy: chartType == "histogram" ? null : "municipality"), chartType));
        }
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("pie")]
    [InlineData("map")]
    [InlineData("heatmap")]
    [InlineData("pyramid")]
    [InlineData("likert")]
    [InlineData("crosstab")]
    public void A_color_on_any_other_kind_is_refused_on_the_color(string chartType)
    {
        var errors = Check(Drawn(new WidgetDisplay(3)), chartType);

        Assert.Equal($"A {chartType} widget has no color of its own.", Assert.Single(errors["config.display.color"]));
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    public void A_color_on_a_widget_with_a_series_is_refused_on_the_color(string chartType)
    {
        var errors = Check(Drawn(new WidgetDisplay(3), seriesBy: "water_source"), chartType);

        Assert.Equal("A widget with a series takes its colors from its series.", Assert.Single(errors["config.display.color"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-3)]
    public void A_color_outside_1_to_8_is_refused_on_the_color(int color)
    {
        var errors = Check(Drawn(new WidgetDisplay(color)), "column");

        Assert.Equal("The color must be a number from 1 to 8.", Assert.Single(errors["config.display.color"]));
    }

    [Fact]
    public void A_color_outside_1_to_8_is_refused_whatever_the_kind_and_even_with_none_given()
    {
        Assert.Contains("config.display.color", Check(Drawn(new WidgetDisplay(9)), "pie").Keys);
        Assert.Contains("config.display.color", Check(Drawn(new WidgetDisplay(9)), null).Keys);
    }

    [Theory]
    [InlineData("map", "geometry")]
    [InlineData("heatmap", null)]
    public void Every_scale_is_accepted_on_a_map_and_a_heat_map(string chartType, string? geometry)
    {
        foreach (var scale in Palettes.Scales)
        {
            var config = new WidgetConfigInput("households_by_source", GroupBy: "municipality", GeometryColumn: geometry, SeriesBy: chartType == "heatmap" ? "water_source" : null,
                Display: new WidgetDisplay(Ramp: scale)).Normalize();

            Assert.Empty(Check(config, chartType));
        }
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("histogram")]
    [InlineData("pie")]
    [InlineData("pyramid")]
    [InlineData("likert")]
    [InlineData("crosstab")]
    [InlineData("boxplot")]
    public void A_scale_on_any_other_kind_is_refused_on_the_scale(string chartType)
    {
        var errors = Check(Drawn(new WidgetDisplay(Ramp: "green")), chartType);

        Assert.Equal("Only map and heatmap widgets have a scale.", Assert.Single(errors["config.display.ramp"]));
    }

    [Theory]
    [InlineData("Green")]
    [InlineData("earth")]
    [InlineData("#00ff00")]
    [InlineData("magenta")]
    public void A_scale_that_is_not_one_of_the_seven_is_refused_on_the_scale(string scale)
    {
        var errors = Check(Drawn(new WidgetDisplay(Ramp: scale)), "heatmap");

        Assert.Equal("The scale must be one of blue, teal, green, orange, red, purple, gray.", Assert.Single(errors["config.display.ramp"]));
        Assert.Contains("config.display.ramp", Check(Drawn(new WidgetDisplay(Ramp: scale)), null).Keys);
    }

    [Fact]
    public void Without_a_chart_type_only_the_shape_of_the_display_is_checked()
    {
        Assert.Empty(Check(Drawn(new WidgetDisplay(3, "green")), null));
        Assert.Empty(Check(Drawn(new WidgetDisplay(3), seriesBy: "water_source"), null));
    }

    [Fact]
    public void The_errors_of_the_color_and_the_scale_are_kept_apart()
    {
        var errors = Check(Drawn(new WidgetDisplay(3, "green")), "pie");

        Assert.Equal(["config.display.color", "config.display.ramp"], errors.Keys.Order(StringComparer.Ordinal));
    }

    // A widget's own column labels (design D5 of the indicator column metadata)

    public static TheoryData<string> EveryKind() => new(ChartTypes.All);

    private static WidgetConfig Labelled(params (string Column, string Label)[] labels) =>
        Drawn(new WidgetDisplay(Labels: labels.ToDictionary(l => l.Column, l => l.Label)));

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Labels_are_accepted_on_every_kind_of_widget(string chartType)
    {
        var config = Labelled(("numerator", "Hogares que asistieron"), ("denominator", "Hogares"), ("municipality", "Municipio"));

        Assert.DoesNotContain("config.display.labels", Check(config, chartType).Keys);
        Assert.DoesNotContain(Check(config, chartType).Keys, key => key.StartsWith("config.display.labels", StringComparison.Ordinal));
    }

    [Fact]
    public void Labels_are_accepted_without_a_chart_type()
    {
        Assert.Empty(Check(Labelled(("numerator", "Hogares")), null));
    }

    [Fact]
    public void A_chart_type_change_of_a_widget_with_labels_is_accepted_and_the_labels_are_kept()
    {
        var config = new WidgetConfigInput(
            "households_by_source", GroupBy: "municipality", Display: new WidgetDisplay(Color: 3, Labels: new Dictionary<string, string> { ["municipality"] = "Municipio" })).Normalize();

        Assert.Empty(Check(config, ChartTypes.Column));
        Assert.Equal(["config.display.color"], Check(config, ChartTypes.Pie).Keys); // only the color has a per-kind rule
        Assert.Equal("Municipio", config.Display!.Labels!["municipality"]);
    }

    [Fact]
    public void A_widget_labels_at_most_10_columns()
    {
        var ten = Labelled(Enumerable.Range(1, 10).Select(i => ($"columna_{i}", $"Columna {i}")).ToArray());
        var eleven = Labelled(Enumerable.Range(1, 11).Select(i => ($"columna_{i}", $"Columna {i}")).ToArray());

        Assert.Empty(Check(ten, ChartTypes.Column));
        var error = Assert.Single(Check(eleven, ChartTypes.Column));
        Assert.Equal("config.display.labels", error.Key);
        Assert.Equal(["A widget labels at most 10 columns."], error.Value);
    }

    [Theory]
    [InlineData("Comuna")]
    [InlineData("hogar menores")]
    [InlineData("2020_total")]
    [InlineData("comuna-1")]
    public void A_key_that_is_not_a_columns_name_is_refused_on_the_labels(string column)
    {
        var error = Assert.Single(Check(Labelled((column, "Comuna")), ChartTypes.Column));

        Assert.Equal("config.display.labels", error.Key);
        Assert.Equal(
            [$"'{column}' is not a column's name: use the name the indicator's table gives the column, in lowercase letters, digits and underscores."],
            error.Value);
    }

    [Fact]
    public void A_label_of_81_characters_is_refused_on_its_column_and_80_are_accepted()
    {
        Assert.Empty(Check(Labelled(("numerator", new string('a', 80))), ChartTypes.Column));

        var error = Assert.Single(Check(Labelled(("numerator", new string('a', 81))), ChartTypes.Column));

        Assert.Equal("config.display.labels.numerator", error.Key);
        Assert.Equal(["A column label can have up to 80 characters."], error.Value);
    }

    [Theory]
    [InlineData("Hogares\nque asistieron")]
    [InlineData("Hogares\tque")]
    [InlineData("Hogares\0")]
    public void A_label_with_a_control_character_is_refused_on_its_column(string label)
    {
        var error = Assert.Single(Check(Labelled(("numerator", label)), ChartTypes.Column));

        Assert.Equal("config.display.labels.numerator", error.Key);
        Assert.Equal(["A column label cannot hold a line break or another control character."], error.Value);
    }

    [Fact]
    public void A_label_for_a_column_the_table_lost_does_not_make_the_widget_stale()
    {
        var errors = new FieldErrors();
        IndicatorValidation.CheckAgainstColumns(Labelled(("estrato", "Estrato")), Columns, "UTC", "config.", errors);

        Assert.False(errors.Any);
    }

    // A widget's texts (design D2 of the widget context)

    private static IDictionary<string, string[]> CheckContext(WidgetDisplay display, string? chartType = ChartTypes.Bar) =>
        Check(Drawn(display), chartType);

    [Fact]
    public void A_subtitle_of_200_characters_is_accepted_and_201_are_refused_on_its_key()
    {
        Assert.Empty(CheckContext(new WidgetDisplay(Subtitle: new string('a', 200))));

        var error = Assert.Single(CheckContext(new WidgetDisplay(Subtitle: new string('a', 201))));
        Assert.Equal("config.display.subtitle", error.Key);
        Assert.Equal(["The subtitle is longer than 200 characters."], error.Value);
    }

    [Fact]
    public void A_footnote_of_500_characters_is_accepted_and_501_are_refused_on_its_key()
    {
        Assert.Empty(CheckContext(new WidgetDisplay(Footnote: new string('a', 500))));

        var error = Assert.Single(CheckContext(new WidgetDisplay(Footnote: new string('a', 501))));
        Assert.Equal("config.display.footnote", error.Key);
        Assert.Equal(["The footnote is longer than 500 characters."], error.Value);
    }

    [Fact]
    public void A_caption_text_of_200_characters_is_accepted_and_201_are_refused_on_its_key()
    {
        Assert.Empty(CheckContext(new WidgetDisplay(Caption: new WidgetCaption(new string('a', 200)))));

        var error = Assert.Single(CheckContext(new WidgetDisplay(Caption: new WidgetCaption(new string('a', 201)))));
        Assert.Equal("config.display.caption.text", error.Key);
        Assert.Equal(["The caption is longer than 200 characters."], error.Value);
    }

    [Fact]
    public void A_unit_of_32_characters_is_accepted_and_33_are_refused_on_its_key()
    {
        Assert.Empty(CheckContext(new WidgetDisplay(Unit: new string('a', 32))));

        var error = Assert.Single(CheckContext(new WidgetDisplay(Unit: new string('a', 33))));
        Assert.Equal("config.display.unit", error.Key);
        Assert.Equal(["The unit is longer than 32 characters."], error.Value);
    }

    [Fact]
    public void The_lengths_are_counted_after_trimming()
    {
        var padded = new WidgetDisplay(
            Subtitle: "  " + new string('a', 200) + "  ", Footnote: " " + new string('a', 500) + " ",
            Caption: new WidgetCaption(" " + new string('a', 200) + " "), Unit: "  " + new string('a', 32) + "  ");

        Assert.Empty(CheckContext(padded));
        // The same texts with no trimming at all (a display that was not normalized) are counted trimmed too.
        var errors = new FieldErrors();
        IndicatorValidation.CheckConfig(new WidgetConfig("coverage", null, null, "municipality", [], null, null, Display: padded), ChartTypes.Bar, "config.", errors);
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("subtitle", "\0")]
    [InlineData("subtitle", "\n")]
    [InlineData("subtitle", "\t")]
    [InlineData("subtitle", "\u0085")]
    [InlineData("footnote", "\0")]
    [InlineData("footnote", "\r\n")]
    [InlineData("footnote", "\t")]
    [InlineData("footnote", "\u0085")]
    [InlineData("caption", "\0")]
    [InlineData("caption", "\n")]
    [InlineData("caption", "\t")]
    [InlineData("caption", "\u0085")]
    [InlineData("unit", "\0")]
    [InlineData("unit", "\n")]
    [InlineData("unit", "\t")]
    [InlineData("unit", "\u0085")]
    public void A_control_character_inside_a_text_is_refused_on_the_fields_key(string field, string control)
    {
        var text = "uno" + control + "dos";
        var display = field switch
        {
            "subtitle" => new WidgetDisplay(Subtitle: text),
            "footnote" => new WidgetDisplay(Footnote: text),
            "caption" => new WidgetDisplay(Caption: new WidgetCaption(text)),
            _ => new WidgetDisplay(Unit: text),
        };

        var error = Assert.Single(CheckContext(display));

        Assert.Equal(field == "caption" ? "config.display.caption.text" : $"config.display.{field}", error.Key);
        Assert.Equal([$"The {field} cannot contain line breaks or other control characters."], error.Value);
    }

    [Theory]
    [InlineData("uno\n")]
    [InlineData("\tuno")]
    [InlineData("  uno\r\n")]
    public void A_control_character_at_the_end_of_a_text_is_refused_too_and_not_quietly_trimmed(string text)
    {
        var errors = CheckContext(new WidgetDisplay(Subtitle: text, Footnote: text, Caption: new WidgetCaption(text), Unit: text));

        Assert.Equal(
            ["config.display.caption.text", "config.display.footnote", "config.display.subtitle", "config.display.unit"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_caption_with_a_text_and_hidden_is_refused_on_the_caption()
    {
        var error = Assert.Single(CheckContext(new WidgetDisplay(Caption: new WidgetCaption("% de quienes asistieron", true))));

        Assert.Equal("config.display.caption", error.Key);
        Assert.Equal(["Give the caption its own text or hide it, not both."], error.Value);
        Assert.Empty(CheckContext(new WidgetDisplay(Caption: new WidgetCaption(null, true))));
        Assert.Empty(CheckContext(new WidgetDisplay(Caption: new WidgetCaption("% de quienes asistieron"))));
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void The_texts_are_accepted_on_every_kind_of_widget(string chartType)
    {
        var display = new WidgetDisplay(
            Subtitle: "¿En qué lugares ha visto teatro?", Footnote: "No suman 100 %: respuesta múltiple.",
            Caption: new WidgetCaption(Hidden: true), Unit: "de quienes asistieron", ShowDescription: true);
        var other = new WidgetDisplay(Caption: new WidgetCaption("% de quienes asistieron"));

        Assert.DoesNotContain(Check(Drawn(display), chartType).Keys, key => key.StartsWith("config.display", StringComparison.Ordinal));
        Assert.DoesNotContain(Check(Drawn(other), chartType).Keys, key => key.StartsWith("config.display", StringComparison.Ordinal));
    }

    [Fact]
    public void The_texts_are_accepted_with_no_chart_type()
    {
        Assert.Empty(CheckContext(
            new WidgetDisplay(Subtitle: "Pregunta", Footnote: "Nota", Caption: new WidgetCaption("Texto"), Unit: "hogares", ShowDescription: true), null));
    }

    [Fact]
    public void A_chart_type_change_of_a_widget_with_texts_is_accepted()
    {
        var config = Drawn(new WidgetDisplay(Subtitle: "Pregunta", Unit: "hogares"));

        Assert.Empty(Check(config, ChartTypes.Bar));
        Assert.Empty(Check(config, ChartTypes.Pie));
        Assert.Equal(new WidgetDisplay(Subtitle: "Pregunta", Unit: "hogares"), config.Display);
    }

    [Fact]
    public void The_texts_come_with_the_other_settings_without_changing_their_errors()
    {
        var errors = Check(Drawn(new WidgetDisplay(Color: 3, Subtitle: new string('a', 201))), ChartTypes.Pie);

        Assert.Equal(["config.display.color", "config.display.subtitle"], errors.Keys.Order(StringComparer.Ordinal));
    }

    // Reference lines, steps and data labels (design D2 of the chart annotations)

    private static WidgetConfig Annotated(WidgetDisplay display, bool ratio = true, string? seriesBy = null, string? layout = null) =>
        new WidgetConfigInput(
            "teatro_hoy", Ratio: ratio ? new WidgetRatio("numerator", "denominator") : null, GroupBy: "comuna", SeriesBy: seriesBy, Layout: layout,
            Display: display).Normalize();

    private static WidgetDisplay Lines(params WidgetReferenceLine?[] lines) => new(ReferenceLines: lines);

    private static IDictionary<string, string[]> CheckLines(
        WidgetDisplay display, string? chartType = ChartTypes.Column, bool ratio = true, string? seriesBy = null, string? layout = null) =>
        Check(Annotated(display, ratio, seriesBy, layout), chartType);

    private static void AssertRefused(IDictionary<string, string[]> errors, string key, string message)
    {
        var error = Assert.Single(errors);
        Assert.Equal(key, error.Key);
        Assert.Equal([message], error.Value);
    }

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    public void The_four_kinds_are_accepted_on_a_bar_a_column_and_a_line(string chartType)
    {
        var display = Lines(
            new WidgetReferenceLine("mean"), new WidgetReferenceLine("median", Label: "Mediana"),
            new WidgetReferenceLine("overall"), new WidgetReferenceLine("value", 0.5, "Meta"));

        Assert.Empty(CheckLines(display, chartType));
    }

    [Fact]
    public void Five_lines_are_refused_on_the_lines_and_four_are_accepted()
    {
        var four = new[] { new WidgetReferenceLine("value", 1), new WidgetReferenceLine("value", 2), new WidgetReferenceLine("value", 3), new WidgetReferenceLine("value", 4) };

        Assert.Empty(CheckLines(Lines(four)));
        AssertRefused(CheckLines(Lines([.. four, new WidgetReferenceLine("value", 5)])), "config.display.referenceLines", "A widget has at most 4 reference lines.");
    }

    [Fact]
    public void A_null_line_is_refused_on_its_index()
    {
        AssertRefused(CheckLines(Lines(new WidgetReferenceLine("mean"), null)), "config.display.referenceLines[1]", "Each reference line needs a kind.");
    }

    [Theory]
    [InlineData("average")]
    [InlineData("Mean")]
    [InlineData("")]
    [InlineData(null)]
    public void A_kind_that_is_not_one_of_the_four_is_refused_on_its_kind(string? kind)
    {
        var errors = CheckLines(Lines(new WidgetReferenceLine("mean"), new WidgetReferenceLine(kind)));

        AssertRefused(errors, "config.display.referenceLines[1].kind", "The kind must be 'mean', 'median', 'overall' or 'value'.");
    }

    [Theory]
    [InlineData("mean")]
    [InlineData("median")]
    [InlineData("overall")]
    public void A_second_mean_median_or_overall_line_is_refused_on_the_second(string kind)
    {
        var errors = CheckLines(Lines(new WidgetReferenceLine(kind), new WidgetReferenceLine(kind, Label: "Otra")));

        AssertRefused(errors, "config.display.referenceLines[1].kind", $"The {kind} line is listed twice.");
    }

    [Fact]
    public void Several_value_lines_are_accepted()
    {
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("value", 0.2), new WidgetReferenceLine("value", 0.2, "Otra"))));
    }

    [Fact]
    public void A_value_line_without_its_value_is_refused_on_its_value()
    {
        AssertRefused(CheckLines(Lines(new WidgetReferenceLine("value"))), "config.display.referenceLines[0].value", "A 'value' line needs its value.");
    }

    [Theory]
    [InlineData("mean")]
    [InlineData("median")]
    [InlineData("overall")]
    public void A_value_on_another_kind_is_refused_on_its_value(string kind)
    {
        AssertRefused(CheckLines(Lines(new WidgetReferenceLine(kind, 0.5))), "config.display.referenceLines[0].value", "Only a 'value' line takes a value.");
    }

    [Fact]
    public void A_label_of_40_characters_is_accepted_and_41_are_refused_on_its_label_counted_after_trimming()
    {
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("mean", Label: new string('a', 40)))));
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("mean", Label: "  " + new string('a', 40) + "  "))));
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("mean", Label: "   "))));

        AssertRefused(
            CheckLines(Lines(new WidgetReferenceLine("mean"), new WidgetReferenceLine("median", Label: new string('a', 41)))),
            "config.display.referenceLines[1].label", "A reference line's label has at most 40 characters.");
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    public void A_control_character_in_a_label_is_refused_on_its_label(string control)
    {
        var errors = CheckLines(Lines(new WidgetReferenceLine("value", 0.5, "Meta" + control + "2027")));

        AssertRefused(errors, "config.display.referenceLines[0].label", "A reference line's label cannot contain line breaks or other control characters.");
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Pie)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Map)]
    [InlineData(ChartTypes.Heatmap)]
    [InlineData(ChartTypes.Boxplot)]
    [InlineData(ChartTypes.Crosstab)]
    public void Lines_on_a_kind_that_is_not_a_bar_a_column_or_a_line_are_refused_on_the_lines(string chartType)
    {
        var errors = CheckLines(Lines(new WidgetReferenceLine("mean")), chartType);

        // Some of these kinds are refused for other reasons too (a heat map needs its series): only the keys of the lines are looked at.
        Assert.Equal("Only bar, column and line widgets have reference lines.", Assert.Single(errors["config.display.referenceLines"]));
        Assert.DoesNotContain(errors.Keys, key => key.StartsWith("config.display.referenceLines[", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("mean")]
    [InlineData("median")]
    public void A_mean_or_median_line_with_a_series_is_refused_on_its_kind(string kind)
    {
        var errors = CheckLines(Lines(new WidgetReferenceLine(kind)), ChartTypes.Column, ratio: true, seriesBy: "estrato");

        AssertRefused(errors, "config.display.referenceLines[0].kind", "A mean or median line needs a chart with no series.");
    }

    [Fact]
    public void An_overall_line_over_a_measure_is_refused_on_its_kind()
    {
        AssertRefused(
            CheckLines(Lines(new WidgetReferenceLine("overall")), ChartTypes.Bar, ratio: false),
            "config.display.referenceLines[0].kind", "An overall line needs a ratio: over a measure it would be the sum of every bar.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SeriesLayouts.Grouped)]
    public void Overall_and_value_lines_are_accepted_on_side_by_side_bars_and_on_a_line_with_a_series(string? layout)
    {
        var display = Lines(new WidgetReferenceLine("overall"), new WidgetReferenceLine("value", 0.4));

        Assert.Empty(CheckLines(display, ChartTypes.Column, seriesBy: "estrato", layout: layout));
        Assert.Empty(CheckLines(display, ChartTypes.Bar, seriesBy: "estrato", layout: layout));
        Assert.Empty(CheckLines(display, ChartTypes.Line, seriesBy: "estrato"));
    }

    [Theory]
    [InlineData(SeriesLayouts.Stacked)]
    [InlineData(SeriesLayouts.Percent)]
    public void An_overall_line_on_a_stacked_or_percent_layout_gets_the_ratio_message_and_value_lines_are_accepted(string layout)
    {
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("value", 0.5)), ChartTypes.Bar, ratio: false, seriesBy: "respuesta", layout: layout));
        AssertRefused(
            CheckLines(Lines(new WidgetReferenceLine("overall")), ChartTypes.Bar, ratio: false, seriesBy: "respuesta", layout: layout),
            "config.display.referenceLines[0].kind", "An overall line needs a ratio: over a measure it would be the sum of every bar.");
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    [InlineData(50)]
    public void A_value_outside_0_to_1_in_a_percent_layout_is_refused_on_its_value(double value)
    {
        var errors = CheckLines(Lines(new WidgetReferenceLine("value", value)), ChartTypes.Bar, ratio: false, seriesBy: "respuesta", layout: SeriesLayouts.Percent);

        AssertRefused(errors, "config.display.referenceLines[0].value", "In a 100% layout a value is a share from 0 to 1.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void A_share_from_0_to_1_is_accepted_in_a_percent_layout_and_any_number_in_the_others(double value)
    {
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("value", value)), ChartTypes.Bar, ratio: false, seriesBy: "respuesta", layout: SeriesLayouts.Percent));
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("value", 150)), ChartTypes.Bar, ratio: false, seriesBy: "respuesta", layout: SeriesLayouts.Stacked));
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("value", -3.5)), ChartTypes.Column, ratio: false));
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("step")]
    public void A_known_interpolation_is_accepted_on_a_line_with_or_without_a_series(string interpolation)
    {
        var display = new WidgetDisplay(Interpolation: interpolation);

        Assert.Empty(CheckLines(display, ChartTypes.Line));
        Assert.Empty(CheckLines(display, ChartTypes.Line, seriesBy: "estrato"));
    }

    [Theory]
    [InlineData("curve")]
    [InlineData("Step")]
    public void An_interpolation_that_is_not_linear_or_step_is_refused_on_its_key_even_with_no_chart_type(string interpolation)
    {
        var display = new WidgetDisplay(Interpolation: interpolation);

        AssertRefused(CheckLines(display, ChartTypes.Line), "config.display.interpolation", "The interpolation must be 'linear' or 'step'.");
        AssertRefused(CheckLines(display, null), "config.display.interpolation", "The interpolation must be 'linear' or 'step'.");
    }

    [Theory]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Pie)]
    [InlineData(ChartTypes.Stat)]
    public void An_interpolation_on_a_kind_that_is_not_a_line_is_refused_on_its_key(string chartType)
    {
        AssertRefused(
            CheckLines(new WidgetDisplay(Interpolation: "step"), chartType), "config.display.interpolation", "Only a line chart has an interpolation.");
    }

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Pie)]
    public void Data_labels_are_accepted_on_a_bar_a_column_a_line_and_a_pie(string chartType)
    {
        Assert.Empty(CheckLines(new WidgetDisplay(DataLabels: true), chartType));
        Assert.Empty(CheckLines(new WidgetDisplay(DataLabels: false), chartType));
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Map)]
    [InlineData(ChartTypes.Heatmap)]
    [InlineData(ChartTypes.Pyramid)]
    [InlineData(ChartTypes.Likert)]
    [InlineData(ChartTypes.Crosstab)]
    [InlineData(ChartTypes.Boxplot)]
    public void Data_labels_on_another_kind_are_refused_on_their_key(string chartType)
    {
        // Some of these kinds are refused for other reasons too (a heat map needs its series): only the key of the labels is looked at.
        var errors = CheckLines(new WidgetDisplay(DataLabels: true), chartType);

        Assert.Equal("Only bar, column, line and pie widgets have data labels.", Assert.Single(errors["config.display.dataLabels"]));
    }

    [Theory]
    [InlineData(ChartTypes.Bar, SeriesLayouts.Stacked)]
    [InlineData(ChartTypes.Column, SeriesLayouts.Stacked)]
    [InlineData(ChartTypes.Bar, SeriesLayouts.Percent)]
    [InlineData(ChartTypes.Column, SeriesLayouts.Percent)]
    public void Data_labels_on_a_stacked_or_percent_layout_are_refused_but_off_is_accepted(string chartType, string layout)
    {
        AssertRefused(
            CheckLines(new WidgetDisplay(DataLabels: true), chartType, ratio: false, seriesBy: "respuesta", layout: layout),
            "config.display.dataLabels", "Stacked bars have no data labels.");
        Assert.Empty(CheckLines(new WidgetDisplay(DataLabels: false), chartType, ratio: false, seriesBy: "respuesta", layout: layout));
    }

    [Fact]
    public void Data_labels_are_accepted_on_side_by_side_bars()
    {
        Assert.Empty(CheckLines(new WidgetDisplay(DataLabels: true), ChartTypes.Column, seriesBy: "estrato", layout: SeriesLayouts.Grouped));
        Assert.Empty(CheckLines(new WidgetDisplay(DataLabels: true), ChartTypes.Bar, seriesBy: "estrato"));
    }

    [Fact]
    public void With_no_chart_type_only_the_shape_of_the_lines_the_steps_and_the_labels_is_checked()
    {
        var everything = new WidgetDisplay(
            ReferenceLines: [new WidgetReferenceLine("mean"), new WidgetReferenceLine("overall")], Interpolation: "step", DataLabels: true);

        Assert.Empty(CheckLines(everything, null, ratio: false, seriesBy: "estrato", layout: SeriesLayouts.Stacked));
        Assert.Empty(CheckLines(Lines(new WidgetReferenceLine("value", 7)), null, ratio: false, seriesBy: "estrato", layout: SeriesLayouts.Percent));

        var errors = CheckLines(Lines(new WidgetReferenceLine("value"), new WidgetReferenceLine("nope"), null), null);
        Assert.Equal(
            ["config.display.referenceLines[0].value", "config.display.referenceLines[1].kind", "config.display.referenceLines[2]"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_lines_the_steps_and_the_labels_come_with_the_other_settings_without_changing_their_errors()
    {
        var errors = CheckLines(new WidgetDisplay(Color: 3, Subtitle: new string('a', 201), ReferenceLines: [new WidgetReferenceLine("mean")]), ChartTypes.Column);

        Assert.Equal(["config.display.subtitle"], errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["config.display.color", "config.display.subtitle"], CheckLines(new WidgetDisplay(Color: 3, Subtitle: new string('a', 201), DataLabels: true), ChartTypes.Pie).Keys.Order(StringComparer.Ordinal));
    }

    // A widget's own order of values (design D6 of add-declared-category-order)

    private static WidgetDisplay OrderOf(IReadOnlyList<string?>? group = null, IReadOnlyList<string?>? series = null) =>
        new(Order: new WidgetCategoryOrder(group, series));

    private static IDictionary<string, string[]> CheckOrder(WidgetConfig config, WidgetDisplay display, string? chartType) =>
        Check(config with { Display = display }, chartType);

    private static readonly WidgetSort ByPlottedValue = new(SortValues.ByValue, null, null, SortValues.Desc);

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Pie)]
    [InlineData(ChartTypes.Boxplot)]
    public void An_order_of_groups_is_accepted_with_a_group_column_and_no_sort(string chartType)
    {
        Assert.Empty(CheckOrder(WithSeries(seriesBy: null), OrderOf(["Cali", "Buga"]), chartType));
    }

    [Theory]
    [InlineData(ChartTypes.Heatmap)]
    [InlineData(ChartTypes.Crosstab)]
    [InlineData(ChartTypes.Pyramid)]
    [InlineData(ChartTypes.Likert)]
    public void An_order_of_groups_is_accepted_on_the_kinds_with_a_series_that_have_groups(string chartType)
    {
        var config = WithSeries(pyramid: chartType == ChartTypes.Pyramid ? Sides : null, scale: chartType == ChartTypes.Likert ? FiveStep : null);

        Assert.Empty(CheckOrder(config, OrderOf(["Cali", "Buga"]), chartType));
    }

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Heatmap)]
    [InlineData(ChartTypes.Crosstab)]
    public void An_order_of_series_is_accepted_with_a_series_column(string chartType)
    {
        Assert.Empty(CheckOrder(WithSeries(), OrderOf(series: ["River", "Well"]), chartType));
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Map)]
    public void An_order_of_groups_is_refused_on_a_stat_tile_a_histogram_and_a_map(string chartType)
    {
        var errors = CheckOrder(WithSeries(seriesBy: null), OrderOf(["Cali"]), chartType);

        Assert.Equal([$"A {chartType} widget has no groups to order."], errors["config.display.order.group"]);
    }

    [Fact]
    public void An_order_of_groups_is_refused_without_a_group_column()
    {
        var errors = CheckOrder(Boxed(groupBy: null), OrderOf(["Cali"]), ChartTypes.Boxplot);

        Assert.Equal(["Ordering groups needs a column to group by."], errors["config.display.order.group"]);
    }

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    public void An_order_of_groups_is_refused_with_a_sort(string chartType)
    {
        var errors = CheckOrder(WithSeries(seriesBy: null, sort: ByPlottedValue), OrderOf(["Cali"]), chartType);

        Assert.Equal(
            ["A sorted widget takes the order of its groups from its sort: remove the sort or this order."], errors["config.display.order.group"]);
    }

    [Fact]
    public void An_order_of_groups_is_refused_with_a_sort_by_a_column_too()
    {
        var errors = CheckOrder(WithSeries(seriesBy: null, sort: ByColumn), OrderOf(["Cali"]), ChartTypes.Column);

        Assert.Equal(
            ["A sorted widget takes the order of its groups from its sort: remove the sort or this order."], errors["config.display.order.group"]);
    }

    [Fact]
    public void An_order_of_boxes_is_refused_with_an_order_by_median()
    {
        var errors = CheckOrder(Boxed(sort: ByPlottedValue), OrderOf(["Cali"]), ChartTypes.Boxplot);

        Assert.Equal(
            ["A box plot ordered by median takes the order of its boxes from their medians: remove that order or this one."],
            errors["config.display.order.group"]);
    }

    [Fact]
    public void An_order_of_series_is_still_accepted_with_a_sort()
    {
        Assert.Empty(CheckOrder(WithSeries(sort: ByPlottedValue), OrderOf(series: ["River"]), ChartTypes.Bar));
    }

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    public void An_order_of_series_is_refused_without_a_series_column(string chartType)
    {
        var errors = CheckOrder(WithSeries(seriesBy: null), OrderOf(series: ["River"]), chartType);

        Assert.Equal(["Only a widget with a series has an order of series."], errors["config.display.order.series"]);
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Pie)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Map)]
    [InlineData(ChartTypes.Boxplot)]
    public void An_order_of_series_is_refused_on_a_kind_that_takes_none(string chartType)
    {
        var errors = CheckOrder(WithSeries(seriesBy: null), OrderOf(series: ["River"]), chartType);

        Assert.Equal(["Only a widget with a series has an order of series."], errors["config.display.order.series"]);
    }

    [Fact]
    public void An_order_of_series_is_refused_on_a_pyramid()
    {
        var errors = CheckOrder(WithSeries(pyramid: Sides), OrderOf(series: ["Male", "Female"]), ChartTypes.Pyramid);

        Assert.Equal(["A population pyramid's sides are its left and right values."], errors["config.display.order.series"]);
    }

    [Fact]
    public void An_order_of_series_is_refused_on_a_Likert_widget()
    {
        var errors = CheckOrder(WithSeries(scale: FiveStep), OrderOf(series: ["Agree"]), ChartTypes.Likert);

        Assert.Equal(["A Likert widget's answers follow its scale."], errors["config.display.order.series"]);
    }

    [Fact]
    public void Changing_a_column_chart_with_an_order_of_series_into_a_pie_is_refused_on_the_order_of_series()
    {
        var config = WithSeries(seriesBy: null);

        var errors = CheckOrder(config, OrderOf(series: ["River", "Well"]), ChartTypes.Pie);

        Assert.Equal(["config.display.order.series"], errors.Keys);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public void An_order_lists_at_most_a_hundred_values(int count, bool refused)
    {
        var values = Enumerable.Range(1, count).Select(i => (string?)$"Value {i}").ToList();

        var groups = CheckOrder(WithSeries(seriesBy: null), OrderOf(values), ChartTypes.Column);
        var series = CheckOrder(WithSeries(), OrderOf(series: values), ChartTypes.Column);

        if (refused)
        {
            Assert.Equal(["An order lists at most 100 values."], groups["config.display.order.group"]);
            Assert.Equal(["An order lists at most 100 values."], series["config.display.order.series"]);
        }
        else
        {
            Assert.Empty(groups);
            Assert.Empty(series);
        }
    }

    [Fact]
    public void A_null_value_and_a_value_over_500_characters_are_refused_on_their_own_item()
    {
        var errors = CheckOrder(WithSeries(), OrderOf(["Cali", null, new string('a', 501), new string('b', 500)], ["No", new string('c', 501)]), ChartTypes.Column);

        Assert.Equal(
            ["config.display.order.group[1]", "config.display.order.group[2]", "config.display.order.series[1]"],
            errors.Keys.Order(StringComparer.Ordinal));
        Assert.All(errors.Values, messages => Assert.Equal(["Each value is a text of up to 500 characters."], messages));
    }

    [Fact]
    public void A_value_with_a_NUL_character_is_refused()
    {
        var errors = CheckOrder(WithSeries(), OrderOf(["Cali", "Bu\0ga"]), ChartTypes.Column);

        Assert.Equal(["The value cannot contain a NUL character."], errors["config.display.order.group[1]"]);
    }

    [Fact]
    public void A_value_listed_twice_is_refused_on_the_second_one()
    {
        var errors = CheckOrder(WithSeries(), OrderOf(series: ["Sí", "No sabe", "No", "No sabe"]), ChartTypes.Column);

        Assert.Equal(["config.display.order.series[3]"], errors.Keys);
        Assert.Equal(["'No sabe' is listed twice."], errors["config.display.order.series[3]"]);
    }

    [Fact]
    public void Values_are_exact_text_so_a_different_case_or_the_empty_text_are_values()
    {
        Assert.Empty(CheckOrder(WithSeries(), OrderOf(["Sí", "sí", "SÍ", "", " "]), ChartTypes.Column));
        var errors = CheckOrder(WithSeries(), OrderOf(["", ""]), ChartTypes.Column);
        Assert.Equal(["'' is listed twice."], errors["config.display.order.group[1]"]);
    }

    [Fact]
    public void An_empty_order_checks_nothing()
    {
        Assert.Empty(CheckOrder(WithSeries(seriesBy: null, sort: ByPlottedValue), OrderOf(group: [], series: []), ChartTypes.Bar));
    }

    [Fact]
    public void With_no_chart_type_only_the_shape_of_the_lists_is_checked()
    {
        Assert.Empty(CheckOrder(WithSeries(seriesBy: null, sort: ByPlottedValue), OrderOf(["Cali"], ["River"]), null));

        var errors = CheckOrder(WithSeries(), OrderOf(["Cali", "Cali"], [null]), null);
        Assert.Equal(["config.display.order.group[1]", "config.display.order.series[0]"], errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_order_comes_with_the_other_settings_without_changing_their_errors()
    {
        var errors = CheckOrder(WithSeries(), new WidgetDisplay(Color: 3, Order: new WidgetCategoryOrder(["Cali"], ["River"])), ChartTypes.Column);

        Assert.Equal(["config.display.color"], errors.Keys);
    }

    // The base settings and leaving out no-data values (design D6 and D7 of add-small-sample-warnings)

    private static WidgetConfig WithBase(WidgetDisplay? display = null, bool? missingFooter = null, string? groupBy = "municipality", string? seriesBy = null) =>
        new WidgetConfigInput("households_by_source", GroupBy: groupBy, SeriesBy: seriesBy, Display: display, MissingFooter: missingFooter).Normalize();

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void The_base_settings_are_accepted_on_every_kind_of_widget(string chartType)
    {
        var errors = Check(WithBase(new WidgetDisplay(MinN: 30, ShowN: false), seriesBy: ChartTypes.HasSeries(chartType) ? "water_source" : null), chartType);

        Assert.DoesNotContain(errors.Keys, key => key.StartsWith("config.display", StringComparison.Ordinal));
    }

    [Fact]
    public void A_chart_type_change_of_a_widget_with_both_base_settings_is_accepted()
    {
        var config = WithBase(new WidgetDisplay(MinN: 0, ShowN: true));

        Assert.Empty(Check(config, ChartTypes.Bar));
        Assert.Empty(Check(config, ChartTypes.Histogram));
        Assert.Empty(Check(config, ChartTypes.Stat));
        Assert.Equal(new WidgetDisplay(MinN: 0, ShowN: true), config.Display);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10000)]
    public void A_threshold_from_0_to_10000_is_accepted(int minN) => Assert.Empty(Check(WithBase(new WidgetDisplay(MinN: minN)), ChartTypes.Column));

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void A_threshold_out_of_range_is_refused_on_the_threshold(int minN)
    {
        var error = Assert.Single(Check(WithBase(new WidgetDisplay(MinN: minN)), ChartTypes.Column));

        Assert.Equal("config.display.minN", error.Key);
        Assert.Equal(["The small-base threshold must be a whole number from 0 (off) to 10,000."], error.Value);
        Assert.Single(Check(WithBase(new WidgetDisplay(MinN: minN)), null));
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Missing_footer_is_accepted_on_the_kinds_that_take_it_and_refused_on_the_others(string chartType)
    {
        var errors = Check(WithBase(missingFooter: true, seriesBy: ChartTypes.HasSeries(chartType) ? "water_source" : null), chartType);

        if (ChartTypes.TakesMissingFooter(chartType))
        {
            Assert.DoesNotContain("config.missingFooter", errors.Keys);
        }
        else
        {
            Assert.Equal(
                ["Only bar, column, line, pie, heat map, population pyramid, Likert and cross table widgets can leave out values that mean no data."],
                errors["config.missingFooter"]);
        }
    }

    [Fact]
    public void A_stat_tile_that_leaves_out_no_data_values_is_refused_on_that_option_and_no_chart_type_checks_nothing()
    {
        var error = Assert.Single(Check(WithBase(missingFooter: true, groupBy: null), ChartTypes.Stat));

        Assert.Equal("config.missingFooter", error.Key);
        Assert.Empty(Check(WithBase(missingFooter: true), null));
        Assert.Empty(Check(WithBase(), ChartTypes.Stat));
    }

    // The statistic and the secondary lines of a stat tile (design D2 and D3 of improve-stat-cards)

    private static WidgetConfig Tile(string? statistic = null, WidgetDisplay? display = null) =>
        new WidgetConfigInput("coverage", Display: display, Statistic: statistic).Normalize();

    [Theory]
    [InlineData("median")]
    [InlineData("p25")]
    [InlineData("p75")]
    [InlineData("mean")]
    public void A_stat_tile_takes_each_of_the_four_statistics(string statistic) => Assert.Empty(Check(Tile(statistic), ChartTypes.Stat));

    [Fact]
    public void An_unknown_statistic_is_refused_on_the_statistic()
    {
        foreach (var statistic in new[] { "p90", "Median", "average", " median" })
        {
            Assert.Equal(["The statistic must be median, p25, p75 or mean."], Check(Tile(statistic), ChartTypes.Stat)["config.statistic"]);
        }
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void A_statistic_is_refused_on_every_kind_but_a_stat_tile(string chartType)
    {
        var errors = Check(WithBase(groupBy: ChartTypes.NeedsGroup(chartType) ? "municipality" : null, seriesBy: ChartTypes.NeedsSeries(chartType) ? "water_source" : null) with { Statistic = "median" }, chartType);

        if (chartType == ChartTypes.Stat)
        {
            Assert.DoesNotContain("config.statistic", errors.Keys);
        }
        else
        {
            Assert.Equal(["Only a stat tile has a statistic."], errors["config.statistic"]);
        }
    }

    [Fact]
    public void A_query_with_no_chart_type_that_names_a_statistic_is_refused_and_one_that_names_none_is_not()
    {
        Assert.Equal(["A statistic needs the chart type stat."], Check(Tile("median"), null)["config.statistic"]);
        Assert.Empty(Check(Tile(), null));
        Assert.Empty(Check(Tile(), ChartTypes.Stat));
    }

    [Fact]
    public void An_unknown_statistic_with_no_chart_type_names_both_problems()
    {
        Assert.Equal(
            ["The statistic must be median, p25, p75 or mean.", "A statistic needs the chart type stat."],
            Check(Tile("p90"), null)["config.statistic"]);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("unfiltered")]
    public void A_stat_tile_takes_each_secondary_line(string line) =>
        Assert.Empty(Check(Tile(display: new WidgetDisplay(Secondary: [line])), ChartTypes.Stat));

    [Fact]
    public void A_stat_tile_takes_both_lines_in_either_order_and_a_query_with_no_chart_type_takes_them_too()
    {
        Assert.Empty(Check(Tile(display: new WidgetDisplay(Secondary: ["n", "unfiltered"])), ChartTypes.Stat));
        Assert.Empty(Check(Tile(display: new WidgetDisplay(Secondary: ["unfiltered", "n"])), ChartTypes.Stat));
        Assert.Empty(Check(Tile(display: new WidgetDisplay(Secondary: ["n", "unfiltered"])), null));
    }

    [Fact]
    public void An_unknown_secondary_line_is_refused_on_the_secondary_lines()
    {
        foreach (var lines in new[] { new[] { "total" }, new[] { "N" }, new[] { "n", "share" }, new string?[] { null } })
        {
            Assert.Equal(
                ["Each secondary line must be 'n' or 'unfiltered'."],
                Check(Tile(display: new WidgetDisplay(Secondary: lines!)), ChartTypes.Stat)["config.display.secondary"]);
        }
    }

    [Fact]
    public void A_repeated_secondary_line_is_refused_once()
    {
        Assert.Equal(
            ["A secondary line is listed twice."],
            Check(Tile(display: new WidgetDisplay(Secondary: ["n", "n"])), ChartTypes.Stat)["config.display.secondary"]);
        Assert.Equal(
            ["A secondary line is listed twice."],
            Check(Tile(display: new WidgetDisplay(Secondary: ["unfiltered", "n", "unfiltered"])), ChartTypes.Stat)["config.display.secondary"]);
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Secondary_lines_are_refused_on_every_kind_but_a_stat_tile(string chartType)
    {
        var config = WithBase(
            new WidgetDisplay(Secondary: ["n"]), groupBy: ChartTypes.NeedsGroup(chartType) ? "municipality" : null,
            seriesBy: ChartTypes.NeedsSeries(chartType) ? "water_source" : null);

        var errors = Check(config, chartType);

        if (chartType == ChartTypes.Stat)
        {
            Assert.DoesNotContain("config.display.secondary", errors.Keys);
        }
        else
        {
            Assert.Equal(["Only a stat tile has secondary lines."], errors["config.display.secondary"]);
        }
    }

    [Fact]
    public void Secondary_lines_with_no_chart_type_skip_the_kind_rule()
    {
        Assert.Empty(Check(WithBase(new WidgetDisplay(Secondary: ["n"])), null));
        Assert.Contains("config.display.secondary", Check(WithBase(new WidgetDisplay(Secondary: ["total"])), null).Keys);
    }
}
