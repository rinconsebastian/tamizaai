using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class IndicatorQueryBuilderTests
{
    private const string From = "\"p_x_indicators\".\"indicators\"";

    private static readonly TableColumn[] Columns =
    [
        new("indicator", DataColumnType.Text, null),
        new("disaggregation", DataColumnType.Text, null),
        new("value", DataColumnType.Decimal, null),
        new("numerator", DataColumnType.Integer, null),
        new("denominator", DataColumnType.Decimal, null),
        new("year", DataColumnType.Integer, null),
        new("geometry", DataColumnType.Text, null),
        new("Odd \"name\"", DataColumnType.Text, null),
        new("flag", DataColumnType.Boolean, null),
    ];

    private static IndicatorQueryPlan Plan(WidgetConfigInput input, int maxRows = 5000, TableColumn[]? columns = null) =>
        IndicatorQueryBuilder.Plan(input.Normalize(), columns ?? Columns, "UTC", From, maxRows);

    private static WidgetConfigInput One(string? groupBy = null, string? valueField = null, string? geometry = null, IReadOnlyList<DataFilter>? filters = null) =>
        new(["coverage"], groupBy, valueField, filters, geometry);

    [Fact]
    public void The_default_query_sums_the_value_per_indicator_and_disaggregation()
    {
        var plan = Plan(One());

        Assert.Null(plan.Errors);
        Assert.Empty(plan.MissingColumns);
        Assert.Contains("sum(\"value\")::double precision", plan.Sql!.Sql);
        Assert.Contains("GROUP BY \"indicator\"::text, \"disaggregation\"", plan.Sql.Sql);
        Assert.Contains("FROM " + From, plan.Sql.Sql);
        Assert.Contains("NULL::text", plan.Sql.Sql); // no geometry requested
    }

    [Fact]
    public void Indicators_and_the_limit_are_parameters_and_one_extra_row_is_asked_for()
    {
        var plan = Plan(new WidgetConfigInput(["a'; DROP TABLE x; --", "b"]), maxRows: 7);

        var indicators = plan.Sql!.Parameters.Single(p => p.Name == "indicators");
        var limit = plan.Sql.Parameters.Single(p => p.Name == "limit");
        Assert.Equal(["a'; DROP TABLE x; --", "b"], (string[])indicators.Value);
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Text, indicators.Type);
        Assert.Equal(8, limit.Value);
        Assert.DoesNotContain("DROP", plan.Sql.Sql);
        Assert.DoesNotContain("a'", plan.Sql.Sql);
    }

    [Fact]
    public void Identifiers_come_from_the_catalog_quoted()
    {
        var plan = Plan(One(groupBy: "Odd \"name\"", geometry: "geometry", valueField: "numerator"));

        Assert.Contains("\"Odd \"\"name\"\"\"", plan.Sql!.Sql);
        Assert.Contains("min(\"geometry\"::text)", plan.Sql.Sql);
        Assert.Contains("sum(\"numerator\")", plan.Sql.Sql);
    }

    [Fact]
    public void Filters_become_conditions_with_parameters_after_the_indicator_condition()
    {
        var plan = Plan(One(filters: [new DataFilter("disaggregation", "equals", "' OR 1=1 --"), new DataFilter("year", "range", Min: "2020")]));

        Assert.Contains("\"disaggregation\"::text = @f0", plan.Sql!.Sql);
        Assert.Contains("\"year\" >= @f1min", plan.Sql.Sql);
        Assert.DoesNotContain("OR 1=1", plan.Sql.Sql);
        Assert.Contains(plan.Sql.Parameters, p => p.Name == "f0" && (string)p.Value == "' OR 1=1 --");
    }

    [Fact]
    public void A_row_without_a_value_to_plot_is_left_out()
    {
        Assert.Contains("\"value\" IS NOT NULL", Plan(One()).Sql!.Sql);
    }

    // Stale references (design D8)

    [Fact]
    public void Missing_columns_are_listed_in_the_order_they_are_met_and_there_is_nothing_to_run()
    {
        var plan = Plan(One(groupBy: "round", geometry: "shape", filters: [new DataFilter("region", "empty"), new DataFilter("region", "notEmpty"), new DataFilter("year", "empty")]));

        Assert.Null(plan.Errors);
        Assert.Null(plan.Sql);
        Assert.Equal(["round", "shape", "region"], plan.MissingColumns);
    }

    [Fact]
    public void A_table_without_the_indicator_or_value_column_reports_them()
    {
        var columns = Columns.Where(c => c.Name is not ("indicator" or "numerator")).ToArray();

        var plan = Plan(One(valueField: "numerator"), columns: columns);

        Assert.Equal(["indicator", "numerator"], plan.MissingColumns);
        Assert.Null(plan.Sql);
    }

    [Fact]
    public void A_malformed_filter_on_a_present_column_is_an_error_even_when_something_else_is_missing()
    {
        var plan = Plan(One(groupBy: "round", filters: [new DataFilter("disaggregation", "range", Min: "1")]));

        Assert.NotNull(plan.Errors);
        Assert.Contains("filters[0]", plan.Errors.Keys);
        Assert.Null(plan.Sql);
    }

    [Theory]
    [InlineData("disaggregation")]
    [InlineData("flag")]
    public void A_value_field_that_is_not_a_number_in_the_table_is_an_error(string column)
    {
        var columns = Columns.Select(c => c.Name == "value" ? c with { Type = column == "flag" ? DataColumnType.Boolean : DataColumnType.Text } : c).ToArray();

        var plan = Plan(One(), columns: columns);

        Assert.Contains("valueField", plan.Errors!.Keys);
    }

    [Fact]
    public void Integer_and_decimal_value_columns_are_summed()
    {
        Assert.NotNull(Plan(One(valueField: "numerator")).Sql);
        Assert.NotNull(Plan(One(valueField: "denominator")).Sql);
    }

    [Fact]
    public void Grouping_by_any_type_of_column_is_allowed()
    {
        foreach (var column in new[] { "year", "flag", "geometry", "indicator" })
        {
            Assert.NotNull(Plan(One(groupBy: column)).Sql);
        }
    }
}
