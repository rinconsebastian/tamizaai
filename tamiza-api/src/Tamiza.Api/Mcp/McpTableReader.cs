using Microsoft.Extensions.Options;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Mcp;

/// <summary>A published table as the tools describe it.</summary>
public sealed record McpTable(string Name, long? Rows, IReadOnlyList<TableColumn> Columns);

/// <summary>The tables of one published schema and when and by which run they were published; both are null for a schema no run published.</summary>
public sealed record McpPublished(string Source, bool Published, DateTimeOffset? PublishedAt, Guid? RunId, IReadOnlyList<McpTable> Tables);

/// <summary>
/// Reads the tables a project's runs published, whichever schema (design D6). One catalog read inside the dashboards' read-only transaction
/// with their time limits, so a publish never waits for it.
/// </summary>
public sealed class McpTableReader(DataDashboard dashboard, IOptions<DataDashboardOptions> options)
{
    /// <summary>The tables of <paramref name="source"/> (<c>raw</c>, <c>clean</c> or <c>output</c>) with their columns, types, question labels and row counts.</summary>
    public async Task<McpPublished> ReadAsync(Guid projectId, string source, CancellationToken ct)
    {
        var schema = McpSources.SchemaName(projectId, source);
        var step = McpSources.StepOf(source) ?? throw new ArgumentException("The source publishes one table per step.", nameof(source));
        return await dashboard.InTransactionAsync(projectId, options.Value.StatementTimeout, async (connection, transaction, _) =>
        {
            var tables = await TableCatalog.FindAllAsync(connection, transaction, schema, ct);
            var latest = tables.Count == 0 ? null : await DataDashboard.LatestStepAsync(connection, transaction, projectId, step, ct);
            return new McpPublished(
                source,
                tables.Count > 0,
                latest?.FinishedAt,
                latest?.RunId,
                tables.Select(t => new McpTable(t.Key, latest is not null && latest.Rows.TryGetValue(t.Key, out var rows) ? rows : null, t.Value)).ToList());
        }, ct);
    }
}
