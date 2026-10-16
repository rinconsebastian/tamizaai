using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;

namespace Tamiza.Api.Pipeline;

/// <summary>
/// Reads what a script of a given kind receives: the tables the last run published (raw for <c>clean</c>, clean for
/// <c>output</c> and <c>indicator</c>), the project's reference tables and the form's questions. Shared by the script-inputs
/// endpoint and the assistant, so both describe the same schema.
/// </summary>
public sealed class ScriptInputsReader(DataDashboard dashboard, IOptions<DataDashboardOptions> options, TamizaDbContext db)
{
    /// <summary>The source and the run step that publishes it, by the kind of script that reads it; null for an unknown kind.</summary>
    public static (string Source, string Step)? SourceOf(string? kind) => kind?.ToLowerInvariant() switch
    {
        "clean" => ("raw", "flatten"),
        "output" or "indicator" => ("clean", "clean"),
        _ => null,
    };

    /// <summary>
    /// Reads the inputs of a script of <paramref name="kind"/>, which must be one <see cref="SourceOf"/> knows.
    /// Throws <see cref="DataTimeoutException"/> when the catalog read outlasts the statement timeout.
    /// </summary>
    public async Task<ScriptInputs> ReadAsync(Guid projectId, string kind, CancellationToken ct)
    {
        if (SourceOf(kind) is not var (source, step))
        {
            throw new ArgumentException("Unknown script kind.", nameof(kind));
        }

        // The schema name is the project id and one of two fixed words, never request text.
        var schema = $"p_{projectId:N}_{source}";
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

        var (tablesByName, latestRun) = published;
        var inputs = tablesByName
            .Select(t => new InputTable(t.Key, latestRun is not null && latestRun.Rows.TryGetValue(t.Key, out var rows) ? rows : null, t.Value))
            .ToList();
        return new ScriptInputs(source, latestRun?.FinishedAt, latestRun?.RunId, inputs, references, questions);
    }
}
