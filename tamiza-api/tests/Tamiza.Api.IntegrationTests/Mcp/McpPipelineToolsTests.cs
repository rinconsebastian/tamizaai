using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpPipelineToolsTests(PostgresFixture postgres)
{
    private const string Draft = "import pandas as pd\n\ndef compute(cleaned, ctx):\n    return pd.DataFrame({'municipality': ['Santa Ana'], 'value': [0.9]})\n";

    private Task<McpProjectHarness> CreateAsync(McpAccess level = McpAccess.Aggregates, McpAccess maximum = McpAccess.Rows) =>
        McpProjectHarness.CreateAsync(postgres, level, maximum, formFieldsJson: McpDataFixtures.FieldsJson, formChoicesJson: McpDataFixtures.ChoicesJson);

    private static (string, long, (string, string)[], object[][]?)[] Tables(params (string, long, (string, string)[], object[][]?)[] tables) => tables;

    private static object[][] Rows(int count) => Enumerable.Range(1, count).Select(i => new object[] { $"Person {i}", i }).ToArray();

    private static object[] IndicatorSteps(object? dashboard = null, string log = "", string? error = null, string status = "succeeded", object[]? shown = null, (string, long, (string, string)[], object[][]?)[]? tables = null) =>
    [
        FakeWorker.TestStep("flatten", tables: [("main", 480, [("water_source", "text")], null)]),
        FakeWorker.TestStep("clean", tables: [("main", 480, [("water_source", "text")], null)], reused: true),
        FakeWorker.TestStep("indicator", status, tables: tables ?? [("piped_water_rate", 12, [("municipality", "text"), ("value", "decimal")], null)], log: log, error: error, shown: shown, dashboard: dashboard),
    ];

    private async Task<(JsonDocument Result, Guid TestId)> RunTestAsync(McpProjectHarness h, string person, object[] steps, string status = "succeeded", object? extra = null, string? error = null)
    {
        var args = new Dictionary<string, object?> { ["kind"] = "indicator", ["name"] = "piped_water_rate", ["code"] = Draft, ["waitSeconds"] = 0 };
        if (extra is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            {
                args[property.Name] = property.Value;
            }
        }

        var started = (await h.CallAsync(person, "test_script", args)).Json();
        var testId = Guid.Parse(started.RootElement.GetProperty("testId").GetString()!);
        await FakeWorker.CompleteTestAsync(h.Db, h.ProjectId, h.Users[person], status, steps, error);
        var read = (await h.CallAsync(person, "get_test", new { testId, waitSeconds = 0, format = args.GetValueOrDefault("format"), previewRows = args.GetValueOrDefault("previewRows") })).Json();
        return (read, testId);
    }

    [Fact]
    public async Task A_test_is_queued_and_a_call_that_does_not_wait_says_how_to_read_it_later()
    {
        await using var h = await CreateAsync();

        using var started = (await h.CallAsync("analyst", "test_script", new { kind = "indicator", name = "piped_water_rate", code = Draft, waitSeconds = 0 })).Json();

        Assert.False(started.RootElement.GetProperty("done").GetBoolean());
        Assert.Equal("queued", started.RootElement.GetProperty("status").GetString());
        Assert.Contains("get_test", started.RootElement.GetProperty("next").GetString());
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests WHERE status = 'queued' AND kind = 'indicator'"));
    }

    [Fact]
    public async Task A_test_that_ends_while_the_call_waits_is_answered_as_soon_as_it_ends()
    {
        await using var h = await CreateAsync();
        var worker = Task.Run(async () =>
        {
            await FakeWorker.WaitForQueuedTestAsync(h.Db, h.ProjectId, h.Users["analyst"]);
            await Task.Delay(1500);
            await FakeWorker.CompleteTestAsync(h.Db, h.ProjectId, h.Users["analyst"], "succeeded", IndicatorSteps(FakeWorker.IndicatorView(["value"], "value", false, ["municipality"])));
        });

        var started = DateTimeOffset.UtcNow;
        using var body = (await h.CallAsync("analyst", "test_script", new { kind = "indicator", name = "piped_water_rate", code = Draft, waitSeconds = 30 })).Json();
        await worker;

        Assert.True(body.RootElement.GetProperty("done").GetBoolean());
        Assert.Equal("succeeded", body.RootElement.GetProperty("status").GetString());
        Assert.InRange((DateTimeOffset.UtcNow - started).TotalSeconds, 1.0, 15.0);
    }

    [Fact]
    public async Task A_test_of_a_new_indicator_gives_the_steps_the_table_and_how_it_will_look_on_a_dashboard()
    {
        await using var h = await CreateAsync();
        var view = FakeWorker.IndicatorView(["value", "numerator", "denominator"], "value", ratio: true, ["municipality"], rows: 12, max: 0.9, totals: new() { ["value"] = 1.2, ["numerator"] = 12, ["denominator"] = 20 });

        var (body, _) = await RunTestAsync(h, "analyst", IndicatorSteps(view));

        using var disposeBody = body;
        var test = body.RootElement;
        Assert.True(test.GetProperty("done").GetBoolean());
        Assert.Equal(["flatten", "clean", "indicator"], test.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("step").GetString()).ToArray());
        Assert.True(test.GetProperty("steps")[1].GetProperty("reused").GetBoolean());
        var table = test.GetProperty("steps")[2].GetProperty("tables")[0];
        Assert.Equal(("piped_water_rate", 12), (table.GetProperty("name").GetString(), table.GetProperty("rows").GetInt64()));
        var dashboard = test.GetProperty("steps")[2].GetProperty("dashboard");
        Assert.Equal("value", dashboard.GetProperty("defaultMeasure").GetString());
        Assert.True(dashboard.GetProperty("ratio").GetBoolean());
        Assert.Equal(12, dashboard.GetProperty("totals").GetProperty("numerator").GetDouble());
        Assert.Equal(2, dashboard.GetProperty("charts").GetProperty("municipality").GetProperty("groups").GetArrayLength());
        Assert.Equal(0, test.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task The_warnings_of_the_dashboard_view_are_the_editors()
    {
        await using var h = await CreateAsync();
        var view = FakeWorker.IndicatorView(["year", "numerator"], "year", ratio: false, ["municipality"], rows: 0, nulls: 4, max: 1.5, duplicates: 3);

        var (body, _) = await RunTestAsync(h, "analyst", IndicatorSteps(view), extra: new { format = "percent" });

        using var disposeBody = body;
        var keys = body.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("key").GetString()).ToArray();
        Assert.Equal(["noRows", "noValue", "ratioIncomplete", "emptyValues", "percentAboveOne", "dimensionAsMeasure", "duplicateRows"], keys);
    }

    [Fact]
    public async Task At_summaries_only_a_test_gives_shapes_and_never_rows_or_ctx_show_values_or_printed_lines()
    {
        await using var h = await CreateAsync(McpAccess.Aggregates);
        var shown = new object[]
        {
            new { name = "water_source values", line = 9, rows = 3L, table = new { name = "x", rows = 3L, columns = new[] { new { name = "water_source", type = "text" } }, previewRows = 3, preview = new[] { new[] { "CANARY-ROW" } } }, text = (string?)null },
            new { name = "total", line = 12, rows = 0L, table = (object?)null, text = "CANARY-TEXT 12345" },
        };
        var steps = IndicatorSteps(
            log: "INFO started\nCANARY-PRINTED df.head()\nWARNING 3 rows dropped",
            shown: shown,
            tables: [("main", 480, [("water_source", "text")], [["CANARY-PREVIEW", 1]])]);

        var (body, _) = await RunTestAsync(h, "analyst", steps);

        using var disposeBody = body;
        var text = body.RootElement.ToString();
        Assert.DoesNotContain("CANARY", text);
        Assert.Contains("need access to rows", body.RootElement.GetProperty("withheld").GetString());
        var draft = body.RootElement.GetProperty("steps")[2];
        Assert.Equal(480, draft.GetProperty("tables")[0].GetProperty("rows").GetInt64());
        Assert.False(draft.GetProperty("tables")[0].TryGetProperty("preview", out _));
        Assert.Equal("water_source values", draft.GetProperty("shown")[0].GetProperty("name").GetString());
        Assert.Equal("text", draft.GetProperty("shown")[1].GetProperty("kind").GetString());
        // Only lines that start with a log level reach the client, and their words are masked.
        var log = draft.GetProperty("log").EnumerateArray().Select(l => l.GetString()!).ToArray();
        Assert.Equal(2, log.Length);
        Assert.StartsWith("INFO ", log[0]);
        Assert.StartsWith("WARNING ", log[1]);
        Assert.DoesNotContain("CANARY-PRINTED", draft.GetProperty("log").ToString());
    }

    [Fact]
    public async Task At_summaries_only_an_error_is_the_exception_with_its_words_masked_and_the_lines_of_the_script()
    {
        await using var h = await CreateAsync(McpAccess.Aggregates);
        const string error = "KeyError: 'Juan Pérez'\n\nTraceback (most recent call last):\n  File \"script.py\", line 4, in compute\n    x = cleaned['main']['Juan Pérez']\nKeyError: 'Juan Pérez'";

        var (body, _) = await RunTestAsync(h, "analyst", IndicatorSteps(status: "failed", error: error), status: "failed", error: error);

        using var disposeBody = body;
        var step = body.RootElement.GetProperty("steps")[2].GetProperty("error").GetString()!;
        Assert.StartsWith("KeyError: '…'", step);
        Assert.Contains("line 4 in compute", step);
        Assert.DoesNotContain("Juan", body.RootElement.ToString());
        Assert.DoesNotContain("x = cleaned", body.RootElement.ToString());
    }

    [Fact]
    public async Task With_rows_allowed_a_test_gives_twenty_preview_rows_or_the_number_asked_and_the_ctx_show_values_and_the_full_error()
    {
        await using var h = await CreateAsync(McpAccess.Rows);
        var shown = new object[] { new { name = "total", line = 12, rows = 0L, table = (object?)null, text = "TEXT-VALUE 12345" } };
        var tables = Tables(("main", 480, [("name", "text"), ("n", "integer")], Rows(100)));
        const string error = "Traceback (most recent call last):\n  File \"script.py\", line 4, in compute\nKeyError: 'Juan Pérez'";

        var (twenty, _) = await RunTestAsync(h, "analyst", IndicatorSteps(shown: shown, tables: tables, log: "print line\nINFO x", error: error));
        var (fifty, _) = await RunTestAsync(h, "analyst", IndicatorSteps(tables: tables), extra: new { previewRows = 50 });

        using var _1 = twenty;
        using var _2 = fifty;
        var draft = twenty.RootElement.GetProperty("steps")[2];
        Assert.Equal(20, draft.GetProperty("tables")[0].GetProperty("preview").GetProperty("rows").GetArrayLength());
        Assert.Equal("TEXT-VALUE 12345", draft.GetProperty("shown")[0].GetProperty("text").GetString());
        Assert.Contains("Juan Pérez", draft.GetProperty("error").GetString());
        Assert.Contains("print line", draft.GetProperty("log").ToString());
        Assert.Null(twenty.RootElement.TryGetProperty("withheld", out var w) ? w.GetString() : null);
        Assert.Equal(50, fifty.RootElement.GetProperty("steps")[2].GetProperty("tables")[0].GetProperty("preview").GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task A_new_test_replaces_the_same_persons_queued_test_in_the_project()
    {
        await using var h = await CreateAsync();
        var rest = h.Factory.CreateClient().WithToken(TestTokens.Create("analyst", "analyst", "analyst@example.org"));
        var queued = await rest.PostAsJsonAsync($"/api/v1/projects/{h.ProjectId}/script-tests", new { kind = "clean", code = "def clean(raw, ctx):\n    return raw\n" });
        Assert.True(queued.IsSuccessStatusCode);
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));

        await h.CallAsync("analyst", "test_script", new { kind = "indicator", name = "piped_water_rate", code = Draft, waitSeconds = 0 });

        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));
        Assert.Equal("indicator", await TestData.ScalarAsync<string>(h.Db, "SELECT kind FROM tamiza.script_tests"));
    }

    [Fact]
    public async Task Only_the_person_who_started_a_test_can_read_it_and_only_for_a_day()
    {
        await using var h = await CreateAsync();
        using var started = (await h.CallAsync("analyst", "test_script", new { kind = "indicator", name = "piped_water_rate", code = Draft, waitSeconds = 0 })).Json();
        var testId = started.RootElement.GetProperty("testId").GetString();
        await FakeWorker.CompleteTestAsync(h.Db, h.ProjectId, h.Users["analyst"], "succeeded", IndicatorSteps());

        var admin = await h.CallAsync("admin", "get_test", new { testId, waitSeconds = 0 });
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_tests SET finished_at = now() - interval '25 hours'");
        var expired = await h.CallAsync("analyst", "get_test", new { testId, waitSeconds = 0 });

        Assert.Equal("mcp.notFound", admin.Code());
        Assert.Equal("mcp.notFound", expired.Code());
    }

    [Fact]
    public async Task A_cancelled_call_stops_waiting_and_leaves_the_test_running()
    {
        await using var h = await CreateAsync();
        using var cancel = new CancellationTokenSource();
        var client = await h.ClientAsync("analyst");

        var call = client.CallToolAsync("test_script",
            new Dictionary<string, object?> { ["projectId"] = h.ProjectId, ["kind"] = "indicator", ["name"] = "piped_water_rate", ["code"] = Draft, ["waitSeconds"] = 60 },
            cancellationToken: cancel.Token).AsTask();
        await FakeWorker.WaitForQueuedTestAsync(h.Db, h.ProjectId, h.Users["analyst"]);
        await cancel.CancelAsync();
        // The client abandons the request: it ends as a cancellation, or as the 499 the transport reports for a request that was aborted.
        var ended = await Record.ExceptionAsync(() => call);
        Assert.True(ended is OperationCanceledException or HttpRequestException { StatusCode: (HttpStatusCode)499 }, $"{ended?.GetType().Name}: {ended?.Message}");
        await Task.Delay(500);

        Assert.Equal("queued", await TestData.ScalarAsync<string>(h.Db, "SELECT status FROM tamiza.script_tests"));
    }

    [Fact]
    public async Task A_call_that_asks_for_progress_gets_it_while_it_waits()
    {
        await using var h = await CreateAsync();
        var client = await h.ClientAsync("analyst");
        var reports = new List<ProgressNotificationValue>();
        var worker = Task.Run(async () =>
        {
            await FakeWorker.WaitForQueuedTestAsync(h.Db, h.ProjectId, h.Users["analyst"]);
            await Task.Delay(7000);
            await FakeWorker.CompleteTestAsync(h.Db, h.ProjectId, h.Users["analyst"], "succeeded", IndicatorSteps());
        });

        var result = await client.CallToolAsync("test_script",
            new Dictionary<string, object?> { ["projectId"] = h.ProjectId, ["kind"] = "indicator", ["name"] = "piped_water_rate", ["code"] = Draft, ["waitSeconds"] = 40 },
            progress: new Progress<ProgressNotificationValue>(reports.Add));
        await worker;
        await Task.Delay(300);

        Assert.Null(result.Code());
        Assert.NotEmpty(reports);
        Assert.Contains(reports, r => r.Message is not null);
    }

    [Fact]
    public async Task A_run_is_queued_waited_for_and_read_with_its_steps_and_the_scripts_it_used()
    {
        await using var h = await CreateAsync();
        var worker = Task.Run(async () =>
        {
            var run = await FakeWorker.WaitForQueuedRunAsync(h.Db, h.ProjectId);
            await Task.Delay(1000);
            await FakeWorker.CompleteRunAsync(h.Db, run, "succeeded",
                ("flatten", "", "succeeded", null, "INFO flattened", "[{\"name\":\"main\",\"rows\":480}]"),
                ("indicator", "piped_water_rate", "succeeded", null, "", "[{\"name\":\"piped_water_rate\",\"rows\":12}]"));
        });

        using var body = (await h.CallAsync("analyst", "start_run", new { waitSeconds = 30 })).Json();
        await worker;

        Assert.True(body.RootElement.GetProperty("done").GetBoolean());
        Assert.Equal("succeeded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("manual", body.RootElement.GetProperty("trigger").GetString());
        Assert.Equal("analyst", body.RootElement.GetProperty("requestedBy").GetString());
        var indicator = body.RootElement.GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("step").GetString() == "indicator");
        Assert.Equal(("piped_water_rate", 12), (indicator.GetProperty("name").GetString(), indicator.GetProperty("tables")[0].GetProperty("rows").GetInt32()));
    }

    [Fact]
    public async Task A_run_that_is_not_done_after_the_wait_says_so_and_the_latest_run_is_read_by_default()
    {
        await using var h = await CreateAsync();

        using var started = (await h.CallAsync("analyst", "start_run", new { waitSeconds = 0 })).Json();
        var again = await h.CallAsync("analyst", "start_run", new { waitSeconds = 0 });
        var run = await FakeWorker.WaitForQueuedRunAsync(h.Db, h.ProjectId);
        await FakeWorker.CompleteRunAsync(h.Db, run, "succeeded");
        using var latest = (await h.CallAsync("analyst", "get_run", new { waitSeconds = 0 })).Json();

        Assert.False(started.RootElement.GetProperty("done").GetBoolean());
        Assert.Contains("get_run", started.RootElement.GetProperty("next").GetString());
        Assert.Equal("run.already_queued", again.Code());
        Assert.Equal(run.ToString(), latest.RootElement.GetProperty("runId").GetString());
        Assert.True(latest.RootElement.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task A_project_that_never_ran_has_no_latest_run()
    {
        await using var h = await CreateAsync();

        var result = await h.CallAsync("analyst", "get_run", new { waitSeconds = 0 });

        Assert.Equal("mcp.notFound", result.Code());
        Assert.Contains("start_run", result.Text());
    }

    [Fact]
    public async Task At_summaries_only_the_error_and_log_of_a_failed_step_are_masked_and_with_rows_they_are_whole()
    {
        await using var h = await CreateAsync(McpAccess.Aggregates);
        const string error = "Traceback (most recent call last):\n  File \"script.py\", line 7, in clean\nValueError: could not convert 'Juan Pérez' to int";
        await h.CallAsync("analyst", "start_run", new { waitSeconds = 0 });
        var run = await FakeWorker.WaitForQueuedRunAsync(h.Db, h.ProjectId);
        await FakeWorker.CompleteRunAsync(h.Db, run, "failed", ("flatten", "", "succeeded", null, "", "[]"), ("clean", "", "failed", error, "INFO reading\nCANARY printed row", "[]"));

        using var masked = (await h.CallAsync("analyst", "get_run", new { runId = run, waitSeconds = 0 })).Json();
        await h.SetLevelAsync(McpAccess.Rows);
        using var whole = (await h.CallAsync("analyst", "get_run", new { runId = run, waitSeconds = 0 })).Json();

        var maskedClean = masked.RootElement.GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("step").GetString() == "clean");
        Assert.DoesNotContain("Juan", masked.RootElement.ToString());
        Assert.DoesNotContain("CANARY", masked.RootElement.ToString());
        Assert.Contains("line 7 in clean", maskedClean.GetProperty("error").GetString());
        var wholeClean = whole.RootElement.GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("step").GetString() == "clean");
        Assert.Contains("Juan Pérez", wholeClean.GetProperty("error").GetString());
        Assert.Contains("CANARY printed row", wholeClean.GetProperty("log").ToString());
    }

    [Fact]
    public async Task A_viewer_can_neither_test_nor_run()
    {
        await using var h = await CreateAsync();

        Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", "test_script", new { kind = "clean", code = "def clean(raw, ctx):\n    return raw\n", waitSeconds = 0 })).Code());
        Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", "start_run", new { waitSeconds = 0 })).Code());
        Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", "get_run", new { waitSeconds = 0 })).Code());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.runs"));
    }

    [Fact]
    public async Task The_rules_of_the_editors_test_apply_to_a_test_through_mcp()
    {
        await using var h = await CreateAsync();

        var kind = await h.CallAsync("analyst", "test_script", new { kind = "dashboard", code = Draft, waitSeconds = 0 });
        var size = await h.CallAsync("analyst", "test_script", new { kind = "clean", code = "def clean(raw, ctx):\n    return raw\n", sampleSize = 9000, waitSeconds = 0 });
        var line = await h.CallAsync("analyst", "test_script", new { kind = "clean", code = "def clean(raw, ctx):\n    return raw\n", stopAtLine = 2, waitSeconds = 0 });

        Assert.All(new[] { kind, size, line }, r => Assert.Equal("mcp.validation", r.Code()));
        Assert.Contains("sampleSize", size.Text());
        Assert.Contains("stopAtLine", line.Text());
    }
}
