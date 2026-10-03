using Microsoft.Extensions.Configuration;
using Tamiza.Api.Ingestion;

namespace Tamiza.Api.UnitTests.Ingestion;

public sealed class IngestionOptionsTests
{
    private static IngestionOptions Bind(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
        var options = new IngestionOptions();
        configuration.GetSection(IngestionOptions.Section).Bind(options);
        return options;
    }

    [Fact]
    public void Defaults_follow_the_design()
    {
        var options = Bind();

        Assert.True(options.SchedulerEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Tick);
        Assert.Equal(2, options.MaxConcurrentRuns);
        Assert.Equal(500, options.PageSize);
        Assert.Equal(5_000, options.IdPageSize);
        Assert.Equal(50 * 1024 * 1024, options.MaxPageBytes);
        Assert.Equal(TimeSpan.FromMinutes(2), options.PageTimeout);
        Assert.Equal(TimeSpan.FromHours(1), options.SafetyMargin);
        Assert.Equal(TimeSpan.FromHours(1), options.RetryDelay);
        Assert.Equal(10 * 1024 * 1024, options.MaxWebhookBytes);
    }

    [Fact]
    public void Values_bind_from_the_ingestion_section()
    {
        var options = Bind(
            ("Ingestion:SchedulerEnabled", "false"),
            ("Ingestion:Tick", "00:00:00.200"),
            ("Ingestion:PageSize", "3"),
            ("Ingestion:SafetyMargin", "00:30:00"));

        Assert.False(options.SchedulerEnabled);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.Tick);
        Assert.Equal(3, options.PageSize);
        Assert.Equal(TimeSpan.FromMinutes(30), options.SafetyMargin);
    }
}
