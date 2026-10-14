using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.Ingestion;

/// <summary>
/// Tuning for webhook intake and synchronization, bound from the <c>Ingestion</c> configuration section
/// (for example <c>Ingestion__PageSize</c>). The defaults suit the reference volume of 500,000 submissions per project.
/// </summary>
public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    /// <summary>Runs the scheduler. Off only in tests that do not exercise synchronization.</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>How often the scheduler looks for due and queued runs when nothing wakes it earlier.</summary>
    public TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Runs executed at the same time across all projects.</summary>
    [Range(1, 16)]
    public int MaxConcurrentRuns { get; set; } = 2;

    /// <summary>Submissions per page when reading full records from Kobo.</summary>
    [Range(1, 30_000)]
    public int PageSize { get; set; } = 500;

    /// <summary>Submission IDs per page during reconciliation.</summary>
    [Range(1, 30_000)]
    public int IdPageSize { get; set; } = 5_000;

    /// <summary>Largest page body accepted from Kobo.</summary>
    [Range(1, long.MaxValue)]
    public long MaxPageBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Time allowed to receive one whole page, body included.</summary>
    public TimeSpan PageTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How far before the last successful synchronization an incremental one starts reading.</summary>
    public TimeSpan SafetyMargin { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Delay before retrying a failed scheduled run, unless the project's interval is shorter.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Time a re-check spends fetching form version definitions; versions left over are taken by the background worker.</summary>
    public TimeSpan RecheckVersionBudget { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Largest webhook body accepted.</summary>
    public long MaxWebhookBytes { get; set; } = 10 * 1024 * 1024;
}
