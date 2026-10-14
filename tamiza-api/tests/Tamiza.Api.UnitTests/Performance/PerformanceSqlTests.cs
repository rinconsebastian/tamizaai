using System.Text.RegularExpressions;
using Tamiza.Api.Performance;

namespace Tamiza.Api.UnitTests.Performance;

public sealed class PerformanceSqlTests
{
    private static readonly string[] Always = ["@p", "@tz", "@k_start", "@k_end", "@k_enum"];

    [Fact]
    public void The_statement_reads_each_row_s_document_once_and_every_key_once()
    {
        var sql = PerformanceSql.Build(3, false, false, false);

        Assert.Single(Regex.Matches(sql, @"r\.data \|\| '\{\}'::jsonb"));
        Assert.Contains("OFFSET 0", sql);
        foreach (var key in new[] { "@k_start", "@k_end", "@k_enum", "@k0", "@k1", "@k2" })
        {
            Assert.Single(Regex.Matches(sql, Regex.Escape($"x.d ->> {key} ")));
        }

        Assert.DoesNotContain("@k3", sql);
        Assert.Equal(4, Regex.Matches(sql, @"x\.d ->>").Count - 2); // start, end, enumerator and the three dimensions: 6 in all
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void Only_parameters_and_positions_appear_in_the_text(int dimensions)
    {
        var sql = PerformanceSql.Build(dimensions, true, true, true);

        var parameters = Regex.Matches(sql, @"@\w+").Select(m => m.Value).ToHashSet();
        var expected = Always.Concat(["@from", "@to", "@sleep"]).Concat(Enumerable.Range(0, dimensions).Select(i => $"@k{i}"));
        if (dimensions > 0)
        {
            expected = expected.Append("@frame");
        }

        Assert.Equal(expected.Order(), parameters.Order());
    }

    [Fact]
    public void The_range_and_the_sleep_are_in_the_text_only_when_asked_for()
    {
        var none = PerformanceSql.Build(1, false, false, false);
        var all = PerformanceSql.Build(1, true, true, true);

        Assert.DoesNotContain("@from", none);
        Assert.DoesNotContain("@to", none);
        Assert.DoesNotContain("pg_sleep", none);
        Assert.Contains("r.submitted_at >= (@from::date)::timestamp AT TIME ZONE @tz", all);
        Assert.Contains("r.submitted_at < ((@to::date + 1)::timestamp AT TIME ZONE @tz)", all);
        Assert.Contains("pg_sleep(@sleep)", all);
    }

    [Fact]
    public void Durations_are_guarded_by_nested_case_expressions_in_a_safe_order()
    {
        var sql = PerformanceSql.Build(0, false, false, false);

        var order = new[]
        {
            "y.s ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}'", "y.e ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}'", "pg_input_is_valid(y.s, 'timestamptz')",
            "pg_input_is_valid(y.e, 'timestamptz')", "isfinite(y.s::timestamptz)", "isfinite(y.e::timestamptz)", "extract(epoch FROM y.e::timestamptz) >=",
        };
        var positions = order.Select(part => sql.IndexOf(part, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.Order(), positions);
        Assert.Equal(7, Regex.Matches(sql, "CASE WHEN").Count);
        Assert.DoesNotContain(" AND pg_input_is_valid", sql);
    }

    [Fact]
    public void Answers_are_trimmed_of_spaces_tabs_line_breaks_and_non_breaking_spaces_and_the_frame_the_same_way()
    {
        var sql = PerformanceSql.Build(1, false, false, false);

        Assert.Contains(@"btrim(y.en, E' \t\r\n ')", sql);
        Assert.Contains(@"btrim(y.r0, E' \t\r\n ')", sql);
        Assert.Contains(@"nullif(lower(btrim(j.v #>> '{values,0}', E' \t\r\n ')), '')", sql);
    }

    [Fact]
    public void Fences_come_from_percentile_cont_and_the_day_series_only_spans_the_data()
    {
        var sql = PerformanceSql.Build(1, false, false, false);

        Assert.Contains("percentile_cont(0.25)", sql);
        Assert.Contains("percentile_cont(0.75)", sql);
        Assert.Contains("1.5 * (q.q3 - q.q1)", sql);
        Assert.Contains("(SELECT min(day) AS a, max(day) AS z FROM daily)", sql);
    }
}
