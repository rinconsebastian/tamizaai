using DbUp;
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

    [Fact]
    public async Task Ingestion_script_upgrades_a_database_at_the_initial_schema_and_schedules_existing_projects()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(connectionString, SchemaMigrator.InitialScript);
        await ExecuteAsync(connectionString, """
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('00000000-0000-0000-0000-000000000001', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES (gen_random_uuid(), 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(), '00000000-0000-0000-0000-000000000001', now(), now());
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        Assert.True(await TableExistsAsync(connectionString, "raw_submissions"));
        Assert.True(await TableExistsAsync(connectionString, "syncs"));
        // Existing projects are due for their first synchronization at once and reconcile a day later.
        Assert.Equal("12 24 t t", await ScalarAsync<string>(connectionString, """
            SELECT format('%s %s %s %s', sync_interval_hours, reconcile_interval_hours,
                          next_sync_at <= now(), next_reconcile_at > now() + interval '23 hours')
            FROM tamiza.projects
            """));
    }

    [Fact]
    public async Task Pipeline_script_upgrades_a_database_at_the_ingestion_schema()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql");
        await ExecuteAsync(connectionString, """
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('00000000-0000-0000-0000-000000000001', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES (gen_random_uuid(), 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(), '00000000-0000-0000-0000-000000000001', now(), now());
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        foreach (var table in new[] { "script_versions", "runs", "run_steps" })
        {
            Assert.True(await TableExistsAsync(connectionString, table), table);
        }

        // Existing projects run after each synchronization, with no schedule and no choices until the next snapshot.
        Assert.Equal("t <NULL> <NULL> []", await ScalarAsync<string>(connectionString, """
            SELECT format('%s %s %s %s', run_after_sync, coalesce(run_interval_hours::text, '<NULL>'),
                          coalesce(next_run_at::text, '<NULL>'), form_choices)
            FROM tamiza.projects
            """));
    }

    [Fact]
    public async Task Pipeline_authoring_script_upgrades_a_database_at_the_pipeline_schema()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql", "0003_add_pipeline.sql");
        await ExecuteAsync(connectionString, """
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('00000000-0000-0000-0000-000000000001', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-0000000000aa', 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(),
                    '00000000-0000-0000-0000-000000000001', now(), now());
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000aa', 'manual', 'succeeded', now());
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        foreach (var table in new[] { "script_tests", "reference_tables", "reference_table_contents" })
        {
            Assert.True(await TableExistsAsync(connectionString, table), table);
        }

        // Existing runs used no reference tables.
        Assert.Equal("[]", await ScalarAsync<string>(connectionString, "SELECT reference_tables::text FROM tamiza.runs"));
    }

    [Fact]
    public async Task Choice_labels_script_records_the_versions_of_stored_submissions_as_pending()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql", "0003_add_pipeline.sql", "0004_add_pipeline_authoring.sql");
        await ExecuteAsync(connectionString, """
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('00000000-0000-0000-0000-000000000001', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-0000000000aa', 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(),
                    '00000000-0000-0000-0000-000000000001', now(), now());
            INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, form_version, data, origin, received_at, updated_at, deleted)
            VALUES ('00000000-0000-0000-0000-0000000000aa', 1, 'u1', 'vA', '{}', 'sync', now(), now(), false),
                   ('00000000-0000-0000-0000-0000000000aa', 2, 'u2', 'vA', '{}', 'webhook', now(), now(), false),
                   ('00000000-0000-0000-0000-0000000000aa', 3, 'u3', 'vB', '{}', 'sync', now(), now(), false),
                   ('00000000-0000-0000-0000-0000000000aa', 4, 'u4', NULL, '{}', 'sync', now(), now(), false),
                   ('00000000-0000-0000-0000-0000000000aa', 5, 'u5', 'vGone', '{}', 'sync', now(), now(), true);
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        // One pending row per version in use, due at once; a submission without a version and a deleted one record nothing.
        Assert.Equal("vA pending t 0 | vB pending t 0", await ScalarAsync<string>(connectionString, """
            SELECT string_agg(format('%s %s %s %s', uid, status, next_attempt_at <= now(), attempts), ' | ' ORDER BY uid)
            FROM tamiza.form_versions
            """));
        Assert.Equal(2L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.form_versions"));
        // The project has no snapshot version until its next synchronization.
        Assert.True(await ScalarAsync<bool>(connectionString, "SELECT form_version IS NULL FROM tamiza.projects"));
    }

    [Fact]
    public async Task Performance_dashboard_script_gives_existing_projects_the_utc_time_zone_and_adds_its_index()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(
            connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql", "0003_add_pipeline.sql", "0004_add_pipeline_authoring.sql",
            "0005_add_choice_labels.sql");
        await ExecuteAsync(connectionString, """
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('00000000-0000-0000-0000-000000000001', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-0000000000aa', 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(),
                    '00000000-0000-0000-0000-000000000001', now(), now());
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        Assert.Equal("UTC", await ScalarAsync<string>(connectionString, "SELECT time_zone FROM tamiza.projects"));
        Assert.Equal(
            "CREATE INDEX ix_raw_submissions_project_id_submitted_at ON tamiza.raw_submissions USING btree (project_id, submitted_at) WHERE (NOT deleted)",
            await ScalarAsync<string>(connectionString, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_raw_submissions_project_id_submitted_at'"));
    }

    [Fact]
    public async Task Indicators_dashboard_script_adds_the_widgets_table_and_keeps_existing_data()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(
            connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql", "0003_add_pipeline.sql", "0004_add_pipeline_authoring.sql",
            "0005_add_choice_labels.sql", "0006_add_performance_dashboard.sql");
        await ExecuteAsync(connectionString, """
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('00000000-0000-0000-0000-000000000001', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-0000000000aa', 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(),
                    '00000000-0000-0000-0000-000000000001', now(), now());
            """);
        Assert.False(await TableExistsAsync(connectionString, "dashboard_widgets"));

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        Assert.Contains("0007_add_indicators_dashboard.sql", Scripts);
        Assert.True(await TableExistsAsync(connectionString, "dashboard_widgets"));
        Assert.Equal("Existing", await ScalarAsync<string>(connectionString, "SELECT name FROM tamiza.projects"));
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.dashboard_widgets"));

        // The defaults fill in what a widget does not say, and the constraints refuse what it must not be.
        await ExecuteAsync(connectionString, """
            INSERT INTO tamiza.dashboard_widgets (project_id, chart_type, title, created_by, updated_by)
            VALUES ('00000000-0000-0000-0000-0000000000aa', 'map', 'First', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001')
            """);
        Assert.Equal("{} {}", await ScalarAsync<string>(connectionString, "SELECT config::text || ' ' || layout::text FROM tamiza.dashboard_widgets"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, """
            INSERT INTO tamiza.dashboard_widgets (project_id, chart_type, title, created_by, updated_by)
            VALUES ('00000000-0000-0000-0000-0000000000aa', 'radar', 'Bad', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001')
            """));

        // Deleting the project deletes its widgets.
        await ExecuteAsync(connectionString, "DELETE FROM tamiza.projects WHERE id = '00000000-0000-0000-0000-0000000000aa'");
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.dashboard_widgets"));
    }

    /// <summary>Brings a database to an earlier schema by applying only the named scripts.</summary>
    private static void ApplyScripts(string connectionString, params string[] names)
    {
        var builder = DeployChanges.To
            .PostgresqlDatabase(connectionString, SchemaMigrator.Schema)
            .JournalToPostgresqlTable(SchemaMigrator.Schema, SchemaMigrator.JournalTable)
            .WithVariablesDisabled();
        foreach (var name in names)
        {
            builder = builder.WithScript(name, ReadScript(name));
        }

        Assert.True(builder.Build().PerformUpgrade().Successful);
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
