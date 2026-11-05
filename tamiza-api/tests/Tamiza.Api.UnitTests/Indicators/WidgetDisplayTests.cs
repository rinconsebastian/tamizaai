using System.Text.Json;
using System.Text.Json.Nodes;
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
            "coverage", GroupBy: "municipality", SeriesBy: "water_source", GeometryColumn: "geometry",
            Display: new WidgetDisplay(3, "green", Order: new WidgetCategoryOrder(["Cali"], ["Piped"]))).Normalize();

        var query = WidgetQuery.For(chartType, config);

        Assert.Null(query.Display);
        Assert.DoesNotContain("display", JsonSerializer.Serialize(query, Json));
        Assert.DoesNotContain("order", JsonSerializer.Serialize(query, Json));
    }

    // Column labels (design D5 of the indicator column metadata)

    [Fact]
    public void A_display_with_only_labels_is_written_with_only_its_labels_and_read_back_unchanged()
    {
        const string stored = """{"indicator":"teatro_hoy","display":{"labels":{"numerator":"Hogares que asistieron"}}}""";

        var config = Read(stored);
        var json = JsonSerializer.Serialize(config, Json);

        Assert.Equal(new Dictionary<string, string> { ["numerator"] = "Hogares que asistieron" }, config.Display!.Labels);
        Assert.Contains(""","display":{"labels":{"numerator":"Hogares que asistieron"}},""", json);
        Assert.Equal(json, JsonSerializer.Serialize(Read(json), Json));
        Assert.Null(config.Display.Color);
    }

    [Fact]
    public void Keys_are_kept_as_given_and_sorted_so_the_order_sent_does_not_matter()
    {
        var one = Read("""{"indicator":"teatro_hoy","display":{"labels":{"numerator":"Hogares","comuna":"Comuna","_version":"Versión"}}}""");
        var other = Read("""{"indicator":"teatro_hoy","display":{"labels":{"_version":"Versión","comuna":"Comuna","numerator":"Hogares"}}}""");

        Assert.Equal(["_version", "comuna", "numerator"], one.Display!.Labels!.Keys);
        Assert.Equal(JsonSerializer.Serialize(one, Json), JsonSerializer.Serialize(other, Json));
    }

    [Fact]
    public void Labels_are_trimmed_and_null_or_empty_ones_are_dropped()
    {
        var config = Read("""{"indicator":"teatro_hoy","display":{"color":2,"labels":{"numerator":"  Hogares  ","denominator":"","comuna":"   ","estrato":null}}}""");

        Assert.Equal(new Dictionary<string, string> { ["numerator"] = "Hogares" }, config.Display!.Labels);
        Assert.Equal(2, config.Display.Color);
    }

    [Theory]
    [InlineData("""{"indicator":"teatro_hoy","display":{"labels":{}}}""")]
    [InlineData("""{"indicator":"teatro_hoy","display":{"labels":{"numerator":"","denominator":null}}}""")]
    [InlineData("""{"indicator":"teatro_hoy","display":{"labels":null}}""")]
    public void A_display_holding_only_empty_or_null_labels_is_no_display(string stored)
    {
        var config = Read(stored);

        Assert.Null(config.Display);
        Assert.DoesNotContain("display", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void Labels_with_the_other_settings_survive_the_round_trip()
    {
        var config = Read("""{"indicator":"teatro_hoy","display":{"color":4,"ramp":"green","orientation":"horizontal","labels":{"value":"Hogares"}}}""");

        var again = Read(JsonSerializer.Serialize(config, Json));

        Assert.Equal(4, again.Display!.Color);
        Assert.Equal("green", again.Display.Ramp);
        Assert.Equal("horizontal", again.Display.Orientation);
        Assert.Equal(new Dictionary<string, string> { ["value"] = "Hogares" }, again.Display.Labels);
    }

    [Fact]
    public void A_config_with_labels_reads_back_to_the_same_text_whatever_order_the_store_gives_the_keys()
    {
        // jsonb gives the keys of an object back shorter first; the text compared by an update is the config as the API reads it.
        var sent = new WidgetConfigInput("teatro_hoy", Display: new WidgetDisplay(Labels: new Dictionary<string, string> { ["denominator"] = "Hogares", ["numerator"] = "Asistentes" })).Normalize();
        var fromStore = Read("""{"indicator": "teatro_hoy", "display": {"labels": {"numerator": "Asistentes", "denominator": "Hogares"}}}""");

        Assert.Equal(JsonSerializer.Serialize(fromStore, Json), JsonSerializer.Serialize(sent, Json));
    }

    // The widget's texts (design D1 and D2 of the widget context)

    [Fact]
    public void The_texts_of_a_widget_round_trip_unchanged_and_in_the_order_of_the_record()
    {
        const string stored =
            """{"display":{"subtitle":"¿En qué lugares ha visto teatro en el último año? (E4)","footnote":"No suman 100 %: respuesta múltiple.","caption":{"hidden":true},"unit":"de quienes asistieron","showDescription":true}}""";

        var config = Read("""{"indicator":"encuesta_hoy",""" + stored[1..]);
        var json = JsonSerializer.Serialize(config, Json);

        // The default encoder writes accents as unicode escapes, which jsonb stores as the characters: the texts are compared as JSON, and
        // the members in the order the record declares them.
        var display = JsonNode.Parse(json)!["display"]!.AsObject();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(stored)!["display"], display));
        Assert.Equal(["subtitle", "footnote", "caption", "unit", "showDescription"], display.Select(member => member.Key));
        Assert.Equal(json, JsonSerializer.Serialize(Read(json), Json));
        Assert.Equal("¿En qué lugares ha visto teatro en el último año? (E4)", config.Display!.Subtitle);
        Assert.Equal("No suman 100 %: respuesta múltiple.", config.Display.Footnote);
        Assert.Equal(new WidgetCaption(null, true), config.Display.Caption);
        Assert.Equal("de quienes asistieron", config.Display.Unit);
        Assert.True(config.Display.ShowDescription);
    }

    [Fact]
    public void A_caption_with_a_text_round_trips_unchanged()
    {
        const string stored = """{"indicator":"encuesta_hoy","display":{"caption":{"text":"% de quienes asistieron"}}}""";

        var json = JsonSerializer.Serialize(Read(stored), Json);

        Assert.Contains(""","display":{"caption":{"text":"% de quienes asistieron"}},""", json);
        Assert.Equal(json, JsonSerializer.Serialize(Read(json), Json));
    }

    [Fact]
    public void A_display_with_a_color_labels_and_a_subtitle_writes_the_old_members_first()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"subtitle":"Pregunta","unit":"hogares","labels":{"value":"Hogares"},"color":3,"ramp":"green"}}""");

        Assert.Contains(""","display":{"color":3,"ramp":"green","labels":{"value":"Hogares"},"subtitle":"Pregunta","unit":"hogares"},""", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void A_display_holding_only_a_subtitle_is_kept()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"subtitle":"Pregunta"}}""");

        Assert.Equal(new WidgetDisplay(Subtitle: "Pregunta"), config.Display);
        Assert.Contains(""","display":{"subtitle":"Pregunta"},""", JsonSerializer.Serialize(config, Json));
    }

    [Theory]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"subtitle":"   "}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"footnote":""}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"unit":" "}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"showDescription":false}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"caption":{"hidden":false}}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"caption":{"text":" "}}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"caption":{}}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"caption":null,"subtitle":null,"footnote":null,"unit":null,"showDescription":null}}""")]
    public void A_display_holding_only_blank_texts_false_or_an_empty_caption_is_no_display(string stored)
    {
        var config = Read(stored);

        Assert.Null(config.Display);
        Assert.DoesNotContain("display", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void Blank_texts_and_false_next_to_a_real_setting_are_left_out()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"color":2,"subtitle":" ","showDescription":false,"caption":{"hidden":false,"text":""}}}""");

        Assert.Equal(new WidgetDisplay(Color: 2), config.Display);
        Assert.Contains(""","display":{"color":2},""", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void The_texts_are_stored_trimmed()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"subtitle":"  Pregunta  ","footnote":" Nota ","unit":" hogares ","caption":{"text":"  Mi texto "}}}""");

        Assert.Equal("Pregunta", config.Display!.Subtitle);
        Assert.Equal("Nota", config.Display.Footnote);
        Assert.Equal("hogares", config.Display.Unit);
        Assert.Equal("Mi texto", config.Display.Caption!.Text);
    }

    [Fact]
    public void Only_spaces_are_trimmed_so_a_line_break_at_the_end_of_a_text_is_kept_for_validation_to_refuse()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"subtitle":" Pregunta\n ","unit":"\thogares","footnote":" Nota "}}""");

        Assert.Equal("Pregunta\n", config.Display!.Subtitle);
        Assert.Equal("\thogares", config.Display.Unit);
        Assert.Equal("Nota", config.Display.Footnote); // a no-break space is a space
    }

    [Fact]
    public void A_caption_with_a_text_and_hidden_is_kept_so_validation_can_refuse_it()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"caption":{"text":"Texto","hidden":true}}}""");

        Assert.Equal(new WidgetCaption("Texto", true), config.Display!.Caption);
    }

    [Fact]
    public void The_limits_are_the_ones_the_design_gives()
    {
        Assert.Equal(200, WidgetDisplay.MaxSubtitleLength);
        Assert.Equal(500, WidgetDisplay.MaxFootnoteLength);
        Assert.Equal(200, WidgetDisplay.MaxCaptionLength);
        Assert.Equal(4, WidgetDisplay.MaxReferenceLines);
        Assert.Equal(40, WidgetDisplay.MaxReferenceLineLabel);
    }

    // Reference lines, steps and data labels (design D1 of the chart annotations)

    [Fact]
    public void A_display_with_lines_steps_and_data_labels_round_trips_unchanged()
    {
        const string stored =
            """{"indicator":"encuesta_hoy","display":{"referenceLines":[{"kind":"mean"},{"kind":"value","value":0.5,"label":"Meta"}],"interpolation":"step","dataLabels":true}}""";

        var config = Read(stored);
        var json = JsonSerializer.Serialize(config, Json);

        Assert.Contains(""","display":{"referenceLines":[{"kind":"mean"},{"kind":"value","value":0.5,"label":"Meta"}],"interpolation":"step","dataLabels":true},""", json);
        Assert.Equal(json, JsonSerializer.Serialize(Read(json), Json));
        Assert.Equal([new WidgetReferenceLine("mean"), new WidgetReferenceLine("value", 0.5, "Meta")], config.Display!.ReferenceLines);
        Assert.Equal("step", config.Display.Interpolation);
        Assert.True(config.Display.DataLabels);
    }

    [Fact]
    public void A_display_with_everything_writes_the_new_members_last_in_the_order_of_the_record()
    {
        var config = Read(
            """{"indicator":"encuesta_hoy","display":{"dataLabels":true,"interpolation":"linear","referenceLines":[{"kind":"overall"}],"showDescription":true,"unit":"hogares","caption":{"hidden":true},"footnote":"Nota","subtitle":"Pregunta","labels":{"value":"Hogares"},"orientation":"vertical","ramp":"green","color":3}}""");

        var display = JsonNode.Parse(JsonSerializer.Serialize(config, Json))!["display"]!.AsObject();

        Assert.Equal(
            ["color", "ramp", "orientation", "labels", "subtitle", "footnote", "caption", "unit", "showDescription", "referenceLines", "interpolation", "dataLabels"],
            display.Select(member => member.Key));
    }

    [Fact]
    public void A_display_without_the_new_settings_serializes_as_it_did_before()
    {
        const string stored = """{"color":3,"labels":{"value":"Hogares"},"subtitle":"Pregunta","unit":"hogares","showDescription":true}""";

        var json = JsonSerializer.Serialize(Read("""{"indicator":"encuesta_hoy","display":""" + stored + "}"), Json);

        Assert.Contains(",\"display\":" + stored + ",", json);
        Assert.DoesNotContain("referenceLines", json);
        Assert.DoesNotContain("interpolation", json);
        Assert.DoesNotContain("dataLabels", json);
    }

    [Fact]
    public void A_saved_linear_interpolation_and_false_data_labels_are_kept_as_given()
    {
        var linear = Read("""{"indicator":"encuesta_hoy","display":{"interpolation":"linear"}}""");
        var off = Read("""{"indicator":"encuesta_hoy","display":{"dataLabels":false}}""");
        var both = Read("""{"indicator":"encuesta_hoy","display":{"interpolation":"linear","dataLabels":false}}""");

        Assert.Contains(""","display":{"interpolation":"linear"},""", JsonSerializer.Serialize(linear, Json));
        Assert.Equal(new WidgetDisplay(DataLabels: false), off.Display);
        Assert.Contains(""","display":{"dataLabels":false},""", JsonSerializer.Serialize(off, Json));
        Assert.Contains(""","display":{"interpolation":"linear","dataLabels":false},""", JsonSerializer.Serialize(both, Json));
    }

    [Theory]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"referenceLines":[]}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"referenceLines":null,"interpolation":"","dataLabels":null}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"interpolation":""}}""")]
    public void A_display_holding_only_an_empty_list_of_lines_or_an_empty_interpolation_is_no_display(string stored)
    {
        var config = Read(stored);

        Assert.Null(config.Display);
        Assert.DoesNotContain("display", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void An_empty_list_of_lines_next_to_a_real_setting_is_left_out()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"color":2,"referenceLines":[],"interpolation":""}}""");

        Assert.Equal(new WidgetDisplay(Color: 2), config.Display);
        Assert.Contains(""","display":{"color":2},""", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void A_label_of_spaces_is_none_and_the_others_are_trimmed()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"referenceLines":[{"kind":"mean","label":"   "},{"kind":"median","label":""},{"kind":"value","value":2,"label":"  Meta  "}]}}""");

        Assert.Equal([new WidgetReferenceLine("mean"), new WidgetReferenceLine("median"), new WidgetReferenceLine("value", 2, "Meta")], config.Display!.ReferenceLines);
        Assert.Contains("""[{"kind":"mean"},{"kind":"median"},{"kind":"value","value":2,"label":"Meta"}]""", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void A_null_line_and_a_line_with_no_kind_are_kept_for_validation_to_name()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"referenceLines":[null,{"label":"Meta"},{"kind":""}]}}""");

        var lines = config.Display!.ReferenceLines!;
        Assert.Equal(3, lines.Count);
        Assert.Null(lines[0]);
        Assert.Equal(new WidgetReferenceLine(null, null, "Meta"), lines[1]);
        Assert.Equal("", lines[2]!.Kind);
    }

    [Fact]
    public void Lines_keep_the_order_the_author_gave_and_the_value_as_a_number()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"referenceLines":[{"kind":"value","value":0.333},{"kind":"overall"},{"kind":"mean"},{"kind":"value","value":150}]}}""");

        Assert.Equal(["value", "overall", "mean", "value"], config.Display!.ReferenceLines!.Select(line => line!.Kind));
        Assert.Contains("""{"kind":"value","value":0.333},{"kind":"overall"},{"kind":"mean"},{"kind":"value","value":150}""", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void A_query_carries_none_of_the_new_settings()
    {
        var config = new WidgetConfigInput(
            "encuesta_hoy", GroupBy: "zona", Display: new WidgetDisplay(ReferenceLines: [new WidgetReferenceLine("mean")], Interpolation: "step", DataLabels: true)).Normalize();

        var query = WidgetQuery.For(ChartTypes.Line, config);

        Assert.Null(query.Display);
        Assert.DoesNotContain("referenceLines", JsonSerializer.Serialize(query, Json));
    }

    // The declared category order (design D7 of add-declared-category-order)

    /// <summary>The display as the stored text holds it, read back as JSON so that the way the serializer escapes a letter does not matter.</summary>
    private static JsonNode? StoredDisplay(WidgetConfig config) => JsonNode.Parse(JsonSerializer.Serialize(config, Json))!["display"];

    [Fact]
    public void A_display_with_an_order_of_series_round_trips_unchanged()
    {
        const string stored = """{"order":{"series":["Acceso","Interés","Otra"]}}""";

        var config = Read("""{"indicator":"encuesta_hoy","display":""" + stored + "}");

        Assert.Equal(["Acceso", "Interés", "Otra"], config.Display!.Order!.Series);
        Assert.Null(config.Display.Order.Group);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(stored), StoredDisplay(config)));
        Assert.Equal(JsonSerializer.Serialize(config, Json), JsonSerializer.Serialize(Read(JsonSerializer.Serialize(config, Json)), Json));
    }

    [Fact]
    public void An_order_of_groups_and_of_series_is_written_group_first_and_exact()
    {
        const string stored = """{"order":{"group":["1. Sí"," 2. Tal vez ","","Sin dato"],"series":["a"]}}""";

        var config = Read("""{"indicator":"encuesta_hoy","display":""" + stored + "}");

        Assert.Equal(["1. Sí", " 2. Tal vez ", "", "Sin dato"], config.Display!.Order!.Group);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(stored), StoredDisplay(config)));
    }

    [Fact]
    public void A_display_with_everything_writes_the_order_last()
    {
        var config = Read(
            """{"indicator":"encuesta_hoy","display":{"order":{"group":["a"]},"dataLabels":true,"interpolation":"linear","referenceLines":[{"kind":"overall"}],"showDescription":true,"unit":"hogares","caption":{"hidden":true},"footnote":"Nota","subtitle":"Pregunta","labels":{"value":"Hogares"},"orientation":"vertical","ramp":"green","color":3}}""");

        var display = JsonNode.Parse(JsonSerializer.Serialize(config, Json))!["display"]!.AsObject();

        Assert.Equal(
            ["color", "ramp", "orientation", "labels", "subtitle", "footnote", "caption", "unit", "showDescription", "referenceLines", "interpolation", "dataLabels", "order"],
            display.Select(member => member.Key));
    }

    [Fact]
    public void A_display_holding_only_an_order_is_kept()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"order":{"group":["Sí"]}}}""");

        Assert.NotNull(config.Display);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"order":{"group":["Sí"]}}"""), StoredDisplay(config)));
    }

    [Theory]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"order":{"group":[]}}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"order":{"group":[],"series":[]}}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"order":{}}}""")]
    [InlineData("""{"indicator":"encuesta_hoy","display":{"order":null}}""")]
    public void A_display_holding_only_an_empty_order_is_no_display(string stored)
    {
        var config = Read(stored);

        Assert.Null(config.Display);
        Assert.DoesNotContain("display", JsonSerializer.Serialize(config, Json));
    }

    [Fact]
    public void An_empty_list_next_to_a_real_one_and_next_to_another_setting_is_left_out()
    {
        var one = Read("""{"indicator":"encuesta_hoy","display":{"order":{"group":[],"series":["No"]}}}""");
        var other = Read("""{"indicator":"encuesta_hoy","display":{"color":2,"order":{"series":[]}}}""");

        Assert.Contains(""","display":{"order":{"series":["No"]}},""", JsonSerializer.Serialize(one, Json));
        Assert.Equal(new WidgetDisplay(Color: 2), other.Display);
        Assert.Contains(""","display":{"color":2},""", JsonSerializer.Serialize(other, Json));
    }

    [Fact]
    public void A_null_value_in_an_order_is_kept_for_validation_to_name()
    {
        var config = Read("""{"indicator":"encuesta_hoy","display":{"order":{"group":["Sí",null,"No"]}}}""");

        var group = config.Display!.Order!.Group!;
        Assert.Equal(3, group.Count);
        Assert.Null(group[1]);
    }

    [Fact]
    public void A_display_without_an_order_serializes_as_it_did_before()
    {
        const string stored = """{"color":3,"labels":{"value":"Hogares"},"subtitle":"Pregunta","referenceLines":[{"kind":"mean"}],"interpolation":"step","dataLabels":true}""";

        var json = JsonSerializer.Serialize(Read("""{"indicator":"encuesta_hoy","display":""" + stored + "}"), Json);

        Assert.Contains(",\"display\":" + stored + ",", json);
        Assert.DoesNotContain("order", json);
    }

    [Fact]
    public void A_query_carries_no_order()
    {
        var config = new WidgetConfigInput(
            "encuesta_hoy", GroupBy: "zona", SeriesBy: "respuesta", Display: new WidgetDisplay(Order: new WidgetCategoryOrder(["Zona 2"], ["Sí", "No"]))).Normalize();

        var query = WidgetQuery.For(ChartTypes.Column, config);

        Assert.Null(query.Display);
        Assert.DoesNotContain("order", JsonSerializer.Serialize(query, Json));
    }
}
