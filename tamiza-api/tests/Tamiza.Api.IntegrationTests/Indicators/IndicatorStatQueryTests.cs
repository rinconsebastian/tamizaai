using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>POST /projects/{id}/indicators/query</c> for a stat tile with a statistic, and for its value without the dashboard filters
/// (design D2, D3 and D10 of improve-stat-cards). The reference figures of the 50 values were computed with numpy's default
/// <c>quantile</c>; the tables are shaped like <c>teatro_precio_hogar</c> (one row per household) and <c>teatro_hoy</c>, with invented
/// names and values.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorStatQueryTests(PostgresFixture postgres)
{
    private const string Households = "comuna text, variable text, value numeric, numerator numeric, denominator numeric, hogares bigint";

    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);

    private async Task<IndicatorsHarness> WithAsync(string rows, string columns = Households, object? declared = null, int? maxRows = null, string format = "count")
    {
        var h = await IndicatorsHarness.CreateAsync(
            postgres, maxRows is null ? null : new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = maxRows.Value.ToString(CultureInfo.InvariantCulture) });
        await h.PublishAsync("precio_hogar", columns, rows, format: format, labels: declared);
        return h;
    }

    private static string Values(params double[] values) => string.Join(", ", values.Select(v => $"(NULL, NULL, {F(v)}, NULL, NULL, NULL)"));

    private static object Tile(string? statistic = "median", object? filters = null) =>
        new { indicator = "precio_hogar", measure = "value", filters, statistic };

    private static object RatioTile(string? statistic = "median", object? filters = null) =>
        new { indicator = "precio_hogar", ratio = new { numerator = "numerator", denominator = "denominator" }, filters, statistic };

    private static object Paid(string variable = "Pago") => new[] { new { column = "variable", op = "equals", value = variable } };

    private static object ByComuna(params string[] comunas) => new[] { new { column = "comuna", kind = "text", values = comunas } };

    private static double Value(JsonElement result) => result.GetProperty("rows")[0].GetProperty("value").GetDouble();

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    // The four statistics

    [Fact]
    public async Task Quartiles_of_four_values()
    {
        await using var h = await WithAsync(Values(1, 2, 3, 4), format: "decimal");

        var first = await h.QueryAsync(Tile("p25"), "stat");
        var median = await h.QueryAsync(Tile("median"), "stat");
        var third = await h.QueryAsync(Tile("p75"), "stat");
        var mean = await h.QueryAsync(Tile("mean"), "stat");

        Assert.Equal([1.75, 2.5, 3.25, 2.5], new[] { first, median, third, mean }.Select(Value));
        Assert.All(new[] { first, median, third, mean }, result =>
        {
            Assert.Single(result.GetProperty("rows").EnumerateArray());
            Assert.Equal(4, Number(result, "n"));
            Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        });
    }

    // numpy.quantile(values, q) of these 50 values, computed beforehand (numpy 2.5.3, linear interpolation).
    private static readonly double[] Fifty =
    [
        37.5, 75.0, 11.5, 49.0, 86.5, 23.0, 57.0, 94.5, 31.0, 68.5, 5.0, 42.5, 80.0, 13.0, 50.5, 88.0, 24.5, 62.0, 99.5, 36.0, 70.0, 6.5, 44.0, 81.5, 18.0,
        55.5, 93.0, 26.0, 63.5, 101.0, 37.5, 75.0, 11.5, 49.0, 83.0, 19.5, 57.0, 94.5, 31.0, 68.5, 5.0, 39.0, 76.5, 13.0, 50.5, 88.0, 24.5, 62.0, 96.0, 32.5,
    ];

    [Fact]
    public async Task Fifty_values_give_the_figures_numpy_gives()
    {
        await using var h = await WithAsync(Values(Fifty), format: "decimal");

        Assert.Equal(27.25, Value(await h.QueryAsync(Tile("p25"), "stat")), 9);
        Assert.Equal(50.5, Value(await h.QueryAsync(Tile("median"), "stat")), 9);
        Assert.Equal(76.125, Value(await h.QueryAsync(Tile("p75"), "stat")), 9);
        Assert.Equal(52.14, Value(await h.QueryAsync(Tile("mean"), "stat")), 9);
    }

    [Fact]
    public async Task Statistic_rows_not_summed()
    {
        await using var h = await WithAsync(Values(10000, 40000, 90000));

        var median = await h.QueryAsync(Tile(), "stat");
        var summed = await h.QueryAsync(Tile(statistic: null), "stat");

        Assert.Equal(40000, Value(median));
        Assert.Equal(140000, Value(summed));
        // A tile without a statistic is what it was: one sum and no n, since the indicator declares no base column.
        Assert.Null(Number(summed, "n"));
        Assert.Null(Number(summed.GetProperty("rows")[0], "n"));
    }

    // Rows with no value

    [Fact]
    public async Task Rows_with_no_value_left_out()
    {
        await using var h = await WithAsync(
            "(NULL, NULL, 10, NULL, NULL, NULL), (NULL, NULL, 20, NULL, NULL, NULL), (NULL, NULL, 30, NULL, NULL, NULL), (NULL, NULL, NULL, NULL, NULL, NULL), "
            + "(NULL, NULL, 'NaN', NULL, NULL, NULL), (NULL, NULL, 'Infinity', NULL, NULL, NULL), (NULL, NULL, '-Infinity', NULL, NULL, NULL)");

        var median = await h.QueryAsync(Tile(), "stat");
        var mean = await h.QueryAsync(Tile("mean"), "stat");

        Assert.Equal(20, Value(median));
        Assert.Equal(20, Value(mean));
        // The number of values is the three that have one.
        Assert.Equal(3, Number(median, "n"));
    }

    [Fact]
    public async Task Ratio_of_each_row_in_a_statistic()
    {
        // 0.75, 0.25 and 0.9: the ratio of sums would be 13 / 18. The rows with a zero denominator, a null and NaN have no value.
        await using var h = await WithAsync(
            "(NULL, NULL, NULL, 3, 4, NULL), (NULL, NULL, NULL, 1, 4, NULL), (NULL, NULL, NULL, 9, 10, NULL), (NULL, NULL, NULL, 1, 0, NULL), "
            + "(NULL, NULL, NULL, 3, 0, NULL), (NULL, NULL, NULL, NULL, 2, NULL), (NULL, NULL, NULL, 5, NULL, NULL), (NULL, NULL, NULL, 'NaN', 4, NULL)", format: "percent");

        var median = await h.QueryAsync(RatioTile(), "stat");

        Assert.Equal(0.75, Value(median), 9);
        Assert.Equal(3, Number(median, "n"));
    }

    [Fact]
    public async Task Statistic_of_a_ratio_returns_its_n_only()
    {
        await using var h = await WithAsync("(NULL, NULL, NULL, 3, 4, NULL), (NULL, NULL, NULL, 1, 4, NULL), (NULL, NULL, NULL, 9, 10, NULL)", format: "percent");

        var result = await h.QueryAsync(RatioTile(), "stat");
        var sums = await h.QueryAsync(RatioTile(statistic: null), "stat");
        var row = result.GetProperty("rows")[0];

        // The median of the three rows' own ratios, not the ratio of sums (13 / 18) a tile without a statistic shows.
        Assert.Equal(0.75, Value(result), 9);
        Assert.Equal(13d / 18, Value(sums), 9);
        Assert.Equal(3, Number(row, "n"));
        Assert.Equal(3, Number(result, "n"));
        Assert.Null(Number(row, "numerator"));
        Assert.Null(Number(row, "denominator"));
        Assert.False(row.TryGetProperty("numerator", out _));
        Assert.False(row.TryGetProperty("denominator", out _));
        Assert.False(result.TryGetProperty("numerator", out _));
        Assert.False(result.TryGetProperty("denominator", out _));
    }

    [Fact]
    public async Task No_row_with_a_value()
    {
        await using var h = await WithAsync("(NULL, NULL, NULL, NULL, NULL, NULL), (NULL, NULL, NULL, NULL, NULL, NULL)");

        var result = await h.QueryAsync(Tile(), "stat");

        Assert.True(result.GetProperty("published").GetBoolean());
        Assert.Empty(result.GetProperty("rows").EnumerateArray());
        Assert.False(result.TryGetProperty("n", out _));
    }

    [Fact]
    public async Task A_filter_that_leaves_no_row_gives_no_row()
    {
        await using var h = await WithAsync("('COMUNA 1', 'Pago', 100, NULL, NULL, NULL)");

        Assert.Empty((await h.QueryAsync(Tile(filters: Paid("Otro")), "stat")).GetProperty("rows").EnumerateArray());
    }

    // The filters

    private const string Prices =
        "('COMUNA 1', 'Pago', 30000, NULL, NULL, NULL), ('COMUNA 1', 'Pago', 40000, NULL, NULL, NULL), "
        + "('COMUNA 2', 'Pago', 40000, NULL, NULL, NULL), ('COMUNA 2', 'Pago', 90000, NULL, NULL, NULL), ('COMUNA 2', 'Pago', 20000, NULL, NULL, NULL), "
        + "('COMUNA 1', 'Otro', 1, NULL, NULL, NULL), ('COMUNA 2', 'Otro', 5, NULL, NULL, NULL)";

    [Fact]
    public async Task Median_that_follows_the_dashboard_filters()
    {
        await using var h = await WithAsync(Prices);

        var all = await h.QueryAsync(Tile(filters: Paid()), "stat");
        var comuna = await h.QueryAsync(Tile(filters: Paid()), "stat", dashboardFilters: ByComuna("COMUNA 1"));

        Assert.Equal(40000, Value(all));
        Assert.Equal(35000, Value(comuna));
        Assert.Equal(5, Number(all, "n"));
        Assert.Equal(2, Number(comuna, "n"));
    }

    [Fact]
    public async Task A_widget_filter_and_a_dashboard_filter_both_hold()
    {
        await using var h = await WithAsync(Prices);

        var result = await h.QueryAsync(Tile(filters: Paid("Otro")), "stat", dashboardFilters: ByComuna("COMUNA 2"));

        Assert.Equal(5, Value(result));
    }

    [Fact]
    public async Task Ignored_dashboard_filter()
    {
        await using var h = await WithAsync(Prices);

        // The page leaves an ignored filter out of the request, and a filter on a column the table lacks narrows nothing and is named.
        var result = await h.QueryAsync(
            Tile(filters: Paid()), "stat", dashboardFilters: new[] { new { column = "estrato", kind = "text", values = new[] { "Estrato 1" } } });

        Assert.Equal(40000, Value(result));
        Assert.Equal(["estrato"], result.GetProperty("notFiltered").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Large_table()
    {
        await using var h = await WithAsync("", maxRows: 5);
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".precio_hogar (value) SELECT g FROM generate_series(1, 200000) g");

        var result = await h.QueryAsync(Tile(), "stat");

        Assert.Equal(100000.5, Value(result), 6);
        Assert.Equal(200000, Number(result, "n"));
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(1, result.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task The_median_of_a_tile_is_the_median_of_a_box_plot_over_the_same_rows_and_filters()
    {
        await using var h = await WithAsync(Prices);

        foreach (var filters in new object?[] { null, ByComuna("COMUNA 1"), ByComuna("COMUNA 2") })
        {
            var tile = await h.QueryAsync(Tile(filters: Paid()), "stat", dashboardFilters: filters);
            var box = await h.QueryAsync(new { indicator = "precio_hogar", measure = "value", filters = Paid() }, "boxplot", dashboardFilters: filters);

            Assert.Equal(box.GetProperty("boxes")[0].GetProperty("median").GetDouble(), Value(tile), 9);
            Assert.Equal(box.GetProperty("boxes")[0].GetProperty("values").GetInt64(), Number(tile, "n"));
        }
    }

    // The n of a statistic (design D10)

    [Fact]
    public async Task Number_of_values_of_a_median()
    {
        await using var h = await WithAsync("");
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".precio_hogar (value) SELECT g * 1000 FROM generate_series(1, 37) g");

        var result = await h.QueryAsync(Tile(), "stat");

        Assert.Equal(19000, Value(result));
        Assert.Equal(37, Number(result, "n"));
        Assert.Equal(37, Number(result.GetProperty("rows")[0], "n"));
    }

    [Fact]
    public async Task Statistic_over_a_measure_without_a_base_and_statistic_tile_with_nothing_declared()
    {
        await using var h = await WithAsync(Values(10, 20, 30, 40, 50));

        var measure = await h.QueryAsync(Tile(), "stat");

        Assert.Equal(5, Number(measure.GetProperty("rows")[0], "n"));
        Assert.Equal(5, Number(measure, "n"));
    }

    [Fact]
    public async Task Answer_columns_declared_change_nothing_in_the_n_of_a_statistic()
    {
        // Two answers of one question per household would be counted twice by a base summed over rows: a statistic counts values.
        var declared = new { variable = new { categories = "multiple" }, comuna = new { categories = "single" } };
        await using var h = await WithAsync(Prices, declared: declared);

        var result = await h.QueryAsync(Tile(filters: Paid()), "stat");

        Assert.Equal(40000, Value(result));
        Assert.Equal(5, Number(result, "n"));
        Assert.Equal(5, Number(result.GetProperty("rows")[0], "n"));
    }

    [Fact]
    public async Task Base_column_of_a_statistic()
    {
        await using var h = await WithAsync(
            "(NULL, NULL, 10, NULL, NULL, 1), (NULL, NULL, 20, NULL, NULL, 2), (NULL, NULL, 30, NULL, NULL, 1)", declared: new { hogares = new { @base = true } });

        var result = await h.QueryAsync(Tile("mean"), "stat");

        Assert.Equal(20, Value(result));
        Assert.Equal(4, Number(result, "n"));
        Assert.Equal(4, Number(result.GetProperty("rows")[0], "n"));
    }

    [Fact]
    public async Task A_row_with_a_zero_denominator_is_counted_in_neither_the_median_nor_its_n()
    {
        const string rows = "(NULL, NULL, NULL, 1, 2, 5), (NULL, NULL, NULL, 3, 0, 7), (NULL, NULL, NULL, 3, 4, 2)";
        await using var plain = await WithAsync(rows);
        await using var based = await WithAsync(rows, declared: new { hogares = new { @base = true } });

        var counted = await plain.QueryAsync(RatioTile(), "stat");
        var summed = await based.QueryAsync(RatioTile(), "stat");

        Assert.Equal(0.625, Value(counted), 9);
        Assert.Equal(2, Number(counted, "n"));
        Assert.Equal(0.625, Value(summed), 9);
        Assert.Equal(7, Number(summed, "n"));
    }

    [Fact]
    public async Task Small_base_of_a_median()
    {
        await using var h = await WithAsync("");
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".precio_hogar (comuna, value) SELECT CASE WHEN g <= 31 THEN 'COMUNA 1' ELSE 'COMUNA 7' END, g FROM generate_series(1, 37) g");

        var all = await h.QueryAsync(Tile(), "stat");
        var small = await h.QueryAsync(Tile(), "stat", dashboardFilters: ByComuna("COMUNA 7"));

        // The page dims a figure whose n is under the threshold in force: the API gives it the 6 households of COMUNA 7.
        Assert.Equal(37, Number(all.GetProperty("rows")[0], "n"));
        Assert.Equal(6, Number(small.GetProperty("rows")[0], "n"));
        Assert.Equal(6, Number(small, "n"));
        Assert.Equal(34.5, Value(small), 9);
    }

    // The settings and what they are checked against

    [Fact]
    public async Task A_statistic_on_another_kind_is_refused_on_the_statistic()
    {
        await using var h = await WithAsync(Values(1, 2, 3));

        var column = await h.PostQueryAsync(new { indicator = "precio_hogar", measure = "value", groupBy = "comuna", statistic = "median" }, "column");
        var unknown = await h.PostQueryAsync(Tile("p90"), "stat");
        var noKind = await h.PostQueryAsync(Tile(), null);

        foreach (var response in new[] { column, unknown, noKind })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("statistic", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
        }
    }

    [Fact]
    public async Task A_tile_with_a_stray_group_column_is_never_grouped()
    {
        await using var h = await WithAsync(Prices);

        var tile = await h.QueryAsync(new { indicator = "precio_hogar", measure = "value", groupBy = "comuna", filters = Paid(), statistic = "median" }, "stat");

        Assert.Equal(40000, Value(tile));
        Assert.Equal(JsonValueKind.Null, tile.GetProperty("rows")[0].GetProperty("group").ValueKind);
    }

    // The value without the dashboard filters (design D3)

    private const string Coverage =
        "('COMUNA 1', 'Asistio', NULL, 4, 10, NULL), ('COMUNA 2', 'Asistio', NULL, 6, 10, NULL), ('COMUNA 1', 'Otro', NULL, 9, 10, NULL)";

    private static object Attended(string? statistic = null) => RatioTile(statistic, new[] { new { column = "variable", op = "equals", value = "Asistio" } });

    [Fact]
    public async Task Value_without_the_dashboard_filters()
    {
        await using var h = await WithAsync(Coverage, format: "percent");

        var result = await h.QueryAsync(Attended(), "stat", dashboardFilters: ByComuna("COMUNA 1"), unfiltered: true);

        // The widget's own filter still applies: the rows of "Otro" are in neither figure.
        Assert.Equal(0.4, Value(result), 9);
        Assert.Equal(0.5, Number(result, "unfiltered")!.Value, 9);
    }

    [Fact]
    public async Task No_dashboard_filter_in_force()
    {
        await using var h = await WithAsync(Coverage, format: "percent");

        var none = await h.QueryAsync(Attended(), "stat", unfiltered: true);
        var empty = await h.QueryAsync(Attended(), "stat", dashboardFilters: Array.Empty<object>(), unfiltered: true);

        Assert.Equal(0.5, Value(none), 9);
        Assert.False(none.TryGetProperty("unfiltered", out _));
        Assert.False(empty.TryGetProperty("unfiltered", out _));
    }

    [Fact]
    public async Task Filter_the_tile_does_not_take()
    {
        await using var h = await WithAsync("(NULL, 'Asistio', NULL, 4, 10, NULL), (NULL, 'Asistio', NULL, 6, 10, NULL)", format: "percent");

        var result = await h.QueryAsync(Attended(), "stat", dashboardFilters: new[] { new { column = "estrato", kind = "text", values = new[] { "Estrato 1" } } }, unfiltered: true);

        Assert.Equal(0.5, Value(result), 9);
        Assert.False(result.TryGetProperty("unfiltered", out _));
        Assert.Equal(["estrato"], result.GetProperty("notFiltered").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Median_without_the_dashboard_filters()
    {
        await using var h = await WithAsync(Prices);

        var result = await h.QueryAsync(Tile(filters: Paid()), "stat", dashboardFilters: ByComuna("COMUNA 1"), unfiltered: true);

        Assert.Equal(35000, Value(result));
        Assert.Equal(40000, Number(result, "unfiltered"));
        Assert.Equal(2, Number(result, "n"));
    }

    [Fact]
    public async Task A_query_without_the_flag_asks_for_no_unfiltered_value_and_a_bar_chart_with_it_gets_none()
    {
        await using var h = await WithAsync(Coverage, format: "percent");
        var bar = new { indicator = "precio_hogar", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "comuna" };

        var without = await h.QueryAsync(Attended(), "stat", dashboardFilters: ByComuna("COMUNA 1"));
        var plain = await h.QueryAsync(bar, "bar", dashboardFilters: ByComuna("COMUNA 1"));
        var flagged = await h.QueryAsync(bar, "bar", dashboardFilters: ByComuna("COMUNA 1"), unfiltered: true);
        var noKind = await h.QueryAsync(Attended(), null, dashboardFilters: ByComuna("COMUNA 1"), unfiltered: true);

        Assert.False(without.TryGetProperty("unfiltered", out _));
        Assert.False(flagged.TryGetProperty("unfiltered", out _));
        Assert.False(noKind.TryGetProperty("unfiltered", out _));
        Assert.Equal(plain.GetRawText(), flagged.GetRawText());
    }

    [Fact]
    public async Task The_response_of_a_tile_with_the_flag_is_the_response_without_it_plus_the_value()
    {
        await using var h = await WithAsync(Coverage, format: "percent");

        var without = await h.QueryAsync(Attended(), "stat", dashboardFilters: ByComuna("COMUNA 1"));
        var with = await h.QueryAsync(Attended(), "stat", dashboardFilters: ByComuna("COMUNA 1"), unfiltered: true);

        Assert.Equal(without.GetRawText()[..^1] + ",\"unfiltered\":0.5}", with.GetRawText());
    }
}
