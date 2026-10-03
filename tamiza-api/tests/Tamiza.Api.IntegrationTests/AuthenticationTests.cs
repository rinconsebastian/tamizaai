using System.Net;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AuthenticationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Request_without_token_is_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(token: null));
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = TestTokens.Create("user-1", expires: DateTime.UtcNow.AddHours(-1));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(token));
    }

    [Fact]
    public async Task Token_from_another_issuer_is_rejected()
    {
        var token = TestTokens.Create("user-1", issuer: "https://idp.test/realms/other");
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(token));
    }

    [Fact]
    public async Task Token_signed_by_another_key_is_rejected()
    {
        var foreignKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };
        var token = TestTokens.Create("user-1", key: foreignKey);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(token));
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected()
    {
        var token = TestTokens.Create("user-1", audience: "another-api");
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(token));
    }

    [Fact]
    public async Task Valid_token_is_accepted()
    {
        Assert.Equal(HttpStatusCode.OK, await GetMeStatusAsync(TestTokens.Create("user-1")));
    }

    private async Task<HttpStatusCode> GetMeStatusAsync(string? token)
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.WithToken(token);
        }

        return (await client.GetAsync("/api/v1/me")).StatusCode;
    }
}
