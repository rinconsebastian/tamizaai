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
