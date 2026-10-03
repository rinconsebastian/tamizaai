namespace Tamiza.Api.Pipeline;

public static class PipelineRegistration
{
    public static IServiceCollection AddTamizaPipeline(this IServiceCollection services)
    {
        services.AddScoped<RunQueue>();
        return services;
    }
}
