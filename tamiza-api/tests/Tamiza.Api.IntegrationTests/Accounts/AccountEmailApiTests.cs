using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Accounts;

/// <summary>Invitation, resend and password reset emails through the API, with the real Keycloak adapter against <see cref="KeycloakStub"/>.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountEmailApiTests(PostgresFixture postgres)
{
    private sealed class Setup(TamizaApiFactory factory, string db, Guid projectId, Guid adminId, HttpClient admin, KeycloakStub kc, AdjustableTimeProvider time)
        : IAsyncDisposable
    {
        public TamizaApiFactory Factory { get; } = factory;

        public string Db { get; } = db;

        public Guid ProjectId { get; } = projectId;

        public Guid AdminId { get; } = adminId;

        public HttpClient Admin { get; } = admin;

        public KeycloakStub Kc { get; } = kc;

        public AdjustableTimeProvider Time { get; } = time;

        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            await Factory.DisposeAsync();
            Kc.Dispose();
        }
    }

    /// <summary>A project whose only admin is "ada", on a deployment with (or without) account emails.</summary>
    private async Task<Setup> ProjectAsync(bool enabled = true, int? perHour = null)
    {
        var kc = new KeycloakStub();
        var time = new AdjustableTimeProvider();
        var settings = enabled ? kc.Settings() : new Dictionary<string, string?>();
        if (perHour is { } limit)
        {
            settings["TAMIZA_ACCOUNT_EMAILS_PER_HOUR"] = limit.ToString();
        }

        var db = await postgres.CreateDatabaseAsync();
        var factory = new TamizaApiFactory(db, settings, services => services.AddSingleton<TimeProvider>(time));
        var adaId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adaId);
        await TestData.AddMemberAsync(db, projectId, adaId, "admin");
        return new Setup(factory, db, projectId, adaId, TestData.ClientFor(factory, "ada", "ada@example.org"), kc, time);
    }

    /// <summary>Signs a person in and makes them a member; returns their local id and a client.</summary>
    private static async Task<(Guid Id, HttpClient Client)> MemberAsync(Setup s, string subject, string email, string role)
    {
        var id = await TestData.SignInAsync(s.Factory, subject, email);
        await TestData.AddMemberAsync(s.Db, s.ProjectId, id, role);
        return (id, TestData.ClientFor(s.Factory, subject, email));
    }

    private static Task<HttpResponseMessage> InviteAsync(Setup s, string email, string role = "analyst", string? language = null) =>
        s.Admin.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/members", new { email, role, language });

    private static Task<HttpResponseMessage> ResendAsync(Setup s, Guid invitationId, HttpClient? client = null, string? language = null) =>
        (client ?? s.Admin).PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/invitations/{invitationId}/email", new { language });

    private static Task<HttpResponseMessage> ResetAsync(Setup s, Guid userId, HttpClient? client = null) =>
        (client ?? s.Admin).PostAsync($"/api/v1/projects/{s.ProjectId}/members/{userId}/password-reset", null);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        // Read as text, which can be repeated, so a test may look at the code and then at another property.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static async Task<Guid> InvitedAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("invitation").GetProperty("id").GetGuid();

    private static async Task<JsonElement[]> InvitationsAsync(Setup s) =>
        (await s.Admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{s.ProjectId}/invitations"))!;

    private static string[] Actions(KeycloakStub.Call call) => call.Json!.AsArray().Select(a => a!.GetValue<string>()).ToArray();

    // Inviting

    [Fact]
    public async Task Without_account_emails_inviting_works_as_before_and_nothing_reaches_the_provider()
    {
        await using var s = await ProjectAsync(enabled: false);

        var response = await InviteAsync(s, "ana@example.org");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("invitation", body.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("emailOutcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("invitation").GetProperty("lastEmailAt").ValueKind);
        Assert.Empty(s.Kc.Calls);
        Assert.Single(await InvitationsAsync(s));
    }

    [Fact]
    public async Task A_new_person_gets_an_account_and_the_setup_email()
    {
        await using var s = await ProjectAsync();

        var response = await InviteAsync(s, "Ana@Example.org");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("sent", body.GetProperty("emailOutcome").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("invitation").GetProperty("lastEmailAt").ValueKind);
        var account = s.Kc.FindUser("ana@example.org")!;
        Assert.True(account.CreatedByApi);
        Assert.False(account.EmailVerified);
        Assert.True(account.Enabled);
        var action = Assert.Single(s.Kc.ActionCalls);
        Assert.Contains(account.Id, action.Path);
        Assert.Equal(["UPDATE_PASSWORD", "VERIFY_EMAIL"], Actions(action));
        var listed = Assert.Single(await InvitationsAsync(s));
        Assert.Equal("Ana@Example.org", listed.GetProperty("email").GetString());
        Assert.NotEqual(JsonValueKind.Null, listed.GetProperty("lastEmailAt").ValueKind);
        Assert.Equal(1L, await TestData.ScalarAsync<long>(s.Db, "SELECT count(*) FROM tamiza.invited_accounts WHERE normalized_email = 'ana@example.org'"));
    }

    [Theory]
    [InlineData("es", "es")]
    [InlineData("EN", "en")]
    [InlineData("fr", null)]
    [InlineData(null, null)]
    public async Task The_inviters_interface_language_becomes_the_accounts_locale(string? language, string? locale)
    {
        await using var s = await ProjectAsync();

        await InviteAsync(s, "ana@example.org", language: language);

        var attributes = s.Kc.CreateCalls.Single().Json!["attributes"];
        Assert.Equal(locale, attributes?["locale"]?[0]?.GetValue<string>());
    }

    [Fact]
    public async Task A_failing_provider_does_not_lose_the_invitation_nor_leak_secrets_into_the_log()
    {
        await using var s = await ProjectAsync();
        s.Kc.ActionsFailure = (500, "Failed to send execute actions email for ana@example.org");

        var response = await InviteAsync(s, "ana@example.org");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("failed", (await JsonAsync(response)).GetProperty("emailOutcome").GetString());
        var listed = Assert.Single(await InvitationsAsync(s));
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("lastEmailAt").ValueKind);
        var logs = string.Join('\n', s.Factory.Logs.Lines);
        Assert.Contains("HTTP 500", logs);
        Assert.DoesNotContain(KeycloakStub.ClientSecret, logs);
        Assert.DoesNotContain("kc-token-", logs);
        Assert.DoesNotContain("ana@example.org", logs.Replace("ada@example.org", ""));
    }

    [Fact]
    public async Task An_unreachable_provider_is_a_failed_outcome()
    {
        await using var s = await ProjectAsync();
        s.Kc.Dispose();

        var response = await InviteAsync(s, "ana@example.org");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("failed", (await JsonAsync(response)).GetProperty("emailOutcome").GetString());
        Assert.Single(await InvitationsAsync(s));
    }

    [Fact]
    public async Task An_account_made_in_the_console_is_left_alone()
    {
        await using var s = await ProjectAsync();
        s.Kc.AddUser("ana@example.org", emailVerified: false);

        var response = await InviteAsync(s, "ana@example.org");

        Assert.Equal("existingAccount", (await JsonAsync(response)).GetProperty("emailOutcome").GetString());
        Assert.Empty(s.Kc.CreateCalls);
        Assert.Empty(s.Kc.ActionCalls);
        Assert.Single(await InvitationsAsync(s));
    }

    [Fact]
    public async Task Someone_who_already_uses_tamiza_becomes_a_member_without_any_provider_request()
    {
        await using var s = await ProjectAsync();
        await TestData.SignInAsync(s.Factory, "grace", "grace@example.org");

        var response = await InviteAsync(s, "grace@example.org");

        var body = await JsonAsync(response);
        Assert.Equal("member", body.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("emailOutcome").ValueKind);
        Assert.Empty(s.Kc.Calls);
    }

    [Fact]
    public async Task A_refused_invitation_makes_no_provider_request()
    {
        await using var s = await ProjectAsync();
        await InviteAsync(s, "ana@example.org");
        var before = s.Kc.Calls.Count;

        var duplicate = await InviteAsync(s, "ana@example.org");
        var member = await InviteAsync(s, "ada@example.org");
        var invalid = await InviteAsync(s, "not-an-email");

        Assert.Equal("invitation.exists", await CodeAsync(duplicate));
        Assert.Equal("member.exists", await CodeAsync(member));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(before, s.Kc.Calls.Count);
    }

    [Fact]
    public async Task The_hourly_limit_saves_the_invitation_without_a_provider_request()
    {
        await using var s = await ProjectAsync(perHour: 1);
        await InviteAsync(s, "ana@example.org");
        var before = s.Kc.Calls.Count;

        var response = await InviteAsync(s, "bea@example.org");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("rateLimited", (await JsonAsync(response)).GetProperty("emailOutcome").GetString());
        Assert.Equal(2, (await InvitationsAsync(s)).Length);
        Assert.Equal(before, s.Kc.Calls.Count);
    }

    [Fact]
    public async Task The_same_person_invited_to_another_project_is_not_emailed_twice_within_ten_minutes()
    {
        await using var s = await ProjectAsync();
        var other = await TestData.InsertProjectAsync(s.Db, s.AdminId, "Second project");
        await TestData.AddMemberAsync(s.Db, other, s.AdminId, "admin");
        await InviteAsync(s, "ana@example.org");

        var second = await s.Admin.PostAsJsonAsync($"/api/v1/projects/{other}/members", new { email = "ana@example.org", role = "viewer" });

        Assert.Equal("recentlySent", (await JsonAsync(second)).GetProperty("emailOutcome").GetString());
        Assert.Single(s.Kc.CreateCalls);
        Assert.Single(s.Kc.ActionCalls);
    }

    // Resending

    [Fact]
    public async Task A_link_that_expired_can_be_sent_again()
    {
        await using var s = await ProjectAsync();
        var invitation = await InvitedAsync(await InviteAsync(s, "ana@example.org"));
        var first = (await InvitationsAsync(s)).Single().GetProperty("lastEmailAt").GetDateTimeOffset();
        s.Time.Advance(TimeSpan.FromHours(13));

        var response = await ResendAsync(s, invitation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(invitation, body.GetProperty("id").GetGuid());
        Assert.True(body.GetProperty("lastEmailAt").GetDateTimeOffset() > first.AddHours(12));
        Assert.Equal(2, s.Kc.ActionCalls.Count);
        Assert.Single(s.Kc.CreateCalls);
    }

    [Fact]
    public async Task An_account_not_made_by_tamiza_or_already_verified_has_nothing_to_send()
    {
        await using var s = await ProjectAsync();
        s.Kc.AddUser("outside@example.org", emailVerified: false);
        var outside = await InvitedAsync(await InviteAsync(s, "outside@example.org"));
        var made = await InvitedAsync(await InviteAsync(s, "made@example.org"));
        s.Kc.FindUser("made@example.org")!.EmailVerified = true;
        s.Time.Advance(TimeSpan.FromMinutes(11));

        foreach (var invitation in new[] { outside, made })
        {
            var response = await ResendAsync(s, invitation);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("accountEmail.existingAccount", await CodeAsync(response));
        }

        Assert.Single(s.Kc.ActionCalls);
    }

    [Fact]
    public async Task An_account_deleted_in_the_console_is_created_again()
    {
        await using var s = await ProjectAsync();
        var invitation = await InvitedAsync(await InviteAsync(s, "ana@example.org"));
        s.Kc.RemoveUser(s.Kc.FindUser("ana@example.org")!.Id);
        s.Time.Advance(TimeSpan.FromMinutes(11));

        var response = await ResendAsync(s, invitation, language: "es");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, s.Kc.CreateCalls.Count);
        Assert.NotNull(s.Kc.FindUser("ana@example.org"));
        Assert.Equal("es", s.Kc.CreateCalls.Last().Json!["attributes"]!["locale"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task A_provider_failure_on_resend_is_a_502_and_the_invitation_stays()
    {
        await using var s = await ProjectAsync();
        var invitation = await InvitedAsync(await InviteAsync(s, "ana@example.org"));
        s.Time.Advance(TimeSpan.FromMinutes(11));
        s.Kc.ActionsFailure = (500, "Failed to send execute actions email");

        var response = await ResendAsync(s, invitation);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("accountEmail.failed", await CodeAsync(response));
        Assert.Single(await InvitationsAsync(s));
    }

    [Fact]
    public async Task Resending_within_ten_minutes_is_a_429_with_the_seconds_to_wait()
    {
        await using var s = await ProjectAsync();
        var invitation = await InvitedAsync(await InviteAsync(s, "ana@example.org"));
        s.Time.Advance(TimeSpan.FromMinutes(5));

        var response = await ResendAsync(s, invitation);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("accountEmail.rateLimited", await CodeAsync(response));
        var retryAfter = (await JsonAsync(response)).GetProperty("retryAfter").GetInt32();
        Assert.InRange(retryAfter, 290, 301);
        Assert.Equal(retryAfter.ToString(), response.Headers.GetValues("Retry-After").Single());
        Assert.Single(s.Kc.ActionCalls);
    }

    [Fact]
    public async Task Resending_past_the_hourly_limit_is_a_429_with_the_time_left_in_the_hour()
    {
        await using var s = await ProjectAsync(perHour: 1);
        var invitation = await InvitedAsync(await InviteAsync(s, "ana@example.org"));
        s.Time.Advance(TimeSpan.FromMinutes(20));

        var response = await ResendAsync(s, invitation);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("accountEmail.rateLimited", await CodeAsync(response));
        Assert.InRange((await JsonAsync(response)).GetProperty("retryAfter").GetInt32(), 2390, 2401);
    }

    [Fact]
    public async Task Resending_without_account_emails_is_a_503()
    {
        await using var s = await ProjectAsync(enabled: false);
        var invitation = await InvitedAsync(await InviteAsync(s, "ana@example.org"));

        var response = await ResendAsync(s, invitation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("accountEmail.disabled", await CodeAsync(response));
    }

    [Fact]
    public async Task Only_admins_resend_and_only_for_their_own_projects_invitations()
    {
        await using var s = await ProjectAsync();
        var invitation = await InvitedAsync(await InviteAsync(s, "ana@example.org"));
        s.Time.Advance(TimeSpan.FromMinutes(11));
        var (_, analyst) = await MemberAsync(s, "grace", "grace@example.org", "analyst");
        var (_, viewer) = await MemberAsync(s, "hedy", "hedy@example.org", "viewer");
        await TestData.SignInAsync(s.Factory, "ines", "ines@example.org");
        using var outsider = TestData.ClientFor(s.Factory, "ines", "ines@example.org");
        var other = await TestData.InsertProjectAsync(s.Db, s.AdminId, "Second project");
        await TestData.AddMemberAsync(s.Db, other, s.AdminId, "admin");
        var otherInvitation = await InvitedAsync(await s.Admin.PostAsJsonAsync($"/api/v1/projects/{other}/members", new { email = "zoe@example.org", role = "viewer" }));
        s.Time.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(HttpStatusCode.Forbidden, (await ResendAsync(s, invitation, analyst)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ResendAsync(s, invitation, viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ResendAsync(s, invitation, outsider)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ResendAsync(s, otherInvitation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ResendAsync(s, Guid.NewGuid())).StatusCode);
        Assert.Equal(2, s.Kc.ActionCalls.Count);
        analyst.Dispose();
        viewer.Dispose();
    }

    // Password reset

    [Fact]
    public async Task A_member_is_sent_the_reset_email_for_the_account_they_sign_in_with()
    {
        await using var s = await ProjectAsync();
        var account = s.Kc.AddUser("grace@example.org");
        var (graceId, grace) = await MemberAsync(s, account.Id, "grace@example.org", "analyst");

        var response = await ResetAsync(s, graceId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var action = Assert.Single(s.Kc.ActionCalls);
        Assert.Contains(account.Id, action.Path);
        Assert.Equal(["UPDATE_PASSWORD"], Actions(action));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(s.Db, "SELECT count(*) FROM tamiza.invited_accounts"));
        Assert.Single(s.Kc.Users);
        grace.Dispose();
    }

    [Fact]
    public async Task An_admin_can_reset_their_own_password()
    {
        await using var s = await ProjectAsync();
        var account = s.Kc.AddUser("ada@example.org");
        await TestData.ExecuteAsync(s.Db, "UPDATE tamiza.users SET keycloak_sub = @sub WHERE id = @id", ("sub", account.Id), ("id", s.AdminId));

        var response = await ResetAsync(s, s.AdminId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(account.Id, Assert.Single(s.Kc.ActionCalls).Path);
    }

    [Fact]
    public async Task A_member_without_an_active_account_has_nobody_to_email()
    {
        await using var s = await ProjectAsync();
        var disabled = s.Kc.AddUser("disabled@example.org", enabled: false);
        var (missingId, missing) = await MemberAsync(s, "no-such-account", "missing@example.org", "viewer");
        var (disabledId, disabledClient) = await MemberAsync(s, disabled.Id, "disabled@example.org", "viewer");

        foreach (var id in new[] { missingId, disabledId })
        {
            var response = await ResetAsync(s, id);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("accountEmail.noAccount", await CodeAsync(response));
        }

        Assert.Empty(s.Kc.ActionCalls);
        missing.Dispose();
        disabledClient.Dispose();
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_of_the_project_is_404_without_any_provider_request()
    {
        await using var s = await ProjectAsync();
        var account = s.Kc.AddUser("grace@example.org");
        var stranger = await TestData.SignInAsync(s.Factory, account.Id, "grace@example.org");

        var response = await ResetAsync(s, stranger);
        var unknown = await ResetAsync(s, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty(s.Kc.Calls);
    }

    [Fact]
    public async Task A_provider_failure_on_reset_is_a_502()
    {
        await using var s = await ProjectAsync();
        var account = s.Kc.AddUser("grace@example.org");
        var (graceId, grace) = await MemberAsync(s, account.Id, "grace@example.org", "viewer");
        s.Kc.ActionsFailure = (500, "Failed to send execute actions email");

        var response = await ResetAsync(s, graceId);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("accountEmail.failed", await CodeAsync(response));
        grace.Dispose();
    }

    [Fact]
    public async Task A_second_reset_within_ten_minutes_is_a_429()
    {
        await using var s = await ProjectAsync();
        var account = s.Kc.AddUser("grace@example.org");
        var (graceId, grace) = await MemberAsync(s, account.Id, "grace@example.org", "viewer");
        await ResetAsync(s, graceId);
        s.Time.Advance(TimeSpan.FromMinutes(4));

        var response = await ResetAsync(s, graceId);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("accountEmail.rateLimited", await CodeAsync(response));
        Assert.InRange((await JsonAsync(response)).GetProperty("retryAfter").GetInt32(), 350, 361);
        Assert.NotEmpty(response.Headers.GetValues("Retry-After"));
        Assert.Single(s.Kc.ActionCalls);
        grace.Dispose();
    }

    [Fact]
    public async Task A_reset_without_account_emails_is_a_503()
    {
        await using var s = await ProjectAsync(enabled: false);
        var (graceId, grace) = await MemberAsync(s, "grace", "grace@example.org", "viewer");

        var response = await ResetAsync(s, graceId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("accountEmail.disabled", await CodeAsync(response));
        grace.Dispose();
    }

    [Fact]
    public async Task Only_admins_reset_passwords_and_outsiders_do_not_see_the_project()
    {
        await using var s = await ProjectAsync();
        var account = s.Kc.AddUser("grace@example.org");
        var (graceId, grace) = await MemberAsync(s, account.Id, "grace@example.org", "viewer");
        var (_, analyst) = await MemberAsync(s, "hedy", "hedy@example.org", "analyst");
        await TestData.SignInAsync(s.Factory, "ines", "ines@example.org");
        using var outsider = TestData.ClientFor(s.Factory, "ines", "ines@example.org");

        Assert.Equal(HttpStatusCode.Forbidden, (await ResetAsync(s, graceId, grace)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ResetAsync(s, graceId, analyst)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ResetAsync(s, graceId, outsider)).StatusCode);
        Assert.Empty(s.Kc.ActionCalls);
        grace.Dispose();
        analyst.Dispose();
    }

    // Lists, configuration and contract

    [Fact]
    public async Task Changing_an_invitations_role_keeps_its_last_email_time()
    {
        await using var s = await ProjectAsync();
        var invitation = await InvitedAsync(await InviteAsync(s, "ana@example.org"));

        var response = await s.Admin.PatchAsJsonAsync($"/api/v1/projects/{s.ProjectId}/invitations/{invitation}", new { role = "viewer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await JsonAsync(response)).GetProperty("lastEmailAt").ValueKind);
    }

    [Fact]
    public async Task The_system_configuration_says_whether_account_emails_are_enabled_and_nothing_else()
    {
        using var kc = new KeycloakStub();
        foreach (var (settings, enabled) in new[] { (new Dictionary<string, string?>(), false), (kc.Settings(), true) })
        {
            await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), settings);
            using var client = factory.CreateClient();

            var text = await client.GetStringAsync("/api/v1/system/config");

            using var document = JsonDocument.Parse(text);
            var accountEmails = document.RootElement.GetProperty("accountEmails");
            Assert.Equal(["enabled"], accountEmails.EnumerateObject().Select(p => p.Name));
            Assert.Equal(enabled, accountEmails.GetProperty("enabled").GetBoolean());
            Assert.DoesNotContain(KeycloakStub.ClientSecret, text);
            Assert.DoesNotContain(KeycloakStub.ClientId, text);
            Assert.DoesNotContain(kc.Url, text);
            Assert.DoesNotContain("Keycloak", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_openapi_document_describes_the_account_email_endpoints_and_fields()
    {
        await using var s = await ProjectAsync();
        using var anonymous = s.Factory.CreateClient();

        var document = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        const string project = "/api/v1/projects/{projectId}";
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty(project + "/invitations/{invitationId}/email").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty(project + "/members/{userId}/password-reset").TryGetProperty("post", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.Contains("lastEmailAt", Properties(schemas.GetProperty("InvitationView")));
        Assert.Contains("emailOutcome", Properties(schemas.GetProperty("InviteResult")));
        Assert.Contains("language", Properties(schemas.GetProperty("InviteRequest")));
    }

    private static string[] Properties(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
}
