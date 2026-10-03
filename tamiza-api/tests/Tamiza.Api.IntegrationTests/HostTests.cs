using System.Net;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class HostTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Unknown_api_route_returns_problem_details()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient().WithToken(TestTokens.Create("user-1"));

        var response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unauthenticated_request_gets_problem_details()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/openapi.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync());
    }
}
