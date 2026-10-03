using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Data;

public static class DatabaseRegistration
{
    public const string ConnectionStringName = "Tamiza";

    public static IServiceCollection AddTamizaDatabase(this IServiceCollection services)
    {
        services.AddSingleton<MigrationStatus>();
        services.AddDbContext<TamizaDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"ConnectionStrings__{ConnectionStringName} is not set.");
            Configure(options, connectionString);
        });
        return services;
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TamizaDbContext.Schema))
            .UseSnakeCaseNamingConvention();

    /// <summary>
    /// Applies pending migrations when enabled. Returns false when they fail, so the process can exit
    /// before it starts serving requests.
    /// </summary>
    public static async Task<bool> MigrateDatabaseAsync(this WebApplication app)
    {
        var status = app.Services.GetRequiredService<MigrationStatus>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseRegistration));

        if (!app.Services.GetRequiredService<IOptions<TamizaOptions>>().Value.MigrateOnStartup)
        {
            logger.LogInformation("Startup migrations are disabled; assuming the schema is current.");
            status.MarkCompleted();
            return true;
        }

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TamizaDbContext>();
            await db.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
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
}

/// <summary>Records whether startup migrations have finished, for the readiness check.</summary>
public sealed class MigrationStatus
{
    private volatile bool _completed;

    public bool Completed => _completed;

    public void MarkCompleted() => _completed = true;
}
