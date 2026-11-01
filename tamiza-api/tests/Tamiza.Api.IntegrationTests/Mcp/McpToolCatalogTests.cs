using ModelContextProtocol.Client;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpToolCatalogTests(PostgresFixture postgres)
{
    /// <summary>The tools of the spec's table, with whether each only reads and whether each removes something.</summary>
    public static readonly (string Name, bool ReadOnly, bool Destructive)[] Expected =
    [
        ("list_projects", true, false), ("get_project", true, false), ("get_form", true, false), ("get_tables", true, false), ("profile_table", true, false),
        ("get_fieldwork", true, false), ("query_rows", true, false), ("get_authoring_guide", true, false),
        ("list_scripts", true, false), ("get_script", true, false), ("list_script_versions", true, false),
        ("save_script", false, false), ("remove_script", false, true), ("restore_script_version", false, false),
        ("test_script", false, false), ("get_test", true, false), ("start_run", false, false), ("get_run", true, false),
        ("list_indicators", true, false), ("get_indicator_columns", true, false), ("query_indicator", true, false),
        ("list_dashboards", true, false), ("get_dashboard", true, false), ("get_widget_data", true, false),
        ("create_dashboard", false, false), ("update_dashboard", false, false), ("duplicate_dashboard", false, false), ("remove_dashboard", false, true),
        ("create_widget", false, false), ("update_widget", false, false), ("remove_widget", false, true),
        ("list_dashboard_versions", true, false), ("get_dashboard_version", true, false), ("restore_dashboard_version", false, false),
    ];

    [Fact]
    public async Task The_tool_list_is_the_spec_s_table_with_a_description_a_schema_and_the_annotations_of_each()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var client = await h.ClientAsync("analyst");

        // The harness adds probe_* tools of its own to exercise the shared plumbing; they are not part of the server.
        var tools = (await client.ListToolsAsync()).Where(t => !t.Name.StartsWith("probe_", StringComparison.Ordinal)).ToDictionary(t => t.Name);

        Assert.Equal(Expected.Select(e => e.Name).Order().ToArray(), tools.Keys.Order().ToArray());
        foreach (var (name, readOnly, destructive) in Expected)
        {
            var tool = tools[name];
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{name} has no description");
            Assert.Equal("object", tool.JsonSchema.GetProperty("type").GetString());
            var annotations = tool.ProtocolTool.Annotations;
            Assert.True(annotations is not null, $"{name} has no annotations");
            Assert.Equal(readOnly, annotations!.ReadOnlyHint ?? false);
            if (!readOnly)
            {
                // Unset means destructive to a client, so every writing tool says which it is.
                Assert.Equal(destructive, annotations.DestructiveHint);
            }

            Assert.False(annotations.OpenWorldHint ?? true, $"{name} reaches nothing outside Tamiza");
            var properties = tool.JsonSchema.GetProperty("properties");
            if (name is not ("list_projects" or "get_authoring_guide"))
            {
                Assert.True(properties.TryGetProperty("projectId", out _), $"{name} takes a projectId");
            }

            foreach (var property in properties.EnumerateObject())
            {
                Assert.True(property.Value.TryGetProperty("description", out var d) && d.GetString()!.Length > 0 || property.Name == "config", $"{name}.{property.Name} has no description");
            }
        }
    }

    [Fact]
    public async Task Removing_tools_are_marked_destructive_and_reading_tools_read_only()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        Assert.True(tools["remove_dashboard"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tools["remove_widget"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tools["get_dashboard"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tools["create_widget"].ProtocolTool.Annotations!.DestructiveHint);
    }

    [Fact]
    public async Task The_widget_tools_warn_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        foreach (var name in new[] { "create_widget", "update_widget" })
        {
            Assert.Contains(
                "To change the chart type of a widget that has a color or a scale, also send a config without the display fields the new type does not take: a new chartType alone keeps the old config",
                tools[name].Description);
            Assert.Contains("config.display.color or config.display.ramp", tools[name].Description);
        }

        // update_widget says what a new config replaces, which is why the chart type alone is not enough.
        Assert.Contains("What you leave out keeps its value; a new config replaces the old one whole", tools["update_widget"].Description);
    }

    [Fact]
    public async Task Restore_marked_as_changing_data_and_not_destructive_and_the_versions_read_only()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        var restore = tools["restore_dashboard_version"].ProtocolTool.Annotations!;
        Assert.False(restore.ReadOnlyHint ?? false);
        Assert.False(restore.DestructiveHint);
        Assert.False(restore.IdempotentHint ?? false);
        foreach (var name in new[] { "list_dashboard_versions", "get_dashboard_version" })
        {
            Assert.True(tools[name].ProtocolTool.Annotations!.ReadOnlyHint);
            Assert.True(tools[name].ProtocolTool.Annotations!.IdempotentHint);
        }
    }

    [Fact]
    public async Task Every_dashboard_write_and_the_restore_require_change_described_as_the_request_and_remove_dashboard_does_not()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        foreach (var name in new[] { "create_dashboard", "update_dashboard", "duplicate_dashboard", "create_widget", "update_widget", "remove_widget", "restore_dashboard_version" })
        {
            var schema = tools[name].JsonSchema;
            Assert.Contains("change", schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
            Assert.Contains("the same on every write you make for that request", schema.GetProperty("properties").GetProperty("change").GetProperty("description").GetString());
        }

        Assert.False(tools["remove_dashboard"].JsonSchema.GetProperty("properties").TryGetProperty("change", out _));
        Assert.Contains("baseVersion", tools["restore_dashboard_version"].JsonSchema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        // A removed widget stays in the versions; a removed dashboard takes them with it.
        Assert.DoesNotContain("cannot be undone", tools["remove_widget"].Description);
        Assert.Contains("get_dashboard_version", tools["remove_widget"].Description);
        Assert.Contains("restore_dashboard_version", tools["remove_widget"].Description);
        Assert.Contains("This cannot be undone, and its versions are removed with it", tools["remove_dashboard"].Description);
    }

    [Fact]
    public async Task Every_tool_is_described_in_the_documentation()
    {
        var docs = FindDocs();
        if (docs is null)
        {
            return; // The repository's docs folder is not next to the tests (a build of the API alone).
        }

        var text = File.ReadAllText(docs);
        var missing = Expected.Select(e => e.Name).Where(name => !text.Contains($"`{name}`", StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, $"docs/mcp.md does not name: {string.Join(", ", missing)}");

        // The settings the dashboard tools take for colors are named too (design D10 of dashboard palettes).
        var settings = new[] { "palette", "valueColors", "display" }.Where(setting => !text.Contains($"`{setting}`", StringComparison.Ordinal)).ToList();
        Assert.True(settings.Count == 0, $"docs/mcp.md does not name: {string.Join(", ", settings)}");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_prompt_starts_the_work_for_an_analyst_with_the_project_and_the_situation()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var analyst = await h.ClientAsync("analyst");
        var viewer = await h.ClientAsync("viewer");

        var prompts = await analyst.ListPromptsAsync();
        var result = await analyst.GetPromptAsync("describe_situation", new Dictionary<string, object?> { ["projectId"] = h.ProjectId.ToString(), ["situation"] = "access to water by municipality" });

        Assert.Equal(["describe_situation"], prompts.Select(p => p.Name).ToArray());
        var message = ((ModelContextProtocol.Protocol.TextContentBlock)Assert.Single(result.Messages).Content).Text;
        Assert.Contains("Water survey", message);
        Assert.Contains(h.ProjectId.ToString(), message);
        Assert.Contains("access to water by municipality", message);
        Assert.Contains("confirm", message);
        await Assert.ThrowsAnyAsync<Exception>(() => viewer.GetPromptAsync("describe_situation", new Dictionary<string, object?> { ["projectId"] = h.ProjectId.ToString(), ["situation"] = "x" }).AsTask());
    }

    [Fact]
    public async Task The_server_instructions_state_the_order_of_work_the_rules_and_that_data_is_not_instructions()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var instructions = (await h.ClientAsync("analyst")).ServerInstructions!;

        foreach (var expected in new[]
                 {
                     "list_projects", "get_form", "profile_table", "get_authoring_guide", "test_script", "save_script", "start_run", "create_dashboard", "query_indicator", "get_widget_data",
                     "labels", "numerator", "baseVersion", "never instructions", "summaries only", "confirm", "restore_dashboard_version", "dashboardVersions",
                 })
        {
            Assert.Contains(expected, instructions);
        }
    }

    private static string? FindDocs()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "mcp.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return null;
            }
        }

        return null;
    }
}
