using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.Indicators;

/// <summary>Where and when the project's indicator table was published; <see cref="Published"/> is false while script 3 has never published.</summary>
public sealed record IndicatorPublication(bool Published, DateTimeOffset? PublishedAt, Guid? RunId);

/// <summary>
/// Reads the table script 3 published, <c>p_&lt;id&gt;_indicators.indicators</c> (design D2 to D4). It reads through the clean data
/// dashboard's machinery: a read-only transaction on a connection of its own, the table locked against the swap that replaces
/// it, the same time limits and query slots. The schema name is built from the route's project id and a constant, and every
/// column name that reaches SQL was returned by the catalog.
/// </summary>
public sealed class IndicatorDashboard(DataDashboard data, IOptions<DataDashboardOptions> dataOptions, IOptions<IndicatorDashboardOptions> options)
{
    public const string Step = "indicators";
    public const string Table = "indicators";

    /// <summary>The most indicators the column catalog lists.</summary>
    public const int MaxListedIndicators = 500;

    private readonly IndicatorDashboardOptions _options = options.Value;

    /// <summary><c>p_&lt;32 hex&gt;_indicators</c>, the name the analytics worker publishes under.</summary>
    public static string SchemaName(Guid projectId) => $"p_{projectId:N}_{Step}";

    private static string From(Guid projectId) => $"{DataSources.Quote(SchemaName(projectId))}.{DataSources.Quote(Table)}";

    /// <summary>Whether the table is published and, if so, by which run and when (resolved as the clean data dashboard does).</summary>
    public Task<IndicatorPublication> GetPublicationAsync(Guid projectId, CancellationToken ct) =>
        data.InTransactionAsync(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, _) =>
        {
            if (await TableCatalog.FindAsync(connection, transaction, SchemaName(projectId), Table, ct) is null)
            {
                return new IndicatorPublication(false, null, null);
            }

            var step = await DataDashboard.LatestStepAsync(connection, transaction, projectId, Step, ct);
            return new IndicatorPublication(true, step?.FinishedAt, step?.RunId);
        }, ct);

    /// <summary>The table's columns and the indicators it holds. Not published: no columns and no indicators.</summary>
    public Task<IndicatorColumns> GetColumnsAsync(Guid projectId, CancellationToken ct) =>
        data.InTransactionAsync(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, _) =>
        {
            var columns = await DataDashboard.LockAndDescribeAsync(connection, transaction, SchemaName(projectId), Table, ct);
            if (columns is null)
            {
                return new IndicatorColumns(false, [], []);
            }

            var indicators = columns.Any(c => c.Name == IndicatorQueryBuilder.IndicatorColumn)
                ? await ListIndicatorsAsync(connection, transaction, projectId, ct)
                : [];
            return new IndicatorColumns(true, columns, indicators);
        }, ct);

    /// <summary>
    /// Checks a widget's query against the published table's columns, adding to <paramref name="errors"/>. Returns false, and
    /// checks nothing, while the table is not published: a widget may be configured before script 3 has published.
    /// </summary>
    public Task<bool> CheckConfigAgainstTableAsync(Guid projectId, WidgetConfig config, string prefix, FieldErrors errors, CancellationToken ct) =>
        data.InTransactionAsync(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, project) =>
        {
            var columns = await TableCatalog.FindAsync(connection, transaction, SchemaName(projectId), Table, ct);
            if (columns is null)
            {
                return false;
            }

            IndicatorValidation.CheckAgainstColumns(config, columns, project.TimeZone, prefix, errors);
            return true;
        }, ct);

    /// <summary>
    /// Runs a widget's query (design D2, D8, D9). The response lists what the query names that the table does not have instead of
    /// failing; a request that is malformed is returned as validation errors. Throws <see cref="DataTimeoutException"/> when the
    /// read takes too long.
    /// </summary>
    public async Task<IndicatorQueryOutcome> QueryAsync(Guid projectId, WidgetConfigInput input, CancellationToken ct)
    {
        var config = input.Normalize();
        var errors = new FieldErrors();
        IndicatorValidation.CheckConfig(config, chartType: null, prefix: "", errors);
        if (errors.Any)
        {
            return new IndicatorQueryOutcome(null, errors.ToDictionary());
        }

        var maxRows = _options.MaxWidgetQueryRows;
        return await data.WithSlotAsync(() => data.InTransactionAsync(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, project) =>
        {
            var columns = await DataDashboard.LockAndDescribeAsync(connection, transaction, SchemaName(projectId), Table, ct);
            if (columns is null)
            {
                return new IndicatorQueryOutcome(new IndicatorQueryResponse(false, [], false, MissingReferences.None), null);
            }

            var plan = IndicatorQueryBuilder.Plan(config, columns, project.TimeZone, From(projectId), maxRows);
            if (plan.Errors is not null)
            {
                return new IndicatorQueryOutcome(null, plan.Errors);
            }

            var group = columns.FirstOrDefault(c => c.Name == config.GroupBy);
            var missingIndicators = columns.Any(c => c.Name == IndicatorQueryBuilder.IndicatorColumn)
                ? await MissingIndicatorsAsync(connection, transaction, projectId, config.Indicators, ct)
                : [];
            if (plan.Sql is null || missingIndicators.Count > 0)
            {
                return new IndicatorQueryOutcome(
                    new IndicatorQueryResponse(true, [], false, new MissingReferences(missingIndicators, plan.MissingColumns)), null);
            }

            await data.SleepForTestsAsync(connection, transaction, ct);
            var (rows, limitReached) = await ReadRowsAsync(connection, transaction, plan.Sql, group!.Type, maxRows, ct);
            return new IndicatorQueryOutcome(new IndicatorQueryResponse(true, rows, limitReached, MissingReferences.None), null);
        }, ct), ct);
    }

    /// <summary>
    /// Writes the whole published table to a file and registers its single-use link (design D4). Returns null when the table is not
    /// published. Throws what <see cref="DataDashboard.ExportAsync"/> throws.
    /// </summary>
    public Task<DataExportOutcome?> ExportAsync(Guid projectId, Guid userId, string format, CancellationToken ct) =>
        data.ExportFromSchemaAsync(projectId, userId, SchemaName(projectId), Table, new DataExportRequest(format), ct);

    private static async Task<List<string>> ListIndicatorsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid projectId, CancellationToken ct)
    {
        var indicator = DataSources.Quote(IndicatorQueryBuilder.IndicatorColumn);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT DISTINCT {indicator}::text COLLATE "C" FROM {From(projectId)}
            WHERE {indicator} IS NOT NULL ORDER BY 1 LIMIT {MaxListedIndicators}
            """, connection, transaction);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    /// <summary>The requested indicators that are not among the distinct values of the <c>indicator</c> column.</summary>
    private static async Task<List<string>> MissingIndicatorsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid projectId, IReadOnlyList<string> requested, CancellationToken ct)
    {
        var indicator = DataSources.Quote(IndicatorQueryBuilder.IndicatorColumn);
        await using var command = new NpgsqlCommand(
            $"SELECT DISTINCT {indicator}::text FROM {From(projectId)} WHERE {indicator}::text = ANY(@requested)", connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("requested", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = requested.ToArray() });
        var found = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            found.Add(reader.GetString(0));
        }

        return requested.Distinct(StringComparer.Ordinal).Where(i => !found.Contains(i)).ToList();
    }

    private static async Task<(List<IndicatorRow> Rows, bool LimitReached)> ReadRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IndicatorSql sql, DataColumnType groupType, int maxRows, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql.Sql, connection, transaction);
        foreach (var parameter in sql.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        var rows = new List<IndicatorRow>();
        var limitReached = false;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count == maxRows)
            {
                limitReached = true;
                break;
            }

            var sum = reader.GetDouble(2);
            if (!double.IsFinite(sum))
            {
                continue; // NaN and infinity have no number in JSON
            }

            var group = reader.IsDBNull(1) ? null : reader.GetString(1);
            rows.Add(new IndicatorRow(
                reader.GetString(0),
                group is not null && groupType == DataColumnType.Timestamp ? DataValues.IsoTimestamp(group) : group,
                sum,
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return (rows, limitReached);
    }
}
