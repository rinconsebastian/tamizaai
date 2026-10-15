using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class IndicatorQueryBuilderTests
{
    private const string From = "\"p_x_indicators\".\"coverage\"";

    private static readonly TableColumn[] Columns =
    [
        new("municipality", DataColumnType.Text, null),
        new("value", DataColumnType.Decimal, null),
        new("numerator", DataColumnType.Integer, null),
        new("denominator", DataColumnType.Decimal, null),
        new("year", DataColumnType.Integer, null),
        new("geometry", DataColumnType.Text, null),
        new("Odd \"name\"", DataColumnType.Text, null),
        new("flag", DataColumnType.Boolean, null),
    ];

    private static IndicatorQueryPlan Plan(WidgetConfigInput input, int maxRows = 5000, TableColumn[]? columns = null, bool perRow = false) =>
        IndicatorQueryBuilder.Plan(input.Normalize(), columns ?? Columns, "UTC", From, maxRows, perRow);

    private static WidgetConfigInput Config(
        string? groupBy = "municipality", string? measure = null, WidgetRatio? ratio = null, string? geometry = null, IReadOnlyList<DataFilter>? filters = null) =>
        new("coverage", measure, ratio, groupBy, filters, geometry);

    // Defaults

    [Fact]
    public void The_default_measure_is_value_when_it_is_a_number()
    {
        Assert.Equal("value", IndicatorQueryBuilder.DefaultMeasure(Columns));
        Assert.Equal("municipality", IndicatorQueryBuilder.DefaultGroupBy(Columns));
    }

    [Fact]
    public void Without_a_value_column_the_default_measure_is_the_first_numeric_column()
    {
        var columns = Columns.Where(c => c.Name != "value").ToArray();

        Assert.Equal("numerator", IndicatorQueryBuilder.DefaultMeasure(columns));
    }

    [Fact]
    public void A_value_column_that_is_not_a_number_is_not_the_default_measure()
    {
        var columns = Columns.Select(c => c.Name == "value" ? c with { Type = DataColumnType.Text } : c).ToArray();

        Assert.Equal("numerator", IndicatorQueryBuilder.DefaultMeasure(columns));
    }

    [Fact]
    public void A_table_with_no_numeric_column_has_no_default_measure_and_planning_it_is_an_error()
    {
        var columns = Columns.Where(c => c.Name is "municipality" or "flag").ToArray();

        Assert.Null(IndicatorQueryBuilder.DefaultMeasure(columns));
        Assert.Contains("measure", Plan(Config(), columns: columns).Errors!.Keys);
    }

    [Fact]
    public void A_table_with_only_numbers_has_no_default_group()
    {
        Assert.Null(IndicatorQueryBuilder.DefaultGroupBy(Columns.Where(c => c.Name is "value" or "year").ToArray()));
    }

    // The measure

    [Fact]
    public void The_default_query_sums_the_default_measure_per_group()
    {
        var plan = Plan(Config());

        Assert.Null(plan.Errors);
        Assert.Empty(plan.MissingColumns);
        Assert.Contains("sum(\"value\")::double precision", plan.Sql!.Sql);
        Assert.Contains("GROUP BY \"municipality\"", plan.Sql.Sql);
        Assert.Contains("FROM " + From, plan.Sql.Sql);
        Assert.Contains("NULL::text", plan.Sql.Sql); // no geometry requested
    }

    [Fact]
    public void A_chosen_measure_replaces_the_default()
    {
        var plan = Plan(Config(measure: "numerator"));

        Assert.Contains("sum(\"numerator\")", plan.Sql!.Sql);
        Assert.DoesNotContain("sum(\"value\")", plan.Sql.Sql);
    }

    [Fact]
    public void A_row_without_a_value_to_plot_is_left_out()
    {
        Assert.Contains("\"value\" IS NOT NULL", Plan(Config()).Sql!.Sql);
    }

    [Fact]
    public void The_limit_is_a_parameter_and_one_extra_row_is_asked_for()
    {
        var plan = Plan(Config(), maxRows: 7);

        var limit = plan.Sql!.Parameters.Single(p => p.Name == "limit");
        Assert.Equal(8, limit.Value);
        Assert.Equal(NpgsqlDbType.Integer, limit.Type);
    }

    [Fact]
    public void Identifiers_come_from_the_catalog_quoted()
    {
        var plan = Plan(Config(groupBy: "Odd \"name\"", geometry: "geometry", measure: "numerator"));

        Assert.Contains("\"Odd \"\"name\"\"\"", plan.Sql!.Sql);
        Assert.Contains("min(\"geometry\"::text)", plan.Sql.Sql);
        Assert.Contains("sum(\"numerator\")", plan.Sql.Sql);
    }

    [Fact]
    public void Filters_become_conditions_with_parameters()
    {
        var plan = Plan(Config(filters: [new DataFilter("municipality", "equals", "' OR 1=1 --"), new DataFilter("year", "range", Min: "2020")]));

        Assert.Contains("\"municipality\"::text = @f0", plan.Sql!.Sql);
        Assert.Contains("\"year\" >= @f1min", plan.Sql.Sql);
        Assert.DoesNotContain("OR 1=1", plan.Sql.Sql);
        Assert.Contains(plan.Sql.Parameters, p => p.Name == "f0" && (string)p.Value == "' OR 1=1 --");
    }

    // The ratio

    [Fact]
    public void A_ratio_is_the_sum_of_the_numerator_over_the_sum_of_the_denominator_with_no_division_by_zero()
    {
        var plan = Plan(Config(ratio: new WidgetRatio("numerator", "denominator")));

        Assert.Null(plan.Errors);
        Assert.Contains("sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)", plan.Sql!.Sql);
        Assert.DoesNotContain("sum(\"value\")", plan.Sql.Sql);
        // A group whose denominator sums to zero has no value.
        Assert.Contains("HAVING (sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)) IS NOT NULL", plan.Sql.Sql);
    }

    [Fact]
    public void A_ratio_column_that_is_not_a_number_is_an_error_naming_the_field()
    {
        var plan = Plan(Config(ratio: new WidgetRatio("municipality", "flag")));

        Assert.Contains("ratio.numerator", plan.Errors!.Keys);
        Assert.Contains("ratio.denominator", plan.Errors.Keys);
        Assert.Null(plan.Sql);
    }

    [Theory]
    [InlineData("municipality")]
    [InlineData("flag")]
    public void A_measure_that_is_not_a_number_in_the_table_is_an_error(string column)
    {
        var plan = Plan(Config(measure: column));

        Assert.Contains("measure", plan.Errors!.Keys);
        Assert.Contains($"'{column}'", plan.Errors["measure"][0]);
    }

    [Fact]
    public void Integer_and_decimal_columns_can_be_measures()
    {
        Assert.NotNull(Plan(Config(measure: "numerator")).Sql);
        Assert.NotNull(Plan(Config(measure: "denominator")).Sql);
    }

    // Group and rows

    [Fact]
    public void Without_a_group_the_query_returns_one_total()
    {
        var plan = Plan(Config(groupBy: null));

        Assert.Null(plan.Errors);
        Assert.Contains("SELECT NULL::text, (sum(\"value\")::double precision)", plan.Sql!.Sql);
        Assert.DoesNotContain("GROUP BY", plan.Sql.Sql);
    }

    [Fact]
    public void Without_a_group_a_per_row_query_returns_each_rows_value_for_a_histogram()
    {
        var plan = Plan(Config(groupBy: null), perRow: true);

        Assert.DoesNotContain("sum(", plan.Sql!.Sql);
        Assert.DoesNotContain("GROUP BY", plan.Sql.Sql);
        Assert.Contains("\"value\"::double precision", plan.Sql.Sql);
    }

    [Fact]
    public void A_per_row_query_with_a_group_still_sums_by_group()
    {
        var plan = Plan(Config(groupBy: "municipality"), perRow: true);

        Assert.Contains("GROUP BY \"municipality\"", plan.Sql!.Sql);
    }

    [Fact]
    public void Grouping_by_any_type_of_column_is_allowed()
    {
        foreach (var column in new[] { "year", "flag", "geometry", "municipality" })
        {
            Assert.NotNull(Plan(Config(groupBy: column)).Sql);
        }
    }

    // Stale references

    [Fact]
    public void Missing_columns_are_listed_in_the_order_they_are_met_and_there_is_nothing_to_run()
    {
        var plan = Plan(Config(groupBy: "round", geometry: "shape",
            filters: [new DataFilter("region", "empty"), new DataFilter("region", "notEmpty"), new DataFilter("year", "empty")]));

        Assert.Null(plan.Errors);
        Assert.Null(plan.Sql);
        Assert.Equal(["round", "shape", "region"], plan.MissingColumns);
    }

    [Fact]
    public void A_measure_the_table_no_longer_has_is_reported()
    {
        var columns = Columns.Where(c => c.Name != "numerator").ToArray();

        var plan = Plan(Config(measure: "numerator"), columns: columns);

        Assert.Equal(["numerator"], plan.MissingColumns);
        Assert.Null(plan.Sql);
    }

    [Fact]
    public void Ratio_columns_the_table_no_longer_has_are_reported()
    {
        var columns = Columns.Where(c => c.Name is not ("numerator" or "denominator")).ToArray();

        var plan = Plan(Config(ratio: new WidgetRatio("numerator", "denominator")), columns: columns);

        Assert.Equal(["numerator", "denominator"], plan.MissingColumns);
        Assert.Null(plan.Sql);
    }

    [Fact]
    public void A_malformed_filter_on_a_present_column_is_an_error_even_when_something_else_is_missing()
    {
        var plan = Plan(Config(groupBy: "round", filters: [new DataFilter("municipality", "range", Min: "1")]));

        Assert.NotNull(plan.Errors);
        Assert.Contains("filters[0]", plan.Errors.Keys);
        Assert.Null(plan.Sql);
    }

    // Order

    private const string Group = From + ".\"municipality\" NULLS LAST";

    private static WidgetConfigInput Sorted(WidgetSort? sort, string groupBy = "municipality") =>
        new("coverage", null, null, groupBy, null, null, null, sort);

    private static string OrderBy(WidgetConfigInput input, TableColumn[]? columns = null) =>
        Plan(input, columns: columns).Sql!.Sql.Split('\n').Single(l => l.StartsWith("ORDER BY", StringComparison.Ordinal));

    [Fact]
    public void Without_an_order_the_groups_follow_the_group_columns_own_type()
    {
        Assert.Equal("ORDER BY " + Group, OrderBy(Config()));
        Assert.Equal("ORDER BY " + From + ".\"year\" NULLS LAST", OrderBy(Config(groupBy: "year")));
    }

    [Fact]
    public void An_order_by_the_plotted_value_sorts_by_the_aggregate_and_breaks_ties_by_group()
    {
        var order = OrderBy(Sorted(new WidgetSort("value", null, null, "desc")));

        Assert.Equal("ORDER BY (sum(\"value\")::double precision) DESC NULLS LAST, " + Group, order);
    }

    [Fact]
    public void An_order_by_the_plotted_ratio_sorts_by_the_ratio()
    {
        var input = Sorted(new WidgetSort("value", null, null, "asc")) with { Ratio = new WidgetRatio("numerator", "denominator") };

        Assert.Equal(
            "ORDER BY (sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)) ASC NULLS LAST, " + Group, OrderBy(input));
    }

    [Theory]
    [InlineData("year", null, "min(\"year\")")]
    [InlineData("year", "number", "min(\"year\")")]
    [InlineData("year", "text", "min(NULLIF(\"year\"::text, ''))")]
    [InlineData("value", null, "min(NULLIF(\"value\", 'NaN'))")]
    [InlineData("value", "text", "min(NULLIF(\"value\"::text, ''))")]
    [InlineData("geometry", null, "min(NULLIF(\"geometry\"::text, ''))")]
    [InlineData("geometry", "text", "min(NULLIF(\"geometry\"::text, ''))")]
    [InlineData("flag", null, "bool_and(\"flag\")")]
    public void The_sort_key_depends_on_the_column_type_and_the_comparison(string column, string? compareAs, string key)
    {
        var order = OrderBy(Sorted(new WidgetSort("column", column, compareAs, "asc")));

        Assert.Equal($"ORDER BY {key} ASC NULLS LAST, {Group}", order);
    }

    [Fact]
    public void A_date_or_timestamp_column_sorts_by_its_smallest_value()
    {
        TableColumn[] columns = [.. Columns, new("visited_on", DataColumnType.Date, null), new("seen_at", DataColumnType.Timestamp, null)];

        Assert.Contains("min(\"visited_on\") DESC", OrderBy(Sorted(new WidgetSort("column", "visited_on", null, "desc")), columns));
        Assert.Contains("min(\"seen_at\") DESC", OrderBy(Sorted(new WidgetSort("column", "seen_at", null, "desc")), columns));
    }

    [Fact]
    public void Text_read_as_a_number_is_guarded_inside_the_aggregate()
    {
        var order = OrderBy(Sorted(new WidgetSort("column", "geometry", "number", "asc")));

        Assert.StartsWith("ORDER BY min(CASE WHEN \"geometry\"::text ~ '^[[:space:]]*[-+]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][-+]?[0-9]+)?[[:space:]]*$' "
            + "AND pg_input_is_valid(\"geometry\"::text, 'numeric') THEN \"geometry\"::text::numeric END) ASC NULLS LAST, ", order);
        Assert.DoesNotContain("\\", order);
    }

    [Fact]
    public void A_column_named_with_quotes_is_quoted_in_the_sort_key()
    {
        var order = OrderBy(Sorted(new WidgetSort("column", "Odd \"name\"", "number", "asc")));

        Assert.Contains("\"Odd \"\"name\"\"\"::text", order);
    }

    [Fact]
    public void The_sort_column_is_reported_missing_after_the_filter_columns()
    {
        var input = Config(groupBy: "round", filters: [new DataFilter("region", "empty")]) with { Sort = new WidgetSort("column", "rank", null, "asc") };

        var plan = Plan(input);

        Assert.Null(plan.Errors);
        Assert.Null(plan.Sql);
        Assert.Equal(["round", "region", "rank"], plan.MissingColumns);
    }

    [Theory]
    [InlineData("flag", "a true/false column")]
    public void Comparing_a_true_false_column_as_a_number_is_an_error_under_sort_column(string column, string words)
    {
        var plan = Plan(Sorted(new WidgetSort("column", column, "number", "asc")));

        Assert.Null(plan.Sql);
        Assert.Equal($"Column 'flag' is {words}, so it is sorted in its own order and cannot be compared as text or as a number.", plan.Errors!["sort.column"][0]);
    }

    [Fact]
    public void A_date_or_timestamp_column_compared_as_text_is_an_error_under_sort_column()
    {
        TableColumn[] columns = [.. Columns, new("visited_on", DataColumnType.Date, null), new("seen_at", DataColumnType.Timestamp, null)];

        foreach (var (column, words) in new[] { ("visited_on", "a date column"), ("seen_at", "a timestamp column") })
        {
            var plan = Plan(Sorted(new WidgetSort("column", column, "text", "asc")), columns: columns);

            Assert.Contains($"is {words}", plan.Errors!["sort.column"][0]);
        }
    }

    [Fact]
    public void A_missing_sort_column_is_reported_not_checked_for_its_type()
    {
        var plan = Plan(Sorted(new WidgetSort("column", "gone", "number", "asc")));

        Assert.Null(plan.Errors);
        Assert.Equal(["gone"], plan.MissingColumns);
    }

    [Fact]
    public void A_per_row_query_has_no_order()
    {
        var input = Config(groupBy: null) with { Sort = null };

        Assert.DoesNotContain("ORDER BY", Plan(input, perRow: true).Sql!.Sql);
    }
}
