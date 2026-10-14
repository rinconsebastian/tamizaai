namespace Tamiza.Api.Ingestion;

public static class IngestionRegistration
{
    public static IServiceCollection AddTamizaIngestion(this IServiceCollection services)
    {
        services.AddOptions<IngestionOptions>()
            .BindConfiguration(IngestionOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<SubmissionStore>();
        services.AddScoped<FormVersionStore>();
        services.AddScoped<FormVersionFetcher>();
        services.AddScoped<WebhookCredentials>();
        services.AddScoped<SyncQueue>();
        services.AddScoped<SyncRunner>();
        services.AddSingleton<IngestionSignal>();
        services.AddHostedService<IngestionScheduler>();
        services.AddHostedService<FormVersionWorker>();
        return services;
    }
}
