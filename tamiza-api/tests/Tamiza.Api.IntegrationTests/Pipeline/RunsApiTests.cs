using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Pipeline;

[Collection(PostgresCollection.Name)]
public sealed class RunsApiTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static string Runs(IngestionHarness h) => $"/api/v1/projects/{h.ProjectId}/runs";

    private static string Settings(IngestionHarness h) => $"/api/v1/projects/{h.ProjectId}/pipeline";

    private static async Task<HttpClient> MemberAsync(IngestionHarness harness, string subject, string role)
    {
        var userId = await TestData.SignInAsync(harness.Factory, subject, $"{subject}@example.org");
        await TestData.AddMemberAsync(harness.Db, harness.ProjectId, userId, role);
        return TestData.ClientFor(harness.Factory, subject, $"{subject}@example.org");
    }

    private static Task<long> RunCountAsync(IngestionHarness h, string where = "true") =>
        TestData.ScalarAsync<long>(h.Db, $"SELECT count(*) FROM tamiza.runs WHERE {where}");

    private static async Task WaitUntilAsync(IngestionHarness h, string condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await TestData.ScalarAsync<bool>(h.Db, condition))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {condition}");
            await Task.Delay(50);
        }
    }

    // 6.2 — starting runs, the list and details

    [Fact]
    public async Task An_analyst_queues_a_run_and_a_second_waits_only_behind_a_running_one()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        using var viewer = await MemberAsync(h, "vera", "viewer");

        var first = await analyst.PostAsync(Runs(h), null);
        var duplicate = await h.Admin.PostAsync(Runs(h), null);
        var forbidden = await viewer.PostAsync(Runs(h), null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var run = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("manual", run.GetProperty("trigger").GetString());
        Assert.Equal("queued", run.GetProperty("status").GetString());
        Assert.Equal("ana", run.GetProperty("requestedBy").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("run.already_queued", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Once the worker starts it, one more may wait behind it.
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.runs SET status = 'running', started_at = now()");
        Assert.Equal(HttpStatusCode.Accepted, (await analyst.PostAsync(Runs(h), null)).StatusCode);
        Assert.Equal(2L, await RunCountAsync(h));
    }

    [Fact]
    public async Task The_list_pages_newest_first_and_details_show_each_step_to_analysts_and_not_to_viewers()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        using var viewer = await MemberAsync(h, "vera", "viewer");
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at, finished_at)
            SELECT gen_random_uuid(), @p, 'schedule', 'succeeded', t, t, t + interval '1 minute'
            FROM generate_series(1, 25) AS n, LATERAL (SELECT timestamptz '2026-10-01T00:00:00Z' + n * interval '1 hour' AS t) AS x
            """, ("p", h.ProjectId));
        var runId = Guid.CreateVersion7();
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at, finished_at, error_code, error, reference_tables)
            VALUES (@id, @p, 'manual', 'failed', '2026-10-05T00:00:00Z', '2026-10-05T00:00:00Z', '2026-10-05T00:01:00Z', 'step.clean', 'KeyError: ''age''',
                    '[{"name": "population", "rows": 1123, "updatedAt": "2026-10-04T12:00:00+00:00"}]');
            INSERT INTO tamiza.run_steps (run_id, step, name, status, input_rows, output_rows, tables, log, error) VALUES
                (@id, 'flatten', '', 'succeeded', 3, 9, '[{"name":"main","rows":3},{"name":"members","rows":6}]', 'Flattened 3 submissions', NULL),
                (@id, 'clean', '', 'failed', NULL, NULL, '[]', 'INFO starting', 'KeyError: ''age'''),
                (@id, 'output', '', 'skipped', NULL, NULL, '[]', 'Not run: the clean step failed.', NULL),
                (@id, 'indicator', 'coverage', 'skipped', NULL, NULL, '[]', 'Not run: the clean step failed.', NULL);
            """, ("id", runId), ("p", h.ProjectId));

        var page = await analyst.GetFromJsonAsync<JsonElement>($"{Runs(h)}?page=1&pageSize=10");
        var details = await analyst.GetFromJsonAsync<JsonElement>($"{Runs(h)}/{runId}");
        var missing = await analyst.GetAsync($"{Runs(h)}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Runs(h))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{Runs(h)}/{runId}")).StatusCode);

        Assert.Equal(26, page.GetProperty("total").GetInt32());
        Assert.Equal(10, page.GetProperty("items").GetArrayLength());
        var newest = page.GetProperty("items")[0];
        Assert.Equal(runId.ToString(), newest.GetProperty("id").GetString());
        Assert.Equal("step.clean", newest.GetProperty("errorCode").GetString());
        Assert.Equal(["succeeded", "failed", "skipped", "skipped"], newest.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("status").GetString()));
        var steps = details.GetProperty("steps");
        Assert.Equal(["flatten", "clean", "output", "indicator"], steps.EnumerateArray().Select(s => s.GetProperty("step").GetString()));
        Assert.Equal(["", "", "", "coverage"], steps.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(0, newest.GetProperty("versions").GetProperty("indicators").GetArrayLength());
        Assert.Equal(6, steps[0].GetProperty("tables")[1].GetProperty("rows").GetInt64());
        Assert.Equal("KeyError: 'age'", steps[1].GetProperty("error").GetString());
        Assert.Equal("INFO starting", steps[1].GetProperty("log").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var reference = Assert.Single(details.GetProperty("referenceTables").EnumerateArray());
        Assert.Equal("population", reference.GetProperty("name").GetString());
        Assert.Equal(1123, reference.GetProperty("rows").GetInt64());
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T12:00:00Z"), reference.GetProperty("updatedAt").GetDateTimeOffset());
        var older = page.GetProperty("items")[1].GetProperty("id").GetString();
        Assert.Empty((await analyst.GetFromJsonAsync<JsonElement>($"{Runs(h)}/{older}")).GetProperty("referenceTables").EnumerateArray());
    }

    // Indicator steps and the versions a run used

    private static async Task<Guid> InsertVersionAsync(IngestionHarness h, string kind, string? name, int number, string? label, string format = "decimal")
    {
        var id = Guid.CreateVersion7();
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, author_id, created_at)
            VALUES (@id, @p, @kind, @name, @number, 'x', @label, @format, (SELECT created_by FROM tamiza.projects WHERE id = @p), now())
            """,
            ("id", id), ("p", h.ProjectId), ("kind", kind), ("name", (object?)name ?? DBNull.Value), ("number", number),
            ("label", (object?)label ?? DBNull.Value), ("format", kind == "indicator" ? format : (object)DBNull.Value));
        return id;
    }

    [Fact]
    public async Task A_run_lists_its_indicator_steps_by_name_and_the_versions_it_used_not_the_current_ones()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        var clean4 = await InsertVersionAsync(h, "clean", null, 4, null);
        var coverage2 = await InsertVersionAsync(h, "indicator", "coverage", 2, "Coverage (as the run saw it)");
        var attendance1 = await InsertVersionAsync(h, "indicator", "attendance", 1, "Attendance");
        await InsertVersionAsync(h, "indicator", "coverage", 3, "Coverage (saved during the run)");
        await InsertVersionAsync(h, "clean", null, 5, null);
        var runId = Guid.CreateVersion7();
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at, finished_at, error_code, error, clean_version_id, indicator_versions)
            VALUES (@id, @p, 'manual', 'failed', '2026-10-05T00:00:00Z', '2026-10-05T00:00:00Z', '2026-10-05T00:01:00Z',
                    'step.indicator', 'Indicator coverage failed: KeyError: ''age''', @clean, @versions::jsonb);
            INSERT INTO tamiza.run_steps (run_id, step, name, status, input_rows, output_rows, tables, log, error) VALUES
                (@id, 'flatten', '', 'succeeded', 3, 3, '[{"name":"main","rows":3}]', '', NULL),
                (@id, 'clean', '', 'succeeded', 3, 3, '[{"name":"main","rows":3}]', '', NULL),
                (@id, 'output', '', 'skipped', NULL, NULL, '[]', 'No script 2.', NULL),
                (@id, 'indicator', 'coverage', 'failed', 3, NULL, '[]', 'Traceback ...', 'KeyError: ''age'''),
                (@id, 'indicator', 'attendance', 'succeeded', 3, 2, '[{"name":"attendance","rows":2}]', 'ok', NULL);
            """,
            ("id", runId), ("p", h.ProjectId), ("clean", clean4),
            // Stored in no particular order: the API lists them by name.
            ("versions", $$"""[{"name":"coverage","versionId":"{{coverage2}}","number":2},{"name":"attendance","versionId":"{{attendance1}}","number":1}]"""));

        var list = await analyst.GetFromJsonAsync<JsonElement>(Runs(h));
        var details = await analyst.GetFromJsonAsync<JsonElement>($"{Runs(h)}/{runId}");

        var versions = list.GetProperty("items")[0].GetProperty("versions");
        Assert.Equal(4, versions.GetProperty("clean").GetInt32());
        Assert.Equal(JsonValueKind.Null, versions.GetProperty("output").ValueKind);
        Assert.Equal([("attendance", 1), ("coverage", 2)], versions.GetProperty("indicators").EnumerateArray()
            .Select(i => (i.GetProperty("name").GetString(), i.GetProperty("number").GetInt32())));
        Assert.Equal("step.indicator", details.GetProperty("run").GetProperty("errorCode").GetString());
        Assert.Contains("coverage", details.GetProperty("run").GetProperty("error").GetString());
        var steps = details.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal([("flatten", ""), ("clean", ""), ("output", ""), ("indicator", "attendance"), ("indicator", "coverage")],
            steps.Select(s => (s.GetProperty("step").GetString()!, s.GetProperty("name").GetString()!)));
        // The label is the one of the version the run used.
        Assert.Equal([JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.String, JsonValueKind.String], steps.Select(s => s.GetProperty("label").ValueKind));
        Assert.Equal("Attendance", steps[3].GetProperty("label").GetString());
        Assert.Equal("Coverage (as the run saw it)", steps[4].GetProperty("label").GetString());
        Assert.Equal("KeyError: 'age'", steps[4].GetProperty("error").GetString());
        Assert.Equal("failed", steps[4].GetProperty("status").GetString());
        Assert.Equal(2, steps[3].GetProperty("outputRows").GetInt64());
        var summarySteps = details.GetProperty("run").GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["", "", "", "attendance", "coverage"], summarySteps.Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_run_without_indicators_has_an_empty_list_of_versions_and_no_indicator_steps()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        var runId = Guid.CreateVersion7();
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at, finished_at)
            VALUES (@id, @p, 'manual', 'succeeded', now(), now(), now());
            INSERT INTO tamiza.run_steps (run_id, step, name, status) VALUES
                (@id, 'flatten', '', 'succeeded'), (@id, 'clean', '', 'succeeded'), (@id, 'output', '', 'skipped');
            """, ("id", runId), ("p", h.ProjectId));

        var details = await h.Admin.GetFromJsonAsync<JsonElement>($"{Runs(h)}/{runId}");

        Assert.Equal(0, details.GetProperty("run").GetProperty("versions").GetProperty("indicators").GetArrayLength());
        Assert.Equal(["flatten", "clean", "output"], details.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("step").GetString()));
    }

    [Fact]
    public async Task A_run_is_queued_with_no_versions_and_the_worker_fills_them_when_it_claims_it()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        await InsertVersionAsync(h, "indicator", "coverage", 1, "Coverage");

        var queued = await (await analyst.PostAsync(Runs(h), null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, queued.GetProperty("versions").GetProperty("indicators").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, queued.GetProperty("versions").GetProperty("clean").ValueKind);
        Assert.Equal("[]", await TestData.ScalarAsync<string>(h.Db, "SELECT indicator_versions::text FROM tamiza.runs"));
    }


    // 6.3 — settings and triggers

    [Fact]
    public async Task Settings_default_to_running_after_sync_without_a_schedule_and_only_admins_change_them()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at)
            VALUES (gen_random_uuid(), @p, 'manual', 'succeeded', '2026-10-01T08:00:00Z', '2026-10-01T08:00:00Z')
            """, ("p", h.ProjectId));

        var defaults = await analyst.GetFromJsonAsync<JsonElement>(Settings(h));
        var forbidden = await analyst.PutAsJsonAsync(Settings(h), new { runAfterSync = false, runIntervalHours = 24 });
        var invalid = await h.Admin.PutAsJsonAsync(Settings(h), new { runAfterSync = true, runIntervalHours = 200 });
        var incomplete = await h.Admin.PutAsJsonAsync(Settings(h), new { runIntervalHours = 24 });
        var saved = await h.Admin.PutAsJsonAsync(Settings(h), new { runAfterSync = false, runIntervalHours = 24 });
        var cleared = await h.Admin.PutAsJsonAsync(Settings(h), new { runAfterSync = true, runIntervalHours = (int?)null });

        Assert.True(defaults.GetProperty("runAfterSync").GetBoolean());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("runIntervalHours").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("1 to 168", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("runIntervalHours")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        var body = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("runAfterSync").GetBoolean());
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), body.GetProperty("nextRunAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, (await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nextRunAt").ValueKind);
    }

    [Fact]
    public async Task A_synchronization_with_changes_queues_a_run_and_one_without_changes_does_not()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(3));
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo, scheduler: true);
        await h.WaitForIdleAsync();
        await WaitUntilAsync(h, "SELECT count(*) = 1 FROM tamiza.runs WHERE trigger = 'sync'");

        // The worker would have run it; nothing changed since.
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.runs SET status = 'succeeded', started_at = now(), finished_at = now()");
        await h.SyncAsync("full");
        await Task.Delay(300);

        Assert.Equal(1L, await RunCountAsync(h));
    }

    [Fact]
    public async Task No_run_is_queued_when_the_setting_is_off_or_for_a_webhook()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo, scheduler: true);
        await h.WaitForIdleAsync();
        (await h.Admin.PutAsJsonAsync(Settings(h), new { runAfterSync = false, runIntervalHours = (int?)null })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await h.DeliverAsync("""{"_id": 77, "_uuid": "w-77"}""")).StatusCode);
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(2));
        var sync = await h.SyncAsync("full");
        await Task.Delay(300);

        Assert.Equal(2, sync.GetProperty("createdCount").GetInt32());
        Assert.Equal(0L, await RunCountAsync(h));
    }

    [Fact]
    public async Task A_due_schedule_queues_a_scheduled_run()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo, scheduler: true);
        await h.WaitForIdleAsync();
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.projects SET run_interval_hours = 24, next_run_at = now() - interval '1 minute'");

        await WaitUntilAsync(h, "SELECT count(*) = 1 FROM tamiza.runs WHERE trigger = 'schedule' AND status = 'queued'");
        await Task.Delay(500);

        Assert.Equal(1L, await RunCountAsync(h, "trigger = 'schedule'"));
    }

    // 6.4 — OpenAPI

    [Fact]
    public async Task The_openapi_document_lists_the_pipeline_operations()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var operations = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(op => op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("operationId", out _))
            .Select(op => op.Value.GetProperty("operationId").GetString())
            .ToHashSet();

        Assert.Superset(new HashSet<string?>
        {
            "ListScripts", "SaveScript", "RemoveScript", "ListScriptVersions", "GetScriptVersion", "RestoreScriptVersion",
            "StartRun", "ListRuns", "GetRun", "GetPipelineSettings", "UpdatePipelineSettings",
            "SaveIndicatorScript", "RemoveIndicatorScript", "ListIndicatorScriptVersions", "GetIndicatorScriptVersion", "RestoreIndicatorScriptVersion",
            "StartScriptTest", "GetScriptTest",
            "ListReferenceTables", "GetReferenceTable", "UploadReferenceTable", "ReplaceReferenceTable", "DeleteReferenceTable",
        }, operations);
    }

    public void Dispose() => _kobo.Dispose();
}
