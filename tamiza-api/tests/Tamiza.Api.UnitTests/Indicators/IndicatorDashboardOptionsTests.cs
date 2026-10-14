using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class IndicatorDashboardOptionsTests
{
    private static IndicatorDashboardOptions Resolve(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => $"IndicatorDashboard:{v.Key}", v => (string?)v.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTamizaIndicators();
        return services.BuildServiceProvider().GetRequiredService<IOptions<IndicatorDashboardOptions>>().Value;
    }

    [Fact]
    public void Defaults_are_50_widgets_and_5000_rows()
    {
        var options = Resolve();

        Assert.Equal(50, options.MaxWidgetsPerProject);
        Assert.Equal(5000, options.MaxWidgetQueryRows);
    }

    [Fact]
    public void Values_bind_from_configuration()
    {
        var options = Resolve(("MaxWidgetsPerProject", "7"), ("MaxWidgetQueryRows", "123"));

        Assert.Equal(7, options.MaxWidgetsPerProject);
        Assert.Equal(123, options.MaxWidgetQueryRows);
    }

    [Theory]
    [InlineData("MaxWidgetsPerProject", "0")]
    [InlineData("MaxWidgetsPerProject", "501")]
    [InlineData("MaxWidgetQueryRows", "0")]
    [InlineData("MaxWidgetQueryRows", "100001")]
    public void Out_of_range_values_fail_when_the_options_are_read(string key, string value) =>
        Assert.Throws<OptionsValidationException>(() => Resolve((key, value)));
}
