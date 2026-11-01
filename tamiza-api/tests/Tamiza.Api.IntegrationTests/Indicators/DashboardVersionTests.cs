using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Data;
using Tamiza.Api.Indicators;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.IntegrationTests.Reports;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// The versions the dashboard routes record (dashboard-versions): one per request of each dashboard it changed, none for a save that
/// changes nothing or for what is not a dashboard change, the 10 newest kept, and the rule that lets an AI client's request grow one.
/// Then the history and the restore of a kept version (design D7, D9).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardVersionTests(PostgresFixture postgres)
{
    private const string AddWaterWidgets = "Add water widgets";

    private async Task<IndicatorsHarness> PublishedAsync(Action<IServiceCollection>? configureServices = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, configureServices: configureServices);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 40, 4, 10), ('Palmira', 60, 6, 10)");
        return h;
    }

    private static object Column(string title, object? layout = null) => new
    {
        title,
        chartType = "column",
        config = new { indicator = "coverage", groupBy = "municipality" },
        layout,
    };

    private static object Layout(int x, int y, int w = 6, int h = 4) => new { x, y, w, h };

    private static async Task<Guid> AddWidgetAsync(IndicatorsHarness h, string title, Guid dashboardId, object? layout = null) =>
        (await h.AddWidgetAsync(Column(title, layout), dashboardId: dashboardId)).GetProperty("id").GetGuid();

    /// <summary>
    /// A dashboard's versions, oldest first, as <c>number author origin changes</c>: the author's subject, and the changes as
    /// PostgreSQL prints them.
    /// </summary>
    private static async Task<string[]> VersionsAsync(IndicatorsHarness h, Guid dashboardId)
    {
        var rows = await TestData.ScalarAsync<string>(h.Db,
            """
            SELECT coalesce(string_agg(format('%s %s %s %s', v.number, u.keycloak_sub, v.origin, v.changes), E'\n' ORDER BY v.number), '')
            FROM tamiza.dashboard_versions AS v JOIN tamiza.users AS u ON u.id = v.author_id
            WHERE v.project_id = @project AND v.dashboard_id = @dashboard
            """,
            ("project", h.ProjectId), ("dashboard", dashboardId));
        return rows!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Every version of the project as <c>dashboard id number saved_at</c>, to check that a request recorded nothing.</summary>
    private static async Task<string> AllVersionsAsync(IndicatorsHarness h) =>
        (await TestData.ScalarAsync<string>(h.Db,
            """
            SELECT coalesce(string_agg(format('%s %s %s %s', dashboard_id, number, changes, saved_at), E'\n' ORDER BY dashboard_id, number), '')
            FROM tamiza.dashboard_versions WHERE project_id = @project
            """,
            ("project", h.ProjectId)))!;

    private static async Task<int[]> NumbersAsync(IndicatorsHarness h, Guid dashboardId) =>
        (await VersionsAsync(h, dashboardId)).Select(row => int.Parse(row.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    /// <summary>A version's content as PostgreSQL holds it.</summary>
    private static async Task<JsonElement> ContentAsync(IndicatorsHarness h, Guid dashboardId, int number) =>
        JsonDocument.Parse((await TestData.ScalarAsync<string>(h.Db,
            "SELECT content::text FROM tamiza.dashboard_versions WHERE project_id = @project AND dashboard_id = @dashboard AND number = @number",
            ("project", h.ProjectId), ("dashboard", dashboardId), ("number", number)))!).RootElement.Clone();

    private static string[] Titles(JsonElement content) =>
        content.GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("title").GetString()!).ToArray();

    private static string LayoutOf(JsonElement content, string title) =>
        content.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("title").GetString() == title).GetProperty("layout").GetRawText();

    /// <summary>Adds versions <paramref name="from"/> to <paramref name="to"/> to a dashboard with SQL, each holding a state it no longer has.</summary>
    private static Task SeedVersionsAsync(IndicatorsHarness h, Guid dashboardId, int from, int to) =>
        h.ExecuteAsync(
            """
            INSERT INTO tamiza.dashboard_versions (id, project_id, dashboard_id, number, content, changes, author_id, created_at, saved_at)
            SELECT gen_random_uuid(), d.project_id, d.id, n, '{}', '[{"kind": "layoutChanged"}]', d.created_by, now() - interval '1 day',
                   now() - interval '1 day'
            FROM tamiza.dashboards AS d, generate_series(@from, @to) AS n
            WHERE d.id = @id
            """,
            ("id", dashboardId), ("from", from), ("to", to));

    private static Task<Guid> AnalystIdAsync(IndicatorsHarness h) =>
        TestData.ScalarAsync<Guid>(h.Db, "SELECT id FROM tamiza.users WHERE keycloak_sub = 'grace'");

    /// <summary>
    /// Notes <paramref name="changes"/> and flushes them in a scope of their own, as one request of the analyst does, with the given
    /// origin and request. The writes they stand for are made with SQL first, as a handler would have committed them.
    /// </summary>
    private static async Task<IReadOnlyList<DashboardVersionNumber>> FlushAsync(
        IndicatorsHarness h, DashboardVersionOrigin origin, string? note, params (Guid DashboardId, DashboardChange Change)[] changes)
    {
        var analyst = await AnalystIdAsync(h);
        using var scope = h.Factory.Services.CreateScope();
        var versions = scope.ServiceProvider.GetRequiredService<DashboardVersions>();
        foreach (var (dashboardId, change) in changes)
        {
            versions.Note(h.ProjectId, dashboardId, change);
        }

        return await versions.FlushAsync(origin, analyst, note);
    }

    private static Task RenameWithSqlAsync(IndicatorsHarness h, Guid dashboardId, string name) =>
        h.ExecuteAsync("UPDATE tamiza.dashboards SET name = @name, normalized_name = lower(@name) WHERE id = @id", ("id", dashboardId), ("name", name));

    private static DashboardChange Renamed(string name) => new(DashboardChangeKinds.Renamed, Dashboard: name);

    // Content (design D2)

    [Fact]
    public async Task A_dashboard_gives_the_same_content_every_time_it_is_built()
    {
        await using var h = await PublishedAsync();
        await AddWidgetAsync(h, "Coverage", h.DashboardId, Layout(0, 4));
        await AddWidgetAsync(h, "Water source", h.DashboardId, Layout(0, 4));
        await AddWidgetAsync(h, "Households", h.DashboardId, Layout(6, 0));
        using var scope = h.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TamizaDbContext>();

        var first = await DashboardVersions.ContentAsync(db, h.ProjectId, h.DashboardId, CancellationToken.None);
        var second = await DashboardVersions.ContentAsync(db, h.ProjectId, h.DashboardId, CancellationToken.None);

        Assert.Equal(first, second);
        // Top to bottom, then left to right, then the older first.
        Assert.Equal(["Households", "Coverage", "Water source"], Titles(JsonDocument.Parse(first!).RootElement));
        Assert.Null(await DashboardVersions.ContentAsync(db, h.ProjectId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Two_contents_that_differ_only_in_key_order_and_spacing_are_equal_so_the_flush_records_nothing()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var housing = await h.InsertDashboardAsync("Housing");
        await h.ExecuteAsync(
            """
            INSERT INTO tamiza.dashboard_versions (id, project_id, dashboard_id, number, content, changes, author_id, created_at, saved_at)
            SELECT gen_random_uuid(), project_id, id, 1, '{ "widgets" : [ ],   "filters": [], "name" : "Housing" }', '[{"kind": "created"}]',
                   created_by, now(), now()
            FROM tamiza.dashboards WHERE id = @id
            """,
            ("id", housing));
        using var scope = h.Factory.Services.CreateScope();
        var built = await DashboardVersions.ContentAsync(scope.ServiceProvider.GetRequiredService<TamizaDbContext>(), h.ProjectId, housing, CancellationToken.None);
        var stored = await TestData.ScalarAsync<string>(h.Db, "SELECT content::text FROM tamiza.dashboard_versions WHERE dashboard_id = @id", ("id", housing));

        var numbers = await FlushAsync(h, DashboardVersionOrigin.App, null, (housing, Renamed("Housing")));

        Assert.NotEqual(stored, built); // not the same text: the comparison is PostgreSQL's
        Assert.Equal([new DashboardVersionNumber(housing, 1)], numbers);
        Assert.Equal(new[] { 1 }, await NumbersAsync(h, housing));
    }

    // Changes that make a version (dashboard-versions)

    [Fact]
    public async Task Widget_added_makes_the_next_version_by_that_analyst_at_that_time_holding_it_with_the_others()
    {
        await using var h = await PublishedAsync();
        var housing = await h.AddDashboardAsync("Housing");
        await AddWidgetAsync(h, "Households", housing, Layout(0, 0));
        await AddWidgetAsync(h, "Coverage", housing, Layout(6, 0));
        await AddWidgetAsync(h, "Blocks", housing, Layout(0, 4));
        Assert.Equal(new[] { 1, 2, 3, 4 }, await NumbersAsync(h, housing));
        var before = DateTime.UtcNow.AddMilliseconds(-1);

        await AddWidgetAsync(h, "Water source", housing, Layout(6, 4));

        var after = DateTime.UtcNow.AddMilliseconds(1);
        Assert.Equal("""5 grace app [{"kind": "widgetAdded", "widget": "Water source"}]""", (await VersionsAsync(h, housing))[^1]);
        Assert.Equal(["Households", "Coverage", "Blocks", "Water source"], Titles(await ContentAsync(h, housing, 5)));
        var times = await TestData.ScalarAsync<string>(h.Db,
            "SELECT format('%s %s', created_at = saved_at, note IS NULL) FROM tamiza.dashboard_versions WHERE dashboard_id = @id AND number = 5",
            ("id", housing));
        Assert.Equal("t t", times);
        var savedAt = await TestData.ScalarAsync<DateTime>(h.Db,
            "SELECT saved_at FROM tamiza.dashboard_versions WHERE dashboard_id = @id AND number = 5", ("id", housing));
        Assert.InRange(savedAt, before, after);
    }

    [Fact]
    public async Task One_character_in_a_widget_title_makes_a_version_with_that_title()
    {
        await using var h = await PublishedAsync();
        var id = await AddWidgetAsync(h, "Coverage", h.DashboardId);

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { title = "Coverage." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                """1 grace app [{"kind": "widgetAdded", "widget": "Coverage"}]""",
                """2 grace app [{"kind": "widgetChanged", "widget": "Coverage."}]""",
            ],
            await VersionsAsync(h, h.DashboardId));
        Assert.Equal(["Coverage."], Titles(await ContentAsync(h, h.DashboardId, 2)));
    }

    [Fact]
    public async Task Two_drags_make_two_versions_the_second_holding_both_new_places()
    {
        await using var h = await PublishedAsync();
        var first = await AddWidgetAsync(h, "First", h.DashboardId, Layout(0, 0));
        var second = await AddWidgetAsync(h, "Second", h.DashboardId, Layout(6, 0));

        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.PatchJsonAsync(h.WidgetUrl(first), new { layout = Layout(0, 4) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.PatchJsonAsync(h.WidgetUrl(second), new { layout = Layout(6, 4, 6, 2) })).StatusCode);

        Assert.Equal(
            [
                """3 grace app [{"kind": "layoutChanged"}]""",
                """4 grace app [{"kind": "layoutChanged"}]""",
            ],
            (await VersionsAsync(h, h.DashboardId))[2..]);
        var last = await ContentAsync(h, h.DashboardId, 4);
        Assert.Equal("""{"h": 4, "w": 6, "x": 0, "y": 4}""", LayoutOf(last, "First"));
        Assert.Equal("""{"h": 2, "w": 6, "x": 6, "y": 4}""", LayoutOf(last, "Second"));
    }

    [Fact]
    public async Task Filters_saved_unchanged_make_no_version_and_leave_the_stored_list_as_it_was()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var saved = await h.SaveFiltersAsync(new[]
        {
            new { label = "Municipality", column = "municipality", kind = "text", @default = (object?)new { values = new[] { "Cali" } } },
            new { label = "Round", column = "round", kind = "number", @default = (object?)null },
        });
        var dashboards = await h.StoredDashboardsAsync();
        var versions = await VersionsAsync(h, h.DashboardId);

        var again = await h.SaveFiltersAsync((await h.GetDashboardAsync()).GetProperty("filters"));

        Assert.Equal(saved.GetRawText(), again.GetRawText());
        Assert.Equal(["""1 grace app [{"kind": "filtersChanged"}]"""], versions);
        Assert.Equal(versions, await VersionsAsync(h, h.DashboardId));
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task Widget_form_saved_unchanged_on_a_widget_saved_before_series_settings_makes_no_version_and_leaves_its_stored_text()
    {
        await using var h = await PublishedAsync();
        var id = await AddWidgetAsync(h, "Coverage by municipality", h.DashboardId, Layout(0, 0));
        // Stored before the series settings existed, and recorded as such, as the migration records a dashboard.
        await h.ExecuteAsync("""UPDATE tamiza.dashboard_widgets SET config = '{"indicator": "coverage", "groupBy": "municipality"}' WHERE id = @id""", ("id", id));
        await FlushAsync(h, DashboardVersionOrigin.App, null, (h.DashboardId, new DashboardChange(DashboardChangeKinds.WidgetChanged, Widget: "Coverage by municipality")));
        var widget = (await h.GetWidgetsAsync()).EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("config").GetProperty("seriesBy").ValueKind);
        var stored = await h.StoredWidgetsAsync();
        var versions = await AllVersionsAsync(h);

        var form = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new
        {
            title = widget.GetProperty("title").GetString(),
            chartType = widget.GetProperty("chartType").GetString(),
            config = widget.GetProperty("config"),
        });
        var place = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { layout = widget.GetProperty("layout") });

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal(HttpStatusCode.OK, place.StatusCode);
        Assert.Equal(widget.GetRawText(), (await form.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await AllVersionsAsync(h));
        Assert.Equal(new[] { 1, 2 }, await NumbersAsync(h, h.DashboardId));
    }

    [Fact]
    public async Task Widget_moved_to_another_dashboard_makes_a_version_of_each_saying_where_it_went_and_where_it_came_from()
    {
        await using var h = await PublishedAsync();
        var health = await h.AddDashboardAsync("Health");
        var field = await h.AddDashboardAsync("Field");
        var id = await AddWidgetAsync(h, "Coverage by municipality", health);

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id, health), new { dashboardId = field });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """3 grace app [{"kind": "widgetMovedOut", "widget": "Coverage by municipality", "dashboard": "Field"}]""",
            (await VersionsAsync(h, health))[^1]);
        Assert.Equal(
            [
                """1 grace app [{"kind": "created"}]""",
                """2 grace app [{"kind": "widgetMovedIn", "widget": "Coverage by municipality", "dashboard": "Health"}]""",
            ],
            await VersionsAsync(h, field));
        Assert.Empty(Titles(await ContentAsync(h, health, 3)));
        Assert.Equal(["Coverage by municipality"], Titles(await ContentAsync(h, field, 2)));
    }

    [Fact]
    public async Task New_dashboard_has_version_1_with_no_widgets_saying_it_was_added()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var field = await h.AddDashboardAsync("Field");

        Assert.Equal(["""1 grace app [{"kind": "created"}]"""], await VersionsAsync(h, field));
        Assert.Equal("""{"name": "Field", "filters": [], "widgets": []}""", (await ContentAsync(h, field, 1)).GetRawText());
        Assert.Equal("0 t t t", await TestData.ScalarAsync<string>(h.Db,
            "SELECT format('%s %s %s %s', more_changes, restored_from IS NULL, note IS NULL, created_at = saved_at) FROM tamiza.dashboard_versions WHERE dashboard_id = @id",
            ("id", field)));
    }

    [Fact]
    public async Task A_rename_and_a_copy_record_the_names_and_the_copy_starts_its_own_history()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var water = await h.AddDashboardAsync("Water");

        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.PatchJsonAsync(h.DashboardUrl(water), new { name = "Agua" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.PatchJsonAsync(h.DashboardUrl(water), new { name = "Agua" })).StatusCode);
        var copy = await h.Analyst.PostAsJsonAsync(h.DashboardUrl(water) + "/copies", new { name = "Agua 2" });

        Assert.Equal(
            [
                """1 grace app [{"kind": "created"}]""",
                """2 grace app [{"kind": "renamed", "dashboard": "Agua"}]""",
            ],
            await VersionsAsync(h, water));
        var copyId = (await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(["""1 grace app [{"kind": "duplicated", "dashboard": "Agua"}]"""], await VersionsAsync(h, copyId));
    }

    [Fact]
    public async Task A_widget_removed_makes_a_version_naming_it_and_holding_the_others()
    {
        await using var h = await PublishedAsync();
        var keep = await AddWidgetAsync(h, "Keep", h.DashboardId, Layout(0, 0));
        var gone = await AddWidgetAsync(h, "Water <b>source</b>", h.DashboardId, Layout(6, 0));

        var removed = await h.Analyst.DeleteAsync(h.WidgetUrl(gone));
        var again = await h.Analyst.DeleteAsync(h.WidgetUrl(gone));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("""3 grace app [{"kind": "widgetRemoved", "widget": "Water <b>source</b>"}]""", (await VersionsAsync(h, h.DashboardId))[^1]);
        Assert.Equal(new[] { 1, 2, 3 }, await NumbersAsync(h, h.DashboardId));
        Assert.Equal(["Keep"], Titles(await ContentAsync(h, h.DashboardId, 3)));
        Assert.Equal(keep, (await h.GetWidgetsAsync()).EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_refused_change_such_as_a_name_that_is_taken_records_nothing()
    {
        await using var h = await PublishedAsync();
        var health = await h.AddDashboardAsync("Health");
        await h.AddDashboardAsync("Field");
        var versions = await AllVersionsAsync(h);

        var rename = await h.Analyst.PatchJsonAsync(h.DashboardUrl(health), new { name = "FIELD" });
        var add = await h.PostDashboardAsync("field");
        var widget = await h.PostWidgetAsync(new { title = "", chartType = "column", config = new { indicator = "coverage" } }, dashboardId: health);
        var viewer = await h.PostWidgetAsync(Column("Coverage"), h.Viewer, health);

        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, widget.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    // Changes that are not dashboard changes (dashboard-versions)

    [Fact]
    public async Task Dashboard_moved_makes_no_version_of_either_dashboard()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var housing = await h.AddDashboardAsync("Housing");
        var health = await h.AddDashboardAsync("Health");
        var versions = await AllVersionsAsync(h);

        var response = await h.Analyst.PutAsJsonAsync(h.DashboardsUrl + "/order",
            new { expectedIds = new[] { h.DashboardId, housing, health }, dashboardIds = new[] { h.DashboardId, health, housing } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    [Fact]
    public async Task Scheduled_email_saved_makes_no_version()
    {
        await using var reports = await ReportsHarness.CreateAsync(postgres);
        var h = reports.Indicators;
        await reports.AddStatAsync();
        var versions = await VersionsAsync(h, h.DashboardId);

        var response = await reports.PutReportAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(versions);
        Assert.Equal(versions, await VersionsAsync(h, h.DashboardId));
    }

    [Fact]
    public async Task Run_publishes_new_data_and_the_widgets_show_it_with_no_new_version()
    {
        await using var h = await PublishedAsync();
        await AddWidgetAsync(h, "Coverage", h.DashboardId);
        var versions = await AllVersionsAsync(h);

        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 50, 5, 10), ('Palmira', 70, 7, 10), ('Buga', 10, 1, 10)");
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "clean", ("households", "municipality text", "('Cali')"));

        Assert.Equal(["Buga|10", "Cali|50", "Palmira|70"], IndicatorsHarness.Rows(await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality" }, "column")).Order().ToArray());
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    [Fact]
    public async Task Filter_values_picked_by_a_member_make_no_version()
    {
        await using var h = await PublishedAsync();
        await h.SaveFiltersAsync(new[] { new { label = "Municipality", column = "municipality", kind = "text" } });
        await AddWidgetAsync(h, "Coverage", h.DashboardId);
        var versions = await AllVersionsAsync(h);

        var values = await h.Viewer.GetAsync(h.DashboardUrl() + "/values?column=municipality&search=Cal");
        var query = await h.PostQueryAsync(new { indicator = "coverage", groupBy = "municipality" }, "column", h.Viewer,
            new[] { new { column = "municipality", kind = "text", values = new[] { "Cali" } } });

        Assert.Equal(HttpStatusCode.OK, values.StatusCode);
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    // Versions kept (dashboard-versions)

    [Fact]
    public async Task Eleventh_version_deletes_the_oldest_and_the_next_number_is_the_newest_plus_one()
    {
        await using var h = await PublishedAsync();
        var housing = await h.InsertDashboardAsync("Housing");
        await SeedVersionsAsync(h, housing, 1, 10);

        await AddWidgetAsync(h, "Water source", housing);

        Assert.Equal(Enumerable.Range(2, 10).ToArray(), await NumbersAsync(h, housing));
        Assert.Equal("""11 grace app [{"kind": "widgetAdded", "widget": "Water source"}]""", (await VersionsAsync(h, housing))[^1]);

        await AddWidgetAsync(h, "Coverage", housing);

        Assert.Equal(Enumerable.Range(3, 10).ToArray(), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task Copy_has_only_version_1_saying_where_it_comes_from_and_the_original_keeps_its_versions()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var housing = await h.InsertDashboardAsync("Housing");
        await SeedVersionsAsync(h, housing, 3, 12);
        var original = await VersionsAsync(h, housing);

        var response = await h.Analyst.PostAsJsonAsync(h.DashboardUrl(housing) + "/copies", new { name = "Housing (copy)" });
        var copy = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["""1 grace app [{"kind": "duplicated", "dashboard": "Housing"}]"""], await VersionsAsync(h, copy));
        Assert.Equal("Housing (copy)", (await ContentAsync(h, copy, 1)).GetProperty("name").GetString());
        Assert.Equal(original, await VersionsAsync(h, housing));
        Assert.Equal(Enumerable.Range(3, 10).ToArray(), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task Removed_dashboard_takes_its_versions_with_it()
    {
        await using var h = await PublishedAsync();
        var housing = await h.AddDashboardAsync("Housing");
        await AddWidgetAsync(h, "Coverage", housing);
        await AddWidgetAsync(h, "Kept", h.DashboardId);
        var kept = await VersionsAsync(h, h.DashboardId);

        var response = await h.Analyst.DeleteAsync(h.DashboardUrl(housing));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await VersionsAsync(h, housing));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_versions WHERE dashboard_id = @id", ("id", housing)));
        Assert.Equal(kept, await VersionsAsync(h, h.DashboardId));
    }

    // The flush (design D3)

    [Fact]
    public async Task A_flush_that_finds_the_content_recorded_records_nothing_and_answers_the_current_number()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var housing = await h.AddDashboardAsync("Housing");
        var versions = await AllVersionsAsync(h);

        var numbers = await FlushAsync(h, DashboardVersionOrigin.App, null, (housing, Renamed("Housing")));

        Assert.Equal([new DashboardVersionNumber(housing, 1)], numbers);
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    [Fact]
    public async Task A_flush_answers_each_dashboard_noted_in_id_order_and_skips_one_removed_meanwhile()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var first = await h.InsertDashboardAsync("First");
        var second = await h.InsertDashboardAsync("Second");
        var gone = await h.InsertDashboardAsync("Gone");
        await h.RemoveDashboardAsync(gone);

        var numbers = await FlushAsync(h, DashboardVersionOrigin.App, null,
            (second, Renamed("Second")), (gone, Renamed("Gone")), (first, Renamed("First")), (second, new DashboardChange(DashboardChangeKinds.FiltersChanged)));

        Assert.Equal(new[] { first, second }.Order().Select(id => new DashboardVersionNumber(id, 1)).ToArray(), numbers);
        Assert.Equal(["""1 grace app [{"kind": "renamed", "dashboard": "Second"}, {"kind": "filtersChanged"}]"""], await VersionsAsync(h, second));
        Assert.Empty(await VersionsAsync(h, gone));
    }

    [Fact]
    public async Task A_flush_that_fails_leaves_the_widget_created_and_its_201_and_is_logged()
    {
        await using var h = await PublishedAsync(services =>
            services.ConfigureDbContext<TamizaDbContext>(options => options.AddInterceptors(new UnavailableVersions())));

        var response = await h.PostWidgetAsync(Column("Coverage"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["Coverage"], (await h.StoredWidgetsAsync()).Select(row => row.Split('|')[0]).ToArray());
        Assert.Empty(await VersionsAsync(h, h.DashboardId));
        Assert.Contains(h.Factory.Logs.Lines, line =>
            line.StartsWith("Error Tamiza.Api.Indicators.DashboardVersions:", StringComparison.Ordinal)
            && line.Contains($"A version of dashboard {h.DashboardId} of project {h.ProjectId} could not be recorded", StringComparison.Ordinal)
            && line.Contains(UnavailableVersions.Message, StringComparison.Ordinal));
    }

    /// <summary>A database that refuses every new dashboard version, as one that fails during the flush would.</summary>
    private sealed class UnavailableVersions : DbCommandInterceptor
    {
        public const string Message = "The dashboard versions are unavailable.";

        private static void Refuse(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO tamiza.dashboard_versions", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(Message);
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

    // One version per request of an AI client (design D4)

    [Fact]
    public async Task Writes_through_mcp_for_the_same_request_grow_one_version_keeping_its_number_start_author_and_note()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await RenameWithSqlAsync(h, h.DashboardId, "Water");
        await FlushAsync(h, DashboardVersionOrigin.Mcp, AddWaterWidgets, (h.DashboardId, Renamed("Water")));
        const string start = "SELECT format('%s %s %s', created_at, note, origin) FROM tamiza.dashboard_versions WHERE dashboard_id = @id";
        var started = await TestData.ScalarAsync<string>(h.Db, start, ("id", h.DashboardId));
        var savedAt = "SELECT saved_at FROM tamiza.dashboard_versions WHERE dashboard_id = @id";
        var firstSave = await TestData.ScalarAsync<DateTime>(h.Db, savedAt, ("id", h.DashboardId));
        await RenameWithSqlAsync(h, h.DashboardId, "Agua");

        var numbers = await FlushAsync(h, DashboardVersionOrigin.Mcp, "  add WATER widgets ", (h.DashboardId, Renamed("Agua")));

        Assert.Equal([new DashboardVersionNumber(h.DashboardId, 1)], numbers);
        Assert.Equal(
            ["""1 grace mcp [{"kind": "renamed", "dashboard": "Water"}, {"kind": "renamed", "dashboard": "Agua"}]"""],
            await VersionsAsync(h, h.DashboardId));
        Assert.Equal("Agua", (await ContentAsync(h, h.DashboardId, 1)).GetProperty("name").GetString());
        Assert.Equal(started, await TestData.ScalarAsync<string>(h.Db, start, ("id", h.DashboardId)));
        Assert.EndsWith($" {AddWaterWidgets} mcp", started);
        Assert.True(await TestData.ScalarAsync<DateTime>(h.Db, savedAt, ("id", h.DashboardId)) > firstSave);
    }

    [Fact]
    public async Task A_flush_that_finds_its_content_recorded_by_the_same_request_only_adds_its_entry()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await FlushAsync(h, DashboardVersionOrigin.Mcp, AddWaterWidgets, (h.DashboardId, new DashboardChange(DashboardChangeKinds.WidgetAdded, Widget: "Coverage")));
        var content = (await ContentAsync(h, h.DashboardId, 1)).GetRawText();

        // A second call of the request whose write the first flush already recorded.
        var numbers = await FlushAsync(h, DashboardVersionOrigin.Mcp, AddWaterWidgets, (h.DashboardId, new DashboardChange(DashboardChangeKinds.WidgetAdded, Widget: "Households")));
        var app = await FlushAsync(h, DashboardVersionOrigin.App, null, (h.DashboardId, new DashboardChange(DashboardChangeKinds.LayoutChanged)));

        Assert.Equal([new DashboardVersionNumber(h.DashboardId, 1)], numbers);
        Assert.Equal([new DashboardVersionNumber(h.DashboardId, 1)], app);
        Assert.Equal(
            ["""1 grace mcp [{"kind": "widgetAdded", "widget": "Coverage"}, {"kind": "widgetAdded", "widget": "Households"}]"""],
            await VersionsAsync(h, h.DashboardId));
        Assert.Equal(content, (await ContentAsync(h, h.DashboardId, 1)).GetRawText());
    }

    [Theory]
    [InlineData("""[{"kind": "initial"}]""", null)]
    [InlineData("""[{"kind": "unrecorded"}]""", null)]
    [InlineData("""[{"kind": "restored", "version": 1}]""", 1)]
    public async Task The_state_before_history_an_unrecorded_state_and_a_restore_never_grow(string changes, int? restoredFrom)
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var analyst = await AnalystIdAsync(h);
        // Everything else would let it grow: the same request of the same person through MCP, just now.
        await h.ExecuteAsync(
            """
            INSERT INTO tamiza.dashboard_versions (id, project_id, dashboard_id, number, content, changes, restored_from, origin, note, author_id,
                                                   created_at, saved_at)
            VALUES (gen_random_uuid(), @project, @dashboard, 2, '{}', @changes::jsonb, @restoredFrom, 'mcp', @note, @analyst, now(), now())
            """,
            ("project", h.ProjectId), ("dashboard", h.DashboardId), ("changes", changes), ("restoredFrom", (object?)restoredFrom ?? DBNull.Value),
            ("note", AddWaterWidgets), ("analyst", analyst));

        var numbers = await FlushAsync(h, DashboardVersionOrigin.Mcp, AddWaterWidgets, (h.DashboardId, Renamed("Indicators")));

        Assert.Equal([new DashboardVersionNumber(h.DashboardId, 3)], numbers);
        Assert.Equal(new[] { 2, 3 }, await NumbersAsync(h, h.DashboardId));
    }

    // History (design D9)

    private static string VersionsUrl(IndicatorsHarness h, Guid dashboardId) => h.DashboardUrl(dashboardId) + "/versions";

    private static Task<HttpResponseMessage> RestoreAsync(IndicatorsHarness h, Guid dashboardId, int number, HttpClient? client = null) =>
        (client ?? h.Analyst).PostAsync($"{VersionsUrl(h, dashboardId)}/{number}/restore", null);

    /// <summary>Restores a version as the analyst and returns the answer; fails the test when the restore is not created.</summary>
    private static async Task<JsonElement> RestoredAsync(IndicatorsHarness h, Guid dashboardId, int number)
    {
        var response = await RestoreAsync(h, dashboardId, number);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Restores a version as the analyst through the restore an AI client uses, with its request and the current version it read, and
    /// returns what it restored; throws <see cref="DashboardChangedException"/> as the guard refuses.
    /// </summary>
    private static async Task<DashboardRestore> RestoreThroughMcpAsync(IndicatorsHarness h, Guid dashboardId, int number, string change, int baseVersion)
    {
        var analyst = await AnalystIdAsync(h);
        using var scope = h.Factory.Services.CreateScope();
        var versions = scope.ServiceProvider.GetRequiredService<DashboardVersions>();
        var result = await versions.RestoreAsync(h.ProjectId, dashboardId, number, DashboardRestoreWrite.Mcp(change, baseVersion), analyst, CancellationToken.None);
        return Assert.IsType<Created<DashboardRestore>>(result.Result).Value!;
    }

    private static async Task<JsonElement> HistoryAsync(IndicatorsHarness h, Guid dashboardId)
    {
        var response = await h.Analyst.GetAsync(VersionsUrl(h, dashboardId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task RenameAsync(IndicatorsHarness h, Guid dashboardId, string name) =>
        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.PatchJsonAsync(h.DashboardUrl(dashboardId), new { name })).StatusCode);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.TryGetProperty("code", out var value) ? value.GetString() : null);
    }

    /// <summary>Whether two versions of a dashboard hold the same content, as PostgreSQL compares them.</summary>
    private static async Task<bool> SameContentAsync(IndicatorsHarness h, Guid dashboardId, int first, int second) =>
        await TestData.ScalarAsync<bool>(h.Db,
            """
            SELECT a.content = b.content FROM tamiza.dashboard_versions AS a, tamiza.dashboard_versions AS b
            WHERE a.dashboard_id = @dashboard AND a.number = @first AND b.dashboard_id = @dashboard AND b.number = @second
            """,
            ("dashboard", dashboardId), ("first", first), ("second", second));

    private static Guid[] Ids(JsonElement widgets) => widgets.EnumerateArray().Select(w => w.GetProperty("id").GetGuid()).ToArray();

    private static Task<DateTime> SavedAtAsync(IndicatorsHarness h, Guid dashboardId, int number) =>
        TestData.ScalarAsync<DateTime>(h.Db, "SELECT saved_at FROM tamiza.dashboard_versions WHERE dashboard_id = @dashboard AND number = @number",
            ("dashboard", dashboardId), ("number", number));

    [Fact]
    public async Task History_listed_newest_first_with_the_current_version_marked_and_what_changed_its_author_and_time()
    {
        await using var h = await PublishedAsync();
        var housing = await h.AddDashboardAsync("Housing");
        await h.SaveFiltersAsync(new[] { new { label = "Municipality", column = "municipality", kind = "text" } }, dashboardId: housing);
        await AddWidgetAsync(h, "Water source", housing);
        foreach (var n in Enumerable.Range(4, 8))
        {
            await RenameAsync(h, housing, $"Housing {n}");
        }

        // Version 12 is made by an AI client for the analyst's request.
        await RenameWithSqlAsync(h, housing, "Housing 12");
        await FlushAsync(h, DashboardVersionOrigin.Mcp, "Rename it", (housing, Renamed("Housing 12")));

        var history = await HistoryAsync(h, housing);

        var versions = history.GetProperty("versions").EnumerateArray().ToArray();
        Assert.Equal(12, history.GetProperty("current").GetInt32());
        Assert.Equal(Enumerable.Range(3, 10).Reverse(), versions.Select(v => v.GetProperty("number").GetInt32()));
        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(n => n == 12), versions.Select(v => v.GetProperty("current").GetBoolean()));
        var (newest, oldest) = (versions[0], versions[^1]);
        Assert.Equal(("Housing 12", 1, 1), (newest.GetProperty("name").GetString(), newest.GetProperty("widgetCount").GetInt32(), newest.GetProperty("filterCount").GetInt32()));
        Assert.Equal("""[{"kind":"renamed","dashboard":"Housing 12"}]""", newest.GetProperty("changes").GetRawText());
        Assert.Equal(("mcp", "Rename it", 0), (newest.GetProperty("origin").GetString(), newest.GetProperty("note").GetString(), newest.GetProperty("moreChanges").GetInt32()));
        Assert.Equal(JsonValueKind.Null, newest.GetProperty("restoredFrom").ValueKind);
        Assert.Equal(("Housing", 1, 1), (oldest.GetProperty("name").GetString(), oldest.GetProperty("widgetCount").GetInt32(), oldest.GetProperty("filterCount").GetInt32()));
        Assert.Equal("""[{"kind":"widgetAdded","widget":"Water source"}]""", oldest.GetProperty("changes").GetRawText());
        Assert.Equal(("app", JsonValueKind.Null), (oldest.GetProperty("origin").GetString(), oldest.GetProperty("note").ValueKind));
        var analyst = await AnalystIdAsync(h);
        Assert.All(versions, v => Assert.Equal(
            $$"""{"id":"{{analyst}}","name":"grace"}""", v.GetProperty("author").GetRawText()));
        // The time each shows is when its last change was saved.
        Assert.Equal(await SavedAtAsync(h, housing, 12), newest.GetProperty("savedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(await SavedAtAsync(h, housing, 3), oldest.GetProperty("savedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(oldest.GetProperty("savedAt").GetDateTimeOffset(), oldest.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_dashboard_answers_the_number_of_its_current_version_and_null_while_it_has_none()
    {
        await using var h = await PublishedAsync();
        var before = await h.GetDashboardAsync();
        var history = await HistoryAsync(h, h.DashboardId);

        await AddWidgetAsync(h, "Coverage", h.DashboardId);
        await AddWidgetAsync(h, "Households", h.DashboardId);

        // The seeded dashboard has no version until its first change, as one whose flush failed (design D5).
        Assert.Equal(JsonValueKind.Null, before.GetProperty("version").ValueKind);
        Assert.Equal("""{"current":0,"versions":[]}""", history.GetRawText());
        Assert.Equal(2, (await h.GetDashboardAsync(client: h.Viewer)).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Version_no_longer_kept_is_404_version_not_found_for_a_read_and_a_restore_and_the_dashboard_is_unchanged()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var housing = await h.InsertDashboardAsync("Housing");
        await SeedVersionsAsync(h, housing, 2, 11);
        var dashboards = await h.StoredDashboardsAsync();
        var versions = await AllVersionsAsync(h);

        await AssertProblemAsync(await h.Analyst.GetAsync(VersionsUrl(h, housing) + "/1"), HttpStatusCode.NotFound, DashboardEndpoints.VersionNotFoundCode);
        await AssertProblemAsync(await h.Analyst.GetAsync(VersionsUrl(h, housing) + "/12"), HttpStatusCode.NotFound, DashboardEndpoints.VersionNotFoundCode);
        await AssertProblemAsync(await RestoreAsync(h, housing, 1), HttpStatusCode.NotFound, DashboardEndpoints.VersionNotFoundCode);

        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await AllVersionsAsync(h));

        // A dashboard the project does not have answers as every route of a dashboard does, whatever version it names.
        var (_, other) = await h.AddOtherProjectAsync();
        await h.RemoveDashboardAsync(housing);
        foreach (var url in new[] { VersionsUrl(h, housing), VersionsUrl(h, housing) + "/5", VersionsUrl(h, other), VersionsUrl(h, other) + "/1" })
        {
            await AssertProblemAsync(await h.Admin.GetAsync(url), HttpStatusCode.NotFound, DashboardEndpoints.NotFoundCode);
        }
    }

    [Fact]
    public async Task The_details_hold_the_versions_name_filters_and_widgets_with_each_config_read_as_the_dashboard_reads_it()
    {
        await using var h = await PublishedAsync();
        await h.SaveFiltersAsync(new[] { new { label = "Municipality", column = "municipality", kind = "text", @default = new { values = new[] { "Cali" } } } });
        var kept = await AddWidgetAsync(h, "Coverage by municipality", h.DashboardId, Layout(0, 0));
        var gone = await AddWidgetAsync(h, "Water source", h.DashboardId, Layout(6, 0));
        // Stored before the series settings existed, and recorded as such.
        await h.ExecuteAsync("""UPDATE tamiza.dashboard_widgets SET config = '{"indicator": "coverage", "groupBy": "municipality"}' WHERE id = @id""", ("id", kept));
        await FlushAsync(h, DashboardVersionOrigin.App, null, (h.DashboardId, new DashboardChange(DashboardChangeKinds.WidgetChanged, Widget: "Coverage by municipality")));
        var dashboard = await h.GetDashboardAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(gone))).StatusCode);
        var versions = await AllVersionsAsync(h);

        var response = await h.Analyst.GetAsync(VersionsUrl(h, h.DashboardId) + "/4");
        var details = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((4, false, "Indicators"), (details.GetProperty("number").GetInt32(), details.GetProperty("current").GetBoolean(), details.GetProperty("name").GetString()));
        Assert.Equal(dashboard.GetProperty("filters").GetRawText(), details.GetProperty("filters").GetRawText());
        // The widget removed since is there, each one as GET .../dashboards/{id} answered it then: a config without series reads with their defaults.
        var held = details.GetProperty("widgets").EnumerateArray().ToArray();
        var shown = dashboard.GetProperty("widgets").EnumerateArray().ToArray();
        Assert.Equal([kept, gone], held.Select(w => w.GetProperty("id").GetGuid()));
        foreach (var (widget, then) in held.Zip(shown))
        {
            foreach (var property in new[] { "id", "title", "chartType", "config", "layout" })
            {
                Assert.Equal(then.GetProperty(property).GetRawText(), widget.GetProperty(property).GetRawText());
            }
        }

        Assert.Equal(JsonValueKind.Null, held[0].GetProperty("config").GetProperty("seriesBy").ValueKind);
        Assert.Equal("""[{"kind":"widgetChanged","widget":"Coverage by municipality"}]""", details.GetProperty("changes").GetRawText());
        Assert.Equal("grace", details.GetProperty("author").GetProperty("name").GetString());
        // Reading a version changes nothing.
        Assert.Equal([kept], Ids((await h.GetDashboardAsync()).GetProperty("widgets")));
        Assert.Equal(versions, await AllVersionsAsync(h));
        Assert.True((await h.Analyst.GetFromJsonAsync<JsonElement>(VersionsUrl(h, h.DashboardId) + "/5")).GetProperty("current").GetBoolean());
    }

    // Restoring a version (design D7)

    [Fact]
    public async Task Removed_widget_brought_back_by_restoring_version_7_as_version_9_and_the_restore_undone_as_version_10()
    {
        await using var h = await PublishedAsync();
        var housing = await h.InsertDashboardAsync("Housing");
        await SeedVersionsAsync(h, housing, 1, 5);
        await AddWidgetAsync(h, "Coverage", housing, Layout(0, 0));
        var water = await AddWidgetAsync(h, "Water source", housing, Layout(6, 0));
        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(water, housing))).StatusCode);
        Assert.Equal(Enumerable.Range(1, 8), await NumbersAsync(h, housing));

        var response = await RestoreAsync(h, housing, 7);
        var restore = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{VersionsUrl(h, housing)}/9", response.Headers.Location!.OriginalString);
        Assert.Equal((true, false, 0), (restore.GetProperty("restored").GetBoolean(), restore.GetProperty("nameKept").GetBoolean(), restore.GetProperty("copies").GetArrayLength()));
        var version = restore.GetProperty("version");
        Assert.Equal((9, true, 7), (version.GetProperty("number").GetInt32(), version.GetProperty("current").GetBoolean(), version.GetProperty("restoredFrom").GetInt32()));
        Assert.Equal("""[{"kind":"restored","version":7}]""", version.GetProperty("changes").GetRawText());
        Assert.Equal(("app", "grace"), (version.GetProperty("origin").GetString(), version.GetProperty("author").GetProperty("name").GetString()));
        var dashboard = restore.GetProperty("dashboard");
        Assert.Equal((housing, "Housing", 9), (dashboard.GetProperty("id").GetGuid(), dashboard.GetProperty("name").GetString(), dashboard.GetProperty("version").GetInt32()));
        Assert.Equal(["Coverage", "Water source"], Titles(dashboard));
        Assert.Equal(water, Ids(dashboard.GetProperty("widgets"))[1]); // with its own id
        Assert.Equal((await h.GetDashboardAsync(housing)).GetRawText(), dashboard.GetRawText());
        Assert.True(await SameContentAsync(h, housing, 7, 9));
        Assert.Equal("""9 grace app [{"kind": "restored", "version": 7}]""", (await VersionsAsync(h, housing))[^1]);

        // Restore undone.
        var undone = await RestoredAsync(h, housing, 8);

        Assert.Equal(10, undone.GetProperty("version").GetProperty("number").GetInt32());
        Assert.Equal(["Coverage"], Titles(undone.GetProperty("dashboard")));
        Assert.True(await SameContentAsync(h, housing, 8, 10));
        Assert.Equal(Enumerable.Range(1, 10), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task Numbers_never_reused_the_restore_of_version_9_after_versions_3_to_12_is_version_13()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var housing = await h.AddDashboardAsync("Housing");
        foreach (var n in Enumerable.Range(2, 11))
        {
            await RenameAsync(h, housing, $"Housing {n}");
        }

        Assert.Equal(Enumerable.Range(3, 10), await NumbersAsync(h, housing));

        var restore = await RestoredAsync(h, housing, 9);

        Assert.Equal(13, restore.GetProperty("version").GetProperty("number").GetInt32());
        Assert.Equal("Housing 9", restore.GetProperty("dashboard").GetProperty("name").GetString());
        Assert.Equal(Enumerable.Range(4, 10), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task Scheduled_email_kept_with_its_recipients_days_and_time_and_the_dashboard_its_place_when_an_older_version_is_restored()
    {
        await using var reports = await ReportsHarness.CreateAsync(postgres);
        var h = reports.Indicators;
        await reports.AddStatAsync();
        await reports.AddStatAsync();
        await reports.SaveReportAsync(days: [1, 3, 5], time: "08:30");
        await h.AddDashboardAsync("Health");
        const string report = "SELECT row_to_json(r)::text FROM tamiza.dashboard_reports AS r WHERE dashboard_id = @dashboard";
        var email = await reports.ScalarAsync<string>(report);
        var places = (await h.DashboardsAsync()).Select(d => (d.Id, d.Position)).ToArray();

        var restore = await RestoredAsync(h, h.DashboardId, 1);

        Assert.Equal(1, restore.GetProperty("dashboard").GetProperty("widgets").GetArrayLength());
        Assert.NotNull(email);
        Assert.Equal(email, await reports.ScalarAsync<string>(report));
        Assert.Equal(places, (await h.DashboardsAsync()).Select(d => (d.Id, d.Position)).ToArray());
    }

    [Fact]
    public async Task Name_taken_keeps_the_current_name_restores_the_widgets_and_filters_and_restoring_it_again_changes_nothing()
    {
        await using var h = await PublishedAsync();
        var water = await h.AddDashboardAsync("Water");
        await h.SaveFiltersAsync(new[] { new { label = "Municipality", column = "municipality", kind = "text" } }, dashboardId: water);
        var coverage = await AddWidgetAsync(h, "Coverage", water);
        await RenameAsync(h, water, "Agua");
        await h.SaveFiltersAsync(Array.Empty<object>(), dashboardId: water);
        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(coverage, water))).StatusCode);
        // Another dashboard now has the name version 3 holds, ignoring case.
        await h.AddDashboardAsync("WATER");

        var restore = await RestoredAsync(h, water, 3);

        var dashboard = restore.GetProperty("dashboard");
        Assert.True(restore.GetProperty("nameKept").GetBoolean());
        Assert.Equal("Agua", dashboard.GetProperty("name").GetString());
        Assert.Equal(["Municipality"], dashboard.GetProperty("filters").EnumerateArray().Select(f => f.GetProperty("label").GetString()));
        Assert.Equal(["Coverage"], Titles(dashboard));
        Assert.Equal("Agua", (await ContentAsync(h, water, 7)).GetProperty("name").GetString());
        var dashboards = await h.StoredDashboardsAsync();
        var widgets = await h.StoredWidgetsAsync();
        var versions = await AllVersionsAsync(h);

        // The version's content with the name kept is the dashboard as it is.
        var again = await RestoreAsync(h, water, 3);
        var answer = await again.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal((false, true), (answer.GetProperty("restored").GetBoolean(), answer.GetProperty("nameKept").GetBoolean()));
        Assert.Equal(7, answer.GetProperty("version").GetProperty("number").GetInt32());
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    [Fact]
    public async Task A_rename_of_another_dashboard_to_the_versions_name_between_the_check_and_the_save_keeps_the_current_name_and_restores_the_rest()
    {
        var rename = new RenameBeforeSave();
        await using var h = await PublishedAsync(services =>
            services.ConfigureDbContext<TamizaDbContext>(options => options.AddInterceptors(rename)));
        var water = await h.AddDashboardAsync("Water");
        var coverage = await AddWidgetAsync(h, "Coverage", water);
        await RenameAsync(h, water, "Agua");
        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(coverage, water))).StatusCode);
        var health = await h.AddDashboardAsync("Health");
        rename.Arm(h.Db, health, "Water");

        var restore = await RestoredAsync(h, water, 2);

        Assert.Equal(1, rename.Runs);
        Assert.True(restore.GetProperty("nameKept").GetBoolean());
        Assert.Equal("Agua", restore.GetProperty("dashboard").GetProperty("name").GetString());
        Assert.Equal([coverage], Ids(restore.GetProperty("dashboard").GetProperty("widgets")));
        Assert.Contains((await h.DashboardsAsync()), d => d.Id == health && d.Name == "Water");
        Assert.Equal("""5 grace app [{"kind": "restored", "version": 2}]""", (await VersionsAsync(h, water))[^1]);
        Assert.Equal(Enumerable.Range(1, 5), await NumbersAsync(h, water));
        Assert.Equal("Agua", (await ContentAsync(h, water, 5)).GetProperty("name").GetString());
    }

    /// <summary>
    /// Another member's rename, which takes no lock, committing between the restore's check of the name and its save: once armed, it
    /// renames a dashboard on a connection of its own just before the first save that renames one, once.
    /// </summary>
    private sealed class RenameBeforeSave : DbCommandInterceptor
    {
        private (string Db, Guid DashboardId, string Name)? _armed;

        public int Runs { get; private set; }

        public void Arm(string db, Guid dashboardId, string name) => _armed = (db, dashboardId, name);

        private async Task RunAsync(DbCommand command)
        {
            if (_armed is { } armed
                && command.CommandText.Contains("UPDATE tamiza.dashboards SET", StringComparison.Ordinal)
                && command.CommandText.Contains("normalized_name", StringComparison.Ordinal))
            {
                _armed = null;
                Runs++;
                await TestData.ExecuteAsync(armed.Db, "UPDATE tamiza.dashboards SET name = @name, normalized_name = lower(@name) WHERE id = @id",
                    ("id", armed.DashboardId), ("name", armed.Name));
            }
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await RunAsync(command);
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await RunAsync(command);
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Widget_since_moved_to_another_dashboard_comes_back_as_a_copy_with_a_new_id_and_the_other_dashboard_is_unchanged()
    {
        await using var h = await PublishedAsync();
        var health = await h.AddDashboardAsync("Health");
        var field = await h.AddDashboardAsync("Field");
        var coverage = await AddWidgetAsync(h, "Coverage", health);
        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.PatchJsonAsync(h.WidgetUrl(coverage, health), new { dashboardId = field })).StatusCode);
        var onField = (await h.StoredWidgetsAsync()).Single();
        var fieldDashboard = (await h.GetDashboardAsync(field)).GetRawText();
        var fieldVersions = await VersionsAsync(h, field);

        var restore = await RestoredAsync(h, health, 2);

        var copy = restore.GetProperty("copies").EnumerateArray().Single();
        var copyId = copy.GetProperty("copyId").GetGuid();
        Assert.Equal((coverage, "Coverage", field, "Field"), (copy.GetProperty("originalId").GetGuid(), copy.GetProperty("title").GetString(),
            copy.GetProperty("dashboardId").GetGuid(), copy.GetProperty("dashboardName").GetString()));
        Assert.NotEqual(coverage, copyId);
        Assert.Equal(7, copyId.Version);
        Assert.Equal([copyId], Ids(restore.GetProperty("dashboard").GetProperty("widgets")));
        Assert.Equal("Coverage", Titles(restore.GetProperty("dashboard")).Single());
        // "Field" still holds the widget, as it was, and has no new version.
        Assert.Equal(fieldDashboard, (await h.GetDashboardAsync(field)).GetRawText());
        Assert.Contains(onField, await h.StoredWidgetsAsync());
        Assert.Equal(fieldVersions, await VersionsAsync(h, field));
        // The new version holds the copy; the version restored still holds the original.
        Assert.Equal(copyId, (await ContentAsync(h, health, 4)).GetProperty("widgets")[0].GetProperty("id").GetGuid());
        Assert.Equal(coverage, (await ContentAsync(h, health, 2)).GetProperty("widgets")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Indicator_gone_the_widget_is_restored_as_it_was_and_its_query_says_its_indicator_is_not_published()
    {
        await using var h = await PublishedAsync();
        await h.PublishAsync("retired", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)");
        var id = (await h.AddWidgetAsync(new { title = "Retired", chartType = "column", config = new { indicator = "retired", groupBy = "municipality" } }))
            .GetProperty("id").GetGuid();
        var saved = (await h.GetWidgetsAsync())[0];
        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(id))).StatusCode);
        // The indicator is removed since, and the next run dropped its table.
        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.DeleteAsync($"/api/v1/projects/{h.ProjectId}/scripts/indicators/retired")).StatusCode);
        await h.ExecuteAsync($"""DROP TABLE "{h.Schema}".retired""");

        var restore = await RestoredAsync(h, h.DashboardId, 1);

        var widget = restore.GetProperty("dashboard").GetProperty("widgets").EnumerateArray().Single();
        Assert.Equal((id, "Retired"), (widget.GetProperty("id").GetGuid(), widget.GetProperty("title").GetString()));
        Assert.Equal(saved.GetProperty("config").GetRawText(), widget.GetProperty("config").GetRawText());
        Assert.False((await h.QueryAsync(widget.GetProperty("config"), "column")).GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task Limit_lowered_a_version_of_30_widgets_is_refused_naming_the_limit_of_20_and_the_dashboard_is_unchanged()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "20" });
        // 30 widgets saved before an operator lowered the limit to 20, recorded as version 1.
        await h.ExecuteAsync(
            """
            INSERT INTO tamiza.dashboard_widgets (project_id, dashboard_id, chart_type, title, config, layout, created_by, updated_by)
            SELECT d.project_id, d.id, 'stat', 'Widget ' || n, '{"indicator": "coverage"}', jsonb_build_object('x', 0, 'y', 2 * n, 'w', 3, 'h', 2),
                   d.created_by, d.created_by
            FROM tamiza.dashboards AS d, generate_series(1, 30) AS n
            WHERE d.id = @id
            """,
            ("id", h.DashboardId));
        await FlushAsync(h, DashboardVersionOrigin.App, null, (h.DashboardId, new DashboardChange(DashboardChangeKinds.WidgetAdded, Widget: "Widget 30")));
        var last = await TestData.ScalarAsync<Guid>(h.Db, "SELECT id FROM tamiza.dashboard_widgets WHERE title = 'Widget 30'");
        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(last))).StatusCode);
        var dashboards = await h.StoredDashboardsAsync();
        var widgets = await h.StoredWidgetsAsync();
        var versions = await AllVersionsAsync(h);

        var response = await RestoreAsync(h, h.DashboardId, 1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A dashboard can have at most 20 widgets. Remove one or move it to another dashboard first."],
            IndicatorsHarness.Errors(await response.Content.ReadFromJsonAsync<JsonElement>(), "widgets"));
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    // Restoring while others change (dashboard-versions)

    [Fact]
    public async Task Changed_meanwhile_the_restore_replaces_the_dashboard_and_the_other_analysts_version_stays_in_the_history()
    {
        await using var h = await PublishedAsync();
        var housing = await h.AddDashboardAsync("Housing");
        await AddWidgetAsync(h, "Coverage", housing);
        var listed = await HistoryAsync(h, housing);
        // Another member adds a widget after the history was opened.
        Assert.Equal(HttpStatusCode.Created, (await h.PostWidgetAsync(Column("Households"), h.Admin, housing)).StatusCode);

        var restore = await RestoredAsync(h, housing, 2);

        Assert.Equal(2, listed.GetProperty("current").GetInt32());
        Assert.Equal(["Coverage"], Titles(restore.GetProperty("dashboard")));
        Assert.Equal(
            [
                """1 grace app [{"kind": "created"}]""",
                """2 grace app [{"kind": "widgetAdded", "widget": "Coverage"}]""",
                """3 ada app [{"kind": "widgetAdded", "widget": "Households"}]""",
                """4 grace app [{"kind": "restored", "version": 2}]""",
            ],
            await VersionsAsync(h, housing));
        Assert.Equal(["Coverage", "Households"], Titles(await ContentAsync(h, housing, 3)));
    }

    [Fact]
    public async Task Edit_saved_as_the_restore_starts_is_recorded_as_a_version_of_its_editor_before_the_restore()
    {
        await using var h = await PublishedAsync();
        var housing = await h.AddDashboardAsync("Housing");
        var id = await AddWidgetAsync(h, "Coverage", housing);
        // Another member's title, committed just before the restore takes the lock; its flush is still to come.
        await h.ExecuteAsync(
            """
            UPDATE tamiza.dashboard_widgets SET title = 'Coverage.', updated_by = (SELECT id FROM tamiza.users WHERE keycloak_sub = 'ada'),
                                                updated_at = now() + interval '1 second'
            WHERE id = @id
            """,
            ("id", id));
        var edited = await TestData.ScalarAsync<DateTime>(h.Db, "SELECT updated_at FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id));

        var restore = await RestoredAsync(h, housing, 1);

        Assert.Equal(4, restore.GetProperty("version").GetProperty("number").GetInt32());
        Assert.Equal(
            [
                """3 ada app [{"kind": "unrecorded"}]""",
                """4 grace app [{"kind": "restored", "version": 1}]""",
            ],
            (await VersionsAsync(h, housing))[2..]);
        Assert.Equal(["Coverage."], Titles(await ContentAsync(h, housing, 3)));
        Assert.Equal("t t", await TestData.ScalarAsync<string>(h.Db,
            "SELECT format('%s %s', created_at = @edited, saved_at = @edited) FROM tamiza.dashboard_versions WHERE dashboard_id = @id AND number = 3",
            ("id", housing), ("edited", edited)));
        // That member's flush then finds the restored dashboard recorded, and adds nothing.
        await FlushAsync(h, DashboardVersionOrigin.App, null, (housing, new DashboardChange(DashboardChangeKinds.WidgetChanged, Widget: "Coverage.")));
        Assert.Equal(Enumerable.Range(1, 4), await NumbersAsync(h, housing));
    }

    [Fact]
    public async Task Version_deleted_meanwhile_by_ten_other_changes_is_404_version_not_found_and_the_dashboard_is_unchanged()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var housing = await h.AddDashboardAsync("Housing");
        var oldest = (await HistoryAsync(h, housing)).GetProperty("versions")[0].GetProperty("number").GetInt32();
        foreach (var n in Enumerable.Range(2, 10))
        {
            await RenameAsync(h, housing, $"Housing {n}");
        }

        var dashboards = await h.StoredDashboardsAsync();
        var versions = await AllVersionsAsync(h);

        await AssertProblemAsync(await RestoreAsync(h, housing, oldest), HttpStatusCode.NotFound, DashboardEndpoints.VersionNotFoundCode);

        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await AllVersionsAsync(h));
        Assert.Equal(Enumerable.Range(2, 10).Reverse(),
            (await HistoryAsync(h, housing)).GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
    }

    [Fact]
    public async Task Dashboard_removed_meanwhile_is_404_dashboards_not_found_and_nothing_is_restored()
    {
        await using var h = await PublishedAsync();
        var housing = await h.AddDashboardAsync("Housing");
        await AddWidgetAsync(h, "Coverage", housing);
        Assert.Equal(HttpStatusCode.NoContent, (await h.Admin.DeleteAsync(h.DashboardUrl(housing))).StatusCode);
        var dashboards = await h.StoredDashboardsAsync();
        var widgets = await h.StoredWidgetsAsync();
        var versions = await AllVersionsAsync(h);

        await AssertProblemAsync(await RestoreAsync(h, housing, 1), HttpStatusCode.NotFound, DashboardEndpoints.NotFoundCode);

        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    [Fact]
    public async Task Version_equal_to_the_current_one_answers_200_with_the_current_version_and_writes_nothing()
    {
        await using var h = await PublishedAsync();
        var housing = await h.InsertDashboardAsync("Housing");
        await SeedVersionsAsync(h, housing, 1, 7);
        await RenameAsync(h, housing, "Casas");
        await RenameAsync(h, housing, "Housing");
        await RenameAsync(h, housing, "Casas");
        var dashboards = await h.StoredDashboardsAsync();
        var versions = await AllVersionsAsync(h);

        var response = await RestoreAsync(h, housing, 8);
        var restore = await response.Content.ReadFromJsonAsync<JsonElement>();
        var current = await RestoreAsync(h, housing, 10);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal((false, false, 0), (restore.GetProperty("restored").GetBoolean(), restore.GetProperty("nameKept").GetBoolean(), restore.GetProperty("copies").GetArrayLength()));
        Assert.Equal((10, true), (restore.GetProperty("version").GetProperty("number").GetInt32(), restore.GetProperty("version").GetProperty("current").GetBoolean()));
        var dashboard = restore.GetProperty("dashboard");
        Assert.Equal((housing, "Casas", 10), (dashboard.GetProperty("id").GetGuid(), dashboard.GetProperty("name").GetString(), dashboard.GetProperty("version").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    [Fact]
    public async Task First_change_after_the_upgrade_is_version_2_and_restoring_version_1_brings_the_widget_back()
    {
        await using var h = await PublishedAsync();
        var housing = await h.InsertDashboardAsync("Housing");
        // Two widgets saved before version history began, and version 1 as the migration records it (design D1).
        await h.ExecuteAsync(
            """
            INSERT INTO tamiza.dashboard_widgets (project_id, dashboard_id, chart_type, title, config, layout, created_by, updated_by)
            SELECT d.project_id, d.id, 'column', w.title, '{"indicator": "coverage", "groupBy": "municipality"}', w.layout::jsonb, d.created_by, d.created_by
            FROM tamiza.dashboards AS d,
                 (VALUES ('Coverage', '{"x": 0, "y": 0, "w": 6, "h": 4}'), ('Water source', '{"x": 6, "y": 0, "w": 6, "h": 4}')) AS w (title, layout)
            WHERE d.id = @id;

            INSERT INTO tamiza.dashboard_versions (id, project_id, dashboard_id, number, content, changes, origin, author_id, created_at, saved_at)
            SELECT gen_random_uuid(), d.project_id, d.id, 1,
                   jsonb_build_object(
                       'name', d.name,
                       'filters', d.filters,
                       'widgets', COALESCE((
                           SELECT jsonb_agg(jsonb_build_object(
                                      'id', w.id, 'title', w.title, 'chartType', w.chart_type, 'config', w.config, 'layout', w.layout)
                                  ORDER BY (w.layout ->> 'y')::integer, (w.layout ->> 'x')::integer, w.created_at, w.id)
                           FROM tamiza.dashboard_widgets w
                           WHERE w.dashboard_id = d.id), '[]'::jsonb)),
                   '[{"kind": "initial"}]'::jsonb,
                   'app',
                   last_change.updated_by, last_change.updated_at, last_change.updated_at
            FROM tamiza.dashboards d
            CROSS JOIN LATERAL (
                SELECT edits.updated_by, edits.updated_at
                FROM (
                    SELECT d.updated_by, d.updated_at
                    UNION ALL
                    SELECT w.updated_by, w.updated_at FROM tamiza.dashboard_widgets w WHERE w.dashboard_id = d.id
                ) AS edits
                ORDER BY edits.updated_at DESC
                LIMIT 1
            ) AS last_change
            WHERE d.id = @id;
            """,
            ("id", housing));
        var water = await TestData.ScalarAsync<Guid>(h.Db, "SELECT id FROM tamiza.dashboard_widgets WHERE title = 'Water source'");

        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(water, housing))).StatusCode);
        var restore = await RestoredAsync(h, housing, 1);

        Assert.Equal(
            [
                """1 ada app [{"kind": "initial"}]""",
                """2 grace app [{"kind": "widgetRemoved", "widget": "Water source"}]""",
                """3 grace app [{"kind": "restored", "version": 1}]""",
            ],
            await VersionsAsync(h, housing));
        Assert.Equal(["Coverage", "Water source"], Titles(restore.GetProperty("dashboard")));
        Assert.Equal(water, Ids(restore.GetProperty("dashboard").GetProperty("widgets"))[1]);
        Assert.True(await SameContentAsync(h, housing, 1, 3));
    }

    [Fact]
    public async Task A_restore_never_grows_and_the_next_write_of_the_same_request_or_in_Tamiza_makes_a_new_version()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var water = await h.AddDashboardAsync("Water");
        await RenameAsync(h, water, "Agua");

        var restore = await RestoreThroughMcpAsync(h, water, 1, $"  {AddWaterWidgets} ", baseVersion: 2);
        await RenameWithSqlAsync(h, water, "Aguas");
        var numbers = await FlushAsync(h, DashboardVersionOrigin.Mcp, AddWaterWidgets, (water, Renamed("Aguas")));
        await RenameAsync(h, water, "Water");

        Assert.Equal((3, DashboardVersionOrigin.Mcp, AddWaterWidgets), (restore.Version.Number, restore.Version.Origin, restore.Version.Note));
        Assert.Equal([new DashboardVersionNumber(water, 4)], numbers);
        Assert.Equal(
            [
                """3 grace mcp [{"kind": "restored", "version": 1}]""",
                """4 grace mcp [{"kind": "renamed", "dashboard": "Aguas"}]""",
                """5 grace app [{"kind": "renamed", "dashboard": "Water"}]""",
            ],
            (await VersionsAsync(h, water))[2..]);
    }

    // The guard of an AI client's restore (design D7 step 3, D10)

    [Fact]
    public async Task A_guarded_restore_against_an_older_base_is_refused_naming_the_current_version_and_changes_nothing()
    {
        await using var h = await PublishedAsync();
        var housing = await h.AddDashboardAsync("Housing");
        await AddWidgetAsync(h, "Coverage", housing);
        // Another member adds a widget in Tamiza after the client read version 2.
        Assert.Equal(HttpStatusCode.Created, (await h.PostWidgetAsync(Column("Households"), h.Admin, housing)).StatusCode);
        var dashboards = await h.StoredDashboardsAsync();
        var widgets = await h.StoredWidgetsAsync();
        var versions = await AllVersionsAsync(h);

        var changed = await Assert.ThrowsAsync<DashboardChangedException>(() => RestoreThroughMcpAsync(h, housing, 1, "Go back", baseVersion: 2));

        Assert.Equal((3, "ada", DashboardVersionOrigin.App), (changed.Current, changed.Author, changed.Origin));
        Assert.Equal(await SavedAtAsync(h, housing, 3), changed.SavedAt.UtcDateTime);
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await AllVersionsAsync(h));
    }

    [Fact]
    public async Task A_guarded_restore_that_finds_a_state_no_version_holds_records_it_is_refused_naming_it_and_then_goes_ahead_on_it()
    {
        await using var h = await PublishedAsync();
        var housing = await h.AddDashboardAsync("Housing");
        var id = await AddWidgetAsync(h, "Coverage", housing);
        await h.ExecuteAsync(
            "UPDATE tamiza.dashboard_widgets SET title = 'Coverage.', updated_by = (SELECT id FROM tamiza.users WHERE keycloak_sub = 'ada') WHERE id = @id",
            ("id", id));
        var widgets = await h.StoredWidgetsAsync();

        var changed = await Assert.ThrowsAsync<DashboardChangedException>(() => RestoreThroughMcpAsync(h, housing, 1, "Go back", baseVersion: 2));

        Assert.Equal((3, "ada", DashboardVersionOrigin.App), (changed.Current, changed.Author, changed.Origin));
        Assert.Equal("""3 ada app [{"kind": "unrecorded"}]""", (await VersionsAsync(h, housing))[^1]);
        Assert.Equal(["Coverage."], Titles(await ContentAsync(h, housing, 3)));
        Assert.Equal(widgets, await h.StoredWidgetsAsync());

        var restore = await RestoreThroughMcpAsync(h, housing, 1, "Go back", baseVersion: 3);

        Assert.Equal((4, 1, "Go back"), (restore.Version.Number, restore.Version.RestoredFrom, restore.Version.Note));
        Assert.Empty(restore.Dashboard.Widgets);
    }
}
