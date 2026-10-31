using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.Indicators;

/// <summary>A widget query as SQL: identifiers from the catalog (quoted), every value a parameter.</summary>
/// <param name="Sql">Selects <c>group, value, geometry</c> in that order; a map query goes on with <c>count(*)</c> and one output per tooltip column.</param>
/// <param name="TooltipTypes">Set for a map query: the type of each tooltip output, from the fifth column of <paramref name="Sql"/> on; null for every other query.</param>
public sealed record IndicatorSql(string Sql, IReadOnlyList<SqlParam> Parameters, IReadOnlyList<DataColumnType>? TooltipTypes = null)
{
    public const string LimitParameter = "limit";
}

/// <summary>
/// The outcome of planning a query against the table's columns. Exactly one of <see cref="Errors"/> (a malformed request) or
/// <see cref="Sql"/> is set unless something is <see cref="MissingColumns"/>, in which case there is nothing to run.
/// </summary>
/// <param name="NotFiltered">The columns of the dashboard filters that do not narrow this table, in the request's order (design D3).</param>
/// <param name="Series">Set instead of <paramref name="Sql"/> for a widget with a series (design D3): its statements are built and run in two steps.</param>
public sealed record IndicatorQueryPlan(
    IndicatorSql? Sql, IReadOnlyList<string> MissingColumns, IDictionary<string, string[]>? Errors, IReadOnlyList<string>? NotFiltered = null,
    SeriesQueryPlan? Series = null);

/// <summary>Validates a widget query against the indicator table's columns and turns it into SQL.</summary>
public static class IndicatorQueryBuilder
{
    public const string ValueColumn = "value";

    /// <summary><c>value</c> when the table has it as a number, otherwise its first numeric column.</summary>
    public static string? DefaultMeasure(IReadOnlyList<TableColumn> columns)
    {
        var numeric = columns.Where(IndicatorValidation.IsNumeric).ToList();
        return numeric.Any(c => c.Name == ValueColumn) ? ValueColumn : numeric.FirstOrDefault()?.Name;
    }

    /// <summary>The first column that is not a number, if any.</summary>
    public static string? DefaultGroupBy(IReadOnlyList<TableColumn> columns) =>
        columns.FirstOrDefault(c => !IndicatorValidation.IsNumeric(c))?.Name;

    /// <param name="from">The quoted <c>"schema"."table"</c>.</param>
    /// <param name="maxRows">The most rows the query returns; one more is selected so the caller can tell the limit was reached.</param>
    /// <param name="perRow">With no group column, return each row's value instead of one aggregate (a histogram's input).</param>
    /// <summary>
    /// The groups' order. The group column is qualified with the table, so it is the column and not the <c>::text</c> output of the
    /// same name: without an order, groups follow the column's own type. With one, the sort key comes first and the group
    /// column breaks ties, so the order is total.
    /// </summary>
    internal static string OrderBy(WidgetConfig config, IReadOnlyDictionary<string, TableColumn> byName, string from, string aggregate)
    {
        var group = $"{from}.{DataSources.Quote(byName[config.GroupBy!].Name)} NULLS LAST";
        if (config.Sort is not { } sort)
        {
            return "ORDER BY " + group;
        }

        var key = sort.By == SortValues.ByValue ? $"({aggregate})" : ColumnSortKey(sort, byName[sort.Column!]);
        var direction = sort.Direction == SortValues.Desc ? "DESC" : "ASC";
        return $"ORDER BY {key} {direction} NULLS LAST, {group}";
    }

    /// <summary>
    /// The smallest value of the sort column in the group, compared the way the order says. Text read as a number is guarded, so a
    /// value that is not one is NULL (and goes last) instead of raising an error that would fail the widget.
    /// </summary>
    private static string ColumnSortKey(WidgetSort sort, TableColumn column)
    {
        var name = DataSources.Quote(column.Name);
        var asNumber = sort.CompareAs == SortValues.Number || (sort.CompareAs is null && column.Type is DataColumnType.Integer or DataColumnType.Decimal);
        return column.Type switch
        {
            DataColumnType.Boolean => $"bool_and({name})",
            DataColumnType.Date or DataColumnType.Timestamp => $"min({name})",
            DataColumnType.Integer or DataColumnType.Decimal when asNumber =>
                column.Type == DataColumnType.Decimal ? $"min(NULLIF({name}, 'NaN'))" : $"min({name})",
            _ when asNumber =>
                $"min(CASE WHEN {name}::text ~ '{NumberShape}' AND pg_input_is_valid({name}::text, 'numeric') THEN {name}::text::numeric END)",
            _ => $"min(NULLIF({name}::text, ''))",
        };
    }

    /// <summary>
    /// The text that reads as a number: an optional sign, digits with an optional point and digits, or a point and digits, an optional
    /// exponent, and spaces around. Bracket classes, not <c>\d</c>, so it reads the same under any locale and
    /// <c>standard_conforming_strings</c>. Whether the value fits <c>numeric</c> is <c>pg_input_is_valid</c>'s check.
    /// </summary>
    private const string NumberShape = "^[[:space:]]*[-+]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][-+]?[0-9]+)?[[:space:]]*$";

    /// <summary>
    /// What a map's tooltip shows of one column over the rows of a group, as text so PostgreSQL does the work of its own types: a number
    /// is summed; a date or timestamp is the earliest; true/false is false unless every row is true; text is the first in the column's
    /// collation, an empty text counting as no value, cut one character past what the response keeps so the cut can be told.
    /// </summary>
    private static string TooltipOutput(TableColumn column)
    {
        var name = DataSources.Quote(column.Name);
        return column.Type switch
        {
            DataColumnType.Integer or DataColumnType.Decimal => $"sum({name})::text",
            DataColumnType.Date or DataColumnType.Timestamp => $"min({name})::text",
            DataColumnType.Boolean => $"bool_and({name})",
            _ => $"left(min(NULLIF({name}::text, '')), {WidgetConfig.MaxTooltipText + 1})",
        };
    }

    /// <param name="dashboardFilters">
    /// The dashboard filters in force, already checked by <see cref="IndicatorValidation.CheckDashboardFilters"/>. Those the table has
    /// a column of the right kind for narrow the rows as a second set of filters, so a widget filter and a dashboard filter on the same
    /// column both hold; the others are <see cref="IndicatorQueryPlan.NotFiltered"/>, never missing.
    /// </param>
    public static IndicatorQueryPlan Plan(
        WidgetConfig config, IReadOnlyList<TableColumn> columns, string timeZone, string from, int maxRows, bool perRow = false,
        IReadOnlyList<AppliedDashboardFilter>? dashboardFilters = null, string? chartType = null)
    {
        var byName = columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

        // A column the table no longer has is reported, not an error; a filter on one is left out of the validation below.
        var missing = new List<string>();
        void Need(string? name)
        {
            if (name is not null && !byName.ContainsKey(name) && !missing.Contains(name))
            {
                missing.Add(name);
            }
        }

        var measure = config.Ratio is null ? config.Measure ?? DefaultMeasure(columns) : null;
        var errors = new FieldErrors();
        if (config.Ratio is null && measure is null)
        {
            errors.Add("measure", "The table has no numeric column to plot.");
        }

        Need(config.GroupBy);
        Need(config.SeriesBy);
        Need(config.GeometryColumn);
        Need(measure);
        Need(config.Ratio?.Numerator);
        Need(config.Ratio?.Denominator);
        foreach (var filter in config.Filters)
        {
            if (filter is not null && !string.IsNullOrEmpty(filter.Column))
            {
                Need(filter.Column);
            }
        }

        Need(config.Sort?.Column);

        // Tooltip columns come last, so a script change that removes several lists the widget's own columns first.
        foreach (var item in config.Tooltip)
        {
            Need(item?.Column);
        }

        if (config.Sort is { By: SortValues.ByColumn, Column: { } sortColumn } sortOrder
            && byName.TryGetValue(sortColumn, out var sortedColumn)
            && IndicatorValidation.SortColumnError(sortOrder, sortedColumn) is { } sortError)
        {
            errors.Add("sort.column", sortError);
        }

        foreach (var (name, field) in new[] { (measure, "measure"), (config.Ratio?.Numerator, "ratio.numerator"), (config.Ratio?.Denominator, "ratio.denominator") })
        {
            if (name is not null && byName.TryGetValue(name, out var column) && !IndicatorValidation.IsNumeric(column))
            {
                errors.Add(field, $"Column '{name}' is not a number, so it cannot be plotted.");
            }
        }

        var present = config.Filters.Where(f => f is null || string.IsNullOrEmpty(f.Column) || byName.ContainsKey(f.Column)).ToList();
        var query = DataQueryBuilder.Build(new DataQueryRequest(present), columns, timeZone, errors);
        var (dashboard, notFiltered) = PlanDashboardFilters(dashboardFilters ?? [], byName, columns, timeZone, errors);
        if (query is null || dashboard is null || errors.Any)
        {
            return new IndicatorQueryPlan(null, missing, errors.ToDictionary());
        }

        if (missing.Count > 0)
        {
            return new IndicatorQueryPlan(null, missing, null);
        }

        string Q(string name) => DataSources.Quote(byName[name].Name);
        var ratio = config.Ratio;
        var filters = string.Concat(new[] { query.Where, dashboard.Where }.Where(w => w.Length > 0).Select(w => " AND " + w));
        var grouped = config.GroupBy is not null;
        var rows = perRow && !grouped;

        // Rows that share the group are summed, or combined as a ratio of sums; a row with nothing to plot adds nothing, and a
        // group whose denominator sums to zero has no value (Q is the quoted column; the measure is a number, so the cast is safe).
        string aggregate;
        string plotted;
        string rowFilter;
        if (ratio is null)
        {
            aggregate = $"sum({Q(measure!)})::double precision";
            plotted = $"{Q(measure!)}::double precision";
            rowFilter = $"{Q(measure!)} IS NOT NULL";
        }
        else
        {
            aggregate = $"sum({Q(ratio.Numerator)})::double precision / nullif(sum({Q(ratio.Denominator)}), 0)";
            plotted = $"{Q(ratio.Numerator)}::double precision / nullif({Q(ratio.Denominator)}, 0)";
            rowFilter = $"({Q(ratio.Numerator)} IS NOT NULL OR {Q(ratio.Denominator)} IS NOT NULL)";
        }

        if (config.SeriesBy is not null && grouped)
        {
            var series = SeriesQueryBuilder.Build(config, chartType, byName, from, aggregate, rowFilter, query, dashboard, maxRows);
            return new IndicatorQueryPlan(null, missing, null, notFiltered, series);
        }

        var group = grouped ? $"{Q(config.GroupBy!)}::text" : "NULL::text";
        string geometry;
        if (config.GeometryColumn is null)
        {
            geometry = "NULL::text";
        }
        else
        {
            geometry = rows ? $"{Q(config.GeometryColumn)}::text" : $"min({Q(config.GeometryColumn)}::text)";
        }

        // A map (a grouped query that selects geometry) also returns how many rows each group combines and its tooltip columns,
        // over the same rows the WHERE keeps (see TooltipOutput).
        var isMap = grouped && config.GeometryColumn is not null;
        var tooltip = isMap ? config.Tooltip.Where(item => item is not null).ToList() : [];
        var extra = isMap ? ", count(*)" + string.Concat(tooltip.Select(item => ", " + TooltipOutput(byName[item.Column]))) : "";

        string sql;
        if (rows)
        {
            sql =
                $"""
                SELECT {group}, {plotted}, {geometry}
                FROM {from}
                WHERE {rowFilter} AND ({plotted}) IS NOT NULL{filters}
                LIMIT @{IndicatorSql.LimitParameter}
                """;
        }
        else
        {
            var groupBy = grouped ? $"GROUP BY {Q(config.GroupBy!)}" : "";
            var orderBy = grouped ? OrderBy(config, byName, from, aggregate) : "";
            sql =
                $"""
                SELECT {group}, ({aggregate}), {geometry}{extra}
                FROM {from}
                WHERE {rowFilter}{filters}
                {groupBy}
                HAVING ({aggregate}) IS NOT NULL
                {orderBy}
                LIMIT @{IndicatorSql.LimitParameter}
                """;
        }

        // Both sets bind the project's zone as tz when they have a timestamp range: it is bound once.
        var parameters = new List<SqlParam>(query.Parameters);
        parameters.AddRange(dashboard.Parameters.Where(p => !parameters.Any(q => q.Name == p.Name)));
        parameters.Add(new(IndicatorSql.LimitParameter, NpgsqlDbType.Integer, maxRows + 1));
        return new IndicatorQueryPlan(
            new IndicatorSql(sql, parameters, isMap ? tooltip.Select(item => byName[item.Column].Type).ToList() : null), missing, null, notFiltered);
    }

    /// <summary>
    /// The dashboard filters the table can take, as a second set of filters with parameters <c>d0</c>, <c>d1min</c>…, and the columns of
    /// those it cannot. An error is keyed <c>dashboardFilters[i]</c> at the filter's index in the request. Null when there is an error.
    /// </summary>
    private static (DataQuery? Query, IReadOnlyList<string> NotFiltered) PlanDashboardFilters(
        IReadOnlyList<AppliedDashboardFilter> dashboardFilters, Dictionary<string, TableColumn> byName, IReadOnlyList<TableColumn> columns,
        string timeZone, FieldErrors errors)
    {
        var notFiltered = new List<string>();
        var applied = new List<(int Index, AppliedDashboardFilter Filter)>();
        for (var i = 0; i < dashboardFilters.Count; i++)
        {
            var filter = dashboardFilters[i];
            if (byName.TryGetValue(filter.Column!, out var column) && DashboardFilterKinds.Matches(filter.Kind!, column.Type))
            {
                applied.Add((i, filter));
            }
            else
            {
                notFiltered.Add(filter.Column!);
            }
        }

        // Each applied filter is built against its column as its kind reads it: a number filter compares as a decimal on an integer column.
        var kinds = applied.ToDictionary(a => a.Filter.Column!, a => a.Filter.Kind!, StringComparer.Ordinal);
        var asFiltered = columns.Select(c => kinds.TryGetValue(c.Name, out var kind) ? DashboardFilterKinds.AsFiltered(kind, c) : c).ToList();
        var dashboardErrors = new FieldErrors();
        var query = DataQueryBuilder.Build(
            new DataQueryRequest(applied.Select(a => DashboardFilterKinds.ToDataFilter(a.Filter.Column!, a.Filter.Kind!, a.Filter.Value)).ToList()),
            asFiltered, timeZone, dashboardErrors, parameterPrefix: "d", fieldName: "dashboardFilters");
        foreach (var (field, messages) in dashboardErrors.ToDictionary())
        {
            // dashboardFilters[j] is the j-th applied filter; the request knows it by its own index.
            var key = field.StartsWith("dashboardFilters[", StringComparison.Ordinal)
                && int.TryParse(field["dashboardFilters[".Length..^1], System.Globalization.CultureInfo.InvariantCulture, out var j)
                ? $"dashboardFilters[{applied[j].Index}]"
                : field;
            foreach (var message in messages)
            {
                errors.Add(key, message);
            }
        }

        return (query, notFiltered);
    }
}
