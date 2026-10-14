using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tamiza.Api.Performance;

public static class PerformanceRegistration
{
    public static IServiceCollection AddTamizaPerformance(this IServiceCollection services)
    {
        services.AddOptions<PerformanceOptions>()
            .BindConfiguration(PerformanceOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton<IPerformanceProbe, NullPerformanceProbe>();
        // One instance: it owns the cache, the computations in flight and the slots.
        services.AddSingleton<PerformanceDashboard>();
        return services;
    }
}
