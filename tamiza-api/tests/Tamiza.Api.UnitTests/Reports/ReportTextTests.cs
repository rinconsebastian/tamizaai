using Tamiza.Api.Reports;

namespace Tamiza.Api.UnitTests.Reports;

public sealed class ReportTextTests
{
    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        var (english, spanish) = ReportText.Keys;

        Assert.Equal(english.Order(StringComparer.Ordinal), spanish.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("es-CO")]
    [InlineData("es-MX")]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    public void Spanish_and_english_locales_are_accepted(string locale) => Assert.True(ReportText.IsSupported(locale));

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("xx")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a locale at all")]
    public void Other_locales_are_refused(string? locale) => Assert.False(ReportText.IsSupported(locale));

    [Theory]
    [InlineData("es-CO", "es-CO")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("en-CO", "en")]
    [InlineData("es-DE", "es")]
    [InlineData("en-MX", "en")]
    public void A_region_the_server_has_no_data_for_resolves_to_its_language(string locale, string resolved)
    {
        Assert.Equal(resolved, ReportText.Resolve(locale));
        Assert.Equal(resolved, ReportText.For(resolved).Culture.Name);
    }

    [Fact]
    public void The_language_follows_the_locale()
    {
        Assert.Equal("Resumen", ReportText.For("es-CO")["summary"]);
        Assert.Equal("Summary", ReportText.For("en-GB")["summary"]);
    }

    [Fact]
    public void More_rows_has_a_singular_and_a_plural()
    {
        Assert.Equal("1 more row", ReportText.For("en-US").MoreRows(1));
        Assert.Equal("22 more rows", ReportText.For("en-US").MoreRows(22));
        Assert.Equal("22 filas más", ReportText.For("es-CO").MoreRows(22));
    }
}
