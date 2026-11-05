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
    /// <param name="dashboardFilters">The dashboard filter values in force that narrow the rows too (design D3); null or empty for none.</param>
    /// <param name="overall">
    /// Also reads the overall value of a plain grouped query (<see cref="IndicatorQueryResponse.Overall"/>, design D4 of add-chart-annotations),
    /// after its rows and in the same transaction. A caller derives it from the saved widget (<see cref="WidgetQuery.Overall"/>).
    /// </param>
    public async Task<IndicatorQueryOutcome> QueryAsync(
        Guid projectId, WidgetConfigInput input, string? chartType, CancellationToken ct, IReadOnlyList<AppliedDashboardFilter>? dashboardFilters = null,
        bool overall = false)
    {
        var config = input.Normalize();
        var errors = new FieldErrors();
        if (chartType is not null)
        {
            IndicatorValidation.CheckChartType(chartType, errors);
        }

        IndicatorValidation.CheckConfig(config, ChartTypes.IsKnown(chartType) ? chartType : null, prefix: "", errors);
        IndicatorValidation.CheckDashboardFilters(dashboardFilters, errors);
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

            // What the indicator's current version says about its columns: one statement of this read-only transaction, resolved against
            // the table just described (design D5 of add-small-sample-warnings).
            var bases = IndicatorBases.Resolve(await ReadColumnMetadataAsync(connection, transaction, projectId, config.Indicator, ct), columns);
            var plan = IndicatorQueryBuilder.Plan(
                config, columns, project.TimeZone, From(projectId, config.Indicator), maxRows, perRow: chartType == ChartTypes.Histogram, dashboardFilters,
                chartType, overall, bases);
            if (plan.Errors is not null)
            {
                return new IndicatorQueryOutcome(null, plan.Errors);
            }

            if (plan.Sql is null && plan.Series is null && plan.Boxes is null)
            {
                return new IndicatorQueryOutcome(new IndicatorQueryResponse(true, [], false, new MissingReferences(false, plan.MissingColumns)), null);
            }

            await data.SleepForTestsAsync(connection, transaction, ct);
            if (plan.Boxes is { } boxesPlan)
            {
                var boxGroupType = columns.FirstOrDefault(c => c.Name == config.GroupBy)?.Type;
                var boxNotFiltered = dashboardFilters is { Count: > 0 } ? plan.NotFiltered : null;
                return new IndicatorQueryOutcome(await ReadBoxesAsync(connection, transaction, boxesPlan, boxGroupType, config.Sort, maxRows, boxNotFiltered, ct), null);
            }

            if (plan.Series is { } seriesPlan)
            {
                var seriesGroupType = columns.First(c => c.Name == config.GroupBy).Type;
                var notFiltered = dashboardFilters is { Count: > 0 } ? plan.NotFiltered : null;
                var seriesResponse = await ReadSeriesAsync(connection, transaction, seriesPlan, seriesGroupType, maxRows, notFiltered, ct);
                var seriesNoData = plan.NoData is null ? null : await ReadNoDataAsync(connection, transaction, plan.NoData, ct);
                return new IndicatorQueryOutcome(seriesNoData is null ? seriesResponse : seriesResponse with { NoData = seriesNoData }, null);
            }

            var group = columns.FirstOrDefault(c => c.Name == config.GroupBy);
            // A pie's domain is read before its rows (design D8 of dashboard palettes); no other kind has one.
            var domain = plan.Domain is null ? null : await ReadPieDomainAsync(connection, transaction, plan.Domain, group?.Type, ct);
            var (rows, limitReached, widgetN) = await ReadRowsAsync(connection, transaction, plan.Sql!, group?.Type, maxRows, ct);
            if (plan.Counted is { } counted)
            {
                // Answers are declared: each mark's n and the widget's are counted once across the answers (design D3).
                var counts = await ReadCountedAsync(connection, transaction, counted, group?.Type, series: false, ct);
                widgetN = counts.GetValueOrDefault((BaseQueryBuilder.Parts.Widget, null, null, null));
                for (var i = 0; i < rows.Count; i++)
                {
                    // A group's n; a stat tile has one row, which carries the widget's.
                    rows[i] = rows[i] with { N = config.GroupBy is null ? widgetN : counts.GetValueOrDefault((BaseQueryBuilder.Parts.Group, rows[i].Group, null, null), rows[i].N) };
                }
            }

            var overallValue = plan.Overall is null ? null : await ReadOverallAsync(connection, transaction, plan.Overall, ct);
            var noData = plan.NoData is null ? null : await ReadNoDataAsync(connection, transaction, plan.NoData, ct);
            return new IndicatorQueryOutcome(
                new IndicatorQueryResponse(
                    true, rows, limitReached, MissingReferences.None, plan.Sql!.TooltipTypes, dashboardFilters is { Count: > 0 } ? plan.NotFiltered : null,
                    Domain: domain, Overall: overallValue, N: widgetN, NoData: noData),
                null);
        }, ct), ct);
    }

    /// <summary>
    /// The 50 most frequent values of a text column across the published tables of <paramref name="indicators"/> that have it as text,
    /// most frequent first, for a dashboard's text filter (design D5). Tables without the column, with it as another type, or not
    /// published are skipped, so the list may be empty. Values longer than a filter accepts are left out. Throws
    /// <see cref="DataTimeoutException"/> when the read takes too long.
    /// </summary>
    /// <param name="indicators">Indicator names, each a valid table name, as the dashboard's widgets name them.</param>
    /// <param name="search">Restricts the list to values that contain this text, ignoring case.</param>
    public Task<IReadOnlyList<DataValueCount>> ValuesAsync(
        Guid projectId, IReadOnlyList<string> indicators, string column, string? search, CancellationToken ct) =>
        data.WithSlotAsync(() => data.InTransactionAsync<IReadOnlyList<DataValueCount>>(projectId, dataOptions.Value.StatementTimeout, async (connection, transaction, _) =>
        {
            // Every table and column name below was returned by the catalog, and is quoted.
            var tables = new List<(string From, string Column)>();
            foreach (var indicator in indicators)
            {
                var columns = await DataDashboard.LockAndDescribeAsync(connection, transaction, SchemaName(projectId), indicator, ct);
                if (columns?.FirstOrDefault(c => c.Name == column && c.Type == DataColumnType.Text) is { } found)
                {
                    tables.Add((From(projectId, indicator), DataSources.Quote(found.Name)));
                }
            }

            if (tables.Count == 0)
            {
                return [];
            }

            await data.SleepForTestsAsync(connection, transaction, ct);
            var searching = !string.IsNullOrEmpty(search);
            string Matching(string quoted) => searching ? $" AND {quoted}::text ILIKE '%' || @search || '%' ESCAPE '\\'" : "";
            // Each table's counts, then the counts of a value added up across tables.
            var each = tables.Select(t =>
                $"""
                SELECT {t.Column}::text AS v, count(*) AS n FROM {t.From}
                WHERE {t.Column} IS NOT NULL AND char_length({t.Column}::text) <= {DataQueryBuilder.MaxTextLength}{Matching(t.Column)}
                GROUP BY 1
                """);
            await using var command = new NpgsqlCommand(
                $"""
                SELECT v, sum(n)::bigint FROM (
                {string.Join("\nUNION ALL\n", each)}
                ) AS s GROUP BY v ORDER BY sum(n) DESC, v LIMIT {DataDashboard.MaxFrequentValues}
                """, connection, transaction);
            if (searching)
            {
                command.Parameters.Add(new NpgsqlParameter("search", NpgsqlDbType.Text) { Value = DataQueryBuilder.EscapeLike(search!) });
            }

            var values = new List<DataValueCount>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                values.Add(new DataValueCount(reader.GetString(0), reader.GetInt64(1)));
            }

            return values;
        }, ct), ct);

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

    /// <summary>
    /// A tooltip output as the response carries it: a number (text beyond what a decimal holds stays text; NaN and infinity have no
    /// number in JSON and read as no value), the text cut at <see cref="WidgetConfig.MaxTooltipText"/> with an ellipsis, an ISO date or
    /// timestamp, or true/false. Null when no row had a value.
    /// </summary>
    private static object? ReadTooltipValue(NpgsqlDataReader reader, int ordinal, DataColumnType type)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        if (type == DataColumnType.Boolean)
        {
            return reader.GetBoolean(ordinal);
        }

        var text = reader.GetString(ordinal);
        switch (type)
        {
            case DataColumnType.Integer or DataColumnType.Decimal:
                return text is "NaN" or "Infinity" or "-Infinity" ? null : DataValues.Number(text);
            case DataColumnType.Timestamp:
                return DataValues.IsoTimestamp(text);
            case DataColumnType.Date:
                return text;
            default:
                var (shown, cut) = DataValues.Display(text, WidgetConfig.MaxTooltipText);
                return cut ? shown + "…" : shown;
        }
    }

    /// <summary>
    /// Runs the statements of a widget with a series (design D3) and puts the answer together: the series in display order, one cell per
    /// group and series value that has a value, and the totals. A group is shown whole or not at all, so when the row limit falls inside
    /// a group, that group is left out.
    /// </summary>
    private static async Task<IndicatorQueryResponse> ReadSeriesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, SeriesQueryPlan plan, DataColumnType groupType, int maxRows,
        IReadOnlyList<string>? notFiltered, CancellationToken ct)
    {
        IReadOnlyList<string> kept;
        List<SeriesInfo> series;
        if (plan.FixedDomain is { } fixedValues)
        {
            kept = fixedValues;
            series = fixedValues.Select(value => new SeriesInfo(value, SeriesKinds.Value)).ToList();
        }
        else
        {
            var listed = await ReadDomainAsync(connection, transaction, plan.Domain!, ct);
            var values = listed.OfType<string>().ToList();
            var hasNone = listed.Contains(null);
            var (keptValues, hasOther) = SeriesDomain.Fold(values, hasNone, plan.MaxSeries);
            kept = keptValues;
            series = SeriesDomain.Order(keptValues, hasNone, hasOther);
        }

        var seriesIndex = new Dictionary<(string Kind, string? Value), int>();
        for (var i = 0; i < series.Count; i++)
        {
            seriesIndex[(series[i].Kind, series[i].Value)] = i;
        }

        var groups = new List<(long Rank, GroupTotal Total)>();
        var cells = new List<(long Rank, string? Group, int Series, double Value, long Rows, double? Numerator, double? Denominator, double? N)>();
        var seriesTotals = new SeriesTotal?[series.Count];
        GrandTotal grand = new(null, 0);
        long? leftOut = null;

        var cellsSql = plan.Cells(kept);
        await using (var command = new NpgsqlCommand(cellsSql.Sql, connection, transaction))
        {
            foreach (var parameter in cellsSql.Parameters)
            {
                command.Parameters.Add(parameter.ToParameter());
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var part = reader.GetInt32(0);
                var group = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (group is not null && groupType == DataColumnType.Timestamp)
                {
                    group = DataValues.IsoTimestamp(group);
                }

                var kind = reader.IsDBNull(2) ? null : reader.GetString(2);
                var name = reader.IsDBNull(3) ? null : reader.GetString(3);
                var value = reader.IsDBNull(4) ? (double?)null : FiniteOrNull(reader.GetDouble(4));
                var rows = reader.GetInt64(5);
                var rank = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
                var (numerator, denominator, n) = ReadFigures(reader, cellsSql.Figures);
                switch (part)
                {
                    case SeriesParts.GroupTotal:
                        groups.Add((rank, new GroupTotal(group, value, rows, numerator, denominator, n)));
                        break;
                    case SeriesParts.Cell:
                        // A value that is not a number has nothing to draw; the group's total still counts it.
                        if (value is { } plotted && kind is not null && seriesIndex.TryGetValue((kind, kind == SeriesKinds.Value ? name : null), out var index))
                        {
                            cells.Add((rank, group, index, plotted, rows, numerator, denominator, n));
                        }

                        break;
                    case SeriesParts.SeriesTotal:
                        if (kind is not null && seriesIndex.TryGetValue((kind, kind == SeriesKinds.Value ? name : null), out var at))
                        {
                            seriesTotals[at] = new SeriesTotal(series[at].Value, series[at].Kind, value, rows, numerator, denominator, n);
                        }

                        break;
                    case SeriesParts.Grand:
                        grand = new GrandTotal(value, rows, numerator, denominator, n);
                        break;
                    case SeriesParts.LeftOut:
                        leftOut = rows;
                        break;
                }
            }
        }

        // The row limit counts cells: a group whose cells cross it is left out whole, with the groups after it.
        var cut = long.MaxValue;
        if (cells.Count > maxRows)
        {
            cut = cells.OrderBy(c => c.Rank).ThenBy(c => c.Series).ElementAt(maxRows).Rank;
        }

        if (groups.Count > maxRows)
        {
            cut = Math.Min(cut, groups.OrderBy(g => g.Rank).ElementAt(maxRows).Rank);
        }

        var limitReached = cut != long.MaxValue;
        var shown = groups.Where(g => g.Rank < cut).OrderBy(g => g.Rank).ToList();
        if (plan.NaturalGroups)
        {
            shown = shown.OrderBy(g => g.Total.Group, NaturalOrder.ByText.Instance).ToList();
        }

        // Answers are declared: each mark's n and the widget's are counted once across the answers (design D3), over the series values kept.
        var counts = plan.Counted is { } countedFor
            ? await ReadCountedAsync(connection, transaction, countedFor(kept), groupType, series: true, ct)
            : null;
        double? CountOf(int part, string? group, string? kind, string? name, double? plain) =>
            counts is not null && counts.TryGetValue((part, group, kind, name), out var counted) ? counted : plain;
        if (counts is not null)
        {
            shown = shown.Select(g => (g.Rank, g.Total with { N = CountOf(BaseQueryBuilder.SeriesCountParts.Group, g.Total.Group, null, null, g.Total.N) })).ToList();
            for (var i = 0; i < seriesTotals.Length; i++)
            {
                if (seriesTotals[i] is { } total)
                {
                    seriesTotals[i] = total with { N = CountOf(BaseQueryBuilder.SeriesCountParts.Series, null, total.Kind, total.Series, total.N) };
                }
            }

            grand = grand with { N = CountOf(BaseQueryBuilder.SeriesCountParts.All, null, null, null, grand.N) };
        }

        var rowsOut = new List<IndicatorRow>();
        foreach (var (rank, total) in shown)
        {
            foreach (var cell in cells.Where(c => c.Rank == rank).OrderBy(c => c.Series))
            {
                var cellN = CountOf(BaseQueryBuilder.SeriesCountParts.Cell, total.Group, series[cell.Series].Kind, series[cell.Series].Value, cell.N);
                rowsOut.Add(new IndicatorRow(
                    total.Group, cell.Value, null, cell.Rows, null, series[cell.Series].Value, series[cell.Series].Kind, cell.Numerator, cell.Denominator, cellN));
            }
        }

        var totals = new SeriesTotals(
            shown.Select(g => g.Total).ToList(),
            series.Select((info, i) => seriesTotals[i] ?? new SeriesTotal(info.Value, info.Kind, null, 0)).ToList(),
            grand);
        // The widget's n (design D4): counted across the answers when they are declared, else the base column's sum over every row it plots.
        var widgetN = counts is not null ? CountOf(BaseQueryBuilder.SeriesCountParts.All, null, null, null, null) : cellsSql.Figures?.Base is not null ? grand.N : null;
        return new IndicatorQueryResponse(true, rowsOut, limitReached, MissingReferences.None, null, notFiltered, series, totals, leftOut, N: widgetN);
    }

    /// <summary>
    /// A pie's domain (design D8 of dashboard palettes): the groups of its own rows, the missing one in its place, by total, largest
    /// first, folded to the pie's <see cref="WidgetConfig.MaxSlices"/> colors with "Other" (<see cref="SeriesDomain.BySize"/>). The
    /// values are written as the pie's rows write their groups, so a row finds its place in the domain by its group.
    /// </summary>
    private static async Task<IReadOnlyList<SeriesInfo>> ReadPieDomainAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IndicatorSql sql, DataColumnType? groupType, CancellationToken ct)
    {
        var groups = (await ReadDomainAsync(connection, transaction, sql, ct))
            .Select(group => group is not null && groupType == DataColumnType.Timestamp ? DataValues.IsoTimestamp(group) : group)
            .ToList();
        return SeriesDomain.BySize(groups, WidgetConfig.MaxSlices);
    }

    /// <summary>The values a domain statement lists, in its order; null is the missing value.</summary>
    private static async Task<List<string?>> ReadDomainAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IndicatorSql sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql.Sql, connection, transaction);
        foreach (var parameter in sql.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        var values = new List<string?>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return values;
    }

    /// <summary>
    /// A box plot's boxes (design D3 of add-boxplot-widget). The statement cuts at the row limit in the group column's own order, or in the
    /// order by median; the boxes it kept are then listed as the spec says: in natural order, or by median with natural order between equal
    /// medians. A box with a figure that is not a number is skipped, as a row with one is.
    /// </summary>
    private static async Task<IndicatorQueryResponse> ReadBoxesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IndicatorSql sql, DataColumnType? groupType, WidgetSort? sort, int maxRows,
        IReadOnlyList<string>? notFiltered, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql.Sql, connection, transaction);
        foreach (var parameter in sql.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        var boxes = new List<IndicatorBox>();
        var limitReached = false;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (boxes.Count == maxRows)
            {
                limitReached = true;
                break;
            }

            var quartiles = reader.GetFieldValue<double[]>(BoxColumns.Quartiles);
            var outliers = reader.IsDBNull(BoxColumns.OutlierValues) ? [] : reader.GetFieldValue<double[]>(BoxColumns.OutlierValues);
            double[] figures =
            [
                reader.GetDouble(BoxColumns.Min), quartiles[0], quartiles[1], quartiles[2], reader.GetDouble(BoxColumns.Max), reader.GetDouble(BoxColumns.Mean),
                reader.GetDouble(BoxColumns.LowerWhisker), reader.GetDouble(BoxColumns.UpperWhisker),
            ];
            if (!figures.All(double.IsFinite) || !outliers.All(double.IsFinite))
            {
                continue; // NaN and infinity have no number in JSON
            }

            var group = reader.IsDBNull(BoxColumns.Group) ? null : reader.GetString(BoxColumns.Group);
            boxes.Add(new IndicatorBox(
                group is not null && groupType == DataColumnType.Timestamp ? DataValues.IsoTimestamp(group) : group,
                reader.GetInt64(BoxColumns.Values), figures[0], figures[1], figures[2], figures[3], figures[4], figures[5], figures[6], figures[7],
                reader.GetInt64(BoxColumns.Outliers), outliers));
        }

        // OrderBy is stable, so the natural order of the groups holds between equal medians.
        var natural = boxes.OrderBy(box => box.Group, NaturalOrder.ByText.Instance);
        var shown = sort is { By: SortValues.ByValue }
            ? (sort.Direction == SortValues.Desc ? natural.OrderByDescending(box => box.Median) : natural.OrderBy(box => box.Median)).ToList()
            : natural.ToList();
        var rows = shown.Select(box => new IndicatorRow(box.Group, box.Median, null, box.Values)).ToList();
        return new IndicatorQueryResponse(true, rows, limitReached, MissingReferences.None, null, notFiltered, Boxes: shown);
    }

    /// <summary>NaN and infinity have no number in JSON.</summary>
    private static double? FiniteOrNull(double value) => double.IsFinite(value) ? value : null;

    /// <summary>
    /// The overall value (design D4 of add-chart-annotations): the value of the first row of a stat tile's statement. No row, because the
    /// denominators sum to zero, or a value that is not a number, is null.
    /// </summary>
    private static async Task<double?> ReadOverallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IndicatorSql sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql.Sql, connection, transaction);
        foreach (var parameter in sql.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) && !reader.IsDBNull(1) ? FiniteOrNull(reader.GetDouble(1)) : null;
    }

    /// <summary>
    /// What the widget left out as no data (design D6 of add-small-sample-warnings): null when no left-out value had a row. The value is a
    /// ratio's left-out numerator over its left-out denominator, or a measure's left-out sum, whose share is its part of the sum of every
    /// row. NaN and infinity have no number in JSON, and read as no value.
    /// </summary>
    private static async Task<IndicatorNoData?> ReadNoDataAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NoDataSql plan, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(plan.Sql.Sql, connection, transaction);
        foreach (var parameter in plan.Sql.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        string[] ValuesAt(int ordinal) => reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<string[]>(ordinal);
        var values = ValuesAt(NoDataSql.GroupValues).Concat(ValuesAt(NoDataSql.SeriesValues)).Distinct(StringComparer.Ordinal)
            .Order(NaturalOrder.ByText.Instance).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        var n = ReadFigure(reader, NoDataSql.N);
        if (plan.IsRatio)
        {
            var numerator = ReadFigure(reader, NoDataSql.Numerator);
            var denominator = ReadFigure(reader, NoDataSql.Denominator);
            return new IndicatorNoData(values, numerator is { } top && denominator is { } bottom && bottom != 0 ? FiniteOrNull(top / bottom) : null, numerator, denominator, n);
        }

        var sum = ReadFigure(reader, NoDataSql.LeftOutSum);
        var total = ReadFigure(reader, NoDataSql.TotalSum);
        return new IndicatorNoData(values, sum, N: n, Share: sum is { } part && total is { } whole && whole != 0 ? FiniteOrNull(part / whole) : null);
    }

    /// <summary>
    /// A figure output of the current row (design D5 of add-small-sample-warnings): a sum as a number; null when the statement has no such
    /// output, when no row had a value, and for NaN and infinity, which have no number in JSON.
    /// </summary>
    private static double? ReadFigure(NpgsqlDataReader reader, int? ordinal) =>
        ordinal is { } at && !reader.IsDBNull(at) ? FiniteOrNull(reader.GetDouble(at)) : null;

    /// <summary>
    /// The numerator, the denominator and the n of the current row, from the figure outputs of its statement. The n is the sum of the
    /// base column when the indicator declares one, else a ratio's denominator (design D2); a measure with no base column has none.
    /// </summary>
    private static (double? Numerator, double? Denominator, double? N) ReadFigures(NpgsqlDataReader reader, SqlFigures? figures)
    {
        if (figures is null)
        {
            return (null, null, null);
        }

        var denominator = ReadFigure(reader, figures.Denominator);
        return (ReadFigure(reader, figures.Numerator), denominator, figures.Base is not null ? ReadFigure(reader, figures.Base) : denominator);
    }

    /// <summary>
    /// The facts the current version of an indicator's script holds about its columns, or null when it holds none or the indicator is
    /// removed (its newest version has no code): the version <see cref="ScriptEndpoints.CurrentIndicatorsAsync"/> takes, read as
    /// <see cref="IndicatorEndpoints.CurrentColumnsAsync"/> reads it.
    /// </summary>
    private static async Task<Dictionary<string, IndicatorColumnMetadata>?> ReadColumnMetadataAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid projectId, string indicator, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT code IS NULL, columns::text FROM tamiza.script_versions
            WHERE project_id = @project AND kind = 'indicator' AND name = @name
            ORDER BY number DESC LIMIT 1
            """, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("project", NpgsqlDbType.Uuid) { Value = projectId });
        command.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = indicator });
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) && !reader.GetBoolean(0) && !reader.IsDBNull(1) ? ColumnMetadata.Read(reader.GetString(1)) : null;
    }

    /// <summary>
    /// Runs a counted statement (<see cref="BaseQueryBuilder.Counted"/>, <see cref="BaseQueryBuilder.CountedSeries"/>) and reads its n by
    /// part and keys: the group (as the plotted rows write it, a timestamp through <see cref="DataValues.IsoTimestamp"/>), the series
    /// kind and the series text. A part that has no key leaves it null.
    /// </summary>
    private static async Task<Dictionary<(int Part, string? Group, string? Kind, string? Series), double?>> ReadCountedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IndicatorSql sql, DataColumnType? groupType, bool series, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql.Sql, connection, transaction);
        foreach (var parameter in sql.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        var counts = new Dictionary<(int Part, string? Group, string? Kind, string? Series), double?>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var group = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (group is not null && groupType == DataColumnType.Timestamp)
            {
                group = DataValues.IsoTimestamp(group);
            }

            var kind = series && !reader.IsDBNull(2) ? reader.GetString(2) : null;
            var name = series && !reader.IsDBNull(3) ? reader.GetString(3) : null;
            counts[(reader.GetInt32(0), group, kind, name)] = ReadFigure(reader, series ? 4 : 2);
        }

        return counts;
    }

    private static async Task<(List<IndicatorRow> Rows, bool LimitReached, double? WidgetN)> ReadRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IndicatorSql sql, DataColumnType? groupType, int maxRows, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql.Sql, connection, transaction);
        foreach (var parameter in sql.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        var rows = new List<IndicatorRow>();
        var limitReached = false;
        double? widgetN = null;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count == maxRows)
            {
                limitReached = true;
                break;
            }

            // The window's value is the same on every row: the n of every plotted group, past the row limit too.
            widgetN ??= ReadFigure(reader, sql.Figures?.WidgetN);
            var value = reader.GetDouble(1);
            if (!double.IsFinite(value))
            {
                continue; // NaN and infinity have no number in JSON
            }

            var group = reader.IsDBNull(0) ? null : reader.GetString(0);
            long? combined = null;
            List<object?>? tooltip = null;
            if (sql.TooltipTypes is { } types)
            {
                // A map query: the row count is the fourth column, and each tooltip column follows.
                combined = reader.GetInt64(3);
                tooltip = new List<object?>(types.Count);
                for (var i = 0; i < types.Count; i++)
                {
                    tooltip.Add(ReadTooltipValue(reader, 4 + i, types[i]));
                }
            }

            var (numerator, denominator, n) = ReadFigures(reader, sql.Figures);
            rows.Add(new IndicatorRow(
                group is not null && groupType == DataColumnType.Timestamp ? DataValues.IsoTimestamp(group) : group,
                value,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                combined,
                tooltip,
                Numerator: numerator,
                Denominator: denominator,
                N: n));
        }

        return (rows, limitReached, widgetN);
    }
}
