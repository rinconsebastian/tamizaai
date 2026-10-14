using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary><c>GET /projects/{id}/indicators</c> and <c>.../indicators/{name}/columns</c>: the dashboard's state and a table's columns.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorOverviewTests(PostgresFixture postgres)
{
    private const string Columns = IndicatorsHarness.StandardColumns + ", year bigint, geometry text, ok boolean";

    private const string Rows = """
        ('Cali',    40, 4, 10, 2025, 'POINT(1 1)', false),
        ('Palmira', 60, 6, 10, 2025, 'POINT(2 2)', NULL),
        ('Cali',    35, 3, 10, 2026, NULL, NULL)
        """;

    private static readonly object Widget = new { title = "Coverage", chartType = "column", config = new { indicator = "coverage", groupBy = "municipality" } };

    private static JsonElement Entry(JsonElement overview, string name) =>
        overview.GetProperty("indicators").EnumerateArray().Single(i => i.GetProperty("name").GetString() == name);

    // Viewer opens a published dashboard

    [Fact]
    public async Task A_viewer_opens_a_published_dashboard_and_sees_the_widgets_and_the_indicators_metadata()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var runId = await h.PublishAsync("coverage", Columns, Rows, label: "Piped water coverage", format: "percent", unit: "households", description: "Share of homes");
        await h.AddWidgetAsync(Widget);

        var overview = await h.GetAsync(client: h.Viewer);

        var coverage = Entry(overview, "coverage");
        Assert.Equal("Piped water coverage", coverage.GetProperty("label").GetString());
        Assert.Equal("Share of homes", coverage.GetProperty("description").GetString());
        Assert.Equal("households", coverage.GetProperty("unit").GetString());
        Assert.Equal("percent", coverage.GetProperty("format").GetString());
        Assert.True(coverage.GetProperty("published").GetBoolean());
        Assert.Equal(runId, coverage.GetProperty("runId").GetGuid());
        Assert.Equal(JsonValueKind.String, coverage.GetProperty("publishedAt").ValueKind);
        Assert.Equal(3, coverage.GetProperty("rows").GetInt64());
        Assert.Equal("Coverage", overview.GetProperty("widgets").EnumerateArray().Single().GetProperty("title").GetString());
        Assert.Equal((50, 5000), (overview.GetProperty("limits").GetProperty("maxWidgets").GetInt32(), overview.GetProperty("limits").GetProperty("maxQueryRows").GetInt32()));
    }

    [Fact]
    public async Task The_indicators_are_listed_by_name_each_with_its_own_publication()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var attendance = await h.PublishAsync("attendance", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1), ('Yumbo', 2, 2, 1)");
        var coverage = await h.PublishAsync("coverage", Columns, Rows);
        await h.AddIndicatorAsync("drafting", label: "Not run yet", format: "count");

        var overview = await h.GetAsync();

        Assert.Equal(["attendance", "coverage", "drafting"], overview.GetProperty("indicators").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal((attendance, 2L), (Entry(overview, "attendance").GetProperty("runId").GetGuid(), Entry(overview, "attendance").GetProperty("rows").GetInt64()));
        Assert.Equal((coverage, 3L), (Entry(overview, "coverage").GetProperty("runId").GetGuid(), Entry(overview, "coverage").GetProperty("rows").GetInt64()));
        var drafting = Entry(overview, "drafting");
        Assert.False(drafting.GetProperty("published").GetBoolean());
        Assert.Equal("Not run yet", drafting.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, drafting.GetProperty("runId").ValueKind);
        Assert.Equal(JsonValueKind.Null, drafting.GetProperty("publishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, drafting.GetProperty("rows").ValueKind);
    }

    [Fact]
    public async Task The_overview_names_the_latest_run_that_published_an_indicator()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", Columns, Rows);
        var latest = await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)");

        var coverage = Entry(await h.GetAsync(), "coverage");

        Assert.Equal(latest, coverage.GetProperty("runId").GetGuid());
        Assert.Equal(1, coverage.GetProperty("rows").GetInt64());
    }

    [Fact]
    public async Task A_failed_run_after_a_publication_does_not_unpublish_the_indicator()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var published = await h.PublishAsync("coverage", Columns, Rows);
        await PublishedTables.RecordStepAsync(h.Db, h.ProjectId, "indicators", [], status: "failed", indicator: "coverage");

        var coverage = Entry(await h.GetAsync(), "coverage");

        Assert.True(coverage.GetProperty("published").GetBoolean());
        Assert.Equal(published, coverage.GetProperty("runId").GetGuid());
    }

    [Fact]
    public async Task Another_steps_run_does_not_count_as_an_indicators_publication()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddIndicatorAsync("coverage");
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "output", ("persons", "name text", "('Ana')"));

        var coverage = Entry(await h.GetAsync(), "coverage");

        Assert.False(coverage.GetProperty("published").GetBoolean());
        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("runId").ValueKind);
    }

    [Fact]
    public async Task A_table_whose_script_was_removed_is_not_listed_until_the_next_run_drops_it()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", Columns, Rows);
        await h.PublishAsync("retired", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)");

        var removal = await h.Analyst.DeleteAsync($"/api/v1/projects/{h.ProjectId}/scripts/indicators/retired");
        var overview = await h.GetAsync();

        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        Assert.Equal(["coverage"], overview.GetProperty("indicators").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
    }

    // No indicator has published

    [Fact]
    public async Task While_no_indicator_has_published_the_overview_lists_them_unpublished_and_still_lists_the_widgets()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddIndicatorAsync("coverage", label: "Coverage");
        await h.AddWidgetAsync(Widget);

        var response = await h.Viewer.GetAsync(h.Url);
        var overview = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(Entry(overview, "coverage").GetProperty("published").GetBoolean());
        Assert.Equal(1, overview.GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task A_project_with_no_indicators_has_an_empty_dashboard_that_still_lists_its_widgets()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddWidgetAsync(Widget);

        var overview = await h.GetAsync();

        Assert.Equal(0, overview.GetProperty("indicators").GetArrayLength());
        Assert.Equal(1, overview.GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task A_project_with_nothing_at_all_has_an_empty_dashboard()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var overview = await h.GetAsync();

        Assert.Equal(0, overview.GetProperty("indicators").GetArrayLength());
        Assert.Equal(0, overview.GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task A_table_of_another_name_does_not_make_an_indicator_published()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddIndicatorAsync("coverage");
        await h.PublishAsync("other", "n bigint", "(1)", script: false);

        var overview = await h.GetAsync();

        Assert.False(Entry(overview, "coverage").GetProperty("published").GetBoolean());
        Assert.Equal(1, overview.GetProperty("indicators").GetArrayLength());
    }

    // Not a member

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_404_for_the_overview_and_the_columns()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", Columns, Rows);

        var overview = await h.Outsider.GetAsync(h.Url);
        var columns = await h.Outsider.GetAsync(h.Url + "/coverage/columns");
        var unknown = await h.Admin.GetAsync(IndicatorsHarness.UrlOf(Guid.NewGuid()));

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], new[] { overview.StatusCode, columns.StatusCode, unknown.StatusCode });
    }

    [Fact]
    public async Task A_request_without_a_token_is_401()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        using var anonymous = h.Factory.CreateClient();

        var response = await anonymous.GetAsync(h.Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Columns

    [Fact]
    public async Task Every_published_column_is_listed_in_table_order_with_its_type_and_the_numeric_ones_apart()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", Columns, Rows);
        await PublishedTables.CommentAsync(h.Db, h.ProjectId, "indicators", "coverage", "year", "Survey year");

        var columns = await h.GetAsync("/coverage/columns");

        Assert.True(columns.GetProperty("published").GetBoolean());
        var list = columns.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal(["municipality", "value", "numerator", "denominator", "year", "geometry", "ok"], list.Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(["text", "decimal", "decimal", "decimal", "integer", "text", "boolean"], list.Select(c => c.GetProperty("type").GetString()));
        Assert.Equal("Survey year", list[4].GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, list[0].GetProperty("label").ValueKind);
        Assert.Equal(["value", "numerator", "denominator", "year"], IndicatorsHarness.Strings(columns.GetProperty("numeric")));
    }

    [Fact]
    public async Task The_default_measure_is_value_and_the_default_group_is_the_first_column_that_is_not_a_number()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", "year bigint, municipality text, value numeric", "(2025, 'Cali', 1)");

        var defaults = (await h.GetAsync("/coverage/columns")).GetProperty("defaults");

        Assert.Equal("value", defaults.GetProperty("measure").GetString());
        Assert.Equal("municipality", defaults.GetProperty("groupBy").GetString());
    }

    [Fact]
    public async Task Without_a_value_column_the_default_measure_is_the_first_numeric_column()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("attendance", "round text, students bigint, hours numeric", "('first', 20, 1.5)");

        var columns = await h.GetAsync("/attendance/columns");

        Assert.Equal("students", columns.GetProperty("defaults").GetProperty("measure").GetString());
        Assert.Equal("round", columns.GetProperty("defaults").GetProperty("groupBy").GetString());
        Assert.Equal(["students", "hours"], IndicatorsHarness.Strings(columns.GetProperty("numeric")));
    }

    [Fact]
    public async Task A_text_column_named_value_is_not_the_default_measure()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("attendance", "value text, students bigint", "('high', 20)");

        var defaults = (await h.GetAsync("/attendance/columns")).GetProperty("defaults");

        Assert.Equal("students", defaults.GetProperty("measure").GetString());
        Assert.Equal("value", defaults.GetProperty("groupBy").GetString());
    }

    [Fact]
    public async Task A_table_of_numbers_only_has_no_default_group()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("totals", "value numeric", "(5)");

        var defaults = (await h.GetAsync("/totals/columns")).GetProperty("defaults");

        Assert.Equal("value", defaults.GetProperty("measure").GetString());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("groupBy").ValueKind);
    }

    [Fact]
    public async Task An_indicator_that_has_not_published_has_no_columns()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddIndicatorAsync("coverage");

        var response = await h.Viewer.GetAsync(h.Url + "/coverage/columns");
        var columns = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(columns.GetProperty("published").GetBoolean());
        Assert.Equal(0, columns.GetProperty("columns").GetArrayLength());
        Assert.Equal(0, columns.GetProperty("numeric").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, columns.GetProperty("defaults").GetProperty("measure").ValueKind);
    }

    [Fact]
    public async Task The_columns_of_one_indicator_are_not_those_of_another()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", Columns, Rows);
        await h.PublishAsync("attendance", "round text, students bigint", "('first', 20)");

        var attendance = await h.GetAsync("/attendance/columns");

        Assert.Equal(["round", "students"], attendance.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_new_publication_replaces_what_the_columns_endpoint_lists()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", Columns, Rows);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('only', 1, 1, 1)");

        var columns = await h.GetAsync("/coverage/columns");

        Assert.Equal(4, columns.GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task A_name_that_is_not_a_table_name_is_not_published_and_is_never_interpolated()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", Columns, Rows);

        var columns = await h.Viewer.GetFromJsonAsync<JsonElement>(h.Url + "/" + Uri.EscapeDataString("coverage\"; DROP SCHEMA public CASCADE; --") + "/columns");

        Assert.False(columns.GetProperty("published").GetBoolean());
        Assert.True(await TestData.ScalarAsync<bool>(h.Db, "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'public')"));
    }

    // Clean data keeps ignoring this schema

    [Fact]
    public async Task The_clean_data_dashboard_still_does_not_offer_the_indicators_schema()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", Columns, Rows);

        var overview = await h.Viewer.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{h.ProjectId}/data");
        var query = await h.Viewer.PostAsJsonAsync($"/api/v1/projects/{h.ProjectId}/data/indicators/coverage/query", new { });

        Assert.Equal(0, overview.GetProperty("sources").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_describes_the_indicator_endpoints()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        using var anonymous = h.Factory.CreateClient();

        var document = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).Where(p => p.Contains("/indicators") && !p.Contains("/scripts/")).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            [
                "/api/v1/projects/{projectId}/indicators",
                "/api/v1/projects/{projectId}/indicators/query",
                "/api/v1/projects/{projectId}/indicators/widgets",
                "/api/v1/projects/{projectId}/indicators/widgets/{widgetId}",
                "/api/v1/projects/{projectId}/indicators/{name}/columns",
                "/api/v1/projects/{projectId}/indicators/{name}/exports",
            ],
            paths);
    }
}
