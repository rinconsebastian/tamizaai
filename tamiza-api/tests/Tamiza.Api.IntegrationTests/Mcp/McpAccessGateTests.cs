using ModelContextProtocol.Client;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Mcp;
using Tamiza.Api.Projects;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpAccessGateTests(PostgresFixture postgres)
{
    private static readonly string[] People = ["outsider", "viewer", "analyst", "admin", "superadmin"];
    private static readonly string[] Tools = ["probe_viewer", "probe_analyst", "probe_rows"];

    [Theory]
    [InlineData("aggregates")]
    [InlineData("rows")]
    public async Task Every_role_project_level_and_need_gets_the_documented_answer(string maximum)
    {
        var settings = new Dictionary<string, string?> { ["TAMIZA_MCP_MAX_DATA_ACCESS"] = maximum, ["TAMIZA_SUPERADMIN_EMAILS"] = "superadmin@example.org" };
        await using var harness = await McpHarness.StartAsync(postgres, settings);
        var ids = new Dictionary<string, Guid>();
        foreach (var person in People)
        {
            ids[person] = await TestData.SignInAsync(harness.Factory, person, $"{person}@example.org");
        }

        var projects = new Dictionary<McpAccess, Guid>();
        foreach (var level in Enum.GetValues<McpAccess>())
        {
            var project = await TestData.InsertProjectAsync(harness.ConnectionString, ids["admin"], name: $"Project {level}");
            await TestData.ExecuteAsync(harness.ConnectionString, "UPDATE tamiza.projects SET mcp_access = @level WHERE id = @id",
                ("level", level.ToString().ToLowerInvariant()), ("id", project));
            await TestData.AddMemberAsync(harness.ConnectionString, project, ids["viewer"], "viewer");
            await TestData.AddMemberAsync(harness.ConnectionString, project, ids["analyst"], "analyst");
            await TestData.AddMemberAsync(harness.ConnectionString, project, ids["admin"], "admin");
            projects[level] = project;
        }

        var deploymentMaximum = maximum == "rows" ? McpAccess.Rows : McpAccess.Aggregates;
        var mismatches = new List<string>();
        foreach (var person in People)
        {
            await using var client = await harness.ConnectAsync(McpHarness.TokenFor(person, person, $"{person}@example.org"));
            foreach (var (level, project) in projects)
            {
                foreach (var tool in Tools)
                {
                    var result = await client.CallToolAsync(tool, new Dictionary<string, object?> { ["projectId"] = project });
                    var (code, effective) = Expected(person, level, deploymentMaximum, tool);
                    var actual = result.Code();
                    if (actual != code)
                    {
                        mismatches.Add($"{person} {tool} on {level} with maximum {maximum}: expected {code ?? "success"}, got {actual ?? "success"}");
                    }
                    else if (code is null)
                    {
                        using var body = result.Json();
                        if (body.RootElement.GetProperty("level").GetString() != effective.ToString().ToLowerInvariant())
                        {
                            mismatches.Add($"{person} {tool} on {level} with maximum {maximum}: level {body.RootElement.GetProperty("level").GetString()}, expected {effective}");
                        }
                    }
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    /// <summary>The documented answer: who may ask, then what the project and the deployment allow.</summary>
    private static (string? Code, McpAccess Effective) Expected(string person, McpAccess project, McpAccess maximum, string tool)
    {
        if (person == "outsider")
        {
            return (McpRefusal.ProjectNotFoundCode, McpAccess.Off);
        }

        var role = person switch
        {
            "viewer" => ProjectRole.Viewer,
            "analyst" => ProjectRole.Analyst,
            _ => ProjectRole.Admin, // The superadmin acts as admin without being a member.
        };
        var needed = tool == "probe_analyst" ? ProjectRole.Analyst : ProjectRole.Viewer;
        if (role < needed)
        {
            return (McpRefusal.ForbiddenCode, McpAccess.Off);
        }

        var effective = project < maximum ? project : maximum;
        if (effective == McpAccess.Off)
        {
            return (McpRefusal.ProjectOffCode, effective);
        }

        return tool == "probe_rows" && effective < McpAccess.Rows ? (McpRefusal.RowsNotAllowedCode, effective) : (null, effective);
    }

    [Fact]
    public async Task A_refusal_says_what_to_do_and_who_decides()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        var admin = await TestData.SignInAsync(harness.Factory, "admin", "admin@example.org");
        var project = await TestData.InsertProjectAsync(harness.ConnectionString, admin);
        await TestData.AddMemberAsync(harness.ConnectionString, project, admin, "admin");
        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("admin", "admin", "admin@example.org"));

        var off = await client.CallToolAsync("probe_viewer", new Dictionary<string, object?> { ["projectId"] = project });
        await TestData.ExecuteAsync(harness.ConnectionString, "UPDATE tamiza.projects SET mcp_access = 'rows'");
        var rows = await client.CallToolAsync("probe_rows", new Dictionary<string, object?> { ["projectId"] = project });

        Assert.Contains("Settings > General", off.Text());
        Assert.Contains("admin of the project", off.Text());
        Assert.Contains("operator of the deployment", rows.Text());
    }

    [Fact]
    public async Task A_member_of_nothing_learns_nothing_about_a_project_that_exists()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        var admin = await TestData.SignInAsync(harness.Factory, "admin", "admin@example.org");
        var project = await TestData.InsertProjectAsync(harness.ConnectionString, admin);
        await TestData.AddMemberAsync(harness.ConnectionString, project, admin, "admin");
        await using var outsider = await harness.ConnectAsync(McpHarness.TokenFor("outsider", "outsider", "outsider@example.org"));

        var real = await outsider.CallToolAsync("probe_viewer", new Dictionary<string, object?> { ["projectId"] = project });
        var invented = await outsider.CallToolAsync("probe_viewer", new Dictionary<string, object?> { ["projectId"] = Guid.NewGuid() });

        Assert.Equal(real.Text(), invented.Text());
    }
}
