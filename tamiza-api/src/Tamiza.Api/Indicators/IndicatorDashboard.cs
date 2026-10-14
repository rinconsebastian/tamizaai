using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.Indicators;

/// <summary>Where and when an indicator's table was published; <see cref="Published"/> is false while the table does not exist.</summary>
public sealed record IndicatorPublication(bool Published, DateTimeOffset? PublishedAt, Guid? RunId, long? Rows)
{
    public static readonly IndicatorPublication None = new(false, null, null, null);
}

/// <summary>
/// Reads the tables the indicator scripts published, one per indicator in <c>p_&lt;id&gt;_indicators</c>. It reads through the clean data
/// dashboard's machinery: a read-only transaction on a connection of its own, the table locked against the swap that replaces
/// it, the same time limits and query slots. The schema name is built from the route's project id and a constant, and every
/// table and column name that reaches SQL was returned by the catalog.
/// </summary>
public sealed class IndicatorDashboard(DataDashboard data, IOptions<DataDashboardOptions> dataOptions, IOptions<IndicatorDashboardOptions> options)
{
    public const string Schema = "indicators";

    /// <summary>The name of an indicator's step in <c>run_steps</c>.</summary>
    public const string Step = "indicator";

    private readonly IndicatorDashboardOptions _options = options.Value;

    /// <summary><c>p_&lt;32 hex&gt;_indicators</c>, the name the analytics worker publishes under.</summary>
    public static string SchemaName(Guid projectId) => $"p_{projectId:N}_{Schema}";

    private static string From(Guid projectId, string table) => $"{DataSources.Quote(SchemaName(projectId))}.{DataSources.Quote(table)}";

    /// <summary>
    /// The indicator tables that exist, with the latest succeeded step that published each. A table is replaced only by a succeeded
    /// step, so while it exists that step is the one that published it, whatever failed since.
    /// </summary>
    public Task<IReadOnlyDictionary<string, IndicatorPublication>> GetPublicationsAsync(Guid projectId, CancellationToken ct) =>
        data.InTransactionAsync<IReadOnlyDictionary<string, IndicatorPublication>>(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, _) =>
        {
            var tables = await DataDashboard.TableNamesAsync(connection, transaction, SchemaName(projectId), ct);
            var publications = tables.ToDictionary(t => t, _ => new IndicatorPublication(true, null, null, null), StringComparer.Ordinal);
            if (tables.Count == 0)
            {
                return publications;
            }

            await using var command = new NpgsqlCommand(
                """
                SELECT DISTINCT ON (s.name) s.name, s.run_id, s.finished_at, s.tables
                FROM tamiza.run_steps s JOIN tamiza.runs r ON r.id = s.run_id
                WHERE r.project_id = @project AND s.step = @step AND s.status = 'succeeded'
                ORDER BY s.name, s.finished_at DESC NULLS LAST
                """, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter("project", NpgsqlDbType.Uuid) { Value = projectId });
            command.Parameters.Add(new NpgsqlParameter("step", NpgsqlDbType.Text) { Value = Step });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                if (!publications.ContainsKey(name))
                {
                    continue;
                }

                publications[name] = new IndicatorPublication(
                    true, reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2), reader.GetGuid(1), RowsOf(reader.GetString(3), name));
            }

            return publications;
        }, ct);

    /// <summary>The table's columns, those a widget can plot, and the measure and group a new widget starts with. Not published: none.</summary>
    public Task<IndicatorColumns> GetColumnsAsync(Guid projectId, string indicator, CancellationToken ct) =>
        data.InTransactionAsync(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, _) =>
        {
            var columns = await DataDashboard.LockAndDescribeAsync(connection, transaction, SchemaName(projectId), indicator, ct);
            return columns is null
                ? new IndicatorColumns(false, [], [], new ColumnDefaults(null, null))
                : new IndicatorColumns(
                    true,
                    columns,
                    columns.Where(IndicatorValidation.IsNumeric).Select(c => c.Name).ToList(),
                    new ColumnDefaults(IndicatorQueryBuilder.DefaultMeasure(columns), IndicatorQueryBuilder.DefaultGroupBy(columns)));
        }, ct);

    /// <summary>
    /// Checks a widget's query against its indicator's table, adding to <paramref name="errors"/>. Returns false, and checks
    /// nothing, while the table is not published: a widget may be configured before its indicator has published.
    /// </summary>
    public Task<bool> CheckConfigAgainstTableAsync(Guid projectId, WidgetConfig config, string prefix, FieldErrors errors, CancellationToken ct) =>
        data.InTransactionAsync(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, project) =>
        {
            var columns = await TableCatalog.FindAsync(connection, transaction, SchemaName(projectId), config.Indicator, ct);
            if (columns is null)
            {
                return false;
            }

            IndicatorValidation.CheckAgainstColumns(config, columns, project.TimeZone, prefix, errors);
            return true;
        }, ct);

    /// <summary>
    /// Runs a widget's query. The response says that the indicator is not published, or lists what the query names that the table
    /// does not have, instead of failing; a request that is malformed is returned as validation errors. Throws
    /// <see cref="DataTimeoutException"/> when the read takes too long.
    /// </summary>
    public async Task<IndicatorQueryOutcome> QueryAsync(Guid projectId, WidgetConfigInput input, string? chartType, CancellationToken ct)
    {
        var config = input.Normalize();
        var errors = new FieldErrors();
        IndicatorValidation.CheckConfig(config, ChartTypes.IsKnown(chartType) ? chartType : null, prefix: "", errors);
        if (errors.Any)
        {
            return new IndicatorQueryOutcome(null, errors.ToDictionary());
        }

        var maxRows = _options.MaxWidgetQueryRows;
        return await data.WithSlotAsync(() => data.InTransactionAsync(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, project) =>
        {
            var columns = await DataDashboard.LockAndDescribeAsync(connection, transaction, SchemaName(projectId), config.Indicator, ct);
            if (columns is null)
            {
                return new IndicatorQueryOutcome(new IndicatorQueryResponse(false, [], false, new MissingReferences(true, [])), null);
            }

            var plan = IndicatorQueryBuilder.Plan(
                config, columns, project.TimeZone, From(projectId, config.Indicator), maxRows, perRow: chartType == ChartTypes.Histogram);
            if (plan.Errors is not null)
            {
                return new IndicatorQueryOutcome(null, plan.Errors);
            }

            if (plan.Sql is null)
            {
                return new IndicatorQueryOutcome(new IndicatorQueryResponse(true, [], false, new MissingReferences(false, plan.MissingColumns)), null);
            }

            await data.SleepForTestsAsync(connection, transaction, ct);
            var group = columns.FirstOrDefault(c => c.Name == config.GroupBy);
            var (rows, limitReached) = await ReadRowsAsync(connection, transaction, plan.Sql, group?.Type, maxRows, ct);
            return new IndicatorQueryOutcome(new IndicatorQueryResponse(true, rows, limitReached, MissingReferences.None), null);
        }, ct), ct);
    }

    /// <summary>
    /// Writes the whole published table of one indicator to a file and registers its single-use link. Returns null when the table is
    /// not published. Throws what <see cref="DataDashboard.ExportAsync"/> throws.
    /// </summary>
    public Task<DataExportOutcome?> ExportAsync(Guid projectId, Guid userId, string indicator, string format, CancellationToken ct) =>
        data.ExportFromSchemaAsync(projectId, userId, SchemaName(projectId), indicator, new DataExportRequest(format), ct);

    private static long? RowsOf(string tablesJson, string name)
    {
        using var tables = JsonDocument.Parse(tablesJson);
        foreach (var table in tables.RootElement.EnumerateArray())
        {
            if (table.TryGetProperty("name", out var tableName) && tableName.GetString() == name
                && table.TryGetProperty("rows", out var count) && count.TryGetInt64(out var rows))
            {
                return rows;
            }
        }

        return null;
    }

    private static async Task<(List<IndicatorRow> Rows, bool LimitReached)> ReadRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IndicatorSql sql, DataColumnType? groupType, int maxRows, CancellationToken ct)
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

            var value = reader.GetDouble(1);
            if (!double.IsFinite(value))
            {
                continue; // NaN and infinity have no number in JSON
            }

            var group = reader.IsDBNull(0) ? null : reader.GetString(0);
            rows.Add(new IndicatorRow(
                group is not null && groupType == DataColumnType.Timestamp ? DataValues.IsoTimestamp(group) : group,
                value,
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return (rows, limitReached);
    }
}
