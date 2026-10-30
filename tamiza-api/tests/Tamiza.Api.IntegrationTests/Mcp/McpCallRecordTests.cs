using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Mcp;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpCallRecordTests(PostgresFixture postgres)
{
    private sealed record Row(Guid UserId, Guid? ProjectId, string Tool, string? Access, string Status, string? ErrorCode, int DurationMs, int? ResultChars);

    [Fact]
    public async Task A_succeeded_a_failed_and_a_refused_call_each_write_one_row_without_arguments_or_results()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        var ada = await TestData.SignInAsync(harness.Factory, "ada", "ada@example.org");
        var project = await TestData.InsertProjectAsync(harness.ConnectionString, ada);
        await TestData.AddMemberAsync(harness.ConnectionString, project, ada, "analyst");
        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("ada", "ada", "ada@example.org"));

        var ok = await client.CallToolAsync("probe_ok");
        var refused = await client.CallToolAsync("probe_viewer", new Dictionary<string, object?> { ["projectId"] = project });
        await TestData.ExecuteAsync(harness.ConnectionString, "UPDATE tamiza.projects SET mcp_access = 'aggregates'");
        var allowed = await client.CallToolAsync("probe_viewer", new Dictionary<string, object?> { ["projectId"] = project });
        var failed = await client.CallToolAsync("probe_fail");

        Assert.Null(ok.Code());
        Assert.Equal("mcp.projectOff", refused.Code());
        Assert.Null(allowed.Code());
        Assert.Equal("mcp.failed", failed.Code());
        Assert.DoesNotContain("secret value", failed.Text());

        var rows = await ReadAsync(harness.ConnectionString);
        Assert.Equal(["probe_ok", "probe_viewer", "probe_viewer", "probe_fail"], rows.Select(r => r.Tool).ToArray());
        Assert.All(rows, r => Assert.Equal(ada, r.UserId));

        Assert.Equal(("succeeded", null, null, null), (rows[0].Status, rows[0].ProjectId, rows[0].Access, rows[0].ErrorCode));
        Assert.Equal(("refused", project, null, "mcp.projectOff"), (rows[1].Status, rows[1].ProjectId, rows[1].Access, rows[1].ErrorCode));
        Assert.Equal(("succeeded", project, "aggregates", null), (rows[2].Status, rows[2].ProjectId, rows[2].Access, rows[2].ErrorCode));
        Assert.Equal(("failed", null, null, "mcp.failed"), (rows[3].Status, rows[3].ProjectId, rows[3].Access, rows[3].ErrorCode));
        Assert.All(rows, r => Assert.True(r.DurationMs >= 0 && r.ResultChars > 0));
        Assert.Equal(ok.Text().Length, rows[0].ResultChars);
    }

    [Fact]
    public async Task A_call_about_a_project_of_others_is_recorded_without_pointing_at_it()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        var admin = await TestData.SignInAsync(harness.Factory, "admin", "admin@example.org");
        var project = await TestData.InsertProjectAsync(harness.ConnectionString, admin);
        await using var outsider = await harness.ConnectAsync(McpHarness.TokenFor("outsider", "outsider", "outsider@example.org"));

        var result = await outsider.CallToolAsync("probe_viewer", new Dictionary<string, object?> { ["projectId"] = project });

        Assert.Equal("mcp.projectNotFound", result.Code());
        var row = Assert.Single(await ReadAsync(harness.ConnectionString));
        Assert.Null(row.ProjectId);
        Assert.Equal(("refused", "mcp.projectNotFound"), (row.Status, row.ErrorCode));
    }

    [Fact]
    public async Task A_record_that_cannot_be_written_does_not_fail_the_call()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("ada", "ada", "ada@example.org"));
        await TestData.ExecuteAsync(harness.ConnectionString, "DROP TABLE tamiza.mcp_calls");

        var result = await client.CallToolAsync("probe_ok");

        Assert.Null(result.Code());
        Assert.Contains("fine", result.Text());
        Assert.Contains(harness.Factory.Logs.Lines, line => line.StartsWith("Error", StringComparison.Ordinal) && line.Contains("Could not record the MCP call probe_ok"));
    }

    [Fact]
    public async Task A_client_that_disconnects_in_the_middle_of_a_call_is_still_recorded()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("ada", "ada", "ada@example.org"));
        using var cancel = new CancellationTokenSource();

        var call = client.CallToolAsync("probe_slow", new Dictionary<string, object?> { ["milliseconds"] = 10_000 }, cancellationToken: cancel.Token).AsTask();
        await Task.Delay(500);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

        var row = await WaitForRowAsync(harness.ConnectionString, "probe_slow");
        Assert.Equal("failed", row.Status);
        Assert.Equal("mcp.cancelled", row.ErrorCode);
    }

    [Fact]
    public async Task Calls_over_the_minute_limit_are_refused_with_the_seconds_to_wait_and_do_not_run()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("ada", "ada", "ada@example.org"));
        var startedBefore = McpProbeTools.Started;

        var results = new List<ModelContextProtocol.Protocol.CallToolResult>();
        for (var i = 0; i < 130; i++)
        {
            results.Add(await client.CallToolAsync("probe_slow", new Dictionary<string, object?> { ["milliseconds"] = 0 }));
        }

        Assert.All(results.Take(120), r => Assert.Null(r.Code()));
        Assert.All(results.Skip(120), r =>
        {
            Assert.Equal("mcp.rateLimited", r.Code());
            using var body = r.Json();
            Assert.InRange(body.RootElement.GetProperty("retryAfter").GetInt32(), 1, 60);
        });
        Assert.Equal(120, McpProbeTools.Started - startedBefore);

        // Refused calls are recorded too, as refused.
        var rows = await ReadAsync(harness.ConnectionString);
        Assert.Equal(130, rows.Count);
        Assert.Equal(10, rows.Count(r => r.Status == "refused" && r.ErrorCode == "mcp.rateLimited"));
    }

    [Fact]
    public async Task A_fifth_call_at_the_same_time_is_refused_and_a_place_is_free_again_when_one_ends()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        await using var client = await harness.ConnectAsync(McpHarness.TokenFor("ada", "ada", "ada@example.org"));
        var slow = new Dictionary<string, object?> { ["milliseconds"] = 1_500 };

        var running = Enumerable.Range(0, 4).Select(_ => client.CallToolAsync("probe_slow", slow).AsTask()).ToList();
        await Task.Delay(500);
        var fifth = await client.CallToolAsync("probe_ok");
        await Task.WhenAll(running);
        var afterwards = await client.CallToolAsync("probe_ok");

        Assert.Equal("mcp.rateLimited", fifth.Code());
        Assert.All(running, r => Assert.Null(r.Result.Code()));
        Assert.Null(afterwards.Code());
    }

    [Fact]
    public async Task People_do_not_share_limits()
    {
        await using var harness = await McpHarness.StartAsync(postgres, new Dictionary<string, string?> { ["Mcp:CallsPerMinutePerUser"] = "10" });
        await using var ada = await harness.ConnectAsync(McpHarness.TokenFor("ada", "ada", "ada@example.org"));
        await using var grace = await harness.ConnectAsync(McpHarness.TokenFor("grace", "grace", "grace@example.org"));
        for (var i = 0; i < 10; i++)
        {
            await ada.CallToolAsync("probe_ok");
        }

        Assert.Equal("mcp.rateLimited", (await ada.CallToolAsync("probe_ok")).Code());
        Assert.Null((await grace.CallToolAsync("probe_ok")).Code());
    }

    [Fact]
    public async Task The_sweep_deletes_records_older_than_the_retention_and_keeps_newer_ones()
    {
        await using var harness = await McpHarness.StartAsync(postgres);
        var ada = await TestData.SignInAsync(harness.Factory, "ada", "ada@example.org");
        foreach (var days in new[] { 200, 181, 179, 10, 0 })
        {
            await TestData.ExecuteAsync(harness.ConnectionString,
                "INSERT INTO tamiza.mcp_calls (id, user_id, tool, status, duration_ms, started_at) VALUES (gen_random_uuid(), @user, @tool, 'succeeded', 1, now() - make_interval(days => @days))",
                ("user", ada), ("tool", $"old_{days}"), ("days", days));
        }

        var deleted = await harness.Factory.Services.GetRequiredService<McpCallSweeper>().SweepAsync(CancellationToken.None);

        Assert.Equal(2, deleted);
        var left = (await ReadAsync(harness.ConnectionString)).Select(r => r.Tool).Order().ToArray();
        Assert.Equal(["old_0", "old_10", "old_179"], left);
    }

    private static async Task<Row> WaitForRowAsync(string connectionString, string tool)
    {
        for (var i = 0; i < 100; i++)
        {
            var row = (await ReadAsync(connectionString)).FirstOrDefault(r => r.Tool == tool);
            if (row is not null)
            {
                return row;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No record of {tool}.");
    }

    private static async Task<List<Row>> ReadAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT user_id, project_id, tool, access, status, error_code, duration_ms, result_chars FROM tamiza.mcp_calls ORDER BY started_at, id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Row>();
        while (await reader.ReadAsync())
        {
            rows.Add(new Row(
                reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }

        return rows;
    }
}
