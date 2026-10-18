using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>GET /projects/{id}/dashboards/{dashboardId}</c> and the widget routes under it: each dashboard holds, counts and places its own
/// widgets, a widget moves from one to another, and a dashboard that is gone answers <c>dashboards.notFound</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardWidgetTests(PostgresFixture postgres)
{
    private const string NotFoundCode = "dashboards.notFound";
    private const string MissingTarget = "The dashboard does not exist.";

    private async Task<IndicatorsHarness> PublishedAsync(Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 40, 4, 10), ('Palmira', 60, 6, 10)");
        return h;
    }

    private static object Column(string title = "Coverage by municipality", object? layout = null) => new
    {
        title,
        chartType = "column",
        config = new { indicator = "coverage", groupBy = "municipality" },
        layout,
    };

    private static object Stat(string title) => new { title, chartType = "stat", config = new { indicator = "coverage" } };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? Code(JsonElement problem) => problem.TryGetProperty("code", out var code) ? code.GetString() : null;

    private static string[] Titles(JsonElement widgets) => widgets.EnumerateArray().Select(w => w.GetProperty("title").GetString()!).ToArray();

    private static (int X, int Y, int W, int H) Layout(JsonElement widget)
    {
        var layout = widget.GetProperty("layout");
        return (layout.GetProperty("x").GetInt32(), layout.GetProperty("y").GetInt32(), layout.GetProperty("w").GetInt32(), layout.GetProperty("h").GetInt32());
    }

    // Reading a dashboard

    [Fact]
    public async Task A_dashboard_answers_its_name_and_only_its_widgets_top_to_bottom_then_left_to_right()
    {
        await using var h = await PublishedAsync();
        var health = await h.InsertDashboardAsync("Health");
        await h.AddWidgetAsync(Column("third", new { x = 0, y = 4, w = 3, h = 2 }), dashboardId: health);
        await h.AddWidgetAsync(Column("second", new { x = 6, y = 0, w = 3, h = 2 }), dashboardId: health);
        await h.AddWidgetAsync(Column("elsewhere", new { x = 0, y = 0, w = 3, h = 2 }));
        await h.AddWidgetAsync(Column("first", new { x = 0, y = 0, w = 3, h = 2 }), dashboardId: health);
        await h.AddWidgetAsync(Column("fourth", new { x = 0, y = 4, w = 3, h = 2 }), dashboardId: health);

        var dashboard = await h.GetDashboardAsync(health);
        var indicators = await h.GetDashboardAsync();

        Assert.Equal(health, dashboard.GetProperty("id").GetGuid());
        Assert.Equal("Health", dashboard.GetProperty("name").GetString());
        Assert.Equal(["first", "second", "third", "fourth"], Titles(dashboard.GetProperty("widgets")));
        Assert.All(dashboard.GetProperty("widgets").EnumerateArray(), w => Assert.Equal(health, w.GetProperty("dashboardId").GetGuid()));
        Assert.Equal((h.DashboardId, "Indicators"), (indicators.GetProperty("id").GetGuid(), indicators.GetProperty("name").GetString()));
        Assert.Equal(["elsewhere"], Titles(indicators.GetProperty("widgets")));
    }

    [Fact]
    public async Task A_dashboard_the_project_does_not_have_is_404_with_the_not_found_code()
    {
        await using var h = await PublishedAsync();
        var (_, otherDashboard) = await h.AddOtherProjectAsync();

        // The admin is a member of both projects: the other project's dashboard is still not one of this project's.
        var foreign = await h.Admin.GetAsync(h.DashboardUrl(otherDashboard));
        var missing = await h.Viewer.GetAsync(h.DashboardUrl(Guid.NewGuid()));

        foreach (var response in new[] { foreign, missing })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(NotFoundCode, Code(await ReadAsync(response)));
        }
    }

    // Analyst adds a widget

    [Fact]
    public async Task A_widget_added_on_a_dashboard_is_shown_there_to_every_member_and_on_no_other_dashboard()
    {
        await using var h = await PublishedAsync();
        var health = await h.InsertDashboardAsync("Health");

        var response = await h.PostWidgetAsync(Column(), dashboardId: health);
        var widget = await ReadAsync(response);
        var id = widget.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/projects/{h.ProjectId}/dashboards/{health}/widgets/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal(health, widget.GetProperty("dashboardId").GetGuid());
        foreach (var client in new[] { h.Viewer, h.Analyst, h.Admin })
        {
            var saved = (await h.GetWidgetsAsync(health, client)).EnumerateArray().Single();
            Assert.Equal((id, health), (saved.GetProperty("id").GetGuid(), saved.GetProperty("dashboardId").GetGuid()));
            Assert.Equal("Coverage by municipality", saved.GetProperty("title").GetString());
        }

        Assert.Equal(0, (await h.GetWidgetsAsync()).GetArrayLength());
    }

    // Widget limit

    [Fact]
    public async Task A_full_dashboard_refuses_another_widget_with_a_message_naming_the_limit()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "2" });
        await h.AddWidgetAsync(Column("one"));
        await h.AddWidgetAsync(Column("two"));

        var response = await h.PostWidgetAsync(Column("three"));
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A dashboard can have at most 2 widgets. Remove one or move it to another dashboard first."], IndicatorsHarness.Errors(problem, "widgets"));
        Assert.Equal(["one", "two"], Titles(await h.GetWidgetsAsync()));
    }

    // Limit counted per dashboard

    [Fact]
    public async Task The_limit_counts_the_widgets_of_the_dashboard_added_to_not_those_of_the_project()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "4" });
        var health = await h.InsertDashboardAsync("Health");
        var field = await h.InsertDashboardAsync("Field");
        foreach (var i in Enumerable.Range(0, 4))
        {
            await h.AddWidgetAsync(Column($"health {i}"), dashboardId: health);
        }

        foreach (var i in Enumerable.Range(0, 3))
        {
            await h.AddWidgetAsync(Column($"field {i}"), dashboardId: field);
        }

        var toField = await h.PostWidgetAsync(Column("field 3"), dashboardId: field);
        var toHealth = await h.PostWidgetAsync(Column("health 4"), dashboardId: health);

        Assert.Equal(HttpStatusCode.Created, toField.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toHealth.StatusCode);
        Assert.Equal(4, (await h.GetWidgetsAsync(field)).GetArrayLength());
        Assert.Equal(4, (await h.GetWidgetsAsync(health)).GetArrayLength());
    }

    // First widget of a dashboard

    [Fact]
    public async Task The_first_widget_of_a_dashboard_is_placed_at_its_top_whatever_another_dashboard_holds()
    {
        await using var h = await PublishedAsync();
        var health = await h.InsertDashboardAsync("Health");
        var field = await h.InsertDashboardAsync("Field");
        await h.AddWidgetAsync(Column("deep", new { x = 0, y = 26, w = 6, h = 4 }), dashboardId: health); // down to row 30

        var first = await h.AddWidgetAsync(Stat("First"), dashboardId: field);
        var next = await h.AddWidgetAsync(Stat("Next"), dashboardId: health);

        Assert.Equal((0, 0, 3, 2), Layout(first));
        Assert.Equal((0, 30, 3, 2), Layout(next));
    }

    // A widget only through its own dashboard

    [Fact]
    public async Task A_widget_reached_through_another_dashboard_of_the_project_is_not_found_and_stays_as_it_was()
    {
        await using var h = await PublishedAsync();
        var health = await h.InsertDashboardAsync("Health");
        var id = (await h.AddWidgetAsync(Column("Keep"))).GetProperty("id").GetGuid();
        var before = await h.StoredWidgetsAsync();

        var edit = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id, health), new { title = "Changed" });
        var remove = await h.Analyst.DeleteAsync(h.WidgetUrl(id, health));

        foreach (var response in new[] { edit, remove })
        {
            var problem = await ReadAsync(response);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Widget not found.", problem.GetProperty("title").GetString());
            Assert.Null(Code(problem));
        }

        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    // Widget added to a removed dashboard

    [Fact]
    public async Task A_widget_added_to_a_removed_dashboard_is_404_dashboards_not_found_and_saves_nothing()
    {
        await using var h = await PublishedAsync();
        var housing = await h.InsertDashboardAsync("Housing");
        await h.AddWidgetAsync(Column("Kept"));
        await h.RemoveDashboardAsync(housing);
        var before = await h.StoredWidgetsAsync();

        var valid = await h.PostWidgetAsync(Column("New"), dashboardId: housing);
        var invalid = await h.PostWidgetAsync(new { title = " ", chartType = "radar" }, dashboardId: housing); // the dashboard is checked first

        foreach (var response in new[] { valid, invalid })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(NotFoundCode, Code(await ReadAsync(response)));
        }

        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    // Widget edited on a removed dashboard

    [Fact]
    public async Task A_widget_edited_or_removed_on_a_removed_dashboard_is_404_dashboards_not_found_and_changes_nothing()
    {
        await using var h = await PublishedAsync();
        var health = await h.InsertDashboardAsync("Health");
        var onHealth = (await h.AddWidgetAsync(Column("On health"), dashboardId: health)).GetProperty("id").GetGuid();
        var kept = (await h.AddWidgetAsync(Column("Kept"))).GetProperty("id").GetGuid();
        await h.RemoveDashboardAsync(health);
        var before = await h.StoredWidgetsAsync();

        HttpResponseMessage[] responses =
        [
            await h.Analyst.PatchJsonAsync(h.WidgetUrl(onHealth, health), new { title = "Changed" }),
            await h.Analyst.PatchJsonAsync(h.WidgetUrl(onHealth, health), new { layout = new { x = 6, y = 0, w = 6, h = 4 } }),
            await h.Analyst.PatchJsonAsync(h.WidgetUrl(onHealth, health), new { dashboardId = h.DashboardId }),
            await h.Analyst.DeleteAsync(h.WidgetUrl(onHealth, health)),
            // A widget of a dashboard that still exists is not reached through the removed one.
            await h.Analyst.PatchJsonAsync(h.WidgetUrl(kept, health), new { title = "Changed" }),
            await h.Analyst.DeleteAsync(h.WidgetUrl(kept, health)),
        ];

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(NotFoundCode, Code(await ReadAsync(response)));
        }

        Assert.Equal(before, await h.StoredWidgetsAsync());
        Assert.Equal(["Kept"], Titles(await h.GetWidgetsAsync()));
    }

    [Theory]
    [InlineData("DELETE FROM tamiza.dashboards WHERE id = OLD.dashboard_id", NotFoundCode)]
    [InlineData("DELETE FROM tamiza.dashboard_widgets WHERE id = OLD.id", null)]
    public async Task A_widget_removed_between_its_read_and_its_save_is_404_never_500(string removal, string? code)
    {
        await using var h = await PublishedAsync();
        var id = (await h.AddWidgetAsync(Column("Racing"))).GetProperty("id").GetGuid();
        // The removal lands after the PATCH has read the widget: the save then finds no row to update.
        await h.ExecuteAsync(
            $"""
            CREATE FUNCTION tamiza.remove_before_update() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                {removal};
                RETURN NULL;
            END $$;
            CREATE TRIGGER remove_before_update BEFORE UPDATE ON tamiza.dashboard_widgets
                FOR EACH ROW EXECUTE FUNCTION tamiza.remove_before_update();
            """);

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { title = "Changed" });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(code, Code(problem));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    // Widget moved to another dashboard

    [Fact]
    public async Task A_widget_moved_to_another_dashboard_is_shown_there_below_its_widgets_and_no_longer_where_it_was()
    {
        await using var h = await PublishedAsync();
        var health = await h.InsertDashboardAsync("Health");
        var field = await h.InsertDashboardAsync("Field");
        await h.AddWidgetAsync(Column("Field top", new { x = 0, y = 0, w = 6, h = 4 }), dashboardId: field);
        await h.AddWidgetAsync(Column("Field side", new { x = 6, y = 2, w = 6, h = 5 }), dashboardId: field); // bottom edge at 7
        await h.AddWidgetAsync(Column("Stays", new { x = 0, y = 0, w = 6, h = 4 }), dashboardId: health);
        var created = await h.AddWidgetAsync(Column(layout: new { x = 8, y = 0, w = 4, h = 5 }), dashboardId: health);
        var id = created.GetProperty("id").GetGuid();

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id, health), new { dashboardId = field });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((id, field), (moved.GetProperty("id").GetGuid(), moved.GetProperty("dashboardId").GetGuid()));
        Assert.Equal(("Coverage by municipality", "column"), (moved.GetProperty("title").GetString(), moved.GetProperty("chartType").GetString()));
        Assert.Equal(created.GetProperty("config").ToString(), moved.GetProperty("config").ToString());
        Assert.Equal((0, 7, 4, 5), Layout(moved)); // at the left edge under Field's lowest widget, with the size it had
        var onField = await h.GetWidgetsAsync(field);
        Assert.Equal(["Field top", "Field side", "Coverage by municipality"], Titles(onField));
        Assert.Equal(moved.ToString(), onField[2].ToString());
        Assert.Equal(["Stays"], Titles(await h.GetWidgetsAsync(health)));
        // From now on the widget is reached through its new dashboard only.
        var throughOld = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id, health), new { title = "Changed" });
        Assert.Equal(HttpStatusCode.NotFound, throughOld.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.PatchJsonAsync(h.WidgetUrl(id, field), new { title = "Changed" })).StatusCode);
    }

    [Fact]
    public async Task A_layout_sent_with_a_move_is_ignored_and_the_widget_still_lands_below_the_targets_widgets()
    {
        await using var h = await PublishedAsync();
        var field = await h.InsertDashboardAsync("Field");
        await h.AddWidgetAsync(Column("Field top", new { x = 0, y = 0, w = 6, h = 4 }), dashboardId: field);
        var id = (await h.AddWidgetAsync(Column("Mover"))).GetProperty("id").GetGuid();

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = field, title = "Moved", layout = new { x = 6, y = 0, w = 6, h = 4 } });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Moved", moved.GetProperty("title").GetString()); // the rest of the change is saved with the move
        Assert.Equal((0, 4, 6, 4), Layout(moved));
        Assert.Equal(["Field top", "Moved"], Titles(await h.GetWidgetsAsync(field)));
    }

    [Fact]
    public async Task A_patch_naming_the_widgets_own_dashboard_is_an_ordinary_change_that_moves_and_counts_nothing()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "1" });
        var id = (await h.AddWidgetAsync(Column("Only", new { x = 0, y = 0, w = 6, h = 4 }))).GetProperty("id").GetGuid();

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = h.DashboardId, title = "Renamed", layout = new { x = 6, y = 1, w = 6, h = 4 } });
        var changed = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((h.DashboardId, "Renamed"), (changed.GetProperty("dashboardId").GetGuid(), changed.GetProperty("title").GetString()));
        Assert.Equal((6, 1, 6, 4), Layout(changed));
    }

    // Stale widget moved

    [Fact]
    public async Task A_widget_whose_group_column_is_gone_is_moved_by_a_patch_of_its_dashboard_alone_and_still_reports_the_column()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns + ", year bigint", "('Cali', 40, 4, 10, 2025)");
        var field = await h.InsertDashboardAsync("Field");
        var created = await h.AddWidgetAsync(new { title = "By year", chartType = "bar", config = new { indicator = "coverage", groupBy = "year" } });
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)"); // no year column any more

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { dashboardId = field });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(field, moved.GetProperty("dashboardId").GetGuid());
        Assert.Equal(created.GetProperty("config").ToString(), moved.GetProperty("config").ToString());
        Assert.Equal(["By year"], Titles(await h.GetWidgetsAsync(field)));
        Assert.Equal(0, (await h.GetWidgetsAsync()).GetArrayLength());
        var result = await h.QueryAsync(JsonSerializer.Deserialize<JsonElement>(moved.GetProperty("config").GetRawText()), "bar");
        Assert.Equal(["year"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    // Full dashboard

    [Fact]
    public async Task A_widget_moved_to_a_full_dashboard_is_refused_on_the_dashboard_naming_the_limit_and_stays_where_it_was()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "2" });
        var field = await h.InsertDashboardAsync("Field");
        await h.AddWidgetAsync(Column("On field"), dashboardId: field);
        var first = (await h.AddWidgetAsync(Column("First"))).GetProperty("id").GetGuid();
        var second = (await h.AddWidgetAsync(Column("Second"))).GetProperty("id").GetGuid();

        var room = await h.Analyst.PatchJsonAsync(h.WidgetUrl(first), new { dashboardId = field }); // the last place on "Field"
        var before = await h.StoredWidgetsAsync();
        var full = await h.Analyst.PatchJsonAsync(h.WidgetUrl(second), new { dashboardId = field, title = "Changed" });
        var problem = await ReadAsync(full);

        Assert.Equal(HttpStatusCode.OK, room.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
        Assert.Equal(["A dashboard can have at most 2 widgets. Remove one or move it to another dashboard first."], IndicatorsHarness.Errors(problem, "dashboardId"));
        Assert.Equal(before, await h.StoredWidgetsAsync());
        Assert.Equal(["Second"], Titles(await h.GetWidgetsAsync()));
    }

    // Dashboard of another project (dashboard-indicators)

    [Fact]
    public async Task A_widget_moved_to_a_dashboard_of_another_project_is_refused_on_the_dashboard_and_stays_where_it_was()
    {
        await using var h = await PublishedAsync();
        var (_, otherDashboard) = await h.AddOtherProjectAsync();
        var id = (await h.AddWidgetAsync(Column("Mover"))).GetProperty("id").GetGuid();
        var before = await h.StoredWidgetsAsync();

        // The admin is a member of both projects: the other project's dashboard is still not one of this project's.
        var foreign = await h.Admin.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = otherDashboard });
        var missing = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = Guid.NewGuid(), title = " " });

        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal([MissingTarget], IndicatorsHarness.Errors(await ReadAsync(foreign), "dashboardId"));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var problem = await ReadAsync(missing);
        Assert.Equal([MissingTarget], IndicatorsHarness.Errors(problem, "dashboardId"));
        Assert.Single(IndicatorsHarness.Errors(problem, "title")); // reported together with the other fields' errors
        Assert.Equal(before, await h.StoredWidgetsAsync());
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db,
            "SELECT count(*) FROM tamiza.dashboard_widgets WHERE dashboard_id = @id", ("id", otherDashboard)));
    }

    // Target removed meanwhile

    [Fact]
    public async Task A_widget_moved_to_a_removed_dashboard_is_refused_on_the_dashboard_and_stays_where_it_was()
    {
        await using var h = await PublishedAsync();
        var field = await h.InsertDashboardAsync("Field");
        var id = (await h.AddWidgetAsync(Column("Mover"))).GetProperty("id").GetGuid();
        await h.RemoveDashboardAsync(field);
        var before = await h.StoredWidgetsAsync();

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = field });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([MissingTarget], IndicatorsHarness.Errors(await ReadAsync(response), "dashboardId"));
        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task A_target_removed_while_the_move_waits_for_the_lock_is_a_400_on_the_dashboard_never_500()
    {
        await using var h = await PublishedAsync();
        var field = await h.InsertDashboardAsync("Field");
        var id = (await h.AddWidgetAsync(Column("Mover"))).GetProperty("id").GetGuid();
        var before = await h.StoredWidgetsAsync();

        var response = await MoveWhileLockedAsync(h, id, field, "DELETE FROM tamiza.dashboards WHERE id = @id", ("id", field));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([MissingTarget], IndicatorsHarness.Errors(await ReadAsync(response), "dashboardId"));
        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    // The move reads again under the lock (design D3)

    [Fact]
    public async Task A_target_filled_while_the_move_waits_for_the_lock_refuses_the_move_with_the_limit()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "1" });
        var field = await h.InsertDashboardAsync("Field");
        var other = (await h.AddWidgetAsync(Column("Other"), dashboardId: await h.InsertDashboardAsync("Health"))).GetProperty("id").GetGuid();
        var id = (await h.AddWidgetAsync(Column("Mover"))).GetProperty("id").GetGuid();

        var response = await MoveWhileLockedAsync(h, id, field,
            "UPDATE tamiza.dashboard_widgets SET dashboard_id = @field WHERE id = @other", ("field", field), ("other", other));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A dashboard can have at most 1 widgets. Remove one or move it to another dashboard first."],
            IndicatorsHarness.Errors(await ReadAsync(response), "dashboardId"));
        Assert.Equal(["Other"], Titles(await h.GetWidgetsAsync(field)));
        Assert.Equal(["Mover"], Titles(await h.GetWidgetsAsync()));
    }

    [Fact]
    public async Task A_widget_another_member_moved_while_the_move_waited_for_the_lock_is_not_found_and_stays_where_they_put_it()
    {
        await using var h = await PublishedAsync();
        var health = await h.InsertDashboardAsync("Health");
        var field = await h.InsertDashboardAsync("Field");
        var id = (await h.AddWidgetAsync(Column("Mover"))).GetProperty("id").GetGuid();

        var response = await MoveWhileLockedAsync(h, id, field,
            "UPDATE tamiza.dashboard_widgets SET dashboard_id = @health WHERE id = @id", ("health", health), ("id", id));
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Widget not found.", problem.GetProperty("title").GetString());
        Assert.Null(Code(problem));
        Assert.Equal(["Mover"], Titles(await h.GetWidgetsAsync(health)));
        Assert.Equal(0, (await h.GetWidgetsAsync(field)).GetArrayLength());
    }

    [Theory]
    // Another member saves a stat with no group column meanwhile: the bar keeps the group column it was checked with.
    [InlineData("chartType", "UPDATE tamiza.dashboard_widgets SET chart_type = 'stat', config = '{\"indicator\":\"coverage\"}' WHERE id = @id")]
    // Another member makes it a map meanwhile: the query sent, which has no geometry column, keeps the chart type it was checked with.
    [InlineData("config", "UPDATE tamiza.dashboard_widgets SET chart_type = 'map' WHERE id = @id")]
    public async Task A_chart_type_or_query_sent_with_a_move_is_saved_with_the_one_it_was_checked_against_whatever_lands_meanwhile(
        string sent, string meanwhile)
    {
        await using var h = await PublishedAsync();
        var field = await h.InsertDashboardAsync("Field");
        var id = (await h.AddWidgetAsync(Column("Mover"))).GetProperty("id").GetGuid(); // a column grouped by municipality
        object body = sent == "chartType"
            ? new { dashboardId = field, chartType = "bar" }
            : new { dashboardId = field, config = new { indicator = "coverage", groupBy = "municipality", measure = "value" } };

        var response = await PatchWhileLockedAsync(h, id, body, meanwhile, ("id", id));
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (chartType, config) = (moved.GetProperty("chartType").GetString()!, moved.GetProperty("config"));
        Assert.Equal(sent == "chartType" ? "bar" : "column", chartType);
        Assert.Equal("municipality", config.GetProperty("groupBy").GetString());
        Assert.Equal(sent == "chartType" ? null : "value", config.TryGetProperty("measure", out var measure) ? measure.GetString() : null);
        // What was saved is a pair the dashboard can draw, not one its query refuses.
        var stored = (await h.GetWidgetsAsync(field)).EnumerateArray().Single();
        Assert.Equal((chartType, config.ToString()), (stored.GetProperty("chartType").GetString(), stored.GetProperty("config").ToString()));
        await h.QueryAsync(JsonSerializer.Deserialize<JsonElement>(config.GetRawText()), chartType);
    }

    [Fact]
    public async Task A_move_alone_saves_the_chart_type_and_query_another_member_saved_while_it_waited_for_the_lock()
    {
        await using var h = await PublishedAsync();
        var field = await h.InsertDashboardAsync("Field");
        var id = (await h.AddWidgetAsync(Column("Mover"))).GetProperty("id").GetGuid();

        var response = await MoveWhileLockedAsync(h, id, field,
            "UPDATE tamiza.dashboard_widgets SET chart_type = 'stat', config = '{\"indicator\":\"coverage\"}' WHERE id = @id", ("id", id));
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((field, "stat"), (moved.GetProperty("dashboardId").GetGuid(), moved.GetProperty("chartType").GetString()));
        Assert.Equal(JsonValueKind.Null, moved.GetProperty("config").GetProperty("groupBy").ValueKind);
    }

    /// <summary>
    /// Moves the widget, as the analyst, to <paramref name="target"/> while another session holds the project's lock, and runs
    /// <paramref name="meanwhile"/> in that session once the move waits for it: another member's change landing between the move's
    /// first read and its locked step.
    /// </summary>
    private static Task<HttpResponseMessage> MoveWhileLockedAsync(
        IndicatorsHarness h, Guid widgetId, Guid target, string meanwhile, params (string Name, object Value)[] parameters) =>
        PatchWhileLockedAsync(h, widgetId, new { dashboardId = target }, meanwhile, parameters);

    /// <summary>
    /// Sends <paramref name="body"/>, which moves the widget, as the analyst while another session holds the project's lock, and runs
    /// <paramref name="meanwhile"/> in that session once the move waits for it.
    /// </summary>
    private static async Task<HttpResponseMessage> PatchWhileLockedAsync(
        IndicatorsHarness h, Guid widgetId, object body, string meanwhile, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(h.Db);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        async Task<object?> RunAsync(string sql, params (string Name, object Value)[] values)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            foreach (var (name, value) in values)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return await command.ExecuteScalarAsync();
        }

        await RunAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", ("key", h.ProjectId.ToString()));
        var move = h.Analyst.PatchJsonAsync(h.WidgetUrl(widgetId), body);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        const string waiting = """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """;
        // A move that never waits for the lock finishes first, and the change below then lands too late to matter.
        while (!move.IsCompleted && (long)(await RunAsync(waiting))! == 0)
        {
            await Task.Delay(20, timeout.Token);
        }

        await RunAsync(meanwhile, parameters);
        await transaction.CommitAsync();
        return await move;
    }
}
