using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Pipeline;

/// <summary>A column of a reference table as scripts see it; <paramref name="Type"/> is <c>integer</c>, <c>decimal</c>, <c>date</c> or <c>text</c>.</summary>
public sealed record InputColumn(string Name, string Type);

/// <summary>A table a script receives: its columns in order and, from the run that published it, its row count (null when unknown).</summary>
public sealed record InputTable(string Name, long? Rows, IReadOnlyList<TableColumn> Columns);

public sealed record InputReferenceTable(string Name, IReadOnlyList<InputColumn> Columns);

/// <summary>A question of the project's current form as scripts see it in <c>ctx.form.questions</c>.</summary>
public sealed record InputQuestion(string Name, string Path, string Type, string? Label);

/// <summary>
/// What the editor shows beside a script. <paramref name="Source"/> is <c>raw</c> or <c>clean</c>; <paramref name="PublishedAt"/> and
/// <paramref name="RunId"/> are null while no run has published that source.
/// </summary>
public sealed record ScriptInputs(
    string Source,
    DateTimeOffset? PublishedAt,
    Guid? RunId,
    IReadOnlyList<InputTable> Tables,
    IReadOnlyList<InputReferenceTable> ReferenceTables,
    IReadOnlyList<InputQuestion> Questions);

/// <summary>
/// The tables the last run published for a script to read: the flattened (raw) tables for script 1 and the clean tables for script 2
/// and the indicator scripts, with the project's reference tables and form questions. A draft's real inputs are in its test.
/// </summary>
public static class ScriptInputEndpoints
{
    public static RouteGroupBuilder MapScriptInputEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/projects/{projectId:guid}/script-inputs").WithTags("Pipeline")
            .MapGet("", GetAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetScriptInputs");
        return api;
    }

    /// <summary>The source and the run step that publishes it, by the kind of script that reads it.</summary>
    private static (string Source, string Step)? SourceOf(string? kind) => kind?.ToLowerInvariant() switch
    {
        "clean" => ("raw", "flatten"),
        "output" or "indicator" => ("clean", "clean"),
        _ => null,
    };

    private static async Task<Results<Ok<ScriptInputs>, ValidationProblem, ProblemHttpResult>> GetAsync(
        Guid projectId, string? kind, DataDashboard dashboard, IOptions<DataDashboardOptions> options, TamizaDbContext db, CancellationToken ct)
    {
        if (SourceOf(kind) is not var (source, step))
        {
            return Problems.Validation("kind", "Choose the script: clean, output or indicator.");
        }

        // The schema name is the project id and one of two fixed words, never request text.
        var schema = $"p_{projectId:N}_{source}";
        try
        {
            var published = await dashboard.InTransactionAsync(projectId, options.Value.StatementTimeout, async (connection, transaction, _) =>
            {
                var tables = await TableCatalog.FindAllAsync(connection, transaction, schema, ct);
                var latest = tables.Count == 0 ? null : await DataDashboard.LatestStepAsync(connection, transaction, projectId, step, ct);
                return (tables, latest);
            }, ct);

            var references = (await db.ReferenceTables.AsNoTracking().Where(t => t.ProjectId == projectId).OrderBy(t => t.Name).ToListAsync(ct))
                .Select(t => new InputReferenceTable(t.Name, t.Columns.Select(c => new InputColumn(c.Name, c.Type)).ToList()))
                .ToList();
            var questions = (await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.FormFields).SingleAsync(ct))
                .Select(f => new InputQuestion(f.Name, f.Xpath, f.Type, f.Label))
                .ToList();

            var (tablesByName, latest) = published;
            var inputs = tablesByName
                .Select(t => new InputTable(t.Key, latest is not null && latest.Rows.TryGetValue(t.Key, out var rows) ? rows : null, t.Value))
                .ToList();
            return TypedResults.Ok(new ScriptInputs(source, latest?.FinishedAt, latest?.RunId, inputs, references, questions));
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }
}
