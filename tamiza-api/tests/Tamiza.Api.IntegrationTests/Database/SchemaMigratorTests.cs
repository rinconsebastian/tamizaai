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

    [Fact]
    public async Task Indicator_scripts_script_deletes_script_3_and_its_widgets_and_keeps_the_other_scripts()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(
            connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql", "0003_add_pipeline.sql", "0004_add_pipeline_authoring.sql",
            "0005_add_choice_labels.sql", "0006_add_performance_dashboard.sql", "0007_add_indicators_dashboard.sql");
        const string project = "00000000-0000-0000-0000-0000000000aa";
        const string user = "00000000-0000-0000-0000-000000000001";
        const string indicatorsSchema = "p_000000000000000000000000000000aa_indicators";
        const string cleanSchema = "p_000000000000000000000000000000aa_clean";
        await ExecuteAsync(connectionString, $"""
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('{user}', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES ('{project}', 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(), '{user}', now(), now());
            INSERT INTO tamiza.script_versions (id, project_id, kind, number, code, author_id, created_at) VALUES
                ('00000000-0000-0000-0000-0000000000c1', '{project}', 'clean', 1, 'def clean(raw, ctx): return raw', '{user}', now()),
                ('00000000-0000-0000-0000-0000000000c3', '{project}', 'indicators', 1, 'def compute_indicators(cleaned, ctx): pass', '{user}', now());
            INSERT INTO tamiza.runs (id, project_id, trigger, status, clean_version_id, indicators_version_id, queued_at) VALUES
                ('00000000-0000-0000-0000-0000000000d1', '{project}', 'manual', 'succeeded',
                 '00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000c3', now());
            INSERT INTO tamiza.run_steps (run_id, step, status) VALUES
                ('00000000-0000-0000-0000-0000000000d1', 'clean', 'succeeded'),
                ('00000000-0000-0000-0000-0000000000d1', 'indicators', 'succeeded');
            INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at) VALUES
                (gen_random_uuid(), '{project}', '{user}', 'clean', 'x', 500, 'succeeded', now()),
                (gen_random_uuid(), '{project}', '{user}', 'indicators', 'x', 500, 'succeeded', now());
            INSERT INTO tamiza.dashboard_widgets (project_id, chart_type, title, created_by, updated_by)
            VALUES ('{project}', 'stat', 'Total', '{user}', '{user}');
            CREATE SCHEMA {indicatorsSchema};
            CREATE TABLE {indicatorsSchema}.indicators (indicator text, value numeric);
            CREATE SCHEMA {cleanSchema};
            CREATE TABLE {cleanSchema}.main (id integer);
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        Assert.Contains("0008_split_indicator_scripts.sql", Scripts);
        // Everything of script 3 is gone: its versions, tests and steps, the widgets that read it and its published schema.
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.script_versions WHERE kind NOT IN ('clean', 'output')"));
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.script_tests WHERE kind NOT IN ('clean', 'output')"));
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.run_steps WHERE step NOT IN ('flatten', 'clean', 'output')"));
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.dashboard_widgets"));
        Assert.False(await ScalarAsync<bool>(connectionString, $"SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = '{indicatorsSchema}')"));
        // The other scripts, their tests and steps, the run and the clean schema stay.
        Assert.True(await ScalarAsync<bool>(connectionString, $"SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = '{cleanSchema}')"));
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.script_versions WHERE kind = 'clean'"));
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.script_tests WHERE kind = 'clean'"));
        Assert.Equal("clean|", await ScalarAsync<string>(connectionString, "SELECT step || '|' || name FROM tamiza.run_steps"));
        Assert.Equal("[]", await ScalarAsync<string>(connectionString, "SELECT indicator_versions::text FROM tamiza.runs"));

        // Indicator versions are numbered per indicator and need a name; the clean script still has one sequence.
        await ExecuteAsync(connectionString, $"""
            INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, author_id, created_at) VALUES
                (gen_random_uuid(), '{project}', 'indicator', 'coverage', 1, 'x', 'Coverage', 'percent', '{user}', now()),
                (gen_random_uuid(), '{project}', 'indicator', 'attendance', 1, 'x', 'Attendance', 'count', '{user}', now());
            """);
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"""
            INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, author_id, created_at)
            VALUES (gen_random_uuid(), '{project}', 'indicator', 'coverage', 1, 'x', 'Again', 'count', '{user}', now())
            """));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"""
            INSERT INTO tamiza.script_versions (id, project_id, kind, number, code, author_id, created_at)
            VALUES (gen_random_uuid(), '{project}', 'clean', 1, 'x', '{user}', now())
            """));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"""
            INSERT INTO tamiza.script_versions (id, project_id, kind, number, code, author_id, created_at)
            VALUES (gen_random_uuid(), '{project}', 'indicator', 1, 'x', '{user}', now())
            """));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"""
            INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, author_id, created_at)
            VALUES (gen_random_uuid(), '{project}', 'indicator', 'other', 1, 'x', 'Other', 'ratio', '{user}', now())
            """));
        // An indicator step needs its name, and the fixed steps have none.
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            "INSERT INTO tamiza.run_steps (run_id, step, status) VALUES ('00000000-0000-0000-0000-0000000000d1', 'indicator', 'succeeded')"));
        await ExecuteAsync(connectionString, """
            INSERT INTO tamiza.run_steps (run_id, step, name, status) VALUES
                ('00000000-0000-0000-0000-0000000000d1', 'indicator', 'coverage', 'succeeded'),
                ('00000000-0000-0000-0000-0000000000d1', 'indicator', 'attendance', 'failed')
            """);
        Assert.Equal(3L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.run_steps"));
    }

    [Fact]
    public async Task Script_workbench_script_keeps_existing_tests_and_adds_an_optional_stop_line()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(
            connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql", "0003_add_pipeline.sql", "0004_add_pipeline_authoring.sql",
            "0005_add_choice_labels.sql", "0006_add_performance_dashboard.sql", "0007_add_indicators_dashboard.sql", "0008_split_indicator_scripts.sql");
        const string project = "00000000-0000-0000-0000-0000000000aa";
        const string user = "00000000-0000-0000-0000-000000000001";
        await ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('{{user}}', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES ('{{project}}', 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(), '{{user}}', now(), now());
            INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at, steps)
            VALUES ('00000000-0000-0000-0000-0000000000e1', '{{project}}', '{{user}}', 'clean', 'def clean(raw, ctx): return raw', 500, 'succeeded', now(),
                    '[{"step":"flatten","status":"succeeded","tables":[],"log":""}]');
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        Assert.Contains("0009_add_script_workbench.sql", Scripts);
        // An existing test keeps its code and steps, and has no stop line: it was a whole test.
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.script_tests WHERE stop_line IS NULL AND sample_size = 500"));
        Assert.Equal("flatten", await ScalarAsync<string>(connectionString, "SELECT steps -> 0 ->> 'step' FROM tamiza.script_tests"));

        await ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at, stop_line)
            VALUES (gen_random_uuid(), '{{project}}', '{{user}}', 'clean', 'x', 500, 'succeeded', now(), 4)
            """);
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at, stop_line)
            VALUES (gen_random_uuid(), '{{project}}', '{{user}}', 'clean', 'x', 500, 'succeeded', now(), 0)
            """));
    }

    [Fact]
    public async Task Local_dev_kit_script_keeps_existing_data_and_adds_sample_downloads_that_go_with_their_project()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(
            connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql", "0003_add_pipeline.sql", "0004_add_pipeline_authoring.sql",
            "0005_add_choice_labels.sql", "0006_add_performance_dashboard.sql", "0007_add_indicators_dashboard.sql", "0008_split_indicator_scripts.sql",
            "0009_add_script_workbench.sql");
        const string project = "00000000-0000-0000-0000-0000000000aa";
        const string user = "00000000-0000-0000-0000-000000000001";
        await ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('{{user}}', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES ('{{project}}', 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(), '{{user}}', now(), now());
            INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at)
            VALUES ('00000000-0000-0000-0000-0000000000e1', '{{project}}', '{{user}}', 'clean', 'def clean(raw, ctx): return raw', 500, 'succeeded', now());
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        Assert.Contains("0010_add_local_dev_kit.sql", Scripts);
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.script_tests"));
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.sample_downloads"));

        // One queued download per user and project; a ready one does not count.
        await ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.sample_downloads (id, project_id, requested_by, sample_size, status, queued_at)
            VALUES ('00000000-0000-0000-0000-0000000000f1', '{{project}}', '{{user}}', 500, 'queued', now()),
                   ('00000000-0000-0000-0000-0000000000f2', '{{project}}', '{{user}}', 500, 'succeeded', now());
            INSERT INTO tamiza.sample_download_parts (download_id, part, data)
            VALUES ('00000000-0000-0000-0000-0000000000f2', 0, '\x504b0304'), ('00000000-0000-0000-0000-0000000000f2', 1, '\x00');
            """);
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.sample_downloads (id, project_id, requested_by, sample_size, status, queued_at)
            VALUES (gen_random_uuid(), '{{project}}', '{{user}}', 100, 'queued', now())
            """));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.sample_downloads (id, project_id, requested_by, sample_size, status, queued_at)
            VALUES (gen_random_uuid(), '{{project}}', '{{user}}', 5001, 'succeeded', now())
            """));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.sample_downloads (id, project_id, requested_by, sample_size, status, queued_at)
            VALUES (gen_random_uuid(), '{{project}}', '{{user}}', 100, 'ready', now())
            """));

        // Deleting a download deletes its parts; deleting a project deletes its downloads and their parts.
        await ExecuteAsync(connectionString, "DELETE FROM tamiza.sample_downloads WHERE id = '00000000-0000-0000-0000-0000000000f1'");
        Assert.Equal(2L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.sample_download_parts"));
        await ExecuteAsync(connectionString, $"DELETE FROM tamiza.projects WHERE id = '{project}'");
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.sample_downloads"));
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.sample_download_parts"));
    }

    [Fact]
    public async Task Script_assistant_script_keeps_existing_data_and_adds_records_that_go_with_their_project()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        ApplyScripts(
            connectionString, SchemaMigrator.InitialScript, "0002_add_ingestion.sql", "0003_add_pipeline.sql", "0004_add_pipeline_authoring.sql",
            "0005_add_choice_labels.sql", "0006_add_performance_dashboard.sql", "0007_add_indicators_dashboard.sql", "0008_split_indicator_scripts.sql",
            "0009_add_script_workbench.sql", "0010_add_local_dev_kit.sql");
        const string project = "00000000-0000-0000-0000-0000000000aa";
        const string user = "00000000-0000-0000-0000-000000000001";
        await ExecuteAsync(connectionString, $$"""
            INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES ('{{user}}', 'sub-1', 'Ada', now(), now());
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES ('{{project}}', 'Existing', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form', '[]', now(), '{{user}}', now(), now());
            INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at)
            VALUES ('00000000-0000-0000-0000-0000000000e1', '{{project}}', '{{user}}', 'clean', 'def clean(raw, ctx): return raw', 500, 'succeeded', now());
            INSERT INTO tamiza.sample_downloads (id, project_id, requested_by, sample_size, status, queued_at)
            VALUES ('00000000-0000-0000-0000-0000000000f1', '{{project}}', '{{user}}', 500, 'queued', now());
            """);

        await SchemaMigrator.MigrateAsync(connectionString, NullLogger.Instance);

        Assert.Equal(Scripts, await JournalAsync(connectionString));
        Assert.Contains("0011_add_script_assistant.sql", Scripts);
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.script_tests"));
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.sample_downloads"));
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.projects"));
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.assistant_calls"));

        // A record defaults to not estimated and starts now; a user has one running request at a time, and a finished one does not count.
        const string insert = """
            INSERT INTO tamiza.assistant_calls (id, project_id, user_id, kind, action, model, prompt_kit, context, status)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000aa', '00000000-0000-0000-0000-000000000001',
                    'indicator', 'fix', 'stub', 1, '{}', 'STATUS')
            """;
        await ExecuteAsync(connectionString, insert.Replace("STATUS", "running"));
        await ExecuteAsync(connectionString, insert.Replace("STATUS", "succeeded"));
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.assistant_calls WHERE tokens_estimated"));
        Assert.Equal(2L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.assistant_calls WHERE started_at <= now()"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, insert.Replace("STATUS", "running")));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, insert.Replace("STATUS", "queued")));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, insert.Replace("STATUS", "succeeded").Replace("'fix'", "'review'")));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, insert.Replace("STATUS", "succeeded").Replace("'indicator'", "'indicators'")));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            "UPDATE tamiza.assistant_calls SET outcome = 'everything' WHERE status = 'succeeded'"));

        // Deleting a project deletes its records.
        await ExecuteAsync(connectionString, $"DELETE FROM tamiza.projects WHERE id = '{project}'");
        Assert.Equal(0L, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM tamiza.assistant_calls"));
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
