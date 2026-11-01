using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using Tamiza.Api.Data;
using Tamiza.Api.Indicators;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Pipeline;

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
    public async Task Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        const string warning = "To change the chart type of a widget that has a color or a scale, also send a config without the display fields the new type does not take: a new chartType alone keeps the old config";
        var update = tools["update_widget"].Description;
        Assert.Contains(warning, update);
        Assert.Contains("config.display.color or config.display.ramp", update);
        // update_widget says what a new config replaces, which is why the chart type alone is not enough.
        Assert.Contains("What you leave out keeps its value; a new config replaces the old one whole", update);

        // A new widget has no old config to keep, so the warning would only mislead there; what both need is still said.
        var create = tools["create_widget"].Description;
        Assert.DoesNotContain("To change the chart type", create);
        Assert.DoesNotContain("a new chartType alone keeps the old config", create);
        foreach (var description in new[] { create, update })
        {
            Assert.Contains("config.display = { color, ramp } sets how the widget is drawn", description);
            Assert.Contains("ramp is a sequential scale", description);
        }
    }

    [Fact]
    public async Task The_dashboard_tools_say_that_palette_scale_and_column_names_are_matched_exactly_in_lower_case_and_values_as_exact_text()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        var update = tools["update_dashboard"];
        Assert.Contains("the palette is lower case (Vivid is refused)", update.Description);
        Assert.Contains("column is a column's name in lower case (a capital letter is refused)", update.Description);
        Assert.Contains("value is matched as exact text, case and spaces included, so female does not color Female", update.Description);
        Assert.Contains("in lower case exactly as written (Vivid is refused)", update.JsonSchema.GetProperty("properties").GetProperty("palette").GetProperty("description").GetString());
        foreach (var name in new[] { "create_widget", "update_widget" })
        {
            Assert.Contains("The scale is matched exactly, in lower case: Green is refused.", tools[name].Description);
        }
    }

    [Fact]
    public async Task The_version_list_names_every_kind_of_change_a_version_can_list()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        var listed = Regex.Match(tools["list_dashboard_versions"].Description, @"kind one of ([^)]*)\)");

        Assert.True(listed.Success, "list_dashboard_versions does not list the kinds of change");
        var kinds = listed.Groups[1].Value.Replace(" or ", ", ").Split(", ");
        Assert.Equal(DashboardChangeKinds.All.Order().ToArray(), kinds.Order().ToArray());
    }

    [Fact]
    public async Task The_schemas_of_the_color_fields_carry_their_allowed_values_and_a_description()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).ToDictionary(t => t.Name);

        // A client that reads only tools/list sees the palettes, the scales and the numbers 1 to 8, and what each field is.
        var update = tools["update_dashboard"].JsonSchema.GetProperty("properties");
        AssertNamed(update.GetProperty("palette"), Palettes.All);
        var kept = update.GetProperty("valueColors").GetProperty("items").GetProperty("properties");
        AssertColor(kept.GetProperty("color"));
        foreach (var field in new[] { "column", "value" })
        {
            AssertDescribed(kept.GetProperty(field));
        }

        // The column's description says all that the validation refuses, not only the letters: a client reads nothing else of the rule.
        var column = kept.GetProperty("column").GetProperty("description").GetString()!;
        Assert.Contains("lowercase letters, digits and underscores", column);
        Assert.Contains("not starting with a digit", column);
        Assert.Contains($"at most {ColumnNames.MaxLength} characters", column);

        foreach (var name in new[] { "create_widget", "update_widget" })
        {
            var displayField = tools[name].JsonSchema.GetProperty("properties").GetProperty("config").GetProperty("properties").GetProperty("display");
            Assert.Contains("How the widget is drawn", displayField.GetProperty("description").GetString());
            var display = displayField.GetProperty("properties");
            AssertColor(display.GetProperty("color"));
            AssertNamed(display.GetProperty("ramp"), Palettes.Scales);
        }

        // The names are in the description and not in an enum: the field is nullable, "not set" is a null the server accepts, and an enum
        // without null would make a client that validates against the schema (or sends every field) refuse it or force a value.
        static void AssertNamed(JsonElement field, IEnumerable<string> names)
        {
            var description = field.GetProperty("description").GetString()!;
            foreach (var name in names)
            {
                Assert.Contains(name, description);
            }

            Assert.False(field.TryGetProperty("enum", out _), "an enum without null would refuse the null that means not set");
        }

        static void AssertColor(JsonElement color)
        {
            Assert.Equal(1, color.GetProperty("minimum").GetInt32());
            Assert.Equal(Palettes.ColorCount, color.GetProperty("maximum").GetInt32());
            AssertDescribed(color);
        }

        static void AssertDescribed(JsonElement field) => Assert.False(string.IsNullOrWhiteSpace(field.GetProperty("description").GetString()));
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
