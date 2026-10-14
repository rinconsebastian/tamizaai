using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.UnitTests.CleanData;

public sealed class DataDashboardOptionsTests
{
    private static DataDashboardOptions Resolve(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => $"DataDashboard:{v.Key}", v => (string?)v.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTamizaCleanData();
        return services.BuildServiceProvider().GetRequiredService<IOptions<DataDashboardOptions>>().Value;
    }

    [Fact]
    public void Defaults_are_the_documented_limits()
    {
        var options = Resolve();

        Assert.Equal(TimeSpan.FromSeconds(25), options.StatementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(25), options.QueueTimeout);
        Assert.Equal(4, options.MaxConcurrentQueries);
        Assert.Equal(TimeSpan.FromSeconds(5), options.LockTimeout);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ExportStatementTimeout);
        Assert.Equal(2, options.MaxConcurrentExports);
        Assert.Equal(1024L * 1024 * 1024, options.MaxExportFileSize);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ExportLinkLifetime);
        Assert.Equal(1_048_575, options.XlsxMaxRows);
        Assert.EndsWith("tamiza-exports", options.ExportDirectory);
    }

    [Fact]
    public void Values_bind_from_configuration()
    {
        var options = Resolve(("MaxConcurrentExports", "3"), ("MaxExportFileSize", "5000"), ("LockTimeout", "00:00:02"));

        Assert.Equal(3, options.MaxConcurrentExports);
        Assert.Equal(5000, options.MaxExportFileSize);
        Assert.Equal(TimeSpan.FromSeconds(2), options.LockTimeout);
    }

    [Theory]
    [InlineData("MaxConcurrentQueries", "0")]
    [InlineData("MaxConcurrentExports", "17")]
    [InlineData("StatementTimeout", "00:00:00")]
    [InlineData("StatementTimeout", "01:00:00")]
    [InlineData("LockTimeout", "00:10:00")]
    [InlineData("ExportLinkLifetime", "00:00:00")]
    [InlineData("MaxExportFileSize", "0")]
    [InlineData("XlsxMaxRows", "1048576")]
    [InlineData("ExportDirectory", "")]
    public void Out_of_range_values_fail_when_the_options_are_read(string key, string value) =>
        Assert.Throws<OptionsValidationException>(() => Resolve((key, value)));
}
