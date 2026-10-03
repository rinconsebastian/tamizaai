using System.Net;
using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProvisioningTests(PostgresFixture postgres)
{
    [Fact]
    public async Task First_request_creates_one_local_user_from_the_token()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new TamizaApiFactory(connectionString);
        using var client = factory.CreateClient().WithToken(TestTokens.Create("sub-1", name: "Ada", email: "ada@example.org"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);

        var users = await ReadUsersAsync(connectionString);
        var user = Assert.Single(users);
        Assert.Equal(("sub-1", "Ada", "ada@example.org"), (user.Sub, user.Name, user.Email));
    }

    [Fact]
    public async Task Changed_name_or_email_updates_the_existing_row()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new TamizaApiFactory(connectionString);
        using var client = factory.CreateClient();

        await client.WithToken(TestTokens.Create("sub-1", name: "Ada", email: "ada@example.org")).GetAsync("/api/v1/me");
        var before = Assert.Single(await ReadUsersAsync(connectionString));
        await client.WithToken(TestTokens.Create("sub-1", name: "Ada L.", email: "ada@new.example.org")).GetAsync("/api/v1/me");

        var after = Assert.Single(await ReadUsersAsync(connectionString));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(("Ada L.", "ada@new.example.org"), (after.Name, after.Email));
    }

    [Fact]
    public async Task Concurrent_first_requests_leave_exactly_one_row()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new TamizaApiFactory(connectionString);
        var token = TestTokens.Create("sub-concurrent");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var client = factory.CreateClient().WithToken(token);
            return (await client.GetAsync("/api/v1/me")).StatusCode;
        }));

        Assert.All(responses, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Single(await ReadUsersAsync(connectionString));
    }

    [Fact]
    public async Task Token_without_name_falls_back_to_the_subject()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new TamizaApiFactory(connectionString);
        using var client = factory.CreateClient().WithToken(TestTokens.Create("sub-anon", name: null, email: null));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);

        var user = Assert.Single(await ReadUsersAsync(connectionString));
        Assert.Equal(("sub-anon", (string?)null), (user.Name, user.Email));
    }

    private sealed record UserRow(Guid Id, string Sub, string Name, string? Email);

    private static async Task<List<UserRow>> ReadUsersAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id, keycloak_sub, name, email FROM tamiza.users", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<UserRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new UserRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return rows;
    }
}
