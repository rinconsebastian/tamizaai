using System.Net;
using System.Net.Http.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MeTests(PostgresFixture postgres)
{
    private sealed record Me(Guid Id, string Name, string? Email, bool IsSuperAdmin);

    [Fact]
    public async Task Returns_the_profile_of_the_signed_in_user()
    {
        await using var factory = new TamizaApiFactory(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { ["TAMIZA_SUPERADMIN_EMAILS"] = "root@example.org, ADA@example.org" });
        using var client = factory.CreateClient().WithToken(TestTokens.Create("sub-1", name: "Ada", email: "ada@example.org"));

        var me = await client.GetFromJsonAsync<Me>("/api/v1/me");

        Assert.NotNull(me);
        Assert.NotEqual(Guid.Empty, me.Id);
        Assert.Equal(("Ada", "ada@example.org", true), (me.Name, me.Email, me.IsSuperAdmin));
    }

    [Fact]
    public async Task Unverified_listed_email_is_not_superadmin()
    {
        await using var factory = new TamizaApiFactory(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { ["TAMIZA_SUPERADMIN_EMAILS"] = "ada@example.org" });
        using var client = factory.CreateClient().WithToken(TestTokens.Create("sub-1", email: "ada@example.org", emailVerified: false));

        var me = await client.GetFromJsonAsync<Me>("/api/v1/me");

        Assert.False(me!.IsSuperAdmin);
    }

    [Fact]
    public async Task Requires_a_token()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }
}
