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
        Assert.Equal(["accountEmails", "assistant", "email", "mcp", "oidc", "version"], Keys(body.RootElement));
        Assert.Equal(["enabled"], Keys(body.RootElement.GetProperty("mcp")));
        Assert.False(body.RootElement.GetProperty("mcp").GetProperty("enabled").GetBoolean());
        Assert.Equal(["enabled"], Keys(body.RootElement.GetProperty("accountEmails")));
        Assert.Equal(["enabled"], Keys(body.RootElement.GetProperty("email")));
        Assert.Equal(["enabled"], Keys(body.RootElement.GetProperty("assistant")));
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

    [Fact]
    public async Task Config_says_the_assistant_is_off_without_its_variables()
    {
        var assistant = await GetAssistantAsync(new Dictionary<string, string?>());

        Assert.False(assistant.GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Config_says_the_assistant_is_off_unless_all_three_variables_are_set(bool url, bool model, bool key)
    {
        var settings = new Dictionary<string, string?>();
        if (url)
        {
            settings["TAMIZA_ASSISTANT_BASE_URL"] = "https://llm.provider.test/v1";
        }

        if (model)
        {
            settings["TAMIZA_ASSISTANT_MODEL"] = "secret-model-name";
        }

        if (key)
        {
            settings["TAMIZA_ASSISTANT_API_KEY"] = "sk-secret-key-value";
        }

        Assert.False((await GetAssistantAsync(settings)).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Config_reports_the_assistant_as_enabled_without_the_provider_the_model_or_the_key()
    {
        var settings = new Dictionary<string, string?>
        {
            ["TAMIZA_ASSISTANT_BASE_URL"] = "https://llm.provider.test/v1",
            ["TAMIZA_ASSISTANT_MODEL"] = "secret-model-name",
            ["TAMIZA_ASSISTANT_API_KEY"] = "sk-secret-key-value",
            ["TAMIZA_ASSISTANT_EXTRA_BODY"] = """{"reasoning_effort":"low"}""",
        };
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), settings);
        using var client = factory.CreateClient();

        var text = await client.GetStringAsync("/api/v1/system/config");

        using var body = JsonDocument.Parse(text);
        var assistant = body.RootElement.GetProperty("assistant");
        Assert.True(assistant.GetProperty("enabled").GetBoolean());
        Assert.Equal(["enabled"], Keys(assistant));
        Assert.DoesNotContain("provider.test", text);
        Assert.DoesNotContain("secret-model-name", text);
        Assert.DoesNotContain("sk-secret-key-value", text);
        Assert.DoesNotContain("reasoning_effort", text);
    }

    [Fact]
    public async Task Email_not_configured()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/system/config"));

        Assert.False(body.RootElement.GetProperty("email").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Email_configured_without_the_server_the_user_the_password_or_the_sender()
    {
        var settings = new Dictionary<string, string?>
        {
            ["TAMIZA_SMTP_HOST"] = "smtp.provider.test",
            ["TAMIZA_SMTP_PORT"] = "2525",
            ["TAMIZA_SMTP_USERNAME"] = "smtp-user-name",
            ["TAMIZA_SMTP_PASSWORD"] = "smtp-secret-password",
            ["TAMIZA_SMTP_FROM"] = "reports@sender.test",
            ["TAMIZA_SMTP_FROM_NAME"] = "Census reports",
        };
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), settings);
        using var client = factory.CreateClient();

        var text = await client.GetStringAsync("/api/v1/system/config");

        using var body = JsonDocument.Parse(text);
        var email = body.RootElement.GetProperty("email");
        Assert.True(email.GetProperty("enabled").GetBoolean());
        Assert.Equal(["enabled"], Keys(email));
        foreach (var secret in new[] { "provider.test", "2525", "smtp-user-name", "smtp-secret-password", "sender.test", "Census reports" })
        {
            Assert.DoesNotContain(secret, text);
        }
    }

    private async Task<JsonElement> GetAssistantAsync(Dictionary<string, string?> overrides)
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), overrides);
        using var client = factory.CreateClient();
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/system/config"));
        return body.RootElement.GetProperty("assistant").Clone();
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
