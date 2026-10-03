using Tamiza.Api.Kobo;

namespace Tamiza.Api.UnitTests.Kobo;

public sealed class KoboServerUrlTests
{
    [Theory]
    [InlineData(KoboServerUrl.KoboToolbox, "https://kf.kobotoolbox.org")]
    [InlineData(KoboServerUrl.KoboToolboxEu, "https://eu.kobotoolbox.org")]
    [InlineData("  https://Kobo.Example.org/  ", "https://kobo.example.org")]
    [InlineData("https://example.org/kpi/", "https://example.org/kpi")]
    [InlineData("https://example.org:8443", "https://example.org:8443")]
    public void Valid_https_urls_are_normalized(string input, string expected)
    {
        var (url, error) = KoboServerUrl.Parse(input, allowInsecure: false);
        Assert.Null(error);
        Assert.Equal(expected, url);
    }

    [Fact]
    public void Plain_http_requires_the_private_networks_setting()
    {
        Assert.Equal(KoboErrorCodes.HttpsRequired, KoboServerUrl.Parse("http://kobo.lan", allowInsecure: false).Error?.Code);
        Assert.Equal("http://kobo.lan", KoboServerUrl.Parse("http://kobo.lan", allowInsecure: true).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kf.kobotoolbox.org")]
    [InlineData("/api/v2")]
    [InlineData("ftp://kobo.example.org")]
    [InlineData("https://user:secret@kobo.example.org")]
    [InlineData("https://kobo.example.org/?next=/admin")]
    [InlineData("https://kobo.example.org/#top")]
    public void Malformed_urls_are_rejected(string? input) =>
        Assert.Equal(KoboErrorCodes.InvalidUrl, KoboServerUrl.Parse(input, allowInsecure: true).Error?.Code);
}
