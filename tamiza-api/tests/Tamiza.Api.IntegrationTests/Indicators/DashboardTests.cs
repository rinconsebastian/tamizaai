using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// A project's own dashboards: adding, renaming, removing, reordering and duplicating them through
/// <c>/projects/{id}/dashboards</c>, their names, their limits, and what two members changing them at once get.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardTests(PostgresFixture postgres)
{
    private const string NotFoundCode = "dashboards.notFound";
    private const string LimitCode = "dashboards.limit";
    private const string LastOneCode = "dashboards.lastOne";
    private const string OrderChangedCode = "dashboards.orderChanged";
    private const string LimitMessage = "A project can have at most 7 dashboards. Remove one before adding another.";

    private static readonly string FortyOne = new('n', 41);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? Code(JsonElement problem) => problem.TryGetProperty("code", out var code) ? code.GetString() : null;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string? detail = null)
    {
        var problem = await ReadAsync(response);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, Code(problem));
        if (detail is not null)
        {
            Assert.Equal(detail, problem.GetProperty("detail").GetString());
        }
    }

    private static async Task AssertNameErrorAsync(HttpResponseMessage response, string error)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([error], IndicatorsHarness.Errors(await ReadAsync(response), "name"));
    }

    private static string Taken(string name) => $"Another dashboard of this project is named \"{name}\".";

    private static Task<HttpResponseMessage> RenameAsync(IndicatorsHarness h, Guid dashboardId, string name, HttpClient? client = null) =>
        (client ?? h.Analyst).PatchJsonAsync(h.DashboardUrl(dashboardId), new { name });

    private static Task<HttpResponseMessage> RemoveAsync(IndicatorsHarness h, Guid dashboardId, HttpClient? client = null) =>
        (client ?? h.Analyst).DeleteAsync(h.DashboardUrl(dashboardId));

    private static Task<HttpResponseMessage> ReorderAsync(
        IndicatorsHarness h, IEnumerable<Guid>? expectedIds, IEnumerable<Guid>? dashboardIds, HttpClient? client = null) =>
        (client ?? h.Analyst).PutAsJsonAsync(h.DashboardsUrl + "/order", new { expectedIds, dashboardIds });

    private static Task<HttpResponseMessage> DuplicateAsync(IndicatorsHarness h, Guid dashboardId, string name, HttpClient? client = null) =>
        (client ?? h.Analyst).PostAsJsonAsync(h.DashboardUrl(dashboardId) + "/copies", new { name });

    private static async Task<string[]> NamesAsync(IndicatorsHarness h) => (await h.DashboardsAsync()).Select(d => d.Name).ToArray();

    private static async Task<int[]> PositionsAsync(IndicatorsHarness h) => (await h.DashboardsAsync()).Select(d => d.Position).ToArray();

    private static string[] Titles(JsonElement widgets) => widgets.EnumerateArray().Select(w => w.GetProperty("title").GetString()!).ToArray();

    private static string[] Ids(JsonElement entries) => entries.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();

    /// <summary>"Housing", "Health" and "Field", in that order: the seeded "Indicators" renamed with SQL, then two added through the API.</summary>
    private static async Task<(Guid Housing, Guid Health, Guid Field)> ThreeAsync(IndicatorsHarness h)
    {
        await h.ExecuteAsync("UPDATE tamiza.dashboards SET name = 'Housing', normalized_name = 'housing' WHERE id = @id", ("id", h.DashboardId));
        return (h.DashboardId, await h.AddDashboardAsync("Health"), await h.AddDashboardAsync("Field"));
    }

    /// <summary>Adds dashboards with SQL until the project has <paramref name="count"/>.</summary>
    private static async Task FillAsync(IndicatorsHarness h, int count)
    {
        for (var i = (await h.DashboardsAsync()).Length; i < count; i++)
        {
            await h.InsertDashboardAsync($"Filler {i}");
        }
    }

    private Task<IndicatorsHarness> CreateAsync() => IndicatorsHarness.CreateAsync(postgres);

    /// <summary>A project whose <c>coverage</c> indicator has published, for the tests that need widgets.</summary>
    private async Task<IndicatorsHarness> PublishedAsync(Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 40, 4, 10), ('Palmira', 60, 6, 10)");
        return h;
    }

    /// <summary>Eight widgets of every kind but a map, each in its own place, as a dashboard an analyst built.</summary>
    private static object[] EightWidgets() =>
    [
        new { title = "Total", chartType = "stat", config = new { indicator = "coverage" }, layout = new { x = 0, y = 0, w = 3, h = 2 } },
        new
        {
            title = "Rate", chartType = "stat",
            config = new { indicator = "coverage", ratio = new { numerator = "numerator", denominator = "denominator" } },
            layout = new { x = 3, y = 0, w = 3, h = 2 },
        },
        new { title = "By municipality", chartType = "column", config = new { indicator = "coverage", groupBy = "municipality" }, layout = new { x = 6, y = 0, w = 6, h = 4 } },
        new
        {
            title = "Ranked", chartType = "bar",
            config = new { indicator = "coverage", groupBy = "municipality", sort = new { by = "value", direction = "desc" } },
            layout = new { x = 0, y = 2, w = 6, h = 4 },
        },
        new { title = "Trend", chartType = "line", config = new { indicator = "coverage", groupBy = "municipality" }, layout = new { x = 6, y = 4, w = 6, h = 4 } },
        new { title = "Share", chartType = "pie", config = new { indicator = "coverage", groupBy = "municipality" }, layout = new { x = 0, y = 6, w = 6, h = 5 } },
        new { title = "Spread", chartType = "histogram", config = new { indicator = "coverage", measure = "value", buckets = 5 }, layout = new { x = 6, y = 8, w = 6, h = 4 } },
        new
        {
            title = "Cali only", chartType = "column",
            config = new
            {
                indicator = "coverage", groupBy = "municipality",
                filters = new[] { new { column = "municipality", op = "equals", value = "Cali" } },
            },
            layout = new { x = 0, y = 11, w = 12, h = 3 },
        },
    ];

    private static async Task AddEightAsync(IndicatorsHarness h, Guid dashboardId)
    {
        foreach (var widget in EightWidgets())
        {
            await h.AddWidgetAsync(widget, dashboardId: dashboardId);
        }
    }

    // Analyst adds a dashboard

    [Fact]
    public async Task An_analyst_adds_a_dashboard_after_the_others_with_no_widgets()
    {
        await using var h = await CreateAsync();

        var response = await h.PostDashboardAsync("Field progress");
        var added = await ReadAsync(response);
        var id = added.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/projects/{h.ProjectId}/dashboards/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal("Field progress", added.GetProperty("name").GetString());
        Assert.Equal([(h.DashboardId, "Indicators", 0), (id, "Field progress", 1)], await h.DashboardsAsync());
        foreach (var client in new[] { h.Viewer, h.Analyst, h.Admin })
        {
            var dashboard = await h.GetDashboardAsync(id, client);
            Assert.Equal("Field progress", dashboard.GetProperty("name").GetString());
            Assert.Equal(0, dashboard.GetProperty("widgets").GetArrayLength());
        }
    }

    // Eighth dashboard

    [Fact]
    public async Task An_eighth_dashboard_is_refused_with_the_limit()
    {
        await using var h = await CreateAsync();
        await FillAsync(h, 7);
        var before = await h.StoredDashboardsAsync();

        var response = await h.PostDashboardAsync("Eighth", h.Admin);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, LimitCode, LimitMessage);
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    // Two added at the same moment

    [Fact]
    public async Task Of_eight_dashboards_added_at_the_same_moment_to_a_project_with_six_one_is_added()
    {
        await using var h = await CreateAsync();
        await FillAsync(h, 6);
        // Each insert takes a moment, so every request has counted before the first one commits unless the count waits for it.
        await h.ExecuteAsync(
            """
            CREATE FUNCTION tamiza.slow_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_sleep(0.3);
                RETURN NEW;
            END $$;
            CREATE TRIGGER slow_insert BEFORE INSERT ON tamiza.dashboards FOR EACH ROW EXECUTE FUNCTION tamiza.slow_insert();
            """);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => h.PostDashboardAsync($"Racing {i}", i % 2 == 0 ? h.Analyst : h.Admin)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, LimitCode);
        }

        Assert.Equal(Enumerable.Range(0, 7), await PositionsAsync(h));
    }

    // Name trimmed

    [Fact]
    public async Task A_name_is_saved_and_answered_trimmed_when_added_and_when_renamed()
    {
        await using var h = await CreateAsync();

        var added = await h.PostDashboardAsync("  Salud  ");
        var renamed = await RenameAsync(h, h.DashboardId, "  Coverage  ");

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal("Salud", (await ReadAsync(added)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Coverage", (await ReadAsync(renamed)).GetProperty("name").GetString());
        Assert.Equal(["Coverage", "Salud"], await NamesAsync(h));
        Assert.Equal(["coverage", "salud"], (await h.StoredDashboardsAsync()).Select(row => row.Split('|')[3]).ToArray());
    }

    // Name too long, Name of a permanent tab

    public static TheoryData<string, string> RefusedNames() => new()
    {
        { new string('n', 41), "The name can have at most 40 characters." },
        { "  " + new string('n', 41) + "  ", "The name can have at most 40 characters." },
        { "clean data", "\"Clean data\" is the name of a permanent tab. Choose another name." },
        { "PERFORMANCE", "\"Performance\" is the name of a permanent tab. Choose another name." },
        { "  Settings  ", "\"Settings\" is the name of a permanent tab. Choose another name." },
        { "   ", "The name is required." },
        { "Field\nprogress", "The name cannot contain control characters." },
    };

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task A_refused_name_is_a_400_on_the_name_and_neither_adds_nor_renames(string name, string error)
    {
        await using var h = await CreateAsync();
        var health = await h.AddDashboardAsync("Health");
        var before = await h.StoredDashboardsAsync();

        await AssertNameErrorAsync(await h.PostDashboardAsync(name), error);
        await AssertNameErrorAsync(await RenameAsync(h, health, name), error);

        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_name_of_40_characters_is_accepted()
    {
        await using var h = await CreateAsync();
        var forty = new string('n', 40);

        var added = await h.PostDashboardAsync(forty);
        var renamed = await RenameAsync(h, h.DashboardId, new string('m', 40));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal([new string('m', 40), forty], await NamesAsync(h));
    }

    [Fact]
    public async Task A_body_without_a_name_is_a_400_on_the_name()
    {
        await using var h = await CreateAsync();

        var added = await h.Analyst.PostAsJsonAsync(h.DashboardsUrl, new { });
        var renamed = await h.Analyst.PatchJsonAsync(h.DashboardUrl(), new { });

        await AssertNameErrorAsync(added, "The name is required.");
        await AssertNameErrorAsync(renamed, "The name is required.");
        Assert.Equal(["Indicators"], await NamesAsync(h));
    }

    // Name taken

    [Fact]
    public async Task A_name_another_dashboard_of_the_project_has_in_any_case_is_refused_on_add_and_rename()
    {
        await using var h = await CreateAsync();
        var salud = await h.AddDashboardAsync("Salud");
        var before = await h.StoredDashboardsAsync();

        await AssertNameErrorAsync(await h.PostDashboardAsync("SALUD"), Taken("Salud"));
        await AssertNameErrorAsync(await RenameAsync(h, h.DashboardId, " salud "), Taken("Salud"));
        Assert.Equal(before, await h.StoredDashboardsAsync());

        // A dashboard's own name in another case is not another dashboard's.
        var recased = await RenameAsync(h, salud, "SALUD");
        Assert.Equal(HttpStatusCode.OK, recased.StatusCode);
        Assert.Equal(["Indicators", "SALUD"], await NamesAsync(h));
    }

    // Same name in another project

    [Fact]
    public async Task A_name_a_dashboard_of_another_project_has_is_accepted()
    {
        await using var h = await CreateAsync();
        var (otherId, otherDashboard) = await h.AddOtherProjectAsync();
        var renamed = await h.Admin.PatchJsonAsync(IndicatorsHarness.DashboardUrlOf(otherId, otherDashboard), new { name = "Salud" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var response = await h.PostDashboardAsync("Salud");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["Indicators", "Salud"], await NamesAsync(h));
    }

    // Analyst renames a dashboard

    [Fact]
    public async Task An_analyst_renames_a_dashboard_and_its_widgets_place_and_address_stay()
    {
        await using var h = await PublishedAsync();
        var (housing, health, field) = await ThreeAsync(h);
        await h.AddWidgetAsync(EightWidgets()[0], dashboardId: health);
        await h.AddWidgetAsync(EightWidgets()[2], dashboardId: health);
        var widgets = await h.StoredWidgetsAsync();

        var response = await RenameAsync(h, health, "Coverage");
        var renamed = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((health, "Coverage"), (renamed.GetProperty("id").GetGuid(), renamed.GetProperty("name").GetString()));
        Assert.Equal([(housing, "Housing", 0), (health, "Coverage", 1), (field, "Field", 2)], await h.DashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
        var dashboard = await h.GetDashboardAsync(health, h.Viewer);
        Assert.Equal("Coverage", dashboard.GetProperty("name").GetString());
        Assert.Equal(["Total", "By municipality"], Titles(dashboard.GetProperty("widgets")));
    }

    // Two renames

    [Fact]
    public async Task Of_two_renames_of_the_same_dashboard_the_last_wins()
    {
        await using var h = await CreateAsync();

        var first = await RenameAsync(h, h.DashboardId, "Salud", h.Analyst);
        var second = await RenameAsync(h, h.DashboardId, "Health", h.Admin);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Health", (await h.GetDashboardAsync()).GetProperty("name").GetString());
    }

    // Dashboard with widgets removed

    [Fact]
    public async Task A_dashboard_removed_takes_its_widgets_with_it_for_every_member()
    {
        await using var h = await PublishedAsync();
        var (housing, health, field) = await ThreeAsync(h);
        var kept = (await h.AddWidgetAsync(EightWidgets()[0], dashboardId: housing)).GetProperty("title").GetString();
        foreach (var widget in EightWidgets().Take(5))
        {
            await h.AddWidgetAsync(widget, dashboardId: health);
        }

        var response = await RemoveAsync(h, health);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([(housing, "Housing", 0), (field, "Field", 1)], await h.DashboardsAsync());
        Assert.Equal([kept!], (await h.StoredWidgetsAsync()).Select(row => row.Split('|')[0]).ToArray());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_widgets WHERE dashboard_id = @id", ("id", health)));
        foreach (var client in new[] { h.Viewer, h.Analyst })
        {
            await AssertProblemAsync(await client.GetAsync(h.DashboardUrl(health)), HttpStatusCode.NotFound, NotFoundCode);
        }
    }

    // Only dashboard

    [Fact]
    public async Task The_only_dashboard_of_a_project_cannot_be_removed()
    {
        await using var h = await PublishedAsync();
        await h.AddWidgetAsync(EightWidgets()[0]);
        var dashboards = await h.StoredDashboardsAsync();
        var widgets = await h.StoredWidgetsAsync();

        var response = await RemoveAsync(h, h.DashboardId, h.Admin);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, LastOneCode, "A project needs at least one dashboard.");
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
    }

    // Last two removed at the same moment

    [Fact]
    public async Task Of_the_last_two_dashboards_removed_at_the_same_moment_one_stays()
    {
        await using var h = await CreateAsync();
        // Each removal takes a moment, so both have counted two dashboards before either commits unless the count waits for the other.
        await h.ExecuteAsync(
            """
            CREATE FUNCTION tamiza.slow_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_sleep(0.3);
                RETURN OLD;
            END $$;
            CREATE TRIGGER slow_delete BEFORE DELETE ON tamiza.dashboards FOR EACH ROW EXECUTE FUNCTION tamiza.slow_delete();
            """);
        for (var round = 0; round < 3; round++)
        {
            var first = (await h.DashboardsAsync()).Single().Id;
            var second = await h.AddDashboardAsync($"Round {round}");

            var responses = await Task.WhenAll(RemoveAsync(h, first, h.Analyst), RemoveAsync(h, second, h.Admin));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
            await AssertProblemAsync(responses.Single(r => r.StatusCode != HttpStatusCode.NoContent), HttpStatusCode.Conflict, LastOneCode);
            Assert.Equal(0, Assert.Single(await PositionsAsync(h)));
        }
    }

    [Fact]
    public async Task A_removal_renumbers_the_dashboards_after_it()
    {
        await using var h = await CreateAsync();
        var housing = await h.AddDashboardAsync("Housing");
        var health = await h.AddDashboardAsync("Health");
        var field = await h.AddDashboardAsync("Field");

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(h, housing)).StatusCode);
        Assert.Equal([(h.DashboardId, "Indicators", 0), (health, "Health", 1), (field, "Field", 2)], await h.DashboardsAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(h, h.DashboardId)).StatusCode);
        Assert.Equal([(health, "Health", 0), (field, "Field", 1)], await h.DashboardsAsync());

        // A dashboard added afterwards is placed last, not over another.
        var added = await h.AddDashboardAsync("Added");
        Assert.Equal([(health, "Health", 0), (field, "Field", 1), (added, "Added", 2)], await h.DashboardsAsync());
    }

    [Fact]
    public async Task An_add_renumbers_positions_left_with_a_gap_or_a_tie()
    {
        await using var h = await CreateAsync();
        var health = await h.InsertDashboardAsync("Health");
        var field = await h.InsertDashboardAsync("Field");
        // A gap after "Indicators" and a tie between "Health" and "Field", which the order still lists by creation.
        await h.ExecuteAsync("UPDATE tamiza.dashboards SET position = 5 WHERE id IN (@health, @field)", ("health", health), ("field", field));

        var added = await h.AddDashboardAsync("Added");

        Assert.Equal([(h.DashboardId, "Indicators", 0), (health, "Health", 1), (field, "Field", 2), (added, "Added", 3)], await h.DashboardsAsync());
    }

    // Dashboard of another project (custom-dashboards)

    [Fact]
    public async Task A_dashboard_of_another_project_is_not_found_under_this_projects_address()
    {
        await using var h = await CreateAsync();
        var (otherId, otherDashboard) = await h.AddOtherProjectAsync();
        await h.AddDashboardAsync("Health");
        var before = await h.StoredDashboardsAsync();
        var otherBefore = await TestData.ScalarAsync<string>(h.Db,
            "SELECT string_agg(concat_ws('|', id, name, position, updated_at), ',') FROM tamiza.dashboards WHERE project_id = @id", ("id", otherId));

        // The admin is a member of both projects: the other project's dashboard is still not one of this project's.
        HttpResponseMessage[] responses =
        [
            await h.Admin.GetAsync(h.DashboardUrl(otherDashboard)),
            await RenameAsync(h, otherDashboard, "Taken over", h.Admin),
            await RemoveAsync(h, otherDashboard, h.Admin),
            await DuplicateAsync(h, otherDashboard, "Copied over", h.Admin),
        ];

        foreach (var response in responses)
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, NotFoundCode);
        }

        Assert.Equal(before, await h.StoredDashboardsAsync());
        Assert.Equal(otherBefore, await TestData.ScalarAsync<string>(h.Db,
            "SELECT string_agg(concat_ws('|', id, name, position, updated_at), ',') FROM tamiza.dashboards WHERE project_id = @id", ("id", otherId)));
    }

    // A change to a dashboard removed meanwhile

    [Fact]
    public async Task A_rename_or_removal_of_a_removed_dashboard_is_not_found_and_changes_nothing()
    {
        await using var h = await CreateAsync();
        var health = await h.AddDashboardAsync("Health");
        await h.AddDashboardAsync("Field");
        await h.RemoveDashboardAsync(health);
        var before = await h.StoredDashboardsAsync();

        await AssertProblemAsync(await RenameAsync(h, health, "Salud"), HttpStatusCode.NotFound, NotFoundCode);
        await AssertProblemAsync(await RenameAsync(h, health, FortyOne), HttpStatusCode.NotFound, NotFoundCode); // the dashboard is checked first
        await AssertProblemAsync(await RemoveAsync(h, health), HttpStatusCode.NotFound, NotFoundCode);

        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_dashboard_removed_between_the_check_and_the_rename_is_not_found_never_500()
    {
        await using var h = await CreateAsync();
        var health = await h.AddDashboardAsync("Health");
        // The removal lands after the rename has checked the dashboard: the update then finds no row.
        await h.ExecuteAsync(
            """
            CREATE FUNCTION tamiza.remove_before_update() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                DELETE FROM tamiza.dashboards WHERE id = OLD.id;
                RETURN NULL;
            END $$;
            CREATE TRIGGER remove_before_update BEFORE UPDATE ON tamiza.dashboards
                FOR EACH ROW EXECUTE FUNCTION tamiza.remove_before_update();
            """);

        var response = await RenameAsync(h, health, "Salud");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, NotFoundCode);
        Assert.Equal(["Indicators"], await NamesAsync(h));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("rename")]
    [InlineData("duplicate")]
    public async Task A_name_taken_by_another_request_after_the_check_is_a_400_on_the_name_never_500(string action)
    {
        await using var h = await CreateAsync();
        var health = await h.AddDashboardAsync("Health");
        var before = await h.StoredDashboardsAsync();
        // Another member's rename gives a dashboard of the project the same name just before this request writes its own.
        await h.ExecuteAsync(
            $"""
            CREATE FUNCTION tamiza.take_the_name_first() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF pg_trigger_depth() = 1 THEN
                    INSERT INTO tamiza.dashboards (project_id, name, normalized_name, position, created_by, updated_by)
                    VALUES (NEW.project_id, NEW.name, NEW.normalized_name, 99, NEW.created_by, NEW.updated_by);
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER take_the_name_first BEFORE {(action == "rename" ? "UPDATE" : "INSERT")} ON tamiza.dashboards
                FOR EACH ROW EXECUTE FUNCTION tamiza.take_the_name_first();
            """);

        var response = action switch
        {
            "add" => await h.PostDashboardAsync("Salud"),
            "rename" => await RenameAsync(h, health, "Salud"),
            _ => await DuplicateAsync(h, health, "Salud"),
        };

        await AssertNameErrorAsync(response, Taken("Salud"));
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    // Move right

    [Fact]
    public async Task Moving_a_dashboard_right_saves_the_new_order_and_answers_it()
    {
        await using var h = await CreateAsync();
        var (housing, health, field) = await ThreeAsync(h);

        var response = await ReorderAsync(h, [housing, health, field], [health, housing, field]);
        var order = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([health.ToString(), housing.ToString(), field.ToString()], Ids(order));
        Assert.Equal(["Health", "Housing", "Field"], order.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToArray());
        Assert.Equal([(health, "Health", 0), (housing, "Housing", 1), (field, "Field", 2)], await h.DashboardsAsync());
    }

    [Fact]
    public async Task Two_moves_in_a_row_each_from_the_order_the_last_one_answered()
    {
        await using var h = await CreateAsync();
        var (housing, health, field) = await ThreeAsync(h);

        var first = await ReorderAsync(h, [housing, health, field], [health, housing, field]);
        var second = await ReorderAsync(h, [health, housing, field], [health, field, housing]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(["Health", "Field", "Housing"], await NamesAsync(h));
    }

    // Order changed meanwhile

    [Fact]
    public async Task A_move_from_a_page_that_missed_an_added_dashboard_is_refused_and_changes_nothing()
    {
        await using var h = await CreateAsync();
        var housing = h.DashboardId;
        var health = await h.AddDashboardAsync("Health");
        // Another member adds a dashboard the analyst's page does not show.
        await h.AddDashboardAsync("Field", h.Admin);
        var before = await h.StoredDashboardsAsync();

        var response = await ReorderAsync(h, [housing, health], [health, housing]);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, OrderChangedCode, "The project's dashboards have changed. Reload and try again.");
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task An_order_that_misses_repeats_or_adds_a_dashboard_is_refused_and_changes_nothing()
    {
        await using var h = await CreateAsync();
        var (housing, health, field) = await ThreeAsync(h);
        var (_, otherDashboard) = await h.AddOtherProjectAsync();
        var removed = Guid.NewGuid();
        Guid[] shown = [housing, health, field];
        var before = await h.StoredDashboardsAsync();

        (Guid[]? Expected, Guid[]? Asked)[] refused =
        [
            ([housing, health], [health, housing]), // the page missed an added dashboard
            ([housing, health, field, removed], [health, housing, field, removed]), // the page still shows a removed one
            (shown, [health, housing]), // misses one
            (shown, [health, health, field]), // repeats one in place of another
            (shown, [health, housing, field, field]), // repeats one besides the others
            (shown, [health, housing, field, removed]), // adds one the project does not have
            (shown, [health, housing, otherDashboard]), // swaps in another project's
            (shown, []),
            (shown, null),
            (null, [health, housing, field]),
            (null, null),
        ];

        foreach (var (expected, asked) in refused)
        {
            await AssertProblemAsync(await ReorderAsync(h, expected, asked), HttpStatusCode.Conflict, OrderChangedCode);
        }

        await AssertProblemAsync(await h.Analyst.PutAsJsonAsync(h.DashboardsUrl + "/order", new { }), HttpStatusCode.Conflict, OrderChangedCode);
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    // Moved meanwhile

    [Fact]
    public async Task A_move_from_a_page_that_missed_another_members_move_is_refused_and_the_other_move_stays()
    {
        await using var h = await CreateAsync();
        var (housing, health, field) = await ThreeAsync(h);
        var moved = await ReorderAsync(h, [housing, health, field], [housing, field, health], h.Admin); // "Field" to the left
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var before = await h.StoredDashboardsAsync();

        // The analyst's page still shows the earlier order and moves "Housing" to the right.
        var response = await ReorderAsync(h, [housing, health, field], [health, housing, field]);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, OrderChangedCode);
        Assert.Equal(before, await h.StoredDashboardsAsync());
        Assert.Equal(["Housing", "Field", "Health"], await NamesAsync(h));
    }

    // Bookmark after a rename

    [Fact]
    public async Task A_dashboards_address_answers_its_new_name_after_a_rename_and_a_move()
    {
        await using var h = await CreateAsync();
        var (housing, health, field) = await ThreeAsync(h);

        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(h, health, "Salud")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReorderAsync(h, [housing, health, field], [health, housing, field])).StatusCode);
        var dashboard = await h.GetDashboardAsync(health, h.Viewer);

        Assert.Equal((health, "Salud"), (dashboard.GetProperty("id").GetGuid(), dashboard.GetProperty("name").GetString()));
        Assert.Equal(["Salud", "Housing", "Field"], await NamesAsync(h));
    }

    // Dashboard duplicated

    [Fact]
    public async Task A_duplicate_holds_a_copy_of_each_widget_and_comes_right_after_the_original()
    {
        await using var h = await PublishedAsync();
        var (housing, health, field) = await ThreeAsync(h);
        await AddEightAsync(h, housing);
        await h.AddWidgetAsync(EightWidgets()[0], dashboardId: health);
        var widgets = await h.StoredWidgetsAsync();

        var response = await DuplicateAsync(h, housing, "Housing 2026", h.Admin);
        var copy = await ReadAsync(response);
        var copyId = copy.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/projects/{h.ProjectId}/dashboards/{copyId}", response.Headers.Location!.OriginalString);
        Assert.Equal("Housing 2026", copy.GetProperty("name").GetString());
        Assert.Equal(
            [(housing, "Housing", 0), (copyId, "Housing 2026", 1), (health, "Health", 2), (field, "Field", 3)],
            await h.DashboardsAsync());

        var originals = (await h.GetWidgetsAsync(housing)).EnumerateArray().ToArray();
        var copies = (await h.GetWidgetsAsync(copyId, h.Viewer)).EnumerateArray().ToArray();
        Assert.Equal(8, copies.Length);
        Assert.Equal(
            originals.Select(w => (w.GetProperty("title").GetString(), w.GetProperty("chartType").GetString(), w.GetProperty("config").GetRawText(), w.GetProperty("layout").GetRawText())),
            copies.Select(w => (w.GetProperty("title").GetString(), w.GetProperty("chartType").GetString(), w.GetProperty("config").GetRawText(), w.GetProperty("layout").GetRawText())));
        Assert.All(copies, w => Assert.Equal(copyId, w.GetProperty("dashboardId").GetGuid()));
        Assert.Empty(copies.Select(w => w.GetProperty("id").GetGuid()).Intersect(originals.Select(w => w.GetProperty("id").GetGuid())));
        Assert.Equal(8, copies.Select(w => w.GetProperty("id").GetGuid()).Distinct().Count());

        // Stored as they were, and authored by the member who duplicated them.
        Assert.Equal(8, await TestData.ScalarAsync<long>(h.Db,
            """
            SELECT count(*) FROM tamiza.dashboard_widgets AS c
            JOIN tamiza.dashboard_widgets AS o ON o.dashboard_id = @original AND o.title = c.title AND o.chart_type = c.chart_type
                                               AND o.config = c.config AND o.layout = c.layout
            JOIN tamiza.users AS u ON u.id = c.created_by AND u.id = c.updated_by AND u.keycloak_sub = 'ada'
            WHERE c.dashboard_id = @copy AND c.project_id = @project
            """,
            ("original", housing), ("copy", copyId), ("project", h.ProjectId)));
        Assert.Equal(widgets, (await h.StoredWidgetsAsync()).Where(row => !row.Contains(copyId.ToString())).ToArray());
    }

    [Fact]
    public async Task A_duplicate_of_the_last_dashboard_comes_last()
    {
        await using var h = await CreateAsync();
        var (housing, health, field) = await ThreeAsync(h);

        var response = await DuplicateAsync(h, field, "Field (copy)");
        var copyId = (await ReadAsync(response)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            [(housing, "Housing", 0), (health, "Health", 1), (field, "Field", 2), (copyId, "Field (copy)", 3)],
            await h.DashboardsAsync());
        Assert.Equal(0, (await h.GetWidgetsAsync(copyId)).GetArrayLength());
    }

    // Copies are independent

    [Fact]
    public async Task A_duplicate_and_its_original_change_independently()
    {
        await using var h = await PublishedAsync();
        await AddEightAsync(h, h.DashboardId);
        var copyResponse = await DuplicateAsync(h, h.DashboardId, "Housing 2026");
        var copyId = (await ReadAsync(copyResponse)).GetProperty("id").GetGuid();
        var copies = (await h.GetWidgetsAsync(copyId)).EnumerateArray().ToArray();
        var originals = (await h.GetWidgetsAsync()).EnumerateArray().ToArray();

        var removed = await h.Analyst.DeleteAsync(h.WidgetUrl(copies[0].GetProperty("id").GetGuid(), copyId));
        var retitled = await h.Analyst.PatchJsonAsync(h.WidgetUrl(copies[1].GetProperty("id").GetGuid(), copyId), new { title = "Copy only" });
        var originalMoved = await h.Analyst.PatchJsonAsync(
            h.WidgetUrl(originals[2].GetProperty("id").GetGuid()), new { layout = new { x = 0, y = 30, w = 12, h = 4 } });

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retitled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, originalMoved.StatusCode);
        var housing = await h.GetWidgetsAsync();
        Assert.Equal(8, housing.GetArrayLength());
        Assert.DoesNotContain("Copy only", Titles(housing));
        var copy = (await h.GetWidgetsAsync(copyId)).EnumerateArray().ToArray();
        Assert.Equal(7, copy.Length);
        Assert.Equal(copies[2].GetProperty("layout").GetRawText(), copy.Single(w => w.GetProperty("id").GetGuid() == copies[2].GetProperty("id").GetGuid()).GetProperty("layout").GetRawText());
    }

    // Duplicate at the limit

    [Fact]
    public async Task Duplicating_a_dashboard_of_a_project_with_seven_is_refused_with_the_limit()
    {
        await using var h = await CreateAsync();
        await FillAsync(h, 7);
        var before = await h.StoredDashboardsAsync();

        var response = await DuplicateAsync(h, h.DashboardId, "Indicators (copy)");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, LimitCode, LimitMessage);
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    // Original above the widget limit

    [Fact]
    public async Task A_dashboard_holding_more_widgets_than_the_limit_is_not_duplicated()
    {
        await using var h = await PublishedAsync(new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "2" });
        await h.AddWidgetAsync(EightWidgets()[0]);
        await h.AddWidgetAsync(EightWidgets()[1]);
        // The operator lowered the limit after a third widget was added.
        await h.ExecuteAsync(
            """
            INSERT INTO tamiza.dashboard_widgets (project_id, dashboard_id, chart_type, title, config, layout, created_by, updated_by)
            SELECT project_id, dashboard_id, chart_type, 'Third', config, layout, created_by, updated_by FROM tamiza.dashboard_widgets LIMIT 1
            """);
        var dashboards = await h.StoredDashboardsAsync();
        var widgets = await h.StoredWidgetsAsync();

        var response = await DuplicateAsync(h, h.DashboardId, "Indicators (copy)");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["A dashboard can have at most 2 widgets. Remove one or move it to another dashboard first."],
            IndicatorsHarness.Errors(await ReadAsync(response), "widgets"));
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());

        // At the limit itself it is duplicated.
        await h.ExecuteAsync("DELETE FROM tamiza.dashboard_widgets WHERE title = 'Third'");
        Assert.Equal(HttpStatusCode.Created, (await DuplicateAsync(h, h.DashboardId, "Indicators (copy)")).StatusCode);
    }

    public static TheoryData<string, string> RefusedCopyNames() => new()
    {
        { "SALUD", "Another dashboard of this project is named \"Salud\"." },
        { " indicators ", "Another dashboard of this project is named \"Indicators\"." }, // the original's own name
        { "Clean data", "\"Clean data\" is the name of a permanent tab. Choose another name." },
        { new string('n', 41), "The name can have at most 40 characters." },
    };

    [Theory]
    [MemberData(nameof(RefusedCopyNames))]
    public async Task A_copy_with_a_refused_name_is_a_400_on_the_name_and_adds_nothing(string name, string error)
    {
        await using var h = await PublishedAsync();
        await h.AddWidgetAsync(EightWidgets()[0]);
        await h.AddDashboardAsync("Salud");
        var dashboards = await h.StoredDashboardsAsync();
        var widgets = await h.StoredWidgetsAsync();

        await AssertNameErrorAsync(await DuplicateAsync(h, h.DashboardId, name), error);

        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task A_copy_of_a_removed_dashboard_is_not_found_and_adds_nothing()
    {
        await using var h = await PublishedAsync();
        var health = await h.AddDashboardAsync("Health");
        await h.AddWidgetAsync(EightWidgets()[0], dashboardId: health);
        await h.RemoveDashboardAsync(health);
        var dashboards = await h.StoredDashboardsAsync();

        await AssertProblemAsync(await DuplicateAsync(h, health, "Health (copy)"), HttpStatusCode.NotFound, NotFoundCode);
        await AssertProblemAsync(await DuplicateAsync(h, health, FortyOne), HttpStatusCode.NotFound, NotFoundCode); // the dashboard is checked first

        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    // API contract

    [Fact]
    public async Task The_openapi_document_describes_the_dashboard_endpoints()
    {
        await using var h = await CreateAsync();
        using var anonymous = h.Factory.CreateClient();

        var document = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        const string project = "/api/v1/projects/{projectId}";
        var paths = document.GetProperty("paths");
        var dashboards = paths.EnumerateObject().Where(p => p.Name.Contains("/dashboards")).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            [
                "/dashboards post",
                "/dashboards/order put",
                "/dashboards/{dashboardId} delete get patch",
                "/dashboards/{dashboardId}/copies post",
                "/dashboards/{dashboardId}/widgets post",
                "/dashboards/{dashboardId}/widgets/{widgetId} delete patch",
            ],
            dashboards.Select(p => $"{p.Name.Replace(project, "")} {string.Join(' ', Verbs(p.Value))}"));
        var widgets = paths.GetProperty(project + "/dashboards/{dashboardId}/widgets");
        var widget = paths.GetProperty(project + "/dashboards/{dashboardId}/widgets/{widgetId}");
        var order = paths.GetProperty(project + "/dashboards/order");
        Assert.Contains("dashboardId", Properties(Component(document, Answer(widgets.GetProperty("post"), "201"))));
        Assert.Contains("dashboardId", Properties(Component(document, Answer(widget.GetProperty("patch"), "200"))));
        Assert.Contains("dashboardId", Properties(Component(document, Body(widget.GetProperty("patch")))));
        Assert.Equal(["dashboardIds", "expectedIds"], Properties(Component(document, Body(order.GetProperty("put")))));
        Assert.Contains("dashboards", Properties(document.GetProperty("components").GetProperty("schemas").GetProperty("ProjectDetails")));
    }

    private static string[] Verbs(JsonElement path) =>
        path.EnumerateObject().Select(o => o.Name).Where(name => name != "parameters").Order(StringComparer.Ordinal).ToArray();

    private static JsonElement Body(JsonElement operation) =>
        operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");

    private static JsonElement Answer(JsonElement operation, string status) =>
        operation.GetProperty("responses").GetProperty(status).GetProperty("content").GetProperty("application/json").GetProperty("schema");

    private static string[] Properties(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    /// <summary>The component a schema refers to, directly or as the branch that is not null of a body that may be left out.</summary>
    private static JsonElement Component(JsonElement document, JsonElement schema)
    {
        var reference = schema.TryGetProperty("oneOf", out var branches)
            ? branches.EnumerateArray().Single(b => b.TryGetProperty("$ref", out _)).GetProperty("$ref")
            : schema.GetProperty("$ref");
        return document.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!["#/components/schemas/".Length..]);
    }
}
