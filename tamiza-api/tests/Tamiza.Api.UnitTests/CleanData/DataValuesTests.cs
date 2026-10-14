using Tamiza.Api.CleanData;

namespace Tamiza.Api.UnitTests.CleanData;

public sealed class DataValuesTests
{
    private static string Emoji(int count) => string.Concat(Enumerable.Repeat("😀", count));

    [Theory]
    [InlineData("2026-09-01 21:30:00-05", "2026-09-01T21:30:00-05:00")]
    [InlineData("2026-09-01 21:30:00+00", "2026-09-01T21:30:00+00:00")]
    [InlineData("2026-09-01 21:30:00.125+05:30", "2026-09-01T21:30:00.125+05:30")]
    [InlineData("2026-09-01 21:30:00.5-04:56:02", "2026-09-01T21:30:00.5-04:56")]
    [InlineData("2026-09-01 10:00:00", "2026-09-01T10:00:00")]
    [InlineData("12026-09-01 10:00:00+00", "12026-09-01T10:00:00+00:00")]
    [InlineData("infinity", "infinity")]
    [InlineData("-infinity", "-infinity")]
    [InlineData("0044-03-15 10:00:00+00 BC", "0044-03-15 10:00:00+00 BC")]
    public void Timestamps_become_iso_8601_with_their_offset(string postgres, string expected) =>
        Assert.Equal(expected, DataValues.IsoTimestamp(postgres));

    [Theory]
    [InlineData("10.50", "10.50")]
    [InlineData("-3", "-3")]
    [InlineData("1e+20", "100000000000000000000")]
    [InlineData("0.30000000000000004", "0.30000000000000004")]
    public void Numbers_that_fit_a_decimal_are_decimals(string postgres, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Assert.IsType<decimal>(DataValues.Number(postgres)));

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e+40")]
    [InlineData("123456789012345678901234567890.5")]
    public void Numbers_that_do_not_fit_stay_as_the_text_postgresql_wrote(string postgres) =>
        Assert.Equal(postgres, Assert.IsType<string>(DataValues.Number(postgres)));

    [Fact]
    public void Text_of_500_characters_or_fewer_is_shown_whole()
    {
        Assert.Equal(("", false), DataValues.Display(""));
        Assert.Equal((new string('a', 500), false), DataValues.Display(new string('a', 500)));
        Assert.Equal((Emoji(500), false), DataValues.Display(Emoji(500)));
    }

    [Fact]
    public void Text_over_500_characters_is_cut_to_500_and_marked()
    {
        var (text, cut) = DataValues.Display(new string('a', 501));

        Assert.True(cut);
        Assert.Equal(new string('a', 500), text);
    }

    [Fact]
    public void A_cut_counts_characters_not_utf16_units_so_it_never_splits_a_pair()
    {
        var (text, cut) = DataValues.Display(Emoji(501));

        Assert.True(cut);
        Assert.Equal(500, DataValues.Length(text));
        Assert.Equal(1000, text.Length);
        Assert.DoesNotContain('�', text);
    }

    [Fact]
    public void Truncate_keeps_the_first_characters()
    {
        Assert.Equal("héll", DataValues.Truncate("héllo wörld", 4));
        Assert.Equal("short", DataValues.Truncate("short", 100));
    }
}
