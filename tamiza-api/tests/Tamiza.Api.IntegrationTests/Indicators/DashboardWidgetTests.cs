using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // Column labels of a widget (dashboard-indicators: Column labels of a widget)

    private static object Labelled(string chartType, object? labels, string title = "Labelled") => new
    {
        title,
        chartType,
        config = new { indicator = "coverage", groupBy = chartType == "stat" ? null : "municipality", display = new { labels } },
    };

    [Fact]
    public async Task A_stat_tile_is_saved_with_its_own_label_for_a_column_and_returns_it()
    {
        await using var h = await PublishedAsync();

        var created = await h.AddWidgetAsync(Labelled("stat", new { numerator = "Hogares que asistieron" }));

        var labels = created.GetProperty("config").GetProperty("display").GetProperty("labels");
        Assert.Equal(["numerator"], labels.EnumerateObject().Select(l => l.Name));
        Assert.Equal("Hogares que asistieron", labels.GetProperty("numerator").GetString());
        Assert.False(created.GetProperty("config").GetProperty("display").TryGetProperty("color", out _));
        var read = (await h.GetWidgetsAsync(client: h.Viewer))[0];
        Assert.Equal("Hogares que asistieron", read.GetProperty("config").GetProperty("display").GetProperty("labels").GetProperty("numerator").GetString());
        Assert.Equal(
            """{"labels": {"numerator": "Hogares que asistieron"}}""",
            await TestData.ScalarAsync<string>(h.Db, "SELECT config -> 'display' FROM tamiza.dashboard_widgets WHERE id = @id", ("id", created.GetProperty("id").GetGuid())));
    }

    [Fact]
    public async Task A_widget_of_every_kind_takes_labels()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns + ", geometry text, water_source text", "('Cali', 40, 4, 10, 'POINT(0 0)', 'Piped')");
        var configs = new (string Type, object Config)[]
        {
            ("stat", new { indicator = "coverage" }),
            ("bar", new { indicator = "coverage", groupBy = "municipality" }),
            ("column", new { indicator = "coverage", groupBy = "municipality" }),
            ("line", new { indicator = "coverage", groupBy = "municipality" }),
            ("histogram", new { indicator = "coverage", measure = "value" }),
            ("pie", new { indicator = "coverage", groupBy = "municipality" }),
            ("map", new { indicator = "coverage", groupBy = "municipality", geometryColumn = "geometry" }),
            ("boxplot", new { indicator = "coverage", measure = "value", groupBy = "municipality" }),
        };

        foreach (var (type, config) in configs)
        {
            var body = JsonSerializer.SerializeToNode(new { title = type, chartType = type, config })!;
            body["config"]!["display"] = JsonSerializer.SerializeToNode(new { labels = new { value = "Hogares" } });
            var response = await h.PostWidgetAsync(body);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("Hogares", (await ReadAsync(response)).GetProperty("config").GetProperty("display").GetProperty("labels").GetProperty("value").GetString());
        }
    }

    [Fact]
    public async Task A_widget_labelling_a_column_that_the_table_loses_is_drawn_with_no_missing_column()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns + ", estrato text", "('Cali', 40, 4, 10, '1')");
        var created = await h.AddWidgetAsync(Labelled("column", new { estrato = "Estrato" }));
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)"); // no estrato any more

        var result = await h.QueryAsync(JsonSerializer.Deserialize<JsonElement>(created.GetProperty("config").GetRawText()), "column");

        Assert.Empty(result.GetProperty("missing").GetProperty("columns").EnumerateArray());
        Assert.Equal(["Cali|1"], IndicatorsHarness.Rows(result));
        var widget = (await h.GetWidgetsAsync())[0];
        Assert.Equal("Estrato", widget.GetProperty("config").GetProperty("display").GetProperty("labels").GetProperty("estrato").GetString());
    }

    [Fact]
    public async Task A_widget_with_labels_for_11_columns_is_refused_on_its_labels_and_nothing_is_saved()
    {
        await using var h = await PublishedAsync();
        var labels = Enumerable.Range(1, 11).ToDictionary(i => $"columna_{i}", i => $"Columna {i}");

        var response = await h.PostWidgetAsync(Labelled("column", labels));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A widget labels at most 10 columns."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.labels"));
        Assert.Empty(await h.StoredWidgetsAsync());
        Assert.Equal(HttpStatusCode.Created, (await h.PostWidgetAsync(Labelled("column", labels.Take(10).ToDictionary(l => l.Key, l => l.Value)))).StatusCode);
    }

    [Fact]
    public async Task A_label_of_81_characters_is_refused_on_its_column_and_nothing_is_saved()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(Labelled("column", new { numerator = new string('a', 81) }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A column label can have up to 80 characters."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.labels.numerator"));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task A_label_for_a_name_that_is_not_a_columns_is_refused_on_the_labels_and_the_edit_changes_nothing()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(Column());
        var before = await h.StoredWidgetsAsync();

        var response = await h.Analyst.PatchJsonAsync(
            h.WidgetUrl(created.GetProperty("id").GetGuid()),
            new { config = new { indicator = "coverage", groupBy = "municipality", display = new { labels = new Dictionary<string, string> { ["Comuna"] = "Comuna" } } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = IndicatorsHarness.Errors(await ReadAsync(response), "config.display.labels");
        Assert.StartsWith("'Comuna' is not a column's name", Assert.Single(errors));
        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task A_widget_moved_to_another_dashboard_keeps_its_column_labels()
    {
        await using var h = await PublishedAsync();
        var cruces = await h.InsertDashboardAsync("Cruces");
        var created = await h.AddWidgetAsync(Labelled("column", new { denominator = "Hogares que asistieron" }));
        var id = created.GetProperty("id").GetGuid();

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = cruces });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.GetProperty("config").ToString(), moved.GetProperty("config").ToString());
        var onCruces = (await h.GetWidgetsAsync(cruces))[0];
        Assert.Equal("Hogares que asistieron", onCruces.GetProperty("config").GetProperty("display").GetProperty("labels").GetProperty("denominator").GetString());
    }

    [Fact]
    public async Task A_chart_type_change_keeps_the_labels()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(Labelled("column", new { municipality = "Municipio", denominator = "Hogares" }));
        var id = created.GetProperty("id").GetGuid();

        var toBar = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { chartType = "bar" });
        var toStat = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { chartType = "stat", config = new { indicator = "coverage", display = new { labels = new { municipality = "Municipio", denominator = "Hogares" } } } });

        Assert.Equal(HttpStatusCode.OK, toBar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, toStat.StatusCode);
        var labels = (await ReadAsync(toBar)).GetProperty("config").GetProperty("display").GetProperty("labels");
        Assert.Equal(["denominator", "municipality"], labels.EnumerateObject().Select(l => l.Name));
    }

    // The texts of a widget (dashboard-indicators: Widget context validation)

    private static object Texted(string chartType, object display, string title = "Texted") => new
    {
        title,
        chartType,
        config = new { indicator = "coverage", groupBy = chartType == "stat" ? null : "municipality", display },
    };

    public static TheoryData<string, string> RefusedTexts() => new()
    {
        // Long subtitle, Line break in a footnote, Caption with a text and hidden, Long unit
        { "subtitle", "config.display.subtitle" },
        { "footnote", "config.display.footnote" },
        { "caption", "config.display.caption" },
        { "unit", "config.display.unit" },
    };

    private static (object Display, string Message) RefusedDisplay(string field) => field switch
    {
        "subtitle" => (new { subtitle = new string('s', 201) }, "The subtitle is longer than 200 characters."),
        "footnote" => (new { footnote = "No suman 100 %\nrespuesta múltiple" }, "The footnote cannot contain line breaks or other control characters."),
        "caption" => (new { caption = new { text = "% de quienes asistieron", hidden = true } }, "Give the caption its own text or hide it, not both."),
        _ => (new { unit = new string('u', 33) }, "The unit is longer than 32 characters."),
    };

    [Theory]
    [MemberData(nameof(RefusedTexts))]
    public async Task A_text_that_breaks_a_rule_is_refused_on_its_field_when_a_widget_is_created_and_nothing_is_saved(string field, string key)
    {
        await using var h = await PublishedAsync();
        var (display, message) = RefusedDisplay(field);

        var response = await h.PostWidgetAsync(Texted("column", display));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], IndicatorsHarness.Errors(await ReadAsync(response), key));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Theory]
    [MemberData(nameof(RefusedTexts))]
    public async Task A_text_that_breaks_a_rule_is_refused_on_its_field_when_a_widget_is_updated_and_nothing_is_saved(string field, string key)
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(Texted("column", new { subtitle = "Pregunta", unit = "hogares" }));
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionCountAsync(h);
        var (display, message) = RefusedDisplay(field);

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { config = new { indicator = "coverage", groupBy = "municipality", display } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], IndicatorsHarness.Errors(await ReadAsync(response), key));
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_subtitle_of_spaces_is_saved_as_no_subtitle_and_the_texts_are_stored_trimmed()
    {
        await using var h = await PublishedAsync();

        var blank = await h.AddWidgetAsync(Texted("column", new { subtitle = "   " }, "Blank"));
        var trimmed = await h.AddWidgetAsync(Texted("column", new { subtitle = "  Pregunta  ", footnote = " Nota ", caption = new { text = " Mi texto " }, unit = " hogares " }, "Trimmed"));

        Assert.False(blank.GetProperty("config").TryGetProperty("display", out _));
        var display = trimmed.GetProperty("config").GetProperty("display");
        Assert.Equal(("Pregunta", "Nota", "Mi texto", "hogares"),
            (display.GetProperty("subtitle").GetString(), display.GetProperty("footnote").GetString(), display.GetProperty("caption").GetProperty("text").GetString(), display.GetProperty("unit").GetString()));
        Assert.Equal(
            """{"unit": "hogares", "caption": {"text": "Mi texto"}, "footnote": "Nota", "subtitle": "Pregunta"}""",
            await TestData.ScalarAsync<string>(h.Db, "SELECT config -> 'display' FROM tamiza.dashboard_widgets WHERE id = @id", ("id", trimmed.GetProperty("id").GetGuid())));
        Assert.DoesNotContain("display", (await h.StoredWidgetsAsync()).Single(w => w.StartsWith("Blank|", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_widget_of_every_chart_type_takes_the_texts()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(
            "households_by_source", "municipality text, water_source text, value numeric, numerator numeric, denominator numeric, geometry text",
            "('Cali', 'Piped', 35, 7, 10, 'POINT(1 1)'), ('Palmira', 'Well', 15, 3, 10, 'POINT(2 2)')");
        const string indicator = "households_by_source";
        var configs = new (string Type, object Config)[]
        {
            ("stat", new { indicator, measure = "value" }),
            ("bar", new { indicator, measure = "value", groupBy = "municipality" }),
            ("column", new { indicator, measure = "value", groupBy = "municipality", seriesBy = "water_source" }),
            ("line", new { indicator, measure = "value", groupBy = "municipality" }),
            ("histogram", new { indicator, measure = "value", buckets = 3 }),
            ("pie", new { indicator, measure = "value", groupBy = "municipality" }),
            ("map", new { indicator, measure = "value", groupBy = "municipality", geometryColumn = "geometry" }),
            ("heatmap", new { indicator, measure = "value", groupBy = "municipality", seriesBy = "water_source" }),
            ("crosstab", new { indicator, measure = "value", groupBy = "municipality", seriesBy = "water_source" }),
            ("pyramid", new { indicator, measure = "value", groupBy = "municipality", seriesBy = "water_source", pyramid = new { left = "Piped", right = "Well" } }),
            ("boxplot", new { indicator, measure = "value", groupBy = "municipality" }),
            ("likert", new
            {
                indicator, measure = "value", groupBy = "municipality", seriesBy = "water_source",
                scale = new object[] { new { value = "Piped", side = "negative" }, new { value = "Well", side = "positive" } },
            }),
        };
        Assert.Equal(Tamiza.Api.Indicators.ChartTypes.All.Order(StringComparer.Ordinal), configs.Select(c => c.Type).Order(StringComparer.Ordinal));

        foreach (var (type, config) in configs)
        {
            var body = JsonSerializer.SerializeToNode(new { title = type, chartType = type, config })!;
            body["config"]!["display"] = JsonSerializer.SerializeToNode(new
            {
                subtitle = "¿En qué lugares ha visto teatro?", footnote = "No suman 100 %", caption = new { text = "% de quienes asistieron" }, unit = "de quienes asistieron",
                showDescription = true,
            });
            var response = await h.PostWidgetAsync(body);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var display = (await ReadAsync(response)).GetProperty("config").GetProperty("display");
            Assert.Equal(("¿En qué lugares ha visto teatro?", "No suman 100 %", "% de quienes asistieron", "de quienes asistieron", true),
                (display.GetProperty("subtitle").GetString(), display.GetProperty("footnote").GetString(), display.GetProperty("caption").GetProperty("text").GetString(),
                 display.GetProperty("unit").GetString(), display.GetProperty("showDescription").GetBoolean()));
        }

        var hidden = await h.PostWidgetAsync(JsonSerializer.SerializeToNode(new { title = "Hidden", chartType = "map", config = new { indicator, measure = "value", groupBy = "municipality", geometryColumn = "geometry", display = new { caption = new { hidden = true } } } })!);
        Assert.Equal(HttpStatusCode.Created, hidden.StatusCode);
        Assert.True((await ReadAsync(hidden)).GetProperty("config").GetProperty("display").GetProperty("caption").GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public async Task A_chart_type_change_keeps_the_subtitle_and_the_unit()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(Texted("bar", new { subtitle = "Pregunta", unit = "hogares" }));

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { chartType = "pie" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var widget = await ReadAsync(response);
        Assert.Equal("pie", widget.GetProperty("chartType").GetString());
        var display = widget.GetProperty("config").GetProperty("display");
        Assert.Equal(("Pregunta", "hogares"), (display.GetProperty("subtitle").GetString(), display.GetProperty("unit").GetString()));
        Assert.Equal(created.GetProperty("config").GetRawText(), widget.GetProperty("config").GetRawText());
    }

    [Fact]
    public async Task A_column_chart_in_color_2_stored_before_the_texts_sent_back_unchanged_writes_nothing_and_makes_no_version()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(Column());
        var id = created.GetProperty("id").GetGuid();
        // As an earlier release stored it: a display with a color and nothing else.
        await h.ExecuteAsync(
            """UPDATE tamiza.dashboard_widgets SET config = '{"indicator": "coverage", "groupBy": "municipality", "display": {"color": 2}}' WHERE id = @id""", ("id", id));
        var widget = (await h.GetWidgetsAsync()).EnumerateArray().Single();
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionCountAsync(h);

        var form = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new
        {
            title = widget.GetProperty("title").GetString(),
            chartType = widget.GetProperty("chartType").GetString(),
            config = widget.GetProperty("config"),
        });

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
        Assert.Equal("""{"color": 2}""", await TestData.ScalarAsync<string>(h.Db, "SELECT config -> 'display' FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id)));
    }

    [Fact]
    public async Task A_widget_with_texts_sent_back_unchanged_writes_nothing_and_makes_no_version()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(Texted("column", new { subtitle = "¿En qué lugares?", footnote = "No suman 100 %", caption = new { hidden = true }, unit = "hogares", showDescription = true }));
        var id = created.GetProperty("id").GetGuid();
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionCountAsync(h);
        var read = (await h.GetWidgetsAsync()).EnumerateArray().Single();

        var form = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { title = "Texted", chartType = "column", config = read.GetProperty("config") });

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_widget_moved_to_another_dashboard_keeps_its_texts_byte_for_byte()
    {
        await using var h = await PublishedAsync();
        var segments = await h.InsertDashboardAsync("Segmentos");
        var created = await h.AddWidgetAsync(Texted("bar", new { subtitle = "¿En qué lugares?", footnote = "No suman 100 %", caption = new { hidden = true }, unit = "hogares", showDescription = true }));
        var id = created.GetProperty("id").GetGuid();
        var before = await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id));

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = segments });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.GetProperty("config").GetRawText(), moved.GetProperty("config").GetRawText());
        Assert.Equal(before, await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id)));
        Assert.Equal(segments, (await TestData.ScalarAsync<Guid>(h.Db, "SELECT dashboard_id FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id))));
    }

    [Fact]
    public async Task A_widget_moved_to_another_dashboard_keeps_a_caption_kept_in_sight()
    {
        // improve-widget-text-hierarchy, task 1.3.
        await using var h = await PublishedAsync();
        var segments = await h.InsertDashboardAsync("Segmentos");
        var created = await h.AddWidgetAsync(Texted("column", new { subtitle = "¿En qué lugares?", caption = new { hidden = false } }));
        var id = created.GetProperty("id").GetGuid();
        var before = await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id));

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = segments });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(moved.GetProperty("config").GetProperty("display").GetProperty("caption").GetProperty("hidden").GetBoolean());
        Assert.Equal(before, await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id)));
    }

    private static async Task<long> VersionCountAsync(IndicatorsHarness h) =>
        await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_versions WHERE project_id = @id", ("id", h.ProjectId));

    // Reference lines, steps and data labels (dashboard-indicators: Reference line settings refused, Chart annotations kept with the widget)

    private const string BySource = "households_by_source";

    private async Task<IndicatorsHarness> PublishedBySourceAsync()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(
            BySource, "municipality text, water_source text, value numeric, numerator numeric, denominator numeric",
            "('Cali', 'Piped', 35, 7, 10), ('Cali', 'Well', 15, 3, 10), ('Palmira', 'Piped', 20, 4, 10), ('Palmira', 'Well', 30, 6, 10)");
        return h;
    }

    private static readonly object Ratio = new { numerator = "numerator", denominator = "denominator" };

    private static object Annotated(string chartType, object display, string title = "Annotated") => new
    {
        title,
        chartType,
        config = new { indicator = BySource, ratio = Ratio, groupBy = "municipality", display },
    };

    private static object[] Lines(params object[] each) => each;

    public static TheoryData<string> RefusedAnnotations() => new()
    {
        "Five lines", "Line on a pie chart", "Mean line with a series", "Overall line over a measure", "Value line without its number",
        "Share past the whole bar", "Line break in a label", "Steps on a column chart", "Stacked bars",
    };

    /// <summary>The chart type, the config, and the key and message the API answers for each refused case.</summary>
    private static (string ChartType, object Config, string Key, string Message) RefusedAnnotation(string name)
    {
        return name switch
        {
            "Five lines" => ("column",
                new { indicator = BySource, ratio = Ratio, groupBy = "municipality", display = new { referenceLines = Enumerable.Range(1, 5).Select(i => new { kind = "value", value = i }).ToArray() } },
                "config.display.referenceLines", "A widget has at most 4 reference lines."),
            "Line on a pie chart" => ("pie",
                new { indicator = BySource, measure = "value", groupBy = "municipality", display = new { referenceLines = Lines(new { kind = "mean" }) } },
                "config.display.referenceLines", "Only bar, column and line widgets have reference lines."),
            "Mean line with a series" => ("bar",
                new { indicator = BySource, measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "percent", display = new { referenceLines = Lines(new { kind = "mean" }) } },
                "config.display.referenceLines[0].kind", "A mean or median line needs a chart with no series."),
            "Overall line over a measure" => ("bar",
                new { indicator = BySource, measure = "value", groupBy = "municipality", display = new { referenceLines = Lines(new { kind = "overall" }) } },
                "config.display.referenceLines[0].kind", "An overall line needs a ratio: over a measure it would be the sum of every bar."),
            "Value line without its number" => ("column",
                new { indicator = BySource, ratio = Ratio, groupBy = "municipality", display = new { referenceLines = Lines(new { kind = "value" }) } },
                "config.display.referenceLines[0].value", "A 'value' line needs its value."),
            "Share past the whole bar" => ("bar",
                new { indicator = BySource, measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "percent", display = new { referenceLines = Lines(new { kind = "value", value = 1.5 }) } },
                "config.display.referenceLines[0].value", "In a 100% layout a value is a share from 0 to 1."),
            "Line break in a label" => ("column",
                new { indicator = BySource, ratio = Ratio, groupBy = "municipality", display = new { referenceLines = Lines(new { kind = "value", value = 0.5, label = "Meta\n2027" }) } },
                "config.display.referenceLines[0].label", "A reference line's label cannot contain line breaks or other control characters."),
            "Steps on a column chart" => ("column",
                new { indicator = BySource, ratio = Ratio, groupBy = "municipality", display = new { interpolation = "step" } },
                "config.display.interpolation", "Only a line chart has an interpolation."),
            _ => ("bar",
                new { indicator = BySource, measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "stacked", display = new { dataLabels = true } },
                "config.display.dataLabels", "Stacked bars have no data labels."),
        };
    }

    [Fact]
    public async Task A_column_chart_with_two_lines_and_a_line_chart_with_steps_and_data_labels_are_saved_and_read_back_as_sent()
    {
        await using var h = await PublishedBySourceAsync();
        var lines = new object[] { new { kind = "mean" }, new { kind = "value", value = 0.5, label = "Meta" } };

        var column = await h.AddWidgetAsync(Annotated("column", new { referenceLines = lines }, "Column"));
        var line = await h.AddWidgetAsync(Annotated("line", new { interpolation = "step", dataLabels = true }, "Line"));

        var sentColumn = JsonSerializer.SerializeToNode(new { referenceLines = lines });
        var sentLine = JsonSerializer.SerializeToNode(new { interpolation = "step", dataLabels = true });
        Assert.True(JsonNode.DeepEquals(sentColumn, JsonNode.Parse(column.GetProperty("config").GetProperty("display").GetRawText())));
        Assert.True(JsonNode.DeepEquals(sentLine, JsonNode.Parse(line.GetProperty("config").GetProperty("display").GetRawText())));
        var read = await h.GetWidgetsAsync(client: h.Viewer);
        var readByTitle = read.EnumerateArray().ToDictionary(w => w.GetProperty("title").GetString()!);
        Assert.True(JsonNode.DeepEquals(sentColumn, JsonNode.Parse(readByTitle["Column"].GetProperty("config").GetProperty("display").GetRawText())));
        Assert.True(JsonNode.DeepEquals(sentLine, JsonNode.Parse(readByTitle["Line"].GetProperty("config").GetProperty("display").GetRawText())));
        Assert.Equal(
            """{"referenceLines": [{"kind": "mean"}, {"kind": "value", "label": "Meta", "value": 0.5}]}""",
            await TestData.ScalarAsync<string>(h.Db, "SELECT config -> 'display' FROM tamiza.dashboard_widgets WHERE id = @id", ("id", column.GetProperty("id").GetGuid())));
    }

    [Fact]
    public async Task A_label_of_spaces_is_saved_as_no_label_and_the_others_are_stored_trimmed()
    {
        await using var h = await PublishedBySourceAsync();

        var created = await h.AddWidgetAsync(Annotated("column", new { referenceLines = new object[] { new { kind = "mean", label = "   " }, new { kind = "median", label = "  Mediana  " } } }));

        var lines = created.GetProperty("config").GetProperty("display").GetProperty("referenceLines");
        Assert.Equal("""[{"kind":"mean"},{"kind":"median","label":"Mediana"}]""", lines.GetRawText());
    }

    [Theory]
    [MemberData(nameof(RefusedAnnotations))]
    public async Task An_annotation_that_breaks_a_rule_is_refused_on_its_key_when_a_widget_is_created_and_nothing_is_saved(string name)
    {
        await using var h = await PublishedBySourceAsync();
        var (chartType, config, key, message) = RefusedAnnotation(name);

        var response = await h.PostWidgetAsync(new { title = name, chartType, config });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], IndicatorsHarness.Errors(await ReadAsync(response), key));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Theory]
    [MemberData(nameof(RefusedAnnotations))]
    public async Task An_annotation_that_breaks_a_rule_is_refused_on_its_key_when_a_widget_is_updated_and_nothing_is_saved(string name)
    {
        await using var h = await PublishedBySourceAsync();
        var (chartType, config, key, message) = RefusedAnnotation(name);
        var created = await h.AddWidgetAsync(Annotated("column", new { color = 2 }));
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionCountAsync(h);

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { chartType, config });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], IndicatorsHarness.Errors(await ReadAsync(response), key));
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_widget_saved_before_reference_lines_reads_without_them_and_keeps_its_stored_text_when_renamed()
    {
        await using var h = await PublishedBySourceAsync();
        var created = await h.AddWidgetAsync(Annotated("column", new { color = 2 }));
        var id = created.GetProperty("id").GetGuid();
        // As an earlier release stored it: a display with a color and nothing else.
        await h.ExecuteAsync(
            """UPDATE tamiza.dashboard_widgets SET config = '{"indicator": "households_by_source", "groupBy": "municipality", "display": {"color": 2}}' WHERE id = @id""", ("id", id));
        var before = await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id));

        var read = (await h.GetWidgetsAsync(client: h.Viewer)).EnumerateArray().Single().GetProperty("config").GetProperty("display");
        var renamed = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { title = "Renamed" });

        Assert.Equal("""{"color":2}""", read.GetRawText());
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(before, await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id)));
    }

    [Fact]
    public async Task A_widget_with_lines_steps_and_data_labels_sent_back_unchanged_writes_nothing_and_makes_no_version()
    {
        await using var h = await PublishedBySourceAsync();
        var display = new
        {
            referenceLines = new object[] { new { kind = "mean" }, new { kind = "overall", label = "General" }, new { kind = "value", value = 0.5, label = "Meta" } },
            interpolation = "linear",
            dataLabels = false,
        };
        var created = await h.AddWidgetAsync(Annotated("line", display));
        var id = created.GetProperty("id").GetGuid();
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionCountAsync(h);
        var read = (await h.GetWidgetsAsync()).EnumerateArray().Single();

        var form = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { title = "Annotated", chartType = "line", config = read.GetProperty("config") });

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
        Assert.Contains("\"linear\"", stored.Single());
        Assert.Contains("false", stored.Single());
    }

    [Fact]
    public async Task A_widget_moved_to_another_dashboard_keeps_its_data_labels_byte_for_byte()
    {
        await using var h = await PublishedBySourceAsync();
        var centro = await h.InsertDashboardAsync("Centro");
        var created = await h.AddWidgetAsync(Annotated("bar", new { dataLabels = true, referenceLines = new object[] { new { kind = "median" } } }));
        var id = created.GetProperty("id").GetGuid();
        var before = await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id));

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = centro });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.GetProperty("config").GetRawText(), moved.GetProperty("config").GetRawText());
        Assert.Equal(before, await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id)));
        var onCentro = (await h.GetWidgetsAsync(centro)).EnumerateArray().Single().GetProperty("config").GetProperty("display");
        Assert.True(onCentro.GetProperty("dataLabels").GetBoolean());
    }

    [Fact]
    public async Task A_chart_type_change_alone_keeps_the_new_settings_in_the_config()
    {
        await using var h = await PublishedBySourceAsync();
        var created = await h.AddWidgetAsync(Annotated("column", new { referenceLines = new object[] { new { kind = "mean" } }, dataLabels = true }));

        var toLine = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { chartType = "line" });
        var toPie = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { chartType = "pie" });

        Assert.Equal(HttpStatusCode.OK, toLine.StatusCode);
        Assert.Equal(created.GetProperty("config").GetRawText(), (await ReadAsync(toLine)).GetProperty("config").GetRawText());
        // The config is kept as it is, so a pie refuses the lines it carries: a client changing to it sends the display it wants.
        Assert.Equal(HttpStatusCode.BadRequest, toPie.StatusCode);
        Assert.Equal(["Only bar, column and line widgets have reference lines."], IndicatorsHarness.Errors(await ReadAsync(toPie), "config.display.referenceLines"));
    }

    [Fact]
    public async Task A_flag_that_is_not_a_setting_of_a_widget_is_not_stored()
    {
        await using var h = await PublishedBySourceAsync();

        var response = await h.PostWidgetAsync(new
        {
            title = "Overall",
            chartType = "column",
            config = new { indicator = BySource, ratio = Ratio, groupBy = "municipality", overall = true, display = new { referenceLines = new object[] { new { kind = "overall" } }, overall = true } },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var config = (await ReadAsync(response)).GetProperty("config");
        Assert.False(config.TryGetProperty("overall", out _));
        Assert.False(config.GetProperty("display").TryGetProperty("overall", out _));
        Assert.DoesNotContain("\"overall\":", (await h.StoredWidgetsAsync()).Single());
    }

    [Fact]
    public async Task The_unfiltered_flag_of_a_stat_tile_is_not_a_setting_of_a_widget_and_is_not_stored()
    {
        await using var h = await PublishedBySourceAsync();

        // The value without the dashboard filters is a flag of the query request, derived from display.secondary: an unknown key is dropped as today.
        var response = await h.PostWidgetAsync(new
        {
            title = "Tile",
            chartType = "stat",
            config = new
            {
                indicator = BySource, ratio = Ratio, unfiltered = true, statistic = "median",
                display = new { secondary = new[] { "n", "unfiltered" }, unfiltered = true },
            },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var config = (await ReadAsync(response)).GetProperty("config");
        Assert.False(config.TryGetProperty("unfiltered", out _));
        Assert.False(config.GetProperty("display").TryGetProperty("unfiltered", out _));
        Assert.Equal("median", config.GetProperty("statistic").GetString());
        Assert.Equal(["n", "unfiltered"], config.GetProperty("display").GetProperty("secondary").EnumerateArray().Select(l => l.GetString()));
        Assert.DoesNotContain("\"unfiltered\":", (await h.StoredWidgetsAsync()).Single());
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

    // Layout of several widgets (design D8 of dashboard versions)

    private static string LayoutUrl(IndicatorsHarness h, Guid? dashboardId = null) => h.DashboardUrl(dashboardId) + "/layout";

    private static object Place(Guid id, int x, int y, int w = 6, int height = 4) => new { id, layout = new { x, y, w, h = height } };

    private static Task<HttpResponseMessage> PutLayoutAsync(IndicatorsHarness h, object[] widgets, HttpClient? client = null, Guid? dashboardId = null) =>
        (client ?? h.Analyst).PutAsJsonAsync(LayoutUrl(h, dashboardId), new { widgets });

    private static async Task<Guid> AddAsync(IndicatorsHarness h, string title, int x, int y, Guid? dashboardId = null) =>
        (await h.AddWidgetAsync(Column(title, new { x, y, w = 6, h = 4 }), dashboardId: dashboardId)).GetProperty("id").GetGuid();

    /// <summary>The dashboard's versions as <c>number changes</c>, oldest first.</summary>
    private static async Task<string[]> VersionsAsync(IndicatorsHarness h, Guid? dashboardId = null) =>
        (await TestData.ScalarAsync<string>(h.Db,
            """
            SELECT coalesce(string_agg(format('%s %s', number, changes), E'\n' ORDER BY number), '')
            FROM tamiza.dashboard_versions WHERE project_id = @project AND dashboard_id = @dashboard
            """,
            ("project", h.ProjectId), ("dashboard", dashboardId ?? h.DashboardId)))!.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task Drag_that_pushes_other_widgets_saves_the_four_new_places_in_one_request_and_one_version()
    {
        await using var h = await PublishedAsync();
        var first = await AddAsync(h, "First", 0, 0);
        var second = await AddAsync(h, "Second", 6, 0);
        var third = await AddAsync(h, "Third", 0, 4);
        var dragged = await AddAsync(h, "Dragged", 6, 4);
        var versions = await VersionsAsync(h);

        // "Dragged" goes to the top left and pushes the three others down.
        var response = await PutLayoutAsync(h, [Place(dragged, 0, 0), Place(first, 0, 4), Place(second, 6, 4), Place(third, 0, 8)]);
        var saved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Dragged", "First", "Second", "Third"], Titles(saved)); // in the order sent
        Assert.Equal([(0, 0, 6, 4), (0, 4, 6, 4), (6, 4, 6, 4), (0, 8, 6, 4)], saved.EnumerateArray().Select(Layout));
        Assert.All(saved.EnumerateArray(), w => Assert.Equal(h.DashboardId, w.GetProperty("dashboardId").GetGuid()));
        Assert.Equal(["Dragged", "First", "Second", "Third"], Titles(await h.GetWidgetsAsync()));
        Assert.Equal(versions.Append("""5 [{"kind": "layoutChanged"}]"""), await VersionsAsync(h));
        var places = await TestData.ScalarAsync<string>(h.Db,
            """
            SELECT string_agg(format('%s %s', widget ->> 'title', widget -> 'layout'), ', ' ORDER BY place)
            FROM tamiza.dashboard_versions, jsonb_array_elements(content -> 'widgets') WITH ORDINALITY AS w (widget, place)
            WHERE dashboard_id = @id AND number = 5
            """,
            ("id", h.DashboardId));
        Assert.Equal(
            """Dragged {"h": 4, "w": 6, "x": 0, "y": 0}, First {"h": 4, "w": 6, "x": 0, "y": 4}, Second {"h": 4, "w": 6, "x": 6, "y": 4}, Third {"h": 4, "w": 6, "x": 0, "y": 8}""",
            places);
    }

    [Fact]
    public async Task A_layout_that_leaves_every_widget_where_it_is_writes_nothing_and_makes_no_version()
    {
        await using var h = await PublishedAsync();
        var first = await AddAsync(h, "First", 0, 0);
        var second = await AddAsync(h, "Second", 6, 0);
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionsAsync(h);

        var response = await PutLayoutAsync(h, [Place(second, 6, 0), Place(first, 0, 0)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Second", "First"], Titles(await ReadAsync(response)));
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionsAsync(h));
    }

    [Fact]
    public async Task A_layout_naming_a_widget_that_is_not_on_the_dashboard_answers_as_its_patch_does_and_saves_nothing()
    {
        await using var h = await PublishedAsync();
        var health = await h.InsertDashboardAsync("Health");
        var mine = await AddAsync(h, "Mine", 0, 0);
        var theirs = await AddAsync(h, "Theirs", 0, 0, health);
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionsAsync(h);

        var elsewhere = await PutLayoutAsync(h, [Place(mine, 6, 0), Place(theirs, 0, 4)]);
        var unknown = await PutLayoutAsync(h, [Place(mine, 6, 0), Place(Guid.NewGuid(), 0, 4)]);
        var patch = await h.Analyst.PatchJsonAsync(h.WidgetUrl(theirs), new { layout = new { x = 0, y = 4, w = 6, h = 4 } });

        foreach (var response in new[] { elsewhere, unknown, patch })
        {
            var problem = await ReadAsync(response);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Widget not found.", problem.GetProperty("title").GetString());
            Assert.Null(Code(problem));
        }

        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionsAsync(h));

        await h.RemoveDashboardAsync(health);
        var removed = await PutLayoutAsync(h, [Place(theirs, 6, 0)], dashboardId: health);

        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal(NotFoundCode, Code(await ReadAsync(removed)));
    }

    [Fact]
    public async Task Duplicate_ids_more_entries_than_the_widget_limit_and_a_layout_the_patch_refuses_answer_400_and_save_nothing()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "2" });
        var first = await AddAsync(h, "First", 0, 0);
        var second = await AddAsync(h, "Second", 6, 0);
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionsAsync(h);

        var duplicate = await PutLayoutAsync(h, [Place(first, 6, 4), Place(first, 0, 8)]);
        var tooMany = await PutLayoutAsync(h, [Place(first, 0, 4), Place(second, 6, 4), Place(Guid.NewGuid(), 0, 8)]);
        var none = await PutLayoutAsync(h, []);
        var missing = await h.Analyst.PutAsJsonAsync(LayoutUrl(h), new { });
        var invalid = await PutLayoutAsync(h, [Place(first, 8, 0), new { id = second }]);
        var anonymous = await PutLayoutAsync(h, [new { layout = new { x = 0, y = 0, w = 6, h = 4 } }]);
        var patch = await h.Analyst.PatchJsonAsync(h.WidgetUrl(first), new { layout = new { x = 8, y = 0, w = 6, h = 4 } });

        foreach (var response in new[] { duplicate, tooMany, none, missing, invalid, anonymous, patch })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(["The widget is listed more than once."], IndicatorsHarness.Errors(await ReadAsync(duplicate), "widgets[1].id"));
        Assert.Equal(["Send the layout of 1 to 2 widgets."], IndicatorsHarness.Errors(await ReadAsync(tooMany), "widgets"));
        Assert.Equal(["Send the layout of 1 to 2 widgets."], IndicatorsHarness.Errors(await ReadAsync(none), "widgets"));
        Assert.Equal(["Send the layout of 1 to 2 widgets."], IndicatorsHarness.Errors(await ReadAsync(missing), "widgets"));
        var errors = await ReadAsync(invalid);
        // Each entry's layout is refused as the PATCH refuses it, under the entry's key.
        Assert.Equal(IndicatorsHarness.Errors(await ReadAsync(patch), "layout.w"), IndicatorsHarness.Errors(errors, "widgets[0].layout.w"));
        Assert.Equal(["The layout is required."], IndicatorsHarness.Errors(errors, "widgets[1].layout"));
        Assert.Equal(["The widget's id is required."], IndicatorsHarness.Errors(await ReadAsync(anonymous), "widgets[0].id"));
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionsAsync(h));
    }

    [Theory]
    [InlineData("viewer", HttpStatusCode.Forbidden)]
    [InlineData("outsider", HttpStatusCode.NotFound)]
    public async Task A_viewer_gets_403_for_a_layout_and_a_user_who_is_not_a_member_404_and_nothing_is_saved(string role, HttpStatusCode expected)
    {
        await using var h = await PublishedAsync();
        var first = await AddAsync(h, "First", 0, 0);
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionsAsync(h);

        var response = await PutLayoutAsync(h, [Place(first, 6, 0)], role == "viewer" ? h.Viewer : h.Outsider);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionsAsync(h));
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

    // The own order of a widget's values (dashboard-indicators: Declared category order of a widget)

    private static object Ordered(string chartType, object? display, string? groupBy = "municipality", string? seriesBy = "water_source", object? sort = null, string title = "Ordered") => new
    {
        title,
        chartType,
        config = new { indicator = BySource, measure = "value", groupBy, seriesBy, sort, display },
    };

    private static readonly object ByValueDesc = new { by = "value", direction = "desc" };

    [Fact]
    public async Task A_widget_is_saved_with_its_own_order_of_groups_and_of_series_and_returns_it()
    {
        await using var h = await PublishedBySourceAsync();

        var created = await h.AddWidgetAsync(Ordered("column", new { order = new { group = new[] { "Palmira", "Cali" }, series = new[] { "Well", "Piped" } } }));

        var order = created.GetProperty("config").GetProperty("display").GetProperty("order");
        Assert.Equal(["Palmira", "Cali"], IndicatorsHarness.Strings(order.GetProperty("group")));
        Assert.Equal(["Well", "Piped"], IndicatorsHarness.Strings(order.GetProperty("series")));
        var read = (await h.GetWidgetsAsync(client: h.Viewer))[0];
        Assert.Equal(order.GetRawText(), read.GetProperty("config").GetProperty("display").GetProperty("order").GetRawText());
        Assert.Equal(
            """{"order": {"group": ["Palmira", "Cali"], "series": ["Well", "Piped"]}}""",
            await TestData.ScalarAsync<string>(h.Db, "SELECT config -> 'display' FROM tamiza.dashboard_widgets WHERE id = @id", ("id", created.GetProperty("id").GetGuid())));
    }

    [Fact]
    public async Task A_widget_with_no_series_is_saved_with_an_order_of_groups_and_a_sorted_one_with_an_order_of_series()
    {
        await using var h = await PublishedBySourceAsync();

        var bar = await h.AddWidgetAsync(Ordered("bar", new { order = new { group = new[] { "Palmira" } } }, seriesBy: null, title: "Bar"));
        var sorted = await h.AddWidgetAsync(Ordered("bar", new { order = new { series = new[] { "Well" } } }, sort: ByValueDesc, title: "Sorted"));

        Assert.Equal(["Palmira"], IndicatorsHarness.Strings(bar.GetProperty("config").GetProperty("display").GetProperty("order").GetProperty("group")));
        Assert.Equal(["Well"], IndicatorsHarness.Strings(sorted.GetProperty("config").GetProperty("display").GetProperty("order").GetProperty("series")));
    }

    [Fact]
    public async Task An_empty_order_is_saved_as_none_and_the_widget_is_stored_without_a_display()
    {
        await using var h = await PublishedBySourceAsync();

        var created = await h.AddWidgetAsync(Ordered("column", new { order = new { group = Array.Empty<string>(), series = Array.Empty<string>() } }));

        Assert.False(created.GetProperty("config").TryGetProperty("display", out _));
        Assert.DoesNotContain("display", (await h.StoredWidgetsAsync()).Single());
        Assert.DoesNotContain("order", (await h.StoredWidgetsAsync()).Single());
    }

    [Fact]
    public async Task A_widget_updated_with_an_order_then_with_none_is_stored_each_time_as_sent()
    {
        await using var h = await PublishedBySourceAsync();
        var created = await h.AddWidgetAsync(Ordered("column", new { color = (int?)null, order = new { series = new[] { "Well", "Piped" } } }));
        var id = created.GetProperty("id").GetGuid();

        var cleared = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { config = new { indicator = BySource, measure = "value", groupBy = "municipality", seriesBy = "water_source" } });
        var again = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { config = new { indicator = BySource, measure = "value", groupBy = "municipality", seriesBy = "water_source", display = new { order = new { group = new[] { "Palmira" } } } } });

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.False((await ReadAsync(cleared)).GetProperty("config").TryGetProperty("display", out _));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(["Palmira"], IndicatorsHarness.Strings((await ReadAsync(again)).GetProperty("config").GetProperty("display").GetProperty("order").GetProperty("group")));
    }

    public static TheoryData<string, string> RefusedOrders() => new()
    {
        { "stat", "A stat widget has no groups to order." },
        { "histogram", "A histogram widget has no groups to order." },
        { "map", "A map widget has no groups to order." },
    };

    [Theory]
    [MemberData(nameof(RefusedOrders))]
    public async Task An_order_of_groups_on_a_stat_tile_a_histogram_or_a_map_is_refused_and_nothing_is_saved(string chartType, string message)
    {
        await using var h = await PublishedBySourceAsync();

        var response = await h.PostWidgetAsync(new
        {
            title = "Ordered",
            chartType,
            config = new { indicator = BySource, measure = "value", groupBy = "municipality", geometryColumn = chartType == "map" ? "municipality" : null, display = new { order = new { group = new[] { "Cali" } } } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.order.group"));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task An_order_of_groups_without_a_group_column_is_refused()
    {
        await using var h = await PublishedBySourceAsync();

        var response = await h.PostWidgetAsync(Ordered("boxplot", new { order = new { group = new[] { "Cali" } } }, groupBy: null, seriesBy: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Ordering groups needs a column to group by."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.order.group"));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task An_order_of_series_without_a_series_column_is_refused()
    {
        await using var h = await PublishedBySourceAsync();

        var response = await h.PostWidgetAsync(Ordered("column", new { order = new { series = new[] { "Well" } } }, seriesBy: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Only a widget with a series has an order of series."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.order.series"));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task An_order_of_series_on_a_pyramid_or_a_Likert_widget_is_refused_saying_what_sets_that_order()
    {
        await using var h = await PublishedBySourceAsync();
        var display = new { order = new { series = new[] { "Well" } } };

        var pyramid = await h.PostWidgetAsync(new
        {
            title = "Pyramid", chartType = "pyramid",
            config = new { indicator = BySource, measure = "value", groupBy = "municipality", seriesBy = "water_source", pyramid = new { left = "Piped", right = "Well" }, display },
        });
        var likert = await h.PostWidgetAsync(new
        {
            title = "Likert", chartType = "likert",
            config = new
            {
                indicator = BySource, measure = "value", groupBy = "municipality", seriesBy = "water_source", display,
                scale = new object[] { new { value = "Piped", side = "negative" }, new { value = "Well", side = "positive" } },
            },
        });

        Assert.Equal(["A population pyramid's sides are its left and right values."], IndicatorsHarness.Errors(await ReadAsync(pyramid), "config.display.order.series"));
        Assert.Equal(["A Likert widget's answers follow its scale."], IndicatorsHarness.Errors(await ReadAsync(likert), "config.display.order.series"));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task An_order_of_101_values_and_a_value_listed_twice_are_refused_on_their_own_keys()
    {
        await using var h = await PublishedBySourceAsync();

        var response = await h.PostWidgetAsync(Ordered("column", new
        {
            order = new
            {
                group = Enumerable.Range(1, 101).Select(i => $"Value {i}").ToArray(),
                series = new[] { "Well", "Piped", "Well" },
            },
        }));
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["An order lists at most 100 values."], IndicatorsHarness.Errors(problem, "config.display.order.group"));
        Assert.Equal(["'Well' is listed twice."], IndicatorsHarness.Errors(problem, "config.display.order.series[2]"));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task A_sort_and_an_own_order_of_groups_are_refused_together_and_the_order_of_series_is_not()
    {
        await using var h = await PublishedBySourceAsync();

        var refused = await h.PostWidgetAsync(Ordered("column", new { order = new { group = new[] { "Cali" } } }, seriesBy: null, sort: ByValueDesc));
        var accepted = await h.PostWidgetAsync(Ordered("column", new { order = new { series = new[] { "Well" } } }, sort: ByValueDesc));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            ["A sorted widget takes the order of its groups from its sort: remove the sort or this order."],
            IndicatorsHarness.Errors(await ReadAsync(refused), "config.display.order.group"));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task An_order_by_median_and_an_own_order_of_boxes_are_refused_together()
    {
        await using var h = await PublishedBySourceAsync();

        var refused = await h.PostWidgetAsync(Ordered("boxplot", new { order = new { group = new[] { "Cali" } } }, seriesBy: null, sort: ByValueDesc));
        var accepted = await h.PostWidgetAsync(Ordered("boxplot", new { order = new { group = new[] { "Cali" } } }, seriesBy: null, title: "Boxes"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            ["A box plot ordered by median takes the order of its boxes from their medians: remove that order or this one."],
            IndicatorsHarness.Errors(await ReadAsync(refused), "config.display.order.group"));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task A_chart_type_change_that_keeps_an_order_of_series_the_new_kind_does_not_take_is_refused()
    {
        await using var h = await PublishedBySourceAsync();
        var created = await h.AddWidgetAsync(Ordered("column", new { order = new { series = new[] { "Well", "Piped" } } }));
        var stored = await h.StoredWidgetsAsync();

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { chartType = "pie" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("config.display.order.series", (await ReadAsync(response)).GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Equal(stored, await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task A_widget_with_an_order_sent_back_unchanged_writes_nothing_and_makes_no_version()
    {
        await using var h = await PublishedBySourceAsync();
        var created = await h.AddWidgetAsync(Ordered("column", new { order = new { group = new[] { "Palmira" }, series = new[] { "Well", "Piped" } } }));
        var id = created.GetProperty("id").GetGuid();
        var stored = await h.StoredWidgetsAsync();
        var versions = await VersionCountAsync(h);
        var read = (await h.GetWidgetsAsync()).EnumerateArray().Single();

        var form = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { title = "Ordered", chartType = "column", config = read.GetProperty("config") });

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal(stored, await h.StoredWidgetsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_change_of_the_order_makes_a_version_that_says_the_widget_changed()
    {
        await using var h = await PublishedBySourceAsync();
        var created = await h.AddWidgetAsync(Ordered("column", new { order = new { series = new[] { "Well", "Piped" } } }));
        var versions = await VersionCountAsync(h);

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new
        {
            config = new { indicator = BySource, measure = "value", groupBy = "municipality", seriesBy = "water_source", display = new { order = new { series = new[] { "Piped", "Well" } } } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(versions + 1, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_widget_moved_to_another_dashboard_keeps_its_own_order_byte_for_byte()
    {
        await using var h = await PublishedBySourceAsync();
        var cruces = await h.InsertDashboardAsync("Cruces");
        var created = await h.AddWidgetAsync(Ordered("column", new { order = new { group = new[] { "Palmira", "Cali" }, series = new[] { "Well", "Piped" } } }));
        var id = created.GetProperty("id").GetGuid();
        var before = await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id));

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { dashboardId = cruces });
        var moved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.GetProperty("config").GetRawText(), moved.GetProperty("config").GetRawText());
        Assert.Equal(before, await TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", id)));
        var onCruces = (await h.GetWidgetsAsync(cruces))[0];
        Assert.Equal(["Well", "Piped"], IndicatorsHarness.Strings(onCruces.GetProperty("config").GetProperty("display").GetProperty("order").GetProperty("series")));
    }
}
