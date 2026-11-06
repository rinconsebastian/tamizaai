using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class SeriesQueryBuilderTests
{
    private static readonly Dictionary<string, TableColumn> Columns = new[]
    {
        new TableColumn("municipality", DataColumnType.Text, null), new TableColumn("water_source", DataColumnType.Text, null),
        new TableColumn("sex", DataColumnType.Text, null), new TableColumn("value", DataColumnType.Decimal, null),
    }.ToDictionary(c => c.Name, StringComparer.Ordinal);

    private const string Aggregate = "sum(\"value\")::double precision";
    private const string RowFilter = "\"value\" IS NOT NULL";
    private const string From = "\"p_x_indicators\".\"households\"";

    private static readonly DataQuery Own = new() { Where = "\"water_source\" <> @f0", Parameters = [new SqlParam("f0", NpgsqlDbType.Text, "Rain")] };
    private static readonly DataQuery Dashboard = new() { Where = "\"sex\" = @d0", Parameters = [new SqlParam("d0", NpgsqlDbType.Text, "Female")] };

    private static SeriesQueryPlan Build(WidgetConfig config, string? chartType) =>
        SeriesQueryBuilder.Build(config, chartType, Columns, From, Aggregate, RowFilter, Own, Dashboard, maxRows: 5000);

    private static WidgetConfig Config(WidgetPyramid? pyramid = null, IReadOnlyList<LikertStep>? scale = null, WidgetSort? sort = null) =>
        new WidgetConfigInput("households", GroupBy: "municipality", SeriesBy: "water_source", Pyramid: pyramid, Scale: scale, Sort: sort).Normalize();

    [Fact]
    public void The_series_domain_is_read_through_the_widgets_own_filters_and_never_the_dashboards()
    {
        var plan = Build(Config(), ChartTypes.Column);

        Assert.NotNull(plan.Domain);
        Assert.Contains("@f0", plan.Domain!.Sql);
        Assert.DoesNotContain("@d0", plan.Domain.Sql);
        Assert.DoesNotContain(plan.Domain.Parameters, p => p.Name == "d0");
        Assert.Contains(plan.Domain.Parameters, p => p is { Name: SeriesQueryBuilder.CapParameter, Value: 9 });
    }

    [Fact]
    public void The_series_domain_statement_is_the_text_it_was_before_a_pie_shared_it()
    {
        var domain = Build(Config(), ChartTypes.Column).Domain!;

        // Pinned as the build before pie domains wrote it (design D8 of dashboard palettes): the statement a pie now builds with
        // SeriesQueryBuilder.Domain is this one, so a change here changes the series and the pie alike.
        Assert.Equal(
            """
            (SELECT "water_source"::text, (sum("value")::double precision) FROM "p_x_indicators"."households"
             WHERE "value" IS NOT NULL AND "water_source" <> @f0 AND "water_source" IS NOT NULL
             GROUP BY "water_source" ORDER BY 2 DESC NULLS LAST, 1 LIMIT @cap)
            UNION ALL
            (SELECT NULL::text, (sum("value")::double precision) FROM "p_x_indicators"."households"
             WHERE "value" IS NOT NULL AND "water_source" <> @f0 AND "water_source" IS NULL
             HAVING count(*) > 0)
            """,
            domain.Sql);
        Assert.Equal(["f0", "cap"], domain.Parameters.Select(p => p.Name));
        Assert.Equal(domain.Sql, SeriesQueryBuilder.Domain("\"water_source\"", From, Aggregate, RowFilter, Own, 9).Sql);
    }

    [Fact]
    public void The_cells_are_read_through_both_sets_of_filters_and_bind_the_kept_series_and_the_limit()
    {
        var sql = Build(Config(), ChartTypes.Column).Cells(["Piped", "Well"]);

        Assert.Contains("@f0", sql.Sql);
        Assert.Contains("@d0", sql.Sql);
        Assert.Equal(["Piped", "Well"], (string[])sql.Parameters.Single(p => p.Name == SeriesQueryBuilder.KeptParameter).Value);
        Assert.Contains(sql.Parameters, p => p is { Name: IndicatorSql.LimitParameter, Value: 5001 });
        Assert.Contains(sql.Parameters, p => p.Name == "f0");
        Assert.Contains(sql.Parameters, p => p.Name == "d0");
    }

    [Fact]
    public void A_parameter_both_sets_use_is_bound_once()
    {
        var own = new DataQuery { Where = "x = @tz", Parameters = [new SqlParam("tz", NpgsqlDbType.Text, "UTC")] };
        var dashboard = new DataQuery { Where = "y = @tz", Parameters = [new SqlParam("tz", NpgsqlDbType.Text, "UTC")] };

        var sql = SeriesQueryBuilder.Build(Config(), ChartTypes.Column, Columns, From, Aggregate, RowFilter, own, dashboard, 10).Cells([]);

        Assert.Single(sql.Parameters, p => p.Name == "tz");
    }

    [Theory]
    [InlineData(ChartTypes.Bar, 8)]
    [InlineData(ChartTypes.Column, 8)]
    [InlineData(ChartTypes.Line, 8)]
    [InlineData(ChartTypes.Heatmap, 30)]
    [InlineData(ChartTypes.Crosstab, 30)]
    public void Each_chart_has_room_for_its_number_of_series(string chartType, int series) =>
        Assert.Equal(series, Build(Config(), chartType).MaxSeries);

    [Fact]
    public void Only_a_bar_and_a_column_keep_the_group_columns_own_order_and_every_other_kind_lists_its_groups_in_natural_order()
    {
        var scale = Config(scale: [new("A", LikertSides.Negative), new("B", LikertSides.Positive)]);
        var pyramid = Config(pyramid: new("Female", "Male"));

        foreach (var chartType in new[] { ChartTypes.Bar, ChartTypes.Column })
        {
            Assert.False(Build(Config(), chartType).NaturalGroups, chartType);
        }

        foreach (var chartType in new[] { ChartTypes.Line, ChartTypes.Heatmap, ChartTypes.Crosstab })
        {
            Assert.True(Build(Config(), chartType).NaturalGroups, chartType);
        }

        Assert.True(Build(pyramid, ChartTypes.Pyramid).NaturalGroups);
        Assert.True(Build(scale, ChartTypes.Likert).NaturalGroups);
    }

    [Fact]
    public void A_pyramid_and_a_likert_widget_have_a_fixed_domain_and_read_no_domain_statement()
    {
        var pyramid = Build(Config(pyramid: new("Female", "Male")), ChartTypes.Pyramid);
        var likert = Build(Config(scale: [new("No", LikertSides.Negative), new("Neutral", LikertSides.Neutral), new("Yes", LikertSides.Positive)]), ChartTypes.Likert);

        Assert.Equal(["Female", "Male"], pyramid.FixedDomain);
        Assert.Null(pyramid.Domain);
        Assert.Equal(["No", "Neutral", "Yes"], likert.FixedDomain);
        Assert.Null(likert.Domain);
        Assert.Null(Build(Config(), ChartTypes.Column).FixedDomain);
    }

    [Fact]
    public void A_fixed_domain_counts_the_rows_it_leaves_out_and_the_other_kinds_do_not()
    {
        var pyramid = Build(Config(pyramid: new("Female", "Male")), ChartTypes.Pyramid).Cells(["Female", "Male"]);
        var column = Build(Config(), ChartTypes.Column).Cells(["Piped"]);

        Assert.Contains($"SELECT {SeriesParts.LeftOut}", pyramid.Sql);
        Assert.DoesNotContain($"SELECT {SeriesParts.LeftOut}", column.Sql);
        Assert.Contains("'other'", column.Sql);
        Assert.DoesNotContain("'other'", pyramid.Sql);
    }

    [Fact]
    public void The_groups_are_ranked_by_the_widgets_order_over_the_source_rows()
    {
        var byValue = Build(Config(sort: new WidgetSort(SortValues.ByValue, null, null, SortValues.Desc)), ChartTypes.Bar).Cells([]);
        var plain = Build(Config(), ChartTypes.Bar).Cells([]);

        Assert.Contains("row_number() OVER (ORDER BY (sum(\"value\")::double precision) DESC NULLS LAST", byValue.Sql);
        Assert.Contains("row_number() OVER (ORDER BY src.\"municipality\" NULLS LAST)", plain.Sql);
    }

    [Fact]
    public void Values_are_never_written_into_the_sql()
    {
        var plan = Build(Config(pyramid: new("Fem'ale; DROP TABLE x", "Male")), ChartTypes.Pyramid);

        Assert.DoesNotContain("DROP TABLE", plan.Cells(["Fem'ale; DROP TABLE x"]).Sql);
        Assert.DoesNotContain("Rain", plan.Cells([]).Sql);
    }

    // The base n: figures appended to every branch of the cells statement, and the counted statement (design D5 of add-small-sample-warnings)

    private static readonly Dictionary<string, TableColumn> BaseColumns = new[]
    {
        new TableColumn("comuna", DataColumnType.Text, null), new TableColumn("categoria", DataColumnType.Text, null),
        new TableColumn("variable", DataColumnType.Text, null), new TableColumn("hogares", DataColumnType.Integer, null),
        new TableColumn("numerator", DataColumnType.Decimal, null), new TableColumn("denominator", DataColumnType.Decimal, null),
        new TableColumn("value", DataColumnType.Decimal, null),
    }.ToDictionary(c => c.Name, StringComparer.Ordinal);

    private const string RatioAggregate = "sum(\"numerator\")::double precision / nullif(sum(\"denominator\"), 0)";

    private static WidgetConfig RatioConfig() =>
        new WidgetConfigInput("teatro", Ratio: new WidgetRatio("numerator", "denominator"), GroupBy: "comuna", SeriesBy: "categoria").Normalize();

    private static SeriesQueryPlan BuildBase(string? chartType, WidgetConfig config, IndicatorBases? bases, string rowFilter = "(\"numerator\" IS NOT NULL OR \"denominator\" IS NOT NULL)") =>
        SeriesQueryBuilder.Build(config, chartType, BaseColumns, From, RatioAggregate, rowFilter, Own, Dashboard, maxRows: 5000, bases);

    /// <summary>How many columns each branch of a cells statement selects: the top-level commas of its select list, counted outside quotes and brackets.</summary>
    private static List<int> ColumnsOfEveryBranch(string sql)
    {
        var counts = new List<int>();
        var at = 0;
        while ((at = sql.IndexOf("(SELECT ", at, StringComparison.Ordinal)) >= 0)
        {
            // A branch starts a line: the CTE bodies' own selects are indented inside their parentheses.
            if (at > 0 && sql[at - 1] != '\n')
            {
                at += 8;
                continue;
            }

            var depth = 0;
            var columns = 1;
            var i = at + 8;
            for (; i < sql.Length; i++)
            {
                var c = sql[i];
                if (c == '"')
                {
                    i = sql.IndexOf('"', i + 1);
                }
                else if (c == '\'')
                {
                    i = sql.IndexOf('\'', i + 1);
                }
                else if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    columns++;
                }
                else if (depth == 0 && (string.CompareOrdinal(sql, i, " FROM ", 0, 6) == 0 || string.CompareOrdinal(sql, i, "\nFROM ", 0, 6) == 0 || string.CompareOrdinal(sql, i, "\n FROM ", 0, 7) == 0))
                {
                    break;
                }
            }

            counts.Add(columns);
            at = i;
        }

        return counts;
    }

    [Fact]
    public void Every_branch_of_a_ratios_cells_statement_selects_the_same_columns()
    {
        var fixedDomain = BuildBase(ChartTypes.Likert, new WidgetConfigInput("teatro", Ratio: new WidgetRatio("numerator", "denominator"), GroupBy: "comuna", SeriesBy: "categoria",
            Scale: [new LikertStep("Sí", "positive"), new LikertStep("No", "negative")]).Normalize(), null).Cells(["Sí", "No"]).Sql;
        var open = BuildBase(ChartTypes.Column, RatioConfig(), null).Cells(["Sí"]).Sql;

        Assert.Equal([9, 9, 9, 9, 9], ColumnsOfEveryBranch(fixedDomain));
        Assert.Equal([9, 9, 9, 9], ColumnsOfEveryBranch(open));
    }

    [Fact]
    public void A_ratios_cells_statement_adds_both_sums_after_its_seven_columns_in_every_branch()
    {
        var sql = BuildBase(ChartTypes.Column, RatioConfig(), null).Cells(["Sí"]);

        Assert.Equal(new SqlFigures(7, 8, null, null), sql.Figures);
        Assert.Contains("row_number() OVER (", sql.Sql);
        Assert.Contains(", sum(\"numerator\")::double precision AS fn, sum(\"denominator\")::double precision AS fd\n", sql.Sql);
        Assert.Contains("grp.rk, cell.fn, cell.fd\n", sql.Sql);
        Assert.Contains("NULL::bigint, sum(\"numerator\")::double precision, sum(\"denominator\")::double precision\n FROM src GROUP BY", sql.Sql);
        Assert.Contains("NULL::bigint, sum(\"numerator\")::double precision, sum(\"denominator\")::double precision FROM src)", sql.Sql);
    }

    [Fact]
    public void A_base_column_adds_its_sum_to_every_branch_and_the_left_out_branch_takes_nulls()
    {
        var config = new WidgetConfigInput("teatro", Ratio: new WidgetRatio("numerator", "denominator"), GroupBy: "comuna", SeriesBy: "categoria",
            Pyramid: new WidgetPyramid("Sí", "No")).Normalize();

        var sql = BuildBase(ChartTypes.Pyramid, config, new IndicatorBases("hogares", [], new Dictionary<string, IReadOnlyList<string>>())).Cells(["Sí", "No"]);

        Assert.Equal(new SqlFigures(7, 8, 9, null), sql.Figures);
        Assert.Contains("AS fn, sum(\"denominator\")::double precision AS fd, sum(\"hogares\")::double precision AS fb", sql.Sql);
        Assert.Contains("NULL::bigint, NULL::double precision, NULL::double precision, NULL::double precision\n FROM", sql.Sql);
        Assert.Equal([10, 10, 10, 10, 10], ColumnsOfEveryBranch(sql.Sql));
    }

    [Fact]
    public void A_measure_with_no_base_column_has_the_cells_statement_it_had_before()
    {
        var measure = new WidgetConfigInput("households", Measure: "value", GroupBy: "comuna", SeriesBy: "categoria").Normalize();

        var without = SeriesQueryBuilder.Build(measure, ChartTypes.Column, BaseColumns, From, Aggregate, "\"value\" IS NOT NULL", Own, Dashboard, maxRows: 5000);
        var declared = SeriesQueryBuilder.Build(
            measure, ChartTypes.Column, BaseColumns, From, Aggregate, "\"value\" IS NOT NULL", Own, Dashboard, 5000,
            new IndicatorBases(null, ["categoria"], new Dictionary<string, IReadOnlyList<string>>()));

        Assert.Equal(without.Cells(["Sí"]).Sql, declared.Cells(["Sí"]).Sql);
        Assert.Null(without.Cells(["Sí"]).Figures);
        Assert.DoesNotContain("fn", without.Cells(["Sí"]).Sql);
        Assert.Equal([7, 7, 7, 7], ColumnsOfEveryBranch(without.Cells(["Sí"]).Sql));
        Assert.Null(declared.Counted);
    }

    [Fact]
    public void The_counted_statement_reads_the_cells_statements_rows_and_groups_by_every_declared_column()
    {
        var plan = BuildBase(ChartTypes.Column, RatioConfig(), new IndicatorBases("hogares", ["variable", "categoria"], new Dictionary<string, IReadOnlyList<string>>()));

        var counted = plan.Counted!(["Sí", "No"]);
        var cells = plan.Cells(["Sí", "No"]);

        Assert.DoesNotContain("IS NOT DISTINCT FROM", counted.Sql);
        Assert.Contains($"SELECT {From}.*", counted.Sql);
        Assert.Contains("WHERE (\"numerator\" IS NOT NULL OR \"denominator\" IS NOT NULL) AND \"water_source\" <> @f0 AND \"sex\" = @d0", counted.Sql);
        Assert.Contains("GROUP BY 1, 2, 3) x GROUP BY g", counted.Sql); // a group: its text and the two declared columns
        Assert.Contains("GROUP BY 1, 2, 3, 4, 5) x GROUP BY g, k, sv", counted.Sql); // a cell: group, kind, series text and the two columns
        Assert.Contains("GROUP BY 1, 2, 3, 4) x GROUP BY k, sv", counted.Sql); // a series: kind, series text and the two columns
        Assert.Contains("FROM src GROUP BY 1, 2) x", counted.Sql); // the whole widget
        Assert.Equal(cells.Parameters.Where(p => p.Name != IndicatorSql.LimitParameter).Select(p => p.Name), counted.Parameters.Select(p => p.Name));
        Assert.Equal(["Sí", "No"], (string[])counted.Parameters.Single(p => p.Name == SeriesQueryBuilder.KeptParameter).Value);
    }

    [Fact]
    public void No_counted_statement_is_built_without_answer_columns()
    {
        Assert.Null(BuildBase(ChartTypes.Column, RatioConfig(), null).Counted);
        Assert.Null(BuildBase(ChartTypes.Column, RatioConfig(), new IndicatorBases("hogares", [], new Dictionary<string, IReadOnlyList<string>>())).Counted);
    }

    // The values that mean no data are left out of every statement of a series (design D6 of add-small-sample-warnings)

    private static string Quote(string name) => "\"" + name + "\"";

    private static NoDataExclusion? LeavingOut(WidgetConfig config, string chartType) => NoDataExclusion.For(
        config with { MissingFooter = true }, chartType, new IndicatorBases(null, [], new Dictionary<string, IReadOnlyList<string>>
        {
            ["municipality"] = ["Sin dato"], ["water_source"] = ["No sabe"],
        }), Quote);

    private const string LeftBoth =
        "(\"municipality\" IS NOT NULL AND \"municipality\"::text = ANY(@ng)) OR (\"water_source\" IS NOT NULL AND \"water_source\"::text = ANY(@ns))";

    [Fact]
    public void Without_an_exclusion_the_statements_are_the_text_they_were()
    {
        var config = Config();
        var plain = SeriesQueryBuilder.Build(config, ChartTypes.Column, Columns, From, Aggregate, RowFilter, Own, Dashboard, maxRows: 5000);
        var none = SeriesQueryBuilder.Build(config, ChartTypes.Column, Columns, From, Aggregate, RowFilter, Own, Dashboard, 5000, null, null);

        Assert.Equal(plain.Domain!.Sql, none.Domain!.Sql);
        Assert.Equal(plain.Cells(["Piped"]).Sql, none.Cells(["Piped"]).Sql);
        Assert.DoesNotContain("@ng", plain.Domain.Sql + plain.Cells(["Piped"]).Sql);
        Assert.Null(LeavingOut(config with { MissingFooter = null }, ChartTypes.Stat));
    }

    [Fact]
    public void An_exclusion_is_in_the_domain_both_branches_the_cells_and_the_totals_and_binds_both_arrays()
    {
        var config = Config();
        var plan = SeriesQueryBuilder.Build(config, ChartTypes.Column, Columns, From, Aggregate, RowFilter, Own, Dashboard, 5000, null, LeavingOut(config, ChartTypes.Column));

        // The domain reads the widget's own filters and the exclusion, in both its branches: a left-out series takes no slot.
        Assert.Equal(2, plan.Domain!.Sql.Split($"AND NOT ({LeftBoth})").Length - 1);
        Assert.Contains(plan.Domain.Parameters, p => p.Name == "ng");
        Assert.Equal(["No sabe"], (string[])plan.Domain.Parameters.Single(p => p.Name == "ns").Value);
        Assert.DoesNotContain("@d0", plan.Domain.Sql);
        var cells = plan.Cells(["Piped"]);
        Assert.Contains($"WHERE {RowFilter} AND \"water_source\" <> @f0 AND \"sex\" = @d0 AND NOT ({LeftBoth})", cells.Sql);
        Assert.Contains(cells.Parameters, p => p.Name == "ng");
        Assert.Contains(cells.Parameters, p => p.Name == "ns");
    }

    [Fact]
    public void A_pyramids_left_out_count_does_not_count_the_rows_the_widget_leaves_out()
    {
        var config = Config(pyramid: new WidgetPyramid("Female", "Male"));
        var plan = SeriesQueryBuilder.Build(config, ChartTypes.Pyramid, Columns, From, Aggregate, RowFilter, Own, Dashboard, 5000, null, LeavingOut(config, ChartTypes.Pyramid));

        var cells = plan.Cells(["Female", "Male"]).Sql;

        Assert.Contains($"AND NOT ({LeftBoth}) AND NOT (", cells[cells.IndexOf("count(*), NULL::bigint", StringComparison.Ordinal)..]);
    }
}

public sealed class SeriesDomainTests
{
    [Fact]
    public void Series_that_fit_are_all_kept()
    {
        var (kept, other) = SeriesDomain.Fold(["a", "b", "c"], hasNone: false, limit: 8);

        Assert.Equal(["a", "b", "c"], kept);
        Assert.False(other);
    }

    [Fact]
    public void Exactly_the_limit_still_fits_and_one_more_folds_the_smallest_into_other()
    {
        var values = Enumerable.Range(1, 9).Select(i => $"s{i}").ToList();

        Assert.False(SeriesDomain.Fold(values.Take(8).ToList(), false, 8).HasOther);
        var (kept, other) = SeriesDomain.Fold(values, false, 8);

        Assert.True(other);
        Assert.Equal(values.Take(7), kept);
    }

    [Fact]
    public void The_missing_value_takes_a_place_and_is_never_folded()
    {
        var values = Enumerable.Range(1, 9).Select(i => $"s{i}").ToList();

        var (kept, other) = SeriesDomain.Fold(values, hasNone: true, limit: 8);
        var (keptFit, otherFit) = SeriesDomain.Fold(values.Take(7).ToList(), hasNone: true, limit: 8);

        Assert.Equal(values.Take(6), kept);
        Assert.True(other);
        Assert.Equal(7, keptFit.Count);
        Assert.False(otherFit);
    }

    [Fact]
    public void The_largest_are_the_ones_kept_in_the_order_they_come()
    {
        var (kept, _) = SeriesDomain.Fold(["big", "mid", "small", "tiny"], false, 3);

        Assert.Equal(["big", "mid"], kept);
    }

    [Fact]
    public void A_limit_of_one_folds_everything_into_other()
    {
        var (kept, other) = SeriesDomain.Fold(["a", "b"], false, 1);

        Assert.Empty(kept);
        Assert.True(other);
    }

    [Fact]
    public void The_display_order_is_natural_then_none_then_other()
    {
        var order = SeriesDomain.Order(["10", "2", "1", "b", "A"], hasNone: true, hasOther: true);

        Assert.Equal(
            [("1", "value"), ("2", "value"), ("10", "value"), ("A", "value"), ("b", "value"), (null, "none"), (null, "other")],
            order.Select(s => (s.Value, s.Kind)).ToArray());
    }

    [Fact]
    public void No_series_is_an_empty_list()
    {
        Assert.Empty(SeriesDomain.Order([], false, false));
    }

    private static (string? Value, string Kind)[] Described(IEnumerable<SeriesInfo> domain) => domain.Select(s => (s.Value, s.Kind)).ToArray();

    [Fact]
    public void A_pies_domain_keeps_every_group_as_it_comes_the_missing_one_in_its_place()
    {
        var domain = SeriesDomain.BySize(["10", "Well", null, "2", "River"], limit: 8);

        // Largest total first, as the groups come: no natural order, and the missing group is not moved to the end.
        Assert.Equal([("10", "value"), ("Well", "value"), (null, "none"), ("2", "value"), ("River", "value")], Described(domain));
    }

    [Fact]
    public void A_pie_of_exactly_eight_groups_keeps_all_eight_and_has_no_other()
    {
        var groups = Enumerable.Range(1, 7).Select(i => (string?)$"s{i}").Append(null).ToList();

        var domain = SeriesDomain.BySize(groups, limit: 8);

        Assert.Equal(8, domain.Count);
        Assert.DoesNotContain(domain, s => s.Kind == SeriesKinds.Other);
        Assert.Equal(groups, domain.Select(s => s.Value));
    }

    [Fact]
    public void A_pie_of_more_than_eight_groups_keeps_the_seven_largest_and_folds_the_rest_into_other()
    {
        // The reader brings one group more than the pie shows, and no more.
        var nine = Enumerable.Range(1, 9).Select(i => (string?)$"s{i}").ToList();

        var domain = SeriesDomain.BySize(nine, limit: 8);

        Assert.Equal(nine.Take(7).Select(v => (v, "value")).Append((null, "other")), Described(domain));
    }

    [Fact]
    public void A_small_missing_group_is_folded_into_other_like_any_value_and_a_large_one_keeps_its_place()
    {
        var small = Enumerable.Range(1, 8).Select(i => (string?)$"s{i}").Append(null).ToList();
        var large = Enumerable.Range(1, 3).Select(i => (string?)$"s{i}").Append(null).Concat(Enumerable.Range(4, 5).Select(i => (string?)$"s{i}")).ToList();

        var foldedNone = SeriesDomain.BySize(small, limit: 8);
        var keptNone = SeriesDomain.BySize(large, limit: 8);

        Assert.DoesNotContain(foldedNone, s => s.Kind == SeriesKinds.None);
        Assert.Equal((null, "other"), Described(foldedNone)[^1]);
        Assert.Equal(7, foldedNone.Count(s => s.Kind == SeriesKinds.Value));
        Assert.Equal((null, "none"), Described(keptNone)[3]);
        Assert.Equal((null, "other"), Described(keptNone)[^1]);
        Assert.Equal(8, keptNone.Count);
    }

    [Fact]
    public void A_pies_domain_is_empty_for_no_groups_and_a_missing_group_alone_is_none()
    {
        Assert.Empty(SeriesDomain.BySize([], 8));
        Assert.Equal([(null, "none")], Described(SeriesDomain.BySize([null], 8)));
        Assert.Equal([("b", "value"), ("a", "value")], Described(SeriesDomain.BySize(["b", "a"], 8)));
    }

    [Fact]
    public void A_limit_of_one_folds_every_group_of_a_pie_into_other()
    {
        Assert.Equal([(null, "other")], Described(SeriesDomain.BySize(["a", "b"], limit: 1)));
        Assert.Equal([("a", "value")], Described(SeriesDomain.BySize(["a"], limit: 1)));
    }
}
