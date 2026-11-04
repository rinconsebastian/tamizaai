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
        await SaveIndicatorAsync(analyst, h, "coverage");

        Assert.Equal(2, second.GetProperty("number").GetInt32());
        Assert.Equal("ana", second.GetProperty("author").GetProperty("name").GetString());
        var scripts = await analyst.GetFromJsonAsync<JsonElement>(Scripts(h));
        Assert.Equal("clean", scripts.GetProperty("clean").GetProperty("kind").GetString());
        Assert.Equal("output", scripts.GetProperty("output").GetProperty("kind").GetString());
        Assert.Equal(2, scripts.GetProperty("clean").GetProperty("current").GetProperty("number").GetInt32());
        Assert.Contains("raw['main']", scripts.GetProperty("clean").GetProperty("current").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, scripts.GetProperty("output").GetProperty("current").ValueKind);
        var indicator = Assert.Single(scripts.GetProperty("indicators").EnumerateArray());
        Assert.Equal("coverage", indicator.GetProperty("name").GetString());
        Assert.Equal(1, indicator.GetProperty("current").GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Viewers_cannot_read_or_save_scripts_analysts_read_them_and_non_members_get_404()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveAsync(h.Admin, h, "clean", Clean);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        using var viewer = await MemberAsync(h, "vera", "viewer");
        using var stranger = TestData.ClientFor(h.Factory, "sam", "sam@example.org");

        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync($"{Scripts(h)}/clean/versions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync($"{Scripts(h)}/clean/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync(Scripts(h))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Scripts(h))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{Scripts(h)}/clean/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{Scripts(h)}/clean/versions/1")).StatusCode);
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
        var never = await h.Admin.DeleteAsync($"{Scripts(h)}/indicators/coverage");

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

    // Indicator scripts

    private const string Compute = "def compute(cleaned, ctx):\n    return cleaned['main']\n";

    private static string IndicatorUrl(IngestionHarness h, string name) => $"{Scripts(h)}/indicators/{name}";

    private static async Task<JsonElement> SaveIndicatorAsync(
        HttpClient client, IngestionHarness h, string name, string? code = null, string? label = null, string format = "decimal", string? unit = null,
        string? description = null)
    {
        var response = await client.PutAsJsonAsync(IndicatorUrl(h, name), new { code = code ?? Compute, label = label ?? "Label of " + name, description, unit, format });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task An_analyst_creates_an_indicator_that_the_project_then_lists_at_version_1_with_its_metadata()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");

        var response = await analyst.PutAsJsonAsync(IndicatorUrl(h, "piped_water_rate"),
            new { code = Compute, label = "Households with piped water", description = "Share of homes with a tap", unit = "households", format = "percent" });
        var version = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/projects/{h.ProjectId}/scripts/indicators/piped_water_rate/versions/1", response.Headers.Location!.OriginalString);
        Assert.Equal(("indicator", "piped_water_rate", 1), (version.GetProperty("kind").GetString(), version.GetProperty("name").GetString(), version.GetProperty("number").GetInt32()));
        Assert.Equal("ana", version.GetProperty("author").GetProperty("name").GetString());
        var list = await analyst.GetFromJsonAsync<JsonElement>(Scripts(h));
        var indicator = Assert.Single(list.GetProperty("indicators").EnumerateArray());
        Assert.Equal(("piped_water_rate", "Households with piped water", "Share of homes with a tap", "households", "percent"),
            (indicator.GetProperty("name").GetString(), indicator.GetProperty("label").GetString(), indicator.GetProperty("description").GetString(),
                indicator.GetProperty("unit").GetString(), indicator.GetProperty("format").GetString()));
        Assert.Equal(1, indicator.GetProperty("current").GetProperty("number").GetInt32());
        Assert.Equal(Compute, indicator.GetProperty("current").GetProperty("code").GetString());
        Assert.Equal(1L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions WHERE kind = 'indicator' AND name = 'piped_water_rate'"));
    }

    [Fact]
    public async Task The_metadata_is_trimmed_empty_optional_fields_are_null_and_the_format_is_lowercased()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "coverage"),
            new { code = Compute, label = "  Coverage  ", description = "   ", unit = "", format = "Percent" });
        var version = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Coverage", version.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, version.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, version.GetProperty("unit").ValueKind);
        Assert.Equal("percent", version.GetProperty("format").GetString());
    }

    [Theory]
    [InlineData("Piped%20water")]
    [InlineData("piped-water")]
    [InlineData("Piped_water")]
    [InlineData("1st_round")]
    [InlineData("_hidden")]
    [InlineData("an_indicator_name_that_is_longer_than_sixty_three_characters_for_sure_yes")]
    public async Task An_indicator_name_that_breaks_the_table_name_rule_is_rejected_with_the_allowed_characters(string name)
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, name), new { code = Compute, label = "L", format = "count" });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("lowercase letters, digits and underscores", Errors(problem, "name")[0]);
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task A_name_of_63_characters_is_accepted()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        var name = "a" + new string('b', 62);

        var version = await SaveIndicatorAsync(h.Admin, h, name);

        Assert.Equal(name, version.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("", "count", "label")]
    [InlineData("   ", "count", "label")]
    [InlineData("L", null, "format")]
    [InlineData("L", "ratio", "format")]
    [InlineData("L", "", "format")]
    public async Task The_label_and_the_format_are_required(string label, string? format, string field)
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "coverage"), new { code = Compute, label, format });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(Errors(problem, field));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task The_metadata_has_length_limits_and_every_problem_is_reported_at_once()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var ok = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "ok"),
            new { code = Compute, label = new string('l', 120), description = new string('d', 1000), unit = new string('u', 32), format = "currency" });
        var response = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "toolong"),
            new { code = "  ", label = new string('l', 121), description = new string('d', 1001), unit = new string('u', 33), format = "count" });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["code", "description", "label", "unit"], problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task An_indicator_script_over_200_KB_is_rejected()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "coverage"),
            new { code = "# " + new string('x', 205 * 1024), label = "L", format = "count" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("200 KB", Errors(await ReadAsync(response), "code")[0]);
    }

    [Fact]
    public async Task A_project_cannot_have_more_than_100_indicators_but_can_keep_saving_the_ones_it_has()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, author_id, created_at)
            SELECT gen_random_uuid(), @p, 'indicator', 'i' || lpad(n::text, 3, '0'), 1, 'x', 'Indicator ' || n, 'count',
                   (SELECT created_by FROM tamiza.projects WHERE id = @p), now()
            FROM generate_series(1, 100) AS n
            """, ("p", h.ProjectId));

        var over = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "one_more"), new { code = Compute, label = "One more", format = "count" });
        var problem = await ReadAsync(over);
        var existing = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "i050"), new { code = Compute, label = "Changed", format = "count" });
        var removal = await h.Admin.DeleteAsync(IndicatorUrl(h, "i001"));
        var afterRemoval = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "one_more"), new { code = Compute, label = "One more", format = "count" });
        var restoreRemoved = await h.Admin.PostAsync($"{IndicatorUrl(h, "i001")}/versions/1/restore", null); // it would be the 101st again

        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Contains("at most 100 indicators", Errors(problem, "name")[0]);
        Assert.Equal(HttpStatusCode.Created, existing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        Assert.Equal(HttpStatusCode.Created, afterRemoval.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, restoreRemoved.StatusCode);
        var list = await h.Admin.GetFromJsonAsync<JsonElement>(Scripts(h));
        Assert.Equal(100, list.GetProperty("indicators").GetArrayLength());
    }

    [Fact]
    public async Task The_metadata_comes_back_with_the_code_when_an_older_version_is_restored()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveIndicatorAsync(h.Admin, h, "coverage", "# one\n" + Compute, label: "Piped water", format: "percent", unit: "households", description: "First definition");
        await SaveIndicatorAsync(h.Admin, h, "coverage", "# two\n" + Compute, label: "Water coverage", format: "count", unit: null, description: null);

        var restore = await h.Admin.PostAsync($"{IndicatorUrl(h, "coverage")}/versions/1/restore", null);
        var restored = await ReadAsync(restore);

        Assert.Equal(HttpStatusCode.Created, restore.StatusCode);
        Assert.Equal($"/api/v1/projects/{h.ProjectId}/scripts/indicators/coverage/versions/3", restore.Headers.Location!.OriginalString);
        Assert.Equal(3, restored.GetProperty("number").GetInt32());
        Assert.Equal("# one\n" + Compute, restored.GetProperty("code").GetString());
        Assert.Equal(("Piped water", "First definition", "households", "percent"),
            (restored.GetProperty("label").GetString(), restored.GetProperty("description").GetString(), restored.GetProperty("unit").GetString(), restored.GetProperty("format").GetString()));
        var current = Assert.Single((await h.Admin.GetFromJsonAsync<JsonElement>(Scripts(h))).GetProperty("indicators").EnumerateArray());
        Assert.Equal(("Piped water", "percent", 3), (current.GetProperty("label").GetString(), current.GetProperty("format").GetString(), current.GetProperty("current").GetProperty("number").GetInt32()));
    }

    [Fact]
    public async Task Each_version_keeps_the_metadata_it_was_saved_with()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveIndicatorAsync(h.Admin, h, "coverage", label: "Old label", format: "decimal");
        await SaveIndicatorAsync(h.Admin, h, "coverage", label: "New label", format: "percent");

        var first = await h.Admin.GetFromJsonAsync<JsonElement>($"{IndicatorUrl(h, "coverage")}/versions/1");
        var second = await h.Admin.GetFromJsonAsync<JsonElement>($"{IndicatorUrl(h, "coverage")}/versions/2");
        var summaries = await h.Admin.GetFromJsonAsync<JsonElement>($"{IndicatorUrl(h, "coverage")}/versions");

        Assert.Equal(("Old label", "decimal"), (first.GetProperty("label").GetString(), first.GetProperty("format").GetString()));
        Assert.Equal(("New label", "percent"), (second.GetProperty("label").GetString(), second.GetProperty("format").GetString()));
        Assert.Equal(["New label", "Old label"], summaries.EnumerateArray().Select(v => v.GetProperty("label").GetString()));
        Assert.False(summaries[0].TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Indicator_versions_are_numbered_per_indicator_and_apart_from_scripts_1_and_2()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        await SaveAsync(h.Admin, h, "clean", Clean);
        await SaveIndicatorAsync(h.Admin, h, "coverage");
        await SaveIndicatorAsync(h.Admin, h, "coverage");
        var third = await SaveIndicatorAsync(h.Admin, h, "coverage");
        var attendance = await SaveIndicatorAsync(h.Admin, h, "attendance");
        var namedClean = await SaveIndicatorAsync(h.Admin, h, "clean"); // a name is not a kind

        Assert.Equal(3, third.GetProperty("number").GetInt32());
        Assert.Equal(1, attendance.GetProperty("number").GetInt32());
        Assert.Equal(1, namedClean.GetProperty("number").GetInt32());
        var list = await h.Admin.GetFromJsonAsync<JsonElement>(Scripts(h));
        Assert.Equal(["attendance", "clean", "coverage"], list.GetProperty("indicators").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal([1, 1, 3], list.GetProperty("indicators").EnumerateArray().Select(i => i.GetProperty("current").GetProperty("number").GetInt32()));
        Assert.Equal(1, list.GetProperty("clean").GetProperty("current").GetProperty("number").GetInt32());
        var coverage = await h.Admin.GetFromJsonAsync<JsonElement>($"{IndicatorUrl(h, "coverage")}/versions");
        var attendanceVersions = await h.Admin.GetFromJsonAsync<JsonElement>($"{IndicatorUrl(h, "attendance")}/versions");
        Assert.Equal([3, 2, 1], coverage.EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
        Assert.Equal([1], attendanceVersions.EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
        Assert.Equal(0, (await h.Admin.GetFromJsonAsync<JsonElement>($"{IndicatorUrl(h, "unknown")}/versions")).GetArrayLength());
    }

    [Fact]
    public async Task Saves_of_the_same_indicator_made_at_the_same_moment_get_different_numbers()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(i =>
            h.Admin.PutAsJsonAsync(IndicatorUrl(h, "coverage"), new { code = Compute + "# " + i, label = "L", format = "count" })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numbers = await TestData.ScalarAsync<string>(h.Db, "SELECT string_agg(number::text, ',' ORDER BY number) FROM tamiza.script_versions WHERE name = 'coverage'");
        Assert.Equal("1,2,3", numbers);
    }

    [Fact]
    public async Task A_viewer_cannot_read_save_remove_or_restore_indicator_scripts_and_an_analyst_reads_them()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveIndicatorAsync(h.Admin, h, "coverage");
        using var analyst = await MemberAsync(h, "ana", "analyst");
        using var viewer = await MemberAsync(h, "vera", "viewer");

        var save = await viewer.PutAsJsonAsync(IndicatorUrl(h, "coverage"), new { code = Compute, label = "L", format = "count" });
        var create = await viewer.PutAsJsonAsync(IndicatorUrl(h, "attendance"), new { code = Compute, label = "L", format = "count" });
        var remove = await viewer.DeleteAsync(IndicatorUrl(h, "coverage"));
        var restore = await viewer.PostAsync($"{IndicatorUrl(h, "coverage")}/versions/1/restore", null);

        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden],
            [save.StatusCode, create.StatusCode, remove.StatusCode, restore.StatusCode]);
        Assert.Equal(1L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
        Assert.Equal(1, (await analyst.GetFromJsonAsync<JsonElement>(Scripts(h))).GetProperty("indicators").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync($"{IndicatorUrl(h, "coverage")}/versions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync($"{IndicatorUrl(h, "coverage")}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Scripts(h))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{IndicatorUrl(h, "coverage")}/versions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{IndicatorUrl(h, "coverage")}/versions")).StatusCode);
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_404_on_every_indicator_script_route()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveIndicatorAsync(h.Admin, h, "coverage");
        using var stranger = TestData.ClientFor(h.Factory, "sam", "sam@example.org");
        var body = new { code = Compute, label = "L", format = "count" };

        var responses = new[]
        {
            await stranger.PutAsJsonAsync(IndicatorUrl(h, "coverage"), body),
            await stranger.DeleteAsync(IndicatorUrl(h, "coverage")),
            await stranger.GetAsync($"{IndicatorUrl(h, "coverage")}/versions"),
            await stranger.GetAsync($"{IndicatorUrl(h, "coverage")}/versions/1"),
            await stranger.PostAsync($"{IndicatorUrl(h, "coverage")}/versions/1/restore", null),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    [Fact]
    public async Task Removing_an_indicator_creates_a_removed_version_and_it_leaves_the_list_but_keeps_its_history()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveIndicatorAsync(h.Admin, h, "coverage");
        await SaveIndicatorAsync(h.Admin, h, "attendance");

        var removed = await h.Admin.DeleteAsync(IndicatorUrl(h, "coverage"));
        var again = await h.Admin.DeleteAsync(IndicatorUrl(h, "coverage"));
        var unknown = await h.Admin.DeleteAsync(IndicatorUrl(h, "unknown"));

        var body = await ReadAsync(removed);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.True(body.GetProperty("removed").GetBoolean());
        Assert.Equal(("coverage", 2), (body.GetProperty("name").GetString(), body.GetProperty("number").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("script.not_set", (await ReadAsync(again)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);
        var list = await h.Admin.GetFromJsonAsync<JsonElement>(Scripts(h));
        Assert.Equal(["attendance"], list.GetProperty("indicators").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        var versions = await h.Admin.GetFromJsonAsync<JsonElement>($"{IndicatorUrl(h, "coverage")}/versions");
        Assert.Equal([2, 1], versions.EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
        Assert.True(versions[0].GetProperty("removed").GetBoolean());
        Assert.True(await TestData.ScalarAsync<bool>(h.Db, "SELECT code IS NULL AND label IS NULL AND format IS NULL FROM tamiza.script_versions WHERE name = 'coverage' AND number = 2"));
    }

    [Fact]
    public async Task A_removed_indicator_comes_back_by_restoring_one_of_its_versions_with_code()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SaveIndicatorAsync(h.Admin, h, "coverage", label: "Coverage", format: "percent");
        await h.Admin.DeleteAsync(IndicatorUrl(h, "coverage"));

        var removedVersion = await h.Admin.PostAsync($"{IndicatorUrl(h, "coverage")}/versions/2/restore", null);
        var missingVersion = await h.Admin.PostAsync($"{IndicatorUrl(h, "coverage")}/versions/9/restore", null);
        var unknownIndicator = await h.Admin.PostAsync($"{IndicatorUrl(h, "unknown")}/versions/1/restore", null);
        var restored = await h.Admin.PostAsync($"{IndicatorUrl(h, "coverage")}/versions/1/restore", null);
        var list = await h.Admin.GetFromJsonAsync<JsonElement>(Scripts(h));

        Assert.Equal(HttpStatusCode.BadRequest, removedVersion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingVersion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownIndicator.StatusCode);
        Assert.Equal(HttpStatusCode.Created, restored.StatusCode);
        Assert.Equal(3, (await ReadAsync(restored)).GetProperty("number").GetInt32());
        Assert.Equal(["coverage"], list.GetProperty("indicators").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
    }

    [Theory]
    [InlineData("PUT", "indicators")]
    [InlineData("DELETE", "indicators")]
    [InlineData("GET", "indicators/versions")]
    [InlineData("PUT", "indicator")]
    public async Task Indicators_are_not_a_kind_of_the_two_fixed_script_routes(string method, string path)
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"{Scripts(h)}/{path}")
        {
            Content = JsonContent.Create(new { code = Compute, label = "L", format = "count" }),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task The_openapi_document_lists_the_indicator_script_routes()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var paths = document.GetProperty("paths");
        var indicator = paths.GetProperty("/api/v1/projects/{projectId}/scripts/indicators/{name}");
        Assert.Equal("SaveIndicatorScript", indicator.GetProperty("put").GetProperty("operationId").GetString());
        Assert.Equal("RemoveIndicatorScript", indicator.GetProperty("delete").GetProperty("operationId").GetString());
        Assert.Equal("ListIndicatorScriptVersions",
            paths.GetProperty("/api/v1/projects/{projectId}/scripts/indicators/{name}/versions").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("GetIndicatorScriptVersion",
            paths.GetProperty("/api/v1/projects/{projectId}/scripts/indicators/{name}/versions/{number}").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("RestoreIndicatorScriptVersion",
            paths.GetProperty("/api/v1/projects/{projectId}/scripts/indicators/{name}/versions/{number}/restore").GetProperty("post").GetProperty("operationId").GetString());
    }

    // Column labels of an indicator (design D3 and D4 of the indicator column metadata)

    private static Task<HttpResponseMessage> PutColumnsAsync(
        HttpClient client, IngestionHarness h, string name, object? columns, string code = Compute, string label = "Teatro hoy", string format = "percent") =>
        client.PutAsJsonAsync(IndicatorUrl(h, name), new { code, label, format, columns });

    /// <summary>A save that sends no <c>columns</c> member at all, as a client that does not know them does.</summary>
    private static Task<HttpResponseMessage> PutWithoutColumnsAsync(HttpClient client, IngestionHarness h, string name, string code = Compute) =>
        client.PutAsJsonAsync(IndicatorUrl(h, name), new { code, label = "Teatro hoy", format = "percent" });

    private static Task<string?> StoredColumnsAsync(IngestionHarness h, string name, int number) =>
        TestData.ScalarAsync<string?>(h.Db, $"SELECT columns::text FROM tamiza.script_versions WHERE name = '{name}' AND number = {number}")!;

    /// <summary>The labels of a <c>columns</c> member as column to label, in any order; null when the member is null or absent.</summary>
    private static Dictionary<string, string?>? LabelsOf(JsonElement version)
    {
        if (!version.TryGetProperty("columns", out var columns) || columns.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return columns.EnumerateObject().ToDictionary(c => c.Name, c => c.Value.TryGetProperty("label", out var label) ? label.GetString() : null);
    }

    [Fact]
    public async Task Labels_of_a_ratio_are_saved_listed_and_returned_with_the_version()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await PutColumnsAsync(h.Admin, h, "teatro_hoy", new
        {
            numerator = new { label = "Hogares que asistieron" },
            denominator = new { label = "Hogares" },
        });
        var saved = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var expected = new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron", ["denominator"] = "Hogares" };
        // Every path that projects a version gives them: the save's answer, the list (indicator and current), a version's details, and the overview.
        Assert.Equal(expected, LabelsOf(saved));
        var list = await h.Admin.GetFromJsonAsync<JsonElement>(Scripts(h));
        var indicator = Assert.Single(list.GetProperty("indicators").EnumerateArray());
        Assert.Equal(expected, LabelsOf(indicator));
        Assert.Equal(expected, LabelsOf(indicator.GetProperty("current")));
        Assert.Equal(expected, LabelsOf(await h.Admin.GetFromJsonAsync<JsonElement>($"{IndicatorUrl(h, "teatro_hoy")}/versions/1")));
        var overview = await h.Admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{h.ProjectId}/indicators");
        Assert.Equal(expected, LabelsOf(Assert.Single(overview.GetProperty("indicators").EnumerateArray())));
        Assert.False(LabelsOf(saved)!.ContainsKey("comuna"));
    }

    [Fact]
    public async Task A_label_is_trimmed_and_an_empty_one_is_no_label()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var saved = await ReadAsync(await PutColumnsAsync(h.Admin, h, "teatro_hoy", new
        {
            comuna = new { label = "  Comuna  " },
            estrato = new { label = "   " },
            barrio = new { label = (string?)null },
            sector = (object?)null,
        }));

        Assert.Equal(new Dictionary<string, string?> { ["comuna"] = "Comuna" }, LabelsOf(saved));
        Assert.Equal("""{"comuna": {"label": "Comuna"}}""", await StoredColumnsAsync(h, "teatro_hoy", 1));
    }

    [Fact]
    public async Task A_label_cleared_leaves_the_column_with_none()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { comuna = new { label = "Comuna" }, numerator = new { label = "Hogares" } });

        var saved = await ReadAsync(await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = "Hogares" }, comuna = new { label = "" } }));

        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares" }, LabelsOf(saved));
    }

    [Fact]
    public async Task A_label_can_be_saved_before_the_indicator_has_ever_published()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var saved = await ReadAsync(await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { grupo_edad = new { label = "Grupo de edad" } }));
        var overview = await h.Admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{h.ProjectId}/indicators");

        Assert.Equal(new Dictionary<string, string?> { ["grupo_edad"] = "Grupo de edad" }, LabelsOf(saved));
        var entry = Assert.Single(overview.GetProperty("indicators").EnumerateArray());
        Assert.False(entry.GetProperty("published").GetBoolean());
        Assert.Equal(new Dictionary<string, string?> { ["grupo_edad"] = "Grupo de edad" }, LabelsOf(entry));
    }

    [Fact]
    public async Task An_indicator_without_labels_has_columns_null()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var saved = await ReadAsync(await PutWithoutColumnsAsync(h.Admin, h, "teatro_hoy"));
        var list = await h.Admin.GetFromJsonAsync<JsonElement>(Scripts(h));

        Assert.Equal(JsonValueKind.Null, saved.GetProperty("columns").ValueKind);
        Assert.Equal(JsonValueKind.Null, Assert.Single(list.GetProperty("indicators").EnumerateArray()).GetProperty("columns").ValueKind);
        Assert.Null(await StoredColumnsAsync(h, "teatro_hoy", 1));
    }

    [Fact]
    public async Task A_label_of_81_characters_is_refused_on_its_column_and_no_version_is_created()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = new string('a', 81) } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A column label can have up to 80 characters."], Errors(await ReadAsync(response), "columns.numerator.label"));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
        Assert.Equal(HttpStatusCode.Created, (await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = new string('a', 80) } })).StatusCode);
    }

    [Fact]
    public async Task A_key_that_is_not_a_columns_name_is_refused_on_the_columns()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await PutColumnsAsync(h.Admin, h, "teatro_hoy", new Dictionary<string, object> { ["Hogar Menores"] = new { label = "Hogares con menores" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync(response);
        Assert.Equal(["columns"], problem.GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.StartsWith("'Hogar Menores' is not a column's name", Assert.Single(Errors(problem, "columns")));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Theory]
    [InlineData("Hogares\nque asistieron")]
    [InlineData("Hogares\tque asistieron")]
    [InlineData("Hogares\u0000")]
    public async Task A_label_with_a_control_character_is_refused_on_its_column(string label)
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A column label cannot hold a line break or another control character."], Errors(await ReadAsync(response), "columns.numerator.label"));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task Labels_for_101_columns_are_refused_and_100_are_accepted()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        Dictionary<string, object> Columns(int count) =>
            Enumerable.Range(1, count).ToDictionary(i => $"columna_{i}", i => (object)new { label = $"Columna {i}" });

        var many = await PutColumnsAsync(h.Admin, h, "teatro_hoy", Columns(101));
        var enough = await PutColumnsAsync(h.Admin, h, "teatro_hoy", Columns(100));

        Assert.Equal(HttpStatusCode.BadRequest, many.StatusCode);
        Assert.Equal(["An indicator labels at most 100 columns."], Errors(await ReadAsync(many), "columns"));
        Assert.Equal(HttpStatusCode.Created, enough.StatusCode);
        Assert.Equal(1L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task A_property_the_api_does_not_know_is_ignored_and_not_refused()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = "Hogares", colour = "red" }, denominator = new { colour = "blue" } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares" }, LabelsOf(await ReadAsync(response)));
        Assert.Equal("""{"numerator": {"label": "Hogares"}}""", await StoredColumnsAsync(h, "teatro_hoy", 1));
    }

    [Fact]
    public async Task Every_problem_of_one_save_is_reported_at_once_with_the_label_and_format_errors()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.PutAsJsonAsync(IndicatorUrl(h, "teatro_hoy"), new
        {
            code = Compute,
            label = new string('l', 121),
            format = "ratio",
            columns = new Dictionary<string, object>
            {
                ["numerator"] = new { label = new string('a', 81) },
                ["Hogar Menores"] = new { label = "Hogares" },
                ["comuna"] = new { label = "Co\nmuna" },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["columns", "columns.comuna.label", "columns.numerator.label", "format", "label"],
            (await ReadAsync(response)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task Code_saved_without_labels_keeps_those_of_the_current_version_as_they_are_stored()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = "Hogares que asistieron" } });
        // A property of a later release, written into the stored text: a save that leaves the labels out moves the text whole.
        await TestData.ExecuteAsync(h.Db, """
            UPDATE tamiza.script_versions SET columns = '{"numerator": {"label": "Hogares que asistieron", "order": ["a", "b"]}}' WHERE number = 1
            """);

        var second = await ReadAsync(await PutWithoutColumnsAsync(h.Admin, h, "teatro_hoy", "# two\n" + Compute));
        var third = await ReadAsync(await PutColumnsAsync(h.Admin, h, "teatro_hoy", null, "# three\n" + Compute));

        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(second));
        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(third));
        Assert.Equal(await StoredColumnsAsync(h, "teatro_hoy", 1), await StoredColumnsAsync(h, "teatro_hoy", 2));
        Assert.Equal(await StoredColumnsAsync(h, "teatro_hoy", 1), await StoredColumnsAsync(h, "teatro_hoy", 3));
        Assert.Contains("\"order\"", await StoredColumnsAsync(h, "teatro_hoy", 3));
    }

    [Fact]
    public async Task Labels_given_replace_all_the_labels_of_the_current_version()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = "Hogares que asistieron" }, denominator = new { label = "Hogares" } });

        var second = await ReadAsync(await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { comuna = new { label = "Comuna" } }));

        Assert.Equal(new Dictionary<string, string?> { ["comuna"] = "Comuna" }, LabelsOf(second));
        Assert.Equal("""{"comuna": {"label": "Comuna"}}""", await StoredColumnsAsync(h, "teatro_hoy", 2));
    }

    [Fact]
    public async Task An_empty_set_of_labels_clears_them_and_stores_null()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = "Hogares" } });

        var second = await ReadAsync(await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { }));
        var third = await ReadAsync(await PutWithoutColumnsAsync(h.Admin, h, "teatro_hoy", "# three\n" + Compute));

        Assert.Null(LabelsOf(second));
        Assert.Null(LabelsOf(third));
        Assert.Null(await StoredColumnsAsync(h, "teatro_hoy", 2));
        Assert.Null(await StoredColumnsAsync(h, "teatro_hoy", 3));
    }

    [Fact]
    public async Task A_removed_indicator_saved_again_has_no_labels_and_restoring_a_version_before_the_removal_brings_them_back()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = "Hogares que asistieron" } });
        var removed = await h.Admin.DeleteAsync(IndicatorUrl(h, "teatro_hoy"));

        var again = await ReadAsync(await PutWithoutColumnsAsync(h.Admin, h, "teatro_hoy", "# again\n" + Compute));
        var restored = await ReadAsync(await h.Admin.PostAsync($"{IndicatorUrl(h, "teatro_hoy")}/versions/1/restore", null));

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Null(await StoredColumnsAsync(h, "teatro_hoy", 2));
        Assert.Null(LabelsOf(again));
        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(restored));
    }

    [Fact]
    public async Task Column_labels_come_back_with_the_version_that_is_restored_and_a_property_this_release_does_not_know_is_kept()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = "Hogares que asistieron" } });
        await TestData.ExecuteAsync(h.Db, """
            UPDATE tamiza.script_versions SET columns = '{"numerator": {"label": "Hogares que asistieron", "order": ["a", "b"]}}' WHERE number = 1
            """);
        await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { }, "# three\n" + Compute);

        var restore = await h.Admin.PostAsync($"{IndicatorUrl(h, "teatro_hoy")}/versions/1/restore", null);
        var restored = await ReadAsync(restore);

        Assert.Equal(HttpStatusCode.Created, restore.StatusCode);
        Assert.Equal(3, restored.GetProperty("number").GetInt32());
        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(restored));
        Assert.Equal(await StoredColumnsAsync(h, "teatro_hoy", 1), await StoredColumnsAsync(h, "teatro_hoy", 3));
        Assert.Contains("\"order\"", await StoredColumnsAsync(h, "teatro_hoy", 3));
    }

    [Fact]
    public async Task Column_labels_are_kept_apart_per_indicator_and_script_1_and_2_hold_none()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await PutColumnsAsync(h.Admin, h, "teatro_hoy", new { numerator = new { label = "Hogares" } });
        await SaveAsync(h.Admin, h, "clean", Clean);

        var other = await ReadAsync(await PutWithoutColumnsAsync(h.Admin, h, "teatro_barreras"));

        Assert.Null(LabelsOf(other));
        Assert.Equal(1L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions WHERE columns IS NOT NULL"));
        Assert.Null(await TestData.ScalarAsync<string?>(h.Db, "SELECT columns::text FROM tamiza.script_versions WHERE kind = 'clean'")!);
    }

    [Fact]
    public async Task The_openapi_document_describes_the_column_labels_of_a_save()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("SaveIndicatorRequest").GetProperty("properties").TryGetProperty("columns", out _));
        Assert.True(schemas.GetProperty("IndicatorColumnMetadata").GetProperty("properties").TryGetProperty("label", out _));
    }

    public void Dispose() => _kobo.Dispose();
}
