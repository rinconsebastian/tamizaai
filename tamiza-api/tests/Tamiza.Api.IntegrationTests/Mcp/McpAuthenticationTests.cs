using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpAuthenticationTests(PostgresFixture postgres)
{
    private const string MetadataUrl = "https://tamiza.test/.well-known/oauth-protected-resource/mcp";

    [Fact]
    public async Task First_request_of_a_client_is_told_where_to_find_the_realm()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http();

        var response = await McpHarness.InitializeAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Equal($"resource_metadata=\"{MetadataUrl}\", scope=\"tamiza-mcp\"", challenge.Parameter);
    }

    [Fact]
    public async Task The_metadata_names_the_address_the_realm_and_the_scope_whatever_the_host_of_the_request()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http();
        http.DefaultRequestHeaders.Host = "internal.example:9999";

        var challenge = Assert.Single((await McpHarness.InitializeAsync(http)).Headers.WwwAuthenticate);
        using var body = JsonDocument.Parse(await http.GetStringAsync(MetadataUrl));

        Assert.Contains(MetadataUrl, challenge.Parameter);
        Assert.Equal(McpHarness.Address, body.RootElement.GetProperty("resource").GetString());
        Assert.Equal([TestTokens.Issuer], body.RootElement.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(["tamiza-mcp"], body.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(["header"], body.RootElement.GetProperty("bearer_methods_supported").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task The_metadata_is_public_and_the_root_form_gives_the_same_document()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http();

        var pathForm = await http.GetStringAsync("/.well-known/oauth-protected-resource/mcp");
        var rootForm = await http.GetStringAsync("/.well-known/oauth-protected-resource");

        Assert.Equal(pathForm, rootForm);
    }

    [Fact]
    public async Task A_valid_token_for_the_mcp_audience_signs_the_person_in()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("sub-1", "Ada", "ada@example.org"));

        var user = Assert.Single(await ReadUsersAsync(harness.ConnectionString));
        Assert.Equal(("sub-1", "Ada", "ada@example.org"), (user.Sub, user.Name, user.Email));
        Assert.NotNull(client.ServerInfo);
    }

    [Fact]
    public async Task Token_of_the_ui_is_refused_at_the_mcp_endpoint()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(TestTokens.Create("sub-1"));

        var response = await McpHarness.InitializeAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal($"error=\"invalid_token\", resource_metadata=\"{MetadataUrl}\", scope=\"tamiza-mcp\"", challenge.Parameter);
        Assert.Empty(await ReadUsersAsync(harness.ConnectionString));
    }

    [Theory]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/projects")]
    [InlineData("/api/v1/projects/6d8c1c1e-0000-4000-8000-000000000001")]
    [InlineData("/api/v1/projects/6d8c1c1e-0000-4000-8000-000000000001/scripts")]
    [InlineData("/api/v1/projects/6d8c1c1e-0000-4000-8000-000000000001/data")]
    [InlineData("/api/v1/projects/6d8c1c1e-0000-4000-8000-000000000001/performance")]
    [InlineData("/api/v1/projects/6d8c1c1e-0000-4000-8000-000000000001/indicators")]
    [InlineData("/api/v1/projects/6d8c1c1e-0000-4000-8000-000000000001/dashboards/6d8c1c1e-0000-4000-8000-000000000002")]
    [InlineData("/api/v1/projects/6d8c1c1e-0000-4000-8000-000000000001/assistant")]
    [InlineData("/api/v1/projects/6d8c1c1e-0000-4000-8000-000000000001/members")]
    public async Task Token_of_an_ai_client_is_refused_by_the_rest_api(string path)
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(McpHarness.TokenFor("sub-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(path)).StatusCode);
        Assert.Empty(await ReadUsersAsync(harness.ConnectionString));
    }

    [Fact]
    public async Task Token_of_the_ui_still_works_on_the_rest_api_with_the_mcp_server_enabled()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(TestTokens.Create("sub-1"));

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Expired_token_gets_the_same_challenge_so_the_client_can_refresh()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(TestTokens.Create("sub-1", audience: TestTokens.McpAudience, expires: DateTime.UtcNow.AddHours(-1)));

        var response = await McpHarness.InitializeAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Contains("error=\"invalid_token\"", challenge.Parameter);
        Assert.Contains($"resource_metadata=\"{MetadataUrl}\"", challenge.Parameter);
    }

    [Fact]
    public async Task Token_from_another_issuer_is_refused()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(TestTokens.Create("sub-1", issuer: "https://idp.test/realms/other", audience: TestTokens.McpAudience));

        Assert.Equal(HttpStatusCode.Unauthorized, (await McpHarness.InitializeAsync(http)).StatusCode);
    }

    [Fact]
    public async Task Token_signed_by_another_key_is_refused()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        var foreignKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };
        using var http = harness.Http(TestTokens.Create("sub-1", audience: TestTokens.McpAudience, key: foreignKey));

        Assert.Equal(HttpStatusCode.Unauthorized, (await McpHarness.InitializeAsync(http)).StatusCode);
    }

    [Fact]
    public async Task Token_without_a_subject_is_refused()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestTokens.Issuer,
            Audience = TestTokens.McpAudience,
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["name"] = "No Subject" },
            SigningCredentials = new SigningCredentials(TestTokens.SigningKey, SecurityAlgorithms.RsaSha256),
        });
        using var http = harness.Http(token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await McpHarness.InitializeAsync(http)).StatusCode);
        Assert.Empty(await ReadUsersAsync(harness.ConnectionString));
    }

    [Fact]
    public async Task The_person_of_an_mcp_token_is_the_same_local_user_as_in_the_ui_with_name_and_email_refreshed()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var ui = harness.Http(TestTokens.Create("sub-1", name: "Ada", email: "ada@example.org"));
        Assert.Equal(HttpStatusCode.OK, (await ui.GetAsync("/api/v1/me")).StatusCode);
        var before = Assert.Single(await ReadUsersAsync(harness.ConnectionString));

        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("sub-1", "Ada L.", "ada@new.example.org"));

        var after = Assert.Single(await ReadUsersAsync(harness.ConnectionString));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(("Ada L.", "ada@new.example.org"), (after.Name, after.Email));
        Assert.NotNull(client.ServerInfo);
    }

    [Fact]
    public async Task A_page_of_another_site_is_refused_before_anything_else()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(McpHarness.TokenFor("sub-1"));

        var response = await McpHarness.InitializeAsync(http, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await ReadUsersAsync(harness.ConnectionString));
    }

    [Theory]
    [InlineData("https://tamiza.test")]
    [InlineData("https://tamiza.test/")]
    [InlineData("HTTPS://TAMIZA.TEST")]
    public async Task The_public_origin_is_accepted(string origin)
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(McpHarness.TokenFor("sub-1"));

        Assert.Equal(HttpStatusCode.OK, (await McpHarness.InitializeAsync(http, origin)).StatusCode);
    }

    [Fact]
    public async Task A_request_without_an_origin_is_accepted()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(McpHarness.TokenFor("sub-1"));

        Assert.Equal(HttpStatusCode.OK, (await McpHarness.InitializeAsync(http)).StatusCode);
    }

    [Theory]
    [InlineData("http://tamiza.test")]
    [InlineData("https://tamiza.test:8443")]
    [InlineData("https://sub.tamiza.test")]
    [InlineData("null")]
    public async Task An_origin_that_only_looks_like_the_public_one_is_refused(string origin)
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        using var http = harness.Http(McpHarness.TokenFor("sub-1"));

        Assert.Equal(HttpStatusCode.Forbidden, (await McpHarness.InitializeAsync(http, origin)).StatusCode);
    }

    private sealed record UserRow(Guid Id, string Sub, string Name, string? Email);

    private static async Task<List<UserRow>> ReadUsersAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id, keycloak_sub, name, email FROM tamiza.users ORDER BY keycloak_sub", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var users = new List<UserRow>();
        while (await reader.ReadAsync())
        {
            users.Add(new UserRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return users;
    }
}
