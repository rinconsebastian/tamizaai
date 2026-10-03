using Microsoft.Extensions.Logging;

namespace Tamiza.DbUp;

// Namespaced rather than top-level statements, so it does not clash with the API's global Program.
internal static class Program
{
    /// <summary>Applies pending scripts to the database in the first argument, or in ConnectionStrings__Tamiza like the API.</summary>
    public static async Task<int> Main(string[] args)
    {
        var connectionString = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("ConnectionStrings__Tamiza");

        using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole(console => console.SingleLine = true));
        var logger = loggerFactory.CreateLogger("Tamiza.DbUp");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogCritical("Pass the connection string as the first argument or set ConnectionStrings__Tamiza.");
            return 1;
        }

        try
        {
            await SchemaMigrator.MigrateAsync(connectionString, logger);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database migration failed.");
            return 1;
        }
    }
}
