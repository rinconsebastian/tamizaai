using System.Text.Json;
using System.Text.Json.Serialization;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Projects;

public static class ProjectsRegistration
{
    public static IServiceCollection AddTamizaProjects(this IServiceCollection services)
    {
        // Enums travel as camelCase strings ("admin", "analyst", "viewer").
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        services.AddScoped<ProjectAccess>();
        services.AddKoboHttpClient();
        services.AddTransient<KoboClient>();
        services.AddSingleton<SecretProtector>();
        services.AddScoped<InvitationAcceptor>();
        return services;
    }
}
