using DbUp;
using DbUp.Builder;
using DbUp.Engine;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tamiza.DbUp;

/// <summary>Brings the metadata schema up to date by applying the SQL scripts in <c>Scripts/</c>.</summary>
public static class SchemaMigrator
{
    /// <summary>Schema that holds the metadata tables and the journal.</summary>
    public const string Schema = "tamiza";

    /// <summary>DbUp's record of the scripts already applied.</summary>
    public const string JournalTable = "schema_versions";

    /// <summary>The script that creates the schema the EF Core migrations used to leave behind.</summary>
    internal const string InitialScript = "0001_initial_schema.sql";

    /// <summary>The last EF Core migration. A database at this migration has exactly the schema <see cref="InitialScript"/> creates.</summary>
    internal const string LastEfMigration = "20261003010233_AddProjects";

    internal const string EfHistoryTable = "__ef_migrations_history";

    /// <summary>Advisory lock held while migrating, so concurrent processes apply each script once. ASCII "TAMIZA".</summary>
    private const long LockKey = 0x54414D495A41;

    /// <summary>
    /// Applies the pending scripts, each in its own transaction. Throws <see cref="SchemaMigrationException"/> when a
    /// script fails or the database cannot be upgraded; scripts applied before the failure stay applied.
    /// </summary>
    public static async Task MigrateAsync(string connectionString, ILogger logger, CancellationToken cancellationToken = default)
    {
        // Unpooled, so closing the connection ends the session and releases the lock even if this method fails.
        var lockConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(lockConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, $"SELECT pg_advisory_lock({LockKey})", cancellationToken);

        var engine = CreateBuilder(connectionString, logger).Build();
        await AdoptEfDatabaseAsync(connection, engine, logger, cancellationToken);

        var result = engine.PerformUpgrade();
        if (!result.Successful)
        {
            throw new SchemaMigrationException($"Script {result.ErrorScript?.Name} failed: {result.Error.Message}", result.Error);
        }
    }

    /// <summary>The engine configuration shared by the API, the console runner and the tests.</summary>
    internal static UpgradeEngineBuilder CreateBuilder(string connectionString, ILogger logger) =>
        DeployChanges.To
            .PostgresqlDatabase(connectionString, Schema)
            .JournalToPostgresqlTable(Schema, JournalTable)
            .WithScriptsEmbeddedInAssembly(typeof(SchemaMigrator).Assembly, name => name.EndsWith(".sql", StringComparison.Ordinal))
            .WithTransactionPerScript()
            // DbUp's $name$ substitution would clash with PostgreSQL dollar quoting.
            .WithVariablesDisabled()
            .LogTo(logger);

    /// <summary>
    /// Takes over a database created by the EF Core migrations that preceded DbUp: records the initial script as applied
    /// and drops the EF history table. Databases at an older EF migration cannot be upgraded.
    /// </summary>
    private static async Task AdoptEfDatabaseAsync(NpgsqlConnection connection, UpgradeEngine engine, ILogger logger, CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, EfHistoryTable, cancellationToken))
        {
            return;
        }

        // With the journal already there, the history table is a leftover from an interrupted adoption.
        if (!await TableExistsAsync(connection, JournalTable, cancellationToken))
        {
            await using var command = new NpgsqlCommand(
                $"SELECT EXISTS (SELECT 1 FROM {Schema}.{EfHistoryTable} WHERE migration_id = @id)", connection);
            command.Parameters.AddWithValue("id", LastEfMigration);
            if (!(bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                throw new SchemaMigrationException(
                    "The database was created by an earlier development version of Tamiza and must be recreated: " +
                    $"drop schema {Schema}, or remove the database volume with `docker compose down -v` (this deletes all data).");
            }

            logger.LogInformation("Adopting a database created by EF Core migrations: recording {Script} as applied.", InitialScript);
            var marked = engine.MarkAsExecuted(InitialScript);
            if (!marked.Successful)
            {
                throw new SchemaMigrationException($"Could not record {InitialScript} as applied: {marked.Error.Message}", marked.Error);
            }
        }

        await ExecuteAsync(connection, $"DROP TABLE {Schema}.{EfHistoryTable}", cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
        command.Parameters.AddWithValue("name", $"{Schema}.{table}");
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
