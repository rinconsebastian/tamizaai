using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

/// <summary>The REST side of the MCP server: the connection details for the connect page, the activity of a project and the OpenAPI document.</summary>
[Collection(PostgresCollection.Name)]
public sealed class McpRestEndpointsTests(PostgresFixture postgres)
{
    private static HttpClient Person(McpProjectHarness h, string person) =>
        TestData.ClientFor(h.Factory, person, $"{person}@example.org");

    [Fact]
    public async Task Connection_details_are_not_found_while_the_server_is_disabled()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");

        var response = await client.GetAsync("/api/v1/mcp");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(McpAccess.Rows, "rows")]
    [InlineData(McpAccess.Aggregates, "aggregates")]
    public async Task Connection_details_name_the_address_the_client_the_port_the_maximum_and_the_account_page(McpAccess maximum, string expected)
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres, maximum: maximum);
        using var viewer = Person(h, "viewer");

        var response = await viewer.GetAsync("/api/v1/mcp");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(McpHarness.Address, body.GetProperty("url").GetString());
        Assert.Equal("tamiza-mcp", body.GetProperty("clientId").GetString());
        Assert.Equal(33418, body.GetProperty("callbackPort").GetInt32());
        Assert.Equal(expected, body.GetProperty("maxDataAccess").GetString());
        Assert.EndsWith("/account/#/applications", body.GetProperty("accountUrl").GetString());
    }

    [Fact]
    public async Task Connection_details_follow_the_operators_settings_and_need_a_signed_in_person()
    {
        var settings = new Dictionary<string, string?>
        {
            ["TAMIZA_MCP_CLIENT_ID"] = "claude-tamiza",
            ["TAMIZA_MCP_CALLBACK_PORT"] = "34000",
        };
        await using var h = await McpProjectHarness.CreateAsync(postgres, settings: settings);
        using var anonymous = h.Factory.CreateClient();
        using var viewer = Person(h, "viewer");

        var withoutToken = await anonymous.GetAsync("/api/v1/mcp");
        var body = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
        Assert.Equal("claude-tamiza", body.GetProperty("clientId").GetString());
        Assert.Equal(34000, body.GetProperty("callbackPort").GetInt32());
    }

    [Fact]
    public async Task An_analyst_sees_the_latest_50_calls_of_the_project_newest_first_with_the_names_of_the_people()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        for (var i = 0; i < 55; i++)
        {
            await TestData.ExecuteAsync(h.Db,
                """
                INSERT INTO tamiza.mcp_calls (id, user_id, project_id, tool, access, status, error_code, duration_ms, result_chars, started_at)
                VALUES (gen_random_uuid(), @user, @project, @tool, 'aggregates', 'succeeded', NULL, 12, 340, now() - make_interval(mins => @minutes))
                """,
                ("user", h.Users["analyst"]), ("project", h.ProjectId), ("tool", $"tool_{i:00}"), ("minutes", i + 1));
        }

        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.mcp_calls (id, user_id, project_id, tool, access, status, error_code, duration_ms, started_at) VALUES (gen_random_uuid(), @user, @project, 'get_form', NULL, 'refused', 'mcp.projectOff', 1, now())",
            ("user", h.Users["admin"]), ("project", h.ProjectId));
        // Another project's calls and calls that name no project are not listed.
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.mcp_calls (id, user_id, project_id, tool, status, duration_ms, started_at) VALUES (gen_random_uuid(), @user, NULL, 'list_projects', 'succeeded', 1, now())",
            ("user", h.Users["admin"]));
        using var analyst = Person(h, "analyst");

        var response = await analyst.GetAsync($"/api/v1/projects/{h.ProjectId}/mcp-calls");
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(50, items.Count);
        Assert.Equal("get_form", items[0].GetProperty("tool").GetString());
        Assert.Equal("refused", items[0].GetProperty("status").GetString());
        Assert.Equal("mcp.projectOff", items[0].GetProperty("errorCode").GetString());
        Assert.Equal("admin", items[0].GetProperty("userName").GetString());
        Assert.Equal("tool_00", items[1].GetProperty("tool").GetString());
        Assert.Equal("analyst", items[1].GetProperty("userName").GetString());
        Assert.Equal("aggregates", items[1].GetProperty("access").GetString());
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
        Assert.Equal("tool_48", items[^1].GetProperty("tool").GetString());
        Assert.DoesNotContain(items, i => i.GetProperty("tool").GetString() == "list_projects");
        Assert.Equal(items.OrderByDescending(i => i.GetProperty("startedAt").GetDateTimeOffset()).Select(i => i.GetProperty("tool").GetString()), items.Select(i => i.GetProperty("tool").GetString()));
    }

    [Fact]
    public async Task A_viewer_and_a_stranger_cannot_read_the_calls_of_a_project()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        using var viewer = Person(h, "viewer");
        using var outsider = Person(h, "outsider");
        using var admin = Person(h, "admin");

        var asViewer = await viewer.GetAsync($"/api/v1/projects/{h.ProjectId}/mcp-calls");
        var asOutsider = await outsider.GetAsync($"/api/v1/projects/{h.ProjectId}/mcp-calls");
        var asAdmin = await admin.GetAsync($"/api/v1/projects/{h.ProjectId}/mcp-calls");

        Assert.Equal(HttpStatusCode.Forbidden, asViewer.StatusCode);
        Assert.True(asOutsider.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{asOutsider.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        Assert.Empty((await asAdmin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task The_openapi_document_describes_the_mcp_endpoints()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var paths = document.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/v1/mcp", out var connection), "GET /api/v1/mcp is missing");
        Assert.True(connection.TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/api/v1/projects/{projectId}/mcp-calls", out var calls), "GET /api/v1/projects/{projectId}/mcp-calls is missing");
        Assert.True(calls.TryGetProperty("get", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("McpConnection", out var connectionSchema));
        foreach (var property in new[] { "url", "clientId", "callbackPort", "maxDataAccess", "accountUrl" })
        {
            Assert.True(connectionSchema.GetProperty("properties").TryGetProperty(property, out _), $"McpConnection.{property}");
        }

        Assert.True(schemas.TryGetProperty("McpCallItem", out _));
        Assert.True(schemas.GetProperty("ProjectDetails").GetProperty("properties").TryGetProperty("mcpAccess", out _));
        Assert.True(schemas.GetProperty("ProjectDetails").GetProperty("properties").TryGetProperty("mcpMaxAccess", out _));
        Assert.True(schemas.GetProperty("UpdateProjectRequest").GetProperty("properties").TryGetProperty("mcpAccess", out _));
        // The MCP endpoint itself is a protocol, not a REST resource, and stays out of the document.
        Assert.False(paths.TryGetProperty("/mcp", out _));
    }
}
