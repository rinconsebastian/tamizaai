namespace Tamiza.Api.Indicators;

public static class IndicatorsRegistration
{
    public static IServiceCollection AddTamizaIndicators(this IServiceCollection services)
    {
        services.AddOptions<IndicatorDashboardOptions>()
            .BindConfiguration(IndicatorDashboardOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // It reads through the clean data dashboard's connections, query slots and time limits (AddTamizaCleanData).
        services.AddSingleton<IndicatorDashboard>();
        return services;
    }
}
