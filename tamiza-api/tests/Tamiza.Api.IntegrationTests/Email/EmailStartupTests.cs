using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Email;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Email;

[Collection(PostgresCollection.Name)]
public sealed class EmailStartupTests(PostgresFixture postgres)
{
    private async Task<TamizaApiFactory> FactoryAsync(Dictionary<string, string?>? settings = null) =>
        new(await postgres.CreateDatabaseAsync(), settings ?? new Dictionary<string, string?>());

    [Fact]
    public async Task Without_the_smtp_variables_the_api_starts_and_there_is_no_sender()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        Assert.Null(factory.Services.GetRequiredService<EmailSenderSource>().Sender);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Blanks_from_compose_start_the_api_with_email_off()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?>
        {
            ["TAMIZA_SMTP_HOST"] = "", ["TAMIZA_SMTP_PORT"] = "", ["TAMIZA_SMTP_SECURITY"] = "", ["TAMIZA_SMTP_USERNAME"] = "",
            ["TAMIZA_SMTP_PASSWORD"] = "", ["TAMIZA_SMTP_FROM"] = "", ["TAMIZA_SMTP_FROM_NAME"] = "", ["TAMIZA_SMTP_TIMEOUT_SECONDS"] = "",
        });
        using var client = factory.CreateClient();

        Assert.Null(factory.Services.GetRequiredService<EmailSenderSource>().Sender);
    }

    [Fact]
    public async Task With_the_host_and_the_sender_there_is_a_sender()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?>
        {
            ["TAMIZA_SMTP_HOST"] = "smtp.example.org",
            ["TAMIZA_SMTP_FROM"] = "reports@example.org",
        });
        using var client = factory.CreateClient();

        Assert.NotNull(factory.Services.GetRequiredService<EmailSenderSource>().Sender);
    }

    [Fact]
    public async Task Port_out_of_range()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?> { ["TAMIZA_SMTP_PORT"] = "70000" });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("TAMIZA_SMTP_PORT must be between 1 and 65535", failure.ToString());
    }

    [Fact]
    public async Task User_name_without_password()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?>
        {
            ["TAMIZA_SMTP_HOST"] = "smtp.example.org",
            ["TAMIZA_SMTP_FROM"] = "reports@example.org",
            ["TAMIZA_SMTP_USERNAME"] = "reports",
        });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("TAMIZA_SMTP_USERNAME and TAMIZA_SMTP_PASSWORD must be set together", failure.ToString());
    }

    [Fact]
    public async Task Sender_without_a_domain()
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?>
        {
            ["TAMIZA_SMTP_HOST"] = "smtp.example.org",
            ["TAMIZA_SMTP_FROM"] = "tamiza",
        });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("TAMIZA_SMTP_FROM must be an email address", failure.ToString());
    }

    [Theory]
    [InlineData("TAMIZA_SMTP_HOST", "smtp.example.org", "TAMIZA_SMTP_FROM")]
    [InlineData("TAMIZA_SMTP_FROM", "reports@example.org", "TAMIZA_SMTP_HOST")]
    public async Task Partly_configured(string set, string value, string missing)
    {
        await using var factory = await FactoryAsync(new Dictionary<string, string?> { [set] = value });
        using var client = factory.CreateClient();

        var warning = Assert.Single(factory.Logs.Lines, line => line.StartsWith("Warning ", StringComparison.Ordinal) && line.Contains("Email is off"));
        Assert.Contains($"because {missing} is not set", warning);
        Assert.Null(factory.Services.GetRequiredService<EmailSenderSource>().Sender);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Says_nothing_at_startup_when_none_or_both_are_set(bool both)
    {
        await using var factory = await FactoryAsync(both
            ? new Dictionary<string, string?> { ["TAMIZA_SMTP_HOST"] = "smtp.example.org", ["TAMIZA_SMTP_FROM"] = "reports@example.org" }
            : null);
        using var client = factory.CreateClient();

        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("Email is off"));
    }
}
