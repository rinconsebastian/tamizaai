using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary><c>POST /projects/{id}/indicators/query</c>: one widget's rows, summed per indicator and group.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorQueryTests(PostgresFixture postgres)
{
    private const string Columns = IndicatorsHarness.RequiredColumns + ", year bigint, geometry text";

    // indicator, disaggregation, value, numerator, denominator, year, geometry
    private const string Rows = """
        ('coverage', 'Cali',    40, 4,  10, 2025, 'POLYGON((0 0,1 0,1 1,0 0))'),
        ('coverage', 'Cali',    35, 3,  10, 2026, 'POLYGON((0 0,1 0,1 1,0 0))'),
        ('coverage', 'Palmira', 60, 6,  10, 2025, 'POINT(2 2)'),
        ('coverage', 'Yumbo',   NULL, NULL, NULL, 2025, NULL),
        ('target',   'Cali',    90, 9,  10, 2025, NULL),
        ('target',   'Palmira', 80, 8,  10, 2025, NULL)
        """;

    private async Task<IndicatorsHarness> WithIndicatorsAsync(Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync(Columns, Rows);
        return h;
    }

    // Default grouping / Duplicate rows summed

    [Fact]
    public async Task A_widget_without_a_group_column_groups_by_disaggregation_and_sums_duplicate_rows()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new { indicators = new[] { "coverage" } });

        Assert.True(result.GetProperty("published").GetBoolean());
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        // Cali's rows 40 and 35 are one point of 75; Yumbo has no value to plot.
        Assert.Equal(["coverage|Cali|75", "coverage|Palmira|60"], IndicatorsHarness.Rows(result));
        Assert.Empty(result.GetProperty("missing").GetProperty("indicators").EnumerateArray());
        Assert.Empty(result.GetProperty("missing").GetProperty("columns").EnumerateArray());
    }

    [Fact]
    public async Task The_field_to_plot_can_be_the_numerator_or_the_denominator()
    {
        await using var h = await WithIndicatorsAsync();

        var numerator = await h.QueryAsync(new { indicators = new[] { "coverage" }, valueField = "numerator" });
        var denominator = await h.QueryAsync(new { indicators = new[] { "coverage" }, valueField = "denominator" });

        Assert.Equal(["coverage|Cali|7", "coverage|Palmira|6"], IndicatorsHarness.Rows(numerator));
        Assert.Equal(["coverage|Cali|20", "coverage|Palmira|10"], IndicatorsHarness.Rows(denominator));
    }

    [Fact]
    public async Task Several_indicators_are_one_series_each_in_the_order_they_were_asked_for()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new { indicators = new[] { "target", "coverage" } });

        Assert.Equal(["target|Cali|90", "target|Palmira|80", "coverage|Cali|75", "coverage|Palmira|60"], IndicatorsHarness.Rows(result));
    }

    // Extra column grouped on

    [Fact]
    public async Task A_widget_can_group_by_a_column_a_script_added()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new { indicators = new[] { "coverage" }, groupBy = "year" });

        Assert.Equal(["coverage|2025|100", "coverage|2026|35"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task Filters_are_built_as_the_clean_data_dashboards_are()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new
        {
            indicators = new[] { "coverage" },
            filters = new object[]
            {
                new { column = "year", op = "range", min = "2025", max = "2025" },
                new { column = "disaggregation", op = "in", values = new[] { "Cali", "Yumbo" } },
            },
        });

        Assert.Equal(["coverage|Cali|40"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task A_value_that_looks_like_sql_is_only_ever_a_value()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new
        {
            indicators = new[] { "coverage'; DROP TABLE x; --" },
            filters = new[] { new { column = "disaggregation", op = "equals", value = "' OR 1=1 --" } },
        });

        Assert.Empty(IndicatorsHarness.Rows(result));
        Assert.Equal(["coverage'; DROP TABLE x; --"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("indicators")));
    }

    [Fact]
    public async Task The_geometry_column_comes_with_the_rows_when_asked_for_and_is_null_otherwise()
    {
        await using var h = await WithIndicatorsAsync();

        var withGeometry = await h.QueryAsync(new { indicators = new[] { "coverage" }, geometryColumn = "geometry" });
        var without = await h.QueryAsync(new { indicators = new[] { "coverage" } });

        Assert.Equal(["POLYGON((0 0,1 0,1 1,0 0))", "POINT(2 2)"],
            withGeometry.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("geometry").GetString()));
        Assert.All(without.GetProperty("rows").EnumerateArray(), r => Assert.Equal(JsonValueKind.Null, r.GetProperty("geometry").ValueKind));
    }

    [Fact]
    public async Task A_group_with_no_value_in_the_group_column_has_a_null_group()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(IndicatorsHarness.RequiredColumns, "('coverage', NULL, 5, 5, 10), ('coverage', 'Cali', 7, 7, 10)");

        var result = await h.QueryAsync(new { indicators = new[] { "coverage" } });

        Assert.Equal(["Cali", null], result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()));
    }

    // Row limit

    [Fact]
    public async Task Past_the_row_limit_the_response_says_so_instead_of_cutting_silently()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "3" });
        await h.PublishAsync(IndicatorsHarness.RequiredColumns, "");
        await h.ExecuteAsync($"INSERT INTO \"p_{h.ProjectId:N}_indicators\".indicators SELECT 'coverage', 'g' || g, g, g, 100 FROM generate_series(1, 10) g");

        var limited = await h.QueryAsync(new { indicators = new[] { "coverage" } });

        Assert.True(limited.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(3, limited.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Exactly_the_row_limit_is_not_a_reached_limit()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "3" });
        await h.PublishAsync(IndicatorsHarness.RequiredColumns, "('c', 'a', 1, 1, 1), ('c', 'b', 2, 2, 1), ('c', 'c', 3, 3, 1)");

        var result = await h.QueryAsync(new { indicators = new[] { "c" } });

        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(3, result.GetProperty("rows").GetArrayLength());
    }

    // Script 3 has not published

    [Fact]
    public async Task While_nothing_is_published_the_query_answers_not_published_with_no_rows()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var result = await h.QueryAsync(new { indicators = new[] { "coverage" } });

        Assert.False(result.GetProperty("published").GetBoolean());
        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.False(result.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(0, result.GetProperty("missing").GetProperty("indicators").GetArrayLength());
        Assert.Equal(0, result.GetProperty("missing").GetProperty("columns").GetArrayLength());
    }

    // Stale configuration

    [Fact]
    public async Task An_indicator_the_table_no_longer_has_is_reported_as_missing()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new { indicators = new[] { "coverage", "retired" } });

        Assert.True(result.GetProperty("published").GetBoolean());
        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(["retired"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("indicators")));
        Assert.Equal(0, result.GetProperty("missing").GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task A_group_column_a_script_change_removed_is_reported_as_missing()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new { indicators = new[] { "coverage" }, groupBy = "round" });

        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(["round"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task A_geometry_column_the_table_no_longer_has_is_reported_as_missing()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new { indicators = new[] { "coverage" }, geometryColumn = "shape" });

        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(["shape"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task A_filter_on_an_unknown_column_is_reported_as_missing_not_as_an_error()
    {
        await using var h = await WithIndicatorsAsync();

        var response = await h.PostQueryAsync(new
        {
            indicators = new[] { "coverage" },
            filters = new[] { new { column = "region", op = "equals", value = "x" } },
        });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(["region"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task Every_missing_indicator_and_column_is_listed_at_once()
    {
        await using var h = await WithIndicatorsAsync();

        var result = await h.QueryAsync(new
        {
            indicators = new[] { "gone" },
            groupBy = "round",
            geometryColumn = "shape",
            filters = new[] { new { column = "region", op = "empty" } },
        });

        Assert.Equal(["gone"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("indicators")));
        Assert.Equal(["round", "shape", "region"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
    }

    [Fact]
    public async Task A_table_without_the_indicator_column_reports_that_column_and_cannot_check_indicators()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("disaggregation text, value numeric", "('Cali', 1)");

        var result = await h.QueryAsync(new { indicators = new[] { "coverage" } });

        Assert.Equal(["indicator"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
        Assert.Equal(0, result.GetProperty("missing").GetProperty("indicators").GetArrayLength());
        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
    }

    // Validation

    [Fact]
    public async Task An_empty_list_of_indicators_is_a_validation_error()
    {
        await using var h = await WithIndicatorsAsync();

        var response = await h.PostQueryAsync(new { indicators = Array.Empty<string>() });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(problem, "indicators"));
    }

    [Fact]
    public async Task A_body_without_indicators_or_an_empty_body_is_a_validation_error()
    {
        await using var h = await WithIndicatorsAsync();

        var empty = await h.PostQueryAsync(new { });
        var none = await h.Viewer.PostAsync(h.Url + "/query", null);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    }

    [Fact]
    public async Task An_unknown_value_field_is_a_validation_error()
    {
        await using var h = await WithIndicatorsAsync();

        var response = await h.PostQueryAsync(new { indicators = new[] { "coverage" }, valueField = "total" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("valueField", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task The_number_of_histogram_buckets_is_between_1_and_100(int buckets)
    {
        await using var h = await WithIndicatorsAsync();

        var response = await h.PostQueryAsync(new { indicators = new[] { "coverage" }, buckets });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_filter_that_does_not_fit_its_column_is_a_validation_error()
    {
        await using var h = await WithIndicatorsAsync();

        var response = await h.PostQueryAsync(new
        {
            indicators = new[] { "coverage" },
            filters = new[] { new { column = "disaggregation", op = "range", min = "1", max = "2" } },
        });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not support the filter 'range'", IndicatorsHarness.Errors(problem, "filters[0]")[0]);
    }

    [Fact]
    public async Task A_reversed_range_is_a_validation_error()
    {
        await using var h = await WithIndicatorsAsync();

        var response = await h.PostQueryAsync(new
        {
            indicators = new[] { "coverage" },
            filters = new[] { new { column = "year", op = "range", min = "2026", max = "2025" } },
        });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("minimum is greater than the maximum", IndicatorsHarness.Errors(problem, "filters[0]")[0]);
    }

    [Fact]
    public async Task More_than_50_filters_is_a_validation_error()
    {
        await using var h = await WithIndicatorsAsync();
        var filters = Enumerable.Range(0, 51).Select(i => new { column = "disaggregation", op = "equals", value = "x" + i }).ToArray();

        var response = await h.PostQueryAsync(new { indicators = new[] { "coverage" }, filters });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("50", IndicatorsHarness.Errors(problem, "filters")[0]);
    }

    [Fact]
    public async Task More_than_100_values_in_a_filter_is_a_validation_error()
    {
        await using var h = await WithIndicatorsAsync();

        var response = await h.PostQueryAsync(new
        {
            indicators = new[] { "coverage" },
            filters = new[] { new { column = "disaggregation", op = "in", values = Enumerable.Range(0, 101).Select(i => "v" + i).ToArray() } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_value_field_that_is_not_a_number_in_the_table_is_a_validation_error_not_a_server_error()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("indicator text, disaggregation text, value text, numerator numeric, denominator numeric", "('c', 'a', 'high', 1, 1)");

        var response = await h.PostQueryAsync(new { indicators = new[] { "c" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Access

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_404()
    {
        await using var h = await WithIndicatorsAsync();

        var response = await h.PostQueryAsync(new { indicators = new[] { "coverage" } }, h.Outsider);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Every_role_can_query()
    {
        await using var h = await WithIndicatorsAsync();

        foreach (var client in new[] { h.Viewer, h.Analyst, h.Admin })
        {
            var result = await h.QueryAsync(new { indicators = new[] { "target" } }, client);
            Assert.Equal(2, result.GetProperty("rows").GetArrayLength());
        }
    }

    [Fact]
    public async Task Another_projects_table_is_never_read()
    {
        await using var h = await WithIndicatorsAsync();
        var other = await h.AddOtherProjectAsync();

        var response = await h.Admin.PostAsJsonAsync(IndicatorsHarness.UrlOf(other) + "/query", new { indicators = new[] { "coverage" } });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(result.GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task A_column_name_with_quotes_and_capitals_is_quoted_not_interpolated()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(IndicatorsHarness.RequiredColumns + ", \"Mixed \"\"Case\"\"\" text", "('c', 'a', 1, 1, 1, 'x'), ('c', 'b', 2, 2, 1, 'x')");

        var result = await h.QueryAsync(new { indicators = new[] { "c" }, groupBy = "Mixed \"Case\"" });

        Assert.Equal(["c|x|3"], IndicatorsHarness.Rows(result));
    }
}
