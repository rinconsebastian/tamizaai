using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;
using Tamiza.DbUp;

namespace Tamiza.Api.Data;

public static class DatabaseRegistration
{
    public const string ConnectionStringName = "Tamiza";

    public static IServiceCollection AddTamizaDatabase(this IServiceCollection services)
    {
        services.AddSingleton<MigrationStatus>();
        services.AddDbContext<TamizaDbContext>((provider, options) =>
            Configure(options, GetConnectionString(provider.GetRequiredService<IConfiguration>())));
        return services;
    }

    /// <summary>EF Core settings for the metadata schema. The schema itself comes from the Tamiza.DbUp scripts.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    /// <summary>
    /// Applies pending DbUp scripts when enabled. Returns false when they fail, so the process can exit
    /// before it starts serving requests.
    /// </summary>
    public static async Task<bool> MigrateDatabaseAsync(this WebApplication app)
    {
        var status = app.Services.GetRequiredService<MigrationStatus>();
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(typeof(DatabaseRegistration));

        if (!app.Services.GetRequiredService<IOptions<TamizaOptions>>().Value.MigrateOnStartup)
        {
            logger.LogInformation("Startup migrations are disabled; assuming the schema is current.");
            status.MarkCompleted();
            return true;
        }

        try
        {
            await SchemaMigrator.MigrateAsync(
                GetConnectionString(app.Configuration), loggerFactory.CreateLogger(typeof(SchemaMigrator)), app.Lifetime.ApplicationStopping);
            status.MarkCompleted();
            logger.LogInformation("Database schema is up to date.");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database migration failed. The API will exit without serving requests.");
            return false;
        }
    }

    private static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
        ?? throw new InvalidOperationException($"ConnectionStrings__{ConnectionStringName} is not set.");
}

/// <summary>Records whether startup migrations have finished, for the readiness check.</summary>
public sealed class MigrationStatus
{
    private volatile bool _completed;

    public bool Completed => _completed;

    public void MarkCompleted() => _completed = true;
}
