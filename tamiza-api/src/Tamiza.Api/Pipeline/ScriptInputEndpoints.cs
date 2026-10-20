using Microsoft.AspNetCore.Http.HttpResults;
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
            .MapGet("", GetAsync).RequireProjectRole(ProjectRole.Analyst).WithName("GetScriptInputs");
        return api;
    }

    private static async Task<Results<Ok<ScriptInputs>, ValidationProblem, ProblemHttpResult>> GetAsync(
        Guid projectId, string? kind, ScriptInputsReader reader, CancellationToken ct)
    {
        if (ScriptInputsReader.SourceOf(kind) is null)
        {
            return Problems.Validation("kind", "Choose the script: clean, output or indicator.");
        }

        try
        {
            return TypedResults.Ok(await reader.ReadAsync(projectId, kind!, ct));
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }
}
