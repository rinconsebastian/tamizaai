using System.Net;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpAvailabilityTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Not_enabled_the_endpoint_and_the_metadata_answer_404_and_the_config_says_so(string? enabled)
    {
        var overrides = enabled is null ? null : new Dictionary<string, string?> { ["TAMIZA_MCP_ENABLED"] = enabled };
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), overrides);
        // Anyone gets 404, with a token or without one.
        foreach (var token in new string?[] { null, McpHarness.TokenFor("user-1") })
        {
            using var http = factory.CreateClient();
            if (token is not null)
            {
                http.WithToken(token);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await McpHarness.InitializeAsync(http)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/mcp")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/.well-known/oauth-protected-resource/mcp")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/.well-known/oauth-protected-resource")).StatusCode);
        }

        using var anonymous = factory.CreateClient();
        Assert.False(await McpEnabledAsync(anonymous));
    }

    [Fact]
    public async Task Enabled_an_unauthenticated_initialize_is_asked_to_sign_in_and_the_config_says_so()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http();

        var response = await McpHarness.InitializeAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(await McpEnabledAsync(http));
    }

    [Fact]
    public async Task Enabled_a_person_with_a_token_initializes_and_gets_the_servers_name_and_instructions()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("user-1"));

        Assert.Equal("tamiza", client.ServerInfo.Name);
        Assert.False(string.IsNullOrWhiteSpace(client.ServerInstructions));
    }

    [Fact]
    public async Task A_malformed_setting_stops_the_api_naming_the_variable()
    {
        await using var harness = await McpHarness.StartAsync(postgres, new Dictionary<string, string?> { ["TAMIZA_MCP_MAX_DATA_ACCESS"] = "everything" });

        var failure = Assert.ThrowsAny<Exception>(() => harness.Http());

        Assert.Contains("TAMIZA_MCP_MAX_DATA_ACCESS", failure.ToString());
    }

    [Theory]
    [InlineData("http://tamiza.example.org", true)]
    [InlineData("https://tamiza.example.org", false)]
    [InlineData("http://localhost:8088", false)]
    public async Task A_public_address_the_hosted_apps_cannot_reach_is_warned_about_once(string publicUrl, bool warned)
    {
        await using var harness = await McpHarness.StartAsync(postgres, new Dictionary<string, string?> { ["TAMIZA_PUBLIC_URL"] = publicUrl });
        using var http = harness.Http();
        await http.GetStringAsync("/api/v1/system/config");

        var warnings = harness.Factory.Logs.Lines.Where(l => l.StartsWith("Warning", StringComparison.Ordinal) && l.Contains("MCP server is enabled")).ToList();

        Assert.Equal(warned ? 1 : 0, warnings.Count);
    }

    private static async Task<bool> McpEnabledAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/system/config"));
        return body.RootElement.GetProperty("mcp").GetProperty("enabled").GetBoolean();
    }
}
