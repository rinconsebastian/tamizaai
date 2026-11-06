using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// A ratio over the answer columns an indicator declares (design D4 of improve-stat-cards): the SQL of the nested form for single choice,
/// of the refusal and the null totals for multiple choice, and the statements that stay what they were.
/// </summary>
public sealed class AnswerColumnsTests
{
    private const string From = "\"p_x_indicators\".\"cruces\"";

    private static readonly TableColumn[] Table =
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

    private static IndicatorBases Declared(string kind, params string[] columns) =>
        new(null, columns.Order(StringComparer.Ordinal).ToList(), new Dictionary<string, IReadOnlyList<string>>())
        {
            Kinds = columns.ToDictionary(column => column, _ => kind, StringComparer.Ordinal),
        };

    private static WidgetConfigInput Ratio(string? groupBy = "comuna", string? seriesBy = null, WidgetSort? sort = null) =>
        new("cruces", Ratio: new WidgetRatio("numerator", "denominator"), GroupBy: groupBy, SeriesBy: seriesBy, Sort: sort);

    private static WidgetConfigInput Measure(string? groupBy = "comuna", string? seriesBy = null) =>
        new("cruces", Measure: "value", GroupBy: groupBy, SeriesBy: seriesBy);

    private static IndicatorQueryPlan Planned(WidgetConfigInput input, IndicatorBases? bases, string chartType, bool overall = false, bool unfiltered = false) =>
        IndicatorQueryBuilder.Plan(
            input.Normalize(), Table, "UTC", From, 5000, dashboardFilters: unfiltered ? [new AppliedDashboardFilter("hogares", "number", Min: "1")] : null,
            chartType: chartType, overall: overall, bases: bases, unfiltered: unfiltered);

    private static string Statement(IndicatorSql sql) =>
        sql.Sql + "\n" + string.Join("\n", sql.Parameters.Select(p => $"{p.Name}|{p.Type}|{System.Text.Json.JsonSerializer.Serialize(p.Value)}"));

    private static string[] Statements(IndicatorQueryPlan plan)
    {
        var series = plan.Series is { } built ? new[] { built.Domain, built.Cells(["Sí", "No"]) } : [];
        return new[] { plan.Sql, plan.Domain, plan.Overall, plan.Counted, plan.Unfiltered, plan.NoData?.Sql, plan.Boxes, plan.Combined?.Sql }
            .Concat(series).OfType<IndicatorSql>().Select(Statement).ToArray();
    }

    // What stays what it was

    [Theory]
    [InlineData("single", ChartTypes.Bar, "comuna", null)]
    [InlineData("single", ChartTypes.Column, "comuna", "categoria")]
    [InlineData("multiple", ChartTypes.Column, "comuna", "categoria")]
    [InlineData("multiple", ChartTypes.Heatmap, "categoria", "comuna")]
    [InlineData("multiple", ChartTypes.Pie, "categoria", null)]
    [InlineData("single", ChartTypes.Stat, null, null)]
    [InlineData("multiple", ChartTypes.Stat, null, null)]
    [InlineData("multiple", ChartTypes.Map, "comuna", null)]
    [InlineData("single", ChartTypes.Boxplot, "comuna", null)]
    [InlineData("multiple", ChartTypes.Histogram, null, null)]
    public void A_measure_plans_as_it_always_did_whatever_the_indicator_declares(string kind, string chartType, string? groupBy, string? seriesBy)
    {
        var input = Measure(groupBy, seriesBy);

        var declared = Planned(input, Declared(kind, "variable", "orden", "categoria"), chartType, overall: chartType == ChartTypes.Bar);
        var plain = Planned(input, null, chartType, overall: chartType == ChartTypes.Bar);

        Assert.Equal(Statements(plain), Statements(declared));
        Assert.Null(declared.Combined);
    }

    [Theory]
    [InlineData(ChartTypes.Bar, "comuna", null)]
    [InlineData(ChartTypes.Column, "comuna", "categoria")]
    [InlineData(ChartTypes.Stat, null, null)]
    [InlineData(ChartTypes.Pie, "categoria", null)]
    public void A_ratio_over_answers_whose_kinds_are_not_given_plans_as_it_always_did(string chartType, string? groupBy, string? seriesBy)
    {
        var withoutKinds = new IndicatorBases(null, ["categoria"], new Dictionary<string, IReadOnlyList<string>>());

        var declared = Planned(Ratio(groupBy, seriesBy), withoutKinds, chartType);

        Assert.DoesNotContain("__num", string.Concat(Statements(declared)));
        Assert.DoesNotContain("count(DISTINCT", string.Concat(Statements(declared).Where(s => !s.Contains("SELECT 1 AS n", StringComparison.Ordinal))));
        Assert.Null(declared.Combined);
    }

    [Fact]
    public void A_ratio_over_no_declared_answer_column_plans_as_it_always_did()
    {
        var others = new IndicatorBases("hogares", [], new Dictionary<string, IReadOnlyList<string>>()) { Kinds = new Dictionary<string, string>() };

        Assert.Equal(Statements(Planned(Ratio(), null, ChartTypes.Column)).Length, Statements(Planned(Ratio(), others, ChartTypes.Column)).Length);
        Assert.DoesNotContain("__num", string.Concat(Statements(Planned(Ratio(), others, ChartTypes.Column))));
    }

    [Fact]
    public void Mixed_kinds_are_taken_as_multiple_choice_and_all_single_ones_as_single_choice()
    {
        var mixed = new IndicatorBases(null, ["categoria", "orden"], new Dictionary<string, IReadOnlyList<string>>())
        {
            Kinds = new Dictionary<string, string> { ["categoria"] = AnswerKinds.Single, ["orden"] = AnswerKinds.Multiple },
        };
        var single = Declared(AnswerKinds.Single, "categoria", "orden");

        Assert.Empty(mixed.SingleAnswers);
        Assert.Equal(["categoria", "orden"], mixed.MultipleAnswers);
        Assert.Equal(["categoria", "orden"], single.SingleAnswers);
        Assert.Empty(single.MultipleAnswers);
    }

    [Fact]
    public void Resolve_reads_the_kind_of_each_answer_column()
    {
        var metadata = ColumnMetadata.Read("""{"categoria":{"categories":"single"},"variable":{"categories":"multiple"},"comuna":{"label":"Comuna"}}""");

        var bases = IndicatorBases.Resolve(metadata, Table)!;

        Assert.Equal(["categoria", "variable"], bases.Answers);
        Assert.Equal(AnswerKinds.Single, bases.Kinds!["categoria"]);
        Assert.Equal(AnswerKinds.Multiple, bases.Kinds["variable"]);
        Assert.Equal(["categoria", "variable"], bases.MultipleAnswers);
    }

    // Single choice: the nested form

    [Fact]
    public void A_grouped_ratio_over_single_choice_sums_each_comuna_and_answer_and_divides_by_the_largest_denominator()
    {
        var plan = Planned(Ratio(), Declared(AnswerKinds.Single, "categoria"), ChartTypes.Column);

        Assert.Equal(
            """
            SELECT "__g"::text, (sum("__num")::double precision / nullif(max("__den"), 0)), NULL::text, sum("__num")::double precision, max("__den")::double precision
            FROM (SELECT "comuna" AS "__g", "categoria" AS "__a0", sum("numerator") AS "__num", sum("denominator") AS "__den", count(*) AS "__n"
                  FROM "p_x_indicators"."cruces"
                  WHERE ("numerator" IS NOT NULL OR "denominator" IS NOT NULL)
                  GROUP BY "comuna", "categoria") a
            GROUP BY "__g"
            HAVING (sum("__num")::double precision / nullif(max("__den"), 0)) IS NOT NULL
            ORDER BY "__g" NULLS LAST
            LIMIT @limit
            """, plan.Sql!.Sql);
        Assert.NotNull(plan.Counted);
        Assert.Equal(new SqlFigures(3, 4, null, null), plan.Sql.Figures);
    }

    [Fact]
    public void A_tile_over_single_choice_has_no_group_and_one_inner_row_per_answer()
    {
        var plan = Planned(Ratio(null), Declared(AnswerKinds.Single, "variable", "categoria"), ChartTypes.Stat);

        Assert.Equal(
            """
            SELECT NULL::text, (sum("__num")::double precision / nullif(max("__den"), 0)), NULL::text, sum("__num")::double precision, max("__den")::double precision
            FROM (SELECT "categoria" AS "__a0", "variable" AS "__a1", sum("numerator") AS "__num", sum("denominator") AS "__den", count(*) AS "__n"
                  FROM "p_x_indicators"."cruces"
                  WHERE ("numerator" IS NOT NULL OR "denominator" IS NOT NULL)
                  GROUP BY "categoria", "variable") a

            HAVING (sum("__num")::double precision / nullif(max("__den"), 0)) IS NOT NULL

            LIMIT @limit
            """, plan.Sql!.Sql);
    }

    [Fact]
    public void A_widget_grouped_by_an_answer_column_does_not_group_by_it_twice()
    {
        var plan = Planned(Ratio("categoria"), Declared(AnswerKinds.Single, "variable", "categoria"), ChartTypes.Bar);

        Assert.Contains("SELECT \"categoria\" AS \"__g\", \"variable\" AS \"__a0\", sum(", plan.Sql!.Sql);
        Assert.Contains("GROUP BY \"categoria\", \"variable\") a", plan.Sql.Sql);
    }

    [Fact]
    public void A_sort_by_value_orders_by_the_corrected_value_and_a_sort_by_a_column_by_the_one_the_inner_statement_carries()
    {
        var byValue = Planned(Ratio(sort: new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc)), Declared(AnswerKinds.Single, "categoria"), ChartTypes.Bar);
        var byColumn = Planned(
            Ratio("categoria", sort: new WidgetSort(SortValues.ByColumn, "orden", SortValues.Number, SortValues.Asc)),
            Declared(AnswerKinds.Single, "categoria"), ChartTypes.Bar);

        Assert.Contains("ORDER BY (sum(\"__num\")::double precision / nullif(max(\"__den\"), 0)) DESC NULLS LAST, \"__g\" NULLS LAST", byValue.Sql!.Sql);
        Assert.Contains("AS \"__sort\"", byColumn.Sql!.Sql);
        Assert.Contains("ORDER BY min(\"__sort\") ASC NULLS LAST, \"__g\" NULLS LAST", byColumn.Sql.Sql);
    }

    [Fact]
    public void A_map_carries_its_geometry_its_row_count_and_its_tooltip_columns_through_the_inner_statement()
    {
        var input = Ratio() with
        {
            GeometryColumn = "geometry",
            Tooltip = [new WidgetTooltipItem("hogares"), new WidgetTooltipItem("variable")],
        };

        var plan = Planned(input, Declared(AnswerKinds.Single, "categoria"), ChartTypes.Map);

        var sql = plan.Sql!.Sql;
        Assert.Contains("min(\"__geom\")", sql);
        Assert.Contains("sum(\"__n\")::bigint", sql);
        Assert.Contains("sum(\"__t0\")::text", sql);
        Assert.Contains("left(min(NULLIF(\"__t1\"::text, '')), ", sql);
        Assert.Contains("sum(\"hogares\") AS \"__t0\"", sql);
        Assert.Contains("min(NULLIF(\"variable\"::text, '')) AS \"__t1\"", sql);
        Assert.Contains("min(\"geometry\"::text) AS \"__geom\"", sql);
        Assert.Equal([DataColumnType.Integer, DataColumnType.Text], plan.Sql.TooltipTypes);
    }

    [Fact]
    public void A_series_widget_over_single_choice_reads_every_branch_through_the_nested_form()
    {
        var plan = Planned(Ratio("comuna", "categoria"), Declared(AnswerKinds.Single, "variable"), ChartTypes.Column);

        var sql = plan.Series!.Cells(["a", "b"]).Sql;
        Assert.Contains("FROM (SELECT \"comuna\" AS g, \"variable\" AS \"__a0\", sum(\"numerator\") AS \"__num\"", sql);
        Assert.Contains("FROM (SELECT \"__kind\" AS k, \"__series\" AS s, \"variable\" AS \"__a0\", sum(", sql);
        Assert.Contains("GROUP BY \"__kind\", \"__series\", \"variable\") a GROUP BY k, s", sql);
        Assert.Contains("FROM (SELECT \"variable\" AS \"__a0\", sum(\"numerator\") AS \"__num\"", sql);
        Assert.Equal(4, sql.Split("max(\"__den\")::double precision", StringSplitOptions.None).Length - 1);
        // The domain that ranks the series values stays on today's aggregate.
        Assert.Contains("sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)", plan.Series.Domain!.Sql);
        Assert.DoesNotContain("__num", plan.Series.Domain.Sql);
    }

    [Fact]
    public void The_overall_statement_of_a_line_over_single_choice_takes_the_nested_form_without_the_other_declarations()
    {
        var plan = Planned(Ratio(), Declared(AnswerKinds.Single, "categoria"), ChartTypes.Bar, overall: true);

        Assert.Contains("FROM (SELECT \"categoria\" AS \"__a0\", sum(\"numerator\") AS \"__num\"", plan.Overall!.Sql);
        Assert.DoesNotContain("\"__g\"", plan.Overall.Sql);
    }

    // Multiple choice: the check and the null totals

    [Fact]
    public void A_grouped_ratio_over_multiple_choice_keeps_its_statement_and_adds_the_check()
    {
        var multiple = Planned(Ratio(), Declared(AnswerKinds.Multiple, "variable", "categoria"), ChartTypes.Column);
        var plain = Planned(Ratio(), null, ChartTypes.Column);

        // The figure outputs apart, which the ratio adds to both, the statement is the one of an undeclared indicator.
        Assert.Equal(plain.Sql!.Sql, multiple.Sql!.Sql);
        Assert.Equal(
            "SELECT count(DISTINCT \"variable\"), count(DISTINCT \"categoria\") FROM \"p_x_indicators\".\"cruces\" "
            + "WHERE (\"numerator\" IS NOT NULL OR \"denominator\" IS NOT NULL) GROUP BY \"comuna\" HAVING count(DISTINCT (\"variable\", \"categoria\")) > 1 LIMIT 1",
            multiple.Combined!.Sql.Sql);
        Assert.Equal(["variable", "categoria"], multiple.Combined.Columns);
        Assert.Empty(multiple.Combined.Sql.Parameters);
    }

    [Fact]
    public void The_columns_of_the_check_follow_the_tables_order_and_a_tie_names_the_last()
    {
        var plan = Planned(Ratio(), Declared(AnswerKinds.Multiple, "categoria", "variable", "orden"), ChartTypes.Bar);

        Assert.Equal(["variable", "orden", "categoria"], plan.Combined!.Columns);
        Assert.Equal("categoria", plan.Combined.Named([2, 2, 2]));
        Assert.Equal("orden", plan.Combined.Named([1, 3, 2]));
        Assert.Equal("variable", plan.Combined.Named([5, 3, 4]));
    }

    [Fact]
    public void A_tile_over_multiple_choice_has_no_value_when_its_rows_hold_several_answers_and_the_check_has_no_group()
    {
        var plan = Planned(Ratio(null), Declared(AnswerKinds.Multiple, "categoria"), ChartTypes.Stat);

        Assert.Contains(
            "(CASE WHEN count(DISTINCT (\"categoria\")) > 1 THEN NULL ELSE sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0) END)",
            plan.Sql!.Sql);
        Assert.Contains("HAVING (sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)) IS NOT NULL", plan.Sql.Sql);
        Assert.DoesNotContain("GROUP BY", plan.Combined!.Sql.Sql);
    }

    [Fact]
    public void The_overall_and_the_unfiltered_statements_over_multiple_choice_have_no_value_over_several_answers()
    {
        var overall = Planned(Ratio(), Declared(AnswerKinds.Multiple, "categoria"), ChartTypes.Bar, overall: true);
        var unfiltered = Planned(Ratio(null), Declared(AnswerKinds.Multiple, "categoria"), ChartTypes.Stat, unfiltered: true);

        Assert.Contains("CASE WHEN count(DISTINCT (\"categoria\")) > 1 THEN NULL ELSE", overall.Overall!.Sql);
        Assert.Contains("CASE WHEN count(DISTINCT (\"categoria\")) > 1 THEN NULL ELSE", unfiltered.Unfiltered!.Sql);
        Assert.DoesNotContain("@d0", unfiltered.Unfiltered.Sql);
    }

    [Fact]
    public void A_series_widget_over_multiple_choice_nulls_its_totals_and_leaves_out_a_cell_that_spans_answers_but_keeps_its_having_and_its_rank()
    {
        var plan = Planned(Ratio("comuna", "categoria", new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc)), Declared(AnswerKinds.Multiple, "categoria"), ChartTypes.Column);

        var sql = plan.Series!.Cells(["a"]).Sql;
        const string Aggregate = "sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)";
        const string Spans = "CASE WHEN count(DISTINCT (\"categoria\")) > 1 THEN NULL ELSE " + Aggregate + " END";
        // The group totals, the series totals and the grand total; the cells and the HAVING of the groups and the cells keep the plain aggregate.
        Assert.Equal(3, sql.Split(Spans, StringSplitOptions.None).Length - 1);
        Assert.Contains($"HAVING ({Aggregate}) IS NOT NULL\n", sql);
        Assert.Contains($"HAVING ({Aggregate}) IS NOT NULL AND count(DISTINCT (\"categoria\")) <= 1", sql);
        Assert.Contains($"row_number() OVER (ORDER BY ({Aggregate}) DESC NULLS LAST", sql);
        Assert.Equal("SELECT count(DISTINCT \"categoria\") FROM \"p_x_indicators\".\"cruces\" WHERE (\"numerator\" IS NOT NULL OR \"denominator\" IS NOT NULL) GROUP BY \"comuna\", \"categoria\" HAVING count(DISTINCT (\"categoria\")) > 1 LIMIT 1", plan.Combined!.Sql.Sql);
        Assert.DoesNotContain("__num", sql);
    }

    [Fact]
    public void The_check_of_a_pyramid_reads_only_the_rows_the_pyramid_plots()
    {
        var input = Ratio("comuna", "categoria") with { Pyramid = new WidgetPyramid("Sí", "No") };

        var plan = Planned(input, Declared(AnswerKinds.Multiple, "variable"), ChartTypes.Pyramid);

        var check = plan.Combined!.Sql;
        Assert.Contains("AND \"comuna\" IS NOT NULL AND \"categoria\" IS NOT NULL AND \"categoria\"::text = ANY(@kept)", check.Sql);
        Assert.Contains(check.Parameters, p => p is { Name: SeriesQueryBuilder.KeptParameter } && p.Type == (NpgsqlDbType.Array | NpgsqlDbType.Text));
    }
}
