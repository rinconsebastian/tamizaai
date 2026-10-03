using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Startup_applies_migrations_to_an_empty_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new TamizaApiFactory(connectionString);
        factory.CreateClient().Dispose();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass('tamiza.users') IS NOT NULL", connection);

        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }
}
