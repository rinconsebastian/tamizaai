using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Accounts;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class AccountsStartupTests(PostgresFixture postgres)
{
    private async Task<TamizaApiFactory> FactoryAsync(Dictionary<string, string?>? settings = null) =>
        new(await postgres.CreateDatabaseAsync(), settings ?? new Dictionary<string, string?>());

    [Fact]
    public async Task Without_the_keycloak_variables_the_api_starts_and_there_is_no_provider()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        Assert.Null(factory.Services.GetRequiredService<IAccountProviderSource>().Provider);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_deployment_on_another_oidc_provider_starts_without_a_realm_in_its_authority()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?> { ["OIDC_AUTHORITY"] = "https://accounts.example.com" });

        using var client = factory.CreateClient();

        Assert.Null(factory.Services.GetRequiredService<IAccountProviderSource>().Provider);
    }

    [Fact]
    public async Task With_the_client_id_and_secret_the_keycloak_adapter_is_the_provider()
    {
        using var stub = new KeycloakStub();
        await using var factory = await FactoryAsync(stub.Settings());

        Assert.NotNull(factory.Services.GetRequiredService<IAccountProviderSource>().Provider);
    }

    [Fact]
    public async Task A_bad_link_lifetime_stops_the_api_naming_the_variable()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?>
        {
            ["TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID"] = "tamiza-admin",
            ["TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET"] = "secret",
            ["TAMIZA_KEYCLOAK_LINK_HOURS"] = "0",
        });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("TAMIZA_KEYCLOAK_LINK_HOURS", failure.ToString());
        Assert.DoesNotContain("secret", failure.ToString().Replace("TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET", ""));
    }

    [Fact]
    public async Task A_bad_hourly_limit_stops_the_api_naming_the_variable()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?> { ["TAMIZA_ACCOUNT_EMAILS_PER_HOUR"] = "5000" });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("TAMIZA_ACCOUNT_EMAILS_PER_HOUR", failure.ToString());
    }

    [Fact]
    public async Task A_bad_keycloak_address_stops_the_api_naming_the_variable()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?> { ["TAMIZA_KEYCLOAK_ADMIN_URL"] = "keycloak:8080" });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("TAMIZA_KEYCLOAK_ADMIN_URL", failure.ToString());
    }

    [Fact]
    public async Task An_authority_without_a_realm_stops_an_enabled_adapter()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?>
        {
            ["OIDC_AUTHORITY"] = "https://accounts.example.com",
            ["TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID"] = "tamiza-admin",
            ["TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET"] = "secret",
        });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("OIDC_AUTHORITY", failure.ToString());
    }

    [Theory]
    [InlineData("TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID", "TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET")]
    [InlineData("TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET", "TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID")]
    public async Task Says_once_at_startup_which_variable_is_missing_when_only_one_is_set(string set, string missing)
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?> { [set] = "value-1" });
        using var client = factory.CreateClient();

        var warning = Assert.Single(factory.Logs.Lines, line => line.StartsWith("Warning ", StringComparison.Ordinal) && line.Contains("Account emails are off"));
        Assert.Contains($"because {missing} is not set", warning);
        Assert.DoesNotContain("value-1", warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Says_nothing_at_startup_when_none_or_both_are_set(bool both)
    {
        using var stub = new KeycloakStub();
        await using var factory = await FactoryAsync(both ? stub.Settings() : null);
        using var client = factory.CreateClient();

        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("Account emails are off"));
    }
}
