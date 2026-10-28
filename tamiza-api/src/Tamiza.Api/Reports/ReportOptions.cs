using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.Reports;

/// <summary>Limits of scheduled dashboard emails (section <c>Reports</c>); the defaults are the spec's, tests lower them.</summary>
public sealed class ReportOptions
{
    public const string Section = "Reports";

    /// <summary>False keeps the scheduler from sending anything; tests drive its passes themselves.</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>How often the scheduler looks for due emails when nothing wakes it.</summary>
    public TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The rows of each widget's table the email body shows; the workbook holds them all.</summary>
    [Range(1, 1000)]
    public int RowsPerBlock { get; set; } = 10;

    /// <summary>The most the HTML body may weigh, in KB: Gmail clips a message's body at about 102 KB.</summary>
    [Range(8, 10240)]
    public int MaxBodyKb { get; set; } = 80;

    /// <summary>The largest workbook attached, in KB: 7 MB is about 9.6 MB once encoded, under Exchange's default 10 MB limit.</summary>
    [Range(1, 102400)]
    public int MaxAttachmentKb { get; set; } = 7168;

    /// <summary>How long after a "send now" request another one is refused for the same dashboard.</summary>
    public TimeSpan SendNowCooldown { get; set; } = TimeSpan.FromMinutes(10);
}
