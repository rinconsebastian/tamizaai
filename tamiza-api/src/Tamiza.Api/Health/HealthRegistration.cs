using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tamiza.Api.Data;

namespace Tamiza.Api.Health;

public static class HealthRegistration
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddTamizaHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessCheck>("database", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    /// <summary>Maps <c>/health/live</c> (process up) and <c>/health/ready</c> (migrations done, database answering).</summary>
    public static IEndpointRouteBuilder MapTamizaHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) }).AllowAnonymous();
        return endpoints;
    }
}

internal sealed class DatabaseReadinessCheck(MigrationStatus migrations, TamizaDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!migrations.Completed)
        {
            return HealthCheckResult.Unhealthy("Database migrations have not completed.");
        }

        // A real round trip: opening a pooled connection alone does not reach the server.
        await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
