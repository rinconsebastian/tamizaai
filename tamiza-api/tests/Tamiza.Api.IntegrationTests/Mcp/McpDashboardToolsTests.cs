using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpDashboardToolsTests(PostgresFixture postgres)
{
    private const string Columns = "municipality text, value numeric, numerator numeric, denominator numeric";

    /// <summary>The request every write of a test is made for, as Claude passes it with each dashboard write.</summary>
    private const string Request = "Build the water dashboard";

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
        using var body = (await h.CallAsync(person, "create_dashboard", new { name, change = Request })).Json();
        return Guid.Parse(body.RootElement.GetProperty("id").GetString()!);
    }

    private static async Task<Guid> AddWidgetAsync(McpProjectHarness h, Guid dashboard, string title, string chartType, object config, string person = "analyst")
    {
        var result = await h.CallAsync(person, "create_widget", new { dashboardId = dashboard, title, chartType, config, change = Request });
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
    public async Task A_pie_keeps_the_row_shape_of_any_grouped_widget_and_the_page_only_domain_is_not_returned()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Share by municipality", "pie", BarConfig());
        await AddWidgetAsync(h, dashboard, "Rate by municipality", "bar", BarConfig());

        using var previewed = (await h.CallAsync("viewer", "query_indicator", new { chartType = "pie", config = BarConfig() })).Json();
        using var bar = (await h.CallAsync("viewer", "query_indicator", new { chartType = "bar", config = BarConfig() })).Json();
        using var shown = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        // The domain is a drawing aid for the page: neither tool returns it, and a pie answers the keys and rows of a bar.
        Assert.False(previewed.RootElement.TryGetProperty("domain", out _));
        Assert.Equal(
            bar.RootElement.EnumerateObject().Select(p => p.Name).ToArray(),
            previewed.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(bar.RootElement.GetProperty("rows").GetRawText(), previewed.RootElement.GetProperty("rows").GetRawText());
        var widgets = shown.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);
        Assert.Equal("shown", widgets["Share by municipality"].GetProperty("status").GetString());
        Assert.False(widgets["Share by municipality"].TryGetProperty("domain", out _));
        Assert.Equal(
            widgets["Rate by municipality"].EnumerateObject().Select(p => p.Name).Where(name => name != "id" && name != "title" && name != "chartType").ToArray(),
            widgets["Share by municipality"].EnumerateObject().Select(p => p.Name).Where(name => name != "id" && name != "title" && name != "chartType").ToArray());
        Assert.Equal(widgets["Rate by municipality"].GetProperty("rows").GetRawText(), widgets["Share by municipality"].GetProperty("rows").GetRawText());
        Assert.DoesNotContain("domain", previewed.RootElement.GetRawText());
        Assert.DoesNotContain("domain", shown.RootElement.GetRawText());
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
            change = Request,
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

        var result = await h.CallAsync("analyst", "create_widget", new { dashboardId = dashboard, title = "Bad", chartType = "bar", config = BarConfig(groupBy: null), change = Request });

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

        var eighth = await h.CallAsync("analyst", "create_dashboard", new { name = "Eighth", change = Request });
        var taken = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = await FirstDashboardAsync(h), name = "DASHBOARD 3", change = Request });
        var reserved = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = await FirstDashboardAsync(h), name = "Performance", change = Request });

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

        var moved = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = third, position = 1, name = "Third renamed", change = Request });
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
            h.CallAsync("analyst", "update_dashboard", new { dashboardId = c, position = 1, change = Request }),
            h.CallAsync("admin", "update_dashboard", new { dashboardId = a, position = 4, change = Request }));

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
        await h.CallAsync("analyst", "update_dashboard", new { dashboardId = original, filters = new[] { new { column = "municipality", label = "Municipality", kind = "text" } }, change = Request });
        await AddDashboardAsync(h, "Later");

        using var copy = (await h.CallAsync("analyst", "duplicate_dashboard", new { dashboardId = original, name = "Copy", change = Request })).Json();
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

        var renamed = await h.CallAsync("analyst", "update_widget", new { dashboardId = first, widgetId = widget, title = "Rate by municipality", change = Request });
        var moved = await h.CallAsync("analyst", "update_widget", new { dashboardId = first, widgetId = widget, moveToDashboardId = second, change = Request });
        var wrong = await h.CallAsync("analyst", "remove_widget", new { dashboardId = second, widgetId = widget, expectedTitle = "By municipality", change = Request });
        var right = await h.CallAsync("analyst", "remove_widget", new { dashboardId = second, widgetId = widget, expectedTitle = "rate by municipality", change = Request });

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
            change = Request,
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
                     ("create_dashboard", new { name = "X", change = Request }),
                     ("update_dashboard", new { dashboardId = dashboard, name = "X", change = Request }),
                     ("update_dashboard", new { dashboardId = dashboard, palette = "earth", valueColors = new[] { new { column = "municipality", value = "Santa Ana", color = 2 } }, change = Request }),
                     ("duplicate_dashboard", new { dashboardId = dashboard, name = "X", change = Request }),
                     ("remove_dashboard", new { dashboardId = dashboard, expectedName = "Indicators" }),
                     ("create_widget", new { dashboardId = dashboard, title = "X", chartType = "stat", config = new { indicator = "piped_water_rate" }, change = Request }),
                     ("update_widget", new { dashboardId = dashboard, widgetId = widget, title = "X", change = Request }),
                     ("remove_widget", new { dashboardId = dashboard, widgetId = widget, expectedTitle = "By municipality", change = Request }),
                 })
        {
            Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", tool, args)).Code());
        }

        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboards"));
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
        Assert.Equal("tamiza | 0", await TestData.ScalarAsync<string>(h.Db, "SELECT format('%s | %s', palette, jsonb_array_length(value_colors)) FROM tamiza.dashboards"));
    }

    // Widgets with a series

    private const string SourceColumns = "municipality text, water_source text, value numeric";

    private static async Task SeedSourcesAsync(McpProjectHarness h) =>
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "households_by_source", SourceColumns,
            "('Cali', 'Piped', 35), ('Cali', 'Well', 15), ('Palmira', 'Piped', 20), ('Buga', 'Well', 7), ('Buga', NULL, 3)");

    [Fact]
    public async Task Checking_a_stacked_chart_gives_the_series_the_cells_the_totals_and_no_rows()
    {
        await using var h = await CreateAsync();
        await SeedSourcesAsync(h);

        using var body = (await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "column",
            config = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "stacked" },
        })).Json();

        var root = body.RootElement;
        Assert.False(root.TryGetProperty("rows", out _));
        Assert.Equal(["Piped", "Well", "none"], root.GetProperty("series").EnumerateArray()
            .Select(x => x.TryGetProperty("kind", out var kind) ? kind.GetString()! : x.GetProperty("value").GetString()!));
        var cells = root.GetProperty("cells").EnumerateArray().Select(c => $"{c.GetProperty("group").GetString()}|{(c.TryGetProperty("series", out var series) ? series.GetString() : "-")}|{c.GetProperty("value").GetDouble():0.##}").ToArray();
        Assert.Contains("Cali|Piped|35", cells);
        Assert.Contains("Buga|-|3", cells);
        var totals = root.GetProperty("totals");
        Assert.Equal(["Buga|10", "Cali|50", "Palmira|20"], totals.GetProperty("groups").EnumerateArray().Select(g => $"{g.GetProperty("group").GetString()}|{g.GetProperty("value").GetDouble():0.##}"));
        Assert.Equal(80, totals.GetProperty("all").GetProperty("value").GetDouble());
        Assert.Equal("80.00", totals.GetProperty("all").GetProperty("formatted").GetString());
        Assert.Equal(await RowsCountAsync(h), totals.GetProperty("all").GetProperty("rows").GetInt64());
        // Nothing was saved.
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    private static Task<long> RowsCountAsync(McpProjectHarness h) =>
        TestData.ScalarAsync<long>(h.Db, $"SELECT count(*) FROM \"{PublishedTables.Schema(h.ProjectId, "indicators")}\".households_by_source");

    [Fact]
    public async Task A_likert_widget_in_widget_data_carries_the_shares_of_each_group()
    {
        await using var h = await CreateAsync();
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "agreement", "municipality text, answer text, value numeric",
            "('Cali', 'No', 30), ('Cali', 'Neither', 20), ('Cali', 'Yes', 50), ('Cali', 'Don''t know', 4)");
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Agreement", "likert", new
        {
            indicator = "agreement", measure = "value", groupBy = "municipality", seriesBy = "answer",
            scale = new object[] { new { value = "No", side = "negative" }, new { value = "Neither", side = "neutral" }, new { value = "Yes", side = "positive" } },
        });

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var widget = body.RootElement.GetProperty("widgets")[0];
        Assert.Equal("shown", widget.GetProperty("status").GetString());
        var shares = widget.GetProperty("cells").EnumerateArray().ToDictionary(c => c.GetProperty("series").GetString()!, c => c.GetProperty("share").GetDouble());
        Assert.Equal(0.3, shares["No"], 6);
        Assert.Equal(0.5, shares["Yes"], 6);
        Assert.Contains("1 rows were left out", widget.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).Single(n => n!.Contains("left out")));
        Assert.False(widget.TryGetProperty("rows", out _));
    }

    [Fact]
    public async Task A_pyramid_is_created_through_mcp_and_read_back()
    {
        await using var h = await CreateAsync();
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "population", "age_band text, sex text, value numeric",
            "('0-4', 'Female', 100), ('0-4', 'Male', 90), ('5-9', 'Female', 80), ('5-9', 'Male', 85)");
        var dashboard = await FirstDashboardAsync(h);

        var id = await AddWidgetAsync(h, dashboard, "Population", "pyramid", new
        {
            indicator = "population", measure = "value", groupBy = "age_band", seriesBy = "sex", pyramid = new { left = "Female", right = "Male" },
        });

        using var dashboardBody = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var widget = dashboardBody.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("id").GetString() == id.ToString());
        Assert.Equal("pyramid", widget.GetProperty("chartType").GetString());
        Assert.Equal("Female", widget.GetProperty("config").GetProperty("pyramid").GetProperty("left").GetString());
        Assert.Equal("sex", widget.GetProperty("config").GetProperty("seriesBy").GetString());
    }

    [Fact]
    public async Task A_likert_scale_out_of_order_is_refused_with_the_key_and_nothing_is_saved()
    {
        await using var h = await CreateAsync();
        await SeedSourcesAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Bad", chartType = "likert",
            config = new
            {
                indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source",
                scale = new object[] { new { value = "Well", side = "positive" }, new { value = "Piped", side = "negative" } },
            },
            change = Request,
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("scale[1].side", result.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task A_stacked_widget_that_plots_a_ratio_is_refused_with_the_key_of_the_layout()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Bad", chartType = "column",
            config = new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality", seriesBy = "value", layout = "stacked" },
            change = Request,
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("layout", result.Text());
    }

    [Fact]
    public async Task A_series_result_longer_than_the_size_is_cut_in_its_cells_and_names_the_filter_that_asks_for_less()
    {
        await using var h = await CreateAsync(new Dictionary<string, string?> { ["Mcp:MaxResultCharacters"] = "10000" });
        var rows = string.Join(", ", Enumerable.Range(1, 300).Select(i => $"('Municipality number {i:000}', 'Piped', {i}), ('Municipality number {i:000}', 'Well', {i})"));
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "households_by_source", SourceColumns, rows);

        var result = await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "heatmap",
            config = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
        });

        Assert.True(result.Text().Length <= 10_000, $"{result.Text().Length} characters");
        using var body = result.Json();
        var note = body.RootElement.GetProperty("truncated");
        Assert.True(note.GetProperty("omitted").GetInt32() > 0);
        Assert.Contains("filter", note.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task The_tool_descriptions_name_the_series_settings_and_the_new_kinds()
    {
        await using var h = await CreateAsync();
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        foreach (var tool in new[] { "query_indicator", "create_widget" })
        {
            foreach (var word in new[] { "seriesBy", "layout", "pyramid", "scale", "heatmap", "likert", "crosstab" })
            {
                Assert.Contains(word, tools[tool].Description);
            }
        }

        Assert.Contains("series", tools["get_widget_data"].Description, StringComparison.OrdinalIgnoreCase);
    }

    // Versions of the dashboards a request changes (mcp-server "Request given with dashboard changes", "One version per request")

    private const string AddWaterWidgets = "Add water widgets";

    private static HttpClient Rest(McpProjectHarness h, string person) =>
        h.Factory.CreateClient().WithToken(TestTokens.Create(person, person, $"{person}@example.org"));

    /// <summary>Adds a dashboard in the page, as the analyst: its version 1 is made in Tamiza.</summary>
    private static async Task<Guid> AddDashboardInTamizaAsync(McpProjectHarness h, string name)
    {
        var response = await Rest(h, "analyst").PostAsJsonAsync($"/api/v1/projects/{h.ProjectId}/dashboards", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<CallToolResult> AddWidgetForAsync(McpProjectHarness h, Guid dashboard, string title, string change, string person = "analyst") =>
        h.CallAsync(person, "create_widget", new { dashboardId = dashboard, title, chartType = "bar", config = BarConfig(), change });

    /// <summary>A dashboard's versions, oldest first, as <c>number | author | origin | note | changes</c>, the changes as PostgreSQL prints them.</summary>
    private static async Task<string[]> VersionsAsync(McpProjectHarness h, Guid dashboardId)
    {
        var rows = await TestData.ScalarAsync<string>(h.Db,
            """
            SELECT coalesce(string_agg(format('%s | %s | %s | %s | %s', v.number, u.keycloak_sub, v.origin, coalesce(v.note, '-'), v.changes), E'\n' ORDER BY v.number), '')
            FROM tamiza.dashboard_versions AS v JOIN tamiza.users AS u ON u.id = v.author_id
            WHERE v.project_id = @project AND v.dashboard_id = @dashboard
            """,
            ("project", h.ProjectId), ("dashboard", dashboardId));
        return rows!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<int[]> NumbersAsync(McpProjectHarness h, Guid dashboardId) =>
        (await VersionsAsync(h, dashboardId)).Select(row => int.Parse(row.Split(" | ")[0], CultureInfo.InvariantCulture)).ToArray();

    private static Task<long> AllVersionsAsync(McpProjectHarness h) =>
        TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_versions WHERE project_id = @project", ("project", h.ProjectId));

    /// <summary>The widget titles a version holds, in reading order.</summary>
    private static async Task<string[]> TitlesAsync(McpProjectHarness h, Guid dashboardId, int number)
    {
        using var content = JsonDocument.Parse((await TestData.ScalarAsync<string>(h.Db,
            "SELECT content::text FROM tamiza.dashboard_versions WHERE project_id = @project AND dashboard_id = @dashboard AND number = @number",
            ("project", h.ProjectId), ("dashboard", dashboardId), ("number", number)))!);
        return content.RootElement.GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("title").GetString()!).ToArray();
    }

    /// <summary>The <c>dashboardVersions</c> of a write's result, as <c>(dashboard, version)</c>.</summary>
    private static (Guid Dashboard, int Version)[] DashboardVersionsOf(CallToolResult result)
    {
        Assert.Null(result.Code());
        using var body = result.Json();
        return body.RootElement.GetProperty("dashboardVersions").EnumerateArray()
            .Select(v => (v.GetProperty("dashboardId").GetGuid(), v.GetProperty("version").GetInt32()))
            .ToArray();
    }

    private static string Changes(params string[] entries) => "[" + string.Join(", ", entries) + "]";

    private static string Added(string title) => $$"""{"kind": "widgetAdded", "widget": "{{title}}"}""";

    [Fact]
    public async Task Request_missing_blank_or_too_long_on_every_dashboard_write_is_refused_naming_change_and_saves_nothing()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.dashboard_widgets (id, project_id, dashboard_id, chart_type, title, config, layout, created_by, updated_by) VALUES (gen_random_uuid(), @p, @d, 'stat', 'Rate', '{\"indicator\":\"piped_water_rate\",\"measure\":\"value\",\"filters\":[]}'::jsonb, '{\"x\":0,\"y\":0,\"w\":3,\"h\":2}'::jsonb, @a, @a)",
            ("p", h.ProjectId), ("d", dashboard), ("a", h.Users["analyst"]));
        var widget = await TestData.ScalarAsync<Guid>(h.Db, "SELECT id FROM tamiza.dashboard_widgets");
        var before = await TestData.ScalarAsync<string>(h.Db,
            "SELECT string_agg(format('%s %s %s', d.name, d.filters, w.title), ',') FROM tamiza.dashboards d LEFT JOIN tamiza.dashboard_widgets w ON w.dashboard_id = d.id");
        var calls = new (string Tool, Dictionary<string, object?> Args)[]
        {
            ("create_dashboard", new() { ["name"] = "Water access" }),
            ("update_dashboard", new() { ["dashboardId"] = dashboard, ["name"] = "Agua" }),
            ("duplicate_dashboard", new() { ["dashboardId"] = dashboard, ["name"] = "Copy" }),
            ("create_widget", new() { ["dashboardId"] = dashboard, ["title"] = "By municipality", ["chartType"] = "bar", ["config"] = BarConfig() }),
            ("update_widget", new() { ["dashboardId"] = dashboard, ["widgetId"] = widget, ["title"] = "Renamed" }),
            ("remove_widget", new() { ["dashboardId"] = dashboard, ["widgetId"] = widget, ["expectedTitle"] = "Rate" }),
        };

        foreach (var (tool, args) in calls)
        {
            var missing = await h.CallAsync("analyst", tool, args);
            Assert.True(missing.Code() == "mcp.validation", $"{tool}: {missing.Text()}");
            Assert.Contains("change", missing.Text());
            // Under the same key as a blank one, although the SDK refuses it before the tool runs.
            using (var left = missing.Json())
            {
                Assert.True(left.RootElement.GetProperty("fields").TryGetProperty("change", out _), $"{tool}: {missing.Text()}");
            }

            foreach (var change in new[] { "   ", new string('x', 121) })
            {
                var refused = await h.CallAsync("analyst", tool, new Dictionary<string, object?>(args) { ["change"] = change });
                Assert.Equal("mcp.validation", refused.Code());
                using var body = refused.Json();
                Assert.True(body.RootElement.GetProperty("fields").TryGetProperty("change", out _), $"{tool}: {refused.Text()}");
            }
        }

        Assert.Equal(before, await TestData.ScalarAsync<string>(h.Db,
            "SELECT string_agg(format('%s %s %s', d.name, d.filters, w.title), ',') FROM tamiza.dashboards d LEFT JOIN tamiza.dashboard_widgets w ON w.dashboard_id = d.id"));
        Assert.Equal(0, await AllVersionsAsync(h));
        // 120 characters, surrounding spaces aside, are enough.
        Assert.Null((await AddWidgetForAsync(h, dashboard, "By municipality", "  " + new string('x', 120) + "  ")).Code());
    }

    [Fact]
    public async Task Version_returned_a_widget_added_to_water_access_whose_current_version_is_4_gives_version_5_as_get_dashboard_and_list_dashboards_do()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water");
        var rest = Rest(h, "analyst");
        foreach (var name in new[] { "Water 2", "Water 3", "Water access" })
        {
            Assert.Equal(HttpStatusCode.OK, (await rest.PatchAsJsonAsync($"/api/v1/projects/{h.ProjectId}/dashboards/{water}", new { name })).StatusCode);
        }

        using var read = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = water })).Json();
        var result = await AddWidgetForAsync(h, water, "By municipality", AddWaterWidgets);

        Assert.Equal(4, read.RootElement.GetProperty("version").GetInt32());
        Assert.Equal([(water, 5)], DashboardVersionsOf(result));
        using var body = result.Json();
        Assert.Equal("By municipality", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(water, body.RootElement.GetProperty("dashboardId").GetGuid());
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = water })).Json();
        Assert.Equal(5, viewed.RootElement.GetProperty("version").GetInt32());
        using var list = (await h.CallAsync("viewer", "list_dashboards")).Json();
        var entries = list.RootElement.GetProperty("dashboards").EnumerateArray().ToDictionary(d => d.GetProperty("name").GetString()!);
        Assert.Equal(5, entries["Water access"].GetProperty("version").GetInt32());
        // The project's first dashboard was seeded with no version: its entry has none.
        Assert.False(entries["Indicators"].TryGetProperty("version", out _));
    }

    [Fact]
    public async Task Only_the_order_changed_moves_the_dashboard_makes_no_version_and_gives_the_current_version_of_water_access()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water access");

        var moved = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, position = 1, change = "Put Water access first" });

        Assert.Equal([(water, 1)], DashboardVersionsOf(moved));
        using var list = (await h.CallAsync("viewer", "list_dashboards")).Json();
        Assert.Equal(["Water access", "Indicators"], list.RootElement.GetProperty("dashboards").EnumerateArray().Select(d => d.GetProperty("name").GetString()!).ToArray());
        Assert.Equal(["""1 | analyst | app | - | [{"kind": "created"}]"""], await VersionsAsync(h, water));
        Assert.Equal(1, await AllVersionsAsync(h));
    }

    [Fact]
    public async Task Five_widgets_one_version_by_the_analyst_via_mcp_holding_the_five_and_showing_the_request()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water access");
        string[] titles = ["Rate", "By municipality", "Santa Ana", "San Luis", "Trend"];

        var results = new List<(Guid, int)[]>();
        foreach (var title in titles)
        {
            results.Add(DashboardVersionsOf(await AddWidgetForAsync(h, water, title, "Add five water indicators")));
        }

        Assert.All(results, r => Assert.Equal([(water, 2)], r));
        Assert.Equal(
            [
                """1 | analyst | app | - | [{"kind": "created"}]""",
                "2 | analyst | mcp | Add five water indicators | " + Changes(titles.Select(Added).ToArray()),
            ],
            await VersionsAsync(h, water));
        Assert.Equal(titles.Order().ToArray(), (await TitlesAsync(h, water, 2)).Order().ToArray());
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db,
            "SELECT count(*) FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 2 AND author_id = @a AND created_at <= saved_at",
            ("d", water), ("a", h.Users["analyst"])));
    }

    [Fact]
    public async Task New_dashboard_with_its_widgets_has_only_version_1_holding_the_three_widgets()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        const string build = "Build the water dashboard";

        var created = await h.CallAsync("analyst", "create_dashboard", new { name = "Water access", change = build });
        using var body = created.Json();
        var water = body.RootElement.GetProperty("id").GetGuid();
        foreach (var title in new[] { "Rate", "By municipality", "Trend" })
        {
            Assert.Equal([(water, 1)], DashboardVersionsOf(await AddWidgetForAsync(h, water, title, build)));
        }

        Assert.Equal([(water, 1)], DashboardVersionsOf(created));
        Assert.Equal(
            ["1 | analyst | mcp | Build the water dashboard | " + Changes("""{"kind": "created"}""", Added("Rate"), Added("By municipality"), Added("Trend"))],
            await VersionsAsync(h, water));
        Assert.Equal(["Rate", "By municipality", "Trend"], await TitlesAsync(h, water, 1));
    }

    [Fact]
    public async Task Two_dashboards_in_one_request_make_one_new_version_of_each_and_the_next_request_another()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water");
        var health = await AddDashboardInTamizaAsync(h, "Health");
        const string request = "Add the coverage widgets";

        await AddWidgetForAsync(h, water, "Rate", request);
        await AddWidgetForAsync(h, health, "Coverage", request);
        await AddWidgetForAsync(h, water, "By municipality", request);

        Assert.Equal(Enumerable.Range(1, 2), await NumbersAsync(h, water));
        Assert.Equal(Enumerable.Range(1, 2), await NumbersAsync(h, health));
        Assert.Equal("2 | analyst | mcp | Add the coverage widgets | " + Changes(Added("Rate"), Added("By municipality")), (await VersionsAsync(h, water))[^1]);

        // Next request.
        var renamed = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, name = "Agua", change = "Rename Water to Agua" });

        Assert.Equal([(water, 3)], DashboardVersionsOf(renamed));
        Assert.Equal("""3 | analyst | mcp | Rename Water to Agua | [{"kind": "renamed", "dashboard": "Agua"}]""", (await VersionsAsync(h, water))[^1]);
        Assert.Equal(2, (await VersionsAsync(h, health)).Length);
    }

    [Fact]
    public async Task Member_changes_in_between_make_three_versions_the_two_widgets_the_drag_and_the_third_widget()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water");
        await AddWidgetForAsync(h, water, "Rate", AddWaterWidgets);
        using var second = (await AddWidgetForAsync(h, water, "By municipality", AddWaterWidgets)).Json();

        // The analyst drags a widget of "Water" in the page, which saves its layout in one request.
        var dragged = await Rest(h, "analyst").PutAsJsonAsync($"/api/v1/projects/{h.ProjectId}/dashboards/{water}/layout",
            new { widgets = new[] { new { id = second.RootElement.GetProperty("id").GetGuid(), layout = new { x = 6, y = 0, w = 6, h = 4 } } } });
        var third = await AddWidgetForAsync(h, water, "Trend", AddWaterWidgets);

        Assert.Equal(HttpStatusCode.OK, dragged.StatusCode);
        Assert.Equal([(water, 4)], DashboardVersionsOf(third));
        Assert.Equal(
            [
                """1 | analyst | app | - | [{"kind": "created"}]""",
                "2 | analyst | mcp | Add water widgets | " + Changes(Added("Rate"), Added("By municipality")),
                """3 | analyst | app | - | [{"kind": "layoutChanged"}]""",
                "4 | analyst | mcp | Add water widgets | " + Changes(Added("Trend")),
            ],
            await VersionsAsync(h, water));
    }

    [Fact]
    public async Task Long_pause_of_40_minutes_between_two_widgets_of_one_request_makes_two_versions()
    {
        var clock = new AdjustableTimeProvider();
        await using var h = await McpProjectHarness.CreateAsync(postgres, McpAccess.Aggregates,
            configureServices: services => services.AddSingleton<TimeProvider>(clock));
        await SeedAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water");

        await AddWidgetForAsync(h, water, "Rate", AddWaterWidgets);
        clock.Advance(TimeSpan.FromMinutes(40));
        var later = await AddWidgetForAsync(h, water, "By municipality", AddWaterWidgets);

        Assert.Equal([(water, 3)], DashboardVersionsOf(later));
        Assert.Equal(
            [
                """1 | analyst | app | - | [{"kind": "created"}]""",
                "2 | analyst | mcp | Add water widgets | " + Changes(Added("Rate")),
                "3 | analyst | mcp | Add water widgets | " + Changes(Added("By municipality")),
            ],
            await VersionsAsync(h, water));
        Assert.True(await TestData.ScalarAsync<bool>(h.Db,
            "SELECT (SELECT created_at FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 3) - (SELECT saved_at FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 2) >= interval '40 minutes'",
            ("d", water)));
    }

    [Fact]
    public async Task Calls_at_the_same_time_make_one_version_that_holds_both_widgets_and_lists_both_as_added()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water");

        var results = await Task.WhenAll(
            AddWidgetForAsync(h, water, "Rate", AddWaterWidgets),
            AddWidgetForAsync(h, water, "By municipality", AddWaterWidgets));

        Assert.All(results, r => Assert.Equal([(water, 2)], DashboardVersionsOf(r)));
        var versions = await VersionsAsync(h, water);
        Assert.Equal(2, versions.Length);
        Assert.StartsWith("2 | analyst | mcp | Add water widgets | [", versions[1]);
        Assert.Contains(Added("Rate"), versions[1]);
        Assert.Contains(Added("By municipality"), versions[1]);
        Assert.Equal(["By municipality", "Rate"], (await TitlesAsync(h, water, 2)).Order().ToArray());
    }

    [Fact]
    public async Task Part_of_a_call_refused_the_rename_saved_before_filters_that_are_not_valid_is_a_version_with_the_filters_it_had()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water");
        Assert.Equal(HttpStatusCode.OK, (await Rest(h, "analyst").PutAsJsonAsync($"/api/v1/projects/{h.ProjectId}/dashboards/{water}/filters",
            new { filters = new[] { new { column = "municipality", label = "Municipality", kind = "text" } } })).StatusCode);

        var result = await h.CallAsync("analyst", "update_dashboard", new
        {
            dashboardId = water,
            name = "Agua",
            filters = new[] { new { column = "municipality", label = "Municipality", kind = "colour" } },
            change = "Rename Water and filter by municipality",
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("filters[0].kind", result.Text());
        Assert.Equal("""3 | analyst | mcp | Rename Water and filter by municipality | [{"kind": "renamed", "dashboard": "Agua"}]""", (await VersionsAsync(h, water))[^1]);
        Assert.Equal("""{"name": "Agua", "filters": [{"kind": "text", "label": "Municipality", "column": "municipality", "default": null}], "widgets": []}""",
            await TestData.ScalarAsync<string>(h.Db, "SELECT content::text FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 3", ("d", water)));
    }

    [Fact]
    public async Task The_name_it_has_given_with_new_filters_is_no_rename_and_the_version_lists_only_the_filters()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water");

        var result = await h.CallAsync("analyst", "update_dashboard", new
        {
            dashboardId = water,
            name = "Water",
            filters = new[] { new { column = "municipality", label = "Municipality", kind = "text" } },
            change = "Add a municipality filter",
        });

        Assert.Equal([(water, 2)], DashboardVersionsOf(result));
        Assert.Equal(
            [
                """1 | analyst | app | - | [{"kind": "created"}]""",
                """2 | analyst | mcp | Add a municipality filter | [{"kind": "filtersChanged"}]""",
            ],
            await VersionsAsync(h, water));
    }

    [Fact]
    public async Task The_name_it_has_alone_in_a_request_that_grows_a_version_leaves_its_changes_and_its_time_as_they_were()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water");
        Assert.Null((await AddWidgetForAsync(h, water, "Rate", AddWaterWidgets)).Code());
        const string version = "SELECT format('%s %s', changes, saved_at) FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 2";
        const string dashboard = "SELECT format('%s %s %s', name, updated_by, updated_at) FROM tamiza.dashboards WHERE id = @d";
        var before = await TestData.ScalarAsync<string>(h.Db, version, ("d", water));
        var stored = await TestData.ScalarAsync<string>(h.Db, dashboard, ("d", water));

        // The same request, which would grow version 2, gives the name the dashboard has.
        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, name = "Water", change = AddWaterWidgets });

        Assert.Equal([(water, 2)], DashboardVersionsOf(result));
        Assert.Equal(Enumerable.Range(1, 2), await NumbersAsync(h, water));
        Assert.Equal(before, await TestData.ScalarAsync<string>(h.Db, version, ("d", water)));
        Assert.Equal(stored, await TestData.ScalarAsync<string>(h.Db, dashboard, ("d", water)));
    }

    [Fact]
    public async Task A_create_dashboard_whose_move_is_refused_with_order_changed_still_has_its_version_1()
    {
        var removal = new RemovalAfterOrderRead();
        await using var h = await McpProjectHarness.CreateAsync(postgres, McpAccess.Aggregates,
            configureServices: services => services.ConfigureDbContext<TamizaDbContext>(options => options.AddInterceptors(removal)));
        var field = await AddDashboardInTamizaAsync(h, "Field");
        removal.Arm(h.Db, field);

        var result = await h.CallAsync("analyst", "create_dashboard", new { name = "Water access", position = 1, change = "Add a water dashboard first" });

        Assert.Equal(1, removal.Runs);
        Assert.Equal("dashboards.orderChanged", result.Code());
        var water = await TestData.ScalarAsync<Guid>(h.Db, "SELECT id FROM tamiza.dashboards WHERE name = 'Water access'");
        Assert.Equal(["""1 | analyst | mcp | Add a water dashboard first | [{"kind": "created"}]"""], await VersionsAsync(h, water));
    }

    /// <summary>
    /// Another member's removal of a dashboard between the order a move read and its reorder: once armed, it removes a dashboard on a
    /// connection of its own just after the next read of the project's order, once.
    /// </summary>
    private sealed class RemovalAfterOrderRead : DbCommandInterceptor
    {
        private (string Db, Guid DashboardId)? _armed;

        public int Runs { get; private set; }

        public void Arm(string db, Guid dashboardId) => _armed = (db, dashboardId);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            // The move's read of the order: the ids alone, in their order.
            var sql = command.CommandText.ReplaceLineEndings(" ");
            if (_armed is { } armed && sql.StartsWith("SELECT d.id FROM tamiza.dashboards AS d ", StringComparison.Ordinal)
                && sql.Contains("ORDER BY d.position", StringComparison.Ordinal))
            {
                _armed = null;
                Runs++;
                await TestData.ExecuteAsync(armed.Db, "DELETE FROM tamiza.dashboards WHERE id = @id", ("id", armed.DashboardId));
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task A_flush_that_fails_leaves_the_widget_added_and_its_dashboard_out_of_dashboard_versions()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres, McpAccess.Aggregates,
            configureServices: services => services.ConfigureDbContext<TamizaDbContext>(options => options.AddInterceptors(new UnavailableVersions())));
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var result = await AddWidgetForAsync(h, dashboard, "By municipality", AddWaterWidgets);

        // The widget is saved and the call succeeds; no version holds it, so none is named.
        Assert.Empty(DashboardVersionsOf(result));
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets WHERE title = 'By municipality'"));
        Assert.Equal(0, await AllVersionsAsync(h));
    }

    /// <summary>A database that refuses every new dashboard version, as one that fails during the flush would.</summary>
    private sealed class UnavailableVersions : DbCommandInterceptor
    {
        private static void Refuse(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO tamiza.dashboard_versions", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The dashboard versions are unavailable.");
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Refuse(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Refuse(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task A_widget_moved_to_another_dashboard_gives_the_version_of_both_and_a_copy_the_version_1_of_the_copy()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var health = await AddDashboardInTamizaAsync(h, "Health");
        var field = await AddDashboardInTamizaAsync(h, "Field");
        using var added = (await AddWidgetForAsync(h, health, "Coverage", "Add coverage")).Json();

        var moved = await h.CallAsync("analyst", "update_widget",
            new { dashboardId = health, widgetId = added.RootElement.GetProperty("id").GetGuid(), moveToDashboardId = field, change = "Move coverage to Field" });
        var copy = await h.CallAsync("analyst", "duplicate_dashboard", new { dashboardId = field, name = "Field copy", change = "Copy Field" });

        Assert.Equal([(health, 3), (field, 2)], DashboardVersionsOf(moved));
        Assert.Equal("""3 | analyst | mcp | Move coverage to Field | [{"kind": "widgetMovedOut", "widget": "Coverage", "dashboard": "Field"}]""", (await VersionsAsync(h, health))[^1]);
        Assert.Equal("""2 | analyst | mcp | Move coverage to Field | [{"kind": "widgetMovedIn", "widget": "Coverage", "dashboard": "Health"}]""", (await VersionsAsync(h, field))[^1]);
        using var copyBody = copy.Json();
        var copyId = copyBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal([(copyId, 1)], DashboardVersionsOf(copy));
        Assert.Equal(2, (await VersionsAsync(h, field)).Length);
    }

    // Colors (mcp-server "Dashboards and widgets through MCP", design D10 of dashboard palettes)

    private const string ColorsRequest = "Color the water dashboard";

    private static object Kept(string column, string value, int color) => new { column, value, color };

    /// <summary>The indicator <c>households</c>: a group, a series and a geometry column to draw a map from.</summary>
    private static Task SeedHouseholdsAsync(McpProjectHarness h) =>
        PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "households", "municipality text, sex text, geometry text, value numeric",
            "('Cali', 'Female', 'POINT(0 0)', 40), ('Cali', 'Male', 'POINT(0 0)', 35), ('Palmira', 'Female', 'POINT(1 1)', 60)");

    /// <summary>A dashboard's palette and colors kept by value as <c>get_dashboard</c> gives them: <c>earth [sex=Female:5, sex=Male:2]</c>, the colors as <c>column=value:color</c>.</summary>
    private static async Task<string> ColorsOfAsync(McpProjectHarness h, Guid dashboard, string person = "viewer")
    {
        using var body = (await h.CallAsync(person, "get_dashboard", new { dashboardId = dashboard })).Json();
        var colors = body.RootElement.GetProperty("valueColors").EnumerateArray()
            .Select(c => $"{c.GetProperty("column").GetString()}={c.GetProperty("value").GetString()}:{c.GetProperty("color").GetInt32()}");
        return $"{body.RootElement.GetProperty("palette").GetString()} [{string.Join(", ", colors)}]";
    }

    private static Task<string?> StoredPaletteAsync(McpProjectHarness h, Guid dashboard) =>
        TestData.ScalarAsync<string>(h.Db, "SELECT palette FROM tamiza.dashboards WHERE id = @d", ("d", dashboard));

    [Fact]
    public async Task Colors_through_mcp_a_palette_and_a_color_kept_then_a_map_on_the_green_scale_are_saved_and_read_back_by_the_tools_and_the_page()
    {
        await using var h = await CreateAsync();
        await SeedHouseholdsAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water access");

        var colored = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, palette = "earth", valueColors = new[] { Kept("sex", "Female", 5) }, change = ColorsRequest });
        var map = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = water, title = "Households", chartType = "map", change = ColorsRequest,
            config = new { indicator = "households", measure = "value", groupBy = "municipality", geometryColumn = "geometry", display = new { ramp = "green" } },
        });

        // The call answers with the colors as saved, and both writes are one version of the request.
        Assert.Equal([(water, 2)], DashboardVersionsOf(colored));
        using (var answer = colored.Json())
        {
            Assert.Equal("earth", answer.RootElement.GetProperty("palette").GetString());
            Assert.Equal("""[{"column":"sex","value":"Female","color":5}]""", answer.RootElement.GetProperty("valueColors").GetRawText());
        }

        Assert.Null(map.Code());
        Assert.Equal([(water, 2)], DashboardVersionsOf(map));
        Assert.Equal(
            [
                """1 | analyst | app | - | [{"kind": "created"}]""",
                "2 | analyst | mcp | " + ColorsRequest + " | " + Changes("""{"kind": "colorsChanged"}""", Added("Households")),
            ],
            await VersionsAsync(h, water));
        // get_dashboard gives the palette, the color kept and the widget's scale to a viewer, and the page gives the same.
        Assert.Equal("earth [sex=Female:5]", await ColorsOfAsync(h, water));
        using (var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = water })).Json())
        {
            Assert.Equal("green", viewed.RootElement.GetProperty("widgets")[0].GetProperty("config").GetProperty("display").GetProperty("ramp").GetString());
        }

        var page = await Rest(h, "viewer").GetAsync($"/api/v1/projects/{h.ProjectId}/dashboards/{water}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using (var pageBody = JsonDocument.Parse(await page.Content.ReadAsStringAsync()))
        {
            Assert.Equal("earth", pageBody.RootElement.GetProperty("palette").GetString());
            Assert.Equal("""[{"column":"sex","value":"Female","color":5}]""", pageBody.RootElement.GetProperty("valueColors").GetRawText());
            Assert.Equal("green", pageBody.RootElement.GetProperty("widgets")[0].GetProperty("config").GetProperty("display").GetProperty("ramp").GetString());
        }

        // The version holds the colors, and a version from before any colors reads the defaults.
        using var kept = (await h.CallAsync("analyst", "get_dashboard_version", new { dashboardId = water, version = 2 })).Json();
        Assert.Equal("earth", kept.RootElement.GetProperty("palette").GetString());
        Assert.Equal("""[{"column":"sex","value":"Female","color":5}]""", kept.RootElement.GetProperty("valueColors").GetRawText());
        using var first = (await h.CallAsync("analyst", "get_dashboard_version", new { dashboardId = water, version = 1 })).Json();
        Assert.Equal("tamiza", first.RootElement.GetProperty("palette").GetString());
        Assert.Equal("[]", first.RootElement.GetProperty("valueColors").GetRawText());
    }

    [Fact]
    public async Task Unknown_palette_through_mcp_is_refused_with_the_error_on_the_palette_and_nothing_is_saved()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water access");

        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, palette = "neon", change = ColorsRequest });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("palette: Choose one of the palettes: tamiza, institutional, earth, vivid.", result.Text());
        using (var body = result.Json())
        {
            Assert.Equal(["palette"], body.RootElement.GetProperty("fields").EnumerateObject().Select(f => f.Name).ToArray());
        }

        Assert.Equal("tamiza", await StoredPaletteAsync(h, water));
        Assert.Equal("tamiza []", await ColorsOfAsync(h, water));
        Assert.Equal(Enumerable.Range(1, 1), await NumbersAsync(h, water));
    }

    [Fact]
    public async Task One_call_with_a_rename_and_colors_makes_one_version_that_holds_both()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water");

        var result = await h.CallAsync("analyst", "update_dashboard", new
        {
            dashboardId = water, name = "Agua", palette = "vivid", valueColors = new[] { Kept("sex", "Female", 5), Kept("sex", "Male", 2) }, change = "Rename Water and color it",
        });

        Assert.Equal([(water, 2)], DashboardVersionsOf(result));
        Assert.Equal(
            [
                """1 | analyst | app | - | [{"kind": "created"}]""",
                """2 | analyst | mcp | Rename Water and color it | [{"kind": "renamed", "dashboard": "Agua"}, {"kind": "colorsChanged"}]""",
            ],
            await VersionsAsync(h, water));
        Assert.Equal("vivid", await TestData.ScalarAsync<string>(h.Db, "SELECT content->>'palette' FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 2", ("d", water)));
        Assert.Equal("Agua", await TestData.ScalarAsync<string>(h.Db, "SELECT content->>'name' FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 2", ("d", water)));
        Assert.Equal(2, await TestData.ScalarAsync<int>(h.Db, "SELECT jsonb_array_length(content->'valueColors') FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 2", ("d", water)));
        Assert.Equal("vivid [sex=Female:5, sex=Male:2]", await ColorsOfAsync(h, water));
    }

    [Fact]
    public async Task A_palette_alone_keeps_the_colors_and_the_colors_alone_keep_the_palette_and_an_empty_list_removes_them()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water access");
        Task<CallToolResult> Update(string? palette = null, object[]? valueColors = null) =>
            h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, palette, valueColors, change = ColorsRequest });

        Assert.Null((await Update(palette: "earth", valueColors: [Kept("sex", "Female", 5), Kept("sex", "Male", 2)])).Code());
        Assert.Null((await Update(palette: "vivid")).Code());
        Assert.Equal("vivid [sex=Female:5, sex=Male:2]", await ColorsOfAsync(h, water));

        // The list sent is the whole list, and the palette stays.
        Assert.Null((await Update(valueColors: [Kept("answer", "No", 8)])).Code());
        Assert.Equal("vivid [answer=No:8]", await ColorsOfAsync(h, water));

        Assert.Null((await Update(valueColors: [])).Code());
        Assert.Equal("vivid []", await ColorsOfAsync(h, water));
    }

    [Fact]
    public async Task Colors_the_dashboard_already_has_make_no_version_and_the_call_gives_the_version_it_has()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water access");
        Assert.Equal([(water, 2)], DashboardVersionsOf(await h.CallAsync("analyst", "update_dashboard",
            new { dashboardId = water, palette = "earth", valueColors = new[] { Kept("sex", "Female", 5) }, change = "Color the dashboard" })));
        var stored = await TestData.ScalarAsync<string>(h.Db, "SELECT updated_at::text FROM tamiza.dashboards WHERE id = @d", ("d", water));

        // Another request sets what is already there.
        var again = await h.CallAsync("analyst", "update_dashboard",
            new { dashboardId = water, palette = "earth", valueColors = new[] { Kept("sex", "Female", 5) }, change = "Color it again" });

        Assert.Equal([(water, 2)], DashboardVersionsOf(again));
        Assert.Equal(Enumerable.Range(1, 2), await NumbersAsync(h, water));
        Assert.Equal(stored, await TestData.ScalarAsync<string>(h.Db, "SELECT updated_at::text FROM tamiza.dashboards WHERE id = @d", ("d", water)));
    }

    [Fact]
    public async Task Colors_refused_carry_the_keys_and_messages_of_the_page_and_save_nothing()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water access");

        var result = await h.CallAsync("analyst", "update_dashboard", new
        {
            dashboardId = water, palette = "earth", change = ColorsRequest,
            valueColors = new object[] { Kept("sex", "Female", 5), Kept("sex", "Male", 5), Kept("Bad Column", "x", 9), Kept("sex", "Female", 6) },
        });

        Assert.Equal("mcp.validation", result.Code());
        using (var body = result.Json())
        {
            var fields = body.RootElement.GetProperty("fields");
            Assert.Equal(["valueColors[1]", "valueColors[2].column", "valueColors[2].color", "valueColors[3]"], fields.EnumerateObject().Select(f => f.Name).ToArray());
            Assert.Equal("sex already uses color 5.", fields.GetProperty("valueColors[1]")[0].GetString());
            Assert.Equal("sex already keeps a color for Female.", fields.GetProperty("valueColors[3]")[0].GetString());
        }

        Assert.Equal("tamiza []", await ColorsOfAsync(h, water));
        Assert.Equal(Enumerable.Range(1, 1), await NumbersAsync(h, water));
    }

    [Fact]
    public async Task A_call_that_names_no_setting_to_change_is_refused()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water access");

        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, change = ColorsRequest });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("Give a name, a position, filters, a palette or colors kept by value to change.", result.Text());
        Assert.Equal(Enumerable.Range(1, 1), await NumbersAsync(h, water));
    }

    [Fact]
    public async Task A_rename_saved_before_colors_that_are_refused_is_still_a_version_and_the_palette_is_not_changed()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water");

        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, name = "Agua", palette = "neon", change = "Rename Water and color it" });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("palette", result.Text());
        Assert.Equal("""2 | analyst | mcp | Rename Water and color it | [{"kind": "renamed", "dashboard": "Agua"}]""", (await VersionsAsync(h, water))[^1]);
        Assert.Equal("tamiza", await StoredPaletteAsync(h, water));
    }

    [Fact]
    public async Task Colors_of_a_dashboard_that_is_not_the_projects_are_not_found()
    {
        await using var h = await CreateAsync();

        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = Guid.NewGuid(), palette = "earth", change = ColorsRequest });

        Assert.NotNull(result.Code());
        Assert.NotEqual("mcp.validation", result.Code());
    }

    [Fact]
    public async Task A_widgets_color_is_set_with_display_checked_like_the_widget_form_and_replaced_with_the_config()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        await SeedHouseholdsAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water access");
        object Bar(object? display = null, string? seriesBy = null) => new { indicator = "households", measure = "value", groupBy = "municipality", seriesBy, display };

        var created = await h.CallAsync("analyst", "create_widget", new { dashboardId = water, title = "Households", chartType = "bar", config = Bar(new { color = 3 }), change = ColorsRequest });

        Assert.Null(created.Code());
        using var made = created.Json();
        Assert.Equal(3, made.RootElement.GetProperty("config").GetProperty("display").GetProperty("color").GetInt32());
        var widget = made.RootElement.GetProperty("id").GetGuid();

        // The rules of the widget form: no color with a series, none above 8, a scale only on a map or a heat map, an unknown scale.
        var series = await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = widget, config = Bar(new { color = 3 }, seriesBy: "sex"), change = ColorsRequest });
        var tooHigh = await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = widget, config = Bar(new { color = 9 }), change = ColorsRequest });
        var scale = await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = widget, config = Bar(new { ramp = "green" }), change = ColorsRequest });
        var unknown = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = water, title = "Map", chartType = "map", change = ColorsRequest,
            config = new { indicator = "households", measure = "value", groupBy = "municipality", geometryColumn = "geometry", display = new { ramp = "pink" } },
        });

        Assert.All(new[] { series, tooHigh, scale, unknown }, r => Assert.Equal("mcp.validation", r.Code()));
        Assert.Contains("config.display.color: A widget with a series takes its colors from its series.", series.Text());
        Assert.Contains("config.display.color: The color must be a number from 1 to 8.", tooHigh.Text());
        Assert.Contains("config.display.ramp: Only map and heatmap widgets have a scale.", scale.Text());
        Assert.Contains("config.display.ramp", unknown.Text());
        using (var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = water })).Json())
        {
            var widgets = viewed.RootElement.GetProperty("widgets");
            Assert.Equal(1, widgets.GetArrayLength());
            Assert.Equal(3, widgets[0].GetProperty("config").GetProperty("display").GetProperty("color").GetInt32());
        }

        // A new config replaces the old one whole, so one without display leaves the widget in color 1.
        Assert.Null((await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = widget, config = Bar(), change = ColorsRequest })).Code());
        using var after = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = water })).Json();
        Assert.False(after.RootElement.GetProperty("widgets")[0].GetProperty("config").TryGetProperty("display", out _));
    }

    [Fact]
    public async Task The_tool_descriptions_and_schemas_name_the_color_settings()
    {
        await using var h = await CreateAsync();
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        var update = tools["update_dashboard"];
        var properties = update.JsonSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("palette", out var palette));
        Assert.True(properties.TryGetProperty("valueColors", out var valueColors));
        Assert.DoesNotContain("palette", update.JsonSchema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.DoesNotContain("valueColors", update.JsonSchema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.Contains("tamiza, institutional, earth or vivid", palette.GetProperty("description").GetString());
        Assert.Contains("whole list", valueColors.GetProperty("description").GetString());
        foreach (var word in new[] { "palette", "valueColors", "color 1 to 8", "one version" })
        {
            Assert.Contains(word, update.Description);
        }

        foreach (var tool in new[] { "create_widget", "update_widget" })
        {
            foreach (var word in new[] { "display", "color", "ramp", "blue, teal, green, orange, red, purple or gray", "palette" })
            {
                Assert.Contains(word, tools[tool].Description);
            }

            var config = tools[tool].JsonSchema.GetProperty("properties").GetProperty("config");
            Assert.Contains("\"display\"", config.GetRawText() + tools[tool].JsonSchema.GetRawText());
        }

        foreach (var tool in new[] { "get_dashboard", "get_dashboard_version" })
        {
            Assert.Contains("palette", tools[tool].Description);
            Assert.Contains("valueColors", tools[tool].Description);
        }
    }
}
