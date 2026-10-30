using System.Text.Json;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>Plays the analytics worker for tests: ends a queued script test or run the way the worker records it.</summary>
public static class FakeWorker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A step of a test as the worker writes it. <paramref name="tables"/> are <c>(name, rows, columns, preview rows)</c>.</summary>
    public static object TestStep(
        string step, string status = "succeeded", (string Name, long Rows, (string Name, string Type)[] Columns, object[][]? Preview)[]? tables = null,
        string log = "", string? error = null, object[]? shown = null, object? dashboard = null, int? stoppedAtLine = null, bool reused = false) => new
    {
        step,
        status,
        startedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
        finishedAt = DateTimeOffset.UtcNow,
        inputRows = 480,
        outputRows = tables?.FirstOrDefault().Rows,
        tables = (tables ?? []).Select(t => new
        {
            name = t.Name,
            rows = t.Rows,
            columns = t.Columns.Select(c => new { name = c.Name, type = c.Type }).ToArray(),
            previewRows = t.Preview?.Length ?? 0,
            preview = t.Preview?.Select(r => r.Select(v => JsonSerializer.SerializeToElement(v)).ToArray()).ToArray(),
            cutCells = (object?)null,
        }).ToArray(),
        log,
        error,
        reused,
        stoppedAtLine,
        shown,
        dashboard,
    };

    /// <summary>The facts of an indicator draft's table, as the worker records them.</summary>
    public static object IndicatorView(
        string[] measures, string? defaultMeasure, bool ratio, string[] dimensions, long rows = 12, long nulls = 0, double? max = 0.9,
        long duplicates = 0, Dictionary<string, double>? totals = null, object? charts = null) => new
    {
        measures,
        defaultMeasure,
        ratio,
        dimensions,
        defaultDimension = dimensions.FirstOrDefault(),
        geometry = Array.Empty<string>(),
        rows,
        defaultMeasureNulls = nulls,
        defaultMeasureMax = max,
        totals = totals ?? new Dictionary<string, double> { ["value"] = 4.2 },
        duplicateRows = duplicates,
        charts = charts ?? new Dictionary<string, object>
        {
            ["municipality"] = new { values = 2, groups = new[] { new { group = "Santa Ana", value = 0.9, numerator = 9.0, denominator = 10.0 }, new { group = "San Luis", value = 0.3, numerator = 3.0, denominator = 10.0 } } },
        },
    };

    /// <summary>Ends the project's queued or running test of <paramref name="person"/> (the newest one) with the given steps.</summary>
    public static async Task CompleteTestAsync(string db, Guid projectId, Guid requestedBy, string status, object[] steps, string? error = null, string? errorCode = null)
    {
        await TestData.ExecuteAsync(db,
            """
            UPDATE tamiza.script_tests
            SET status = @status, started_at = now(), finished_at = now(), steps = @steps::jsonb, error = @error, error_code = @code
            WHERE id = (SELECT id FROM tamiza.script_tests WHERE project_id = @p AND requested_by = @u AND status IN ('queued', 'running') ORDER BY queued_at DESC LIMIT 1)
            """,
            ("status", status), ("steps", JsonSerializer.Serialize(steps, Json)), ("error", (object?)error ?? DBNull.Value), ("code", (object?)errorCode ?? DBNull.Value),
            ("p", projectId), ("u", requestedBy));
    }

    /// <summary>Waits until the person has a queued test in the project, as the worker would see it.</summary>
    public static async Task<Guid> WaitForQueuedTestAsync(string db, Guid projectId, Guid requestedBy)
    {
        for (var i = 0; i < 200; i++)
        {
            var id = await TestData.ScalarAsync<Guid?>(db,
                "SELECT id FROM tamiza.script_tests WHERE project_id = @p AND requested_by = @u AND status = 'queued' ORDER BY queued_at DESC LIMIT 1", ("p", projectId), ("u", requestedBy));
            if (id is { } found)
            {
                return found;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("No test was queued.");
    }

    public static async Task<Guid> WaitForQueuedRunAsync(string db, Guid projectId)
    {
        for (var i = 0; i < 200; i++)
        {
            var id = await TestData.ScalarAsync<Guid?>(db,
                "SELECT id FROM tamiza.runs WHERE project_id = @p AND status = 'queued' ORDER BY queued_at DESC LIMIT 1", ("p", projectId));
            if (id is { } found)
            {
                return found;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("No run was queued.");
    }

    /// <summary>Ends a run with the given status and steps <c>(step, name, status, error, log, tables json)</c>.</summary>
    public static async Task CompleteRunAsync(string db, Guid runId, string status, params (string Step, string Name, string Status, string? Error, string Log, string TablesJson)[] steps)
    {
        await TestData.ExecuteAsync(db, "UPDATE tamiza.runs SET status = @status, started_at = now() - interval '5 seconds', finished_at = now() WHERE id = @id", ("status", status), ("id", runId));
        foreach (var (step, name, stepStatus, error, log, tables) in steps)
        {
            await TestData.ExecuteAsync(db,
                """
                INSERT INTO tamiza.run_steps (run_id, step, name, status, started_at, finished_at, input_rows, output_rows, tables, log, error)
                VALUES (@id, @step, @name, @status, now() - interval '4 seconds', now() - interval '2 seconds', 480, 480, @tables::jsonb, @log, @error)
                """,
                ("id", runId), ("step", step), ("name", name), ("status", stepStatus), ("tables", tables), ("log", log), ("error", (object?)error ?? DBNull.Value));
        }
    }
}
