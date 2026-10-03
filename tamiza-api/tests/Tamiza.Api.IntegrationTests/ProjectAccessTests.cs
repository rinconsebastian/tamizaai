using System.Net;
using System.Net.Http.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectAccessTests(PostgresFixture postgres)
{
    private async Task<(TamizaApiFactory Factory, string Db, Guid ProjectId)> ProjectOwnedByAdminAsync(Dictionary<string, string?>? settings = null)
    {
        var db = await postgres.CreateDatabaseAsync();
        var factory = new TamizaApiFactory(db, settings);
        var owner = await TestData.SignInAsync(factory, "owner", "owner@example.org");
        var projectId = await TestData.InsertProjectAsync(db, owner);
        await TestData.AddMemberAsync(db, projectId, owner, "admin");
        return (factory, db, projectId);
    }

    [Fact]
    public async Task Non_member_gets_404_like_a_missing_project()
    {
        var (factory, _, projectId) = await ProjectOwnedByAdminAsync();
        await using var _f = factory;
        using var stranger = TestData.ClientFor(factory, "stranger", "stranger@example.org");

        var existing = await stranger.GetAsync($"/api/v1/projects/{projectId}");
        var missing = await stranger.GetAsync($"/api/v1/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, existing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", existing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await WithoutTraceIdAsync(missing), await WithoutTraceIdAsync(existing));
    }

    private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }

    [Fact]
    public async Task Member_with_a_lower_role_gets_403()
    {
        var (factory, db, projectId) = await ProjectOwnedByAdminAsync();
        await using var _f = factory;
        var viewerId = await TestData.SignInAsync(factory, "viewer", "viewer@example.org");
        await TestData.AddMemberAsync(db, projectId, viewerId, "viewer");
        using var viewer = TestData.ClientFor(factory, "viewer", "viewer@example.org");

        var response = await viewer.PatchAsJsonAsync($"/api/v1/projects/{projectId}", new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Member_with_a_sufficient_role_is_served_with_their_role()
    {
        var (factory, db, projectId) = await ProjectOwnedByAdminAsync();
        await using var _f = factory;
        var analystId = await TestData.SignInAsync(factory, "analyst", "analyst@example.org");
        await TestData.AddMemberAsync(db, projectId, analystId, "analyst");
        using var analyst = TestData.ClientFor(factory, "analyst", "analyst@example.org");

        var project = await analyst.GetFromJsonAsync<Dictionary<string, object>>($"/api/v1/projects/{projectId}");

        Assert.Equal("analyst", project!["myRole"].ToString());
    }

    [Fact]
    public async Task Superadmin_without_membership_acts_as_admin()
    {
        var (factory, _, projectId) = await ProjectOwnedByAdminAsync(new() { ["TAMIZA_SUPERADMIN_EMAILS"] = "root@example.org" });
        await using var _f = factory;
        using var root = TestData.ClientFor(factory, "root", "root@example.org");

        var project = await root.GetFromJsonAsync<Dictionary<string, object>>($"/api/v1/projects/{projectId}");
        var rename = await root.PatchAsJsonAsync($"/api/v1/projects/{projectId}", new { name = "Renamed by root" });

        Assert.Equal("admin", project!["myRole"].ToString());
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task Each_project_applies_the_role_the_user_has_in_it()
    {
        var (factory, db, adminProject) = await ProjectOwnedByAdminAsync();
        await using var _f = factory;
        var userId = await TestData.SignInAsync(factory, "multi", "multi@example.org");
        await TestData.AddMemberAsync(db, adminProject, userId, "admin");
        var viewerProject = await TestData.InsertProjectAsync(db, userId, "Other project");
        await TestData.AddMemberAsync(db, viewerProject, userId, "viewer");
        using var client = TestData.ClientFor(factory, "multi", "multi@example.org");

        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/projects/{adminProject}", new { name = "A" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/v1/projects/{viewerProject}", new { name = "B" })).StatusCode);
    }
}
