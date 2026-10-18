using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Maps;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class IndicatorDashboardOptionsTests
{
    private static ServiceProvider Build(LogCapture logs, (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => $"IndicatorDashboard:{v.Key}", v => (string?)v.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddTamizaIndicators();
        return services.BuildServiceProvider();
    }

    private static IndicatorDashboardOptions Resolve(params (string Key, string Value)[] values)
    {
        using var provider = Build(new LogCapture(), values);
        return provider.GetRequiredService<IOptions<IndicatorDashboardOptions>>().Value;
    }

    /// <summary>The warnings logged while the module's hosted services start, as the api does once at startup.</summary>
    private static async Task<string[]> StartupWarningsAsync(params (string Key, string Value)[] values)
    {
        var logs = new LogCapture();
        await using var provider = Build(logs, values);
        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }

        return [.. logs.Lines.Where(line => line.StartsWith($"{LogLevel.Warning} ", StringComparison.Ordinal))];
    }

    [Fact]
    public void Defaults_are_50_widgets_per_dashboard_5000_rows_and_7_dashboards()
    {
        var options = Resolve();

        Assert.Equal(50, options.MaxWidgetsPerDashboard);
        Assert.Equal(5000, options.MaxWidgetQueryRows);
        Assert.Equal(7, IndicatorDashboardOptions.MaxDashboards);
    }

    [Fact]
    public void Values_bind_from_configuration()
    {
        var options = Resolve(("MaxWidgetsPerDashboard", "9"), ("MaxWidgetQueryRows", "123"));

        Assert.Equal(9, options.MaxWidgetsPerDashboard);
        Assert.Equal(123, options.MaxWidgetQueryRows);
    }

    [Fact]
    public void The_former_per_project_name_sets_the_limit_when_the_new_one_is_not_set()
    {
        var options = Resolve(("MaxWidgetsPerProject", "9"));

        Assert.Equal(9, options.MaxWidgetsPerDashboard);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("0")]
    [InlineData("501")]
    public void The_new_name_wins_when_both_are_set(string former)
    {
        var options = Resolve(("MaxWidgetsPerDashboard", "12"), ("MaxWidgetsPerProject", former));

        Assert.Equal(12, options.MaxWidgetsPerDashboard);
    }

    [Theory]
    [InlineData("MaxWidgetsPerDashboard", "0")]
    [InlineData("MaxWidgetsPerDashboard", "501")]
    [InlineData("MaxWidgetsPerProject", "0")]
    [InlineData("MaxWidgetsPerProject", "501")]
    [InlineData("MaxWidgetQueryRows", "0")]
    [InlineData("MaxWidgetQueryRows", "100001")]
    public void Out_of_range_values_fail_when_the_options_are_read(string key, string value) =>
        Assert.Throws<OptionsValidationException>(() => Resolve((key, value)));

    [Theory]
    [InlineData("MaxWidgetsPerDashboard", "fifty")]
    [InlineData("MaxWidgetsPerProject", "fifty")]
    [InlineData("MaxWidgetsPerDashboard", "")]
    [InlineData("MaxWidgetsPerProject", "")]
    public void A_limit_that_is_not_a_number_fails_under_either_name(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve((key, value)));

        Assert.Contains($"IndicatorDashboard:{key}", error.Message);
    }

    [Fact]
    public async Task The_former_name_alone_draws_one_startup_warning_naming_both_variables()
    {
        var warning = Assert.Single(await StartupWarningsAsync(("MaxWidgetsPerProject", "37")));

        Assert.Contains("IndicatorDashboard__MaxWidgetsPerProject", warning);
        Assert.Contains("IndicatorDashboard__MaxWidgetsPerDashboard", warning);
        Assert.Contains("37", warning);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("9", null)]
    [InlineData("9", "9")]
    public async Task No_startup_warning_when_the_former_name_does_not_set_the_limit(string? current, string? former)
    {
        (string Key, string Value)[] values =
        [
            .. current is null ? [] : new[] { ("MaxWidgetsPerDashboard", current) },
            .. former is null ? [] : new[] { ("MaxWidgetsPerProject", former) },
        ];

        Assert.Empty(await StartupWarningsAsync(values));
    }
}
