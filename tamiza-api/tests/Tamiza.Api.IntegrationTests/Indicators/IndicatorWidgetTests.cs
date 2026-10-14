using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary><c>POST/PATCH/DELETE /projects/{id}/indicators/widgets</c>: adding, editing, arranging and removing widgets.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorWidgetTests(PostgresFixture postgres)
{
    private const string Columns = IndicatorsHarness.StandardColumns + ", year bigint, geometry text";

    private const string Rows = """
        ('Cali',    40, 4, 10, 2025, 'POINT(1 1)'),
        ('Palmira', 60, 6, 10, 2025, 'POINT(2 2)')
        """;

    private async Task<IndicatorsHarness> PublishedAsync(Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync("coverage", Columns, Rows);
        await h.PublishAsync("target", IndicatorsHarness.StandardColumns, "('Cali', 90, 9, 10)");
        return h;
    }

    private static object Column(string title = "Coverage by municipality", object? layout = null) => new
    {
        title,
        chartType = "column",
        config = new { indicator = "coverage", groupBy = "municipality" },
        layout,
    };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    // Analyst adds a widget

    [Fact]
    public async Task An_analyst_adds_a_widget_that_every_member_then_sees()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(Column());
        var widget = await ReadAsync(response);
        var overview = await h.GetAsync(client: h.Viewer);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/projects/{h.ProjectId}/indicators/widgets/{widget.GetProperty("id").GetGuid()}", response.Headers.Location!.OriginalString);
        Assert.Equal("Coverage by municipality", widget.GetProperty("title").GetString());
        Assert.Equal("column", widget.GetProperty("chartType").GetString());
        Assert.Equal(JsonValueKind.String, widget.GetProperty("updatedAt").ValueKind);
        var saved = overview.GetProperty("widgets").EnumerateArray().Single();
        Assert.Equal(widget.GetProperty("id").GetGuid(), saved.GetProperty("id").GetGuid());
        Assert.Equal("Coverage by municipality", saved.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_admin_can_add_a_widget_too()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(Column(), h.Admin);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Configuration

    [Fact]
    public async Task A_widget_created_with_only_an_indicator_and_a_group_keeps_the_measure_open_and_every_other_default_is_filled()
    {
        await using var h = await PublishedAsync();

        var config = (await h.AddWidgetAsync(Column())).GetProperty("config");

        Assert.Equal("coverage", config.GetProperty("indicator").GetString());
        Assert.Equal("municipality", config.GetProperty("groupBy").GetString());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("measure").ValueKind); // the table's default measure applies when it is queried
        Assert.Equal(JsonValueKind.Null, config.GetProperty("ratio").ValueKind);
        Assert.Equal(0, config.GetProperty("filters").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("geometryColumn").ValueKind);
        Assert.Equal(JsonValueKind.Null, config.GetProperty("buckets").ValueKind);
    }

    [Fact]
    public async Task The_whole_configuration_is_saved_and_returned_as_sent()
    {
        await using var h = await PublishedAsync();

        var widget = await h.AddWidgetAsync(new
        {
            title = "Distribution",
            chartType = "histogram",
            config = new
            {
                indicator = "coverage",
                groupBy = "year",
                measure = "numerator",
                filters = new[] { new { column = "municipality", op = "in", values = new[] { "Cali" } } },
                buckets = 20,
            },
            layout = new { x = 6, y = 2, w = 6, h = 5 },
        });
        var overview = await h.GetAsync();

        var config = widget.GetProperty("config");
        Assert.Equal(widget.ToString(), overview.GetProperty("widgets")[0].ToString());
        Assert.Equal(("coverage", "year", "numerator", 20), (config.GetProperty("indicator").GetString(), config.GetProperty("groupBy").GetString(),
            config.GetProperty("measure").GetString(), config.GetProperty("buckets").GetInt32()));
        Assert.Equal("Cali", config.GetProperty("filters")[0].GetProperty("values")[0].GetString());
        Assert.Equal((6, 2, 6, 5), (widget.GetProperty("layout").GetProperty("x").GetInt32(), widget.GetProperty("layout").GetProperty("y").GetInt32(),
            widget.GetProperty("layout").GetProperty("w").GetInt32(), widget.GetProperty("layout").GetProperty("h").GetInt32()));
    }

    [Fact]
    public async Task A_ratio_is_saved_and_returned_with_no_measure()
    {
        await using var h = await PublishedAsync();

        var widget = await h.AddWidgetAsync(new
        {
            title = "Rate",
            chartType = "bar",
            config = new { indicator = "coverage", groupBy = "municipality", ratio = new { numerator = "numerator", denominator = "denominator" } },
        });
        var overview = await h.GetAsync();

        var ratio = overview.GetProperty("widgets")[0].GetProperty("config").GetProperty("ratio");
        Assert.Equal(("numerator", "denominator"), (ratio.GetProperty("numerator").GetString(), ratio.GetProperty("denominator").GetString()));
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("config").GetProperty("measure").ValueKind);
    }

    [Fact]
    public async Task A_measure_together_with_a_ratio_is_rejected()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new
        {
            title = "Both",
            chartType = "bar",
            config = new { indicator = "coverage", groupBy = "municipality", measure = "value", ratio = new { numerator = "numerator", denominator = "denominator" } },
        });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(problem, "config.measure"));
    }

    // Default layout

    [Theory]
    [InlineData("stat", 3, 2)]
    [InlineData("bar", 6, 4)]
    [InlineData("column", 6, 4)]
    [InlineData("line", 6, 4)]
    [InlineData("histogram", 6, 4)]
    [InlineData("pie", 6, 4)]
    [InlineData("map", 6, 6)]
    public async Task A_widget_without_a_layout_gets_the_default_size_of_its_chart_type_at_the_left_edge(string chartType, int w, int height)
    {
        await using var h = await PublishedAsync();

        var widget = await h.AddWidgetAsync(new
        {
            title = "W", chartType, config = new { indicator = "coverage", groupBy = "municipality", geometryColumn = "geometry" },
        });

        var layout = widget.GetProperty("layout");
        Assert.Equal((0, 0, w, height), (layout.GetProperty("x").GetInt32(), layout.GetProperty("y").GetInt32(), layout.GetProperty("w").GetInt32(), layout.GetProperty("h").GetInt32()));
    }

    [Fact]
    public async Task A_widget_without_a_layout_is_placed_under_the_existing_ones()
    {
        await using var h = await PublishedAsync();
        await h.AddWidgetAsync(Column(layout: new { x = 0, y = 0, w = 6, h = 4 }));
        await h.AddWidgetAsync(Column(layout: new { x = 6, y = 3, w = 6, h = 5 })); // bottom edge at 8

        var next = await h.AddWidgetAsync(new { title = "Next", chartType = "stat", config = new { indicator = "coverage" } });

        Assert.Equal(8, next.GetProperty("layout").GetProperty("y").GetInt32());
        Assert.Equal(0, next.GetProperty("layout").GetProperty("x").GetInt32());
    }

    // Overview order

    [Fact]
    public async Task Widgets_are_listed_top_to_bottom_then_left_to_right_then_by_creation()
    {
        await using var h = await PublishedAsync();
        await h.AddWidgetAsync(Column("third", new { x = 0, y = 4, w = 3, h = 2 }));
        await h.AddWidgetAsync(Column("second", new { x = 6, y = 0, w = 3, h = 2 }));
        await h.AddWidgetAsync(Column("first", new { x = 0, y = 0, w = 3, h = 2 }));
        await h.AddWidgetAsync(Column("fourth", new { x = 0, y = 4, w = 3, h = 2 }));

        var titles = (await h.GetAsync()).GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("title").GetString());

        Assert.Equal(["first", "second", "third", "fourth"], titles);
    }

    // Viewer cannot edit

    [Fact]
    public async Task A_viewer_cannot_add_edit_or_remove_a_widget()
    {
        await using var h = await PublishedAsync();
        var widget = await h.AddWidgetAsync(Column());
        var id = widget.GetProperty("id").GetGuid();

        var add = await h.PostWidgetAsync(Column(), h.Viewer);
        var rename = await h.Viewer.PatchJsonAsync($"{h.Url}/widgets/{id}", new { title = "Renamed" });
        var move = await h.Viewer.PatchJsonAsync($"{h.Url}/widgets/{id}", new { layout = new { x = 3, y = 0, w = 3, h = 2 } });
        var remove = await h.Viewer.DeleteAsync($"{h.Url}/widgets/{id}");

        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden],
            [add.StatusCode, rename.StatusCode, move.StatusCode, remove.StatusCode]);
        var after = (await h.GetAsync()).GetProperty("widgets").EnumerateArray().Single();
        Assert.Equal("Coverage by municipality", after.GetProperty("title").GetString());
        Assert.Equal(0, after.GetProperty("layout").GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_404_on_every_widget_endpoint()
    {
        await using var h = await PublishedAsync();
        var id = (await h.AddWidgetAsync(Column())).GetProperty("id").GetGuid();

        var add = await h.PostWidgetAsync(Column(), h.Outsider);
        var edit = await h.Outsider.PatchJsonAsync($"{h.Url}/widgets/{id}", new { title = "x" });
        var remove = await h.Outsider.DeleteAsync($"{h.Url}/widgets/{id}");

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], [add.StatusCode, edit.StatusCode, remove.StatusCode]);
    }

    // Widget limit

    [Fact]
    public async Task Adding_a_widget_past_the_limit_fails_with_a_message_naming_it()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerProject"] = "2" });
        await h.AddWidgetAsync(Column("one"));
        await h.AddWidgetAsync(Column("two"));

        var response = await h.PostWidgetAsync(Column("three"));
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at most 2 widgets", IndicatorsHarness.Errors(problem, "widgets")[0]);
        Assert.Equal(2, (await h.GetAsync()).GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task Widgets_added_at_the_same_moment_cannot_pass_the_limit()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerProject"] = "2" });

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => h.PostWidgetAsync(Column($"w{i}"))));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(6, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(2, (await h.GetAsync()).GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task The_limit_counts_one_projects_widgets_only_and_frees_a_place_when_one_is_removed()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerProject"] = "1" });
        var other = await h.AddOtherProjectAsync();
        var otherResponse = await h.Admin.PostAsJsonAsync(IndicatorsHarness.UrlOf(other) + "/widgets", Column("elsewhere"));
        var first = await h.AddWidgetAsync(Column("one"));

        var full = await h.PostWidgetAsync(Column("two"));
        await h.Analyst.DeleteAsync($"{h.Url}/widgets/{first.GetProperty("id").GetGuid()}");
        var again = await h.PostWidgetAsync(Column("two"));

        Assert.Equal(HttpStatusCode.Created, otherResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task The_overview_reports_the_limits()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerProject"] = "7", ["IndicatorDashboard:MaxWidgetQueryRows"] = "123" });

        var limits = (await h.GetAsync()).GetProperty("limits");

        Assert.Equal((7, 123), (limits.GetProperty("maxWidgets").GetInt32(), limits.GetProperty("maxQueryRows").GetInt32()));
    }

    // Chart type constraints (a missing groupBy on a column chart rejected)

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("pie")]
    public async Task A_chart_that_groups_is_rejected_without_a_group_column_and_names_its_type(string chartType)
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new { title = "W", chartType, config = new { indicator = "coverage" } });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"A {chartType} widget needs a column to group by", IndicatorsHarness.Errors(problem, "config.groupBy")[0]);
        Assert.Equal(0, (await h.GetAsync()).GetProperty("widgets").GetArrayLength());
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("histogram")]
    public async Task A_stat_tile_and_a_histogram_need_no_group_column(string chartType)
    {
        await using var h = await PublishedAsync();

        var widget = await h.AddWidgetAsync(new { title = "W", chartType, config = new { indicator = "coverage" } });

        Assert.Equal(JsonValueKind.Null, widget.GetProperty("config").GetProperty("groupBy").ValueKind);
    }

    [Fact]
    public async Task A_widget_over_no_indicator_is_rejected()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new { title = "W", chartType = "stat", config = new { } });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(problem, "config.indicator"));
    }

    [Fact]
    public async Task A_widget_without_a_config_is_rejected()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new { title = "W", chartType = "stat" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_indicator_that_is_not_a_valid_name_is_rejected()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new { title = "W", chartType = "stat", config = new { indicator = "Piped water" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_map_widget_needs_a_geometry_column()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new { title = "Map", chartType = "map", config = new { indicator = "coverage", groupBy = "municipality" } });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(problem, "config.geometryColumn"));
    }

    [Fact]
    public async Task A_map_widget_with_a_geometry_column_is_saved()
    {
        await using var h = await PublishedAsync();

        var widget = await h.AddWidgetAsync(new
        {
            title = "Map", chartType = "map", config = new { indicator = "coverage", groupBy = "municipality", geometryColumn = "geometry" },
        });

        Assert.Equal("geometry", widget.GetProperty("config").GetProperty("geometryColumn").GetString());
    }

    // Field validation

    [Fact]
    public async Task The_title_chart_type_layout_and_buckets_are_validated_together()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new
        {
            title = "  ",
            chartType = "radar",
            config = new { indicator = "coverage", buckets = 101 },
            layout = new { x = 10, y = -1, w = 5, h = 41 },
        });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["chartType", "config.buckets", "layout.h", "layout.w", "layout.y", "title"], fields);
    }

    [Fact]
    public async Task A_title_of_200_characters_is_accepted_and_201_is_not()
    {
        await using var h = await PublishedAsync();

        var ok = await h.PostWidgetAsync(Column(new string('t', 200)));
        var tooLong = await h.PostWidgetAsync(Column(new string('t', 201)));

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Theory]
    [InlineData(0, 0, 12, 1, true)]
    [InlineData(11, 0, 1, 40, true)]
    [InlineData(0, 100, 1, 1, true)]
    [InlineData(-1, 0, 3, 2, false)]
    [InlineData(0, 0, 0, 2, false)]
    [InlineData(0, 0, 3, 0, false)]
    [InlineData(0, 0, 13, 2, false)]
    [InlineData(10, 0, 3, 2, false)]
    [InlineData(0, 0, 3, 41, false)]
    [InlineData(0, -1, 3, 2, false)]
    public async Task The_layout_must_fit_the_12_column_grid(int x, int y, int w, int h, bool valid)
    {
        await using var harness = await PublishedAsync();

        var response = await harness.PostWidgetAsync(Column(layout: new { x, y, w, h }));

        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_layout_missing_some_of_its_numbers_is_rejected()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(Column(layout: new { x = 0, y = 0 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_body_is_a_validation_error()
    {
        await using var h = await PublishedAsync();

        var response = await h.Analyst.PostAsync(h.Url + "/widgets", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Validation against the table

    [Fact]
    public async Task While_the_table_is_published_the_group_geometry_and_filter_columns_must_exist_and_are_named()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new
        {
            title = "W",
            chartType = "map",
            config = new
            {
                indicator = "coverage",
                groupBy = "round",
                geometryColumn = "shape",
                filters = new[] { new { column = "region", op = "equals", value = "x" } },
            },
        });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'round'", IndicatorsHarness.Errors(problem, "config.groupBy")[0]);
        Assert.Contains("'shape'", IndicatorsHarness.Errors(problem, "config.geometryColumn")[0]);
        Assert.Contains("'region'", IndicatorsHarness.Errors(problem, "config.filters[0]")[0]);
    }

    [Fact]
    public async Task While_the_table_is_published_the_measure_and_ratio_columns_must_be_numeric_columns_of_it()
    {
        await using var h = await PublishedAsync();

        var missing = await h.PostWidgetAsync(new { title = "W", chartType = "bar", config = new { indicator = "coverage", groupBy = "municipality", measure = "total" } });
        var text = await h.PostWidgetAsync(new { title = "W", chartType = "bar", config = new { indicator = "coverage", groupBy = "municipality", measure = "municipality" } });
        var ratio = await h.PostWidgetAsync(new
        {
            title = "W", chartType = "bar", config = new { indicator = "coverage", groupBy = "municipality", ratio = new { numerator = "geometry", denominator = "population" } },
        });
        var ratioProblem = await ReadAsync(ratio);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("'total'", IndicatorsHarness.Errors(await ReadAsync(missing), "config.measure")[0]);
        Assert.Contains("is not a number", IndicatorsHarness.Errors(await ReadAsync(text), "config.measure")[0]);
        Assert.Contains("is not a number", IndicatorsHarness.Errors(ratioProblem, "config.ratio.numerator")[0]);
        Assert.Contains("'population'", IndicatorsHarness.Errors(ratioProblem, "config.ratio.denominator")[0]);
    }

    [Fact]
    public async Task A_filter_that_does_not_fit_its_column_is_rejected_when_the_table_is_published()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new
        {
            title = "W",
            chartType = "bar",
            config = new { indicator = "coverage", groupBy = "municipality", filters = new[] { new { column = "municipality", op = "range", min = "1" } } },
        });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not support the filter 'range'", IndicatorsHarness.Errors(problem, "config.filters[0]")[0]);
    }

    [Fact]
    public async Task The_widgets_columns_are_checked_against_its_own_indicators_table_not_another()
    {
        await using var h = await PublishedAsync();

        // `year` is a column of coverage, not of target.
        var response = await h.PostWidgetAsync(new { title = "W", chartType = "bar", config = new { indicator = "target", groupBy = "year" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_widget_can_be_configured_before_its_indicator_has_published()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddIndicatorAsync("attendance");

        var widget = await h.AddWidgetAsync(new
        {
            title = "Early",
            chartType = "line",
            config = new { indicator = "attendance", groupBy = "round", filters = new[] { new { column = "region", op = "notEmpty" } } },
        });
        var overview = await h.GetAsync();

        Assert.False(overview.GetProperty("indicators")[0].GetProperty("published").GetBoolean());
        Assert.Equal(widget.GetProperty("id").GetGuid(), overview.GetProperty("widgets")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Before_publication_only_what_needs_no_table_is_checked()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var response = await h.PostWidgetAsync(new
        {
            title = "Early",
            chartType = "line",
            config = new { indicator = "x", groupBy = "round", filters = new[] { new { column = "region", op = "contains-ish" } } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Edit

    [Fact]
    public async Task A_patch_changes_only_the_fields_it_names()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(new
        {
            title = "Original",
            chartType = "bar",
            config = new { indicator = "coverage", groupBy = "year" },
            layout = new { x = 0, y = 0, w = 6, h = 4 },
        });
        var id = created.GetProperty("id").GetGuid();

        var response = await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{id}", new { layout = new { x = 6, y = 1, w = 4, h = 3 } });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Original", moved.GetProperty("title").GetString());
        Assert.Equal("bar", moved.GetProperty("chartType").GetString());
        Assert.Equal("year", moved.GetProperty("config").GetProperty("groupBy").GetString());
        Assert.Equal((6, 1, 4, 3), (moved.GetProperty("layout").GetProperty("x").GetInt32(), moved.GetProperty("layout").GetProperty("y").GetInt32(),
            moved.GetProperty("layout").GetProperty("w").GetInt32(), moved.GetProperty("layout").GetProperty("h").GetInt32()));
        Assert.True(moved.GetProperty("updatedAt").GetDateTimeOffset() >= created.GetProperty("updatedAt").GetDateTimeOffset());

        var renamed = await ReadAsync(await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{id}", new { title = "  Renamed  " }));
        Assert.Equal("Renamed", renamed.GetProperty("title").GetString());
        Assert.Equal(6, renamed.GetProperty("layout").GetProperty("x").GetInt32());

        var reconfigured = await ReadAsync(await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{id}",
            new { config = new { indicator = "target", groupBy = "municipality" } }));
        Assert.Equal("target", reconfigured.GetProperty("config").GetProperty("indicator").GetString());
        Assert.Equal("municipality", reconfigured.GetProperty("config").GetProperty("groupBy").GetString()); // the config is replaced whole
    }

    [Fact]
    public async Task A_change_of_chart_type_is_checked_against_the_saved_query()
    {
        await using var h = await PublishedAsync();
        var id = (await h.AddWidgetAsync(new { title = "Total", chartType = "stat", config = new { indicator = "coverage" } })).GetProperty("id").GetGuid();

        var response = await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{id}", new { chartType = "pie" });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A pie widget needs a column to group by", IndicatorsHarness.Errors(problem, "config.groupBy")[0]);

        var fixedUp = await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{id}", new { chartType = "pie", config = new { indicator = "coverage", groupBy = "municipality" } });
        Assert.Equal(HttpStatusCode.OK, fixedUp.StatusCode);
        Assert.Equal("pie", (await ReadAsync(fixedUp)).GetProperty("chartType").GetString());
    }

    [Fact]
    public async Task A_patch_with_an_invalid_value_changes_nothing()
    {
        await using var h = await PublishedAsync();
        var id = (await h.AddWidgetAsync(Column("Keep"))).GetProperty("id").GetGuid();

        var response = await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{id}", new { title = "Changed", layout = new { x = 11, y = 0, w = 5, h = 2 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Keep", (await h.GetAsync()).GetProperty("widgets")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_widget_whose_query_went_stale_can_still_be_moved_and_renamed()
    {
        await using var h = await PublishedAsync();
        var id = (await h.AddWidgetAsync(new { title = "By year", chartType = "bar", config = new { indicator = "coverage", groupBy = "year" } })).GetProperty("id").GetGuid();
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)"); // no year column any more

        var moved = await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{id}", new { layout = new { x = 3, y = 0, w = 3, h = 2 }, title = "Still here" });
        var reconfigured = await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{id}", new { config = new { indicator = "coverage", groupBy = "year" } });

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reconfigured.StatusCode); // a new query is checked against the table
    }

    [Fact]
    public async Task Patching_a_widget_that_does_not_exist_or_belongs_to_another_project_is_404()
    {
        await using var h = await PublishedAsync();
        var other = await h.AddOtherProjectAsync();
        var foreign = (await ReadAsync(await h.Admin.PostAsJsonAsync(IndicatorsHarness.UrlOf(other) + "/widgets", Column("Foreign")))).GetProperty("id").GetGuid();

        var missing = await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{Guid.NewGuid()}", new { title = "x" });
        var wrongProject = await h.Analyst.PatchJsonAsync($"{h.Url}/widgets/{foreign}", new { title = "x" });
        var wrongDelete = await h.Analyst.DeleteAsync($"{h.Url}/widgets/{foreign}");

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], [missing.StatusCode, wrongProject.StatusCode, wrongDelete.StatusCode]);
        var otherOverview = await ReadAsync(await h.Admin.GetAsync(IndicatorsHarness.UrlOf(other)));
        Assert.Equal("Foreign", otherOverview.GetProperty("widgets")[0].GetProperty("title").GetString());
    }

    // Remove

    [Fact]
    public async Task An_analyst_removes_a_widget_and_it_is_gone_for_everyone()
    {
        await using var h = await PublishedAsync();
        var id = (await h.AddWidgetAsync(Column())).GetProperty("id").GetGuid();

        var response = await h.Analyst.DeleteAsync($"{h.Url}/widgets/{id}");
        var again = await h.Analyst.DeleteAsync($"{h.Url}/widgets/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(0, (await h.GetAsync()).GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task Deleting_a_project_removes_its_widgets()
    {
        await using var h = await PublishedAsync();
        await h.AddWidgetAsync(Column());

        await h.ExecuteAsync("DELETE FROM tamiza.projects WHERE id = @id", ("id", h.ProjectId));

        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    // Preview of a saved widget

    [Fact]
    public async Task A_saved_widgets_config_can_be_posted_to_the_query_endpoint_as_it_is()
    {
        await using var h = await PublishedAsync();
        var widget = await h.AddWidgetAsync(new
        {
            title = "Rate", chartType = "bar", config = new { indicator = "coverage", groupBy = "municipality", ratio = new { numerator = "numerator", denominator = "denominator" } },
        });

        var result = await h.QueryAsync(JsonSerializer.Deserialize<JsonElement>(widget.GetProperty("config").GetRawText()), widget.GetProperty("chartType").GetString());

        Assert.Equal(["Cali|0.4", "Palmira|0.6"], IndicatorsHarness.Rows(result));
    }
}
