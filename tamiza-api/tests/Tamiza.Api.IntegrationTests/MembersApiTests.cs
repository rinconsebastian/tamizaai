using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MembersApiTests(PostgresFixture postgres)
{
    private sealed record Setup(TamizaApiFactory Factory, string Db, Guid ProjectId, HttpClient Admin) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            await Factory.DisposeAsync();
        }
    }

    /// <summary>A project whose only admin is "ada".</summary>
    private async Task<Setup> ProjectAsync()
    {
        var db = await postgres.CreateDatabaseAsync();
        var factory = new TamizaApiFactory(db);
        var adaId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adaId);
        await TestData.AddMemberAsync(db, projectId, adaId, "admin");
        return new Setup(factory, db, projectId, TestData.ClientFor(factory, "ada", "ada@example.org"));
    }

    private static Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid projectId, string email, string role = "analyst") =>
        client.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new { email, role });

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static Task<long> CountAsync(Setup s, string table) => TestData.ScalarAsync<long>(s.Db, $"SELECT count(*) FROM tamiza.{table}");

    // 4.1

    [Fact]
    public async Task Members_are_listed_to_viewers_and_invitations_only_to_admins()
    {
        await using var s = await ProjectAsync();
        var analystId = await TestData.SignInAsync(s.Factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(s.Db, s.ProjectId, analystId, "analyst");
        using var analyst = TestData.ClientFor(s.Factory, "grace", "grace@example.org");
        await InviteAsync(s.Admin, s.ProjectId, "later@example.org", "viewer");

        var members = await analyst.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{s.ProjectId}/members");
        var forbidden = await analyst.GetAsync($"/api/v1/projects/{s.ProjectId}/invitations");
        var invitations = await s.Admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{s.ProjectId}/invitations");

        Assert.Equal([("ada", "admin"), ("grace", "analyst")], members!.Select(m => (m.GetProperty("name").GetString(), m.GetProperty("role").GetString())));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("later@example.org", Assert.Single(invitations!).GetProperty("email").GetString());
    }

    // 4.2

    [Fact]
    public async Task Inviting_a_verified_existing_user_adds_them_directly_ignoring_case()
    {
        await using var s = await ProjectAsync();
        await TestData.SignInAsync(s.Factory, "grace", "grace@example.org");

        var response = await InviteAsync(s.Admin, s.ProjectId, "  GRACE@Example.org ".Trim());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("member", body.GetProperty("kind").GetString());
        Assert.Equal("analyst", body.GetProperty("member").GetProperty("role").GetString());
        Assert.Equal(0L, await CountAsync(s, "project_invitations"));
    }

    [Fact]
    public async Task Inviting_an_unknown_or_unverified_email_creates_a_pending_invitation()
    {
        await using var s = await ProjectAsync();
        await TestData.SignInAsync(s.Factory, "unverified", "unverified@example.org", emailVerified: false);

        var unknown = await InviteAsync(s.Admin, s.ProjectId, "nobody@example.org");
        var unverified = await InviteAsync(s.Admin, s.ProjectId, "unverified@example.org");

        Assert.Equal("invitation", (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString());
        Assert.Equal("invitation", (await unverified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString());
        Assert.Equal(2L, await CountAsync(s, "project_invitations"));
        Assert.Equal(1L, await CountAsync(s, "project_members"));
    }

    [Fact]
    public async Task Inviting_a_member_or_an_already_invited_email_conflicts()
    {
        await using var s = await ProjectAsync();
        await InviteAsync(s.Admin, s.ProjectId, "later@example.org");

        var member = await InviteAsync(s.Admin, s.ProjectId, "Ada@Example.org");
        var duplicate = await InviteAsync(s.Admin, s.ProjectId, "LATER@example.org");

        Assert.Equal(HttpStatusCode.Conflict, member.StatusCode);
        Assert.Equal("member.exists", await CodeAsync(member));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("invitation.exists", await CodeAsync(duplicate));
    }

    [Fact]
    public async Task Invalid_email_or_role_returns_field_errors()
    {
        await using var s = await ProjectAsync();

        var response = await s.Admin.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/members", new { email = "not-an-email", role = "owner" });
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("role", out _));
    }

    // 4.3

    [Fact]
    public async Task A_pending_invitee_who_signs_in_verified_becomes_a_member()
    {
        await using var s = await ProjectAsync();
        await InviteAsync(s.Admin, s.ProjectId, "Grace@Example.org", "analyst");

        using var grace = TestData.ClientFor(s.Factory, "grace", "grace@example.org");
        var project = await grace.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{s.ProjectId}");

        Assert.Equal("analyst", project.GetProperty("myRole").GetString());
        Assert.Equal(0L, await CountAsync(s, "project_invitations"));
    }

    [Fact]
    public async Task An_unverified_sign_in_leaves_the_invitation_pending_until_verified()
    {
        await using var s = await ProjectAsync();
        await InviteAsync(s.Admin, s.ProjectId, "grace@example.org", "viewer");

        using var unverified = TestData.ClientFor(s.Factory, "grace", "grace@example.org", emailVerified: false);
        Assert.Equal(HttpStatusCode.NotFound, (await unverified.GetAsync($"/api/v1/projects/{s.ProjectId}")).StatusCode);
        Assert.Equal(1L, await CountAsync(s, "project_invitations"));

        using var verified = TestData.ClientFor(s.Factory, "grace", "grace@example.org", emailVerified: true);
        Assert.Equal(HttpStatusCode.OK, (await verified.GetAsync($"/api/v1/projects/{s.ProjectId}")).StatusCode);
        Assert.Equal(0L, await CountAsync(s, "project_invitations"));
    }

    [Fact]
    public async Task A_revoked_invitation_is_never_accepted()
    {
        await using var s = await ProjectAsync();
        var invitation = await (await InviteAsync(s.Admin, s.ProjectId, "grace@example.org")).Content.ReadFromJsonAsync<JsonElement>();
        var invitationId = invitation.GetProperty("invitation").GetProperty("id").GetString();

        var revoke = await s.Admin.DeleteAsync($"/api/v1/projects/{s.ProjectId}/invitations/{invitationId}");
        using var grace = TestData.ClientFor(s.Factory, "grace", "grace@example.org");

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync($"/api/v1/projects/{s.ProjectId}")).StatusCode);
    }

    [Fact]
    public async Task A_pending_invitation_role_can_be_changed()
    {
        await using var s = await ProjectAsync();
        var invitation = await (await InviteAsync(s.Admin, s.ProjectId, "grace@example.org", "viewer")).Content.ReadFromJsonAsync<JsonElement>();
        var invitationId = invitation.GetProperty("invitation").GetProperty("id").GetString();

        var changed = await s.Admin.PatchAsJsonAsync($"/api/v1/projects/{s.ProjectId}/invitations/{invitationId}", new { role = "analyst" });
        using var grace = TestData.ClientFor(s.Factory, "grace", "grace@example.org");
        var project = await grace.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{s.ProjectId}");

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("analyst", project.GetProperty("myRole").GetString());
    }

    // 4.4

    [Fact]
    public async Task A_role_change_takes_effect_on_the_next_request_and_removal_hides_the_project()
    {
        await using var s = await ProjectAsync();
        var graceId = await TestData.SignInAsync(s.Factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(s.Db, s.ProjectId, graceId, "viewer");
        using var grace = TestData.ClientFor(s.Factory, "grace", "grace@example.org");
        Assert.Equal(HttpStatusCode.Forbidden, (await grace.GetAsync($"/api/v1/projects/{s.ProjectId}/invitations")).StatusCode);

        var promoted = await s.Admin.PatchAsJsonAsync($"/api/v1/projects/{s.ProjectId}/members/{graceId}", new { role = "admin" });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await grace.GetAsync($"/api/v1/projects/{s.ProjectId}/invitations")).StatusCode);

        var removed = await s.Admin.DeleteAsync($"/api/v1/projects/{s.ProjectId}/members/{graceId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync($"/api/v1/projects/{s.ProjectId}")).StatusCode);
    }

    [Fact]
    public async Task The_only_admin_cannot_leave_or_step_down()
    {
        await using var s = await ProjectAsync();
        var adaId = await TestData.SignInAsync(s.Factory, "ada", "ada@example.org");

        var demote = await s.Admin.PatchAsJsonAsync($"/api/v1/projects/{s.ProjectId}/members/{adaId}", new { role = "viewer" });
        var leave = await s.Admin.DeleteAsync($"/api/v1/projects/{s.ProjectId}/members/{adaId}");

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal("project.last_admin", await CodeAsync(demote));
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
        Assert.Equal("project.last_admin", await CodeAsync(leave));
    }

    [Fact]
    public async Task Two_admins_demoting_each_other_at_once_leave_exactly_one_admin()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var s = await ProjectAsync();
            var adaId = await TestData.SignInAsync(s.Factory, "ada", "ada@example.org");
            var graceId = await TestData.SignInAsync(s.Factory, "grace", "grace@example.org");
            await TestData.AddMemberAsync(s.Db, s.ProjectId, graceId, "admin");
            using var grace = TestData.ClientFor(s.Factory, "grace", "grace@example.org");

            var results = await Task.WhenAll(
                s.Admin.PatchAsJsonAsync($"/api/v1/projects/{s.ProjectId}/members/{graceId}", new { role = "viewer" }),
                grace.PatchAsJsonAsync($"/api/v1/projects/{s.ProjectId}/members/{adaId}", new { role = "viewer" }));

            Assert.Equal(1L, await TestData.ScalarAsync<long>(s.Db, "SELECT count(*) FROM tamiza.project_members WHERE role = 'admin'"));
            Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        }
    }
}
