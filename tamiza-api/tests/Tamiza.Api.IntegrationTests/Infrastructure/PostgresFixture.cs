using Npgsql;
using Testcontainers.PostgreSql;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>One PostGIS container per test run; every test gets its own empty database in it.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgis/postgis:17-3.5";

    private readonly PostgreSqlContainer _container = CreateContainer();

    public static PostgreSqlContainer CreateContainer() => new PostgreSqlBuilder(Image).Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
