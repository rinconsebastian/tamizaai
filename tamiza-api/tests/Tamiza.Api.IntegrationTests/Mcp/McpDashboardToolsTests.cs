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
        Assert.Equal(10, body.RootElement.GetProperty("limits").GetProperty("maxDashboards").GetInt32());
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

        for (var i = 2; i <= 10; i++)
        {
            await AddDashboardAsync(h, $"Dashboard {i}");
        }

        var eleventh = await h.CallAsync("analyst", "create_dashboard", new { name = "Eleventh", change = Request });
        var copied = await h.CallAsync("analyst", "duplicate_dashboard", new { dashboardId = await FirstDashboardAsync(h), name = "Copy", change = Request });
        var taken = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = await FirstDashboardAsync(h), name = "DASHBOARD 3", change = Request });
        var reserved = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = await FirstDashboardAsync(h), name = "Performance", change = Request });

        Assert.Equal("dashboards.limit", eleventh.Code());
        Assert.Equal("dashboards.limit", copied.Code());
        // The page's code, not an mcp. one, and the HTTP status next to it (docs/mcp.md, Troubleshooting).
        foreach (var refused in new[] { eleventh, copied })
        {
            using var body = refused.Json();
            Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
            Assert.Contains("at most 10 dashboards", body.RootElement.GetProperty("message").GetString());
        }

        Assert.Equal("mcp.validation", taken.Code());
        Assert.Equal("mcp.validation", reserved.Code());
        Assert.Equal(10, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboards"));
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
    public async Task A_dashboard_is_duplicated_with_its_widgets_filters_palette_and_colors_right_after_the_original()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var original = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, original, "By municipality", "bar", BarConfig());
        await h.CallAsync("analyst", "update_dashboard", new { dashboardId = original, filters = new[] { new { column = "municipality", label = "Municipality", kind = "text" } }, change = Request });
        var colored = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = original, palette = "earth", valueColors = new[] { Kept("sex", "Female", 5) }, change = Request });
        Assert.Null(colored.Code());
        await AddDashboardAsync(h, "Later");

        using var copy = (await h.CallAsync("analyst", "duplicate_dashboard", new { dashboardId = original, name = "Copy", change = Request })).Json();
        using var list = (await h.CallAsync("viewer", "list_dashboards")).Json();

        Assert.Equal(2, copy.RootElement.GetProperty("position").GetInt32());
        var names = list.RootElement.GetProperty("dashboards").EnumerateArray().Select(d => d.GetProperty("name").GetString()).ToArray();
        Assert.Equal(["Indicators", "Copy", "Later"], names);
        var copied = list.RootElement.GetProperty("dashboards")[1];
        Assert.Equal((1, 1), (copied.GetProperty("widgets").GetInt32(), copied.GetProperty("filters").GetInt32()));

        // The copy has the original's palette and colors kept by value, as get_dashboard gives them and as the column stores them.
        var copyId = copy.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("earth [sex=Female:5]", await ColorsOfAsync(h, copyId));
        Assert.Equal("earth", await StoredPaletteAsync(h, copyId));

        // The two are separate afterwards: changing the copy leaves the original as it was, and the other way round.
        var recolored = await h.CallAsync(
            "analyst", "update_dashboard", new { dashboardId = copyId, palette = "vivid", valueColors = new[] { Kept("sex", "Female", 2), Kept("sex", "Male", 7) }, change = Request });
        Assert.Null(recolored.Code());
        Assert.Equal("vivid [sex=Female:2, sex=Male:7]", await ColorsOfAsync(h, copyId));
        Assert.Equal("earth [sex=Female:5]", await ColorsOfAsync(h, original));
        Assert.Equal("earth", await StoredPaletteAsync(h, original));
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = original, valueColors = Array.Empty<object>(), change = Request })).Code());
        Assert.Equal("earth []", await ColorsOfAsync(h, original));
        Assert.Equal("vivid [sex=Female:2, sex=Male:7]", await ColorsOfAsync(h, copyId));
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
    public async Task Reading_a_dashboard_the_project_does_not_have_gives_the_pages_code_and_the_http_status_whichever_tool_reads_it()
    {
        await using var h = await CreateAsync();
        var missing = Guid.NewGuid();

        var refusals = new[]
        {
            await h.CallAsync("viewer", "get_widget_data", new { dashboardId = missing }),
            await h.CallAsync("viewer", "get_dashboard", new { dashboardId = missing }),
        };

        // The page's code, not an mcp. one, and the HTTP status next to it (docs/mcp.md, Troubleshooting).
        foreach (var refused in refusals)
        {
            Assert.Equal("dashboards.notFound", refused.Code());
            using var body = refused.Json();
            Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        }

        using var widgetData = refusals[0].Json();
        Assert.Equal("The project has no such dashboard. Call list_dashboards.", widgetData.RootElement.GetProperty("message").GetString());
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

    // Box plots (mcp-server "Indicator queries and widget data", "Dashboards and widgets through MCP")

    // Like any row, a box with no group value has no group in the result: the result leaves out what is null.
    private static string GroupOf(JsonElement box) => box.TryGetProperty("group", out var group) && group.ValueKind != JsonValueKind.Null ? group.GetString()! : "-";

    private static async Task SeedHouseholdSizesAsync(McpProjectHarness h)
    {
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "household_size", "municipality text, value numeric",
            "('Cali', 3), ('Cali', 4), ('Cali', 5), ('Cali', 6), ('Cali', 40), ('Buga', 2), ('Buga', 2), (NULL, 7)");
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, unit, format, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'household_size', 1, 'x', 'Household size', 'people', 'count', @a, now())",
            ("p", h.ProjectId), ("a", h.Users["analyst"]));
    }

    [Fact]
    public async Task Checking_a_box_plot_gives_each_box_with_its_figures_formatted_as_decimals_and_no_rows_and_no_outlier_values()
    {
        await using var h = await CreateAsync();
        await SeedHouseholdSizesAsync(h);

        using var body = (await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "boxplot",
            config = new { indicator = "household_size", groupBy = "municipality", sort = new { by = "value", direction = "desc" } },
        })).Json();

        var root = body.RootElement;
        Assert.False(root.TryGetProperty("rows", out _));
        Assert.False(root.TryGetProperty("cells", out _));
        var boxes = root.GetProperty("boxes").EnumerateArray().ToArray();
        // By median, largest first: the box with no group (7), Cali (5) and Buga (2).
        Assert.Equal(["-", "Cali", "Buga"], boxes.Select(b => GroupOf(b)));
        var cali = boxes[1];
        Assert.Equal(5, cali.GetProperty("values").GetInt64());
        Assert.Equal([3.0, 4.0, 5.0, 6.0, 40.0], new[] { "min", "q1", "median", "q3", "max" }.Select(n => cali.GetProperty(n).GetDouble()));
        Assert.Equal([3.0, 6.0], new[] { cali.GetProperty("lowerWhisker").GetDouble(), cali.GetProperty("upperWhisker").GetDouble() });
        Assert.Equal(1, cali.GetProperty("outlierCount").GetInt64());
        // A count's figures have two decimals. The result lists no outliers: the maximum is one box's figure, which here is also its one outlier.
        Assert.Equal("5.00 people", cali.GetProperty("formatted").GetProperty("median").GetString());
        Assert.Equal("40.00 people", cali.GetProperty("formatted").GetProperty("max").GetString());
        Assert.False(cali.TryGetProperty("outliers", out _));
        Assert.DoesNotContain("outliers\"", root.GetRawText());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task A_saved_box_plot_is_read_with_get_widget_data_and_a_plain_widget_has_no_boxes()
    {
        await using var h = await CreateAsync();
        await SeedHouseholdSizesAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Spread", "boxplot", new { indicator = "household_size", groupBy = "municipality" });
        await AddWidgetAsync(h, dashboard, "Bars", "bar", new { indicator = "household_size", groupBy = "municipality" });

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var spread = body.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("title").GetString() == "Spread");
        var bars = body.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("title").GetString() == "Bars");
        Assert.Equal("shown", spread.GetProperty("status").GetString());
        Assert.Equal(["Buga", "Cali", "-"], spread.GetProperty("boxes").EnumerateArray().Select(b => GroupOf(b)));
        Assert.False(spread.TryGetProperty("rows", out _));
        Assert.False(bars.TryGetProperty("boxes", out _));
        Assert.True(bars.GetProperty("rows").GetArrayLength() > 0);
    }

    [Fact]
    public async Task A_box_result_longer_than_the_size_is_cut_in_its_boxes_and_names_the_filter_that_asks_for_less()
    {
        await using var h = await CreateAsync(new Dictionary<string, string?> { ["Mcp:MaxResultCharacters"] = "10000" });
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "household_size", "municipality text, value numeric",
            string.Join(", ", Enumerable.Range(1, 200).Select(i => $"('Municipality number {i:000}', {i}), ('Municipality number {i:000}', {i + 1})")));

        var result = await h.CallAsync("viewer", "query_indicator", new { chartType = "boxplot", config = new { indicator = "household_size", groupBy = "municipality" } });

        Assert.True(result.Text().Length <= 10_000, $"{result.Text().Length} characters");
        using var body = result.Json();
        var note = body.RootElement.GetProperty("truncated");
        Assert.Equal("boxes", note.GetProperty("list").GetString());
        Assert.True(note.GetProperty("omitted").GetInt32() > 0);
        Assert.Contains("filter", note.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task A_horizontal_box_plot_ordered_by_median_is_saved_and_read_back_with_get_dashboard()
    {
        await using var h = await CreateAsync();
        await SeedHouseholdSizesAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var widget = await AddWidgetAsync(h, dashboard, "Spread", "boxplot", new
        {
            indicator = "household_size", groupBy = "municipality", sort = new { by = "value", direction = "desc" }, display = new { color = 4, orientation = "horizontal" },
        });

        using var body = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var saved = body.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == widget);
        Assert.Equal("boxplot", saved.GetProperty("chartType").GetString());
        Assert.Equal("horizontal", saved.GetProperty("config").GetProperty("display").GetProperty("orientation").GetString());
        Assert.Equal(4, saved.GetProperty("config").GetProperty("display").GetProperty("color").GetInt32());
        Assert.Equal("desc", saved.GetProperty("config").GetProperty("sort").GetProperty("direction").GetString());
    }

    [Theory]
    [InlineData("boxplot", """{"indicator":"household_size","groupBy":"municipality","seriesBy":"municipality2"}""", "seriesBy")]
    [InlineData("boxplot", """{"indicator":"household_size","groupBy":"municipality","display":{"orientation":"sideways"}}""", "display.orientation")]
    [InlineData("column", """{"indicator":"household_size","groupBy":"municipality","display":{"orientation":"horizontal"}}""", "display.orientation")]
    public async Task A_box_plot_that_breaks_a_rule_is_refused_with_the_key_and_nothing_is_saved(string chartType, string config, string key)
    {
        await using var h = await CreateAsync();
        await SeedHouseholdSizesAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Bad", chartType, config = JsonDocument.Parse(config).RootElement, change = Request,
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains(key, result.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Theory]
    [InlineData("""{"indicator":"household_size","groupBy":"municipality","seriesBy":"municipality2"}""", "seriesBy")]
    [InlineData("""{"indicator":"household_size","groupBy":"municipality","display":{"orientation":"diagonal"}}""", "display.orientation")]
    public async Task A_saved_box_plot_updated_into_one_that_breaks_a_rule_is_refused_with_the_key_and_stays_as_it_was(string config, string key)
    {
        await using var h = await CreateAsync();
        await SeedHouseholdSizesAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "Spread", "boxplot", new { indicator = "household_size", groupBy = "municipality", display = new { orientation = "horizontal" } });

        var result = await h.CallAsync("analyst", "update_widget", new { dashboardId = dashboard, widgetId = widget, config = JsonDocument.Parse(config).RootElement, change = Request });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains(key, result.Text());
        Assert.Contains("\"orientation\": \"horizontal\"", await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task A_bare_chart_type_change_of_a_horizontal_box_plot_is_refused_on_the_orientation()
    {
        await using var h = await CreateAsync();
        await SeedHouseholdSizesAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "Spread", "boxplot", new { indicator = "household_size", groupBy = "municipality", display = new { orientation = "horizontal" } });

        var result = await h.CallAsync("analyst", "update_widget", new { dashboardId = dashboard, widgetId = widget, chartType = "column", change = Request });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("display.orientation", result.Text());
    }

    [Fact]
    public async Task The_row_limit_note_of_a_box_plot_counts_boxes()
    {
        await using var h = await CreateAsync(new Dictionary<string, string?> { ["IndicatorDashboard:MaxWidgetQueryRows"] = "2" });
        await SeedHouseholdSizesAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Spread", "boxplot", new { indicator = "household_size", groupBy = "municipality" });
        await AddWidgetAsync(h, dashboard, "Bars", "bar", new { indicator = "household_size", groupBy = "municipality" });

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var widgets = body.RootElement.GetProperty("widgets").EnumerateArray().ToList();
        var notes = (string title) => widgets.Single(w => w.GetProperty("title").GetString() == title).GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Contains("The row limit was reached (2 boxes).", notes("Spread"));
        Assert.Contains("The row limit was reached (2 rows).", notes("Bars"));
    }

    [Fact]
    public async Task The_tool_descriptions_name_the_box_plot_and_its_orientation()
    {
        await using var h = await CreateAsync();
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        foreach (var tool in new[] { "query_indicator", "create_widget" })
        {
            foreach (var word in new[] { "boxplot", "boxes", "outlierCount", "orientation", "median" })
            {
                Assert.Contains(word, tools[tool].Description);
            }
        }

        Assert.Contains("boxplot returns boxes", tools["get_widget_data"].Description);
        Assert.Contains("orientation", tools["update_widget"].Description);
    }

    // Chart annotations (mcp-server "Chart annotations through MCP", "Reference lines in MCP widget data")

    /// <summary>
    /// <c>piped_water_rate</c> with three municipalities whose rates have a mean of 0.4111 and an overall value of 13/50 = 0.26: the mean of the
    /// bars is not the indicator's value over all its rows.
    /// </summary>
    private static async Task SeedThreeAsync(McpProjectHarness h)
    {
        await SeedAsync(h);
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "piped_water_rate", Columns, "('Santa Ana', 0.9, 9, 10),('San Luis', 0.3, 3, 10),('Pasto', 0.1, 1, 30)");
    }

    private static object Lines(params object[] lines) => lines;

    private static object AnnotatedConfig(params object[] lines) => new
    {
        indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality",
        display = new { referenceLines = Lines(lines) },
    };

    [Fact]
    public async Task Lines_in_widget_data_give_their_kind_label_value_and_formatted_value_and_a_widget_without_lines_has_no_such_key()
    {
        await using var h = await CreateAsync();
        await SeedThreeAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Marked", "column", AnnotatedConfig(new { kind = "mean" }, new { kind = "overall" }, new { kind = "value", value = 0.5, label = "Meta" }));
        await AddWidgetAsync(h, dashboard, "Plain", "column", BarConfig());

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var widgets = body.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);
        var lines = widgets["Marked"].GetProperty("referenceLines").EnumerateArray().ToList();
        Assert.Equal(["mean", "overall", "value"], lines.Select(l => l.GetProperty("kind").GetString()));
        Assert.Equal(["Mean", "Overall", "Meta"], lines.Select(l => l.GetProperty("label").GetString()));
        Assert.Equal((0.9 + 0.3 + 1.0 / 30) / 3, lines[0].GetProperty("value").GetDouble(), 9);
        Assert.Equal(0.26, lines[1].GetProperty("value").GetDouble(), 9);
        Assert.Equal(0.5, lines[2].GetProperty("value").GetDouble(), 9);
        Assert.Equal(["41.11%", "26%", "50%"], lines.Select(l => l.GetProperty("formatted").GetString()));
        // Bars and lines the widget does not ask for are as before: the rows, and no key for the lines.
        Assert.False(widgets["Plain"].TryGetProperty("referenceLines", out _));
        Assert.Equal(
            ["chartType", "indicator", "label", "rows", "status", "title", "widgetId"],
            widgets["Plain"].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_overall_line_previewed_is_the_figure_of_a_stat_tile_with_the_same_ratio_and_saves_nothing()
    {
        await using var h = await CreateAsync();
        await SeedThreeAsync(h);
        var config = AnnotatedConfig(new { kind = "overall" }, new { kind = "median", label = "Mediana" });

        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "column", config })).Json();
        using var tile = (await h.CallAsync("viewer", "query_indicator", new { chartType = "stat", config = new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" } } })).Json();

        var lines = preview.RootElement.GetProperty("referenceLines").EnumerateArray().ToList();
        var tileValue = tile.RootElement.GetProperty("rows")[0].GetProperty("value").GetDouble();
        Assert.Equal(["overall", "median"], lines.Select(l => l.GetProperty("kind").GetString()));
        Assert.Equal(["Overall", "Mediana"], lines.Select(l => l.GetProperty("label").GetString()));
        Assert.Equal(tileValue, lines[0].GetProperty("value").GetDouble(), 12);
        Assert.Equal(0.26, tileValue, 9);
        Assert.Equal(0.3, lines[1].GetProperty("value").GetDouble(), 9);
        Assert.Equal(3, preview.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));

        // Without a chart type there is no widget to draw lines on, and a widget without lines has no key.
        using var noType = (await h.CallAsync("viewer", "query_indicator", new { config })).Json();
        using var plain = (await h.CallAsync("viewer", "query_indicator", new { chartType = "column", config = BarConfig() })).Json();
        Assert.False(noType.RootElement.TryGetProperty("referenceLines", out _));
        Assert.False(plain.RootElement.TryGetProperty("referenceLines", out _));
    }

    [Fact]
    public async Task A_preview_that_plots_nothing_has_no_lines_even_a_fixed_value()
    {
        await using var h = await CreateAsync();
        await SeedThreeAsync(h);
        var config = new
        {
            indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality",
            filters = new[] { new { column = "municipality", op = "equals", value = "Nowhere" } },
            display = new { referenceLines = Lines(new { kind = "value", value = 0.5 }, new { kind = "mean" }) },
        };

        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "column", config })).Json();

        Assert.Equal(0, preview.RootElement.GetProperty("rows").GetArrayLength());
        Assert.False(preview.RootElement.TryGetProperty("referenceLines", out _));
    }

    [Fact]
    public async Task Lines_through_mcp_are_saved_and_get_dashboard_returns_them_in_the_widgets_display()
    {
        await using var h = await CreateAsync();
        await SeedThreeAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var widget = await AddWidgetAsync(h, dashboard, "Marked", "column", AnnotatedConfig(new { kind = "mean" }, new { kind = "overall", label = "General" }));
        await h.CallAsync("analyst", "update_widget", new
        {
            dashboardId = dashboard, widgetId = widget, change = Request,
            config = new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality", display = new { referenceLines = Lines(new { kind = "value", value = 0.5 }), dataLabels = true } },
        });

        using var body = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var display = body.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == widget).GetProperty("config").GetProperty("display");
        Assert.Equal(0.5, display.GetProperty("referenceLines")[0].GetProperty("value").GetDouble());
        Assert.True(display.GetProperty("dataLabels").GetBoolean());

        var created = await AddWidgetAsync(h, dashboard, "Second", "column", AnnotatedConfig(new { kind = "mean" }, new { kind = "overall", label = "General" }));
        using var again = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var lines = again.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == created).GetProperty("config").GetProperty("display").GetProperty("referenceLines");
        Assert.Equal(["mean", "overall"], lines.EnumerateArray().Select(l => l.GetProperty("kind").GetString()));
        Assert.Equal("General", lines[1].GetProperty("label").GetString());
    }

    [Fact]
    public async Task An_overall_line_over_a_measure_through_mcp_is_refused_on_the_lines_kind_and_nothing_is_saved()
    {
        await using var h = await CreateAsync();
        await SeedThreeAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Sum", chartType = "bar", change = Request,
            config = new { indicator = "piped_water_rate", measure = "value", groupBy = "municipality", display = new { referenceLines = Lines(new { kind = "overall" }) } },
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("config.display.referenceLines[0].kind", result.Text());
        Assert.Contains("An overall line needs a ratio", result.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task A_chart_type_changed_alone_is_refused_on_the_reference_lines_and_the_widget_is_unchanged()
    {
        await using var h = await CreateAsync();
        await SeedThreeAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "Marked", "column", AnnotatedConfig(new { kind = "mean" }));

        var result = await h.CallAsync("analyst", "update_widget", new { dashboardId = dashboard, widgetId = widget, chartType = "pie", change = Request });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("config.display.referenceLines", result.Text());
        using var body = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var saved = body.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == widget);
        Assert.Equal("column", saved.GetProperty("chartType").GetString());
        Assert.Equal("mean", saved.GetProperty("config").GetProperty("display").GetProperty("referenceLines")[0].GetProperty("kind").GetString());
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
    public async Task A_value_with_a_NUL_character_through_mcp_is_refused_on_the_value_and_nothing_is_saved()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water access");

        // PostgreSQL's jsonb cannot hold a NUL, so a value that got through would end as mcp.failed when it is stored.
        var result = await h.CallAsync("analyst", "update_dashboard", new
        {
            dashboardId = water, palette = "earth", change = ColorsRequest,
            valueColors = new[] { Kept("sex", "Male", 2), Kept("sex", "Fe\0male", 5) },
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("valueColors[1].value: The value cannot contain a NUL character.", result.Text());
        using (var body = result.Json())
        {
            Assert.Equal(["valueColors[1].value"], body.RootElement.GetProperty("fields").EnumerateObject().Select(f => f.Name).ToArray());
        }

        Assert.Equal("tamiza", await StoredPaletteAsync(h, water));
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
        Assert.Contains("Give a name, a position, filters, a palette, colors kept by value, a source, a small-base threshold or a base n line to change.", result.Text());
        Assert.Equal(Enumerable.Range(1, 1), await NumbersAsync(h, water));
    }

    [Fact]
    public async Task Palette_scale_and_column_are_matched_exactly_in_lower_case_while_a_value_keeps_its_case_so_two_cases_are_two_values()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        await SeedHouseholdsAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water access");

        var shouted = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, palette = "Vivid", change = ColorsRequest });
        var padded = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, palette = " vivid", change = ColorsRequest });
        var column = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, valueColors = new[] { Kept("Sex", "Female", 5) }, change = ColorsRequest });
        var ramp = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = water, title = "Map", chartType = "map", change = ColorsRequest,
            config = new { indicator = "households", measure = "value", groupBy = "municipality", geometryColumn = "geometry", display = new { ramp = "Green" } },
        });

        Assert.All(new[] { shouted, padded, column, ramp }, r => Assert.Equal("mcp.validation", r.Code()));
        Assert.Contains("palette: Choose one of the palettes: tamiza, institutional, earth, vivid.", shouted.Text());
        Assert.Contains("palette: Choose one of the palettes: tamiza, institutional, earth, vivid.", padded.Text());
        Assert.Contains("valueColors[0].column: The column must be a published column's name", column.Text());
        Assert.Contains("config.display.ramp: The scale must be one of blue, teal, green, orange, red, purple, gray.", ramp.Text());
        Assert.Equal("tamiza []", await ColorsOfAsync(h, water));

        // A value is matched as exact text: Female and female are two values, and each keeps a color.
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = water, valueColors = new[] { Kept("sex", "Female", 5), Kept("sex", "female", 2) }, change = ColorsRequest })).Code());
        Assert.Equal("tamiza [sex=Female:5, sex=female:2]", await ColorsOfAsync(h, water));
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
    public async Task A_new_chart_type_alone_keeps_the_config_so_a_widget_with_a_color_or_a_scale_needs_a_config_without_what_the_new_type_does_not_take()
    {
        await using var h = await CreateAsync();
        await SeedHouseholdsAsync(h);
        var water = await AddDashboardInTamizaAsync(h, "Water access");
        object Config(object? display = null, string? geometryColumn = null) =>
            new { indicator = "households", measure = "value", groupBy = "municipality", geometryColumn, display };
        var bar = await AddWidgetAsync(h, water, "Households", "bar", Config(new { color = 3 }));
        var map = await AddWidgetAsync(h, water, "Map", "map", Config(new { ramp = "green" }, "geometry"));

        // The chart type alone is checked against the config it keeps, which is the color of a bar and the scale of a map.
        var barToPie = await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = bar, chartType = "pie", change = ColorsRequest });
        var mapToPie = await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = map, chartType = "pie", change = ColorsRequest });

        Assert.Equal("mcp.validation", barToPie.Code());
        Assert.Contains("config.display.color: A pie widget has no color of its own.", barToPie.Text());
        Assert.Equal("mcp.validation", mapToPie.Code());
        Assert.Contains("config.display.ramp", mapToPie.Text());

        // A type that takes the color or the scale keeps it, and a config without them makes the change to a type that does not.
        Assert.Null((await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = bar, chartType = "column", change = ColorsRequest })).Code());
        Assert.Null((await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = bar, chartType = "pie", config = Config(), change = ColorsRequest })).Code());
        Assert.Null((await h.CallAsync("analyst", "update_widget", new { dashboardId = water, widgetId = map, chartType = "pie", config = Config(), change = ColorsRequest })).Code());
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = water })).Json();
        var widgets = viewed.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);
        Assert.Equal("pie", widgets["Households"].GetProperty("chartType").GetString());
        Assert.False(widgets["Households"].GetProperty("config").TryGetProperty("display", out _));
        Assert.False(widgets["Map"].GetProperty("config").TryGetProperty("display", out _));
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

    // Column labels (mcp-server: Indicator column labels through MCP, Widget column labels through MCP)

    /// <summary>Gives the current version of <c>piped_water_rate</c> these column labels, as a save of the script would.</summary>
    private static Task LabelAsync(McpProjectHarness h, string columnsJson = """{"numerator": {"label": "Hogares que asistieron"}, "denominator": {"label": "Hogares"}, "municipality": {"label": "Municipio"}}""") =>
        TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_versions SET columns = @columns::jsonb WHERE project_id = @p AND name = 'piped_water_rate'",
            ("columns", columnsJson), ("p", h.ProjectId));

    private static Dictionary<string, string?> LabelsOf(JsonElement widget) =>
        widget.GetProperty("labels").EnumerateObject().ToDictionary(l => l.Name, l => l.Value.GetString());

    [Fact]
    public async Task The_columns_of_a_table_carry_the_label_their_indicator_gives_and_the_indicators_list_returns_the_labels()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        await LabelAsync(h);

        using var columns = (await h.CallAsync("viewer", "get_indicator_columns", new { name = "piped_water_rate" })).Json();
        using var indicators = (await h.CallAsync("viewer", "list_indicators")).Json();

        var byName = columns.RootElement.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!);
        Assert.Equal("Hogares que asistieron", byName["numerator"].GetProperty("label").GetString());
        Assert.Equal("Municipio", byName["municipality"].GetProperty("label").GetString());
        Assert.False(byName["value"].TryGetProperty("label", out _));
        var listed = Assert.Single(indicators.RootElement.GetProperty("indicators").EnumerateArray()).GetProperty("columns");
        Assert.Equal("Hogares", listed.GetProperty("denominator").GetProperty("label").GetString());
    }

    [Fact]
    public async Task A_widget_created_with_its_own_labels_returns_them_in_the_dashboard_and_names_its_columns_in_its_data()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        await LabelAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var id = await AddWidgetAsync(h, dashboard, "Asistencia", "column", new
        {
            indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality",
            display = new { labels = new { denominator = "Hogares de la base E1" } },
        });

        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = id })).Json();
        var widget = viewed.RootElement.GetProperty("widgets")[0];
        Assert.Equal("Hogares de la base E1", widget.GetProperty("config").GetProperty("display").GetProperty("labels").GetProperty("denominator").GetString());
        Assert.Equal(
            new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron", ["denominator"] = "Hogares de la base E1", ["municipality"] = "Municipio" },
            LabelsOf(data.RootElement.GetProperty("widgets")[0]));
    }

    [Fact]
    public async Task A_widget_whose_columns_have_no_label_has_no_labels_member_and_a_dashboard_without_labels_returns_none()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "By municipality", "bar", BarConfig());
        await AddWidgetAsync(h, dashboard, "Rate", "stat", new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" } });

        var without = await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard });
        await LabelAsync(h, """{"estrato": {"label": "Estrato"}}""");
        var other = await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard });

        Assert.DoesNotContain("\"labels\"", without.Text());
        Assert.DoesNotContain("\"labels\"", other.Text()); // a label for a column no widget names changes nothing
    }

    [Fact]
    public async Task A_stat_tile_names_only_its_ratio_and_a_map_names_its_tooltip_columns_without_a_label()
    {
        await using var h = await CreateAsync();
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "piped_water_rate",
            "municipality text, value numeric, numerator numeric, denominator numeric, geometry text, registros bigint, meta bigint",
            "('Santa Ana', 0.9, 9, 10, 'POINT(1 1)', 4, 8)");
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, unit, format, columns, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'piped_water_rate', 1, 'x', 'Piped water rate', '%', 'percent', @columns::jsonb, @a, now())",
            ("p", h.ProjectId), ("a", h.Users["analyst"]),
            ("columns", """{"numerator": {"label": "Hogares que asistieron"}, "municipality": {"label": "Municipio"}, "registros": {"label": "Registros recibidos"}, "meta": {"label": "Meta indicador"}}"""));
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Rate", "stat", new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality" });
        await AddWidgetAsync(h, dashboard, "Map", "map", new
        {
            indicator = "piped_water_rate", measure = "value", groupBy = "municipality", geometryColumn = "geometry",
            tooltip = new object[] { new { column = "registros" }, new { column = "meta", label = "Meta" } },
        });

        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var widgets = data.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);
        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(widgets["Rate"]));
        Assert.Equal(new Dictionary<string, string?> { ["municipality"] = "Municipio", ["registros"] = "Registros recibidos" }, LabelsOf(widgets["Map"]));
    }

    [Fact]
    public async Task A_new_config_without_labels_replaces_the_widgets_own_labels_with_the_indicators()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        await LabelAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var id = await AddWidgetAsync(h, dashboard, "Asistencia", "column", new
        {
            indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality",
            display = new { labels = new { denominator = "Hogares de la base E1" } },
        });

        var update = await h.CallAsync("analyst", "update_widget", new { dashboardId = dashboard, widgetId = id, config = BarConfig(), change = Request });

        Assert.Null(update.Code());
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        Assert.False(viewed.RootElement.GetProperty("widgets")[0].GetProperty("config").TryGetProperty("display", out _));
        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = id })).Json();
        Assert.Equal("Hogares", LabelsOf(data.RootElement.GetProperty("widgets")[0])["denominator"]);
    }

    [Fact]
    public async Task Labels_for_11_columns_are_refused_with_the_key_of_the_labels_and_nothing_is_saved()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var labels = Enumerable.Range(1, 11).ToDictionary(i => $"columna_{i}", i => $"Columna {i}");

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Many", chartType = "bar", change = Request,
            config = new { indicator = "piped_water_rate", groupBy = "municipality", display = new { labels } },
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("config.display.labels", result.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task A_query_preview_with_labels_returns_the_names_it_would_show()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        await LabelAsync(h, """{"numerator": {"label": "Hogares que asistieron"}}""");

        using var body = (await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "bar",
            config = new
            {
                indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality",
                display = new { labels = new { municipality = "Municipio", denominator = "Hogares" } },
            },
        })).Json();

        Assert.Equal(
            new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron", ["denominator"] = "Hogares", ["municipality"] = "Municipio" },
            LabelsOf(body.RootElement));
        using var plain = (await h.CallAsync("viewer", "query_indicator", new { chartType = "bar", config = BarConfig("municipality") })).Json();
        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(plain.RootElement));
    }

    [Fact]
    public async Task A_query_preview_of_an_indicator_without_labels_has_no_labels_member()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);

        using var body = (await h.CallAsync("viewer", "query_indicator", new { chartType = "bar", config = BarConfig() })).Json();

        Assert.False(body.RootElement.TryGetProperty("labels", out _));
    }

    // Widget context (mcp-server "Widget context through MCP", "Dashboard source through MCP")

    private const string ContextRequest = "Add the texts of the water dashboard";

    private static object WithDisplay(object display, string indicator = "piped_water_rate") =>
        new { indicator, ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality", display };

    [Fact]
    public async Task Widget_context_through_MCP_is_saved_read_back_and_formats_the_data_with_the_widgets_unit()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        const string subtitle = "¿En qué lugares ha visto teatro en el último año? (E4)";

        var created = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "2.4", chartType = "bar", change = ContextRequest,
            config = WithDisplay(new { subtitle, caption = new { hidden = true }, unit = "de quienes asistieron", footnote = "No suman 100 %.", showDescription = true }),
        });

        Assert.Null(created.Code());
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var display = viewed.RootElement.GetProperty("widgets")[0].GetProperty("config").GetProperty("display");
        Assert.Equal(subtitle, display.GetProperty("subtitle").GetString());
        Assert.True(display.GetProperty("caption").GetProperty("hidden").GetBoolean());
        Assert.False(display.GetProperty("caption").TryGetProperty("text", out _));
        Assert.Equal("de quienes asistieron", display.GetProperty("unit").GetString());
        Assert.True(display.GetProperty("showDescription").GetBoolean());

        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();
        var widget = data.RootElement.GetProperty("widgets")[0];
        Assert.Equal(subtitle, widget.GetProperty("subtitle").GetString());
        Assert.Equal("No suman 100 %.", widget.GetProperty("footnote").GetString());
        // A hidden caption has no text of its own to return.
        Assert.False(widget.TryGetProperty("caption", out _));
        Assert.Equal(["30% de quienes asistieron", "90% de quienes asistieron"], widget.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("formatted").GetString()).Order());
    }

    [Fact]
    public async Task An_own_caption_text_and_the_texts_come_back_for_every_status_of_a_widget()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Shown", "bar", WithDisplay(new { subtitle = "S", footnote = "F", caption = new { text = "% de quienes asistieron" } }));
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.dashboard_widgets (id, project_id, dashboard_id, chart_type, title, config, layout, created_by, updated_by) VALUES (gen_random_uuid(), @p, @d, 'stat', 'Waiting', '{\"indicator\":\"not_yet\",\"measure\":\"value\",\"filters\":[],\"display\":{\"subtitle\":\"S2\",\"footnote\":\"F2\",\"caption\":{\"text\":\"C2\"}}}'::jsonb, '{\"x\":0,\"y\":9,\"w\":3,\"h\":2}'::jsonb, @a, @a)",
            ("p", h.ProjectId), ("d", dashboard), ("a", h.Users["analyst"]));

        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var widgets = data.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);
        Assert.Equal("shown", widgets["Shown"].GetProperty("status").GetString());
        Assert.Equal(["S", "F", "% de quienes asistieron"], new[] { "subtitle", "footnote", "caption" }.Select(n => widgets["Shown"].GetProperty(n).GetString()));
        Assert.Equal("notPublished", widgets["Waiting"].GetProperty("status").GetString());
        Assert.Equal(["S2", "F2", "C2"], new[] { "subtitle", "footnote", "caption" }.Select(n => widgets["Waiting"].GetProperty(n).GetString()));
    }

    [Fact]
    public async Task A_widget_unit_formats_the_cells_of_a_series_widget_and_the_figures_of_a_box_plot_and_a_preview_keeps_the_indicators_unit()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        await SeedSourcesAsync(h);
        await SeedHouseholdSizesAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "Stacked", "column", new
        {
            indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "stacked", display = new { unit = "viviendas" },
        });
        await AddWidgetAsync(h, dashboard, "Spread", "boxplot", new { indicator = "household_size", groupBy = "municipality", display = new { unit = "personas" } });

        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();
        var widgets = data.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);

        var cell = widgets["Stacked"].GetProperty("cells").EnumerateArray().First(c => c.GetProperty("group").GetString() == "Palmira");
        Assert.Equal("20.00 viviendas", cell.GetProperty("formatted").GetString());
        Assert.Equal("80.00 viviendas", widgets["Stacked"].GetProperty("totals").GetProperty("all").GetProperty("formatted").GetString());
        var cali = widgets["Spread"].GetProperty("boxes").EnumerateArray().Single(b => GroupOf(b) == "Cali");
        Assert.Equal("5.00 personas", cali.GetProperty("formatted").GetProperty("median").GetString());

        // query_indicator previews a query and keeps the indicator's unit, whatever display it is given.
        using var preview = (await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "bar", config = WithDisplay(new { unit = "de quienes asistieron" }),
        })).Json();
        Assert.Equal("%", preview.RootElement.GetProperty("unit").GetString());
        Assert.Equal(["30%", "90%"], preview.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("formatted").GetString()).Order());
    }

    [Fact]
    public async Task Widget_data_without_these_settings_has_no_subtitle_footnote_or_caption_at_all()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "By municipality", "bar", BarConfig());
        await AddWidgetAsync(h, dashboard, "Colored", "bar", WithDisplay(new { color = 2 }));
        await AddWidgetAsync(h, dashboard, "Waiting", "stat", new { indicator = "never_published", measure = "value" });

        var result = await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard });

        Assert.Null(result.Code());
        foreach (var member in new[] { "subtitle", "footnote", "caption" })
        {
            Assert.DoesNotContain($"\"{member}\"", result.Text());
        }

        Assert.Contains("\"formatted\":\"90%\"", result.Text());
    }

    [Fact]
    public async Task Caption_refused_through_MCP_has_the_error_on_the_caption_and_saves_nothing()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "By municipality", "bar", WithDisplay(new { subtitle = "Before" }));
        var versions = await AllVersionsAsync(h);

        var refused = await h.CallAsync("analyst", "update_widget", new
        {
            dashboardId = dashboard, widgetId = widget, change = ContextRequest, config = WithDisplay(new { subtitle = "After", caption = new { text = "x", hidden = true } }),
        });
        var longUnit = await h.CallAsync("analyst", "update_widget", new
        {
            dashboardId = dashboard, widgetId = widget, change = ContextRequest, config = WithDisplay(new { unit = new string('u', 33) }),
        });
        var lineBreak = await h.CallAsync("analyst", "update_widget", new
        {
            dashboardId = dashboard, widgetId = widget, change = ContextRequest, config = WithDisplay(new { footnote = "one\ntwo" }),
        });

        Assert.All(new[] { refused, longUnit, lineBreak }, r => Assert.Equal("mcp.validation", r.Code()));
        Assert.Contains("config.display.caption", refused.Text());
        Assert.Contains("config.display.unit", longUnit.Text());
        Assert.Contains("config.display.footnote", lineBreak.Text());
        Assert.Equal(versions, await AllVersionsAsync(h));
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        Assert.Equal("Before", viewed.RootElement.GetProperty("widgets")[0].GetProperty("config").GetProperty("display").GetProperty("subtitle").GetString());
    }

    [Fact]
    public async Task Config_replaced_through_MCP_has_the_texts_of_the_new_config_only()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "By municipality", "bar", WithDisplay(new { subtitle = "A subtitle", footnote = "A footnote", unit = "u" }));

        var result = await h.CallAsync("analyst", "update_widget", new { dashboardId = dashboard, widgetId = widget, change = ContextRequest, config = BarConfig() });

        Assert.Null(result.Code());
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        Assert.False(viewed.RootElement.GetProperty("widgets")[0].GetProperty("config").TryGetProperty("display", out _));
    }

    [Fact]
    public async Task A_chart_type_change_keeps_and_never_refuses_the_texts_of_a_widget()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "By municipality", "bar", WithDisplay(new { subtitle = "A subtitle", caption = new { hidden = true } }));

        var result = await h.CallAsync("analyst", "update_widget", new { dashboardId = dashboard, widgetId = widget, chartType = "pie", change = ContextRequest });

        Assert.Null(result.Code());
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        Assert.Equal("A subtitle", viewed.RootElement.GetProperty("widgets")[0].GetProperty("config").GetProperty("display").GetProperty("subtitle").GetString());
    }

    private const string TeatroSource = "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas";

    [Fact]
    public async Task Source_through_MCP_is_read_by_get_dashboard_and_is_a_version_of_its_own_kind()
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");

        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, source = TeatroSource, change = "Name the source" });

        Assert.Null(result.Code());
        using var body = result.Json();
        Assert.Equal(TeatroSource, body.RootElement.GetProperty("source").GetString());
        Assert.Equal([(teatro, 2)], DashboardVersionsOf(result));
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = teatro })).Json();
        Assert.Equal(TeatroSource, viewed.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            [
                """1 | analyst | app | - | [{"kind": "created"}]""",
                """2 | analyst | mcp | Name the source | [{"kind": "sourceChanged"}]""",
            ],
            await VersionsAsync(h, teatro));
        using var version = (await h.CallAsync("analyst", "get_dashboard_version", new { dashboardId = teatro, version = 2 })).Json();
        Assert.Equal(TeatroSource, version.RootElement.GetProperty("source").GetString());
        using var first = (await h.CallAsync("analyst", "get_dashboard_version", new { dashboardId = teatro, version = 1 })).Json();
        Assert.False(first.RootElement.TryGetProperty("source", out _));
        var page = await Rest(h, "viewer").GetFromJsonAsync<JsonElement>($"/api/v1/projects/{h.ProjectId}/dashboards/{teatro}");
        Assert.Equal(TeatroSource, page.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Rename_and_source_in_one_call_make_one_version_holding_both()
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");

        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, name = "Teatro 2026", source = TeatroSource, change = "Rename and name the source" });

        Assert.Equal([(teatro, 2)], DashboardVersionsOf(result));
        Assert.Equal(
            """2 | analyst | mcp | Rename and name the source | [{"kind": "renamed", "dashboard": "Teatro 2026"}, {"kind": "sourceChanged"}]""",
            (await VersionsAsync(h, teatro))[1]);
        Assert.Equal(TeatroSource, await TestData.ScalarAsync<string>(h.Db, "SELECT content->>'source' FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 2", ("d", teatro)));
    }

    [Fact]
    public async Task Source_removed_through_MCP_by_an_empty_text_and_the_same_source_makes_no_version()
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, source = TeatroSource, change = ContextRequest })).Code());
        var same = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, source = TeatroSource, change = ContextRequest });
        Assert.Null(same.Code());
        Assert.Equal(new[] { 1, 2 }, await NumbersAsync(h, teatro));

        var removed = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, source = "", change = "Remove the source" });

        Assert.Null(removed.Code());
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = teatro })).Json();
        Assert.False(viewed.RootElement.TryGetProperty("source", out _));
        Assert.Null(await TestData.ScalarAsync<string>(h.Db, "SELECT source FROM tamiza.dashboards WHERE id = @d", ("d", teatro)));
        Assert.Equal(new[] { 1, 2, 3 }, await NumbersAsync(h, teatro));
        // A call that leaves the source out keeps it.
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, source = TeatroSource, change = "Name it again" })).Code());
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, name = "Teatro 2", change = "Rename it" })).Code());
        Assert.Equal(TeatroSource, await TestData.ScalarAsync<string>(h.Db, "SELECT source FROM tamiza.dashboards WHERE id = @d", ("d", teatro)));
    }

    [Fact]
    public async Task Long_source_through_MCP_is_refused_on_the_source_and_nothing_is_saved()
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");

        var tooLong = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, source = new string('s', 301), change = ContextRequest });
        var lineBreak = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, source = "one\ntwo", change = ContextRequest });

        Assert.All(new[] { tooLong, lineBreak }, r => Assert.Equal("mcp.validation", r.Code()));
        Assert.Contains("source", tooLong.Text());
        Assert.Contains("source", lineBreak.Text());
        Assert.Null(await TestData.ScalarAsync<string>(h.Db, "SELECT source FROM tamiza.dashboards WHERE id = @d", ("d", teatro)));
        Assert.Equal(new[] { 1 }, await NumbersAsync(h, teatro));
    }

    [Fact]
    public async Task A_viewer_cannot_set_a_source()
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");

        var result = await h.CallAsync("viewer", "update_dashboard", new { dashboardId = teatro, source = TeatroSource, change = ContextRequest });

        Assert.NotNull(result.Code());
        Assert.Null(await TestData.ScalarAsync<string>(h.Db, "SELECT source FROM tamiza.dashboards WHERE id = @d", ("d", teatro)));
    }

    // The declared category order in the results of the tools (add-declared-category-order, design D12)

    /// <summary>The indicator <c>teatro_conteo</c>, published and saved as a script whose version says what it declares for its columns.</summary>
    private static async Task SeedAnswersAsync(McpProjectHarness h, string? columns)
    {
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "teatro_conteo", "comuna text, respuesta text, conteo numeric", """
            ('COMUNA 1', '4. No sabe', 5), ('COMUNA 1', '2. Tal vez', 10), ('COMUNA 1', '1. Sí', 30), ('COMUNA 2', '1. Sí', 20), ('COMUNA 2', '3. No', 25), ('Sin dato', '1. Sí', 3)
            """);
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, columns, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'teatro_conteo', 1, 'x', 'Teatro conteo', 'count', @c::jsonb, @a, now())",
            ("p", h.ProjectId), ("c", (object?)columns ?? DBNull.Value), ("a", h.Users["analyst"]));
    }

    private const string DeclaredAnswers = """{"respuesta": {"order": ["1. Sí", "2. Tal vez", "3. No"], "last": ["4. No sabe"], "stripPrefix": true}, "comuna": {"last": ["Sin dato"]}}""";

    private static object AnswersConfig(object? display = null) => new
    {
        indicator = "teatro_conteo", measure = "conteo", groupBy = "comuna", seriesBy = "respuesta", layout = "percent", display,
    };

    [Fact]
    public async Task Full_values_in_results()
    {
        await using var h = await CreateAsync();
        await SeedAnswersAsync(h, DeclaredAnswers);
        var dashboard = await AddDashboardAsync(h, "Teatro");
        var widget = await AddWidgetAsync(h, dashboard, "2.8. Iría al teatro, por comuna", "bar", AnswersConfig());

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = widget })).Json();

        var root = body.RootElement.GetProperty("widgets")[0];
        Assert.Equal(["1. Sí", "2. Tal vez", "3. No", "4. No sabe"], root.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("value").GetString()));
        Assert.Equal(["COMUNA 1", "COMUNA 2", "Sin dato"], root.GetProperty("totals").GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("group").GetString()));
        Assert.Contains(root.GetProperty("cells").EnumerateArray(), c => c.GetProperty("series").GetString() == "1. Sí" && c.GetProperty("group").GetString() == "COMUNA 1");
    }

    [Fact]
    public async Task A_preview_with_a_widgets_own_order_lists_its_series_in_it()
    {
        await using var h = await CreateAsync();
        await SeedAnswersAsync(h, DeclaredAnswers);

        using var body = (await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "column",
            config = AnswersConfig(new { order = new { series = new[] { "4. No sabe", "3. No" } } }),
        })).Json();

        Assert.Equal(["4. No sabe", "3. No", "1. Sí", "2. Tal vez"], body.RootElement.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("value").GetString()));
    }

    [Fact]
    public async Task A_group_kept_last_comes_last_in_the_plain_rows()
    {
        await using var h = await CreateAsync();
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "teatro_hoy", "comuna text, value numeric", "('COMUNA 1', 5), ('Sin dato', 2), ('ZONA RURAL', 3)");
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, columns, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'teatro_hoy', 1, 'x', 'Teatro hoy', 'count', @c::jsonb, @a, now())",
            ("p", h.ProjectId), ("c", """{"comuna": {"last": ["Sin dato"]}}"""), ("a", h.Users["analyst"]));

        using var body = (await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "column", config = new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna" },
        })).Json();

        Assert.Equal(["COMUNA 1", "ZONA RURAL", "Sin dato"], body.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()));
    }

    [Fact]
    public async Task Results_of_a_widget_that_declares_nothing()
    {
        await using var h = await CreateAsync();
        await SeedAnswersAsync(h, """{"respuesta": {"label": "Respuesta"}}""");
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "demanda", "precio text, value numeric", "('$10.000', 30), ('$0', 40), ('$50.000', 5), ('$20.000', 20)");

        using var series = (await h.CallAsync("viewer", "query_indicator", new { chartType = "bar", config = AnswersConfig() })).Json();
        using var line = (await h.CallAsync("viewer", "query_indicator", new { chartType = "line", config = new { indicator = "demanda", measure = "value", groupBy = "precio" } })).Json();

        Assert.Equal(["1. Sí", "2. Tal vez", "3. No", "4. No sabe"], series.RootElement.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("value").GetString()).Order(StringComparer.Ordinal));
        // The rows of a line keep the order of the response: the tool takes no step when nothing is declared.
        var rows = line.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()).ToArray();
        using var again = (await h.CallAsync("viewer", "query_indicator", new { chartType = "line", config = new { indicator = "demanda", measure = "value", groupBy = "precio" } })).Json();
        Assert.Equal(rows, again.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()));
    }

    [Fact]
    public async Task The_boxes_follow_the_order_the_widget_declares()
    {
        await using var h = await CreateAsync();
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "tarifa", "vehiculo text, value numeric",
            "('Carro', 5), ('Carro', 6), ('Moto', 1), ('Moto', 2), ('Todos', 3), ('Todos', 4)");

        using var body = (await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "boxplot",
            config = new { indicator = "tarifa", measure = "value", groupBy = "vehiculo", display = new { order = new { group = new[] { "Todos", "Carro", "Moto" } } } },
        })).Json();

        Assert.Equal(["Todos", "Carro", "Moto"], body.RootElement.GetProperty("boxes").EnumerateArray().Select(b => b.GetProperty("group").GetString()));
    }

    // Writing the own order of a widget through MCP (mcp-server: Declared category order through MCP)

    [Fact]
    public async Task Widget_order_through_MCP()
    {
        await using var h = await CreateAsync();
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "barreras", "tipo text, razon text, value numeric", "('A', 'Acceso', 3), ('A', 'Interés', 2), ('A', 'Otra', 1), ('B', 'Acceso', 4)");
        var dashboard = await AddDashboardAsync(h, "Barreras");

        var widget = await AddWidgetAsync(h, dashboard, "Barreras por tipo", "column", new
        {
            indicator = "barreras", measure = "value", groupBy = "tipo", seriesBy = "razon", layout = "stacked",
            display = new { order = new { series = new[] { "Acceso", "Interés", "Otra" } } },
        });

        using var read = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var widgets = read.RootElement.GetProperty("widgets").EnumerateArray().ToList();
        var order = Assert.Single(widgets).GetProperty("config").GetProperty("display").GetProperty("order");
        Assert.Equal(["Acceso", "Interés", "Otra"], order.GetProperty("series").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(widget.ToString(), widgets[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Order_of_groups_with_a_sort_refused()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Ordenado", chartType = "bar", change = Request,
            config = new
            {
                indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality",
                sort = new { by = "value", direction = "desc" }, display = new { order = new { group = new[] { "San Luis" } } },
            },
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("config.display.order.group", result.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task An_order_of_series_on_a_pyramid_is_refused_through_MCP_saying_what_sets_it()
    {
        await using var h = await CreateAsync();
        await SeedSourcesAsync(h);
        var dashboard = await AddDashboardAsync(h, "Fuentes");

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Pirámide", chartType = "pyramid", change = Request,
            config = new
            {
                indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source",
                pyramid = new { left = "Piped", right = "Well" }, display = new { order = new { series = new[] { "Well" } } },
            },
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("config.display.order.series", result.Text());
        Assert.Contains("A population pyramid's sides are its left and right values.", result.Text());
    }

    // Base figures through MCP (add-small-sample-warnings, design D14)

    private const string TeatroColumns = "variable text, orden text, categoria text, comuna text, value numeric, numerator numeric, denominator numeric";

    /// <summary>Every comuna with its households: 476 in all, 16 of them in COMUNA 7.</summary>
    private static readonly (string Name, int Households)[] Comunas = [("COMUNA 1", 300), ("COMUNA 2", 140), ("COMUNA 7", 16), ("SIN NOMBRE", 20)];

    private const string DeclaredAnswersAndNoData =
        """{"variable": {"categories": "multiple"}, "orden": {"categories": "multiple"}, "categoria": {"categories": "multiple"}, "comuna": {"missing": ["Sin dato"]}}""";

    /// <summary>
    /// The indicator <c>teatro_hoy</c>, a long % table with a row per comuna and answer of one question, every household of a comuna in the
    /// denominator of both answers, saved as a script whose version declares <paramref name="declared"/>. Names and values are invented.
    /// </summary>
    private static async Task SeedTeatroAsync(McpProjectHarness h, string? declared, bool sinDato = false)
    {
        var rows = new List<string>();
        foreach (var (name, households) in Comunas)
        {
            rows.Add($"('Asistio', '1', 'Si', '{name}', 0, {households / 2}, {households})");
            rows.Add($"('Asistio', '2', 'No', '{name}', 0, {households / 2}, {households})");
        }

        if (sinDato)
        {
            // Four households with no block, all of whom attended.
            rows.Add("('Asistio', '1', 'Si', 'Sin dato', 0, 4, 4)");
        }

        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "teatro_hoy", TeatroColumns, string.Join(", ", rows));
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, columns, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'teatro_hoy', 1, 'x', 'Teatro hoy', 'percent', @c::jsonb, @a, now())",
            ("p", h.ProjectId), ("c", (object?)declared ?? DBNull.Value), ("a", h.Users["analyst"]));
    }

    private static object Attended(string groupBy = "comuna", object? display = null, bool? missingFooter = null, bool filterAnswer = true) => new
    {
        indicator = "teatro_hoy",
        ratio = new { numerator = "numerator", denominator = "denominator" },
        groupBy,
        filters = filterAnswer
            ? new object[] { new { column = "variable", op = "equals", value = "Asistio" }, new { column = "categoria", op = "equals", value = "Si" } }
            : new object[] { new { column = "variable", op = "equals", value = "Asistio" } },
        display,
        missingFooter,
    };

    private static JsonElement WidgetOf(JsonDocument body) => body.RootElement.GetProperty("widgets")[0];

    private static string[] Notes(JsonElement widget) =>
        widget.TryGetProperty("notes", out var notes) ? notes.EnumerateArray().Select(n => n.GetString()!).ToArray() : [];

    [Fact]
    public async Task Base_figures_of_a_widget()
    {
        await using var h = await CreateAsync();
        await SeedTeatroAsync(h, DeclaredAnswersAndNoData);
        var dashboard = await AddDashboardAsync(h, "Teatro");
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = dashboard, minN = 30, change = Request })).Code());
        var widget = await AddWidgetAsync(h, dashboard, "2.1. Asistió, por comuna", "column", Attended());

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = widget })).Json();

        var shown = WidgetOf(body);
        Assert.Equal("shown", shown.GetProperty("status").GetString());
        string Figures(JsonElement row) =>
            $"{row.GetProperty("group").GetString()}|{row.GetProperty("numerator").GetDouble()}|{row.GetProperty("denominator").GetDouble()}|{row.GetProperty("n").GetDouble()}"
            + (row.TryGetProperty("smallBase", out var small) && small.GetBoolean() ? "|small" : "");
        var rows = shown.GetProperty("rows").EnumerateArray().Select(Figures).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["COMUNA 1|150|300|300", "COMUNA 2|70|140|140", "COMUNA 7|8|16|16|small", "SIN NOMBRE|10|20|20|small"], rows);
        // The indicator declares its answers, so the widget has an n, counted once across them: 476 households, not 952.
        Assert.Equal(476, shown.GetProperty("n").GetDouble());
        Assert.Equal(30, shown.GetProperty("minN").GetInt32());
        Assert.False(shown.TryGetProperty("showN", out _));
        Assert.Equal(["2 values have a base below 30, marked smallBase."], Notes(shown));
        // The value is as it was.
        Assert.Equal(0.5, shown.GetProperty("rows")[0].GetProperty("value").GetDouble(), 6);
    }

    [Fact]
    public async Task A_widget_own_threshold_and_a_cell_of_a_series_widget_carry_their_small_base()
    {
        await using var h = await CreateAsync();
        await SeedTeatroAsync(h, DeclaredAnswersAndNoData);
        var dashboard = await AddDashboardAsync(h, "Teatro");
        // The dashboard sets none: the widget's own display.minN applies, and display.showN true is a setting in force.
        var widget = await AddWidgetAsync(h, dashboard, "Por comuna y respuesta", "heatmap", new
        {
            indicator = "teatro_hoy", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "comuna", seriesBy = "categoria",
            filters = new object[] { new { column = "variable", op = "equals", value = "Asistio" } },
            display = new { minN = 25, showN = true },
        });

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = widget })).Json();

        var shown = WidgetOf(body);
        var cells = shown.GetProperty("cells").EnumerateArray().Where(c => c.GetProperty("series").GetString() == "Si").ToDictionary(c => c.GetProperty("group").GetString()!);
        Assert.Equal(16, cells["COMUNA 7"].GetProperty("n").GetDouble());
        Assert.True(cells["COMUNA 7"].GetProperty("smallBase").GetBoolean());
        Assert.True(cells["SIN NOMBRE"].GetProperty("smallBase").GetBoolean());
        Assert.False(cells["COMUNA 2"].TryGetProperty("smallBase", out _));
        // Each total has its three figures: COMUNA 7 holds both answers, each over the same 16 households.
        var group = shown.GetProperty("totals").GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("group").GetString() == "COMUNA 7");
        Assert.Equal((32d, 16d), (group.GetProperty("denominator").GetDouble(), group.GetProperty("n").GetDouble()));
        Assert.Equal(476, shown.GetProperty("totals").GetProperty("all").GetProperty("n").GetDouble());
        Assert.Equal(25, shown.GetProperty("minN").GetInt32());
        Assert.True(shown.GetProperty("showN").GetBoolean());
        Assert.Contains("4 values have a base below 25, marked smallBase.", Notes(shown));
    }

    [Fact]
    public async Task Measure_without_a_base_through_MCP()
    {
        await using var h = await CreateAsync();
        await SeedAnswersAsync(h, null);

        using var body = (await h.CallAsync("viewer", "query_indicator", new
        {
            chartType = "column", config = new { indicator = "teatro_conteo", measure = "conteo", groupBy = "comuna" },
        })).Json();

        Assert.NotEmpty(body.RootElement.GetProperty("rows").EnumerateArray());
        foreach (var row in body.RootElement.GetProperty("rows").EnumerateArray())
        {
            Assert.Equal(["group", "value", "formatted"], row.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.False(row.TryGetProperty("numerator", out _));
            Assert.False(row.TryGetProperty("denominator", out _));
            Assert.False(row.TryGetProperty("n", out _));
        }

        Assert.False(body.RootElement.TryGetProperty("n", out _));
        Assert.False(body.RootElement.TryGetProperty("noData", out _));
    }

    [Fact]
    public async Task The_widget_n_only_under_the_rule_of_the_design()
    {
        await using var h = await CreateAsync();
        // Only no-data values declared: no base column and no answer column, so the marks keep their n and the widget has none.
        await SeedTeatroAsync(h, """{"comuna": {"missing": ["Sin dato"]}}""");
        var dashboard = await AddDashboardAsync(h, "Teatro");
        var widget = await AddWidgetAsync(h, dashboard, "2.1. Asistió, por comuna", "column", Attended());

        using var undeclared = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = widget })).Json();
        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "column", config = Attended() })).Json();

        Assert.False(WidgetOf(undeclared).TryGetProperty("n", out _));
        Assert.False(preview.RootElement.TryGetProperty("n", out _));
        Assert.Equal([16d, 20d, 140d, 300d], WidgetOf(undeclared).GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("n").GetDouble()).Order().ToArray());

        // Declaring the answers gives the widget its n, in a preview too.
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_versions SET columns = @c::jsonb WHERE name = 'teatro_hoy'", ("c", DeclaredAnswersAndNoData));
        using var declared = (await h.CallAsync("viewer", "query_indicator", new { chartType = "column", config = Attended() })).Json();
        Assert.Equal(476, declared.RootElement.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task noData_for_a_widget_with_the_option()
    {
        await using var h = await CreateAsync();
        await SeedTeatroAsync(h, DeclaredAnswersAndNoData, sinDato: true);
        var dashboard = await AddDashboardAsync(h, "Teatro");
        var widget = await AddWidgetAsync(h, dashboard, "2.1. Asistió, por comuna", "column", Attended(missingFooter: true));
        var plain = await AddWidgetAsync(h, dashboard, "2.1b. Asistió, por comuna", "column", Attended());

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();
        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "column", config = Attended(missingFooter: true) })).Json();

        var widgets = body.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("widgetId").GetGuid());
        void Check(JsonElement result)
        {
            Assert.DoesNotContain("Sin dato", result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()));
            var noData = result.GetProperty("noData");
            Assert.Equal(["Sin dato"], noData.GetProperty("values").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(1, noData.GetProperty("value").GetDouble());
            Assert.Contains("100", noData.GetProperty("formatted").GetString());
            Assert.Equal(4, noData.GetProperty("n").GetDouble());
            Assert.False(noData.TryGetProperty("share", out _));
        }

        Check(widgets[widget]);
        Check(preview.RootElement);
        // Without the option the value keeps its column and nothing is reported.
        Assert.Contains("Sin dato", widgets[plain].GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()));
        Assert.False(widgets[plain].TryGetProperty("noData", out _));
    }

    [Fact]
    public async Task No_data_option_on_a_stat_tile_through_MCP()
    {
        await using var h = await CreateAsync();
        await SeedTeatroAsync(h, DeclaredAnswersAndNoData);
        var dashboard = await AddDashboardAsync(h, "Teatro");

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Asistió", chartType = "stat", change = Request,
            config = new { indicator = "teatro_hoy", ratio = new { numerator = "numerator", denominator = "denominator" }, missingFooter = true },
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("missingFooter", result.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task A_chart_type_change_alone_is_refused_on_missingFooter()
    {
        await using var h = await CreateAsync();
        await SeedTeatroAsync(h, DeclaredAnswersAndNoData);
        var dashboard = await AddDashboardAsync(h, "Teatro");
        var widget = await AddWidgetAsync(h, dashboard, "Asistió", "column", Attended(missingFooter: true));

        var result = await h.CallAsync("analyst", "update_widget", new { dashboardId = dashboard, widgetId = widget, chartType = "stat", change = Request });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("missingFooter", result.Text());
    }

    [Fact]
    public async Task No_overall_line_and_its_note_over_a_declared_answer_column()
    {
        await using var h = await CreateAsync();
        await SeedTeatroAsync(h, DeclaredAnswersAndNoData);
        var dashboard = await AddDashboardAsync(h, "Teatro");
        var lines = new { referenceLines = new[] { new { kind = "overall" } } };
        var answers = await AddWidgetAsync(h, dashboard, "Por respuesta", "bar", Attended("categoria", lines, filterAnswer: false));
        var comunas = await AddWidgetAsync(h, dashboard, "Por comuna", "bar", Attended("comuna", lines));

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();
        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "bar", config = Attended("categoria", lines, filterAnswer: false) })).Json();

        const string note = "No overall line: the bars are the answers of one question.";
        var widgets = body.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("widgetId").GetGuid());
        // Over the answers of one question the line is only the mean of the bars: not drawn, and the note says why.
        Assert.False(widgets[answers].TryGetProperty("referenceLines", out _));
        Assert.Contains(note, Notes(widgets[answers]));
        Assert.False(preview.RootElement.TryGetProperty("referenceLines", out _));
        Assert.Contains(note, preview.RootElement.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        // Over comunas it is drawn as before, with no note.
        Assert.Equal("overall", widgets[comunas].GetProperty("referenceLines")[0].GetProperty("kind").GetString());
        Assert.DoesNotContain(note, Notes(widgets[comunas]));
    }

    [Fact]
    public async Task A_long_result_is_cut_in_its_rows_with_the_existing_hint()
    {
        await using var h = await CreateAsync(new Dictionary<string, string?> { ["Mcp:MaxResultCharacters"] = "10000" });
        var rows = string.Join(", ", Enumerable.Range(1, 400).Select(i => $"('Municipality number {i:000}', {i}, {i}, 1000)"));
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "piped_water_rate", Columns, rows);

        var result = await h.CallAsync("viewer", "query_indicator", new { chartType = "column", config = BarConfig() });

        Assert.True(result.Text().Length <= 10_000, $"{result.Text().Length} characters");
        using var body = result.Json();
        var note = body.RootElement.GetProperty("truncated");
        Assert.True(note.GetProperty("omitted").GetInt32() > 0);
        Assert.Contains("filter", note.GetProperty("hint").GetString());
        Assert.True(body.RootElement.GetProperty("rows").GetArrayLength() < 400);
    }

    // The base settings of a dashboard through MCP (mcp-server: Base settings through MCP)

    [Fact]
    public async Task Threshold_through_MCP()
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");
        // A dashboard without base settings answers with the seven members it always had.
        using var before = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = teatro })).Json();
        Assert.Equal(["id", "name", "filters", "palette", "valueColors", "widgets", "version"], before.RootElement.EnumerateObject().Select(p => p.Name).ToArray());

        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, minN = 30, change = "Dim the small bases" });

        Assert.Null(result.Code());
        Assert.Equal([(teatro, 2)], DashboardVersionsOf(result));
        using var body = result.Json();
        Assert.Equal(30, body.RootElement.GetProperty("minN").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("showN", out _));
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = teatro })).Json();
        Assert.Equal(30, viewed.RootElement.GetProperty("minN").GetInt32());
        Assert.False(viewed.RootElement.TryGetProperty("showN", out _));
        Assert.Equal(["1 | analyst | app | - | [{\"kind\": \"created\"}]", "2 | analyst | mcp | Dim the small bases | [{\"kind\": \"basesChanged\"}]"], await VersionsAsync(h, teatro));
        Assert.Equal(30, await TestData.ScalarAsync<int>(h.Db, "SELECT min_n FROM tamiza.dashboards WHERE id = @d", ("d", teatro)));
        Assert.Equal(30, await TestData.ScalarAsync<int>(h.Db, "SELECT (content->>'minN')::int FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 2", ("d", teatro)));
        var page = await Rest(h, "viewer").GetFromJsonAsync<JsonElement>($"/api/v1/projects/{h.ProjectId}/dashboards/{teatro}");
        Assert.Equal(30, page.GetProperty("minN").GetInt32());
    }

    [Fact]
    public async Task The_line_and_the_threshold_are_kept_apart_turned_off_and_one_version_of_a_request()
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");

        // A rename with both settings is one version holding all three changes.
        var first = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, name = "Teatro 2", minN = 30, showN = true, change = "Set the bases" });
        Assert.Equal([(teatro, 2)], DashboardVersionsOf(first));
        Assert.Equal(
            """2 | analyst | mcp | Set the bases | [{"kind": "renamed", "dashboard": "Teatro 2"}, {"kind": "basesChanged"}]""",
            (await VersionsAsync(h, teatro))[1]);
        using var viewed = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = teatro })).Json();
        Assert.Equal((30, true), (viewed.RootElement.GetProperty("minN").GetInt32(), viewed.RootElement.GetProperty("showN").GetBoolean()));

        // The same settings make no version, and a call that names one keeps the other.
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, minN = 30, showN = true, change = "Again" })).Code());
        Assert.Equal(new[] { 1, 2 }, await NumbersAsync(h, teatro));
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, showN = false, change = "Hide the line" })).Code());
        Assert.Equal((30, false), (await TestData.ScalarAsync<int>(h.Db, "SELECT min_n FROM tamiza.dashboards WHERE id = @d", ("d", teatro)), await TestData.ScalarAsync<bool>(h.Db, "SELECT show_n FROM tamiza.dashboards WHERE id = @d", ("d", teatro))));

        // 0 turns the threshold off.
        var off = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, minN = 0, change = "Dim nothing" });
        Assert.Null(off.Code());
        using var body = off.Json();
        Assert.False(body.RootElement.TryGetProperty("minN", out _));
        Assert.Null(await TestData.ScalarAsync<int?>(h.Db, "SELECT min_n FROM tamiza.dashboards WHERE id = @d", ("d", teatro)));
        Assert.Equal(new[] { 1, 2, 3, 4 }, await NumbersAsync(h, teatro));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public async Task A_threshold_out_of_range_through_MCP_is_refused_on_minN_and_nothing_is_saved(int minN)
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");

        var result = await h.CallAsync("analyst", "update_dashboard", new { dashboardId = teatro, minN, change = Request });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("minN", result.Text());
        Assert.Null(await TestData.ScalarAsync<int?>(h.Db, "SELECT min_n FROM tamiza.dashboards WHERE id = @d", ("d", teatro)));
        Assert.Equal(new[] { 1 }, await NumbersAsync(h, teatro));
    }

    [Fact]
    public async Task A_viewer_cannot_set_the_base_sizes()
    {
        await using var h = await CreateAsync();
        var teatro = await AddDashboardInTamizaAsync(h, "Teatro");

        var result = await h.CallAsync("viewer", "update_dashboard", new { dashboardId = teatro, minN = 30, showN = true, change = Request });

        Assert.NotNull(result.Code());
        Assert.Null(await TestData.ScalarAsync<int?>(h.Db, "SELECT min_n FROM tamiza.dashboards WHERE id = @d", ("d", teatro)));
    }

    [Fact]
    public async Task A_widget_without_a_threshold_follows_the_dashboards_and_the_line_shows_when_it_is_on()
    {
        await using var h = await CreateAsync();
        await SeedTeatroAsync(h, DeclaredAnswersAndNoData);
        var dashboard = await AddDashboardAsync(h, "Teatro");
        await AddWidgetAsync(h, dashboard, "Sigue", "column", Attended());
        await AddWidgetAsync(h, dashboard, "Propio", "column", Attended(display: new { minN = 0, showN = false }));
        Assert.Null((await h.CallAsync("analyst", "update_dashboard", new { dashboardId = dashboard, minN = 30, showN = true, change = Request })).Code());

        using var body = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        var widgets = body.RootElement.GetProperty("widgets").EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);
        Assert.Equal((30, true), (widgets["Sigue"].GetProperty("minN").GetInt32(), widgets["Sigue"].GetProperty("showN").GetBoolean()));
        Assert.Contains(widgets["Sigue"].GetProperty("rows").EnumerateArray(), r => r.TryGetProperty("smallBase", out _));
        // The widget's own threshold of 0 and hidden line win over the dashboard's.
        Assert.False(widgets["Propio"].TryGetProperty("minN", out _));
        Assert.False(widgets["Propio"].GetProperty("showN").GetBoolean());
        Assert.DoesNotContain(widgets["Propio"].GetProperty("rows").EnumerateArray(), r => r.TryGetProperty("smallBase", out _));
    }

    [Fact]
    public async Task Widget_base_settings_are_saved_and_read_back_by_get_dashboard()
    {
        await using var h = await CreateAsync();
        await SeedTeatroAsync(h, DeclaredAnswersAndNoData);
        var dashboard = await AddDashboardAsync(h, "Teatro");

        await AddWidgetAsync(h, dashboard, "Asistió", "column", Attended(display: new { minN = 50, showN = true }, missingFooter: true));

        using var read = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var config = read.RootElement.GetProperty("widgets")[0].GetProperty("config");
        Assert.Equal((50, true, true), (config.GetProperty("display").GetProperty("minN").GetInt32(), config.GetProperty("display").GetProperty("showN").GetBoolean(), config.GetProperty("missingFooter").GetBoolean()));
        var refused = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Mal", chartType = "column", change = Request, config = Attended(display: new { minN = 10001 }),
        });
        Assert.Equal("mcp.validation", refused.Code());
        Assert.Contains("config.display.minN", refused.Text());
    }

    // Stat tiles and ratios over declared answers through MCP (improve-stat-cards, design D2, D3, D4 and D8). A table shaped like
    // teatro_precio_hogar with 37 households that paid (18 paid 10,000, one 40,000 and 18 90,000: the median is 40,000), and a table shaped like
    // cruces_intencion; names and values are invented.

    private static readonly object[] PaidFilter = [new { column = "variable", op = "equals", value = "Pago" }];

    private static async Task SeedPricesAsync(McpProjectHarness h)
    {
        var rows = Enumerable.Range(0, 18).Select(_ => "('COMUNA 1', 'Pago', 10000)")
            .Append("('COMUNA 2', 'Pago', 40000)")
            .Concat(Enumerable.Range(0, 18).Select(_ => "('COMUNA 2', 'Pago', 90000)"))
            .Append("('COMUNA 2', 'No pago', 5)");
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "precio_hogar", "comuna text, variable text, value numeric", string.Join(", ", rows));
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, unit, format, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'precio_hogar', 1, 'x', 'Precio', 'pesos', 'count', @a, now())",
            ("p", h.ProjectId), ("a", h.Users["analyst"]));
    }

    private static object PriceTile(string? statistic, params string[] lines) =>
        new { indicator = "precio_hogar", measure = "value", filters = PaidFilter, statistic, display = lines.Length == 0 ? null : new { secondary = lines } };

    private static JsonElement WidgetNamed(JsonDocument body, Guid id) =>
        body.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("widgetId").GetGuid() == id);

    [Fact]
    public async Task Median_card_through_MCP()
    {
        await using var h = await CreateAsync();
        await SeedPricesAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var widget = await AddWidgetAsync(h, dashboard, "Pagó por la última boleta", "stat", PriceTile("median"));

        using var saved = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        Assert.Equal("median", saved.RootElement.GetProperty("widgets")[0].GetProperty("config").GetProperty("statistic").GetString());
        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = widget })).Json();
        var tile = WidgetNamed(data, widget);
        Assert.Equal("shown", tile.GetProperty("status").GetString());
        Assert.Equal("median", tile.GetProperty("statistic").GetString());
        var row = Assert.Single(tile.GetProperty("rows").EnumerateArray());
        Assert.Equal(40000, row.GetProperty("value").GetDouble());
        Assert.Equal("40,000 pesos", row.GetProperty("formatted").GetString());
        // The tile's n is its number of values, on the row and on the widget, and a statistic has no numerator or denominator to return.
        Assert.Equal(37, row.GetProperty("n").GetDouble());
        Assert.Equal(37, tile.GetProperty("n").GetDouble());
        Assert.False(row.TryGetProperty("numerator", out _));
        Assert.False(row.TryGetProperty("denominator", out _));
        Assert.False(tile.TryGetProperty("unfiltered", out _));
        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "stat", config = PriceTile("median") })).Json();
        Assert.Equal("median", preview.RootElement.GetProperty("statistic").GetString());
        Assert.Equal(37, preview.RootElement.GetProperty("n").GetDouble());
        Assert.Equal("40,000 pesos", preview.RootElement.GetProperty("rows")[0].GetProperty("formatted").GetString());
    }

    [Fact]
    public async Task Value_without_the_filters_through_MCP()
    {
        await using var h = await CreateAsync();
        await SeedPricesAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await h.CallAsync("analyst", "update_dashboard", new
        {
            dashboardId = dashboard, change = Request,
            filters = new[] { new { column = "comuna", label = "Comuna", kind = "text" } },
        });
        var line = await AddWidgetAsync(h, dashboard, "Con línea", "stat", PriceTile("median", "unfiltered"));
        var plain = await AddWidgetAsync(h, dashboard, "Sin línea", "stat", PriceTile("median"));
        var one = new[] { new { column = "comuna", kind = "text", values = new[] { "COMUNA 1" } } };

        using var narrowed = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, filterValues = one })).Json();
        using var open = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();

        // COMUNA 1's households paid 10,000; every household's median is 40,000, shown as the tile shows its value.
        var tile = WidgetNamed(narrowed, line);
        Assert.Equal(10000, tile.GetProperty("rows")[0].GetProperty("value").GetDouble());
        var unfiltered = tile.GetProperty("unfiltered");
        Assert.Equal(40000, unfiltered.GetProperty("value").GetDouble());
        Assert.Equal("40,000 pesos", unfiltered.GetProperty("formatted").GetString());
        // A tile that does not show the line, and a query with no filter in force, have none.
        Assert.False(WidgetNamed(narrowed, plain).TryGetProperty("unfiltered", out _));
        Assert.False(WidgetNamed(open, line).TryGetProperty("unfiltered", out _));
        // A preview has no dashboard: it sets no flag, so it never has a value without filters.
        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "stat", config = PriceTile("median", "unfiltered") })).Json();
        Assert.False(preview.RootElement.TryGetProperty("unfiltered", out _));
        Assert.DoesNotContain("unfiltered", preview.RootElement.GetRawText());
    }

    private const string CrucesColumns = "variable text, orden text, categoria text, comuna text, numerator numeric, denominator numeric";

    private const string Components = "Componentes con sí o tal vez";

    /// <summary>Four answers of one question, given by 9,517 of 10,000 households (COMUNA 1 has 6,000, COMUNA 2 4,000), declared as <paramref name="kind"/>.</summary>
    private static async Task SeedCrucesAsync(McpProjectHarness h, string kind)
    {
        (string Orden, string Answer, int First, int Second)[] answers = [("01", "1", 25, 17), ("02", "2", 500, 319), ("03", "3", 2500, 1639), ("04", "4", 2700, 1817)];
        var rows = answers.SelectMany(a => new[]
        {
            $"('{Components}', '{a.Orden}', '{a.Answer}', 'COMUNA 1', {a.First}, 6000)",
            $"('{Components}', '{a.Orden}', '{a.Answer}', 'COMUNA 2', {a.Second}, 4000)",
        });
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "cruces_intencion", CrucesColumns, string.Join(", ", rows));
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, columns, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'cruces_intencion', 1, 'x', 'Cruces', 'percent', @c::jsonb, @a, now())",
            ("p", h.ProjectId), ("c", JsonSerializer.Serialize(new { variable = new { categories = kind }, orden = new { categories = kind }, categoria = new { categories = kind } })), ("a", h.Users["analyst"]));
    }

    private static object CrucesCard(params string[] answers) => new
    {
        indicator = "cruces_intencion", ratio = new { numerator = "numerator", denominator = "denominator" },
        filters = new object[] { new { column = "variable", op = "equals", value = Components }, new { column = "categoria", op = "in", values = answers } },
    };

    [Fact]
    public async Task Card_over_several_answers_through_MCP()
    {
        await using var h = await CreateAsync();
        await SeedCrucesAsync(h, "single");
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "Componentes", "stat", CrucesCard("1", "2", "3", "4"));

        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "stat", config = CrucesCard("1", "2", "3", "4") })).Json();
        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard, widgetId = widget })).Json();

        // 9,517 of the 10,000 households, the base counted once across the four answers: not 23.79 %.
        Assert.Equal("95.17%", preview.RootElement.GetProperty("rows")[0].GetProperty("formatted").GetString());
        var row = WidgetNamed(data, widget).GetProperty("rows")[0];
        Assert.Equal("95.17%", row.GetProperty("formatted").GetString());
        Assert.Equal(9517, row.GetProperty("numerator").GetDouble());
        Assert.Equal(10000, row.GetProperty("denominator").GetDouble());
        Assert.Equal(10000, WidgetNamed(data, widget).GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task Refused_card_through_MCP()
    {
        await using var h = await CreateAsync();
        await SeedCrucesAsync(h, "multiple");
        var dashboard = await FirstDashboardAsync(h);
        var refused = await AddWidgetAsync(h, dashboard, "Componentes", "stat", CrucesCard("1", "2", "3", "4"));
        var other = await AddWidgetAsync(h, dashboard, "Una respuesta", "stat", CrucesCard("4"));

        using var data = (await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard })).Json();
        using var preview = (await h.CallAsync("viewer", "query_indicator", new { chartType = "stat", config = CrucesCard("1", "2", "3", "4") })).Json();

        // The refused widget has no rows and the message naming the column; the other widget has its rows.
        var tile = WidgetNamed(data, refused);
        Assert.Equal("combinedCategories", tile.GetProperty("status").GetString());
        Assert.StartsWith("`categoria` holds the answers of a multiple-choice question.", tile.GetProperty("message").GetString());
        Assert.Contains("group or split the widget by `categoria`", tile.GetProperty("message").GetString());
        Assert.False(tile.TryGetProperty("rows", out _));
        var shown = WidgetNamed(data, other);
        Assert.Equal("shown", shown.GetProperty("status").GetString());
        Assert.Equal("45.17%", shown.GetProperty("rows")[0].GetProperty("formatted").GetString());
        // The preview returns the column and the same message, and no rows.
        Assert.Equal("categoria", preview.RootElement.GetProperty("combinedCategories").GetString());
        Assert.Equal(tile.GetProperty("message").GetString(), preview.RootElement.GetProperty("message").GetString());
        Assert.Empty(preview.RootElement.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task A_widget_without_the_new_fields_returns_what_it_returned()
    {
        await using var h = await CreateAsync();
        await SeedAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        await AddWidgetAsync(h, dashboard, "By municipality", "bar", BarConfig());
        await AddWidgetAsync(h, dashboard, "Overall", "stat", new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" } });

        var data = await h.CallAsync("viewer", "get_widget_data", new { dashboardId = dashboard });
        var preview = await h.CallAsync("viewer", "query_indicator", new { chartType = "stat", config = new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" } } });

        foreach (var text in new[] { data.Text(), preview.Text() })
        {
            foreach (var member in new[] { "statistic", "unfiltered", "combinedCategories" })
            {
                Assert.DoesNotContain($"\"{member}\"", text);
            }
        }

        Assert.Contains("\"formatted\":\"60%\"", data.Text());
    }

    [Fact]
    public async Task Statistic_on_a_bar_chart_through_MCP()
    {
        await using var h = await CreateAsync();
        await SeedPricesAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var result = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Mediana", chartType = "bar", change = Request,
            config = new { indicator = "precio_hogar", measure = "value", groupBy = "comuna", statistic = "median" },
        });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("config.statistic", result.Text());
        Assert.Contains("Only a stat tile has a statistic.", result.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));

        var lines = await h.CallAsync("analyst", "create_widget", new
        {
            dashboardId = dashboard, title = "Líneas", chartType = "bar", change = Request,
            config = new { indicator = "precio_hogar", measure = "value", groupBy = "comuna", display = new { secondary = new[] { "n" } } },
        });
        Assert.Equal("mcp.validation", lines.Code());
        Assert.Contains("config.display.secondary", lines.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    [Fact]
    public async Task Chart_type_changed_alone_through_MCP()
    {
        await using var h = await CreateAsync();
        await SeedPricesAsync(h);
        var dashboard = await FirstDashboardAsync(h);
        var widget = await AddWidgetAsync(h, dashboard, "Pagó por la última boleta", "stat", PriceTile("median", "n"));

        var result = await h.CallAsync("analyst", "update_widget", new { dashboardId = dashboard, widgetId = widget, chartType = "bar", change = Request });

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("config.statistic", result.Text());
        using var body = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var saved = body.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == widget);
        Assert.Equal("stat", saved.GetProperty("chartType").GetString());
        Assert.Equal("median", saved.GetProperty("config").GetProperty("statistic").GetString());
        Assert.Equal(["n"], saved.GetProperty("config").GetProperty("display").GetProperty("secondary").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task A_config_that_sends_unfiltered_saves_a_config_without_it()
    {
        await using var h = await CreateAsync();
        await SeedPricesAsync(h);
        var dashboard = await FirstDashboardAsync(h);

        var widget = await AddWidgetAsync(h, dashboard, "Pagó", "stat", new { indicator = "precio_hogar", measure = "value", filters = PaidFilter, unfiltered = true });

        using var body = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId = dashboard })).Json();
        var config = body.RootElement.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == widget).GetProperty("config");
        Assert.DoesNotContain("unfiltered", config.GetRawText());
        Assert.False(await TestData.ScalarAsync<bool>(h.Db, "SELECT config::text LIKE '%unfiltered%' FROM tamiza.dashboard_widgets WHERE id = @w", ("w", widget)));
    }
}
