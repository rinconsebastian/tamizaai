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
}
