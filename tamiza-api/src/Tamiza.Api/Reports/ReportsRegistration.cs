using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tamiza.Api.Reports;

public static class ReportsRegistration
{
    /// <summary>
    /// The key of the clock scheduled emails read: the system's, unless a test sets a day and time of its own without moving the clock
    /// that signs in members.
    /// </summary>
    public const string ClockKey = "reports";

    public static IServiceCollection AddTamizaReports(this IServiceCollection services)
    {
        services.AddOptions<ReportOptions>().BindConfiguration(ReportOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.TryAddKeyedSingleton<TimeProvider>(ClockKey, (provider, _) => provider.GetRequiredService<TimeProvider>());
        services.AddScoped<ReportDataBuilder>();
        services.AddScoped<ReportSender>();
        services.AddScoped<ReportQueue>();
        services.AddSingleton<ReportSignal>();
        services.AddHostedService<ReportScheduler>();
        return services;
    }
}
