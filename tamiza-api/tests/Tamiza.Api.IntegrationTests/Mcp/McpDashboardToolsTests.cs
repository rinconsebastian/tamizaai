using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpDashboardToolsTests(PostgresFixture postgres)
{
    private const string Columns = "municipality text, value numeric, numerator numeric, denominator numeric";

    private Task<McpProjectHarness> CreateAsync(IReadOnlyDictionary<string, string?>? settings = null) =>
        McpProjectHarness.CreateAsync(postgres, McpAccess.Aggregates, settings: settings);

    /// <summary>The indicator <c>piped_water_rate</c>: published with two municipalities, and saved as a script with its details.</summary>
    private static async Task SeedAsync(McpProjectHarness h)
    {
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "piped_water_rate", Columns, "('Santa Ana', 0.9, 9, 10),('San Luis', 0.3, 3, 10)");
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, unit, format, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'piped_water_rate', 1, 'x', 'Piped water rate', '%', 'percent', @a, now())",
            ("p", h.ProjectId), ("a", h.Users["analyst"]));
    }

    private static object BarConfig(string? groupBy = "municipality") => new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy };

    private static async Task<Guid> AddDashboardAsync(McpProjectHarness h, string name, string person = "analyst")
    {
        using var body = (await h.CallAsync(person, "create_dashboard", new { name })).Json();
        return Guid.Parse(body.RootElement.GetProperty("id").GetString()!);
    }

    private static async Task<Guid> AddWidgetAsync(McpProjectHarness h, Guid dashboard, string title, string chartType, object config, string person = "analyst")
    {
        var result = await h.CallAsync(person, "create_widget", new { dashboardId = dashboard, title, chartType, config });
        Assert.Null(result.Code());
        using var body = result.Json();
        return Guid.Parse(body.RootElement.GetProperty("id").GetString()!);
    }

    private static async Task<Guid> FirstDashboardAsync(McpProjectHarness h)
    {
        using var body = (await h.CallAsync("viewer", "list_dashboards")).Json();
        return Guid.Parse(body.RootElement.GetProperty("dashboards")[0].GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task The_indicators_list_shows_the_label_the_format_and_whether_each_has_published()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'not_yet', 1, 'x', 'Not yet', 'count', @a, now())",
            ("p", h.ProjectId), ("a", h.Users["analyst"]));

        using var body = (await h.CallAsync("viewer", "list_indicators")).Json();

        var byName = body.RootElement.GetProperty("indicators").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!);
        Assert.Equal(("Piped water rate", "percent", true, 2), (byName["piped_water_rate"].GetProperty("label").GetString(), byName["piped_water_rate"].GetProperty("format").GetString(), byName["piped_water_rate"].GetProperty("published").GetBoolean(), byName["piped_water_rate"].GetProperty("rows").GetInt32()));
        Assert.False(byName["not_yet"].GetProperty("published").GetBoolean());
        Assert.Equal(7, body.RootElement.GetProperty("limits").GetProperty("maxDashboards").GetInt32());
    }

    [Fact]
    public async Task The_columns_of_an_indicator_table_say_what_a_widget_can_plot()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);

        using var body = (await h.CallAsync("viewer", "get_indicator_columns", new { name = "piped_water_rate" })).Json();

        Assert.Equal(["value", "numerator", "denominator"], body.RootElement.GetProperty("numeric").EnumerateArray().Select(c => c.GetString()).ToArray());
        Assert.Equal("municipality", body.RootElement.GetProperty("columns")[0].GetProperty("name").GetString());
        using var unpublished = (await h.CallAsync("viewer", "get_indicator_columns", new { name = "nothing" })).Json();
        Assert.False(unpublished.RootElement.GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task A_widget_query_is_checked_before_it_is_saved_and_saves_nothing()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);

        var result = await h.CallAsync("viewer", "query_indicator", new { chartType = "bar", config = BarConfig() });

        using var body = result.Json();
        Assert.True(body.RootElement.GetProperty("published").GetBoolean());
        Assert.Equal("Piped water rate", body.RootElement.GetProperty("label").GetString());
        var rows = body.RootElement.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("group").GetString()!);
        Assert.Equal(0.9, rows["Santa Ana"].GetProperty("value").GetDouble(), 3);
        Assert.Contains("90", rows["Santa Ana"].GetProperty("formatted").GetString());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task A_query_over_an_indicator_that_has_not_published_says_so_and_the_row_limit_is_reported()
    {
        await using var h = await CreateAsync(new Dictionary<string, string?> { ["IndicatorDashboard:MaxWidgetQueryRows"] = "1" });
        await SeedAsync(h);

        using var unpublished = (await h.CallAsync("viewer", "query_indicator", new { config = new { indicator = "nothing", groupBy = "municipality" } })).Json();
        using var limited = (await h.CallAsync("viewer", "query_indicator", new { chartType = "bar", config = BarConfig() })).Json();

        Assert.False(unpublished.RootElement.GetProperty("published").GetBoolean() && !unpublished.RootElement.TryGetProperty("missing", out _));
        Assert.True(limited.RootElement.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(1, limited.RootElement.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task A_dashboard_for_a_situation_is_built_with_widgets_a_filter_and_the_analysts_name_on_them()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);

        var dashboard = await AddDashboardAsync(h, "Water access");
        var stat = await AddWidgetAsync(h, dashboard, "Piped water rate", "stat", new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" } });
        var bar = await AddWidgetAsync(h, dashboard, "By municipality", "bar", BarConfig());
        var filters = await h.CallAsync("analyst", "update_dashboard", new
        {
            dashboardId = dashboard,
            filters = new[] { new { column = "municipality", label = "Municipality", kind = "text" } },
        });

        Assert.Null(filters.Code());
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var widgets = viewed.RootElement.GetProperty("widgets").EnumerateArray().ToList();
        Assert.Equal(["Piped water rate", "By municipality"], widgets.Select(w => w.GetProperty("title").GetString()).ToArray());
        Assert.True(widgets[1].GetProperty("layout").GetProperty("y").GetInt32() >= widgets[0].GetProperty("layout").GetProperty("y").GetInt32() + widgets[0].GetProperty("layout").GetProperty("h").GetInt32());
        Assert.Equal("municipality", viewed.RootElement.GetProperty("filters")[0].GetProperty("column").GetString());
        Assert.Equal(2, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets WHERE created_by = @a AND updated_by = @a", ("a", h.Users["analyst"])));
        Assert.NotEqual(stat, bar);
    }

    [Fact]
    public async Task A_widget_that_needs_a_group_column_is_refused_with_the_endpoints_message()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var result = await h.CallAsync("analyst", "create_widget", new { dashboardId = dashboard, title = "Bad", chartType = "bar", config = BarConfig(groupBy: null) });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("config.groupBy", result.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task The_limit_of_dashboards_and_the_rules_of_their_names_are_the_uis()
    {
        await using var h = await CreateAsync();

        for (var i = 2; i <= 7; i++)
        {
            await AddDashboardAsync(h, $"Dashboard {i}");
        }

        var eighth = await h.CallAsync("analyst", "create_dashboard", new { name = "Eighth" });
        var taken = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = await FirstDashboardAsync(h), name = "DASHBOARD 3" });
        var reserved = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = await FirstDashboardAsync(h), name = "Performance" });

        Assert.Equal("dashboards.limit", eighth.Code());
        Assert.Equal("mcp.validation", taken.Code());
        Assert.Equal("mcp.validation", reserved.Code());
        Assert.Equal(7, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboards"));
    }

    [Fact]
    public async Task A_dashboard_is_moved_to_a_place_renamed_and_listed_in_its_new_order()
    {
        await using var h = await CreateAsync();
        var second = await AddDashboardAsync(h, "Second");
        var third = await AddDashboardAsync(h, "Third");

        var moved = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = third, position = 1, name = "Third renamed" });
        using var list = (await h.CallAsync("viewer", "list_dashboards")).Json();

        Assert.Null(moved.Code());
        Assert.Equal(["Third renamed", "Indicators", "Second"], list.RootElement.GetProperty("dashboards").EnumerateArray().Select(d => d.GetProperty("name").GetString()).ToArray());
        Assert.Equal([1, 2, 3], list.RootElement.GetProperty("dashboards").EnumerateArray().Select(d => d.GetProperty("position").GetInt32()).ToArray());
        Assert.NotEqual(second, third);
    }

    [Fact]
    public async Task Two_moves_at_the_same_time_leave_a_valid_order_and_a_stale_one_is_refused_like_in_the_ui()
    {
        await using var h = await CreateAsync();
        var a = await AddDashboardAsync(h, "A");
        var b = await AddDashboardAsync(h, "B");
        var c = await AddDashboardAsync(h, "C");

        var results = await Task.WhenAll(
            h.CallAsync("analyst", "update_dashboard", new { dashboardId = c, position = 1 }),
            h.CallAsync("admin", "update_dashboard", new { dashboardId = a, position = 4 }));

        Assert.All(results, r => Assert.True(r.Code() is null or "dashboards.orderChanged", r.Text()));
        var positions = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            positions.Add(await TestData.ScalarAsync<int>(h.Db, "SELECT position FROM tamiza.dashboards ORDER BY position OFFSET @i LIMIT 1", ("i", i)));
        }

        Assert.Equal([0, 1, 2, 3], positions);
        Assert.NotEqual(b, a);
    }

    [Fact]
    public async Task A_dashboard_is_duplicated_with_its_widgets_and_filters_right_after_the_original()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var original = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, original, "By municipality", "bar", BarConfig());
        await h.CallAsync("analyst", "update_dashboard", new { dashboardId = original, filters = new[] { new { column = "municipality", label = "Municipality", kind = "text" } } });
        await AddDashboardAsync(h, "Later");

        using var copy = (await h.CallAsync("analyst", "duplicate_dashboard", new { dashboardId = original, name = "Copy" })).Json();
        using var list = (await h.CallAsync("viewer", "list_dashboards")).Json();

        Assert.Equal(2, copy.RootElement.GetProperty("position").GetInt32());
        var names = list.RootElement.GetProperty("dashboards").EnumerateArray().Select(d => d.GetProperty("name").GetString()).ToArray();
        Assert.Equal(["Indicators", "Copy", "Later"], names);
        var copied = list.RootElement.GetProperty("dashboards")[1];
        Assert.Equal((1, 1), (copied.GetProperty("widgets").GetInt32(), copied.GetProperty("filters").GetInt32()));
    }

    [Fact]
    public async Task Removing_a_dashboard_needs_its_name_and_a_project_keeps_one()
    {
        await using var h = await CreateAsync();
        var health = await AddDashboardAsync(h, "Health");
        await AddDashboardAsync(h, "Field");
        var first = await FirstDashboardAsync(h);

        var wrong = await h.CallAsync("analyst", "remove_dashboard", new { dashboardId = health, expectedName = "Field" });
        var right = await h.CallAsync("analyst", "remove_dashboard", new { dashboardId = health, expectedName = "  health " });
        var removedFirst = await h.CallAsync("analyst", "remove_dashboard", new { dashboardId = first, expectedName = "Indicators" });
        var last = await h.CallAsync("analyst", "remove_dashboard", new { dashboardId = await LastDashboardAsync(h), expectedName = "Field" });

        Assert.Equal("mcp.nameMismatch", wrong.Code());
        using var mismatch = wrong.Json();
        Assert.Equal("Health", mismatch.RootElement.GetProperty("current").GetString());
        Assert.Null(right.Code());
        Assert.Null(removedFirst.Code());
        Assert.Equal("dashboards.lastOne", last.Code());
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboards"));
    }

    private static async Task<Guid> LastDashboardAsync(McpProjectHarness h)
    {
        using var body = (await h.CallAsync("viewer", "list_dashboards")).Json();
        return Guid.Parse(body.RootElement.GetProperty("dashboards").EnumerateArray().Last().GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task A_removed_dashboard_takes_its_widgets_with_it_and_a_removal_of_a_missing_one_is_not_found()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var extra = await AddDashboardAsync(h, "Extra");
        await AddWidgetAsync(h, extra, "By municipality", "bar", BarConfig());

        Assert.Null((await h.CallAsync("analyst", "remove_dashboard", new { dashboardId = extra, expectedName = "Extra" })).Code());
        var again = await h.CallAsync("analyst", "remove_dashboard", new { dashboardId = extra, expectedName = "Extra" });

        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
        Assert.Equal("dashboards.notFound", again.Code());
    }

    [Fact]
    public async Task A_widget_is_changed_moved_to_another_dashboard_and_removed_by_its_title()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var first = await FirstDashboardAsync(h);
        var second = await AddDashboardAsync(h, "Second");
        var widget = await AddWidgetAsync(h, first, "By municipality", "bar", BarConfig());

        var renamed = await h.CallAsync("analyst", "update_widget", new { dashboardId = first, widgetId = widget, title = "Rate by municipality" });
        var moved = await h.CallAsync("analyst", "update_widget", new { dashboardId = first, widgetId = widget, moveToDashboardId = second });
        var wrong = await h.CallAsync("analyst", "remove_widget", new { dashboardId = second, widgetId = widget, expectedTitle = "By municipality" });
        var right = await h.CallAsync("analyst", "remove_widget", new { dashboardId = second, widgetId = widget, expectedTitle = "rate by municipality" });

        Assert.Null(renamed.Code());
        Assert.Null(moved.Code());
        using var movedBody = moved.Json();
        Assert.Equal(second.ToString(), movedBody.RootElement.GetProperty("dashboardId").GetString());
        Assert.Equal("mcp.nameMismatch", wrong.Code());
        Assert.Contains("Rate by municipality", wrong.Text());
        Assert.Null(right.Code());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task Widget_data_is_what_the_dashboard_shows_formatted_and_a_stale_widget_says_what_is_missing()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Rate", "stat", new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" } });
        var bar = await AddWidgetAsync(h, dashboard, "By municipality", "bar", BarConfig());
        await AddWidgetAsync(h, dashboard, "Waiting", "stat", new { indicator = "piped_water_rate", measure = "value" });
        // The indicator republishes without the column the bar groups by.
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "piped_water_rate", "town text, value numeric, numerator numeric, denominator numeric", "('Santa Ana', 0.9, 9, 10)");

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var widgets = body.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);
        Assert.Equal("shown", widgets["Rate"].GetProperty("status").GetString());
        Assert.Contains("90", widgets["Rate"].GetProperty("rows")[0].GetProperty("formatted").GetString());
        Assert.Equal("missingColumns", widgets["By municipality"].GetProperty("status").GetString());
        Assert.Contains("municipality", widgets["By municipality"].GetProperty("message").GetString());
        Assert.Equal("shown", widgets["Waiting"].GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, bar);
    }

    [Fact]
    public async Task Widget_data_applies_the_filters_defaults_or_the_values_given_and_can_name_one_widget()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var bar = await AddWidgetAsync(h, dashboard, "By municipality", "bar", BarConfig());
        await h.CallAsync("analyst", "update_dashboard", new
        {
            dashboardId = dashboard,
            filters = new[] { new { column = "municipality", label = "Municipality", kind = "text", @default = new { values = new[] { "Santa Ana" } } } },
        });

        using var byDefault = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = bar })).Json();
        using var given = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = bar, filterValues = new[] { new { column = "municipality", kind = "text", values = new[] { "San Luis" } } } })).Json();
        var missing = await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = Guid.NewGuid() });

        string[] Groups(JsonDocument d) => d.RootElement.GetProperty("widgets")[0].GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()!).ToArray();
        Assert.Equal(["Santa Ana"], Groups(byDefault));
        Assert.Equal(["San Luis"], Groups(given));
        Assert.Single(byDefault.RootElement.GetProperty("widgets").EnumerateArray());
        Assert.Equal("mcp.notFound", missing.Code());
    }

    [Fact]
    public async Task A_widget_of_an_indicator_that_has_not_published_says_so()
    {
        await using var h = await CreateAsync();
        var dashboard = await FirstDashboardAsync(h);
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.dashboard_widgets (id, project_id, dashboard_id, chart_type, title, config, layout, created_by, updated_by) VALUES (gen_random_uuid(), @p, @d, 'stat', 'Waiting', '{\"indicator\":\"not_yet\",\"measure\":\"value\",\"filters\":[]}'::jsonb, '{\"x\":0,\"y\":0,\"w\":3,\"h\":2}'::jsonb, @a, @a)",
            ("p", h.ProjectId), ("d", dashboard), ("a", h.Users["analyst"]));

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var widget = body.RootElement.GetProperty("widgets")[0];
        Assert.Equal("notPublished", widget.GetProperty("status").GetString());
        Assert.Contains("not_yet", widget.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_viewer_can_read_dashboards_and_cannot_change_them()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "By municipality", "bar", BarConfig());

        Assert.Null((await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Code());
        Assert.Null((await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Code());
        foreach (var (tool, args) in new (string, object)[]
                 {
                     ("create_dashboard", new { name = "X" }),
                     ("update_dashboard", new { dashboardId = dashboard, name = "X" }),
                     ("duplicate_dashboard", new { dashboardId = dashboard, name = "X" }),
                     ("remove_dashboard", new { dashboardId = dashboard, expectedName = "Indicators" }),
                     ("create_widget", new { dashboardId = dashboard, title = "X", chartType = "stat", config = new { indicator = "piped_water_rate" } }),
                     ("update_widget", new { dashboardId = dashboard, widgetId = widget, title = "X" }),
                     ("remove_widget", new { dashboardId = dashboard, widgetId = widget, expectedTitle = "By municipality" }),
                 })
        {
            Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", tool, args)).Code());
        }

        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboards"));
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }
}
