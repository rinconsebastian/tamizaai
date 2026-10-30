using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpProjectToolsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task List_projects_gives_the_role_the_effective_level_and_whether_the_project_is_available()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres, McpAccess.Rows, maximum: McpAccess.Aggregates);
        var off = await TestData.InsertProjectAsync(h.Db, h.Users["admin"], "Closed project");
        await TestData.AddMemberAsync(h.Db, off, h.Users["admin"], "admin");
        await TestData.AddMemberAsync(h.Db, off, h.Users["viewer"], "viewer");

        using var body = (await h.CallAsync("viewer", "list_projects", withProject: false)).Json();

        Assert.Equal("aggregates", body.RootElement.GetProperty("deploymentMaximum").GetString());
        var projects = body.RootElement.GetProperty("projects").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        Assert.Equal(2, projects.Count);
        // The project's level is rows, but the deployment's maximum limits it to summaries.
        Assert.Equal(("viewer", "aggregates", true), (projects["Water survey"].GetProperty("role").GetString(), projects["Water survey"].GetProperty("access").GetString(), projects["Water survey"].GetProperty("available").GetBoolean()));
        Assert.Equal(("viewer", "off", false), (projects["Closed project"].GetProperty("role").GetString(), projects["Closed project"].GetProperty("access").GetString(), projects["Closed project"].GetProperty("available").GetBoolean()));
    }

    [Fact]
    public async Task List_projects_shows_nothing_of_projects_the_person_does_not_belong_to_and_everything_to_a_superadmin()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);

        using var outsider = (await h.CallAsync("outsider", "list_projects", withProject: false)).Json();
        using var superadmin = (await h.CallAsync("superadmin", "list_projects", withProject: false)).Json();

        Assert.Empty(outsider.RootElement.GetProperty("projects").EnumerateArray());
        var seen = Assert.Single(superadmin.RootElement.GetProperty("projects").EnumerateArray());
        Assert.Equal("admin", seen.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Get_project_gives_a_viewer_the_state_without_the_configuration_and_an_analyst_the_rest()
    {
        await using var h = await McpProjectHarness.CreateAsync(postgres);
        await InsertSubmissionAsync(h, 1, deleted: false);
        await InsertSubmissionAsync(h, 2, deleted: false);
        await InsertSubmissionAsync(h, 3, deleted: true);
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "clean", ("main", "municipality text", "('Ana'),('Luis'),('Rosa')"));
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "piped_water_rate", "municipality text, value numeric", "('Ana', 0.5)");
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, author_id, created_at) VALUES (gen_random_uuid(), @p, 'indicator', 'piped_water_rate', 1, 'x', 'Piped water', 'percent', @a, now())",
            ("p", h.ProjectId), ("a", h.Users["analyst"]));

        using var viewer = (await h.CallAsync("viewer", "get_project")).Json();
        using var analyst = (await h.CallAsync("analyst", "get_project")).Json();

        var v = viewer.RootElement;
        Assert.Equal("Water survey", v.GetProperty("name").GetString());
        Assert.Equal(2, v.GetProperty("submissions").GetProperty("stored").GetInt64());
        Assert.Equal("clean", v.GetProperty("publishedTables")[0].GetProperty("source").GetString());
        Assert.Equal(3, v.GetProperty("publishedTables")[0].GetProperty("tables")[0].GetProperty("rowCount").GetInt64());
        Assert.Equal("piped_water_rate", v.GetProperty("indicators")[0].GetProperty("name").GetString());
        Assert.Equal("Indicators", v.GetProperty("dashboards")[0].GetProperty("name").GetString());
        Assert.Equal(("aggregates", "rows"), (v.GetProperty("access").GetProperty("effective").GetString(), v.GetProperty("access").GetProperty("deploymentMaximum").GetString()));
        Assert.False(v.TryGetProperty("scripts", out _));
        Assert.False(v.TryGetProperty("ingestion", out _));
        Assert.False(v.TryGetProperty("lastRun", out _));

        var a = analyst.RootElement;
        Assert.Equal("piped_water_rate", a.GetProperty("scripts").GetProperty("indicators")[0].GetProperty("name").GetString());
        Assert.Equal(1, a.GetProperty("scripts").GetProperty("indicators")[0].GetProperty("version").GetInt32());
        Assert.Equal(1, a.GetProperty("lastRun").GetProperty("steps").GetArrayLength() > 0 ? 1 : 0);
        Assert.Equal(1, a.GetProperty("ingestion").GetProperty("deletedCount").GetInt32());
    }

    private static Task InsertSubmissionAsync(McpProjectHarness h, long koboId, bool deleted) =>
        TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, data, origin, received_at, updated_at, deleted) VALUES (@p, @id, @uuid, '{}', 'sync', now(), now(), @deleted)",
            ("p", h.ProjectId), ("id", koboId), ("uuid", $"uuid-{koboId}"), ("deleted", deleted));
}
