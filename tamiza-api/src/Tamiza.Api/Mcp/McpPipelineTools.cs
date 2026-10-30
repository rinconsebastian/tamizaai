using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>Trying a draft on a sample of the submissions, and running the whole chain that publishes the tables.</summary>
[McpServerToolType]
public sealed class McpPipelineTools(
    McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options, TamizaDbContext db, ICurrentUser user, TimeProvider time, RunQueue runs,
    McpMaskerFactory maskers) : McpToolBase(gate, call, options)
{
    private const string WaitHelp = "Seconds to wait for the result, 0 to 120; 50 when omitted. The call returns as soon as it is done.";

    [McpServerTool(Name = "test_script", Title = "Test a draft script", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Runs a draft on a sample of the stored submissions (500 by default) without publishing or saving anything, exactly as the script editor's Test does, and waits for the result. " +
                 "For an indicator the result also holds how its table will look on a dashboard (measures, dimensions, totals, up to 50 groups per chart) and warnings such as empty values or duplicate rows. " +
                 "At summaries-only access it holds table shapes and masked errors, never rows. If it is not done after the wait it says so: call get_test with its testId. " +
                 "A new test replaces your own queued test in the project. Code that imports os, subprocess, socket and similar, calls eval, exec or open, or holds a web address is refused.")]
    public Task<CallToolResult> TestScript(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("clean (script 1), output (script 2) or indicator.")] string kind,
        [Description("The whole draft.")] string code,
        [Description("The indicator's name. Only for kind indicator.")] string? name = null,
        [Description("Optional. Submissions to sample, 1 to 5000; 500 when omitted.")] int? sampleSize = null,
        [Description("Optional. The line of a '# %%' marker to run through, to try only the first cells.")] int? stopAtLine = null,
        [Description("Optional. The indicator's format (count, decimal, percent, currency), to warn about a percent above 1.")] string? format = null,
        [Description("Optional. Preview rows per table when rows are allowed, 1 to 100; 20 when omitted.")] int? previewRows = null,
        [Description(WaitHelp)] int? waitSeconds = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var project = await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            if (UnsafeCode.RefusalFor(code) is { } unsafeCode)
            {
                throw unsafeCode;
            }

            var started = McpResults.Unwrap<ScriptTestDetails>(
                await ScriptTestEndpoints.StartAsync(projectId, new ScriptTestRequest(kind, name, code, sampleSize, stopAtLine), db, user, time, ct));
            return await ReadTestAsync(projectId, started.Id, project, McpWait.Seconds(waitSeconds), previewRows, format, progress, ct);
        }, hint: "Ask for fewer preview rows with previewRows.");

    [McpServerTool(Name = "get_test", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Read a script test"),
     Description("Reads a test you started with test_script, waiting up to waitSeconds for it to end. Only the person who started a test can read it, for 24 hours.")]
    public Task<CallToolResult> GetTest(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("The testId test_script returned.")] Guid testId,
        [Description("Optional. The indicator's format, to warn about a percent above 1.")] string? format = null,
        [Description("Optional. Preview rows per table when rows are allowed, 1 to 100; 20 when omitted.")] int? previewRows = null,
        [Description(WaitHelp)] int? waitSeconds = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var project = await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            return await ReadTestAsync(projectId, testId, project, McpWait.Seconds(waitSeconds), previewRows, format, progress, ct);
        }, hint: "Ask for fewer preview rows with previewRows.");

    [McpServerTool(Name = "start_run", Title = "Run the chain", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Queues a run of the project's saved scripts, as 'Run now' does: it flattens the submissions, runs script 1 and 2 when they exist, then every indicator, and publishes the tables. " +
                 "Dashboards show the new tables when it succeeds. Waits for the result; if it is not done after the wait, call get_run with waitSeconds. Refused when a run of the project is already waiting to start.")]
    public Task<CallToolResult> StartRun(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description(WaitHelp)] int? waitSeconds = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var project = await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var queued = McpResults.Unwrap<RunSummary>(await RunEndpoints.StartAsync(projectId, db, runs, user, time, ct));
            return await ReadRunAsync(projectId, queued.Id, project, McpWait.Seconds(waitSeconds), progress, ct);
        });

    [McpServerTool(Name = "get_run", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Read a run"),
     Description("A run of the pipeline with its status, what triggered it, the script versions it used and each step with its status, duration, row counts, tables and error. " +
                 "The latest run when runId is omitted. Waits up to waitSeconds for a queued or running run to end.")]
    public Task<CallToolResult> GetRun(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("Optional. A run's id; the latest run when omitted.")] Guid? runId = null,
        [Description(WaitHelp)] int? waitSeconds = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var project = await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var id = runId ?? await db.Runs.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.QueuedAt).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct)
                ?? throw McpRefusal.NotFound("A run", "The project has not run yet; save the scripts and call start_run.");
            return await ReadRunAsync(projectId, id, project, McpWait.Seconds(waitSeconds), progress, ct);
        });

    private async Task<object> ReadTestAsync(
        Guid projectId, Guid testId, McpProject project, int seconds, int? previewRows, string? format, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
    {
        var details = await McpWait.UntilAsync(async token =>
        {
            var test = await ScriptTestEndpoints.FindOwnAsync(db, projectId, testId, user.Id, time.GetUtcNow(), token)
                ?? throw McpRefusal.NotFound("The test", "Tests last 24 hours after they end, and only the person who started one can read it.");
            var current = await ScriptTestEndpoints.DetailsAsync(db, test, token);
            var done = current.Status is not (RunStatus.Queued or RunStatus.Running);
            return (current, done, current.WaitingFor is { } waiting ? $"waiting for a {waiting.ToString().ToLowerInvariant()}" : $"the test is {current.Status.ToString().ToLowerInvariant()}");
        }, seconds, progress, ct);

        var finished = details.Status is not (RunStatus.Queued or RunStatus.Running);
        var masker = project.CanSeeRows ? null : await maskers.ForTestAsync(projectId, details.Kind, details.Name, details.Code, ct);
        var preview = Math.Clamp(previewRows ?? McpTestView.DefaultPreviewRows, 1, McpTestView.MaxPreviewRows);
        return McpTestView.From(details, project.Level, masker, preview, format, finished);
    }

    private async Task<object> ReadRunAsync(Guid projectId, Guid runId, McpProject project, int seconds, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
    {
        var details = await McpWait.UntilAsync(async token =>
        {
            var current = McpResults.Unwrap<RunDetails>(await RunEndpoints.GetAsync(projectId, runId, db, token));
            var done = current.Run.Status is not (RunStatus.Queued or RunStatus.Running);
            return (current, done, $"the run is {current.Run.Status.ToString().ToLowerInvariant()}, {current.Steps.Count} steps done");
        }, seconds, progress, ct);

        var rows = project.CanSeeRows;
        var masker = rows ? null : await maskers.ForRunAsync(projectId, ct);
        var done = details.Run.Status is not (RunStatus.Queued or RunStatus.Running);
        return new
        {
            done,
            next = done ? null : $"The run is {details.Run.Status.ToString().ToLowerInvariant()}. Call get_run with this runId and waitSeconds to read it when it ends.",
            runId = details.Run.Id,
            details.Run.Status,
            details.Run.Trigger,
            requestedBy = details.Run.RequestedBy?.Name,
            details.Run.QueuedAt,
            details.Run.StartedAt,
            details.Run.FinishedAt,
            details.Run.Versions,
            errorCode = details.Run.ErrorCode,
            error = RunError(details.Run.Error, rows, masker),
            steps = details.Steps.Select(s => new
            {
                step = s.Step,
                s.Name,
                s.Label,
                s.Status,
                s.StartedAt,
                s.FinishedAt,
                s.InputRows,
                s.OutputRows,
                tables = s.Tables,
                error = RunError(s.Error, rows, masker),
                log = RunLog(s.Log, rows, masker),
            }).ToList(),
            referenceTables = details.ReferenceTables,
            withheld = rows ? null : "Errors and log lines are masked at summaries-only access: words that could be a respondent's data appear as …",
        };
    }

    private static string? RunError(string? error, bool rows, Assistant.Masker? masker)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        if (rows)
        {
            return error;
        }

        var parsed = Assistant.TestFacts.ParseError(error);
        if (!parsed.HasTraceback)
        {
            return string.Join('\n', parsed.Message.Split('\n').Take(Assistant.TestFacts.MaxMessageLines).Select(l => masker!.Mask(l.TrimEnd())));
        }

        var head = parsed.Exception is null ? "the script failed" : masker!.Mask(parsed.Exception);
        return string.Join('\n', new[] { head }.Concat(parsed.Frames.Select(f => $"line {f.Line}{(f.Function is null ? "" : " in " + masker!.Mask(f.Function))}")));
    }

    private static object? RunLog(string? log, bool rows, Assistant.Masker? masker)
    {
        if (string.IsNullOrEmpty(log))
        {
            return null;
        }

        if (rows)
        {
            return log.Replace("\r\n", "\n").Split('\n').TakeLast(200).ToList();
        }

        var leveled = Assistant.TestFacts.LogLines(log);
        return leveled.Count == 0 ? null : leveled.Select(l => $"{l.Level} {masker!.Mask(l.Text)}").ToList();
    }
}
