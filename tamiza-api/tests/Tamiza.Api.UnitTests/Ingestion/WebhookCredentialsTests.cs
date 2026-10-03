using System.Text;
using Tamiza.Api.Ingestion;

namespace Tamiza.Api.UnitTests.Ingestion;

public sealed class WebhookCredentialsTests
{
    private static string Basic(string value) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    [Fact]
    public void Reads_username_and_password()
    {
        Assert.True(WebhookCredentials.TryParseBasic(Basic("tamiza:s3cret"), out var username, out var password));
        Assert.Equal("tamiza", username);
        Assert.Equal("s3cret", password);
    }

    [Fact]
    public void The_password_may_contain_colons_and_the_scheme_any_case()
    {
        Assert.True(WebhookCredentials.TryParseBasic(Basic("tamiza:a:b").Replace("Basic", "basic"), out _, out var password));
        Assert.Equal("a:b", password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("Basic !!not-base64!!")]
    public void Missing_or_malformed_headers_are_rejected(string? header) =>
        Assert.False(WebhookCredentials.TryParseBasic(header, out _, out _));

    [Fact]
    public void A_value_without_a_colon_is_rejected() =>
        Assert.False(WebhookCredentials.TryParseBasic(Basic("tamiza"), out _, out _));
}
