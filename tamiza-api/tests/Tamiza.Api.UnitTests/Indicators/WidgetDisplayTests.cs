using System.Text.Json;
using Tamiza.Api.CleanData;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// A widget's display (dashboard-colors, design D3): a config saved before it existed reads and serializes exactly as it did, and a
/// display survives the JSON round trip that reading a stored config and the PATCH use. Those use the web defaults, as here.
/// </summary>
public sealed class WidgetDisplayTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // What the API stored, and returned, for these two configs before widgets had a display, captured from the code of that time.
    private const string MinimalBefore =
        """{"indicator":"coverage","measure":null,"ratio":null,"groupBy":"municipality","filters":[],"geometryColumn":null,"buckets":null,"sort":null,"basemap":null,"seriesBy":null,"layout":null,"pyramid":null,"tooltip":[],"ignoredDashboardFilters":[],"scale":[]}""";

    private const string RichBefore =
        """{"indicator":"households_by_source","measure":"value","ratio":null,"groupBy":"municipality","filters":[{"column":"round","op":"in","value":null,"values":["2"],"min":null,"max":null}],"geometryColumn":null,"buckets":null,"sort":{"by":"value","column":null,"compareAs":null,"direction":"desc"},"basemap":null,"seriesBy":"water_source","layout":"stacked","pyramid":null,"tooltip":[],"ignoredDashboardFilters":["round"],"scale":[]}""";

    /// <summary>A stored config as the API reads it: through the input record, with the defaults applied.</summary>
    private static WidgetConfig Read(string json) => (JsonSerializer.Deserialize<WidgetConfigInput>(json, Json) ?? new WidgetConfigInput()).Normalize();

    [Fact]
    public void A_config_saved_before_has_no_display_and_serializes_exactly_as_before()
    {
        var minimal = new WidgetConfigInput("coverage", GroupBy: "municipality").Normalize();
        var rich = new WidgetConfigInput(
            "households_by_source", "value", null, "municipality", [new DataFilter("round", "in", Values: ["2"])], null, null,
            new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc), null, null, ["round"], "water_source", SeriesLayouts.Stacked).Normalize();

        Assert.Null(minimal.Display);
        Assert.Null(rich.Display);
        Assert.Equal(MinimalBefore, JsonSerializer.Serialize(minimal, Json));
        Assert.Equal(RichBefore, JsonSerializer.Serialize(rich, Json));
        Assert.DoesNotContain("display", JsonSerializer.Serialize(minimal, Json));
    }

    [Fact]
    public void Reading_a_stored_config_without_a_display_and_writing_it_again_changes_nothing()
    {
        foreach (var stored in new[] { MinimalBefore, RichBefore, """{"indicator":"coverage"}""" })
        {
            var config = Read(stored);

            Assert.Null(config.Display);
            Assert.Equal(JsonSerializer.Serialize(Read(JsonSerializer.Serialize(config, Json)), Json), JsonSerializer.Serialize(config, Json));
            Assert.DoesNotContain("display", JsonSerializer.Serialize(config, Json));
        }
    }

    [Fact]
    public void A_display_with_a_color_is_written_with_only_its_color_and_read_back()
    {
        var config = new WidgetConfigInput("coverage", GroupBy: "municipality", Display: new WidgetDisplay(Color: 3)).Normalize();

        var json = JsonSerializer.Serialize(config, Json);

        Assert.Contains(""","display":{"color":3},""", json);
        Assert.Equal(new WidgetDisplay(3), Read(json).Display);
        Assert.Equal(json, JsonSerializer.Serialize(Read(json), Json));
    }

    [Fact]
    public void A_display_with_a_scale_is_written_with_only_its_scale_and_read_back()
    {
        var config = new WidgetConfigInput("coverage", GroupBy: "municipality", GeometryColumn: "geometry", Display: new WidgetDisplay(Ramp: "green")).Normalize();

        var json = JsonSerializer.Serialize(config, Json);

        Assert.Contains(""","display":{"ramp":"green"},""", json);
        Assert.Equal(new WidgetDisplay(null, "green"), Read(json).Display);
    }

    [Fact]
    public void A_display_of_both_survives_the_round_trip_and_an_empty_one_does_not_appear()
    {
        var both = Read("""{"indicator":"coverage","display":{"color":8,"ramp":"gray"}}""");
        var empty = Read("""{"indicator":"coverage","display":{}}""");
        var nulls = Read("""{"indicator":"coverage","display":{"color":null,"ramp":null}}""");
        var blank = Read("""{"indicator":"coverage","display":{"ramp":""}}""");

        Assert.Equal(new WidgetDisplay(8, "gray"), Read(JsonSerializer.Serialize(both, Json)).Display);
        Assert.All(new[] { empty, nulls, blank }, config =>
        {
            Assert.Null(config.Display);
            Assert.DoesNotContain("display", JsonSerializer.Serialize(config, Json));
        });
    }

    [Fact]
    public void A_display_with_an_orientation_is_written_with_only_its_orientation_and_read_back()
    {
        var config = new WidgetConfigInput("household_income", GroupBy: "municipality", Display: new WidgetDisplay(Orientation: BoxOrientations.Horizontal)).Normalize();

        var json = JsonSerializer.Serialize(config, Json);

        Assert.Contains(""","display":{"orientation":"horizontal"},""", json);
        Assert.Equal(new WidgetDisplay(Orientation: "horizontal"), Read(json).Display);
        Assert.Equal(json, JsonSerializer.Serialize(Read(json), Json));
    }

    [Fact]
    public void A_saved_vertical_orientation_is_kept_as_it_is_and_an_empty_one_is_none()
    {
        var vertical = Read("""{"indicator":"household_income","display":{"orientation":"vertical"}}""");
        var blank = Read("""{"indicator":"household_income","display":{"orientation":""}}""");
        var all = Read("""{"indicator":"household_income","display":{"color":4,"ramp":"green","orientation":"horizontal"}}""");

        Assert.Equal("vertical", vertical.Display?.Orientation);
        Assert.Contains("\"display\":{\"orientation\":\"vertical\"}", JsonSerializer.Serialize(vertical, Json));
        Assert.Null(blank.Display);
        Assert.Equal(new WidgetDisplay(4, "green", "horizontal"), Read(JsonSerializer.Serialize(all, Json)).Display);
    }

    [Fact]
    public void A_display_set_to_null_in_the_json_is_none()
    {
        var config = Read("""{"indicator":"coverage","display":null}""");

        Assert.Null(config.Display);
        Assert.DoesNotContain("display", JsonSerializer.Serialize(config, Json));
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Pie)]
    [InlineData(ChartTypes.Map)]
    [InlineData(ChartTypes.Heatmap)]
    [InlineData(ChartTypes.Pyramid)]
    [InlineData(ChartTypes.Likert)]
    [InlineData(ChartTypes.Crosstab)]
    [InlineData(ChartTypes.Boxplot)]
    public void A_query_never_carries_the_display(string chartType)
    {
        var config = new WidgetConfigInput(
            "coverage", GroupBy: "municipality", SeriesBy: "water_source", GeometryColumn: "geometry", Display: new WidgetDisplay(3, "green")).Normalize();

        var query = WidgetQuery.For(chartType, config);

        Assert.Null(query.Display);
        Assert.DoesNotContain("display", JsonSerializer.Serialize(query, Json));
    }
}
