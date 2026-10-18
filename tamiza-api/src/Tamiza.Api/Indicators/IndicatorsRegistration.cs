using Microsoft.Extensions.Options;

namespace Tamiza.Api.Indicators;

public static class IndicatorsRegistration
{
    public static IServiceCollection AddTamizaIndicators(this IServiceCollection services)
    {
        services.AddOptions<IndicatorDashboardOptions>()
            .BindConfiguration(IndicatorDashboardOptions.Section)
            // An operator's IndicatorDashboard__MaxWidgetsPerProject keeps working while the new variable is not set (design D8).
            .Configure<IConfiguration>(IndicatorDashboardOptions.ApplyFormerWidgetLimit)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // It reads through the clean data dashboard's connections, query slots and time limits (AddTamizaCleanData).
        services.AddSingleton<IndicatorDashboard>();
        services.AddHostedService<IndicatorDashboardStartupCheck>();
        return services;
    }
}

/// <summary>Says once at startup that the former widget-limit variable is in use and which variable replaces it.</summary>
internal sealed class IndicatorDashboardStartupCheck(
    IConfiguration configuration, IOptions<IndicatorDashboardOptions> options, ILogger<IndicatorDashboardStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (IndicatorDashboardOptions.UsesFormerWidgetLimit(configuration))
        {
            logger.LogWarning(
                "{Former} is deprecated: its value, {Limit}, is used as the most widgets one dashboard can hold. Rename it to {Current}.",
                IndicatorDashboardOptions.MaxWidgetsPerProjectVariable, options.Value.MaxWidgetsPerDashboard,
                IndicatorDashboardOptions.MaxWidgetsPerDashboardVariable);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
