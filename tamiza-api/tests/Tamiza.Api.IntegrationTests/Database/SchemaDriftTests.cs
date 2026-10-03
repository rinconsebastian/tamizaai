using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.DbUp;

namespace Tamiza.Api.IntegrationTests.Database;

/// <summary>
/// The SQL scripts own the schema and EF Core only maps it, so the two are maintained separately. This test builds one
/// database from the scripts and one from the EF model and requires the same tables, columns, constraints and indexes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaDriftTests(PostgresFixture postgres)
{
    /// <summary>Tables in the schema that EF Core deliberately does not map.</summary>
    private static readonly string[] Unmapped = [SchemaMigrator.JournalTable];

    [Fact]
    public async Task Ef_model_matches_the_schema_built_by_the_scripts()
    {
        var fromScripts = await postgres.CreateDatabaseAsync();
        await SchemaMigrator.MigrateAsync(fromScripts, NullLogger.Instance);

        var fromModel = await postgres.CreateDatabaseAsync();
        var options = new DbContextOptionsBuilder<TamizaDbContext>();
        DatabaseRegistration.Configure(options, fromModel);
        await using (var db = new TamizaDbContext(options.Options))
        {
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        var scripts = await CatalogAsync(fromScripts);
        var model = await CatalogAsync(fromModel);
        var onlyInScripts = scripts.Except(model).ToList();
        var onlyInModel = model.Except(scripts).ToList();

        Assert.True(onlyInScripts.Count == 0 && onlyInModel.Count == 0,
            "The EF model and the SQL scripts disagree.\n" +
            $"Only in the SQL scripts:\n  {string.Join("\n  ", onlyInScripts)}\n" +
            $"Only in the EF model:\n  {string.Join("\n  ", onlyInModel)}");
    }

    /// <summary>One line per table, column, constraint and index in the metadata schema; column order is ignored.</summary>
    private static async Task<HashSet<string>> CatalogAsync(string connectionString)
    {
        const string sql = """
            SELECT format('table %s', table_name)
            FROM information_schema.tables
            WHERE table_schema = @schema AND NOT table_name = ANY(@unmapped)
            UNION ALL
            SELECT format('column %s.%s %s(%s) nullable=%s default=%s',
                table_name, column_name, data_type, character_maximum_length, is_nullable, column_default)
            FROM information_schema.columns
            WHERE table_schema = @schema AND NOT table_name = ANY(@unmapped)
            UNION ALL
            SELECT format('constraint %s.%s %s', c.relname, con.conname, pg_get_constraintdef(con.oid))
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = con.connamespace
            WHERE n.nspname = @schema AND NOT c.relname = ANY(@unmapped)
            UNION ALL
            SELECT format('index %s.%s %s', tablename, indexname, indexdef)
            FROM pg_indexes
            WHERE schemaname = @schema AND NOT tablename = ANY(@unmapped)
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", SchemaMigrator.Schema);
        command.Parameters.AddWithValue("unmapped", Unmapped);
        await using var reader = await command.ExecuteReaderAsync();
        var entries = new HashSet<string>();
        while (await reader.ReadAsync())
        {
            entries.Add(reader.GetString(0));
        }

        return entries;
    }
}
