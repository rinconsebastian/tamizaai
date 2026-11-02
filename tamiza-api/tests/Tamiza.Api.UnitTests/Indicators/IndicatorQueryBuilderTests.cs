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
}
