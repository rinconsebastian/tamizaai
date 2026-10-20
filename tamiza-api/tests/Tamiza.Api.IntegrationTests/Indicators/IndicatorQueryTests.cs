using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary><c>POST /projects/{id}/indicators/query</c>: one widget's rows, summed (or combined as a ratio of sums) per group.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorQueryTests(PostgresFixture postgres)
{
    private const string Columns = IndicatorsHarness.StandardColumns + ", year bigint, geometry text";

    // municipality, value, numerator, denominator, year, geometry
    private const string Rows = """
        ('Cali',    40, 4,  10, 2025, 'POLYGON((0 0,1 0,1 1,0 0))'),
        ('Cali',    35, 3,  10, 2026, 'POLYGON((0 0,1 0,1 1,0 0))'),
        ('Palmira', 60, 6,  10, 2025, 'POINT(2 2)'),
        ('Yumbo',   NULL, NULL, NULL, 2025, NULL)
        """;

    private async Task<IndicatorsHarness> WithCoverageAsync(Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync("coverage", Columns, Rows);
        return h;
    }

    // Default measure / Duplicate rows summed

    [Fact]
    public async Task A_widget_without_a_measure_plots_value_and_sums_the_rows_of_a_group()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality" });

        Assert.True(result.GetProperty("published").GetBoolean());
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        // Cali's rows 40 and 35 are one point of 75; Yumbo has no value to plot.
        Assert.Equal(["Cali|75", "Palmira|60"], IndicatorsHarness.Rows(result));
        Assert.False(result.GetProperty("missing").GetProperty("indicator").GetBoolean());
        Assert.Empty(result.GetProperty("missing").GetProperty("columns").EnumerateArray());
    }

    [Fact]
    public async Task The_default_measure_is_the_first_numeric_column_when_the_table_has_no_value_column()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("attendance", "round text, students bigint, hours numeric", "('first', 20, 1.5), ('first', 5, 2), ('second', 7, 1)");

        var result = await h.QueryAsync(new { indicator = "attendance", groupBy = "round" });

        Assert.Equal(["first|25", "second|7"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task Any_numeric_column_can_be_the_measure()
    {
        await using var h = await WithCoverageAsync();

        var numerator = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality", measure = "numerator" });
        var denominator = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality", measure = "denominator" });

        Assert.Equal(["Cali|7", "Palmira|6"], IndicatorsHarness.Rows(numerator));
        Assert.Equal(["Cali|20", "Palmira|10"], IndicatorsHarness.Rows(denominator));
    }

    [Fact]
    public async Task Without_a_group_the_whole_table_is_one_row_with_a_null_group_as_a_stat_tile_uses()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new { indicator = "coverage" }, chartType: "stat");

        Assert.Equal(["|135"], IndicatorsHarness.Rows(result));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("rows")[0].GetProperty("group").ValueKind);
    }

    [Fact]
    public async Task A_histogram_with_no_group_gets_each_rows_value_instead_of_one_sum()
    {
        await using var h = await WithCoverageAsync();

        var perRow = await h.QueryAsync(new { indicator = "coverage" }, chartType: "histogram");
        var summed = await h.QueryAsync(new { indicator = "coverage" });

        Assert.Equal(["|35", "|40", "|60"], IndicatorsHarness.Rows(perRow).Order(StringComparer.Ordinal));
        Assert.Equal(3, perRow.GetProperty("rows").GetArrayLength());
        Assert.Equal(["|135"], IndicatorsHarness.Rows(summed));
    }

    // Rate over several rows / Zero denominator

    [Fact]
    public async Task A_ratio_is_the_sum_of_the_numerators_over_the_sum_of_the_denominators_of_a_group()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("piped_water_rate", IndicatorsHarness.StandardColumns,
            """
            ('Cali', 0.4, 40, 100), ('Cali', 0.2, 10, 50), ('Cali', 1.0, 50, 50),
            ('Palmira', 0.25, 5, 20)
            """, format: "percent");

        var result = await h.QueryAsync(new { indicator = "piped_water_rate", groupBy = "municipality", ratio = new { numerator = "numerator", denominator = "denominator" } });

        // Not the mean of the three Cali rates (0.5333...): 100 / 200.
        Assert.Equal(["Cali|0.5", "Palmira|0.25"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_group_whose_denominators_sum_to_zero_has_no_value()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("piped_water_rate", IndicatorsHarness.StandardColumns,
            "('Cali', 0, 5, 10), ('Yumbo', 0, 3, 0), ('Yumbo', 0, 4, 0), ('Palmira', 0, 0, 8)");

        var result = await h.QueryAsync(new { indicator = "piped_water_rate", groupBy = "municipality", ratio = new { numerator = "numerator", denominator = "denominator" } });

        Assert.Equal(["Cali|0.5", "Palmira|0"], IndicatorsHarness.Rows(result));
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
    }

    [Fact]
    public async Task A_ratio_without_a_group_is_one_ratio_of_sums_and_none_when_the_total_denominator_is_zero()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("rate", IndicatorsHarness.StandardColumns, "('a', 0, 1, 4), ('b', 0, 3, 4)");
        await h.PublishAsync("empty_rate", IndicatorsHarness.StandardColumns, "('a', 0, 1, 0)");

        var rate = await h.QueryAsync(new { indicator = "rate", ratio = new { numerator = "numerator", denominator = "denominator" } }, chartType: "stat");
        var empty = await h.QueryAsync(new { indicator = "empty_rate", ratio = new { numerator = "numerator", denominator = "denominator" } }, chartType: "stat");

        Assert.Equal(["|0.5"], IndicatorsHarness.Rows(rate));
        Assert.Empty(IndicatorsHarness.Rows(empty));
    }

    [Fact]
    public async Task A_ratio_may_use_any_two_numeric_columns()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality", ratio = new { numerator = "value", denominator = "denominator" } });

        Assert.Equal(["Cali|3.75", "Palmira|6"], IndicatorsHarness.Rows(result));
    }

    // Extra column grouped on

    [Fact]
    public async Task A_widget_can_group_by_a_column_a_script_added()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new { indicator = "coverage", groupBy = "year" });

        Assert.Equal(["2025|100", "2026|35"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task Filters_are_built_as_the_clean_data_dashboards_are()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new
        {
            indicator = "coverage",
            groupBy = "municipality",
            filters = new object[]
            {
                new { column = "year", op = "range", min = "2025", max = "2025" },
                new { column = "municipality", op = "in", values = new[] { "Cali", "Yumbo" } },
            },
        });

        Assert.Equal(["Cali|40"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_filter_changes_a_ratio_by_filtering_the_rows_before_the_sums()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new
        {
            indicator = "coverage",
            ratio = new { numerator = "numerator", denominator = "denominator" },
            filters = new[] { new { column = "year", op = "range", min = "2026", max = "2026" } },
        }, chartType: "stat");

        Assert.Equal(["|0.3"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_value_that_looks_like_sql_is_only_ever_a_value()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new
        {
            indicator = "coverage",
            groupBy = "municipality",
            filters = new[] { new { column = "municipality", op = "equals", value = "' OR 1=1 --" } },
        });

        Assert.Empty(IndicatorsHarness.Rows(result));
        Assert.True(await TestData.ScalarAsync<bool>(h.Db, $"SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = '{h.Schema}' AND tablename = 'coverage')"));
    }

    [Fact]
    public async Task The_geometry_column_comes_with_the_rows_when_asked_for_and_is_null_otherwise()
    {
        await using var h = await WithCoverageAsync();

        var withGeometry = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality", geometryColumn = "geometry" });
        var without = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality" });

        Assert.Equal(["POLYGON((0 0,1 0,1 1,0 0))", "POINT(2 2)"],
            withGeometry.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("geometry").GetString()));
        Assert.All(without.GetProperty("rows").EnumerateArray(), r => Assert.Equal(JsonValueKind.Null, r.GetProperty("geometry").ValueKind));
    }

    [Fact]
    public async Task A_group_with_no_value_in_the_group_column_has_a_null_group()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "(NULL, 5, 5, 10), ('Cali', 7, 7, 10)");

        var result = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality" });

        Assert.Equal(["Cali", null], result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()));
    }

    [Fact]
    public async Task A_timestamp_group_is_returned_as_an_iso_instant()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("visits", "visited_at timestamptz, value numeric", "('2026-03-01T10:00:00Z', 2), ('2026-03-01T10:00:00Z', 3)");

        var result = await h.QueryAsync(new { indicator = "visits", groupBy = "visited_at" });

        var row = Assert.Single(result.GetProperty("rows").EnumerateArray());
        Assert.Equal(DateTimeOffset.Parse("2026-03-01T10:00:00Z"), DateTimeOffset.Parse(row.GetProperty("group").GetString()!));
        Assert.Matches(@"^2026-03-01T10:00:00(Z|\+00:00)$", row.GetProperty("group").GetString());
        Assert.Equal(5, row.GetProperty("value").GetDouble());
    }

    // Row limit

    [Fact]
    public async Task Past_the_row_limit_the_response_says_so_instead_of_cutting_silently()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "3" });
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "");
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".coverage SELECT 'g' || g, g, g, 100 FROM generate_series(1, 10) g");

        var limited = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality" });

        Assert.True(limited.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(3, limited.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Exactly_the_row_limit_is_not_a_reached_limit()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "3" });
        await h.PublishAsync("c", IndicatorsHarness.StandardColumns, "('a', 1, 1, 1), ('b', 2, 2, 1), ('c', 3, 3, 1)");

        var result = await h.QueryAsync(new { indicator = "c", groupBy = "municipality" });

        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(3, result.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task The_row_limit_applies_to_a_histograms_rows_too()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "4" });
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "");
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".coverage SELECT 'g', g, g, 100 FROM generate_series(1, 10) g");

        var result = await h.QueryAsync(new { indicator = "coverage" }, chartType: "histogram");

        Assert.True(result.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(4, result.GetProperty("rows").GetArrayLength());
    }

    // Indicator not published

    [Fact]
    public async Task While_the_indicator_has_not_published_the_query_says_so_with_no_rows()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddIndicatorAsync("attendance");

        var result = await h.QueryAsync(new { indicator = "attendance", groupBy = "round" });

        Assert.False(result.GetProperty("published").GetBoolean());
        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        Assert.True(result.GetProperty("missing").GetProperty("indicator").GetBoolean());
        Assert.Equal(0, result.GetProperty("missing").GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task Another_indicator_publishing_does_not_publish_this_one()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new { indicator = "attendance", groupBy = "round" });

        Assert.False(result.GetProperty("published").GetBoolean());
        Assert.True(result.GetProperty("missing").GetProperty("indicator").GetBoolean());
    }

    // Column removed by a script change

    [Fact]
    public async Task A_group_column_a_script_change_removed_is_reported_as_missing()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new { indicator = "coverage", groupBy = "round" });

        Assert.True(result.GetProperty("published").GetBoolean());
        Assert.False(result.GetProperty("missing").GetProperty("indicator").GetBoolean());
        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(["round"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task A_republished_table_without_the_column_turns_a_working_query_into_a_missing_column()
    {
        await using var h = await WithCoverageAsync();
        var before = await h.QueryAsync(new { indicator = "coverage", groupBy = "year" });
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)");

        var after = await h.QueryAsync(new { indicator = "coverage", groupBy = "year" });

        Assert.Equal(2, before.GetProperty("rows").GetArrayLength());
        Assert.Equal(0, after.GetProperty("rows").GetArrayLength());
        Assert.Equal(["year"], IndicatorsHarness.Strings(after.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task A_measure_or_ratio_column_the_table_no_longer_has_is_reported_as_missing()
    {
        await using var h = await WithCoverageAsync();

        var measure = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality", measure = "total" });
        var ratio = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality", ratio = new { numerator = "numerator", denominator = "population" } });

        Assert.Equal(["total"], IndicatorsHarness.Strings(measure.GetProperty("missing").GetProperty("columns")));
        Assert.Equal(["population"], IndicatorsHarness.Strings(ratio.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task A_geometry_column_the_table_no_longer_has_is_reported_as_missing()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality", geometryColumn = "shape" });

        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(["shape"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task A_filter_on_an_unknown_column_is_reported_as_missing_not_as_an_error()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new
        {
            indicator = "coverage",
            groupBy = "municipality",
            filters = new[] { new { column = "region", op = "equals", value = "x" } },
        });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(["region"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task Every_missing_column_is_listed_at_once()
    {
        await using var h = await WithCoverageAsync();

        var result = await h.QueryAsync(new
        {
            indicator = "coverage",
            groupBy = "round",
            geometryColumn = "shape",
            filters = new[] { new { column = "region", op = "empty" } },
        });

        Assert.Equal(["round", "shape", "region"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    // Validation

    [Fact]
    public async Task A_query_with_no_indicator_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new { groupBy = "municipality" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(problem, "indicator"));
    }

    [Fact]
    public async Task A_body_without_config_or_an_empty_body_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();

        var empty = await h.Viewer.PostAsJsonAsync(h.Url + "/query", new { });
        var none = await h.Viewer.PostAsync(h.Url + "/query", null);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    }

    [Fact]
    public async Task An_indicator_that_is_not_a_valid_name_is_a_validation_error_and_reaches_no_sql()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new { indicator = "coverage\"; DROP SCHEMA public CASCADE; --", groupBy = "municipality" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await TestData.ScalarAsync<bool>(h.Db, "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'public')"));
    }

    [Fact]
    public async Task A_measure_together_with_a_ratio_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new
        {
            indicator = "coverage",
            groupBy = "municipality",
            measure = "value",
            ratio = new { numerator = "numerator", denominator = "denominator" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("measure", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task A_ratio_missing_one_of_its_columns_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new { indicator = "coverage", groupBy = "municipality", ratio = new { numerator = "numerator" } });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(problem, "ratio.denominator"));
    }

    [Fact]
    public async Task A_chart_that_groups_needs_a_group_column()
    {
        await using var h = await WithCoverageAsync();

        foreach (var chartType in new[] { "bar", "column", "line", "pie", "map" })
        {
            var response = await h.PostQueryAsync(new { indicator = "coverage", geometryColumn = "geometry" }, chartType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"A {chartType} widget needs a column to group by", IndicatorsHarness.Errors(problem, "groupBy")[0]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task The_number_of_histogram_buckets_is_between_1_and_100(int buckets)
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new { indicator = "coverage", buckets }, "histogram");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_filter_that_does_not_fit_its_column_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new
        {
            indicator = "coverage",
            groupBy = "municipality",
            filters = new[] { new { column = "municipality", op = "range", min = "1", max = "2" } },
        });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not support the filter 'range'", IndicatorsHarness.Errors(problem, "filters[0]")[0]);
    }

    [Fact]
    public async Task A_reversed_range_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new
        {
            indicator = "coverage",
            groupBy = "municipality",
            filters = new[] { new { column = "year", op = "range", min = "2026", max = "2025" } },
        });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("minimum is greater than the maximum", IndicatorsHarness.Errors(problem, "filters[0]")[0]);
    }

    [Fact]
    public async Task More_than_50_filters_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();
        var filters = Enumerable.Range(0, 51).Select(i => new { column = "municipality", op = "equals", value = "x" + i }).ToArray();

        var response = await h.PostQueryAsync(new { indicator = "coverage", groupBy = "municipality", filters });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("50", IndicatorsHarness.Errors(problem, "filters")[0]);
    }

    [Fact]
    public async Task More_than_100_values_in_a_filter_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new
        {
            indicator = "coverage",
            groupBy = "municipality",
            filters = new[] { new { column = "municipality", op = "in", values = Enumerable.Range(0, 101).Select(i => "v" + i).ToArray() } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_measure_that_is_not_a_number_in_the_table_is_a_validation_error_not_a_server_error()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("c", "municipality text, value text, count bigint", "('a', 'high', 1)");

        var defaultMeasure = await h.PostQueryAsync(new { indicator = "c", groupBy = "municipality", measure = "value" });
        var group = await h.QueryAsync(new { indicator = "c", groupBy = "municipality" }); // the default measure skips a text "value"

        Assert.Equal(HttpStatusCode.BadRequest, defaultMeasure.StatusCode);
        Assert.Equal(["a|1"], IndicatorsHarness.Rows(group));
    }

    [Fact]
    public async Task A_ratio_column_that_is_not_a_number_is_a_validation_error()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new { indicator = "coverage", groupBy = "municipality", ratio = new { numerator = "municipality", denominator = "denominator" } });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("is not a number", IndicatorsHarness.Errors(problem, "ratio.numerator")[0]);
    }

    // Access

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_404()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostQueryAsync(new { indicator = "coverage", groupBy = "municipality" }, client: h.Outsider);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Every_role_can_query()
    {
        await using var h = await WithCoverageAsync();

        foreach (var client in new[] { h.Viewer, h.Analyst, h.Admin })
        {
            var result = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality" }, client: client);
            Assert.Equal(2, result.GetProperty("rows").GetArrayLength());
        }
    }

    [Fact]
    public async Task Another_projects_table_is_never_read()
    {
        await using var h = await WithCoverageAsync();
        var (other, _) = await h.AddOtherProjectAsync();

        var response = await h.Admin.PostAsJsonAsync(
            IndicatorsHarness.UrlOf(other) + "/query", new { config = new { indicator = "coverage", groupBy = "municipality" } });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(result.GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task A_column_name_with_quotes_and_capitals_is_quoted_not_interpolated()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("c", "value numeric, \"Mixed \"\"Case\"\"\" text", "(1, 'x'), (2, 'x')");

        var result = await h.QueryAsync(new { indicator = "c", groupBy = "Mixed \"Case\"" });

        Assert.Equal(["x|3"], IndicatorsHarness.Rows(result));
    }

    // Dashboard filters (dashboard-filters: Filter kinds, Widgets a filter narrows, Widgets a filter does not narrow)

    private const string VisitColumns = "municipality text, sex text, round bigint, visited_at timestamptz, value numeric, sample_target bigint, block text, geometry text";

    // municipality, sex, round, visited_at, value, sample_target, block, geometry
    private const string VisitRows = """
        ('Cali',    'female', 1, '2026-09-01T15:00:00Z', 40, 10, '0042', 'POINT(1 1)'),
        ('Cali',    'male',   2, '2026-09-02T04:30:00Z', 35, 20, '0042', 'POINT(1 1)'),
        ('Palmira', 'female', 2, '2026-09-02T05:30:00Z', 60, 30, '0043', 'POINT(2 2)'),
        ('Buga',    'male',   3, '2026-09-03T15:00:00Z', 25, 40, '0044', 'POINT(3 3)')
        """;

    private async Task<IndicatorsHarness> WithVisitsAsync()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("visits", VisitColumns, VisitRows);
        return h;
    }

    private static object ByMunicipality(object? filters = null) => new { indicator = "visits", groupBy = "municipality", filters };

    private static object Text(string column, params string[] values) => new { column, kind = "text", values };

    private static object Range(string column, string kind, string? min = null, string? max = null) => new { column, kind, min, max };

    [Fact]
    public async Task A_text_dashboard_filter_keeps_the_rows_holding_one_of_its_values()
    {
        await using var h = await WithVisitsAsync();

        var result = await h.QueryAsync(ByMunicipality(), dashboardFilters: new[] { Text("municipality", "Cali", "Palmira") });

        Assert.Equal(["Cali|75", "Palmira|60"], IndicatorsHarness.Rows(result));
        Assert.Empty(result.GetProperty("notFiltered").EnumerateArray());
    }

    [Fact]
    public async Task A_date_dashboard_filter_keeps_whole_days_in_the_projects_time_zone()
    {
        await using var h = await WithVisitsAsync();
        await h.ExecuteAsync("UPDATE tamiza.projects SET time_zone = 'America/Bogota'");

        // 04:30 UTC on 2 September is 23:30 on 1 September in Bogotá; 05:30 UTC is already the 2nd there.
        var result = await h.QueryAsync(ByMunicipality(), dashboardFilters: new[] { Range("visited_at", "date", "2026-09-01", "2026-09-01") });

        Assert.Equal(["Cali|75"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_number_dashboard_filter_with_one_end_keeps_the_rows_from_it()
    {
        await using var h = await WithVisitsAsync();

        var result = await h.QueryAsync(ByMunicipality(), dashboardFilters: new[] { Range("round", "number", min: "2") });

        Assert.Equal(["Buga|25", "Cali|35", "Palmira|60"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_dashboard_filter_narrows_a_column_chart_and_a_stat_tile_alike()
    {
        await using var h = await WithVisitsAsync();
        var cali = new[] { Text("municipality", "Cali") };

        var column = await h.QueryAsync(ByMunicipality(), "column", dashboardFilters: cali);
        var stat = await h.QueryAsync(new { indicator = "visits" }, "stat", dashboardFilters: cali);

        Assert.Equal(["Cali|75"], IndicatorsHarness.Rows(column));
        Assert.Equal(["|75"], IndicatorsHarness.Rows(stat));
    }

    [Fact]
    public async Task A_number_dashboard_filter_narrows_integer_and_decimal_columns_of_two_indicators()
    {
        await using var h = await WithVisitsAsync();
        await h.PublishAsync("attendance", "municipality text, round double precision, value numeric", "('Cali', 1, 5), ('Cali', 2, 7), ('Buga', 2.5, 9)");
        var roundTwo = new[] { Range("round", "number", "2", "2") };

        var visits = await h.QueryAsync(ByMunicipality(), dashboardFilters: roundTwo);
        var attendance = await h.QueryAsync(new { indicator = "attendance", groupBy = "municipality" }, dashboardFilters: roundTwo);
        // A decimal bound on the integer column: 2.5 and more is 3 and more.
        var fromHalf = await h.QueryAsync(ByMunicipality(), dashboardFilters: new[] { Range("round", "number", min: "2.5") });

        Assert.Equal(["Cali|35", "Palmira|60"], IndicatorsHarness.Rows(visits));
        Assert.Equal(["Cali|7"], IndicatorsHarness.Rows(attendance));
        Assert.Equal(["Buga|25"], IndicatorsHarness.Rows(fromHalf));
    }

    [Fact]
    public async Task A_widget_filter_and_a_dashboard_filter_on_one_column_must_both_hold()
    {
        await using var h = await WithVisitsAsync();

        var result = await h.QueryAsync(
            ByMunicipality(new[] { new { column = "municipality", op = "in", values = new[] { "Cali", "Buga" } } }),
            dashboardFilters: new[] { Text("municipality", "Cali", "Palmira") });

        Assert.Equal(["Cali|75"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_dashboard_filter_narrows_a_maps_shapes_row_counts_and_tooltip_sums()
    {
        await using var h = await WithVisitsAsync();
        var map = new { indicator = "visits", groupBy = "block", geometryColumn = "geometry", tooltip = new[] { new { column = "sample_target" } } };

        var result = await h.QueryAsync(map, "map", dashboardFilters: new[] { Range("round", "number", "2", "2") });

        var rows = result.GetProperty("rows").EnumerateArray()
            .Select(r => $"{r.GetProperty("group").GetString()} {r.GetProperty("rows").GetInt64()} {r.GetProperty("tooltip")[0].GetDouble()}")
            .ToArray();
        Assert.Equal(["0042 1 20", "0043 1 30"], rows);
    }

    [Fact]
    public async Task A_dashboard_filter_narrows_the_rows_before_they_are_summed()
    {
        await using var h = await WithVisitsAsync();

        var result = await h.QueryAsync(ByMunicipality(), dashboardFilters: new[] { Text("sex", "female") });

        Assert.Equal(["Cali|40", "Palmira|60"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_widget_with_fifty_filters_is_narrowed_by_a_dashboard_filter_too()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var own = Enumerable.Range(0, 50).Select(i => $"c{i}").ToArray();
        await h.PublishAsync(
            "wide", "municipality text, value numeric, " + string.Join(", ", own.Select(c => c + " text")),
            string.Join(", ", new[] { "Cali", "Buga" }.Select(m => $"('{m}', 1, {string.Join(", ", own.Select(_ => "'x'"))})")));

        var result = await h.QueryAsync(
            new { indicator = "wide", groupBy = "municipality", filters = own.Select(c => new { column = c, op = "in", values = new[] { "x" } }).ToArray() },
            dashboardFilters: new[] { Text("municipality", "Cali") });

        Assert.Equal(["Cali|1"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_dashboard_filter_the_table_cannot_take_leaves_the_rows_unfiltered_and_is_named()
    {
        await using var h = await WithVisitsAsync();
        // `zone` is not in the table; `round` is a number there, not text. Neither makes the widget stale.
        var result = await h.QueryAsync(
            ByMunicipality(),
            dashboardFilters: new[] { Text("zone", "north"), Text("round", "2"), Text("sex", "male") });

        Assert.Equal(["Buga|25", "Cali|35"], IndicatorsHarness.Rows(result));
        Assert.Equal(["zone", "round"], IndicatorsHarness.Strings(result.GetProperty("notFiltered")));
        Assert.Empty(result.GetProperty("missing").GetProperty("columns").EnumerateArray());
    }

    [Fact]
    public async Task A_dashboard_filter_whose_column_a_script_change_removed_is_not_a_missing_column()
    {
        await using var h = await WithVisitsAsync();
        await h.ExecuteAsync($"ALTER TABLE \"{h.Schema}\".visits DROP COLUMN municipality");

        var result = await h.QueryAsync(new { indicator = "visits", groupBy = "sex" }, dashboardFilters: new[] { Text("municipality", "Cali") });

        Assert.Equal(["female|100", "male|60"], IndicatorsHarness.Rows(result));
        Assert.Equal(["municipality"], IndicatorsHarness.Strings(result.GetProperty("notFiltered")));
        Assert.Empty(result.GetProperty("missing").GetProperty("columns").EnumerateArray());
    }

    [Theory]
    [InlineData("number", "second", null, "round")]
    [InlineData("date", "2026-09-30", "2026-09-01", "visited_at")]
    public async Task A_dashboard_filter_value_of_the_wrong_kind_or_a_reversed_range_is_refused(string kind, string min, string? max, string column)
    {
        await using var h = await WithVisitsAsync();

        var response = await h.PostQueryAsync(ByMunicipality(), dashboardFilters: new[] { Range(column, kind, min, max) });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"'{column}'", IndicatorsHarness.Errors(problem, "dashboardFilters[0]").Single());
    }

    [Fact]
    public async Task A_query_without_dashboard_filters_answers_no_list_of_filters_that_did_not_apply()
    {
        await using var h = await WithVisitsAsync();

        var without = await h.QueryAsync(ByMunicipality());
        var empty = await h.QueryAsync(ByMunicipality(), dashboardFilters: Array.Empty<object>());

        Assert.False(without.TryGetProperty("notFiltered", out _));
        Assert.False(empty.TryGetProperty("notFiltered", out _));
    }
}
