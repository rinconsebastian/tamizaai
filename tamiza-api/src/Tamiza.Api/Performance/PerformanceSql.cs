using System.Text;

namespace Tamiza.Api.Performance;

/// <summary>
/// The text of the dashboard's statement (design D4). Only its shape depends on the request: how many dimensions the frame
/// has, whether a bound of the range is set and whether the test sleep is on. Keys, the time zone, dates and the frame always
/// travel as parameters (<c>@k_start</c>, <c>@k_end</c>, <c>@k_enum</c>, <c>@k0</c>..<c>@k9</c>, <c>@tz</c>, <c>@p</c>,
/// <c>@from</c>, <c>@to</c>, <c>@frame</c>, <c>@sleep</c>); the only numbers written into the text are positions.
/// </summary>
public static class PerformanceSql
{
    /// <summary>Spaces, tabs, line breaks and non-breaking spaces: what trimming removes from an answer (the frame's own trimming plus U+00A0).</summary>
    private const string Trim = @"E' \t\r\n '";

    private const string DateShape = "'^[0-9]{4}-[0-9]{2}-[0-9]{2}'";

    public static string Build(int dimensions, bool hasFrom, bool hasTo, bool sleep)
    {
        var sql = new StringBuilder();
        sql.Append("WITH base AS MATERIALIZED (\n");
        sql.Append("  SELECT r.submitted_at AS ts,\n");
        sql.Append("         (r.submitted_at AT TIME ZONE @tz)::date AS day,\n");
        sql.Append("         ").Append(Duration).Append(" AS dur,\n");
        sql.Append($"         nullif(btrim(y.en, {Trim}), '') AS en");
        for (var i = 0; i < dimensions; i++)
        {
            sql.Append($",\n         nullif(btrim(y.r{i}, {Trim}), '') AS t{i}");
        }

        sql.Append('\n');
        sql.Append("  FROM tamiza.raw_submissions r\n");
        // The document is read once per row: `||` builds an in-memory copy and OFFSET 0 keeps the planner from folding the
        // subquery back into every key expression, which would detoast the whole row again for each key.
        sql.Append("  CROSS JOIN LATERAL (SELECT r.data || '{}'::jsonb AS d OFFSET 0) x\n");
        sql.Append("  CROSS JOIN LATERAL (SELECT x.d ->> @k_start AS s, x.d ->> @k_end AS e, x.d ->> @k_enum AS en");
        for (var i = 0; i < dimensions; i++)
        {
            sql.Append($", x.d ->> @k{i} AS r{i}");
        }

        sql.Append(" OFFSET 0) y\n");
        sql.Append("  WHERE r.project_id = @p AND NOT r.deleted");
        if (hasFrom)
        {
            sql.Append("\n    AND r.submitted_at >= (@from::date)::timestamp AT TIME ZONE @tz");
        }

        if (hasTo)
        {
            sql.Append("\n    AND r.submitted_at < ((@to::date + 1)::timestamp AT TIME ZONE @tz)");
        }

        sql.Append("\n),\n");

        sql.Append(
            """
            fences AS (
              SELECT q.med, q.q1 - 1.5 * (q.q3 - q.q1) AS lo, q.q3 + 1.5 * (q.q3 - q.q1) AS hi
              FROM (SELECT percentile_cont(0.25) WITHIN GROUP (ORDER BY dur) AS q1,
                           percentile_cont(0.5) WITHIN GROUP (ORDER BY dur) AS med,
                           percentile_cont(0.75) WITHIN GROUP (ORDER BY dur) AS q3
                    FROM base WHERE dur IS NOT NULL) q
            ),
            comp AS (
              SELECT count(*) FILTER (WHERE b.dur IS NOT NULL) AS usable,
                     count(*) FILTER (WHERE b.dur IS NULL) AS unusable,
                     count(*) FILTER (WHERE b.dur < f.lo OR b.dur > f.hi) AS outliers,
                     avg(b.dur) FILTER (WHERE b.dur >= f.lo AND b.dur <= f.hi) AS mean
              FROM base b CROSS JOIN fences f
            ),
            enums AS (
              SELECT min(b.en) AS name, count(*) AS cnt,
                     avg(b.dur) FILTER (WHERE b.dur >= f.lo AND b.dur <= f.hi) AS mean,
                     count(*) FILTER (WHERE b.dur < f.lo OR b.dur > f.hi) AS outliers,
                     max(b.ts) AS last
              FROM base b CROSS JOIN fences f
              GROUP BY lower(b.en)
            ),
            daily AS (
              SELECT b.day, count(*) AS cnt FROM base b WHERE b.day IS NOT NULL GROUP BY b.day
            ),
            series AS (
              SELECT g::date AS day
              FROM (SELECT min(day) AS a, max(day) AS z FROM daily) m
              CROSS JOIN LATERAL generate_series(m.a::timestamp, m.z::timestamp, interval '1 day') g
              WHERE m.a IS NOT NULL
            )
            """);

        if (dimensions > 0)
        {
            sql.Append(",\nframe AS (\n  SELECT j.ord");
            for (var i = 0; i < dimensions; i++)
            {
                sql.Append($",\n         nullif(lower(btrim(j.v #>> '{{values,{i}}}', {Trim})), '') AS n{i}");
            }

            sql.Append("\n  FROM jsonb_array_elements(@frame) WITH ORDINALITY AS j(v, ord)\n),\n");

            sql.Append("combos AS (\n  SELECT ");
            sql.Append(string.Join(", ", Enumerable.Range(0, dimensions).Select(i => $"lower(b.t{i}) AS n{i}, min(b.t{i}) AS o{i}")));
            sql.Append(", count(*) AS cnt\n  FROM base b\n  GROUP BY ");
            sql.Append(string.Join(", ", Enumerable.Range(0, dimensions).Select(i => $"lower(b.t{i})")));
            sql.Append("\n),\n");

            var join = string.Join(" AND ", Enumerable.Range(0, dimensions).Select(i => $"cb.n{i} = fr.n{i}"));
            sql.Append("matched AS (\n  SELECT fr.ord, coalesce(cb.cnt, 0) AS achieved\n  FROM frame fr LEFT JOIN combos cb ON ").Append(join).Append("\n),\n");
            sql.Append("outside AS (\n  SELECT cb.* FROM combos cb\n  WHERE NOT EXISTS (SELECT 1 FROM frame fr WHERE ").Append(join).Append(")\n)");
        }

        sql.Append("\nSELECT now() AS computed_at,\n");
        sql.Append("       (SELECT count(*) FROM base) AS total,\n");
        sql.Append("       (SELECT count(*) FROM base WHERE ts IS NULL) AS no_time,\n");
        sql.Append("       (SELECT coalesce(jsonb_agg(jsonb_build_object('day', s.day, 'n', coalesce(d.cnt, 0)) ORDER BY s.day), '[]'::jsonb)\n");
        sql.Append("        FROM series s LEFT JOIN daily d ON d.day = s.day) AS days,\n");
        sql.Append("       comp.usable, comp.unusable, comp.outliers, f.med::float8 AS median, comp.mean::float8 AS mean, f.lo::float8 AS lo, f.hi::float8 AS hi,\n");
        sql.Append("       (SELECT coalesce(jsonb_agg(jsonb_build_object('name', e.name, 'n', e.cnt, 'mean', e.mean, 'outliers', e.outliers, 'last', e.last)\n");
        sql.Append("                                  ORDER BY e.cnt DESC, e.name), '[]'::jsonb) FROM enums e) AS enumerators,\n");
        if (dimensions > 0)
        {
            sql.Append("       (SELECT coalesce(jsonb_agg(m.achieved ORDER BY m.ord), '[]'::jsonb) FROM matched m) AS achieved,\n");
            sql.Append("       (SELECT coalesce(sum(o.cnt), 0)::bigint FROM outside o) AS outside_total,\n");
            var values = string.Join(", ", Enumerable.Range(0, dimensions).Select(i => $"t.o{i}"));
            var order = string.Join(", ", Enumerable.Range(0, dimensions).Select(i => $"t.o{i}"));
            sql.Append($"       (SELECT coalesce(jsonb_agg(jsonb_build_object('values', jsonb_build_array({values}), 'n', t.cnt) ORDER BY t.cnt DESC, {order}), '[]'::jsonb)\n");
            sql.Append("        FROM (SELECT * FROM outside ORDER BY cnt DESC");
            sql.Append(string.Concat(Enumerable.Range(0, dimensions).Select(i => $", o{i}")));
            sql.Append(" LIMIT 10) t) AS outside_top");
        }
        else
        {
            sql.Append("       '[]'::jsonb AS achieved, 0::bigint AS outside_total, '[]'::jsonb AS outside_top");
        }

        if (sleep)
        {
            sql.Append(",\n       (SELECT 1 FROM (SELECT pg_sleep(@sleep)) z) AS slept");
        }

        sql.Append("\nFROM comp CROSS JOIN fences f");
        return sql.ToString();
    }

    /// <summary>
    /// The duration in seconds, or null when it cannot be used. SQL does not promise the order in which the parts of an AND run,
    /// and the casts raise errors on what the earlier checks rule out, so every guard is a nested CASE: the value looks like a date
    /// (which also rules out <c>infinity</c>, <c>now</c> and years of more than four digits), reads as a timestamp, is finite, and
    /// <c>end</c> is not before <c>start</c>. The difference is of epoch values, which cannot overflow like interval arithmetic.
    /// </summary>
    private static readonly string Duration =
        $"""
        CASE WHEN y.s ~ {DateShape} THEN
          CASE WHEN y.e ~ {DateShape} THEN
            CASE WHEN pg_input_is_valid(y.s, 'timestamptz') THEN
              CASE WHEN pg_input_is_valid(y.e, 'timestamptz') THEN
                CASE WHEN isfinite(y.s::timestamptz) THEN
                  CASE WHEN isfinite(y.e::timestamptz) THEN
                    CASE WHEN extract(epoch FROM y.e::timestamptz) >= extract(epoch FROM y.s::timestamptz)
                         THEN (extract(epoch FROM y.e::timestamptz) - extract(epoch FROM y.s::timestamptz))::float8 END
                  END
                END
              END
            END
          END
        END
        """;
}
