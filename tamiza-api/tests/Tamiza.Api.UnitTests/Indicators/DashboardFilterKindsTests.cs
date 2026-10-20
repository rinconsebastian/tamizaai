using Tamiza.Api.CleanData;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;
using static Tamiza.Api.Indicators.DashboardFilterKinds;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class DashboardFilterKindsTests
{
    private static string[] Check(string kind, DashboardFilterValue? value)
    {
        var errors = new FieldErrors();
        CheckValue(kind, value, "round", "dashboardFilters[0]", errors);
        return errors.Any ? errors.ToDictionary()["dashboardFilters[0]"] : [];
    }

    private static IReadOnlyList<string> Many(int count, int length = 4) =>
        Enumerable.Range(0, count).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(length, 'v')).ToList();

    [Theory]
    [InlineData(Text, DataColumnType.Text, true)]
    [InlineData(Text, DataColumnType.Integer, false)]
    [InlineData(Text, DataColumnType.Decimal, false)]
    [InlineData(Text, DataColumnType.Boolean, false)]
    [InlineData(Text, DataColumnType.Date, false)]
    [InlineData(Text, DataColumnType.Timestamp, false)]
    [InlineData(DashboardFilterKinds.Boolean, DataColumnType.Boolean, true)]
    [InlineData(DashboardFilterKinds.Boolean, DataColumnType.Text, false)]
    [InlineData(DashboardFilterKinds.Boolean, DataColumnType.Integer, false)]
    [InlineData(DashboardFilterKinds.Boolean, DataColumnType.Decimal, false)]
    [InlineData(DashboardFilterKinds.Boolean, DataColumnType.Date, false)]
    [InlineData(DashboardFilterKinds.Boolean, DataColumnType.Timestamp, false)]
    [InlineData(Number, DataColumnType.Integer, true)]
    [InlineData(Number, DataColumnType.Decimal, true)]
    [InlineData(Number, DataColumnType.Text, false)]
    [InlineData(Number, DataColumnType.Boolean, false)]
    [InlineData(Number, DataColumnType.Date, false)]
    [InlineData(Number, DataColumnType.Timestamp, false)]
    [InlineData(Date, DataColumnType.Date, true)]
    [InlineData(Date, DataColumnType.Timestamp, true)]
    [InlineData(Date, DataColumnType.Text, false)]
    [InlineData(Date, DataColumnType.Integer, false)]
    [InlineData(Date, DataColumnType.Decimal, false)]
    [InlineData(Date, DataColumnType.Boolean, false)]
    [InlineData("color", DataColumnType.Text, false)]
    public void A_kind_matches_its_family_of_column_types(string kind, DataColumnType type, bool matches) =>
        Assert.Equal(matches, Matches(kind, type));

    [Theory]
    [InlineData(DataColumnType.Text, Text)]
    [InlineData(DataColumnType.Boolean, DashboardFilterKinds.Boolean)]
    [InlineData(DataColumnType.Integer, Number)]
    [InlineData(DataColumnType.Decimal, Number)]
    [InlineData(DataColumnType.Date, Date)]
    [InlineData(DataColumnType.Timestamp, Date)]
    public void Every_column_type_belongs_to_one_kind(DataColumnType type, string kind)
    {
        Assert.Equal(kind, Of(type));
        Assert.True(Matches(Of(type), type));
    }

    [Fact]
    public void Values_become_an_is_one_of_filter_and_a_range_a_range_filter()
    {
        var text = ToDataFilter("municipality", Text, new DashboardFilterValue(["Cali", "Buga"]));
        Assert.Equal(("municipality", "in", (string?)null, (string?)null), (text.Column, text.Op, text.Min, text.Max));
        Assert.Equal(["Cali", "Buga"], text.Values!);

        var flag = ToDataFilter("consent", DashboardFilterKinds.Boolean, new DashboardFilterValue(["true"]));
        Assert.Equal(("in", "true"), (flag.Op, flag.Values!.Single()));

        // An empty end is no end.
        Assert.Equal(new DataFilter("round", "range", Min: "2", Max: null), ToDataFilter("round", Number, new DashboardFilterValue(Min: "2", Max: "")));
        Assert.Equal(new DataFilter("visit", "range", Min: null, Max: "2026-09-01"), ToDataFilter("visit", Date, new DashboardFilterValue(Max: "2026-09-01")));
    }

    [Fact]
    public void A_number_filter_is_built_against_an_integer_column_as_a_decimal()
    {
        Assert.Equal(DataColumnType.Decimal, AsFiltered(Number, new TableColumn("round", DataColumnType.Integer, null)).Type);
        Assert.Equal(DataColumnType.Decimal, AsFiltered(Number, new TableColumn("round", DataColumnType.Decimal, null)).Type);
        Assert.Equal(DataColumnType.Timestamp, AsFiltered(Date, new TableColumn("visit", DataColumnType.Timestamp, null)).Type);
        Assert.Equal(DataColumnType.Text, AsFiltered(Text, new TableColumn("municipality", DataColumnType.Text, null)).Type);
    }

    // Text

    [Fact]
    public void A_text_filter_takes_1_to_100_values_of_at_most_500_characters()
    {
        Assert.Empty(Check(Text, new DashboardFilterValue(["Cali"])));
        Assert.Empty(Check(Text, new DashboardFilterValue(Many(100))));
        Assert.Empty(Check(Text, new DashboardFilterValue([new string('x', 500)])));
        Assert.Contains("at least one value", Check(Text, new DashboardFilterValue([])).Single());
        Assert.Contains("at least one value", Check(Text, new DashboardFilterValue()).Single());
        Assert.Contains("at most 100 values", Check(Text, new DashboardFilterValue(Many(101))).Single());
        Assert.Contains("longer than 500", Check(Text, new DashboardFilterValue([new string('x', 501)])).Single());
        Assert.Contains("not a range", Check(Text, new DashboardFilterValue(["Cali"], Min: "1")).Single());
    }

    [Fact]
    public void A_filter_without_a_value_is_refused() =>
        Assert.Contains("needs a value", Check(Text, null).Single());

    // True/false

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", true)]
    [InlineData("yes", false)]
    public void A_true_false_filter_takes_true_and_false(string value, bool valid) =>
        Assert.Equal(valid, Check(DashboardFilterKinds.Boolean, new DashboardFilterValue([value])).Length == 0);

    // Number

    [Theory]
    [InlineData("2", true)]
    [InlineData("2.5", true)]
    [InlineData("-3", true)]
    [InlineData("-1e3", false)]
    [InlineData("second", false)]
    public void A_number_bound_is_an_invariant_decimal(string min, bool valid)
    {
        var errors = Check(Number, new DashboardFilterValue(Min: min));

        Assert.Equal(valid, errors.Length == 0);
        if (!valid)
        {
            // Value of the wrong kind: the message names the filter's column.
            Assert.Contains("'round'", errors.Single());
        }
    }

    [Fact]
    public void A_range_needs_an_end_and_may_have_only_one()
    {
        Assert.Empty(Check(Number, new DashboardFilterValue(Min: "2")));
        Assert.Empty(Check(Number, new DashboardFilterValue(Max: "2")));
        Assert.Contains("minimum or a maximum", Check(Number, new DashboardFilterValue()).Single());
        Assert.Contains("minimum or a maximum", Check(Number, new DashboardFilterValue(Min: "", Max: "")).Single());
        Assert.Contains("not values", Check(Number, new DashboardFilterValue(["2"])).Single());
    }

    [Fact]
    public void A_reversed_range_is_refused()
    {
        Assert.Contains("greater than the maximum", Check(Number, new DashboardFilterValue(Min: "3", Max: "2.5")).Single());
        Assert.Contains("greater than the maximum", Check(Date, new DashboardFilterValue(Min: "2026-09-30", Max: "2026-09-01")).Single());
        Assert.Empty(Check(Date, new DashboardFilterValue(Min: "2026-09-01", Max: "2026-09-01")));
    }

    // Date

    [Theory]
    [InlineData("2026-09-01", true)]
    [InlineData("2026-13-01", false)]
    [InlineData("01/09/2026", false)]
    public void A_date_bound_is_a_day(string max, bool valid) =>
        Assert.Equal(valid, Check(Date, new DashboardFilterValue(Max: max)).Length == 0);
}
