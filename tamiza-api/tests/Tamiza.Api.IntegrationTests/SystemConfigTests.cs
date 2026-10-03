using System.Net;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class SystemConfigTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Config_is_public_and_exposes_only_the_documented_keys()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["oidc", "version"], Keys(body.RootElement));
        Assert.Equal(["authority", "clientId", "scope"], Keys(body.RootElement.GetProperty("oidc")));
        Assert.Equal(TestTokens.Issuer, body.RootElement.GetProperty("oidc").GetProperty("authority").GetString());
    }

    [Fact]
    public async Task Config_reflects_each_deployment_settings()
    {
        var first = await GetOidcAsync(new Dictionary<string, string?> { ["OIDC_CLIENT_ID"] = "client-a", ["OIDC_SCOPE"] = "openid" });
        var second = await GetOidcAsync(new Dictionary<string, string?> { ["OIDC_CLIENT_ID"] = "client-b" });

        Assert.Equal("client-a", first.GetProperty("clientId").GetString());
        Assert.Equal("openid", first.GetProperty("scope").GetString());
        Assert.Equal("client-b", second.GetProperty("clientId").GetString());
        Assert.Equal("openid profile email", second.GetProperty("scope").GetString());
    }

    private async Task<JsonElement> GetOidcAsync(Dictionary<string, string?> overrides)
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), overrides);
        using var client = factory.CreateClient();
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/system/config"));
        return body.RootElement.GetProperty("oidc").Clone();
    }

    private static string[] Keys(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
}
