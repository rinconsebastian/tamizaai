using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

/// <summary>
/// The dashboard version tools (mcp-server "Dashboard versions through MCP", "Restoring dashboards against the current version"): the
/// history and a version as the page shows them, and the restore of a kept version guarded by the current version the client read.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class McpDashboardVersionToolsTests(PostgresFixture postgres)
{
    private const string Columns = "municipality text, value numeric, numerator numeric, denominator numeric";

    private async Task<McpProjectHarness> CreateAsync()
    {
        var h = await McpProjectHarness.CreateAsync(postgres, McpAccess.Aggregates);
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "piped_water_rate", Columns, "('Santa Ana', 0.9, 9, 10),('San Luis', 0.3, 3, 10)");
        return h;
    }

    private static object BarConfig() => new { indicator = "piped_water_rate", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality" };

    private static HttpClient Rest(McpProjectHarness h, string person) =>
        h.Factory.CreateClient().WithToken(TestTokens.Create(person, person, $"{person}@example.org"));

    private static string DashboardsUrl(McpProjectHarness h) => $"/api/v1/projects/{h.ProjectId}/dashboards";

    /// <summary>Adds a dashboard in the page, as the analyst: its version 1 is made in Tamiza.</summary>
    private static async Task<Guid> AddDashboardInTamizaAsync(McpProjectHarness h, string name)
    {
        var response = await Rest(h, "analyst").PostAsJsonAsync(DashboardsUrl(h), new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Adds a widget in the page, which makes a version of its own.</summary>
    private static async Task<Guid> AddWidgetInTamizaAsync(McpProjectHarness h, Guid dashboardId, string title, object? layout = null, string person = "analyst")
    {
        var response = await Rest(h, person).PostAsJsonAsync($"{DashboardsUrl(h)}/{dashboardId}/widgets", new { title, chartType = "bar", config = BarConfig(), layout });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Adds versions <paramref name="from"/> to <paramref name="to"/> to a dashboard with SQL, each holding a state it no longer has.</summary>
    private static Task SeedVersionsAsync(McpProjectHarness h, Guid dashboardId, int from, int to) =>
        TestData.ExecuteAsync(h.Db,
            """
            INSERT INTO tamiza.dashboard_versions (id, project_id, dashboard_id, number, content, changes, author_id, created_at, saved_at)
            SELECT gen_random_uuid(), d.project_id, d.id, n, '{}', '[{"kind": "layoutChanged"}]', d.created_by, now() - interval '1 day',
                   now() - interval '1 day'
            FROM tamiza.dashboards AS d, generate_series(@from, @to) AS n
            WHERE d.id = @id
            """,
            ("id", dashboardId), ("from", from), ("to", to));

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

    /// <summary>The dashboard's name and widgets as stored, to check that a refused call changed nothing.</summary>
    private static async Task<string> StoredAsync(McpProjectHarness h, Guid dashboardId) =>
        (await TestData.ScalarAsync<string>(h.Db,
            """
            SELECT d.name || ' ' || d.filters::text || ' ' || coalesce(string_agg(format('%s %s %s %s', w.id, w.title, w.config, w.layout), ',' ORDER BY w.id), '')
            FROM tamiza.dashboards AS d LEFT JOIN tamiza.dashboard_widgets AS w ON w.dashboard_id = d.id
            WHERE d.id = @id GROUP BY d.id
            """,
            ("id", dashboardId)))!;

    /// <summary>The dashboard's widgets as <c>get_dashboard</c> lists them: (id, title).</summary>
    private static async Task<(Guid Id, string Title)[]> WidgetsAsync(McpProjectHarness h, Guid dashboardId)
    {
        using var body = (await h.CallAsync("viewer", "get_dashboard", new { dashboardId })).Json();
        return body.RootElement.GetProperty("widgets").EnumerateArray()
            .Select(w => (w.GetProperty("id").GetGuid(), w.GetProperty("title").GetString()!))
            .ToArray();
    }

    private static Task<CallToolResult> RestoreAsync(McpProjectHarness h, Guid dashboardId, int version, int baseVersion, string change, string person = "analyst") =>
        h.CallAsync(person, "restore_dashboard_version", new { dashboardId, version, baseVersion, change });

    private static JsonElement Body(CallToolResult result)
    {
        Assert.True(result.Code() is null, result.Text());
        using var body = result.Json();
        return body.RootElement.Clone();
    }

    [Fact]
    public async Task Undoing_a_request_lists_the_versions_restores_version_8_and_water_access_is_as_before_with_version_10_via_mcp()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water access");
        await SeedVersionsAsync(h, water, 2, 7);
        var rate = await AddWidgetInTamizaAsync(h, water, "Rate");
        // Claude's last request made version 9.
        const string request = "Add the water widgets";
        foreach (var title in new[] { "By municipality", "Trend" })
        {
            Assert.Null((await h.CallAsync("analyst", "create_widget", new { dashboardId = water, title, chartType = "bar", config = BarConfig(), change = request })).Code());
        }

        var listed = Body(await h.CallAsync("analyst", "list_dashboard_versions", new { dashboardId = water }));
        var before = Body(await h.CallAsync("analyst", "get_dashboard_version", new { dashboardId = water, version = 8 }));
        var restore = Body(await RestoreAsync(h, water, 8, listed.GetProperty("current").GetInt32(), "Undo adding the water widgets"));

        Assert.Equal(water, listed.GetProperty("dashboardId").GetGuid());
        Assert.Equal(9, listed.GetProperty("current").GetInt32());
        var versions = listed.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(Enumerable.Range(1, 9).Reverse(), versions.Select(v => v.GetProperty("number").GetInt32()));
        Assert.True(versions[0].GetProperty("current").GetBoolean());
        Assert.Equal(("mcp", request, "analyst"), (versions[0].GetProperty("origin").GetString(), versions[0].GetProperty("note").GetString(), versions[0].GetProperty("author").GetProperty("name").GetString()));
        Assert.Equal(["widgetAdded", "widgetAdded"], versions[0].GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("kind").GetString()!).ToArray());
        Assert.Equal(("app", false), (versions[1].GetProperty("origin").GetString(), versions[1].GetProperty("current").GetBoolean()));
        Assert.False(versions[1].TryGetProperty("note", out _));
        Assert.Equal(["Rate"], before.GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("title").GetString()!).ToArray());

        Assert.True(restore.GetProperty("restored").GetBoolean());
        Assert.Equal((water, 10, 8, "Water access", false), (restore.GetProperty("dashboardId").GetGuid(), restore.GetProperty("version").GetInt32(),
            restore.GetProperty("restoredFrom").GetInt32(), restore.GetProperty("name").GetString(), restore.GetProperty("nameKept").GetBoolean()));
        Assert.Empty(restore.GetProperty("copies").EnumerateArray());
        Assert.Equal([(water, 10)], restore.GetProperty("dashboardVersions").EnumerateArray().Select(v => (v.GetProperty("dashboardId").GetGuid(), v.GetProperty("version").GetInt32())).ToArray());
        Assert.Equal([(rate, "Rate")], await WidgetsAsync(h, water));
        Assert.Equal("""10 | analyst | mcp | Undo adding the water widgets | [{"kind": "restored", "version": 8}]""", (await VersionsAsync(h, water))[^1]);
    }

    [Fact]
    public async Task Comparing_a_version_gives_a_widget_since_removed_with_its_title_chart_type_query_position_and_size_and_changes_nothing()
    {
        await using var h = await CreateAsync();
        var housing = await AddDashboardInTamizaAsync(h, "Housing");
        await SeedVersionsAsync(h, housing, 2, 5);
        await AddWidgetInTamizaAsync(h, housing, "Coverage");
        var source = await AddWidgetInTamizaAsync(h, housing, "Water source", new { x = 6, y = 8, w = 6, h = 5 });
        Assert.Equal(HttpStatusCode.NoContent, (await Rest(h, "analyst").DeleteAsync($"{DashboardsUrl(h)}/{housing}/widgets/{source}")).StatusCode);
        var stored = await StoredAsync(h, housing);

        var version = Body(await h.CallAsync("analyst", "get_dashboard_version", new { dashboardId = housing, version = 7 }));

        Assert.Equal((7, false, "Housing"), (version.GetProperty("number").GetInt32(), version.GetProperty("current").GetBoolean(), version.GetProperty("name").GetString()));
        var widget = version.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("title").GetString() == "Water source");
        Assert.Equal(source, widget.GetProperty("id").GetGuid());
        Assert.Equal("bar", widget.GetProperty("chartType").GetString());
        Assert.Equal(("piped_water_rate", "municipality", "numerator"), (widget.GetProperty("config").GetProperty("indicator").GetString(),
            widget.GetProperty("config").GetProperty("groupBy").GetString(), widget.GetProperty("config").GetProperty("ratio").GetProperty("numerator").GetString()));
        Assert.Equal((6, 8, 6, 5), (widget.GetProperty("layout").GetProperty("x").GetInt32(), widget.GetProperty("layout").GetProperty("y").GetInt32(),
            widget.GetProperty("layout").GetProperty("w").GetInt32(), widget.GetProperty("layout").GetProperty("h").GetInt32()));
        Assert.Equal("""[{"kind": "widgetAdded", "widget": "Water source"}]""", (await VersionsAsync(h, housing))[6].Split(" | ")[4]);
        Assert.Equal(stored, await StoredAsync(h, housing));
        Assert.Equal(Enumerable.Range(1, 8), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task Viewer_is_refused_the_version_tools_saying_they_need_the_analyst_role()
    {
        await using var h = await CreateAsync();
        var housing = await AddDashboardInTamizaAsync(h, "Housing");

        var list = await h.CallAsync("viewer", "list_dashboard_versions", new { dashboardId = housing });
        var read = await h.CallAsync("viewer", "get_dashboard_version", new { dashboardId = housing, version = 1 });
        var restore = await RestoreAsync(h, housing, 1, 1, "Go back", person: "viewer");

        Assert.Equal("mcp.forbidden", list.Code());
        Assert.Contains("analyst role", list.Text());
        Assert.Equal("mcp.forbidden", read.Code());
        Assert.Equal("mcp.forbidden", restore.Code());
        Assert.Equal(["""1 | analyst | app | - | [{"kind": "created"}]"""], await VersionsAsync(h, housing));
    }

    [Fact]
    public async Task Version_no_longer_kept_is_refused_with_version_not_found_for_a_read_and_a_restore_and_the_dashboard_is_unchanged()
    {
        await using var h = await CreateAsync();
        var housing = await AddDashboardInTamizaAsync(h, "Housing");
        await SeedVersionsAsync(h, housing, 2, 10);
        await AddWidgetInTamizaAsync(h, housing, "Coverage");
        var stored = await StoredAsync(h, housing);

        var read = await h.CallAsync("analyst", "get_dashboard_version", new { dashboardId = housing, version = 1 });
        var restore = await RestoreAsync(h, housing, 1, 11, "Go back to the start");

        Assert.Equal(Enumerable.Range(2, 10), await NumbersAsync(h, housing));
        Assert.Equal("dashboards.versionNotFound", read.Code());
        Assert.Equal("dashboards.versionNotFound", restore.Code());
        Assert.Equal(stored, await StoredAsync(h, housing));
        Assert.Equal(Enumerable.Range(2, 10), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task Member_changed_meanwhile_the_restore_is_refused_naming_version_13_its_author_time_and_origin_and_housing_is_unchanged()
    {
        await using var h = await CreateAsync();
        var housing = await AddDashboardInTamizaAsync(h, "Housing");
        await SeedVersionsAsync(h, housing, 2, 11);
        await AddWidgetInTamizaAsync(h, housing, "Coverage");
        // Claude, signed in as the admin, read version 12; the analyst then adds a widget in the page, making version 13.
        var read = Body(await h.CallAsync("admin", "get_dashboard", new { dashboardId = housing }));
        await AddWidgetInTamizaAsync(h, housing, "Water source");
        var stored = await StoredAsync(h, housing);

        var result = await RestoreAsync(h, housing, 9, read.GetProperty("version").GetInt32(), "Go back to version 9", person: "admin");

        Assert.Equal(12, read.GetProperty("version").GetInt32());
        Assert.Equal("mcp.dashboardChanged", result.Code());
        using var body = result.Json();
        var current = body.RootElement.GetProperty("current");
        Assert.Equal((13, "analyst", "app"), (current.GetProperty("version").GetInt32(), current.GetProperty("author").GetString(), current.GetProperty("origin").GetString()));
        var savedAt = await TestData.ScalarAsync<DateTime>(h.Db, "SELECT saved_at FROM tamiza.dashboard_versions WHERE dashboard_id = @d AND number = 13", ("d", housing));
        Assert.Equal(savedAt, current.GetProperty("savedAt").GetDateTimeOffset().UtcDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Contains("current version is 13", result.Text());
        Assert.Contains("nothing was restored", result.Text());
        Assert.Equal(stored, await StoredAsync(h, housing));
        Assert.Equal(Enumerable.Range(4, 10), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task Widget_removal_undone_by_restoring_version_7_brings_water_source_back_to_housing()
    {
        await using var h = await CreateAsync();
        var housing = await AddDashboardInTamizaAsync(h, "Housing");
        await SeedVersionsAsync(h, housing, 2, 5);
        var coverage = await AddWidgetInTamizaAsync(h, housing, "Coverage");
        var source = await AddWidgetInTamizaAsync(h, housing, "Water source");

        var removed = Body(await h.CallAsync("analyst", "remove_widget", new { dashboardId = housing, widgetId = source, expectedTitle = "Water source", change = "Remove the water source" }));
        var restore = Body(await RestoreAsync(h, housing, 7, 8, "Bring back the water source"));

        Assert.Equal(8, removed.GetProperty("dashboardVersions")[0].GetProperty("version").GetInt32());
        Assert.Equal(9, restore.GetProperty("version").GetInt32());
        Assert.Equal([(coverage, "Coverage"), (source, "Water source")], await WidgetsAsync(h, housing));
    }

    [Fact]
    public async Task Restore_then_edit_in_one_request_makes_two_versions_the_restore_and_the_widget()
    {
        await using var h = await CreateAsync();
        var water = await AddDashboardInTamizaAsync(h, "Water");
        await SeedVersionsAsync(h, water, 2, 6);
        await AddWidgetInTamizaAsync(h, water, "Rate");
        await AddWidgetInTamizaAsync(h, water, "Trend");
        const string request = "Go back and add a pie chart";

        var restore = Body(await RestoreAsync(h, water, 7, 8, request));
        var added = Body(await h.CallAsync("analyst", "create_widget", new { dashboardId = water, title = "Shares", chartType = "pie", config = BarConfig(), change = request }));

        Assert.Equal(9, restore.GetProperty("version").GetInt32());
        Assert.Equal(10, added.GetProperty("dashboardVersions")[0].GetProperty("version").GetInt32());
        Assert.Equal(
            [
                """9 | analyst | mcp | Go back and add a pie chart | [{"kind": "restored", "version": 7}]""",
                """10 | analyst | mcp | Go back and add a pie chart | [{"kind": "widgetAdded", "widget": "Shares"}]""",
            ],
            (await VersionsAsync(h, water))[^2..]);
        Assert.Equal(["Rate", "Shares"], (await WidgetsAsync(h, water)).Select(w => w.Title).ToArray());
    }

    [Fact]
    public async Task Undoing_a_request_that_moved_a_widget_restores_field_then_health_and_coverage_is_back_on_health_with_its_identifier()
    {
        await using var h = await CreateAsync();
        var health = await AddDashboardInTamizaAsync(h, "Health");
        var coverage = await AddWidgetInTamizaAsync(h, health, "Coverage");
        var field = await AddDashboardInTamizaAsync(h, "Field");
        var moved = Body(await h.CallAsync("analyst", "update_widget", new { dashboardId = health, widgetId = coverage, moveToDashboardId = field, change = "Move coverage to Field" }));
        var named = moved.GetProperty("dashboardVersions").EnumerateArray().ToDictionary(v => v.GetProperty("dashboardId").GetGuid(), v => v.GetProperty("version").GetInt32());

        // The dashboard it moved to first, then the one it left, each to the version before the request.
        var onField = Body(await RestoreAsync(h, field, named[field] - 1, named[field], "Undo moving coverage"));
        var onHealth = Body(await RestoreAsync(h, health, named[health] - 1, named[health], "Undo moving coverage"));

        Assert.Equal((3, 2), (named[health], named[field]));
        Assert.Empty(onField.GetProperty("copies").EnumerateArray());
        Assert.Empty(onHealth.GetProperty("copies").EnumerateArray());
        Assert.Equal([(coverage, "Coverage")], await WidgetsAsync(h, health));
        Assert.Empty(await WidgetsAsync(h, field));
    }

    [Fact]
    public async Task A_restore_without_change_or_with_a_blank_one_is_refused_with_validation_and_restores_nothing()
    {
        await using var h = await CreateAsync();
        var housing = await AddDashboardInTamizaAsync(h, "Housing");
        await AddWidgetInTamizaAsync(h, housing, "Coverage");
        var stored = await StoredAsync(h, housing);

        var missing = await h.CallAsync("analyst", "restore_dashboard_version", new { dashboardId = housing, version = 1, baseVersion = 2 });
        var blank = await RestoreAsync(h, housing, 1, 2, "  ");

        Assert.Equal("mcp.validation", missing.Code());
        Assert.Contains("change", missing.Text());
        using (var left = missing.Json())
        {
            Assert.True(left.RootElement.GetProperty("fields").TryGetProperty("change", out _), missing.Text());
        }

        Assert.Equal("mcp.validation", blank.Code());
        using var body = blank.Json();
        Assert.True(body.RootElement.GetProperty("fields").TryGetProperty("change", out _));
        Assert.Equal(stored, await StoredAsync(h, housing));
        Assert.Equal(Enumerable.Range(1, 2), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task A_widget_since_moved_to_another_dashboard_comes_back_as_a_copy_named_in_copies_with_the_dashboard_that_still_holds_it()
    {
        await using var h = await CreateAsync();
        var health = await AddDashboardInTamizaAsync(h, "Health");
        var coverage = await AddWidgetInTamizaAsync(h, health, "Coverage");
        var field = await AddDashboardInTamizaAsync(h, "Field");
        Assert.Equal(HttpStatusCode.OK, (await Rest(h, "analyst").PatchAsJsonAsync($"{DashboardsUrl(h)}/{health}/widgets/{coverage}", new { dashboardId = field })).StatusCode);
        var fieldBefore = await StoredAsync(h, field);

        var restore = Body(await RestoreAsync(h, health, 2, 3, "Bring coverage back to Health"));

        var copy = Assert.Single(restore.GetProperty("copies").EnumerateArray());
        Assert.Equal((coverage, "Coverage", field, "Field"), (copy.GetProperty("originalId").GetGuid(), copy.GetProperty("title").GetString(),
            copy.GetProperty("dashboardId").GetGuid(), copy.GetProperty("dashboardName").GetString()));
        var copyId = copy.GetProperty("copyId").GetGuid();
        Assert.NotEqual(coverage, copyId);
        Assert.Equal([(copyId, "Coverage")], await WidgetsAsync(h, health));
        Assert.Equal(fieldBefore, await StoredAsync(h, field));
        Assert.Equal([(coverage, "Coverage")], await WidgetsAsync(h, field));
    }

    [Fact]
    public async Task Restoring_again_a_version_whose_widget_came_back_as_a_copy_answers_restored_false_and_keeps_the_copy()
    {
        await using var h = await CreateAsync();
        var health = await AddDashboardInTamizaAsync(h, "Health");
        var coverage = await AddWidgetInTamizaAsync(h, health, "Coverage");
        var field = await AddDashboardInTamizaAsync(h, "Field");
        Assert.Equal(HttpStatusCode.OK, (await Rest(h, "analyst").PatchAsJsonAsync($"{DashboardsUrl(h)}/{health}/widgets/{coverage}", new { dashboardId = field })).StatusCode);
        var first = Body(await RestoreAsync(h, health, 2, 3, "Bring coverage back to Health"));
        var copyId = first.GetProperty("copies")[0].GetProperty("copyId").GetGuid();
        var stored = await StoredAsync(h, health);
        var versions = await VersionsAsync(h, health);

        // The client calls it again, having read the restore's version 4 as the current one.
        var again = Body(await RestoreAsync(h, health, 2, 4, "Bring coverage back to Health"));

        Assert.Equal(4, first.GetProperty("version").GetInt32());
        Assert.False(again.GetProperty("restored").GetBoolean());
        Assert.Equal(4, again.GetProperty("version").GetInt32());
        Assert.Equal(4, again.GetProperty("dashboardVersions")[0].GetProperty("version").GetInt32());
        Assert.Equal([(copyId, "Coverage")], await WidgetsAsync(h, health));
        Assert.Equal(stored, await StoredAsync(h, health));
        Assert.Equal(versions, await VersionsAsync(h, health));
    }
}
