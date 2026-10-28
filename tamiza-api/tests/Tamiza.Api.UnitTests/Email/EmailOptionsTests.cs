using Microsoft.Extensions.Configuration;
using Tamiza.Api.Email;

namespace Tamiza.Api.UnitTests.Email;

public sealed class EmailOptionsTests
{
    private static EmailOptions Bind(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
        var options = new EmailOptions();
        configuration.Bind(options);
        return options;
    }

    private static string[] Failures(EmailOptions options) =>
        new EmailOptionsValidator().Validate(null, options).Failures?.ToArray() ?? [];

    [Fact]
    public void Nothing_is_set_by_default()
    {
        var options = Bind();

        Assert.False(options.Enabled);
        Assert.Equal(EmailOptions.StartTls, options.SecurityMode);
        Assert.Equal(587, options.EffectivePort);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal("Tamiza", options.SenderName);
        Assert.False(options.HasCredentials);
        Assert.Null(options.MissingVariable());
        Assert.Empty(Failures(options));
    }

    [Fact]
    public void Compose_passes_blanks_for_unset_values()
    {
        var options = Bind(
            ("TAMIZA_SMTP_HOST", ""), ("TAMIZA_SMTP_PORT", ""), ("TAMIZA_SMTP_SECURITY", ""), ("TAMIZA_SMTP_USERNAME", ""),
            ("TAMIZA_SMTP_PASSWORD", ""), ("TAMIZA_SMTP_FROM", ""), ("TAMIZA_SMTP_FROM_NAME", ""), ("TAMIZA_SMTP_TIMEOUT_SECONDS", ""));

        Assert.False(options.Enabled);
        Assert.Null(options.Port);
        Assert.Null(options.TimeoutSeconds);
        Assert.Empty(Failures(options));
    }

    [Fact]
    public void Every_variable_binds_and_text_is_trimmed()
    {
        var options = Bind(
            ("TAMIZA_SMTP_HOST", " smtp.example.org "), ("TAMIZA_SMTP_PORT", "2525"), ("TAMIZA_SMTP_SECURITY", " TLS "),
            ("TAMIZA_SMTP_USERNAME", " reports "), ("TAMIZA_SMTP_PASSWORD", "s3cret\n"), ("TAMIZA_SMTP_FROM", " reports@example.org "),
            ("TAMIZA_SMTP_FROM_NAME", " Census reports "), ("TAMIZA_SMTP_TIMEOUT_SECONDS", "45"));

        Assert.True(options.Enabled);
        Assert.Equal("smtp.example.org", options.Host);
        Assert.Equal(2525, options.EffectivePort);
        Assert.Equal(EmailOptions.Tls, options.SecurityMode);
        Assert.Equal("reports", options.Username);
        Assert.Equal("s3cret", options.Password);
        Assert.True(options.HasCredentials);
        Assert.Equal("reports@example.org", options.From);
        Assert.Equal("Census reports", options.SenderName);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Timeout);
        Assert.Empty(Failures(options));
    }

    [Theory]
    [InlineData("starttls", 587)]
    [InlineData("tls", 465)]
    [InlineData("none", 25)]
    public void The_default_port_follows_the_security(string security, int port) =>
        Assert.Equal(port, Bind(("TAMIZA_SMTP_SECURITY", security)).EffectivePort);

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    public void Port_out_of_range(string port)
    {
        var failure = Assert.Single(Failures(Bind(("TAMIZA_SMTP_PORT", port))));

        Assert.Contains("TAMIZA_SMTP_PORT", failure);
        Assert.Contains("between 1 and 65535", failure);
    }

    [Fact]
    public void An_unknown_security_is_refused()
    {
        var failure = Assert.Single(Failures(Bind(("TAMIZA_SMTP_SECURITY", "ssl"))));

        Assert.Contains("TAMIZA_SMTP_SECURITY", failure);
        Assert.Contains("starttls, tls or none", failure);
    }

    [Theory]
    [InlineData("tamiza")]
    [InlineData("Tamiza <reports@example.org>")]
    [InlineData("reports at example.org")]
    [InlineData("reports.@example.org")]
    public void Sender_without_a_domain_or_with_a_display_name(string from)
    {
        var failure = Assert.Single(Failures(Bind(("TAMIZA_SMTP_HOST", "smtp.example.org"), ("TAMIZA_SMTP_FROM", from))));

        Assert.Contains("TAMIZA_SMTP_FROM must be an email address", failure);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("301")]
    public void A_timeout_out_of_range_is_refused(string seconds)
    {
        var failure = Assert.Single(Failures(Bind(("TAMIZA_SMTP_TIMEOUT_SECONDS", seconds))));

        Assert.Contains("TAMIZA_SMTP_TIMEOUT_SECONDS must be between 5 and 300", failure);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(300)]
    public void The_timeout_range_includes_its_ends(int seconds) =>
        Assert.Empty(Failures(Bind(("TAMIZA_SMTP_TIMEOUT_SECONDS", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)))));

    [Theory]
    [InlineData("TAMIZA_SMTP_USERNAME")]
    [InlineData("TAMIZA_SMTP_PASSWORD")]
    public void User_name_without_password(string set)
    {
        var failure = Assert.Single(Failures(Bind((set, "value"))));

        Assert.Equal("TAMIZA_SMTP_USERNAME and TAMIZA_SMTP_PASSWORD must be set together.", failure);
    }

    [Theory]
    [InlineData("TAMIZA_SMTP_HOST", "smtp.example.org", "TAMIZA_SMTP_FROM")]
    [InlineData("TAMIZA_SMTP_FROM", "reports@example.org", "TAMIZA_SMTP_HOST")]
    public void Partly_configured_names_the_missing_variable(string set, string value, string missing)
    {
        var options = Bind((set, value));

        Assert.False(options.Enabled);
        Assert.Equal(missing, options.MissingVariable());
    }
}
