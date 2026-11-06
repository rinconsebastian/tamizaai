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

    // Tooltip columns of a map

    private static readonly TableColumn[] TooltipColumns =
    [
        .. Columns,
        new("address", DataColumnType.Text, null),
        new("target", DataColumnType.Integer, null),
        new("progress", DataColumnType.Decimal, null),
        new("visited_on", DataColumnType.Date, null),
        new("seen_at", DataColumnType.Timestamp, null),
    ];

    private static IndicatorQueryPlan MapPlan(params string[] tooltip) =>
        Plan(Config(geometry: "geometry") with { Tooltip = tooltip.Select(c => new WidgetTooltipItem(c)).ToList() }, columns: TooltipColumns);

    [Theory]
    [InlineData("target", "sum(\"target\")::text")]
    [InlineData("progress", "sum(\"progress\")::text")]
    [InlineData("address", "left(min(NULLIF(\"address\"::text, '')), 201)")]
    [InlineData("visited_on", "min(\"visited_on\")::text")]
    [InlineData("seen_at", "min(\"seen_at\")::text")]
    [InlineData("flag", "bool_and(\"flag\")")]
    public void Each_column_type_has_its_own_tooltip_expression(string column, string expression)
    {
        var plan = MapPlan(column);

        Assert.Contains($", count(*), {expression}\nFROM", plan.Sql!.Sql);
        Assert.Equal([TooltipColumns.Single(c => c.Name == column).Type], plan.Sql.TooltipTypes);
    }

    [Fact]
    public void A_map_selects_the_row_count_and_the_tooltip_columns_in_order_after_the_geometry()
    {
        var plan = MapPlan("address", "target", "progress");

        Assert.Contains(
            "SELECT \"municipality\"::text, (sum(\"value\")::double precision), min(\"geometry\"::text), count(*), "
            + "left(min(NULLIF(\"address\"::text, '')), 201), sum(\"target\")::text, sum(\"progress\")::text\n",
            plan.Sql!.Sql);
        Assert.Equal([DataColumnType.Text, DataColumnType.Integer, DataColumnType.Decimal], plan.Sql.TooltipTypes);
    }

    [Fact]
    public void A_map_with_no_tooltip_columns_still_counts_its_rows()
    {
        var plan = MapPlan();

        Assert.Contains("min(\"geometry\"::text), count(*)\n", plan.Sql!.Sql);
        Assert.Empty(plan.Sql.TooltipTypes!);
    }

    [Fact]
    public void Tooltip_columns_are_quoted_through_the_catalog()
    {
        TableColumn[] columns = [.. TooltipColumns, new("Dirección \"x\"", DataColumnType.Text, null)];
        var plan = Plan(Config(geometry: "geometry") with { Tooltip = [new WidgetTooltipItem("Dirección \"x\"")] }, columns: columns);

        Assert.Contains("min(NULLIF(\"Dirección \"\"x\"\"\"::text, ''))", plan.Sql!.Sql);
    }

    [Fact]
    public void A_tooltip_value_is_never_text_in_the_sql_only_its_column_name_is()
    {
        var plan = MapPlan("address");

        Assert.DoesNotContain(plan.Sql!.Parameters, p => p.Name != "limit");
    }

    [Fact]
    public void Missing_tooltip_columns_are_listed_last_after_the_sort_column()
    {
        var input = Config(groupBy: "round", geometry: "shape") with
        {
            Tooltip = [new WidgetTooltipItem("address"), new WidgetTooltipItem("gone"), new WidgetTooltipItem("round")],
            Sort = null,
        };
        var plan = Plan(input, columns: TooltipColumns);

        Assert.Null(plan.Sql);
        Assert.Equal(["round", "shape", "gone"], plan.MissingColumns);
    }

    [Fact]
    public void Tooltip_columns_come_after_the_sort_column_in_the_missing_list()
    {
        var input = Config(geometry: "geometry") with
        {
            Sort = new WidgetSort("column", "order_key", "text", "asc"),
            Tooltip = [new WidgetTooltipItem("gone")],
        };

        Assert.Equal(["order_key", "gone"], Plan(input, columns: TooltipColumns).MissingColumns);
    }

    [Fact]
    public void A_grouped_query_that_is_not_a_map_has_no_extra_outputs()
    {
        var plan = Plan(Config(), columns: TooltipColumns);

        Assert.DoesNotContain("count(*)", plan.Sql!.Sql);
        Assert.Null(plan.Sql.TooltipTypes);
        Assert.StartsWith("SELECT \"municipality\"::text, (sum(\"value\")::double precision), NULL::text\n", plan.Sql.Sql);
    }

    [Fact]
    public void The_tooltip_rows_are_those_the_where_keeps_including_the_filters()
    {
        var plan = Plan(
            Config(geometry: "geometry", filters: [new DataFilter("year", "range", Min: "2026", Max: "2026")]) with { Tooltip = [new WidgetTooltipItem("target")] },
            columns: TooltipColumns);

        // One WHERE serves the aggregate, the row count and the tooltip outputs alike.
        Assert.Equal(1, plan.Sql!.Sql.Split("WHERE").Length - 1);
        Assert.Contains("\"year\" >= ", plan.Sql.Sql);
    }

    // Dashboard filters (design D3)

    private static IndicatorQueryPlan Filtered(
        WidgetConfigInput input, IReadOnlyList<AppliedDashboardFilter> dashboardFilters, TableColumn[]? columns = null, string zone = "UTC") =>
        IndicatorQueryBuilder.Plan(input.Normalize(), columns ?? Columns, zone, From, 5000, dashboardFilters: dashboardFilters);

    [Fact]
    public void A_widget_filter_and_a_dashboard_filter_on_one_column_both_hold()
    {
        var plan = Filtered(
            Config(filters: [new DataFilter("municipality", "in", Values: ["Cali", "Buga"])]),
            [new AppliedDashboardFilter("municipality", DashboardFilterKinds.Text, Values: ["Cali", "Palmira"])]);

        Assert.Contains("WHERE \"value\" IS NOT NULL AND \"municipality\"::text = ANY(@f0) AND \"municipality\"::text = ANY(@d0)", plan.Sql!.Sql);
        Assert.Equal(["Cali", "Buga"], (string[])plan.Sql.Parameters.Single(p => p.Name == "f0").Value);
        Assert.Equal(["Cali", "Palmira"], (string[])plan.Sql.Parameters.Single(p => p.Name == "d0").Value);
        Assert.Empty(plan.NotFiltered!);
    }

    [Fact]
    public void A_number_filter_on_an_integer_column_binds_a_numeric_bound()
    {
        var plan = Filtered(Config(), [new AppliedDashboardFilter("year", DashboardFilterKinds.Number, Min: "2025.5")]);

        Assert.Contains("\"year\" >= @d0min", plan.Sql!.Sql);
        var bound = plan.Sql.Parameters.Single(p => p.Name == "d0min");
        Assert.Equal((NpgsqlDbType.Numeric, 2025.5m), (bound.Type, (decimal)bound.Value));
    }

    [Fact]
    public void A_date_filter_on_a_timestamp_column_keeps_whole_days_in_the_zone_with_one_zone_parameter()
    {
        TableColumn[] columns = [.. Columns, new("visited_at", DataColumnType.Timestamp, null)];
        var plan = Filtered(
            Config(filters: [new DataFilter("visited_at", "range", Min: "2026-01-01")]),
            [new AppliedDashboardFilter("visited_at", DashboardFilterKinds.Date, Min: "2026-09-01", Max: "2026-09-01")],
            columns, "America/Bogota");

        Assert.Contains(
            "\"visited_at\" >= (@d0min::date)::timestamp AT TIME ZONE @tz AND \"visited_at\" < ((@d0max::date + 1)::timestamp AT TIME ZONE @tz)",
            plan.Sql!.Sql);
        Assert.Equal("America/Bogota", plan.Sql.Parameters.Single(p => p.Name == "tz").Value);
    }

    [Fact]
    public void A_missing_column_or_one_of_another_kind_is_not_filtered_and_the_widget_still_draws()
    {
        // `year` is an integer here, so a text filter on it does not apply; `sex` is not in the table.
        var plan = Filtered(
            Config(),
            [
                new AppliedDashboardFilter("sex", DashboardFilterKinds.Text, Values: ["female"]),
                new AppliedDashboardFilter("year", DashboardFilterKinds.Text, Values: ["2026"]),
                new AppliedDashboardFilter("municipality", DashboardFilterKinds.Text, Values: ["Cali"]),
            ]);

        Assert.NotNull(plan.Sql);
        Assert.Empty(plan.MissingColumns);
        Assert.Equal(["sex", "year"], plan.NotFiltered!);
        Assert.Contains("\"municipality\"::text = ANY(@d0)", plan.Sql.Sql);
        Assert.DoesNotContain("\"year\"", plan.Sql.Sql);
    }

    [Fact]
    public void A_true_false_filter_applies_only_to_a_true_false_column()
    {
        var plan = Filtered(
            Config(),
            [
                new AppliedDashboardFilter("flag", DashboardFilterKinds.Boolean, Values: ["true"]),
                new AppliedDashboardFilter("municipality", DashboardFilterKinds.Boolean, Values: ["true"]),
            ]);

        Assert.Contains("\"flag\" = ANY(@d0)", plan.Sql!.Sql);
        Assert.Equal(["municipality"], plan.NotFiltered!);
    }

    [Fact]
    public void Fifty_widget_filters_and_eight_dashboard_filters_plan_together()
    {
        TableColumn[] columns =
        [
            .. Columns,
            .. Enumerable.Range(0, 50).Select(i => new TableColumn($"w{i}", DataColumnType.Text, null)),
            .. Enumerable.Range(0, 8).Select(i => new TableColumn($"d{i}", DataColumnType.Text, null)),
        ];
        var plan = Filtered(
            Config(filters: Enumerable.Range(0, 50).Select(i => new DataFilter($"w{i}", "in", Values: ["x"])).ToList()),
            Enumerable.Range(0, 8).Select(i => new AppliedDashboardFilter($"d{i}", DashboardFilterKinds.Text, Values: ["y"])).ToList(),
            columns);

        Assert.Null(plan.Errors);
        Assert.Equal(50 + 8 + 1, plan.Sql!.Parameters.Count); // and the limit
    }

    [Fact]
    public void An_invalid_value_is_an_error_at_the_requests_index()
    {
        // The first filter does not apply, so the second is the first of the set built; its error keeps index 1.
        var plan = Filtered(
            Config(),
            [
                new AppliedDashboardFilter("sex", DashboardFilterKinds.Text, Values: ["female"]),
                new AppliedDashboardFilter("year", DashboardFilterKinds.Number, Min: "3", Max: "2"),
            ]);

        Assert.Null(plan.Sql);
        Assert.Equal(["dashboardFilters[1]"], plan.Errors!.Keys);
    }

    [Fact]
    public void Without_dashboard_filters_nothing_is_reported_as_not_filtered()
    {
        Assert.Empty(Plan(Config()).NotFiltered!);
        Assert.DoesNotContain("@d", Plan(Config()).Sql!.Sql);
    }

    // A pie's domain (design D8 of dashboard palettes)

    private static readonly DataFilter Own = new("municipality", "in", Values: ["Cali", "Buga"]);

    private static readonly AppliedDashboardFilter OnDashboard = new("year", DashboardFilterKinds.Number, Min: "2025");

    /// <summary>A statement's parameters as <c>name type value</c>, a list of values joined, to compare two statements.</summary>
    private static string[] Described(IndicatorSql sql) =>
        [.. sql.Parameters.Select(p => $"{p.Name} {p.Type} {(p.Value is string[] values ? string.Join(",", values) : p.Value)}")];

    private static IndicatorQueryPlan Of(string? chartType, WidgetConfigInput input, bool dashboard = true, TableColumn[]? columns = null) =>
        IndicatorQueryBuilder.Plan(input.Normalize(), columns ?? Columns, "UTC", From, 5000, dashboardFilters: dashboard ? [OnDashboard] : null, chartType: chartType);

    [Fact]
    public void A_pie_plans_a_domain_statement_over_its_group_column_through_its_own_filters_and_never_the_dashboards()
    {
        var plan = Of(ChartTypes.Pie, Config(filters: [Own]));

        Assert.NotNull(plan.Sql);
        Assert.Null(plan.Series);
        var domain = plan.Domain!;
        Assert.Contains("SELECT \"municipality\"::text, (sum(\"value\")::double precision) FROM " + From, domain.Sql);
        Assert.Contains("\"value\" IS NOT NULL AND \"municipality\"::text = ANY(@f0)", domain.Sql);
        Assert.Contains("GROUP BY \"municipality\"", domain.Sql);
        Assert.EndsWith("LIMIT @cap", domain.Sql);
        Assert.DoesNotContain("@d0", domain.Sql);
        Assert.DoesNotContain("\"year\"", domain.Sql);
        // The pie's rows are read through both sets of filters, its domain through the widget's own.
        Assert.Contains(plan.Sql!.Parameters, p => p.Name == "d0min");
        Assert.DoesNotContain(domain.Parameters, p => p.Name.StartsWith('d'));
        Assert.Equal(["f0", "cap"], domain.Parameters.Select(p => p.Name));
        Assert.Equal(["Cali", "Buga"], (string[])domain.Parameters.Single(p => p.Name == "f0").Value);
    }

    [Fact]
    public void A_pies_domain_ranks_the_missing_group_with_the_others_in_one_statement()
    {
        var domain = Of(ChartTypes.Pie, Config()).Domain!;

        // One grouping, so the rows with no group are a group like any other and take their place by their total.
        Assert.Equal(
            $"""
            SELECT "municipality"::text, (sum("value")::double precision) FROM {From}
            WHERE "value" IS NOT NULL
            GROUP BY "municipality"
            HAVING (sum("value")::double precision) > 0 AND (sum("value")::double precision) < 'Infinity'::double precision
            ORDER BY 2 DESC, {From}."municipality" NULLS LAST
            LIMIT @cap
            """,
            domain.Sql);
        Assert.DoesNotContain("UNION", domain.Sql);
        Assert.DoesNotContain("\"municipality\" IS NULL", domain.Sql);
        Assert.DoesNotContain("\"municipality\" IS NOT NULL", domain.Sql);
    }

    [Fact]
    public void A_pies_domain_keeps_only_groups_whose_total_is_a_number_above_zero()
    {
        var domain = Of(ChartTypes.Pie, Config(measure: null, ratio: new WidgetRatio("numerator", "denominator"))).Domain!;

        // NaN sorts above every number in PostgreSQL, so it is left out in the statement and cannot take a place of the cap.
        const string ratio = "sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)";
        Assert.Contains($"HAVING ({ratio}) > 0 AND ({ratio}) < 'Infinity'::double precision", domain.Sql);
    }

    [Fact]
    public void A_pies_domain_breaks_equal_totals_in_the_order_of_the_pies_own_rows_the_missing_group_last()
    {
        // The rows of a pie, which cannot be sorted, are in the group column's own order with the missing group last: the domain's tie-break is that text.
        foreach (var groupBy in new[] { "municipality", "year", "Odd \"name\"" })
        {
            var plan = Of(ChartTypes.Pie, Config(groupBy: groupBy), dashboard: false);
            var order = $"{From}.{DataSources.Quote(groupBy)} NULLS LAST";

            Assert.Contains($"ORDER BY {order}\n", plan.Sql!.Sql);
            Assert.Contains($"ORDER BY 2 DESC, {order}\n", plan.Domain!.Sql);
        }

        // The column itself, not its text: a number group breaks a tie as a number (2 before 10), as its rows are ordered.
        var byYear = Of(ChartTypes.Pie, Config(groupBy: "year"), dashboard: false).Domain!.Sql;
        Assert.DoesNotContain("ORDER BY 2 DESC, 1", byYear);
    }

    [Fact]
    public void A_pies_domain_reads_one_value_more_than_the_pie_has_colors_for_so_a_fold_can_be_told()
    {
        var domain = Of(ChartTypes.Pie, Config()).Domain!;

        Assert.Equal(8, WidgetConfig.MaxSlices);
        var cap = domain.Parameters.Single(p => p.Name == SeriesQueryBuilder.CapParameter);
        Assert.Equal((NpgsqlDbType.Integer, 9), (cap.Type, cap.Value));
    }

    [Fact]
    public void A_ratio_pie_sums_numerator_and_denominator_in_its_domain_as_it_does_in_its_rows()
    {
        var plan = Of(ChartTypes.Pie, Config(measure: null, ratio: new WidgetRatio("numerator", "denominator")));

        const string ratio = "sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)";
        Assert.Contains($"SELECT \"municipality\"::text, ({ratio}) FROM {From}", plan.Domain!.Sql);
        Assert.Contains("(\"numerator\" IS NOT NULL OR \"denominator\" IS NOT NULL)", plan.Domain.Sql);
        Assert.Contains($"SELECT \"municipality\"::text, ({ratio}), NULL::text", plan.Sql!.Sql);
    }

    [Fact]
    public void A_pies_domain_is_not_the_statement_of_a_series_which_keeps_the_missing_value_apart()
    {
        var pie = Of(ChartTypes.Pie, Config(groupBy: "Odd \"name\"", filters: [Own]), dashboard: false);
        var series = Of(ChartTypes.Column, new WidgetConfigInput("coverage", GroupBy: "municipality", SeriesBy: "Odd \"name\"", Filters: [Own]), dashboard: false);

        // The same filters bind the same parameters, but the series' statement lists the missing value after the others and a pie ranks it.
        Assert.NotEqual(series.Series!.Domain!.Sql, pie.Domain!.Sql);
        Assert.Equal(Described(series.Series.Domain), Described(pie.Domain));
        Assert.Contains("UNION ALL", series.Series.Domain.Sql);
        Assert.DoesNotContain("UNION", pie.Domain.Sql);
    }

    [Theory]
    [InlineData(ChartTypes.Pie)]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Stat)]
    [InlineData(null)]
    public void A_pies_rows_are_the_statement_any_other_query_over_the_same_column_plans(string? chartType)
    {
        var config = Config(filters: [Own]);
        var pie = Of(ChartTypes.Pie, config);
        var other = Of(chartType, config);

        Assert.Equal(other.Sql!.Sql, pie.Sql!.Sql);
        Assert.Equal(Described(other.Sql), Described(pie.Sql));
        Assert.Equal(chartType == ChartTypes.Pie, other.Domain is not null);
    }

    [Fact]
    public void Every_other_kind_plans_no_domain_and_a_pie_with_no_group_plans_none_either()
    {
        var withSeries = new WidgetConfigInput("coverage", GroupBy: "municipality", SeriesBy: "Odd \"name\"", Filters: [Own]);
        var map = new WidgetConfigInput("coverage", GroupBy: "municipality", GeometryColumn: "geometry");
        var pyramid = new WidgetConfigInput("coverage", GroupBy: "municipality", SeriesBy: "Odd \"name\"", Pyramid: new WidgetPyramid("Female", "Male"));

        foreach (var chartType in new[] { ChartTypes.Bar, ChartTypes.Column, ChartTypes.Line, ChartTypes.Heatmap, ChartTypes.Crosstab })
        {
            Assert.Null(Of(chartType, withSeries).Domain);
        }

        Assert.Null(Of(ChartTypes.Pyramid, pyramid).Domain);
        Assert.Null(Of(ChartTypes.Map, map).Domain);
        Assert.Null(Of(ChartTypes.Stat, Config(groupBy: null)).Domain);
        Assert.Null(Of(ChartTypes.Pie, Config(groupBy: null)).Domain);
        Assert.Null(Of(null, Config()).Domain);
        // A pie that names a column the table no longer has plans nothing.
        Assert.Null(Of(ChartTypes.Pie, Config(groupBy: "gone")).Domain);
    }

    // Box plot (design D3 of add-boxplot-widget)

    private static IndicatorQueryPlan BoxPlan(WidgetConfigInput input, int maxRows = 5000, TableColumn[]? columns = null) =>
        IndicatorQueryBuilder.Plan(input.Normalize(), columns ?? Columns, "UTC", From, maxRows, chartType: ChartTypes.Boxplot);

    [Fact]
    public void A_box_plot_is_one_statement_and_neither_a_row_statement_nor_a_series()
    {
        var plan = BoxPlan(Config());

        Assert.Null(plan.Sql);
        Assert.Null(plan.Series);
        Assert.NotNull(plan.Boxes);
        Assert.Empty(plan.MissingColumns);
    }

    [Fact]
    public void A_box_plot_groups_by_the_typed_column_and_takes_no_aggregate_over_it()
    {
        var text = BoxPlan(Config()).Boxes!.Sql;
        var flag = BoxPlan(Config(groupBy: "flag")).Boxes!.Sql;

        Assert.Contains("SELECT \"municipality\" AS g, (\"value\"::double precision) AS x", text);
        Assert.Contains("SELECT \"flag\" AS g,", flag);
        // PostgreSQL has no min(boolean): the group is grouped on, never aggregated.
        Assert.DoesNotContain("min(g)", flag);
        Assert.DoesNotContain("max(g)", flag);
        Assert.Contains("FROM src GROUP BY g", flag);
    }

    [Fact]
    public void A_box_plot_with_no_group_has_one_box()
    {
        var sql = BoxPlan(Config(groupBy: null)).Boxes!.Sql;

        Assert.Contains("SELECT NULL::text AS g,", sql);
    }

    [Fact]
    public void A_box_plot_keeps_only_finite_values_and_takes_each_rows_value_without_summing()
    {
        var sql = BoxPlan(Config(measure: "numerator")).Boxes!.Sql;

        Assert.Contains("(\"numerator\"::double precision) AS x", sql);
        Assert.Contains("> '-Infinity'::double precision", sql);
        Assert.Contains("< 'Infinity'::double precision", sql);
        Assert.Contains("\"numerator\" IS NOT NULL", sql);
        Assert.DoesNotContain("sum(\"numerator\")", sql);
    }

    [Fact]
    public void A_box_plot_ratio_is_each_rows_numerator_over_its_own_denominator()
    {
        var sql = BoxPlan(Config(ratio: new WidgetRatio("numerator", "denominator"))).Boxes!.Sql;

        Assert.Contains("(\"numerator\"::double precision / nullif(\"denominator\", 0)) AS x", sql);
        Assert.DoesNotContain("sum(\"numerator\")", sql);
    }

    [Fact]
    public void A_box_plot_filters_apply_before_the_statistics_and_share_their_parameters()
    {
        var filters = new[] { new DataFilter("municipality", "equals", "Cali") };
        var plan = BoxPlan(Config(filters: filters));
        var sql = plan.Boxes!.Sql;

        Assert.True(sql.IndexOf("WHERE", StringComparison.Ordinal) < sql.IndexOf("percentile_cont", StringComparison.Ordinal));
        Assert.Contains(plan.Boxes.Parameters, p => p.Name == "f0");
        Assert.Contains(plan.Boxes.Parameters, p => p.Name == BoxQueryBuilder.MaxOutliersParameter && Equals(p.Value, WidgetConfig.MaxBoxOutliers));
        Assert.Contains(plan.Boxes.Parameters, p => p.Name == IndicatorSql.LimitParameter && Equals(p.Value, 5001));
    }

    [Fact]
    public void A_box_plot_computes_the_quartiles_the_fences_and_the_whiskers_in_the_statement()
    {
        var sql = BoxPlan(Config()).Boxes!.Sql;

        Assert.Contains("percentile_cont(ARRAY[0.25, 0.5, 0.75]) WITHIN GROUP (ORDER BY x)", sql);
        Assert.Contains("q[1] - 1.5 * (q[3] - q[1]) AS lf", sql);
        Assert.Contains("q[3] + 1.5 * (q[3] - q[1]) AS uf", sql);
        Assert.Contains("sum(x) / count(*) AS mean", sql);
        Assert.DoesNotContain("avg(", sql);
        Assert.Contains("r <= @maxoutliers", sql);
    }

    [Fact]
    public void A_box_plot_joins_without_distinct_from_so_that_every_join_can_hash()
    {
        var sql = BoxPlan(Config()).Boxes!.Sql;

        Assert.DoesNotContain("IS NOT DISTINCT FROM", sql);
        Assert.Contains("JOIN ends e USING (b) LEFT JOIN shown s USING (b)", sql);
        Assert.Contains("WITH src AS MATERIALIZED", sql);
        Assert.Contains("fence AS MATERIALIZED", sql);
        Assert.Contains("marked AS MATERIALIZED", sql);
    }

    [Fact]
    public void A_box_plot_without_an_order_follows_the_group_columns_own_order_and_limits_the_boxes()
    {
        var sql = BoxPlan(Config(), maxRows: 40).Boxes!.Sql;

        Assert.Matches(@"ORDER BY f\.g NULLS LAST\s+LIMIT @limit", sql);
        Assert.Contains(BoxPlan(Config(), maxRows: 40).Boxes!.Parameters, p => p.Name == "limit" && Equals(p.Value, 41));
    }

    [Theory]
    [InlineData(SortValues.Asc, "ASC")]
    [InlineData(SortValues.Desc, "DESC")]
    public void A_box_plot_ordered_by_median_sorts_by_the_second_quartile_then_the_group(string direction, string sql)
    {
        var input = Config() with { Sort = new WidgetSort(SortValues.ByValue, null, null, direction) };

        Assert.Contains($"ORDER BY f.q[2] {sql}, f.g NULLS LAST", BoxPlan(input).Boxes!.Sql);
    }

    [Fact]
    public void A_box_plot_with_no_group_ignores_an_order()
    {
        var input = Config(groupBy: null) with { Sort = new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc) };

        Assert.Contains("ORDER BY f.g NULLS LAST", BoxPlan(input).Boxes!.Sql);
    }

    [Fact]
    public void A_box_plot_over_a_column_the_table_lost_runs_no_statement()
    {
        var columns = Columns.Where(c => c.Name != "municipality").ToArray();
        var plan = BoxPlan(Config(), columns: columns);

        Assert.Null(plan.Boxes);
        Assert.Null(plan.Sql);
        Assert.Equal(["municipality"], plan.MissingColumns);
    }

    [Fact]
    public void A_box_plot_over_a_text_measure_is_an_error()
    {
        var plan = BoxPlan(Config(measure: "municipality"));

        Assert.Null(plan.Boxes);
        Assert.Contains("measure", plan.Errors!.Keys);
    }

    [Fact]
    public void Without_the_box_plot_chart_type_a_query_is_planned_as_before()
    {
        var plan = Plan(Config());

        Assert.NotNull(plan.Sql);
        Assert.Null(plan.Boxes);
    }

    // The overall value (design D4 of add-chart-annotations)

    private static readonly WidgetRatio CoverageRatio = new("numerator", "denominator");

    private static IndicatorQueryPlan Overall(
        WidgetConfigInput input, string? chartType, IReadOnlyList<AppliedDashboardFilter>? dashboardFilters = null, bool overall = true, bool perRow = false) =>
        IndicatorQueryBuilder.Plan(input.Normalize(), Columns, "UTC", From, 5000, perRow, dashboardFilters, chartType, overall);

    /// <summary>A statement as text can be compared: its SQL and each parameter's name, type and value.</summary>
    private static string Statement(IndicatorSql sql) =>
        sql.Sql + "\n" + string.Join("\n", sql.Parameters.Select(p => $"{p.Name}|{p.Type}|{System.Text.Json.JsonSerializer.Serialize(p.Value)}"));

    public static TheoryData<string> OverallChartTypes() => new() { ChartTypes.Bar, ChartTypes.Column, ChartTypes.Line };

    [Theory]
    [MemberData(nameof(OverallChartTypes))]
    public void The_overall_statement_is_the_statement_of_a_stat_tile_over_the_same_config_and_filters(string chartType)
    {
        var input = new WidgetConfigInput(
            "coverage", Ratio: CoverageRatio, GroupBy: "municipality", Filters: [new DataFilter("year", "range", Min: "2025", Max: "2026"), new DataFilter("municipality", "in", Values: ["Cali", "Palmira"])],
            Sort: new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc));
        AppliedDashboardFilter[] dashboard =
        [
            new("municipality", DashboardFilterKinds.Text, Values: ["Cali"]),
            new("year", DashboardFilterKinds.Number, Min: "2025", Max: "2025"),
        ];

        var plan = Overall(input, chartType, dashboard);
        var tile = IndicatorQueryBuilder.Plan(WidgetQuery.For(ChartTypes.Stat, input.Normalize()).Normalize(), Columns, "UTC", From, 5000, dashboardFilters: dashboard, chartType: ChartTypes.Stat);

        Assert.NotNull(plan.Overall);
        Assert.Equal(Statement(tile.Sql!), Statement(plan.Overall));
        Assert.DoesNotContain("GROUP BY", plan.Overall.Sql);
        Assert.DoesNotContain("ORDER BY", plan.Overall.Sql);
        Assert.Contains("HAVING", plan.Overall.Sql);
        Assert.Contains("sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)", plan.Overall.Sql);
        // The rows of the widget are planned as they are without the flag.
        Assert.Equal(Statement(Overall(input, chartType, dashboard, overall: false).Sql!), Statement(plan.Sql!));
    }

    [Fact]
    public void The_overall_statement_of_a_widget_without_filters_has_only_the_row_limit_parameter()
    {
        var plan = Overall(new WidgetConfigInput("coverage", Ratio: CoverageRatio, GroupBy: "municipality"), ChartTypes.Column);

        Assert.Equal(["limit"], plan.Overall!.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void Without_the_flag_there_is_no_overall_statement()
    {
        var plan = Overall(new WidgetConfigInput("coverage", Ratio: CoverageRatio, GroupBy: "municipality"), ChartTypes.Column, overall: false);

        Assert.Null(plan.Overall);
        Assert.NotNull(plan.Sql);
    }

    [Fact]
    public void A_widget_with_a_series_a_box_plot_a_histogram_per_row_and_a_stat_tile_have_no_overall_statement()
    {
        var series = Overall(new WidgetConfigInput("coverage", Ratio: CoverageRatio, GroupBy: "municipality", SeriesBy: "Odd \"name\""), ChartTypes.Column);
        var box = Overall(new WidgetConfigInput("coverage", Measure: "value", GroupBy: "municipality"), ChartTypes.Boxplot);
        var histogram = Overall(new WidgetConfigInput("coverage", Measure: "value"), ChartTypes.Histogram, perRow: true);
        var tile = Overall(new WidgetConfigInput("coverage", Ratio: CoverageRatio), ChartTypes.Stat);
        var tileWithAGroup = Overall(new WidgetConfigInput("coverage", Ratio: CoverageRatio, GroupBy: "municipality"), ChartTypes.Stat);

        Assert.NotNull(series.Series);
        Assert.Null(series.Overall);
        Assert.NotNull(box.Boxes);
        Assert.Null(box.Overall);
        Assert.NotNull(histogram.Sql);
        Assert.Null(histogram.Overall);
        Assert.NotNull(tile.Sql);
        Assert.Null(tile.Overall);
        Assert.Null(tileWithAGroup.Overall);
    }

    [Fact]
    public void A_pie_a_map_and_a_query_with_no_chart_type_answer_with_no_overall_whatever_the_request_asks()
    {
        var grouped = new WidgetConfigInput("coverage", Ratio: CoverageRatio, GroupBy: "municipality");
        var pie = Overall(grouped, ChartTypes.Pie);
        var map = Overall(grouped with { GeometryColumn = "geometry" }, ChartTypes.Map);
        var untyped = Overall(grouped, null);
        var bar = Overall(grouped, ChartTypes.Bar);

        Assert.NotNull(pie.Sql);
        Assert.Null(pie.Overall);
        Assert.NotNull(map.Sql);
        Assert.Null(map.Overall);
        Assert.NotNull(untyped.Sql);
        Assert.Null(untyped.Overall);
        // The three kinds that draw a line still get it.
        Assert.NotNull(bar.Overall);
    }

    [Fact]
    public void An_overall_statement_does_not_change_the_rows_statement_or_the_pie_domain()
    {
        var input = new WidgetConfigInput("coverage", Ratio: CoverageRatio, GroupBy: "municipality");

        var pie = Overall(input, ChartTypes.Pie);

        Assert.NotNull(pie.Domain);
        Assert.Equal(Statement(Overall(input, ChartTypes.Pie, overall: false).Domain!), Statement(pie.Domain));
        Assert.Equal(Statement(Overall(input, ChartTypes.Pie, overall: false).Sql!), Statement(pie.Sql!));
    }

    [Fact]
    public void The_overall_statement_of_a_measure_is_the_sum_of_every_row()
    {
        var plan = Overall(new WidgetConfigInput("coverage", Measure: "value", GroupBy: "municipality"), ChartTypes.Bar);

        Assert.Contains("sum(\"value\")::double precision", plan.Overall!.Sql);
        Assert.DoesNotContain("GROUP BY", plan.Overall.Sql);
    }

    [Fact]
    public void A_widget_that_names_a_missing_column_has_no_overall_statement()
    {
        var plan = IndicatorQueryBuilder.Plan(
            new WidgetConfigInput("coverage", Ratio: CoverageRatio, GroupBy: "gone").Normalize(), Columns, "UTC", From, 5000, chartType: ChartTypes.Column, overall: true);

        Assert.Null(plan.Sql);
        Assert.Null(plan.Overall);
        Assert.Equal(["gone"], plan.MissingColumns);
    }

    // The base n: figures appended to the select list, the widget's n window, and the answers counted once (design D5 of add-small-sample-warnings)

    private static readonly TableColumn[] BaseColumns =
    [
        new("variable", DataColumnType.Text, null),
        new("orden", DataColumnType.Text, null),
        new("categoria", DataColumnType.Text, null),
        new("comuna", DataColumnType.Text, null),
        new("hogares", DataColumnType.Integer, null),
        new("value", DataColumnType.Decimal, null),
        new("numerator", DataColumnType.Decimal, null),
        new("denominator", DataColumnType.Decimal, null),
        new("geometry", DataColumnType.Text, null),
    ];

    private static IndicatorBases Declared(string? baseColumn = null, string[]? answers = null) =>
        new(baseColumn, answers ?? [], new Dictionary<string, IReadOnlyList<string>>());

    private static IndicatorQueryPlan Planned(
        WidgetConfigInput input, IndicatorBases? bases = null, string? chartType = null, TableColumn[]? columns = null, bool perRow = false,
        IReadOnlyList<AppliedDashboardFilter>? dashboardFilters = null) =>
        IndicatorQueryBuilder.Plan(
            input.Normalize(), columns ?? BaseColumns, "UTC", From, 5000, perRow, dashboardFilters, chartType, bases: bases);

    private static WidgetConfigInput RatioBy(string? groupBy = "comuna") =>
        new("coverage", Ratio: new WidgetRatio("numerator", "denominator"), GroupBy: groupBy);

    private const string RatioAggregate = "sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)";

    [Fact]
    public void A_ratios_statement_is_todays_text_with_the_two_sums_appended_to_its_select_list()
    {
        var plan = Planned(RatioBy(), chartType: ChartTypes.Column);

        Assert.StartsWith(
            $"SELECT \"comuna\"::text, ({RatioAggregate}), NULL::text, sum(\"numerator\")::double precision, sum(\"denominator\")::double precision\nFROM {From}\n",
            plan.Sql!.Sql);
        Assert.Equal(new SqlFigures(3, 4, null, null), plan.Sql.Figures);
        Assert.Null(plan.Counted);
        // What follows the select list is what it was: the same groups, order, limit and parameters.
        var without = Planned(RatioBy() with { Ratio = null, Measure = "value" }, chartType: ChartTypes.Column);
        Assert.Null(without.Sql!.Figures);
    }

    [Fact]
    public void A_ratios_figures_leave_every_other_clause_as_it_was()
    {
        var plan = Planned(RatioBy(), chartType: ChartTypes.Column);
        var before = $"""
            SELECT "comuna"::text, ({RatioAggregate}), NULL::text
            FROM {From}
            WHERE ("numerator" IS NOT NULL OR "denominator" IS NOT NULL)
            GROUP BY "comuna"
            HAVING ({RatioAggregate}) IS NOT NULL
            ORDER BY {From}."comuna" NULLS LAST
            LIMIT @limit
            """;

        Assert.Equal(before, plan.Sql!.Sql.Replace(", sum(\"numerator\")::double precision, sum(\"denominator\")::double precision", ""));
    }

    [Fact]
    public void A_base_column_adds_its_sum_and_a_measure_with_one_has_only_that()
    {
        var ratio = Planned(RatioBy(), Declared("hogares"), ChartTypes.Column);
        var measure = Planned(new WidgetConfigInput("coverage", Measure: "value", GroupBy: "comuna"), Declared("hogares"), ChartTypes.Column);

        Assert.Contains("NULL::text, sum(\"numerator\")::double precision, sum(\"denominator\")::double precision, sum(\"hogares\")::double precision,", ratio.Sql!.Sql);
        Assert.Equal(new SqlFigures(3, 4, 5, 6), ratio.Sql.Figures);
        Assert.Contains("NULL::text, sum(\"hogares\")::double precision, (sum(sum(\"hogares\")) OVER ())::double precision\nFROM", measure.Sql!.Sql);
        Assert.Equal(new SqlFigures(null, null, 3, 4), measure.Sql.Figures);
    }

    [Fact]
    public void The_widget_n_window_appears_only_with_a_base_column_and_no_answer_column()
    {
        const string window = "(sum(sum(\"hogares\")) OVER ())::double precision";
        var withBase = Planned(RatioBy(), Declared("hogares"), ChartTypes.Column);
        var nothing = Planned(RatioBy(), null, ChartTypes.Column);
        var answersOnly = Planned(RatioBy(), Declared(answers: ["categoria"]), ChartTypes.Column);
        var both = Planned(RatioBy(), Declared("hogares", ["categoria"]), ChartTypes.Column);

        Assert.Contains(window, withBase.Sql!.Sql);
        Assert.DoesNotContain("OVER", nothing.Sql!.Sql);
        Assert.DoesNotContain("OVER", answersOnly.Sql!.Sql);
        Assert.DoesNotContain("OVER", both.Sql!.Sql);
        Assert.Null(withBase.Counted);
        // The window sees every plotted group: it runs after HAVING and before LIMIT.
        Assert.True(withBase.Sql.Sql.IndexOf("OVER", StringComparison.Ordinal) < withBase.Sql.Sql.IndexOf("HAVING", StringComparison.Ordinal));
    }

    [Fact]
    public void A_histograms_statement_and_a_box_plots_are_todays_text_with_everything_declared()
    {
        var everything = Declared("hogares", ["categoria"]);
        var histogram = new WidgetConfigInput("coverage", Measure: "value");
        var box = new WidgetConfigInput("coverage", Measure: "value", GroupBy: "comuna");

        var rows = Planned(histogram, everything, ChartTypes.Histogram, perRow: true);
        var grouped = Planned(histogram with { GroupBy = "comuna", Ratio = new WidgetRatio("numerator", "denominator"), Measure = null }, everything, ChartTypes.Histogram);

        Assert.Equal(Planned(histogram, null, ChartTypes.Histogram, perRow: true).Sql!.Sql, rows.Sql!.Sql);
        Assert.Null(rows.Sql.Figures);
        Assert.Null(grouped.Sql!.Figures);
        Assert.Null(grouped.Counted);
        Assert.Equal(Planned(box, null, ChartTypes.Boxplot).Boxes!.Sql, Planned(box, everything, ChartTypes.Boxplot).Boxes!.Sql);
        Assert.Null(Planned(box, everything, ChartTypes.Boxplot).Boxes!.Figures);
    }

    [Fact]
    public void A_maps_count_and_tooltip_outputs_keep_their_ordinals_and_the_figures_follow_them()
    {
        var map = (RatioBy() with { GeometryColumn = "geometry", Tooltip = [new WidgetTooltipItem("hogares")] });

        var plan = Planned(map, Declared("hogares"), ChartTypes.Map);

        // group, value, geometry, count(*), the tooltip column, then the figures.
        Assert.StartsWith(
            $"SELECT \"comuna\"::text, ({RatioAggregate}), min(\"geometry\"::text), count(*), sum(\"hogares\")::text, sum(\"numerator\")::double precision,",
            plan.Sql!.Sql);
        Assert.Equal(new SqlFigures(5, 6, 7, 8), plan.Sql.Figures);
        Assert.Single(plan.Sql.TooltipTypes!);
    }

    [Fact]
    public void A_stat_tile_takes_the_same_outputs_and_a_pie_and_a_series_free_widget_too()
    {
        var tile = Planned(RatioBy(groupBy: null), Declared("hogares"), ChartTypes.Stat);
        var pie = Planned(RatioBy(), null, ChartTypes.Pie);

        Assert.Contains("NULL::text, sum(\"numerator\")::double precision, sum(\"denominator\")::double precision, sum(\"hogares\")::double precision,", tile.Sql!.Sql);
        Assert.Equal(new SqlFigures(3, 4, 5, 6), tile.Sql.Figures);
        Assert.Equal(new SqlFigures(3, 4, null, null), pie.Sql!.Figures);
        // The pie's domain statement is not changed by the figures.
        Assert.DoesNotContain("numerator\")::double precision, sum", pie.Domain!.Sql);
    }

    [Fact]
    public void An_indicator_without_metadata_plans_as_before()
    {
        var measure = new WidgetConfigInput("coverage", Measure: "value", GroupBy: "comuna");

        Assert.Equal(Planned(measure).Sql!.Sql, Planned(measure, IndicatorBases.Resolve(null, BaseColumns)).Sql!.Sql);
        Assert.Null(IndicatorBases.Resolve(new Dictionary<string, IndicatorColumnMetadata> { ["comuna"] = new("Comuna") }, BaseColumns));
        Assert.Null(Planned(measure).Sql!.Figures);
        Assert.Null(Planned(measure).Counted);
    }

    [Fact]
    public void The_declarations_that_apply_are_resolved_against_the_published_columns()
    {
        var metadata = new Dictionary<string, IndicatorColumnMetadata>
        {
            ["hogares"] = new(Base: true),
            ["categoria"] = new(Categories: "multiple"),
            ["variable"] = new(Categories: "single", Missing: ["Sin dato"]),
            ["comuna"] = new(Missing: ["Sin dato", "No sabe"]),
            ["gone"] = new(Categories: "single", Missing: ["x"]),
        };

        var bases = IndicatorBases.Resolve(metadata, BaseColumns)!;

        Assert.Equal("hogares", bases.BaseColumn);
        Assert.Equal(["categoria", "variable"], bases.Answers);
        Assert.Equal(["Sin dato", "No sabe"], bases.Missing["comuna"]);
        Assert.Equal(["Sin dato"], bases.Missing["variable"]);
        Assert.False(bases.Missing.ContainsKey("gone"));
    }

    [Fact]
    public void A_base_held_as_text_and_answers_held_as_numbers_are_ignored()
    {
        var columns = BaseColumns.Select(c => c.Name switch
        {
            "hogares" => c with { Type = DataColumnType.Text },
            "orden" => c with { Type = DataColumnType.Integer },
            _ => c,
        }).ToArray();
        var metadata = new Dictionary<string, IndicatorColumnMetadata>
        {
            ["hogares"] = new(Base: true),
            ["orden"] = new(Categories: "single", Missing: ["1"]),
            ["value"] = new(Categories: "single"),
        };

        Assert.Null(IndicatorBases.Resolve(metadata, columns));
    }

    [Fact]
    public void A_column_that_is_true_or_false_can_hold_answers_and_no_data_values()
    {
        var columns = new TableColumn[] { new("flag", DataColumnType.Boolean, null), new("value", DataColumnType.Decimal, null) };

        var bases = IndicatorBases.Resolve(new Dictionary<string, IndicatorColumnMetadata> { ["flag"] = new(Categories: "single", Missing: ["false"]) }, columns)!;

        Assert.Equal(["flag"], bases.Answers);
        Assert.Equal(["false"], bases.Missing["flag"]);
    }

    private static string CountedOf(IndicatorQueryPlan plan) => plan.Counted!.Sql;

    [Fact]
    public void The_counted_statement_groups_by_every_declared_column_and_keeps_the_plotted_where_and_parameters()
    {
        var filter = new DataFilter("variable", "equals", Value: "Frecuencia (E2)");
        var input = RatioBy() with { Filters = [filter] };
        var dashboard = new[] { new AppliedDashboardFilter("comuna", "text", Values: ["COMUNA 1"]) };
        var plan = Planned(input, Declared("hogares", ["variable", "orden", "categoria"]), ChartTypes.Column, dashboardFilters: dashboard);

        Assert.NotNull(plan.Counted);
        var sql = CountedOf(plan);
        Assert.DoesNotContain("IS NOT DISTINCT FROM", sql);
        // The WHERE of the plotted statement, whole: the row filter, the widget's filters and the dashboard's.
        var plotted = plan.Sql!.Sql;
        var where = plotted[(plotted.IndexOf("WHERE ", StringComparison.Ordinal) + 6)..plotted.IndexOf("\nGROUP BY", StringComparison.Ordinal)];
        Assert.Contains("@f0", where);
        Assert.Contains("@d0", where);
        Assert.Equal(
            $"""
            SELECT 1 AS part, g, max(s) FROM (SELECT "comuna"::text AS g, "variable", "orden", "categoria", sum("hogares")::double precision AS s FROM {From} WHERE {where} GROUP BY 1, 2, 3, 4) x GROUP BY g
            UNION ALL
            SELECT 2, NULL::text, max(s) FROM (SELECT "variable", "orden", "categoria", sum("hogares")::double precision AS s FROM {From} WHERE {where} GROUP BY 1, 2, 3) y
            """,
            sql);
        // The plotted statement's filter parameters, and not its limit.
        Assert.Equal(plan.Sql!.Parameters.Where(p => p.Name != IndicatorSql.LimitParameter).Select(p => p.Name), plan.Counted!.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void The_counted_statement_sums_the_denominator_when_there_is_no_base_column_and_is_one_part_for_a_tile()
    {
        var plan = Planned(RatioBy(), Declared(answers: ["categoria"]), ChartTypes.Column);
        var tile = Planned(RatioBy(groupBy: null), Declared(answers: ["categoria"]), ChartTypes.Stat);

        Assert.Contains("sum(\"denominator\")::double precision AS s", CountedOf(plan));
        Assert.StartsWith("SELECT 2, NULL::text, max(s) FROM (SELECT \"categoria\", sum(\"denominator\")", CountedOf(tile));
        Assert.DoesNotContain("part", CountedOf(tile));
    }

    [Fact]
    public void A_measure_with_no_base_column_has_no_n_so_nothing_is_counted()
    {
        var measure = new WidgetConfigInput("coverage", Measure: "value", GroupBy: "comuna");

        Assert.Null(Planned(measure, Declared(answers: ["categoria"]), ChartTypes.Column).Counted);
        Assert.Null(Planned(measure, Declared(answers: ["categoria"]), ChartTypes.Column).Sql!.Figures);
        Assert.NotNull(Planned(measure, Declared("hogares", ["categoria"]), ChartTypes.Column).Counted);
    }

    [Fact]
    public void Nothing_is_counted_without_an_answer_column()
    {
        Assert.Null(Planned(RatioBy(), Declared("hogares"), ChartTypes.Column).Counted);
        Assert.Null(Planned(RatioBy(), null, ChartTypes.Column).Counted);
    }

    // The values that mean no data are left out of every plotted statement (design D6 of add-small-sample-warnings)

    private const string SinDatoLeft = "(\"comuna\" IS NOT NULL AND \"comuna\"::text = ANY(@ng))";

    private static IndicatorBases Missing(params (string Column, string[] Values)[] declared) =>
        new(null, [], declared.ToDictionary(d => d.Column, d => (IReadOnlyList<string>)d.Values, StringComparer.Ordinal));

    private static readonly IndicatorBases SinDato = Missing(("comuna", ["Sin dato"]));

    private static WidgetConfigInput Leaving(WidgetConfigInput input) => input with { MissingFooter = true };

    private static string[] Statements(IndicatorQueryPlan plan) =>
        new[] { plan.Sql, plan.Domain, plan.Overall, plan.Counted, plan.NoData?.Sql }.OfType<IndicatorSql>().Select(Statement).ToArray();

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Pie)]
    public void Without_the_option_every_statement_is_the_text_it_was_whatever_the_indicator_declares(string chartType)
    {
        var input = RatioBy();
        var declared = new IndicatorBases(null, ["categoria"], new Dictionary<string, IReadOnlyList<string>> { ["comuna"] = ["Sin dato"], ["categoria"] = ["No sabe"] });

        var plain = IndicatorQueryBuilder.Plan(input.Normalize(), BaseColumns, "UTC", From, 5000, chartType: chartType, overall: chartType != ChartTypes.Pie, bases: declared);
        var none = IndicatorQueryBuilder.Plan(input.Normalize(), BaseColumns, "UTC", From, 5000, chartType: chartType, overall: chartType != ChartTypes.Pie, bases: new IndicatorBases(null, ["categoria"], new Dictionary<string, IReadOnlyList<string>>()));

        Assert.Equal(Statements(none), Statements(plain));
        Assert.DoesNotContain("@ng", string.Concat(Statements(plain)));
        Assert.Null(plain.NoData);
    }

    [Fact]
    public void With_the_option_and_declared_values_the_plotted_statement_leaves_the_rows_out_and_keeps_a_null_group()
    {
        var plan = Planned(Leaving(RatioBy()), SinDato, ChartTypes.Column);
        var without = Planned(RatioBy(), SinDato, ChartTypes.Column);

        var left = $"AND NOT ({SinDatoLeft})";
        Assert.Contains($"WHERE (\"numerator\" IS NOT NULL OR \"denominator\" IS NOT NULL) {left}\n", plan.Sql!.Sql);
        // The condition is the only difference, and it keeps a missing group: "(none)" is not a declared value.
        Assert.Equal(without.Sql!.Sql.Replace("OR \"denominator\" IS NOT NULL)\n", $"OR \"denominator\" IS NOT NULL) {left}\n"), plan.Sql.Sql);
        Assert.Contains("\"comuna\" IS NOT NULL AND", plan.Sql.Sql);
        var parameter = Assert.Single(plan.Sql.Parameters, p => p.Name == "ng");
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Text, parameter.Type);
        Assert.Equal(["Sin dato"], (string[])parameter.Value);
        Assert.DoesNotContain(plan.Sql.Parameters, p => p.Name == "ns");
    }

    [Fact]
    public void The_option_with_nothing_declared_for_the_columns_changes_nothing()
    {
        var nothing = Missing(("categoria", ["No sabe"]));

        Assert.Equal(Statements(Planned(RatioBy(), nothing, ChartTypes.Column)), Statements(Planned(Leaving(RatioBy()), nothing, ChartTypes.Column)));
        Assert.Equal(Statements(Planned(RatioBy(), null, ChartTypes.Column)), Statements(Planned(Leaving(RatioBy()), null, ChartTypes.Column)));
        Assert.Null(Planned(Leaving(RatioBy()), nothing, ChartTypes.Column).NoData);
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Histogram)]
    [InlineData(ChartTypes.Map)]
    [InlineData(ChartTypes.Boxplot)]
    public void The_kinds_that_do_not_take_the_option_are_planned_as_they_were(string chartType)
    {
        var input = RatioBy() with { GeometryColumn = chartType == ChartTypes.Map ? "geometry" : null };

        Assert.Equal(
            Statements(Planned(input, SinDato, chartType)),
            Statements(Planned(Leaving(input), SinDato, chartType)));
    }

    [Fact]
    public void A_pies_domain_changes_only_with_the_option()
    {
        var without = Planned(RatioBy(), SinDato, ChartTypes.Pie).Domain!;
        var with = Planned(Leaving(RatioBy()), SinDato, ChartTypes.Pie).Domain!;

        Assert.DoesNotContain("@ng", without.Sql);
        Assert.Contains($"AND NOT ({SinDatoLeft})", with.Sql);
        Assert.Equal(without.Sql.Replace("\nGROUP BY", $" AND NOT ({SinDatoLeft})\nGROUP BY"), with.Sql);
        Assert.Contains(with.Parameters, p => p.Name == "ng");
        Assert.Contains(with.Parameters, p => p is { Name: SeriesQueryBuilder.CapParameter });
    }

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    [InlineData(ChartTypes.Line)]
    public void The_overall_statement_carries_the_same_condition_as_the_plotted_one(string chartType)
    {
        var plan = IndicatorQueryBuilder.Plan(
            Leaving(RatioBy()).Normalize(), BaseColumns, "UTC", From, 5000, chartType: chartType, overall: true, bases: SinDato);

        Assert.Contains($"AND NOT ({SinDatoLeft})", plan.Sql!.Sql);
        Assert.Contains($"AND NOT ({SinDatoLeft})", plan.Overall!.Sql);
        Assert.DoesNotContain("GROUP BY", plan.Overall.Sql);
        Assert.Contains(plan.Overall.Parameters, p => p.Name == "ng");
        // A widget that does not leave them out has the overall statement it had.
        var plain = IndicatorQueryBuilder.Plan(RatioBy().Normalize(), BaseColumns, "UTC", From, 5000, chartType: chartType, overall: true, bases: SinDato);
        Assert.DoesNotContain("@ng", plain.Overall!.Sql);
    }

    [Fact]
    public void The_counted_statement_reads_the_same_rows_as_the_plotted_one()
    {
        var plan = Planned(Leaving(RatioBy()), new IndicatorBases(null, ["categoria"], new Dictionary<string, IReadOnlyList<string>> { ["comuna"] = ["Sin dato"] }), ChartTypes.Column);

        Assert.Contains($"AND NOT ({SinDatoLeft})", plan.Counted!.Sql);
        Assert.Contains(plan.Counted.Parameters, p => p.Name == "ng");
    }

    [Fact]
    public void A_series_leaves_out_both_columns_values_with_one_array_each()
    {
        var input = Leaving(RatioBy() with { SeriesBy = "categoria" });
        var bases = Missing(("comuna", ["Sin dato"]), ("categoria", ["No sabe", "NS/NR"]));

        var plan = Planned(input, bases, ChartTypes.Column);

        var left = "(\"comuna\" IS NOT NULL AND \"comuna\"::text = ANY(@ng)) OR (\"categoria\" IS NOT NULL AND \"categoria\"::text = ANY(@ns))";
        Assert.Contains($"AND NOT ({left})", plan.Series!.Domain!.Sql);
        var cells = plan.Series.Cells(["Sí"]);
        Assert.Contains($"AND NOT ({left})", cells.Sql);
        Assert.Equal(["No sabe", "NS/NR"], (string[])cells.Parameters.Single(p => p.Name == "ns").Value);
        Assert.Contains(plan.Series.Domain.Parameters, p => p.Name == "ns");
    }

    [Fact]
    public void The_no_data_statement_reads_the_rows_without_the_condition_and_runs_only_for_the_option()
    {
        var ratio = Planned(Leaving(RatioBy()), SinDato, ChartTypes.Column).NoData!;
        var measure = Planned(Leaving(new WidgetConfigInput("coverage", Measure: "value", GroupBy: "comuna")), SinDato, ChartTypes.Column).NoData!;

        Assert.True(ratio.IsRatio);
        Assert.False(measure.IsRatio);
        Assert.DoesNotContain("NOT (", ratio.Sql.Sql);
        Assert.Contains($"array_agg(DISTINCT \"comuna\"::text) FILTER (WHERE {SinDatoLeft[1..^1]})", ratio.Sql.Sql);
        Assert.Contains($"(sum(\"numerator\") FILTER (WHERE {SinDatoLeft}))::double precision", ratio.Sql.Sql);
        Assert.Contains("(sum(\"denominator\") FILTER", ratio.Sql.Sql);
        Assert.Contains($"(sum(\"value\") FILTER (WHERE {SinDatoLeft}))::double precision", measure.Sql.Sql);
        Assert.Contains("sum(\"value\")::double precision", measure.Sql.Sql);
        Assert.Contains(ratio.Sql.Parameters, p => p.Name == "ng");
        Assert.Null(Planned(RatioBy(), SinDato, ChartTypes.Column).NoData);
    }

    [Fact]
    public void The_no_data_statement_counts_the_n_of_the_left_out_rows_per_answer_when_answers_are_declared()
    {
        var bases = new IndicatorBases("hogares", ["categoria"], new Dictionary<string, IReadOnlyList<string>> { ["comuna"] = ["Sin dato"] });

        var statement = Planned(Leaving(RatioBy()), bases, ChartTypes.Column).NoData!.Sql.Sql;

        Assert.Contains($"(SELECT max(s) FROM (SELECT \"categoria\", sum(\"hogares\")::double precision AS s FROM {From} WHERE", statement);
        Assert.Contains($"AND ({SinDatoLeft}) GROUP BY 1) y)", statement);
        var plain = Planned(Leaving(RatioBy()), Missing(("comuna", ["Sin dato"])), ChartTypes.Column).NoData!.Sql.Sql;
        Assert.Contains("(sum(\"denominator\") FILTER", plain.Split('\n').Last(line => line.Contains("FILTER")));
    }

    // A stat tile's statistic and its value without the dashboard filters (design D2, D3 and D10 of improve-stat-cards)

    private static IndicatorQueryPlan Tile(
        WidgetConfigInput input, IndicatorBases? bases = null, IReadOnlyList<AppliedDashboardFilter>? dashboardFilters = null, bool unfiltered = false,
        string? chartType = ChartTypes.Stat) =>
        IndicatorQueryBuilder.Plan(input.Normalize(), BaseColumns, "UTC", From, 5000, false, dashboardFilters, chartType, false, bases, null, unfiltered);

    private static WidgetConfigInput Median(string statistic = "median", string? measure = "value", WidgetRatio? ratio = null, string? groupBy = null, IReadOnlyList<DataFilter>? filters = null) =>
        new("coverage", ratio is null ? measure : null, ratio, groupBy, filters, Statistic: statistic);


    [Theory]
    [InlineData("median", "percentile_cont(0.5) WITHIN GROUP (ORDER BY x)")]
    [InlineData("p25", "percentile_cont(0.25) WITHIN GROUP (ORDER BY x)")]
    [InlineData("p75", "percentile_cont(0.75) WITHIN GROUP (ORDER BY x)")]
    [InlineData("mean", "sum(x) / count(*)")]
    public void A_statistic_is_one_row_over_the_finite_values_of_the_rows(string statistic, string expected)
    {
        var plan = Tile(Median(statistic));

        Assert.Null(plan.Errors);
        var sql = plan.Sql!.Sql;
        Assert.StartsWith($"SELECT NULL::text, {expected}, NULL::text, count(*)::double precision", sql);
        Assert.Contains("FROM (SELECT (\"value\"::double precision) AS x FROM", sql);
        Assert.Contains("WHERE \"value\" IS NOT NULL AND (\"value\"::double precision) > '-Infinity'::double precision AND (\"value\"::double precision) < 'Infinity'::double precision) v", sql);
        Assert.EndsWith("HAVING count(*) > 0", sql.Trim());
        Assert.DoesNotContain("GROUP BY", sql);
        Assert.DoesNotContain("LIMIT", sql);
        Assert.DoesNotContain("avg(", sql);
        Assert.Empty(plan.Sql.Parameters);
        Assert.Equal(new SqlFigures(null, null, 3, 3), plan.Sql.Figures);
        Assert.Null(plan.Counted);
        Assert.Null(plan.Unfiltered);
    }

    [Fact]
    public void A_statistic_of_a_ratio_takes_each_rows_own_ratio_and_leaves_out_a_zero_denominator()
    {
        var sql = Tile(Median(ratio: CoverageRatio)).Sql!.Sql;

        Assert.Contains("(\"numerator\"::double precision / nullif(\"denominator\", 0)) AS x", sql);
        Assert.Contains("WHERE (\"numerator\" IS NOT NULL OR \"denominator\" IS NOT NULL) AND (\"numerator\"::double precision / nullif(\"denominator\", 0)) > '-Infinity'::double precision", sql);
        Assert.DoesNotContain("sum(\"numerator\")", sql);
        Assert.DoesNotContain("sum(\"denominator\")", sql);
    }

    [Fact]
    public void A_statistic_returns_the_value_and_the_n_and_never_the_sums_of_a_ratio()
    {
        var plan = Tile(Median(ratio: CoverageRatio), Declared(answers: ["categoria"]));

        Assert.Null(plan.Sql!.Figures!.Numerator);
        Assert.Null(plan.Sql.Figures.Denominator);
        Assert.Equal(3, plan.Sql.Figures.Base);
        Assert.Equal(3, plan.Sql.Figures.WidgetN);
        Assert.Contains("count(*)::double precision", plan.Sql.Sql);
        Assert.Null(plan.Counted);
    }

    [Fact]
    public void The_n_of_a_statistic_is_the_sum_of_the_base_column_over_the_rows_it_keeps_when_the_indicator_declares_one()
    {
        var sql = Tile(Median(), Declared("hogares")).Sql!.Sql;

        Assert.StartsWith("SELECT NULL::text, percentile_cont(0.5) WITHIN GROUP (ORDER BY x), NULL::text, sum(b)::double precision", sql);
        Assert.Contains("FROM (SELECT (\"value\"::double precision) AS x, \"hogares\"::double precision AS b FROM", sql);
        Assert.DoesNotContain("count(*)::double precision", sql);
        Assert.Equal(new SqlFigures(null, null, 3, 3), Tile(Median(), Declared("hogares")).Sql!.Figures);
    }

    [Fact]
    public void A_stray_group_column_never_groups_a_statistic_and_the_filters_apply_before_it()
    {
        var plan = Tile(Median(groupBy: "comuna", filters: [new DataFilter("variable", "equals", Value: "Asistió (E1)")]));

        var sql = plan.Sql!.Sql;
        Assert.DoesNotContain("GROUP BY", sql);
        Assert.StartsWith("SELECT NULL::text, percentile_cont(0.5)", sql);
        Assert.Contains("AND \"variable\"::text = @f0) v", sql.Replace("\n", " "));
        Assert.Equal(["f0"], plan.Sql.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void A_dashboard_filter_narrows_a_statistic_beside_the_widget_filters()
    {
        AppliedDashboardFilter[] dashboard = [new("comuna", DashboardFilterKinds.Text, Values: ["COMUNA 1"])];

        var plan = Tile(Median(filters: [new DataFilter("variable", "equals", Value: "Pagó")]), dashboardFilters: dashboard);

        Assert.Contains("\"variable\"::text = @f0 AND \"comuna\"::text = ANY(@d0)) v", plan.Sql!.Sql.Replace("\n", " "));
        Assert.Equal(["f0", "d0"], plan.Sql.Parameters.Select(p => p.Name));
        Assert.Empty(plan.NotFiltered!);
    }

    [Fact]
    public void A_dashboard_filter_the_table_lacks_is_listed_and_narrows_nothing_in_a_statistic()
    {
        var plan = Tile(Median(), dashboardFilters: [new("estrato", DashboardFilterKinds.Text, Values: ["Estrato 1"])]);

        Assert.Equal(["estrato"], plan.NotFiltered);
        Assert.DoesNotContain("estrato", plan.Sql!.Sql);
    }

    [Fact]
    public void A_statistic_is_planned_only_for_a_stat_tile_and_a_tile_without_one_is_planned_as_before()
    {
        var withStatistic = Tile(Median());
        var asBar = Tile(Median(groupBy: "comuna"), chartType: ChartTypes.Bar);
        var plain = Tile(new WidgetConfigInput("coverage"));

        Assert.Contains("percentile_cont", withStatistic.Sql!.Sql);
        Assert.DoesNotContain("percentile_cont", asBar.Sql!.Sql);
        Assert.Contains("GROUP BY \"comuna\"", asBar.Sql.Sql);
        Assert.Contains("SELECT NULL::text, (sum(\"value\")::double precision), NULL::text", plain.Sql!.Sql);
        Assert.Contains("LIMIT @limit", plain.Sql.Sql);
    }

    [Fact]
    public void The_overall_statement_of_a_chart_never_takes_a_statistic()
    {
        var plan = IndicatorQueryBuilder.Plan(
            (Median(groupBy: "comuna") with { Ratio = CoverageRatio, Measure = null }).Normalize(), BaseColumns, "UTC", From, 5000, false, null, ChartTypes.Column, true);

        Assert.DoesNotContain("percentile_cont", plan.Overall!.Sql);
        Assert.Contains("sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)", plan.Overall.Sql);
    }

    // The value without the dashboard filters

    private static readonly AppliedDashboardFilter[] ByComuna = [new("comuna", DashboardFilterKinds.Text, Values: ["COMUNA 1"])];

    [Fact]
    public void The_unfiltered_statement_is_the_tile_without_the_dashboard_filters_and_the_tile_itself_is_unchanged()
    {
        var input = new WidgetConfigInput("coverage", Ratio: CoverageRatio, Filters: [new DataFilter("variable", "equals", Value: "Asistió (E1)")]);

        var plan = Tile(input, dashboardFilters: ByComuna, unfiltered: true);
        var alone = Tile(input);
        var withFilter = Tile(input, dashboardFilters: ByComuna);

        Assert.NotNull(plan.Unfiltered);
        Assert.Equal(Statement(alone.Sql!), Statement(plan.Unfiltered));
        Assert.DoesNotContain("@d0", plan.Unfiltered.Sql);
        Assert.Contains("\"variable\"::text = @f0", plan.Unfiltered.Sql);
        Assert.Equal(Statement(withFilter.Sql!), Statement(plan.Sql!));
        Assert.Contains("@d0", plan.Sql!.Sql);
        Assert.Null(withFilter.Unfiltered);
    }

    [Fact]
    public void The_unfiltered_statement_of_a_statistic_is_the_statistic_without_the_dashboard_filters()
    {
        var plan = Tile(Median(), dashboardFilters: ByComuna, unfiltered: true);

        Assert.Equal(Statement(Tile(Median()).Sql!), Statement(plan.Unfiltered!));
        Assert.Contains("percentile_cont(0.5)", plan.Unfiltered!.Sql);
        Assert.DoesNotContain("@d0", plan.Unfiltered.Sql);
        Assert.Contains("@d0", plan.Sql!.Sql);
    }

    [Fact]
    public void The_unfiltered_statement_is_planned_only_while_a_dashboard_filter_narrows_the_tile()
    {
        var input = new WidgetConfigInput("coverage", Measure: "value");

        Assert.Null(Tile(input, unfiltered: true).Unfiltered);
        Assert.Null(Tile(input, dashboardFilters: [], unfiltered: true).Unfiltered);
        // A filter the table lacks, or has with another kind of type, narrows nothing.
        Assert.Null(Tile(input, dashboardFilters: [new("estrato", DashboardFilterKinds.Text, Values: ["Estrato 1"])], unfiltered: true).Unfiltered);
        Assert.Null(Tile(input, dashboardFilters: [new("comuna", DashboardFilterKinds.Number, Min: "1", Max: "2")], unfiltered: true).Unfiltered);
        Assert.NotNull(Tile(input, dashboardFilters: ByComuna, unfiltered: true).Unfiltered);
    }

    [Fact]
    public void Without_the_flag_or_for_another_kind_no_unfiltered_statement_is_planned()
    {
        var input = new WidgetConfigInput("coverage", Measure: "value", GroupBy: "comuna");

        Assert.Null(Tile(input, dashboardFilters: ByComuna).Unfiltered);
        Assert.Null(Tile(input, dashboardFilters: ByComuna, unfiltered: true, chartType: ChartTypes.Bar).Unfiltered);
        Assert.Null(Tile(input, dashboardFilters: ByComuna, unfiltered: true, chartType: null).Unfiltered);
        Assert.Null(Tile(Median(), dashboardFilters: ByComuna).Unfiltered);
    }
}
