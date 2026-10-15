using System.Data;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Tamiza.Api.CleanData;

/// <param name="Format"><c>csv</c> or <c>xlsx</c>.</param>
/// <param name="Filters">The filters of the view being exported, as in a page request.</param>
/// <param name="Sort">The sort of the view being exported.</param>
public sealed record DataExportRequest(string? Format, IReadOnlyList<DataFilter>? Filters = null, DataSort? Sort = null);

/// <summary>A prepared file. The browser downloads it once from <see cref="DownloadUrl"/> before <see cref="ExpiresAt"/>.</summary>
/// <param name="RowCount">The data rows in the file, without the header.</param>
/// <param name="CutCells">Cells cut to Excel's 32,767 characters (XLSX only).</param>
public sealed record DataExportResult(string DownloadUrl, string FileName, long RowCount, long CutCells, DateTimeOffset ExpiresAt);

/// <summary>The outcome of an export: the file, or the validation errors of the request. Null from the service means the table is not published.</summary>
public sealed record DataExportOutcome(DataExportResult? Export, IDictionary<string, string[]>? Errors);

/// <summary>Every export slot stayed taken for as long as a request may wait.</summary>
public sealed class DataExportBusyException() : Exception("Other exports are being prepared.")
{
    public const string Code = "data.exportBusy";
}

/// <summary>An XLSX export would hold more rows than a sheet can.</summary>
public sealed class DataXlsxTooManyRowsException(long rows, int limit) : Exception($"{rows} rows do not fit an XLSX sheet of {limit}.")
{
    public const string Code = "data.xlsxTooManyRows";

    public long Rows { get; } = rows;

    public int Limit { get; } = limit;
}

public sealed partial class DataDashboard
{
    public const string Csv = "csv";
    public const string Xlsx = "xlsx";

    public const string ExportTooLargeCode = "data.exportTooLarge";

    /// <summary>
    /// Writes the whole table, with the filters and sort applied, to a file and registers a link that downloads it once (design D4).
    /// The database is read in one read-only transaction that holds the table's lock, so a run cannot replace the table midway, and
    /// the lock is released as soon as the file is written, before anyone downloads it. Returns null when the table is not published.
    /// Throws <see cref="DataExportBusyException"/>, <see cref="DataXlsxTooManyRowsException"/>, <see cref="ExportTooLargeException"/>
    /// or <see cref="DataTimeoutException"/>; no file is left behind in those cases.
    /// </summary>
    public async Task<DataExportOutcome?> ExportAsync(Guid projectId, Guid userId, string source, string table, DataExportRequest request, CancellationToken ct)
    {
        if (!DataSources.IsKnown(source))
        {
            return null;
        }

        return await ExportFromSchemaAsync(projectId, userId, DataSources.SchemaName(projectId, source), table, request, ct);
    }

    /// <summary>
    /// <see cref="ExportAsync"/> for a schema the caller built from the project id (the indicators dashboard exports
    /// <c>p_&lt;id&gt;_indicators.&lt;name&gt;</c>, which is not one of the clean data sources).
    /// </summary>
    internal async Task<DataExportOutcome?> ExportFromSchemaAsync(
        Guid projectId, Guid userId, string schema, string table, DataExportRequest request, CancellationToken ct)
    {
        var csv = request.Format == Csv;

        if (!await _exportSlots.WaitAsync(_options.QueueTimeout, ct))
        {
            throw new DataExportBusyException();
        }

        string? path = null;
        try
        {
            var written = await InTransactionAsync(projectId, _options.ExportStatementTimeout, async (connection, transaction, project) =>
            {
                var columns = await LockAndDescribeAsync(connection, transaction, schema, table, ct);
                if (columns is null)
                {
                    return null;
                }

                var errors = new Http.FieldErrors();
                var query = DataQueryBuilder.Build(new DataQueryRequest(request.Filters, request.Sort), columns, project.TimeZone, errors);
                if (query is null)
                {
                    return new Written(null, 0, 0, errors.ToDictionary());
                }

                await SleepForTestsAsync(connection, transaction, ct);
                var from = $"{DataSources.Quote(schema)}.{DataSources.Quote(table)}";
                var total = await CountAsync(connection, transaction, from, query, ct);
                if (!csv && total > _options.XlsxMaxRows)
                {
                    throw new DataXlsxTooManyRowsException(total, _options.XlsxMaxRows);
                }

                path = _exports.NewFilePath();
                var file = path;
                var outcome = await Task.Run(() => WriteFile(connection, transaction, from, table, columns, query, csv, file, ct), ct);
                var extension = csv ? Csv : Xlsx;
                var name = ExportFileName.Create(project.Name, table, DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime), extension);
                return new Written(name, outcome.Rows, outcome.CutCells, null);
            }, ct, heapOrder: request.Sort is null);

            if (written is null)
            {
                return null;
            }

            if (written.Errors is not null)
            {
                return new DataExportOutcome(null, written.Errors);
            }

            var link = _exports.Add(projectId, userId, path!, written.FileName!, csv ? "text/csv; charset=utf-8" : XlsxContentType);
            path = null; // the registry owns the file now
            return new DataExportOutcome(
                new DataExportResult($"/api/v1/data-exports/{link.Token}", written.FileName!, written.Rows, written.CutCells, link.ExpiresAt), null);
        }
        catch
        {
            if (path is not null)
            {
                ExportRegistry.DeleteQuietly(path);
            }

            throw;
        }
        finally
        {
            _exportSlots.Release();
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> in one of the export slots, which also bound other files prepared for download (a sample
    /// download's zip). Throws <see cref="DataExportBusyException"/> when none frees up within the queue timeout.
    /// </summary>
    internal async Task<T> WithExportSlotAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (!await _exportSlots.WaitAsync(_options.QueueTimeout, ct))
        {
            throw new DataExportBusyException();
        }

        try
        {
            return await action();
        }
        finally
        {
            _exportSlots.Release();
        }
    }

    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private sealed record Written(string? FileName, long Rows, long CutCells, IDictionary<string, string[]>? Errors);

    /// <summary>Streams the matching rows from the database into the file. Runs on a thread of its own: the writers are synchronous.</summary>
    private ExportOutcome WriteFile(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string from, string table, IReadOnlyList<TableColumn> columns,
        DataQuery query, bool csv, string path, CancellationToken ct)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024);
        var limited = new LimitedStream(file, _options.MaxExportFileSize);

        var select = string.Join(", ", columns.Select(ExportSelectExpression));
        using var command = new NpgsqlCommand($"SELECT {select} FROM {from}{query.WhereClause} {(query.HasSort ? $" ORDER BY {query.OrderBy}" : "")}", connection, transaction)
        {
            CommandTimeout = (int)Math.Ceiling(_options.ExportStatementTimeout.TotalSeconds) + 30,
        };
        foreach (var parameter in query.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
        var rows = ReadRows(reader, columns, ct);
        return csv ? CsvExport.Write(limited, columns, rows) : XlsxExport.Write(limited, table, columns, rows);
    }

    /// <summary>What an export selects for a column: the same as a page, but text is whole.</summary>
    private static string ExportSelectExpression(TableColumn column)
    {
        var quoted = DataSources.Quote(column.Name);
        return column.Type is DataColumnType.Integer or DataColumnType.Boolean ? quoted : $"{quoted}::text";
    }

    private static IEnumerable<object?[]> ReadRows(NpgsqlDataReader reader, IReadOnlyList<TableColumn> columns, CancellationToken ct)
    {
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var row = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                if (reader.IsDBNull(i))
                {
                    continue;
                }

                row[i] = columns[i].Type switch
                {
                    DataColumnType.Integer => Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture),
                    DataColumnType.Boolean => reader.GetBoolean(i),
                    DataColumnType.Decimal => DataValues.Number(reader.GetString(i)),
                    DataColumnType.Timestamp => DataValues.IsoTimestamp(reader.GetString(i)),
                    _ => reader.GetString(i), // days and text, whole
                };
            }

            yield return row;
        }
    }
}
