using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.CleanData;

/// <summary>A run republishing the tables while members browse them, and the OpenAPI document of the dashboard.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DataRepublishTests(PostgresFixture postgres)
{
    private static async Task<HttpResponseMessage> QueryAsync(CleanDataHarness h, string table, object? body = null) =>
        await h.Viewer.PostAsJsonAsync($"{h.Url}/output/{table}/query", body ?? new { });

    [Fact]
    public async Task After_a_run_republishes_the_next_page_has_the_new_rows_and_the_new_publishing_time()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var first = await h.PublishAsync("output", ("persons", "name text", "('old1'), ('old2')"));

        var before = await (await QueryAsync(h, "persons")).Content.ReadFromJsonAsync<JsonElement>();
        await Task.Delay(20);
        var second = await h.PublishAsync("output", ("persons", "name text, age bigint", "('new1', 1), ('new2', 2), ('new3', 3)"));
        var after = await (await QueryAsync(h, "persons")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, before.GetProperty("total").GetInt32());
        Assert.Equal(first, before.GetProperty("runId").GetGuid());
        Assert.Equal(3, after.GetProperty("total").GetInt32());
        Assert.Equal(second, after.GetProperty("runId").GetGuid());
        Assert.True(after.GetProperty("publishedAt").GetDateTimeOffset() > before.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.Equal(["name", "age"], after.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_filter_on_a_column_the_new_tables_no_longer_have_is_a_validation_error_not_a_failure()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("persons", "name text, age bigint", "('a', 1)"));
        await h.PublishAsync("output", ("persons", "name text", "('a')"));

        var response = await QueryAsync(h, "persons", new { filters = new[] { new { column = "age", op = "range", min = "1", max = (string?)null } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_table_a_run_removed_is_404_and_the_overview_lists_the_new_tables()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("households", "size bigint", "(1)"), ("persons", "name text", "('a')"));
        Assert.Equal(HttpStatusCode.OK, (await QueryAsync(h, "persons")).StatusCode);

        await h.PublishAsync("output", ("households", "size bigint", "(1), (2)"));
        var response = await QueryAsync(h, "persons");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var overview = await h.GetOverviewAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("data.tableNotFound", problem.GetProperty("code").GetString());
        var tables = overview.GetProperty("sources")[0].GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("name").GetString());
        Assert.Equal(["households"], tables);
    }

    [Fact]
    public async Task When_script_2_is_removed_the_output_source_disappears_and_the_clean_tables_remain()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("clean", ("main", "name text", "('a')"));
        await h.PublishAsync("output", ("persons", "name text", "('a')"));
        Assert.Equal("output", (await h.GetOverviewAsync()).GetProperty("defaultSource").GetString());

        await PublishedTables.DropAsync(h.Db, h.ProjectId, "output");
        var overview = await h.GetOverviewAsync();

        Assert.Equal("clean", overview.GetProperty("defaultSource").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await QueryAsync(h, "persons")).StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_lists_the_dashboard_operations_with_their_shapes()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var paths = document.GetProperty("paths");
        var overview = paths.GetProperty("/api/v1/projects/{projectId}/data").GetProperty("get");
        var query = paths.GetProperty("/api/v1/projects/{projectId}/data/{source}/{table}/query").GetProperty("post");
        var values = paths.GetProperty("/api/v1/projects/{projectId}/data/{source}/{table}/columns/{column}/values").GetProperty("get");
        var schemas = document.GetProperty("components").GetProperty("schemas");

        Assert.Equal("GetDataOverview", overview.GetProperty("operationId").GetString());
        Assert.Equal("QueryDataTable", query.GetProperty("operationId").GetString());
        Assert.Equal("GetDataColumnValues", values.GetProperty("operationId").GetString());
        Assert.True(query.TryGetProperty("requestBody", out _));
        Assert.Contains("search", values.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        foreach (var schema in new[] { "DataOverview", "DataPage", "DataQueryRequest", "DataFilter", "DataSort", "DataValueList", "TableColumn" })
        {
            Assert.True(schemas.TryGetProperty(schema, out _), $"schema {schema} is missing");
        }
    }
}
