using Microsoft.Extensions.Configuration;
using Tamiza.Api.Accounts;

namespace Tamiza.Api.UnitTests.Accounts;

public sealed class AccountEmailOptionsTests
{
    private static AccountEmailOptions Bind(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
        var options = new AccountEmailOptions();
        configuration.Bind(options);
        return options;
    }

    private static string[] Failures(AccountEmailOptions options) =>
        new AccountEmailOptionsValidator().Validate(null, options).Failures?.ToArray() ?? [];

    [Fact]
    public void The_default_is_twenty_emails_per_hour()
    {
        var options = Bind();

        Assert.Equal(20, options.PerHour);
        Assert.Empty(Failures(options));
    }

    [Fact]
    public void The_limit_binds_from_its_variable()
    {
        Assert.Equal(5, Bind(("TAMIZA_ACCOUNT_EMAILS_PER_HOUR", "5")).PerHour);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(0, false)]
    [InlineData(1001, false)]
    [InlineData(-3, false)]
    public void The_limit_must_be_between_1_and_1000(int value, bool valid)
    {
        var failures = Failures(new AccountEmailOptions { PerHour = value });

        Assert.Equal(valid, failures.Length == 0);
        Assert.All(failures, failure => Assert.Contains("TAMIZA_ACCOUNT_EMAILS_PER_HOUR", failure));
    }
}
