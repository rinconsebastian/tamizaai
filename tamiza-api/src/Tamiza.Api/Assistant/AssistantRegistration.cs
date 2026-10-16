using Microsoft.Extensions.Options;

namespace Tamiza.Api.Assistant;

public static class AssistantRegistration
{
    public static IServiceCollection AddTamizaAssistant(this IServiceCollection services)
    {
        // Flat TAMIZA_ASSISTANT_* variables bind from the configuration root, like the other deployment settings.
        services.AddOptions<AssistantOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AssistantOptions>, AssistantOptionsValidator>();

        services.AddAssistantHttpClient();
        services.AddSingleton<AssistantClient>();
        services.AddScoped<ContextAssembler>();
        services.AddScoped<AssistantLedger>();
        services.AddHostedService<AssistantCallRecovery>();
        services.AddHostedService<AssistantStartupCheck>();
        return services;
    }
}

/// <summary>Says once at startup which variables are missing when the assistant is only partly configured.</summary>
internal sealed class AssistantStartupCheck(IOptions<AssistantOptions> options, ILogger<AssistantStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var missing = settings.MissingVariables();
        if (missing.Count is > 0 and < 3)
        {
            logger.LogWarning(
                "The AI assistant is off because {Missing} {Verb} not set. Set all of {BaseUrl}, {Model} and {ApiKey} to turn it on.",
                string.Join(" and ", missing), missing.Count == 1 ? "is" : "are",
                AssistantOptions.BaseUrlVariable, AssistantOptions.ModelVariable, AssistantOptions.ApiKeyVariable);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
