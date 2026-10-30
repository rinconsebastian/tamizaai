using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.Data;

namespace Tamiza.Api.CleanData;

/// <summary>A published table; <see cref="RowCount"/> is what the run that published it recorded, or null when no run did.</summary>
public sealed record DataTableInfo(string Name, long? RowCount);

/// <summary>
/// The tables a source holds and when and by which run they were published. <see cref="RunId"/> and <see cref="PublishedAt"/>
/// are null only for tables no recorded run published.
/// </summary>
public sealed record DataSourceInfo(string Source, DateTimeOffset? PublishedAt, Guid? RunId, IReadOnlyList<DataTableInfo> Tables);

/// <param name="Sources">The published sources, output first. Empty when no run has published clean or output tables.</param>
/// <param name="DefaultSource">The source the dashboard opens on: the output tables when published, otherwise the clean tables.</param>
/// <param name="TimeZone">The project's zone, in which timestamps are shown and filtered by day.</param>
public sealed record DataOverview(IReadOnlyList<DataSourceInfo> Sources, string? DefaultSource, string TimeZone);

/// <summary>One page of a table. <see cref="Rows"/> hold the values in the order of <see cref="Columns"/>: numbers, true/false, or text (days as <c>YYYY-MM-DD</c>, timestamps as ISO 8601 with the project's offset).</summary>
/// <param name="CutCells">The <c>[row, column]</c> of each text value cut at 500 characters.</param>
/// <param name="Total">The rows that match the filters.</param>
public sealed record DataPage(
    IReadOnlyList<TableColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    IReadOnlyList<int[]> CutCells,
    long Total,
    int Page,
    int PageSize,
    DateTimeOffset? PublishedAt,
    Guid? RunId,
    string TimeZone);

/// <summary>The outcome of a query: the page, or the validation errors of the request. Null from the service means the table is not published.</summary>
public sealed record DataQueryResult(DataPage? Page, IDictionary<string, string[]>? Errors);

/// <summary>A value of a column and how many rows hold it.</summary>
public sealed record DataValueCount(string Value, long Count);

public enum DataValuesProblem
{
    ColumnNotFound,
    Unsupported,
}

/// <summary>The values of a column, or the reason there are none to list.</summary>
public sealed record DataValuesResult(IReadOnlyList<DataValueCount>? Values, DataValuesProblem? Problem);

/// <summary>What a read needs to know about the project it reads for.</summary>
public sealed record DataProject(string Name, string TimeZone);

/// <summary>A read of the published tables was stopped by the time limit, or waited too long for a slot or a lock.</summary>
public sealed class DataTimeoutException(Exception? inner = null) : Exception("Reading the published tables took too long.", inner)
{
    public const string Code = "data.timeout";
}

/// <summary>
/// Reads the tables the project's runs published (design D1 to D3). Every read runs in its own scope and connection, in a read-only
/// transaction whose time limits and time zone are set with <c>set_config</c> (<c>SET</c> takes no parameters). One instance
/// serves the process: it owns the slots.
/// </summary>
public sealed partial class DataDashboard : IDisposable
{
    /// <summary>Values offered to the "is one of" filter.</summary>
    public const int MaxFrequentValues = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly DataDashboardOptions _options;
    private readonly ExportRegistry _exports;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _exportSlots;

    public DataDashboard(IServiceScopeFactory scopes, IOptions<DataDashboardOptions> options, ExportRegistry exports, TimeProvider time)
    {
        _scopes = scopes;
        _options = options.Value;
        _exports = exports;
        _time = time;
        _slots = new SemaphoreSlim(_options.MaxConcurrentQueries, _options.MaxConcurrentQueries);
        _exportSlots = new SemaphoreSlim(_options.MaxConcurrentExports, _options.MaxConcurrentExports);
    }

    public void Dispose()
    {
        _slots.Dispose();
        _exportSlots.Dispose();
    }

    /// <summary>The published sources and their tables (design D1). A source is published when its schema exists.</summary>
    public Task<DataOverview> GetOverviewAsync(Guid projectId, CancellationToken ct) =>
        InTransactionAsync(projectId, _options.StatementTimeout, async (connection, transaction, project) =>
        {
            var schemas = DataSources.All.ToDictionary(s => s, s => DataSources.SchemaName(projectId, s));
            var existing = await ExistingSchemasAsync(connection, transaction, schemas.Values.ToArray(), ct);

            var sources = new List<DataSourceInfo>();
            foreach (var source in DataSources.All.Where(s => existing.Contains(schemas[s])))
            {
                var names = await TableNamesAsync(connection, transaction, schemas[source], ct);
                var step = await LatestStepAsync(connection, transaction, projectId, source, ct);
                sources.Add(new DataSourceInfo(
                    source, step?.FinishedAt, step?.RunId,
                    names.Select(n => new DataTableInfo(n, step is not null && step.Rows.TryGetValue(n, out var rows) ? rows : null)).ToList()));
            }

            return new DataOverview(sources, sources.FirstOrDefault()?.Source, project.TimeZone);
        }, ct);

    /// <summary>
    /// One page of a published table with the filters and sort applied (design D3). Returns null when the source or table is not
    /// published, and the request's validation errors when it names a column the table lacks or a value of the wrong type.
    /// Throws <see cref="DataTimeoutException"/> when the page takes too long.
    /// </summary>
    public async Task<DataQueryResult?> QueryAsync(Guid projectId, string source, string table, DataQueryRequest request, CancellationToken ct)
    {
        if (!DataSources.IsKnown(source))
        {
            return null;
        }

        return await QuerySchemaAsync(projectId, DataSources.SchemaName(projectId, source), source, table, request, ct);
    }

    /// <summary>
    /// <see cref="QueryAsync"/> for any published schema, for the callers that are allowed to read the ones the dashboard does not offer
    /// (the MCP server). <paramref name="schema"/> is built by the caller from the project id and a fixed word, never from request text;
    /// <paramref name="stepName"/> is the run step whose publication the page reports.
    /// </summary>
    internal async Task<DataQueryResult?> QuerySchemaAsync(
        Guid projectId, string schema, string stepName, string table, DataQueryRequest request, CancellationToken ct)
    {
        return await WithSlotAsync(() => InTransactionAsync(projectId, _options.StatementTimeout, async (connection, transaction, project) =>
        {
            var columns = await LockAndDescribeAsync(connection, transaction, schema, table, ct);
            if (columns is null)
            {
                return null;
            }

            var errors = new Http.FieldErrors();
            var query = DataQueryBuilder.Build(request, columns, project.TimeZone, errors);
            if (query is null)
            {
                return new DataQueryResult(null, errors.ToDictionary());
            }

            await SleepForTestsAsync(connection, transaction, ct);
            var from = $"{DataSources.Quote(schema)}.{DataSources.Quote(table)}";
            var total = await CountAsync(connection, transaction, from, query, ct);
            var (rows, cutCells) = await ReadPageAsync(connection, transaction, from, columns, query, ct);
            var step = await LatestStepAsync(connection, transaction, projectId, stepName, ct);
            return new DataQueryResult(new DataPage(columns, rows, cutCells, total, query.Page, query.PageSize, step?.FinishedAt, step?.RunId, project.TimeZone), null);
        }, ct), ct);
    }

    /// <summary>
    /// The most frequent values of a text or true/false column, for the "is one of" filter. It ignores the other filters, so the
    /// list does not change as filters are added. Values longer than the 500 characters a filter accepts are left out. Returns
    /// null when the table is not published.
    /// </summary>
    /// <param name="search">Restricts the list to values that contain this text, ignoring case.</param>
    public async Task<DataValuesResult?> ValuesAsync(Guid projectId, string source, string table, string column, string? search, CancellationToken ct)
    {
        if (!DataSources.IsKnown(source))
        {
            return null;
        }

        var schema = DataSources.SchemaName(projectId, source);
        return await WithSlotAsync(() => InTransactionAsync(projectId, _options.StatementTimeout, async (connection, transaction, _) =>
        {
            var columns = await LockAndDescribeAsync(connection, transaction, schema, table, ct);
            if (columns is null)
            {
                return null;
            }

            var found = columns.FirstOrDefault(c => string.Equals(c.Name, column, StringComparison.Ordinal));
            if (found is null)
            {
                return new DataValuesResult(null, DataValuesProblem.ColumnNotFound);
            }

            if (found.Type is not (DataColumnType.Text or DataColumnType.Boolean))
            {
                return new DataValuesResult(null, DataValuesProblem.Unsupported);
            }

            await SleepForTestsAsync(connection, transaction, ct);
            var quoted = DataSources.Quote(found.Name);
            var text = $"{quoted}::text";
            var matching = string.IsNullOrEmpty(search) ? "" : $" AND {text} ILIKE '%' || @search || '%' ESCAPE '\\'";
            await using var command = new NpgsqlCommand(
                $"""
                SELECT {text}, count(*) FROM {DataSources.Quote(schema)}.{DataSources.Quote(table)}
                WHERE {quoted} IS NOT NULL AND char_length({text}) <= {DataQueryBuilder.MaxTextLength}{matching}
                GROUP BY {text} ORDER BY count(*) DESC, {text} LIMIT {MaxFrequentValues}
                """, connection, transaction);
            if (matching.Length > 0)
            {
                command.Parameters.Add(new NpgsqlParameter("search", NpgsqlDbType.Text) { Value = DataQueryBuilder.EscapeLike(search!) });
            }

            var values = new List<DataValueCount>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                values.Add(new DataValueCount(reader.GetString(0), reader.GetInt64(1)));
            }

            return new DataValuesResult(values, null);
        }, ct), ct);
    }

    /// <summary>
    /// Describes the table and locks it against the swap that publishes new tables. The first lookup only finds out that the name
    /// is a published table; once it is locked it cannot be replaced until this transaction ends, so a second lookup gives the
    /// columns every statement that follows will see.
    /// </summary>
    internal static async Task<IReadOnlyList<TableColumn>?> LockAndDescribeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, string table, CancellationToken ct)
    {
        if (await TableCatalog.FindAsync(connection, transaction, schema, table, ct) is null)
        {
            return null;
        }

        try
        {
            // The name was returned by the catalog lookup above (it equals the request's text exactly), and is quoted.
            await using var lockTable = new NpgsqlCommand(
                $"LOCK TABLE {DataSources.Quote(schema)}.{DataSources.Quote(table)} IN ACCESS SHARE MODE", connection, transaction);
            await lockTable.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null; // replaced by a run in between
        }

        return await TableCatalog.FindAsync(connection, transaction, schema, table, ct);
    }

    internal async Task SleepForTestsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        if (_options.TestStatementSleep <= TimeSpan.Zero)
        {
            return;
        }

        await using var sleep = new NpgsqlCommand("SELECT pg_sleep(@seconds)", connection, transaction);
        sleep.Parameters.Add(new NpgsqlParameter("seconds", NpgsqlDbType.Double) { Value = _options.TestStatementSleep.TotalSeconds });
        await sleep.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<long> CountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string from, DataQuery query, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {from}{query.WhereClause}", connection, transaction);
        foreach (var parameter in query.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<(List<object?[]> Rows, List<int[]> CutCells)> ReadPageAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string from, IReadOnlyList<TableColumn> columns, DataQuery query, CancellationToken ct)
    {
        var select = string.Join(", ", columns.Select(SelectExpression));

        // The inner query orders and pages only the row positions (ctid) and the sort key, which are narrow; ordering whole rows of
        // a table of 60 columns would sort, and spill to disk, every row to return the first fifty. The outer query then reads just
        // the rows of the page by position and puts them in order again. The table is locked, so positions cannot move.
        await using var command = new NpgsqlCommand(
            $"""
            SELECT {select} FROM {from}
            WHERE ctid = ANY (ARRAY(SELECT ctid FROM {from}{query.WhereClause} ORDER BY {query.OrderBy} LIMIT @limit OFFSET @offset))
            ORDER BY {query.OrderBy}
            """, connection, transaction);
        foreach (var parameter in query.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = query.PageSize });
        command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Bigint) { Value = query.Offset });

        var rows = new List<object?[]>();
        var cutCells = new List<int[]>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                if (reader.IsDBNull(i))
                {
                    continue;
                }

                row[i] = ReadValue(reader, i, columns[i].Type, out var cut);
                if (cut)
                {
                    cutCells.Add([rows.Count, i]);
                }
            }

            rows.Add(row);
        }

        return (rows, cutCells);
    }

    /// <summary>What the page selects for a column. Numbers, days and timestamps come as text (see <see cref="DataValues"/>); text is cut in SQL.</summary>
    private static string SelectExpression(TableColumn column)
    {
        var quoted = DataSources.Quote(column.Name);
        return column.Type switch
        {
            DataColumnType.Integer or DataColumnType.Boolean => quoted,
            DataColumnType.Decimal or DataColumnType.Date or DataColumnType.Timestamp => $"{quoted}::text",
            _ => $"left({quoted}::text, {DataValues.FetchLength})",
        };
    }

    private static object ReadValue(NpgsqlDataReader reader, int ordinal, DataColumnType type, out bool cut)
    {
        cut = false;
        switch (type)
        {
            case DataColumnType.Integer:
                return Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
            case DataColumnType.Boolean:
                return reader.GetBoolean(ordinal);
            case DataColumnType.Decimal:
                return DataValues.Number(reader.GetString(ordinal));
            case DataColumnType.Date:
                return reader.GetString(ordinal);
            case DataColumnType.Timestamp:
                return DataValues.IsoTimestamp(reader.GetString(ordinal));
            default:
                var (text, wasCut) = DataValues.Display(reader.GetString(ordinal));
                cut = wasCut;
                return text;
        }
    }

    /// <summary>Runs one read in one of the query slots; waiting longer than the queue timeout fails as too long.</summary>
    internal async Task<T> WithSlotAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        if (!await _slots.WaitAsync(_options.QueueTimeout, ct))
        {
            throw new DataTimeoutException();
        }

        try
        {
            return await work();
        }
        finally
        {
            _slots.Release();
        }
    }

    internal sealed record StepInfo(Guid RunId, DateTimeOffset? FinishedAt, IReadOnlyDictionary<string, long> Rows);

    private static async Task<HashSet<string>> ExistingSchemasAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string[] names, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT nspname FROM pg_catalog.pg_namespace WHERE nspname = ANY(@names)", connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("names", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = names });
        var found = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            found.Add(reader.GetString(0));
        }

        return found;
    }

    internal static async Task<List<string>> TableNamesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relkind = 'r'
            ORDER BY c.relname
            """, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("schema", NpgsqlDbType.Text) { Value = schema });
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>
    /// The latest succeeded run step of this source. A source's schema is replaced only by a succeeded step, so while the schema
    /// exists this step is the one that published it, whatever failed since.
    /// </summary>
    internal static async Task<StepInfo?> LatestStepAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid projectId, string step, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT s.run_id, s.finished_at, s.tables
            FROM tamiza.run_steps s JOIN tamiza.runs r ON r.id = s.run_id
            WHERE r.project_id = @project AND s.step = @step AND s.status = 'succeeded'
            ORDER BY s.finished_at DESC NULLS LAST
            LIMIT 1
            """, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("project", NpgsqlDbType.Uuid) { Value = projectId });
        command.Parameters.Add(new NpgsqlParameter("step", NpgsqlDbType.Text) { Value = step });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var rows = new Dictionary<string, long>(StringComparer.Ordinal);
        using var tables = JsonDocument.Parse(reader.GetString(2));
        foreach (var table in tables.RootElement.EnumerateArray())
        {
            if (table.TryGetProperty("name", out var name) && table.TryGetProperty("rows", out var count) && count.TryGetInt64(out var n))
            {
                rows[name.GetString()!] = n;
            }
        }

        return new StepInfo(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1), rows);
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a read-only transaction on a connection of its own. The statement and lock time limits and the
    /// project's time zone apply to every statement in it; a limit that is hit becomes a <see cref="DataTimeoutException"/>.
    /// </summary>
    internal async Task<T> InTransactionAsync<T>(
        Guid projectId, TimeSpan statementTimeout, Func<NpgsqlConnection, NpgsqlTransaction, DataProject, Task<T>> work, CancellationToken ct,
        bool heapOrder = false)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TamizaDbContext>();
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => new DataProject(p.Name, p.TimeZone)).SingleAsync(ct);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var setup = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await setup.ExecuteNonQueryAsync(ct);
            }

            await using (var settings = new NpgsqlCommand(
                "SELECT set_config('statement_timeout', @statement, true), set_config('lock_timeout', @lock, true), set_config('TimeZone', @tz, true), set_config('DateStyle', 'ISO, MDY', true)",
                connection, transaction))
            {
                settings.Parameters.Add(new NpgsqlParameter("statement", NpgsqlDbType.Text) { Value = Milliseconds(statementTimeout) });
                settings.Parameters.Add(new NpgsqlParameter("lock", NpgsqlDbType.Text) { Value = Milliseconds(_options.LockTimeout) });
                settings.Parameters.Add(new NpgsqlParameter("tz", NpgsqlDbType.Text) { Value = project.TimeZone });
                await settings.ExecuteNonQueryAsync(ct);
            }

            if (heapOrder)
            {
                // An export without a sort writes rows in the order they were published. A serial scan from the first block returns them
                // in that order, which saves sorting every wide row of the table; a synchronized or parallel scan would not keep it.
                await using var order = new NpgsqlCommand(
                    "SELECT set_config('synchronize_seqscans', 'off', true), set_config('max_parallel_workers_per_gather', '0', true)", connection, transaction);
                await order.ExecuteNonQueryAsync(ct);
            }

            return await work(connection, transaction, project);
        }
        catch (PostgresException ex) when (!ct.IsCancellationRequested && ex.SqlState is PostgresErrorCodes.QueryCanceled or PostgresErrorCodes.LockNotAvailable)
        {
            throw new DataTimeoutException(ex);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static string Milliseconds(TimeSpan span) => ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}
