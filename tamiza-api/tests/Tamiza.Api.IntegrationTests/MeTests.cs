using System.Net;
using System.Net.Http.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MeTests(PostgresFixture postgres)
{
    private sealed record Me(Guid Id, string Name, string? Email, bool IsSuperAdmin, bool CanCreateProjects);

    [Fact]
    public async Task Returns_the_profile_of_the_signed_in_user()
    {
        await using var factory = new TamizaApiFactory(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { ["TAMIZA_SUPERADMIN_EMAILS"] = "root@example.org, ADA@example.org" });
        using var client = factory.CreateClient().WithToken(TestTokens.Create("sub-1", name: "Ada", email: "ada@example.org"));

        var me = await client.GetFromJsonAsync<Me>("/api/v1/me");

        Assert.NotNull(me);
        Assert.NotEqual(Guid.Empty, me.Id);
        Assert.Equal(("Ada", "ada@example.org", true), (me.Name, me.Email, me.IsSuperAdmin));
        // A superadmin may create projects even when none exists.
        Assert.True(me.CanCreateProjects);
    }

    private async Task<(TamizaApiFactory Factory, string Db, Guid ProjectId)> ProjectOfAdaAsync()
    {
        var db = await postgres.CreateDatabaseAsync();
        var factory = new TamizaApiFactory(db);
        var adaId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adaId);
        await TestData.AddMemberAsync(db, projectId, adaId, "admin");
        return (factory, db, projectId);
    }

    private static async Task<bool> CanCreateAsync(TamizaApiFactory factory, string subject, bool emailVerified = true)
    {
        using var client = TestData.ClientFor(factory, subject, $"{subject}@example.org", emailVerified);
        return (await client.GetFromJsonAsync<Me>("/api/v1/me"))!.CanCreateProjects;
    }

    [Fact]
    public async Task Admins_of_a_project_can_create_projects_and_nobody_else_can()
    {
        var (factory, db, projectId) = await ProjectOfAdaAsync();
        await using var _f = factory;
        foreach (var role in new[] { "analyst", "viewer" })
        {
            await TestData.AddMemberAsync(db, projectId, await TestData.SignInAsync(factory, role, $"{role}@example.org"), role);
        }

        Assert.True(await CanCreateAsync(factory, "ada"));
        Assert.False(await CanCreateAsync(factory, "analyst"));
        Assert.False(await CanCreateAsync(factory, "viewer"));
        // Signed in, in no project.
        Assert.False(await CanCreateAsync(factory, "newcomer"));
    }

    [Fact]
    public async Task A_user_who_is_analyst_of_one_project_and_viewer_of_another_cannot_create_projects()
    {
        var (factory, db, projectId) = await ProjectOfAdaAsync();
        await using var _f = factory;
        var other = await TestData.InsertProjectAsync(db, await TestData.SignInAsync(factory, "ada", "ada@example.org"), "Other project");
        var userId = await TestData.SignInAsync(factory, "mixed", "mixed@example.org");
        await TestData.AddMemberAsync(db, projectId, userId, "analyst");
        await TestData.AddMemberAsync(db, other, userId, "viewer");

        Assert.False(await CanCreateAsync(factory, "mixed"));
    }

    [Fact]
    public async Task A_pending_admin_invitation_does_not_count_until_the_email_is_verified()
    {
        var (factory, _, projectId) = await ProjectOfAdaAsync();
        await using var _f = factory;
        using var ada = TestData.ClientFor(factory, "ada", "ada@example.org");
        (await ada.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new { email = "newbie@example.org", role = "admin" })).EnsureSuccessStatusCode();

        Assert.False(await CanCreateAsync(factory, "newbie", emailVerified: false));
        Assert.True(await CanCreateAsync(factory, "newbie"));
    }

    [Fact]
    public async Task Losing_the_admin_role_changes_the_answer_on_the_next_request()
    {
        var (factory, db, projectId) = await ProjectOfAdaAsync();
        await using var _f = factory;
        var bobId = await TestData.SignInAsync(factory, "bob", "bob@example.org");
        await TestData.AddMemberAsync(db, projectId, bobId, "admin");
        using var ada = TestData.ClientFor(factory, "ada", "ada@example.org");
        using var bob = TestData.ClientFor(factory, "bob", "bob@example.org");

        var before = (await bob.GetFromJsonAsync<Me>("/api/v1/me"))!.CanCreateProjects;
        (await ada.PatchAsJsonAsync($"/api/v1/projects/{projectId}/members/{bobId}", new { role = "analyst" })).EnsureSuccessStatusCode();
        var after = (await bob.GetFromJsonAsync<Me>("/api/v1/me"))!.CanCreateProjects;

        Assert.True(before);
        Assert.False(after);
    }

    [Fact]
    public async Task Unverified_listed_email_is_not_superadmin()
    {
        await using var factory = new TamizaApiFactory(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { ["TAMIZA_SUPERADMIN_EMAILS"] = "ada@example.org" });
        using var client = factory.CreateClient().WithToken(TestTokens.Create("sub-1", email: "ada@example.org", emailVerified: false));

        var me = await client.GetFromJsonAsync<Me>("/api/v1/me");

        Assert.False(me!.IsSuperAdmin);
        // The listed email is not verified, so it is neither superadmin nor, with no project, able to create projects.
        Assert.False(me.CanCreateProjects);
    }

    [Fact]
    public async Task Requires_a_token()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }
}
