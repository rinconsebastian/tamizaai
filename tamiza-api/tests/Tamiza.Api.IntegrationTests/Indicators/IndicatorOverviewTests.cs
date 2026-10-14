using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary><c>GET /projects/{id}/indicators</c> and <c>.../indicators/columns</c>: the dashboard's state and the table's columns.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorOverviewTests(PostgresFixture postgres)
{
    private const string Columns = IndicatorsHarness.RequiredColumns + ", year bigint, geometry text, ok boolean";

    private const string Rows = """
        ('target',   'Cali',    90, 9, 10, 2025, NULL, true),
        ('coverage', 'Cali',    40, 4, 10, 2025, 'POINT(1 1)', false),
        ('coverage', 'Palmira', 60, 6, 10, 2025, 'POINT(2 2)', NULL),
        ('coverage', 'Cali',    35, 3, 10, 2026, NULL, NULL),
        (NULL,       'Cali',    1,  1, 1,  2026, NULL, NULL)
        """;

    private static readonly object[] Widget = [new { title = "Coverage", chartType = "column", config = new { indicators = new[] { "coverage" } } }];

    // Viewer opens a published dashboard

    [Fact]
    public async Task A_viewer_opens_a_published_dashboard_and_sees_when_and_by_which_run_it_was_published()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var runId = await h.PublishAsync(Columns, Rows);
        await h.AddWidgetAsync(Widget[0]);

        var overview = await h.GetAsync(client: h.Viewer);

        Assert.True(overview.GetProperty("published").GetBoolean());
        Assert.Equal(runId, overview.GetProperty("runId").GetGuid());
        Assert.Equal(JsonValueKind.String, overview.GetProperty("publishedAt").ValueKind);
        Assert.Equal("Coverage", overview.GetProperty("widgets").EnumerateArray().Single().GetProperty("title").GetString());
        Assert.Equal((50, 5000), (overview.GetProperty("limits").GetProperty("maxWidgets").GetInt32(), overview.GetProperty("limits").GetProperty("maxQueryRows").GetInt32()));
    }

    [Fact]
    public async Task The_overview_names_the_latest_run_that_published_the_indicators()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(Columns, Rows);
        var latest = await h.PublishAsync(Columns, Rows);

        var overview = await h.GetAsync();

        Assert.Equal(latest, overview.GetProperty("runId").GetGuid());
    }

    [Fact]
    public async Task Another_steps_run_does_not_count_as_the_indicators_publication()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "output", ("persons", "name text", "('Ana')"));

        var overview = await h.GetAsync();

        Assert.False(overview.GetProperty("published").GetBoolean());
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("runId").ValueKind);
    }

    // Script 3 has not published

    [Fact]
    public async Task While_script_3_has_not_published_the_overview_says_so_and_still_lists_the_widgets()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddWidgetAsync(Widget[0]);

        var response = await h.Viewer.GetAsync(h.Url);
        var overview = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(overview.GetProperty("published").GetBoolean());
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("publishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("runId").ValueKind);
        Assert.Equal(1, overview.GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task A_project_with_nothing_at_all_has_an_empty_unpublished_dashboard()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var overview = await h.GetAsync();

        Assert.False(overview.GetProperty("published").GetBoolean());
        Assert.Equal(0, overview.GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task A_schema_without_the_indicators_table_is_not_published()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "indicators", ("other", "n bigint", "(1)"));

        var overview = await h.GetAsync();
        var columns = await h.GetAsync("/columns");

        Assert.False(overview.GetProperty("published").GetBoolean());
        Assert.False(columns.GetProperty("published").GetBoolean());
    }

    // Not a member

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_404_for_the_overview_and_the_columns()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(Columns, Rows);

        var overview = await h.Outsider.GetAsync(h.Url);
        var columns = await h.Outsider.GetAsync(h.Url + "/columns");
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
    public async Task Every_published_column_is_listed_in_table_order_with_its_type()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(Columns, Rows);
        await PublishedTables.CommentAsync(h.Db, h.ProjectId, "indicators", "indicators", "year", "Survey year");

        var columns = await h.GetAsync("/columns");

        Assert.True(columns.GetProperty("published").GetBoolean());
        var list = columns.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal(["indicator", "disaggregation", "value", "numerator", "denominator", "year", "geometry", "ok"], list.Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(["text", "text", "decimal", "decimal", "decimal", "integer", "text", "boolean"], list.Select(c => c.GetProperty("type").GetString()));
        Assert.Equal("Survey year", list[5].GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, list[0].GetProperty("label").ValueKind);
    }

    [Fact]
    public async Task The_indicators_are_the_distinct_non_null_values_sorted()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(Columns, Rows);

        var columns = await h.GetAsync("/columns");

        Assert.Equal(["coverage", "target"], IndicatorsHarness.Strings(columns.GetProperty("indicators")));
    }

    [Fact]
    public async Task At_most_500_indicators_are_listed()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(IndicatorsHarness.RequiredColumns, "");
        await h.ExecuteAsync($"INSERT INTO \"p_{h.ProjectId:N}_indicators\".indicators SELECT 'i' || lpad(g::text, 4, '0'), 'x', 1, 1, 1 FROM generate_series(1, 600) g");

        var columns = await h.GetAsync("/columns");

        var indicators = IndicatorsHarness.Strings(columns.GetProperty("indicators"));
        Assert.Equal(500, indicators.Length);
        Assert.Equal(("i0001", "i0500"), (indicators[0], indicators[^1]));
    }

    [Fact]
    public async Task While_script_3_has_not_published_there_are_no_columns_and_no_indicators()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var response = await h.Viewer.GetAsync(h.Url + "/columns");
        var columns = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(columns.GetProperty("published").GetBoolean());
        Assert.Equal(0, columns.GetProperty("columns").GetArrayLength());
        Assert.Equal(0, columns.GetProperty("indicators").GetArrayLength());
    }

    [Fact]
    public async Task A_table_without_an_indicator_column_lists_its_columns_and_no_indicators()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("disaggregation text, value numeric", "('Cali', 1)");

        var columns = await h.GetAsync("/columns");

        Assert.Equal(2, columns.GetProperty("columns").GetArrayLength());
        Assert.Equal(0, columns.GetProperty("indicators").GetArrayLength());
    }

    [Fact]
    public async Task A_new_run_replaces_what_the_columns_endpoint_lists()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(Columns, Rows);
        await h.PublishAsync(IndicatorsHarness.RequiredColumns, "('only', 'x', 1, 1, 1)");

        var columns = await h.GetAsync("/columns");

        Assert.Equal(5, columns.GetProperty("columns").GetArrayLength());
        Assert.Equal(["only"], IndicatorsHarness.Strings(columns.GetProperty("indicators")));
    }

    // Clean data keeps ignoring this schema

    [Fact]
    public async Task The_clean_data_dashboard_still_does_not_offer_the_indicators_schema()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(Columns, Rows);

        var overview = await h.Viewer.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{h.ProjectId}/data");
        var query = await h.Viewer.PostAsJsonAsync($"/api/v1/projects/{h.ProjectId}/data/indicators/indicators/query", new { });

        Assert.Equal(0, overview.GetProperty("sources").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_describes_the_indicator_endpoints()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        using var anonymous = h.Factory.CreateClient();

        var document = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).Where(p => p.Contains("/indicators")).Order().ToArray();
        Assert.Equal(
            [
                "/api/v1/projects/{projectId}/indicators",
                "/api/v1/projects/{projectId}/indicators/columns",
                "/api/v1/projects/{projectId}/indicators/exports",
                "/api/v1/projects/{projectId}/indicators/query",
                "/api/v1/projects/{projectId}/indicators/widgets",
                "/api/v1/projects/{projectId}/indicators/widgets/{widgetId}",
            ],
            paths);
    }
}
