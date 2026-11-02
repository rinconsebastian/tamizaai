using NpgsqlTypes;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Indicators;

/// <summary>The columns of a box plot statement, by position.</summary>
public static class BoxColumns
{
    public const int Group = 0;
    public const int Values = 1;
    public const int Min = 2;
    public const int Quartiles = 3;
    public const int Max = 4;
    public const int Mean = 5;
    public const int LowerWhisker = 6;
    public const int UpperWhisker = 7;
    public const int Outliers = 8;
    public const int OutlierValues = 9;
}

/// <summary>
/// The statement of a box plot (design D3 of add-boxplot-widget): one row per box with the number of values, the minimum, the quartiles
/// (<c>percentile_cont</c>, the linear interpolation pandas' <c>quantile</c> uses), the maximum, the mean, Tukey's whisker ends, the number
/// of outliers and the outliers farthest from the median. Identifiers come from the catalog (quoted); every value is a parameter.
/// </summary>
public static class BoxQueryBuilder
{
    public const string MaxOutliersParameter = "maxoutliers";

    /// <param name="group">The quoted group column, or null for the one box of a widget with no group column.</param>
    /// <param name="from">The quoted <c>"schema"."table"</c>.</param>
    /// <param name="plotted">One row's value: its measure, or its numerator over its own denominator.</param>
    /// <param name="rowFilter">The condition that keeps the rows that have something to plot.</param>
    /// <param name="filters">The widget's and the dashboard's filters, each already started with <c>AND</c>.</param>
    /// <param name="parameters">The filters' parameters.</param>
    /// <param name="median">Null for the group column's own order, otherwise the direction of the order by median.</param>
    /// <param name="maxRows">The most boxes the query returns; one more is selected so the caller can tell the limit was reached.</param>
    public static IndicatorSql Build(
        string? group, string from, string plotted, string rowFilter, string filters, IReadOnlyList<SqlParam> parameters, string? median, int maxRows)
    {
        var groupColumn = group ?? "NULL::text";
        var fence = WidgetConfig.WhiskerRange.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var order = median is null ? "f.g NULLS LAST" : $"f.q[2] {median}, f.g NULLS LAST";

        // src, fence and marked are read more than once, so they are materialized: the box number b must be the same everywhere.
        // The joins between boxes use b and the joins of values use equality on g, so none needs IS NOT DISTINCT FROM, which PostgreSQL
        // cannot hash. The rows with no group value meet the missing box in the second branch of marked.
        var sql =
            $"""
            WITH src AS MATERIALIZED (
              SELECT {groupColumn} AS g, ({plotted}) AS x
              FROM {from}
              WHERE {rowFilter} AND ({plotted}) > '-Infinity'::double precision AND ({plotted}) < 'Infinity'::double precision{filters}
            ),
            box AS (
              SELECT g, row_number() OVER () AS b, count(*) AS n, min(x) AS lo, max(x) AS hi, sum(x) / count(*) AS mean,
                     percentile_cont(ARRAY[0.25, 0.5, 0.75]) WITHIN GROUP (ORDER BY x) AS q
              FROM src GROUP BY g
            ),
            fence AS MATERIALIZED (
              SELECT box.*, q[1] - {fence} * (q[3] - q[1]) AS lf, q[3] + {fence} * (q[3] - q[1]) AS uf FROM box
            ),
            marked AS MATERIALIZED (
              SELECT f.b, s.x, f.q[2] AS med, (s.x < f.lf OR s.x > f.uf) AS is_out
              FROM src s JOIN fence f ON s.g = f.g
              UNION ALL
              SELECT f.b, s.x, f.q[2], (s.x < f.lf OR s.x > f.uf)
              FROM src s JOIN fence f ON f.g IS NULL WHERE s.g IS NULL
            ),
            ends AS (
              SELECT b, min(x) FILTER (WHERE NOT is_out) AS lw, max(x) FILTER (WHERE NOT is_out) AS uw, count(*) FILTER (WHERE is_out) AS outliers
              FROM marked GROUP BY b
            ),
            shown AS (
              SELECT b, array_agg(x ORDER BY x) AS xs
              FROM (SELECT b, x, row_number() OVER (PARTITION BY b ORDER BY abs(x - med) DESC, x) AS r FROM marked WHERE is_out) far
              WHERE r <= @{MaxOutliersParameter}
              GROUP BY b
            )
            SELECT f.g::text, f.n, f.lo, f.q, f.hi, f.mean, e.lw, e.uw, e.outliers, s.xs
            FROM fence f JOIN ends e USING (b) LEFT JOIN shown s USING (b)
            ORDER BY {order}
            LIMIT @{IndicatorSql.LimitParameter}
            """;

        var all = new List<SqlParam>(parameters)
        {
            new(MaxOutliersParameter, NpgsqlDbType.Integer, WidgetConfig.MaxBoxOutliers),
            new(IndicatorSql.LimitParameter, NpgsqlDbType.Integer, maxRows + 1),
        };
        return new IndicatorSql(sql, all);
    }
}
