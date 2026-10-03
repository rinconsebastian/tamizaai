using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Pipeline;

[Collection(PostgresCollection.Name)]
public sealed class ScriptsApiTests(PostgresFixture postgres) : IDisposable
{
    private const string Clean = "def clean(raw, ctx):\n    return raw\n";

    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static string Scripts(IngestionHarness h) => $"/api/v1/projects/{h.ProjectId}/scripts";

    private static async Task<HttpClient> MemberAsync(IngestionHarness harness, string subject, string role)
    {
        var userId = await TestData.SignInAsync(harness.Factory, subject, $"{subject}@example.org");
        await TestData.AddMemberAsync(harness.Db, harness.ProjectId, userId, role);
        return TestData.ClientFor(harness.Factory, subject, $"{subject}@example.org");
    }

    private static async Task<JsonElement> SaveAsync(HttpClient client, IngestionHarness h, string kind, string code)
    {
        var response = await client.PutAsJsonAsync($"{Scripts(h)}/{kind}", new { code });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Every_save_creates_a_numbered_version_and_the_latest_is_current()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");

        await SaveAsync(analyst, h, "clean", Clean);
        var second = await SaveAsync(analyst, h, "clean", Clean.Replace("raw\n", "{'main': raw['main']}\n"));
        await SaveAsync(analyst, h, "indicators", "def compute_indicators(cleaned, ctx):\n    pass\n");

        Assert.Equal(2, second.GetProperty("number").GetInt32());
        Assert.Equal("ana", second.GetProperty("author").GetProperty("name").GetString());
        var scripts = await analyst.GetFromJsonAsync<JsonElement>(Scripts(h));
        Assert.Equal(["clean", "output", "indicators"], scripts.EnumerateArray().Select(s => s.GetProperty("kind").GetString()));
        Assert.Equal(2, scripts[0].GetProperty("current").GetProperty("number").GetInt32());
        Assert.Contains("raw['main']", scripts[0].GetProperty("current").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, scripts[1].GetProperty("current").ValueKind);
        Assert.Equal(1, scripts[2].GetProperty("current").GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Viewers_read_scripts_but_cannot_save_and_non_members_get_404()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveAsync(h.Admin, h, "clean", Clean);
        using var viewer = await MemberAsync(h, "vera", "viewer");
        using var stranger = TestData.ClientFor(h.Factory, "sam", "sam@example.org");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Scripts(h)}/clean/versions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"{Scripts(h)}/clean", new { code = Clean })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"{Scripts(h)}/clean")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Scripts(h))).StatusCode);
        Assert.Equal(1L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task Removing_creates_a_removed_version_and_cannot_be_repeated()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveAsync(h.Admin, h, "output", "def prepare_output(cleaned, ctx):\n    return cleaned\n");

        var removed = await h.Admin.DeleteAsync($"{Scripts(h)}/output");
        var again = await h.Admin.DeleteAsync($"{Scripts(h)}/output");
        var never = await h.Admin.DeleteAsync($"{Scripts(h)}/indicators");

        var body = await removed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.True(body.GetProperty("removed").GetBoolean());
        Assert.Equal(2, body.GetProperty("number").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("script.not_set", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, never.StatusCode);
        Assert.True(await TestData.ScalarAsync<bool>(h.Db, "SELECT code IS NULL FROM tamiza.script_versions WHERE number = 2"));
    }

    [Fact]
    public async Task Restoring_copies_an_older_version_into_a_new_one()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveAsync(h.Admin, h, "clean", "# version one\n" + Clean);
        await SaveAsync(h.Admin, h, "clean", "# version two\n" + Clean);
        await h.Admin.DeleteAsync($"{Scripts(h)}/clean");

        var restored = await h.Admin.PostAsync($"{Scripts(h)}/clean/versions/1/restore", null);
        var removedOne = await h.Admin.PostAsync($"{Scripts(h)}/clean/versions/3/restore", null);
        var missing = await h.Admin.PostAsync($"{Scripts(h)}/clean/versions/9/restore", null);

        var body = await restored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, restored.StatusCode);
        Assert.Equal(4, body.GetProperty("number").GetInt32());
        Assert.StartsWith("# version one", body.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, removedOne.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var versions = await h.Admin.GetFromJsonAsync<JsonElement>($"{Scripts(h)}/clean/versions");
        Assert.Equal([4, 3, 2, 1], versions.EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
        Assert.False(versions[0].TryGetProperty("code", out _));
        Assert.True(versions[1].GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task Empty_oversized_and_unknown_kinds_are_rejected()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var empty = await h.Admin.PutAsJsonAsync($"{Scripts(h)}/clean", new { code = "  " });
        var big = await h.Admin.PutAsJsonAsync($"{Scripts(h)}/clean", new { code = "# " + new string('x', 205 * 1024) });
        var kind = await h.Admin.PutAsJsonAsync($"{Scripts(h)}/everything", new { code = Clean });
        var numeric = await h.Admin.GetAsync($"{Scripts(h)}/1/versions");

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        Assert.Contains("200 KB", (await big.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("code")[0].GetString());
        Assert.Equal(HttpStatusCode.NotFound, kind.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, numeric.StatusCode);
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    public void Dispose() => _kobo.Dispose();
}
