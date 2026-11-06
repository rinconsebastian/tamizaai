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
/// <param name="NaturalGroups">Whether the groups are listed in natural order (the kinds that cannot be ordered: a line chart, a heat map, a pyramid, Likert bars, a cross table) and not in the group column's own order or the widget's.</param>
/// <param name="Cells">The cells statement for the series values to keep.</param>
/// <param name="Counted">
/// The statement that counts the n of every group, cell, series and the whole widget once across the answers (design D5 of
/// add-small-sample-warnings), for the series values to keep; null unless the indicator declares answer columns and the marks have an n.
/// </param>
public sealed record SeriesQueryPlan(
    IndicatorSql? Domain,
    IReadOnlyList<string>? FixedDomain,
    int MaxSeries,
    bool NaturalGroups,
    Func<IReadOnlyList<string>, IndicatorSql> Cells,
    Func<IReadOnlyList<string>, IndicatorSql>? Counted = null);

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
    /// <param name="bases">
    /// What the indicator declares for its columns (design D5 of add-small-sample-warnings): the figure outputs every branch of the cells
    /// statement appends after its seven columns, and the counted statement. Null: the statement of an indicator that declares nothing.
    /// </param>
    /// <param name="exclusion">
    /// The rows the widget leaves out as no data (design D6 of add-small-sample-warnings): out of the domain, the cells, the totals and the
    /// left-out count alike, so a left-out series takes no slot and no color. Null: nothing is left out and every statement is as it was.
    /// </param>
    /// <param name="answers">
    /// The answer columns of a ratio (design D4 of improve-stat-cards). Of single choice, each group, cell, series total and the grand total
    /// takes the nested form, counting its base once across the answers; of multiple choice, the group totals, the series totals and the
    /// grand total have no value when their rows hold several answers, and so does a cell that does (the folded "Other"). Null or empty:
    /// every statement is as it was. The <paramref name="aggregate"/> stays what the domain, the rank of the groups and the
    /// <c>HAVING</c> of the multiple-choice form read.
    /// </param>
    public static SeriesQueryPlan Build(
        WidgetConfig config, string? chartType, IReadOnlyDictionary<string, TableColumn> byName, string from, string aggregate, string rowFilter,
        DataQuery own, DataQuery dashboard, int maxRows, IndicatorBases? bases = null, NoDataExclusion? exclusion = null, AnswerColumns? answers = null)
    {
        string Q(string name) => DataSources.Quote(byName[name].Name);
        answers ??= AnswerColumns.None;
        var nested = answers.Corrects;
        var sums = BaseQueryBuilder.Sums(config, bases, Q, nested);
        // The sums are CTE columns of each group and cell (fn, fd, fb), and sums over the rows of a total.
        var named = new[] { ("fn", sums.Numerator), ("fd", sums.Denominator), ("fb", sums.Base) }.Where(pair => pair.Item2 is not null).ToList();
        var cteFigures = string.Concat(named.Select(pair => $", {pair.Item2} AS {pair.Item1}"));
        string Columns(string prefix) => string.Concat(named.Select(pair => $", {prefix}{pair.Item1}"));
        var totalFigures = string.Concat(named.Select(pair => $", {pair.Item2}"));
        var nullFigures = string.Concat(named.Select(_ => ", NULL::double precision"));
        var figures = sums.Any ? sums.Ordinals(7, widgetN: false) : null;
        var group = Q(config.GroupBy!);
        var series = Q(config.SeriesBy!);
        var ownWhere = own.Where.Length > 0 ? " AND " + own.Where : "";
        var allWhere = ownWhere + (dashboard.Where.Length > 0 ? " AND " + dashboard.Where : "") + exclusion?.Condition;

        var fixedDomain = FixedDomain(config, chartType);
        var maxSeries = chartType is ChartTypes.Heatmap or ChartTypes.Crosstab ? WidgetConfig.MaxMatrixSeries : WidgetConfig.MaxSeries;
        // The kinds an analyst cannot order list their groups in natural order, so age bands read 0-4, 5-9, 10-14; bars and columns keep the column's own order.
        var natural = chartType is ChartTypes.Line or ChartTypes.Pyramid or ChartTypes.Heatmap or ChartTypes.Likert or ChartTypes.Crosstab;

        // Both sets bind the project's zone as tz when they have a timestamp range: it is bound once.
        var parameters = new List<SqlParam>(own.Parameters);
        parameters.AddRange(dashboard.Parameters.Where(p => !parameters.Any(q => q.Name == p.Name)));
        parameters.AddRange(exclusion?.Parameters ?? []);

        // The values with the largest totals first, one more than the widget shows so the caller can tell that some will be folded.
        var domain = fixedDomain is null ? Domain(series, from, aggregate, rowFilter, own, maxSeries + 1, exclusion) : null;

        // A pyramid and a Likert widget draw only the rows of their fixed values that have a group; the rest are counted.
        var included = fixedDomain is null ? "" : $"{group} IS NOT NULL AND {series} IS NOT NULL AND {series}::text = ANY(@{KeptParameter})";
        // The folded series lose their value, so their rows add up to one "Other" cell and one "Other" total.
        var seriesText = fixedDomain is null
            ? $"CASE WHEN {series}::text = ANY(@{KeptParameter}) THEN {series}::text END"
            : $"{series}::text";
        var kind = fixedDomain is null
            ? $"CASE WHEN {series} IS NULL THEN '{SeriesKinds.None}' WHEN {series}::text = ANY(@{KeptParameter}) THEN '{SeriesKinds.Value}' ELSE '{SeriesKinds.Other}' END"
            : $"'{SeriesKinds.Value}'";
        var rank = nested
            ? IndicatorQueryBuilder.OrderBy(config, new Dictionary<string, TableColumn>(byName, StringComparer.Ordinal), "src", NestedRatio.Value, new NestedSort("g"))
            : IndicatorQueryBuilder.OrderBy(config, new Dictionary<string, TableColumn>(byName, StringComparer.Ordinal), "src", aggregate);
        var leftOut = fixedDomain is null
            ? ""
            : $"""

              UNION ALL
              (SELECT {SeriesParts.LeftOut}, NULL::text, NULL::text, NULL::text, NULL::double precision, count(*), NULL::bigint{nullFigures}
               FROM {from} WHERE {rowFilter}{allWhere} AND NOT ({included}))
              """;

        // The widget's rows with the series text and kind of each, as the cells statement and the counted statement both read them.
        string Source(string indent = "") =>
            $"SELECT {from}.*, {seriesText} AS {SeriesColumn}, {kind} AS {KindColumn}\n{indent}FROM {from}\n{indent}WHERE {rowFilter}{allWhere}{(included.Length > 0 ? " AND " + included : "")}";

        // A ratio over declared answers (design D4 of improve-stat-cards). Single choice: every value and total reads a nested statement
        // that sums per mark and answer and divides by the largest denominator sum. Multiple choice: the totals have no value when their
        // rows hold several answers, a cell that does is left out, and the groups keep the plain aggregate in their HAVING and their rank.
        var having = nested ? NestedRatio.Value : aggregate;
        var total = nested ? NestedRatio.Value : answers.OneAnswerOnly(aggregate);
        var count = nested ? $"sum({NestedRatio.Rows})::bigint" : "count(*)";
        var cellGuard = answers.Refuses ? $" AND {answers.DistinctAnswers} <= 1" : "";
        var carriedSort = NestedSort.Carry(config, byName);
        string NestedSource(string levelItems, string levelKeys, string? exceptGroup, bool carrySort)
        {
            var (select, keys) = NestedRatio.Answers(answers.Single, exceptGroup);
            var items = new List<string>();
            if (levelItems.Length > 0)
            {
                items.Add(levelItems);
            }

            items.AddRange(select);
            items.Add(NestedRatio.InnerSums(Q(config.Ratio!.Numerator), Q(config.Ratio!.Denominator)));
            if (sums.Base is not null)
            {
                items.Add($"sum({Q(bases!.BaseColumn!)}) AS {NestedRatio.Base}");
            }

            var list = (levelKeys.Length > 0 ? new[] { levelKeys } : []).Concat(keys);
            return $"(SELECT {string.Join(", ", items)}{(carrySort ? carriedSort : null)} FROM src GROUP BY {string.Join(", ", list)}) a";
        }

        var grpFrom = nested ? NestedSource($"{group} AS g", group, group, true) : "src";
        var grpKey = nested ? "g" : group;
        var cellFrom = nested ? NestedSource($"{group} AS g, {KindColumn} AS k, {SeriesColumn} AS s", $"{group}, {KindColumn}, {SeriesColumn}", group, false) : "src";
        var (cellG, cellK, cellS) = nested ? ("g", "k", "s") : (group, KindColumn, SeriesColumn);
        var seriesFrom = nested ? NestedSource($"{KindColumn} AS k, {SeriesColumn} AS s", $"{KindColumn}, {SeriesColumn}", null, false) : "src";
        var (seriesK, seriesS) = nested ? ("k", "s") : (KindColumn, SeriesColumn);
        var seriesKeys = nested ? "k, s" : $"{KindColumn}, {SeriesColumn}";
        var grandFrom = nested ? NestedSource("", "", null, false) : "src";

        IndicatorSql Cells(IReadOnlyList<string> kept)
        {
            var sql =
                $"""
                WITH src AS (
                  {Source("  ")}
                ),
                grp AS (
                  SELECT {grpKey} AS g, ({total}) AS v, {count} AS n, row_number() OVER ({rank}) AS rk{cteFigures}
                  FROM {grpFrom} GROUP BY {grpKey} HAVING ({having}) IS NOT NULL
                ),
                cell AS (
                  SELECT {cellG} AS g, {cellK} AS k, {cellS} AS s, ({having}) AS v, {count} AS n{cteFigures}
                  FROM {cellFrom} GROUP BY {cellG}, {cellK}, {cellS} HAVING ({having}) IS NOT NULL{cellGuard}
                )
                (SELECT {SeriesParts.GroupTotal} AS part, g::text, NULL::text, NULL::text, v, n, rk{Columns("")} FROM grp ORDER BY rk LIMIT @{IndicatorSql.LimitParameter})
                UNION ALL
                (SELECT {SeriesParts.Cell}, cell.g::text, cell.k, cell.s, cell.v, cell.n, grp.rk{Columns("cell.")}
                 FROM cell JOIN grp ON cell.g IS NOT DISTINCT FROM grp.g
                 ORDER BY grp.rk, cell.k, cell.s LIMIT @{IndicatorSql.LimitParameter})
                UNION ALL
                (SELECT {SeriesParts.SeriesTotal}, NULL::text, {seriesK}, {seriesS}, ({total}), {count}, NULL::bigint{totalFigures}
                 FROM {seriesFrom} GROUP BY {seriesKeys})
                UNION ALL
                (SELECT {SeriesParts.Grand}, NULL::text, NULL::text, NULL::text, ({total}), {count}, NULL::bigint{totalFigures} FROM {grandFrom}){leftOut}
                """;
            var all = new List<SqlParam>(parameters)
            {
                new(KeptParameter, NpgsqlDbType.Array | NpgsqlDbType.Text, kept.ToArray()),
                new(IndicatorSql.LimitParameter, NpgsqlDbType.Integer, maxRows + 1),
            };
            return new IndicatorSql(sql, all, Figures: figures);
        }

        // With answer columns declared, a second statement counts each mark's n once across the answers over the same rows (design D3, D5).
        Func<IReadOnlyList<string>, IndicatorSql>? counted = null;
        if (bases is not null && BaseQueryBuilder.CountsAnswers(bases, sums))
        {
            var answered = bases.Answers.Select(Q).ToList();
            counted = kept => BaseQueryBuilder.CountedSeries(
                group, SeriesColumn, KindColumn, answered, sums.NColumn!, Source(),
                [.. parameters, new(KeptParameter, NpgsqlDbType.Array | NpgsqlDbType.Text, kept.ToArray())]);
        }

        return new SeriesQueryPlan(domain, fixedDomain, maxSeries, natural, Cells, counted);
    }

    /// <summary>
    /// The statement that lists the values of a column by their total over the widget's own rows (design D3): <c>value, total</c> for
    /// the non-missing values, largest total first and then by value, at most <paramref name="cap"/> of them, plus one row of null value
    /// when some rows have none. The dashboard's filters are never part of it, so a dashboard filter never changes the values a widget
    /// keeps. A pie ranks its groups differently, the missing one in its place (<see cref="IndicatorQueryBuilder.PieDomain"/>).
    /// </summary>
    /// <param name="column">The quoted column.</param>
    /// <param name="from">The quoted <c>"schema"."table"</c>.</param>
    /// <param name="aggregate">The widget's aggregate over a set of rows: a sum, or a ratio of sums.</param>
    /// <param name="rowFilter">The condition that keeps the rows that have something to plot.</param>
    /// <param name="own">The widget's own filters.</param>
    /// <param name="cap">How many values to read; one more than the widget shows tells the caller that some will be folded.</param>
    /// <param name="exclusion">The rows the widget leaves out as no data (design D6 of add-small-sample-warnings); null for a widget that leaves none out.</param>
    public static IndicatorSql Domain(string column, string from, string aggregate, string rowFilter, DataQuery own, int cap, NoDataExclusion? exclusion = null)
    {
        var ownWhere = (own.Where.Length > 0 ? " AND " + own.Where : "") + exclusion?.Condition;
        var sql =
            $"""
            (SELECT {column}::text, ({aggregate}) FROM {from}
             WHERE {rowFilter}{ownWhere} AND {column} IS NOT NULL
             GROUP BY {column} ORDER BY 2 DESC NULLS LAST, 1 LIMIT @{CapParameter})
            UNION ALL
            (SELECT NULL::text, ({aggregate}) FROM {from}
             WHERE {rowFilter}{ownWhere} AND {column} IS NULL
             HAVING count(*) > 0)
            """;
        var parameters = new List<SqlParam>(own.Parameters);
        parameters.AddRange(exclusion?.Parameters ?? []);
        parameters.Add(new(CapParameter, NpgsqlDbType.Integer, cap));
        return new IndicatorSql(sql, parameters);
    }

    /// <summary>A population pyramid's two sides or a Likert widget's scale, in order; null for every other kind.</summary>
    public static IReadOnlyList<string>? FixedDomain(WidgetConfig config, string? chartType) => chartType switch
    {
        ChartTypes.Pyramid when config.Pyramid is { Left: { } left, Right: { } right } => [left, right],
        ChartTypes.Likert => config.Scale.Where(step => step?.Value is not null).Select(step => step.Value!).ToList(),
        _ => null,
    };
}
