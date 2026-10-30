using Tamiza.DbUp;

namespace Tamiza.Api.UnitTests.Database;

/// <summary>DbUp applies scripts by name, once; these rules keep that order predictable.</summary>
public sealed class ScriptConventionTests
{
    private static readonly string[] Resources = typeof(SchemaMigrator).Assembly.GetManifestResourceNames();

    [Fact]
    public void Every_embedded_resource_is_a_numbered_sql_script()
    {
        Assert.NotEmpty(Resources);
        Assert.All(Resources, name => Assert.Matches(@"^\d{4}_[a-z0-9_]+\.sql$", name));
    }

    [Fact]
    public void Sequence_numbers_are_unique()
    {
        var duplicates = Resources
            .GroupBy(name => name.Split('_')[0])
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group));

        Assert.Empty(duplicates);
    }

    [Fact]
    public void The_indicators_dashboard_script_names_every_constraint_and_index_explicitly()
    {
        var resource = Resources.Single(name => name.StartsWith("0007_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0007_add_indicators_dashboard.sql", resource);
        Assert.Contains("CONSTRAINT pk_dashboard_widgets PRIMARY KEY", sql);
        Assert.Contains("CONSTRAINT ck_dashboard_widgets_chart_type CHECK", sql);
        Assert.Contains("CONSTRAINT fk_dashboard_widgets_projects_project_id FOREIGN KEY", sql);
        Assert.Contains("CONSTRAINT fk_dashboard_widgets_users_created_by FOREIGN KEY", sql);
        Assert.Contains("CONSTRAINT fk_dashboard_widgets_users_updated_by FOREIGN KEY", sql);
        // Every key, check and foreign key is introduced by a CONSTRAINT name; every index has an ix_ name.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, @"PRIMARY KEY"));
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"(?:PRIMARY KEY|FOREIGN KEY|CHECK)").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)").Count);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value),
            name => Assert.StartsWith("ix_dashboard_widgets_", name));
    }

    [Fact]
    public void The_indicator_scripts_script_names_every_constraint_and_index_it_adds()
    {
        var resource = Resources.Single(name => name.StartsWith("0008_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0008_split_indicator_scripts.sql", resource);
        Assert.Contains("ADD CONSTRAINT pk_run_steps PRIMARY KEY (run_id, step, name)", sql);
        foreach (var check in new[] { "ck_run_steps_step", "ck_run_steps_name", "ck_script_tests_kind", "ck_script_tests_name", "ck_script_versions_kind", "ck_script_versions_name", "ck_script_versions_format" })
        {
            Assert.Contains($"ADD CONSTRAINT {check} CHECK", sql);
        }

        // Every key, check and foreign key the script adds is introduced by a CONSTRAINT name; every index it creates has an ix_ name.
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"ADD CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value),
            name => Assert.StartsWith("ix_script_versions_", name));
        Assert.Contains("CREATE UNIQUE INDEX ix_script_versions_project_id_kind_name_number", sql);
    }

    [Fact]
    public void The_script_workbench_script_names_its_check_constraint()
    {
        var resource = Resources.Single(name => name.StartsWith("0009_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0009_add_script_workbench.sql", resource);
        Assert.Contains("ADD CONSTRAINT ck_script_tests_stop_line CHECK (stop_line > 0)", sql);
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"ADD CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count);
    }

    [Fact]
    public void The_local_dev_kit_script_names_every_constraint_and_index()
    {
        var resource = Resources.Single(name => name.StartsWith("0010_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0010_add_local_dev_kit.sql", resource);
        foreach (var constraint in new[]
                 {
                     "pk_sample_downloads PRIMARY KEY", "ck_sample_downloads_status CHECK", "ck_sample_downloads_sample_size CHECK",
                     "fk_sample_downloads_projects_project_id FOREIGN KEY", "fk_sample_downloads_users_requested_by FOREIGN KEY",
                     "fk_sample_downloads_script_versions_clean_version_id FOREIGN KEY", "pk_sample_download_parts PRIMARY KEY",
                     "fk_sample_download_parts_sample_downloads_download_id FOREIGN KEY",
                 })
        {
            Assert.Contains($"CONSTRAINT {constraint}", sql);
        }

        // Every key, check and foreign key is introduced by a CONSTRAINT name; every index has an ix_ name.
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value),
            name => Assert.StartsWith("ix_sample_downloads_", name));
    }

    [Fact]
    public void The_script_assistant_script_names_every_constraint_and_index()
    {
        var resource = Resources.Single(name => name.StartsWith("0011_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0011_add_script_assistant.sql", resource);
        foreach (var constraint in new[]
                 {
                     "pk_assistant_calls PRIMARY KEY", "ck_assistant_calls_kind CHECK", "ck_assistant_calls_action CHECK",
                     "ck_assistant_calls_status CHECK", "ck_assistant_calls_outcome CHECK",
                     "fk_assistant_calls_projects_project_id FOREIGN KEY", "fk_assistant_calls_users_user_id FOREIGN KEY",
                 })
        {
            Assert.Contains($"CONSTRAINT {constraint}", sql);
        }

        // Every key, check and foreign key is introduced by a CONSTRAINT name; every index has an ix_ name.
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value),
            name => Assert.StartsWith("ix_assistant_calls_", name));
        Assert.Contains("CREATE UNIQUE INDEX ix_assistant_calls_user_id_running ON tamiza.assistant_calls (user_id) WHERE status = 'running'", sql);
    }

    [Fact]
    public void The_multiple_dashboards_script_names_every_constraint_and_index()
    {
        var resource = Resources.Single(name => name.StartsWith("0012_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0012_add_multiple_dashboards.sql", resource);
        foreach (var constraint in new[]
                 {
                     "pk_dashboards PRIMARY KEY", "ck_dashboards_position CHECK", "fk_dashboards_projects_project_id FOREIGN KEY",
                     "fk_dashboards_users_created_by FOREIGN KEY", "fk_dashboards_users_updated_by FOREIGN KEY",
                     "fk_dashboard_widgets_dashboards_dashboard_id FOREIGN KEY",
                 })
        {
            Assert.Contains($"CONSTRAINT {constraint}", sql);
        }

        // Every key, check and foreign key is introduced by a CONSTRAINT name; every index has an ix_ name.
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value),
            name => Assert.True(name.StartsWith("ix_dashboards_", StringComparison.Ordinal) || name.StartsWith("ix_dashboard_widgets_", StringComparison.Ordinal), name));
        Assert.Contains("CREATE UNIQUE INDEX ix_dashboards_project_id_normalized_name ON tamiza.dashboards (project_id, normalized_name)", sql);
        Assert.Contains("CREATE INDEX ix_dashboard_widgets_dashboard_id ON tamiza.dashboard_widgets (dashboard_id)", sql);
    }

    [Fact]
    public void The_account_emails_script_names_every_constraint_and_index()
    {
        var resource = Resources.Single(name => name.StartsWith("0013_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0013_add_account_emails.sql", resource);
        foreach (var constraint in new[] { "pk_invited_accounts PRIMARY KEY", "fk_invited_accounts_users_created_by FOREIGN KEY" })
        {
            Assert.Contains($"CONSTRAINT {constraint}", sql);
        }

        // Every key, check and foreign key is introduced by a CONSTRAINT name; every index has an ix_ name.
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value),
            name => Assert.StartsWith("ix_invited_accounts_", name));
        Assert.Contains("CREATE INDEX ix_invited_accounts_normalized_email ON tamiza.invited_accounts (normalized_email)", sql);
    }

    [Fact]
    public void The_dashboard_reports_script_names_every_constraint_and_index()
    {
        var resource = Resources.Single(name => name.StartsWith("0015_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0015_add_dashboard_reports.sql", resource);
        foreach (var constraint in new[]
                 {
                     "pk_dashboard_reports PRIMARY KEY", "ck_dashboard_reports_recipients CHECK", "ck_dashboard_reports_days CHECK",
                     "fk_dashboard_reports_dashboards_dashboard_id FOREIGN KEY", "fk_dashboard_reports_users_updated_by FOREIGN KEY",
                 })
        {
            Assert.Contains($"CONSTRAINT {constraint}", sql);
        }

        // Every key, check and foreign key is introduced by a CONSTRAINT name; every index has an ix_ name.
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value),
            name => Assert.StartsWith("ix_dashboard_reports_", name));
        Assert.Contains("CREATE INDEX ix_dashboard_reports_updated_by ON tamiza.dashboard_reports (updated_by)", sql);
        Assert.Contains("REFERENCES tamiza.dashboards (id) ON DELETE CASCADE", sql);
    }

    [Fact]
    public void The_dashboard_filters_script_adds_one_column()
    {
        var resource = Resources.Single(name => name.StartsWith("0014_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0014_add_dashboard_filters.sql", resource);
        Assert.Contains("ALTER TABLE tamiza.dashboards ADD COLUMN filters jsonb NOT NULL DEFAULT '[]'::jsonb;", sql);

        // No key, check, foreign key or index, so nothing can be left unnamed.
        Assert.Empty(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK|INDEX)\b"));
    }

    [Fact]
    public void The_mcp_server_script_names_every_constraint_and_index()
    {
        var resource = Resources.Single(name => name.StartsWith("0016_", StringComparison.Ordinal));
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream(resource)!);
        var sql = reader.ReadToEnd();

        Assert.Equal("0016_add_mcp_server.sql", resource);
        foreach (var constraint in new[]
                 {
                     "ck_projects_mcp_access CHECK", "ck_script_versions_origin CHECK", "pk_mcp_calls PRIMARY KEY",
                     "fk_mcp_calls_users_user_id FOREIGN KEY", "fk_mcp_calls_projects_project_id FOREIGN KEY",
                     "ck_mcp_calls_access CHECK", "ck_mcp_calls_status CHECK",
                 })
        {
            Assert.Contains($"CONSTRAINT {constraint}", sql);
        }

        // Every key, check and foreign key is introduced by a CONSTRAINT name; every index has an ix_ name.
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\b(?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count,
            System.Text.RegularExpressions.Regex.Matches(sql, @"CONSTRAINT (?:pk|fk|ck)_\w+ (?:PRIMARY KEY|FOREIGN KEY|CHECK)\b").Count);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value),
            name => Assert.StartsWith("ix_mcp_calls_", name));
        Assert.Contains("CREATE INDEX ix_mcp_calls_project_id_started_at ON tamiza.mcp_calls (project_id, started_at DESC)", sql);
        Assert.Contains("REFERENCES tamiza.projects (id) ON DELETE CASCADE", sql);
    }
}
