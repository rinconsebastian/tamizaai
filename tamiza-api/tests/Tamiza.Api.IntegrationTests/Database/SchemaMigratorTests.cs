using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.DbUp;

namespace Tamiza.Api.IntegrationTests.Database;

[Collection(PostgresCollection.Name)]
public sealed class SchemaMigratorTests(PostgresFixture postgres)
{
    private static readonly string[] Scripts = typeof(SchemaMigrator).Assembly.GetManifestResourceNames()
        .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public async Task Migrates_an_empty_database_and_journals_every_script()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Contains(SchemaMigrator.InitialScript, Scripts);
        Assert.Equal(Scripts, await JournalAsync(connectionString));
    }

    [Fact]
    public async Task A_second_run_applies_nothing_and_keeps_the_data()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);
        await ExecuteAsync(connectionString,
            "INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES (gen_random_uuid(), 'sub-1', 'Ada', now(), now())");
        const string journalRows = "SELECT string_agg(schemaversionsid || ' ' || scriptname || ' ' || applied, ', ' ORDER BY schemaversionsid) FROM tamiza.schema_versions";
        var journalBefore = await ScalarAsync<string>(connectionString, journalRows);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(journalBefore, await ScalarAsync<string>(connectionString, journalRows));
        Assert.Equal("Ada", await ScalarAsync<string>(connectionString, "SELECT name FROM tamiza.users WHERE keycloak_sub = 'sub-1'"));
    }

    [Fact]
    public async Task Concurrent_runs_apply_each_script_once()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => Task.Run(() => SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance))));

        Assert.Equal(Scripts, await JournalAsync(connectionString));
    }

    [Fact]
    public async Task A_failing_script_leaves_no_trace_and_earlier_scripts_stay_applied()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = SchemaMigrator.CreateBuilder(connectionString, NullLogger.Instance)
            .WithScript("9999_failing.sql", "CREATE TABLE tamiza.half_done (id int);\nSELECT 1 / 0;")
            .Build()
            .PerformUpgrade();

        Assert.False(result.Successful);
        Assert.Equal("9999_failing.sql", result.ErrorScript.Name);
        Assert.False(await TableExistsAsync(connectionString, "half_done"));
        Assert.Equal(Scripts, await JournalAsync(connectionString));
    }

    [Fact]
    public async Task Adopts_a_database_created_by_the_last_ef_migration()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ExecuteAsync(connectionString, ReadScript(SchemaMigrator.InitialScript));
        await CreateEfHistoryAsync(connectionString, "20261002214441_InitialCreate", SchemaMigrator.LastEfMigration);
        await ExecuteAsync(connectionString,
            "INSERT INTO tamiza.users (id, keycloak_sub, name, email_verified, created_at, updated_at) VALUES (gen_random_uuid(), 'sub-1', 'Ada', true, now(), now())");

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        Assert.False(await TableExistsAsync(connectionString, SchemaMigrator.EfHistoryTable));
        Assert.Equal("Ada", await ScalarAsync<string>(connectionString, "SELECT name FROM tamiza.users WHERE keycloak_sub = 'sub-1' AND email_verified"));
    }

    [Fact]
    public async Task Refuses_a_database_at_an_older_ef_migration_without_changing_it()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ExecuteAsync(connectionString, """
            CREATE SCHEMA tamiza;
            CREATE TABLE tamiza.users (
                id uuid NOT NULL,
                keycloak_sub character varying(255) NOT NULL,
                name character varying(255) NOT NULL,
                email character varying(320),
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_users PRIMARY KEY (id)
            );
            """);
        await CreateEfHistoryAsync(connectionString, "20261002214441_InitialCreate");

        var error = await Assert.ThrowsAsync<SchemaMigrationException>(() => SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance));

        Assert.Contains("must be recreated", error.Message);
        Assert.False(await TableExistsAsync(connectionString, SchemaMigrator.JournalTable));
        Assert.True(await TableExistsAsync(connectionString, SchemaMigrator.EfHistoryTable));
        Assert.False(await TableExistsAsync(connectionString, "projects"));
    }

    private static string ReadScript(string name)
    {
        using var stream = typeof(SchemaMigrator).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The history table exactly as EF Core created it.</summary>
    private static Task CreateEfHistoryAsync(string connectionString, params string[] migrations) =>
        ExecuteAsync(connectionString, $"""
            CREATE TABLE tamiza.__ef_migrations_history (
                migration_id character varying(150) NOT NULL,
                product_version character varying(32) NOT NULL,
                CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
            );
            {string.Concat(migrations.Select(id => $"INSERT INTO tamiza.__ef_migrations_history VALUES ('{id}', '10.0.12');"))}
            """);

    private static async Task<string[]> JournalAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT scriptname FROM tamiza.schema_versions ORDER BY scriptname", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return [.. names];
    }

    private static Task<bool> TableExistsAsync(string connectionString, string table) =>
        ScalarAsync<bool>(connectionString, $"SELECT to_regclass('tamiza.{table}') IS NOT NULL");

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
