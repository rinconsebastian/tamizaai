using Tamiza.Api.CleanData;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class IndicatorValidationTests
{
    private static readonly TableColumn[] Columns =
    [
        new("indicator", DataColumnType.Text, null),
        new("disaggregation", DataColumnType.Text, null),
        new("value", DataColumnType.Decimal, null),
        new("numerator", DataColumnType.Decimal, null),
        new("denominator", DataColumnType.Decimal, null),
        new("year", DataColumnType.Integer, null),
        new("geometry", DataColumnType.Text, null),
    ];

    private static WidgetConfig Config(params string[] indicators) => new WidgetConfigInput(indicators).Normalize();

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

        Assert.Empty(config.Indicators);
        Assert.Equal("disaggregation", config.GroupBy);
        Assert.Equal("value", config.ValueField);
        Assert.Empty(config.Filters);
        Assert.Null(config.GeometryColumn);
        Assert.Null(config.Buckets);
    }

    [Fact]
    public void Empty_text_counts_as_left_out()
    {
        var config = new WidgetConfigInput(["a"], GroupBy: "", ValueField: "", GeometryColumn: "").Normalize();

        Assert.Equal("disaggregation", config.GroupBy);
        Assert.Equal("value", config.ValueField);
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
    public void Each_chart_type_has_a_default_size(string chartType, int w, int h) =>
        Assert.Equal((w, h), IndicatorValidation.DefaultSize(chartType));

    // Per-type indicator constraints (design D9)

    [Theory]
    [InlineData("stat")]
    [InlineData("pie")]
    [InlineData("map")]
    public void Stat_pie_and_map_need_exactly_one_indicator(string chartType)
    {
        Assert.Empty(Check(WithGeometry(Config("a")), chartType));

        var two = Check(WithGeometry(Config("a", "b")), chartType);

        Assert.Equal([$"A {chartType} widget uses exactly one indicator."], two["config.indicators"]);
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("histogram")]
    public void The_other_charts_accept_one_or_more(string chartType)
    {
        Assert.Empty(Check(Config("a"), chartType));
        Assert.Empty(Check(Config("a", "b", "c"), chartType));
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("bar")]
    [InlineData(null)]
    public void No_indicator_is_always_an_error(string? chartType) =>
        Assert.Equal(["Choose at least one indicator."], Check(Config(), chartType)["config.indicators"]);

    [Fact]
    public void Without_a_chart_type_as_in_the_query_endpoint_only_the_general_rules_apply() =>
        Assert.Empty(Check(Config("a", "b"), chartType: null));

    [Fact]
    public void A_map_needs_a_geometry_column_and_other_types_do_not()
    {
        Assert.Contains("config.geometryColumn", Check(Config("a"), "map").Keys);
        Assert.Empty(Check(Config("a"), "bar"));
    }

    [Fact]
    public void An_empty_or_overlong_indicator_is_an_error()
    {
        Assert.Contains("config.indicators", Check(Config("a", ""), "bar").Keys);
        Assert.Contains("config.indicators", Check(Config(new string('x', 501)), "bar").Keys);
        Assert.Empty(Check(Config(new string('x', 500)), "bar"));
    }

    [Fact]
    public void At_most_100_indicators_are_allowed()
    {
        Assert.Empty(Check(Config(Enumerable.Range(0, 100).Select(i => "i" + i).ToArray()), "bar"));
        Assert.Contains("config.indicators", Check(Config(Enumerable.Range(0, 101).Select(i => "i" + i).ToArray()), "bar").Keys);
    }

    [Theory]
    [InlineData("value", true)]
    [InlineData("numerator", true)]
    [InlineData("denominator", true)]
    [InlineData("Value", false)]
    [InlineData("total", false)]
    public void The_field_to_plot_is_one_of_three(string field, bool valid)
    {
        var config = new WidgetConfigInput(["a"], ValueField: field).Normalize();

        Assert.Equal(valid, Check(config, "bar").Count == 0);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-3, false)]
    [InlineData(101, false)]
    public void Buckets_are_between_1_and_100(int buckets, bool valid)
    {
        var config = new WidgetConfigInput(["a"], Buckets: buckets).Normalize();

        Assert.Equal(valid, Check(config, "histogram").Count == 0);
    }

    // Filters

    [Fact]
    public void A_filter_needs_a_column_and_a_known_operator()
    {
        var config = new WidgetConfigInput(["a"], Filters: [new DataFilter("", "equals", "x"), new DataFilter("year", "like", "x"), null!]).Normalize();

        var errors = Check(config, "bar");

        Assert.Equal(["config.filters[0]", "config.filters[1]", "config.filters[2]"], errors.Keys.Order());
    }

    [Fact]
    public void At_most_50_filters_and_100_values_per_filter()
    {
        var many = Enumerable.Range(0, 51).Select(i => new DataFilter("c" + i, "empty")).ToArray();
        var values = new DataFilter("c", "in", Values: Enumerable.Range(0, 101).Select(i => "v" + i).ToArray());

        Assert.Contains("config.filters", Check(new WidgetConfigInput(["a"], Filters: many).Normalize(), "bar").Keys);
        Assert.Contains("config.filters[0]", Check(new WidgetConfigInput(["a"], Filters: [values]).Normalize(), "bar").Keys);
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
        var config = new WidgetConfigInput(["a"], GroupBy: "round", GeometryColumn: "shape", Filters: [new DataFilter("region", "empty")]).Normalize();

        var errors = CheckTable(config);

        Assert.Contains("'round'", errors["config.groupBy"][0]);
        Assert.Contains("'shape'", errors["config.geometryColumn"][0]);
        Assert.Contains("'region'", errors["config.filters[0]"][0]);
    }

    [Fact]
    public void A_config_that_matches_the_table_has_no_errors()
    {
        var config = new WidgetConfigInput(["a"], GroupBy: "year", GeometryColumn: "geometry",
            Filters: [new DataFilter("year", "range", Min: "2020", Max: "2026"), new DataFilter("disaggregation", "in", Values: ["Cali"])]).Normalize();

        Assert.Empty(CheckTable(config));
    }

    [Fact]
    public void A_filter_that_does_not_fit_its_columns_type_is_an_error()
    {
        var config = new WidgetConfigInput(["a"], Filters: [new DataFilter("disaggregation", "range", Min: "1")]).Normalize();

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
}
