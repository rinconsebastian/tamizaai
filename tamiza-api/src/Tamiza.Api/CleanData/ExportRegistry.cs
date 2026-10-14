using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.CleanData;

/// <summary>The secret of a download link and when the link stops working.</summary>
public sealed record ExportLink(string Token, DateTimeOffset ExpiresAt);

/// <summary>A prepared export waiting for its download.</summary>
public sealed record PreparedExport(Guid ProjectId, Guid UserId, string Path, string FileName, string ContentType, DateTimeOffset ExpiresAt);

/// <summary>
/// The prepared export files and the single-use links that give access to them (design D4). A link is a random token; taking it
/// removes it, so it works once, and an entry that outlived <see cref="DataDashboardOptions.ExportLinkLifetime"/> is never given
/// out and its file is deleted. The entries live in this process only: a restart drops them and the startup sweep deletes their files.
/// </summary>
public sealed class ExportRegistry(TimeProvider time, IOptions<DataDashboardOptions> options)
{
    /// <summary>Files this registry creates; only these are ever deleted from the export directory.</summary>
    public const string FileExtension = ".export";

    private readonly ConcurrentDictionary<string, PreparedExport> _entries = new(StringComparer.Ordinal);
    private readonly DataDashboardOptions _options = options.Value;

    public int Pending => _entries.Count;

    /// <summary>A new path in the export directory, which is created when missing. The file does not exist yet.</summary>
    public string NewFilePath()
    {
        Directory.CreateDirectory(_options.ExportDirectory);
        return Path.Combine(_options.ExportDirectory, Guid.NewGuid().ToString("N") + FileExtension);
    }

    /// <summary>Registers a finished file and returns the link that downloads it.</summary>
    public ExportLink Add(Guid projectId, Guid userId, string path, string fileName, string contentType)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var entry = new PreparedExport(projectId, userId, path, fileName, contentType, time.GetUtcNow() + _options.ExportLinkLifetime);
        _entries[token] = entry;
        return new ExportLink(token, entry.ExpiresAt);
    }

    /// <summary>
    /// The export for the link, removed so that no one can take it again. Null when the link is unknown, was used or expired;
    /// an expired file is deleted. When two callers race for one link, only one gets it.
    /// </summary>
    public PreparedExport? TryTake(string token)
    {
        if (!_entries.TryRemove(token, out var entry))
        {
            return null;
        }

        if (time.GetUtcNow() >= entry.ExpiresAt)
        {
            DeleteQuietly(entry.Path);
            return null;
        }

        return entry;
    }

    /// <summary>Deletes the files of entries that expired and forgets them. Returns how many were removed.</summary>
    public int SweepExpired()
    {
        var now = time.GetUtcNow();
        var removed = 0;
        foreach (var (token, entry) in _entries)
        {
            if (now >= entry.ExpiresAt && _entries.TryRemove(token, out var expired))
            {
                DeleteQuietly(expired.Path);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Deletes the export files left in the directory, for example by a previous process. Other files are left alone.</summary>
    public int ClearDirectory()
    {
        if (!Directory.Exists(_options.ExportDirectory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_options.ExportDirectory, "*" + FileExtension))
        {
            if (DeleteQuietly(file))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Deletes a file that is no longer needed, whether it exists or not.</summary>
    public static bool DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (IOException)
        {
            // Still open by a download that is ending: its own delete-on-close removes it.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
