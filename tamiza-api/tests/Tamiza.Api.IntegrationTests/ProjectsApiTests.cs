using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectsApiTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private async Task<(TamizaApiFactory Factory, string Db)> FactoryAsync(bool allowPrivate = true, Dictionary<string, string?>? extra = null)
    {
        var db = await postgres.CreateDatabaseAsync();
        var settings = new Dictionary<string, string?> { ["TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS"] = allowPrivate ? "true" : "false" };
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

    // 3.3 — list, details, update

    [Fact]
    public async Task The_list_shows_own_projects_and_superadmins_see_all()
    {
        var (factory, _) = await FactoryAsync(extra: new() { ["TAMIZA_SUPERADMIN_EMAILS"] = "root@example.org" });
        await using var owner = factory;
        using var ada = TestData.ClientFor(factory, "ada", "ada@example.org");
        using var grace = TestData.ClientFor(factory, "grace", "grace@example.org");
        using var root = TestData.ClientFor(factory, "root", "root@example.org");
        await CreateProjectAsync(ada);
        await CreateProjectAsync(grace);

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

    // 3.5 — form fields and webhook

    [Fact]
    public async Task Form_fields_are_readable_by_viewers()
    {
        var (factory, db) = await FactoryAsync();
        await using var owner = factory;
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var id = (await CreateProjectAsync(admin)).GetProperty("project").GetProperty("id").GetGuid();
        var viewerId = await TestData.SignInAsync(factory, "vic", "vic@example.org");
        await TestData.AddMemberAsync(db, id, viewerId, "viewer");
        using var viewer = TestData.ClientFor(factory, "vic", "vic@example.org");

        var fields = await viewer.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{id}/form-fields");

        Assert.Equal(["start", "end", "municipality", "sex", "enumerator"], fields!.Select(f => f.GetProperty("xpath").GetString()));
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
