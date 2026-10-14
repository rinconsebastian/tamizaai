using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.CleanData;

/// <summary>
/// Limits of the clean data dashboard and its exports, bound from the <c>DataDashboard</c> configuration section
/// (for example <c>DataDashboard__MaxExportFileSize</c>). See design D3 and D4.
/// </summary>
public sealed class DataDashboardOptions
{
    public const string Section = "DataDashboard";

    /// <summary>The largest XLSX sheet: Excel's 1,048,576 rows less the header.</summary>
    public const int ExcelMaxRows = 1_048_575;

    /// <summary>The time a page or a list of values may take in PostgreSQL before it is stopped (<c>statement_timeout</c>).</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>The time a request waits for one of the query slots before it fails as too long.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Pages and value lists computed at the same time, across all projects.</summary>
    [Range(1, 32)]
    public int MaxConcurrentQueries { get; set; } = 4;

    /// <summary>
    /// How long a request waits for a lock on a published table. A run swapping the tables queues for an exclusive lock, so a
    /// request that waits longer gives up instead of hanging behind it.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The time preparing an export may take in PostgreSQL before it is stopped.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan ExportStatementTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Exports prepared at the same time, across all projects.</summary>
    [Range(1, 16)]
    public int MaxConcurrentExports { get; set; } = 2;

    /// <summary>The largest export file, in bytes. An export that grows past it fails and leaves no file.</summary>
    [Range(1L, long.MaxValue)]
    public long MaxExportFileSize { get; set; } = 1024L * 1024 * 1024;

    /// <summary>How long a prepared file can be downloaded, counted from the moment it was prepared.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan ExportLinkLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Where prepared files wait for their download. Cleared when the API starts.</summary>
    [Required]
    public string ExportDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "tamiza-exports");

    /// <summary>The most rows an XLSX export may hold. Lowered in tests only.</summary>
    [Range(1, ExcelMaxRows)]
    public int XlsxMaxRows { get; set; } = ExcelMaxRows;

    /// <summary>Test only: makes the statement sleep this long (<c>pg_sleep</c>), to exercise the time limit deterministically.</summary>
    public TimeSpan TestStatementSleep { get; set; } = TimeSpan.Zero;
}
