using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>POST /projects/{id}/indicators/query</c> for a box plot: the statistics of every box, computed over all the rows the query
/// matches (design D3, D4 of add-boxplot-widget). The reference figures were computed with pandas' <c>quantile</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorBoxQueryTests(PostgresFixture postgres)
{
    private const string Plain = "municipality text, value numeric";

    private static string Values(string group, params double[] values) =>
        string.Join(", ", values.Select(v => $"('{group}', {v.ToString(CultureInfo.InvariantCulture)})"));

    private async Task<IndicatorsHarness> WithAsync(string rows, string columns = Plain, int? maxRows = null)
    {
        var h = await IndicatorsHarness.CreateAsync(
            postgres, maxRows is null ? null : new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = maxRows.Value.ToString(CultureInfo.InvariantCulture) });
        await h.PublishAsync("household_income", columns, rows);
        return h;
    }

    private static object Config(string? groupBy = "municipality", object? sort = null) =>
        new { indicator = "household_income", groupBy, sort };

    private static JsonElement[] Boxes(JsonElement result) => result.GetProperty("boxes").EnumerateArray().ToArray();

    private static string[] Groups(JsonElement result) =>
        Boxes(result).Select(b => b.GetProperty("group").ValueKind == JsonValueKind.Null ? "-" : b.GetProperty("group").GetString()!).ToArray();

    private static double Number(JsonElement box, string name) => box.GetProperty(name).GetDouble();

    private static double[] Outliers(JsonElement box) => box.GetProperty("outliers").EnumerateArray().Select(o => o.GetDouble()).ToArray();

    [Fact]
    public async Task Four_values_give_the_quartiles_pandas_gives()
    {
        await using var h = await WithAsync(Values("A", 1, 2, 3, 4));

        var result = await h.QueryAsync(Config(), "boxplot");
        var box = Assert.Single(Boxes(result));

        Assert.Equal(4, box.GetProperty("values").GetInt64());
        Assert.Equal(new[] { 1, 1.75, 2.5, 3.25, 4, 2.5 }, new[] { "min", "q1", "median", "q3", "max", "mean" }.Select(n => Number(box, n)).ToArray());
        Assert.Equal(new[] { 1.0, 4.0 }, new[] { Number(box, "lowerWhisker"), Number(box, "upperWhisker") });
        Assert.Equal(0, box.GetProperty("outlierCount").GetInt64());
        Assert.Empty(Outliers(box));
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
    }

    [Fact]
    public async Task A_value_beyond_the_fences_is_an_outlier_and_the_whiskers_stop_at_the_last_value_inside()
    {
        await using var h = await WithAsync(Values("A", 1, 2, 3, 4, 20));

        var box = Assert.Single(Boxes(await h.QueryAsync(Config(), "boxplot")));

        // Q1 2 and Q3 4: the fences are -1 and 7.
        Assert.Equal(new[] { 2.0, 3.0, 4.0 }, new[] { Number(box, "q1"), Number(box, "median"), Number(box, "q3") });
        Assert.Equal(new[] { 1.0, 4.0 }, new[] { Number(box, "lowerWhisker"), Number(box, "upperWhisker") });
        Assert.Equal(20, Number(box, "max"));
        Assert.Equal(1, box.GetProperty("outlierCount").GetInt64());
        Assert.Equal(new[] { 20.0 }, Outliers(box));
    }

    [Fact]
    public async Task Fifty_values_match_the_figures_pandas_computes()
    {
        double[] values =
        [
            0.0, 55.75, 111.5, 250.0, 71.5, 127.25, 31.5, 85.5, 141.25, 45.5, 101.25, 5.5, 61.25, 117.0, 19.5, 75.25, 131.0, -80.0, 91.0, 146.75, 51.0,
            105.0, 9.25, 65.0, 120.75, 25.0, 80.75, 136.5, 39.0, 94.75, 150.5, 54.75, 110.5, 14.75, 70.5, 124.5, 28.75, 84.5, 140.25, 44.5, 100.25, 4.5,
            58.5, 114.25, 18.5, 74.25, 130.0, 34.25, 90.0, 144.0,
        ];
        await using var h = await WithAsync(Values("A", values));

        var box = Assert.Single(Boxes(await h.QueryAsync(Config(), "boxplot")));

        // pandas: quantile([0.25, 0.5, 0.75]) = 40.375, 78.0, 116.3125; mean 78.145; Tukey whiskers 0.0 and 150.5; outliers -80 and 250.
        Assert.Equal(50, box.GetProperty("values").GetInt64());
        Assert.Equal(40.375, Number(box, "q1"), 9);
        Assert.Equal(78.0, Number(box, "median"), 9);
        Assert.Equal(116.3125, Number(box, "q3"), 9);
        Assert.Equal(78.145, Number(box, "mean"), 9);
        Assert.Equal(new[] { -80.0, 250.0 }, new[] { Number(box, "min"), Number(box, "max") });
        Assert.Equal(new[] { 0.0, 150.5 }, new[] { Number(box, "lowerWhisker"), Number(box, "upperWhisker") });
        Assert.Equal(new[] { -80.0, 250.0 }, Outliers(box));
    }

    [Fact]
    public async Task One_value_has_equal_figures_equal_whisker_ends_and_no_outliers()
    {
        await using var h = await WithAsync(Values("A", 7));

        var box = Assert.Single(Boxes(await h.QueryAsync(Config(), "boxplot")));

        Assert.Equal(1, box.GetProperty("values").GetInt64());
        Assert.All(new[] { "min", "q1", "median", "q3", "max", "mean", "lowerWhisker", "upperWhisker" }, n => Assert.Equal(7, Number(box, n)));
        Assert.Equal(0, box.GetProperty("outlierCount").GetInt64());
    }

    [Fact]
    public async Task Rows_are_values_and_are_never_summed_and_the_plain_rows_stand_for_the_boxes()
    {
        await using var h = await WithAsync(Values("Cali", 400, 900));

        var result = await h.QueryAsync(Config(), "boxplot");
        var box = Assert.Single(Boxes(result));

        Assert.Equal(2, box.GetProperty("values").GetInt64());
        Assert.Equal(650, Number(box, "median"));
        // rows[i] stands for boxes[i]: the median, and how many values the box summarizes.
        var row = Assert.Single(result.GetProperty("rows").EnumerateArray());
        Assert.Equal(("Cali", 650.0, 2L), (row.GetProperty("group").GetString(), row.GetProperty("value").GetDouble(), row.GetProperty("rows").GetInt64()));
    }

    [Fact]
    public async Task A_missing_group_is_a_box_of_its_own_listed_last_and_no_group_column_gives_one_box()
    {
        await using var h = await WithAsync(Values("A", 1, 3) + ", (NULL, 10), (NULL, 30), (NULL, 20)");

        var grouped = await h.QueryAsync(Config(), "boxplot");
        var all = await h.QueryAsync(Config(groupBy: null), "boxplot");

        Assert.Equal(["A", "-"], Groups(grouped));
        Assert.Equal(20, Number(Boxes(grouped)[1], "median"));
        Assert.Equal(3, Boxes(grouped)[1].GetProperty("values").GetInt64());
        Assert.Equal(["-"], Groups(all));
        var box = Assert.Single(Boxes(all));
        Assert.Equal(5, box.GetProperty("values").GetInt64());
        Assert.Equal(10, Number(box, "median"));
    }

    [Fact]
    public async Task A_ratio_is_taken_per_row_and_rows_with_nothing_to_plot_are_left_out()
    {
        await using var h = await WithAsync(
            "('A', 3, 4), ('A', 1, 0), ('A', NULL, NULL), ('A', 5, NULL), ('A', 1, 4)", "municipality text, numerator numeric, denominator numeric");

        var result = await h.QueryAsync(
            new { indicator = "household_income", groupBy = "municipality", ratio = new { numerator = "numerator", denominator = "denominator" } }, "boxplot");
        var box = Assert.Single(Boxes(result));

        // 3/4 and 1/4 are the two values: a zero denominator and a missing numerator or denominator add none.
        Assert.Equal(2, box.GetProperty("values").GetInt64());
        Assert.Equal(new[] { 0.25, 0.5, 0.75 }, new[] { Number(box, "min"), Number(box, "median"), Number(box, "max") });
    }

    [Fact]
    public async Task Null_nan_and_infinite_values_are_left_out()
    {
        await using var h = await WithAsync(
            "('A', 'NaN'), ('A', 'Infinity'), ('A', '-Infinity'), ('A', NULL), ('A', 1), ('A', 3)", "municipality text, value double precision");

        var box = Assert.Single(Boxes(await h.QueryAsync(Config(), "boxplot")));

        Assert.Equal(2, box.GetProperty("values").GetInt64());
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, new[] { Number(box, "min"), Number(box, "median"), Number(box, "max") });
    }

    [Fact]
    public async Task Widget_and_dashboard_filters_apply_before_the_statistics()
    {
        await using var h = await WithAsync(
            "('Cali', 'F', 10), ('Cali', 'F', 20), ('Cali', 'M', 100), ('Buga', 'F', 5)", "municipality text, sex text, value numeric");

        var widget = await h.QueryAsync(
            new { indicator = "household_income", groupBy = "municipality", filters = new[] { new { column = "municipality", op = "equals", value = "Cali" } } }, "boxplot");
        var dashboard = await h.QueryAsync(Config(), "boxplot", dashboardFilters: new[] { new { column = "sex", kind = "text", values = new[] { "F" } } });

        Assert.Equal(["Cali"], Groups(widget));
        Assert.Equal(3, Boxes(widget)[0].GetProperty("values").GetInt64());
        Assert.Equal(["Buga", "Cali"], Groups(dashboard));
        Assert.Equal(15, Number(Boxes(dashboard)[1], "median"));
        Assert.Equal(2, Boxes(dashboard)[1].GetProperty("values").GetInt64());
    }

    [Fact]
    public async Task A_box_draws_the_hundred_outliers_farthest_from_its_median_and_counts_them_all()
    {
        await using var h = await WithAsync("", Plain);
        // 1,000 values of 0 to 9 (median about 4.5, no fences wider than 4) and 130 far outliers 1,000 to 1,129.
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".household_income SELECT 'A', g % 10 FROM generate_series(1, 1000) g");
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".household_income SELECT 'A', 1000 + g FROM generate_series(0, 129) g");

        var box = Assert.Single(Boxes(await h.QueryAsync(Config(), "boxplot")));

        Assert.Equal(1130, box.GetProperty("values").GetInt64());
        Assert.Equal(130, box.GetProperty("outlierCount").GetInt64());
        var drawn = Outliers(box);
        Assert.Equal(100, drawn.Length);
        // The farthest 100 of 1,000..1,129 are 1,030..1,129, in ascending order.
        Assert.Equal(1030, drawn[0]);
        Assert.Equal(1129, drawn[^1]);
        Assert.Equal(drawn.Order().ToArray(), drawn);
    }

    [Fact]
    public async Task Groups_of_every_type_are_boxes_and_a_timestamp_group_reads_as_every_other_widgets_does()
    {
        await using var h = await WithAsync(
            "('t', true, '2026-09-01 21:30:00-05', '2026-09-01', 10, 1), ('t', false, '2026-09-01 21:30:00-05', '2026-09-02', 2, 2), " +
            "('t', true, '2026-09-02 08:00:00-05', '2026-09-01', 1, 3), ('t', false, '2026-09-02 08:00:00-05', '2026-09-02', 10, 4)",
            "municipality text, flag boolean, seen timestamptz, day date, code bigint, value numeric");

        var flag = await h.QueryAsync(Config("flag"), "boxplot");
        var code = await h.QueryAsync(Config("code"), "boxplot");
        var day = await h.QueryAsync(Config("day"), "boxplot");
        var seen = await h.QueryAsync(Config("seen"), "boxplot");
        var column = await h.QueryAsync(Config("seen"), "column");

        Assert.Equal(["false", "true"], Groups(flag));
        Assert.Equal(["1", "2", "10"], Groups(code));
        Assert.Equal(["2026-09-01", "2026-09-02"], Groups(day));
        Assert.Equal(2, Boxes(code)[2].GetProperty("values").GetInt64());
        Assert.Equal(Groups(seen), column.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()!).ToArray());
        Assert.Contains("T", Groups(seen)[0]);
    }

    [Fact]
    public async Task Without_an_order_the_boxes_follow_natural_order_and_a_median_order_keeps_equal_medians_in_that_order()
    {
        await using var h = await WithAsync(
            Values("10-14", 1) + ", " + Values("5-9", 1) + ", " + Values("0-4", 1) + ", " + Values("80+", 1) + ", " + Values("B", 300) + ", " + Values("A", 300) + ", " + Values("C", 500));

        var natural = await h.QueryAsync(Config(), "boxplot");
        var descending = await h.QueryAsync(Config(sort: new { by = "value", direction = "desc" }), "boxplot");
        var ascending = await h.QueryAsync(Config(sort: new { by = "value", direction = "asc" }), "boxplot");

        Assert.Equal(["0-4", "5-9", "10-14", "80+", "A", "B", "C"], Groups(natural));
        Assert.Equal(["C", "A", "B", "0-4", "5-9", "10-14", "80+"], Groups(descending));
        Assert.Equal(["0-4", "5-9", "10-14", "80+", "A", "B", "C"], Groups(ascending));
    }

    [Fact]
    public async Task The_row_limit_counts_boxes_and_not_the_rows_that_make_them_up()
    {
        await using var h = await WithAsync("", Plain, maxRows: 5);
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".household_income SELECT 'g' || (g % 4), g FROM generate_series(1, 200000) g");

        var within = await h.QueryAsync(Config(), "boxplot");

        Assert.Equal(4, Boxes(within).Length);
        Assert.False(within.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(50000, Boxes(within)[0].GetProperty("values").GetInt64());
    }

    [Fact]
    public async Task Past_the_limit_an_order_by_median_keeps_the_boxes_with_the_largest_medians_and_says_so()
    {
        await using var h = await WithAsync(string.Join(", ", Enumerable.Range(1, 8).Select(i => Values($"g{i}", i * 10))), Plain, maxRows: 5);

        var result = await h.QueryAsync(Config(sort: new { by = "value", direction = "desc" }), "boxplot");

        Assert.Equal(["g8", "g7", "g6", "g5", "g4"], Groups(result));
        Assert.True(result.GetProperty("rowLimitReached").GetBoolean());
    }

    [Fact]
    public async Task A_column_the_table_lost_makes_the_widget_stale_and_runs_nothing()
    {
        await using var h = await WithAsync(Values("A", 1, 2));

        var result = await h.QueryAsync(Config("round"), "boxplot");

        Assert.False(result.TryGetProperty("boxes", out _));
        Assert.Equal(["round"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task The_other_kinds_answer_with_no_boxes()
    {
        await using var h = await WithAsync(Values("A", 1, 2));

        var result = await h.QueryAsync(Config(), "column");

        Assert.False(result.TryGetProperty("boxes", out _));
    }

    [Fact]
    public async Task A_box_plot_is_saved_with_its_orientation_and_read_back_as_saved()
    {
        await using var h = await WithAsync(Values("A", 1, 2));

        var horizontal = await h.AddWidgetAsync(new
        {
            title = "Income", chartType = "boxplot",
            config = new { indicator = "household_income", groupBy = "municipality", display = new { color = 3, orientation = "horizontal" } },
        });
        var vertical = await h.AddWidgetAsync(new
        {
            title = "Income 2", chartType = "boxplot", config = new { indicator = "household_income", display = new { orientation = "vertical" } },
        });
        var plain = await h.AddWidgetAsync(new { title = "Income 3", chartType = "boxplot", config = new { indicator = "household_income" } });

        Assert.Equal("horizontal", horizontal.GetProperty("config").GetProperty("display").GetProperty("orientation").GetString());
        Assert.Equal(3, horizontal.GetProperty("config").GetProperty("display").GetProperty("color").GetInt32());
        Assert.Equal("vertical", vertical.GetProperty("config").GetProperty("display").GetProperty("orientation").GetString());
        Assert.False(plain.GetProperty("config").TryGetProperty("display", out _));
        Assert.Equal(6, horizontal.GetProperty("layout").GetProperty("w").GetInt32());
        Assert.Equal(4, horizontal.GetProperty("layout").GetProperty("h").GetInt32());
    }

    private static string OrientationOf(JsonElement widget) =>
        widget.GetProperty("config").TryGetProperty("display", out var display) && display.TryGetProperty("orientation", out var orientation) ? orientation.GetString()! : "-";

    [Fact]
    public async Task A_box_plots_orientation_is_kept_by_a_duplicate_a_move_and_a_restored_version()
    {
        await using var h = await WithAsync(Values("A", 1, 2));
        var created = await h.AddWidgetAsync(new
        {
            title = "Income", chartType = "boxplot",
            config = new { indicator = "household_income", groupBy = "municipality", display = new { orientation = "horizontal" } },
        });
        var id = created.GetProperty("id").GetGuid();

        // Duplicated with its dashboard: the copy of the box plot is horizontal.
        var copyResponse = await h.Analyst.PostAsJsonAsync(h.DashboardUrl() + "/copies", new { name = "Income 2" });
        Assert.Equal(HttpStatusCode.Created, copyResponse.StatusCode);
        var copyId = (await copyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var copied = (await h.GetWidgetsAsync(copyId)).EnumerateArray().Single();
        Assert.Equal("horizontal", OrientationOf(copied));

        // Moved to another dashboard: still horizontal.
        var other = await h.InsertDashboardAsync("Other");
        var moveResponse = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = other });
        Assert.Equal(HttpStatusCode.OK, moveResponse.StatusCode);
        Assert.Equal("horizontal", OrientationOf(await moveResponse.Content.ReadFromJsonAsync<JsonElement>()));

        // A version holds it: vertical, then horizontal, then the vertical version restored.
        var vertical = await h.AddWidgetAsync(new
        {
            title = "Vertical", chartType = "boxplot", config = new { indicator = "household_income", display = new { orientation = "vertical" } },
        }, dashboardId: other);
        var verticalId = vertical.GetProperty("id").GetGuid();
        var before = await h.Analyst.GetFromJsonAsync<JsonElement>(h.DashboardUrl(other) + "/versions");
        var number = before.GetProperty("versions").EnumerateArray().Max(v => v.GetProperty("number").GetInt32());
        var change = await h.Analyst.PatchJsonAsync(h.WidgetUrl(verticalId, other), new
        {
            config = new { indicator = "household_income", display = new { orientation = "horizontal" } },
        });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal("horizontal", OrientationOf((await h.GetWidgetsAsync(other)).EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == verticalId)));

        var restore = await h.Analyst.PostAsync($"{h.DashboardUrl(other)}/versions/{number}/restore", null);
        Assert.Equal(HttpStatusCode.Created, restore.StatusCode);
        Assert.Equal("vertical", OrientationOf((await h.GetWidgetsAsync(other)).EnumerateArray().Single(w => w.GetProperty("title").GetString() == "Vertical")));
    }

    [Theory]
    [InlineData("boxplot", """{"indicator":"household_income","groupBy":"municipality","seriesBy":"sex"}""", "config.seriesBy")]
    [InlineData("boxplot", """{"indicator":"household_income","groupBy":"municipality","display":{"orientation":"sideways"}}""", "config.display.orientation")]
    [InlineData("column", """{"indicator":"household_income","groupBy":"municipality","display":{"orientation":"horizontal"}}""", "config.display.orientation")]
    [InlineData("boxplot", """{"indicator":"household_income","groupBy":"municipality","sort":{"by":"column","column":"value","compareAs":"number","direction":"asc"}}""", "config.sort.by")]
    [InlineData("boxplot", """{"indicator":"household_income","sort":{"by":"value","direction":"asc"}}""", "config.sort")]
    [InlineData("boxplot", """{"indicator":"household_income","groupBy":"municipality","display":{"ramp":"green"}}""", "config.display.ramp")]
    public async Task A_box_plot_that_breaks_a_rule_is_refused_on_the_field_when_it_is_created(string chartType, string config, string field)
    {
        await using var h = await WithAsync(Values("A", 1, 2), "municipality text, sex text, value numeric");
        await h.ExecuteAsync($"UPDATE \"{h.Schema}\".household_income SET sex = 'F'");

        var response = await h.PostWidgetAsync(new { title = "Income", chartType, config = JsonDocument.Parse(config).RootElement });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(IndicatorsHarness.Errors(problem, field));
    }

    [Theory]
    [InlineData("""{"config":{"indicator":"household_income","groupBy":"municipality","seriesBy":"sex"}}""", "config.seriesBy")]
    [InlineData("""{"config":{"indicator":"household_income","groupBy":"municipality","display":{"orientation":"sideways"}}}""", "config.display.orientation")]
    [InlineData("""{"config":{"indicator":"household_income","groupBy":"municipality","sort":{"by":"column","column":"value","compareAs":"number","direction":"asc"}}}""", "config.sort.by")]
    [InlineData("""{"config":{"indicator":"household_income","sort":{"by":"value","direction":"asc"}}}""", "config.sort")]
    public async Task A_saved_box_plot_changed_into_one_that_breaks_a_rule_is_refused_on_the_field(string body, string field)
    {
        await using var h = await WithAsync(Values("A", 1, 2), "municipality text, sex text, value numeric");
        var created = await h.AddWidgetAsync(new
        {
            title = "Income", chartType = "boxplot", config = new { indicator = "household_income", groupBy = "municipality", display = new { orientation = "horizontal" } },
        });

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), JsonDocument.Parse(body).RootElement);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(IndicatorsHarness.Errors(problem, field));
        // Nothing changed.
        Assert.Equal("horizontal", OrientationOf((await h.GetWidgetsAsync()).EnumerateArray().Single()));
    }

    [Fact]
    public async Task A_horizontal_box_plot_turned_into_a_column_chart_with_no_new_config_is_refused_on_the_orientation()
    {
        await using var h = await WithAsync(Values("A", 1, 2));
        var created = await h.AddWidgetAsync(new
        {
            title = "Income", chartType = "boxplot", config = new { indicator = "household_income", groupBy = "municipality", display = new { orientation = "horizontal" } },
        });

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { chartType = "column" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(IndicatorsHarness.Errors(problem, "config.display.orientation"));
    }
}
