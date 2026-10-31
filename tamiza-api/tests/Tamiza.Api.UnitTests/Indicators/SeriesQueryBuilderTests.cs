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
    public void Only_a_line_chart_and_a_pyramid_list_their_groups_in_natural_order()
    {
        foreach (var chartType in new[] { ChartTypes.Bar, ChartTypes.Column, ChartTypes.Heatmap, ChartTypes.Crosstab, ChartTypes.Likert })
        {
            Assert.False(Build(Config(scale: [new("A", LikertSides.Negative), new("B", LikertSides.Positive)]), chartType).NaturalGroups, chartType);
        }

        Assert.True(Build(Config(), ChartTypes.Line).NaturalGroups);
        Assert.True(Build(Config(pyramid: new("Female", "Male")), ChartTypes.Pyramid).NaturalGroups);
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
}
