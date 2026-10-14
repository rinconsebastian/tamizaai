using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.Indicators;

/// <summary>A widget query as SQL: identifiers from the catalog (quoted), every value a parameter.</summary>
/// <param name="Sql">Selects <c>group, value, geometry</c> in that order.</param>
public sealed record IndicatorSql(string Sql, IReadOnlyList<SqlParam> Parameters)
{
    public const string LimitParameter = "limit";
}

/// <summary>
/// The outcome of planning a query against the table's columns. Exactly one of <see cref="Errors"/> (a malformed request) or
/// <see cref="Sql"/> is set unless something is <see cref="MissingColumns"/>, in which case there is nothing to run.
/// </summary>
public sealed record IndicatorQueryPlan(IndicatorSql? Sql, IReadOnlyList<string> MissingColumns, IDictionary<string, string[]>? Errors);

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
    public static IndicatorQueryPlan Plan(
        WidgetConfig config, IReadOnlyList<TableColumn> columns, string timeZone, string from, int maxRows, bool perRow = false)
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

        foreach (var (name, field) in new[] { (measure, "measure"), (config.Ratio?.Numerator, "ratio.numerator"), (config.Ratio?.Denominator, "ratio.denominator") })
        {
            if (name is not null && byName.TryGetValue(name, out var column) && !IndicatorValidation.IsNumeric(column))
            {
                errors.Add(field, $"Column '{name}' is not a number, so it cannot be plotted.");
            }
        }

        var present = config.Filters.Where(f => f is null || string.IsNullOrEmpty(f.Column) || byName.ContainsKey(f.Column)).ToList();
        var query = DataQueryBuilder.Build(new DataQueryRequest(present), columns, timeZone, errors);
        if (query is null || errors.Any)
        {
            return new IndicatorQueryPlan(null, missing, errors.ToDictionary());
        }

        if (missing.Count > 0)
        {
            return new IndicatorQueryPlan(null, missing, null);
        }

        string Q(string name) => DataSources.Quote(byName[name].Name);
        var ratio = config.Ratio;
        var filters = query.Where.Length == 0 ? "" : " AND " + query.Where;
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
            var orderBy = grouped ? $"ORDER BY {Q(config.GroupBy!)} NULLS LAST" : "";
            sql =
                $"""
                SELECT {group}, ({aggregate}), {geometry}
                FROM {from}
                WHERE {rowFilter}{filters}
                {groupBy}
                HAVING ({aggregate}) IS NOT NULL
                {orderBy}
                LIMIT @{IndicatorSql.LimitParameter}
                """;
        }

        var parameters = new List<SqlParam>(query.Parameters)
        {
            new(IndicatorSql.LimitParameter, NpgsqlDbType.Integer, maxRows + 1),
        };
        return new IndicatorQueryPlan(new IndicatorSql(sql, parameters), missing, null);
    }
}
