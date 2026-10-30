using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpRowsAndFieldworkTests(PostgresFixture postgres)
{
    private Task<McpProjectHarness> CreateAsync(McpAccess level, McpAccess maximum = McpAccess.Rows) =>
        McpProjectHarness.CreateAsync(postgres, level, maximum, formFieldsJson: McpDataFixtures.FieldsJson, formChoicesJson: McpDataFixtures.ChoicesJson);

    [Fact]
    public async Task Rows_are_refused_at_summaries_only_and_say_who_decides()
    {
        await using var h = await CreateAsync(McpAccess.Aggregates);
        await McpDataFixtures.PublishMainAsync(h);

        var result = await h.CallAsync("analyst", "query_rows", new { source = "clean", table = "main" });

        Assert.Equal("mcp.rowsNotAllowed", result.Code());
        Assert.DoesNotContain("Piped", result.Text());
    }

    [Fact]
    public async Task Rows_are_refused_when_the_deployment_allows_only_summaries_whatever_the_project_says()
    {
        await using var h = await CreateAsync(McpAccess.Rows, maximum: McpAccess.Aggregates);
        await McpDataFixtures.PublishMainAsync(h);

        Assert.Equal("mcp.rowsNotAllowed", (await h.CallAsync("analyst", "query_rows", new { source = "clean", table = "main" })).Code());
    }

    [Fact]
    public async Task Rows_are_filtered_sorted_and_counted_as_the_clean_data_dashboard_does()
    {
        await using var h = await CreateAsync(McpAccess.Rows);
        await McpDataFixtures.PublishMainAsync(h);

        using var rivers = (await h.CallAsync("analyst", "query_rows", new
        {
            source = "clean",
            table = "main",
            columns = new[] { "water_source", "age" },
            filters = new[] { new { column = "water_source", op = "equals", value = "River" } },
        })).Json();
        using var oldest = (await h.CallAsync("analyst", "query_rows", new { source = "clean", table = "main", columns = new[] { "age" }, sortBy = "age", sortDirection = "desc", pageSize = 25 })).Json();

        Assert.Equal(3, rivers.RootElement.GetProperty("total").GetInt64());
        Assert.Equal(["water_source", "age"], rivers.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray());
        Assert.Equal(3, rivers.RootElement.GetProperty("rows").GetArrayLength());
        Assert.All(rivers.RootElement.GetProperty("rows").EnumerateArray(), row => Assert.Equal("River", row[0].GetString()));
        Assert.Equal(59, oldest.RootElement.GetProperty("rows")[0][0].GetInt64());
        Assert.Equal(25, oldest.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(60, oldest.RootElement.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task A_call_returns_at_most_a_hundred_rows_and_refuses_other_page_sizes()
    {
        await using var h = await CreateAsync(McpAccess.Rows);
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "clean", ("many", "n bigint", string.Join(",", Enumerable.Range(1, 250).Select(i => $"({i})"))));

        using var all = (await h.CallAsync("analyst", "query_rows", new { source = "clean", table = "many", pageSize = 100, page = 3 })).Json();
        var odd = await h.CallAsync("analyst", "query_rows", new { source = "clean", table = "many", pageSize = 101 });

        Assert.Equal((250, 50), (all.RootElement.GetProperty("total").GetInt64(), all.RootElement.GetProperty("rows").GetArrayLength()));
        Assert.Equal("mcp.validation", odd.Code());
    }

    [Fact]
    public async Task Long_text_is_cut_at_five_hundred_characters_and_marked()
    {
        await using var h = await CreateAsync(McpAccess.Rows);
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "clean", ("notes", "id bigint, note text", $"(1, '{new string('x', 700)}')"));

        using var body = (await h.CallAsync("analyst", "query_rows", new { source = "clean", table = "notes", columns = new[] { "note" } })).Json();

        Assert.Equal(500, body.RootElement.GetProperty("rows")[0][0].GetString()!.Length);
        Assert.Equal([0, 0], body.RootElement.GetProperty("cutCells")[0].EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    [Fact]
    public async Task The_raw_and_indicator_schemas_can_be_read_by_an_analyst_and_not_through_the_rest_routes()
    {
        await using var h = await CreateAsync(McpAccess.Rows);
        await McpDataFixtures.PublishMainAsync(h, "raw");
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "rate", "municipality text, value numeric", "('Santa Ana', 0.5)");

        using var raw = (await h.CallAsync("analyst", "query_rows", new { source = "raw", table = "main", pageSize = 25 })).Json();
        using var rate = (await h.CallAsync("viewer", "query_rows", new { source = "indicators", table = "rate" })).Json();
        var viewerRaw = await h.CallAsync("viewer", "query_rows", new { source = "raw", table = "main" });
        using var rest = h.Factory.CreateClient().WithToken(TestTokens.Create("analyst", "analyst", "analyst@example.org"));
        var restRaw = await rest.PostAsync($"/api/v1/projects/{h.ProjectId}/data/raw/main/query", JsonContent.Create(new { }));
        var restIndicators = await rest.PostAsync($"/api/v1/projects/{h.ProjectId}/data/indicators/rate/query", JsonContent.Create(new { }));

        Assert.Equal(60, raw.RootElement.GetProperty("total").GetInt64());
        Assert.Equal("Santa Ana", rate.RootElement.GetProperty("rows")[0][0].GetString());
        Assert.Equal("mcp.forbidden", viewerRaw.Code());
        Assert.Equal(HttpStatusCode.NotFound, restRaw.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, restIndicators.StatusCode);
    }

    [Fact]
    public async Task Reference_tables_can_be_paged_and_not_filtered()
    {
        await using var h = await CreateAsync(McpAccess.Rows);
        var rows = "[" + string.Join(",", Enumerable.Range(1, 60).Select(i => $"[\"Town {i}\",{i}]")) + "]";
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.reference_tables (id, project_id, name, file_name, columns, row_count, updated_by, created_at, updated_at) VALUES (@id, @p, 'population', 'p.csv', '[{\"name\":\"town\",\"header\":\"Town\",\"type\":\"text\"},{\"name\":\"people\",\"header\":\"People\",\"type\":\"integer\"}]'::jsonb, 60, @u, now(), now())",
            ("id", Guid.Parse("00000000-0000-0000-0000-0000000000c1")), ("p", h.ProjectId), ("u", h.Users["analyst"]));
        await TestData.ExecuteAsync(h.Db, "INSERT INTO tamiza.reference_table_contents (reference_table_id, rows) VALUES ('00000000-0000-0000-0000-0000000000c1', @rows::jsonb)", ("rows", rows));

        using var second = (await h.CallAsync("analyst", "query_rows", new { source = "reference", table = "population", page = 2, pageSize = 25, columns = new[] { "people" } })).Json();
        var filtered = await h.CallAsync("analyst", "query_rows", new { source = "reference", table = "population", filters = new[] { new { column = "town", op = "equals", value = "Town 1" } } });

        Assert.Equal(60, second.RootElement.GetProperty("total").GetInt64());
        Assert.Equal(26, second.RootElement.GetProperty("rows")[0][0].GetInt32());
        Assert.Equal(25, second.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal("mcp.validation", filtered.Code());
    }

    [Fact]
    public async Task Fieldwork_gives_the_performance_dashboards_figures_to_a_viewer()
    {
        await using var h = await CreateAsync(McpAccess.Aggregates);
        for (var i = 1; i <= 3; i++)
        {
            await TestData.ExecuteAsync(h.Db,
                "INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, submitted_at, data, origin, received_at, updated_at) VALUES (@p, @i, @u, now(), '{}', 'sync', now(), now())",
                ("p", h.ProjectId), ("i", (long)i), ("u", $"u{i}"));
        }

        using var mcp = (await h.CallAsync("viewer", "get_fieldwork")).Json();
        using var rest = h.Factory.CreateClient().WithToken(TestTokens.Create("viewer", "viewer", "viewer@example.org"));
        using var performance = JsonDocument.Parse(await rest.GetStringAsync($"/api/v1/projects/{h.ProjectId}/performance"));

        Assert.Equal(3, mcp.RootElement.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Equal(performance.RootElement.GetProperty("submissions").GetProperty("total").GetInt64(), mcp.RootElement.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Equal(performance.RootElement.GetProperty("availability").GetArrayLength(), mcp.RootElement.GetProperty("availability").GetArrayLength());
        Assert.Equal(performance.RootElement.GetProperty("timeZone").GetString(), mcp.RootElement.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task Fieldwork_takes_a_range_and_refuses_a_wrong_one()
    {
        await using var h = await CreateAsync(McpAccess.Aggregates);

        using var ranged = (await h.CallAsync("viewer", "get_fieldwork", new { from = "2026-03-01", to = "2026-03-31" })).Json();
        var wrong = await h.CallAsync("viewer", "get_fieldwork", new { from = "March" });

        Assert.Equal("2026-03-01", ranged.RootElement.GetProperty("from").GetString());
        Assert.Equal("mcp.validation", wrong.Code());
    }
}
