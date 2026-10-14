namespace Tamiza.Api.CleanData;

public static class CleanDataRegistration
{
    public static IServiceCollection AddTamizaCleanData(this IServiceCollection services)
    {
        services.AddOptions<DataDashboardOptions>()
            .BindConfiguration(DataDashboardOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // One instance: it owns the query slots.
        services.AddSingleton<DataDashboard>();
        // One instance: it owns the prepared files and their single-use links.
        services.AddSingleton<ExportRegistry>();
        services.AddHostedService<ExportSweeper>();
        return services;
    }
}
