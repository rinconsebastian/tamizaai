using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.Indicators;

/// <summary>A widget query as SQL: identifiers from the catalog (quoted), every value a parameter.</summary>
/// <param name="Sql">Selects <c>indicator, group, sum, geometry</c> in that order.</param>
public sealed record IndicatorSql(string Sql, IReadOnlyList<SqlParam> Parameters)
{
    public const string IndicatorsParameter = "indicators";
    public const string LimitParameter = "limit";
}

/// <summary>
/// The outcome of planning a query against the table's columns. Exactly one of <see cref="Errors"/> (a malformed request) or
/// <see cref="Sql"/> is set unless something is <see cref="MissingColumns"/>, in which case there is nothing to run (design D8).
/// </summary>
public sealed record IndicatorQueryPlan(IndicatorSql? Sql, IReadOnlyList<string> MissingColumns, IDictionary<string, string[]>? Errors);

/// <summary>Validates a widget query against the published table's columns and turns it into SQL (design D2 and D9).</summary>
public static class IndicatorQueryBuilder
{
    public const string IndicatorColumn = "indicator";

    /// <param name="from">The quoted <c>"schema"."table"</c>.</param>
    /// <param name="maxRows">The most rows the query returns; one more is selected so the caller can tell the limit was reached.</param>
    public static IndicatorQueryPlan Plan(WidgetConfig config, IReadOnlyList<TableColumn> columns, string timeZone, string from, int maxRows)
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

        Need(config.GroupBy);
        Need(config.GeometryColumn);
        foreach (var filter in config.Filters)
        {
            if (filter is not null && !string.IsNullOrEmpty(filter.Column))
            {
                Need(filter.Column);
            }
        }

        Need(IndicatorColumn);
        Need(config.ValueField);

        var present = config.Filters.Where(f => f is null || string.IsNullOrEmpty(f.Column) || byName.ContainsKey(f.Column)).ToList();
        var errors = new FieldErrors();
        var query = DataQueryBuilder.Build(new DataQueryRequest(present), columns, timeZone, errors);

        if (byName.TryGetValue(config.ValueField, out var valueColumn) && valueColumn.Type is not (DataColumnType.Integer or DataColumnType.Decimal))
        {
            errors.Add("valueField", $"Column '{valueColumn.Name}' is not a number, so it cannot be plotted.");
        }

        if (query is null || errors.Any)
        {
            return new IndicatorQueryPlan(null, missing, errors.ToDictionary());
        }

        if (missing.Count > 0)
        {
            return new IndicatorQueryPlan(null, missing, null);
        }

        var group = DataSources.Quote(byName[config.GroupBy].Name);
        var indicator = DataSources.Quote(byName[IndicatorColumn].Name);
        var value = DataSources.Quote(valueColumn!.Name);
        var geometry = config.GeometryColumn is null ? "NULL::text" : $"min({DataSources.Quote(byName[config.GeometryColumn].Name)}::text)";
        var filters = query.Where.Length == 0 ? "" : " AND " + query.Where;

        // Rows that share the indicator and the group are summed here (design D9); a row with no value to plot adds nothing.
        var sql =
            $"""
            SELECT {indicator}::text, {group}::text, sum({value})::double precision, {geometry}
            FROM {from}
            WHERE {indicator}::text = ANY(@{IndicatorSql.IndicatorsParameter}) AND {value} IS NOT NULL{filters}
            GROUP BY {indicator}::text, {group}
            ORDER BY array_position(@{IndicatorSql.IndicatorsParameter}, {indicator}::text), {group} NULLS LAST
            LIMIT @{IndicatorSql.LimitParameter}
            """;

        var parameters = new List<SqlParam>(query.Parameters)
        {
            new(IndicatorSql.IndicatorsParameter, NpgsqlDbType.Array | NpgsqlDbType.Text, config.Indicators.ToArray()),
            new(IndicatorSql.LimitParameter, NpgsqlDbType.Integer, maxRows + 1),
        };
        return new IndicatorQueryPlan(new IndicatorSql(sql, parameters), missing, null);
    }
}
