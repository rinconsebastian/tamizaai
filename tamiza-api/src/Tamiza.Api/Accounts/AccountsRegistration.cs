using Microsoft.Extensions.Options;
using Tamiza.Api.Accounts.Keycloak;

namespace Tamiza.Api.Accounts;

public static class AccountsRegistration
{
    public static IServiceCollection AddTamizaAccounts(this IServiceCollection services)
    {
        // Flat TAMIZA_* variables bind from the configuration root, like the other deployment settings.
        services.AddOptions<AccountEmailOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AccountEmailOptions>, AccountEmailOptionsValidator>();

        services.AddOptions<KeycloakAdminOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<KeycloakAdminOptions>, KeycloakAdminOptionsValidator>();

        services.AddKeycloakHttpClient();
        services.AddSingleton<KeycloakAccountProvider>();
        services.AddSingleton<IAccountProviderSource, KeycloakProviderSource>();
        services.AddHostedService<KeycloakStartupCheck>();

        services.AddSingleton<AccountEmailLimits>();
        services.AddScoped<AccountEmails>();
        return services;
    }
}

/// <summary>The Keycloak adapter is the provider when its client ID and secret are set, and there is none otherwise.</summary>
internal sealed class KeycloakProviderSource(IOptions<KeycloakAdminOptions> options, KeycloakAccountProvider provider) : IAccountProviderSource
{
    public IAccountProvider? Provider => options.Value.Enabled ? provider : null;
}

/// <summary>Says once at startup which variable is missing when the Keycloak adapter is only partly configured.</summary>
internal sealed class KeycloakStartupCheck(IOptions<KeycloakAdminOptions> options, ILogger<KeycloakStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var missing = options.Value.MissingVariables();
        if (missing.Count == 1)
        {
            logger.LogWarning(
                "Account emails are off because {Missing} is not set. Set both {ClientId} and {ClientSecret} to turn them on.",
                missing[0], KeycloakAdminOptions.ClientIdVariable, KeycloakAdminOptions.ClientSecretVariable);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
