using NpgsqlTypes;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Indicators;

/// <summary>
/// The statements of a widget with a series (design D3). The domain statement, when there is one, lists the series values by their
/// total; the cells statement is built for the values the widget keeps.
/// </summary>
/// <param name="Domain">Selects <c>series, total</c> for the non-missing series values, largest total first, plus one row of null series when some rows have no value. Null when the domain is fixed.</param>
/// <param name="FixedDomain">A population pyramid's two sides or a Likert widget's scale, in order; null when the domain comes from the rows.</param>
/// <param name="MaxSeries">The most series the widget shows before the smallest are folded into "Other".</param>
/// <param name="NaturalGroups">Whether the groups are listed in natural order (a line chart, a pyramid) and not in the group column's own order or the widget's.</param>
/// <param name="Cells">The cells statement for the series values to keep.</param>
public sealed record SeriesQueryPlan(
    IndicatorSql? Domain,
    IReadOnlyList<string>? FixedDomain,
    int MaxSeries,
    bool NaturalGroups,
    Func<IReadOnlyList<string>, IndicatorSql> Cells);

/// <summary>The parts of the cells statement's result, in its first column.</summary>
public static class SeriesParts
{
    public const int GroupTotal = 0;
    public const int Cell = 1;
    public const int SeriesTotal = 2;
    public const int Grand = 3;
    public const int LeftOut = 4;
}

/// <summary>Builds the SQL of a widget with a series: identifiers from the catalog (quoted), every value a parameter.</summary>
public static class SeriesQueryBuilder
{
    public const string CapParameter = "cap";
    public const string KeptParameter = "kept";

    private const string SeriesColumn = "\"__series\"";
    private const string KindColumn = "\"__kind\"";

    /// <param name="aggregate">The widget's aggregate over a set of rows: a sum, or a ratio of sums.</param>
    /// <param name="rowFilter">The condition that keeps the rows that have something to plot.</param>
    /// <param name="own">The widget's own filters; the series domain is read through these only, so a dashboard filter never changes it.</param>
    /// <param name="dashboard">The dashboard filters that narrow the cells and the totals too.</param>
    public static SeriesQueryPlan Build(
        WidgetConfig config, string? chartType, IReadOnlyDictionary<string, TableColumn> byName, string from, string aggregate, string rowFilter,
        DataQuery own, DataQuery dashboard, int maxRows)
    {
        string Q(string name) => DataSources.Quote(byName[name].Name);
        var group = Q(config.GroupBy!);
        var series = Q(config.SeriesBy!);
        var ownWhere = own.Where.Length > 0 ? " AND " + own.Where : "";
        var allWhere = ownWhere + (dashboard.Where.Length > 0 ? " AND " + dashboard.Where : "");

        var fixedDomain = FixedDomain(config, chartType);
        var maxSeries = chartType is ChartTypes.Heatmap or ChartTypes.Crosstab ? WidgetConfig.MaxMatrixSeries : WidgetConfig.MaxSeries;
        var natural = chartType is ChartTypes.Line or ChartTypes.Pyramid;

        // Both sets bind the project's zone as tz when they have a timestamp range: it is bound once.
        var parameters = new List<SqlParam>(own.Parameters);
        parameters.AddRange(dashboard.Parameters.Where(p => !parameters.Any(q => q.Name == p.Name)));

        IndicatorSql? domain = null;
        if (fixedDomain is null)
        {
            // The values with the largest totals first, one more than the widget shows so the caller can tell that some will be folded.
            var sql =
                $"""
                (SELECT {series}::text, ({aggregate}) FROM {from}
                 WHERE {rowFilter}{ownWhere} AND {series} IS NOT NULL
                 GROUP BY {series} ORDER BY 2 DESC NULLS LAST, 1 LIMIT @{CapParameter})
                UNION ALL
                (SELECT NULL::text, ({aggregate}) FROM {from}
                 WHERE {rowFilter}{ownWhere} AND {series} IS NULL
                 HAVING count(*) > 0)
                """;
            var domainParameters = new List<SqlParam>(own.Parameters) { new(CapParameter, NpgsqlDbType.Integer, maxSeries + 1) };
            domain = new IndicatorSql(sql, domainParameters);
        }

        // A pyramid and a Likert widget draw only the rows of their fixed values that have a group; the rest are counted.
        var included = fixedDomain is null ? "" : $"{group} IS NOT NULL AND {series} IS NOT NULL AND {series}::text = ANY(@{KeptParameter})";
        // The folded series lose their value, so their rows add up to one "Other" cell and one "Other" total.
        var seriesText = fixedDomain is null
            ? $"CASE WHEN {series}::text = ANY(@{KeptParameter}) THEN {series}::text END"
            : $"{series}::text";
        var kind = fixedDomain is null
            ? $"CASE WHEN {series} IS NULL THEN '{SeriesKinds.None}' WHEN {series}::text = ANY(@{KeptParameter}) THEN '{SeriesKinds.Value}' ELSE '{SeriesKinds.Other}' END"
            : $"'{SeriesKinds.Value}'";
        var rank = IndicatorQueryBuilder.OrderBy(config, new Dictionary<string, TableColumn>(byName, StringComparer.Ordinal), "src", aggregate);
        var leftOut = fixedDomain is null
            ? ""
            : $"""

              UNION ALL
              (SELECT {SeriesParts.LeftOut}, NULL::text, NULL::text, NULL::text, NULL::double precision, count(*), NULL::bigint
               FROM {from} WHERE {rowFilter}{allWhere} AND NOT ({included}))
              """;

        IndicatorSql Cells(IReadOnlyList<string> kept)
        {
            var sql =
                $"""
                WITH src AS (
                  SELECT {from}.*, {seriesText} AS {SeriesColumn}, {kind} AS {KindColumn}
                  FROM {from}
                  WHERE {rowFilter}{allWhere}{(included.Length > 0 ? " AND " + included : "")}
                ),
                grp AS (
                  SELECT {group} AS g, ({aggregate}) AS v, count(*) AS n, row_number() OVER ({rank}) AS rk
                  FROM src GROUP BY {group} HAVING ({aggregate}) IS NOT NULL
                ),
                cell AS (
                  SELECT {group} AS g, {KindColumn} AS k, {SeriesColumn} AS s, ({aggregate}) AS v, count(*) AS n
                  FROM src GROUP BY {group}, {KindColumn}, {SeriesColumn} HAVING ({aggregate}) IS NOT NULL
                )
                (SELECT {SeriesParts.GroupTotal} AS part, g::text, NULL::text, NULL::text, v, n, rk FROM grp ORDER BY rk LIMIT @{IndicatorSql.LimitParameter})
                UNION ALL
                (SELECT {SeriesParts.Cell}, cell.g::text, cell.k, cell.s, cell.v, cell.n, grp.rk
                 FROM cell JOIN grp ON cell.g IS NOT DISTINCT FROM grp.g
                 ORDER BY grp.rk, cell.k, cell.s LIMIT @{IndicatorSql.LimitParameter})
                UNION ALL
                (SELECT {SeriesParts.SeriesTotal}, NULL::text, {KindColumn}, {SeriesColumn}, ({aggregate}), count(*), NULL::bigint
                 FROM src GROUP BY {KindColumn}, {SeriesColumn})
                UNION ALL
                (SELECT {SeriesParts.Grand}, NULL::text, NULL::text, NULL::text, ({aggregate}), count(*), NULL::bigint FROM src){leftOut}
                """;
            var all = new List<SqlParam>(parameters)
            {
                new(KeptParameter, NpgsqlDbType.Array | NpgsqlDbType.Text, kept.ToArray()),
                new(IndicatorSql.LimitParameter, NpgsqlDbType.Integer, maxRows + 1),
            };
            return new IndicatorSql(sql, all);
        }

        return new SeriesQueryPlan(domain, fixedDomain, maxSeries, natural, Cells);
    }

    /// <summary>A population pyramid's two sides or a Likert widget's scale, in order; null for every other kind.</summary>
    public static IReadOnlyList<string>? FixedDomain(WidgetConfig config, string? chartType) => chartType switch
    {
        ChartTypes.Pyramid when config.Pyramid is { Left: { } left, Right: { } right } => [left, right],
        ChartTypes.Likert => config.Scale.Where(step => step?.Value is not null).Select(step => step.Value!).ToList(),
        _ => null,
    };
}
