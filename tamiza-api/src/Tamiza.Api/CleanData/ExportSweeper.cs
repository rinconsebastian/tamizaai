using System.Globalization;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace Tamiza.Api.CleanData;

/// <summary>
/// Deletes the export files left by a previous process when the API starts, and then, every minute, the files of exports that
/// were never downloaded and have expired.
/// </summary>
public sealed class ExportSweeper(ExportRegistry registry, TimeProvider time, ILogger<ExportSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var left = registry.ClearDirectory();
            if (left > 0)
            {
                logger.LogInformation("Deleted {Count} export files left by a previous run.", left);
            }

            using var timer = new PeriodicTimer(Interval, time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var removed = registry.SweepExpired();
                if (removed > 0)
                {
                    logger.LogInformation("Deleted {Count} export files that expired before they were downloaded.", removed);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}

/// <summary>The name an export is downloaded under: the project, the table and the day.</summary>
public static class ExportFileName
{
    private const int MaxProjectLength = 40;

    public static string Create(string projectName, string table, DateOnly day, string extension) =>
        $"{Slug(projectName)}_{table}_{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.{extension}";

    /// <summary>Lower-case ASCII letters and digits separated by single dashes; accents are dropped to their letters.</summary>
    public static string Slug(string text)
    {
        var builder = new StringBuilder();
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
            }
            else if (character is >= 'A' and <= 'Z')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-' && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxProjectLength)
        {
            slug = slug[..MaxProjectLength].TrimEnd('-');
        }

        return slug.Length == 0 ? "project" : slug;
    }
}
