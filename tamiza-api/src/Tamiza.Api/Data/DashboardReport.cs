namespace Tamiza.Api.Data;

/// <summary>
/// The scheduled email of one <see cref="Dashboard"/>: who gets it and when, and how the last sends went. A dashboard has at most one,
/// removed with it; duplicating a dashboard does not copy it.
/// </summary>
public sealed class DashboardReport
{
    public Guid DashboardId { get; set; }

    /// <summary>1 to 20 addresses, as saved, distinct ignoring case, or none once removed. They need not belong to Tamiza users.</summary>
    public List<string> Recipients { get; set; } = [];

    /// <summary>ISO weekdays, 1 for Monday to 7 for Sunday, ascending.</summary>
    public short[] Days { get; set; } = [];

    /// <summary>The time of day in the project's time zone.</summary>
    public TimeOnly SendTime { get; set; }

    public bool Active { get; set; }

    /// <summary>The interface language and region of the member who last saved the settings, such as <c>es-CO</c>.</summary>
    public required string Locale { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set by a save made after that day's time: the day it falls on in the project's time zone is not due.</summary>
    public DateTimeOffset? NotBefore { get; set; }

    /// <summary>
    /// When a member removed the email. A removed email has no recipients and reads as none; the day it was last sent and the last
    /// "send now" stay, so removing and saving it again neither sends a day twice nor skips the 10-minute rule.
    /// </summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>When the scheduler last took a day; the day it falls on in the project's time zone is done.</summary>
    public DateTimeOffset? LastScheduledAt { get; set; }

    public bool SendNowPending { get; set; }

    /// <summary>The last "send now" request, for the 10-minute rule.</summary>
    public DateTimeOffset? SendNowRequestedAt { get; set; }

    public DateTimeOffset? LastSentAt { get; set; }

    public DateTimeOffset? LastFailedAt { get; set; }

    /// <summary>The code of the last failure, cleared by a send that succeeds.</summary>
    public string? LastFailure { get; set; }

    /// <summary>The recipient the server refused, for <c>email.rejected</c>.</summary>
    public string? LastFailureAddress { get; set; }
}
