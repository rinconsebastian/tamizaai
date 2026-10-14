using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.Performance;

/// <summary>
/// Limits of the performance dashboard, bound from the <c>Performance</c> configuration section
/// (for example <c>Performance__StatementTimeout</c>).
/// </summary>
public sealed class PerformanceOptions
{
    public const string Section = "Performance";

    /// <summary>The time one computation may take in PostgreSQL before it is stopped (<c>statement_timeout</c>).</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>The time a computation waits for one of the slots before it fails as too long.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Computations that run at the same time, across all projects.</summary>
    [Range(1, 16)]
    public int MaxConcurrentComputations { get; set; } = 2;

    /// <summary>Test only: makes the statement sleep this long (<c>pg_sleep</c>), to exercise the time limit deterministically.</summary>
    public TimeSpan TestStatementSleep { get; set; } = TimeSpan.Zero;
}

/// <summary>
/// A hook around the computation of the dashboard. The default does nothing; tests replace it to count computations and
/// to hold one open while a slot is taken.
/// </summary>
public interface IPerformanceProbe
{
    /// <summary>Called when a computation holds a slot, right before its statement runs.</summary>
    ValueTask ComputationStartedAsync(Guid projectId);
}

internal sealed class NullPerformanceProbe : IPerformanceProbe
{
    public ValueTask ComputationStartedAsync(Guid projectId) => ValueTask.CompletedTask;
}

/// <summary>The computation was stopped by the time limit, or waited too long for a slot.</summary>
public sealed class DashboardTimeoutException(Exception? inner = null) : Exception("The performance dashboard took too long to compute.", inner)
{
    public const string Code = "dashboard.timeout";
}
