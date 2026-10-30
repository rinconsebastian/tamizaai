using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpRoleMatrixTests(PostgresFixture postgres)
{
    private enum Needs { Viewer, Analyst, AnyProject }

    private static readonly Guid Nobody = Guid.Parse("00000000-0000-0000-0000-00000000dead");

    /// <summary>Every tool with the role it needs and arguments that name nothing real, so a call that passes the role changes nothing.</summary>
    private static List<(string Tool, Needs Needs, object Args)> Matrix() =>
    [
        ("list_projects", Needs.AnyProject, new { }),
        ("get_project", Needs.Viewer, new { }),
        ("get_form", Needs.Analyst, new { }),
        ("get_tables", Needs.Viewer, new { source = "clean" }),
        ("profile_table", Needs.Viewer, new { source = "clean", table = "nothing" }),
        ("get_fieldwork", Needs.Viewer, new { }),
        ("query_rows", Needs.Viewer, new { source = "clean", table = "nothing" }),
        ("get_authoring_guide", Needs.AnyProject, new { kind = "clean" }),
        ("list_scripts", Needs.Analyst, new { }),
        ("get_script", Needs.Analyst, new { kind = "clean" }),
        ("list_script_versions", Needs.Analyst, new { kind = "clean" }),
        ("save_script", Needs.Analyst, new { kind = "clean", code = "", baseVersion = 99 }),
        ("remove_script", Needs.Analyst, new { kind = "clean", baseVersion = 99 }),
        ("restore_script_version", Needs.Analyst, new { kind = "clean", version = 99, baseVersion = 99 }),
        ("test_script", Needs.Analyst, new { kind = "dashboard", code = "x", waitSeconds = 0 }),
        ("get_test", Needs.Analyst, new { testId = Nobody, waitSeconds = 0 }),
        ("start_run", Needs.Analyst, new { waitSeconds = 0 }),
        ("get_run", Needs.Analyst, new { runId = Nobody, waitSeconds = 0 }),
        ("list_indicators", Needs.Viewer, new { }),
        ("get_indicator_columns", Needs.Viewer, new { name = "nothing" }),
        ("query_indicator", Needs.Viewer, new { config = new { indicator = "nothing" } }),
        ("list_dashboards", Needs.Viewer, new { }),
        ("get_dashboard", Needs.Viewer, new { dashboardId = Nobody }),
        ("get_widget_data", Needs.Viewer, new { dashboardId = Nobody }),
        ("create_dashboard", Needs.Analyst, new { name = "" }),
        ("update_dashboard", Needs.Analyst, new { dashboardId = Nobody, name = "x" }),
        ("duplicate_dashboard", Needs.Analyst, new { dashboardId = Nobody, name = "x" }),
        ("remove_dashboard", Needs.Analyst, new { dashboardId = Nobody, expectedName = "x" }),
        ("create_widget", Needs.Analyst, new { dashboardId = Nobody, title = "x", chartType = "stat", config = new { indicator = "nothing" } }),
        ("update_widget", Needs.Analyst, new { dashboardId = Nobody, widgetId = Nobody, title = "x" }),
        ("remove_widget", Needs.Analyst, new { dashboardId = Nobody, widgetId = Nobody, expectedTitle = "x" }),
    ];

    [Fact]
    public async Task Every_tool_is_in_the_matrix()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        var tools = (await (await h.ClientAsync("analyst")).ListToolsAsync()).Select(t => t.Name).Where(name => !name.StartsWith("probe_", StringComparison.Ordinal)).Order().ToArray();

        Assert.Equal(tools, Matrix().Select(m => m.Tool).Order().ToArray());
        Assert.Equal(McpToolCatalogTests.Expected.Select(e => e.Name).Order().ToArray(), tools);
    }

    [Fact]
    public async Task Each_tool_answers_each_person_by_their_role_and_a_viewers_refused_calls_change_nothing()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres, McpAccess.Rows);
        var mismatches = new List<string>();

        foreach (var (tool, needs, args) in Matrix())
        {
            foreach (var person in McpProjectHarness.People)
            {
                var withProject = needs != Needs.AnyProject;
                var code = (await h.CallAsync(person, tool, args, withProject)).Code();
                var allowed = needs switch
                {
                    Needs.AnyProject => person is "viewer" or "analyst" or "admin" or "superadmin" && !(tool == "get_authoring_guide" && person == "viewer"),
                    Needs.Viewer => person != "outsider",
                    _ => person is "analyst" or "admin" or "superadmin",
                };
                string expected = needs == Needs.AnyProject
                    ? (allowed ? "allowed" : "mcp.forbidden")
                    : allowed ? "allowed" : person == "outsider" ? "mcp.projectNotFound" : "mcp.forbidden";
                var actual = code is "mcp.forbidden" or "mcp.projectNotFound" or "mcp.projectOff" ? code : "allowed";
                if (tool == "list_projects")
                {
                    // Any signed-in person may ask; a person with no project gets an empty list.
                    expected = "allowed";
                    actual = code is null ? "allowed" : code;
                }

                if (actual != expected)
                {
                    mismatches.Add($"{person} {tool}: expected {expected}, got {actual}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    [Fact]
    public async Task A_viewer_who_calls_every_changing_tool_changes_nothing()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres, McpAccess.Rows);
        string[] changing = ["save_script", "remove_script", "restore_script_version", "test_script", "start_run", "create_dashboard", "update_dashboard", "duplicate_dashboard", "remove_dashboard", "create_widget", "update_widget", "remove_widget"];

        foreach (var (tool, _, args) in Matrix().Where(m => changing.Contains(m.Tool)))
        {
            Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", tool, args)).Code());
        }

        foreach (var table in new[] { "script_versions", "script_tests", "runs", "dashboard_widgets" })
        {
            Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, $"SELECT count(*) FROM tamiza.{table}"));
        }

        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboards"));
    }

    [Fact]
    public async Task A_project_that_is_off_refuses_every_tool_that_names_it_and_still_answers_the_ones_that_do_not()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres, McpAccess.Off);
        var refused = new List<string>();

        foreach (var (tool, needs, args) in Matrix().Where(m => m.Needs != Needs.AnyProject))
        {
            var code = (await h.CallAsync("admin", tool, args)).Code();
            if (code != "mcp.projectOff")
            {
                refused.Add($"{tool}: {code ?? "allowed"}");
            }
        }

        using var list = (await h.CallAsync("admin", "list_projects", withProject: false)).Json();

        Assert.True(refused.Count == 0, string.Join("\n", refused));
        Assert.False(list.RootElement.GetProperty("projects")[0].GetProperty("available").GetBoolean());
    }
}
