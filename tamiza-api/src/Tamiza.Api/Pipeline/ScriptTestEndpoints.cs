using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Pipeline;

/// <summary>
/// Tests of a draft script on a sample of submissions. The analytics worker runs them ahead of queued runs; nothing is
/// published, and a test is visible only to the person who requested it, for 24 hours after it ends.
/// </summary>
public static partial class ScriptTestEndpoints
{
    public const int MinSampleSize = 1;
    public const int MaxSampleSize = 5000;
    public const int DefaultSampleSize = 500;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public static RouteGroupBuilder MapScriptTestEndpoints(this RouteGroupBuilder api)
    {
        var tests = api.MapGroup("/projects/{projectId:guid}/script-tests").WithTags("Pipeline");
        tests.MapPost("", StartAsync).RequireProjectRole(ProjectRole.Analyst).WithName("StartScriptTest");
        tests.MapGet("/{testId:guid}", GetAsync).RequireProjectRole(ProjectRole.Analyst).WithName("GetScriptTest");
        return api;
    }

    private static async Task<Results<Accepted<ScriptTestDetails>, ValidationProblem>> StartAsync(
        Guid projectId, ScriptTestRequest request, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        if (!Enum.TryParse<ScriptKind>(request.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || int.TryParse(request.Kind, out _))
        {
            errors.Add("kind", "Choose the script to test: clean, output or indicator.");
        }

        string? name = null;
        if (kind == ScriptKind.Indicator)
        {
            name = request.Name;
            if (!ColumnNames.IsValidTableName(name))
            {
                errors.Add("name", "Name the indicator to test, using lowercase letters, digits and underscores, starting with a letter.");
            }
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors.Add("code", "Enter the code to test.");
        }
        else if (Encoding.UTF8.GetByteCount(request.Code) > ScriptEndpoints.MaxCodeBytes)
        {
            errors.Add("code", $"The script is larger than {ScriptEndpoints.MaxCodeBytes / 1024} KB.");
        }

        var sampleSize = request.SampleSize ?? DefaultSampleSize;
        if (sampleSize is < MinSampleSize or > MaxSampleSize)
        {
            errors.Add("sampleSize", $"Enter a sample of {MinSampleSize} to {MaxSampleSize:N0} submissions.");
        }

        if (request.StopAtLine is { } stopAtLine && !string.IsNullOrWhiteSpace(request.Code) && !IsMarkerLine(request.Code, stopAtLine))
        {
            errors.Add("stopAtLine", "Choose a line that starts a cell with # %%.");
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var test = await QueueAsync(db, projectId, user.Id, kind, name, request.Code!, sampleSize, request.StopAtLine, time.GetUtcNow(), ct);
        return TypedResults.Accepted($"/api/v1/projects/{projectId}/script-tests/{test.Id}", await DetailsAsync(db, test, ct));
    }

    /// <summary>Whether line <paramref name="line"/> (1-based) of <paramref name="code"/> starts a cell: after leading spaces or tabs, `# %%`.</summary>
    internal static bool IsMarkerLine(string code, int line)
    {
        var lines = code.Split('\n');
        return line >= 1 && line <= lines.Length && CellMarker().IsMatch(lines[line - 1]);
    }

    [GeneratedRegex(@"^[ \t]*# %%")]
    private static partial Regex CellMarker();

    private static async Task<Results<Ok<ScriptTestDetails>, NotFound>> GetAsync(
        Guid projectId, Guid testId, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var test = await FindOwnAsync(db, projectId, testId, user.Id, time.GetUtcNow(), ct);
        return test is null ? TypedResults.NotFound() : TypedResults.Ok(await DetailsAsync(db, test, ct));
    }

    /// <summary>
    /// The test <paramref name="testId"/> of the project when <paramref name="userId"/> requested it and it has not expired (it is
    /// still running or ended within <see cref="Lifetime"/>); otherwise null, as for a test that never existed.
    /// </summary>
    internal static Task<ScriptTest?> FindOwnAsync(
        TamizaDbContext db, Guid projectId, Guid testId, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var expired = now - Lifetime;
        return db.ScriptTests.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == testId && t.ProjectId == projectId && t.RequestedBy == userId
                                       && (t.FinishedAt == null || t.FinishedAt > expired), ct);
    }

    /// <summary>Replaces the user's queued test in the project, if any, with a new one; a running test is never touched.</summary>
    private static async Task<ScriptTest> QueueAsync(
        TamizaDbContext db, Guid projectId, Guid userId, ScriptKind kind, string? name, string code, int sampleSize, int? stopLine, DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var test = new ScriptTest
            {
                Id = Guid.CreateVersion7(now),
                ProjectId = projectId,
                RequestedBy = userId,
                Kind = kind,
                Name = name,
                Code = code,
                SampleSize = sampleSize,
                StopLine = stopLine,
                Status = RunStatus.Queued,
                QueuedAt = now,
            };
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.ScriptTests.Where(t => t.ProjectId == projectId && t.RequestedBy == userId && t.Status == RunStatus.Queued).ExecuteDeleteAsync(ct);
            db.ScriptTests.Add(test);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return test;
            }
            catch (DbUpdateException exception) when (
                attempt < 3 && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another request of the same user queued a test at the same time; replace that one.
                db.Entry(test).State = EntityState.Detached;
            }
        }
    }

    private static async Task<ScriptTestDetails> DetailsAsync(TamizaDbContext db, ScriptTest test, CancellationToken ct)
    {
        var cleanVersion = test.CleanVersionId is { } versionId
            ? await db.ScriptVersions.Where(v => v.Id == versionId).Select(v => (int?)v.Number).SingleOrDefaultAsync(ct)
            : null;
        return new ScriptTestDetails(
            test.Id, test.Kind, test.Name, test.Status, test.SampleSize, test.StopLine, test.Code, await WaitingForAsync(db, test, ct), cleanVersion,
            test.QueuedAt, test.StartedAt, test.FinishedAt, test.ErrorCode, test.Error, test.Steps);
    }

    /// <summary>What a queued test waits for. The worker is shared by every project and runs one job at a time.</summary>
    private static async Task<ScriptTestWait?> WaitingForAsync(TamizaDbContext db, ScriptTest test, CancellationToken ct)
    {
        if (test.Status != RunStatus.Queued)
        {
            return null;
        }

        if (await db.Runs.AnyAsync(r => r.Status == RunStatus.Running, ct))
        {
            return ScriptTestWait.Run;
        }

        // Queued downloads never hold a test back: the worker takes tests first. Only one being prepared does.
        if (await db.SampleDownloads.AnyAsync(d => d.Status == RunStatus.Running, ct))
        {
            return ScriptTestWait.Download;
        }

        var testAhead = await db.ScriptTests.AnyAsync(t => t.Id != test.Id
            && (t.Status == RunStatus.Running || (t.Status == RunStatus.Queued && t.QueuedAt < test.QueuedAt)), ct);
        return testAhead ? ScriptTestWait.Test : null;
    }
}
