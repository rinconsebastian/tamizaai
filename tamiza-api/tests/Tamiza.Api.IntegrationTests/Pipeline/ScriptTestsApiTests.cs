using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Pipeline;

[Collection(PostgresCollection.Name)]
public sealed class ScriptTestsApiTests(PostgresFixture postgres) : IDisposable
{
    private const string Clean = "def clean(raw, ctx):\n    return raw\n";

    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static string Tests(IngestionHarness h) => $"/api/v1/projects/{h.ProjectId}/script-tests";

    private static async Task<HttpClient> MemberAsync(IngestionHarness harness, string subject, string role)
    {
        var userId = await TestData.SignInAsync(harness.Factory, subject, $"{subject}@example.org");
        await TestData.AddMemberAsync(harness.Db, harness.ProjectId, userId, role);
        return TestData.ClientFor(harness.Factory, subject, $"{subject}@example.org");
    }

    private static async Task<JsonElement> StartAsync(HttpClient client, IngestionHarness h, object? body = null)
    {
        var response = await client.PostAsJsonAsync(Tests(h), body ?? new { kind = "clean", code = Clean });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Id(JsonElement test) => test.GetProperty("id").GetString()!;

    [Fact]
    public async Task An_analyst_queues_a_test_without_creating_a_version_or_a_run()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");

        var test = await StartAsync(analyst, h);

        Assert.Equal("queued", test.GetProperty("status").GetString());
        Assert.Equal("clean", test.GetProperty("kind").GetString());
        Assert.Equal(500, test.GetProperty("sampleSize").GetInt32());
        Assert.Equal(Clean, test.GetProperty("code").GetString());
        Assert.Empty(test.GetProperty("steps").EnumerateArray());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.runs WHERE project_id = @p", ("p", h.ProjectId)));
        var read = await analyst.GetFromJsonAsync<JsonElement>($"{Tests(h)}/{Id(test)}");
        Assert.Equal(Id(test), Id(read));
    }

    [Fact]
    public async Task A_viewer_cannot_start_or_read_tests()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var viewer = await MemberAsync(h, "vic", "viewer");

        var response = await viewer.PostAsJsonAsync(Tests(h), new { kind = "clean", code = Clean });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));
    }

    [Theory]
    [InlineData("clean", "x = 1", 10000, "sampleSize")]
    [InlineData("clean", "x = 1", 0, "sampleSize")]
    [InlineData("cleanup", "x = 1", 500, "kind")]
    [InlineData("1", "x = 1", 500, "kind")]
    [InlineData("clean", "   ", 500, "code")]
    public async Task Invalid_requests_queue_nothing(string kind, string code, int sampleSize, string field)
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.PostAsJsonAsync(Tests(h), new { kind, code, sampleSize });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));
    }

    [Fact]
    public async Task Code_over_200_KB_is_rejected()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.PostAsJsonAsync(Tests(h), new { kind = "clean", code = new string('#', 200 * 1024 + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_new_test_replaces_the_users_queued_test()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        var other = await StartAsync(h.Admin, h);
        var first = await StartAsync(analyst, h);

        var second = await StartAsync(analyst, h, new { kind = "output", code = "def prepare_output(cleaned, ctx):\n    return cleaned\n", sampleSize = 50 });

        Assert.Equal(HttpStatusCode.NotFound, (await analyst.GetAsync($"{Tests(h)}/{Id(first)}")).StatusCode);
        Assert.Equal(50, (await analyst.GetFromJsonAsync<JsonElement>($"{Tests(h)}/{Id(second)}")).GetProperty("sampleSize").GetInt32());
        // Another user's queued test is kept.
        Assert.Equal(HttpStatusCode.OK, (await h.Admin.GetAsync($"{Tests(h)}/{Id(other)}")).StatusCode);
        Assert.Equal(2, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));
    }

    [Fact]
    public async Task A_running_test_is_not_replaced()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        var first = await StartAsync(h.Admin, h);
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_tests SET status = 'running', started_at = now()");

        await StartAsync(h.Admin, h);

        var running = await h.Admin.GetFromJsonAsync<JsonElement>($"{Tests(h)}/{Id(first)}");
        Assert.Equal("running", running.GetProperty("status").GetString());
        Assert.Equal(2, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));
    }

    [Fact]
    public async Task Another_members_test_is_not_found()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        var test = await StartAsync(analyst, h);

        Assert.Equal(HttpStatusCode.NotFound, (await h.Admin.GetAsync($"{Tests(h)}/{Id(test)}")).StatusCode);
    }

    [Fact]
    public async Task A_test_that_ended_more_than_24_hours_ago_is_not_found()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        var test = await StartAsync(h.Admin, h);
        var url = $"{Tests(h)}/{Id(test)}";

        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_tests SET status = 'succeeded', finished_at = now() - interval '23 hours'");
        Assert.Equal(HttpStatusCode.OK, (await h.Admin.GetAsync(url)).StatusCode);

        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_tests SET finished_at = now() - interval '25 hours'");
        Assert.Equal(HttpStatusCode.NotFound, (await h.Admin.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task A_queued_test_says_what_it_waits_for()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        var earlier = await StartAsync(h.Admin, h);
        var test = await StartAsync(analyst, h);
        var url = $"{Tests(h)}/{Id(test)}";
        async Task<string?> WaitingFor() => (await analyst.GetFromJsonAsync<JsonElement>(url)).GetProperty("waitingFor").GetString();

        Assert.Equal("test", await WaitingFor());

        await TestData.ExecuteAsync(h.Db, "INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at) VALUES (gen_random_uuid(), @p, 'manual', 'running', now(), now())",
            ("p", h.ProjectId));
        Assert.Equal("run", await WaitingFor());

        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.runs SET status = 'succeeded', finished_at = now()");
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_tests SET status = 'succeeded', finished_at = now() WHERE id = @id",
            ("id", Guid.Parse(Id(earlier))));
        Assert.Null(await WaitingFor());
    }

    [Fact]
    public async Task Returns_the_steps_the_worker_recorded()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        var test = await StartAsync(h.Admin, h);
        const string steps = """
            [{"step": "flatten", "status": "succeeded", "startedAt": "2026-10-03T10:00:00+00:00", "finishedAt": "2026-10-03T10:00:01+00:00",
              "inputRows": 2, "outputRows": 2, "log": "", "error": null,
              "tables": [{"name": "main", "rows": 2, "columns": [{"name": "_id", "type": "integer"}, {"name": "note", "type": "text"}],
                          "previewRows": 2, "preview": [[1, "a"], [9007199254740993, "bb…"]], "cutCells": [[1, 1]]}]},
             {"step": "clean", "status": "failed", "startedAt": null, "finishedAt": null, "inputRows": 2, "outputRows": null,
              "log": "Traceback ...", "error": "KeyError: 'age'", "tables": []}]
            """;
        await TestData.ExecuteAsync(h.Db,
            "UPDATE tamiza.script_tests SET status = 'failed', finished_at = now(), error_code = 'step.clean', error = 'KeyError: ''age''', steps = @steps::jsonb",
            ("steps", steps));

        var read = await h.Admin.GetFromJsonAsync<JsonElement>($"{Tests(h)}/{Id(test)}");

        Assert.Equal("step.clean", read.GetProperty("errorCode").GetString());
        var flatten = read.GetProperty("steps")[0];
        var table = flatten.GetProperty("tables")[0];
        Assert.Equal("flatten", flatten.GetProperty("step").GetString());
        Assert.Equal("integer", table.GetProperty("columns")[0].GetProperty("type").GetString());
        Assert.Equal(9007199254740993, table.GetProperty("preview")[1][0].GetInt64());
        Assert.Equal([1, 1], table.GetProperty("cutCells")[0].EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal("KeyError: 'age'", read.GetProperty("steps")[1].GetProperty("error").GetString());
    }

    public void Dispose() => _kobo.Dispose();
}
