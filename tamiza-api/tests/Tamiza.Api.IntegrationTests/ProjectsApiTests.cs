using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectsApiTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    /// <summary>
    /// Only admins of a project and superadmins can create projects, so "ada" is the superadmin by default, which lets her create the
    /// first project. <paramref name="superAdmin"/> <c>false</c> gives a factory without any, for the creation-rule tests.
    /// </summary>
    private async Task<(TamizaApiFactory Factory, string Db)> FactoryAsync(
        bool allowPrivate = true, Dictionary<string, string?>? extra = null, bool superAdmin = true)
    {
        var db = await postgres.CreateDatabaseAsync();
        var settings = new Dictionary<string, string?>
        {
            ["TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS"] = allowPrivate ? "true" : "false",
            ["TAMIZA_SUPERADMIN_EMAILS"] = superAdmin ? "ada@example.org" : "",
        };
        foreach (var (key, value) in extra ?? new())
        {
            settings[key] = value;
        }

        return (new TamizaApiFactory(db, settings), db);
    }

    private object CreateRequest(string? token = null, string? assetUid = null, string? server = null, string? name = "Household survey 2026") =>
        new { name, koboServerUrl = server ?? _kobo.Url, assetUid = assetUid ?? KoboStub.AssetUid, apiToken = token ?? KoboStub.Token };

    private async Task<JsonElement> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/projects", CreateRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    /// <summary>The <c>dashboards</c> of project details as <c>(id, name)</c>, in the order listed.</summary>
    private static (Guid Id, string Name)[] Dashboards(JsonElement details) =>
        details.GetProperty("dashboards").EnumerateArray().Select(d => (d.GetProperty("id").GetGuid(), d.GetProperty("name").GetString()!)).ToArray();

    /// <summary>Seeds a project whose member is <paramref name="subject"/> (their email is <c>subject@example.org</c>), in <paramref name="role"/>.</summary>
    private static async Task<Guid> SeedProjectAsync(
        TamizaApiFactory factory, string db, string subject, string name = "Seeded project", string role = "admin")
    {
        var userId = await TestData.SignInAsync(factory, subject, $"{subject}@example.org");
        var projectId = await TestData.InsertProjectAsync(db, userId, name);
        await TestData.AddMemberAsync(db, projectId, userId, role);
        return projectId;
    }

    // 3.2 — creation

    [Fact]
    public async Task Creating_a_project_checks_kobo_and_makes_the_creator_admin()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");

        var response = await client.PostAsJsonAsync("/api/v1/projects", CreateRequest());
        var raw = await response.Content.ReadAsStringAsync();
        var created = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = created.GetProperty("project");
        Assert.Equal("Household survey", project.GetProperty("formName").GetString());
        Assert.Equal("admin", project.GetProperty("myRole").GetString());
        Assert.Equal("tamiza", created.GetProperty("webhook").GetProperty("username").GetString());
        Assert.Equal($"https://tamiza.test/api/v1/webhooks/kobo/{project.GetProperty("id").GetString()}", created.GetProperty("webhook").GetProperty("url").GetString());
        Assert.DoesNotContain(KoboStub.Token, raw);
        Assert.Equal(1L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.project_members WHERE role = 'admin'"));
        Assert.NotEqual(KoboStub.Token, await TestData.ScalarAsync<string>(db, "SELECT encrypted_api_token FROM tamiza.projects"));
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(KoboStub.Token));
    }

    [Theory]
    [InlineData("wrong-token", null, null, "kobo.unauthorized")]
    [InlineData(null, "aMissingForm", null, "kobo.form_not_found")]
    [InlineData(null, null, "https://kobo.tamiza-test.invalid", "kobo.unreachable")]
    public async Task Failed_kobo_checks_return_422_with_a_code_and_save_nothing(string? token, string? assetUid, string? server, string code)
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");

        var response = await client.PostAsJsonAsync("/api/v1/projects", CreateRequest(token, assetUid, server));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.projects"));
    }

    [Fact]
    public async Task Plain_http_is_rejected_when_private_networks_are_not_allowed()
    {
        var (factory, db) = await FactoryAsync(allowPrivate: false);
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");

        var response = await client.PostAsJsonAsync("/api/v1/projects", CreateRequest());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("kobo.https_required", await CodeAsync(response));
        Assert.Empty(_kobo.Server.LogEntries);
        Assert.Equal(0L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.projects"));
    }

    [Fact]
    public async Task Invalid_input_returns_field_errors_and_saves_nothing()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");

        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = " ", koboServerUrl = "kf.kobotoolbox.org", assetUid = "../etc", apiToken = "" });
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        foreach (var field in new[] { "name", "koboServerUrl", "assetUid", "apiToken" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }

        Assert.Equal(0L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.projects"));
    }

    // Who may create a project

    private async Task<HttpResponseMessage> TryCreateAsync(HttpClient client) =>
        await client.PostAsJsonAsync("/api/v1/projects", CreateRequest());

    private static async Task AssertNotAllowedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("project.create_not_allowed", await CodeAsync(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("analyst")]
    [InlineData("viewer")]
    public async Task A_user_who_is_not_admin_of_any_project_gets_403_and_nothing_is_saved_or_sent_to_kobo(string? role)
    {
        var (factory, db) = await FactoryAsync(superAdmin: false);
        await using var owner = factory;
        var projects = 0L;
        if (role is not null)
        {
            var projectId = await SeedProjectAsync(factory, db, "ada");
            var userId = await TestData.SignInAsync(factory, "mallory", "mallory@example.org");
            await TestData.AddMemberAsync(db, projectId, userId, role);
            projects = 1;
        }

        using var client = TestData.ClientFor(factory, "mallory", "mallory@example.org");
        var response = await TryCreateAsync(client);

        await AssertNotAllowedAsync(response);
        Assert.Equal(projects, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.projects"));
        Assert.Empty(_kobo.Server.LogEntries);
    }

    [Fact]
    public async Task The_refusal_comes_before_the_fields_and_the_server_address_are_checked()
    {
        var (factory, db) = await FactoryAsync(allowPrivate: false, superAdmin: false);
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "mallory", "mallory@example.org");

        var response = await client.PostAsJsonAsync("/api/v1/projects",
            new { name = " ", koboServerUrl = "http://kf.kobotoolbox.org", assetUid = "../etc", apiToken = "" });

        await AssertNotAllowedAsync(response);
        Assert.Equal(0L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.projects"));
        Assert.Empty(_kobo.Server.LogEntries);
    }

    [Fact]
    public async Task An_admin_of_a_seeded_project_creates_another_one_and_becomes_its_admin()
    {
        var (factory, db) = await FactoryAsync(superAdmin: false);
        await using var owner = factory;
        await SeedProjectAsync(factory, db, "grace");
        await SeedProjectAsync(factory, db, "ada");
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");

        var response = await TryCreateAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.project_members m JOIN tamiza.users u ON u.id = m.user_id WHERE u.keycloak_sub = 'ada' AND m.role = 'admin'"));
        Assert.Equal(3L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.projects"));
    }

    [Fact]
    public async Task A_verified_superadmin_creates_the_first_project_of_an_empty_installation_and_becomes_its_admin()
    {
        var (factory, db) = await FactoryAsync(superAdmin: false, extra: new() { ["TAMIZA_SUPERADMIN_EMAILS"] = "root@example.org" });
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "root", "root@example.org");

        var response = await TryCreateAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.projects"));
        Assert.Equal("admin", await TestData.ScalarAsync<string>(db, "SELECT m.role FROM tamiza.project_members m JOIN tamiza.users u ON u.id = m.user_id WHERE u.keycloak_sub = 'root'"));
    }

    [Fact]
    public async Task A_pending_admin_invitation_does_not_allow_creating_until_a_verified_sign_in_accepts_it()
    {
        var (factory, db) = await FactoryAsync(superAdmin: false);
        await using var owner = factory;
        var projectId = await SeedProjectAsync(factory, db, "ada");
        using var ada = TestData.ClientFor(factory, "ada", "ada@example.org");
        (await ada.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new { email = "newbie@example.org", role = "admin" })).EnsureSuccessStatusCode();

        using var unverified = TestData.ClientFor(factory, "newbie", "newbie@example.org", emailVerified: false);
        var refused = await TryCreateAsync(unverified);
        using var verified = TestData.ClientFor(factory, "newbie", "newbie@example.org");
        var created = await TryCreateAsync(verified);

        await AssertNotAllowedAsync(refused);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(2L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.projects"));
    }

    [Theory]
    [InlineData("demoted")]
    [InlineData("removed")]
    public async Task Losing_the_only_admin_role_takes_away_the_right_to_create_on_the_next_attempt(string how)
    {
        var (factory, db) = await FactoryAsync(superAdmin: false);
        await using var owner = factory;
        var projectId = await SeedProjectAsync(factory, db, "ada");
        var bobId = await TestData.SignInAsync(factory, "bob", "bob@example.org");
        await TestData.AddMemberAsync(db, projectId, bobId, "admin");
        using var ada = TestData.ClientFor(factory, "ada", "ada@example.org");
        using var bob = TestData.ClientFor(factory, "bob", "bob@example.org");

        var change = how == "demoted"
            ? await ada.PatchAsJsonAsync($"/api/v1/projects/{projectId}/members/{bobId}", new { role = "analyst" })
            : await ada.DeleteAsync($"/api/v1/projects/{projectId}/members/{bobId}");
        var next = await TryCreateAsync(bob);

        Assert.True(change.IsSuccessStatusCode);
        await AssertNotAllowedAsync(next);
    }

    [Fact]
    public async Task The_openapi_document_lists_the_refusal_to_create_and_the_optional_configuration_values()
    {
        var (factory, _) = await FactoryAsync();
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var responses = document.GetProperty("paths").GetProperty("/api/v1/projects").GetProperty("post").GetProperty("responses");
        Assert.True(responses.TryGetProperty("403", out _));
        var details = document.GetProperty("components").GetProperty("schemas").GetProperty("ProjectDetails");
        foreach (var name in ConfigurationValues)
        {
            Assert.Contains("null", details.GetProperty("properties").GetProperty(name).GetRawText());
        }
    }

    // 3.3 — list, details, update

    private static readonly string[] ConfigurationValues =
        ["koboServerUrl", "assetUid", "formCheckedAt", "enumeratorField", "fieldCheck", "tokenUnreadable", "unavailableFormVersions"];

    [Fact]
    public async Task A_viewers_details_leave_out_the_configuration_values_and_an_analysts_carry_them_all()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var created = (await CreateProjectAsync(admin)).GetProperty("project");
        var id = created.GetProperty("id").GetGuid();
        (await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { enumeratorField = "enumerator", timeZone = "America/Bogota" })).EnsureSuccessStatusCode();
        foreach (var (subject, role) in new[] { ("vic", "viewer"), ("grace", "analyst") })
        {
            await TestData.AddMemberAsync(db, id, await TestData.SignInAsync(factory, subject, $"{subject}@example.org"), role);
        }

        using var viewer = TestData.ClientFor(factory, "vic", "vic@example.org");
        using var analyst = TestData.ClientFor(factory, "grace", "grace@example.org");
        var viewerRaw = await viewer.GetStringAsync($"/api/v1/projects/{id}");
        var seenByViewer = JsonDocument.Parse(viewerRaw).RootElement;
        var seenByAnalyst = await analyst.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");

        Assert.Equal("Household survey 2026", seenByViewer.GetProperty("name").GetString());
        Assert.Equal("Household survey", seenByViewer.GetProperty("formName").GetString());
        Assert.Equal("viewer", seenByViewer.GetProperty("myRole").GetString());
        Assert.Equal("America/Bogota", seenByViewer.GetProperty("timeZone").GetString());
        Assert.Equal(Dashboards(created), Dashboards(seenByViewer));
        Assert.Contains("createdAt", viewerRaw);
        foreach (var name in ConfigurationValues)
        {
            // Present and null, not left out.
            Assert.Equal(JsonValueKind.Null, seenByViewer.GetProperty(name).ValueKind);
            Assert.NotEqual(JsonValueKind.Null, seenByAnalyst.GetProperty(name).ValueKind);
        }

        Assert.DoesNotContain(KoboStub.AssetUid, viewerRaw);
        Assert.Equal(created.GetProperty("koboServerUrl").GetString(), seenByAnalyst.GetProperty("koboServerUrl").GetString());
        Assert.Equal(KoboStub.AssetUid, seenByAnalyst.GetProperty("assetUid").GetString());
        Assert.Equal("enumerator", seenByAnalyst.GetProperty("enumeratorField").GetString());
        Assert.False(seenByAnalyst.GetProperty("tokenUnreadable").GetBoolean());
        Assert.Equal(0, seenByAnalyst.GetProperty("unavailableFormVersions").GetInt32());
        Assert.NotEmpty(seenByAnalyst.GetProperty("fieldCheck").EnumerateArray());
    }

    [Fact]
    public async Task The_list_shows_own_projects_and_superadmins_see_all()
    {
        var (factory, db) = await FactoryAsync(extra: new() { ["TAMIZA_SUPERADMIN_EMAILS"] = "root@example.org" });
        await using var owner = factory;
        using var ada = TestData.ClientFor(factory, "ada", "ada@example.org");
        using var grace = TestData.ClientFor(factory, "grace", "grace@example.org");
        using var root = TestData.ClientFor(factory, "root", "root@example.org");
        await SeedProjectAsync(factory, db, "ada", "Ada's project");
        await SeedProjectAsync(factory, db, "grace", "Grace's project");

        var adaList = await ada.GetFromJsonAsync<JsonElement[]>("/api/v1/projects");
        var rootList = await root.GetFromJsonAsync<JsonElement[]>("/api/v1/projects");

        Assert.Single(adaList!);
        Assert.Equal("admin", adaList![0].GetProperty("myRole").GetString());
        Assert.Equal(2, rootList!.Length);
    }

    [Fact]
    public async Task Details_never_contain_the_token_or_the_secret()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");
        var created = await CreateProjectAsync(client);
        var id = created.GetProperty("project").GetProperty("id").GetString();
        var secret = created.GetProperty("webhook").GetProperty("secret").GetString()!;
        var encryptedToken = await TestData.ScalarAsync<string>(db, "SELECT encrypted_api_token FROM tamiza.projects");

        var raw = await client.GetStringAsync($"/api/v1/projects/{id}");

        Assert.DoesNotContain(KoboStub.Token, raw);
        Assert.DoesNotContain(secret, raw);
        Assert.DoesNotContain(encryptedToken!, raw);
        Assert.DoesNotContain("apiToken", raw);
        Assert.Contains("\"tokenUnreadable\":false", raw);
    }

    [Fact]
    public async Task Admins_rename_and_pick_the_enumerator_field_analysts_cannot()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        var analystId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, id, analystId, "analyst");
        using var analyst = TestData.ClientFor(factory, "grace", "grace@example.org");

        var forbidden = await analyst.PatchAsJsonAsync($"/api/v1/projects/{id}", new { name = "Hijacked" });
        var renamed = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { name = "Renamed", enumeratorField = "enumerator" });
        var unknown = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { enumeratorField = "nope" });
        var body = await renamed.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Renamed", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("fieldCheck").EnumerateArray().Single(m => m.GetProperty("metric").GetString() == "perEnumerator").GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.True((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("enumeratorField", out _));
    }

    [Fact]
    public async Task A_new_project_is_off_for_AI_clients_and_only_analysts_see_the_level()
    {
        var (factory, db) = await FactoryAsync(extra: new() { ["TAMIZA_MCP_MAX_DATA_ACCESS"] = "rows" });
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var created = await CreateProjectAsync(admin);
        var id = created.GetProperty("project").GetProperty("id").GetGuid();
        foreach (var (subject, role) in new[] { ("grace", "analyst"), ("vera", "viewer") })
        {
            await TestData.AddMemberAsync(db, id, await TestData.SignInAsync(factory, subject, $"{subject}@example.org"), role);
        }

        using var analyst = TestData.ClientFor(factory, "grace", "grace@example.org");
        using var viewer = TestData.ClientFor(factory, "vera", "vera@example.org");

        var asAdmin = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");
        var asAnalyst = await analyst.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");
        var asViewer = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");

        Assert.Equal("off", asAdmin.GetProperty("mcpAccess").GetString());
        Assert.Equal("rows", asAdmin.GetProperty("mcpMaxAccess").GetString());
        Assert.Equal("off", asAnalyst.GetProperty("mcpAccess").GetString());
        Assert.Equal(JsonValueKind.Null, asViewer.GetProperty("mcpAccess").ValueKind);
        Assert.Equal(JsonValueKind.Null, asViewer.GetProperty("mcpMaxAccess").ValueKind);
        Assert.Equal("off", await TestData.ScalarAsync<string>(db, "SELECT mcp_access FROM tamiza.projects WHERE id = @id", ("id", id)));
    }

    [Theory]
    [InlineData("aggregates")]
    [InlineData("rows")]
    [InlineData("Aggregates")]
    [InlineData("off")]
    public async Task An_admin_chooses_what_AI_clients_may_read(string level)
    {
        var (factory, db) = await FactoryAsync(extra: new() { ["TAMIZA_MCP_MAX_DATA_ACCESS"] = "rows" });
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();

        var response = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { mcpAccess = level });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(level.ToLowerInvariant(), body.GetProperty("mcpAccess").GetString());
        Assert.Equal(level.ToLowerInvariant(), await TestData.ScalarAsync<string>(db, "SELECT mcp_access FROM tamiza.projects WHERE id = @id", ("id", id)));
        Assert.Equal("Household survey 2026", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rows_above_the_deployments_maximum_and_unknown_levels_are_refused_and_change_nothing()
    {
        var (factory, _) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        (await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { mcpAccess = "aggregates" })).EnsureSuccessStatusCode();

        var rows = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { mcpAccess = "rows", name = "Not renamed" });
        var unknown = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { mcpAccess = "everything" });
        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, rows.StatusCode);
        var rowsError = (await rows.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("mcpAccess").EnumerateArray().Single().GetString();
        Assert.Contains("summaries only", rowsError);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.True((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("mcpAccess", out _));
        Assert.Equal("aggregates", details.GetProperty("mcpAccess").GetString());
        Assert.Equal("aggregates", details.GetProperty("mcpMaxAccess").GetString());
        Assert.Equal("Household survey 2026", details.GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_analyst_cannot_change_what_AI_clients_may_read()
    {
        var (factory, db) = await FactoryAsync(extra: new() { ["TAMIZA_MCP_MAX_DATA_ACCESS"] = "rows" });
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        await TestData.AddMemberAsync(db, id, await TestData.SignInAsync(factory, "grace", "grace@example.org"), "analyst");
        using var analyst = TestData.ClientFor(factory, "grace", "grace@example.org");

        var response = await analyst.PatchAsJsonAsync($"/api/v1/projects/{id}", new { mcpAccess = "rows" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("off", await TestData.ScalarAsync<string>(db, "SELECT mcp_access FROM tamiza.projects WHERE id = @id", ("id", id)));
    }

    [Fact]
    public async Task An_admin_sets_the_time_zone_and_every_member_sees_it()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var created = await CreateProjectAsync(admin);
        var id = created.GetProperty("project").GetProperty("id").GetGuid();
        var viewerId = await TestData.SignInAsync(factory, "linus", "linus@example.org");
        await TestData.AddMemberAsync(db, id, viewerId, "viewer");
        using var viewer = TestData.ClientFor(factory, "linus", "linus@example.org");

        var set = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { timeZone = "America/Bogota" });
        var seen = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");

        Assert.Equal("UTC", created.GetProperty("project").GetProperty("timeZone").GetString());
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("America/Bogota", (await set.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("timeZone").GetString());
        Assert.Equal("America/Bogota", seen.GetProperty("timeZone").GetString());
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task An_unknown_time_zone_is_rejected_naming_the_value_and_changes_nothing(string zone)
    {
        var (factory, _) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();

        var rejected = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { timeZone = zone });
        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var message = (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("timeZone")[0].GetString();
        Assert.Contains($"'{zone.Trim()}'", message);
        Assert.Equal("UTC", details.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task An_analyst_cannot_set_the_time_zone_and_the_openapi_document_lists_it()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        var analystId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, id, analystId, "analyst");
        using var analyst = TestData.ClientFor(factory, "grace", "grace@example.org");

        var forbidden = await analyst.PatchAsJsonAsync($"/api/v1/projects/{id}", new { timeZone = "America/Bogota" });
        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");
        var openApi = await admin.GetStringAsync("/api/v1/openapi.json");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("UTC", details.GetProperty("timeZone").GetString());
        Assert.Contains("\"timeZone\"", openApi);
    }

    // Dashboards: New project (custom-dashboards), Details list the dashboards (projects)

    [Fact]
    public async Task A_new_project_starts_with_one_dashboard_named_indicators_and_no_widgets()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var client = TestData.ClientFor(factory, "ada", "ada@example.org");

        var created = (await CreateProjectAsync(client)).GetProperty("project");
        var id = created.GetProperty("id").GetGuid();
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");

        var dashboard = Assert.Single(Dashboards(created));
        Assert.Equal("Indicators", dashboard.Name);
        Assert.Equal([dashboard], Dashboards(details));
        // Saved with the project: first, named as the migration names it, and owned by the project's creator.
        Assert.Equal($"{dashboard.Id}|Indicators|indicators|0|t", await TestData.ScalarAsync<string>(db,
            """
            SELECT string_agg(concat_ws('|', d.id, d.name, d.normalized_name, d.position, d.created_by = p.created_by AND d.updated_by = p.created_by), ',')
            FROM tamiza.dashboards AS d JOIN tamiza.projects AS p ON p.id = d.project_id
            """));
        var opened = await client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}/dashboards/{dashboard.Id}");
        Assert.Equal("Indicators", opened.GetProperty("name").GetString());
        Assert.Equal(0, opened.GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task The_details_list_the_dashboards_in_their_order_to_every_member_including_a_viewer()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var created = (await CreateProjectAsync(admin)).GetProperty("project");
        var id = created.GetProperty("id").GetGuid();
        var field = Dashboards(created).Single().Id;
        var viewerId = await TestData.SignInAsync(factory, "vic", "vic@example.org");
        await TestData.AddMemberAsync(db, id, viewerId, "viewer");
        using var viewer = TestData.ClientFor(factory, "vic", "vic@example.org");
        // "Field" is older, stored first and first by name; only its position puts it after "Health".
        await TestData.ExecuteAsync(db,
            "UPDATE tamiza.dashboards SET name = 'Field', normalized_name = 'field', position = 1 WHERE id = @id", ("id", field));
        var health = await TestData.ScalarAsync<Guid>(db,
            """
            INSERT INTO tamiza.dashboards (project_id, name, normalized_name, position, created_by, updated_by)
            SELECT id, 'Health', 'health', 0, created_by, created_by FROM tamiza.projects WHERE id = @id
            RETURNING id
            """,
            ("id", id));

        var seen = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");
        var seenByAdmin = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}");

        Assert.Equal([(health, "Health"), (field, "Field")], Dashboards(seen));
        Assert.Equal([(health, "Health"), (field, "Field")], Dashboards(seenByAdmin));
    }

    [Fact]
    public async Task The_details_a_rename_answers_still_list_the_dashboards()
    {
        var (factory, _) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var created = (await CreateProjectAsync(admin)).GetProperty("project");
        var id = created.GetProperty("id").GetGuid();
        var indicators = Dashboards(created).Single().Id;
        var added = await admin.PostAsJsonAsync($"/api/v1/projects/{id}/dashboards", new { name = "Field progress" });
        var field = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var renamed = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { name = "Renamed" });
        var body = await renamed.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Renamed", body.GetProperty("name").GetString());
        Assert.Equal([(indicators, "Indicators"), (field, "Field progress")], Dashboards(body));
    }

    // 3.4 — token replacement and re-check

    [Fact]
    public async Task A_valid_new_token_replaces_the_old_one_and_an_invalid_one_is_refused()
    {
        var (factory, _) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();

        _kobo.WithForm(token: "rotated-token");
        var replaced = await admin.PutAsJsonAsync($"/api/v1/projects/{id}/kobo-token", new { apiToken = "rotated-token" });
        var refused = await admin.PutAsJsonAsync($"/api/v1/projects/{id}/kobo-token", new { apiToken = "bad-token" });
        _kobo.Server.ResetLogEntries();
        var recheck = await admin.PostAsync($"/api/v1/projects/{id}/kobo-check", null);

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("kobo.unauthorized", await CodeAsync(refused));
        Assert.Equal(HttpStatusCode.OK, recheck.StatusCode);
        Assert.All(_kobo.Server.LogEntries, e => Assert.Equal("Token rotated-token", e.RequestMessage!.Headers!["Authorization"].Single()));
    }

    [Fact]
    public async Task Creation_and_recheck_keep_the_form_choice_lists()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();

        Assert.Equal("Cali", await TestData.ScalarAsync<string>(db,
            "SELECT c->>'label' FROM tamiza.projects, jsonb_array_elements(form_choices) c WHERE c->>'listName' = 'municipalities' AND c->>'name' = 'cali'"));
        Assert.Equal("municipalities", await TestData.ScalarAsync<string>(db,
            "SELECT f->>'listName' FROM tamiza.projects, jsonb_array_elements(form_fields) f WHERE f->>'name' = 'municipality'"));

        _kobo.WithForm(KoboStub.FormJson.Replace("\"Cali\"", "\"Santiago de Cali\""));
        (await admin.PostAsync($"/api/v1/projects/{id}/kobo-check", null)).EnsureSuccessStatusCode();

        Assert.Equal("Santiago de Cali", await TestData.ScalarAsync<string>(db,
            "SELECT c->>'label' FROM tamiza.projects, jsonb_array_elements(form_choices) c WHERE c->>'name' = 'cali'"));
    }

    [Fact]
    public async Task Recheck_refreshes_the_form_snapshot_and_viewers_cannot_recheck()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        var viewerId = await TestData.SignInAsync(factory, "vic", "vic@example.org");
        await TestData.AddMemberAsync(db, id, viewerId, "viewer");
        using var viewer = TestData.ClientFor(factory, "vic", "vic@example.org");

        _kobo.WithForm(KoboStub.FormJson.Replace("\"Household survey\"", "\"Household survey v2\"").Replace("{ \"type\": \"start\", \"name\": \"start\", \"$xpath\": \"start\" },", ""));
        var recheck = await admin.PostAsync($"/api/v1/projects/{id}/kobo-check", null);
        var body = await recheck.Content.ReadFromJsonAsync<JsonElement>();
        var forbidden = await viewer.PostAsync($"/api/v1/projects/{id}/kobo-check", null);

        Assert.Equal(HttpStatusCode.OK, recheck.StatusCode);
        Assert.Equal("Household survey v2", body.GetProperty("formName").GetString());
        var completion = body.GetProperty("fieldCheck").EnumerateArray().Single(m => m.GetProperty("metric").GetString() == "completionTime");
        Assert.False(completion.GetProperty("available").GetBoolean());
        Assert.Equal("start", completion.GetProperty("missingFields")[0].GetString());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Paths_built_from_the_group_nesting_keep_the_enumerator_question_and_the_frame_dimension_valid()
    {
        // The document has no $xpath, so the paths come from the nesting: respondent/municipality, respondent/enumerator.
        const string grouped = """
            {
              "name": "Grouped form",
              "content": {
                "survey": [
                  { "type": "start", "name": "start" },
                  { "type": "end", "name": "end" },
                  { "type": "begin_group", "name": "respondent" },
                  { "type": "select_one municipalities", "name": "municipality", "label": ["Municipality"] },
                  { "type": "text", "name": "enumerator", "label": ["Enumerator"] },
                  { "type": "end_group" }
                ],
                "choices": [{ "list_name": "municipalities", "name": "cali", "label": ["Cali"] }]
              }
            }
            """;
        _kobo.WithForm(grouped);
        var (factory, _) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        var frame = new
        {
            dimensions = new[] { new { name = "Municipality", field = "respondent/municipality" } },
            targets = new[] { new { values = new[] { "Cali" }, target = 120 } },
        };

        var enumerator = await admin.PatchAsJsonAsync($"/api/v1/projects/{id}", new { enumeratorField = "respondent/enumerator" });
        var saved = await admin.PutAsJsonAsync($"/api/v1/projects/{id}/sampling-frame", frame);
        var recheck = await admin.PostAsync($"/api/v1/projects/{id}/kobo-check", null);
        var savedAgain = await admin.PutAsJsonAsync($"/api/v1/projects/{id}/sampling-frame", frame);
        var details = await recheck.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, enumerator.StatusCode);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recheck.StatusCode);
        Assert.Equal(HttpStatusCode.OK, savedAgain.StatusCode);
        Assert.Equal("respondent/enumerator", details.GetProperty("enumeratorField").GetString());
        Assert.True(details.GetProperty("fieldCheck").EnumerateArray()
            .Single(m => m.GetProperty("metric").GetString() == "perEnumerator").GetProperty("available").GetBoolean());
    }

    // 3.5 — form fields and webhook

    [Fact]
    public async Task Form_fields_are_readable_by_analysts_and_admins_but_not_by_viewers()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        foreach (var (subject, role) in new[] { ("vic", "viewer"), ("grace", "analyst") })
        {
            await TestData.AddMemberAsync(db, id, await TestData.SignInAsync(factory, subject, $"{subject}@example.org"), role);
        }

        using var viewer = TestData.ClientFor(factory, "vic", "vic@example.org");
        using var analyst = TestData.ClientFor(factory, "grace", "grace@example.org");

        var fields = await analyst.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{id}/form-fields");
        var forbidden = await viewer.GetAsync($"/api/v1/projects/{id}/form-fields");

        Assert.Equal(["start", "end", "municipality", "sex", "enumerator"], fields!.Select(f => f.GetProperty("xpath").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Admins_see_and_regenerate_the_webhook_secret_analysts_cannot()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var created = await CreateProjectAsync(admin);
        var id = created.GetProperty("project").GetProperty("id").GetGuid();
        var analystId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, id, analystId, "analyst");
        using var analyst = TestData.ClientFor(factory, "grace", "grace@example.org");
        var cipherBefore = await TestData.ScalarAsync<string>(db, "SELECT encrypted_webhook_secret FROM tamiza.projects");

        var settings = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{id}/webhook");
        var regenerated = await (await admin.PostAsync($"/api/v1/projects/{id}/webhook/secret", null)).Content.ReadFromJsonAsync<JsonElement>();
        var forbidden = await analyst.GetAsync($"/api/v1/projects/{id}/webhook");

        Assert.Equal(created.GetProperty("webhook").GetProperty("secret").GetString(), settings.GetProperty("secret").GetString());
        Assert.Equal($"https://tamiza.test/api/v1/webhooks/kobo/{id}", settings.GetProperty("url").GetString());
        Assert.Equal("tamiza", settings.GetProperty("username").GetString());
        Assert.NotEqual(settings.GetProperty("secret").GetString(), regenerated.GetProperty("secret").GetString());
        Assert.NotEqual(cipherBefore, await TestData.ScalarAsync<string>(db, "SELECT encrypted_webhook_secret FROM tamiza.projects"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    // 3.6 — unreadable token

    [Fact]
    public async Task An_undecryptable_token_is_reported_not_thrown()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        await TestData.ExecuteAsync(db, "UPDATE tamiza.projects SET encrypted_api_token = 'corrupted'");

        var response = await admin.GetAsync($"/api/v1/projects/{id}");
        var recheck = await admin.PostAsync($"/api/v1/projects/{id}/kobo-check", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokenUnreadable").GetBoolean());
        Assert.Equal("kobo.token_unreadable", await CodeAsync(recheck));
    }

    public void Dispose() => _kobo.Dispose();
}
