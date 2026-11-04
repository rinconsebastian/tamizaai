using System.Text.Json;
using System.Text.Json.Nodes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// The shapes of widget that a real project's dashboards use (add-indicator-column-metadata, design D13), against goldens written from
/// the code before the changes of the widget and dashboard program. Every later change of the program runs this test and regenerates no
/// golden: a change to a stored config, a query, a plan or a version's content shows here. The names and values are invented; only the
/// structure is a real project's. The tests do nothing when the UI is not next to the API (the fixture is the UI's).
/// </summary>
public sealed class ProductionShapesTests
{
    private const string FixtureFile = "tamiza-ui/src/app/pages/projects/indicators/testing/production-shapes.json";

    /// <summary>The name a table is read from, as the planner is given it by the query endpoint.</summary>
    private const string From = "\"p_x_indicators\".\"encuesta_hoy\"";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JsonNode? Fixture()
    {
        var path = PromptKitChecks.FindUp(FixtureFile);
        return path is null ? null : JsonNode.Parse(File.ReadAllText(path));
    }

    private static JsonArray? Shapes() => Fixture()?["shapes"]?.AsArray();

    public static TheoryData<string> ShapeNames()
    {
        var data = new TheoryData<string>();
        foreach (var shape in Shapes() ?? [])
        {
            data.Add(shape!["name"]!.GetValue<string>());
        }

        return data;
    }

    private static JsonNode Shape(string name) => Shapes()!.Single(s => s!["name"]!.GetValue<string>() == name)!;

    /// <summary>A stored config as the API reads it: <c>DashboardEndpoints.ReadConfig</c>, which is internal, step for step.</summary>
    internal static WidgetConfig Read(string json) =>
        (JsonSerializer.Deserialize<WidgetConfigInput>(json, Web) ?? new WidgetConfigInput()).Normalize();

    internal static string Write(WidgetConfig config) => JsonSerializer.Serialize(config, Web);

    internal static IReadOnlyList<TableColumn> ColumnsOf(JsonNode shape) =>
        shape["columns"]!.AsArray()
            .Select(c => new TableColumn(c!["name"]!.GetValue<string>(), Enum.Parse<DataColumnType>(c["type"]!.GetValue<string>(), true), null))
            .ToList();

    /// <summary>The query the page sends for the shape, as the server's twin builds it.</summary>
    internal static JsonNode QueryOf(JsonNode shape) =>
        JsonSerializer.SerializeToNode(WidgetQuery.For(shape["chartType"]!.GetValue<string>(), Read(shape["stored"]!.GetValue<string>())), Web)!;

    internal static IndicatorQueryPlan PlanFor(JsonNode shape)
    {
        var chartType = shape["chartType"]!.GetValue<string>();
        var query = WidgetQuery.For(chartType, Read(shape["stored"]!.GetValue<string>())).Normalize();
        return IndicatorQueryBuilder.Plan(query, ColumnsOf(shape), "UTC", From, 5000, perRow: chartType == ChartTypes.Histogram, chartType: chartType);
    }

    /// <summary>A plan as text can be compared: its SQL texts and the names of its parameters, what is built for the database.</summary>
    internal static JsonNode Describe(IndicatorQueryPlan plan)
    {
        static JsonObject Sql(IndicatorSql sql) => new()
        {
            ["sql"] = sql.Sql,
            ["parameters"] = new JsonArray(sql.Parameters.Select(p => (JsonNode?)p.Name).ToArray()),
        };

        static JsonArray Names(IEnumerable<string> names) => new(names.Select(n => (JsonNode?)n).ToArray());

        var node = new JsonObject();
        if (plan.Errors is not null)
        {
            node["errors"] = JsonSerializer.SerializeToNode(plan.Errors, Web);
        }

        if (plan.MissingColumns.Count > 0)
        {
            node["missingColumns"] = Names(plan.MissingColumns);
        }

        if (plan.NotFiltered is { Count: > 0 } notFiltered)
        {
            node["notFiltered"] = Names(notFiltered);
        }

        if (plan.Sql is not null)
        {
            node["main"] = Sql(plan.Sql);
        }

        if (plan.Domain is not null)
        {
            node["domain"] = Sql(plan.Domain);
        }

        if (plan.Boxes is not null)
        {
            node["boxes"] = Sql(plan.Boxes);
        }

        if (plan.Series is { } series)
        {
            var described = new JsonObject
            {
                ["maxSeries"] = series.MaxSeries,
                ["naturalGroups"] = series.NaturalGroups,
            };
            if (series.Domain is not null)
            {
                described["domain"] = Sql(series.Domain);
            }

            if (series.FixedDomain is not null)
            {
                described["fixedDomain"] = Names(series.FixedDomain);
            }

            // The statement does not depend on the values kept, only its @kept parameter's value does.
            described["cells"] = Sql(series.Cells(series.FixedDomain ?? []));
            node["series"] = described;
        }

        return node;
    }

    /// <summary>The version content of the fixture's dashboard, built from what the dashboard and its widgets hold.</summary>
    internal static string ContentOf(JsonNode dashboard, JsonArray shapes)
    {
        JsonNode ShapeOf(JsonNode widget) => shapes.Single(s => s!["name"]!.GetValue<string>() == widget["shape"]!.GetValue<string>())!;
        var widgets = dashboard["widgets"]!.AsArray().Select(w => new DashboardWidget
        {
            Id = Guid.Parse(w!["id"]!.GetValue<string>()),
            ChartType = ShapeOf(w)["chartType"]!.GetValue<string>(),
            Title = w["title"]!.GetValue<string>(),
            Config = ShapeOf(w)["stored"]!.GetValue<string>(),
            Layout = w["layout"]!.ToJsonString(),
            CreatedAt = DateTimeOffset.Parse(w["createdAt"]!.GetValue<string>()),
        });
        return DashboardVersions.Content(
            dashboard["name"]!.GetValue<string>(),
            dashboard["filters"]!.ToJsonString(),
            dashboard["palette"]!.GetValue<string>(),
            dashboard["valueColors"]!.ToJsonString(),
            dashboard["source"]?.GetValue<string>(),
            widgets);
    }

    [Fact]
    public void The_fixture_holds_each_shape_the_design_lists_once()
    {
        if (Shapes() is not { } shapes)
        {
            return;
        }

        var names = shapes.Select(s => s!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(25, names.Count);
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(
            [ChartTypes.Column, ChartTypes.Column, ChartTypes.Bar, ChartTypes.Bar, ChartTypes.Bar, ChartTypes.Bar, ChartTypes.Stat, ChartTypes.Stat, ChartTypes.Line,
             ChartTypes.Boxplot, ChartTypes.Map, ChartTypes.Heatmap, ChartTypes.Likert, ChartTypes.Crosstab, ChartTypes.Pie,
             // The shapes of the widget texts (add-widget-context)
             ChartTypes.Bar, ChartTypes.Column, ChartTypes.Stat, ChartTypes.Map, ChartTypes.Likert,
             // The shapes of the chart annotations (add-chart-annotations)
             ChartTypes.Column, ChartTypes.Bar, ChartTypes.Line, ChartTypes.Bar, ChartTypes.Column],
            shapes.Select(s => s!["chartType"]!.GetValue<string>()));
        Assert.All(shapes, s => Assert.True(ChartTypes.IsKnown(s!["chartType"]!.GetValue<string>())));
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void A_stored_config_reads_and_writes_back_byte_for_byte(string name)
    {
        var stored = Shape(name)["stored"]!.GetValue<string>();

        Assert.Equal(stored, Write(Read(stored)));
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void The_query_a_shape_sends_is_its_golden(string name)
    {
        var shape = Shape(name);

        Assert.Equal(shape["query"]!.ToJsonString(), QueryOf(shape).ToJsonString());
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void The_plan_of_a_shape_is_its_golden(string name)
    {
        var shape = Shape(name);
        var plan = PlanFor(shape);

        Assert.Null(plan.Errors);
        Assert.Empty(plan.MissingColumns);
        Assert.Equal(shape["plan"]!.ToJsonString(), Describe(plan).ToJsonString());
    }

    /// <summary>
    /// What the widget form sends when it is saved without a change reads back, through the API's reader, to the config that is
    /// stored: the API then finds nothing changed and makes no dashboard version (<c>UpdateWidgetAsync</c> compares exactly this).
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void A_widget_form_saved_without_a_change_makes_no_version(string name)
    {
        var shape = Shape(name);
        var formSaved = Assert.IsAssignableFrom<JsonNode>(shape["formSaved"]);

        Assert.Equal(shape["stored"]!.GetValue<string>(), Write(Read(formSaved.ToJsonString())));
    }

    /// <summary>The members of a widget's display that the widget context adds (design D1).</summary>
    private static readonly string[] ContextMembers = ["subtitle", "footnote", "caption", "unit", "showDescription"];

    /// <summary>The same shape with its config stored without the texts of the widget context.</summary>
    private static JsonNode WithoutTheTexts(JsonNode shape) => WithoutMembers(shape, ContextMembers);

    /// <summary>The same shape with its config stored without these members of its display.</summary>
    private static JsonNode WithoutMembers(JsonNode shape, string[] members)
    {
        var stored = JsonNode.Parse(shape["stored"]!.GetValue<string>())!.AsObject();
        if (stored["display"] is JsonObject display)
        {
            foreach (var member in members)
            {
                display.Remove(member);
            }

            if (display.Count == 0)
            {
                stored.Remove("display");
            }
        }

        var without = shape.DeepClone();
        without["stored"] = Write(Read(stored.ToJsonString()));
        return without;
    }

    /// <summary>
    /// The texts of a widget are drawn, never queried (design D1 of the widget context): the query and the plan of a shape that holds them
    /// are those of the same config without them, computed here, and are the shape's goldens.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void The_query_and_the_plan_of_a_shape_are_those_of_the_same_config_without_the_texts(string name)
    {
        var shape = Shape(name);
        var without = WithoutTheTexts(shape);

        Assert.Equal(shape["query"]!.ToJsonString(), QueryOf(without).ToJsonString());
        Assert.Equal(shape["plan"]!.ToJsonString(), Describe(PlanFor(without)).ToJsonString());
        Assert.Equal(QueryOf(shape).ToJsonString(), QueryOf(without).ToJsonString());
        Assert.Equal(Describe(PlanFor(shape)).ToJsonString(), Describe(PlanFor(without)).ToJsonString());
    }

    [Fact]
    public void The_shapes_of_the_widget_texts_hold_each_text_once_or_more_and_the_others_hold_none()
    {
        if (Shapes() is not { } shapes)
        {
            return;
        }

        var held = shapes
            .Select(s => JsonNode.Parse(s!["stored"]!.GetValue<string>())!["display"] as JsonObject)
            .Where(display => display is not null && ContextMembers.Any(display.ContainsKey))
            .ToList();

        Assert.Equal(5, held.Count);
        foreach (var member in ContextMembers)
        {
            Assert.Contains(held, display => display!.ContainsKey(member));
        }

        Assert.Contains(held, display => display!["caption"] is JsonObject caption && caption.ContainsKey("hidden"));
        Assert.Contains(held, display => display!["caption"] is JsonObject caption && caption.ContainsKey("text"));
    }

    /// <summary>The members of a widget's display that the chart annotations add (design D1 of add-chart-annotations).</summary>
    private static readonly string[] AnnotationMembers = ["referenceLines", "interpolation", "dataLabels"];

    private static bool HoldsAnnotations(JsonNode shape) =>
        JsonNode.Parse(shape["stored"]!.GetValue<string>())!["display"] is JsonObject display && AnnotationMembers.Any(display.ContainsKey);

    /// <summary>
    /// Reference lines, steps and data labels are drawn, never queried (design D1 of the chart annotations): the query and the plan of a
    /// shape that holds them are those of the same config without them, computed here, and are the shape's goldens.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void The_query_and_the_plan_of_a_shape_are_those_of_the_same_config_without_the_annotations(string name)
    {
        var shape = Shape(name);
        var without = WithoutMembers(shape, AnnotationMembers);

        Assert.Equal(shape["query"]!.ToJsonString(), QueryOf(without).ToJsonString());
        Assert.Equal(shape["plan"]!.ToJsonString(), Describe(PlanFor(without)).ToJsonString());
        Assert.Equal(QueryOf(shape).ToJsonString(), QueryOf(without).ToJsonString());
        Assert.Equal(Describe(PlanFor(shape)).ToJsonString(), Describe(PlanFor(without)).ToJsonString());
    }

    /// <summary>
    /// The overall flag is derived from the saved widget and no shape's stored config holds it (design D4 of the chart annotations): it
    /// is false for every shape of the earlier changes, and true among the new ones only for the ratio column chart with an overall line.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void Only_the_ratio_column_chart_with_an_overall_line_asks_for_the_overall_value(string name)
    {
        var shape = Shape(name);

        var asks = WidgetQuery.Overall(shape["chartType"]!.GetValue<string>(), Read(shape["stored"]!.GetValue<string>()));

        Assert.Equal(name == "ratio column chart with a mean line and an overall line", asks);
        Assert.DoesNotContain("overall\":true", shape["stored"]!.GetValue<string>());
    }

    [Fact]
    public void Only_the_last_five_shapes_hold_the_annotations_and_each_holds_its_own_kind()
    {
        if (Shapes() is not { } shapes)
        {
            return;
        }

        var held = shapes.Where(s => HoldsAnnotations(s!)).Select(s => s!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(shapes.Skip(20).Select(s => s!["name"]!.GetValue<string>()), held);

        var displays = shapes.Skip(20).Select(s => (JsonObject)JsonNode.Parse(s!["stored"]!.GetValue<string>())!["display"]!).ToList();
        Assert.Equal(["mean", "overall"], displays[0]["referenceLines"]!.AsArray().Select(l => l!["kind"]!.GetValue<string>()));
        Assert.True(displays[1]["dataLabels"]!.GetValue<bool>());
        Assert.Equal("step", displays[2]["interpolation"]!.GetValue<string>());
        Assert.Equal(0.5, displays[3]["referenceLines"]![0]!["value"]!.GetValue<double>());
        Assert.Equal(["overall"], displays[4]["referenceLines"]!.AsArray().Select(l => l!["kind"]!.GetValue<string>()));
    }

    /// <summary>
    /// What the page sends with a widget workbook request is the saved config (design D10 of the widget context), which the endpoint
    /// validates as a save does: no shape's config may be refused for its kind. A shape with no UI goldens yet is checked as it is stored.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void The_saved_config_of_a_shape_passes_the_checks_of_its_chart_type(string name)
    {
        var shape = Shape(name);
        var chartType = shape["chartType"]!.GetValue<string>();
        var sent = shape["saved"]?.ToJsonString() ?? shape["stored"]!.GetValue<string>();

        var errors = new FieldErrors();
        IndicatorValidation.CheckConfig(Read(sent), chartType, "config.", errors);

        Assert.Empty(errors.ToDictionary());
    }

    [Fact]
    public void The_version_content_of_the_dashboard_is_its_golden()
    {
        if (Fixture() is not { } fixture)
        {
            return;
        }

        var dashboard = fixture["dashboard"]!;
        Assert.Equal(dashboard["content"]!.GetValue<string>(), ContentOf(dashboard, fixture["shapes"]!.AsArray()));
        Assert.Null(dashboard["source"]);
        Assert.DoesNotContain("\"source\"", dashboard["content"]!.GetValue<string>());
    }

    [Fact]
    public void The_version_content_of_the_dashboard_with_a_source_is_its_golden_and_reads_back_the_same()
    {
        if (Fixture() is not { } fixture)
        {
            return;
        }

        var dashboard = fixture["dashboardWithSource"]!;
        var content = ContentOf(dashboard, fixture["shapes"]!.AsArray());
        Assert.Equal(dashboard["content"]!.GetValue<string>(), content);
        // The source follows the colors kept by value and comes before the widgets.
        Assert.Equal(["name", "filters", "palette", "valueColors", "source", "widgets"], JsonNode.Parse(content)!.AsObject().Select(m => m.Key));

        var read = DashboardVersions.ReadContent(content);
        Assert.Equal(dashboard["source"]!.GetValue<string>(), read.Source);
        Assert.Equal(content, DashboardVersions.Content(read.Name, read.Filters, read.Palette, read.ValueColors, read.Source,
            read.Widgets.Select((w, i) => new DashboardWidget
            {
                Id = w.Id,
                Title = w.Title,
                ChartType = w.ChartType,
                Config = w.Config,
                Layout = w.Layout,
                CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(i),
            })));
    }

    [Fact]
    public void The_dashboard_with_a_source_keeps_the_settings_of_the_one_without_it()
    {
        if (Fixture() is not { } fixture)
        {
            return;
        }

        var without = JsonNode.Parse(fixture["dashboard"]!["content"]!.GetValue<string>())!.AsObject();
        var with = JsonNode.Parse(fixture["dashboardWithSource"]!["content"]!.GetValue<string>())!.AsObject();

        Assert.Equal(without["filters"]!.ToJsonString(), with["filters"]!.ToJsonString());
        Assert.Equal(without["palette"]!.ToJsonString(), with["palette"]!.ToJsonString());
        Assert.Equal(without["valueColors"]!.ToJsonString(), with["valueColors"]!.ToJsonString());
        Assert.NotNull(with["source"]);
    }
}
