namespace Tamiza.Api.Configuration;

public static class OptionsRegistration
{
    public static IServiceCollection AddTamizaOptions(this IServiceCollection services)
    {
        // The variables are flat (TAMIZA_PUBLIC_URL, OIDC_AUTHORITY...), so both bind from the configuration root.
        services.AddOptions<TamizaOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OidcOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
