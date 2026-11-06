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

    /// <param name="bases">What the shape's indicator declares for its columns, resolved against its table; null when it declares nothing.</param>
    internal static IndicatorQueryPlan PlanFor(JsonNode shape, IndicatorBases? bases = null)
    {
        var chartType = shape["chartType"]!.GetValue<string>();
        var query = WidgetQuery.For(chartType, Read(shape["stored"]!.GetValue<string>())).Normalize();
        return IndicatorQueryBuilder.Plan(query, ColumnsOf(shape), "UTC", From, 5000, perRow: chartType == ChartTypes.Histogram, chartType: chartType, bases: bases);
    }

    /// <summary>
    /// A statement without the figure outputs that <see cref="IndicatorSql.Figures"/> lists (design D5 of add-small-sample-warnings): the
    /// sums a ratio and a base column append after every output a select list had, and the named sums and columns of a series statement's
    /// groups and cells. What is left is the text the statement had before, which the shapes' goldens hold.
    /// </summary>
    internal static string WithoutFigures(string sql)
    {
        const string Sum = @"sum\(""[^""]+""\)::double precision";
        // The sums of a group and a cell, named for the rows that read them, and the columns the branches select from them.
        sql = System.Text.RegularExpressions.Regex.Replace(sql, $@", {Sum} AS f[ndb]\b", "");
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @", (?:cell\.)?f[ndb]\b", "");
        // The "left out" branch's NULLs, after its last column.
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"(NULL::bigint)(?:, NULL::double precision)+", "$1");
        // The sums appended to a plain select list and to a series total, and the widget's window.
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @", \(sum\(sum\(""[^""]+""\)\) OVER \(\)\)::double precision", "");
        // A plan's aggregates are always parenthesized, so a sum that follows a comma directly is one of the appended outputs.
        return System.Text.RegularExpressions.Regex.Replace(sql, $@", {Sum}(?:, {Sum})*", "");
    }

    /// <summary>
    /// A plan as text can be compared: its SQL texts and the names of its parameters, what is built for the database. The figure outputs
    /// of design D5 of add-small-sample-warnings are removed unless <paramref name="removeFigures"/> is false.
    /// </summary>
    internal static JsonNode Describe(IndicatorQueryPlan plan, bool removeFigures = true)
    {
        JsonObject Sql(IndicatorSql sql) => new()
        {
            ["sql"] = removeFigures ? WithoutFigures(sql.Sql) : sql.Sql,
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
            dashboard["minN"]?.GetValue<int>(),
            dashboard["showN"]?.GetValue<bool>() ?? false,
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
        Assert.Equal(32, names.Count);
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(
            [ChartTypes.Column, ChartTypes.Column, ChartTypes.Bar, ChartTypes.Bar, ChartTypes.Bar, ChartTypes.Bar, ChartTypes.Stat, ChartTypes.Stat, ChartTypes.Line,
             ChartTypes.Boxplot, ChartTypes.Map, ChartTypes.Heatmap, ChartTypes.Likert, ChartTypes.Crosstab, ChartTypes.Pie,
             // The shapes of the widget texts (add-widget-context)
             ChartTypes.Bar, ChartTypes.Column, ChartTypes.Stat, ChartTypes.Map, ChartTypes.Likert,
             // The shapes of the chart annotations (add-chart-annotations)
             ChartTypes.Column, ChartTypes.Bar, ChartTypes.Line, ChartTypes.Bar, ChartTypes.Column,
             // The shapes of the declared category order (add-declared-category-order)
             ChartTypes.Column, ChartTypes.Bar, ChartTypes.Heatmap,
             // The shapes of the base settings (add-small-sample-warnings)
             ChartTypes.Column, ChartTypes.Column,
             // The shapes of the stat tiles (improve-stat-cards)
             ChartTypes.Stat, ChartTypes.Stat],
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
    /// A measure, a box plot and a histogram have no figure outputs (design D5 and D15 of add-small-sample-warnings): their plan equals its
    /// golden as it is, with nothing removed. A ratio's is compared with its figure outputs removed, by the test above.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void The_plan_of_a_measure_a_box_plot_or_a_histogram_is_its_golden_with_nothing_removed(string name)
    {
        var shape = Shape(name);
        var chartType = shape["chartType"]!.GetValue<string>();
        if (Read(shape["stored"]!.GetValue<string>()).Ratio is not null && chartType is not (ChartTypes.Boxplot or ChartTypes.Histogram))
        {
            return;
        }

        Assert.Equal(shape["plan"]!.ToJsonString(), Describe(PlanFor(shape), removeFigures: false).ToJsonString());
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

    /// <summary>The shape of a stat tile with a statistic and secondary lines (improve-stat-cards, task 1.5), the last of the fixture.</summary>
    private const string StatisticShape = "stat tile with a median and both secondary lines";

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
    public void Only_the_five_annotation_shapes_hold_the_annotations_and_each_holds_its_own_kind()
    {
        if (Shapes() is not { } shapes)
        {
            return;
        }

        var annotated = shapes.Skip(20).Take(5).ToList();
        var held = shapes.Where(s => HoldsAnnotations(s!)).Select(s => s!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(annotated.Select(s => s!["name"]!.GetValue<string>()), held);

        var displays = annotated.Select(s => (JsonObject)JsonNode.Parse(s!["stored"]!.GetValue<string>())!["display"]!).ToList();
        Assert.Equal(["mean", "overall"], displays[0]["referenceLines"]!.AsArray().Select(l => l!["kind"]!.GetValue<string>()));
        Assert.True(displays[1]["dataLabels"]!.GetValue<bool>());
        Assert.Equal("step", displays[2]["interpolation"]!.GetValue<string>());
        Assert.Equal(0.5, displays[3]["referenceLines"]![0]!["value"]!.GetValue<double>());
        Assert.Equal(["overall"], displays[4]["referenceLines"]!.AsArray().Select(l => l!["kind"]!.GetValue<string>()));
    }

    /// <summary>The settings of add-small-sample-warnings that a shape's stored config can hold.</summary>
    private static readonly string[] BaseMembers = ["minN", "showN"];

    private static bool HoldsBaseSettings(JsonNode shape)
    {
        var stored = JsonNode.Parse(shape["stored"]!.GetValue<string>())!.AsObject();
        return stored.ContainsKey("missingFooter") || (stored["display"] is JsonObject display && BaseMembers.Any(display.ContainsKey));
    }

    /// <summary>The same shape with its config stored without its base settings: the two members of its display and <c>missingFooter</c>.</summary>
    private static JsonNode WithoutBaseSettings(JsonNode shape)
    {
        var without = WithoutMembers(shape, BaseMembers);
        var stored = JsonNode.Parse(without["stored"]!.GetValue<string>())!.AsObject();
        stored.Remove("missingFooter");
        without["stored"] = Write(Read(stored.ToJsonString()));
        return without;
    }

    /// <summary>
    /// The base settings of a widget only decide what is drawn, and <c>missingFooter</c> only leaves rows out when its indicator declares
    /// values (design D15 of add-small-sample-warnings): the plan of a shape that holds them is the plan of the same config without them,
    /// and so is its query, apart from the option that <c>missingFooter</c> adds to it. Both are the shape's goldens.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void The_query_and_the_plan_of_a_shape_are_those_of_the_same_config_without_the_base_settings(string name)
    {
        var shape = Shape(name);
        var without = WithoutBaseSettings(shape);

        Assert.Equal(shape["plan"]!.ToJsonString(), Describe(PlanFor(without)).ToJsonString());
        Assert.Equal(Describe(PlanFor(shape)).ToJsonString(), Describe(PlanFor(without)).ToJsonString());
        Assert.Equal(shape["query"]!.ToJsonString(), QueryOf(shape).ToJsonString());
        var expected = QueryOf(without);
        if (JsonNode.Parse(shape["stored"]!.GetValue<string>())!["missingFooter"] is { } missingFooter)
        {
            expected["missingFooter"] = missingFooter.DeepClone();
        }

        Assert.True(JsonNode.DeepEquals(expected, QueryOf(shape)), $"{name}: {QueryOf(shape).ToJsonString()}");
    }

    [Fact]
    public void Only_the_two_base_shapes_hold_base_settings_and_each_holds_its_own()
    {
        if (Shapes() is not { } shapes)
        {
            return;
        }

        Assert.Equal(
            ["ratio column chart with a small-base threshold and the n line", "ratio column chart that leaves out the values that mean no data"],
            shapes.Where(s => HoldsBaseSettings(s!)).Select(s => s!["name"]!.GetValue<string>()));
        var threshold = JsonNode.Parse(shapes[28]!["stored"]!.GetValue<string>())!;
        Assert.Equal((30, true), (threshold["display"]!["minN"]!.GetValue<int>(), threshold["display"]!["showN"]!.GetValue<bool>()));
        Assert.Null(threshold["missingFooter"]);
        var missing = JsonNode.Parse(shapes[29]!["stored"]!.GetValue<string>())!;
        Assert.True(missing["missingFooter"]!.GetValue<bool>());
        Assert.Null(missing["display"]);
        // The query of the second carries the option; the first's holds no setting.
        Assert.True(shapes[29]!["query"]!["missingFooter"]!.GetValue<bool>());
        Assert.Null(shapes[28]!["query"]!["missingFooter"]);
    }

    /// <summary>
    /// Every shape planned again with its indicator declaring a base column, no-data values and answer columns, and no setting on (design
    /// D15 of add-small-sample-warnings): the plotted statements, once the figure outputs are removed, are the shape's golden, a value that
    /// means no data leaves nothing out (<c>missingFooter</c> is off), and the only statement that is added is the one that counts the
    /// answers, run for a widget whose marks have an n over answer columns it declares. The exception is a ratio over answer columns that
    /// its table has and the map declares (design D4 of improve-stat-cards): single choice counts the base once across the answers, and
    /// multiple choice has no value where its rows hold several, with a check that runs first. Its declared plan is not the golden, which
    /// is the plan of an indicator that declares nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void A_shape_planned_with_declared_bases_and_no_setting_on_keeps_its_plotted_statements(string name)
    {
        if (Fixture()?["declared"]?["maps"]?.AsArray() is not { } maps)
        {
            return;
        }

        var shape = Shape(name);
        var chartType = shape["chartType"]!.GetValue<string>();
        var hasRatio = Read(shape["stored"]!.GetValue<string>()).Ratio is not null;
        foreach (var map in maps)
        {
            var label = $"{name} with {map!["name"]}";
            var bases = IndicatorBases.Resolve(ColumnMetadata.Read(map["columns"]!.ToJsonString()), ColumnsOf(shape));

            var plan = PlanFor(shape, bases);

            Assert.Null(plan.Errors);
            if (name == StatisticShape)
            {
                // A statistic counts the values it covers, whatever the indicator declares (design D10 of improve-stat-cards): no answers
                // statement, and its n comes from the statement itself, so only its own statement is compared.
                Assert.True(plan.Counted is null && plan.Sql!.Sql.Contains("percentile_cont", StringComparison.Ordinal) && plan.NoData is null && plan.Overall is null, label);
                continue;
            }

            var described = Describe(plan).ToJsonString();
            var stored = Read(shape["stored"]!.GetValue<string>());
            var answers = AnswerColumns.Of(stored, false, bases, ColumnsOf(shape), DataSources.Quote);
            // Multiple choice only changes the text of a tile and of a widget with a series (their totals have no value over several
            // answers); a plain grouped widget keeps its statement and gains the check that runs first.
            var rewritten = answers.Corrects || (answers.Refuses && (stored.GroupBy is null || stored.SeriesBy is not null));
            Assert.True(answers.Refuses == (plan.Combined is not null), label);
            if (rewritten)
            {
                var marker = answers.Corrects ? "__num" : "count(DISTINCT (";
                Assert.True(described.Contains(marker, StringComparison.Ordinal) && shape["plan"]!.ToJsonString() != described, $"{label}: {described}");
            }
            // A value that means no data leaves a row out only for a widget that asks for it (missingFooter) and whose group column declares one.
            var group = Read(shape["stored"]!.GetValue<string>()).GroupBy;
            var leavesOut = HoldsBaseSettings(shape) && group is not null && bases?.Missing.ContainsKey(group) == true
                && JsonNode.Parse(shape["stored"]!.GetValue<string>())!["missingFooter"] is not null;
            if (rewritten)
            {
                Assert.True(plan.NoData is null, label);
            }
            else if (leavesOut)
            {
                Assert.True(described.Contains("AND NOT ((", StringComparison.Ordinal) && plan.NoData is not null, $"{label}: {described}");
            }
            else
            {
                Assert.True(shape["plan"]!.ToJsonString() == described, $"{label}: {described}");
                Assert.True(plan.NoData is null, label);
            }

            Assert.True(plan.Overall is null, label);
            var counted = plan.Counted is not null || plan.Series?.Counted is not null;
            var expected = bases is { Answers.Count: > 0 } && chartType is not (ChartTypes.Boxplot or ChartTypes.Histogram) && (hasRatio || bases.BaseColumn is not null);
            Assert.True(expected == counted, $"{label}: the answers statement {(counted ? "was" : "was not")} planned");
        }
    }

    [Fact]
    public void The_declared_maps_are_invented_valid_and_between_them_declare_each_property_on_the_columns_of_the_shapes()
    {
        if (Fixture() is not { } fixture || fixture["declared"]?["maps"]?.AsArray() is not { } maps)
        {
            return;
        }

        var all = fixture["shapes"]!.AsArray();
        var columns = all.SelectMany(s => s!["columns"]!.AsArray().Select(c => c!["name"]!.GetValue<string>())).ToHashSet();
        Assert.True(maps.Count >= 3);
        foreach (var map in maps)
        {
            var read = ColumnMetadata.Read(map!["columns"]!.ToJsonString())!;
            var errors = new FieldErrors();
            ColumnMetadata.Check(read.ToDictionary(e => e.Key, e => (IndicatorColumnMetadata?)e.Value), errors);
            Assert.Empty(errors.ToDictionary());
            // Each column a map names is a column of some shape, so a declaration is always about a real one.
            Assert.All(read.Keys, key => Assert.Contains(key, columns));
        }

        var entries = maps.SelectMany(m => ColumnMetadata.Read(m!["columns"]!.ToJsonString())!.Values).ToList();
        Assert.Contains(entries, e => e.Base is true);
        Assert.Contains(entries, e => e.Missing is { Count: > 0 });
        Assert.Contains(entries, e => e.Categories == AnswerKinds.Single);
        Assert.Contains(entries, e => e.Categories == AnswerKinds.Multiple);
    }

    /// <summary>
    /// An indicator that gives its group and series columns only a label declares no order and no hidden prefixes (add-declared-category-order,
    /// task 1.1): no order applies to the shapes that carry such columns, so every consumer runs today's code for them.
    /// </summary>
    [Fact]
    public void No_order_applies_to_a_shape_whose_indicator_only_labels_its_group_and_series_columns()
    {
        if (Shapes() is not { } shapes)
        {
            return;
        }

        var labelled = shapes.Where(s => s!["indicatorColumns"] is not null).ToList();
        Assert.Equal(9, labelled.Count);
        foreach (var shape in labelled)
        {
            var columns = ColumnMetadata.Read(shape!["indicatorColumns"]!.ToJsonString());

            var orders = CategoryOrder.For(shape["chartType"]!.GetValue<string>(), Read(shape["stored"]!.GetValue<string>()), columns);

            Assert.True(orders is null, shape["name"]!.GetValue<string>());
        }
    }

    /// <summary>
    /// What the page sends with a widget workbook request is the saved config (design D10 of the widget context), which the endpoint
    /// validates as a save does: no shape's config may be refused for its kind.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void The_saved_config_of_a_shape_passes_the_checks_of_its_chart_type(string name)
    {
        var shape = Shape(name);
        var chartType = shape["chartType"]!.GetValue<string>();
        var sent = Assert.IsAssignableFrom<JsonNode>(shape["saved"]).ToJsonString();

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
        Assert.Equal(content, DashboardVersions.Content(read.Name, read.Filters, read.Palette, read.ValueColors, read.Source, read.MinN, read.ShowN,
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

    /// <summary>
    /// The statistic is a query setting and the line without filters a flag of the request (design D2 and D3 of improve-stat-cards): no
    /// earlier shape holds either, so their query carries no <c>statistic</c> and none asks for the value without the dashboard filters;
    /// the last shape holds both and asks for it. The statistic shape's query is that of the same config without its lines, plus its statistic.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void Only_the_statistic_shape_has_a_statistic_and_asks_for_the_value_without_the_dashboard_filters(string name)
    {
        var shape = Shape(name);
        var chartType = shape["chartType"]!.GetValue<string>();
        var config = Read(shape["stored"]!.GetValue<string>());
        var holds = name == StatisticShape;

        Assert.Equal(holds ? "median" : null, config.Statistic);
        Assert.Equal(holds ? "median" : null, WidgetQuery.For(chartType, config).Statistic);
        Assert.Equal(holds, QueryOf(shape)["statistic"] is not null);
        Assert.Equal(holds, WidgetQuery.Unfiltered(chartType, config));
        Assert.Equal(holds, config.Display?.Secondary is not null);
        Assert.DoesNotContain("\"statistic\"", holds ? "" : shape["stored"]!.GetValue<string>());
        Assert.DoesNotContain("secondary", holds ? "" : shape["stored"]!.GetValue<string>());
    }

    [Fact]
    public void The_statistic_shape_holds_the_median_and_both_lines_and_comes_last_after_the_ratio_tile()
    {
        if (Shapes() is not { } shapes)
        {
            return;
        }

        var tile = JsonNode.Parse(shapes[31]!["stored"]!.GetValue<string>())!;
        Assert.Equal(StatisticShape, shapes[31]!["name"]!.GetValue<string>());
        Assert.Equal("median", tile["statistic"]!.GetValue<string>());
        Assert.Equal(["n", "unfiltered"], tile["display"]!["secondary"]!.AsArray().Select(line => line!.GetValue<string>()));
        // The goldens of the page (task 6.2): the saved config and the form's body hold the statistic and both lines, and the query the page
        // sends holds the statistic and no display, and none of them holds the derived flag.
        foreach (var golden in new[] { "saved", "formSaved" })
        {
            var held = Assert.IsAssignableFrom<JsonNode>(shapes[31]![golden]);
            Assert.Equal("median", held["statistic"]!.GetValue<string>());
            Assert.Equal(["n", "unfiltered"], held["display"]!["secondary"]!.AsArray().Select(line => line!.GetValue<string>()));
        }

        var uiQuery = Assert.IsAssignableFrom<JsonNode>(shapes[31]!["uiQuery"]);
        Assert.Equal("median", uiQuery["statistic"]!.GetValue<string>());
        Assert.Null(uiQuery["display"]);
        foreach (var golden in new[] { "saved", "uiQuery", "formSaved" })
        {
            Assert.DoesNotContain("unfiltered\":", shapes[31]![golden]!.ToJsonString());
        }

        // The ratio tile before it plots a ratio over a question column and an answer column and holds neither setting.
        var ratio = Read(shapes[30]!["stored"]!.GetValue<string>());
        Assert.NotNull(ratio.Ratio);
        Assert.Equal(["pregunta", "respuesta"], ratio.Filters.Select(f => f.Column));
        Assert.Null(ratio.Statistic);
        Assert.Null(ratio.Display);
    }

    [Fact]
    public void The_statistic_shape_is_planned_as_one_statement_with_its_n_and_the_unfiltered_statement_when_a_filter_narrows_it()
    {
        if (Shapes() is not { } shapes)
        {
            return;
        }

        var shape = shapes[31]!;
        var config = Read(shape["stored"]!.GetValue<string>());
        AppliedDashboardFilter[] dashboard = [new("zona", DashboardFilterKinds.Text, Values: ["Zona 1"])];

        var plan = IndicatorQueryBuilder.Plan(
            WidgetQuery.For(ChartTypes.Stat, config).Normalize(), ColumnsOf(shape), "UTC", From, 5000, false, dashboard, ChartTypes.Stat, false, null, null,
            WidgetQuery.Unfiltered(ChartTypes.Stat, config));

        Assert.Contains("percentile_cont(0.5)", plan.Sql!.Sql);
        Assert.Contains("\"zona\"::text = ANY(@d0)", plan.Sql.Sql);
        Assert.NotNull(plan.Unfiltered);
        Assert.DoesNotContain("@d0", plan.Unfiltered.Sql);
        Assert.Equal(shape["plan"]!["main"]!["sql"]!.GetValue<string>(), plan.Unfiltered.Sql);
    }
}
