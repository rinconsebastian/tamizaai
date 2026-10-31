using Tamiza.Api.Indicators;
using Tamiza.Api.Reports;

namespace Tamiza.Api.UnitTests.Reports;

/// <summary>The C# twins of the page's chart steps, on the same cases as tamiza-ui's <c>chart-scales.spec.ts</c>.</summary>
public sealed class ReportDataBuilderTests
{
    private static IndicatorRow Row(string? group, double value) => new(group, value, null);

    [Fact]
    public void Counts_rows_in_equal_width_ranges_between_the_smallest_and_the_largest_value()
    {
        var buckets = ReportDataBuilder.Histogram([0, 2, 5, 9, 10], 2);

        Assert.Equal([(0d, 5d, 2), (5d, 10d, 3)], buckets);
    }

    [Fact]
    public void Answers_no_buckets_when_there_are_no_rows() => Assert.Empty(ReportDataBuilder.Histogram([], 10));

    [Fact]
    public void Treats_a_bucket_count_below_one_as_one() => Assert.Single(ReportDataBuilder.Histogram([1, 3], 0));

    [Fact]
    public void Puts_every_row_in_the_first_bucket_when_all_values_are_equal()
    {
        var buckets = ReportDataBuilder.Histogram([4, 4, 4], 3);

        Assert.Equal((4d, 3), (buckets[0].From, buckets[0].Count));
        Assert.Equal(5d, buckets[2].To);
        Assert.Equal(0, buckets[1].Count + buckets[2].Count);
    }

    [Fact]
    public void Leaves_out_values_that_are_not_finite() =>
        Assert.Equal(2, ReportDataBuilder.Histogram([1, double.NaN, 3, double.PositiveInfinity], 2).Sum(b => b.Count));

    [Fact]
    public void Compares_text_naturally()
    {
        var rows = new[] { "10", "2", "1", "b", "a" }.Select(g => Row(g, 0));

        Assert.Equal(["1", "2", "10", "a", "b"], rows.Order(NaturalOrder.ByGroup.Instance).Select(r => r.Group));
    }

    [Fact]
    public void Orders_groups_naturally_with_a_missing_group_last()
    {
        var ordered = new[] { Row(null, 1), Row("10", 3), Row("2", 2) }.Order(NaturalOrder.ByGroup.Instance).ToList();

        Assert.Equal(["2", "10", null], ordered.Select(r => r.Group));
        Assert.Equal([2d, 3d, 1d], ordered.Select(r => r.Value));
    }

    [Fact]
    public void Ignores_case_and_accents_as_the_page_does()
    {
        var ordered = new[] { "bogotá", "Bogota", "Árbol", "cali" }.Select(g => Row(g, 0)).Order(NaturalOrder.ByGroup.Instance);

        Assert.Equal("Árbol", ordered.First().Group);
        Assert.Equal("cali", ordered.Last().Group);
    }

    [Fact]
    public void Plotted_names_the_measure_or_the_ratio()
    {
        var config = new WidgetConfig("coverage", null, null, null, [], null, null);

        Assert.Equal("Coverage", ReportDataBuilder.Plotted(config, "Coverage"));
        Assert.Equal("Coverage: value", ReportDataBuilder.Plotted(config with { Measure = "value" }, "Coverage"));
        Assert.Equal("Coverage: visited / target",
            ReportDataBuilder.Plotted(config with { Measure = "value", Ratio = new WidgetRatio("visited", "target") }, "Coverage"));
    }

    [Fact]
    public void Describes_each_kind_of_default_as_the_filter_bar_does()
    {
        var text = ReportText.For("en-US");

        Assert.Equal("Norte, Sur", ReportDataBuilder.Describe(Filter("text"), new(["Norte", "Sur"]), text));
        Assert.Equal("(empty text)", ReportDataBuilder.Describe(Filter("text"), new([""]), text));
        Assert.Equal("true", ReportDataBuilder.Describe(Filter("boolean"), new(["true"]), text));
        Assert.Equal("3", ReportDataBuilder.Describe(Filter("number"), new(Min: "3", Max: "3"), text));
        Assert.Equal("2026-10-01 to 2026-10-31", ReportDataBuilder.Describe(Filter("date"), new(Min: "2026-10-01", Max: "2026-10-31"), text));
        Assert.Equal("from 5", ReportDataBuilder.Describe(Filter("number"), new(Min: "5"), text));
        Assert.Equal("up to 5", ReportDataBuilder.Describe(Filter("number"), new(Max: "5"), text));
        Assert.Equal("verdadero", ReportDataBuilder.Describe(Filter("boolean"), new(["true"]), ReportText.For("es-CO")));
    }

    private static DashboardFilter Filter(string kind) => new("column", "Label", kind, null);
}
