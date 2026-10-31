using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary><c>POST /projects/{id}/indicators/query</c> for a widget with a series: cells, totals and the series domain (design D3, D12).</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorSeriesQueryTests(PostgresFixture postgres)
{
    private const string Columns = "municipality text, water_source text, value numeric, numerator numeric, denominator numeric, round text";

    // municipality, water_source, value, numerator, denominator, round
    private const string Rows = """
        ('Cali',    'Piped', 10, 5, 10, '1'),
        ('Cali',    'Piped', 20, 5, 10, '2'),
        ('Cali',    'Piped',  5, 5, 10, '2'),
        ('Cali',    'Well',  15, 5, 10, '1'),
        ('Cali',    'Well',   0, 5, 10, '2'),
        ('Palmira', 'Piped', 20, 6, 10, '1'),
        ('Buga',    'Well',   7, 1, 10, '1'),
        ('Buga',    NULL,     3, 1, 10, '1')
        """;

    private async Task<IndicatorsHarness> WithSourcesAsync(string rows = Rows, string columns = Columns)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("households_by_source", columns, rows);
        return h;
    }

    private static object Config() => new { indicator = "households_by_source", groupBy = "municipality", seriesBy = "water_source" };

    // The series of a cell with no series value (none, other) is left out of the JSON, as a plain row's other members are.
    private static string Cell(JsonElement row) =>
        $"{row.GetProperty("group").GetString() ?? "-"}|{row.GetProperty("seriesKind").GetString()}:{(row.TryGetProperty("series", out var series) ? series.GetString() : "-")}|{row.GetProperty("value").GetDouble():0.###}|{row.GetProperty("rows").GetInt64()}";

    private static string[] Cells(JsonElement result) => result.GetProperty("rows").EnumerateArray().Select(Cell).ToArray();

    private static string[] Series(JsonElement result) =>
        result.GetProperty("series").EnumerateArray().Select(s => $"{s.GetProperty("kind").GetString()}:{(s.GetProperty("value").ValueKind == JsonValueKind.Null ? "-" : s.GetProperty("value").GetString())}").ToArray();

    private static string[] Groups(JsonElement result) =>
        result.GetProperty("totals").GetProperty("groups").EnumerateArray()
            .Select(g => $"{g.GetProperty("group").GetString() ?? "-"}|{g.GetProperty("value").GetDouble():0.###}|{g.GetProperty("rows").GetInt64()}").ToArray();

    [Fact]
    public async Task Rows_that_share_a_group_and_a_series_are_summed_into_one_cell()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(Config(), "column");

        // Cali's three Piped rows are one cell of 35; groups follow the group column's own order, and a group's cells the order of the series.
        Assert.Equal(
            ["Buga|value:Well|7|1", "Buga|none:-|3|1", "Cali|value:Piped|35|3", "Cali|value:Well|15|2", "Palmira|value:Piped|20|1"],
            Cells(result));
        Assert.Equal(["value:Piped", "value:Well", "none:-"], Series(result));
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        Assert.False(result.TryGetProperty("leftOut", out _));
    }

    [Fact]
    public async Task Totals_come_from_the_rows_for_each_group_each_series_and_all_of_them()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(Config(), "column");
        var totals = result.GetProperty("totals");

        Assert.Equal(["Buga|10|2", "Cali|50|5", "Palmira|20|1"], Groups(result));
        // One entry per series, in the order of the series list.
        Assert.Equal(
            ["Piped|55|4", "Well|22|3", "-|3|1"],
            totals.GetProperty("series").EnumerateArray()
                .Select(s => $"{(s.GetProperty("series").ValueKind == JsonValueKind.Null ? "-" : s.GetProperty("series").GetString())}|{s.GetProperty("value").GetDouble():0.###}|{s.GetProperty("rows").GetInt64()}")
                .ToArray());
        Assert.Equal(80, totals.GetProperty("all").GetProperty("value").GetDouble());
        Assert.Equal(8, totals.GetProperty("all").GetProperty("rows").GetInt64());
    }

    [Fact]
    public async Task A_ratio_total_is_the_ratio_of_the_sums_and_not_a_sum_or_a_mean_of_the_cells()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(
            new { indicator = "households_by_source", groupBy = "municipality", seriesBy = "water_source", ratio = new { numerator = "numerator", denominator = "denominator" } },
            "crosstab");

        // Cali: Piped 15 of 30 and Well 10 of 20 are both 0.5, and the group's total is 25 of 50 over all five of its rows.
        Assert.Contains("Cali|value:Piped|0.5|3", Cells(result));
        Assert.Contains("Cali|value:Well|0.5|2", Cells(result));
        Assert.Equal(["Buga|0.1|2", "Cali|0.5|5", "Palmira|0.6|1"], Groups(result));
        // All rows: 33 of 80, which is neither the sum nor the mean of the cells' ratios.
        Assert.Equal(33.0 / 80, result.GetProperty("totals").GetProperty("all").GetProperty("value").GetDouble(), 6);
    }

    [Fact]
    public async Task A_ratio_cell_whose_denominator_sums_to_zero_has_no_value_and_its_total_has_none()
    {
        await using var h = await WithSourcesAsync("('A', 'x', 1, 5, 0, '1'), ('A', 'y', 1, 4, 8, '1')");

        var result = await h.QueryAsync(
            new { indicator = "households_by_source", groupBy = "municipality", seriesBy = "water_source", ratio = new { numerator = "numerator", denominator = "denominator" } },
            "heatmap");

        Assert.Equal(["A|value:y|0.5|1"], Cells(result));
        Assert.Equal(["value:x", "value:y"], Series(result));
        var totals = result.GetProperty("totals").GetProperty("series").EnumerateArray().ToArray();
        Assert.Equal((JsonValueKind.Null, 1), (totals[0].GetProperty("value").ValueKind, (int)totals[0].GetProperty("rows").GetInt64()));
        Assert.Equal(0.5, totals[1].GetProperty("value").GetDouble());
        // The group's own denominators sum to 8, so it has a total: 9 of 8.
        Assert.Equal(["A|1.125|2"], Groups(result));
    }

    [Fact]
    public async Task Series_are_listed_in_natural_order_then_none()
    {
        await using var h = await WithSourcesAsync("('A', '10', 1, 0, 0, '1'), ('A', '2', 1, 0, 0, '1'), ('A', '1', 1, 0, 0, '1'), ('A', NULL, 1, 0, 0, '1')");

        var result = await h.QueryAsync(Config(), "column");

        Assert.Equal(["value:1", "value:2", "value:10", "none:-"], Series(result));
    }

    [Fact]
    public async Task Ten_series_show_the_seven_largest_and_fold_the_others_into_other()
    {
        var rows = string.Join(", ", Enumerable.Range(1, 10).Select(i => $"('A', 's{i:00}', {i * 10}, 0, 0, '1')"));
        await using var h = await WithSourcesAsync(rows);

        var result = await h.QueryAsync(Config(), "column");

        // s04 to s10 are the seven largest; s01 to s03 (10, 20, 30) fold: 60 over 3 rows.
        Assert.Equal(["value:s04", "value:s05", "value:s06", "value:s07", "value:s08", "value:s09", "value:s10", "other:-"], Series(result));
        Assert.Contains("A|other:-|60|3", Cells(result));
        Assert.Equal(550, result.GetProperty("totals").GetProperty("all").GetProperty("value").GetDouble());
        var otherTotal = result.GetProperty("totals").GetProperty("series").EnumerateArray().Last();
        Assert.Equal((60, 3), (otherTotal.GetProperty("value").GetDouble(), otherTotal.GetProperty("rows").GetInt64()));
    }

    [Fact]
    public async Task A_missing_series_value_is_never_folded_but_takes_a_place()
    {
        var rows = string.Join(", ", Enumerable.Range(1, 9).Select(i => $"('A', 's{i}', {i * 10}, 0, 0, '1')")) + ", ('A', NULL, 1, 0, 0, '1')";
        await using var h = await WithSourcesAsync(rows);

        var result = await h.QueryAsync(Config(), "column");

        // 8 entries: six values (s4 to s9), none and other (s1 to s3).
        Assert.Equal(["value:s4", "value:s5", "value:s6", "value:s7", "value:s8", "value:s9", "none:-", "other:-"], Series(result));
    }

    [Fact]
    public async Task A_heat_map_keeps_up_to_thirty_series()
    {
        var rows = string.Join(", ", Enumerable.Range(1, 30).Select(i => $"('A', 's{i:00}', {i}, 0, 0, '1')"));
        await using var h = await WithSourcesAsync(rows);

        var heat = await h.QueryAsync(Config(), "heatmap");
        var stacked = await h.QueryAsync(Config(), "column");

        Assert.Equal(30, Series(heat).Length);
        Assert.DoesNotContain("other:-", Series(heat));
        Assert.Equal(8, Series(stacked).Length);
        Assert.Equal("other:-", Series(stacked)[^1]);
    }

    [Fact]
    public async Task The_series_ignore_the_dashboard_filters_so_their_colors_and_legend_stay()
    {
        await using var h = await WithSourcesAsync();
        var unfiltered = await h.QueryAsync(Config(), "column");

        var filtered = await h.QueryAsync(
            Config(), "column", dashboardFilters: new[] { new { column = "round", kind = "text", values = new[] { "1" } } });

        // The filter on round narrows the cells, never the series.
        Assert.Equal(Series(unfiltered), Series(filtered));
        Assert.Equal(["Buga|value:Well|7|1", "Buga|none:-|3|1", "Cali|value:Piped|10|1", "Cali|value:Well|15|1", "Palmira|value:Piped|20|1"], Cells(filtered));
    }

    [Fact]
    public async Task A_series_with_no_rows_after_a_dashboard_filter_stays_in_the_list_with_no_total()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(
            Config(), "column", dashboardFilters: new[] { new { column = "municipality", kind = "text", values = new[] { "Palmira" } } });

        Assert.Equal(["value:Piped", "value:Well", "none:-"], Series(result));
        var series = result.GetProperty("totals").GetProperty("series").EnumerateArray().ToArray();
        Assert.Equal(20, series[0].GetProperty("value").GetDouble());
        Assert.Equal(JsonValueKind.Null, series[1].GetProperty("value").ValueKind);
        Assert.Equal(0, series[1].GetProperty("rows").GetInt64());
    }

    [Fact]
    public async Task The_widgets_own_filters_do_narrow_the_series()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(
            new { indicator = "households_by_source", groupBy = "municipality", seriesBy = "water_source", filters = new[] { new { column = "water_source", op = "equals", value = "Well" } } },
            "column");

        Assert.Equal(["value:Well"], Series(result));
    }

    [Fact]
    public async Task A_stacked_bar_chart_can_be_ordered_by_the_totals_of_its_bars()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(
            new { indicator = "households_by_source", groupBy = "municipality", seriesBy = "water_source", layout = "stacked", sort = new { by = "value", direction = "desc" } },
            "bar");

        Assert.Equal(["Cali|50|5", "Palmira|20|1", "Buga|10|2"], Groups(result));
        // Inside a bar the series keep their order, whatever the order of the bars.
        Assert.Equal(["Cali|value:Piped|35|3", "Cali|value:Well|15|2"], Cells(result).Take(2));
    }

    [Fact]
    public async Task A_bar_with_a_series_can_be_ordered_by_a_column_of_the_table()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(
            new { indicator = "households_by_source", groupBy = "municipality", seriesBy = "water_source", sort = new { by = "column", column = "round", compareAs = "number", direction = "desc" } },
            "column");

        // Every municipality has a round 1 row, so the smallest round ties and the group column breaks the tie.
        Assert.Equal(["Buga", "Cali", "Palmira"], Groups(result).Select(g => g.Split('|')[0]).ToArray());
    }

    [Fact]
    public async Task A_line_chart_lists_its_groups_in_natural_order()
    {
        await using var h = await WithSourcesAsync("('r10', 'a', 1, 0, 0, '1'), ('r2', 'a', 1, 0, 0, '1'), ('r1', 'a', 1, 0, 0, '1'), (NULL, 'a', 1, 0, 0, '1')");

        var line = await h.QueryAsync(Config(), "line");
        var column = await h.QueryAsync(Config(), "column");

        Assert.Equal(["r1", "r2", "r10", "-"], Groups(line).Select(g => g.Split('|')[0]).ToArray());
        Assert.Equal(["r1", "r10", "r2", "-"], Groups(column).Select(g => g.Split('|')[0]).ToArray());
    }

    [Fact]
    public async Task The_row_limit_counts_cells_and_a_group_that_crosses_it_is_left_out_whole()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new Dictionary<string, string?> { ["IndicatorDashboard:MaxWidgetQueryRows"] = "5" });
        await h.PublishAsync("households_by_source", Columns, Rows);

        var result = await h.QueryAsync(Config(), "column");

        // Buga has 2 cells, Cali 2 and Palmira 1: five cells fit the limit of five.
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());

        await using var small = await IndicatorsHarness.CreateAsync(postgres, new Dictionary<string, string?> { ["IndicatorDashboard:MaxWidgetQueryRows"] = "4" });
        await small.PublishAsync("households_by_source", Columns, Rows);
        var limited = await small.QueryAsync(Config(), "column");

        // 4 cells fit Buga and Cali; Palmira's single cell crosses the limit, so it is left out.
        Assert.True(limited.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(["Buga|10|2", "Cali|50|5"], Groups(limited));
        Assert.Equal(4, limited.GetProperty("rows").GetArrayLength());
        // The totals of the series and of all rows still cover every row the widget matches.
        Assert.Equal(80, limited.GetProperty("totals").GetProperty("all").GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task A_widget_without_a_series_gets_the_response_it_always_did()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(new { indicator = "households_by_source", groupBy = "municipality" }, "column");

        Assert.False(result.TryGetProperty("series", out _));
        Assert.False(result.TryGetProperty("totals", out _));
        Assert.False(result.TryGetProperty("leftOut", out _));
        Assert.False(result.GetProperty("rows")[0].TryGetProperty("seriesKind", out _));
        Assert.Equal(["Buga|10", "Cali|50", "Palmira|20"], IndicatorsHarness.Rows(result));
    }

    // Pyramid and Likert

    private const string PopulationColumns = "age_band text, sex text, value numeric";

    [Fact]
    public async Task A_pyramid_draws_its_two_sides_and_counts_the_rows_it_leaves_out()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("population", PopulationColumns,
            """
            ('0-4', 'Female', 100), ('0-4', 'Male', 90), ('5-9', 'Female', 80), ('10-14', 'Female', 60), ('10-14', 'Male', 70),
            ('5-9', 'Other', 12), (NULL, 'Male', 3), ('5-9', NULL, 1)
            """);

        var result = await h.QueryAsync(
            new { indicator = "population", groupBy = "age_band", seriesBy = "sex", pyramid = new { left = "Female", right = "Male" } }, "pyramid");

        Assert.Equal(["value:Female", "value:Male"], Series(result));
        // The bands are in natural order. Three rows are left out: the third sex, the missing band and the missing sex.
        Assert.Equal(["0-4|190|2", "5-9|80|1", "10-14|130|2"], Groups(result));
        Assert.Equal(3, result.GetProperty("leftOut").GetInt64());
        Assert.Equal(
            ["0-4|value:Female|100|1", "0-4|value:Male|90|1", "5-9|value:Female|80|1", "10-14|value:Female|60|1", "10-14|value:Male|70|1"],
            Cells(result));
        var all = result.GetProperty("totals").GetProperty("all");
        Assert.Equal((400, 5), (all.GetProperty("value").GetDouble(), all.GetProperty("rows").GetInt64()));
    }

    [Fact]
    public async Task A_pyramid_side_with_no_rows_stays_in_the_series()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("population", PopulationColumns, "('0-4', 'Female', 100), ('5-9', 'Female', 80)");

        var result = await h.QueryAsync(
            new { indicator = "population", groupBy = "age_band", seriesBy = "sex", pyramid = new { left = "Female", right = "Male" } }, "pyramid");

        Assert.Equal(["value:Female", "value:Male"], Series(result));
        var male = result.GetProperty("totals").GetProperty("series")[1];
        Assert.Equal(JsonValueKind.Null, male.GetProperty("value").ValueKind);
        Assert.Equal(0, male.GetProperty("rows").GetInt64());
        Assert.Equal(0, result.GetProperty("leftOut").GetInt64());
    }

    [Fact]
    public async Task A_dashboard_filter_that_hides_a_side_leaves_it_in_the_series_and_counts_nothing_extra_as_left_out()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("population", PopulationColumns, "('0-4', 'Female', 100), ('0-4', 'Male', 90)");

        var result = await h.QueryAsync(
            new { indicator = "population", groupBy = "age_band", seriesBy = "sex", pyramid = new { left = "Female", right = "Male" } }, "pyramid",
            dashboardFilters: new[] { new { column = "sex", kind = "text", values = new[] { "Female" } } });

        Assert.Equal(["0-4|value:Female|100|1"], Cells(result));
        Assert.Equal(["value:Female", "value:Male"], Series(result));
        Assert.Equal(0, result.GetProperty("leftOut").GetInt64());
    }

    [Fact]
    public async Task A_likert_widget_keeps_its_scale_in_order_and_counts_values_outside_it()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("agreement", "municipality text, answer text, value numeric",
            """
            ('Cali', 'Strongly disagree', 10), ('Cali', 'Disagree', 20), ('Cali', 'Neither', 20), ('Cali', 'Agree', 30), ('Cali', 'Strongly agree', 20),
            ('Buga', 'Agree', 5), ('Buga', 'Strongly agree', 5), ('Buga', 'Don''t know', 7)
            """);

        var result = await h.QueryAsync(
            new
            {
                indicator = "agreement", groupBy = "municipality", seriesBy = "answer",
                scale = new[]
                {
                    new { value = "Strongly disagree", side = "negative" }, new { value = "Disagree", side = "negative" }, new { value = "Neither", side = "neutral" },
                    new { value = "Agree", side = "positive" }, new { value = "Strongly agree", side = "positive" },
                },
            },
            "likert");

        Assert.Equal(["value:Strongly disagree", "value:Disagree", "value:Neither", "value:Agree", "value:Strongly agree"], Series(result));
        Assert.Equal(["Buga|10|2", "Cali|100|5"], Groups(result));
        Assert.Equal(1, result.GetProperty("leftOut").GetInt64());
        Assert.Equal(110, result.GetProperty("totals").GetProperty("all").GetProperty("value").GetDouble());
    }

    // The request

    [Fact]
    public async Task A_series_column_the_table_lacks_makes_the_widget_stale_and_runs_nothing()
    {
        await using var h = await WithSourcesAsync();

        var result = await h.QueryAsync(new { indicator = "households_by_source", groupBy = "municipality", seriesBy = "gone" }, "column");

        Assert.True(result.GetProperty("published").GetBoolean());
        Assert.Equal(["gone"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
        Assert.Empty(result.GetProperty("rows").EnumerateArray());
        Assert.False(result.TryGetProperty("series", out _));
    }

    [Fact]
    public async Task A_series_without_a_group_column_is_refused()
    {
        await using var h = await WithSourcesAsync();

        var response = await h.PostQueryAsync(new { indicator = "households_by_source", seriesBy = "water_source" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("seriesBy", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task An_unknown_chart_type_is_refused_instead_of_dropped()
    {
        await using var h = await WithSourcesAsync();

        var response = await h.PostQueryAsync(new { indicator = "households_by_source", groupBy = "municipality" }, "radar");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("chartType", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task A_series_on_a_chart_that_cannot_have_one_is_refused()
    {
        await using var h = await WithSourcesAsync();

        var response = await h.PostQueryAsync(Config(), "pie");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("seriesBy", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }
}
